using System;
using System.Linq;
using NUnit.Framework;

namespace Champions.Sim.Tests
{
    public class CleaveTests
    {
        static Simulation Make() => new Simulation(new SimConfig
        { SurvivorWeapons = true, Loadout = Array.Empty<Upgrade>() });
        static void Tap(Simulation s, Key k, double t)
        { s.StepTo(t); s.Press(k, t); s.Release(k, t); }

        [TestCase(false)] [TestCase(true)]
        public void CleaveShieldExpiresAfterActiveTimeEvenAcrossPause(bool pause)
        {
            var s = Make(); Tap(s, Key.F, 1);
            double held = pause ? 5 : 0;
            if (pause)
            {
                s.StepTo(1.1); s.Control(RunControl.Pause, 1.1);
                s.StepTo(6.1);
                Assert.That(Drive.Events(s, SimEventKind.LandingBlast), Is.Empty);
                s.Control(RunControl.Resume, 6.1);
            }
            foreach (double t in new[] { 1.24 + held, 1.26 + held })
            {
                s.StepTo(t);
                s.Health.Hurt(s, new HeroHit { T = t, Damage = 5, Beat = double.NaN });
            }
            s.StepTo(2 + held);
            Assert.That(Drive.Events(s, SimEventKind.HeroHit).Select(e => (HitOutcome)e.I0),
                Is.EqualTo(new[] { HitOutcome.Dodged, HitOutcome.Hurt }));
            var sweep = Drive.Events(s, SimEventKind.LandingBlast).Single();
            Assert.That(sweep.T, Is.EqualTo(1.15 + held).Within(1e-9));
            Assert.That((sweep.D0, sweep.D1), Is.EqualTo((0d, 0d)));
        }

        [TestCase(Key.F)] [TestCase(Key.Space)]
        public void PauseDiscardsBufferedActionAndRequiresANewPress(Key buffered)
        {
            var s = Make(); Tap(s, Key.F, 1); Tap(s, buffered, 1.5);
            Assert.That(s.State.Hero.DashWaiting, Is.True);
            s.Control(RunControl.Pause, 1.5); s.StepTo(6.5);
            s.Control(RunControl.Resume, 6.5); s.StepTo(7);
            Assert.That(s.State.Hero.Dashes, Is.EqualTo(1));
            Tap(s, buffered, 7);
            Assert.That(s.State.Hero.Dashes, Is.EqualTo(2));
            Assert.That(Drive.Events(s, SimEventKind.HeroDash).Last().InPlaceCleave,
                Is.EqualTo(buffered == Key.F));
        }

        [Test]
        public void FRecordsAndReplaysAlongsideSpaceAndPause()
        {
            var run = new RunInputs { SurvivorWeapons = true, Loadout = Array.Empty<Upgrade>() };
            var live = new Simulation(run.Build(null));
            Tap(live, Key.F, 1); Tap(live, Key.Space, 1.5); live.StepTo(1.7);
            live.Control(RunControl.Pause, 1.7); live.Press(Key.F, 1.8);
            live.StepTo(3); live.Control(RunControl.Resume, 3);
            live.Press(Key.F, 3); live.StepTo(4);
            Assert.That(live.State.Hero.Dashes, Is.EqualTo(2), "held F across resume cannot leak");
            live.Release(Key.F, 4); Tap(live, Key.F, 4.2); live.StepTo(4.4);
            string dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString());
            try
            {
                SessionRecord.Of(run, live).Write(dir);
                var record = SessionRecord.Read(dir);
                var replay = new SessionReplay(record);
                var copy = new Simulation(record.Run.Build(null));
                replay.StepTo(copy, double.PositiveInfinity);
                Assert.That(replay.Matches(copy), Is.True);
                Assert.That(record.Inputs.Any(i => i.Key == Key.F), Is.True);
            }
            finally { System.IO.Directory.Delete(dir, true); }
        }

        [Test]
        public void CleaveKillsWeakFodderOnceAndToughTargetSurvives()
        {
            var s = new Simulation(new SimConfig { SurvivorWeapons = true, Loadout = Array.Empty<Upgrade>(),
                Dummies = new[] { new Dummy(2, 0, .65), new Dummy(-2, 0, .65), new Dummy(0, 0, 8) } });
            s.Press(Key.F, 0); s.StepTo(.2);
            Assert.That(s.Enemies[0].Alive || s.Enemies[1].Alive, Is.False);
            Assert.That(s.Enemies[2].Health, Is.EqualTo(7));
            Assert.That(Drive.Events(s, SimEventKind.LandingBlast).Count, Is.EqualTo(1));
        }

        [Test]
        public void OrdinaryCleaveReportsOnlyContactsInItsFootprint()
        {
            var s = new Simulation(new SimConfig { SurvivorWeapons = true, Loadout = Array.Empty<Upgrade>(),
                Dummies = new[] { new Dummy(4.3, 0, 12, .2), new Dummy(-4.5, 0, 12, .2) } });
            foreach (var k in Drive.Known(new FixtureSong().Beats(0, 16, 120, onsets: false).Entries()))
                s.AddKnown(k.Entry, k.KnownAt);
            Tap(s, Key.F, 6.125); s.StepTo(6.4);
            var sweep = Drive.Events(s, SimEventKind.LandingBlast).Single();
            Assert.That(sweep.D2, Is.EqualTo(4.2).Within(1e-6));
            Assert.That(sweep.I1, Is.EqualTo(1));
            Assert.That(s.Enemies[0].Health, Is.EqualTo(11));
            Assert.That(s.Enemies[1].Health, Is.EqualTo(12));
        }

