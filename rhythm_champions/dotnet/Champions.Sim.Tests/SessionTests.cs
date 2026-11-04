using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace Champions.Sim.Tests
{
    /// <summary>
    /// Sessions and replay (plan.md, "Sessions and replay"): a session is its inputs, the stream's records and
    /// run.json, and a replay reproduces the run's final state hash.
    /// </summary>
    public class SessionTests
    {
        /// <summary>The recorded session CI replays: rhythm_champions/sessions/fixture-walk.</summary>
        public const string FixtureSession = "fixture-walk";

        /// <summary>
        /// A live run as the client drives it, 60 s of a fixture song through tempo changes, a silent gap and a
        /// beatless stretch: frames of 1/60 s, entries arriving as the plugin hands them over, and a player's
        /// inputs heard up to 0.1 s before the sim takes them (start, walking, click targets, dashes dropped,
        /// buffered and beatless, a pause).
        /// </summary>
        public static (RunInputs Run, Simulation Sim, List<StreamEntry> Fed) LiveRun()
        {
            var entries = new FixtureSong(21)
                .Beats(0, 20, 128, jitter: 0.003, halfOnsets: true)
                .Gap(20, 21.5, silent: true)
                .Beats(21.5, 40, 92, 100, jitter: 0.004)
                .Gap(40, 44)
                .Beats(44, 60, 174, jitter: 0.002)
                .Entries();
            var run = new RunInputs { Seed = 21, Start = RunStart.OnControl };
            var sim = new Simulation(run.Build(GameContent.Load(Checkout.In("Content"))));
            var script = new Queue<(double At, Action<Simulation> Give)>(new (double, Action<Simulation>)[]
            {
                (0.5, s => s.Control(RunControl.Start, s.Now - 0.08)),
                (1.2, s => s.Press(Key.Right, s.Now - 0.09)),
                (2.7, s => s.Press(Key.Up, s.Now - 0.031)),
                (3.3, s => s.Release(Key.Right, s.Now - 0.1)),
                (4.0, s => s.Press(Key.Space, s.Now - 0.07)),
                (4.1, s => s.Release(Key.Space, s.Now - 0.07)),
                (4.3, s => s.Press(Key.Space, s.Now - 0.05)),
                (4.45, s => s.Press(Key.Space, s.Now - 0.05)),
                (6.0, s => s.Release(Key.Up, s.Now - 0.05)),
                (7.5, s => s.Target(-4.25, 9.5, s.Now - 0.06)),
                (7.6, s => s.Target(-4.5, 9.0, s.Now - 0.06)),
                (12.0, s => s.Control(RunControl.Pause, s.Now - 0.02)),
                (12.4, s => s.Press(Key.Left, s.Now - 0.04)),
                (15.0, s => s.Control(RunControl.Resume, s.Now - 0.03)),
                (18.0, s => s.Release(Key.Left, s.Now - 0.08)),
                (25.0, s => s.Press(Key.Down, s.Now - 0.06)),
                (25.2, s => s.Press(Key.Right, s.Now - 0.06)),
                (26.0, s => s.Press(Key.Space, s.Now - 0.06)),
                (31.0, s => s.Release(Key.Down, s.Now - 0.09)),
                (33.0, s => s.Release(Key.Right, s.Now - 0.02)),
                (41.0, s => s.Press(Key.Space, s.Now - 0.04)),
                (45.0, s => s.Target(10, -10, s.Now - 0.07)),
                (46.0, s => s.Press(Key.Space, s.Now - 0.05)),
                (46.6, s => s.Press(Key.Space, s.Now - 0.08)),
                (52.0, s => s.Press(Key.One, s.Now - 0.05)),
                (59.0, s => s.Control(RunControl.Quit, s.Now - 0.04)),
            });
            int fed = Drive.Feed(sim, entries, 60, eachFrame: s =>
            {
                while (script.Count > 0 && script.Peek().At <= s.Now) script.Dequeue().Give(s);
            });
            return (run, sim, entries.GetRange(0, fed));
        }

        static string TempDir()
        {
            string dir = Path.Combine(Path.GetTempPath(), "champions-session-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            return dir;
        }

        static (Simulation Sim, SessionReplay Replay) Replay(string dir)
        {
            var record = SessionRecord.Read(dir);
            var replay = new SessionReplay(record);
            foreach (var e in SessionRecord.LoadEntries(dir)) replay.Supply(e);
            Assert.IsTrue(replay.AllSupplied, "every entry the run took is in the record");
            var sim = new Simulation(record.Run.Build(GameContent.Load(Checkout.In("Content"))));
            Assert.IsTrue(replay.StepTo(sim, double.PositiveInfinity));
            return (sim, replay);
        }

        [Test]
        public void ARecordedSessionReplaysToItsHash()
        {
            var (run, live, fed) = LiveRun();
            Assert.AreEqual(RunEnd.Quit, live.State.End, "the script ran to its end");
            Assert.AreEqual(6, live.State.Hero.Dashes, "dashes, two of them buffered");
            Assert.AreEqual(2, Drive.Events(live, SimEventKind.HeroDash).Count(e => e.Buffered));
            Assert.AreEqual(1, Drive.Events(live, SimEventKind.DashDropped).Count, "a press too early in the cooldown");
            Assert.Greater(live.Inputs.Length, 15);
            string dir = TempDir();
            try
            {
                var record = SessionRecord.Of(run, live);
                record.Source = "fixture";
                record.Write(dir);
                SessionRecord.WriteEntries(dir, fed);
                var read = SessionRecord.Read(dir);
                Assert.AreEqual(record.Hash, read.Hash);
                Assert.AreEqual(record.Inputs.Length, read.Inputs.Length);
                Assert.AreEqual("fixture", read.Source);
                for (int i = 0; i < record.Inputs.Length; i++) Assert.AreEqual(record.Inputs[i], read.Inputs[i], $"input {i}");
                var (sim, replay) = Replay(dir);
                Assert.IsTrue(replay.Matches(sim), $"replayed {sim.Hash:X16} ({sim.EventCount} events), recorded {record.Hash:X16} ({record.Events})");
                Assert.AreEqual(live.Hash, sim.Hash);
                StringAssert.Contains("\"format\": \"rhythm-champions-session\"", File.ReadAllText(Path.Combine(dir, SessionRecord.RunFile)));
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }

        [Test]
        public void TheRecordedFixtureSessionReplaysToItsHash()
        {
            string dir = Checkout.In("sessions", FixtureSession);
            var (sim, replay) = Replay(dir);
            Assert.IsTrue(replay.Matches(sim),
                $"{dir} replays to {sim.Hash:X16} ({sim.EventCount} events), recorded {replay.Record.Hash:X16} ({replay.Record.Events}): " +
                "if a rule or the content changed on purpose, re-record it (dotnet run --project rhythm_champions/dotnet/Champions.Replay -- " +
                "rhythm_champions/sessions/fixture-walk --update) and say so in the commit");
        }

        [Test]
        public void TheFixtureSessionIsTheLiveRunsRecording()
        {
            // The recorded session is this test's live run: if it no longer is, the run's inputs changed, not the rules.
            var record = SessionRecord.Read(Checkout.In("sessions", FixtureSession));
            var (_, live, fed) = LiveRun();
            Assert.AreEqual(record.Inputs.Length, live.Inputs.Length);
            Assert.AreEqual(record.Entries.Length, fed.Count);
            Assert.AreEqual(record.End, live.Now);
        }

        [Test, Explicit("writes rhythm_champions/sessions/fixture-walk")]
        public void RecordTheFixtureSession()
        {
            var (run, live, fed) = LiveRun();
            string dir = Checkout.In("sessions", FixtureSession);
            var record = SessionRecord.Of(run, live);
            record.Source = "dotnet/Champions.Sim.Tests SessionTests.LiveRun: a synthetic fixture song, no audio";
            record.Build = "test";
            record.Write(dir);
            SessionRecord.WriteEntries(dir, fed);
        }

        [Test]
        public void AReplayWaitsForEntriesNotYetSupplied()
        {
            var (run, live, fed) = LiveRun();
            var replay = new SessionReplay(SessionRecord.Of(run, live));
            for (int i = 0; i < fed.Count / 2; i++) replay.Supply(fed[i]);
            var sim = new Simulation(run.Build(GameContent.Load(Checkout.In("Content"))));
            Assert.IsFalse(replay.StepTo(sim, double.PositiveInfinity));
            Assert.Less(sim.Now, live.Now);
            for (int i = fed.Count / 2; i < fed.Count; i++) replay.Supply(fed[i]);
            Assert.IsTrue(replay.StepTo(sim, double.PositiveInfinity));
            Assert.IsTrue(replay.Matches(sim));
        }

        [Test]
        public void ZenModeIsRecorded()
        {
            string dir = TempDir();
            try
            {
                var run = new RunInputs { Seed = 21, Zen = true };
                var record = SessionRecord.Of(run, new Simulation(run.Build(null)));
                record.Write(dir);
                var read = SessionRecord.Read(dir);
                Assert.IsTrue(read.Run.Zen);
                Assert.IsTrue(read.Run.Build(null).Zen);
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }

        [Test]
        public void ADifferentRunDoesNotMatch()
        {
            var (run, live, fed) = LiveRun();
            var record = SessionRecord.Of(run, live);
            var replay = new SessionReplay(record);
            foreach (var e in fed) replay.Supply(e);
            var other = new RunInputs { Seed = run.Seed, Start = RunStart.Immediately };
            var sim = new Simulation(other.Build(GameContent.Load(Checkout.In("Content"))));
            replay.StepTo(sim, double.PositiveInfinity);
            Assert.IsFalse(replay.Matches(sim), "started at once instead of on the control");
        }

        [Test]
        public void ANonSessionIsRefused()
        {
            string dir = TempDir();
            try
            {
                File.WriteAllBytes(Path.Combine(dir, SessionRecord.InputsFile), new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9 });
                Assert.Throws<FormatException>(() => SessionRecord.Read(dir));
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }

        [Test]
        public void EntriesRoundTripExactly()
        {
            string dir = TempDir();
            try
            {
                var entries = new List<StreamEntry>
                {
                    StreamEntry.Beat(0.1 + 0.2, -2.9, 60.0 / 128, 0.123456789),
                    StreamEntry.Tick(1.0 / 3, -1.7, 0.5, Math.PI / 10, LoudnessLevel.Loud, 7, 0.1, 0.2, 0.3, 0.4, 0.5),
                    StreamEntry.Onset(2.5, -0.4, 0.75, true, (int)DrumBand.Snare),
                };
                SessionRecord.WriteEntries(dir, entries);
                // The plugin's dumps may carry a header line.
                File.WriteAllText(Path.Combine(dir, SessionRecord.EntriesFile),
                    "{\"format\":\"ps-entries\",\"version\":1}\n" + File.ReadAllText(Path.Combine(dir, SessionRecord.EntriesFile)));
                var read = SessionRecord.LoadEntries(dir);
                Assert.AreEqual(entries.Count, read.Count);
                for (int i = 0; i < entries.Count; i++) Assert.AreEqual(entries[i], read[i], $"entry {i}");
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }
    }
}
