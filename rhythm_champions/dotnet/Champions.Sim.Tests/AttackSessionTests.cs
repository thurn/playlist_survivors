using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace Champions.Sim.Tests
{
    /// <summary>
    /// Sessions with attacks (plan.md, "Sessions and replay"): a scripted run with a build and standing enemies
    /// records its loadout and dummies with its inputs, and replays to its hash.
    /// </summary>
    public class AttackSessionTests
    {
        /// <summary>The recorded session CI replays: rhythm_champions/sessions/fixture-attacks.</summary>
        public const string FixtureSession = "fixture-attacks";

        static readonly string[] WaveBuild = { "swordSlash:4", "bladeRain:3", "crescent:4", "twinStrike:1", "tempo:2", "keenEdge:1", "shatterburst:1" };
        static readonly string[] SummonBuild = { "swordSlash:3", "orbitBlades:4", "phantom:3", "wideArcs:2", "lifeblood:1", "afterimage:1", "momentum:2" };

        /// <summary>
        /// A live run as the client drives it, 30 s through 128 BPM, a beatless gap and 174 BPM, with
        /// <paramref name="build"/> and sixty standing enemies around the hero, the player walking about and dashing.
        /// </summary>
        public static (RunInputs Run, Simulation Sim, List<StreamEntry> Fed) AttacksRun(string[] build)
        {
            var entries = new FixtureSong(33)
                .Beats(0, 12, 128, jitter: 0.003, halfOnsets: true)
                .Gap(12, 14.5)
                .Beats(14.5, 30, 174, jitter: 0.002)
                .Entries();
            var dummies = Enumerable.Range(0, 60)
                .Select(i => new Dummy((2 + i % 10 * 1.1) * Math.Cos(2.4 * i), (2 + i % 10 * 1.1) * Math.Sin(2.4 * i), 2 + i % 4 * 3, 0.45))
                .ToArray();
            var run = new RunInputs { Seed = 33, Start = RunStart.OnControl, Loadout = Arena.Build(build), Dummies = dummies };
            var sim = new Simulation(run.Build(GameContent.Load(Checkout.In("Content"))));
            var script = new Queue<(double At, Action<Simulation> Give)>(new (double, Action<Simulation>)[]
            {
                (0.5, s => s.Control(RunControl.Start, s.Now - 0.06)),
                (2.0, s => s.Press(Key.Right, s.Now - 0.08)),
                (4.5, s => s.Press(Key.Up, s.Now - 0.04)),
                (5.5, s => s.Release(Key.Right, s.Now - 0.07)),
                (7.0, s => s.Release(Key.Up, s.Now - 0.05)),
                (9.0, s => s.Target(-3, -6, s.Now - 0.06)),
                (10.5, s => s.Press(Key.Space, s.Now - 0.05)),
                (10.6, s => s.Release(Key.Space, s.Now - 0.05)),
                (15.0, s => s.Control(RunControl.Pause, s.Now - 0.02)),
                (16.0, s => s.Control(RunControl.Resume, s.Now - 0.03)),
                (20.0, s => s.Press(Key.Left, s.Now - 0.09)),
                (22.5, s => s.Press(Key.Space, s.Now - 0.07)),
                (22.6, s => s.Release(Key.Space, s.Now - 0.05)),
                (24.0, s => s.Release(Key.Left, s.Now - 0.05)),
                (29.5, s => s.Control(RunControl.Quit, s.Now - 0.04)),
            });
            int fed = Drive.Feed(sim, entries, 30, eachFrame: s =>
            {
                while (script.Count > 0 && script.Peek().At <= s.Now) script.Dequeue().Give(s);
            });
            return (run, sim, entries.GetRange(0, fed));
        }

        static (Simulation Sim, SessionReplay Replay) Replay(string dir)
        {
            var record = SessionRecord.Read(dir);
            var replay = new SessionReplay(record);
            foreach (var e in SessionRecord.LoadEntries(dir)) replay.Supply(e);
            var sim = new Simulation(record.Run.Build(GameContent.Load(Checkout.In("Content"))));
            Assert.IsTrue(replay.StepTo(sim, double.PositiveInfinity));
            return (sim, replay);
        }

        [Test]
        public void AScriptedSessionWithAttacksReplaysToItsHash([Values(0, 1)] int which)
        {
            var build = which == 0 ? WaveBuild : SummonBuild;
            var (run, live, fed) = AttacksRun(build);
            Assert.AreEqual(RunEnd.Quit, live.State.End, "the script ran to its end");
            Assert.Greater(live.Arsenal.Kills, 20, "the attacks fought");
            Assert.AreEqual(2, live.State.Hero.Dashes, "and the hero dashed");
            Assert.Greater(Drive.Events(live, SimEventKind.Hit).Count, 50);
            string dir = Path.Combine(Path.GetTempPath(), "champions-attacks-" + Guid.NewGuid().ToString("N"));
            try
            {
                var record = SessionRecord.Of(run, live);
                record.Write(dir);
                SessionRecord.WriteEntries(dir, fed);
                var read = SessionRecord.Read(dir);
                CollectionAssert.AreEqual(run.Loadout, read.Run.Loadout, "the loadout is recorded");
                CollectionAssert.AreEqual(run.Dummies, read.Run.Dummies, "and the dummies");
                StringAssert.Contains($"\"loadout\": [{string.Join(", ", build.Select(b => $"\"{b}\""))}]", File.ReadAllText(Path.Combine(dir, SessionRecord.RunFile)));
                var (sim, replay) = Replay(dir);
                Assert.IsTrue(replay.Matches(sim), $"replayed {sim.Hash:X16} ({sim.EventCount} events), recorded {record.Hash:X16} ({record.Events})");
                Assert.AreEqual(live.Arsenal.Kills, sim.Arsenal.Kills);
            }
            finally
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
            }
        }

        [Test]
        public void TheRecordedAttacksSessionReplaysToItsHash()
        {
            string dir = Checkout.In("sessions", FixtureSession);
            var (sim, replay) = Replay(dir);
            Assert.IsTrue(replay.Matches(sim),
                $"{dir} replays to {sim.Hash:X16} ({sim.EventCount} events), recorded {replay.Record.Hash:X16} ({replay.Record.Events}): " +
                "if a rule or the content changed on purpose, re-record it (dotnet run --project rhythm_champions/dotnet/Champions.Replay -- " +
                "rhythm_champions/sessions/fixture-attacks --update) and say so in the commit");
        }

        [Test]
        public void TheAttacksSessionIsTheScriptedRunsRecording()
        {
            var record = SessionRecord.Read(Checkout.In("sessions", FixtureSession));
            var (run, live, fed) = AttacksRun(WaveBuild);
            CollectionAssert.AreEqual(run.Loadout, record.Run.Loadout);
            Assert.AreEqual(record.Inputs.Length, live.Inputs.Length);
            Assert.AreEqual(record.Entries.Length, fed.Count);
            Assert.AreEqual(record.End, live.Now);
        }

        [Test, Explicit("writes rhythm_champions/sessions/fixture-attacks")]
        public void RecordTheAttacksSession()
        {
            var (run, live, fed) = AttacksRun(WaveBuild);
            string dir = Checkout.In("sessions", FixtureSession);
            var record = SessionRecord.Of(run, live);
            record.Source = "dotnet/Champions.Sim.Tests AttackSessionTests.AttacksRun: a synthetic fixture song, no audio";
            record.Build = "test";
            record.Write(dir);
            SessionRecord.WriteEntries(dir, fed);
        }

        [Test]
        public void AVersionOneSessionStartsWithTheStartLoadout()
        {
            string dir = Path.Combine(Path.GetTempPath(), "champions-v1-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                // inputs.bin version 1: the run inputs end at the start byte.
                using (var w = new BinaryWriter(File.Create(Path.Combine(dir, SessionRecord.InputsFile))))
                {
                    w.Write(new[] { (byte)'R', (byte)'C', (byte)'R', (byte)'U', (byte)'N' });
                    w.Write(1);
                    w.Write(5UL);
                    w.Write(0.0);
                    w.Write(0.5);
                    w.Write((byte)RunStart.Immediately);
                    w.Write(10.0);
                    w.Write(0);
                    w.Write(0UL);
                    w.Write(0);
                    w.Write(0);
                }
                var read = SessionRecord.Read(dir);
                Assert.AreEqual(5UL, read.Run.Seed);
                CollectionAssert.AreEqual(new[] { Upgrade.Of(AttackKind.SwordSlash, 1) }, read.Run.Loadout);
                Assert.IsEmpty(read.Run.Dummies);
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }
    }
}
