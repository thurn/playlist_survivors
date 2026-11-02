using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace Champions.Sim.Tests
{
    /// <summary>
    /// The world's structures (design.md, The World &gt; The infinite arena and Structures, Decision 18; plan.md, The Sim
    /// &gt; Systems): chunks made from the seed and their coordinates alone, whatever order the hero meets them in, with
    /// open lanes and a clear start; loading round the hero; blocking with slides; the breaking rules; breaks remembered
    /// across unloading; empty chunks in a duel.
    /// </summary>
    public class StructureTests
    {
        const double Tick = Simulation.TickSeconds;
        static readonly GameContent Content = DashTests.Content;
        static StructureRules Rules => Content.Structures;

        static int Kind(string id) => Rules.IndexOf(id);

        /// <summary>A sim on the content with level 1's structures (no spawner), the hero unhurt and without attacks.</summary>
        static Simulation Sim(ulong seed = 7, Action<SimConfig> tweak = null)
        {
            var config = new RunInputs { Seed = seed, Loadout = new Upgrade[0] }.Build(Content);
            config.Structures = Rules.Clone();
            config.Hero.Health = 1e9;
            tweak?.Invoke(config);
            return new Simulation(config);
        }

        /// <summary>
        /// Rules where every chunk holds one structure of <paramref name="kind"/> at its middle (a quarter turn or not)
        /// and the start needn't be clear: structures at (12 + 24i, 12 + 24j).
        /// </summary>
        static StructureRules OneEach(string kind)
        {
            var rules = Rules.Clone();
            rules.ClearStartM = 0;
            rules.Templates = new[]
            {
                new StructureTemplate { Id = "one", Weight = 1, Placements = new[] { new Placement { Kind = Kind(kind) } } },
            };
            return rules;
        }

        static string Describe(StructList<Placed> placed) =>
            string.Join("; ", Enumerable.Range(0, placed.Count).Select(i =>
                $"{placed[i].Index} {placed[i].Kind} {placed[i].X:R} {placed[i].Y:R} {placed[i].Angle:R}"));

        /// <summary>Walks the hero to (<paramref name="x"/>, <paramref name="y"/>) by click target, stepping until it gets there.</summary>
        static void WalkTo(Simulation sim, double x, double y)
        {
            sim.Target(x, y, sim.Now);
            for (int i = 0; i < 120 * 60; i++)
            {
                sim.StepTo(sim.Now + Tick);
                sim.HeroWas(sim.Now, out double hx, out double hy);
                if (Math.Abs(hx - x) < 1e-6 && Math.Abs(hy - y) < 1e-6) return;
            }
            Assert.Fail($"the hero never reached ({x}, {y})");
        }

        /// <summary>The standing structures in chunk (cx, cy), as kind, position and angle.</summary>
        static List<string> Standing(Simulation sim, int cx, int cy)
        {
            var field = sim.StructureField;
            var list = new List<string>();
            foreach (int id in field.InChunk(cx, cy).ToArray())
            {
                var s = field[id];
                if (s.Standing) list.Add($"{s.Index} {s.Kind} {s.X:R} {s.Y:R} {s.Angle:R}");
            }
            return list;
        }

        // ---- Chunks ----

        [Test]
        public void ChunksAreTheSameForASeedWhateverOrderTheyAreVisitedIn()
        {
            var coords = new List<(int, int)>();
            for (int cy = -6; cy <= 6; cy++)
                for (int cx = -6; cx <= 6; cx++) coords.Add((cx, cy));
            var shuffled = coords.OrderBy(c => (c.Item1 * 7919 + c.Item2 * 104729) & 0xFF).Reverse().ToList();
            var placed = new StructList<Placed>(64);
            var random = new SimRandom(99);
            var first = new Dictionary<(int, int), string>();
            foreach (var (cx, cy) in coords)
            {
                StructureChunks.Generate(Rules, 7, cx, cy, random, placed);
                first[(cx, cy)] = Describe(placed);
            }
            var other = new SimRandom(5);
            foreach (var (cx, cy) in shuffled)
            {
                StructureChunks.Generate(Rules, 7, cx, cy, other, placed);
                Assert.AreEqual(first[(cx, cy)], Describe(placed), $"chunk ({cx}, {cy}) visited in another order");
            }
            int differ = coords.Count(c =>
            {
                StructureChunks.Generate(Rules, 8, c.Item1, c.Item2, random, placed);
                return Describe(placed) != first[c];
            });
            Assert.Greater(differ, coords.Count / 3, "another seed, another world");

            // Through the sim: two routes to the same chunk find it standing alike.
            var east = Sim();
            WalkTo(east, 120, 0);
            WalkTo(east, 120, 120);
            var north = Sim();
            WalkTo(north, 0, 120);
            WalkTo(north, 120, 120);
            StructureChunks.ChunkOf(Rules, 120, 120, out int hcx, out int hcy);
            int compared = 0;
            for (int cy = hcy - 1; cy <= hcy + 1; cy++)
                for (int cx = hcx - 1; cx <= hcx + 1; cx++)
                {
                    Assert.IsTrue(east.StructureField.IsLoaded(cx, cy) && north.StructureField.IsLoaded(cx, cy));
                    CollectionAssert.AreEqual(Standing(east, cx, cy), Standing(north, cx, cy), $"chunk ({cx}, {cy})");
                    compared += Standing(east, cx, cy).Count;
                }
            Assert.Greater(compared, 0, "some structures stand there to compare");
        }

        [Test]
        public void TemplatesKeepTheLanesOpenTheStartClearAndMixAsWeighted()
        {
            var placed = new StructList<Placed>(64);
            var random = new SimRandom(0);
            var uses = new int[Rules.Templates.Length];
            double inside = Rules.ChunkM / 2 - Rules.LaneM;
            for (int cy = -20; cy < 20; cy++)
                for (int cx = -20; cx < 20; cx++)
                {
                    uses[StructureChunks.Generate(Rules, 3, cx, cy, random, placed)]++;
                    double mx = (cx + 0.5) * Rules.ChunkM, my = (cy + 0.5) * Rules.ChunkM;
                    for (int i = 0; i < placed.Count; i++)
                    {
                        var p = placed[i];
                        double bound = Rules.Kinds[p.Kind].Bound;
                        Assert.LessOrEqual(Math.Max(Math.Abs(p.X - mx), Math.Abs(p.Y - my)) + bound, inside + 1e-9, "inside the lanes");
                        Assert.GreaterOrEqual(Math.Sqrt(p.X * p.X + p.Y * p.Y) - bound, Rules.ClearStartM - 1e-9, "clear of the start");
                    }
                }
            double total = Rules.Templates.Sum(t => t.Weight);
            for (int i = 0; i < uses.Length; i++)
                Assert.AreEqual(Rules.Templates[i].Weight / total, uses[i] / 1600.0, 0.05, Rules.Templates[i].Id);
        }

        [Test]
        public void ChunksLoadAsTheHeroNearsThemAndUnloadOnceItIsFar()
        {
            var sim = Sim();
            var field = sim.StructureField;
            Assert.Greater(field.Standing, 0, "the chunks round the start stand from the first tick");
            sim.Press(Key.Right, 0);
            for (int step = 0; step < 120 * 40; step++)
            {
                sim.StepTo(sim.Now + Tick);
                if (step % 60 != 0) continue;
                sim.HeroWas(sim.Now, out double hx, out double hy);
                StructureChunks.ChunkOf(Rules, hx, hy, out int hcx, out int hcy);
                for (int cy = hcy - 5; cy <= hcy + 5; cy++)
                    for (int cx = hcx - 5; cx <= hcx + 5; cx++)
                    {
                        double x0 = cx * Rules.ChunkM, y0 = cy * Rules.ChunkM;
                        double dx = Math.Max(0, Math.Max(x0 - hx, hx - x0 - Rules.ChunkM)), dy = Math.Max(0, Math.Max(y0 - hy, hy - y0 - Rules.ChunkM));
                        double d = Math.Sqrt(dx * dx + dy * dy);
                        if (d <= Rules.LoadM) Assert.IsTrue(field.IsLoaded(cx, cy), $"({cx}, {cy}) at {d:F1} m is loaded");
                        if (d > Rules.UnloadM + 6 * Tick) Assert.IsFalse(field.IsLoaded(cx, cy), $"({cx}, {cy}) at {d:F1} m is unloaded");
                    }
            }
            int spawned = Drive.Events(sim, SimEventKind.StructureSpawn).Count, gone = Drive.Events(sim, SimEventKind.StructureGone).Count;
            Assert.Greater(gone, 0, "a long walk leaves chunks behind");
            Assert.AreEqual(field.Standing, spawned - gone, "every structure that stood is still standing or went with its chunk");
        }

        // ---- Breaking ----

        /// <summary>A sim where every chunk holds one <paramref name="kind"/>, and the id of the one in chunk (0, 0), at (12, 12).</summary>
        static Simulation OneOf(string kind, out int id)
        {
            var sim = Sim(tweak: c => c.Structures = OneEach(kind));
            id = sim.StructureField.InChunk(0, 0)[0];
            var s = sim.StructureField[id];
            Assert.AreEqual(12, s.X, 1e-9);
            Assert.AreEqual(12, s.Y, 1e-9);
            return sim;
        }

        static Blow BlowAt(Simulation sim, double x, double y, double radius = 1, bool shatter = false) => new Blow
        {
            Shape = HitShape.Circle(x, y, radius), T = sim.Now, Damage = 1, Tags = HitTags.Strike, Source = BlowSource.SwordSlash,
            Owner = -1, Part = -1, Shatter = shatter,
        };

        [Test]
        public void ASmallStructureBreaksInOneHitABuildingInItsTwoOrThreeAwayFromTheBlow()
        {
            foreach (var (kind, hits) in new[] { ("fence", 1), ("rock", 1), ("pine", 1), ("tree", 1), ("wall", 1), ("hut", 2), ("cottage", 3), ("tower", 3) })
            {
                Assert.AreEqual(hits, Rules.Kinds[Kind(kind)].Hits, kind);
                var sim = OneOf(kind, out int id);
                for (int h = 1; h <= hits; h++)
                {
                    Assert.AreEqual(1, sim.Strikes.Land(sim, BlowAt(sim, 9, 12, 3)), $"{kind}: hit {h} reaches it");
                    sim.StepTo(sim.Now + Tick);
                    Assert.AreEqual(h < hits, sim.StructureField[id].Standing, $"{kind} after {h} hits");
                }
                var hitEvents = Drive.Events(sim, SimEventKind.StructureHit);
                Assert.AreEqual(hits - 1, hitEvents.Count, kind);
                var broke = Drive.Events(sim, SimEventKind.StructureBreak).Single();
                Assert.AreEqual(id, broke.I0);
                Assert.AreEqual(Kind(kind), broke.I1);
                Assert.AreEqual((int)BreakCause.Blow, broke.I2);
                Assert.AreEqual(1, broke.D0, 1e-9, $"{kind}: thrown away from the blow, east");
                Assert.AreEqual(0, broke.D1, 1e-9);
                Assert.AreEqual(0, sim.Strikes.Land(sim, BlowAt(sim, 9, 12, 5)), $"{kind}: a broken one takes no more blows");
            }
        }

        [Test]
        public void AShatteringBlowABossAttackAndAnEntranceBreakOutright()
        {
            var sim = OneOf("cottage", out int id);
            sim.Strikes.Land(sim, BlowAt(sim, 12, 9, 2, shatter: true));
            Assert.IsFalse(sim.StructureField[id].Standing, "Skyfall's landing shatters a building at once");
            Assert.AreEqual(0, Drive.Events(sim, SimEventKind.StructureHit).Count);

            sim = Sim(tweak: c => c.Structures = OneEach("tower"));
            var field = sim.StructureField;
            int standing = field.Standing;
            int n = field.Shatter(sim, HitShape.Circle(0, 0, 20), sim.Now, BreakCause.Entrance);
            Assert.AreEqual(4, n, "the four towers round the origin, each 17 m off");
            Assert.AreEqual(standing - 4, field.Standing);
            var breaks = Drive.Events(sim, SimEventKind.StructureBreak);
            Assert.IsTrue(breaks.All(b => b.I2 == (int)BreakCause.Entrance));
            foreach (var b in breaks)
                Assert.AreEqual(1, (b.D3 * b.D0 + b.D4 * b.D1) / Math.Sqrt(b.D3 * b.D3 + b.D4 * b.D4), 1e-9, "thrown outward from the boss");
            Assert.AreEqual(1, field.Shatter(sim, HitShape.Path(12, -50, 12, -30, 1), sim.Now, BreakCause.Boss), "a boss's charge breaks what it crosses");
        }

        [Test]
        public void TheHerosAttacksAndDamagingDashesBreakStructuresButMonstersNever()
        {
            // Sword Slash swings at the dummy beside the fence, and its arc takes the fence too.
            var config = new RunInputs { Seed = 7, Dummies = new[] { new Dummy(12, 10.3, 100) } }.Build(Content);
            config.Structures = OneEach("fence");
            config.Hero.Health = 1e9;
            var sim = new Simulation(config);
            WalkTo(sim, 12, 9);
            sim.StepTo(sim.Now + 3);
            Assert.AreEqual(1, sim.StructureField.Breaks, "the slash broke the fence");
            Assert.AreEqual((int)BreakCause.Blow, Drive.Events(sim, SimEventKind.StructureBreak).Single().I2);

            // A damaging dash runs into a wall and breaks through it.
            sim = Sim(tweak: c => c.Structures = OneEach("wall"));
            sim.Dashes.Equip(sim, Content.Dashes.IndexOf("leapSlam"), 1);
            int wall = sim.StructureField.InChunk(0, 0)[0];
            var w = sim.StructureField[wall];
            // Stand 3 m off the wall's face toward the start, square to it, and dash at it.
            double fx = -w.Sin, fy = w.Cos;
            if (fx * 12 + fy * 12 < 0)
            {
                fx = -fx;
                fy = -fy;
            }
            WalkTo(sim, 12 - fx * 3, 12 - fy * 3);
            sim.Target(12 + fx * 10, 12 + fy * 10, sim.Now);
            sim.StepTo(sim.Now + Tick);
            sim.Press(Key.Space, sim.Now);
            sim.StepTo(sim.Now + 1);
            Assert.IsFalse(sim.StructureField[wall].Standing, "the dash broke the wall");

            // Monsters walk round structures and never break them.
            var horde = Sim(tweak: c => c.Structures = OneEach("cottage"));
            int mob = horde.Horde.Spawn(Content.Enemies.IndexOf("skeleton"), 12, 20);
            horde.StepTo(20);
            Assert.AreEqual(0, horde.StructureField.Breaks);
            Assert.IsTrue(horde.Enemies[mob].Alive);
        }

        [Test]
        public void BrokenStructuresStayBrokenAfterTheirChunkUnloadsAndLoadsAgain()
        {
            var sim = Sim(tweak: c => c.Structures = OneEach("fence"));
            var field = sim.StructureField;
            int id = field.InChunk(0, 0)[0];
            sim.Strikes.Land(sim, BlowAt(sim, 12, 12, 2));
            Assert.IsFalse(field[id].Standing);
            Assert.IsTrue(field.WasBroken(0, 0, 0));
            int beside = field.InChunk(1, 0)[0];
            Assert.IsTrue(field[beside].Standing);

            // Along the lanes, where nothing stands in the way.
            WalkTo(sim, -150, 0);
            Assert.IsFalse(field.IsLoaded(0, 0), "far away, the chunk unloaded");
            int spawnsBefore = Drive.Events(sim, SimEventKind.StructureSpawn).Count;
            WalkTo(sim, 0, 0);
            Assert.IsTrue(field.IsLoaded(0, 0) && field.IsLoaded(1, 0));
            Assert.AreEqual(0, field.InChunk(0, 0).Length, "the broken fence never stands again");
            Assert.IsTrue(field[field.InChunk(1, 0)[0]].Standing, "its neighbour's stands again as it was");
            var respawned = Drive.Events(sim, SimEventKind.StructureSpawn).Skip(spawnsBefore).ToList();
            Assert.IsFalse(respawned.Any(e => Math.Abs(e.D0 - 12) < 1e-9 && Math.Abs(e.D1 - 12) < 1e-9), "no spawn where it stood");
        }

        // ---- Blocking ----

        [Test]
        public void BodiesStopAtFootprintsAndSlideAlongThem()
        {
            var sim = OneOf("wall", out int id);
            var field = sim.StructureField;
            var w = field[id];
            var k = Rules.Kinds[w.Kind];
            const double r = 0.4;
            // Square into the wall's face from 3 m off: stopped r from it.
            double fx = -w.Sin, fy = w.Cos, ax = w.Cos, ay = w.Sin;
            double sx = 12 - fx * 3, sy = 12 - fy * 3;
            field.Move(sx, sy, r, fx * 5, fy * 5, out double nx, out double ny, out int blocker);
            Assert.AreEqual(id, blocker);
            Assert.AreEqual(3 - k.HalfY - r, (nx - sx) * fx + (ny - sy) * fy, 1e-3, "it stops at the face");
            // At 45 degrees into it: it slides along.
            double gx = (fx + ax) * Math.Sqrt(0.5) * 5, gy = (fy + ay) * Math.Sqrt(0.5) * 5;
            field.Move(sx, sy, r, gx, gy, out nx, out ny, out blocker);
            Assert.AreEqual(id, blocker);
            Assert.AreEqual(3 - k.HalfY - r, (nx - sx) * fx + (ny - sy) * fy, 1e-3, "held at the face");
            Assert.AreEqual(5 * Math.Sqrt(0.5), (nx - sx) * ax + (ny - sy) * ay, 1e-3, "and slid the whole way along it");
            // Past its end, round the corner.
            double ex = 12 + ax * (k.HalfX + r + 0.01), ey = 12 + ay * (k.HalfX + r + 0.01);
            field.Move(ex - fx * 3, ey - fy * 3, r, fx * 6, fy * 6, out nx, out ny, out blocker);
            Assert.AreEqual(-1, blocker, "clear of its end, nothing stops it");
            // A circle: a rock straight on stops the body short, glancing it slides round.
            sim = OneOf("rock", out id);
            field = sim.StructureField;
            double reach = Rules.Kinds[Kind("rock")].Radius + r;
            field.Move(12, 6, r, 0, 10, out nx, out ny, out blocker);
            Assert.AreEqual(id, blocker);
            Assert.AreEqual(12 - reach, ny, 1e-3);
            field.Move(12.5, 6, r, 0, 12, out nx, out ny, out blocker);
            Assert.AreEqual(id, blocker);
            Assert.Greater(nx, 13.5, "slid off it to the side it was struck on");
            Assert.GreaterOrEqual(Math.Sqrt((nx - 12) * (nx - 12) + (ny - 12) * (ny - 12)), reach - 1e-3, "never inside");
            // A body already deep inside (a structure loaded on it) walks out freely.
            field.Move(12, 12.2, r, 0, 4, out nx, out ny, out blocker);
            Assert.AreEqual(-1, blocker);
            Assert.AreEqual(16.2, ny, 1e-9);
        }

        [Test]
        public void TheHeroNeverWalksIntoAStructure()
        {
            foreach (string kind in new[] { "cottage", "tower", "fence" })
            {
                var sim = OneOf(kind, out int id);
                var s = sim.StructureField[id];
                var k = Rules.Kinds[s.Kind];
                double r = Content.Hero.RadiusM;
                sim.Target(12.3, 12.6, sim.Now);
                for (int i = 0; i < 120 * 8; i++)
                {
                    sim.StepTo(sim.Now + Tick);
                    sim.HeroWas(sim.Now, out double hx, out double hy);
                    double gap = k.Shape == FootprintShape.Circle
                        ? Math.Sqrt((hx - 12) * (hx - 12) + (hy - 12) * (hy - 12)) - k.Radius
                        : Gap(hx, hy, s, k);
                    Assert.GreaterOrEqual(gap, r - 1e-3, $"{kind}: the hero at ({hx:F3}, {hy:F3}) stays outside");
                }
                sim.HeroWas(sim.Now, out double ex, out double ey);
                Assert.Less(Math.Sqrt((ex - 12) * (ex - 12) + (ey - 12) * (ey - 12)), k.Bound + r + 0.05, $"{kind}: it ends against the structure");
            }
        }

        static double Gap(double x, double y, Structure s, StructureKind k)
        {
            HitShape.ToBox(x, y, s.X, s.Y, s.Cos, s.Sin, out double px, out double py);
            return HitShape.BoxDistance(px, py, k.HalfX, k.HalfY);
        }

        [Test]
        public void BlowsReachBoxesByTheirFootprintNotTheirBound()
        {
            // A 6 x 1 box along x at the origin.
            double hx = 3, hy = 0.5, c = 1, s = 0;
            Assert.IsTrue(HitShape.Circle(2.5, 1.2, 0.8).TouchesBox(0, 0, c, s, hx, hy));
            Assert.IsFalse(HitShape.Circle(0, 2, 1.4).TouchesBox(0, 0, c, s, hx, hy), "its bound would reach, its face doesn't");
            Assert.IsTrue(HitShape.Ring(0, 0, 3.0, 4).TouchesBox(0, 0, c, s, hx, hy), "a ring's band crosses its ends");
            Assert.IsFalse(HitShape.Ring(0, 0, 3.2, 4).TouchesBox(0, 0, c, s, hx, hy), "a ring wider than it misses it");
            Assert.IsTrue(HitShape.Path(-1, -3, 1, 3, 0.2).TouchesBox(0, 0, c, s, hx, hy), "a path across it");
            Assert.IsFalse(HitShape.Path(-1, 1, 1, 1.5, 0.4).TouchesBox(0, 0, c, s, hx, hy));
            Assert.IsTrue(HitShape.Cone(0, -2, 0, 1, 2, 0.3).TouchesBox(0, 0, c, s, hx, hy), "a cone facing it");
            Assert.IsFalse(HitShape.Cone(0, -2, 0, -1, 2, 0.3).TouchesBox(0, 0, c, s, hx, hy), "facing away");
            Assert.IsTrue(HitShape.Cone(-4, -2, 1, 0, 6, 0.5).TouchesBox(0, 0, c, s, hx, hy), "its edge sweeps the box's side");
            // Turned a quarter turn, the box runs along y.
            Assert.IsTrue(HitShape.Circle(0, 2.5, 0.2).TouchesBox(0, 0, 0, 1, hx, hy));
            Assert.IsFalse(HitShape.Circle(2.5, 0, 0.2).TouchesBox(0, 0, 0, 1, hx, hy));
        }

        // ---- Duels ----

        [Test]
        public void ChunksMetInADuelStandEmptyUntilTheyUnload()
        {
            var sim = Sim(tweak: c => c.Structures = OneEach("tree"));
            var field = sim.StructureField;
            int standing = field.Standing;
            field.Duel(true);
            WalkTo(sim, 60, 0);
            Assert.IsTrue(field.IsLoaded(4, 0));
            Assert.AreEqual(0, field.InChunk(4, 0).Length, "a chunk met in the duel holds nothing");
            Assert.IsTrue(field[field.InChunk(0, 0)[0]].Standing, "those loaded before it stand");
            field.Duel(false);
            WalkTo(sim, 160, 0);
            Assert.Greater(field.InChunk(7, 0).Length, 0, "after the duel chunks fill again");
            WalkTo(sim, 400, 0);
            WalkTo(sim, 60, 0);
            Assert.AreEqual(1, field.InChunk(4, 0).Length, "unloaded and met again outside a duel, it stands as generated");
            Assert.Greater(standing, 0);
        }

        // ---- The run ----

        [Test]
        public void TheLevelBringsItsStructuresAndTheRunReplaysToTheSameHash()
        {
            var a = new Simulation(new RunInputs { Seed = 11, Level = true }.Build(Content));
            Assert.IsNotNull(a.StructureField, "level 1's world has structures");
            Assert.IsNull(new Simulation(new RunInputs { Seed = 11 }.Build(Content)).StructureField, "without the level, an open arena");
            var b = new Simulation(new RunInputs { Seed = 11, Level = true }.Build(Content));
            foreach (var sim in new[] { a, b })
            {
                sim.Press(Key.Right, 0);
                sim.Press(Key.Up, 0);
                sim.StepTo(30);
            }
            Assert.AreEqual(a.Hash, b.Hash);
            Assert.Greater(a.StructureField.Standing, 0);
        }
    }
}
