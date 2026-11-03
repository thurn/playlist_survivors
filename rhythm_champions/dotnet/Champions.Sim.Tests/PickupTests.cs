using System;
using System.Diagnostics;
using System.Linq;
using NUnit.Framework;

namespace Champions.Sim.Tests
{
    public class PickupTests
    {
        static Simulation Sim(bool structures = false)
        {
            var config = new RunInputs().Build(Arena.Content);
            config.Progression.Levels = new[] { 1e9 };
            if (structures) config.Structures = Arena.Content.Structures.Clone();
            return new Simulation(config);
        }

        [Test]
        public void KillsLeaveXpAtDeathPositionInsteadOfGrantingIt()
        {
            var sim = Sim();
            int skeleton = Array.FindIndex(Arena.Content.Enemies.Cast, c => c.Id == "skeleton") + 1;
            sim.Enemies.Spawn(sim, 10, 2, .3, 1, skeleton);
            sim.Strikes.Land(sim, new Blow { Shape = HitShape.Circle(10, 2, 1), Damage = 10, T = sim.Now, Source = BlowSource.SwordSlash });
            Assert.AreEqual(0, sim.State.Experience);
            var drops = new Pickup[4];
            Assert.AreEqual(1, sim.Pickups.Preview(10, 2, 1, drops));
            Assert.AreEqual(10, drops[0].X);
            Assert.AreEqual(2, drops[0].Y);
            Assert.AreEqual(1, drops[0].Value);
            Assert.AreEqual(1, Drive.Events(sim, SimEventKind.PickupDrop).Count);
        }

        [Test]
        public void MergingStreamingAndRepeatedCollectionConserveAllValue()
        {
            var sim = Sim(structures: true);
            for (int i = 0; i < 10000; i++) sim.Pickups.Drop(sim, PickupKind.Experience, 100.1, -50.1, .5);
            Assert.AreEqual(1, sim.Pickups.StoredCount);
            sim.StepTo(1);
            Assert.AreEqual(5000, sim.Pickups.OutstandingExperience);
            sim.State.Hero.X = 500; sim.State.Hero.Y = 500;
            sim.StepTo(2);
            sim.State.Hero.X = 100; sim.State.Hero.Y = -50;
            sim.Pickups.Tick(sim);
            sim.StepTo(3);
            Assert.AreEqual(5000, sim.State.Experience);
            Assert.AreEqual(0, sim.Pickups.OutstandingExperience);
            Assert.AreEqual(1, Drive.Events(sim, SimEventKind.PickupCollect).Count);
            sim.StepTo(5);
            Assert.AreEqual(5000, sim.State.Experience, "no second collection on revisit");
        }

        [Test]
        public void AttractionMovesBeforeGrantingValueAndKeepsChasing()
        {
            var sim = Sim();
            sim.Pickups.Drop(sim, PickupKind.Experience, 5, 0, 10);
            sim.Pickups.Tick(sim);
            Assert.AreEqual(0, sim.Pickups.FlyingCount);
            sim.State.Build.Set(SupportKind.Attraction, 5);
            sim.Pickups.Tick(sim);
            Assert.AreEqual(1, sim.Pickups.FlyingCount);
            Assert.AreEqual(0, sim.State.Experience);
            sim.State.Hero.X = -5;
            for (int i = 0; i < 100; i++) sim.Pickups.Tick(sim);
            Assert.AreEqual(10, sim.State.Experience);
        }

        [Test]
        public void StartingReachCollectsTheClearedPocketAndAttractionStillExtendsIt()
        {
            var sim = Sim();
            sim.Pickups.Drop(sim, PickupKind.Experience, 3.4, 0, 7);
            sim.Pickups.Drop(sim, PickupKind.Experience, 4, 0, 11);
            sim.StepTo(1);
            Assert.AreEqual(7, sim.State.Experience, "nearby clearing needs no backtracking");
            Assert.AreEqual(11, sim.Pickups.OutstandingExperience);
            sim.State.Build.Set(SupportKind.Attraction, 1);
            sim.StepTo(2);
            Assert.AreEqual(18, sim.State.Experience, "one support level reaches beyond the baseline");
            Assert.AreEqual(0, sim.Pickups.OutstandingExperience);
        }

        [Test]
        public void PickupAndRegenerationClampAndVitalityDoesNotHeal()
        {
            var sim = Sim();
            sim.State.Hero.Health = 95;
            sim.Pickups.Drop(sim, PickupKind.Healing, 0, 0, .1);
            sim.Pickups.Tick(sim);
            Assert.AreEqual(100, sim.State.Hero.Health);
            sim.State.Build.Set(SupportKind.Vitality, 2);
            sim.Health.ApplySupports(sim);
            Assert.AreEqual(120, sim.State.Hero.MaxHealth);
            Assert.AreEqual(100, sim.State.Hero.Health);
            sim.State.Build.Set(SupportKind.Regeneration, 2);
            sim.StepTo(1);
            Assert.AreEqual(101, sim.State.Hero.Health, 1e-8);
            sim.Control(RunControl.Pause, sim.Now);
            sim.StepTo(2);
            Assert.AreEqual(101, sim.State.Hero.Health, 1e-8);
        }

