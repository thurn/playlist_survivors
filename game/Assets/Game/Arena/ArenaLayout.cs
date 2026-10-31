using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Numerics;

namespace PS.Game.Arena
{
    public enum AreaType { Clearing, Path, Edge, Forest, Landmark, Structure }

    /// <summary>
    /// A kit piece placed explicitly by a landmark or structure: offset from the area's anchor, yaw in
    /// degrees, and <c>liftM</c> above the ground.
    /// </summary>
    public sealed class PieceSpec
    {
        public string Prop;
        public Vector2 At;
        public float YawDeg, Scale = 1f, LiftM;
    }

    /// <summary>
    /// One area of the layout parameters (unity-game.md, "The Arena"; demo3d's environment.md,
    /// "Layout"). Shapes: a disc (<c>center</c>, <c>radius</c>), a ring (<c>center</c>, <c>ring</c>:
    /// [inner, outer]), a capsule (<c>from</c>, <c>to</c>, <c>width</c>) or, for forest, <c>fill</c>:
    /// everything not otherwise typed. Landmarks and structures are placed explicitly, never
    /// scattered; a structure's pieces are the crystal structures' sites, snapped to the hero's grid.
    /// </summary>
    public sealed class LayoutArea
    {
        public AreaType Type;
        public string Name;              // clearings, landmarks and structures
        public Vector2 Center;           // discs, rings, landmarks ("at"), structures
        public float Radius;             // discs
        public float RingInner, RingOuter;
        public bool IsRing, IsCapsule, Fill;
        public Vector2 From, To;         // paths and ruin walls
        public float Width;              // paths
        // Structures.
        public string Kind;              // "courtyard", "ruin-wall" or "pieces" (its explicit pieces only)
        public Vector2 Size;             // courtyard: inside [x, y] in metres
        public float YawDeg;             // courtyard: rotation of its square
        public int Gaps;                 // courtyard: 3 or 4 (one per side, the closed side's skipped); ruin wall: any
        public float GapM;               // each gap's width
        public int ClosedSide = 2;       // courtyard with 3 gaps: 0 south, 1 east, 2 north, 3 west
        public string Walls;             // the prop group its wall runs draw from
        public PieceSpec[] Pieces = new PieceSpec[0];
        public float ClearM = -1;        // landmarks and structures: scatter keeps this far from the anchor (or the wall line); -1 computed
        public bool Dressing;            // TD15: placed after all other scatter, so the layout's earlier placements stay as they were
    }

    /// <summary>One scatter rule: which prop group, how far apart, and the scale and rotation jitter.</summary>
    public sealed class ScatterLayer
    {
        public string Group;
        public float SpacingM = 4f;
        public float ScaleMin = 1f, ScaleMax = 1f;
        public float YawJitterDeg = 360f;
        public float Chance = 1f;
        public float MaxSlopeDeg = 90f;   // the ground is flat for now; kept for TD9's terrain
        public bool KeepCanopyOff;        // forest: canopies never overhang fighting ground
        public bool Dressing;             // TD15: run after every other layer, so it never displaces their pieces
    }

    public sealed class PropGroup
    {
        public string Name;
        public string[] Props;
        public float[] Weights;
    }

    /// <summary>A named camera position for the arena's stills: the game camera's ground focus and view width.</summary>
    public sealed class LayoutShot
    {
        public string Name;
        public Vector2 At;
        public float ViewWidthM;
    }

    /// <summary>
    /// The arena's layout parameters (unity-game.md, "The Arena"), demo3d's ArenaLayout copied and
    /// owned: a seed, the square ground, typed areas, seeded scatter rules per area type, explicit
    /// structures and landmarks, and a decoration-density multiplier that scales only edge bands and
    /// forest. <see cref="Generate"/> turns them into the one <see cref="PlacementList"/> that both the
    /// level data (<see cref="LevelExport"/>) and the editor's scene generator (ArenaBaker) use, so a
    /// wall the camera shows is a wall the hero and the monsters walk around. The hero's grid is
    /// <see cref="TileM"/> square tiles from the ground's south-west corner; structures snap to it.
    /// The JSON format is documented in game/Content/arena-layout.json's note.
    /// </summary>
    public sealed class ArenaLayout
    {
        public string Name = "arena";
        public uint Seed = 7;
        public Vector2 Size = new Vector2(240, 240);
        /// <summary>The hero's grid (unity-game.md, "Hero Movement on the Grid"): tiles this many metres square.</summary>
        public float TileM = 1.5f;
        public float DecorationDensity = 1f;
        public Vector2 HeroStart;
        public float EdgeWidthMin = 3f, EdgeWidthMax = 6f;
        public LayoutArea[] Areas = new LayoutArea[0];
        public Dictionary<string, PropGroup> Groups = new Dictionary<string, PropGroup>(StringComparer.Ordinal);
        public Dictionary<AreaType, ScatterLayer[]> Scatter = new Dictionary<AreaType, ScatterLayer[]>();
        public LayoutShot[] Shots = new LayoutShot[0];

