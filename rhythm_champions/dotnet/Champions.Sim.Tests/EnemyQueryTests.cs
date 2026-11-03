using System;
using System.Linq;
using NUnit.Framework;

namespace Champions.Sim.Tests
{
    public class EnemyQueryTests
    {
        [Test]
        public void MixedRadiiInOneCellKeepSlotOrderAfterDeathAndReuse()
        {
            var sim = new Simulation(new SimConfig { Loadout = Array.Empty<Upgrade>(), Dummies = new[]
                { new Dummy(1, 1, 100, .5), new Dummy(1, 1, 100, 3.5), new Dummy(1, 1, 100, .5) } });
            var near = new StructList<int>(1);
            sim.Enemies.Near(1, 1, 0, near);
            Assert.That(near.AsSpan().ToArray(), Is.EqualTo(new[] { 0, 1, 2 }));
            sim.Enemies.Kill(1);
            sim.Enemies.Near(1, 1, 0, near);
            Assert.That(near.AsSpan().ToArray(), Is.EqualTo(new[] { 0, 2 }));
            sim.Enemies.Spawn(sim, 1, 1, .25, 100);
            sim.Enemies.Near(1, 1, 0, near);
            Assert.That(near.AsSpan().ToArray(), Is.EqualTo(new[] { 0, 1, 2 }));
            sim.Enemies.Kill(0);
            sim.Enemies.Near(1, 1, 0, near);
            Assert.That(near.AsSpan().ToArray(), Is.EqualTo(new[] { 1, 2 }), "a cached small body dies without rebuilding");
            sim.Enemies.Spawn(sim, 1, 1, .25, 100);
            sim.Enemies.Near(1, 1, 0, near);
            Assert.That(near.AsSpan().ToArray(), Is.EqualTo(new[] { 0, 1, 2 }));
            sim.Enemies.Kill(0); sim.Enemies.Kill(1); sim.Enemies.Kill(2);
            sim.Enemies.Spawn(sim, -2, -2, 5, 100);
            sim.Enemies.Near(-7, -2, 0, near);
            Assert.That(near.Count, Is.EqualTo(1), "a body edge is queryable with no small bodies alive");
        }
        [Test]
        public void MixedBodyQueriesKeepExactHitsAndCellOrderAcrossGridChanges()
        {
            var sim = new Simulation(new SimConfig { Loadout = Array.Empty<Upgrade>() });
            var random = new Random(732);
            for (int i = 0; i < 400; i++)
                sim.Enemies.Spawn(sim, random.NextDouble() * 80 - 40, random.NextDouble() * 80 - 40,
                    i % 31 == 0 ? 3.5 : .1 + random.NextDouble(), 100);
            var near = new StructList<int>(16);
            for (int phase = 0; phase < 3; phase++)
            {
                for (int q = 0; q < 200; q++)
                {
                    double x = random.NextDouble() * 80 - 40, y = random.NextDouble() * 80 - 40;
                    double reach = q % 9 == 0 ? 0 : random.NextDouble() * 8;
                    Check(x, y, reach);
                    // Often the same cell rectangle, with a different exact circle: cached
                    // cell contents must not become cached query hits.
                    Check(x + .125, y - .125, reach + .0625);
                    Check(x, y, reach);
                }
                // Exact body-edge queries, including zero reach and cell boundaries.
                for (int i = 0; i < sim.Enemies.Count; i++)
                {
                    var e = sim.Enemies[i];
                    Check(e.X + e.Radius, e.Y, 0);
                    Check(e.X + e.Radius + 2, e.Y, 2);
                }
                for (int i = phase; i < sim.Enemies.Count; i += 7) sim.Enemies.Kill(i);
                Check(0, 0, 100); // Deaths do not rebuild the cached grid.
                Check(.125, -.125, 100);
                for (int i = 0; i < sim.Enemies.Count; i++)
                {
                    ref var e = ref sim.Enemies.Edit(i);
                    e.X += 3; e.Y -= 2;
                }
                sim.Enemies.Moved();
                sim.Enemies.Spawn(sim, 0, 0, 6 + phase, 100);
            }
            void Check(double x, double y, double reach)
            {
                var expected = Enumerable.Range(0, sim.Enemies.Count).Where(i =>
                {
                    var e = sim.Enemies[i];
                    double dx = e.X - x, dy = e.Y - y, r = reach + e.Radius;
                    return e.Alive && dx * dx + dy * dy <= r * r;
                }).OrderBy(i => (int)Math.Floor(sim.Enemies[i].Y / Enemies.CellM))
                    .ThenBy(i => (int)Math.Floor(sim.Enemies[i].X / Enemies.CellM)).ThenBy(i => i).ToArray();
                sim.Enemies.Near(x, y, reach, near);
                Assert.That(near.AsSpan().ToArray(), Is.EqualTo(expected));
            }
        }
    }
}
