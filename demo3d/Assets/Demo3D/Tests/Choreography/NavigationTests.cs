using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using NUnit.Framework;
using PS.Choreography;

namespace Demo3D.Choreography.Tests
{
    /// <summary>
    /// TD6's checks (windows.md, "Automated Checks"): the footprints and the placement list, the flow
    /// field against a reference shortest-path search, nobody inside a wall in any timeline, and on
    /// the courtyard test layout every monster with a path reaching the hero, none stuck for more than
    /// 4 beats, and blocked monsters marking time in place instead of jittering.
    /// </summary>
    public class NavigationTests
    {
        const float WallTolerance = 0.05f;

        static string Report(List<string> problems) =>
            problems.Count + " problems, first: " + string.Join("; ", problems.GetRange(0, Math.Min(5, problems.Count)));

        // ------------------------------------------------------------------ footprints and placements

        /// <summary>Every kit piece in the manifest has a footprint under its own GUID; what blocks has a shape, and small or soft pieces never block.</summary>
        [Test]
        public void EveryKitPieceHasAFootprint()
        {
            FootprintTable fp = ChoreographyFiles.LoadFootprints(Fixtures.Root);
            JsonValue manifest = JsonValue.Parse(File.ReadAllText(Path.Combine(Fixtures.Root, "demo3d", "manifest.json")));
            var guids = new Dictionary<string, Footprint>();
            foreach (Footprint f in fp.All) guids[f.Guid] = f;
            int kit = 0;
            var problems = new List<string>();
            JsonValue assets = manifest["assets"];
            for (int i = 0; i < assets.Count; i++)
            {
                string use = assets[i]["use"].AsString("");
                if (!use.StartsWith("kit", StringComparison.Ordinal)) continue;
                kit++;
                if (!guids.TryGetValue(assets[i]["guid"].AsString(), out Footprint f)) { problems.Add("no footprint for " + assets[i]["path"].AsString()); continue; }
                if (f.Blocks && f.Shape == FootprintShape.None) problems.Add(f.Id + " blocks without a shape");
                if (f.Blocks && f.HeightM < 0.5f) problems.Add(f.Id + " blocks at " + f.HeightM + " m tall");
                if (f.Blocks && (f.Id.Contains("_Grass_") || f.Id.Contains("_Flowers_") || f.Id.Contains("_Fern_"))) problems.Add(f.Id + " is soft but blocks");
            }
            Assert.That(kit, Is.GreaterThan(0));
            Assert.That(fp.All.Length, Is.EqualTo(kit), "footprints for pieces no longer in the manifest");
            Assert.That(problems, Is.Empty, Report(problems));
        }

        [Test]
        public void PlacementListIsDeterministic()
        {
            string a = ChoreographyFiles.LoadPlacements(Fixtures.Root).ToJson();
            string b = ChoreographyFiles.LoadPlacements(Fixtures.Root).ToJson();
            Assert.That(b == a, Is.True, "two generations differ");
            Assert.That(ChoreographyFiles.LoadPlacements(Fixtures.Root, density: 2f * ChoreographyFiles.LoadLayout(Fixtures.Root).DecorationDensity).ToJson() == a, Is.False);
        }

        /// <summary>
        /// The layout's rules: fighting ground carries only pieces that do not block (apart from the
        /// placed landmarks and structures); forest canopies never overhang it; every level-script goal
        /// is a named area; and the density multiplier scales edge bands and forest only.
        /// </summary>
        [Test]
        public void PlacementListFollowsTheLayoutRules()
        {
            ArenaLayout layout = ChoreographyFiles.LoadLayout(Fixtures.Root);
            PlacementList list = Fixtures.Data.Layout.Placements;
            var problems = new List<string>();
            var counts = new Dictionary<AreaType, int>();
            foreach (Placement p in list.Items)
            {
                Footprint f = list.Props[p.Prop];
                if (p.Area >= 0) continue;   // landmark and structure pieces are placed explicitly
                float d = layout.FightingDistance(p.Position, out AreaType fight);
                AreaType t = d <= 0 ? fight : layout.Classify(p.Position) ?? AreaType.Landmark;
                counts[t] = (counts.TryGetValue(t, out int c) ? c : 0) + 1;
                if (f.Blocks && d < f.BoundingRadius * p.Scale) problems.Add(f.Id + " at " + p.Position + " reaches fighting ground");
                if (t == AreaType.Forest && d < f.CanopyRadiusM * p.Scale - 1e-3f) problems.Add(f.Id + "'s canopy at " + p.Position + " overhangs fighting ground");
            }
            foreach (LevelSection s in Fixtures.Level.Sections)
                if (s.Goal != null && Array.Find(list.Goals, g => g.Name == s.Goal) == null)
                    problems.Add("level-script goal " + s.Goal + " is not a named area");
            TestContext.WriteLine("scatter: " + string.Join(", ", Describe(counts)) + "; " + list.Items.Length + " placements, " + list.BlockingCount + " blocking");
            Assert.That(counts.ContainsKey(AreaType.Forest) && counts.ContainsKey(AreaType.Edge) && counts.ContainsKey(AreaType.Clearing), Is.True);
            Assert.That(problems, Is.Empty, Report(problems));

            PlacementList dense = ChoreographyFiles.LoadPlacements(Fixtures.Root, density: 2f * ChoreographyFiles.LoadLayout(Fixtures.Root).DecorationDensity);
            var dcounts = new Dictionary<AreaType, int>();
            foreach (Placement p in dense.Items)
            {
                if (p.Area >= 0) continue;
                float d = layout.FightingDistance(p.Position, out AreaType fight);
                AreaType t = d <= 0 ? fight : layout.Classify(p.Position) ?? AreaType.Landmark;
                dcounts[t] = (dcounts.TryGetValue(t, out int c) ? c : 0) + 1;
            }
            TestContext.WriteLine("at density 2: " + string.Join(", ", Describe(dcounts)));
            Assert.That(dcounts[AreaType.Clearing], Is.EqualTo(counts[AreaType.Clearing]), "clearings must not change with density");
            Assert.That(dcounts[AreaType.Path], Is.EqualTo(counts[AreaType.Path]), "paths must not change with density");
            // Doubling the authored density adds at least a quarter more edge and forest (1x to 2x adds
            // more than 1.5x; from 2x, TD15's authored density, the forest starts to fill: 2x to 4x adds 1.36x).
            Assert.That(dcounts[AreaType.Forest] + dcounts[AreaType.Edge], Is.GreaterThan(1.25 * (counts[AreaType.Forest] + counts[AreaType.Edge])));
        }

