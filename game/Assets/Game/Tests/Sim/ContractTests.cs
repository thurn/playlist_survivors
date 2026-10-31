using System;
using NUnit.Framework;

namespace PS.Game.Sim.Tests
{
    /// <summary>The sim's contract: entries held until known, known_at's two terms, ticks, keys and the event log.</summary>
    public class ContractTests
    {
        [Test]
        public void AnEntryIsHiddenUntilItsKnownAt()
        {
            var sim = new Simulation(new SimConfig());
            // known_at = stamp + B = 9.5 + 0.5.
            sim.Add(StreamEntry.Beat(12.0, 9.5, 0.5, 0.9));
            sim.StepTo(9.99);
            Assert.AreEqual(0, sim.Stream.Count(EntryKind.Beat));
            Assert.AreEqual(1, sim.Stream.PendingCount);
            sim.StepTo(10.0);
            Assert.AreEqual(1, sim.Stream.Count(EntryKind.Beat));
            Assert.AreEqual(10.0, sim.Stream.Get(EntryKind.Beat, 0).KnownAt, 1e-12);
        }

        [Test]
        public void KnownAtIsTheStampPlusTheBudgetInNormalPlay()
        {
            // The doc's first example: an onset at 61.00 emitted at position 61.05 is known at 58.60.
            var sim = new Simulation(new SimConfig());
            sim.StepTo(58.0);
            sim.Add(StreamEntry.Onset(61.0, FixtureSong.StampOf(61.05), 0.8, true));
            sim.StepTo(58.6);
            Assert.AreEqual(1, sim.Stream.Count(EntryKind.Onset));
            Assert.AreEqual(58.60, sim.Stream.Get(EntryKind.Onset, 0).KnownAt, 1e-9);
        }

        [Test]
        public void ALateEntryIsKnownAtTheFrontierOfTheFrameItArrivedIn()
        {
            // The doc's third example: from position 120.0, arriving late with the frontier at 118.
            var sim = new Simulation(new SimConfig());
            sim.StepTo(117.9);
            sim.Add(StreamEntry.Change(119.0, FixtureSong.StampOf(120.0), 0.5));
            sim.StepTo(118.0);
            Assert.AreEqual(118.0, sim.Stream.Get(EntryKind.Change, 0).KnownAt, 1e-9);
        }

        [Test]
        public void AReplayedEntryCannotBeKnownInThePast()
        {
            var sim = new Simulation(new SimConfig());
            sim.StepTo(5);
            var e = StreamEntry.Jump(6, 1, 3);
            e.KnownAt = 4;
            Assert.Throws<ArgumentException>(() => sim.AddKnown(e));
            e.KnownAt = 5;
            Assert.DoesNotThrow(() => sim.AddKnown(e));
        }

        [Test]
        public void StepToRunsWholeTicksAt120PerSecond()
        {
            var sim = new Simulation(new SimConfig());
            sim.StepTo(1.0);
            Assert.AreEqual(120, sim.State.Tick);
            Assert.AreEqual(1.0, sim.Now, 1e-12);
            sim.StepTo(1.0 + 1.5 / 120);
            Assert.AreEqual(121, sim.State.Tick);
            sim.StepTo(0.5);
            Assert.AreEqual(121, sim.State.Tick, "time never runs backwards");
            Assert.AreEqual(sim.Now, sim.State.RunClock, 1e-9);
        }

        [Test]
        public void KeysAreRecordedAtTheSimsCurrentTime()
        {
            var sim = new Simulation(new SimConfig());
            sim.StepTo(10.08);
            sim.Press(Key.Left, 10.02);
            sim.Release(Key.Left, 10.2);
            Assert.AreEqual(2, sim.Keys.Length);
            Assert.AreEqual(Key.Left, sim.Keys[0].Key);
            Assert.IsTrue(sim.Keys[0].Down);
            Assert.AreEqual(10.02, sim.Keys[0].Heard, 1e-12);
            Assert.AreEqual(sim.Now, sim.Keys[0].Effective, 1e-12);
            Assert.AreEqual(10.075, sim.Keys[0].Effective, 1e-9); // the frontier, in whole ticks
            Assert.IsFalse(sim.Keys[1].Down);
        }

        [Test]
        public void EventsAreReadByCursorAndDiscardingKeepsTheHash()
        {
            var song = new FixtureSong().Beats(0, 20, 120);
            var sim = Drive.Replay(Drive.Known(song.Entries()), 10);
            int n = sim.EventCount;
            Assert.Greater(n, 20);
            Assert.AreEqual(n, sim.EventsSince(0).Length);
            Assert.AreEqual(0, sim.EventsSince(n).Length);
            ulong hash = sim.Hash;
            var tail = sim.EventsSince(n - 3).ToArray();
            sim.DiscardEventsBefore(n - 3);
            Assert.AreEqual(hash, sim.Hash);
            CollectionAssert.AreEqual(tail, sim.EventsSince(n - 3).ToArray());
            Assert.Throws<ArgumentOutOfRangeException>(() => sim.EventsSince(0));
        }

        [Test]
        public void TypedViewsNameTheSlots()
        {
            var tick = StreamEntry.Tick(1, 0, 0.7, 0.4, (int)LoudnessLevel.Loud, 13, 0.1, 0.2, 0.3, 0.4, 0.5);
            Assert.AreEqual(0.7, tick.AsTick.Evidence);
            Assert.AreEqual(LoudnessLevel.Loud, tick.AsTick.Level);
            Assert.AreEqual(13, tick.AsTick.Key);
            Assert.AreEqual(0.4, tick.AsTick.Bass);
            Assert.Throws<InvalidOperationException>(() => { var _ = tick.AsBeat; });
            var bare = StreamEntry.Tick(1, 0, 0.7, 0.4);
            Assert.IsFalse(bare.AsTick.HasBands);
            Assert.IsFalse(bare.AsTick.HasLevel);
            var chunk = StreamEntry.Hindsight(2, 1, 4, true, new[] { 2.0, 2.5, 3.0 });
            Assert.AreEqual(3, chunk.AsHindsight.Count);
            Assert.AreEqual(2.5, chunk.AsHindsight.Time(1));
            Assert.IsTrue(chunk.AsHindsight.Online);
            var death = SimEvent.Death(3, 7, 2, 1, 2, 0.6, 0.8, 5);
            Assert.AreEqual(7, death.AsDeath.Enemy);
            Assert.AreEqual(5, death.AsDeath.Force);
            var beat = SimEvent.Beat(1, 4, 2, true, Reliability.Reliable, 0.5, 0.9);
            Assert.IsTrue(beat.AsBeat.Downbeat);
            Assert.AreEqual(Reliability.Reliable, beat.AsBeat.Reliability);
        }

        [Test]
        public void BeatsAndTicksOutOfContentOrderAreDropped()
        {
            var sim = new Simulation(new SimConfig());
            sim.Add(StreamEntry.Beat(5.0, 0, 0.5, 0.9));
            sim.Add(StreamEntry.Beat(4.5, 1, 0.5, 0.9));
            sim.StepTo(2);
            Assert.AreEqual(1, sim.Stream.Count(EntryKind.Beat));
            Assert.AreEqual(1, sim.Stream.Dropped);
        }
    }
}
