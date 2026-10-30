using System;
using System.Collections.Generic;
using System.Numerics;
using PS.Choreography;

namespace Demo2D.Choreography
{
    /// <summary>A whole-metre rectangle of ground: [X0, X1) × [Y0, Y1).</summary>
    public readonly struct TileRect
    {
        public readonly int X0, Y0, X1, Y1;
        public TileRect(int x0, int y0, int x1, int y1) { X0 = x0; Y0 = y0; X1 = x1; Y1 = y1; }
        public int W => X1 - X0;
        public int H => Y1 - Y0;
        public Vector2 Center => new Vector2((X0 + X1) / 2f, (Y0 + Y1) / 2f);

        /// <summary>How far p lies outside the rectangle (0 inside).</summary>
        public float Distance(Vector2 p)
        {
            float dx = Math.Max(Math.Max(X0 - p.X, p.X - X1), 0), dy = Math.Max(Math.Max(Y0 - p.Y, p.Y - Y1), 0);
            return (float)Math.Sqrt(dx * dx + dy * dy);
        }

        public static TileRect Parse(JsonValue v, string what)
        {
            if (v.Count != 4) throw new FormatException("arena: " + what + " needs rect [x0, y0, x1, y1]");
            var r = new TileRect(v[0].AsInt(), v[1].AsInt(), v[2].AsInt(), v[3].AsInt());
            for (int i = 0; i < 4; i++)
                if (Math.Abs(v[i].AsDouble() - Math.Round(v[i].AsDouble())) > 1e-6) throw new FormatException("arena: " + what + "'s rect must lie on whole metres");
            if (r.W <= 0 || r.H <= 0) throw new FormatException("arena: " + what + "'s rect is empty");
            return r;
        }
    }

    public sealed class Cliff
    {
        public string Name;
        public TileRect Rect;
    }

    public sealed class WaterChannel
    {
        public string Name;
        public TileRect Rect;
        /// <summary>Each bridge deck's west edge; a deck is <see cref="ForestArena.BridgeDeckM"/> wide.</summary>
        public int[] Bridges = new int[0];
    }

    /// <summary>One prop's sprite (forest-props.json): its sheet, its frames' pixel rectangles and its feet.</summary>
    public sealed class PropSprite
    {
        public string Id, Sheet;
        /// <summary>Each frame's [x, y from the top, w, h] on the sheet; one for a static prop.</summary>
        public int[][] Frames;
        public float Fps;
        /// <summary>The feet, in pixels from the frame's bottom-left.</summary>
        public int PivotX, PivotY;
        public float HeightM;
        public int W => Frames[0][2];
        public int H => Frames[0][3];
    }

    /// <summary>
    /// The Ancient Forest arena (PX6): demo3d's <see cref="ArenaLayout"/> (areas, goals, scatter) plus
    /// a 2D extension (README, "The arena"): ground materials per area, cliffs, water and bridges,
    /// read from Data/arena-layout.json with the 2D kit in Data/forest-props.json. The sim and the
    /// client both build it from the same JSON at load: <see cref="Ground"/> is the layout the
    /// choreographer navigates, <see cref="Placements"/> what the client draws, and
    /// <see cref="ArenaTiles"/> composes its Tilemaps, so a tree, a cliff or the stream on screen is
    /// an obstacle in the sim.
    /// </summary>
    public sealed class ForestArena
    {
        public const int BridgeDeckM = 2;
        /// <summary>Scatter whose body comes this close to water or a cliff is dropped.</summary>
        const float TerrainClearM = 0.3f;

        public ArenaLayout Layout;
        public FootprintTable Footprints;
        public readonly Dictionary<string, PropSprite> Sprites = new Dictionary<string, PropSprite>(StringComparer.Ordinal);
        public string BaseGround;
        /// <summary>The ground materials in drawing order: (name, transition set).</summary>
        public (string name, string set)[] Materials = new (string, string)[0];
        /// <summary>Each area's ground: -1 none, 0 base, k the material k-1.</summary>
        public int[] AreaGround = new int[0];
        public float WobbleM;
        public Cliff[] Cliffs = new Cliff[0];
        public WaterChannel[] Streams = new WaterChannel[0];

        /// <summary>Every placed prop, then one terrain obstacle per cliff and per stretch of water between bridges.</summary>
        public PlacementList Placements;
        /// <summary>The layout the choreographer navigates (<see cref="Placements"/>' obstacles).</summary>
        public GroundLayout Ground;
        /// <summary>Placements from this index on are terrain obstacles, not props.</summary>
        public int TerrainStart;

