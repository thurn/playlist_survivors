using System.Linq;
using NUnit.Framework;

namespace Champions.Sim.Tests
{
    /// <summary>The beat services (plan.md, "Beat services"): the reliable-beat state, bars, next beats and strong beats.</summary>
    public class BeatServicesTests
    {
        const double P128 = 60.0 / 128;

        [Test]
        public void TheBeatIsReliableOnceTheMusicIsClearAndBeatlessAfterItsLastBeat()
        {
            var sim = Drive.Run(new FixtureSong().Beats(0, 20, 128), 25);
            var beats = sim.Beats;
            // Ticks are clear after a second over the gate.
            Assert.AreEqual(Reliability.BelowGate, beats.StateAt(0.5));
            Assert.AreEqual(Reliability.Reliable, beats.StateAt(5));
            Assert.IsTrue(beats.Reliable(19.5));
            // Past the latest beat's continuation (1.5 periods) no beat is known ahead.
            Assert.AreEqual(Reliability.NoBeatAhead, beats.StateAt(24));
            var states = Drive.Events(sim, SimEventKind.BeatState).Select(e => (Reliability)e.I0).ToArray();
            CollectionAssert.AreEqual(new[] { Reliability.BelowGate, Reliability.Reliable, Reliability.BelowGate, Reliability.NoBeatAhead }, states);
            var reliable = Drive.Events(sim, SimEventKind.BeatState)[1];
            Assert.AreEqual(1.0, reliable.T, 1e-9, "clear from the first tick a second over the gate");
        }

        [Test]
        public void BarsAreFourBeatsCountedFromTheFirstBeatAfterTheLock()
        {
            var sim = Drive.Run(new FixtureSong().Beats(0, 20, 128), 20);
            CollectionAssert.AreEqual(new[] { 1.0 }, sim.Beats.Locks.Locks.ToArray(), "the first clear run locks");
            var beats = Drive.Events(sim, SimEventKind.Beat);
            // The first beat at or after the lock at 1.0 s is beat 3 (1.406 s).
            for (int i = 0; i < 3; i++) Assert.AreEqual(-1, beats[i].I1, $"beat {i} comes before any bar");
            for (int i = 3; i < beats.Count; i++)
            {
                Assert.AreEqual((i - 3) / 4, beats[i].I1, $"beat {i}'s bar");
                Assert.AreEqual((i - 3) % 4 == 0, beats[i].Downbeat, $"beat {i} is a downbeat");
            }
            Assert.IsTrue(sim.Beats.IsDownbeat(11));
            Assert.AreEqual(2, sim.Beats.BarOf(12, out bool downbeat));
            Assert.IsFalse(downbeat);
        }

        [Test]
        public void NextBeatsHalfBeatsAndDownbeatsComeFromTheBeatsKnown()
        {
            var sim = Drive.Run(new FixtureSong().Beats(0, 20, 128), 5);
            var beats = sim.Beats;
            Assert.AreEqual(11 * P128, beats.NextBeat(5.0, out int next), 1e-9);
            Assert.AreEqual(11, next);
            Assert.AreEqual(11 * P128, beats.NextBeat(11 * P128, out _), 1e-9, "a beat at the asked time is next");
            Assert.AreEqual(11.5 * P128, beats.NextHalfBeat(5.2), 1e-9);
            Assert.AreEqual(11 * P128, beats.NextDownbeat(5.0, out int down), 1e-9);
            Assert.AreEqual(11, down);
            Assert.AreEqual(15 * P128, beats.NextDownbeat(5.2, out down), 1e-9);
            Assert.AreEqual(15, down);
        }

        [Test]
        public void BeatsPastTheOnesRevealedArePredictedAtTheLatestTempo()
        {
            var sim = Drive.Run(new FixtureSong().Beats(0, 30, 128), 5);
            int last = sim.Beats.Revealed.Length - 1;
            Assert.Less(sim.Beats.Revealed[last].T, 7.0, "beats are known about 1.25 s ahead");
            Assert.AreEqual((last + 8) * P128, sim.Beats.PredictBeat(last + 8), 1e-6);
            Assert.IsNaN(sim.Beats.BeatTime(last + 8), "the continuation reaches only 1.5 periods");
            Assert.AreEqual((last + 1) * P128, sim.Beats.BeatTime(last + 1), 1e-9);
        }

        [Test]
        public void StrongBeatsHaveAStrongOnsetOnThem()
        {
            var sim = Drive.Run(new FixtureSong().Beats(0, 10, 128), 10);
            var beats = Drive.Events(sim, SimEventKind.Beat);
            for (int i = 0; i < beats.Count; i++) Assert.AreEqual(i % 2 == 0, beats[i].Strong, $"beat {i}: the fixture's even beats are strong");
            Assert.IsTrue(sim.Beats.StrongOnsetNear(4 * P128 + 0.03, 0.05));
            Assert.IsFalse(sim.Beats.StrongOnsetNear(4 * P128 + 0.1, 0.05));
            Assert.IsTrue(sim.Beats.PlayedNear(3 * P128, 0.05), "a weak onset is played");
        }

        [Test]
        public void ATempoIsTheMeanOfABarsIntervals()
        {
            var sim = Drive.Run(new FixtureSong().Beats(0, 20, 128, jitter: 0.01), 20);
            var revealed = sim.Beats.Revealed;
            double mean = (revealed[20].T - revealed[16].T) / 4;
            Assert.AreEqual(mean, sim.Beats.TempoAt(20), 1e-12);
            Assert.AreEqual(revealed[0].V0, sim.Beats.TempoAt(0), 1e-12, "the analyzer's period with no interval");
        }
    }
}
