using System;
using System.Linq;
using NUnit.Framework;

namespace Champions.Sim.Tests
{
    public class SwordFeelTests
    {
        static Simulation Make(params Dummy[] targets) => new Simulation(new SimConfig
        { SurvivorWeapons = true, Loadout = Array.Empty<Upgrade>(), Dummies = targets });

        [Test]
        public void SwordFollowsMovementRetainsFacingAndLeavesMovementFree()
        {
            var sim = Make(new Dummy(3, 0, .65), new Dummy(-1, 0, .65));
            sim.Press(Key.Right, 0); sim.StepTo(.25); sim.Release(Key.Right, .25);
            double stopped = sim.State.Hero.X;
            sim.StepTo(.5);
            Assert.That(sim.Enemies[0].Alive, Is.False, "weak fodder dies to one held sword swing");
            Assert.That(sim.Enemies[1].Alive, Is.True, "closer enemy behind must not redirect the sword");
            var hit = Drive.Events(sim, SimEventKind.MeleeAction).Single();
            Assert.That((hit.D2, hit.D3), Is.EqualTo((1d, 0d)));
            sim.Press(Key.Right, .5); sim.StepTo(.75);
            Assert.That(sim.State.Hero.X, Is.GreaterThan(stopped), "attacks cannot lock walking");
            var notice = Drive.Events(sim, SimEventKind.Swing).First();
            Assert.That(notice.D0 - notice.T, Is.GreaterThanOrEqualTo(.34), "body receives windup before impact");
        }
        [TestCase(true)] [TestCase(false)]
        public void EveryDashCleavesOnceOnArrivalAndToughEnemiesSurvive(bool onBeat)
        {
            var sim = Make(new Dummy(2, -4, .65), new Dummy(-2, -4, .65), new Dummy(0, -4, 8));
            foreach (var k in Drive.Known(new FixtureSong().Beats(0, 12, 120, onsets: false).Entries())) sim.AddKnown(k.Entry, k.KnownAt);
            double t = onBeat ? 6 : 6.125;
            Arena.Dash(sim, t); sim.StepTo(t + .2);
            var sweeps = Drive.Events(sim, SimEventKind.LandingBlast);
            Assert.That(sweeps.Count, Is.EqualTo(1));
            Assert.That(sweeps[0].T, Is.EqualTo(t + .15).Within(1e-9));
            Assert.That(sweeps[0].D1, Is.EqualTo(-4).Within(1e-9));
            Assert.That(sweeps[0].D2, Is.EqualTo(3 * Math.Sqrt(onBeat ? 2 : 1)).Within(1e-9));
            Assert.That(sim.Enemies[0].Alive || sim.Enemies[1].Alive, Is.False, "full circle clears both sides");
            Assert.That(sim.Enemies[2].Health, Is.EqualTo(7), "one sweep and no conflicting automatic double hit");
            Assert.That(Drive.Events(sim, SimEventKind.Kill).Count, Is.EqualTo(2));
        }
        [Test]
        public void OffbeatDoesNotCancelAndNewWeaponsShareTheWholeRemainingAura()
        {
            var sim = Make();
            foreach (var k in Drive.Known(new FixtureSong().Beats(0, 14, 120, onsets: false).Entries())) sim.AddKnown(k.Entry, k.KnownAt);
            Arena.Dash(sim, 6); sim.Release(Key.Space, sim.Now);
            Arena.Dash(sim, 6.625);
            Assert.That(sim.State.Hero.DashOnBeat, Is.False);
            Assert.That(sim.State.Build.AuraUntil, Is.EqualTo(9).Within(1e-9));
            sim.State.Build.Set(WeaponKind.Bow, 1);
            Assert.That(sim.State.Build.BeginActivation(WeaponKind.Bow, true, sim.CombatTime(sim.Now)), Is.EqualTo(2));
            sim.StepTo(9);
            Assert.That(sim.State.Build.AreaAt(sim.CombatTime(sim.Now)), Is.EqualTo(1));
        }
        [Test]
        public void PersistentPulsesShrinkAtExpiryWhileLaunchedProjectilesKeepTheirFootprint()
        {
            var sim = Make(new Dummy(1, 0, 1000), new Dummy(0, 10, 1000));
            sim.State.Build.Set(WeaponKind.EmberField, 1); sim.State.Build.Set(WeaponKind.Bow, 1);
            sim.State.Build.Empower(); sim.StepTo(2.55);
            var flight = sim.Ranged.Flights.First();
            Assert.That(flight.Effect.Activation.EmpowermentArea, Is.EqualTo(2));
            sim.StepTo(3);
            var field = Drive.Events(sim, SimEventKind.MagicAction).Last(e => (WeaponKind)e.I0 == WeaponKind.EmberField);
            Assert.That(field.D4, Is.EqualTo(MagicWeapons.FieldRadius).Within(1e-9));
            Assert.That(flight.Effect.Activation.EmpowermentArea, Is.EqualTo(2));
        }
    }
}