        public int TilesW => (int)Layout.Size.X;
        public int TilesH => (int)Layout.Size.Y;
        /// <summary>The ground's south-west corner (tile (0, 0)'s), in metres.</summary>
        public int OriginX => (int)Layout.Min.X;
        public int OriginY => (int)Layout.Min.Y;

        public static ForestArena Parse(string layoutJson, string propsJson)
        {
            JsonValue root = JsonValue.Parse(layoutJson);
            var a = new ForestArena
            {
                Layout = ArenaLayout.Parse(layoutJson),
                Footprints = FootprintTable.Parse(propsJson),
            };
            if (a.Layout.Size.X != Math.Round(a.Layout.Size.X) || a.Layout.Size.Y != Math.Round(a.Layout.Size.Y) || ((int)a.Layout.Size.X & 1) != 0 || ((int)a.Layout.Size.Y & 1) != 0)
                throw new FormatException("arena: size must be whole, even metres, so its tiles meet the origin");
            a.ParseSprites(JsonValue.Parse(propsJson));
            a.ParseGround(root);
            a.ParseTerrain(root);
            a.Generate();
            return a;
        }

        /// <summary>The choreographer's inputs on this arena.</summary>
        public ChoreographyData Data(string annotationsJson, string rosterJson)
        {
            ClipLibrary clips = ClipLibrary.Parse(annotationsJson);
            var data = new ChoreographyData { Clips = clips, Roster = Roster.Parse(rosterJson, clips), Footprints = Footprints, Layout = Ground };
            Layout.Validate(data.Roster);
            float need = 2 * data.Roster.HeroRadius;
            if (BridgeDeckM <= need) throw new FormatException("arena: a " + BridgeDeckM + " m bridge deck is too narrow for the hero");
            return data;
        }

        void ParseSprites(JsonValue root)
        {
            JsonValue props = root["props"];
            for (int i = 0; i < props.Count; i++)
            {
                JsonValue p = props[i];
                var s = new PropSprite
                {
                    Id = p["id"].AsString(),
                    Sheet = p["sheet"].AsString(),
                    PivotX = p["pivot"][0].AsInt(),
                    PivotY = p["pivot"][1].AsInt(),
                    HeightM = p["heightM"].AsFloat(1f),
                };
                if (s.Sheet == null) throw new FormatException("forest props: '" + s.Id + "' names no sheet");
                if (p.Has("rect"))
                {
                    JsonValue r = p["rect"];
                    s.Frames = new[] { new[] { r[0].AsInt(), r[1].AsInt(), r[2].AsInt(), r[3].AsInt() } };
                }
                else
                {
                    int w = p["frame"][0].AsInt(), h = p["frame"][1].AsInt(), grid = p["grid"].AsInt(), n = p["frames"].AsInt();
                    if (w <= 0 || h <= 0 || grid <= 0 || n <= 0) throw new FormatException("forest props: '" + s.Id + "' needs a rect, or frame, grid and frames");
                    s.Frames = new int[n][];
                    for (int k = 0; k < n; k++) s.Frames[k] = new[] { k % grid * w, k / grid * h, w, h };
                    s.Fps = p["fps"].AsFloat(10f);
                }
                Sprites[s.Id] = s;
            }
        }

        void ParseGround(JsonValue root)
        {
            JsonValue g = root["ground"];
            BaseGround = g["base"].AsString("grass");
            WobbleM = g["wobbleM"].AsFloat(1f);
            JsonValue mats = g["materials"];
            Materials = new (string, string)[mats.Count];
            for (int i = 0; i < mats.Count; i++) Materials[i] = (mats[i]["name"].AsString(), mats[i]["set"].AsString());
            JsonValue areas = root["areas"];
            AreaGround = new int[areas.Count];
            for (int i = 0; i < areas.Count; i++)
            {
                string m = areas[i]["ground"].AsString();
                AreaGround[i] = m == null ? -1 : m == "base" ? 0 : Array.FindIndex(Materials, x => x.name == m) + 1;
                if (m != null && AreaGround[i] == 0 && m != "base") throw new FormatException("arena: area " + i + " names unknown ground '" + m + "'");
                if (m != null && Layout.Areas[i].Type != AreaType.Clearing && Layout.Areas[i].Type != AreaType.Path && !Layout.Areas[i].Fill)
                    throw new FormatException("arena: only clearings, paths and the forest fill take a ground");
            }
        }

