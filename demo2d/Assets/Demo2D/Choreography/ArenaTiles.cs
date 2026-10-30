using System;
using System.Collections.Generic;
using PS.Choreography;

namespace Demo2D.Choreography
{
    /// <summary>
    /// The arena's Tilemaps (PX6), composed from the <see cref="ForestArena"/> and the MainLev sheet's
    /// catalogue (Data/forest-tiles.json): a layer of base grass, one corner-autotiled overlay per
    /// ground material, the cliffs and water channels, then the bridges over them. A tile wholly of
    /// one ground takes one of its plain variants, or, in seeded patches, one of its detail tiles
    /// (twigs, pebbles, stones: PX9), so no single tile repeats across a clearing. Each layer holds a
    /// sheet tile (column, row from the sheet's top-left) per arena tile, or none. A pure function of
    /// the JSON, built at load like the sim's placements, so what is drawn is never staler than what is
    /// navigated.
    /// </summary>
    public sealed class ArenaTiles
    {
        public const int None = -1;

        public readonly int W, H;
        public readonly string[] LayerNames;
        /// <summary>Per layer, per tile (row 0 the southmost): an index into <see cref="Cells"/>, or <see cref="None"/>.</summary>
        public readonly int[][] Layers;
        /// <summary>The distinct sheet tiles the layers use, as (column, row from the top).</summary>
        public readonly List<(int col, int row)> Cells = new List<(int, int)>();
        readonly Dictionary<(int, int), int> _index = new Dictionary<(int, int), int>();

        ArenaTiles(int w, int h, string[] names)
        {
            W = w;
            H = h;
            LayerNames = names;
            Layers = new int[names.Length][];
            for (int i = 0; i < Layers.Length; i++)
            {
                Layers[i] = new int[w * h];
                for (int k = 0; k < Layers[i].Length; k++) Layers[i][k] = None;
            }
        }

        void Set(int layer, int x, int y, (int col, int row) cell)
        {
            if (x < 0 || y < 0 || x >= W || y >= H) return;
            if (!_index.TryGetValue(cell, out int i)) { i = Cells.Count; Cells.Add(cell); _index[cell] = i; }
            Layers[layer][y * W + x] = i;
        }

