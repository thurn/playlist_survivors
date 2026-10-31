using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using PS.Game.Sim.Level;

namespace PS.Game.Sim.Tests
{
    /// <summary>
    /// Crystal structures and zones (unity-game.md, "Crystal Structures" and "Music-Driven
    /// Events"): blocking, break thresholds by size, glints, breaks from the sword's heavy strike
    /// and its cleave, pathing after a break, zone changes on downbeats and the fallback, and
    /// regrowth with its 3 m rule and pushes.
    /// </summary>
    public class StructureTests
    {
        /// <summary>Accelerando 100 to 140 BPM, the first 30 s: a loud jump, structure changes and accents.</summary>
        const string Accelerando = "accelerando-100-140-30s.jsonl";

        /// <summary>Beats and ticks only (no onsets, forecasts, changes or jumps), at their normal known-at times.</summary>
        static List<KnownEntry> Plain(double bpm, double until, double from = 0) =>
            Drive.Known(new FixtureSong(3).Beats(from, until, bpm).Entries()
                .Where(e => e.Kind == EntryKind.Beat || e.Kind == EntryKind.Tick));

        /// <summary>A 40 x 40 tile open arena with these structures; the hero starts on tile (20, 20), centred on (0.75, 0.75).</summary>
        static ArenaData Arena(params StructureSite[] sites)
        {
            var arena = ArenaData.Open(40, 40);
            arena.Structures = sites;
            return arena;
        }

        /// <summary>A structure covering the <paramref name="w"/> by <paramref name="h"/> tiles from (x0, y0).</summary>
        static StructureSite Block(int x0, int y0, int w = 1, int h = 1, bool large = false)
        {
            var tiles = new List<Tile>();
            for (int y = y0; y < y0 + h; y++)
                for (int x = x0; x < x0 + w; x++) tiles.Add(new Tile(x, y));
            return new StructureSite { Tiles = tiles.ToArray(), Large = large };
        }

        /// <summary>A sim with content (so zones run), no spawns, a standing hero and the sword at <paramref name="sword"/> (0: none).</summary>
        static Simulation Start(ArenaData arena, IEnumerable<KnownEntry> known, int sword = 0)
        {
            var sim = new Simulation(HordeFixture.Config(minimum: 0, arena: arena, roster: new string[0], sword: sword, heroStands: true));
            foreach (var k in known) sim.AddKnown(k.Entry, k.KnownAt);
            return sim;
        }

        static List<SimEvent> Since(Simulation sim, int cursor, SimEventKind kind) =>
            sim.EventsSince(cursor).ToArray().Where(e => e.Kind == kind).ToList();

        static double[] Downbeats(Simulation sim) =>
            Drive.Events(sim, SimEventKind.Beat).Select(e => e.AsBeat).Where(b => b.Downbeat).Select(b => b.T).ToArray();

        static double FirstAtOrAfter(double[] times, double t) => times.First(x => x >= t - 1e-9);

        static List<ZoneChangeEvent> ZoneChanges(Simulation sim) =>
            Drive.Events(sim, SimEventKind.ZoneChange).Select(e => e.AsZoneChange).ToList();

        /// <summary>A breaking hit from a hook (a boss slam) over the disc around (x, y).</summary>
        static void Slam(Simulation sim, double x, double y, double reach) =>
            sim.Structures.Strike(sim, sim.Now, x, y, reach, StructureHit.BossSlam, 1);

        static void SlamTile(Simulation sim, int tx, int ty) => Slam(sim, sim.Arena.CentreX(tx), sim.Arena.CentreY(ty), 0.1);

        // ---------------------------------------------------------------- hits and breaks

