using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using NUnit.Framework;
using PS.Game.Sim.Level;

namespace PS.Game.Sim.Tests
{
    /// <summary>
    /// The enemies (unity-game.md, Enemies): steps planted exactly on main beats, holding without a
    /// reliable beat, long-step contact, routing around structures, knockback, the spawn ring, the
    /// cap and determinism at 300 alive.
    /// </summary>
    public class HordeTests
    {
        static FixtureSong Steady(double bpm = 128, double until = 30) => new FixtureSong(5).Beats(0, until, bpm);

        static Simulation Run(SimConfig config, FixtureSong song, double until, Action<Simulation> eachFrame = null)
        {
            var sim = new Simulation(config);
            Drive.Feed(sim, song.Entries(), until, eachFrame: eachFrame);
            return sim;
        }

        static double Distance(double ax, double ay, double bx, double by) => Math.Sqrt((ax - bx) * (ax - bx) + (ay - by) * (ay - by));

        [Test]
        public void FootplantsLandExactlyOnMainBeatsAfterAShortMoveWindow()
        {
            var song = Steady(128).Gap(30, 34).Beats(34, 60, 92, 160, jitter: 0.004);
            var sim = Run(HordeFixture.Config(), song, 60);
            var beats = sim.Stream.Beats.ToArray();
            var steps = Drive.Events(sim, SimEventKind.EnemyStep);
            Assert.Greater(steps.Count, 5000);
            var lastBeatOf = new Dictionary<int, int>();
            foreach (var e in steps)
            {
                var s = e.AsEnemyStep;
                int i = Array.FindIndex(beats, b => b.T == s.Footplant);
                Assert.GreaterOrEqual(i, 0, $"a footplant at {s.Footplant} on no committed beat");
                double window = Math.Min(0.1, 0.2 * sim.Beats.PeriodOf(i));
                Assert.That(s.Footplant - s.MoveStart, Is.GreaterThan(0).And.LessThanOrEqualTo(window + 1e-9));
                Assert.AreEqual(s.MoveStart, s.T);
                // One step a beat at most, and the "x." roles (mudman1) every other beat.
                if (lastBeatOf.TryGetValue(s.Enemy, out int last))
                {
                    Assert.Greater(i, last);
                    var role = HordeFixture.Content.Roles[sim.State.Enemies[s.Enemy].Role];
                    if (role.Gait.Pattern == "x.") Assert.AreEqual(0, i % 2);
                }
                lastBeatOf[s.Enemy] = i;
            }
        }

        [Test]
        public void NoEnemyMovesBetweenBeatsExceptKnockback()
        {
            var song = Steady(128, 40).Gap(40, 44).Beats(44, 60, 140);
            var before = new Dictionary<int, (double x, double y)>();
            // Each enemy's latest motion windows from its step and knock events: [start, end].
            var windows = new Dictionary<int, List<(double from, double to)>>();
            int cursor = 0, knocked = 0;
            double previous = 0;
            var sim = new Simulation(HordeFixture.Config());
            Drive.Feed(sim, song.Entries(), 60, eachFrame: s =>
            {
                foreach (var e in s.EventsSince(cursor))
                {
                    if (e.Kind == SimEventKind.Spawn) before.Remove(e.AsSpawn.Enemy);
                    if (e.Kind == SimEventKind.EnemyStep) Window(e.AsEnemyStep.Enemy, e.AsEnemyStep.MoveStart, e.AsEnemyStep.Footplant);
                    if (e.Kind == SimEventKind.EnemyKnock) Window(e.AsEnemyKnock.Enemy, e.AsEnemyKnock.Start, e.AsEnemyKnock.End);
                }
                cursor = s.EventCount;
                var enemies = s.State.Enemies;
                for (int i = 0; i < enemies.Length; i++)
                {
                    if (!enemies[i].Alive) continue;
                    if (before.TryGetValue(i, out var p) && (p.x != enemies[i].X || p.y != enemies[i].Y))
                        Assert.IsTrue(windows[i].Any(w => w.from < s.Now + 1e-9 && w.to > previous - 1e-9),
                            $"enemy {i} moved between beats in ({previous}, {s.Now}]");
                    before[i] = (enemies[i].X, enemies[i].Y);
                }
                previous = s.Now;
                // A sword hit's knockback, now and then: the one travel allowed between beats.
                if (s.State.Tick % 97 == 0 && s.State.EnemiesAlive > 0)
                {
                    int target = Array.FindIndex(s.State.Enemies.ToArray(), e => e.Alive);
                    s.Horde.Knock(s, target, s.Now, 1, 0, 1);
                    knocked++;
                }
            });
            Assert.Greater(knocked, 10);
            Assert.Greater(Drive.Events(sim, SimEventKind.EnemyStep).Count, 1000);

            void Window(int enemy, double from, double to)
            {
                if (!windows.TryGetValue(enemy, out var list)) windows[enemy] = list = new List<(double, double)>();
                list.Add((from, to));
                if (list.Count > 3) list.RemoveAt(0);
            }
        }

