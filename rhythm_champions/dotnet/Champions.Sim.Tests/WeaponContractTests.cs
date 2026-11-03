using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace Champions.Sim.Tests
{
    public class WeaponContractTests
    {
        static CombatRules Content => GameContent.Load(Checkout.In("Content")).Combat;

        [Test]
        public void SixSlotsAllowOwnedUpgradesButRejectNewItems()
        {
            var build = new WeaponBuild(Content);
            for (int i = 0; i < 6; i++) build.Upgrade((WeaponKind)i);
            foreach (var kind in new[] { SupportKind.Might, SupportKind.Tempo, SupportKind.Reach, SupportKind.Multishot, SupportKind.Pierce, SupportKind.Echo }) build.Upgrade(kind);
            Assert.AreEqual((6, 6), (build.WeaponCount, build.SupportCount));
            Assert.IsFalse(build.CanUpgrade(WeaponKind.Sentinel));
            Assert.IsFalse(build.CanUpgrade(SupportKind.Armor));
            Assert.Throws<InvalidOperationException>(() => build.Set(WeaponKind.Sentinel, 1));
            Assert.Throws<InvalidOperationException>(() => build.Set(SupportKind.Armor, 1));
            for (int level = 2; level <= 8; level++) build.Upgrade(WeaponKind.Sword);
            for (int level = 2; level <= 5; level++) build.Upgrade(SupportKind.Might);
            Assert.IsFalse(build.CanUpgrade(WeaponKind.Sword));
            Assert.IsFalse(build.CanUpgrade(SupportKind.Might));
            Assert.Throws<ArgumentOutOfRangeException>(() => build.Set(WeaponKind.Sword, 9));
            Assert.Throws<ArgumentOutOfRangeException>(() => build.Set(WeaponKind.Sword, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => build.Set(SupportKind.Might, 6));
            Assert.Throws<ArgumentOutOfRangeException>(() => build.Set(SupportKind.Might, 0));
        }

        [Test]
        public void AllWeaponsHaveFixedFormsAndNeverLoseSupportCompatibility()
        {
            var rules = Content;
            for (int w = 0; w < CombatRules.WeaponCount; w++)
            {
                var weapon = rules.Weapon((WeaponKind)w);
                CollectionAssert.AreEqual(new[] { 1, 1, 3, 3, 5, 5, 5, 8 }, Enumerable.Range(1, 8).Select(l => weapon.Form(l).StartLevel));
                Assert.AreEqual(4, Enumerable.Range(1, 8).Select(weapon.FormId).Distinct().Count());
                for (int s = 0; s < CombatRules.SupportCount; s++)
                {
                    bool applied = false;
                    for (int level = 1; level <= 8; level++)
                    {
                        bool now = rules.Support((SupportKind)s).AppliesTo(weapon.Form(level).Capabilities);
                        Assert.IsFalse(applied && !now, $"{(WeaponKind)w}/{(SupportKind)s} at {level}");
                        applied = now;
                    }
                }
            }
            var build = new WeaponBuild(rules);
            build.Upgrade(WeaponKind.Sword);
            Assert.IsFalse(build.CanUpgrade(SupportKind.Multishot));
            Assert.IsFalse(build.CanUpgrade(SupportKind.Persistence));
            build.Upgrade(WeaponKind.Bow);
            Assert.IsTrue(build.CanUpgrade(SupportKind.Multishot));
            build.Upgrade(WeaponKind.EmberField);
            Assert.IsTrue(build.CanUpgrade(SupportKind.Persistence));
        }

        [Test]
        public void ANewFormMayAddCompatibilityAndLaterFormsMustRetainIt()
        {
            string source = File.ReadAllText(Checkout.In("Content", CombatRules.File));
            int boundary = source.IndexOf("\"startLevel\": 3", StringComparison.Ordinal);
            int end = source.IndexOf("\"id\": \"hammer\"", boundary, StringComparison.Ordinal);
            string added = source.Substring(boundary, end - boundary).Replace("\"Melee\"", "\"Melee\", \"Projectile\"");
            var rules = CombatRules.Read(JsonValue.Parse(source.Substring(0, boundary) + added + source.Substring(end)));
            var build = new WeaponBuild(rules);
            build.Set(WeaponKind.Sword, 2);
            Assert.IsFalse(build.CanUpgrade(SupportKind.Multishot));
            build.Upgrade(WeaponKind.Sword);
            Assert.IsTrue(build.CanUpgrade(SupportKind.Multishot));
            build.Upgrade(SupportKind.Multishot);
            build.Set(WeaponKind.Sword, 8);
            Assert.AreEqual(1, build.Stats(WeaponKind.Sword).ProjectileBonus);
        }

        [Test]
        public void FamiliesAddInternallyAndMultiplyConnectedDamageAndStrikesOnly()
        {
            var build = new WeaponBuild(Content);
            build.Set(WeaponKind.Bow, 1);
            build.Set(SupportKind.Might, 5);
            build.Set(SupportKind.Tempo, 5);
            build.Set(SupportKind.Reach, 5);
            build.Set(SupportKind.Multishot, 5);
            var stats = build.Stats(WeaponKind.Bow);
            Assert.AreEqual(2.25, stats.ConnectedOutputFactor, 1e-12);
            Assert.AreEqual(Math.Sqrt(1.5), stats.RadiusFactor, 1e-12);
            stats.Add(StatFamily.Damage, 0.5);
            Assert.AreEqual(2, stats.DamageFactor);
            Assert.AreEqual(3, stats.ConnectedOutputFactor);
            Assert.AreEqual(5, stats.ProjectileBonus, "coverage does not imply repeated boss damage");
            var blast = build.Stats(WeaponBuild.LandingBlastCapabilities);
            Assert.AreEqual((1.5, 1.0, 1.5, 0.0), (blast.DamageFactor, blast.StrikeFactor, blast.AreaFactor, blast.ProjectileBonus));
            build.Set(WeaponKind.Bow, 2);
            Assert.Greater(build.Stats(WeaponKind.Bow).DamageFactor, 1.5, "ordinary weapon levels improve stats");
        }

        [Test]
        public void SharedAuraSurvivesFormsAndNewAcquisitions()
        {
            var build = new WeaponBuild(Content);
            build.Set(WeaponKind.Sword, 2);
            build.Set(WeaponKind.EmberField, 1);
            build.Empower(); build.Empower();
            var sword = build.Weapon(WeaponKind.Sword);
            sword.StrikeCarry = 0.75;
            build.Upgrade(WeaponKind.Sword);
            Assert.AreSame(sword, build.Weapon(WeaponKind.Sword));
            Assert.AreEqual(0.75, sword.StrikeCarry);
            build.Set(WeaponKind.Bow, 1);
            Assert.IsTrue((build.AreaAt(0) > 1), "new acquisition shares the timed aura");
            Assert.AreEqual(1, build.BeginActivation(WeaponKind.Sword, false));
            Assert.IsTrue((build.AreaAt(0) > 1));
            Assert.AreEqual(0, sword.ActivationSequence);
            Assert.AreEqual(2, build.BeginActivation(WeaponKind.Sword, true));
            Assert.AreEqual(2, build.BeginActivation(WeaponKind.Sword, true));
            Assert.IsTrue((build.AreaAt(0) > 1));
            Assert.IsTrue(Content.Weapon(WeaponKind.EmberField).Form(1).Persistent);
            Assert.AreEqual(2, build.BeginActivation(WeaponKind.EmberField, true));
            Assert.AreEqual(1, build.AreaAt(3), "exact expiry");
        }

        [Test]
        public void LargeTargetsCannotBeHitByEveryOverlappingProjectileOrEveryContactFrame()
        {
            foreach (HitWindow policy in Enum.GetValues(typeof(HitWindow)))
            {
                var hits = new TargetHitLedger(policy);
                hits.BeginWindow(0);
                Assert.IsTrue(hits.TryHit(42));
                for (int overlap = 0; overlap < 100; overlap++) Assert.IsFalse(hits.TryHit(42));
                Assert.IsTrue(hits.TryHit(43));
                hits.BeginWindow(0); Assert.IsFalse(hits.TryHit(42));
                hits.BeginWindow(1);
                Assert.AreEqual(policy != HitWindow.Activation, hits.TryHit(42));
                if (policy != HitWindow.Activation) Assert.Throws<InvalidOperationException>(() => hits.BeginWindow(0));
            }
        }

        [TestCase(-1)] [TestCase(3)] [TestCase(255)] [TestCase(int.MinValue)] [TestCase(int.MaxValue)]
        public void InvalidProcAndHitWindowValuesStillThrow(int invalid)
        {
            var root = new EffectLineage();
            Assert.Throws<ArgumentOutOfRangeException>(() => root.TryChild((EffectTrigger)invalid, out _));
            Assert.Throws<ArgumentOutOfRangeException>(() => new TargetHitLedger((HitWindow)invalid));
            var supports = new WeaponSupports();
            Assert.Throws<ArgumentOutOfRangeException>(() => supports.Launch(default, 1, (HitWindow)invalid, _ => { }));
        }
        [Test]
        public void ProcTreesBoundDepthBreadthAndPreventRecursiveEchoSplitAndKill()
        {
            var root = new EffectLineage();
            Assert.IsTrue(root.TryChild(EffectTrigger.Split, out var split));
            Assert.IsFalse(split.TryChild(EffectTrigger.Split, out _));
            Assert.IsTrue(split.TryChild(EffectTrigger.Echo, out var echo));
            Assert.IsFalse(echo.TryChild(EffectTrigger.Echo, out _));
            Assert.IsTrue(echo.TryChild(EffectTrigger.Kill, out var kill));
            Assert.IsFalse(kill.TryChild(EffectTrigger.Kill, out _));
            Assert.AreEqual(3, kill.Generation);
            var nodes = new Queue<EffectLineage>(); nodes.Enqueue(new EffectLineage());
            int count = 1;
            while (nodes.Count > 0)
            {
                var node = nodes.Dequeue();
                foreach (EffectTrigger trigger in Enum.GetValues(typeof(EffectTrigger)))
                {
                    int siblings = 0;
                    while (node.TryChild(trigger, out var child)) { siblings++; count++; nodes.Enqueue(child); Assert.LessOrEqual(child.Generation, 3); }
                    Assert.LessOrEqual(siblings, trigger == EffectTrigger.Split ? 2 : 1);
                }
            }
            Assert.LessOrEqual(count, 32);
            Assert.Greater(count, 10, "mixed mechanics remain possible");
        }

        [Test]
        public void ContentRoundtripsCanonicallyUnderOtherLocalesAndRejectsInvalidContracts()
        {
            var rules = Content;
            Assert.AreEqual(CombatRules.Default.ToJson(), rules.ToJson());
            var culture = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
                string json = rules.ToJson();
                Assert.AreEqual(json, CombatRules.Read(JsonValue.Parse(json)).ToJson());
                Assert.Throws<ContentException>(() => CombatRules.Read(JsonValue.Parse(json.Replace("\"startLevel\":3", "\"startLevel\":4"))));
                Assert.Throws<ContentException>(() => CombatRules.Read(JsonValue.Parse(json.Replace("\"maxLevel\":5", "\"maxLevel\":0"))));
                Assert.Throws<ContentException>(() => CombatRules.Read(JsonValue.Parse(json.Replace("\"Area\"", "\"Unknown\""))));
                Assert.Throws<ContentException>(() => CombatRules.Read(JsonValue.Parse(json.Replace("\"damagePerLevel\":0.1", "\"damagePerLevel\":-0.1"))));
                Assert.Throws<ContentException>(() => CombatRules.Read(JsonValue.Parse(json.Replace("\"id\":\"hammer\"", "\"id\":\"sword\""))));
                Assert.Throws<ContentException>(() => CombatRules.Read(JsonValue.Parse(json.Replace("\"damagePerLevel\":0.1", "\"typo\":1,\"damagePerLevel\":0.1"))));
            }
            finally { CultureInfo.CurrentCulture = culture; }
            string source = File.ReadAllText(Checkout.In("Content", CombatRules.File));
            // Lose Echo only at level 3, retaining a structurally valid set of capabilities.
            int boundary = source.IndexOf("\"startLevel\": 3", StringComparison.Ordinal);
            string removed = ", \"Echo\"";
            int capability = source.IndexOf(removed, boundary, StringComparison.Ordinal);
            Assert.Greater(capability, boundary);
            Assert.Throws<ContentException>(() => CombatRules.Read(JsonValue.Parse(source.Remove(capability, removed.Length))));
            var sim = new Simulation(new RunInputs().Build(GameContent.Load(Checkout.In("Content"))));
            Assert.AreEqual(1, sim.State.Build.Level(WeaponKind.Sword));
        }
    }
}
