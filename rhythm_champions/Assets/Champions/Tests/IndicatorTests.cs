using System.Collections.Generic;
using Champions.Client.Scenes;
using Champions.Client.UI;
using Champions.Render;
using Champions.Sim;
using NUnit.Framework;

namespace Champions.Tests
{
    /// <summary>
    /// The hero's indicator (design.md, The Dash > The indicator) following the real sim on a scripted song, 120 BPM to
    /// 20 s and then no beat: the next two beats' ticks converge on the centre and arrive on their beats, none beatless;
    /// the pip cools after a dash and is solid when ready, flashes after an on-beat dash and not after one off the beat,
    /// blinks for a dropped press; health steps when a hit lands and flashes once; the indicator goes with the hero.
    /// </summary>
    public sealed class IndicatorTests
    {
        const double Bpm = 120, Period = 60 / Bpm, BeatsUntil = 20, SongEnd = 30;

        Simulation _sim;
        IndicatorModel _model;
        int _cursor;

        [SetUp]
        public void Make()
        {
            var config = new RunInputs().Build(null);
            _sim = new Simulation(config);
            foreach (var e in new ScriptedSong().Beats(0, BeatsUntil, Bpm).Gap(BeatsUntil, SongEnd).Entries())
                _sim.AddKnown(e, System.Math.Max(e.Stamp + config.KnownAtBudget, _sim.Now)); // as a fixed-time run feeds them
            _model = new IndicatorModel();
        }

        /// <summary>Steps the sim to <paramref name="t"/>, handing the model its events; the indicator at <paramref name="t"/>.</summary>
        IndicatorLook At(double t)
        {
            _sim.StepTo(t);
            foreach (var e in _sim.EventsSince(_cursor)) _model.Take(e);
            _cursor = _sim.EventCount;
            return _model.Look(t, _sim.Snapshot(), _sim.Beats);
        }

        List<SimEvent> Events(SimEventKind kind)
        {
            var list = new List<SimEvent>();
            foreach (var e in _sim.EventsSince(0)) if (e.Kind == kind) list.Add(e);
            return list;
        }

        [Test]
        public void TheNextTwoBeatsTicksArriveAtTheCentreOnTheirBeats()
        {
            double beat = 6.0; // a beat, the beat long reliable
            var before = At(beat - 0.3);
            Assert.That(before.Tick1, Is.EqualTo(0.3 / (2 * Period)).Within(1e-3), "the next beat's ticks, 0.3 s out of a two-beat strip");
            Assert.That(before.Tick2, Is.EqualTo((0.3 + Period) / (2 * Period)).Within(1e-3), "the beat after's");
            Assert.That(before.Tick1Alpha, Is.EqualTo(1f));
            var on = At(beat);
            Assert.That(on.Tick1, Is.EqualTo(0).Within(1e-6), "on its beat, at the centre");
            var after = At(beat + 0.01);
            Assert.That(after.Tick1, Is.EqualTo((Period - 0.01) / (2 * Period)).Within(1e-3), "the next pair carries on");
            Assert.That(after.Tick2, Is.EqualTo((2 * Period - 0.01) / (2 * Period)).Within(1e-3), "the newest pair enters at the ends");
            Assert.That(after.Tick2Alpha, Is.LessThan(0.1f), "and fades in");
        }

        [Test]
        public void BeatlessThereAreNoTicksOnlyThePip()
        {
            var look = At(BeatsUntil + 5);
            Assert.That(_sim.Beats.StateAt(BeatsUntil + 5), Is.Not.EqualTo(Reliability.Reliable));
            Assert.That(look.Tick1Alpha, Is.Zero);
            Assert.That(look.Tick2Alpha, Is.Zero);
            Assert.That(look.Shown && look.Ready, "the pip, ready");
        }

