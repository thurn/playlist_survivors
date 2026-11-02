using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace Champions.Sim.Tests
{
    /// <summary>Quiet moments (design.md, "Held for a quiet moment"): 0.75 s of silence, or 2 s with no reliable beat.</summary>
    public class QuietTests
    {
        static List<(double T, QuietReason Reason)> Moments(Simulation sim) =>
            Drive.Events(sim, SimEventKind.Quiet).Select(e => (e.T, (QuietReason)e.I0)).ToList();

        [Test]
        public void ThreeQuartersOfASecondOfSilenceIsAQuietMoment()
        {
            var sim = Drive.Run(new FixtureSong().Beats(0, 10, 128).Gap(10, 12, silent: true).Beats(12, 30, 128), 30);
            var moments = Moments(sim);
            // Silent from 10 s: quiet at 10.75. The music is back at 12 s, but there has been no reliable beat
            // since 10 s, so the moment goes on as a beatless stretch until the beat is clear again at 13 s.
            Assert.AreEqual(3, moments.Count);
            Assert.AreEqual(QuietReason.Silence, moments[0].Reason);
            Assert.AreEqual(10.75, moments[0].T, 1e-9);
            Assert.AreEqual(QuietReason.Beatless, moments[1].Reason);
            Assert.AreEqual(12.0, moments[1].T, 1e-9);
            Assert.AreEqual(QuietReason.None, moments[2].Reason);
            Assert.AreEqual(13.0, moments[2].T, 1e-9);
            var events = Drive.Events(sim, SimEventKind.Quiet);
            Assert.AreEqual(10.75, events[1].D0, 1e-9, "one moment, since 10.75");
            Assert.AreEqual(QuietReason.None, sim.State.Quiet);
        }

        [Test]
        public void AShortSilenceIsNoQuietMoment()
        {
            var sim = Drive.Run(new FixtureSong().Beats(0, 10, 128).Gap(10, 10.5, silent: true).Beats(10.5, 30, 128), 30);
            Assert.IsEmpty(Moments(sim), "0.5 s of silence, and 1.5 s without a reliable beat");
        }

        [Test]
        public void TwoSecondsWithoutAReliableBeatIsAQuietMoment()
        {
            var sim = Drive.Run(new FixtureSong().Beats(0, 10, 128).Gap(10, 15).Beats(15, 30, 128), 30);
            var moments = Moments(sim);
            Assert.AreEqual(2, moments.Count);
            Assert.AreEqual((12.0, QuietReason.Beatless), (moments[0].T, moments[0].Reason));
            Assert.AreEqual(16.0, moments[1].T, 1e-9, "until the beat is clear again");
            Assert.AreEqual(QuietReason.None, moments[1].Reason);
        }

        [Test]
        public void NoMusicAtAllIsSilence()
        {
            var sim = Drive.Run(new List<KnownEntry>(), 5);
            var moments = Moments(sim);
            Assert.AreEqual(1, moments.Count);
            Assert.AreEqual((0.75, QuietReason.Silence), (moments[0].T, moments[0].Reason));
            Assert.IsTrue(sim.Quiet.Quiet);
            Assert.AreEqual(0.75, sim.State.QuietSince, 1e-9);
        }

        [Test]
        public void ASongsStartIsNoQuietMoment()
        {
            Assert.IsEmpty(Moments(Drive.Run(new FixtureSong().Beats(0, 20, 128), 20)), "unclear for its first second only");
        }
    }
}