        public Vector2 Min => -Size / 2;
        public Vector2 Max => Size / 2;

        public ArenaLayout WithDensity(float density)
        {
            var c = (ArenaLayout)MemberwiseClone();
            c.DecorationDensity = density;
            return c;
        }

        public LayoutArea Area(string name)
        {
            foreach (LayoutArea a in Areas) if (a.Name == name) return a;
            return null;
        }

        // ------------------------------------------------------------------ parsing

        public static ArenaLayout Parse(string json)
        {
            JsonValue root = JsonValue.Parse(json);
            var l = new ArenaLayout
            {
                Name = root["name"].AsString("arena"),
                Seed = (uint)root["seed"].AsDouble(7),
                Size = root.Has("size") ? Vec(root["size"]) : new Vector2(240, 240),
                TileM = root["tileM"].AsFloat(1.5f),
                DecorationDensity = root["decorationDensity"].AsFloat(1f),
                HeroStart = Vec(root["heroStart"]),
            };
            if (!(l.TileM > 0) || Math.Abs(l.Size.X / l.TileM - Math.Round(l.Size.X / l.TileM)) > 1e-3
                || Math.Abs(l.Size.Y / l.TileM - Math.Round(l.Size.Y / l.TileM)) > 1e-3)
                throw new FormatException("layout: size must be a whole number of tileM tiles");
            JsonValue ew = root["edgeWidthM"];
            if (ew.Count >= 2) { l.EdgeWidthMin = ew[0].AsFloat(); l.EdgeWidthMax = ew[1].AsFloat(); }

            JsonValue groups = root["props"];
            if (groups.Type == JsonType.Object)
                foreach (KeyValuePair<string, JsonValue> kv in groups.Members)
                {
                    var g = new PropGroup { Name = kv.Key };
                    var names = new List<string>();
                    var weights = new List<float>();
                    if (kv.Value.Type == JsonType.Object)
                        foreach (KeyValuePair<string, JsonValue> p in kv.Value.Members) { names.Add(p.Key); weights.Add(p.Value.AsFloat(1f)); }
                    else
                        for (int i = 0; i < kv.Value.Count; i++) { names.Add(kv.Value[i].AsString()); weights.Add(1f); }
                    g.Props = names.ToArray();
                    g.Weights = weights.ToArray();
                    if (g.Props.Length == 0) throw new FormatException("layout: prop group '" + g.Name + "' is empty");
                    l.Groups[g.Name] = g;
                }

            JsonValue scatter = root["scatter"];
            if (scatter.Type == JsonType.Object)
                foreach (KeyValuePair<string, JsonValue> kv in scatter.Members)
                {
                    AreaType type = ParseType(kv.Key);
                    var layers = new ScatterLayer[kv.Value.Count];
                    for (int i = 0; i < layers.Length; i++)
                    {
                        JsonValue s = kv.Value[i];
                        layers[i] = new ScatterLayer
                        {
                            Group = s["props"].AsString(),
                            SpacingM = s["spacingM"].AsFloat(4f),
                            ScaleMin = s["scale"].Count >= 2 ? s["scale"][0].AsFloat(1f) : 1f,
                            ScaleMax = s["scale"].Count >= 2 ? s["scale"][1].AsFloat(1f) : 1f,
                            YawJitterDeg = s["yawJitterDeg"].AsFloat(360f),
                            Chance = s["chance"].AsFloat(1f),
                            MaxSlopeDeg = s["maxSlopeDeg"].AsFloat(90f),
                            KeepCanopyOff = s["keepCanopyOff"].AsBool(type == AreaType.Forest),
                            Dressing = s["dressing"].AsBool(false),
                        };
                        if (layers[i].Group == null || !l.Groups.ContainsKey(layers[i].Group))
                            throw new FormatException("layout: scatter for " + kv.Key + " names unknown prop group '" + layers[i].Group + "'");
                        if (layers[i].SpacingM <= 0.2f) throw new FormatException("layout: scatter spacing must exceed 0.2 m");
                    }
                    l.Scatter[type] = layers;
                }

            JsonValue areas = root["areas"];
            l.Areas = new LayoutArea[areas.Count];
            for (int i = 0; i < areas.Count; i++) l.Areas[i] = ParseArea(areas[i], l);

            JsonValue shots = root["shots"];
            l.Shots = new LayoutShot[shots.Count];
            for (int i = 0; i < shots.Count; i++)
                l.Shots[i] = new LayoutShot { Name = shots[i]["name"].AsString(), At = Vec(shots[i]["at"]), ViewWidthM = shots[i]["viewWidthM"].AsFloat(40f) };
            return l;
        }

