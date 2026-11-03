using System;
using System.Linq;
using NUnit.Framework;

namespace Champions.Sim.Tests
{
    public class BossReinforcementTests
    {
        static Simulation Make(string boss = "fatOgre")
        {
            var content = GameContent.Load(Checkout.In("Content"));
            var c = new RunInputs { Duel = boss, Loadout = Array.Empty<Upgrade>() }.Build(content);
            c.Hero.Health = 1e9;
            return new Simulation(c);
        }
        static void Until(Simulation s, Func<bool> ready, double span = 90)
        {
            double end = s.Now + span;
            while (!ready() && s.Now < end) s.StepTo(s.Now + Simulation.TickSeconds);
            Assert.IsTrue(ready());
        }
        static void KillMobs(Simulation s)
        {
            s.Strikes.Land(s, new Blow { T = s.Now, Shape = HitShape.Circle(s.State.Hero.X, s.State.Hero.Y, 1000),
                Damage = 1e9, Source = BlowSource.SwordSlash, Tags = HitTags.Strike, Owner = -1, Part = -1 });
            if (s.State.Offer.Up) { s.Press(Key.One, s.Now); s.Release(Key.One, s.Now); }
        }

        [Test]
        public void ArrivalShattersVisibleMobsRetiresOutsideAndAwardsNothing()
        {
            var s = Make();
            s.Horde.Spawn(0, 5, 0); s.Horde.Spawn(0, -100, 0); s.Horde.Spawn(0, 20, 13);
            s.Pickups.Drop(s, PickupKind.Experience, 90, 90, 7);
            s.Pickups.Drop(s, PickupKind.Healing, 91, 90, 3);
            Until(s, () => s.Bosses.InDuel);
            Assert.AreEqual(1, s.Enemies.Alive);
            Assert.AreEqual(2, Drive.Events(s, SimEventKind.Kill).Count());
            Assert.AreEqual(1, Drive.Events(s, SimEventKind.EnemyGone).Count());
            Assert.AreEqual(2, s.Pickups.StoredCount, "existing drops survive, arrival generates none");
            Assert.AreEqual(0, s.Arsenal.Kills, "environmental clear does not credit the player");
            s.Target(-90, 0, s.Now); s.StepTo(s.Now + 10);
            Assert.AreEqual(1, s.Enemies.Alive, "travel cannot reintroduce the retired outside horde");
        }

        [TestCase("fatOgre")] [TestCase("conductor")] [TestCase("dragon")]
        public void ReinforcementsHaveWarningsFiniteXpAndNoStackedGroups(string boss)
        {
            var s = Make(boss);
            // Keep the boss alive while the public strike path harvests every reinforcement.
            Until(s, () => s.Bosses.InDuel);
            s.Enemies.Edit(s.Bosses.Id).Health = 1e12;
            int groups = 0;
            for (int slot = 0; slot < Bosses.ReinforcementGroups; slot++)
            {
                Until(s, () => s.Bosses.ReinforcementWarning.HasValue);
                var m = s.Bosses.ReinforcementWarning.Value;
                Assert.AreEqual(0, s.Bosses.Marks.Length, "summons start between boss attacks");
                double land = s.Telegraphs.LandTime(m.Telegraph);
                Assert.GreaterOrEqual(land - m.Telegraph.Start, 2);
                s.StepTo(land - .02); Assert.AreEqual(groups * 3, s.Horde.Spawned);
                s.StepTo(land + .02); groups++;
                Assert.AreEqual(groups * 3, s.Horde.Spawned);
                Assert.AreEqual(3, s.Horde.Alive);
                Assert.AreEqual(0, s.Bosses.Marks.Length, "approach time keeps lanes clear of boss telegraphs");
                KillMobs(s); s.StepTo(s.Now + .1);
            }
            s.StepTo(s.Now + 120);
            Assert.AreEqual(9, s.Horde.Spawned, "refusing to finish a boss never renews its budget");
            Assert.AreEqual(9, s.Arsenal.Kills);
            Assert.AreEqual(9, s.Pickups.ExperienceDropped, "at most nine XP from one boss's summons");
        }

