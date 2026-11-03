using System;
using System.Linq;
using System.IO;
using NUnit.Framework;

namespace Champions.Sim.Tests
{
    public class MeleeWeaponsTests
    {
        static Simulation Make(WeaponKind kind, int level, params Dummy[] targets)
        {
            var sim = new Simulation(new SimConfig { SurvivorWeapons = true, Loadout = Array.Empty<Upgrade>(), Dummies = targets });
            sim.State.Build = new WeaponBuild(CombatRules.Default);
            sim.State.Build.Set(kind, level);
            return sim;
        }
        [TestCase(WeaponKind.Sword)]
        [TestCase(WeaponKind.Hammer)]
        public void EveryLevelDealsIncreasingDamageAndFormsRetainSupportScaling(WeaponKind kind)
        {
            double previous = 0;
            for (int level = 1; level <= 8; level++)
            {
                double end = WeaponScheduler.BaseSeconds(kind);
                var plain = Make(kind, level, new Dummy(1, 0, 100));
                plain.StepTo(end);
                double damage = 100 - plain.Enemies[0].Health;
                Assert.That(damage, Is.GreaterThan(previous)); previous = damage;
                var supported = Make(kind, level, new Dummy(1, 0, 100));
                supported.State.Build.Set(SupportKind.Might, 5);
                supported.State.Build.Set(SupportKind.Tempo, 5);
                supported.State.Build.Set(SupportKind.Reach, 5);
                supported.State.Build.Set(SupportKind.Echo, 5);
                supported.StepTo(end * 2 + .5);
                var actions = Drive.Events(supported, SimEventKind.MeleeAction);
                Assert.That(actions.All(e => e.I1 == CombatRules.FormIndex(level)), Is.True);
                Assert.That(actions.Any(e => (e.I2 & 1) != 0), Is.True);
                Assert.That(100 - supported.Enemies[0].Health, Is.GreaterThan(damage * 2));
                Assert.That(Drive.Events(supported, SimEventKind.Strike), Is.Empty, "archived Arsenal is suppressed");
            }
        }
        [TestCase(WeaponKind.Sword, 3)]
        [TestCase(WeaponKind.Sword, 5)]
        [TestCase(WeaponKind.Sword, 8)]
        [TestCase(WeaponKind.Hammer, 3)]
        [TestCase(WeaponKind.Hammer, 5)]
        [TestCase(WeaponKind.Hammer, 8)]
        public void OverlappingComponentsHitOnceAndEchoIsOneReducedRepeat(WeaponKind kind, int level)
        {
            var sim = Make(kind, level, new Dummy(1, 0, 100));
            sim.State.Build.Set(SupportKind.Echo, 5);
            double end = WeaponScheduler.BaseSeconds(kind);
            sim.StepTo(end + .5);
            double basis = (kind == WeaponKind.Hammer ? 1.65 : 1) * sim.State.Build.Stats(kind).DamageFactor;
            Assert.That(100 - sim.Enemies[0].Health, Is.EqualTo(basis * 1.5).Within(1e-9));
            Assert.That(Drive.Events(sim, SimEventKind.MeleeAction).Count, Is.EqualTo(2));
            Assert.That(Drive.Events(sim, SimEventKind.Hit).Count, Is.EqualTo(2));
        }
        [TestCase(WeaponKind.Sword)]
        [TestCase(WeaponKind.Hammer)]
        public void EmpoweredReachConsumesOnlyOnTargetAndChangesAreaNotDamage(WeaponKind kind)
        {
            var sim = Make(kind, 1, new Dummy(MeleeWeapons.Radius(kind) * 1.19, 0, 100, .01));
            sim.StepTo(3);
            Assert.That(Drive.Events(sim, SimEventKind.MeleeAction), Is.Empty);
            sim.State.Build.Empower();
            sim.StepTo(6);
            var actions = Drive.Events(sim, SimEventKind.MeleeAction);
            Assert.That(actions.Count, Is.EqualTo(1));
            Assert.That(actions[0].D4, Is.EqualTo(MeleeWeapons.Radius(kind) * Math.Sqrt(1.5)).Within(1e-9));
            Assert.That(sim.State.Build.Weapon(kind).Empowered, Is.False);
            Assert.That(100 - sim.Enemies[0].Health, Is.EqualTo(kind == WeaponKind.Hammer ? 1.65 : 1).Within(1e-9));
        }
        [Test]
        public void SwordTargetsCrowdAndDanceExtendsLaneWhileHammerProcessionAdvancesOnGrid()
        {
            var sword = Make(WeaponKind.Sword, 8, new Dummy(2, 0, 100), new Dummy(5, 0, 100), new Dummy(2, .8, 100), new Dummy(-2, 0, 100));
            sword.StepTo(1);
            Assert.That(sword.Enemies[1].Health, Is.LessThan(100));
            var hammer = Make(WeaponKind.Hammer, 8, new Dummy(1, 0, 100));
            hammer.StepTo(4.5);
            var falls = Drive.Events(hammer, SimEventKind.MeleeAction);
            Assert.That(falls.Select(e => e.T), Is.EqualTo(new[] { 1.5, 3, 4.5 }));
            Assert.That(falls.Select(e => e.D5), Is.EqualTo(new[] { 0, 2.6 * .7, 2.6 * 1.4 }).Within(1e-9));
        }
        [TestCase(5)]
        [TestCase(8)]
        public void HammerProcessionRetainsTheEruptionLaneEvenAtItsFirstLanding(int level)
        {
            var sim = Make(WeaponKind.Hammer, level, new Dummy(1, 0, 100), new Dummy(4, 0, 100, .1));
            sim.StepTo(1.5);
            Assert.That(sim.Enemies[1].Health, Is.LessThan(100));
        }
        [TestCase(WeaponKind.Sword)]
        [TestCase(WeaponKind.Hammer)]
        public void DenseMixedBodyCrowdSelectsTheLaneWithMostReachableTargets(WeaponKind kind)
        {
            var random = new Random(73);
            var targets = new Dummy[300];
            for (int i = 0; i < targets.Length; i++)
            {
                double angle = random.NextDouble() * 2 * Math.PI, distance = random.NextDouble() * 3.5;
                targets[i] = new Dummy(Math.Cos(angle) * distance, Math.Sin(angle) * distance, 100, .05 + random.NextDouble() * .7);
            }
            var sim = Make(kind, 1, targets);
            sim.State.Build.Empower();
            double radius = MeleeWeapons.Radius(kind) * Math.Sqrt(1.5);
            int expected = 0;
            foreach (var target in targets)
            {
                double distance = Math.Sqrt(target.X * target.X + target.Y * target.Y);
                var shape = HitShape.Cone(0, 0, target.X / distance, target.Y / distance, radius, Math.PI * .42);
                int count = targets.Count(t => shape.Touches(t.X, t.Y, t.Radius));
                expected = Math.Max(expected, count);
            }
            sim.StepTo(WeaponScheduler.BaseSeconds(kind));
            Assert.That(Enumerable.Range(0, targets.Length).Count(i => sim.Enemies[i].Health < 100), Is.EqualTo(expected));
        }
        [Test]
        public void NestedSplitQueriesCannotReplaceTheParentTargetList()
        {
            var sim = Make(WeaponKind.Sword, 1, new Dummy(1, 0, 100), new Dummy(2, 0, 100), new Dummy(10, 0, 100));
            var activation = new WeaponActivation(WeaponKind.Spear, 0, 1, 1, 1, 1, 1,
                new CombatStats { SplitBonus = .5 }, false);
            sim.Weapons.Supports.Launch(activation, 1, HitWindow.Strike, effect =>
            {
                var shape = effect.ImpactTarget.HasValue ? HitShape.Circle(10, 0, 1) : HitShape.Circle(1.5, 0, 1);
                sim.Strikes.LandWeapon(sim, new Blow { T = 0, Shape = shape, Damage = effect.Damage, Source = BlowSource.Sword }, effect);
            }, authoredTargets: 20);
            Assert.That(sim.Enemies[0].Health, Is.EqualTo(99));
            Assert.That(sim.Enemies[1].Health, Is.EqualTo(99));
            Assert.That(sim.Enemies[2].Health, Is.EqualTo(99.5));
        }
        [Test]
        public void SurvivorSessionInputsRoundtripAndReplayTheSameActions()
        {
            var run = new RunInputs { SurvivorWeapons = true, Dummies = new[] { new Dummy(1, 0, 100) } };
            using var bytes = new MemoryStream();
            using (var writer = new BinaryWriter(bytes, System.Text.Encoding.UTF8, true)) run.Write(writer);
            bytes.Position = 0;
            using var reader = new BinaryReader(bytes);
            var restored = RunInputs.Read(reader, 6, "test");
            Assert.That(restored.SurvivorWeapons, Is.True);
            var live = new Simulation(run.Build(null));
            var replay = new Simulation(restored.Build(null));
            live.StepTo(5); replay.StepTo(5);
            Assert.That(replay.Hash, Is.EqualTo(live.Hash));
            Assert.That(Drive.Events(replay, SimEventKind.MeleeAction).Count, Is.EqualTo(5));
        }
        [Test]
        public void RecycledEnemySlotCanBeHitByCapturedEcho()
        {
            var sim = Make(WeaponKind.Sword, 1, new Dummy(1, 0, .5));
            sim.State.Build.Set(SupportKind.Echo, 5);
            sim.StepTo(1);
            int id = sim.Enemies.Spawn(sim, 1, 0, .5, 10);
            sim.StepTo(1.5);
            Assert.That(sim.Enemies[id].Health, Is.EqualTo(9.5).Within(1e-9));
        }
    }
}