        public static ArenaTiles Compose(ForestArena arena, TileSheet sheet)
        {
            var names = new List<string> { "base" };
            foreach (var m in arena.Materials) names.Add(m.name);
            names.Add("terrain");
            names.Add("bridges");
            var t = new ArenaTiles(arena.TilesW, arena.TilesH, names.ToArray());

            uint seed = arena.Layout.Seed;
            var basePlain = sheet.BaseSet != null ? sheet.CornerTiles(sheet.BaseSet)[0] : new List<(int, int)> { sheet.Base };
            var baseDetails = sheet.DetailsOf(sheet.BaseSet);
            for (int y = 0; y < t.H; y++)
            for (int x = 0; x < t.W; x++)
                t.Set(0, x, y, Whole(seed, x, y, 0x6A5E, basePlain, baseDetails));

            byte[] corners = arena.GroundCorners();
            int cw = t.W + 1;
            for (int k = 0; k < arena.Materials.Length; k++)
            {
                var tiles = sheet.CornerTiles(arena.Materials[k].set);
                var details = sheet.DetailsOf(arena.Materials[k].set);
                byte mat = (byte)(k + 1);
                for (int y = 0; y < t.H; y++)
                for (int x = 0; x < t.W; x++)
                {
                    // The set's inner terrain is clear: a corner is inner where it isn't this material.
                    bool Clear(int cx, int cy) => corners[cy * cw + cx] != mat;
                    int key = (Clear(x, y + 1) ? TileSheet.TL : 0) | (Clear(x + 1, y + 1) ? TileSheet.TR : 0) |
                              (Clear(x, y) ? TileSheet.BL : 0) | (Clear(x + 1, y) ? TileSheet.BR : 0);
                    if (key == 15) continue;
                    if (Array.IndexOf(TileSheet.Diagonals, key) >= 0) key = 0;   // no diagonal tiles: fill
                    if (key == 0)
                    {
                        t.Set(1 + k, x, y, Whole(seed, x, y, 0x711E + (uint)k, tiles[0], details));
                        continue;
                    }
                    var options = tiles[key];
                    t.Set(1 + k, x, y, options[(int)(Rng.Hash(arena.Layout.Seed, (uint)(y * t.W + x), 0x711E + (uint)k) % (uint)options.Count)]);
                }
            }

            int terrain = names.Count - 2, bridges = names.Count - 1;
            foreach (Cliff c in arena.Cliffs)
            {
                var b = sheet.Plateau;
                // The plateau's top inside its rims is the base grass (the block's own is that tile), so it
                // varies as the ground does instead of repeating the block's 3×3.
                Stamp(t, arena, terrain, c.Rect, (cx, ry, w, h) =>
                    cx > 0 && cx < w - 1 && ry > 0 && ry < h - 3
                        ? Whole(seed, c.Rect.X0 + cx - arena.OriginX, c.Rect.Y1 - 1 - ry - arena.OriginY, 0x6A5E, basePlain, baseDetails)
                        : (b.col + Slice(cx, w, 1, 3, 1), b.row + (ry < h - 3 ? Slice(ry, h - 3, 1, 3, 0) : 4 + ry - (h - 3))));
            }
            foreach (WaterChannel s in arena.Streams)
            {
                var b = sheet.Channel;
                Stamp(t, arena, terrain, s.Rect, (cx, ry, w, h) =>
                    (b.col + Slice(cx, w, 1, 3, 1), b.row + (ry < 3 ? ry : ry == h - 1 ? 4 : 3)));
                var br = sheet.Bridge;
                foreach (int deck in s.Bridges)
                {
                    // Its posts a tile beyond the banks; its deck's middle rows repeat.
                    var r = new TileRect(deck - 1, s.Rect.Y0 - 1, deck + ForestArena.BridgeDeckM + 1, s.Rect.Y1 + 1);
                    Stamp(t, arena, bridges, r, (cx, ry, w, h) =>
                        (br.col + cx, br.row + (ry < 2 ? ry : ry >= h - 2 ? 5 + ry - (h - 2) : 2 + (ry - 2) % 3)));
                }
            }
            return t;
        }

        /// <summary>
        /// A tile wholly of one ground: one of its <paramref name="plain"/> variants, or in patches (value
        /// noise on a 5 m lattice, up to <see cref="DetailShare"/> of the tiles where it peaks) a tile of
        /// one of its 3×3 <paramref name="details"/> blocks, the block chosen by a broader noise so each
        /// patch keeps to one kind of detail.
        /// </summary>
        static (int col, int row) Whole(uint seed, int x, int y, uint salt, List<(int col, int row)> plain, List<(int col, int row)> details)
        {
            uint h = Rng.Hash(seed, (uint)(y * 4096 + x), salt);
            float share = Math.Min(Math.Max((ForestArena.ValueNoise(seed, x, y, 5, salt + 1) - 0.45f) * 2.4f, 0), DetailShare);
            if (details.Count > 0 && (h & 0xFFFF) / 65536f < share)
            {
                int b = Math.Min((int)(ForestArena.ValueNoise(seed, x, y, 11, salt + 2) * details.Count), details.Count - 1);
                return (details[b].col + (int)(h >> 16) % 3, details[b].row + (int)(h >> 24) % 3);
            }
            return plain[(int)((h >> 16) % (uint)plain.Count)];
        }

        const float DetailShare = 0.7f;

        /// <summary>
        /// A block's column (or row) for position i of n: its first edge, then its middle repeated, then
        /// its last edge (<paramref name="last"/> 0: no last edge).
        /// </summary>
        static int Slice(int i, int n, int first, int middle, int last)
        {
            if (i < first) return i;
            if (last > 0 && i >= n - last) return first + middle + (i - (n - last));
            return first + (i - first) % middle;
        }