        [Test]
        public void SummonWarningCancelsOnDeathAndRecordedSummonsReplay()
        {
            var content = GameContent.Load(Checkout.In("Content"));
            var run = new RunInputs { Duel = "fatOgre", Loadout = Array.Empty<Upgrade>() };
            var s = new Simulation(run.Build(content));
            Until(s, () => s.Bosses.ReinforcementWarning.HasValue);
            s.Control(RunControl.Pause, s.Now); s.StepTo(s.Now + 5); s.Control(RunControl.Resume, s.Now);
            Until(s, () => s.Horde.Spawned == 3, 5);
            s.Control(RunControl.Quit, s.Now); s.StepTo(s.Now + Simulation.TickSeconds);
            var record = SessionRecord.Of(run, s);
            var replay = new SessionReplay(record);
            var second = new Simulation(run.Build(content));
            Assert.IsTrue(replay.StepTo(second, double.PositiveInfinity));
            Assert.IsTrue(replay.Matches(second), "warning, pause, group and XP state replay deterministically");

            var dying = Make(); Until(dying, () => dying.Bosses.ReinforcementWarning.HasValue);
            KillMobs(dying); dying.StepTo(dying.Now + 5);
            Assert.IsNull(dying.Bosses.ReinforcementWarning);
            Assert.AreEqual(0, dying.Horde.Spawned, "death cancels the promised group");
            Assert.IsTrue(Drive.Events(dying, SimEventKind.BossMove).Any(e => e.MarkMove == BossMove.Reinforcements && e.I2 != 0));
        }

        [Test]
        public void WalkingIntoTheWarningSkipsNearbySpawnsWithoutMovingTheWarning()
        {
            var s = Make(); Until(s, () => s.Bosses.ReinforcementWarning.HasValue);
            var mark = s.Bosses.ReinforcementWarning.Value;
            s.Target(mark.Shape.X, mark.Shape.Y, s.Now);
            double land = s.Telegraphs.LandTime(mark.Telegraph);
            s.StepTo(land - .02);
            Assert.AreEqual(mark.Shape.X, s.Bosses.ReinforcementWarning.Value.Shape.X);
            Assert.AreEqual(mark.Shape.Y, s.Bosses.ReinforcementWarning.Value.Shape.Y);
            s.StepTo(land + .02);
            Assert.AreEqual(0, s.Horde.Spawned, "approaching the warning never creates an unavoidable contact hit");
        }

        [Test]
        public void EachNewBossGetsItsOwnBudgetAfterClearingThePreviousGroup()
        {
            var content = GameContent.Load(Checkout.In("Content"));
            var c = new RunInputs { Level = true, Loadout = Array.Empty<Upgrade>() }.Build(content);
            c.Hero.Health = 1e9; c.Structures = null;
            c.Level.Ramp = new Curve((0, 0)); c.Level.Cap = 0;
            c.Level.Bosses = new[] { "fatOgre", "conductor", "dragon" }
                .Select((name, i) => new LevelBoss { Cast = c.Enemies.IndexOf(name), CallAtS = (i + 1) * 2 }).ToArray();
            var s = new Simulation(c);
            for (int i = 0; i < 3; i++)
            {
                Until(s, () => s.Bosses.Called == i + 1 && s.Bosses.InDuel);
                Assert.AreEqual(1, s.Enemies.Alive, "next arrival retires the previous boss's live group");
                Until(s, () => s.Horde.Spawned == (i + 1) * 3);
                Assert.AreEqual(3, s.Horde.Alive, "budget resets for the next boss");
                ref readonly var boss = ref s.Enemies[s.Bosses.Id];
                s.Strikes.Land(s, new Blow { T = s.Now, Shape = HitShape.Circle(boss.X, boss.Y, .01),
                    Damage = 1e9, Source = BlowSource.SwordSlash, Tags = HitTags.Strike, Owner = -1, Part = -1 });
                s.StepTo(s.Now + Simulation.TickSeconds);
                if (s.State.Offer.Up) { s.Press(Key.One, s.Now); s.Release(Key.One, s.Now); }
            }
            Assert.AreEqual(RunEnd.Victory, s.State.End);
        }

        [Test]
        public void LiveGroupConsumesLaterOpportunitiesAndPausePreservesWarning()
        {
            var s = Make(); Until(s, () => s.Bosses.ReinforcementWarning.HasValue);
            var first = s.Bosses.ReinforcementWarning.Value;
            double remaining = s.Telegraphs.LandTime(first.Telegraph) - s.Now;
            s.Control(RunControl.Pause, s.Now); s.StepTo(s.Now + 20); s.Control(RunControl.Resume, s.Now);
            s.Hold(HoldReason.Cards, true); s.StepTo(s.Now + 7); s.Hold(HoldReason.Cards, false);
            var resumed = s.Bosses.ReinforcementWarning.Value;
            Assert.AreEqual(remaining, s.Telegraphs.LandTime(resumed.Telegraph) - s.Now, 1e-8);
            s.StepTo(s.Now + remaining - .02); Assert.AreEqual(0, s.Horde.Spawned);
            s.StepTo(s.Now + 120);
            Assert.AreEqual(3, s.Horde.Spawned, "live groups never stack");
            KillMobs(s); s.StepTo(s.Now + 60);
            Assert.AreEqual(3, s.Horde.Spawned, "missed opportunities are spent");
        }
    }
}