        static LayoutArea ParseArea(JsonValue a, ArenaLayout l)
        {
            var area = new LayoutArea
            {
                Type = ParseType(a["type"].AsString()),
                Name = a["name"].AsString(),
                Kind = a["kind"].AsString(),
                Radius = a["radius"].AsFloat(0f),
                Width = a["width"].AsFloat(6f),
                Fill = a["fill"].AsBool(false),
                YawDeg = a["yawDeg"].AsFloat(0f),
                Gaps = a["gaps"].AsInt(0),
                GapM = a["gapM"].AsFloat(5f),
                ClosedSide = a["closedSide"].AsInt(2),
                Walls = a["walls"].AsString(),
                ClearM = a["clearM"].AsFloat(-1f),
                Dressing = a["dressing"].AsBool(false),
            };
            area.Center = a.Has("center") ? Vec(a["center"]) : Vec(a["at"]);
            if (a.Has("ring")) { area.IsRing = true; area.RingInner = a["ring"][0].AsFloat(); area.RingOuter = a["ring"][1].AsFloat(); }
            if (a.Has("from")) { area.IsCapsule = true; area.From = Vec(a["from"]); area.To = Vec(a["to"]); }
            if (a.Has("size")) area.Size = Vec(a["size"]);
            JsonValue pieces = a["pieces"];
            area.Pieces = new PieceSpec[pieces.Count];
            for (int k = 0; k < pieces.Count; k++)
            {
                JsonValue p = pieces[k];
                area.Pieces[k] = new PieceSpec { Prop = p["prop"].AsString(), At = Vec(p["at"]), YawDeg = p["yawDeg"].AsFloat(0f), Scale = p["scale"].AsFloat(1f), LiftM = p["liftM"].AsFloat(0f) };
            }
            if (area.Type == AreaType.Structure)
            {
                if (area.Kind != "courtyard" && area.Kind != "ruin-wall" && area.Kind != "pieces")
                    throw new FormatException("layout: structure '" + area.Name + "' has unknown kind '" + area.Kind + "' (courtyard, ruin-wall or pieces)");
                if (area.Kind != "pieces" && (area.Walls == null || !l.Groups.ContainsKey(area.Walls)))
                    throw new FormatException("layout: structure '" + area.Name + "' names unknown wall group '" + area.Walls + "'");
                if (area.Kind == "courtyard" && (area.Gaps < 3 || area.Gaps > 4))
                    throw new FormatException("layout: courtyard '" + area.Name + "' needs 3 or 4 gaps");
                if (area.Kind == "ruin-wall" && !area.IsCapsule) throw new FormatException("layout: ruin wall '" + area.Name + "' needs from and to");
                l.Snap(area);
            }
            if (area.Type == AreaType.Path && !area.IsCapsule) throw new FormatException("layout: a path needs from and to");
            return area;
        }

        static AreaType ParseType(string s)
        {
            switch (s)
            {
                case "clearing": return AreaType.Clearing;
                case "path": return AreaType.Path;
                case "edge": return AreaType.Edge;
                case "forest": return AreaType.Forest;
                case "landmark": return AreaType.Landmark;
                case "structure": return AreaType.Structure;
                default: throw new FormatException("layout: unknown area type '" + s + "'");
            }
        }

        static Vector2 Vec(JsonValue v) => v.Count >= 2 ? new Vector2(v[0].AsFloat(), v[1].AsFloat()) : Vector2.Zero;

        // ------------------------------------------------------------------ the hero's grid

        /// <summary>The centre of the tile nearest <paramref name="p"/> (the grid starts at the ground's south-west corner).</summary>
        public Vector2 SnapToTile(Vector2 p) => new Vector2(
            Min.X + ((float)Math.Floor((p.X - Min.X) / TileM) + 0.5f) * TileM,
            Min.Y + ((float)Math.Floor((p.Y - Min.Y) / TileM) + 0.5f) * TileM);

        static float SnapYaw(float deg) => (float)(Math.Round(deg / 90.0) * 90.0);