        static IEnumerable<string> Describe(Dictionary<AreaType, int> counts)
        {
            foreach (KeyValuePair<AreaType, int> kv in counts) yield return kv.Key + " " + kv.Value;
        }

        /// <summary>
        /// The courtyard's walls are continuous apart from its gaps (no slit a monster could see
        /// through), it has the gaps its parameters give, and each is wider than the largest monster's
        /// diameter plus 1 m (choreography.md, "Room for every size").
        /// </summary>
        [TestCase("arena", "courtyard")]
        [TestCase("courtyard-test", "court")]
        public void CourtyardHasItsGapsAndNoSlits(string layoutName, string area)
        {
            ChoreographyData data = layoutName == "arena" ? Fixtures.Data : Fixtures.CourtyardData;
            LayoutArea a = data.Layout.Arena.Area(area);
            var walls = new WallGeometry(data.Layout.Placements);
            float need = 2 * data.Roster.MaxMonsterRadius + 1f;
            float hx = a.Size.X / 2, hy = a.Size.Y / 2;
            var corners = new[] { new Vector2(-hx, -hy), new Vector2(hx, -hy), new Vector2(hx, hy), new Vector2(-hx, hy) };
            int gaps = 0;
            var problems = new List<string>();
            for (int s = 0; s < 4; s++)
            {
                Vector2 A = a.Center + corners[s], B = a.Center + corners[(s + 1) & 3];
                float len = Vector2.Distance(A, B);
                // Walk the side 2 cm at a time; an open stretch is where no footprint lies within 5 cm.
                float open = 0;
                for (float u = 0.3f; u <= len - 0.3f + 1e-3f; u += 0.02f)
                {
                    bool free = walls.Distance(Vector2.Lerp(A, B, u / len)) > 0.05f;
                    if (free) open += 0.02f;
                    if (!free || u + 0.02f > len - 0.3f)
                    {
                        if (open >= need) gaps++;
                        else if (open > 0.1f) problems.Add("a " + open.ToString("F2") + " m slit on side " + s + " at " + u.ToString("F1") + " m");
                        open = 0;
                    }
                }
            }
            Assert.That(gaps, Is.EqualTo(a.Gaps), "gaps of at least " + need + " m");
            Assert.That(problems, Is.Empty, Report(problems));
        }

        [Test]
        public void ArenaLayoutsNeedFootprintsAndWideEnoughGaps()
        {
            string json = File.ReadAllText(ChoreographyFiles.DataPath(Fixtures.Root, "courtyard-test-layout.json"));
            Assert.Throws<FormatException>(() => GroundLayout.Parse(json));
            ArenaLayout l = ArenaLayout.Parse(json);
            l.Area("court").GapM = 3f;
            Assert.Throws<FormatException>(() => l.Validate(Fixtures.Data.Roster));
            Assert.That(GroundLayout.Parse(File.ReadAllText(ChoreographyFiles.DataPath(Fixtures.Root, "open-ground-layout.json"))).Placements, Is.Null);
        }

        // ------------------------------------------------------------------ the flow field

