using System;
using System.Collections.Generic;
using Game.Client.Hud;
using NUnit.Framework;
using PS.Game.Sim;
using Is = NUnit.Framework.Is;

namespace Game.Tests
{
    /// <summary>The HUD: the beats it may show, the beat track and the gem's timing, the cards' flips, damage numbers' sums and its numbers.</summary>
    public class HudTests
    {
        const double Period = 0.5;

        /// <summary>Committed beats every half second from 2 s, each known <paramref name="ahead"/> before it, and steady clear ticks.</summary>
        static Simulation Beats(double ahead, int count = 20)
        {
            var sim = new Simulation(new SimConfig());
            for (int k = 0; k <= 240; k++)
                sim.AddKnown(StreamEntry.Tick(k * 0.05, 0, evidence: 0.9, energy: 0.6, level: LoudnessLevel.Medium, kick: 0.1), Math.Max(0, k * 0.05 - 2));
            for (int b = 0; b < count; b++) sim.AddKnown(StreamEntry.Beat(2 + b * Period, 0, Period, 0.9), Math.Max(0, 2 + b * Period - ahead));
            sim.StepTo(12);
            return sim;
        }

        [Test]
        public void OnlyBeatsKnownByTheFrameShowHoweverFarTheFrontierRan()
        {
            var sim = Beats(ahead: 0.2);
            // At 5.05 s the beat at 5.5 isn't known yet (known at 5.3), though the sim has it.
            Assert.That(HeardBeats.KnownBy(sim.Stream, 5.05), Is.EqualTo(7), "beats 2.0 to 5.0");
            var at = HeardBeats.At(sim.Stream, sim.Beats, 5.05);
            Assert.That(at.LastT, Is.EqualTo(5.0).Within(1e-9));
            Assert.That(at.NextT, Is.EqualTo(5.5).Within(1e-9), "the latest beat's period carries it on");
            Assert.That(HeardBeats.KnownBy(sim.Stream, 5.3), Is.EqualTo(8));
        }

        [Test]
        public void TheGemsRingClosesOnTheBeatFrameAndTheNextBeatStartsThere()
        {
            var sim = Beats(ahead: 2);
            const double beat = 6.0, frame = 1 / 60.0;
            var before = HeardBeats.At(sim.Stream, sim.Beats, beat - frame);
            var on = HeardBeats.At(sim.Stream, sim.Beats, beat);
            Assert.That(before.NextT, Is.EqualTo(beat).Within(1e-9));
            Assert.That(before.Phase(beat - frame), Is.EqualTo(1 - frame / Period).Within(1e-9));
            Assert.That(on.LastT, Is.EqualTo(beat).Within(1e-9), "on the beat's own frame it is the latest");
            Assert.That(on.Phase(beat), Is.EqualTo(0).Within(1e-9));
            Assert.That(on.NextDown, Is.Not.EqualTo(on.LastDown), "two-beat bars alternate");
        }

        [Test]
        public void PastTheLatestBeatsContinuationNoNextBeatIsShown()
        {
            var sim = Beats(ahead: 2, count: 4); // the last beat at 3.5
            var held = HeardBeats.At(sim.Stream, sim.Beats, 3.9);
            Assert.That(held.NextT, Is.EqualTo(4.0).Within(1e-9));
            var gone = HeardBeats.At(sim.Stream, sim.Beats, 3.5 + BeatClock.TrackingGap * Period + 0.01);
            Assert.That(double.IsNaN(gone.NextT));
            Assert.That(gone.Phase(4.5), Is.EqualTo(1));
        }

        [Test]
        public void ABeatOnTimeSitsAtTheHeartAndFadesInFromAfar()
        {
            Assert.That(BeatTrack.Offset(0), Is.EqualTo(BeatTrack.HeartGap));
            Assert.That(BeatTrack.Alpha(0), Is.EqualTo(1).Within(1e-6));
            Assert.That(BeatTrack.Alpha(1.1), Is.EqualTo(0.5).Within(1e-6));
            Assert.That(BeatTrack.Alpha(2.1), Is.EqualTo(0), "beyond the track");
            Assert.That(BeatTrack.Alpha(-0.2), Is.EqualTo(0), "gone just past the heart");
        }

        [Test]
        public void ACardsFaceIsWholeExactlyOnItsBeat()
        {
            const double reveal = 10;
            LevelUpCards.Flip(reveal - LevelUpCards.FlipS - 0.01, reveal, out _, out bool early);
            Assert.That(early, Is.False, "hidden before its flip");
            float edge = LevelUpCards.Flip(reveal - LevelUpCards.FlipS / 2, reveal, out _, out bool mid);
            Assert.That(mid, Is.True);
            Assert.That(edge, Is.LessThan(0.05f), "edge-on halfway");
            float width = LevelUpCards.Flip(reveal, reveal, out bool face, out _);
            Assert.That(face, Is.True);
            Assert.That(width, Is.EqualTo(1).Within(1e-6));
            float back = LevelUpCards.Flip(reveal - LevelUpCards.FlipS * 0.75, reveal, out bool backFace, out _);
            Assert.That(backFace, Is.False, "its back shows first");
            Assert.That(back, Is.GreaterThan(0.5f));
        }

        [Test]
        public void HitsSumPerBeatWhileItContinuesThenInFreeBuckets()
        {
            var beats = new List<(double, int, double)> { (1.0, 4, 0.5), (1.5, 5, 0.5) };
            Assert.That(DamageNumbers.BeatKey(beats, 1.1), Is.EqualTo(DamageNumbers.BeatKey(beats, 1.4)));
            Assert.That(DamageNumbers.BeatKey(beats, 1.6), Is.EqualTo(5));
            Assert.That(DamageNumbers.BeatKey(beats, 1.6), Is.Not.EqualTo(DamageNumbers.BeatKey(beats, 1.4)));
            long free = DamageNumbers.BeatKey(beats, 3.1);
            Assert.That(free, Is.LessThan(0), "past the continuation: a free bucket");
            Assert.That(DamageNumbers.BeatKey(beats, 3.2), Is.EqualTo(free));
            Assert.That(DamageNumbers.BeatKey(beats, 3.6), Is.Not.EqualTo(free));
        }

        [Test]
        public void NumbersAreNativesTimesTen()
        {
            Assert.That(HudText.Damage(1), Is.EqualTo("10"));
            Assert.That(HudText.Damage(3), Is.EqualTo("30"));
            Assert.That(HudText.Damage(1234.5), Is.EqualTo("12.3K"));
            Assert.That(HudText.Damage(123_456), Is.EqualTo("1.2M"));
            Assert.That(HudText.Amount(4.25), Is.EqualTo("4.3"));
            Assert.That(HudText.Amount(15_300), Is.EqualTo("15.3K"));
            Assert.That(HudText.Held(0.533_333), Is.EqualTo("0:00.5"));
            Assert.That(HudText.Held(11.999_999), Is.EqualTo("0:12.0"));
            Assert.That(HudText.Held(1800), Is.EqualTo("30:00.0"));
            Assert.That(HudText.Clock(725.9), Is.EqualTo("12:05"));
            Assert.That(HudText.Name("sword"), Is.EqualTo("SWORD SPIN"));
            Assert.That(HudText.Name("lightning-ring"), Is.EqualTo("LIGHTNING RING"));
            Assert.That(HudText.Mark("magic-wand"), Is.EqualTo("MW"));
            Assert.That(HudText.Mark("garlic"), Is.EqualTo("GA"));
        }
    }
}
