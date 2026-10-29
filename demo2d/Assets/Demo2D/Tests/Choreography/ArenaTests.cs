using System;
using System.Collections.Generic;
using System.Numerics;
using NUnit.Framework;
using PS.Choreography;

namespace Demo2D.Choreography.Tests
{
    /// <summary>
    /// PX6's arena: it generates deterministically from its JSON, its scatter keeps out of the water,
    /// the cliffs and the fighting ground, and the song's timeline keeps the hero and every monster
    /// out of the trees, the cliff and the stream, crossing the stream only by its bridge.
    /// </summary>
    public class ArenaTests
    {
        const float WallTolerance = 0.05f;

        static ForestArena Arena => Fixtures.Arena;

        static string Report(List<string> problems) =>
            problems.Count + " problems, first: " + string.Join("; ", problems.GetRange(0, Math.Min(5, problems.Count)));

        [Test]
        public void TheSongRunsOnTheArena()
        {
            Assert.That(Fixtures.Level.Layout, Is.EqualTo(ChoreographyFiles.ArenaLayout));
            Assert.That(Fixtures.Data.Layout.Nav, Is.Not.Null);
        }

        /// <summary>Two generations from the JSON give the same placements, ground and tiles.</summary>
        [Test]
        public void TheArenaGeneratesDeterministically()
        {
            ForestArena a = ChoreographyFiles.LoadArena(Fixtures.Root), b = ChoreographyFiles.LoadArena(Fixtures.Root);
            Assert.That(b.Placements.ToJson() == a.Placements.ToJson(), Is.True, "two placement lists differ");
            Assert.That(b.GroundCorners(), Is.EqualTo(a.GroundCorners()));
            TileSheet sheet = ChoreographyFiles.LoadTileSheet(Fixtures.Root);
            ArenaTiles ta = ArenaTiles.Compose(a, sheet), tb = ArenaTiles.Compose(b, sheet);
            Assert.That(tb.Cells, Is.EqualTo(ta.Cells));
            for (int i = 0; i < ta.Layers.Length; i++) Assert.That(tb.Layers[i], Is.EqualTo(ta.Layers[i]), ta.LayerNames[i]);
            TestContext.WriteLine(a.Placements.Items.Length + " placements (" + a.Placements.BlockingCount + " blocking), " + ta.Cells.Count + " distinct tiles");
        }

        /// <summary>
        /// Scatter never lands in water or on a cliff, nothing that blocks is scattered on fighting
        /// ground, every prop has a sprite, every level-script goal is a named area clear of obstacles, and
        /// every material is drawn somewhere.
        /// </summary>
        [Test]
        public void TheArenaFollowsItsLayoutRules()
        {
            PlacementList list = Arena.Placements;
            var problems = new List<string>();
            for (int i = 0; i < Arena.TerrainStart; i++)
            {
                Placement p = list.Items[i];
                Footprint f = list.Props[p.Prop];
                if (!Arena.Sprites.ContainsKey(f.Id)) problems.Add(f.Id + " has no sprite");
                if (p.Area >= 0) continue;
                if (Arena.TerrainDistance(p.Position) < f.BoundingRadius) problems.Add(f.Id + " at " + p.Position + " lies in water or on a cliff");
                if (f.Blocks && Arena.Layout.FightingDistance(p.Position, out _) < f.BoundingRadius) problems.Add(f.Id + " at " + p.Position + " blocks fighting ground");
            }
            foreach (LevelSection s in Fixtures.Level.Sections)
            {
                if (s.Goal == null) continue;
                GoalArea g = Array.Find(list.Goals, x => x.Name == s.Goal);
                if (g == null) problems.Add("level-script goal " + s.Goal + " is not a named area");
                else if (Fixtures.Data.Layout.Nav.Distance(g.Center) < Fixtures.Data.Roster.HeroRadius) problems.Add("goal " + s.Goal + " lies in an obstacle");
            }
            byte[] corners = Arena.GroundCorners();
            for (int m = 0; m <= Arena.Materials.Length; m++)
                if (Array.IndexOf(corners, (byte)m) < 0) problems.Add("ground " + (m == 0 ? "base" : Arena.Materials[m - 1].name) + " is never drawn");
            Assert.That(problems, Is.Empty, Report(problems));
        }