        void ParseTerrain(JsonValue root)
        {
            JsonValue cliffs = root["cliffs"];
            Cliffs = new Cliff[cliffs.Count];
            for (int i = 0; i < cliffs.Count; i++)
            {
                var c = new Cliff { Name = cliffs[i]["name"].AsString("cliff " + i) };
                c.Rect = TileRect.Parse(cliffs[i]["rect"], "cliff " + c.Name);
                if (cliffs[i]["facing"].AsString("south") != "south") throw new FormatException("arena: cliff " + c.Name + " must face south (the sheet draws no other face)");
                if (c.Rect.W < 2 || c.Rect.H < 4) throw new FormatException("arena: cliff " + c.Name + " must be at least 2 x 4 m");
                Cliffs[i] = c;
            }
            JsonValue water = root["water"];
            Streams = new WaterChannel[water.Count];
            for (int i = 0; i < water.Count; i++)
            {
                var s = new WaterChannel { Name = water[i]["name"].AsString("water " + i) };
                s.Rect = TileRect.Parse(water[i]["rect"], "water " + s.Name);
                if (s.Rect.W < 3 || s.Rect.H < 4) throw new FormatException("arena: water " + s.Name + " must be at least 3 x 4 m");
                JsonValue b = water[i]["bridges"];
                s.Bridges = new int[b.Count];
                for (int k = 0; k < b.Count; k++)
                {
                    s.Bridges[k] = b[k].AsInt();
                    if (s.Bridges[k] < s.Rect.X0 + 1 || s.Bridges[k] + BridgeDeckM > s.Rect.X1 - 1 || (k > 0 && s.Bridges[k] < s.Bridges[k - 1] + BridgeDeckM + 2))
                        throw new FormatException("arena: water " + s.Name + "'s bridge at " + s.Bridges[k] + " must lie inside its banks, west to east, apart");
                }
                Streams[i] = s;
            }
            foreach (Cliff c in Cliffs)
                foreach (WaterChannel s in Streams)
                    if (c.Rect.X0 < s.Rect.X1 + 1 && s.Rect.X0 - 1 < c.Rect.X1 && c.Rect.Y0 < s.Rect.Y1 + 1 && s.Rect.Y0 - 1 < c.Rect.Y1)
                        throw new FormatException("arena: cliff " + c.Name + " and water " + s.Name + " overlap or touch");
        }

        /// <summary>How far p lies from water or a cliff (0 inside).</summary>
        public float TerrainDistance(Vector2 p)
        {
            float d = float.MaxValue;
            foreach (Cliff c in Cliffs) d = Math.Min(d, c.Rect.Distance(p));
            foreach (WaterChannel s in Streams) d = Math.Min(d, s.Rect.Distance(p));
            return d;
        }

        /// <summary>
        /// The layout's placement list without the scatter that falls in water or on a cliff, then the
        /// terrain obstacles: each cliff's rectangle, and each stream's channel cut at its bridge decks.
        /// </summary>
        void Generate()
        {
            PlacementList gen = Layout.Generate(Footprints);
            var props = new List<Footprint>(gen.Props);
            var items = new List<Placement>(gen.Items.Length);
            foreach (Placement p in gen.Items)
            {
                Footprint f = gen.Props[p.Prop];
                if (!Sprites.ContainsKey(f.Id)) throw new FormatException("forest props: '" + f.Id + "' has no sprite");
                if (p.Area < 0 && TerrainDistance(p.Position) < Math.Max(f.BoundingRadius * p.Scale, 0.2f) + TerrainClearM) continue;
                items.Add(p);
            }
            TerrainStart = items.Count;
            void Obstacle(string id, TileRect r, float x0, float x1)
            {
                Vector2 c = new Vector2((x0 + x1) / 2, r.Center.Y);
                float hx = (x1 - x0) / 2, hy = r.H / 2f;
                props.Add(new Footprint
                {
                    Id = id, Guid = id, Blocks = true, Shape = FootprintShape.Polygon, HeightM = 3,
                    Points = new[] { new Vector2(-hx, -hy), new Vector2(hx, -hy), new Vector2(hx, hy), new Vector2(-hx, hy) },
                });
                items.Add(new Placement(props.Count - 1, c, 0, 1, -1));
            }
            foreach (Cliff c in Cliffs) Obstacle("terrain/cliff/" + c.Name, c.Rect, c.Rect.X0, c.Rect.X1);
            foreach (WaterChannel s in Streams)
            {
                int x = s.Rect.X0;
                for (int k = 0; k <= s.Bridges.Length; k++)
                {
                    int end = k < s.Bridges.Length ? s.Bridges[k] : s.Rect.X1;
                    Obstacle("terrain/water/" + s.Name + "/" + k, s.Rect, x, end);
                    if (k < s.Bridges.Length) x = s.Bridges[k] + BridgeDeckM;
                }
            }
            Placements = new PlacementList
            {
                Layout = gen.Layout, Seed = gen.Seed, Size = gen.Size, DecorationDensity = gen.DecorationDensity,
                Props = props.ToArray(), Items = items.ToArray(), HeroStart = gen.HeroStart, Goals = gen.Goals,
            };
            Ground = new GroundLayout { Name = Layout.Name, HeroStart = Layout.HeroStart, Goals = gen.Goals, Arena = Layout, Placements = Placements };
            foreach (GoalArea g in gen.Goals)
                if (TerrainDistance(g.Center) <= 0) throw new FormatException("arena: goal " + g.Name + " lies in water or on a cliff");
            if (TerrainDistance(Layout.HeroStart) <= 0) throw new FormatException("arena: the hero starts in water or on a cliff");
        }

