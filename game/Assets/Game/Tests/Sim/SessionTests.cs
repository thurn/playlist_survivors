using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using PS.Game.Sim.Level;

namespace PS.Game.Sim.Tests
{
    /// <summary>
    /// Sessions and replay (unity-game.md, "Sessions and Replay"): a live run on the slice's level,
    /// fed as the client feeds it (entries arriving a frame at a time, keys and debug commands taken
    /// at the frontier), is written to a session folder beside the plugin's record, and replays from
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

        static SimConfig Config(ulong seed = 3) =>
            SliceLevel.Config(HordeFixture.Content, HordeFixture.GameArena(), Footprint, seed);

        /// <summary>The plugin's record of house-128's first 20 s: the dump's lines, without its header.</summary>
        void RecordEntries()
        {
            var lines = File.ReadLines(Checkout.File("native", "fixtures", "entries", EntryDump.House128)).Skip(1);
            File.WriteAllLines(Path.Combine(dir, SessionRecord.EntriesFile), lines);
        }

        /// <summary>
        /// A live run as the client plays it: each frame, the entries that have arrived (stamp + 60 ms,
        /// but none during <paramref name="stall"/>, whose entries arrive together after it), the keys
        /// heard 90 ms before the frontier and the debug commands, then a step to the frontier.
        /// </summary>
        static Simulation Live(SimConfig config, IReadOnlyList<StreamEntry> entries, double until, double frame,
            (double from, double to) stall = default)
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
            var commands = new Queue<(double at, DebugCommand command)>(new[]
            {
                (7.0, DebugCommand.BreakStructures), (8.0, DebugCommand.SectionChange), (13.0, DebugCommand.Accent),
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
                while (commands.Count > 0 && commands.Peek().at <= frontier) sim.Command(commands.Dequeue().command);
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
            var sim = new Simulation(record.Config(HordeFixture.Content, HordeFixture.GameArena()));
            Assert.IsTrue(replay.StepTo(sim, double.PositiveInfinity), "every entry supplied");
            return sim;
        }

        [Test]
        public void ARecordedFileSessionReplaysToItsHash()
        {
            RecordEntries();
            var entries = EntryDump.Load(EntryDump.House128, out double duration);
            var config = Config();
            var live = Live(config, entries, duration, 1.0 / 60);
            var written = SessionRecord.Of(config, live);
            written.Flags = new[] { "--file", "house-128.ogg" };
            written.Write(dir);
            Assert.Greater(Drive.Events(live, SimEventKind.Death).Count, 20, "a run with a horde to kill");
            Assert.AreEqual(10, live.Keys.Length);
            Assert.AreEqual(3, live.Commands.Length);

            var sim = Replay(out var replay);
            Assert.IsTrue(replay.Matches(sim), $"replayed {sim.Hash:X16} at {sim.Now}, recorded {live.Hash:X16} at {live.Now}");
            Assert.AreEqual(live.Hash, sim.Hash);
            CollectionAssert.AreEqual(live.EventsSince(0).ToArray(), sim.EventsSince(0).ToArray());
            CollectionAssert.AreEqual(live.Stream.Accepted.ToArray(), sim.Stream.Accepted.ToArray(), "the replay takes each entry as the run did");
            var record = replay.Record;
            Assert.AreEqual(SessionLevel.Slice, record.Level);
            Assert.IsTrue(record.Arena);
            CollectionAssert.AreEqual(Footprint, record.Footprint);
            CollectionAssert.AreEqual(new[] { "--file", "house-128.ogg" }, record.Flags);
            Assert.AreEqual(SessionRecord.CurrentRuntime, record.Runtime);
            StringAssert.Contains($"\"hash\": \"{live.Hash:X16}\"", File.ReadAllText(Path.Combine(dir, SessionRecord.RunFile)));
        }

        [Test]
        public void ALateEntryReplaysFromItsRecordedKnownAt()
        {
            RecordEntries();
            var entries = EntryDump.Load(EntryDump.House128, out double duration);
            var config = Config(seed: 5);
            // An analyzer stall: nothing arrives from 6 to 8 s, then the backlog at once. At 240 frames a
            // second half the frames step no tick, so some entries are known at the very time they arrive.
            var live = Live(config, entries, duration, 1.0 / 240, stall: (6, 8));
            SessionRecord.Of(config, live).Write(dir);
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
            var unstalled = new Simulation(config);
            foreach (var e in entries) unstalled.AddKnown(e, Math.Max(unstalled.Now, e.Stamp + B));
            var naive = new SessionReplay(new SessionRecord { Keys = replay.Record.Keys, Commands = replay.Record.Commands, End = live.Now });
            naive.StepTo(unstalled, double.PositiveInfinity);
            Assert.AreNotEqual(live.Hash, unstalled.Hash);
        }

        [Test]
        public void AReplayWaitsForEntriesStillToBeSupplied()
        {
            RecordEntries();
            var entries = EntryDump.Load(EntryDump.House128, out double duration);
            var config = Config();
            SessionRecord.Of(config, Live(config, entries, duration, 1.0 / 60)).Write(dir);
            var record = SessionRecord.Read(dir);
            var replay = new SessionReplay(record);
            var sim = new Simulation(record.Config(HordeFixture.Content, HordeFixture.GameArena()));
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
        public void ASessionOfAnotherLevelIsRefused()
        {
            var config = HordeFixture.Config(sword: 1);
            Assert.Throws<NotSupportedException>(() => SessionRecord.Of(config, new Simulation(config)));
        }
    }
}
