using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using PS.Game.Sim.Content;

namespace PS.Game.Sim.Tests
{
    /// <summary>
    /// Garlic's aura and Fire Trail's wake (unity-game.md, The Other Weapons) against native's rules
    /// and numbers (engine/weapons.rs and engine/combat.rs, weapons.yaml): when they hit, whom, how
    /// hard and how often.
    /// </summary>
    public class GarlicFireTrailTests
    {
        const double Eps = 1e-9;

        static GameContent Content => HordeFixture.Content;

        /// <summary>A sim with no spawns whose hero holds <paramref name="weapon"/> at <paramref name="rank"/>, levels opening no cards.</summary>
        static Simulation Armed(string weapon, int rank, FixtureSong song = null, Stats? stats = null)
        {
            var config = HordeFixture.Config(minimum: 0, roster: new string[0]);
            config.Arsenal = new ArsenalConfig { Weapons = new[] { (weapon, rank) }, Cards = false, Stats = stats };
            var sim = new Simulation(config);
            if (song != null)
                foreach (var k in Drive.Known(song.Entries().Where(e => e.Kind == EntryKind.Beat || e.Kind == EntryKind.Tick)))
                    sim.AddKnown(k.Entry, k.KnownAt);
            return sim;
        }

        /// <summary>An enemy at (x, y) that no weapon here can kill.</summary>
        static int Dummy(Simulation sim, double x, double y)
        {
            int i = HordeFixture.Place(sim, "skeleton", x, y);
            sim.State.EnemyStore[i].Health = 1e9;
            return i;
        }

        static List<double> Shots(Simulation sim, string id)
        {
            int w = Content.WeaponIndex(id);
            return Drive.Events(sim, SimEventKind.Attack).Select(e => e.AsAttack).Where(a => a.ByHero && a.Weapon == w).Select(a => a.T).ToList();
        }

        static List<HitEvent> Hits(Simulation sim, string id, int enemy) =>
            Drive.Events(sim, SimEventKind.Hit).Select(e => e.AsHit)
                .Where(h => !h.OnHero && h.Weapon == Content.WeaponIndex(id) && h.Target == enemy).ToList();

        static double Body(string role) => 0.5 * Content.Roles[Content.RoleIndex(role)].RadiusM;

        // ---------------------------------------------------------------- Garlic

        [TestCase(1)]
        [TestCase(8)]
        public void GarlicsAuraPulsesOnTheBeatAndHitsWhatItTouchesOnceAPulseWithNativesNumbers(int rank)
        {
            // 120 BPM: rank 1's 1.3 s interval comes ready at 1.3 and 2.6 and fires on the beats at
            // 1.5 and 3.0; rank 8's 1 s is on the beat already.
            var sim = Armed("garlic", rank, new FixtureSong(3).Beats(0, 10, 120));
            var numbers = Content.Weapon("garlic").Rank(rank);
            var hero = sim.State.Hero;
            int i = Dummy(sim, hero.X + 1, hero.Y);
            sim.StepTo(3.2);
            var shots = Shots(sim, "garlic");
            Assert.AreEqual(rank == 1 ? new[] { 0, 1.5, 3.0 } : new[] { 0, 1.0, 2.0, 3.0 }, shots.Select(t => Math.Round(t, 6)).ToArray());
            var hits = Hits(sim, "garlic", i);
            Assert.AreEqual(shots.ToArray(), hits.Select(h => h.T).ToArray(), "a hit at each pulse, once");
            foreach (var h in hits) Assert.AreEqual(numbers.Power, h.Damage, Eps, "the rank's power times Might 1");
            var reach = Drive.Events(sim, SimEventKind.Attack).Select(e => e.AsAttack).Where(a => a.ByHero).Select(a => a.Reach);
            Assert.IsTrue(reach.All(r => Math.Abs(r - 0.32 * 154.27 / 27 * numbers.Area) < 1e-3), "native's 0.32 VS units x Area");
        }

        [TestCase(1)]
        [TestCase(8)]
        public void GarlicsAuraReachesItsRadiusTimesAreaAndNoFarther(int rank)
        {
            // No beat: the shots are on the raw interval and the enemies hold.
            var sim = Armed("garlic", rank);
            double radius = Content.Weapon("garlic").Shape.RadiusM * Content.Weapon("garlic").Rank(rank).Area;
            var hero = sim.State.Hero;
            int inside = Dummy(sim, hero.X + radius + Body("skeleton") - 0.05, hero.Y);
            int outside = Dummy(sim, hero.X, hero.Y - radius - Body("skeleton") - 0.05);
            sim.StepTo(3);
            Assert.AreEqual(Shots(sim, "garlic").Count, Hits(sim, "garlic", inside).Count);
            Assert.AreEqual(0, Hits(sim, "garlic", outside).Count);
        }