        [Test]
        public void EnemiesHoldWithoutAReliableBeatAndResumeWithoutCatchingUp()
        {
            // Beats below the gate, then none at all, then reliable beats again.
            var song = new FixtureSong(3).Beats(0, 20, 120).Beats(20, 30, 120, evidence: 0.3).Gap(30, 36).Beats(36, 50, 120);
            var sim = Run(HordeFixture.Config(), song, 50);
            var held = new List<SimEvent>();
            foreach (var e in sim.EventsSince(0))
            {
                if (e.Kind == SimEventKind.EnemyStep && e.AsEnemyStep.Footplant > 20.3 && e.AsEnemyStep.Footplant < 36) held.Add(e);
                if (e.Kind == SimEventKind.Attack && e.AsAttack.Impact > 20.3 && e.AsAttack.Impact < 36) held.Add(e);
                if (e.Kind == SimEventKind.Hit && e.T > 20.3 && e.T < 36) held.Add(e);
            }
            Assert.IsEmpty(held, "travel or lunges without a reliable beat");
            var after = Drive.Events(sim, SimEventKind.EnemyStep).Where(e => e.T > 36).ToList();
            Assert.Greater(after.Count, 100, "steps resume on relock");
            // No catch-up: the first steps after the gap are ordinary steps (separation adds at most one more).
            foreach (var e in after.Take(200))
            {
                var s = e.AsEnemyStep;
                var role = HordeFixture.Content.Roles[sim.State.Enemies[s.Enemy].Role];
                double normal = Tempo.EnemyStepDistance(Tempo.Ratio(role.RefSpeedMps, HordeFixture.Content.Progression.Hero.SpeedMps),
                    1.5, Tempo.StridesPerStep(1, role.Gait.Pattern.Length));
                Assert.LessOrEqual(Distance(s.FromX, s.FromY, s.ToX, s.ToY), 2 * normal + 1e-9);
            }
        }

        [Test]
        public void ALongStepHitsTheHeroAnywhereAlongIt()
        {
            foreach (bool heroOnPath in new[] { true, false })
            {
                var sim = HordeFixture.Empty();
                var entries = Steady(128).Entries();
                int fed = Drive.Feed(sim, entries, 5);
                double hx = sim.State.Hero.X, hy = sim.State.Hero.Y;
                // Just after a beat, so the bat notices the hero before its first step.
                int lastBeat = sim.State.LastBeat;
                while (sim.State.LastBeat == lastBeat) fed = Drive.Feed(sim, entries, sim.Now + 1.0 / 120, frame: 1.0 / 120, from: fed);
                // A swarm bat steps about 5.9 m a beat at 128 BPM: in lunge range, beyond stopping range.
                int bat = HordeFixture.Place(sim, "bat-swarm", hx - 8, hy);
                int cursor = sim.EventCount;
                SimEvent step = default;
                while (step.Kind == SimEventKind.None)
                {
                    fed = Drive.Feed(sim, entries, sim.Now + 1.0 / 60, from: fed);
                    foreach (var e in sim.EventsSince(cursor))
                        if (e.Kind == SimEventKind.EnemyStep && e.AsEnemyStep.Enemy == bat) step = e;
                    cursor = sim.EventCount;
                }
                var s = step.AsEnemyStep;
                Assert.Greater(Distance(s.FromX, s.FromY, s.ToX, s.ToY), 5);
                Assert.Greater(Distance(s.ToX, s.ToY, hx, hy), 1.5, "the step ends well short of the hero");
                // The hero gets onto the step's path before the footplant.
                if (heroOnPath) HordeFixture.PutHero(sim, 0.5 * (s.FromX + s.ToX), 0.5 * (s.FromY + s.ToY));
                Drive.Feed(sim, entries, s.Footplant + 0.02, from: fed);
                var hits = Drive.Events(sim, SimEventKind.Hit).Where(e => e.AsHit.Target == -1).ToList();
                if (heroOnPath)
                {
                    Assert.AreEqual(1, hits.Count);
                    Assert.AreEqual(s.Footplant, hits[0].T);
                    Assert.AreEqual(HordeFixture.Content.Role("bat-swarm").Power, hits[0].AsHit.Damage);
                    var notice = Drive.Events(sim, SimEventKind.Attack).Single(e => e.AsAttack.Attacker == bat && e.AsAttack.Impact == s.Footplant);
                    Assert.Less(notice.T, s.MoveStart, "the notice pose comes before the step");
                }
                else
                {
                    Assert.IsEmpty(hits);
                }
            }
        }

