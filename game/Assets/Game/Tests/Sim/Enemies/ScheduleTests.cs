using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using PS.Game.Sim.Level;

namespace PS.Game.Sim.Tests
{
    /// <summary>
    /// The run's schedule (unity-game.md, Enemies; native's survivors spawning): schedule.json's
    /// minutes spawn by native's rule, scaled by loudness, refilled to the minimum, in clumps, with
    /// scheduled bosses landing on downbeats and waiting for room under the cap.
    /// </summary>
    public class ScheduleTests
    {
        static SimConfig Run(ulong seed = 1) => new SimConfig
        {
            Seed = seed,
            Content = HordeFixture.Content,
            Horde = new HordeConfig { Schedule = true },
            HeroStands = true,
        };

        /// <summary>The run's fresh spawns (not recycled re-entries), read as a client does.</summary>
        static Simulation Play(SimConfig config, FixtureSong song, double until, List<SpawnEvent> spawns,
            Action<Simulation> eachFrame = null)
        {
            var sim = new Simulation(config);
            int cursor = 0;
            Drive.Feed(sim, song.Entries(), until, eachFrame: s =>
            {
                foreach (var e in s.EventsSince(cursor))
                    if (e.Kind == SimEventKind.Spawn && !e.AsSpawn.Recycled) spawns.Add(e.AsSpawn);
                eachFrame?.Invoke(s);
                cursor = s.EventCount;
                s.DiscardEventsBefore(cursor);
            });
            return sim;
        }

        static int[] PerMinute(List<SpawnEvent> spawns, int minutes)
        {
            var counts = new int[minutes];
            foreach (var s in spawns)
            {
                int m = (int)Math.Floor(s.T / 60 + 1e-9);
                if (m < minutes) counts[m]++;
            }
            return counts;
        }

        [Test]
        public void TheFirstMinutesSpawnUntilTheCapAndABossWaitsForRoom()
        {
            // Native's rule with nothing killed, at medium loudness (scale 1):
            // minute 0 (bat3 and bat1, minimum 40, every 0.5 s): 40 at 0 s, then two a wave: 40 + 119 x 2 = 278.
            // minute 1 (zombie and bat1, every 0.5 s): two a wave until the cap, a slot kept for its boss bat4,
            // which lands on a downbeat: 300 - 278 = 22.
            // minute 2: the cap is full, nothing spawns; minute 3: its boss (bat4) waits.
            var spawns = new List<SpawnEvent>();
            var sim = Play(Run(), new FixtureSong(3).Beats(0, 200, 120), 195, spawns);
            CollectionAssert.AreEqual(new[] { 278, 22, 0, 0 }, PerMinute(spawns, 4));
            Assert.AreEqual(300, sim.State.EnemiesAlive);
            var bat4 = HordeFixture.Content.RoleIndex("bat4");
            Assert.AreEqual(1, spawns.Count(s => s.Boss), "minute 3's boss is still waiting");
            Assert.IsTrue(spawns.Single(s => s.Boss).Role == bat4);

            // Room opens: the boss takes it on the next downbeat, and no wave takes it first.
            int victim = Enumerable.Range(0, 300).First(i => !sim.State.Enemies[i].Boss);
            sim.Horde.Kill(sim, victim, sim.Now, 1, 0, 0);
            var later = new List<SpawnEvent>();
            int cursor = sim.EventCount;
            sim.StepTo(sim.Now + 3);
            foreach (var e in sim.EventsSince(cursor))
                if (e.Kind == SimEventKind.Spawn && !e.AsSpawn.Recycled) later.Add(e.AsSpawn);
            Assert.AreEqual(1, later.Count);
            Assert.IsTrue(later[0].Boss);
            Assert.AreEqual(bat4, later[0].Role);
        }

        // The level blends over the last 2 s, medium before the first tick, so the first wave at 0 s
        // brings minute 0's 40 whatever the music. Then, two roles a wave every 0.5 s:
        // quiet: the minimum is ceil(40 x 0.6) = 24, and a quiet wave with that many up is skipped: 40 in all.
        // medium: two a wave, 40 + 119 x 2 = 278.
        // loud: the minimum climbs to 43, 46, 49 and 52 over the first 2 s (44, 46, 50, 52 up), all loud
        // from 2 s, ceil(40 x 1.3) = 52; then two a wave: 52 + 115 x 2 = 282.
        [TestCase(LoudnessLevel.Quiet, 40)]
        [TestCase(LoudnessLevel.Medium, 278)]
        [TestCase(LoudnessLevel.Loud, 282)]
        public void LoudnessScalesTheMinimumAndQuietWavesBreathe(LoudnessLevel level, int minuteZero)
        {
            var spawns = new List<SpawnEvent>();
            Play(Run(), new FixtureSong(3).Beats(0, 70, 120, level: level), 59.99, spawns);
            Assert.AreEqual(minuteZero, spawns.Count);
        }

        [Test]
        public void KilledEnemiesAreRefilledToTheMinimumOnTheNextWave()
        {
            var spawns = new List<SpawnEvent>();
            var sim = Play(Run(), new FixtureSong(3).Beats(0, 40, 120), 20.5, spawns);
            for (int i = 0; i < sim.State.Enemies.Length; i++)
                if (sim.State.Enemies[i].Alive) sim.Horde.Kill(sim, i, sim.Now, 1, 0, 0);
            Assert.AreEqual(0, sim.State.EnemiesAlive);
            sim.StepTo(21.25);
            Assert.AreEqual(40, sim.State.EnemiesAlive, "minute 0's minimum, at one wave");
        }