        [Test]
        public void ASmallStructureBreaksInOneBreakingHitALargeOneInTwoOrThree()
        {
            var sim = Start(Arena(Block(26, 20), Block(20, 26, 3, 2, large: true), Block(14, 12, 3, 3, large: true)), Plain(120, 5));
            sim.StepTo(1);
            CollectionAssert.AreEqual(new[] { 1, 2, 3 }, sim.State.Structures.ToArray().Select(s => s.HitsToBreak));
            double hx = sim.State.Hero.X, hy = sim.State.Hero.Y;

            int cursor = sim.EventCount;
            for (int k = 0; k < 4; k++) sim.Structures.Strike(sim, sim.Now, hx, hy, 40, StructureHit.Strike, 1);
            var glints = Since(sim, cursor, SimEventKind.Glint).Select(e => e.AsGlint).ToList();
            Assert.AreEqual(12, glints.Count, "an ordinary strike glints every structure in reach");
            Assert.IsTrue(glints.All(g => g.Hits == 0 && g.Source == StructureHit.Strike));
            Assert.IsTrue(sim.State.Structures.ToArray().All(s => s.Standing && s.Hits == 0), "and never breaks one");
            Assert.IsFalse(sim.Arena.IsOpen(26, 20));

            var expected = new[] { new[] { 0 }, new[] { 1 }, new[] { 2 } };
            for (int k = 0; k < 3; k++)
            {
                cursor = sim.EventCount;
                Slam(sim, hx, hy, 40);
                var broke = Since(sim, cursor, SimEventKind.StructureBreak).Select(e => e.AsStructureBreak.Structure);
                CollectionAssert.AreEqual(expected[k], broke, $"breaking hit {k + 1}");
                foreach (var g in Since(sim, cursor, SimEventKind.Glint).Select(e => e.AsGlint))
                    Assert.AreEqual(k + 1, g.Hits, "a large structure short of its break glints with its count");
            }
            Assert.IsTrue(sim.State.Structures.ToArray().All(s => !s.Standing));
            Assert.IsTrue(sim.Arena.IsOpen(26, 20) && sim.Arena.IsOpen(21, 27) && sim.Arena.IsOpen(15, 13), "broken tiles open to the hero");
        }

        [Test]
        public void TheHeavyStrikeAndItsCleaveBreakStructuresAndOtherStrikesGlint()
        {
            // A: small, 3.75 m east of the hero (in an ordinary strike's 7.04 m). B: large (two hits),
            // 8.25 m east (in the heavy strike's 9.15 m and its wave's). C: small, 12.75 m north (in the wave only).
            var sim = Start(Arena(Block(23, 20), Block(26, 19, 2, 3, large: true), Block(20, 29)), Plain(120, 20), sword: 1);
            var beats = new FixtureSong(3).Beats(0, 20, 120).BeatTimes();
            foreach (int b in new[] { 10, 20 })
                sim.AddKnown(StreamEntry.Forecast(beats[b], 0, 4, 0.7), beats[b] - 2);
            sim.StepTo(20);

            var heavies = Drive.Events(sim, SimEventKind.Attack).Select(e => e.AsAttack).Where(a => a.Attacker == -1 && a.Heavy)
                .Select(a => a.Impact).ToList();
            CollectionAssert.AreEqual(new[] { beats[10], beats[20] }, heavies);
            var breaks = Drive.Events(sim, SimEventKind.StructureBreak).Select(e => e.AsStructureBreak).ToList();
            var glints = Drive.Events(sim, SimEventKind.Glint).Select(e => e.AsGlint).ToList();

            // A glints on every ordinary strike before the accent, and shatters on it from the hero's side.
            var before = glints.Where(g => g.Structure == 0).ToList();
            Assert.Greater(before.Count, 5);
            Assert.IsTrue(before.All(g => g.Source == StructureHit.Strike && g.Hits == 0 && g.T < beats[10]));
            var a = breaks.Single(x => x.Structure == 0);
            Assert.AreEqual(beats[10], a.T);
            Assert.AreEqual(StructureHit.HeavyStrike, a.Source);
            Assert.AreEqual(4.5, a.X, 1e-9, "the strike point: the structure's nearest point to the hero");
            Assert.AreEqual(0.75, a.Y, 1e-9);
            Assert.AreEqual(1, a.DirX, 1e-9);
            Assert.AreEqual(0, a.DirY, 1e-9);
            Assert.Greater(a.Force, 0);

            // B takes one hit from the first heavy strike and its wave together, and breaks on the second.
            var bHits = glints.Where(g => g.Structure == 1).ToList();
            Assert.AreEqual(1, bHits.Count);
            Assert.AreEqual((beats[10], StructureHit.HeavyStrike, 1), (bHits[0].T, bHits[0].Source, bHits[0].Hits));
            var b2 = breaks.Single(x => x.Structure == 1);
            Assert.AreEqual((beats[20], StructureHit.HeavyStrike), (b2.T, b2.Source));

            // C breaks when the first wave's band reaches it.
            var cleave = Drive.Events(sim, SimEventKind.Cleave).Select(e => e.AsCleave).First();
            var c = breaks.Single(x => x.Structure == 2);
            Assert.AreEqual(StructureHit.Cleave, c.Source);
            double k = Math.Round((c.T - cleave.FirstBand) / cleave.BandSeconds);
            Assert.AreEqual(cleave.FirstBand + k * cleave.BandSeconds, c.T, 1e-9, "on one of the wave's bands");
            Assert.Less(c.T, beats[20]);
            Assert.IsFalse(glints.Any(g => g.Structure == 2), "out of the strikes' reach, no glint");
        }

