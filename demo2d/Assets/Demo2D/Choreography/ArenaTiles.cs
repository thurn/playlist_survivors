using System;
using System.Collections.Generic;
using PS.Choreography;

namespace Demo2D.Choreography
{
    /// <summary>
    /// The arena's Tilemaps (PX6), composed from the <see cref="ForestArena"/> and the MainLev sheet's
    /// catalogue (Data/forest-tiles.json): a layer of base grass, one corner-autotiled overlay per
    /// ground material, the cliffs and water channels, then the bridges over them. Each layer holds a
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

            for (int y = 0; y < t.H; y++)
            for (int x = 0; x < t.W; x++)
                t.Set(0, x, y, sheet.Base);

            byte[] corners = arena.GroundCorners();
            int cw = t.W + 1;
            for (int k = 0; k < arena.Materials.Length; k++)
            {
                var tiles = sheet.CornerTiles(arena.Materials[k].set);
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
                    var options = tiles[key];
                    t.Set(1 + k, x, y, options[(int)(Rng.Hash(arena.Layout.Seed, (uint)(y * t.W + x), 0x711E + (uint)k) % (uint)options.Count)]);
                }
            }

            int terrain = names.Count - 2, bridges = names.Count - 1;
            foreach (Cliff c in arena.Cliffs)
            {
                var b = sheet.Plateau;
                Stamp(t, arena, terrain, c.Rect, (cx, ry, w, h) =>
                    (b.col + Slice(cx, w, 1, 3, 1), b.row + (ry < h - 3 ? Slice(ry, h - 3, 1, 3, 0) : 4 + ry - (h - 3))));
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
        public (int col, int row) Plateau, Channel, Bridge;
        public readonly Dictionary<string, (int col, int row)> Transitions = new Dictionary<string, (int, int)>(StringComparer.Ordinal);

        public static TileSheet Parse(string json)
        {
            JsonValue j = JsonValue.Parse(json);
            (int, int) Cell(JsonValue v) => (v["col"].AsInt(), v["row"].AsInt());
            var s = new TileSheet { Base = Cell(j["base"]) };
            foreach (JsonValue t in j["transitions"].Items) s.Transitions[t["name"].AsString()] = Cell(t);
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