        [Test]
        public void AnEnemyEnteringTheAuraIsHitWhenItEntersAndNotAgainUntilTheNextPulse()
        {
            // No beat: the shots are on the raw 1.3 s interval and the enemies hold.
            var sim = Armed("garlic", 1);
            sim.StepTo(0.5);
            var hero = sim.State.Hero;
            int late = Dummy(sim, hero.X + 1, hero.Y);
            double placed = sim.Now;
            sim.StepTo(2.7);
            Assert.AreEqual(new[] { Math.Round(placed + Simulation.TickSeconds, 6), 1.3, 2.6 }, Hits(sim, "garlic", late).Select(h => Math.Round(h.T, 6)).ToArray(),
                "at the first tick's end inside, then each new incarnation");
        }

        [Test]
        public void TheAuraRestsWhileTheWorldIsFrozen()
        {
            var sim = Armed("garlic", 1);
            sim.StepTo(0.5);
            sim.Control(RunControl.Pause);
            var hero = sim.State.Hero;
            int i = Dummy(sim, hero.X + 1, hero.Y);
            sim.StepTo(5);
            Assert.AreEqual(0, Hits(sim, "garlic", i).Count);
        }

        [Test]
        public void EachAuraHitAddsNativesKnockbackDebuffUpToTheRolesMaximum()
        {
            // A skeleton (knockback 1, max 3) in the aura: every pulse adds 0.3 while it is below 3,
            // so a later blow throws it farther.
            var sim = Armed("garlic", 1);
            var hero = sim.State.Hero;
            int i = Dummy(sim, hero.X + 1, hero.Y);
            sim.StepTo(0.1);
            Assert.AreEqual(1 + Horde.GarlicKnockback, sim.Horde.KnockbackOf(i), Eps, "one pulse");
            sim.StepTo(20);
            Assert.AreEqual(1 + 7 * Horde.GarlicKnockback, sim.Horde.KnockbackOf(i), Eps, "the last step overshoots 3, then no more");
            var start = sim.State.Enemies[i];
            int cursor = sim.EventCount;
            sim.Horde.Knock(sim, i, sim.Now, 1, 0, 1);
            var knock = sim.EventsSince(cursor)[0].AsEnemyKnock;
            Assert.AreEqual(3 * Horde.KnockbackM, knock.ToX - knock.FromX, 1e-9, "capped at the role's maximum");
            int fresh = Dummy(sim, hero.X + 30, hero.Y);
            Assert.AreEqual(1, sim.Horde.KnockbackOf(fresh), Eps, "a newcomer starts at its role's");
        }

        [Test]
        public void TheAuraHitsANewcomerInTheSlotOfOneItJustKilled()
        {
            // A spawn inside the aura (a boss on the hero, a set piece) into the slot freed by its own kill.
            var sim = Armed("garlic", 1);
            var hero = sim.State.Hero;
            int weak = HordeFixture.Place(sim, "bat-swarm", hero.X + 1, hero.Y);
            sim.StepTo(0.1);
            Assert.IsFalse(sim.State.Enemies[weak].Alive, "killed by the first pulse");
            int newcomer = Dummy(sim, hero.X - 1, hero.Y);
            Assert.AreEqual(weak, newcomer, "the slot freed last goes to the next spawn");
            sim.StepTo(0.2);
            Assert.AreEqual(1, Hits(sim, "garlic", newcomer).Count(h => h.T > 0.1), "the same incarnation hits it at the next tick's end");
        }

        [Test]
        public void TheWeaponsRestOnceTheRunClockEnds()
        {
            // Native's weapons_active: nothing shoots, hits or burns in the finale's wait for its blast.
            var config = HordeFixture.Config(minimum: 0, roster: new string[0]);
            config.Arsenal = new ArsenalConfig
            {
                Weapons = new[] { ("garlic", 1), ("fire-trail", 1), ("lightning-ring", 1), ("magic-wand", 1) }, Cards = false,
            };
            var sim = new Simulation(config);
            var hero = sim.State.Hero;
            Dummy(sim, hero.X + 1, hero.Y);
            Dummy(sim, hero.X + 4, hero.Y + 2);
            sim.StepTo(5);
            var before = sim.EventsSince(0).ToArray().Select(e => e.Kind).ToList();
            foreach (var kind in new[] { SimEventKind.Attack, SimEventKind.Hit, SimEventKind.Burn, SimEventKind.Bolt })
                CollectionAssert.Contains(before, kind, "they work while the run plays");
            sim.State.Phase = RunPhase.Finale;
            int cursor = sim.EventCount;
            sim.StepTo(15);
            var after = sim.EventsSince(cursor).ToArray().Select(e => e.Kind).ToList();
            CollectionAssert.DoesNotContain(after, SimEventKind.Attack);
            CollectionAssert.DoesNotContain(after, SimEventKind.Hit);
            CollectionAssert.DoesNotContain(after, SimEventKind.Burn);
            CollectionAssert.DoesNotContain(after, SimEventKind.Bolt);
        }