        /// <summary>Fills <paramref name="r"/> on a layer: cell(column from the west, row from the north, w, h).</summary>
        static void Stamp(ArenaTiles t, ForestArena arena, int layer, TileRect r, Func<int, int, int, int, (int, int)> cell)
        {
            for (int ry = 0; ry < r.H; ry++)
            for (int cx = 0; cx < r.W; cx++)
                t.Set(layer, r.X0 + cx - arena.OriginX, r.Y1 - 1 - ry - arena.OriginY, cell(cx, ry, r.W, r.H));
        }
    }

    /// <summary>The MainLev sheet's catalogue (Data/forest-tiles.json), engine-free.</summary>
    public sealed class TileSheet
    {
        // Corner patterns: a bit per tile corner that lies in the inner terrain.
        public const int TL = 1, TR = 2, BL = 4, BR = 8;
        public static readonly int[] Diagonals = { TL | BR, TR | BL };

        public (int col, int row) Base;
        /// <summary>The transition set whose outer terrain is the base grass: its plain tiles vary the base (null: <see cref="Base"/> alone).</summary>
        public string BaseSet;
        public (int col, int row) Plateau, Channel, Bridge;
        public readonly Dictionary<string, (int col, int row)> Transitions = new Dictionary<string, (int, int)>(StringComparer.Ordinal);
        /// <summary>Per transition set, its outer terrain's 3×3 detail blocks (their top-left cells).</summary>
        public readonly Dictionary<string, List<(int col, int row)>> Details = new Dictionary<string, List<(int, int)>>(StringComparer.Ordinal);

        public List<(int col, int row)> DetailsOf(string set) =>
            set != null && Details.TryGetValue(set, out var d) ? d : new List<(int, int)>();

        public static TileSheet Parse(string json)
        {
            JsonValue j = JsonValue.Parse(json);
            (int, int) Cell(JsonValue v) => (v["col"].AsInt(), v["row"].AsInt());
            var s = new TileSheet { Base = Cell(j["base"]) };
            foreach (JsonValue t in j["transitions"].Items) s.Transitions[t["name"].AsString()] = Cell(t);
            if (j.Has("baseSet")) s.BaseSet = j["baseSet"].AsString();
            if (j.Has("details"))
                foreach (var (set, blocks) in j["details"].Members)
                {
                    if (!s.Transitions.ContainsKey(set)) throw new FormatException("forest tiles: details for unknown set '" + set + "'");
                    var list = new List<(int, int)>();
                    foreach (JsonValue block in blocks.Items) list.Add(Cell(block));
                    s.Details[set] = list;
                }
            JsonValue b = j["blocks"];
            s.Plateau = Cell(b["plateau"]);
            s.Channel = Cell(b["channel"]);
            s.Bridge = Cell(b["bridge"]);
            return s;
        }

        public Dictionary<int, List<(int col, int row)>> CornerTiles(string set) =>
            Transitions.TryGetValue(set, out var at) ? CornerTiles(at) : throw new FormatException("forest tiles: no transition set '" + set + "'");

        /// <summary>
        /// The tiles of one transition set by corner pattern. In the template at column 0 a corner
        /// is inner when it lies strictly inside the 5×5; in the template at column 5, when it doesn't.
        /// </summary>
        public static Dictionary<int, List<(int col, int row)>> CornerTiles((int col, int row) set)
        {
            var tiles = new Dictionary<int, List<(int, int)>>();
            for (int t = 0; t < 2; t++)
            for (int j = 0; j < 5; j++)
            for (int i = 0; i < 5; i++)
            {
                bool Inner(int vx, int vy) => (vx >= 1 && vx <= 4 && vy >= 1 && vy <= 4) == (t == 0);
                int key = (Inner(i, j) ? TL : 0) | (Inner(i + 1, j) ? TR : 0) | (Inner(i, j + 1) ? BL : 0) | (Inner(i + 1, j + 1) ? BR : 0);
                if (!tiles.TryGetValue(key, out var list)) tiles[key] = list = new List<(int, int)>();
                list.Add((set.col + 5 * t + i, set.row + 2 + j));
            }
            return tiles;
        }
    }
}