        [Test]
        public void ThePipCoolsAfterADashFlashesOnTheBeatAndIsSolidWhenReady()
        {
            Assert.That(At(5.9).Ready, "ready before any dash");
            At(6.0);
            _sim.Press(Key.Space, 6.0); // heard on the beat
            _sim.Release(Key.Space, 6.0);
            var dash = Events(SimEventKind.HeroDash);
            Assert.That(dash, Has.Count.EqualTo(1));
            Assert.That(dash[0].OnBeat, "pressed on the beat");
            var cooling = At(6.15);
            Assert.That(cooling.Ready, Is.False);
            Assert.That(cooling.Cooldown, Is.EqualTo(0.15 / 0.6).Within(0.02), "a quarter of the 0.6 s cooldown done");
            Assert.That(cooling.Flash, Is.EqualTo(1 - 0.15 / IndicatorModel.FlashS).Within(0.02), "the on-beat flash fading");
            Assert.That(At(6.3).Flash, Is.Zero, "gone within a quarter second");
            var ready = At(6.65);
            Assert.That(ready.Ready && ready.Cooldown == 1, "solid once the cooldown is over");
        }

        [Test]
        public void ADashOffTheBeatDoesntFlash()
        {
            At(6.25);
            _sim.Press(Key.Space, 6.25); // a half beat off: 250 ms from either beat
            _sim.Release(Key.Space, 6.25);
            var dash = Events(SimEventKind.HeroDash);
            Assert.That(dash, Has.Count.EqualTo(1));
            Assert.That(dash[0].OnBeat, Is.False, "off the beat");
            var look = At(6.3);
            Assert.That(look.Flash, Is.Zero);
            Assert.That(look.Ready, Is.False);
        }

        [Test]
        public void APressTooEarlyInTheCooldownBlinksThePip()
        {
            At(6.0);
            _sim.Press(Key.Space, 6.0);
            _sim.Release(Key.Space, 6.0);
            At(6.2);
            _sim.Press(Key.Space, 6.2); // 0.4 s before the dash is ready: dropped
            _sim.Release(Key.Space, 6.2);
            Assert.That(Events(SimEventKind.DashDropped), Has.Count.EqualTo(1));
            var blink = At(6.25);
            Assert.That(blink.Blink, Is.EqualTo(1 - 0.05 / IndicatorModel.BlinkS).Within(0.02));
            Assert.That(At(6.4).Blink, Is.Zero, "a blink, not a state");
        }

        [Test]
        public void HealthStepsWhenAHitLandsFlashingOnceAndTheIndicatorGoesWithTheHero()
        {
            var full = At(3);
            Assert.That(full.Health, Is.EqualTo(1f));
            _model.Take(SimEvent.HeroHit(3.5, HitOutcome.Hurt, HitSource.Touch, 0, 30, 70, 1, 0, -1));
            _model.Take(SimEvent.HeroHit(3.6, HitOutcome.Dodged, HitSource.Touch, 0, 0, 70, 1, 0, 0));
            Assert.That(At(3.4).Health, Is.EqualTo(1f), "not before it lands");
            var hit = At(3.55);
            Assert.That(hit.Health, Is.EqualTo(0.7f).Within(1e-4));
            Assert.That(hit.Hurt, Is.EqualTo(1 - 0.05 / IndicatorModel.HurtS).Within(0.02));
            var later = At(3.9);
            Assert.That(later.Hurt, Is.Zero, "once, no blinking: a dodge flashes nothing");
            Assert.That(later.Health, Is.EqualTo(0.7f).Within(1e-4));
            _model.Take(SimEvent.HeroHeal(4, HealReason.LevelUp, 25, 95));
            Assert.That(At(4.1).Health, Is.EqualTo(0.95f).Within(1e-4));
            _model.Take(SimEvent.HeroDeath(4.5, 0, 0, 1, 0));
            Assert.That(At(4.4).Shown);
            Assert.That(At(4.6).Shown, Is.False, "the hero shatters, the indicator with it");
        }

    }
}