        // ---------------------------------------------------------------- Fire Trail

        /// <summary>The hero's tile centres in the order it entered them, with the song time it set off for each (the first at the run's start, 0).</summary>
        static List<(double x, double y, double t)> Path(Simulation sim, double startX, double startY)
        {
            var path = new List<(double, double, double)> { (startX, startY, 0) };
            foreach (var e in Drive.Events(sim, SimEventKind.HeroStep))
            {
                var s = e.AsHeroStep;
                if (s.Bump || s.Stomp) continue;
                path.Add((s.ToX, s.ToY, e.T));
            }
            return path;
        }

        [Test]
        public void FireTrailsWakeBurnsTheTilesTheHeroLeftWithinItsDuration()
        {
            // No beat: the hero strides on the free pulse while Right is held, the shots come every
            // 0.5 s of run clock, and the enemies hold.
            var sim = Armed("fire-trail", 1);
            var hero = sim.State.Hero;
            double x0 = hero.X, y0 = hero.Y;
            sim.Press(Key.Right, 0);
            sim.StepTo(4.2);
            sim.Release(Key.Right, sim.Now);
            var path = Path(sim, x0, y0);
            Assert.Greater(path.Count, 6, "the held key walks the hero");
            int w = Content.WeaponIndex("fire-trail");
            var burns = Drive.Events(sim, SimEventKind.Burn).Select(e => e.AsBurn).ToList();
            var shots = Shots(sim, "fire-trail");
            Assert.AreEqual(Enumerable.Range(0, 9).Select(k => k * 0.5).ToArray(), shots.Select(t => Math.Round(t, 6)).ToArray());
            double duration = Content.Weapon("fire-trail").Rank(1).DurationS, skip = Content.Weapon("fire-trail").Shape.SkipRecentS;
            foreach (double t in shots)
            {
                var at = burns.Where(b => b.T == t).ToList();
                Assert.IsTrue(at.All(b => b.Weapon == w));
                // Each tile on the path the hero stood on in [t - duration, t - skip]: entered by
                // t - skip and left (a tick later at most) no earlier than t - duration.
                var expected = new List<(double, double)>();
                for (int k = path.Count - 1; k >= 0; k--)
                {
                    double entered = path[k].t, left = k + 1 < path.Count ? path[k + 1].t : double.PositiveInfinity;
                    if (entered > t + Eps) continue;
                    if (left + Simulation.TickSeconds < t - duration - Eps) break;
                    if (entered > t - skip + Eps) continue;
                    expected.Add((path[k].x, path[k].y));
                }
                Assert.AreEqual(expected.Distinct().ToArray(), at.Select(b => (b.X, b.Y)).ToArray(), $"the wake at {t}, newest first");
            }
            Assert.IsTrue(burns.Any(b => b.X != x0), "the wake moves with the hero");
            Assert.IsTrue(burns.Select(b => b.Radius).All(r => Math.Abs(r - 20.0 / 27) < 1e-3), "native's 20 px x Area 1");
        }

        [Test]
        public void AnEnemyInTheWakeBurnsOnceAPulseUntilItsTileExpires()
        {
            var sim = Armed("fire-trail", 1);
            var hero = sim.State.Hero;
            double tile = sim.Arena.TileM, x0 = hero.X, y0 = hero.Y;
            // Two tiles to the right of the start: the hero crosses it and walks on.
            int i = Dummy(sim, x0 + 2 * tile, y0);
            sim.Press(Key.Right, 0);
            sim.StepTo(8);
            var path = Path(sim, x0, y0);
            int k = path.FindIndex(p => Math.Abs(p.x - (path[0].x + 2 * tile)) < 1e-6);
            Assert.Greater(k, 0);
            double left = path[k + 1].t;
            var hits = Hits(sim, "fire-trail", i);
            Assert.IsNotEmpty(hits, "the tile burns once the hero is off it");
            Assert.AreEqual(hits.Count, hits.Select(h => h.T).Distinct().Count(), "once a pulse");
            foreach (var h in hits)
            {
                Assert.AreEqual(0.4, h.Damage, Eps, "rank 1's power times Might 1");
                Assert.LessOrEqual(h.T, left + Content.Weapon("fire-trail").Rank(1).DurationS + Simulation.TickSeconds + Eps, "the tile's burn ends with its duration");
            }
        }