        [Test]
        public void TheHeroCannotEnterAStandingStructureButCanOnceItBreaks()
        {
            var sim = Start(Arena(Block(21, 20)), Plain(120, 10));
            sim.StepTo(4);
            sim.Press(Key.Right, sim.Now);
            sim.StepTo(4.6);
            sim.Release(Key.Right, sim.Now);
            var bump = Drive.Events(sim, SimEventKind.HeroStep).Select(e => e.AsHeroStep).Last();
            Assert.IsTrue(bump.Bump);
            Assert.AreEqual(20, sim.State.Hero.TileX);

            SlamTile(sim, 21, 20);
            sim.Press(Key.Right, sim.Now);
            sim.Release(Key.Right, sim.Now);
            sim.StepTo(5.2);
            Assert.AreEqual(21, sim.State.Hero.TileX, "the broken structure's tile is open");
        }

        [Test]
        public void AfterABreakEnemiesPathThroughTheGap()
        {
            // A wall of small structures 6 m east of the hero, 29 tiles long; a ghost beyond it.
            var wall = Enumerable.Range(6, 29).Select(y => Block(24, y)).ToArray();
            double Run(bool breakGap, out Simulation sim)
            {
                sim = Start(Arena(wall), Plain(128, 30));
                sim.StepTo(2);
                int id = HordeFixture.Place(sim, "ghost", sim.Arena.CentreX(28), sim.Arena.CentreY(20));
                sim.StepTo(4);
                // Three structures facing the hero, whichever way the ghost has gone round.
                if (breakGap) Slam(sim, sim.Arena.CentreX(24), sim.Arena.CentreY(20), 1.6);
                sim.StepTo(12);
                var e = sim.State.Enemies[id];
                return Math.Sqrt(Math.Pow(e.X - sim.State.Hero.X, 2) + Math.Pow(e.Y - sim.State.Hero.Y, 2));
            }
            double around = Run(false, out _);
            double through = Run(true, out var broken);
            Assert.AreEqual(3, Drive.Events(broken, SimEventKind.StructureBreak).Count);
            Assert.Greater(around, 4, "with the wall whole the ghost is still going round it");
            Assert.Less(through, 1.5, "through the gap, the ghost reached the hero");
            foreach (var s in Drive.Events(broken, SimEventKind.EnemyStep).Select(e => e.AsEnemyStep))
                Assert.AreEqual(1.0, broken.Horde.Nav.Reach(s.FromX, s.FromY, s.ToX, s.ToY), "no step through a standing structure");
        }

        // ---------------------------------------------------------------- zone changes

        [Test]
        public void ZoneChangesLandOnDownbeatsFromKnownInTimeAndLateChanges()
        {
            var known = Plain(120, 50);
            // A change known before it happens; a loud jump known before; a change known 2 s after
            // it; one 8 s too soon after that; a jump too soft to count.
            known.Add(new KnownEntry(StreamEntry.Change(10.1, 0, 0.8), 9.0));
            known.Add(new KnownEntry(StreamEntry.Jump(20.0, 0, 5.0), 18.0));
            known.Add(new KnownEntry(StreamEntry.Change(25.0, 0, 0.8), 27.0));
            known.Add(new KnownEntry(StreamEntry.Change(31.0, 0, 0.8), 33.0));
            known.Add(new KnownEntry(StreamEntry.Jump(40.0, 0, 6.5), 38.0));
            var sim = Start(Arena(), known);
            sim.StepTo(50);
            var downbeats = Downbeats(sim);
            var changes = ZoneChanges(sim);
            CollectionAssert.AreEqual(new[] { FirstAtOrAfter(downbeats, 10.1), FirstAtOrAfter(downbeats, 27.0), FirstAtOrAfter(downbeats, 40.0) },
                changes.Select(z => z.T));
            CollectionAssert.AreEqual(new[] { ZoneCause.Change, ZoneCause.Change, ZoneCause.Jump }, changes.Select(z => z.Cause));
            CollectionAssert.AreEqual(new[] { 1, 2, 3 }, changes.Select(z => z.Zone));
            CollectionAssert.AreEqual(new[] { 0, 1, 2 }, changes.Select(z => z.Previous));
            Assert.AreEqual((25.0, 27.0), (changes[1].CauseT, changes[1].KnownAt));
            Assert.AreEqual(3, sim.State.Zone);
        }