        /// <summary>
        /// Snaps a structure to the hero's grid (unity-game.md, "The Arena"), so its walls and pieces
        /// fill whole tiles: its anchor (a courtyard's centre, a ruin wall's ends) on a tile centre, a
        /// courtyard's sides on tile centres and its yaw and every explicit piece's to a quarter turn,
        /// each piece on a tile centre. A ruin wall runs along a grid axis; gaps are an odd number of tiles.
        /// </summary>
        void Snap(LayoutArea a)
        {
            a.Center = SnapToTile(a.Center);
            if (a.IsCapsule)
            {
                a.From = SnapToTile(a.From);
                a.To = SnapToTile(a.To);
                if (a.Kind == "ruin-wall" && a.From.X != a.To.X && a.From.Y != a.To.Y)
                    throw new FormatException("layout: ruin wall '" + a.Name + "' must run along a grid axis");
            }
            a.YawDeg = SnapYaw(a.YawDeg);
            a.Size = new Vector2(2 * TileM * (float)Math.Max(1, Math.Round(a.Size.X / (2 * TileM))),
                2 * TileM * (float)Math.Max(1, Math.Round(a.Size.Y / (2 * TileM))));
            // An odd number of tiles, so a gap centred on a tile centre ends on tile edges.
            if (a.Gaps > 0) a.GapM = TileM * (2 * (float)Math.Max(0, Math.Round((a.GapM / TileM - 1) / 2)) + 1);
            foreach (PieceSpec s in a.Pieces)
            {
                s.At = SnapToTile(a.Center + s.At) - a.Center;
                s.YawDeg = SnapYaw(s.YawDeg);
            }
        }

        // ------------------------------------------------------------------ classification

        /// <summary>How far <paramref name="p"/> lies outside the fighting ground (clearings and paths); negative inside.</summary>
        public float FightingDistance(Vector2 p, out AreaType type)
        {
            float best = float.MaxValue;
            type = AreaType.Forest;
            foreach (LayoutArea a in Areas)
            {
                float d;
                if (a.Type == AreaType.Clearing)
                    d = a.IsCapsule ? SegmentDistance(p, a.From, a.To) - a.Width / 2 : Vector2.Distance(p, a.Center) - a.Radius;
                else if (a.Type == AreaType.Path) d = SegmentDistance(p, a.From, a.To) - a.Width / 2;
                else continue;
                if (d < best) { best = d; type = a.Type; }
            }
            return best;
        }

        /// <summary>Is <paramref name="p"/> inside a landmark's or structure's keep-clear zone (inflated by <paramref name="pad"/>)?</summary>
        public bool InKeepOut(Vector2 p, float pad)
        {
            foreach (LayoutArea a in Areas)
            {
                if (a.Type != AreaType.Landmark && a.Type != AreaType.Structure) continue;
                if (a.Dressing) continue; // dressing keeps nothing clear: it comes after the scatter
                float clear = ClearOf(a) + pad;
                float d = a.Type == AreaType.Structure && a.Kind == "ruin-wall" ? SegmentDistance(p, a.From, a.To) : Vector2.Distance(p, a.Center);
                if (d < clear) return true;
            }
            return false;
        }

        float ClearOf(LayoutArea a)
        {
            if (a.ClearM >= 0) return a.ClearM;
            if (a.Type == AreaType.Structure && a.Kind != "pieces")
                return a.Kind == "courtyard" ? new Vector2(a.Size.X / 2, a.Size.Y / 2).Length() + 1.5f : 1.5f;
            float r = 1.5f;
            foreach (PieceSpec s in a.Pieces) r = Math.Max(r, s.At.Length() + 1.5f + (footprints?[s.Prop]?.BoundingRadius ?? 1f) * s.Scale);
            return r;
        }

        FootprintTable footprints;   // set during Generate, for keep-clear radii

        /// <summary>
        /// The scatter type of ground at <paramref name="p"/>: clearing or path (fighting ground), edge
        /// band (within a seeded 3–6 m of fighting ground), forest, or null (outside the arena, or
        /// kept clear around a landmark or structure).
        /// </summary>
        public AreaType? Classify(Vector2 p)
        {
            if (p.X < Min.X || p.Y < Min.Y || p.X > Max.X || p.Y > Max.Y) return null;
            if (InKeepOut(p, 0)) return null;
            float d = FightingDistance(p, out AreaType fight);
            if (d <= 0) return fight;
            if (d <= EdgeWidth(p)) return AreaType.Edge;
            foreach (LayoutArea a in Areas)
            {
                if (a.Type != AreaType.Forest) continue;
                if (a.Fill) return AreaType.Forest;
                float r = Vector2.Distance(p, a.Center);
                if (a.IsRing ? r >= a.RingInner && r <= a.RingOuter : r <= a.Radius) return AreaType.Forest;
            }
            return null;
        }