        /// <summary>The cliff and the stream block, the bridge's deck lets the hero through, and the tiles draw them where they block.</summary>
        [Test]
        public void TerrainBlocksWhereItIsDrawn()
        {
            NavGrid nav = Fixtures.Data.Layout.Nav;
            ArenaTiles tiles = ArenaTiles.Compose(Arena, ChoreographyFiles.LoadTileSheet(Fixtures.Root));
            int terrain = Array.IndexOf(tiles.LayerNames, "terrain"), bridges = Array.IndexOf(tiles.LayerNames, "bridges");
            int drawn = 0, expected = 0;
            foreach (int t in tiles.Layers[terrain]) if (t != ArenaTiles.None) drawn++;
            foreach (Cliff c in Arena.Cliffs)
            {
                expected += c.Rect.W * c.Rect.H;
                Assert.That(nav.Distance(c.Rect.Center), Is.LessThan(0), "cliff " + c.Name + " must block");
            }
            foreach (WaterChannel s in Arena.Streams)
            {
                expected += s.Rect.W * s.Rect.H;
                Assert.That(nav.Distance(new Vector2(s.Rect.X0 + 0.5f, s.Rect.Center.Y)), Is.LessThan(0), "water " + s.Name + " must block");
                foreach (int deck in s.Bridges)
                {
                    var mid = new Vector2(deck + ForestArena.BridgeDeckM / 2f, s.Rect.Center.Y);
                    Assert.That(nav.Distance(mid), Is.GreaterThanOrEqualTo(Fixtures.Data.Roster.HeroRadius), "the bridge at " + deck + " must let the hero over");
                    int x = deck - Arena.OriginX, y = (int)mid.Y - Arena.OriginY;
                    Assert.That(tiles.Layers[bridges][y * tiles.W + x], Is.Not.EqualTo(ArenaTiles.None), "the bridge at " + deck + " must be drawn");
                }
            }
            Assert.That(drawn, Is.EqualTo(expected), "terrain tiles cover exactly the cliffs and the water");
        }

        /// <summary>
        /// No monster and not the hero stands in a tree, a cliff or the stream: at every beat and a
        /// tenth and a fifth of a second after it, no one's footprint reaches more than 5 cm into a
        /// blocking footprint (the sim's obstacle map: trunks, stumps, fallen branches, the cliff and
        /// the stream's channel).
        /// </summary>
        [Test]
        public void NobodyStandsInATreeTheCliffOrTheStream()
        {
            Timeline tl = Fixtures.SongTimeline;
            NavGrid nav = Fixtures.Data.Layout.Nav;
            float heroR = Fixtures.Data.Roster.HeroRadius;
            var problems = new List<string>();
            double worst = 0;
            long samples = 0;
            foreach (double beat in tl.Map.Beats)
                foreach (double off in new[] { 0.0, 0.1, 0.2 })
                {
                    double t = beat + off;
                    if (t > tl.Duration) continue;
                    for (int e = 0; e < tl.EntityCount; e++)
                    {
                        EntityInfo info = tl.Entities[e];
                        if (info.Kind == EntityKind.Projectile || t < info.Spawn || t >= info.Despawn) continue;
                        float r = info.Kind == EntityKind.Hero ? heroR : tl.TypeRadii[info.Type];
                        Vector2 p = tl.PositionAt(e, t);
                        float into = r - nav.Distance(p);
                        samples++;
                        if (into > worst) worst = into;
                        if (into > WallTolerance && problems.Count < 50)
                            problems.Add((info.Kind == EntityKind.Hero ? "the hero" : tl.TypeNames[info.Type] + " " + e) + " stands " + into.ToString("F3") + " m into an obstacle at " + t.ToString("F2") + " s (" + p + ")");
                    }
                }
            TestContext.WriteLine(samples + " samples, deepest " + worst.ToString("F3") + " m inside a radius");
            Assert.That(samples, Is.GreaterThan(100000));
            Assert.That(problems, Is.Empty, Report(problems));
        }

        /// <summary>The hero walks the route: over the stream by its bridge, into the great clearing before the final peak.</summary>
        [Test]
        public void TheHeroCrossesByTheBridgeAndReachesTheGreatClearing()
        {
            Timeline tl = Fixtures.SongTimeline;
            WaterChannel stream = Arena.Streams[0];
            bool crossed = false;
            var problems = new List<string>();
            for (double t = 0; t < tl.Duration; t += 0.05)
            {
                Vector2 h = tl.PositionAt(0, t);
                if (h.Y > stream.Rect.Y1) crossed = true;
                if (h.Y < stream.Rect.Y0 || h.Y > stream.Rect.Y1 || h.X < stream.Rect.X0 || h.X > stream.Rect.X1) continue;
                if (!Array.Exists(stream.Bridges, d => h.X >= d && h.X <= d + ForestArena.BridgeDeckM) && problems.Count < 10)
                    problems.Add("the hero is in the stream off its bridge at " + t.ToString("F2") + " s (" + h + ")");
            }
            Assert.That(crossed, Is.True, "the hero never crosses the stream");
            Assert.That(problems, Is.Empty, Report(problems));
            SectionStats drop2 = Array.Find(tl.Sections, s => s.Goal == "great-clearing");
            SectionStats peak = Array.Find(tl.Sections, s => s.Label == "final-peak");
            TestContext.WriteLine("great clearing reached at " + drop2.GoalReachedAt.ToString("F1") + " s; the final peak starts at " + peak.Start.ToString("F1") + " s");
            Assert.That(drop2.GoalReachedAt, Is.InRange(0, peak.Start));
        }
    }
}
