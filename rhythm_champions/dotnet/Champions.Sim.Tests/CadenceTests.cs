using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace Champions.Sim.Tests
{
    /// <summary>
    /// The attack cadence (design.md Decision 9): 1, 2 or 4 beats, whichever is nearest 1 s, ties to the
    /// longer; held within 8% of the tempo it was chosen at and changed only on downbeats; a 1 s pulse
    /// with no reliable beat.
    /// </summary>
    public class CadenceTests
    {
        static readonly int[] Counts = { 1, 2, 4 };

        [TestCase(60, 1)]
        [TestCase(80, 1)]
        [TestCase(89, 1)]
        [TestCase(90, 2)] // 1 and 2 beats are both 1/3 s off: the longer
        [TestCase(91, 2)]
        [TestCase(128, 2)]
        [TestCase(174, 2)]
        [TestCase(180, 4)] // 2 and 4 beats are both 1/3 s off: the longer
        [TestCase(200, 4)]
        public void TheCountNearestOneSecondIsChosenTiesToTheLonger(double bpm, int count)
        {
            Assert.AreEqual(count, Cadence.Choose(60.0 / bpm, 1.0, Counts));
        }

        [TestCase(80, 1, 0.75)]
        [TestCase(90, 2, 1.3333333333)]
        [TestCase(128, 2, 0.9375)]
        [TestCase(174, 2, 0.6896551724)]
        public void OnAReliableBeatTheAttackStrikesEveryCountBeats(double bpm, int count, double periodS)
        {
            var sim = Drive.Run(new FixtureSong().Beats(0, 30, bpm), 30);
            var changes = Drive.Events(sim, SimEventKind.Cadence);
            Assert.AreEqual(1, changes.Count, $"{bpm} BPM: chosen once, at the lock");
            Assert.AreEqual(count, changes[0].I0);
            var beats = Drive.Events(sim, SimEventKind.Beat).ToDictionary(e => e.I0);
            int firstReliable = beats.Values.First(e => e.BeatReliability == Reliability.Reliable).I0;
            Assert.AreEqual(beats[firstReliable].T, changes[0].T, 1e-9, "chosen on the first reliable beat");
            var strikes = Drive.Events(sim, SimEventKind.Pulse).Where(e => e.I0 >= 0).ToList();
            Assert.Greater(strikes.Count, 10);
            // On the beat it locked on, or a period later when the pulse struck less than half a period before.
            int first = strikes[0].I0;
            Assert.IsTrue(first == firstReliable || first == firstReliable + count, $"{bpm} BPM: the first strike on beat {first}");
            if (first != firstReliable)
                Assert.Less(beats[firstReliable].T - Drive.Events(sim, SimEventKind.Pulse).Last(e => e.I0 < 0).T, 0.5 * periodS);
            for (int i = 0; i < strikes.Count; i++)
            {
                Assert.AreEqual(first + i * count, strikes[i].I0, $"{bpm} BPM: strike {i}'s beat");
                Assert.AreEqual(beats[strikes[i].I0].T, strikes[i].T, 1e-12, "a strike lands on its beat");
                Assert.AreEqual(periodS, strikes[i].D0, 1e-6);
            }
            Assert.AreEqual(count, sim.State.CadenceBeats);
        }

        [Test]
        public void WithNoReliableBeatTheAttackStrikesOnAOneSecondPulse()
        {
            var sim = Drive.Run(new FixtureSong().Gap(0, 20), 20);
            var strikes = Drive.Events(sim, SimEventKind.Pulse);
            Assert.AreEqual(20, strikes.Count);
            for (int i = 0; i < strikes.Count; i++)
            {
                Assert.AreEqual(i + 1.0, strikes[i].T, 1e-9);
                Assert.AreEqual(-1, strikes[i].I0, "on no beat");
                Assert.AreEqual(0, strikes[i].I1);
            }
            Assert.IsEmpty(Drive.Events(sim, SimEventKind.Cadence));
            Assert.AreEqual(20, Drive.Events(Drive.Run(new List<KnownEntry>(), 20), SimEventKind.Pulse).Count, "no music at all");
        }

        [Test]
        public void TheCadenceMovesBetweenThePulseAndTheBeatWithoutStrikingTwiceInABreath()
        {
            var song = new FixtureSong().Gap(0, 10).Beats(10, 30, 128).Gap(30, 40);
            var sim = Drive.Run(song, 40);
            var changes = Drive.Events(sim, SimEventKind.Cadence);
            Assert.AreEqual(2, changes.Count);
            Assert.AreEqual(2, changes[0].I0, "onto the beat once it is reliable");
            Assert.AreEqual(11.0, changes[0].T, 0.47, "a second after the beats start");
            Assert.AreEqual(0, changes[1].I0, "back to the pulse once it isn't");
            Assert.AreEqual(30.0, changes[1].T, 0.3);
            var strikes = Drive.Events(sim, SimEventKind.Pulse);
            for (int i = 1; i < strikes.Count; i++)
                Assert.GreaterOrEqual(strikes[i].T - strikes[i - 1].T, 0.46, $"strikes {i - 1} and {i}");
            var after = strikes.Where(e => e.T > changes[1].T).ToList();
            Assert.AreEqual(-1, after[0].I0);
            var lastBeatStrike = strikes.Last(e => e.I0 >= 0);
            Assert.AreEqual(lastBeatStrike.T + 1.0, after[0].T, 1e-9, "the pulse goes on a second after the last strike");
        }

        [Test]
        public void ADriftWithinEightPercentKeepsTheCount()
        {
            // 88 BPM chooses every beat; 92 BPM alone would choose every 2 beats, but it is within 8% of 88.
            var sim = Drive.Run(new FixtureSong().Beats(0, 30, 88, 92).Beats(30, 60, 92, 88), 60);
            var changes = Drive.Events(sim, SimEventKind.Cadence);
            Assert.AreEqual(1, changes.Count, "no flicker at the boundary");
            Assert.AreEqual(1, changes[0].I0);
            Assert.AreEqual(2, Cadence.Choose(60.0 / 92, 1.0, Counts));
        }

        [Test]
        public void ATempoChangeBeyondEightPercentChangesTheCountOnADownbeat()
        {
            var sim = Drive.Run(new FixtureSong().Beats(0, 20, 88).Beats(20, 40, 100), 40);
            var changes = Drive.Events(sim, SimEventKind.Cadence);
            Assert.AreEqual(2, changes.Count);
            Assert.AreEqual(1, changes[0].I0);
            Assert.AreEqual(2, changes[1].I0);
            Assert.GreaterOrEqual(changes[1].T, 20.0);
            Assert.Less(changes[1].T, 23.0, "within a bar or so of the change");
            var beat = Drive.Events(sim, SimEventKind.Beat).Single(e => e.T == changes[1].T);
            Assert.IsTrue(beat.Downbeat, "only on a downbeat");
            var strikes = Drive.Events(sim, SimEventKind.Pulse).Where(e => e.T >= changes[1].T).ToList();
            // On the downbeat it changed on, unless the old count struck less than half a period before (here
            // the old tempo's last beat, 0.23 s before): then a period later.
            Assert.AreEqual(beat.I0 + 2, strikes[0].I0);
            for (int i = 1; i < strikes.Count; i++) Assert.AreEqual(2, strikes[i].I0 - strikes[i - 1].I0);
        }

        [Test]
        public void ARampPastEightPercentChangesOnce()
        {
            var sim = Drive.Run(new FixtureSong().Beats(0, 60, 84, 100), 60);
            var changes = Drive.Events(sim, SimEventKind.Cadence);
            Assert.AreEqual(2, changes.Count);
            Assert.AreEqual(2, changes[1].I0);
            // The lock is at about 84.3 BPM and 8% faster is 91.6 BPM: the ramp gets there about 28.5 s in,
            // and the change waits for a bar's tempo and a downbeat.
            Assert.AreEqual(30.0, changes[1].T, 2.0);
            Assert.IsTrue(Drive.Events(sim, SimEventKind.Beat).Single(e => e.T == changes[1].T).Downbeat);
        }

        [Test]
        public void TheNextStrikeIsPlannedOnTheCountsGrid()
        {
            var sim = Drive.Run(new FixtureSong().Beats(0, 30, 128), 10);
            double next = sim.Cadence.NextStrike(10);
            sim.StepTo(12);
            var strike = Drive.Events(sim, SimEventKind.Pulse).First(e => e.T >= 10);
            Assert.AreEqual(strike.T, next, 1e-9);
            var beatless = Drive.Run(new FixtureSong().Gap(0, 10), 5.5);
            Assert.AreEqual(6.0, beatless.Cadence.NextStrike(5.5), 1e-9);
        }
    }
}