        [Test]
        public void ArmorAndStrideApplyTheirAuthoredFamilies()
        {
            var sim = Sim();
            sim.State.Build.Set(SupportKind.Armor, 5);
            sim.State.Build.Set(SupportKind.Stride, 4);
            sim.Press(Key.Right, 0);
            sim.Health.Hurt(sim, new HeroHit { T = .1, Damage = 30, Beat = double.NaN });
            sim.StepTo(1);
            Assert.AreEqual(80, sim.State.Hero.Health, 1e-8);
            var baseline = Sim();
            baseline.Press(Key.Right, 0); baseline.StepTo(1);
            Assert.AreEqual(baseline.State.Hero.X * 1.2, sim.State.Hero.X, 1e-8);
        }

        [Test]
        public void ArrivalAndBossCombatRetainRemoteValue()
        {
            var sim = BossTests.Duel("fatOgre", tweak: c => c.Hero.Health = 1e9);
            sim.Pickups.Drop(sim, PickupKind.Experience, 1000, 1000, 123);
            sim.Pickups.Drop(sim, PickupKind.Healing, 1001, 1000, .1);
            while (!sim.Bosses.InDuel) sim.StepTo(sim.Now + Simulation.TickSeconds);
            sim.StepTo(sim.Now + 1);
            var preview = new Pickup[4];
            Assert.AreEqual(2, sim.Pickups.Preview(1000, 1000, 3, preview));
            Assert.AreEqual(123, sim.Pickups.OutstandingExperience);
        }

        [Test]
        public void FlightLimitLeavesExcessStoredUntilSpaceIsAvailable()
        {
            var config = new RunInputs().Build(Arena.Content);
            config.Progression.Levels = new[] { 1e9 };
            config.Progression.PickupRadiusM = 12;
            var sim = new Simulation(config);
            for (int x = -8; x <= 8; x++)
                for (int y = -8; y <= 8; y++) sim.Pickups.Drop(sim, PickupKind.Experience, x, y, 1);
            sim.Pickups.Tick(sim);
            Assert.LessOrEqual(sim.Pickups.FlyingCount, Pickups.FlightLimit);
            Assert.Greater(sim.Pickups.StoredCount, 0);
            Assert.AreEqual(289, sim.Pickups.OutstandingExperience + sim.State.Experience);
            sim.StepTo(2);
            Assert.AreEqual(289, sim.State.Experience);
            Assert.AreEqual(0, sim.Pickups.StoredCount + sim.Pickups.FlyingCount);
        }

        [Test]
        public void HealingCadenceAndPickupHashReplayDeterministically()
        {
            var a = Sim(); var b = Sim();
            int type = Array.FindIndex(Arena.Content.Enemies.Cast, c => c.Id == "skeleton") + 1;
            foreach (var sim in new[] { a, b })
            {
                for (int i = 0; i < 100; i++) sim.Progression.Killed(sim, type, 0, 1, 0);
                Assert.AreEqual(2, sim.Pickups.StoredCount, "one merged XP store and one healing store");
                sim.State.Hero.Health = 80;
                sim.StepTo(.5);
                Assert.AreEqual(90, sim.State.Hero.Health);
                Assert.AreEqual(100, sim.State.Experience);
            }
            Assert.AreEqual(a.Hash, b.Hash);
            a.Pickups.Drop(a, PickupKind.Experience, 100, 100, 2);
            Assert.AreNotEqual(a.Hash, b.Hash, "uncollected world value belongs to replay state");
        }

        [Test]
        public void AccumulatedScaleBoundsLocalWorkAndVisualsWithoutDeletingValue()
        {
            var sim = Sim();
            for (int i = 0; i < 100000; i++) sim.Pickups.Drop(sim, PickupKind.Experience, i * 2 + 100, 100, .5);
            var timer = Stopwatch.StartNew();
            long before = sim.Pickups.CellsVisited;
            for (int i = 0; i < 120; i++) sim.Pickups.Tick(sim);
            timer.Stop();
            Assert.LessOrEqual(sim.Pickups.CellsVisited - before, 120 * 162,
                "bounded local work even with distant accumulated loot");
            var preview = new Pickup[10];
            Assert.AreEqual(10, sim.Pickups.Preview(110, 100, 20, preview));
            Assert.AreEqual(50000, sim.Pickups.OutstandingExperience);
            TestContext.Out.WriteLine($"100000 stored cells; 120 ticks: {timer.Elapsed.TotalMilliseconds:F3} ms; {sim.Pickups.CellsVisited-before} cell lookups; visual sample capacity 10, value retained 50000 XP. Timing is diagnostic, never an assertion.");
        }
    }
}
