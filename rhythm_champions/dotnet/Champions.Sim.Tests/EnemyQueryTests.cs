using System;
using System.Linq;
using NUnit.Framework;

namespace Champions.Sim.Tests
{
    public class EnemyQueryTests
    {
        readonly struct Eligible : IEnemyFilter
        {
            readonly bool none;
            public Eligible(bool none) { this.none = none; }
            public bool Accept(in Enemy e) => !none && e.Generation % 3 != 0;
        }
        [Test]
        public void NearestRetainsTheRoundedReachAtTheNextCellBoundary()
        {
            var sim = new Simulation(new SimConfig { Loadout = Array.Empty<Upgrade>(),
                Dummies = new[] { new Dummy(4, 0, 100, 1) } });
            double x = Math.BitDecrement(2.0);
            Assert.That(4 - x, Is.EqualTo(2), "the original distance calculation rounds to the inclusive reach");
            Assert.That(sim.Enemies.Nearest(x, 0, 1, new Eligible(false)), Is.EqualTo(0));
        }
        [Test]
        public void NearestEligibleMatchesAllBodiesAcrossLocalAndSpreadPopulations()
        {
            var sim = new Simulation(new SimConfig { Loadout = Array.Empty<Upgrade>() });
            var random = new Random(989);
            for (int i = 0; i < 200; i++)
                sim.Enemies.Spawn(sim, random.NextDouble() * 40 - 20, random.NextDouble() * 40 - 20,
                    i % 11 == 0 ? 4 : .1 + random.NextDouble(), 100);
            for (int phase = 0; phase < 3; phase++)
            {
                ref var far = ref sim.Enemies.Edit(199);
                far.X = phase == 1 ? 30000 : 1; far.Y = phase == 1 ? -30000 : 1;
                sim.Enemies.Moved();
                for (int q = 0; q < 150; q++)
                {
                    double x = random.NextDouble() * 50 - 25, y = random.NextDouble() * 50 - 25;
                    double reach = q % 13 == 0 ? 100 : q % 7 == 0 ? 0 : random.NextDouble() * 12;
                    var filter = new Eligible(q % 9 == 0);
                    int expected = Enumerable.Range(0, sim.Enemies.Count).Where(i =>
                    {
                        var e = sim.Enemies[i]; double dx = e.X - x, dy = e.Y - y;
                        return e.Alive && dx * dx + dy * dy <= (reach + e.Radius) * (reach + e.Radius) && filter.Accept(e);
                    }).OrderBy(i => { var e = sim.Enemies[i]; return (e.X - x) * (e.X - x) + (e.Y - y) * (e.Y - y); })
                        .ThenBy(i => i).DefaultIfEmpty(-1).First();
                    Assert.That(sim.Enemies.Nearest(x, y, reach, filter), Is.EqualTo(expected), $"phase {phase}, query {q}");
                }
                for (int id = phase; id < sim.Enemies.Count; id += 7) sim.Enemies.Kill(id);
                sim.Enemies.Spawn(sim, -1, -1, .5, 100);
            }
        }
        [Test]
        public void RepeatedExactCirclesOwnTheirResultsAndTrackDeathsSpawnsAndMovement()
        {
            var sim = new Simulation(new SimConfig { Loadout = Array.Empty<Upgrade>(), Dummies = new[]
                { new Dummy(0, 0, 100, .5), new Dummy(1, 0, 100, 4), new Dummy(2, 0, 100, .5) } });
            var a = new StructList<int>(1); var b = new StructList<int>(1);
            sim.Enemies.Near(0, 0, 3, a);
            var expected = a.AsSpan().ToArray();
            a[0] = 999;
            sim.Enemies.Near(0, 0, 3, b);
            Assert.That(b.AsSpan().ToArray(), Is.EqualTo(expected), "caller edits cannot mutate saved results");
            sim.Enemies.Kill(1); sim.Enemies.Kill(2);
            sim.Enemies.Near(0, 0, 3, a);
            Assert.That(a.AsSpan().ToArray(), Is.EqualTo(new[] { 0 }), "cached ordinary and large bodies die together");
            sim.Enemies.Spawn(sim, 1, 0, .5, 100);
            sim.Enemies.Near(0, 0, 3, a);
            Assert.That(a.AsSpan().ToArray(), Is.EqualTo(new[] { 0, 2 }));
            sim.Enemies.Edit(0).X = 20; sim.Enemies.Moved();
            sim.Enemies.Near(0, 0, 3, a);
            Assert.That(a.AsSpan().ToArray(), Is.EqualTo(new[] { 2 }));
            sim.Enemies.Near(0, 0, 3, b);
            Assert.That(b.AsSpan().ToArray(), Is.EqualTo(new[] { 2 }));
        }
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
        public void QueriesKeepOrderWhenThePopulationSpreadsAndContracts()
        {
            var sim = new Simulation(new SimConfig { Loadout = Array.Empty<Upgrade>() });
            var random = new Random(443);
            for (int i = 0; i < 100; i++)
                sim.Enemies.Spawn(sim, random.NextDouble() * 12 - 6, random.NextDouble() * 12 - 6,
                    i % 13 == 0 ? 4 : .2, 100);
            var near = new StructList<int>(1);
            for (int phase = 0; phase < 4; phase++)
            {
                ref var far = ref sim.Enemies.Edit(99);
                far.X = phase % 2 == 0 ? 4 : 30000;
                far.Y = phase % 2 == 0 ? 2 : -30000;
                sim.Enemies.Moved();
                for (int q = 0; q < 20; q++)
                {
                    double x = random.NextDouble() * 16 - 8, y = random.NextDouble() * 16 - 8;
                    var expected = Enumerable.Range(0, sim.Enemies.Count).Where(i =>
                    {
                        var e = sim.Enemies[i]; double dx = e.X - x, dy = e.Y - y;
                        return e.Alive && dx * dx + dy * dy <= (3 + e.Radius) * (3 + e.Radius);
                    }).OrderBy(i => (int)Math.Floor(sim.Enemies[i].Y / Enemies.CellM))
                        .ThenBy(i => (int)Math.Floor(sim.Enemies[i].X / Enemies.CellM)).ThenBy(i => i).ToArray();
                    sim.Enemies.Near(x, y, 3, near);
                    Assert.That(near.AsSpan().ToArray(), Is.EqualTo(expected));
                    sim.Enemies.Near(x, y, 3, near, cacheCircle: false);
                    Assert.That(near.AsSpan().ToArray(), Is.EqualTo(expected));
                    sim.Enemies.Near(x, y, 3, near);
                    Assert.That(near.AsSpan().ToArray(), Is.EqualTo(expected));
                }
                sim.Enemies.Kill(phase);
            }
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
                sim.Enemies.Near(x, y, reach, near, cacheCircle: false);
                Assert.That(near.AsSpan().ToArray(), Is.EqualTo(expected));
            }
        }
    }
}