        [Test]
        public void ContactLungesHitOnTheBeatWhileTheBeatIsReliable()
        {
            // A standing hero, so the bites go on.
            var sim = HordeFixture.Empty(heroStands: true);
            var song = new FixtureSong(9).Beats(0, 20, 120).Gap(20, 26);
            var entries = song.Entries();
            int fed = Drive.Feed(sim, entries, 4);
            HordeFixture.Place(sim, "skeleton", sim.State.Hero.X + 3, sim.State.Hero.Y);
            Drive.Feed(sim, entries, 26, from: fed);
            var beats = sim.Stream.Beats.ToArray().Select(b => b.T).ToList();
            var hits = Drive.Events(sim, SimEventKind.Hit);
            Assert.Greater(hits.Count, 20, "a skeleton at the hero's side bites every beat");
            foreach (var h in hits)
            {
                Assert.Contains(h.T, beats);
                Assert.Less(h.T, 20.3, "no lunges once the beat is gone");
            }
        }

        [Test]
        public void EnemiesRouteAroundStructures()
        {
            // A wall across the arena between the enemies and the hero, open only at its north end.
            var arena = ArenaData.Open(40, 40);
            var wall = new List<Tile>();
            for (int y = 0; y < 34; y++) wall.AddRange(new[] { new Tile(22, y), new Tile(23, y) });
            arena.Blocked = wall.ToArray();
            arena.HeroStart = new Tile(28, 6);
            var sim = HordeFixture.Empty(arena);
            var entries = Steady(128, 60).Entries();
            int fed = Drive.Feed(sim, entries, 2);
            double hx = sim.State.Hero.X, hy = sim.State.Hero.Y;
            var ids = new List<int>();
            for (int k = 0; k < 6; k++) ids.Add(HordeFixture.Place(sim, "werewolf", -12 + k, -20 + k));
            Drive.Feed(sim, entries, 60, from: fed);
            var nav = sim.Horde.Nav;
            foreach (var e in Drive.Events(sim, SimEventKind.EnemyStep))
            {
                var s = e.AsEnemyStep;
                Assert.AreEqual(1.0, nav.Reach(s.FromX, s.FromY, s.ToX, s.ToY), "a step through a structure");
            }
            foreach (int id in ids)
            {
                var en = sim.State.Enemies[id];
                Assert.Less(Distance(en.X, en.Y, hx, hy), 2.5, $"enemy {id} found its way around the wall");
            }
        }

        /// <summary>Runs <paramref name="roles"/>, placed at the given points, to <paramref name="until"/>: each must reach the hero without a step through a structure.</summary>
        static void AssertEnemiesReachTheHero(ArenaData arena, string[] roles, Func<Simulation, (double x, double y)> place, double until)
        {
            foreach (string role in roles)
            {
                var sim = HordeFixture.Empty(arena);
                var entries = Steady(128, until).Entries();
                int fed = Drive.Feed(sim, entries, 2);
                var (x, y) = place(sim);
                int id = HordeFixture.Place(sim, role, x, y);
                Drive.Feed(sim, entries, until, from: fed);
                foreach (var e in Drive.Events(sim, SimEventKind.EnemyStep))
                {
                    var s = e.AsEnemyStep;
                    Assert.AreEqual(1.0, sim.Horde.Nav.Reach(s.FromX, s.FromY, s.ToX, s.ToY), $"a {role} step through a structure");
                }
                var en = sim.State.Enemies[id];
                Assert.Less(Distance(en.X, en.Y, sim.State.Hero.X, sim.State.Hero.Y), 1.5, $"the {role} reached the hero");
            }
        }