        [Test]
        public void OnbeatBufferedSpaceSurvivesAnOffbeatFReplacement()
        {
            var s = Make();
            foreach (var k in Drive.Known(new FixtureSong().Beats(0, 16, 120, onsets: false).Entries())) s.AddKnown(k.Entry, k.KnownAt);
            Tap(s, Key.F, 6); Tap(s, Key.Space, 6.5); Tap(s, Key.F, 6.59); s.StepTo(6.8);
            var action = Drive.Events(s, SimEventKind.HeroDash).Last();
            Assert.That(action.InPlaceCleave, Is.False);
            Assert.That(action.OnBeat && action.Buffered, Is.True);
            Assert.That(action.Judged, Is.EqualTo(JudgedBy.Beat));
        }
        [Test]
        public void FCannotChooseCardsOrLeakThroughTheirTransition()
        {
            var s = new Simulation(new SimConfig { SurvivorWeapons = true, Start = RunStart.FirstOffer });
            s.StepTo(1); s.Progression.Treasure(s); Assert.That(s.State.Offer.Up, Is.True);
            s.Press(Key.F, 1);
            Assert.That(s.State.Offer.Up, Is.True);
            s.Press(Key.One, 1); s.StepTo(3); s.Press(Key.F, 3);
            Assert.That(s.State.Hero.Dashes, Is.EqualTo(0));
            s.Release(Key.F, 3); Tap(s, Key.F, 3.1);
            Assert.That(s.State.Hero.Dashes, Is.EqualTo(1));
        }

        [TestCase(false)] [TestCase(true)]
        public void CleavePreservesWalkingAndClickTargets(bool click)
        {
            var s = Make();
            if (click) s.Target(20, 0, 0); else s.Press(Key.Right, 0);
            Tap(s, Key.F, 1); s.StepTo(1.2);
            s.State.Hero.PositionAt(1.2, out double x, out double y);
            Assert.That(x, Is.EqualTo(7.2).Within(1e-8));
            Assert.That(y, Is.EqualTo(0).Within(1e-8));
            var sweep = Drive.Events(s, SimEventKind.LandingBlast).Single();
            Assert.That(sweep.T, Is.EqualTo(1.15).Within(1e-9));
            Assert.That(sweep.D0, Is.EqualTo(6.9).Within(1e-8));
        }
        [Test]
        public void StationaryCleaveHasOneHitAndBriefShieldAndCannotRepeatHeld()
        {
            var s = Make(); s.StepTo(1); s.Press(Key.F, 1);
            Assert.That(s.State.Hero.IFramesUntil, Is.EqualTo(1.25).Within(1e-9));
            s.StepTo(2); s.Press(Key.F, 2); s.StepTo(2.2);
            Assert.That(s.State.Hero.Dashes, Is.EqualTo(1));
            Assert.That((s.State.Hero.X, s.State.Hero.Y), Is.EqualTo((0d, 0d)));
            Assert.That(Drive.Events(s, SimEventKind.LandingBlast).Count, Is.EqualTo(1));
            Assert.That(Drive.Events(s, SimEventKind.HeroDash).Single().InPlaceCleave, Is.True);
        }
        [TestCase(Key.F, Key.Space)] [TestCase(Key.Space, Key.F)]
        public void SharedBufferRetainsTheSelectedAction(Key first, Key buffered)
        {
            var s = Make(); Tap(s, first, 1); Tap(s, buffered, 1.1);
            Assert.That(s.State.Hero.Dashes, Is.EqualTo(1));
            Tap(s, buffered, 1.5); s.StepTo(1.8);
            var actions = Drive.Events(s, SimEventKind.HeroDash);
            Assert.That(actions.Count, Is.EqualTo(2));
            Assert.That(actions.Last().D0, Is.EqualTo(1.6).Within(1e-9));
            Assert.That(actions.Last().InPlaceCleave, Is.EqualTo(buffered == Key.F));
        }
        [Test]
        public void TimingRefreshesAuraAndOffbeatRetainsIt()
        {
            var s = Make();
            foreach (var k in Drive.Known(new FixtureSong().Beats(0, 16, 120, onsets: false).Entries())) s.AddKnown(k.Entry, k.KnownAt);
            Tap(s, Key.F, 6); s.StepTo(6.2);
            Assert.That(Drive.Events(s, SimEventKind.LandingBlast).Single().D2, Is.EqualTo(4.2 * Math.Sqrt(2)).Within(1e-9));
            Tap(s, Key.F, 6.625);
            Assert.That(s.State.Hero.DashOnBeat, Is.False);
            Assert.That(s.State.Build.AuraUntil, Is.EqualTo(9).Within(1e-9));
            Tap(s, Key.F, 7.5);
            Assert.That(s.State.Build.AuraUntil, Is.EqualTo(10.5).Within(1e-9));
            s.StepTo(10.5);
            Assert.That(s.State.Build.AreaAt(s.CombatTime(s.Now)), Is.EqualTo(1));
        }
        [Test]
        public void SimultaneousActionsCannotDoubleFireAndBeatlessCleaveEmpowers()
        {
            var s = Make(); s.StepTo(1); s.Press(Key.F, 1); s.Press(Key.Space, 1);
            Assert.That(s.State.Hero.Dashes, Is.EqualTo(1));
            Assert.That(s.State.Build.AuraUntil, Is.EqualTo(4).Within(1e-9));
        }
    }
}
