using NUnit.Framework;

namespace Champions.Sim.Tests
{
    /// <summary>Telegraph timing (design.md, "Threats" and "Every boss"): whole beats landing on a beat, or 0.5 s per beat beatless.</summary>
    public class TelegraphTests
    {
        [TestCase(80)]
        [TestCase(128)]
        [TestCase(174)]
        public void ATelegraphFillsOverWholeBeatsAndLandsOnABeat(double bpm)
        {
            double p = 60.0 / bpm;
            var sim = Drive.Run(new FixtureSong().Beats(0, 30, bpm), 10);
            double asked = 10 + 0.3 * p;
            var t = sim.Telegraphs.Plan(asked, 2);
            Assert.IsTrue(t.OnBeat);
            Assert.AreEqual(2, t.Beats);
            int next = (int)System.Math.Ceiling(asked / p - 1e-9);
            Assert.AreEqual(next, t.StartBeat, "it starts on the next beat");
            Assert.AreEqual(next * p, t.Start, 1e-9);
            Assert.AreEqual((next + 2) * p, t.Land, 1e-6);
            sim.StepTo(t.Land + 0.01);
            Assert.AreEqual(sim.Beats.Revealed[t.LandBeat].T, sim.Telegraphs.LandTime(t), 1e-12, "it lands on its beat");
        }

        [Test]
        public void ATelegraphToADownbeatLandsOnTheFirstDownbeatAfterItsBeats()
        {
            double p = 60.0 / 128;
            var sim = Drive.Run(new FixtureSong().Beats(0, 30, 128), 5);
            // Downbeats are beats 3, 7, 11, 15 (bars count from the lock at 1 s).
            var t = sim.Telegraphs.PlanToDownbeat(5.0, 2);
            Assert.AreEqual(11, t.StartBeat);
            Assert.AreEqual(15, t.LandBeat);
            Assert.AreEqual(15 * p, t.Land, 1e-6);
            var phaseTwo = sim.Telegraphs.PlanToDownbeat(5.0, 1);
            Assert.AreEqual(15, phaseTwo.LandBeat, "a beat at least, to the next downbeat");
            var onDownbeat = sim.Telegraphs.PlanToDownbeat(12.5 * p, 3);
            Assert.AreEqual(13, onDownbeat.StartBeat);
            Assert.AreEqual(19, onDownbeat.LandBeat);
        }

        [Test]
        public void WithoutAReliableBeatATelegraphTakesHalfASecondPerBeat()
        {
            var sim = Drive.Run(new FixtureSong().Gap(0, 30), 10);
            var t = sim.Telegraphs.Plan(10.0, 2);
            Assert.IsFalse(t.OnBeat);
            Assert.AreEqual(10.0, t.Start, 1e-12);
            Assert.AreEqual(11.0, t.Land, 1e-12);
            Assert.AreEqual(11.5, sim.Telegraphs.PlanToDownbeat(10.0, 3).Land, 1e-12);
            Assert.AreEqual(11.0, sim.Telegraphs.LandTime(t), 1e-12);
        }

        [Test]
        public void ALandingPredictedPastTheBeatsKnownMovesToItsBeat()
        {
            var sim = Drive.Run(new FixtureSong().Beats(0, 30, 128, jitter: 0.01), 10);
            int last = sim.Beats.Revealed.Length - 1;
            var t = sim.Telegraphs.Plan(10.0, 6);
            Assert.Greater(t.LandBeat, last, "past the beats revealed");
            sim.StepTo(t.Land + 0.5);
            double actual = sim.Beats.Revealed[t.LandBeat].T;
            Assert.AreNotEqual(actual, t.Land, "the jitter moved it");
            Assert.AreEqual(t.Land, actual, 0.05);
            Assert.AreEqual(actual, sim.Telegraphs.LandTime(t), 1e-12);
        }
    }
}
