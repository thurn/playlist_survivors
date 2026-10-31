using System;
using NUnit.Framework;

namespace PS.Game.Sim.Tests
{
    /// <summary>
    /// Determinism (unity-game.md, "Shape and Determinism"): the same stream, keys and seed give the
    /// same settled event log and hash, whatever the frame pacing; no per-tick allocation.
    /// </summary>
    public class DeterminismTests
    {
        static FixtureSong Song() => new FixtureSong(11)
            .Beats(0, 40, 128, jitter: 0.004)
            .Gap(40, 47)
            .Beats(47, 120, 92, 160, jitter: 0.003);

        [Test]
        public void TheSameStreamGivesTheSameHash()
        {
            var known = Drive.Known(Song().Entries());
            var a = Drive.Replay(known, 120);
            var b = Drive.Replay(known, 120);
            Assert.AreEqual(a.Hash, b.Hash);
            CollectionAssert.AreEqual(a.EventsSince(0).ToArray(), b.EventsSince(0).ToArray());
            Assert.Greater(Drive.Events(a, SimEventKind.Beat).Count, 200);
            Assert.Greater(Drive.Events(a, SimEventKind.StrideStep).Count, 300);
        }

        [Test]
        public void FramePacingDoesNotChangeTheRun()
        {
            var entries = Song().Entries();
            var a = new Simulation(new SimConfig());
            Drive.Feed(a, entries, 120, frame: 1.0 / 60);
            var b = new Simulation(new SimConfig());
            Drive.Feed(b, entries, 120, frame: 1.0 / 144);
            var c = new Simulation(new SimConfig());
            Drive.Feed(c, entries, 120, frame: 1.0 / 23.7);
            var replay = Drive.Replay(Drive.Known(entries), 120);
            Assert.AreEqual(a.Hash, b.Hash);
            Assert.AreEqual(a.Hash, c.Hash);
            Assert.AreEqual(a.Hash, replay.Hash);
        }

        [Test]
        public void ARealDumpRunsTheSameWhateverTheFramePacing()
        {
            var entries = EntryDump.Load(EntryDump.House128, out double duration);
            var a = new Simulation(new SimConfig());
            Drive.Feed(a, entries, duration, frame: 1.0 / 60);
            var b = new Simulation(new SimConfig());
            Drive.Feed(b, entries, duration, frame: 1.0 / 23.7);
            var replay = Drive.Replay(Drive.Known(entries), duration);
            Assert.Greater(Drive.Events(a, SimEventKind.Beat).Count, 40);
            Assert.AreEqual(a.Hash, b.Hash);
            Assert.AreEqual(a.Hash, replay.Hash);
        }

        [Test]
        public void TheHashSeesAChangedEntry()
        {
            var known = Drive.Known(Song().Entries());
            var a = Drive.Replay(known, 120);
            int i = known.FindIndex(k => k.Entry.Kind == EntryKind.Beat && k.Entry.T > 60);
            var changed = known[i];
            changed.Entry.V1 = 0.2; // its evidence
            known[i] = changed;
            var b = Drive.Replay(known, 120);
            Assert.AreNotEqual(a.Hash, b.Hash);
        }

        [Test]
        public void TheRandomGeneratorIsSeeded()
        {
            var a = new SimRandom(42);
            var b = new SimRandom(42);
            var c = new SimRandom(43);
            bool differs = false;
            for (int i = 0; i < 1000; i++)
            {
                ulong x = a.NextULong();
                Assert.AreEqual(x, b.NextULong());
                differs |= x != c.NextULong();
                int k = a.Below(7);
                b.Below(7);
                c.Below(7);
                Assert.That(k, Is.InRange(0, 6));
                double d = a.NextDouble();
                b.NextDouble();
                c.NextDouble();
                Assert.That(d, Is.GreaterThanOrEqualTo(0).And.LessThan(1));
            }
            Assert.IsTrue(differs);
            Assert.AreEqual(new SimRandom(1).NextULong(), new Simulation(new SimConfig { Seed = 1 }).Random.NextULong());
        }

#if !UNITY_5_3_OR_NEWER
        [Test]
        public void TicksAllocateNothing()
        {
            var sim = new Simulation(new SimConfig());
            foreach (var k in Drive.Known(Song().Entries())) sim.AddKnown(k.Entry, k.KnownAt);
            sim.StepTo(5); // warm up (JIT)
            long before = GC.GetAllocatedBytesForCurrentThread();
            sim.StepTo(120);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.AreEqual(0, allocated, "bytes allocated while stepping 115 s");
        }
#endif
    }
}