        // ------------------------------------------------------------------ ground

        /// <summary>
        /// The ground material at each tile corner, (TilesW + 1) × (TilesH + 1) from the south-west, row
        /// by row: 0 base, k the material k-1. The forest fill's ground everywhere, then each area's
        /// over its shape in order, edges moved by seeded noise; base within a tile of water.
        /// </summary>
        public byte[] GroundCorners()
        {
            int w = TilesW + 1, h = TilesH + 1;
            var m = new byte[w * h];
            int fill = 0;
            for (int i = 0; i < Layout.Areas.Length; i++) if (Layout.Areas[i].Fill && AreaGround[i] >= 0) fill = AreaGround[i];
            for (int k = 0; k < m.Length; k++) m[k] = (byte)fill;
            for (int i = 0; i < Layout.Areas.Length; i++)
            {
                LayoutArea a = Layout.Areas[i];
                if (AreaGround[i] < 0 || a.Fill) continue;
                for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    var p = new Vector2(OriginX + x, OriginY + y);
                    float d = a.IsCapsule ? ArenaLayout.SegmentDistance(p, a.From, a.To) - a.Width / 2 : Vector2.Distance(p, a.Center) - a.Radius;
                    if (d + Wobble(x, y, (uint)i) <= 0) m[y * w + x] = (byte)AreaGround[i];
                }
            }
            foreach (WaterChannel s in Streams)
                for (int y = s.Rect.Y0 - 1; y <= s.Rect.Y1 + 1; y++)
                for (int x = s.Rect.X0 - 1; x <= s.Rect.X1 + 1; x++)
                {
                    int cx = x - OriginX, cy = y - OriginY;
                    if (cx >= 0 && cy >= 0 && cx < w && cy < h) m[cy * w + cx] = 0;
                }
            return m;
        }

        /// <summary>Value noise on a 3 m lattice, in [-WobbleM, WobbleM], different per area.</summary>
        float Wobble(int x, int y, uint area) => (ValueNoise(Layout.Seed, x, y, 3, 0xA2E0 + area) * 2 - 1) * WobbleM;

        /// <summary>Smoothed value noise in [0, 1] over whole-metre (x, y) ≥ 0, on a lattice of <paramref name="lattice"/> metres.</summary>
        public static float ValueNoise(uint seed, int x, int y, int lattice, uint salt)
        {
            int x0 = Math.DivRem(x, lattice, out int rx), y0 = Math.DivRem(y, lattice, out int ry);
            float fx = rx / (float)lattice, fy = ry / (float)lattice;
            fx = fx * fx * (3 - 2 * fx); fy = fy * fy * (3 - 2 * fy);
            float N(int i, int j) => (Rng.Hash(seed, (uint)i * 0x9E3779B1u ^ (uint)j, salt) >> 8) * (1f / (1 << 24));
            return (N(x0, y0) * (1 - fx) + N(x0 + 1, y0) * fx) * (1 - fy) + (N(x0, y0 + 1) * (1 - fx) + N(x0 + 1, y0 + 1) * fx) * fy;
        }
    }
}
