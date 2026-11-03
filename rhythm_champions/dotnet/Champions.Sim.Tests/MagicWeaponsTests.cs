using System;
using System.Linq;
using NUnit.Framework;

namespace Champions.Sim.Tests
{
    public class MagicWeaponsTests
    {
        static Simulation Make(WeaponKind kind, int level, params Dummy[] targets)
        {
            var sim = new Simulation(new SimConfig { SurvivorWeapons = true, Loadout = Array.Empty<Upgrade>(), Dummies = targets });
            sim.State.Build = new WeaponBuild(CombatRules.Default);
            sim.State.Build.Set(kind, level);
            return sim;
        }
        [TestCase(WeaponKind.Lightning)]
        [TestCase(WeaponKind.EmberField)]
        public void AllEightLevelsIncreaseDamageAndPreserveMightTempoReachEcho(WeaponKind kind)
        {
            double previous = 0;
            for (int level = 1; level <= 8; level++)
            {
                var plain = Make(kind, level, new Dummy(1, 0, 100));
                double cadence = WeaponScheduler.BaseSeconds(kind);
                plain.StepTo(cadence);
                double damage = 100 - plain.Enemies[0].Health;
                Assert.That(damage, Is.GreaterThan(previous)); previous = damage;
                var supported = Make(kind, level, new Dummy(1, 0, 100));
                supported.State.Build.Set(SupportKind.Might, 5);
                supported.State.Build.Set(SupportKind.Tempo, 5);
                supported.State.Build.Set(SupportKind.Reach, 5);
                supported.State.Build.Set(SupportKind.Echo, 5);
                supported.StepTo(cadence * 2 + .5);
                Assert.That(100 - supported.Enemies[0].Health, Is.GreaterThan(damage * 2));
                var actions = Drive.Events(supported, SimEventKind.MagicAction);
                Assert.That(actions.All(e => e.I1 == CombatRules.FormIndex(level)), Is.True);
                Assert.That(actions.All(e => e.D4 >= (kind == WeaponKind.Lightning ? 1 : MagicWeapons.FieldRadius) * Math.Sqrt(1.5)), Is.True);
            }
        }
        [TestCase(1)] [TestCase(3)] [TestCase(5)] [TestCase(8)]
        public void EmptyFieldHoldsEmpowermentAndStrikeCarryUntilATargetEnters(int level)
        {
            var sim = Make(WeaponKind.EmberField, level);
            sim.State.Build.Set(SupportKind.Tempo, 1);
            sim.State.Build.Empower();
            sim.StepTo(4);
            var weapon = sim.State.Build.Weapon(WeaponKind.EmberField);
            Assert.That(weapon.Empowered, Is.True);
            Assert.That(weapon.StrikeCarry, Is.Zero);
            Assert.That(Drive.Events(sim, SimEventKind.MagicAction), Is.Empty);
            sim.Enemies.Spawn(sim, MagicWeapons.FieldRadius * 1.1, 0, .01, 100);
            sim.StepTo(5);
            Assert.That(weapon.Empowered, Is.False);
            Assert.That(weapon.StrikeCarry, Is.EqualTo(.1).Within(1e-9));
            Assert.That(Drive.Events(sim, SimEventKind.Hit), Is.Not.Empty);
        }
        [TestCase(1)] [TestCase(3)] [TestCase(5)] [TestCase(8)]
        public void LightningVolleyForksAndChainsCannotFarmOneBoss(int level)
        {
            var sim = Make(WeaponKind.Lightning, level);
            int boss = sim.Enemies.Spawn(sim, 1, 0, 2, 100, boss: true);
            sim.State.Build.Set(SupportKind.Multishot, 5);
            sim.State.Build.Set(SupportKind.Pierce, 5);
            sim.State.Build.Set(SupportKind.Split, 5);
            sim.State.Build.Set(SupportKind.Echo, 5);
            sim.StepTo(1.25);
            double basis = 1.1 * sim.State.Build.Stats(WeaponKind.Lightning).DamageFactor;
            Assert.That(100 - sim.Enemies[boss].Health, Is.EqualTo(basis * 1.5).Within(1e-9));
            Assert.That(Drive.Events(sim, SimEventKind.Hit).Count, Is.EqualTo(2));
        }
        [Test]
        public void LightningNearestTiesUseIdRatherThanCellOrder()
        {
            var sim = Make(WeaponKind.Lightning, 1, new Dummy(1, 0, 100, .1), new Dummy(-1, 0, 100, .1));
            sim.StepTo(.75);
            Assert.That(sim.Enemies[0].Health, Is.LessThan(100));
            Assert.That(sim.Enemies[1].Health, Is.EqualTo(100));
        }
        [Test]
        public void ACloserTargetOutsideTheRootCircleCannotHideAnEligibleTarget()
        {
            var sim = Make(WeaponKind.Lightning, 8, new Dummy(7.5, 0, 100, .1), new Dummy(-4, 0, 100, .1));
            sim.StepTo(.75); // First level-eight node is at (2, 0).
            Assert.That(sim.Enemies[0].Health, Is.EqualTo(100));
            Assert.That(sim.Enemies[1].Health, Is.LessThan(100));
        }
        [Test]
        public void ChainReachAndRootReachBothBoundConnectedTargets()
        {
            var sim = Make(WeaponKind.Lightning, 1, new Dummy(1,0,100,.1), new Dummy(3.5,0,100,.1),
                new Dummy(6,0,100,.1), new Dummy(8.5,0,100,.1), new Dummy(-5,0,100,.1));
            sim.State.Build.Set(SupportKind.Pierce, 5);
            sim.StepTo(.75);
            Assert.That(sim.Enemies[0].Health, Is.LessThan(100));
            Assert.That(sim.Enemies[1].Health, Is.LessThan(100));
            Assert.That(sim.Enemies[2].Health, Is.LessThan(100));
            Assert.That(sim.Enemies[3].Health, Is.EqualTo(100), "outside root reach");
            Assert.That(sim.Enemies[4].Health, Is.EqualTo(100), "not connected to the chain");
        }
        [Test]
        public void LevelOneSplitAndMultishotAlreadyReachDistinctTargets()
        {
            var plain = Make(WeaponKind.Lightning, 1, new Dummy(1,0,100), new Dummy(2,0,100), new Dummy(3,0,100));
            plain.StepTo(.75);
            Assert.That(plain.Enemies[1].Health, Is.EqualTo(100));
            var split = Make(WeaponKind.Lightning, 1, new Dummy(1,0,100), new Dummy(2,0,100), new Dummy(3,0,100));
            split.State.Build.Set(SupportKind.Split, 5);
            split.StepTo(.75);
            Assert.That(split.Enemies[1].Health, Is.EqualTo(99.45).Within(1e-9));
            Assert.That(split.Enemies[2].Health, Is.EqualTo(99.45).Within(1e-9));
            var volley = Make(WeaponKind.Lightning, 1, new Dummy(1,0,100), new Dummy(2,0,100));
            volley.State.Build.Set(SupportKind.Multishot, 1); volley.StepTo(.75);
            Assert.That(volley.Enemies[1].Health, Is.EqualTo(98.9).Within(1e-9));
        }
        [TestCase(1)] [TestCase(3)] [TestCase(5)] [TestCase(8)]
        public void FieldComponentsSharePulseLedgerAndEchoIsReduced(int level)
        {
            var sim = Make(WeaponKind.EmberField, level, new Dummy(0,0,100));
            sim.State.Build.Set(SupportKind.Echo, 5); sim.StepTo(1.5);
            Assert.That(100-sim.Enemies[0].Health, Is.EqualTo(.7 * sim.State.Build.Stats(WeaponKind.EmberField).DamageFactor * 1.5).Within(1e-9));
        }
        [Test]
        public void PersistenceHoldsPlacedGroundLongerAndPauseDoesNotConsumeItsLifetime()
        {
            var sim = Make(WeaponKind.EmberField, 1, new Dummy(1,0,100));
            sim.State.Build.Set(SupportKind.Persistence, 5);
            sim.StepTo(1);
            Assert.That(sim.State.Build.Weapon(WeaponKind.EmberField).PersistentUntil, Is.EqualTo(6.25));
            sim.Press(Key.Right, 1.01); sim.StepTo(2); sim.Release(Key.Right, 2);
            var action = Drive.Events(sim, SimEventKind.MagicAction).Last();
            Assert.That(action.D0, Is.EqualTo(0), "stays at the original seal while hero leaves");
            sim.Hold(HoldReason.Pause, true); int hits = Drive.Events(sim, SimEventKind.Hit).Count;
            sim.StepTo(12); sim.Hold(HoldReason.Pause, false);
            Assert.That(Drive.Events(sim, SimEventKind.Hit).Count, Is.EqualTo(hits));
            Assert.That(sim.State.Build.Weapon(WeaponKind.EmberField).PersistentUntil, Is.EqualTo(16.25));
            sim.StepTo(16);
            Assert.That(Drive.Events(sim, SimEventKind.MagicAction).Last().D0, Is.EqualTo(0));
            sim.Enemies.Spawn(sim, sim.State.Hero.X, sim.State.Hero.Y, .1, 100);
            sim.StepTo(17);
            Assert.That(Drive.Events(sim, SimEventKind.MagicAction).Last().D0, Is.GreaterThan(0));
        }
        [TestCase(WeaponKind.Lightning)] [TestCase(WeaponKind.EmberField)]
        public void EmpowermentChangesOnePulseCoverageWithoutChangingDamage(WeaponKind kind)
        {
            var sim = Make(kind, 1, new Dummy(1,0,100));
            sim.State.Build.Empower();
            double seconds = WeaponScheduler.BaseSeconds(kind);
            sim.StepTo(seconds * 2);
            var actions = Drive.Events(sim, SimEventKind.MagicAction);
            Assert.That(actions.Count, Is.EqualTo(2));
            Assert.That(actions[0].D4/actions[1].D4, Is.EqualTo(Math.Sqrt(1.5)).Within(1e-9));
            Assert.That(100-sim.Enemies[0].Health, Is.EqualTo((kind == WeaponKind.Lightning ? 1.1 : .7)*2).Within(1e-9));
        }
        [Test]
        public void MovingConstellationKeepsTheFullHeroSearchCircleAtEveryNode()
        {
            var sim = Make(WeaponKind.Lightning, 8, new Dummy(-6.9,0,100,.01));
            sim.StepTo(.75);
            Assert.That(sim.Enemies[0].Health, Is.LessThan(100));
            Assert.That(Drive.Events(sim, SimEventKind.LightningLink).Count, Is.EqualTo(1));
        }
        [Test]
        public void FieldEchoAfterLongPauseRetainsTheOriginalGroundAndRemainingLifetime()
        {
            var sim = Make(WeaponKind.EmberField, 1, new Dummy(1,0,100));
            sim.State.Build.Set(SupportKind.Echo, 5); sim.StepTo(1);
            sim.Hold(HoldReason.Pause, true); sim.StepTo(11); sim.Hold(HoldReason.Pause, false);
            sim.StepTo(11.5);
            var echo = Drive.Events(sim, SimEventKind.MagicAction).Last();
            Assert.That(echo.T, Is.EqualTo(11.5));
            Assert.That(echo.D5, Is.EqualTo(2.5));
            Assert.That(echo.D0, Is.EqualTo(0));
        }
        [Test]
        public void IncrementalAndSingleFrontierPlaybackProduceIdenticalMagicEventsAndState()
        {
            var a = Make(WeaponKind.Lightning,8,new Dummy(1,0,1000),new Dummy(2,0,1000));
            var b = Make(WeaponKind.Lightning,8,new Dummy(1,0,1000),new Dummy(2,0,1000));
            foreach (var sim in new[] { a,b })
            {
                sim.State.Build.Set(WeaponKind.EmberField,8);
                sim.State.Build.Set(SupportKind.Tempo,5); sim.State.Build.Set(SupportKind.Split,5);
                sim.State.Build.Set(SupportKind.Pierce,2); sim.State.Build.Set(SupportKind.Echo,5);
                sim.State.Build.Set(SupportKind.Persistence,5); sim.State.Build.Empower();
            }
            a.StepTo(20);
            for (int tick=1;tick<=2400;tick++) b.StepTo(tick/120.0);
            Assert.That(b.Hash, Is.EqualTo(a.Hash));
            Assert.That(b.Strikes.Struck, Is.EqualTo(a.Strikes.Struck));
            Assert.That(Drive.Events(a,SimEventKind.LightningLink).Count, Is.GreaterThan(0));
            Assert.That(Drive.Events(a,SimEventKind.MagicAction).Count, Is.GreaterThan(20));
        }
        [Test]
        public void NoTargetHoldsLightningChargeAndSplitEchoStayFiniteInCrowds()
        {
            var sim = Make(WeaponKind.Lightning, 8);
            sim.State.Build.Empower(); sim.StepTo(3);
            Assert.That(sim.State.Build.Weapon(WeaponKind.Lightning).Empowered, Is.True);
            for (int k=0; k<60; k++) sim.Enemies.Spawn(sim, 1+(k%8)*.6, (k/8)*.6, .1, 100);
            sim.State.Build.Set(SupportKind.Multishot,5); sim.State.Build.Set(SupportKind.Pierce,5);
            sim.State.Build.Set(SupportKind.Split,5); sim.State.Build.Set(SupportKind.Echo,5);
            sim.StepTo(4.25);
            Assert.That(Drive.Events(sim, SimEventKind.Hit).Count, Is.LessThanOrEqualTo(120));
            Assert.That(sim.State.Build.Weapon(WeaponKind.Lightning).Empowered, Is.False);
        }
    }
}
