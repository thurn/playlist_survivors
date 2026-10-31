using System.Collections.Generic;
using NUnit.Framework;

namespace PS.Game.Sim.Tests
{
    /// <summary>Committed beats, reliability, locks, bars and downbeats (unity-game.md, "Beats, Bars and Reliability").</summary>
    public class BeatClockTests
    {
        static List<SimEvent> Beats(Simulation sim) => Drive.Events(sim, SimEventKind.Beat);

        static BeatEvent BeatAt(Simulation sim, double t)
        {
            foreach (var e in Beats(sim))
                if (System.Math.Abs(e.T - t) < 1e-6) return e.AsBeat;
            Assert.Fail($"no beat at {t}");
            return default;
        }

        [Test]
        public void EveryCommittedBeatFiresAtItsTime()
        {
            var song = new FixtureSong().Beats(0, 20, 120);
            var sim = Drive.Replay(Drive.Known(song.Entries()), 20);
            var times = song.BeatTimes();
            var fired = Beats(sim);
            Assert.AreEqual(times.Count, fired.Count);
            for (int i = 0; i < times.Count; i++)
            {
                Assert.AreEqual(times[i], fired[i].T, 1e-12);
                Assert.AreEqual(i, fired[i].AsBeat.Index);
                Assert.AreEqual(0.5, fired[i].AsBeat.Period, 1e-9);
            }
            Assert.AreEqual(0, sim.Beats.Missed);
        }

        [Test]
        public void TheBeatIsReliableOnceTheMusicHasBeenClearForASecond()
        {
            var sim = Drive.Replay(Drive.Known(new FixtureSong().Beats(0, 20, 120).Entries()), 20);
            Assert.AreEqual(Reliability.BelowGate, BeatAt(sim, 0.5).Reliability);
            Assert.AreEqual(Reliability.Reliable, BeatAt(sim, 1.0).Reliability);
            Assert.AreEqual(Reliability.Reliable, BeatAt(sim, 10.0).Reliability);
        }

        [Test]
        public void BarsCountTwoBeatsFromTheFirstBeatAfterTheLock()
        {
            // Clear from 0, so clear ticks from 1.0: the lock. Bars start on the beat at 1.0.
            var sim = Drive.Replay(Drive.Known(new FixtureSong().Beats(0, 20, 120).Entries()), 20);
            CollectionAssert.AreEqual(new[] { 1.0 }, sim.Beats.Judging.Locks.ToArray());
            Assert.AreEqual(-1, BeatAt(sim, 0.5).Bar);
            Assert.IsTrue(BeatAt(sim, 1.0).Downbeat);
            Assert.AreEqual(0, BeatAt(sim, 1.0).Bar);
            Assert.IsFalse(BeatAt(sim, 1.5).Downbeat);
            Assert.IsTrue(BeatAt(sim, 2.0).Downbeat);
            Assert.AreEqual(9, BeatAt(sim, 10.0).Bar);
        }

        [Test]
        public void ALongGapRelocksAndRecountsBars()
        {
            // 150 BPM after the gap: the lock at 27.0 falls between beats, so bars restart on 27.2.
            var song = new FixtureSong().Beats(0, 19.75, 120).Gap(19.75, 26).Beats(26, 40, 150);
            var sim = Drive.Replay(Drive.Known(song.Entries()), 40);
            Assert.AreEqual(2, sim.Beats.Judging.Locks.Length);
            Assert.AreEqual(27.0, sim.Beats.Judging.Locks[1], 1e-9);
            Assert.IsTrue(BeatAt(sim, 27.2).Downbeat);
            Assert.AreEqual(0, BeatAt(sim, 27.2).Bar);
            Assert.IsFalse(BeatAt(sim, 27.6).Downbeat);
            Assert.IsTrue(BeatAt(sim, 28.0).Downbeat);
            // Beats before the new lock still count from the old one (26.8 is beat 42, 40 after
            // the beat at 1.0), so a relock can put two downbeats a beat apart.
            Assert.AreEqual(20, BeatAt(sim, 26.8).Bar);
        }

        [Test]
        public void AShortGapKeepsTheBars()
        {
            var song = new FixtureSong().Beats(0, 19.75, 120).Gap(19.75, 22).Beats(22, 40, 150);
            var sim = Drive.Replay(Drive.Known(song.Entries()), 40);
            Assert.AreEqual(1, sim.Beats.Judging.Locks.Length);
            // Beats 0 to 19.5 are indices 0 to 39; 22.0 is 40, counted from the beat at 1.0 (index 2).
            Assert.IsTrue(BeatAt(sim, 22.0).Downbeat);
            Assert.IsFalse(BeatAt(sim, 22.4).Downbeat);
            Assert.IsTrue(BeatAt(sim, 22.8).Downbeat);
        }

        [Test]
        public void TheThreeStates()
        {
            var song = new FixtureSong()
                .Beats(0, 10, 120, evidence: 0.3)
                .Beats(10, 20, 120)
                .Gap(20, 30, ticks: false);
            var sim = Drive.Replay(Drive.Known(song.Entries()), 30);
            Assert.AreEqual(Reliability.BelowGate, BeatAt(sim, 5.0).Reliability);
            Assert.AreEqual(Reliability.BelowGate, BeatAt(sim, 10.5).Reliability);
            Assert.AreEqual(Reliability.Reliable, BeatAt(sim, 11.5).Reliability);
            Assert.AreEqual(Reliability.NoBeatAhead, sim.State.Beat);
            var changes = new List<(double t, Reliability r)>();
            foreach (var e in Drive.Events(sim, SimEventKind.BeatState)) changes.Add((e.T, e.AsBeatState.Now));
            // Below the gate from the start, reliable a second into the clear stretch, nothing ahead
            // once the last beat (19.5) has continued for 1.5 periods, at the first tick after 20.25.
            Assert.AreEqual(Reliability.BelowGate, changes[0].r);
            Assert.AreEqual(Reliability.Reliable, changes[1].r);
            Assert.AreEqual(11.0, changes[1].t, 1e-6);
            Assert.AreEqual(Reliability.NoBeatAhead, changes[changes.Count - 1].r);
            Assert.AreEqual(20.25 + Simulation.TickSeconds, changes[changes.Count - 1].t, 1e-6);
        }

        [Test]
        public void StaleTicksMakeTheBeatUnreliable()
        {
            var entries = Drive.Known(new FixtureSong().Beats(0, 20, 120).Entries());
            entries.RemoveAll(k => k.Entry.Kind == EntryKind.Tick && k.Entry.T > 5.01 && k.Entry.T < 7.99);
            var sim = Drive.Replay(entries, 20);
            Assert.AreEqual(Reliability.Reliable, BeatAt(sim, 5.0).Reliability);
            Assert.AreEqual(Reliability.BelowGate, BeatAt(sim, 6.0).Reliability, "the latest tick is 1 s old");
            Assert.AreEqual(Reliability.Reliable, BeatAt(sim, 8.0).Reliability);
        }

        [Test]
        public void ABeatKnownOnlyAfterItsTimeIsMissed()
        {
            var sim = new Simulation(new SimConfig());
            sim.AddKnown(StreamEntry.Beat(2.0, 0, 0.5, 0.9), 2.5);
            sim.AddKnown(StreamEntry.Beat(3.0, 0, 0.5, 0.9), 2.5);
            sim.StepTo(4);
            Assert.AreEqual(1, sim.Beats.Missed);
            var fired = Beats(sim);
            Assert.AreEqual(1, fired.Count);
            Assert.AreEqual(3.0, fired[0].T, 1e-12);
        }
    }
}