        [Test]
        public void EnemiesJustAcrossAWallFromTheHeroGoRoundIt()
        {
            // A one-tile wall open at its east end, the hero against its north face and enemies
            // against its south face: in step range of the hero, but not in sight.
            var arena = ArenaData.Open(40, 40);
            arena.Blocked = Enumerable.Range(0, 34).Select(x => new Tile(x, 20)).ToArray();
            arena.HeroStart = new Tile(10, 21);
            AssertEnemiesReachTheHero(arena, new[] { "skeleton", "werewolf", "bat-swarm" },
                sim => (sim.State.Hero.X, sim.State.Hero.Y - 2.5), 60);
        }

        [Test]
        public void NoStepSlipsBetweenStructuresThatMeetAtACorner()
        {
            // A diagonal wall of tiles touching only at their corners, open at its north-east end.
            var arena = ArenaData.Open(40, 40);
            arena.Blocked = Enumerable.Range(0, 34).Select(k => new Tile(k, k)).ToArray();
            arena.HeroStart = new Tile(5, 20);
            AssertEnemiesReachTheHero(arena, new[] { "skeleton", "bat-swarm" },
                sim => (sim.Arena.CentreX(12), sim.Arena.CentreY(10)), 90);
        }

        [Test]
        public void AMoveStopsAtTheFirstBlockedCellItTouches()
        {
            var arena = ArenaData.Open(10, 10);
            arena.OriginX = arena.OriginY = 0;
            arena.Blocked = new[] { new Tile(3, 3), new Tile(4, 4) };
            var grid = new NavGrid(new ArenaGrid(arena));
            // Clipping a structure's corner (4.5, 4.5) by a 0.1 m sliver.
            Assert.Less(grid.Reach(4.0, 5.1, 5.1, 4.0), 1);
            // Exactly through the corner (6, 6) where two structures meet, and just beside it.
            Assert.Less(grid.Reach(5.5, 6.5, 6.5, 5.5), 1);
            Assert.Less(grid.Reach(5.5, 6.6, 6.6, 5.5), 1);
            // Beside a structure, and well clear of both.
            Assert.AreEqual(1.0, grid.Reach(4.4, 5.0, 4.4, 1.0));
            Assert.AreEqual(1.0, grid.Reach(1, 8, 8, 8));
            // A stopped move ends walkable, right at the face.
            double u = grid.Reach(1.0, 5.0, 8.0, 5.0);
            Assert.IsTrue(grid.Walkable(1.0 + 7.0 * u, 5.0));
            Assert.AreEqual(4.5, 1.0 + 7.0 * u, 1e-5);
        }

        [Test]
        public void AFrozenWorldHoldsTheHorde()
        {
            var sim = new Simulation(HordeFixture.Config());
            var entries = Steady(128, 30).Entries();
            int fed = Drive.Feed(sim, entries, 10);
            int alive = sim.State.EnemiesAlive;
            sim.State.Frozen = true;
            int cursor = sim.EventCount;
            fed = Drive.Feed(sim, entries, 20, from: fed);
            var during = sim.EventsSince(cursor).ToArray().Select(e => e.Kind).ToList();
            CollectionAssert.DoesNotContain(during, SimEventKind.EnemyStep);
            CollectionAssert.DoesNotContain(during, SimEventKind.Attack);
            CollectionAssert.DoesNotContain(during, SimEventKind.Hit);
            Assert.AreEqual(alive, sim.State.EnemiesAlive, "no spawns while frozen");
            sim.State.Frozen = false;
            cursor = sim.EventCount;
            Drive.Feed(sim, entries, 22, from: fed);
            Assert.Greater(sim.EventsSince(cursor).ToArray().Count(e => e.Kind == SimEventKind.EnemyStep), 100, "steps resume on the thaw");
        }

        [Test]
        public void KnockbackIsImmediateAndPursuitResumesOnALaterBeat()
        {
            var sim = HordeFixture.Empty();
            var entries = Steady(128).Entries();
            int fed = Drive.Feed(sim, entries, 5);
            int id = HordeFixture.Place(sim, "skeleton", 10, 0);
            fed = Drive.Feed(sim, entries, 6, from: fed);
            var start = sim.State.Enemies[id];
            double t = sim.Now;
            int cursor = sim.EventCount;
            sim.Horde.Knock(sim, id, t, 1, 0, 2);
            var knock = sim.EventsSince(cursor)[0].AsEnemyKnock;
            Assert.AreEqual(t, knock.Start);
            Assert.AreEqual(t + Horde.KnockSeconds, knock.End, 1e-12);
            Assert.AreEqual(start.X + 2 * Horde.KnockbackM, knock.ToX, 1e-9, "a fixed distance: role knockback x weapon's");
            Drive.Feed(sim, entries, 8, from: fed);
            var steps = sim.EventsSince(cursor).ToArray().Where(e => e.Kind == SimEventKind.EnemyStep && e.AsEnemyStep.Enemy == id).ToList();
            Assert.Greater(steps.Count, 2);
            Assert.GreaterOrEqual(steps[0].AsEnemyStep.MoveStart, knock.End);
            Assert.AreEqual(knock.ToX, steps[0].AsEnemyStep.FromX, 1e-9);
        }