        static readonly string[] SmallLayouts =
        {
            // A ruin wall with a gap across a small yard.
            "{ \"name\": \"small-wall\", \"seed\": 1, \"size\": [24, 20], \"heroStart\": [0, -6], \"props\": { \"w\": [\"darkfantasy/SM_Bld_Wall_Ruin_01\"] },"
            + " \"areas\": [ { \"type\": \"clearing\", \"center\": [0, 0], \"radius\": 30 }, { \"type\": \"structure\", \"name\": \"wall\", \"kind\": \"ruin-wall\", \"from\": [-10, 0], \"to\": [10, 0], \"gaps\": 1, \"gapM\": 4.5, \"walls\": \"w\" } ] }",
            // A courtyard with three gaps, a tree and a rock.
            "{ \"name\": \"small-court\", \"seed\": 2, \"size\": [22, 22], \"heroStart\": [0, 0], \"props\": { \"w\": [\"darkfantasy/SM_Bld_Wall_Ruin_05\", \"darkfantasy/SM_Bld_Wall_Ruin_01\"] },"
            + " \"areas\": [ { \"type\": \"clearing\", \"center\": [0, 0], \"radius\": 30 }, { \"type\": \"structure\", \"name\": \"court\", \"kind\": \"courtyard\", \"center\": [0, 0], \"size\": [10, 10], \"gaps\": 3, \"gapM\": 4.2, \"walls\": \"w\" },"
            + " { \"type\": \"landmark\", \"at\": [7, 7], \"pieces\": [ { \"prop\": \"kingdom/SM_Env_Tree_Round_04\", \"at\": [0, 0] }, { \"prop\": \"kingdom/SM_Env_Rock_Chunk_02\", \"at\": [-14, 1] } ] } ] }",
            // Open ground: the field is the octile distance.
            "{ \"name\": \"small-open\", \"seed\": 3, \"size\": [16, 16], \"heroStart\": [1, 2], \"props\": {}, \"areas\": [ { \"type\": \"clearing\", \"center\": [0, 0], \"radius\": 30 } ] }",
        };

        static NavGrid SmallGrid(int i, out PlacementList list)
        {
            list = ArenaLayout.Parse(SmallLayouts[i]).Generate(ChoreographyFiles.LoadFootprints(Fixtures.Root));
            return new NavGrid(list);
        }

        /// <summary>
        /// The flow field's costs equal an independent Dijkstra search's (a sorted set, the same 10/14
        /// steps, open cells and no corner cutting) on every cell of small layouts, however the build
        /// is time-sliced, and from goal areas as from points.
        /// </summary>
        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public void FlowFieldMatchesAReferenceShortestPath(int layout)
        {
            NavGrid grid = SmallGrid(layout, out PlacementList list);
            foreach (float clearance in new[] { 0.4f, 0.55f })
            {
                bool[] open = Open(grid, clearance);
                foreach (int budget in new[] { 1, 7, 100, int.MaxValue })
                {
                    var f = new FlowField(grid, clearance);
                    f.Begin(list.HeroStart);
                    int slices = 0;
                    while (!f.Step(budget)) slices++;
                    int[] want = Reference(grid, open, new[] { grid.CellIndex(list.HeroStart) });
                    AssertSame(f.Cost, want, "budget " + budget + ", clearance " + clearance);
                    if (budget == 1) Assert.That(slices, Is.GreaterThan(100));
                }
                var area = new FlowField(grid, clearance);
                Vector2 c = list.HeroStart + new Vector2(1.3f, -0.7f);
                area.BeginArea(c, 1.2f);
                area.BuildAll();
                var sources = new List<int>();
                for (int i = 0; i < open.Length; i++) if (open[i] && Vector2.Distance(grid.CellCenter(i), c) <= 1.2f) sources.Add(i);
                AssertSame(area.Cost, Reference(grid, open, sources.ToArray()), "area, clearance " + clearance);
            }
        }

        /// <summary>On open ground the field's cost lies within the octile metric's bounds of the straight distance (14 for a diagonal step is 1% short of 10√2, and straight-then-diagonal is at most 8.2% long); walking its headings from any reached cell arrives at the source.</summary>
        [TestCase(1)]
        [TestCase(2)]
        public void FlowFieldHeadingsLeadToTheSource(int layout)
        {
            NavGrid grid = SmallGrid(layout, out PlacementList list);
            var f = new FlowField(grid, 0.4f);
            f.Begin(list.HeroStart);
            f.BuildAll();
            var problems = new List<string>();
            int walked = 0;
            for (int i = 0; i < f.Cost.Length; i += 7)
            {
                if (f.Cost[i] == FlowField.Unreached) continue;
                Vector2 p = grid.CellCenter(i);
                if (layout == 2)
                {
                    float straight = Vector2.Distance(p, grid.CellCenter(grid.CellIndex(list.HeroStart)));
                    float cost = f.Cost[i] * FlowField.MetresPerCost;
                    if (cost < 0.99f * straight - 1e-3f || cost > 1.0824f * straight + 1e-3f) problems.Add("cost " + cost + " for straight " + straight);
                }
                walked++;
                bool arrived = false;
                for (int step = 0; step < 400 && !arrived; step++)
                {
                    if (Vector2.Distance(p, list.HeroStart) < 0.75f) { arrived = true; break; }
                    if (!f.Direction(p, out Vector2 dir)) { arrived = f.Cost[grid.CellIndex(p)] == 0; break; }
                    p += dir * 0.2f;
                }
                if (!arrived) problems.Add("the walk from " + grid.CellCenter(i) + " never arrived (stopped at " + p + ")");
            }
            Assert.That(walked, Is.GreaterThan(50));
            Assert.That(problems, Is.Empty, Report(problems));
        }

