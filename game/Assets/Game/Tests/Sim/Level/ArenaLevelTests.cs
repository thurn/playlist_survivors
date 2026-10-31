using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using PS.Game.Arena;
using PS.Game.Sim.Level;

namespace PS.Game.Sim.Tests
{
    /// <summary>
    /// The arena's level data (unity-game.md, "The Arena"): game/Content/arena.json, which the editor's
    /// bake exports from arena-layout.json and footprints.json, is the layout's export, about 240 m
    /// square, bounded by impassable forest, with structure sites the hero can reach, and the sim
    /// plays a horde on it.
    /// </summary>
    public class ArenaLevelTests
    {
        static string Content(string file) => Checkout.File("game", "Content", file);

        static LevelExport Export()
        {
            var layout = ArenaLayout.Parse(File.ReadAllText(Content("arena-layout.json")));
            return LevelExport.From(layout, layout.Generate(FootprintTable.Parse(File.ReadAllText(Content("footprints.json")))));
        }

        static ArenaData Level() => ArenaData.Parse(File.ReadAllText(Content("arena.json")), "arena.json");

        [Test]
        public void TheCommittedLevelDataIsTheLayoutsExport()
        {
            string committed = File.ReadAllText(Content("arena.json")).Replace("\r\n", "\n");
            Assert.IsTrue(Export().ToJson() == committed,
                "game/Content/arena.json is stale: bake the arena (`uv run --project demo3d/tools game unity BakeArena`) after changing the layout or footprints");
        }

        [Test]
        public void TheArenaIs240MetresSquareAndBoundedOnEverySide()
        {
            var grid = new ArenaGrid(Level());
            Assert.AreEqual(240, grid.Width * grid.TileM, 1e-9);
            Assert.AreEqual(240, grid.Height * grid.TileM, 1e-9);
            Assert.AreEqual(-120, grid.OriginX, 1e-9);
            Assert.AreEqual(-120, grid.OriginY, 1e-9);
            // The forest takes a wide band at every edge, not just the edge tiles.
            for (int i = 0; i < grid.Width; i++)
                for (int band = 0; band < 10; band++)
                {
                    Assert.IsFalse(grid.IsOpen(i, band) || grid.IsOpen(i, grid.Height - 1 - band), $"an open tile in column {i}, {band} from the edge");
                    Assert.IsFalse(grid.IsOpen(band, i) || grid.IsOpen(grid.Width - 1 - band, i), $"an open tile in row {i}, {band} from the edge");
                }
            Assert.IsTrue(grid.IsOpen(grid.HeroStart.X, grid.HeroStart.Y));
        }

        [Test]
        public void TheHeroCanReachEveryStructureSiteAndAlmostEveryOpenTile()
        {
            var level = Level();
            var grid = new ArenaGrid(level);
            var reached = new bool[grid.Width * grid.Height];
            var queue = new Queue<Tile>();
            queue.Enqueue(grid.HeroStart);
            reached[grid.HeroStart.Y * grid.Width + grid.HeroStart.X] = true;
            int count = 1;
            var steps = new[] { (1, 0), (-1, 0), (0, 1), (0, -1) };
            while (queue.Count > 0)
            {
                var t = queue.Dequeue();
                foreach (var (dx, dy) in steps)
                {
                    int x = t.X + dx, y = t.Y + dy;
                    if (!grid.IsOpen(x, y) || reached[y * grid.Width + x]) continue;
                    reached[y * grid.Width + x] = true;
                    count++;
                    queue.Enqueue(new Tile(x, y));
                }
            }
            int open = 0;
            for (int y = 0; y < grid.Height; y++)
                for (int x = 0; x < grid.Width; x++)
                    if (grid.IsOpen(x, y)) open++;
            Assert.Greater(count, 0.95 * open, "the hero's start reaches all but stray pockets between bushes");
            Assert.Greater(count * grid.TileM * grid.TileM, 15000, "at least 15,000 m² of open ground");

            Assert.GreaterOrEqual(level.Structures.Length, 30);
            Assert.IsTrue(level.Structures.Any(s => s.Large) && level.Structures.Any(s => !s.Large));
            for (int i = 0; i < level.Structures.Length; i++)
            {
                bool beside = level.Structures[i].Tiles.Any(t => steps.Any(s =>
                {
                    int x = t.X + s.Item1, y = t.Y + s.Item2;
                    return grid.IsOpen(x, y) && reached[y * grid.Width + x];
                }));
                Assert.IsTrue(beside, $"structure site {i} has no tile beside the hero's ground");
            }
        }

        [Test]
        public void AHordeSpawnsAndStepsAroundTheArenasObstacles()
        {
            var sim = new Simulation(HordeFixture.Config(minimum: 120, arena: Level()));
            Drive.Feed(sim, new FixtureSong(5).Beats(0, 20, 128).Entries(), 20);
            var nav = sim.Horde.Nav;
            var spawns = Drive.Events(sim, SimEventKind.Spawn);
            Assert.GreaterOrEqual(spawns.Count, 120);
            foreach (var e in spawns)
                Assert.IsTrue(nav.Walkable(e.AsSpawn.X, e.AsSpawn.Y), $"a spawn at ({e.AsSpawn.X}, {e.AsSpawn.Y}) off the walkable arena");
            var moves = Drive.Events(sim, SimEventKind.EnemyStep);
            Assert.Greater(moves.Count, 1000);
            foreach (var e in moves)
            {
                var s = e.AsEnemyStep;
                Assert.AreEqual(1.0, nav.Reach(s.FromX, s.FromY, s.ToX, s.ToY), "a step through the forest or a structure");
            }
        }
    }
}