        [Test]
        public void SpawnsLandOnTheRingInsideTheArenaAndOutOfStructures()
        {
            // The hero near the arena's corner: the ring's in-bounds arc only.
            var arena = ArenaData.Open(60, 60);
            arena.OriginX = arena.OriginY = 0;
            var scattered = new List<Tile>();
            for (int k = 0; k < 60 * 60; k += 7) scattered.Add(new Tile(k % 60, k / 60));
            arena.Blocked = scattered.ToArray();
            arena.HeroStart = new Tile(8, 6);
            var sim = new Simulation(HordeFixture.Config(minimum: 200, arena: arena));
            double hx = sim.State.Hero.X, hy = sim.State.Hero.Y;
            Drive.Feed(sim, Steady(128, 10).Entries(), 10);
            var spawns = Drive.Events(sim, SimEventKind.Spawn);
            Assert.Greater(spawns.Count, 150);
            foreach (var e in spawns)
            {
                var s = e.AsSpawn;
                Assert.IsTrue(sim.Horde.Nav.Walkable(s.X, s.Y), $"a spawn at ({s.X}, {s.Y}) off the walkable arena");
                bool inView = Math.Abs(s.X - hx) <= 17 && Math.Abs(s.Y - hy) <= 11;
                Assert.IsFalse(inView, $"a spawn at ({s.X}, {s.Y}) inside the camera's footprint");
            }
        }

        [Test]
        public void FarEnemiesAreRecycledOntoTheRing()
        {
            var sim = HordeFixture.Empty();
            Drive.Feed(sim, Steady(128, 4).Entries(), 2);
            int id = HordeFixture.Place(sim, "flower", 55, 55);
            int cursor = sim.EventCount;
            sim.StepTo(sim.Now + 0.1);
            var recycled = sim.EventsSince(cursor).ToArray().Single(e => e.Kind == SimEventKind.Spawn).AsSpawn;
            Assert.AreEqual(id, recycled.Enemy);
            Assert.IsTrue(recycled.Recycled);
            Assert.Less(Distance(recycled.X, recycled.Y, sim.State.Hero.X, sim.State.Hero.Y), 30);
            Assert.AreEqual(1, sim.State.EnemiesAlive);
        }

        [Test]
        public void AtMostTheCapAreAlive()
        {
            var sim = new Simulation(HordeFixture.Config(minimum: 1000));
            int most = 0;
            Drive.Feed(sim, Steady(128, 10).Entries(), 10, eachFrame: s =>
            {
                most = Math.Max(most, s.State.EnemiesAlive);
                int alive = 0;
                foreach (var e in s.State.Enemies) alive += e.Alive ? 1 : 0;
                Assert.AreEqual(s.State.EnemiesAlive, alive);
            });
            Assert.AreEqual(300, HordeFixture.Content.Schedule.Cap);
            Assert.AreEqual(300, most);
            Assert.AreEqual(300, sim.State.Enemies.Length);
        }

        [Test]
        public void ThreeHundredAliveRunTheSameWhateverTheFramePacing()
        {
            var entries = Steady(128, 30).Gap(30, 34).Beats(34, 60, 92, 160, jitter: 0.004).Entries();
            var a = new Simulation(HordeFixture.Config(minimum: 300));
            Drive.Feed(a, entries, 60, frame: 1.0 / 60);
            var b = new Simulation(HordeFixture.Config(minimum: 300));
            Drive.Feed(b, entries, 60, frame: 1.0 / 23.7);
            var replay = new Simulation(HordeFixture.Config(minimum: 300));
            foreach (var k in Drive.Known(entries)) replay.AddKnown(k.Entry, k.KnownAt);
            replay.StepTo(60);
            var c = new Simulation(HordeFixture.Config(minimum: 300, seed: 2));
            Drive.Feed(c, entries, 60, frame: 1.0 / 60);
            Assert.AreEqual(300, a.State.EnemiesAlive);
            Assert.Greater(Drive.Events(a, SimEventKind.EnemyStep).Count, 10000);
            Assert.Greater(Drive.Events(a, SimEventKind.Hit).Count, 100);
            Assert.AreEqual(a.Hash, b.Hash);
            Assert.AreEqual(a.Hash, replay.Hash);
            CollectionAssert.AreEqual(a.EventsSince(0).ToArray(), b.EventsSince(0).ToArray());
            Assert.AreNotEqual(a.Hash, c.Hash, "the seed places the spawns");
        }