        /// <summary>The edge band's width at <paramref name="p"/>: value noise on a 10 m lattice between the two widths.</summary>
        public float EdgeWidth(Vector2 p)
        {
            const float lattice = 10f;
            float gx = p.X / lattice, gy = p.Y / lattice;
            int x0 = (int)Math.Floor(gx), y0 = (int)Math.Floor(gy);
            float fx = gx - x0, fy = gy - y0;
            fx = fx * fx * (3 - 2 * fx); fy = fy * fy * (3 - 2 * fy);
            float n00 = Noise(x0, y0), n10 = Noise(x0 + 1, y0), n01 = Noise(x0, y0 + 1), n11 = Noise(x0 + 1, y0 + 1);
            float n = (n00 * (1 - fx) + n10 * fx) * (1 - fy) + (n01 * (1 - fx) + n11 * fx) * fy;
            return EdgeWidthMin + (EdgeWidthMax - EdgeWidthMin) * n;
        }

        float Noise(int x, int y) => (Rng.Hash(Seed, (uint)x * 0x9E3779B1u ^ (uint)y, 0xED6E) >> 8) * (1f / (1 << 24));

        public static float SegmentDistance(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float l2 = ab.LengthSquared();
            float u = l2 < 1e-9f ? 0 : Math.Max(0, Math.Min(1, Vector2.Dot(p - a, ab) / l2));
            return Vector2.Distance(p, a + ab * u);
        }

        // ------------------------------------------------------------------ generation

        /// <summary>
        /// The placement list: explicit landmark and structure pieces first (in area order), then the
        /// scatter of paths, clearings, forest and edge bands, in that order, so the fighting ground's
        /// clutter is the same at every density. Dressing (TD15: landmarks and scatter layers marked
        /// <c>dressing</c>) comes last, in the same orders, so it never displaces an earlier piece and the
        /// blocking set (what the choreography navigates) is unchanged by flat dressing. Deterministic for
        /// a given layout, density and footprint table.
        /// </summary>
        public PlacementList Generate(FootprintTable table)
        {
            footprints = table;
            var gen = new Generator(this, table);
            foreach (bool dressing in new[] { false, true })
            {
                if (dressing) gen.BeginDressing();
                for (int i = 0; i < Areas.Length; i++)
                {
                    LayoutArea a = Areas[i];
                    if (a.Dressing != dressing) continue;
                    foreach (PieceSpec s in a.Pieces) gen.Place(s.Prop, a.Center + s.At, DegToRad(s.YawDeg), s.Scale, i, true, s.LiftM);
                    if (a.Type == AreaType.Structure && a.Kind != "pieces") gen.Structure(a, i);
                }
                foreach (AreaType t in new[] { AreaType.Path, AreaType.Clearing, AreaType.Forest, AreaType.Edge })
                {
                    if (!Scatter.TryGetValue(t, out ScatterLayer[] layers)) continue;
                    for (int k = 0; k < layers.Length; k++)
                        if (layers[k].Dressing == dressing) gen.ScatterLayer(t, k, layers[k]);
                }
            }
            PlacementList list = gen.Finish();
            footprints = null;
            return list;
        }

        sealed class Generator
        {
            readonly ArenaLayout l;
            readonly FootprintTable table;
            readonly List<Footprint> props = new List<Footprint>();
            readonly Dictionary<string, int> propIndex = new Dictionary<string, int>(StringComparer.Ordinal);
            readonly List<Placement> items = new List<Placement>();
            // Placed bodies, bucketed by 4 m cells, for the overlap test.
            const float Bucket = 4f;
            readonly Dictionary<long, List<int>> buckets = new Dictionary<long, List<int>>();
            readonly List<float> bodyR = new List<float>();

            public Generator(ArenaLayout l, FootprintTable table) { this.l = l; this.table = table; }

            int PropIndex(string id)
            {
                if (propIndex.TryGetValue(id, out int i)) return i;
                Footprint f = table.Require(id);
                i = props.Count;
                props.Add(f);
                propIndex[id] = i;
                return i;
            }

            static float Body(Footprint f, float scale) => f.Blocks ? f.BoundingRadius * scale : Math.Min(0.4f, f.BoundingRadius * scale * 0.5f);

            static long Key(int x, int y) => ((long)x << 32) ^ (uint)y;

            int dressingStart = int.MaxValue;

            /// <summary>From here on, pieces are dressing (see <see cref="OverlapsFixed"/>).</summary>
            public void BeginDressing() => dressingStart = items.Count;

