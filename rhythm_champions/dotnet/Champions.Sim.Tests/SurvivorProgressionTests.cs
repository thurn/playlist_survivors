using System;
using System.Linq;
using NUnit.Framework;

namespace Champions.Sim.Tests
{
    public class SurvivorProgressionTests
    {
        static Simulation Make(ulong seed = 1, FixtureSong song = null)
        {
            var config = new RunInputs { Seed = seed, Start = RunStart.FirstOffer }.Build(Arena.Content);
            config.Dashes.LegacyForms = false;
            var sim = new Simulation(config);
            if (song != null) foreach (var e in Drive.Known(song.Entries())) sim.AddKnown(e.Entry, e.KnownAt);
            return sim;
        }
        static void Earn(Simulation sim, double xp)
        {
            sim.Progression.CollectExperience(sim, xp, sim.Now);
            sim.StepTo(sim.Now + Simulation.TickSeconds);
        }
        static void Pick(Simulation sim, int slot = 0)
        {
            var key = (Key)((int)Key.One + slot);
            sim.Press(key, sim.Now); sim.Release(key, sim.Now);
        }
        static void Full(Simulation sim, int level = 8)
        {
            for (int i = 0; i < 6; i++) sim.State.Build.Set((WeaponKind)i, level);
            for (int i = 0; i < 6; i++) sim.State.Build.Set((SupportKind)i, 5);
        }
        [Test]
        public void StartsWithSwordAndNoMenuThenImmediatelyOffersOnReliableMusic()
        {
            var sim = Make(song: new FixtureSong().Beats(0, 60, 120));
            Assert.AreEqual(RunPhase.Playing, sim.State.Phase);
            Assert.AreEqual(1, sim.State.Build.Level(WeaponKind.Sword));
            Assert.IsFalse(sim.State.Offer.Up);
            sim.StepTo(3);
            Earn(sim, sim.State.ExperienceNeeded);
            Assert.AreEqual(OfferKind.LevelUp, sim.State.Offer.Kind);
            Assert.AreEqual(3, sim.State.Offer.Count);
            Assert.IsTrue(sim.State.Held);
            Assert.IsTrue(Enumerable.Range(0, 3).Any(i => !sim.State.Offer[i].New));
        }
        [Test]
        public void ThreeDistinctLegalCardsAreDeterministicAcrossManyBuilds()
        {
            var a = Make(42); var b = Make(42);
            for (int n = 0; n < 60; n++)
            {
                Earn(a, a.State.ExperienceNeeded); Earn(b, b.State.ExperienceNeeded);
                var offer = a.State.Offer;
                Assert.AreEqual(offer, b.State.Offer);
                Assert.Greater(offer.Count, 0);
                for (int i = 0; i < offer.Count; i++)
                {
                    var c = offer[i];
                    Assert.IsTrue(c.Kind == CardKind.Weapon ? a.State.Build.CanUpgrade(c.Weapon) : a.State.Build.CanUpgrade(c.Support));
                    for (int j = 0; j < i; j++) Assert.IsFalse(c.SameThing(offer[j]));
                }
                int slot = n % offer.Count;
                Pick(a, slot); Pick(b, slot);
                Assert.LessOrEqual(a.State.Build.WeaponCount, 6);
                Assert.LessOrEqual(a.State.Build.SupportCount, 6);
            }
        }
        [Test]
        public void FullSlotsExcludeNewItemsAndCapsLeaveOnlyRemainingEligibleCards()
        {
            var sim = Make(); Full(sim);
            sim.State.Build.Set(WeaponKind.Hammer, 7);
            sim.State.Build.Set(SupportKind.Might, 4);
            Earn(sim, sim.State.ExperienceNeeded);
            Assert.AreEqual(2, sim.State.Offer.Count);
            CollectionAssert.AreEquivalent(new[] { Card.Of(WeaponKind.Hammer, 8, 7), Card.Of(SupportKind.Might, 5, 4) },
                new[] { sim.State.Offer[0], sim.State.Offer[1] });
            Pick(sim); Earn(sim, sim.State.ExperienceNeeded);
            Assert.AreEqual(1, sim.State.Offer.Count);
            Pick(sim);
            double health = sim.State.Hero.Health, maximum = sim.State.Hero.MaxHealth;
            Earn(sim, sim.State.ExperienceNeeded * 10);
            Assert.IsFalse(sim.State.Offer.Up);
            Assert.AreEqual(0, sim.State.HeldLevels);
            Assert.Greater(sim.State.Hero.MaxHealth, maximum);
            Assert.AreEqual(health, sim.State.Hero.Health);
        }
        [Test]
        public void NewSupportsMustBenefitTheCurrentBuild()
        {
            for (ulong seed = 1; seed <= 32; seed++)
            {
                var sim = Make(seed); Earn(sim, sim.State.ExperienceNeeded);
                foreach (var c in new[] { sim.State.Offer[0], sim.State.Offer[1], sim.State.Offer[2] })
                    if (c.Kind == CardKind.Support) Assert.IsTrue(sim.State.Build.BenefitsBuild(c.Support));
            }
        }
        [Test]
        public void MultipleLevelsQueueWithoutHealingOrResumingBetweenPicks()
        {
            var sim = Make(); sim.State.Hero.Health = 40;
            Earn(sim, 20 + 21.8 + 23.762);
            Assert.AreEqual(2, sim.State.HeldLevels);
            Pick(sim); Assert.IsTrue(sim.State.Held);
            Pick(sim); Assert.IsTrue(sim.State.Held);
            Pick(sim); Assert.IsFalse(sim.State.Held);
            Assert.AreEqual(3, sim.Progression.Picks);
            Assert.AreEqual(40, sim.State.Hero.Health);
        }
        [Test]
        public void BossRewardIsOwnedOnlySingleLevelAndCanCrossFormBreakpoints()
        {
            var sim = Make(); sim.State.Build.Set(WeaponKind.Sword, 2);
            sim.Progression.Treasure(sim);
            Assert.AreEqual(1, sim.State.Offer.Count);
            Assert.AreEqual(Card.Of(WeaponKind.Sword, 3, 2), sim.State.Offer[0]);
            Pick(sim);
            Assert.AreEqual(3, sim.State.Build.Level(WeaponKind.Sword));
            Assert.AreEqual(0, sim.State.HeldLevels);
            Full(sim); double max = sim.State.Hero.MaxHealth;
            sim.Progression.Treasure(sim);
            Assert.IsFalse(sim.State.Offer.Up);
            Assert.AreEqual(max + 5, sim.State.Hero.MaxHealth);
        }
        [Test]
        public void BossRewardQueuedDuringCardsResolvesBeforeOtherEarnedLevels()
        {
            var sim = Make(); Earn(sim, 65);
            sim.Progression.Treasure(sim);
            Pick(sim);
            Assert.AreEqual(OfferKind.Treasure, sim.State.Offer.Kind);
            Pick(sim);
            Assert.AreEqual(OfferKind.LevelUp, sim.State.Offer.Kind);
        }
        [Test]
        public void HoldPreservesCooldownInvulnerabilityAndRequiresSpaceRelease()
        {
            var sim = Make(); sim.StepTo(1); sim.Press(Key.Space, sim.Now);
            sim.StepTo(1.05);
            double cooldown = sim.State.Hero.DashReadyAt - sim.Now, shield = sim.State.Hero.IFramesUntil - sim.Now;
            sim.Progression.Treasure(sim);
            double start = sim.Now; sim.StepTo(11.05); double elapsed = sim.Now - start;
            Pick(sim);
            Assert.AreEqual(cooldown, sim.State.Hero.DashReadyAt - sim.Now, 1e-8);
            Assert.AreEqual(shield, sim.State.Hero.IFramesUntil - sim.Now, 1e-8);
            sim.StepTo(sim.Now + 1);
            sim.Press(Key.Space, sim.Now);
            Assert.AreEqual(1, sim.State.Hero.Dashes);
            sim.Release(Key.Space, sim.Now); sim.Press(Key.Space, sim.Now);
            Assert.AreEqual(2, sim.State.Hero.Dashes);
            Assert.Greater(elapsed, 9);
        }
        [Test]
        public void TelegraphResumeKeepsReactionTimeAndLandsOnFutureGrid()
        {
            var sim = Make(song: new FixtureSong().Beats(0, 60, 120)); sim.StepTo(3);
            var original = sim.Telegraphs.Plan(sim.Now, 2);
            double remaining = sim.Telegraphs.LandTime(original) - sim.Now;
            sim.Progression.Treasure(sim); sim.StepTo(13);
            var restored = sim.Telegraphs.Resume(sim.Telegraphs.Freeze(original), 10);
            double land = sim.Telegraphs.LandTime(restored);
            Assert.GreaterOrEqual(land - sim.Now + 1e-8, remaining);
            Assert.AreEqual(0, land % 0.5, 1e-7);
        }
        [Test]
        public void RepeatedHeldChoiceAndMovementWaitForRelease()
        {
            var sim = Make(); sim.Press(Key.Up, sim.Now);
            Earn(sim, 65);
            sim.Press(Key.One, sim.Now);
            int picks = sim.Progression.Picks;
            sim.Press(Key.One, sim.Now);
            Assert.AreEqual(picks, sim.Progression.Picks);
            sim.Release(Key.One, sim.Now);
            Pick(sim); Pick(sim);
            double y = sim.State.Hero.Y;
            sim.Press(Key.Up, sim.Now); sim.StepTo(sim.Now + 1);
            Assert.AreEqual(y, sim.State.Hero.Y, 1e-8);
            sim.Release(Key.Up, sim.Now); sim.Press(Key.Up, sim.Now); sim.StepTo(sim.Now + 1);
            Assert.Greater(sim.State.Hero.Y, y);
        }
        [Test]
        public void OrdinaryInputDoesNotAwardFallbackHealth()
        {
            var sim = Make(); double max = sim.State.Hero.MaxHealth;
            sim.Press(Key.Up, sim.Now); sim.Release(Key.Up, sim.Now);
            sim.Press(Key.Space, sim.Now); sim.Release(Key.Space, sim.Now);
            Assert.AreEqual(max, sim.State.Hero.MaxHealth);
        }
        [Test]
        public void EarnedLevelDuringBossPreservesLivePatternAndRemainingReactionTime()
        {
            var sim = BossTests.Duel("fatOgre", tweak: c => { c.Dashes.LegacyForms = false; c.Hero.Health = 1e9; });
            for (int i = 0; i < 3600 && sim.Bosses.Marks.Length == 0; i++) sim.StepTo(sim.Now + Simulation.TickSeconds);
            Assert.Greater(sim.Bosses.Marks.Length, 0);
            int id = sim.Bosses.Marks[0].Id;
            Earn(sim, sim.State.ExperienceNeeded);
            Assert.AreEqual(OfferKind.LevelUp, sim.State.Offer.Kind);
            var mark = sim.Bosses.Marks[0];
            double remaining = sim.Telegraphs.LandTime(mark.Telegraph) - sim.Now;
            int count = sim.Bosses.Marks.Length;
            sim.StepTo(sim.Now + 10);
            Assert.AreEqual(count, sim.Bosses.Marks.Length);
            Pick(sim);
            Assert.AreEqual(id, sim.Bosses.Marks[0].Id);
            Assert.GreaterOrEqual(sim.Telegraphs.LandTime(sim.Bosses.Marks[0].Telegraph) - sim.Now + 1e-8, remaining);
            int events = sim.EventCount;
            sim.StepTo(sim.Now + Math.Max(0, remaining - 0.05));
            Assert.IsFalse(sim.EventsSince(events).ToArray().Any(e => e.Kind == SimEventKind.BossMove && e.I0 == id));
        }
        [Test]
        public void CardHoldKeepsFlyingSporesAtTheirPosition()
        {
            var sim = Make();
            sim.Horde.Spawn(Arena.Content.Enemies.IndexOf("mushroom"), 8, 0, 1e9);
            for (int i = 0; i < 2400 && sim.Horde.Moves.Spores.Length == 0; i++) sim.StepTo(sim.Now + Simulation.TickSeconds);
            Assert.Greater(sim.Horde.Moves.Spores.Length, 0);
            var before = sim.Horde.Moves.Spores[0];
            sim.Progression.Treasure(sim); sim.StepTo(sim.Now + 10);
            Assert.AreEqual(before.X, sim.Horde.Moves.Spores[0].X);
            Assert.AreEqual(before.Y, sim.Horde.Moves.Spores[0].Y);
            Pick(sim); sim.StepTo(sim.Now + Simulation.TickSeconds);
            Assert.AreEqual(before.Id, sim.Horde.Moves.Spores[0].Id);
            double moved = Math.Abs(sim.Horde.Moves.Spores[0].X - before.X) + Math.Abs(sim.Horde.Moves.Spores[0].Y - before.Y);
            Assert.That(moved, Is.InRange(0.001, 0.2));
        }
        [TestCase(0.85)] [TestCase(1.0)] [TestCase(1.15)]
        public void ScriptedCollectionPaceReportsFirstPickAndRunTotal(double collection)
        {
            var sim = Make(); double first = double.NaN;
            // Collected XP, not elapsed-time awards: routing/output varies ±15%.
            for (int second = 1; second <= 900; second++)
            {
                sim.StepTo(second);
                Earn(sim, collection * (0.8 + 5.2 * Math.Max(0, second - 30) / 870.0));
                while (sim.State.Offer.Up)
                {
                    if (double.IsNaN(first)) first = second;
                    Pick(sim);
                }
            }
            TestContext.WriteLine($"collection={collection:F2}; first={first:F0}s; normal choices={sim.Progression.Picks}; total XP={sim.State.Level - 1} levels + {sim.State.Experience:F1} remainder");
            Assert.That(first, Is.InRange(20, 30));
            Assert.That(sim.Progression.Picks, Is.InRange(25, 35));
        }
    }
}