        static bool[] Open(NavGrid g, float clearance)
        {
            var open = new bool[g.W * g.H];
            for (int y = 1; y < g.H - 1; y++)
                for (int x = 1; x < g.W - 1; x++)
                    open[y * g.W + x] = g.Clearance[y * g.W + x] > clearance;
            return open;
        }

        /// <summary>A plain Dijkstra search with a sorted set, independent of the layer's bucket queue.</summary>
        static int[] Reference(NavGrid g, bool[] open, int[] sources)
        {
            var cost = new int[g.W * g.H];
            for (int i = 0; i < cost.Length; i++) cost[i] = int.MaxValue;
            var queue = new SortedSet<(int, int)>();
            foreach (int s in sources)
                if (s >= 0 && open[s]) { cost[s] = 0; queue.Add((0, s)); }
            while (queue.Count > 0)
            {
                (int c, int n) = queue.Min;
                queue.Remove(queue.Min);
                int x = n % g.W, y = n / g.W;
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        if (dx == 0 && dy == 0) continue;
                        int nx = x + dx, ny = y + dy;
                        if (nx < 0 || ny < 0 || nx >= g.W || ny >= g.H) continue;
                        int m = ny * g.W + nx;
                        if (!open[m]) continue;
                        if (dx != 0 && dy != 0 && (!open[y * g.W + nx] || !open[ny * g.W + x])) continue;
                        int nc = c + (dx != 0 && dy != 0 ? 14 : 10);
                        if (nc >= cost[m]) continue;
                        if (cost[m] != int.MaxValue) queue.Remove((cost[m], m));
                        cost[m] = nc;
                        queue.Add((nc, m));
                    }
            }
            return cost;
        }

        static void AssertSame(int[] got, int[] want, string what)
        {
            int diff = 0, first = -1, reached = 0;
            for (int i = 0; i < got.Length; i++)
            {
                if (want[i] != int.MaxValue) reached++;
                if (got[i] != want[i]) { diff++; if (first < 0) first = i; }
            }
            Assert.That(reached, Is.GreaterThan(100), what);
            Assert.That(diff, Is.EqualTo(0), what + ": first at cell " + first + (first >= 0 ? " (" + got[first] + " against " + want[first] + ")" : ""));
        }

        // ------------------------------------------------------------------ walls in every timeline

        /// <summary>
        /// No monster or hero ever stands closer to a wall than its radius, beyond a small tolerance,
        /// sampled on every beat and 0.1 s and 0.2 s after it (while gait steps and knockbacks are in
        /// motion), by exact distances to the placement list's footprints computed here.
        /// </summary>
        [TestCaseSource(typeof(Fixtures), nameof(Fixtures.AllTimelines))]
        public void NobodyStandsInsideAWall(string name)
        {
            Timeline tl = Fixtures.Named(name);
            CheckWalls(tl, Fixtures.Data);
        }

        [TestCase("sparse")]
        [TestCase("dense")]
        public void NobodyStandsInsideAWallOnTheCourtyardLayout(string run) =>
            CheckWalls(run == "sparse" ? Fixtures.CourtyardSparse : Fixtures.CourtyardDense, Fixtures.CourtyardData);

