using System;
using System.Linq;
using NUnit.Framework;
namespace Champions.Sim.Tests
{
    public class RangedWeaponTests
    {
        static Simulation Make(WeaponKind kind, int level, params Dummy[] enemies)
        {
            var sim = new Simulation(new SimConfig { SurvivorWeapons = true, Loadout = Array.Empty<Upgrade>(), Dummies = enemies });
            sim.State.Build = new WeaponBuild(CombatRules.Default);
            sim.State.Build.Set(kind, level);
            return sim;
        }
        [TestCase(WeaponKind.Spear)] [TestCase(WeaponKind.Bow)]
        public void EveryLevelFiresAndOrdinaryLevelsIncreaseDamage(WeaponKind kind)
        {
            double previous = 0;
            for (int level = 1; level <= 8; level++)
            {
                var sim = Make(kind, level, new Dummy(0, 6, 1000, 1));
                sim.StepTo(kind == WeaponKind.Spear ? 1.49 : 1.99);
                var hits = Drive.Events(sim, SimEventKind.Hit).Where(e => e.I1 == (int)(kind == WeaponKind.Spear ? BlowSource.Spear : BlowSource.Bow)).ToArray();
                Assert.That(hits.Length, Is.EqualTo(1), $"{kind} level {level}: overlapping formation must not farm one body");
                Assert.That(hits[0].D2, Is.GreaterThan(previous));
                previous = hits[0].D2;
            }
        }
        [TestCase(WeaponKind.Spear, 1)] [TestCase(WeaponKind.Spear, 3)]
        [TestCase(WeaponKind.Spear, 5)] [TestCase(WeaponKind.Spear, 8)]
        [TestCase(WeaponKind.Bow, 1)] [TestCase(WeaponKind.Bow, 3)]
        [TestCase(WeaponKind.Bow, 5)] [TestCase(WeaponKind.Bow, 8)]
        public void FormsRetainSupportsAndBoundBossHits(WeaponKind kind, int level)
        {
            var sim = Make(kind, level, new Dummy(0, 6, 10000, 2));
            foreach (var support in new[] { SupportKind.Might, SupportKind.Reach, SupportKind.Multishot, SupportKind.Pierce, SupportKind.Split, SupportKind.Echo })
                sim.State.Build.Set(support, 5);
            sim.State.Build.Empower();
            sim.StepTo(kind == WeaponKind.Spear ? 0.77 : 1.02);
            Assert.That(sim.Ranged.Flights.Count, Is.EqualTo(RangedWeapons.Count(kind, level) + 5));
            Assert.That(sim.Ranged.Flights.All(f => f.Effect.Activation.EmpowermentArea == 1.5), Is.True);
            Assert.That(sim.Ranged.Flights[0].Width, Is.GreaterThan(kind == WeaponKind.Spear ? .2 : .16));
            sim.StepTo(kind == WeaponKind.Spear ? 1.49 : 1.99);
            var hits = Drive.Events(sim, SimEventKind.Hit).Where(e => e.I1 == (int)(kind == WeaponKind.Spear ? BlowSource.Spear : BlowSource.Bow)).ToArray();
            Assert.That(hits.Length, Is.InRange(1, 2), "one primary window and at most one echo window, despite split/multishot");
            Assert.That(sim.Ranged.Flights.Count, Is.LessThan(120));
        }
        [Test]
        public void SpearIsNarrowAndPierceContinuesThroughASecondBody()
        {
            var sim = Make(WeaponKind.Spear, 1, new Dummy(0, 3, 100), new Dummy(0, 5, 100), new Dummy(2, 5, 100));
            sim.StepTo(1.3);
            Assert.That(sim.Enemies[0].Health, Is.LessThan(100));
            Assert.That(sim.Enemies[1].Health, Is.LessThan(100));
            Assert.That(sim.Enemies[2].Health, Is.EqualTo(100));
        }
        [TestCase(5)] [TestCase(8)]
        public void MarchingRanksRetainEveryLaneOfThePreviousPhalanx(int level)
        {
            var dummies = new[] { -1.3, -.65, 0, .65, 1.3 }.Select(x => new Dummy(x, 6, 100, .06)).ToArray();
            var sim = Make(WeaponKind.Spear, level, dummies);
            sim.StepTo(1.49);
            Assert.That(sim.Enemies.All.ToArray().All(e => e.Health < 100), Is.True,
                "the level-eight ranks must not narrow the five-lane level-five footprint");
        }
        [Test]
        public void BowAimsAtDistantPressureAndArcsBeforeLanding()
        {
            var sim = Make(WeaponKind.Bow, 5, new Dummy(0, 3, 100), new Dummy(10, 10, 100));
            sim.StepTo(1.1);
            Assert.That(sim.Ranged.Flights.Count, Is.EqualTo(5));
            var f = sim.Ranged.Flights[2];
            Assert.That(f.ToX, Is.EqualTo(10).Within(.01));
            f.Pose(f.Duration * .5, out _, out _, out double height);
            Assert.That(height, Is.GreaterThan(3));
            sim.StepTo(1.99);
            Assert.That(sim.Enemies[0].Health, Is.EqualTo(100));
            Assert.That(sim.Enemies[1].Health, Is.LessThan(100));
        }
        [TestCase(WeaponKind.Spear)] [TestCase(WeaponKind.Bow)]
        public void HoldsFreezeFlightsAndNoTargetsRetainEmpowerment(WeaponKind kind)
        {
            var empty = Make(kind, 8);
            empty.State.Build.Empower(); empty.StepTo(3);
            Assert.That(empty.Ranged.Flights, Is.Empty);
            Assert.That(empty.State.Build.Weapon(kind).Empowered, Is.True);
            var sim = Make(kind, 8, new Dummy(0, 6, 100));
            sim.StepTo(kind == WeaponKind.Spear ? .85 : 1.1);
            double age = sim.Ranged.Flights[0].Age;
            sim.Hold(HoldReason.Pause, true); sim.StepTo(5);
            Assert.That(sim.Ranged.Flights[0].Age, Is.EqualTo(age));
            sim.Hold(HoldReason.Pause, false); sim.StepTo(5.1);
            Assert.That(sim.Ranged.Flights[0].Age, Is.GreaterThan(age));
        }
        [TestCase(WeaponKind.Spear, 1)] [TestCase(WeaponKind.Spear, 3)]
        [TestCase(WeaponKind.Spear, 5)] [TestCase(WeaponKind.Spear, 8)]
        [TestCase(WeaponKind.Bow, 1)] [TestCase(WeaponKind.Bow, 3)]
        [TestCase(WeaponKind.Bow, 5)] [TestCase(WeaponKind.Bow, 8)]
        public void TempoAddsIndependentDamageWindowsAcrossForms(WeaponKind kind, int level)
        {
            var sim = Make(kind, level, new Dummy(0, 6, 10000, 2));
            sim.State.Build.Set(SupportKind.Tempo, 5);
            sim.StepTo(kind == WeaponKind.Spear ? 2.2 : 2.99);
            var hits = Drive.Events(sim, SimEventKind.Hit).Where(e => e.I1 == (int)(kind == WeaponKind.Spear ? BlowSource.Spear : BlowSource.Bow)).ToArray();
            Assert.That(hits.Length, Is.EqualTo(3), "first activation one window, second activation two independent strikes");
        }
        [TestCase(WeaponKind.Spear, 1)] [TestCase(WeaponKind.Spear, 3)]
        [TestCase(WeaponKind.Spear, 5)] [TestCase(WeaponKind.Spear, 8)]
        [TestCase(WeaponKind.Bow, 1)] [TestCase(WeaponKind.Bow, 3)]
        [TestCase(WeaponKind.Bow, 5)] [TestCase(WeaponKind.Bow, 8)]
        public void EchoActuallyDeliversDamageInEveryForm(WeaponKind kind, int level)
        {
            var plain = Make(kind, level, new Dummy(0, 6, 10000, 2));
            var echo = Make(kind, level, new Dummy(0, 6, 10000, 2));
            echo.State.Build.Set(SupportKind.Echo, 5);
            plain.StepTo(4.99); echo.StepTo(4.99);
            Assert.That(echo.Enemies[0].Health, Is.LessThan(plain.Enemies[0].Health));
        }
        [Test]
        public void SplitArrowsLeaveTheirSeedBodyAndHitBothSideTargets()
        {
            var sim = Make(WeaponKind.Bow, 1, new Dummy(0, 3, 100, .2), new Dummy(0, 10, 100, .1),
                new Dummy(-1.2, 6.5, 100, .2), new Dummy(1.2, 6.5, 100, .2));
            sim.State.Build.Set(SupportKind.Split, 5);
            sim.StepTo(1.99);
            Assert.That(sim.Enemies[0].Health, Is.LessThan(100));
            Assert.That(sim.Enemies[1].Health, Is.EqualTo(100), "primary arrow stops at its first body");
            Assert.That(sim.Enemies[2].Health, Is.LessThan(100));
            Assert.That(sim.Enemies[3].Health, Is.LessThan(100));
        }
        [Test]
        public void RangedReplayDoesNotDependOnStepBatchSize()
        {
            var a = Make(WeaponKind.Bow, 8, new Dummy(0, 8, 10000));
            var b = Make(WeaponKind.Bow, 8, new Dummy(0, 8, 10000));
            foreach (var sim in new[] { a, b })
            { sim.State.Build.Set(SupportKind.Echo, 5); sim.State.Build.Set(SupportKind.Split, 5); sim.State.Build.Set(SupportKind.Tempo, 5); }
            a.StepTo(4);
            for (int i = 1; i <= 40; i++) b.StepTo(i * .1);
            Assert.That(a.Hash, Is.EqualTo(b.Hash));
            Assert.That(a.EventHash, Is.EqualTo(b.EventHash));
        }
        [Test]
        public void SlotReuseHasANewHitIdentity()
        {
            var sim = Make(WeaponKind.Spear, 1, new Dummy(0, 3, 1, .1));
            long generation = sim.Enemies[0].Generation;
            sim.StepTo(.90);
            Assert.That(sim.Enemies[0].Alive, Is.False);
            int reused = sim.Enemies.Spawn(sim, 0, 5, .1, 100);
            Assert.That(reused, Is.EqualTo(0));
            Assert.That(sim.Enemies[0].Generation, Is.GreaterThan(generation));
            sim.StepTo(1.14);
            Assert.That(sim.Enemies[0].Health, Is.LessThan(100), "same piercing head can hit the new spawn in the reused slot");
        }
    }
}
