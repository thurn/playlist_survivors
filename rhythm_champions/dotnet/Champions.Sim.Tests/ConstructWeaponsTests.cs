using System;
using System.Linq;
using NUnit.Framework;

namespace Champions.Sim.Tests
{
    public class ConstructWeaponsTests
    {
        static Simulation Make(WeaponKind kind, int level, params Dummy[] targets)
        {
            var sim = new Simulation(new SimConfig { SurvivorWeapons = true, Loadout = Array.Empty<Upgrade>(), Dummies = targets });
            sim.State.Build = new WeaponBuild(CombatRules.Default);
            sim.State.Build.Set(kind, level);
            return sim;
        }
        [TestCase(WeaponKind.OrbitBlades)]
        [TestCase(WeaponKind.Sentinel)]
        public void EveryLevelScalesDamageAndRetainsAllApplicableSupports(WeaponKind kind)
        {
            double previous = 0;
            for (int level = 1; level <= 8; level++)
            {
                double end = WeaponScheduler.BaseSeconds(kind);
                var sim = Make(kind, level, new Dummy(1, 0, 100));
                sim.StepTo(end);
                double damage = 100 - sim.Enemies[0].Health;
                Assert.That(damage, Is.GreaterThan(previous)); previous = damage;
                var supported = Make(kind, level, new Dummy(1, 0, 100));
                supported.State.Build.Set(SupportKind.Might, 5);
                supported.State.Build.Set(SupportKind.Tempo, 5);
                supported.State.Build.Set(SupportKind.Reach, 5);
                supported.State.Build.Set(SupportKind.Echo, 5);
                supported.StepTo(end * 2 + .5);
                var actions = Drive.Events(supported, SimEventKind.ConstructAction);
                Assert.That(actions.All(e => e.I1 == CombatRules.FormIndex(level)), Is.True);
                Assert.That(100 - supported.Enemies[0].Health, Is.GreaterThan(damage * 2));
                Assert.That(Drive.Events(supported, SimEventKind.Strike), Is.Empty);
            }
        }
        [TestCase(WeaponKind.OrbitBlades)]
        [TestCase(WeaponKind.Sentinel)]
        public void EmpowermentPreservesInnerTargetsAndDoesNotIncreaseDamage(WeaponKind kind)
        {
            foreach (int level in new[] { 1, 3, 5, 8 })
            {
                var sim = Make(kind, level, new Dummy(.1, 0, 100, .01));
                sim.State.Build.Empower();
                sim.StepTo(WeaponScheduler.BaseSeconds(kind));
                double basis = kind == WeaponKind.Sentinel ? .9 : .45;
                Assert.That(100 - sim.Enemies[0].Health, Is.EqualTo(basis * sim.State.Build.Stats(kind).DamageFactor).Within(1e-9));
                var action = Drive.Events(sim, SimEventKind.ConstructAction).Single();
                Assert.That(action.D4, Is.EqualTo(ConstructWeapons.Radius(kind, level) * Math.Sqrt(1.5)).Within(1e-9));
                Assert.That(sim.State.Build.Weapon(kind).Empowered, Is.False);
            }
        }
        [TestCase(WeaponKind.OrbitBlades)]
        [TestCase(WeaponKind.Sentinel)]
        public void EmptySpaceHoldsCarryAndEmpowermentUntilUsefulActivation(WeaponKind kind)
        {
            var sim = Make(kind, 8);
            sim.State.Build.Set(SupportKind.Tempo, 5);
            sim.State.Build.Empower();
            sim.StepTo(4);
            Assert.That(Drive.Events(sim, SimEventKind.ConstructAction), Is.Empty);
            Assert.That(sim.State.Build.Weapon(kind).Empowered, Is.True);
            Assert.That(sim.State.Build.Weapon(kind).StrikeCarry, Is.Zero);
            sim.Enemies.Spawn(sim, 1, 0, .1, 100);
            sim.StepTo(5);
            Assert.That(sim.State.Build.Weapon(kind).Empowered, Is.False);
            Assert.That(Drive.Events(sim, SimEventKind.ConstructAction), Is.Not.Empty);
        }
        [TestCase(WeaponKind.OrbitBlades)]
        [TestCase(WeaponKind.Sentinel)]
        public void EmpowermentEndsAtTheNextPulseAndTempoGrantsIndependentBoundedHits(WeaponKind kind)
        {
            var sim = Make(kind, 8, new Dummy(1, 0, 100));
            sim.State.Build.Set(SupportKind.Tempo, 5);
            sim.State.Build.Empower();
            sim.StepTo(WeaponScheduler.BaseSeconds(kind) * 2);
            var actions = Drive.Events(sim, SimEventKind.ConstructAction);
            Assert.That(actions.Count, Is.EqualTo(3), "one strike then two, independent of roster size");
            Assert.That(actions[0].D4, Is.EqualTo(ConstructWeapons.Radius(kind, 8) * Math.Sqrt(1.5)).Within(1e-9));
            Assert.That(actions[1].D4, Is.EqualTo(ConstructWeapons.Radius(kind, 8)).Within(1e-9));
            double basis = kind == WeaponKind.Sentinel ? .9 : .45;
            Assert.That(100 - sim.Enemies[0].Health, Is.EqualTo(basis * sim.State.Build.Stats(kind).DamageFactor * 3).Within(1e-9));
        }
        [Test]
        public void FormationOverlapAndEchoHaveOneHitPerTargetPerStrike()
        {
            var sim = Make(WeaponKind.Sentinel, 8, new Dummy(2, 0, 100), new Dummy(4, 0, 100));
            sim.State.Build.Set(SupportKind.Echo, 5);
            sim.StepTo(1.5);
            double damage = .9 * sim.State.Build.Stats(WeaponKind.Sentinel).DamageFactor * 1.5;
            Assert.That(100 - sim.Enemies[0].Health, Is.EqualTo(damage).Within(1e-9));
            Assert.That(100 - sim.Enemies[1].Health, Is.EqualTo(damage).Within(1e-9));
            Assert.That(Drive.Events(sim, SimEventKind.ConstructAction).Count, Is.EqualTo(2));
        }
        [TestCase(3)]
        [TestCase(5)]
        [TestCase(8)]
        public void LaterSentinelFormsRetainTheirShieldVolleyAlongsideLeaps(int level)
        {
            var sim = Make(WeaponKind.Sentinel, level, new Dummy(1, 0, 100));
            sim.StepTo(3);
            var roles = Drive.Events(sim, SimEventKind.ConstructAction).Select(e => e.I2 & 3).ToArray();
            Assert.That(roles, Does.Contain(0));
            Assert.That(roles, Does.Contain(1));
            if (level >= 5) Assert.That(roles, Does.Contain(2));
        }
        [Test]
        public void PersistenceAddsVolleyUptimeWithoutAccumulatingRosters()
        {
            var plain = Make(WeaponKind.Sentinel, 8, new Dummy(1, 0, 100));
            var longLived = Make(WeaponKind.Sentinel, 8, new Dummy(1, 0, 100));
            longLived.State.Build.Set(SupportKind.Persistence, 5);
            plain.StepTo(4); longLived.StepTo(4);
            Assert.That(Drive.Events(plain, SimEventKind.ConstructAction).Select(e => e.T), Is.EqualTo(new[] { 1d, 2, 3 }));
            Assert.That(Drive.Events(longLived, SimEventKind.ConstructAction).Select(e => e.T), Is.EqualTo(new[] { 1d, 2, 3, 4 }));
            Assert.That(longLived.State.Build.Weapon(WeaponKind.Sentinel).PersistentUntil, Is.EqualTo(5.2).Within(1e-9));
            plain.StepTo(5); longLived.StepTo(5);
            Assert.That(Drive.Events(plain, SimEventKind.ConstructAction).Last().T, Is.EqualTo(5));
        }
        [Test]
        public void OrbitScythesAlternateAndCrownCoversBothAxesInOneActivation()
        {
            var scythes = Make(WeaponKind.OrbitBlades, 5, new Dummy(4.5, 0, 100, .01), new Dummy(0, 4.5, 100, .01));
            scythes.StepTo(.5);
            Assert.That(scythes.Enemies[1].Health, Is.EqualTo(100));
            Assert.That(scythes.Enemies[0].Health, Is.LessThan(100));
            scythes.StepTo(1);
            Assert.That(scythes.Enemies[1].Health, Is.LessThan(100));
            var crown = Make(WeaponKind.OrbitBlades, 8, new Dummy(4.5, 0, 100, .01), new Dummy(0, 4.5, 100, .01));
            crown.StepTo(.5);
            Assert.That(crown.Enemies[0].Health, Is.LessThan(100));
            Assert.That(crown.Enemies[1].Health, Is.LessThan(100));
        }
        [Test]
        public void IsolatedTargetOnTheOtherScytheAxisDoesNotHoldItsPhaseForever()
        {
            var sim = Make(WeaponKind.OrbitBlades, 5, new Dummy(0, 4.5, 100, .01));
            sim.StepTo(.5);
            Assert.That(sim.Enemies[0].Health, Is.LessThan(100));
        }
        [Test]
        public void PausePreservesSentinelRosterAndEchoLifetime()
        {
            var sim = Make(WeaponKind.Sentinel, 8, new Dummy(1, 0, 100));
            sim.State.Build.Set(SupportKind.Echo, 5);
            sim.StepTo(1.1);
            double until = sim.State.Build.Weapon(WeaponKind.Sentinel).PersistentUntil;
            sim.Control(RunControl.Pause, sim.Now);
            sim.StepTo(11.1);
            sim.Control(RunControl.Resume, sim.Now);
            sim.StepTo(11.5);
            var echo = Drive.Events(sim, SimEventKind.ConstructAction).Last();
            Assert.That(echo.D5, Is.EqualTo(until + 10 - echo.T).Within(1e-9), "echo retains the held roster lifetime");
            sim.StepTo(12);
            Assert.That(sim.State.Build.Weapon(WeaponKind.Sentinel).PersistentUntil,
                Is.EqualTo(until + 10).Within(1e-9), "resume must not create a fresh roster");
            sim.StepTo(14);
            Assert.That(Drive.Events(sim, SimEventKind.ConstructAction).Last().T, Is.LessThan(14), "expired roster waits its remaining cooldown");
            sim.StepTo(15);
            Assert.That(Drive.Events(sim, SimEventKind.ConstructAction).Last().T, Is.EqualTo(15));
        }
        [Test]
        public void HeldWorldDoesNotCatchUpVolleysAndChunkedReplayMatches()
        {
            var a = Make(WeaponKind.Sentinel, 8, new Dummy(1, 0, 100));
            var b = Make(WeaponKind.Sentinel, 8, new Dummy(1, 0, 100));
            a.StepTo(7);
            for (int i = 1; i <= 70; i++) b.StepTo(i / 10d);
            Assert.That(a.Hash, Is.EqualTo(b.Hash));
            a.Hold(HoldReason.Pause, true); a.StepTo(10);
            int count = Drive.Events(a, SimEventKind.ConstructAction).Count;
            a.Hold(HoldReason.Pause, false); a.StepTo(10.1);
            Assert.That(Drive.Events(a, SimEventKind.ConstructAction).Count, Is.EqualTo(count));
        }
    }
}
