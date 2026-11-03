using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace Champions.Sim.Tests
{
    public class WeaponSupportTests
    {
        static WeaponActivation Activation(CombatStats stats, int strikes = 1) => new WeaponActivation(
            WeaponKind.Bow, 1, 1, strikes, 1, 1.5, 1, stats, false);
        [Test]
        public void VolleyPierceAndExtraStrikesHaveBoundedIndependentHitWindows()
        {
            var supports = new WeaponSupports(); var launched = new List<WeaponEffect>();
            supports.Launch(Activation(new CombatStats { ProjectileBonus = 2, PierceBonus = 1 }, 2), 10, HitWindow.Activation, launched.Add);
            Assert.That(launched.Count, Is.EqualTo(6));
            Assert.That(supports.Impact(launched[0], 100, 1.02), Is.True);
            Assert.That(supports.Impact(launched[0], 100, 1.03), Is.False);
            Assert.That(supports.Impact(launched[0], 101, 1.04), Is.True);
            Assert.That(supports.Impact(launched[0], 102, 1.05), Is.False);
            Assert.That(supports.Impact(launched[1], 100, 1.05), Is.False, "overlapping volley cannot farm one target");
            Assert.That(supports.Impact(launched[3], 100, 1.05), Is.True, "Tempo strike is independent");
            Assert.That(launched[3].Spectral, Is.True);
            Assert.That(launched.All(e => e.Activation.EmpowermentArea == 1.5), Is.True);
        }
        [Test]
        public void AllSharedSupportLevelsAreEvaluatedFromTheBuild()
        {
            var build = new WeaponBuild(CombatRules.Default); build.Set(WeaponKind.Bow, 1); build.Set(WeaponKind.EmberField, 1);
            foreach (var support in new[] { SupportKind.Multishot, SupportKind.Pierce, SupportKind.Split, SupportKind.Echo, SupportKind.Persistence }) build.Set(support, 5);
            var bow = build.Stats(WeaponKind.Bow); var field = build.Stats(WeaponKind.EmberField);
            Assert.That(WeaponSupports.VolleyCount(bow), Is.EqualTo(6));
            Assert.That(WeaponSupports.TargetCount(bow), Is.EqualTo(6));
            Assert.That(bow.SplitBonus, Is.EqualTo(0.5)); Assert.That(bow.EchoBonus, Is.EqualTo(0.5));
            Assert.That(WeaponSupports.Duration(field, 4), Is.EqualTo(7));
            Assert.That(WeaponSupports.Duration(bow, 4), Is.EqualTo(4), "Persistence is compatible with duration, not arbitrary melee/projectiles");
            var state = build.Weapon(WeaponKind.EmberField);
            WeaponSupports.Refresh(state, field, 10, 4);
            Assert.That(WeaponSupports.Active(state, 16.9), Is.True);
            Assert.That(WeaponSupports.Active(state, 17), Is.False);
        }
        [Test]
        public void SplitEchoCombinationsCannotRecurseOrMultiplyBossRepeats()
        {
            var supports = new WeaponSupports(); var launched = new List<WeaponEffect>();
            supports.Launch(Activation(new CombatStats { SplitBonus = 0.5, EchoBonus = 0.5 }), 10, HitWindow.Activation, launched.Add);
            Assert.That(supports.Impact(launched[0], 1, 1.02), Is.True);
            Assert.That(launched.Count, Is.EqualTo(3));
            foreach (var child in launched.Skip(1).ToArray())
            {
                Assert.That(supports.Impact(child, 1, 1.03), Is.False, "split cannot hit its parent target");
                supports.Impact(child, 2 + child.Projectile, 1.04);
            }
            supports.Step(1.5, false, false);
            int echoBossHits = 0;
            for (int i = 3; i < launched.Count; i++) if (supports.Impact(launched[i], 1, 1.6)) echoBossHits++;
            supports.Step(2.1, false, false);
            Assert.That(echoBossHits, Is.EqualTo(1), "one shared independent echo window");
            Assert.That(launched.Count, Is.LessThanOrEqualTo(9));
            Assert.That(launched.All(e => e.Lineage.Generation <= 2), Is.True);
            Assert.That(launched.All(e => e.Activation.EmpowermentArea == 1.5), Is.True);
            Assert.That(launched.Skip(1).All(e => e.Spectral && e.Damage <= 5), Is.True);
        }
        [Test]
        public void LargeMixedEchoBatchKeepsOrderAndDefersCallbackWork()
        {
            var supports = new WeaponSupports();
            var delivered = new List<long>();
            void Launch(long sequence, double t)
            {
                var a = new WeaponActivation(WeaponKind.Bow, t, 8, 1, sequence, 1, 1,
                    new CombatStats { EchoBonus = .5 }, false);
                supports.Launch(a, 1, HitWindow.Activation, e =>
                {
                    if (e.Lineage.Generation == 0) return;
                    delivered.Add(e.Activation.Sequence);
                    if (e.Activation.Sequence == 0) Launch(1000, e.T);
                });
            }
            for (int i = 0; i < 512; i++) Launch(i, i % 2 == 0 ? 1 : 2);
            supports.Step(1.5, false, false);
            Assert.That(delivered, Is.EqualTo(Enumerable.Range(0, 256).Select(i => (long)i * 2)));
            supports.Step(2, false, false);
            Assert.That(delivered.Last(), Is.EqualTo(1000));
            supports.Step(2.5, false, false);
            Assert.That(delivered.Skip(257), Is.EqualTo(Enumerable.Range(0, 256).Select(i => (long)i * 2 + 1)));
            supports.Step(100, false, false);
            Assert.That(delivered.Count, Is.EqualTo(513), "each finite echo is delivered once");
        }
        [TestCase(80)]
        [TestCase(120)]
        [TestCase(174)]
        public void EchoLaunchesOnTheNextGridBeatAndDropsRepeatsDueDuringHold(double bpm)
        {
            var sim = new Simulation(new SimConfig { Loadout = Array.Empty<Upgrade>() });
            foreach (var k in Drive.Known(new FixtureSong().Beats(0, 20, bpm).Entries())) sim.AddKnown(k.Entry, k.KnownAt);
            sim.State.Build.Set(SupportKind.Echo, 1);
            var launched = new List<WeaponEffect>();
            sim.Weapons.Bind(WeaponKind.Sword, _ => true, a => sim.Weapons.Supports.Launch(a, 10, HitWindow.Strike, launched.Add));
            sim.StepTo(8);
            var beats = Drive.Events(sim, SimEventKind.Beat).Where(e => e.BeatReliability == Reliability.Reliable).Select(e => e.T).ToArray();
            foreach (var echo in launched.Where(e => e.Lineage.Generation > 0 && e.T > 2))
                Assert.That(echo.T, Is.EqualTo(beats.First(t => t > echo.Activation.T + 1e-9)).Within(1e-9));
            sim.Hold(HoldReason.Pause, true); int count = launched.Count;
            sim.StepTo(15); Assert.That(launched.Count, Is.EqualTo(count));
            int echoes = launched.Count(e => e.Lineage.Generation > 0);
            sim.Hold(HoldReason.Pause, false); sim.StepTo(15.01);
            Assert.That(launched.Count(e => e.Lineage.Generation > 0), Is.EqualTo(echoes));
        }
    }
}