            /// <summary>
            /// Overlap with the pieces that are the same at every density: landmark and structure pieces
            /// (walls, tombs, braziers, the pyre) and dressing already placed on fighting ground. Flat
            /// dressing on fighting ground tests only these, so it never sits through a wall or on another
            /// patch, and stays density-invariant (the density-scaled forest and edge never reach
            /// fighting ground; edge dressing comes after it).
            /// </summary>
            bool OverlapsFixed(Vector2 p, float r)
            {
                int x0 = (int)Math.Floor((p.X - r - 8) / Bucket), x1 = (int)Math.Floor((p.X + r + 8) / Bucket);
                int y0 = (int)Math.Floor((p.Y - r - 8) / Bucket), y1 = (int)Math.Floor((p.Y + r + 8) / Bucket);
                for (int y = y0; y <= y1; y++)
                    for (int x = x0; x <= x1; x++)
                    {
                        if (!buckets.TryGetValue(Key(x, y), out List<int> list)) continue;
                        foreach (int j in list)
                            if ((items[j].Area >= 0 || j >= dressingStart) && Vector2.Distance(items[j].Position, p) < r + bodyR[j]) return true;
                    }
                return false;
            }

            bool Overlaps(Vector2 p, float r)
            {
                int x0 = (int)Math.Floor((p.X - r - 8) / Bucket), x1 = (int)Math.Floor((p.X + r + 8) / Bucket);
                int y0 = (int)Math.Floor((p.Y - r - 8) / Bucket), y1 = (int)Math.Floor((p.Y + r + 8) / Bucket);
                for (int y = y0; y <= y1; y++)
                    for (int x = x0; x <= x1; x++)
                    {
                        if (!buckets.TryGetValue(Key(x, y), out List<int> list)) continue;
                        foreach (int j in list)
                            if (Vector2.Distance(items[j].Position, p) < r + bodyR[j]) return true;
                    }
                return false;
            }

            public void Place(string id, Vector2 p, float yaw, float scale, int area, bool force, float lift = 0f, int segment = -1)
            {
                int pi = PropIndex(id);
                Footprint f = props[pi];
                float body = Body(f, scale);
                if (!force && Overlaps(p, body)) return;
                Add(new Placement(pi, p, yaw, scale, area, lift, segment), body);
            }

            void Add(Placement pl, float body)
            {
                int idx = items.Count;
                items.Add(pl);
                bodyR.Add(body);
                long k = Key((int)Math.Floor(pl.Position.X / Bucket), (int)Math.Floor(pl.Position.Y / Bucket));
                if (!buckets.TryGetValue(k, out List<int> list)) buckets[k] = list = new List<int>();
                list.Add(idx);
            }

            public void Structure(LayoutArea a, int areaIndex)
            {
                var rng = new Rng(l.Seed, 0x5700 + (uint)areaIndex);
                PropGroup walls = l.Groups[a.Walls];
                if (a.Kind == "courtyard")
                {
                    float yaw = DegToRad(a.YawDeg);
                    float hx = a.Size.X / 2, hy = a.Size.Y / 2;
                    // Corners counter-clockwise from south-west (before the yaw): sides south, east, north, west.
                    var c = new[] { new Vector2(-hx, -hy), new Vector2(hx, -hy), new Vector2(hx, hy), new Vector2(-hx, hy) };
                    for (int s = 0; s < 4; s++)
                    {
                        Vector2 A = a.Center + Rotate(c[s], yaw), B = a.Center + Rotate(c[(s + 1) & 3], yaw);
                        float len = Vector2.Distance(A, B);
                        bool gap = a.Gaps == 4 || s != a.ClosedSide;
                        if (gap)
                        {
                            Run(walls, ref rng, A, B, 0, len / 2 - a.GapM / 2, areaIndex, 2 * s);
                            Run(walls, ref rng, A, B, len / 2 + a.GapM / 2, len, areaIndex, 2 * s + 1);
                        }
                        else Run(walls, ref rng, A, B, 0, len, areaIndex, 2 * s);
                    }
                }
                else
                {
                    float len = Vector2.Distance(a.From, a.To);
                    float s0 = 0;
                    for (int g = 0; g < a.Gaps; g++)
                    {
                        float mid = len * (g + 1) / (a.Gaps + 1);
                        Run(walls, ref rng, a.From, a.To, s0, mid - a.GapM / 2, areaIndex, g);
                        s0 = mid + a.GapM / 2;
                    }
                    Run(walls, ref rng, a.From, a.To, s0, len, areaIndex, a.Gaps);
                }
            }