        [Test]
        public void AtTheCapTheFreeSlotsGoRoundTheRoster()
        {
            // The cap is full from minute 1. In minute 2 (bat1-3, every 0.5 s) one slot frees before each wave: native's
            // pass always starts at the roster's first role, which would take every one; here they go round.
            var spawns = new List<SpawnEvent>();
            var sim = Play(Run(), new FixtureSong(3).Beats(0, 200, 120), 150, spawns);
            Assert.AreEqual(300, sim.State.EnemiesAlive);
            var refills = new List<int>();
            for (int wave = 0; wave < 6; wave++)
            {
                int victim = Enumerable.Range(0, 300).First(i => sim.State.Enemies[i].Alive);
                sim.Horde.Kill(sim, victim, sim.Now, 1, 0, 0);
                int cursor = sim.EventCount;
                sim.StepTo(sim.Now + 0.5);
                foreach (var e in sim.EventsSince(cursor))
                    if (e.Kind == SimEventKind.Spawn && !e.AsSpawn.Recycled) refills.Add(e.AsSpawn.Role);
            }
            var content = HordeFixture.Content;
            CollectionAssert.AreEquivalent(new[] { "bat1", "bat1", "bat2", "bat2", "bat3", "bat3" },
                refills.Select(r => content.Roles[r].Id), "each role in turn");
        }

        [Test]
        public void ARolesTrickleGathersIntoOneClumpOnTheRing()
        {
            // Minute 0 spawns one bat3 every half second: within the clump time, each lands by the last.
            var spawns = new List<SpawnEvent>();
            var sim = Play(Run(), new FixtureSong(3).Beats(0, 40, 120), 30, spawns);
            var hero = sim.State.Hero;
            int bat3 = HordeFixture.Content.RoleIndex("bat3");
            var trickle = spawns.Where(s => s.T > 0.25 && s.Role == bat3).ToList();
            int near = 0;
            for (int k = 1; k < trickle.Count; k++)
            {
                double a = Math.Atan2(trickle[k].Y - hero.Y, trickle[k].X - hero.X);
                double b = Math.Atan2(trickle[k - 1].Y - hero.Y, trickle[k - 1].X - hero.X);
                double d = Math.Abs(Math.IEEERemainder(a - b, 2 * Math.PI));
                if (d < 0.35) near++;
            }
            // A fresh point every clump time (3 s) at most: five in six land beside the last; two in three will do.
            Assert.GreaterOrEqual(near, 2 * (trickle.Count - 1) / 3, $"{near} of {trickle.Count - 1}");
        }

        [Test]
        public void ALineupStandsInFrontOfTheHeroWithItsBossesAndNothingElseSpawns()
        {
            var roles = new[] { "bat1", "boss-werewolf", "xlmummy" };
            var spawns = new List<SpawnEvent>();
            var sim = Play(RunLevel.Lineup(HordeFixture.Content, null, new HordeConfig().CameraFootprint, roles),
                new FixtureSong(3).Beats(0, 20, 120), 10, spawns);
            CollectionAssert.AreEqual(roles, spawns.Select(s => HordeFixture.Content.Roles[s.Role].Id));
            CollectionAssert.AreEqual(new[] { false, true, false }, spawns.Select(s => s.Boss));
            Assert.IsTrue(spawns.All(s => s.T == 0 && s.Y > sim.State.Hero.Y));
            Assert.That(spawns.Select(s => s.X), Is.Ordered, "in a row");
        }

        [Test]
        public void ScheduledBossesLandOnADownbeatInViewWithLevelScaledHealth()
        {
            var spawns = new List<SpawnEvent>();
            var downbeats = new List<double>();
            int cursor = 0;
            var sim = Play(Run(), new FixtureSong(3).Beats(0, 80, 120), 66, spawns, s =>
            {
                s.State.Hero.Level = 3;
                foreach (var e in s.EventsSince(cursor))
                    if (e.Kind == SimEventKind.Beat && e.AsBeat.Downbeat) downbeats.Add(e.T);
                cursor = s.EventCount;
            });
            var boss = spawns.Single(e => e.Boss);
            Assert.AreEqual(HordeFixture.Content.RoleIndex("bat4"), boss.Role);
            Assert.That(downbeats, Has.Some.EqualTo(boss.T).Within(1e-9), "on a downbeat");
            Assert.That(boss.T, Is.GreaterThanOrEqualTo(60 - 1e-9).And.LessThan(61.1), "the first after minute 1 starts");
            var e1 = sim.State.Enemies[boss.Enemy];
            Assert.IsTrue(e1.Boss);
            Assert.AreEqual(5 * 3, e1.Health, 1e-9, "bat4's health times the hero's level");
            double dx = boss.X - sim.State.Hero.X, dy = boss.Y - sim.State.Hero.Y;
            Assert.Less(Math.Abs(dx), 17, "inside the view");
            Assert.Less(Math.Abs(dy), 11, "inside the view");
        }
    }
}