        [Test]
        public void TheFallbackChangesZoneEveryMinuteForFiveMinutesThenEveryFour()
        {
            var sim = Start(Arena(), Plain(128, 800, from: 0.1));
            sim.StepTo(800);
            var downbeats = Downbeats(sim);
            var expected = new List<double>();
            double last = 0;
            while (true)
            {
                double due = last + (last < 300 ? 60 : 240);
                if (due > downbeats.Last()) break;
                last = FirstAtOrAfter(downbeats, due);
                Assert.Greater(Math.Abs(last - 300), 0.05, "no landing so close to five minutes that the run clock's tick decides it");
                expected.Add(last);
            }
            var changes = ZoneChanges(sim);
            Assert.AreEqual(7, expected.Count);
            CollectionAssert.AreEqual(expected, changes.Select(z => z.T));
            Assert.IsTrue(changes.All(z => z.Cause == ZoneCause.Fallback));
            CollectionAssert.AreEqual(new[] { 1, 2, 3, 4, 0, 1, 2 }, changes.Select(z => z.Zone), "five zones, cycling");
        }

        [Test]
        public void WithNoDownbeatTheFallbackLandsAfterItsGrace()
        {
            var sim = Start(Arena(), Drive.Known(new FixtureSong(3).Gap(0, 70).Entries()));
            sim.StepTo(70);
            var change = ZoneChanges(sim).Single();
            Assert.AreEqual(ZoneCause.Fallback, change.Cause);
            Assert.AreEqual(60, change.CauseT, 1e-9);
            Assert.AreEqual(60 + Zones.FallbackGraceS, change.T, Simulation.TickSeconds);
        }

        // ---------------------------------------------------------------- regrowth

        [Test]
        public void BrokenStructuresRegrowOnTheDownbeatAfterAZoneChangeOverOneBeat()
        {
            var known = Plain(120, 30);
            known.Add(new KnownEntry(StreamEntry.Change(10.1, 0, 0.8), 9.0));
            var sim = Start(Arena(Block(26, 20), Block(26, 24)), known);
            sim.StepTo(3);
            SlamTile(sim, 26, 20);
            sim.StepTo(30);
            var downbeats = Downbeats(sim);
            double zone = ZoneChanges(sim).Single().T;
            double next = downbeats.First(t => t > zone + 1e-9);
            var regrowth = Drive.Events(sim, SimEventKind.Regrowth).Select(e => e.AsRegrowth).Single();
            Assert.AreEqual(0, regrowth.Structure, "only the broken structure regrows");
            Assert.AreEqual(next, regrowth.T);
            Assert.AreEqual(next, regrowth.Start);
            Assert.AreEqual(next + 0.5, regrowth.End, 1e-9, "over one beat");
            var s = sim.State.Structures[0];
            Assert.IsTrue(s.Standing);
            Assert.AreEqual((next, next + 0.5), (s.GrowStart, s.GrowEnd));
            Assert.IsFalse(sim.Arena.IsOpen(26, 20), "it blocks its tile again");
        }

        [Test]
        public void ASiteWithTheHeroWithinThreeMetresWaitsForTheNextZoneChange()
        {
            var known = Plain(120, 40);
            known.Add(new KnownEntry(StreamEntry.Change(10.1, 0, 0.8), 9.0));
            known.Add(new KnownEntry(StreamEntry.Change(25.1, 0, 0.8), 24.0));
            // Near: 2.25 m from the hero's centre. Clear: 3.75 m.
            var sim = Start(Arena(Block(22, 20), Block(20, 23)), known);
            sim.StepTo(3);
            SlamTile(sim, 22, 20);
            SlamTile(sim, 20, 23);
            sim.StepTo(20);
            var first = Drive.Events(sim, SimEventKind.Regrowth).Select(e => e.AsRegrowth).ToList();
            CollectionAssert.AreEqual(new[] { 1 }, first.Select(r => r.Structure), "the clear site regrows, the near one waits");
            Assert.IsFalse(sim.State.Structures[0].Standing);

            HordeFixture.PutHero(sim, sim.State.Hero.X - 6, sim.State.Hero.Y);
            sim.StepTo(40);
            var regrowths = Drive.Events(sim, SimEventKind.Regrowth).Select(e => e.AsRegrowth).ToList();
            double second = ZoneChanges(sim)[1].T;
            Assert.AreEqual(2, regrowths.Count);
            Assert.AreEqual(0, regrowths[1].Structure);
            Assert.AreEqual(Downbeats(sim).First(t => t > second + 1e-9), regrowths[1].T, "on the downbeat after the next change");
        }