        static void CheckWalls(Timeline tl, ChoreographyData data)
        {
            var walls = new WallGeometry(data.Layout.Placements);
            float heroR = data.Roster.HeroRadius;
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
                        float d = walls.Distance(p);
                        samples++;
                        float into = r - d;
                        if (into > worst) worst = into;
                        if (into > WallTolerance && problems.Count < 50)
                            problems.Add((info.Kind == EntityKind.Hero ? "the hero" : tl.TypeNames[info.Type] + " " + e) + " stands " + into.ToString("F3") + " m inside its radius of a wall at " + t.ToString("F2") + " (" + p + ")");
                    }
                }
            TestContext.WriteLine(tl.Name + ": " + samples + " samples, deepest " + worst.ToString("F3") + " m inside a radius");
            Assert.That(problems, Is.Empty, Report(problems));
        }

        // ------------------------------------------------------------------ the courtyard test layout

        /// <summary>
        /// Every monster with a path reaches the hero: in a sparse run on the courtyard test layout
        /// (monsters starting 22 m out, behind the courtyard's closed side, inside the U and beyond the
        /// long wall, the hero holding the courtyard and not attacking), every monster whose start the
        /// reference search gets inside the courtyard's walls, or within 6.5 m of the hero, with a clear line to it.
        /// </summary>
        [Test]
        public void EveryMonsterWithAPathReachesTheHero()
        {
            Timeline tl = Fixtures.CourtyardSparse;
            ChoreographyData data = Fixtures.CourtyardData;
            NavGrid grid = data.Layout.Nav;
            int[] geo = Reference(grid, Open(grid, 0.4f), new[] { grid.CellIndex(Vector2.Zero) });
            var problems = new List<string>();
            int withPath = 0, inU = 0, behindWall = 0, behindCourt = 0;
            double latest = 0;
            for (int e = 1; e < tl.EntityCount; e++)
            {
                EntityInfo info = tl.Entities[e];
                if (info.Kind != EntityKind.Monster) continue;
                Vector2 start = tl.PositionAt(e, info.Spawn);
                int c = grid.CellIndex(start);
                if (c < 0 || geo[c] == int.MaxValue) continue;
                withPath++;
                if (start.X > 12 && start.X < 24 && start.Y > 12 && start.Y < 24) inU++;
                if (start.X < -22) behindWall++;
                if (start.Y > 8 && Math.Abs(start.X) < 8) behindCourt++;
                double reached = -1;
                for (double t = info.Spawn; t < Math.Min(tl.Duration, info.Despawn); t += 0.25)
                {
                    Vector2 p = tl.PositionAt(e, t), h = tl.PositionAt(0, t);
                    if ((Vector2.Distance(p, h) <= 6.5f || InCourt(p)) && grid.LineClear(p, h)) { reached = t; break; }
                }
                if (reached < 0) problems.Add(tl.TypeNames[info.Type] + " " + e + " from " + start + " never reached the hero (ends at " + tl.PositionAt(e, tl.Duration - 0.01) + ")");
                latest = Math.Max(latest, reached);
            }
            TestContext.WriteLine(withPath + " monsters with a path (" + inU + " in the U, " + behindWall + " beyond the long wall, " + behindCourt + " behind the courtyard's closed side); the last reached the hero at " + latest.ToString("F1") + " s");
            Assert.That(inU, Is.GreaterThan(0));
            Assert.That(behindWall, Is.GreaterThan(0));
            Assert.That(behindCourt, Is.GreaterThan(0));
            Assert.That(problems, Is.Empty, Report(problems));
        }

        /// <summary>
        /// None stays stuck for more than 4 beats: until it reaches the hero, every monster's way to
        /// the hero (the reference search's cost) shrinks by at least 0.25 m over every 4 beats, unless
        /// it is in a crowd (another monster touches it at either end): then it is queueing or marking
        /// time, not stuck.
        /// </summary>
        [TestCase("sparse")]
        [TestCase("dense")]
        public void NoMonsterStaysStuckForMoreThanFourBeats(string run)
        {
            Timeline tl = run == "sparse" ? Fixtures.CourtyardSparse : Fixtures.CourtyardDense;
            NavGrid grid = Fixtures.CourtyardData.Layout.Nav;
            // The way, by the reference search over cells with 0.3 m of clearance, so even the smallest
            // monster squeezed against a wall stands on a reached cell.
            int[] geo = Reference(grid, Open(grid, 0.3f), new[] { grid.CellIndex(Vector2.Zero) });
            float Way(Vector2 p)
            {
                int c = grid.CellIndex(p);
                return c < 0 || geo[c] == int.MaxValue ? float.NaN : geo[c] * FlowField.MetresPerCost;
            }
            double[] beats = tl.Map.Beats;
            var problems = new List<string>();
            int windows = 0, queueing = 0;
            for (int b = 4; b + 4 < beats.Length; b++)
            {
                double t0 = beats[b], t1 = beats[b + 4];
                var alive = new List<int>();
                var pos = new List<Vector2>();
                for (int e = 1; e < tl.EntityCount; e++)
                {
                    EntityInfo info = tl.Entities[e];
                    if (info.Kind != EntityKind.Monster || info.Spawn > t0 || info.Despawn <= t1 || Died(tl, e, t1)) continue;
                    alive.Add(e);
                    pos.Add(tl.PositionAt(e, t0));
                }
                Vector2 h = tl.PositionAt(0, t0);
                for (int i = 0; i < alive.Count; i++)
                {
                    int e = alive[i];
                    float w0 = Way(pos[i]);
                    if (float.IsNaN(w0) || Vector2.Distance(pos[i], h) <= 6.5f || InCourt(pos[i])) continue;
                    // The reserve waits off screen until released (choreography.md); only those in view must move.
                    if (!new ViewGeometry(tl.FramingAt(t0).ViewWidth).Contains(tl.FramingAt(t0).Focus, pos[i])) continue;
                    if (tl.Map.Sections[tl.Map.SectionIndexAt(t0)].Feel == SectionFeel.Free) continue;
                    float w1 = Way(tl.PositionAt(e, t1));
                    if (float.IsNaN(w1)) continue;   // a small monster in a sliver the 0.4 m field leaves out
                    windows++;
                    if (w0 - w1 >= 0.25f) continue;
                    float ri = tl.TypeRadii[tl.Entities[e].Type];
                    bool blocked = false;
                    for (int j = 0; j < alive.Count && !blocked; j++)
                    {
                        if (j == i) continue;
                        float rj = tl.TypeRadii[tl.Entities[alive[j]].Type];
                        if (Vector2.Distance(pos[i], pos[j]) < ri + rj + 0.35f) blocked = true;
                        else if (Vector2.Distance(tl.PositionAt(e, t1), tl.PositionAt(alive[j], t1)) < ri + rj + 0.35f) blocked = true;   // a crowd that gathered meanwhile
                    }
                    if (blocked) { queueing++; continue; }
                    if (problems.Count < 50)
                        problems.Add(tl.TypeNames[tl.Entities[e].Type] + " " + e + " at " + pos[i] + " gained " + (w0 - w1).ToString("F2") + " m in 4 beats from " + t0.ToString("F1") + " s, with nobody beside it");
                }
            }
            TestContext.WriteLine(run + ": " + windows + " four-beat windows short of the hero, " + queueing + " in a crowd");
            Assert.That(windows, Is.GreaterThan(100));
            Assert.That(problems, Is.Empty, Report(problems));
        }

        /// <summary>Inside the courtyard test layout's walls, where the hero holds: a monster there has reached it.</summary>
        static bool InCourt(Vector2 p) => Math.Abs(p.X) < 7.7f && Math.Abs(p.Y) < 7.7f;

        static bool Died(Timeline tl, int e, double t)
        {
            foreach (ActionEvent a in tl.ActionsOf(e)) if (a.Kind == ActionKind.Death && a.Impact <= t) return true;
            return false;
        }

        /// <summary>
        /// Blocked monsters mark time in place instead of jittering: in a dense run on the courtyard
        /// test layout (jams at every gap), a gait step is either in place (a zero-length Pulse
        /// segment on the beat, with a neighbour in the way) or at least a fifth of a stride, and no
        /// monster reverses its step from one beat to the next or zig-zags without getting anywhere.
        /// </summary>
        [Test]
        public void BlockedMonstersMarkTimeInsteadOfJittering()
        {
            Timeline tl = Fixtures.CourtyardDense;
            Roster roster = Fixtures.CourtyardData.Roster;
            var problems = new List<string>();
            int steps = 0, marching = 0, marchingBlocked = 0, reversals = 0, jamReversals = 0, zigzags = 0;
            for (int e = 1; e < tl.EntityCount; e++)
            {
                EntityInfo info = tl.Entities[e];
                if (info.Kind != EntityKind.Monster) continue;
                float stride = roster.Monsters[info.Type].StepM, r = roster.Monsters[info.Type].Radius;
                var gait = new List<MotionSegment>();
                foreach (MotionSegment s in tl.SegmentsOf(e)) if (s.Ease == Ease.Pulse) gait.Add(s);
                for (int i = 0; i < gait.Count; i++)
                {
                    MotionSegment s = gait[i];
                    float len = Vector2.Distance(s.From, s.To);
                    steps++;
                    if (len < 1e-5f)
                    {
                        marching++;
                        if (NeighbourWithin(tl, e, s.From, s.T0, r + 0.6f)) marchingBlocked++;
                        continue;
                    }
                    if (len < 0.2f * stride - 1e-4f) problems.Add("monster " + e + " shuffles " + len.ToString("F3") + " m at " + s.T0.ToString("F2"));
                    if (i > 0 && Math.Abs(gait[i - 1].T1 - s.T0) < 1e-6)
                    {
                        Vector2 a = gait[i - 1].To - gait[i - 1].From;
                        if (a.Length() > 1e-5f && Vector2.Dot(Vector2.Normalize(a), Vector2.Normalize(s.To - s.From)) < -0.5f)
                        {
                            reversals++;
                            if (NeighbourWithin(tl, e, s.From, s.T0, r + 0.3f)) jamReversals++;
                        }
                    }
                    if (i > 1 && Math.Abs(gait[i - 1].T1 - s.T0) < 1e-6 && Math.Abs(gait[i - 2].T1 - gait[i - 1].T0) < 1e-6)
                    {
                        float path = len + Vector2.Distance(gait[i - 1].From, gait[i - 1].To) + Vector2.Distance(gait[i - 2].From, gait[i - 2].To);
                        if (Vector2.Distance(gait[i - 2].From, gait[i - 2].To) > 1e-5f && Vector2.Distance(gait[i - 1].From, gait[i - 1].To) > 1e-5f
                            && Vector2.Distance(gait[i - 2].From, s.To) < 0.3f * path) zigzags++;
                    }
                }
            }
            TestContext.WriteLine(steps + " gait steps: " + marching + " in place (" + marchingBlocked + " beside a neighbour), " + reversals + " reversals (" + jamReversals + " in a jam), " + zigzags + " zig-zags");
            Assert.That(marching, Is.GreaterThan(steps / 20), "a dense run should jam at the gaps");
            Assert.That(marchingBlocked, Is.GreaterThanOrEqualTo((int)(0.95 * marching)), "monsters marking time with nobody in the way");
            Assert.That(jamReversals, Is.LessThanOrEqualTo(steps / 1000), "blocked monsters reversing their step from one beat to the next");
            Assert.That(reversals, Is.LessThanOrEqualTo(steps / 100), "steps reversing from one beat to the next");
            Assert.That(zigzags, Is.LessThanOrEqualTo(steps / 1000), "zig-zags");
            Assert.That(problems, Is.Empty, Report(problems));
        }

        static bool NeighbourWithin(Timeline tl, int e, Vector2 p, double t, float reach)
        {
            for (int j = 1; j < tl.EntityCount; j++)
            {
                if (j == e) continue;
                EntityInfo info = tl.Entities[j];
                if (info.Kind != EntityKind.Monster || t < info.Spawn || t >= info.Despawn) continue;
                if (Vector2.Distance(tl.PositionAt(j, t), p) < reach + tl.TypeRadii[info.Type]) return true;
            }
            return false;
        }

        /// <summary>
        /// S6 (walls and chokes): the hero holds the courtyard and the horde comes in through its gaps.
        /// Its walls ration the screen (the courtyard's inside is the hero's kill zone, fed only through
        /// four gaps), so its on-screen count is reported rather than held: the run's mean stays within
        /// 25% of N and no bar overfills. Monsters do come in: some stand inside the walls at every beat.
        /// </summary>
        [Test]
        public void S6FunnelsTheHordeThroughTheCourtyardGaps()
        {
            Timeline tl = Fixtures.Stress("S6");
            StressScript s = Fixtures.StressScript("S6");
            LayoutArea court = Fixtures.Data.Layout.Arena.Area("courtyard");
            float hx = court.Size.X / 2 - 0.3f, hy = court.Size.Y / 2 - 0.3f;
            int beatsWithNone = 0, beats = 0;
            foreach (double beat in tl.Map.Beats)
            {
                if (beat < 5) continue;
                beats++;
                int inside = 0;
                for (int e = 1; e < tl.EntityCount; e++)
                {
                    EntityInfo info = tl.Entities[e];
                    if (info.Kind != EntityKind.Monster || beat < info.Spawn || beat >= info.Despawn) continue;
                    Vector2 p = tl.PositionAt(e, beat) - court.Center;
                    if (Math.Abs(p.X) < hx && Math.Abs(p.Y) < hy) inside++;
                }
                if (inside == 0) beatsWithNone++;
            }
            double mean = tl.Sections[0].MeanOnScreen;
            TestContext.WriteLine("S6: mean on screen " + mean.ToString("F0") + " of " + s.N + "; " + beatsWithNone + " of " + beats + " beats with nobody inside the walls");
            Assert.That(mean, Is.GreaterThanOrEqualTo(0.75 * s.N));
            Assert.That(tl.Sections[0].MaxOnScreen, Is.LessThanOrEqualTo(1.1 * s.N));
            Assert.That(beatsWithNone, Is.LessThanOrEqualTo(beats / 50));
        }

        /// <summary>
        /// TD15's dressing comes after everything else: the arena generated without it is exactly the
        /// full arena's first pieces, so dressing never moves a piece the choreography navigates.
        /// </summary>
        [Test]
        public void DressingNeverMovesAnEarlierPiece()
        {
            var table = ChoreographyFiles.LoadFootprints(Fixtures.Root);
            ArenaLayout full = ChoreographyFiles.LoadLayout(Fixtures.Root);
            ArenaLayout bare = ChoreographyFiles.LoadLayout(Fixtures.Root);
            Assert.That(Array.Exists(full.Areas, a => a.Dressing), Is.True);
            int lastPlain = Array.FindLastIndex(bare.Areas, a => !a.Dressing && a.Type != AreaType.Forest && a.Type != AreaType.Clearing && a.Type != AreaType.Path);
            Assert.That(Array.FindIndex(bare.Areas, a => a.Dressing), Is.GreaterThan(lastPlain), "dressing areas follow every placed area, so indices hold");
            bare.Areas = Array.FindAll(bare.Areas, a => !a.Dressing);
            foreach (AreaType t in new List<AreaType>(bare.Scatter.Keys))
                bare.Scatter[t] = Array.FindAll(bare.Scatter[t], s => !s.Dressing);
            foreach (float density in new[] { 1f, 2f })
            {
                PlacementList a = bare.WithDensity(density).Generate(table), b = full.WithDensity(density).Generate(table);
                Assert.That(b.Items.Length, Is.GreaterThan(a.Items.Length));
                for (int i = 0; i < a.Items.Length; i++)
                {
                    Assert.That(b.Props[b.Items[i].Prop].Id, Is.EqualTo(a.Props[a.Items[i].Prop].Id), "piece " + i);
                    Assert.That(b.Items[i].Position, Is.EqualTo(a.Items[i].Position), "piece " + i);
                }
            }
        }

        /// <summary>
        /// The cold review's item 4: flat dressing on fighting ground never sits inside a blocking piece
        /// (a wall, a tomb, a brazier) nor in a landmark's or structure's keep-out.
        /// </summary>
        [Test]
        public void DressingStaysOffWallsAndKeepOuts()
        {
            ArenaLayout layout = ChoreographyFiles.LoadLayout(Fixtures.Root);
            PlacementList list = Fixtures.Data.Layout.Placements;
            var walls = new WallGeometry(list);
            var dressing = new HashSet<string>(layout.Groups["ground-dressing"].Props);
            int n = 0;
            var problems = new List<string>();
            foreach (Placement p in list.Items)
            {
                Footprint f = list.Props[p.Prop];
                if (p.Area >= 0 || !dressing.Contains(f.Id) || layout.FightingDistance(p.Position, out _) > 0) continue;
                n++;
                if (walls.Distance(p.Position) <= 0) problems.Add(f.Id + " at " + p.Position + " is inside a blocking piece");
                if (layout.InKeepOut(p.Position, 0)) problems.Add(f.Id + " at " + p.Position + " is in a keep-out");
            }
            Assert.That(n, Is.GreaterThan(100), "dressing on fighting ground");
            Assert.That(problems, Is.Empty, Report(problems));
        }

        /// <summary>
        /// TD15's beauty shots hold the hero at each shot's ground (`--hold-at`): every shot's point is
        /// fighting ground (a clearing or path, not a landmark's or wall's footprint), and a stress run
        /// held there keeps the hero within its hold radius of it.
        /// </summary>
        [Test]
        public void BeautyShotsHoldTheHeroOnOpenGround()
        {
            ArenaLayout layout = Fixtures.Data.Layout.Arena;
            Assert.That(layout.Shots.Length, Is.EqualTo(6));
            var walls = new WallGeometry(Fixtures.Data.Layout.Placements);
            foreach (LayoutShot shot in layout.Shots)
            {
                Assert.That(layout.FightingDistance(shot.At, out _), Is.LessThanOrEqualTo(0f), shot.Name + " is not on fighting ground");
                Assert.That(walls.Distance(shot.At), Is.GreaterThan(0.5f), shot.Name + " stands in or against a blocking piece");
            }
            StressScript s = Fixtures.StressScript("S1").With(n: 40, seconds: 12);
            LayoutShot path = Array.Find(layout.Shots, x => x.Name == "path");
            s.HoldAt = path.At;
            Timeline tl = Choreographer.RunStress(s, Fixtures.Data);
            for (double t = 2; t < 11; t += 1)
                Assert.That(Vector2.Distance(tl.PositionAt(0, t), path.At), Is.LessThanOrEqualTo(s.HoldRadiusM + 0.5f), "hero at t=" + t);
        }
    }

    /// <summary>The placement list's blocking footprints as exact world shapes, bucketed, for the tests' own distances.</summary>
    sealed class WallGeometry
    {
        const float Bucket = 4f, Reach = 3f;
        readonly List<Vector2> centres = new List<Vector2>();
        readonly List<float> radii = new List<float>();
        readonly List<Vector2[]> polys = new List<Vector2[]>();
        readonly Dictionary<long, List<int>> buckets = new Dictionary<long, List<int>>();
        readonly Vector2 half;

        public WallGeometry(PlacementList list)
        {
            half = list.Size / 2;
            foreach (Placement p in list.Items)
            {
                Footprint f = list.Props[p.Prop];
                if (!f.Blocks) continue;
                int idx = centres.Count;
                float bound;
                if (f.Shape == FootprintShape.Circle)
                {
                    centres.Add(p.ToWorld(f.Center)); radii.Add(f.Radius * p.Scale); polys.Add(null);
                    bound = f.Radius * p.Scale;
                }
                else
                {
                    var w = new Vector2[f.Points.Length];
                    Vector2 c = Vector2.Zero;
                    for (int i = 0; i < w.Length; i++) { w[i] = p.ToWorld(f.Points[i]); c += w[i]; }
                    c /= w.Length;
                    bound = 0;
                    foreach (Vector2 q in w) bound = Math.Max(bound, Vector2.Distance(q, c));
                    centres.Add(c); radii.Add(0); polys.Add(w);
                }
                Vector2 cc = centres[idx];
                for (int y = (int)Math.Floor((cc.Y - bound - Reach) / Bucket); y <= (int)Math.Floor((cc.Y + bound + Reach) / Bucket); y++)
                    for (int x = (int)Math.Floor((cc.X - bound - Reach) / Bucket); x <= (int)Math.Floor((cc.X + bound + Reach) / Bucket); x++)
                    {
                        long k = ((long)x << 32) ^ (uint)y;
                        if (!buckets.TryGetValue(k, out List<int> l)) buckets[k] = l = new List<int>();
                        l.Add(idx);
                    }
            }
        }

        /// <summary>Distance to the nearest footprint or the arena's edge (negative inside), exact within 3 m.</summary>
        public float Distance(Vector2 p)
        {
            float d = Math.Min(Math.Min(p.X + half.X, half.X - p.X), Math.Min(p.Y + half.Y, half.Y - p.Y));
            long k = ((long)(int)Math.Floor(p.X / Bucket) << 32) ^ (uint)(int)Math.Floor(p.Y / Bucket);
            if (!buckets.TryGetValue(k, out List<int> l)) return Math.Min(d, Reach);
            foreach (int i in l)
            {
                float di = polys[i] == null ? Vector2.Distance(p, centres[i]) - radii[i] : PolygonDistance(polys[i], p);
                if (di < d) d = di;
            }
            return Math.Min(d, Reach);
        }

        static float PolygonDistance(Vector2[] w, Vector2 p)
        {
            float best = float.MaxValue;
            int crossings = 0;
            for (int i = 0; i < w.Length; i++)
            {
                Vector2 a = w[i], b = w[(i + 1) % w.Length];
                Vector2 e = b - a;
                float u = Math.Max(0, Math.Min(1, Vector2.Dot(p - a, e) / Math.Max(1e-12f, e.LengthSquared())));
                best = Math.Min(best, Vector2.Distance(p, a + e * u));
                // Even-odd ray cast along +X.
                if ((a.Y > p.Y) != (b.Y > p.Y) && p.X < a.X + (p.Y - a.Y) * (b.X - a.X) / (b.Y - a.Y)) crossings++;
            }
            return (crossings & 1) == 1 ? -best : best;
        }
    }
}