        [Test]
        public void ARealDumpRunsTheSameWithAFullHorde()
        {
            var entries = EntryDump.Load(EntryDump.House128, out double duration);
            var a = new Simulation(HordeFixture.Config(minimum: 300));
            Drive.Feed(a, entries, duration, frame: 1.0 / 60);
            var b = new Simulation(HordeFixture.Config(minimum: 300));
            Drive.Feed(b, entries, duration, frame: 1.0 / 23.7);
            Assert.Greater(Drive.Events(a, SimEventKind.EnemyStep).Count, 1000);
            Assert.AreEqual(a.Hash, b.Hash);
        }
    
#if !UNITY_5_3_OR_NEWER
        [Test]
        public void AFullHordeAllocatesNothingPerTick()
        {
            Simulation sim = null;
            long allocated = Drive.LeastAllocated(() =>
            {
                sim = new Simulation(HordeFixture.Config(minimum: 300));
                foreach (var k in Drive.Known(Steady(128, 60).Entries())) sim.AddKnown(k.Entry, k.KnownAt);
                sim.StepTo(10); // warm up (JIT, the first spawns and field builds)
                sim.DiscardEventsBefore(sim.EventCount);
                return () =>
                {
                    for (double t = 10; t < 60; t += 1.0 / 60)
                    {
                        sim.StepTo(t);
                        sim.DiscardEventsBefore(sim.EventCount);
                    }
                };
            });
            Assert.AreEqual(300, sim.State.EnemiesAlive);
            Assert.AreEqual(0, allocated, "bytes allocated while stepping 50 s with 300 alive");
        }

        /// <summary>
        /// The sim's step at 300 alive against the 1 ms p99 target (unity-game.md, "Performance
        /// Targets"): a frame's StepTo at 60 fps, as the client calls it, with the hero on the move.
        /// It reports and asserts nothing about time, since checks run under load;
        /// game/evidence/sim-enemies has the measured numbers.
        /// </summary>
        [Test]
        public void ReportsTheStepTimeAtThreeHundredAlive()
        {
            var entries = Steady(128, 30).Gap(30, 34).Beats(34, 90, 92, 160, jitter: 0.004).Entries();
            // The slice's 240 m arena (the default).
            var sim = new Simulation(HordeFixture.Config(minimum: 300));
            int fed = Drive.Feed(sim, entries, 5);
            var walk = new[] { Key.Right, Key.Up, Key.Left, Key.Down };
            int leg = -1;
            int heroMoves = 0;
            double heroX = sim.State.Hero.X;
            var times = new List<double>();
            var watch = new Stopwatch();
            for (double t = 5; t < 90; t += 1.0 / 60)
            {
                while (fed < entries.Count && entries[fed].Stamp + 0.06 <= t) sim.Add(entries[fed++]);
                // The hero walks a square on held keys, a side every 3 s, so the flow field keeps rebuilding.
                if ((int)(t / 3) != leg)
                {
                    if (leg >= 0) sim.Release(walk[leg % 4], t);
                    leg = (int)(t / 3);
                    sim.Press(walk[leg % 4], t);
                }
                watch.Restart();
                sim.StepTo(t);
                watch.Stop();
                times.Add(watch.Elapsed.TotalMilliseconds);
                if (sim.State.Hero.X != heroX) heroMoves++;
                heroX = sim.State.Hero.X;
                sim.DiscardEventsBefore(sim.EventCount);
            }
            Assert.AreEqual(300, sim.State.EnemiesAlive);
            Assert.Greater(heroMoves, 100);
            times.Sort();
            double P(double q) => times[Math.Min(times.Count - 1, (int)(q * times.Count))];
            TestContext.Progress.WriteLine($"sim step at 300 alive over {times.Count} frames: p50 {P(0.5):F3} ms, p99 {P(0.99):F3} ms, "
                + $"max {times[times.Count - 1]:F3} ms (target: p99 <= 1 ms)");
        }
#endif
    }
}