        [Test]
        public void EnemiesOnARegrowingSiteArePushedOut()
        {
            var known = Plain(120, 20);
            known.Add(new KnownEntry(StreamEntry.Change(10.1, 0, 0.8), 9.0));
            var sim = Start(Arena(Block(26, 19, 3, 3)), known);
            sim.StepTo(3);
            SlamTile(sim, 27, 20);
            while (ZoneChanges(sim).Count == 0) sim.StepTo(sim.Now + 0.1);
            var zone = ZoneChanges(sim).Single();
            var beats = sim.Stream.Beats.ToArray().Select(b => b.T).ToList();
            double regrow = beats[beats.IndexOf(zone.T) + 2];
            // After the beat's decisions, so it can't step off by itself.
            sim.StepTo(regrow - 0.05);
            int id = HordeFixture.Place(sim, "skeleton", sim.Arena.CentreX(27) + 0.2, sim.Arena.CentreY(20) - 0.1);
            int cursor = sim.EventCount;
            sim.StepTo(regrow + 0.5);
            Assert.AreEqual(regrow, Since(sim, cursor, SimEventKind.Regrowth).Single().T);
            var push = Since(sim, cursor, SimEventKind.EnemyKnock).Select(e => e.AsEnemyKnock).Single(k => k.Enemy == id);
            Assert.AreEqual(regrow, push.T);
            Assert.IsTrue(sim.Horde.Nav.Walkable(push.ToX, push.ToY), "out to open ground");
            Assert.Less(Math.Abs(push.ToX - sim.Arena.CentreX(27)), 3.0, "the nearest open ground, just off the site");
            var e = sim.State.Enemies[id];
            Assert.IsTrue(sim.Horde.Nav.Walkable(e.X, e.Y));
        }

        // ---------------------------------------------------------------- a real dump

        [Test]
        public void BreaksAndRegrowthsLandAtTheirTimesOnARealDump()
        {
            var entries = EntryDump.Load(Accelerando, out double duration);
            var sim = new Simulation(HordeFixture.Config(heroStands: true, sword: 1, arena: HordeFixture.GameArena()));
            Drive.Feed(sim, entries, duration);
            var downbeats = Downbeats(sim);
            var heavies = Drive.Events(sim, SimEventKind.Attack).Select(e => e.AsAttack).Where(a => a.Attacker == -1 && a.Heavy)
                .Select(a => a.Impact).ToList();
            var bands = Drive.Events(sim, SimEventKind.Cleave).Select(e => e.AsCleave)
                .SelectMany(c => Enumerable.Range(0, c.Bands).Select(k => c.FirstBand + k * c.BandSeconds)).ToList();
            var zones = ZoneChanges(sim);
            var breaks = Drive.Events(sim, SimEventKind.StructureBreak).Select(e => e.AsStructureBreak).ToList();
            var regrowths = Drive.Events(sim, SimEventKind.Regrowth).Select(e => e.AsRegrowth).ToList();

            foreach (var z in zones)
                TestContext.Out.WriteLine($"zone {z.Previous} -> {z.Zone} at {z.T:F3} ({z.Cause} at {z.CauseT:F3}, known {z.KnownAt:F3})");
            foreach (var b in breaks)
                TestContext.Out.WriteLine($"break {b.Structure} at {b.T:F3} by {b.Source}, point ({b.X:F2}, {b.Y:F2})");
            foreach (var r in regrowths)
                TestContext.Out.WriteLine($"regrowth {r.Structure} at {r.T:F3} to {r.End:F3}");

            Assert.GreaterOrEqual(zones.Count, 2);
            foreach (var z in zones)
                Assert.AreEqual(FirstAtOrAfter(downbeats, Math.Max(z.CauseT, z.KnownAt)), z.T, "a zone change lands on its downbeat");
            Assert.Greater(breaks.Count, 0);
            foreach (var b in breaks)
                Assert.IsTrue(b.Source == StructureHit.HeavyStrike ? heavies.Contains(b.T) : bands.Contains(b.T),
                    $"break at {b.T} on a heavy impact or a cleave band");
            Assert.Greater(regrowths.Count, 0);
            foreach (var r in regrowths)
            {
                double zone = zones.Last(z => z.T < r.T).T;
                Assert.AreEqual(downbeats.First(t => t > zone + 1e-9), r.T, "on the downbeat after the zone change");
                Assert.Less(breaks.Where(b => b.Structure == r.Structure && b.T < r.T).Max(b => b.T), zone, "broken before the zone change");
            }
        }
    }
}