            /// <summary>
            /// Lays wall pieces end to end along A→B from <paramref name="s0"/> to <paramref name="s1"/>
            /// metres, each piece's local X axis along the run. The last piece is the longest that
            /// fits, or ends exactly at <paramref name="s1"/> overlapping the one before. Its pieces are
            /// the wall's <paramref name="segment"/> (<see cref="Placement.Segment"/>).
            /// </summary>
            void Run(PropGroup walls, ref Rng rng, Vector2 A, Vector2 B, float s0, float s1, int areaIndex, int segment)
            {
                if (s1 - s0 < 0.3f) return;
                Vector2 d = Vector2.Normalize(B - A);
                float yaw = (float)Math.Atan2(-d.Y, d.X);   // local +X → d: (cos θ, −sin θ) = d
                float s = s0;
                int guard = 0;
                while (s1 - s > 0.05f && guard++ < 1000)
                {
                    string id = Pick(walls, ref rng);
                    Footprint f = table.Require(id);
                    f.AlongX(out float minX, out float maxX, out float midY);
                    float len = maxX - minX;
                    if (s + len > s1 + 0.05f)
                    {
                        // The longest piece that fits the rest, else the shortest, ending exactly at s1.
                        string best = null, shortest = null;
                        float bestLen = 0, shortLen = float.MaxValue;
                        foreach (string w in walls.Props)
                        {
                            table.Require(w).AlongX(out float a0, out float a1, out _);
                            float wl = a1 - a0;
                            if (wl <= s1 - s + 0.05f && wl > bestLen) { best = w; bestLen = wl; }
                            if (wl < shortLen) { shortest = w; shortLen = wl; }
                        }
                        id = best ?? shortest;
                        f = table.Require(id);
                        f.AlongX(out minX, out maxX, out midY);
                        len = maxX - minX;
                        if (best == null) s = Math.Max(s0, s1 - len);
                    }
                    Vector2 target = A + d * s;
                    Vector2 pos = target - Rotate(new Vector2(minX, midY), yaw);
                    Place(id, pos, yaw, 1f, areaIndex, true, 0f, segment);
                    s += len;
                }
            }

            static string Pick(PropGroup g, ref Rng rng)
            {
                float total = 0;
                foreach (float w in g.Weights) total += w;
                float r = rng.NextFloat() * total;
                for (int i = 0; i < g.Props.Length; i++)
                {
                    r -= g.Weights[i];
                    if (r < 0) return g.Props[i];
                }
                return g.Props[g.Props.Length - 1];
            }

            public void ScatterLayer(AreaType type, int layerIndex, ScatterLayer s)
            {
                PropGroup g = l.Groups[s.Group];
                bool dense = type == AreaType.Edge || type == AreaType.Forest;
                if (!dense)
                    foreach (string id in g.Props)
                        if (table.Require(id).Blocks)
                            throw new FormatException("layout: " + type + " scatter may not block, but group '" + g.Name + "' has '" + id + "'");
                float spacing = dense ? s.SpacingM / (float)Math.Sqrt(Math.Max(1e-3f, l.DecorationDensity)) : s.SpacingM;
                var rng = new Rng(l.Seed, 0x5C00 + (uint)type * 64 + (uint)layerIndex);
                Vector2 min = l.Min;
                int nx = (int)Math.Ceiling(l.Size.X / spacing), ny = (int)Math.Ceiling(l.Size.Y / spacing);
                for (int iy = 0; iy < ny; iy++)
                    for (int ix = 0; ix < nx; ix++)
                    {
                        // Every candidate draws the same numbers whether or not it is kept.
                        var p = new Vector2(min.X + (ix + rng.NextFloat()) * spacing, min.Y + (iy + rng.NextFloat()) * spacing);
                        float chance = rng.NextFloat();
                        string id = Pick(g, ref rng);
                        float scale = s.ScaleMin + (s.ScaleMax - s.ScaleMin) * rng.NextFloat();
                        float yaw = DegToRad((rng.NextFloat() - 0.5f) * s.YawJitterDeg);
                        if (chance >= s.Chance) continue;
                        if (l.Classify(p) != type) continue;
                        Footprint f = table.Require(id);
                        if (dense)
                        {
                            float d = l.FightingDistance(p, out _);
                            float reach = f.Blocks ? f.BoundingRadius * scale + 0.3f : 0f;
                            if (s.KeepCanopyOff) reach = Math.Max(reach, f.CanopyRadiusM * scale);
                            if (d < reach) continue;
                            if (l.InKeepOut(p, f.BoundingRadius * scale)) continue;
                        }
                        if (s.Dressing && !dense)
                        {
                            // Dressing on fighting ground: kept out of landmarks' and structures' clear zones
                            // and off fixed pieces only (OverlapsFixed), so it is the same at every density.
                            float body = Body(f, scale);
                            if (l.InKeepOut(p, body) || OverlapsFixed(p, body)) continue;
                            Add(new Placement(PropIndex(id), p, yaw, scale, -1), body);
                            continue;
                        }
                        Place(id, p, yaw, scale, -1, false);
                    }
            }

