using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using PS.Game.Sim.Level;

namespace PS.Game.Sim.Tests
{
    /// <summary>
    /// Sessions and replay (unity-game.md, "Sessions and Replay"): a live run on the slice's level,
    /// fed as the client feeds it (entries arriving a frame at a time, keys, debug commands and run
    /// controls taken at the frontier), is written to a session folder beside the plugin's record, and replays from
    /// the folder alone to the same settled hash, late entries included.
    /// </summary>
    public class SessionTests
    {
        const double B = 0.5;
        static readonly double[] Footprint = { -18.5, -9.25, 18.5, -9.25, 24.75, 15.5, -24.75, 15.5 };

        string dir;

        [SetUp]
        public void MakeFolder()
        {
            dir = Path.Combine(Path.GetTempPath(), "ps-session-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
        }

        [TearDown]
        public void RemoveFolder() => Directory.Delete(dir, recursive: true);

        /// <summary>The slice's level on the game's arena, started on a key, as a windowed run starts.</summary>
        static RunInputs Inputs(ulong seed = 3) => new RunInputs
        {
            Seed = seed, Level = SessionLevel.Slice, Arena = true, Footprint = Footprint, Start = RunStart.OnKey,
        };

        static SimConfig Build(RunInputs run) => run.Build(HordeFixture.Content, HordeFixture.GameArena());

        /// <summary>The plugin's record of house-128's first 20 s: the dump's lines, without its header.</summary>
        void RecordEntries()
        {
            var lines = File.ReadLines(Checkout.File("native", "fixtures", "entries", EntryDump.House128)).Skip(1);
            File.WriteAllLines(Path.Combine(dir, SessionRecord.EntriesFile), lines);
        }

        /// <summary>
        /// A live run as the client plays it: each frame, the entries that have arrived (stamp + 60 ms,
        /// but none during <paramref name="stall"/>, whose entries arrive together after it), the keys
        /// heard 90 ms before the frontier, the debug commands and the run controls (the start, a pause
        /// and its resume), then a step to the frontier.
        /// </summary>
        static Simulation Live(SimConfig config, IReadOnlyList<StreamEntry> entries, double until, double frame,
            (double from, double to) stall = default, (double at, DebugCommand command, int arg)[] debug = null)
        {
            var sim = new Simulation(config);
            int next = 0;
            double frontier = sim.Now;
            var presses = new Queue<(double at, Key key, bool down)>(new[]
            {
                (2.0, Key.Up, true), (2.6, Key.Up, false), (4.1, Key.Right, true), (4.15, Key.Space, true),
                (4.4, Key.Space, false), (6.0, Key.Right, false), (9.3, Key.Left, true), (9.31, Key.Left, false),
                (12.0, Key.Down, true), (15.5, Key.Down, false),
            });
            var commands = new Queue<(double at, DebugCommand command, int arg)>(debug ?? new[]
            {
                (7.0, DebugCommand.BreakStructures, 0), (8.0, DebugCommand.SectionChange, 0), (13.0, DebugCommand.Accent, 0),
            });
            var controls = new Queue<(double at, RunControl control)>(new[]
            {
                (1.5, RunControl.Start), (10.0, RunControl.Pause), (11.2, RunControl.Resume),
            });
            while (frontier < until - 1e-12)
            {
                frontier = Math.Min(until, frontier + frame);
                bool stalled = frontier > stall.from && frontier < stall.to;
                while (!stalled && next < entries.Count && entries[next].Stamp + 0.06 <= frontier) sim.Add(entries[next++]);
                while (presses.Count > 0 && presses.Peek().at <= frontier)
                {
                    var (at, key, down) = presses.Dequeue();
                    if (down) sim.Press(key, sim.Now - 0.09);
                    else sim.Release(key, sim.Now - 0.09);
                }
                while (commands.Count > 0 && commands.Peek().at <= frontier)
                {
                    var (_, command, arg) = commands.Dequeue();
                    sim.Command(command, arg);
                }
                while (controls.Count > 0 && controls.Peek().at <= frontier) sim.Control(controls.Dequeue().control);
                sim.StepTo(frontier);
            }
            return sim;
        }

        /// <summary>The session in <c>dir</c> replayed from its files alone.</summary>
        Simulation Replay(out SessionReplay replay)
        {
            var record = SessionRecord.Read(dir);
            replay = new SessionReplay(record);
            foreach (var e in SessionRecord.LoadEntries(dir)) replay.Supply(e);
            var sim = new Simulation(Build(record.Run));
            Assert.IsTrue(replay.StepTo(sim, double.PositiveInfinity), "every entry supplied");
            return sim;
        }

        [Test]
        public void ARecordedFileSessionReplaysToItsHash()
        {
            RecordEntries();
            var entries = EntryDump.Load(EntryDump.House128, out double duration);
            var run = Inputs();
            var live = Live(Build(run), entries, duration, 1.0 / 60);
            var written = SessionRecord.Of(run, live);
            written.Flags = new[] { "--file", "house-128.ogg" };
            written.Write(dir);
            Assert.Greater(Drive.Events(live, SimEventKind.Death).Count, 20, "a run with a horde to kill");
            Assert.AreEqual(10, live.Keys.Length);
            Assert.AreEqual(3, live.Commands.Length);
            Assert.AreEqual(3, live.Controls.Length);
            Assert.AreEqual(RunPhase.Playing, live.State.Phase, "the run started on its key");

            var sim = Replay(out var replay);
            Assert.IsTrue(replay.Matches(sim), $"replayed {sim.Hash:X16} at {sim.Now}, recorded {live.Hash:X16} at {live.Now}");
            Assert.AreEqual(live.Hash, sim.Hash);
            CollectionAssert.AreEqual(live.EventsSince(0).ToArray(), sim.EventsSince(0).ToArray());
            CollectionAssert.AreEqual(live.Stream.Accepted.ToArray(), sim.Stream.Accepted.ToArray(), "the replay takes each entry as the run did");
            var record = replay.Record;
            Assert.AreEqual(SessionLevel.Slice, record.Run.Level);
            Assert.AreEqual(RunStart.OnKey, record.Run.Start);
            Assert.IsTrue(record.Run.Arena);
            CollectionAssert.AreEqual(Footprint, record.Run.Footprint);
            CollectionAssert.AreEqual(new[] { "--file", "house-128.ogg" }, record.Flags);
            Assert.AreEqual(SessionRecord.CurrentRuntime, record.Runtime);
            StringAssert.Contains($"\"hash\": \"{live.Hash:X16}\"", File.ReadAllText(Path.Combine(dir, SessionRecord.RunFile)));
        }

        /// <summary>The recap's numbers, exactly: the kills, then each weapon's damage and time held.</summary>
        static string Recap(Simulation sim)
        {
            var recap = sim.State.Recap;
            return recap.Kills + ": " + string.Join(", ", Enumerable.Range(0, recap.Damage.Length)
                .Select(w => $"{recap.Damage[w]:R}/{recap.Held(w):R}"));
        }

        [Test]
        public void TheEndCardsRecapIsTheSameAtAnyFramePacingAndInTheRunsReplay()
        {
            RecordEntries();
            var entries = EntryDump.Load(EntryDump.House128, out double duration);
            var run = new RunInputs
            {
                Seed = 3, Level = SessionLevel.Slice, Arena = true, Footprint = Footprint, Start = RunStart.AfterClearBeat,
            };
            Simulation Paced(double frame)
            {
                var sim = new Simulation(Build(run));
                int next = Drive.Feed(sim, entries, duration - 2, frame);
                sim.Control(RunControl.Quit); // at one song time whatever the pacing
                Drive.Feed(sim, entries, duration, frame, from: next);
                return sim;
            }
            var live = Paced(1.0 / 60);
            Assert.AreEqual(RunEnd.Quit, live.State.End);
            Assert.Greater(live.State.Recap.Kills, 10, "a run with a horde to kill");
            Assert.AreEqual(Recap(live), Recap(Paced(1.0 / 23.7)));

            SessionRecord.Of(run, live).Write(dir);
            var sim = Replay(out var replay);
            Assert.IsTrue(replay.Matches(sim));
            Assert.AreEqual(Recap(live), Recap(sim), "the replay ends with the run's recap");
        }

        [Test]
        public void ARunStartedAtAMinuteReplaysItsDebugCommandsWithTheirArguments()
        {
            RecordEntries();
            var entries = EntryDump.Load(EntryDump.House128, out double duration);
            var content = HordeFixture.Content;
            var run = new RunInputs
            {
                Seed = 7, Level = SessionLevel.Run, Arena = true, Footprint = Footprint, Start = RunStart.OnKey, StartMinute = 25,
            };
            var debug = new[]
            {
                (3.0, DebugCommand.Invulnerable, 1), (4.0, DebugCommand.SpawnRole, content.RoleIndex("werewolf")),
                (5.0, DebugCommand.GrantEvolution, content.WeaponIndex("holy-wand")), (6.0, DebugCommand.SpawnBoss, content.RoleIndex("boss-xlmummy")),
                (9.0, DebugCommand.GrantPassive, content.PassiveIndex("spinach")), (12.0, DebugCommand.JumpClock, 1620),
            };
            var live = Live(Build(run), entries, duration, 1.0 / 60, debug: debug);
            SessionRecord.Of(run, live).Write(dir);
            Assert.Greater(live.State.RunClock, 1620, "the jump");
            Assert.IsTrue(live.State.Hero.Invulnerable);

            var sim = Replay(out var replay);
            Assert.AreEqual(live.Hash, sim.Hash, "replayed to its hash");
            var record = replay.Record;
            Assert.AreEqual(SessionLevel.Run, record.Run.Level);
            Assert.AreEqual(25, record.Run.StartMinute);
            CollectionAssert.AreEqual(debug.Select(d => (d.Item2, d.Item3)), record.Commands.Select(c => (c.Command, c.Arg)));
            StringAssert.Contains("\"startMinute\": 25", File.ReadAllText(Path.Combine(dir, SessionRecord.RunFile)));
        }

        [Test]
        public void ALateEntryReplaysFromItsRecordedKnownAt()
        {
            RecordEntries();
            var entries = EntryDump.Load(EntryDump.House128, out double duration);
            var run = Inputs(seed: 5);
            var config = Build(run);
            // An analyzer stall: nothing arrives from 6 to 8 s, then the backlog at once. At 240 frames a
            // second half the frames step no tick, so some entries are known at the very time they arrive.
            var live = Live(config, entries, duration, 1.0 / 240, stall: (6, 8));
            SessionRecord.Of(run, live).Write(dir);
            var accepted = live.Stream.Accepted.ToArray();
            int late = 0, atOnce = 0;
            for (int i = 0; i < accepted.Length; i++)
            {
                if (accepted[i].KnownAt > entries[i].Stamp + B + 1e-9) late++;
                if (accepted[i].KnownAt == accepted[i].Taken) atOnce++;
            }
            Assert.Greater(late, 10, "the stall's entries were known late, at the frontier");
            Assert.Greater(atOnce, 0, "some known in the frame they arrived in, which stepped no tick");

            var sim = Replay(out var replay);
            Assert.IsTrue(replay.Matches(sim), $"replayed {sim.Hash:X16}, recorded {live.Hash:X16}");

            // Known at stamp + B instead, as if there had been no stall, the run differs: the record is what keeps it.
            var unstalled = new Simulation(Build(run));
            foreach (var e in entries) unstalled.AddKnown(e, Math.Max(unstalled.Now, e.Stamp + B));
            var naive = new SessionReplay(new SessionRecord
            {
                Keys = replay.Record.Keys, Commands = replay.Record.Commands, Controls = replay.Record.Controls, End = live.Now,
            });
            naive.StepTo(unstalled, double.PositiveInfinity);
            Assert.AreNotEqual(live.Hash, unstalled.Hash);
        }

        [Test]
        public void AReplayWaitsForEntriesStillToBeSupplied()
        {
            RecordEntries();
            var entries = EntryDump.Load(EntryDump.House128, out double duration);
            var run = Inputs();
            SessionRecord.Of(run, Live(Build(run), entries, duration, 1.0 / 60)).Write(dir);
            var record = SessionRecord.Read(dir);
            var replay = new SessionReplay(record);
            var sim = new Simulation(Build(record.Run));
            var all = SessionRecord.LoadEntries(dir);
            int half = record.Entries.Length / 2;
            for (int i = 0; i < half; i++) replay.Supply(all[i]);
            Assert.IsFalse(replay.StepTo(sim, duration), "the second half is missing");
            Assert.AreEqual(record.Entries[half].Taken, sim.Now, 1e-9, "stopped where the first missing entry was taken");
            foreach (var e in all.Skip(half)) replay.Supply(e);
            Assert.AreEqual(record.Entries.Length, replay.Supplied, "the entries the run never took are ignored");
            Assert.IsTrue(replay.StepTo(sim, duration));
            Assert.IsTrue(replay.Matches(sim));
        }

        [Test]
        public void AnUnrecordableRunIsRefused()
        {
            var run = Inputs();
            run.Loadout = new[] { ("garlic", 8) };
            var ex = Assert.Throws<NotSupportedException>(() => SessionRecord.Of(run, new Simulation(Build(run))));
            StringAssert.Contains("--arsenal", ex.Message);
            Assert.IsNull(run.On(baked: false, arena: true, Footprint).Unrecordable, "an open level has no arsenal to change");
            run.Loadout = null;
            run.NoCards = true;
            Assert.AreEqual("--no-cards", run.Unrecordable);
        }

        /// <summary>
        /// Every public <see cref="SimConfig"/> field, by where a run's comes from: its <see cref="RunInputs"/>, which a
        /// session records, or the level, the content and fixed sizes, which a replay rebuilds as they were. A new field
        /// goes in one of them, and a field a run sets goes in <see cref="RunInputs"/>, or a replay silently plays the
        /// default.
        /// </summary>
        static readonly string[] FromInputs =
        {
            nameof(SimConfig.Seed), nameof(SimConfig.StartTime), nameof(SimConfig.StartMinute), nameof(SimConfig.Gate),
            nameof(SimConfig.KnownAtBudget), nameof(SimConfig.Arena), nameof(SimConfig.Start), nameof(SimConfig.Easy),
        };
        static readonly string[] FromLevelAndContent =
        {
            nameof(SimConfig.Content), nameof(SimConfig.Horde), nameof(SimConfig.Arsenal), nameof(SimConfig.HeroStands), nameof(SimConfig.TickCapacity),
            nameof(SimConfig.BeatCapacity), nameof(SimConfig.EntryCapacity), nameof(SimConfig.EventCapacity), nameof(SimConfig.PressCapacity),
        };

        [Test]
        public void EveryConfigFieldIsARecordedInputOrTheLevels()
        {
            var fields = typeof(SimConfig).GetFields(BindingFlags.Public | BindingFlags.Instance).Select(f => f.Name).ToArray();
            CollectionAssert.IsEmpty(fields.Except(FromInputs).Except(FromLevelAndContent),
                "classify each new SimConfig field: built from RunInputs (and recorded), or the level's and content's");
            CollectionAssert.IsEmpty(FromInputs.Intersect(FromLevelAndContent));
            CollectionAssert.IsEmpty(FromInputs.Concat(FromLevelAndContent).Except(fields), "a field that's gone");
            Assert.IsEmpty(typeof(SimConfig).GetProperties(BindingFlags.Public | BindingFlags.Instance));

            // Each input reaches the config: inputs unlike the defaults build a config unlike the default's.
            var run = new RunInputs
            {
                Seed = 11, Level = SessionLevel.Run, Arena = true, Footprint = Footprint, Start = RunStart.AfterClearBeat,
                StartTime = 0.25, StartMinute = 3, Gate = 0.3, KnownAtBudget = 0.6, Easy = true,
            };
            var config = Build(run);
            var plain = new SimConfig();
            foreach (var name in FromInputs)
                Assert.AreNotEqual(typeof(SimConfig).GetField(name).GetValue(plain), typeof(SimConfig).GetField(name).GetValue(config), name);
            Assert.AreSame(Footprint, config.Horde.CameraFootprint);
        }

        /// <summary>
        /// Sessions already written read as they were: <c>inputs.bin</c>'s bytes of each version, laid out by hand
        /// (version 1 has no start minute and no input arguments), so a change to the layout fails here, not on disk.
        /// </summary>
        [TestCase(1)]
        [TestCase(2)]
        public void SessionsAlreadyWrittenReadAsTheyWere(int version)
        {
            using (var w = new BinaryWriter(File.Create(Path.Combine(dir, SessionRecord.InputsFile))))
            {
                w.Write(new[] { (byte)'P', (byte)'S', (byte)'R', (byte)'U', (byte)'N' });
                w.Write(version);
                w.Write(42UL);   // seed
                w.Write(0.5);    // start time
                if (version >= 2) w.Write(25); // start minute
                w.Write(0.2);    // gate
                w.Write(0.5);    // B
                w.Write(true);   // easy
                w.Write(false);  // the hero standing
                w.Write((byte)RunStart.OnKey);
                w.Write((byte)SessionLevel.Run);
                w.Write(true);   // arena
                w.Write(2);
                w.Write(-1.5);
                w.Write(2.5);
                w.Write(30.25);  // end
                w.Write(1234);   // events
                w.Write(0xABCDEF0123456789UL);
                w.Write(1);      // entries
                w.Write(1.0);
                w.Write(1.5);
                w.Write(2);      // inputs: a press, then a debug command
                w.Write((byte)0);
                w.Write((byte)Key.Up);
                if (version >= 2) w.Write(0);
                w.Write(2.0);
                w.Write(2.1);
                w.Write((byte)2);
                w.Write((byte)DebugCommand.JumpClock);
                if (version >= 2) w.Write(1620);
                w.Write(0.0);
                w.Write(3.0);
            }
            var record = SessionRecord.Read(dir);
            var run = record.Run;
            Assert.AreEqual(42UL, run.Seed);
            Assert.AreEqual(0.5, run.StartTime);
            Assert.AreEqual(version >= 2 ? 25 : 0, run.StartMinute);
            Assert.AreEqual(0.2, run.Gate);
            Assert.AreEqual(0.5, run.KnownAtBudget);
            Assert.IsTrue(run.Easy);
            Assert.AreEqual(RunStart.OnKey, run.Start);
            Assert.AreEqual(SessionLevel.Run, run.Level);
            Assert.IsTrue(run.Arena);
            CollectionAssert.AreEqual(new[] { -1.5, 2.5 }, run.Footprint);
            Assert.AreEqual(30.25, record.End);
            Assert.AreEqual(1234, record.Events);
            Assert.AreEqual(0xABCDEF0123456789UL, record.Hash);
            Assert.AreEqual(new EntryRecord { Taken = 1.0, KnownAt = 1.5 }, record.Entries.Single());
            Assert.AreEqual(Key.Up, record.Keys.Single().Key);
            Assert.AreEqual(2.1, record.Keys.Single().Effective);
            Assert.AreEqual(DebugCommand.JumpClock, record.Commands.Single().Command);
            Assert.AreEqual(version >= 2 ? 1620 : 0, record.Commands.Single().Arg);
            Assert.AreEqual(1, record.Commands.Single().Order, "in the order the sim took them");

            // Written again, the same bytes (version 2's).
            if (version != 2) return;
            var bytes = File.ReadAllBytes(Path.Combine(dir, SessionRecord.InputsFile));
            record.Write(dir);
            CollectionAssert.AreEqual(bytes, File.ReadAllBytes(Path.Combine(dir, SessionRecord.InputsFile)));
        }

        [Test]
        public void RunInputsRoundTripFieldForField()
        {
            var levels = new[]
            {
                new RunInputs
                {
                    Seed = 9, Level = SessionLevel.Run, Arena = true, Footprint = RunLevel.GameFootprint, Start = RunStart.OnKey,
                    StartTime = 1.5, StartMinute = 25, Gate = 0.25, KnownAtBudget = 0.75, Easy = true,
                },
                Inputs(seed: ulong.MaxValue),
                new RunInputs { Seed = 2, Start = RunStart.AfterClearBeat },
            };
            var recorded = typeof(RunInputs).GetFields(BindingFlags.Public | BindingFlags.Instance);
            foreach (var run in levels)
            {
                var bytes = new MemoryStream();
                using (var w = new BinaryWriter(bytes, System.Text.Encoding.UTF8, leaveOpen: true)) run.Write(w);
                bytes.Position = 0;
                var read = RunInputs.Read(new BinaryReader(bytes), 2, "test");
                Assert.AreEqual(bytes.Length, bytes.Position, $"{run.Level}: all of it read");
                foreach (var f in recorded)
                {
                    if (f.GetValue(run) is double[] xs) CollectionAssert.AreEqual(xs, (double[])f.GetValue(read), $"{run.Level} {f.Name}");
                    else Assert.AreEqual(f.GetValue(run), f.GetValue(read), $"{run.Level} {f.Name}");
                }

                // The same run on the inputs read back: the same hash.
                var entries = EntryDump.Load(EntryDump.House128, out _);
                bool open = run.Level == SessionLevel.Open;
                var a = Live(open ? run.Build(null, null) : Build(run), entries, 6, 1.0 / 60);
                var b = Live(open ? read.Build(null, null) : Build(read), entries, 6, 1.0 / 60);
                Assert.AreEqual(a.Hash, b.Hash, $"{run.Level}");
            }
        }
    }
}