        [Test]
        public void AStandingHeroBurnsItsOwnTileAndRankEightGrowsTheWake()
        {
            var stats = Content.Progression.Hero.Stats;
            var sim = Armed("fire-trail", 8, stats: stats);
            var hero = sim.State.Hero;
            var numbers = Content.Weapon("fire-trail").Rank(8);
            double radius = Content.Weapon("fire-trail").Shape.RadiusM * numbers.Area;
            // Beyond the tile's own circle at rank 1, inside rank 8's.
            int i = Dummy(sim, hero.X + radius + Body("skeleton") - 0.05, hero.Y);
            sim.StepTo(2.1);
            var hits = Hits(sim, "fire-trail", i);
            Assert.AreEqual(new[] { 0.5, 1.0, 1.5, 2.0 }, hits.Select(h => Math.Round(h.T, 6)).ToArray(),
                "every pulse once the hero has stood there its skip (not the first, at once)");
            foreach (var h in hits) Assert.AreEqual(numbers.Power, h.Damage, Eps);
            var burns = Drive.Events(sim, SimEventKind.Burn).Select(e => e.AsBurn).ToList();
            Assert.IsTrue(burns.All(b => b.X == hero.X && b.Y == hero.Y && Math.Abs(b.Radius - radius) < Eps), "one tile, the hero's");
        }

        [Test]
        public void TheWakeAgesOnTheRunClockSoAFreezeDoesntBurnItOut()
        {
            var sim = Armed("fire-trail", 1);
            var hero = sim.State.Hero;
            sim.Press(Key.Right, 0);
            sim.StepTo(2.05);
            sim.Release(Key.Right, sim.Now);
            sim.StepTo(2.1);
            var before = Drive.Events(sim, SimEventKind.Burn).Count;
            sim.Control(RunControl.Pause);
            sim.StepTo(12);
            Assert.AreEqual(before, Drive.Events(sim, SimEventKind.Burn).Count, "no pulse while frozen");
            // It resumes on the stride step at 12.25; the next pulse is 0.4 s of run clock on.
            sim.Control(RunControl.Resume);
            sim.StepTo(12.7);
            var after = Drive.Events(sim, SimEventKind.Burn).Select(e => e.AsBurn).Where(b => b.T > 12).ToList();
            Assert.IsTrue(after.Any(b => b.X != hero.X), "the tiles left just before the freeze still burn after it");
        }
        [Test]
        public void AWakeThatCrossesItselfBurnsEachTileAndEachEnemyOnceAPulse()
        {
            // Right for three tiles and back: the tiles crossed twice burn once a pulse, and an enemy
            // between two tiles, inside both circles, burns once a pulse.
            var sim = Armed("fire-trail", 1);
            var hero = sim.State.Hero;
            double tile = sim.Arena.TileM, x0 = hero.X, y0 = hero.Y;
            int between = Dummy(sim, x0 + 1.5 * tile, y0);
            sim.Press(Key.Right, 0);
            sim.StepTo(1.4);
            sim.Release(Key.Right, sim.Now);
            sim.Press(Key.Left, sim.Now);
            sim.StepTo(2.8);
            sim.Release(Key.Left, sim.Now);
            sim.StepTo(4);
            var path = Path(sim, x0, y0);
            Assert.IsTrue(path.Any(p => p.x < path.Max(q => q.x) && path.FindIndex(q => q.x == p.x) < path.FindLastIndex(q => q.x == p.x)),
                "the hero walks back over its tiles");
            var burns = Drive.Events(sim, SimEventKind.Burn).Select(e => e.AsBurn).ToList();
            foreach (var pulse in burns.GroupBy(b => b.T))
                Assert.AreEqual(pulse.Count(), pulse.Select(b => (b.X, b.Y)).Distinct().Count(), $"each tile once at {pulse.Key}");
            Assert.IsTrue(burns.GroupBy(b => b.T).Any(g => g.Count(b => Math.Abs(b.Y - y0) < Eps && Math.Abs(b.X - (x0 + 1.5 * tile)) < 0.5 * tile + Eps) == 2),
                "a pulse burns both tiles around the enemy");
            var hits = Hits(sim, "fire-trail", between);
            Assert.IsNotEmpty(hits);
            Assert.AreEqual(hits.Count, hits.Select(h => h.T).Distinct().Count(), "once a pulse");
        }
    }
}