            public PlacementList Finish() => new PlacementList
            {
                Layout = l.Name, Seed = l.Seed, Size = l.Size, DecorationDensity = l.DecorationDensity,
                Props = props.ToArray(), Items = items.ToArray(), HeroStart = l.HeroStart,
            };
        }

        public static float DegToRad(float deg) => (float)(deg * Math.PI / 180);

        public static Vector2 Rotate(Vector2 v, float yaw)
        {
            float c = (float)Math.Cos(yaw), s = (float)Math.Sin(yaw);
            return new Vector2(v.X * c + v.Y * s, -v.X * s + v.Y * c);
        }
    }

    /// <summary>
    /// One placed kit piece. Ground (X, Y) is Unity's (x, z); <see cref="Yaw"/> is in radians with
    /// the timeline's convention (Unity's Euler y = Yaw × 180/π); the scale is uniform.
    /// </summary>
    public readonly struct Placement
    {
        public readonly int Prop;          // index into PlacementList.Props
        public readonly Vector2 Position;
        public readonly float Yaw, Scale;
        public readonly int Area;          // the landmark or structure that placed it, or -1 for scatter
        public readonly float Lift;        // metres above the ground (a piece's liftM)
        /// <summary>
        /// A structure's wall pieces: the run between gaps they belong to (a courtyard's side × 2, plus 1 for the
        /// side's second half; a ruin wall's runs from its <c>from</c> end, 0 up). -1 for every other piece.
        /// </summary>
        public readonly int Segment;
        public Placement(int prop, Vector2 position, float yaw, float scale, int area, float lift = 0f, int segment = -1)
        {
            Prop = prop; Position = position; Yaw = yaw; Scale = scale; Area = area; Lift = lift; Segment = segment;
        }

        /// <summary>A footprint point (prefab frame, scale 1) on the ground.</summary>
        public Vector2 ToWorld(Vector2 local) => Position + ArenaLayout.Rotate(local * Scale, Yaw);
    }

    /// <summary>
    /// The arena's placement list: every kit piece's type, position, rotation and scale, with each
    /// type's footprint. The scene generator instantiates <c>Props[p.Prop].Guid</c> at each placement;
    /// the level data (<see cref="LevelExport"/>) rasterizes the blocking ones onto the hero's grid.
    /// </summary>
    public sealed class PlacementList
    {
        public string Layout;
        public uint Seed;
        public Vector2 Size;
        public float DecorationDensity;
        public Footprint[] Props;
        public Placement[] Items;
        public Vector2 HeroStart;

        public int BlockingCount
        {
            get
            {
                int n = 0;
                foreach (Placement p in Items) if (Props[p.Prop].Blocks) n++;
                return n;
            }
        }

        /// <summary>
        /// The debug dump: <c>{"layout", "seed", "size", "decorationDensity", "props": [{"id",
        /// "guid", "blocks"}], "placements": [[prop, x, y, yawDeg, scale], ...]}</c>.
        /// </summary>
        public void WriteJson(TextWriter w)
        {
            var o = new JsonOut(w);
            o.BeginObject().Field("layout", Layout).Field("seed", Seed).Key("size").BeginArray().Value(Size.X, 1).Value(Size.Y, 1).EndArray()
             .Field("decorationDensity", DecorationDensity, 3).Newline().Key("props").BeginArray();
            foreach (Footprint f in Props)
            {
                o.Newline().BeginObject().Field("id", f.Id).Field("guid", f.Guid).Field("blocks", f.Blocks).EndObject();
            }
            o.EndArray().Newline().Key("placements").BeginArray();
            foreach (Placement p in Items)
            {
                o.Newline().BeginArray().Value(p.Prop).Value(p.Position.X, 3).Value(p.Position.Y, 3)
                 .Value(p.Yaw * 180.0 / Math.PI, 2).Value(p.Scale, 3).EndArray();
            }
            o.EndArray().Newline().EndObject().Newline();
        }

        public string ToJson()
        {
            var sw = new StringWriter(CultureInfo.InvariantCulture);
            WriteJson(sw);
            return sw.ToString();
        }
    }
}
