using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PS.Choreography;
using UnityEditor;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace Demo2D.EditorTools.Assets
{
    /// <summary>
    /// The Ancient Forest's MainLev sheet as Data/forest-tiles.json describes it, and PX2's tile
    /// test map: every authored stamp as is, and a plus-shaped patch from each of the 14
    /// transition sets, corner-autotiled so it uses convex corners, concave corners, edges and
    /// fills, with the variants mixed. All of it over a base grass layer, in two Tilemaps.
    /// </summary>
    public static class ForestTiles
    {
        public const string CatalogPath = CharacterPacks.DataDir + "/forest-tiles.json";

        public sealed class Catalog
        {
            public string Sheet, Props;
            public int TilePx;
            public Vector2Int Base;
            public List<(string name, Vector2Int at)> Transitions = new List<(string, Vector2Int)>();
            public List<(string name, RectInt rect)> Stamps = new List<(string, RectInt)>();
        }

        public static Catalog Load()
        {
            var j = JsonValue.Parse(File.ReadAllText(CatalogPath));
            Vector2Int Cell(JsonValue v) => new Vector2Int(v["col"].AsInt(), v["row"].AsInt());
            var c = new Catalog
            {
                Sheet = j["sheet"].AsString(),
                Props = j["props"].AsString(),
                TilePx = j["tilePx"].AsInt(),
                Base = Cell(j["base"]),
            };
            foreach (var t in j["transitions"].Items) c.Transitions.Add((t["name"].AsString(), Cell(t)));
            foreach (var s in j["stamps"].Items)
                c.Stamps.Add((s["name"].AsString(), new RectInt(s["col"].AsInt(), s["row"].AsInt(), s["w"].AsInt(), s["h"].AsInt())));
            return c;
        }

        /// <summary>The sheet's sprites by tile (column, row from the top).</summary>
        public static Dictionary<Vector2Int, Sprite> Sprites(string sheet, int tilePx)
        {
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(sheet) ?? throw new FileNotFoundException("no texture " + sheet);
            var map = new Dictionary<Vector2Int, Sprite>();
            foreach (var s in AssetDatabase.LoadAllAssetsAtPath(sheet).OfType<Sprite>())
            {
                var r = s.rect;
                map[new Vector2Int((int)r.x / tilePx, (tex.height - (int)r.yMax) / tilePx)] = s;
            }
            return map;
        }

        // Corner patterns: a bit per tile corner that lies in the inner terrain.
        public const int TL = 1, TR = 2, BL = 4, BR = 8;
        public static readonly int[] Diagonals = { TL | BR, TR | BL };

        /// <summary>
        /// The tiles of one transition set by corner pattern. In the template at column 0 a corner
        /// is inner when it lies strictly inside the 5×5; in the template at column 5, when it doesn't.
        /// </summary>
        public static Dictionary<int, List<Vector2Int>> CornerTiles(Vector2Int set)
        {
            var tiles = new Dictionary<int, List<Vector2Int>>();
            for (int t = 0; t < 2; t++)
            for (int j = 0; j < 5; j++)
            for (int i = 0; i < 5; i++)
            {
                bool Inner(int vx, int vy) => (vx >= 1 && vx <= 4 && vy >= 1 && vy <= 4) == (t == 0);
                int key = (Inner(i, j) ? TL : 0) | (Inner(i + 1, j) ? TR : 0) | (Inner(i, j + 1) ? BL : 0) | (Inner(i + 1, j + 1) ? BR : 0);
                if (!tiles.TryGetValue(key, out var list)) tiles[key] = list = new List<Vector2Int>();
                list.Add(new Vector2Int(set.x + 5 * t + i, set.y + 2 + j));
            }
            return tiles;
        }

        /// <summary>A map of sheet tiles, rows from the top: a base tile everywhere, overlay tiles where set.</summary>
        public sealed class TestMap
        {
            public int W, H;
            public Vector2Int Base;
            public Vector2Int?[,] Overlay;
            public List<(string name, RectInt rect)> Regions = new List<(string, RectInt)>();
        }

        const int Block = 9, BlockH = 8, PerRow = 4;

        public static TestMap BuildTestMap(Catalog c)
        {
            var water = c.Stamps[1].rect;
            int setsX = water.width + 1;
            int rows = (c.Transitions.Count + PerRow - 1) / PerRow;
            var m = new TestMap
            {
                W = Mathf.Max(c.Stamps[0].rect.width, setsX + PerRow * Block),
                H = c.Stamps[0].rect.height + 1 + Mathf.Max(water.height, rows * BlockH),
                Base = c.Base,
            };
            m.Overlay = new Vector2Int?[m.W, m.H];

            // The stamps: the first across the top, the second below it on the left.
            var at = new[] { new Vector2Int(0, 0), new Vector2Int(0, c.Stamps[0].rect.height + 1) };
            for (int s = 0; s < c.Stamps.Count; s++)
            {
                var r = c.Stamps[s].rect;
                for (int y = 0; y < r.height; y++)
                for (int x = 0; x < r.width; x++)
                    m.Overlay[at[s].x + x, at[s].y + y] = new Vector2Int(r.x + x, r.y + y);
                m.Regions.Add((c.Stamps[s].name, new RectInt(at[s].x, at[s].y, r.width, r.height)));
            }

            // A plus of inner terrain on outer in each transition set's block: its vertex grid is
            // (Block+1)×(BlockH+1), inner where it falls in either bar of the plus.
            for (int k = 0; k < c.Transitions.Count; k++)
            {
                var (name, set) = c.Transitions[k];
                var tiles = CornerTiles(set);
                int ox = setsX + (k % PerRow) * Block, oy = c.Stamps[0].rect.height + 1 + (k / PerRow) * BlockH;
                bool Inner(int vx, int vy) => (vx >= 1 && vx <= Block - 1 && vy >= 3 && vy <= BlockH - 3)
                                              || (vx >= 3 && vx <= Block - 3 && vy >= 1 && vy <= BlockH - 1);
                for (int y = 0; y < BlockH; y++)
                for (int x = 0; x < Block; x++)
                {
                    int key = (Inner(x, y) ? TL : 0) | (Inner(x + 1, y) ? TR : 0) | (Inner(x, y + 1) ? BL : 0) | (Inner(x + 1, y + 1) ? BR : 0);
                    var options = tiles[key];
                    m.Overlay[ox + x, oy + y] = options[Hash(ox + x, oy + y) % options.Count];
                }
                m.Regions.Add((name, new RectInt(ox, oy, Block, BlockH)));
            }
            return m;
        }

        static int Hash(int x, int y) => (int)((uint)(x * 73856093 ^ y * 19349663) % 1000003);

        /// <summary>Builds the map as a Grid with a base and an overlay Tilemap, lit; returns its root.
        /// Tile (x, row) of the map covers world [x, x+1] × [H-1-row, H-row].</summary>
        public static GameObject Instantiate(TestMap m, Catalog c)
        {
            var sprites = Sprites(c.Sheet, c.TilePx);
            var tiles = new Dictionary<Vector2Int, Tile>();
            Tile TileFor(Vector2Int cell)
            {
                if (tiles.TryGetValue(cell, out var t)) return t;
                t = ScriptableObject.CreateInstance<Tile>();
                sprites.TryGetValue(cell, out var sprite); // an empty cell of the sheet has no sprite
                t.sprite = sprite;
                return tiles[cell] = t;
            }
            var root = new GameObject("Tile Test Map");
            root.AddComponent<Grid>().cellSize = Vector3.one;
            Tilemap Layer(string name, int order)
            {
                var go = new GameObject(name);
                go.transform.SetParent(root.transform, false);
                var map = go.AddComponent<Tilemap>();
                var r = go.AddComponent<TilemapRenderer>();
                r.sharedMaterial = PixelShots.SpriteLit;
                r.sortingOrder = order;
                r.mode = TilemapRenderer.Mode.Chunk;
                return map;
            }
            var ground = Layer("Base", 0);
            var overlay = Layer("Overlay", 1);
            for (int row = 0; row < m.H; row++)
            for (int x = 0; x < m.W; x++)
            {
                var p = new Vector3Int(x, m.H - 1 - row, 0);
                ground.SetTile(p, TileFor(m.Base));
                if (m.Overlay[x, row] is Vector2Int cell) overlay.SetTile(p, TileFor(cell));
            }
            return root;
        }

        /// <summary>The map composed on the CPU from the sheet's file: what a seamless render shows.</summary>
        public static Color32[] Compose(TestMap m, Catalog c)
        {
            var sheet = CharacterPacks.ReadPng(c.Sheet);
            try
            {
                var src = sheet.GetPixels32();
                int t = c.TilePx, w = m.W * t, h = m.H * t;
                var px = new Color32[w * h];
                void Draw(Vector2Int cell, int x, int row, bool over)
                {
                    int sx = cell.x * t, sy = sheet.height - (cell.y + 1) * t, dx = x * t, dy = (m.H - 1 - row) * t;
                    for (int yy = 0; yy < t; yy++)
                    for (int xx = 0; xx < t; xx++)
                    {
                        var s = src[(sy + yy) * sheet.width + sx + xx];
                        if (over && s.a == 0) continue;
                        px[(dy + yy) * w + dx + xx] = s;
                    }
                }
                for (int row = 0; row < m.H; row++)
                for (int x = 0; x < m.W; x++)
                {
                    Draw(m.Base, x, row, false);
                    if (m.Overlay[x, row] is Vector2Int cell) Draw(cell, x, row, true);
                }
                return px;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(sheet);
            }
        }
    }
}
