using System;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace Champions.Sim.Tests
{
    public class SurvivorAssemblyTests
    {
        static void Replay(LevelRunTests.Outcome o)
        {
            var record = SessionRecord.Of(o.Run, o.Sim);
            var replay = new SessionReplay(record);
            foreach (var entry in o.Fed) replay.Supply(entry);
            var sim = new Simulation(record.Run.Build(GameContent.Load(Checkout.In("Content"))));
            Assert.IsTrue(replay.StepTo(sim, double.PositiveInfinity));
            Assert.IsTrue(replay.Matches(sim), "full input/stream replay, with current unmodified content");
            Assert.AreEqual(o.End, sim.State.End);
        }

        [TestCase("focused", 0, 31UL)]
        [TestCase("focused", 128, 32UL)]
        [TestCase("broad", 80, 31UL)]
        [TestCase("defensive", 174, 31UL)]
        public void SurvivorBuildsWinAndReplayWithoutLandingBlastOrHordeBeatDashes(string build, double bpm, ulong seed)
        {
            var o = LevelRunTests.Run("positioning", until: 1600, survivor: true, bpm: bpm, seed: seed,
                tweak: p => LevelRunTests.ConfigureBuild(p, build));
            TestContext.Out.WriteLine($"{build}/{bpm}/{seed}: {o.End}, {o.RunClock:F1}s, normal choices {o.NormalPicks}, first {o.Moments.First(m => m.What.StartsWith("level ")).RunClock:F1}s");
            Assert.AreEqual(RunEnd.Victory, o.End);
            Assert.AreEqual(3, o.Sim.Bosses.Called);
            Assert.AreEqual(900, o.Sim.State.HordeClock, 0.02);
            Assert.That(o.NormalPicks, Is.InRange(25, 35));
            Assert.AreEqual(2, o.BossPicks + o.Sim.Progression.Fallbacks, "capped owned items award max health instead of an empty reward");
            Assert.AreEqual(o.NormalPicks + o.BossPicks, o.Picks);
            Assert.AreEqual(0, o.Sim.State.Build.Level(SupportKind.LandingBlast));
            if (build == "broad") Assert.AreEqual(6, o.Sim.State.Build.WeaponCount);
            Replay(o);
        }

        [TestCase(0)] [TestCase(128)]
        public void IdleDeathAndFreshRetryReplayOnMusicAndSilence(double bpm)
        {
            var dead = LevelRunTests.Run("idle", until: 450, survivor: true, bpm: bpm);
            TestContext.Out.WriteLine("idle current: " + dead);
            Assert.AreEqual(RunEnd.Death, dead.End);
            Assert.IsTrue(dead.Sim.State.Hero.Dead);
            Replay(dead);
            // The sim half of Again: a fresh run on the continuing stream starts from the current frontier.
            var run = new RunInputs { Level = true, SurvivorWeapons = true, StartTime = dead.Sim.Now };
            var retry = new Simulation(run.Build(GameContent.Load(Checkout.In("Content"))));
            foreach (var entry in dead.Fed) retry.AddKnown(entry, Math.Max(run.StartTime, entry.Stamp + run.KnownAtBudget));
            retry.StepTo(run.StartTime + 3);
            Assert.AreEqual(RunPhase.Playing, retry.State.Phase);
            Assert.That(retry.State.RunClock, Is.InRange(2.9, 3.1));
            Assert.AreEqual(1, retry.State.Build.Level(WeaponKind.Sword));
            Assert.IsFalse(retry.State.Offer.Up);
            Assert.Greater(retry.State.Hero.Health, 0);
        }

        [Test]
        public void VersionFiveSurvivorSessionStillReplaysWithoutAPreset()
        {
            var run = new RunInputs { SurvivorWeapons = true, Loadout = Array.Empty<Upgrade>() };
            var sim = new Simulation(run.Build(null));
            string dir = Path.Combine(Path.GetTempPath(), "champions-v5-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                using (var writer = new BinaryWriter(File.Create(Path.Combine(dir, SessionRecord.InputsFile))))
                {
                    writer.Write(new[] { (byte)'R', (byte)'C', (byte)'R', (byte)'U', (byte)'N' });
                    writer.Write(5);
                    writer.Write(run.Seed); writer.Write(run.StartTime); writer.Write(run.KnownAtBudget);
                    writer.Write((byte)run.Start); writer.Write(0); writer.Write(0); // loadout and dummies
                    writer.Write(false); writer.Write(""); writer.Write(true); // level, duel, survivor mode
                    writer.Write(sim.Now); writer.Write(sim.EventCount); writer.Write(sim.Hash);
                    writer.Write(0); writer.Write(0); // entry and input counts
                }
                var record = SessionRecord.Read(dir);
                Assert.IsTrue(record.Run.SurvivorWeapons);
                Assert.IsEmpty(record.Run.SurvivorLoadout);
                var copy = new Simulation(record.Run.Build(null));
                Assert.IsTrue(new SessionReplay(record).Matches(copy));
                Assert.AreEqual(1, copy.State.Build.Level(WeaponKind.Sword));
            }
            finally { Directory.Delete(dir, true); }
        }

        [Test]
        public void SurvivorPresetSessionRoundtripPreservesSixSlotsAndRejectsMalformedItems()
        {
            var run = new RunInputs { SurvivorWeapons = true, Loadout = Array.Empty<Upgrade>(),
                SurvivorLoadout = AttackSessionTests.Preset("sword:8", "hammer:5", "spear:3", "bow:8", "orbitBlades:5", "sentinel:8",
                    "might:5", "tempo:5", "reach:5", "echo:3", "persistence:3", "armor:3") };
            string dir = Path.Combine(Path.GetTempPath(), "champions-survivor-" + Guid.NewGuid().ToString("N"));
            try
            {
                var sim = new Simulation(run.Build(null));
                var record = SessionRecord.Of(run, sim); record.Write(dir);
                var restored = SessionRecord.Read(dir);
                CollectionAssert.AreEqual(run.SurvivorLoadout, restored.Run.SurvivorLoadout);
                var copy = new Simulation(restored.Run.Build(null));
                Assert.AreEqual(6, copy.State.Build.WeaponCount);
                Assert.AreEqual(6, copy.State.Build.SupportCount);
                Assert.AreEqual(sim.Hash, copy.Hash);
                run.SurvivorLoadout = new[] { Card.Of(WeaponKind.Sword, 9, 0) };
                SessionRecord.Of(run, sim).Write(dir);
                Assert.Throws<FormatException>(() => SessionRecord.Read(dir));
                run.SurvivorLoadout = new[] { Card.Of(WeaponKind.Sword, 1, 0), Card.Of(WeaponKind.Sword, 2, 0) };
                Assert.Throws<ArgumentException>(() => new Simulation(run.Build(null)));
            }
            finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
        }
    }
}
