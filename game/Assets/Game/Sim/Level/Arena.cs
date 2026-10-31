using System;
using System.Collections.Generic;
using PS.Game.Sim.Content;

namespace PS.Game.Sim.Level
{
    /// <summary>A tile of the hero's grid: column <see cref="X"/> (east) and row <see cref="Y"/> (north).</summary>
    public struct Tile
    {
        public int X, Y;

        public Tile(int x, int y)
        {
            X = x;
            Y = y;
        }
    }

    /// <summary>A crystal structure's site (unity-game.md, "Crystal Structures"): the tiles it blocks while standing.</summary>
    public sealed class StructureSite
    {
        public Tile[] Tiles;
        /// <summary>A large structure takes two or three breaking hits; a small one, one.</summary>
        public bool Large;
    }

    /// <summary>
    /// The level data's arena part (unity-game.md, "The Arena" and "Hero Movement on the Grid"): a
    /// rectangle of square tiles aligned with the arena, the tiles its dressing makes impassable,
    /// the crystal structures' sites and the hero's start. The editor's arena exports it as JSON:
    ///
    /// <code>
    /// { "tileM": 1.5, "origin": [-120, -120], "size": [160, 160], "heroStart": [80, 80],
    ///   "blocked": [[0, 5], [0, 6]],
    ///   "structures": [{ "tiles": [[90, 80], [90, 81]], "large": true }] }
    /// </code>
    ///
    /// <c>origin</c> is tile (0, 0)'s south-west corner in ground-plane metres, and <c>size</c> the
    /// width and height in tiles. Everything outside the rectangle is the impassable edge.
    /// </summary>
    public sealed class ArenaData
    {
        public double TileM = 1.5;
        public double OriginX, OriginY;
        public int Width, Height;
        public Tile HeroStart;
        public Tile[] Blocked = new Tile[0];
        public StructureSite[] Structures = new StructureSite[0];

        /// <summary>
        /// An open arena <paramref name="width"/> by <paramref name="height"/> tiles, centred on the
        /// origin, with the hero in the middle. The default, about 240 m square, is the doc's.
        /// </summary>
        public static ArenaData Open(int width = 160, int height = 160, double tileM = 1.5) => new ArenaData
        {
            TileM = tileM,
            OriginX = -0.5 * width * tileM,
            OriginY = -0.5 * height * tileM,
            Width = width,
            Height = height,
            HeroStart = new Tile(width / 2, height / 2),
        };

        /// <summary>Parses and checks the arena's JSON; bad data fails with a <see cref="ContentException"/>.</summary>
        public static ArenaData Parse(string json, string file = "arena")
        {
            JsonValue value;
            try { value = JsonValue.Parse(json); }
            catch (FormatException e) { throw new ContentException(file + ": " + e.Message, e); }
            var r = new ContentReader(value, file);
            double[] origin = Pair(r.Numbers("origin"), r.Child("origin"));
            int[] size = Pair(r.Ints("size"), r.Child("size"));
            var a = new ArenaData
            {
                TileM = r.Number("tileM"),
                OriginX = origin[0],
                OriginY = origin[1],
                Width = size[0],
                Height = size[1],
                HeroStart = TileAt(r, "heroStart"),
                Blocked = Tiles(r, "blocked"),
                Structures = r.Objects("structures", s => new StructureSite { Tiles = Tiles(s, "tiles"), Large = s.Bool("large") }),
            };
            r.End();
            a.Check(file);
            return a;
        }

        /// <summary>Refuses a shape the sim can't use: no tiles, a tile outside the arena, or a hero who starts blocked.</summary>
        public void Check(string file = "arena")
        {
            if (!(TileM > 0) || Width <= 0 || Height <= 0 || double.IsNaN(OriginX) || double.IsNaN(OriginY))
                throw new ContentException(file + ": expected a positive tile size and width and height");
            var taken = new HashSet<(int, int)>();
            void Inside(Tile t, string path, bool block)
            {
                if (t.X < 0 || t.Y < 0 || t.X >= Width || t.Y >= Height)
                    throw new ContentException(file + ": " + path + ": tile (" + t.X + ", " + t.Y + ") is outside the arena");
                if (block && !taken.Add((t.X, t.Y)))
                    throw new ContentException(file + ": " + path + ": tile (" + t.X + ", " + t.Y + ") is blocked twice");
            }
            for (int i = 0; i < Blocked.Length; i++) Inside(Blocked[i], "blocked[" + i + "]", true);
            for (int i = 0; i < Structures.Length; i++)
            {
                if (Structures[i].Tiles.Length == 0) throw new ContentException(file + ": structures[" + i + "]: no tiles");
                for (int j = 0; j < Structures[i].Tiles.Length; j++)
                    Inside(Structures[i].Tiles[j], "structures[" + i + "].tiles[" + j + "]", true);
            }
            Inside(HeroStart, "heroStart", false);
            if (taken.Contains((HeroStart.X, HeroStart.Y))) throw new ContentException(file + ": heroStart: the tile is blocked");
        }

        static T[] Pair<T>(T[] items, string path) =>
            items.Length == 2 ? items : throw ContentReader.Fail(path, "expected two numbers");

        static Tile TileAt(ContentReader r, string key)
        {
            int[] xy = Pair(r.Ints(key), r.Child(key));
            return new Tile(xy[0], xy[1]);
        }

        static Tile[] Tiles(ContentReader r, string key)
        {
            List<JsonValue> items = r.Array(key);
            var tiles = new Tile[items.Count];
            for (int i = 0; i < items.Count; i++)
            {
                var xy = items[i];
                string path = r.Child(key) + "[" + i + "]";
                bool ok = xy.Type == JsonType.Array && xy.Items.Count == 2;
                for (int k = 0; ok && k < 2; k++)
                    ok = xy.Items[k].Type == JsonType.Number && xy.Items[k].Number == Math.Floor(xy.Items[k].Number)
                        && Math.Abs(xy.Items[k].Number) <= int.MaxValue;
                if (!ok) throw ContentReader.Fail(path, "expected a tile [x, y] of two integers");
                tiles[i] = new Tile((int)xy.Items[0].Number, (int)xy.Items[1].Number);
            }
            return tiles;
        }
    }

    /// <summary>
    /// The arena as the sim plays it: which tiles the hero may enter, and where they are. Only the
    /// sim changes it (structures break and regrow); the client reads it.
    /// </summary>
    public sealed class ArenaGrid
    {
        const byte Open = 0, Edge = 1, Structure = 2;

        readonly byte[] cells;

        public ArenaGrid(ArenaData data)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            data.Check();
            TileM = data.TileM;
            OriginX = data.OriginX;
            OriginY = data.OriginY;
            Width = data.Width;
            Height = data.Height;
            HeroStart = data.HeroStart;
            cells = new byte[Width * Height];
            foreach (var t in data.Blocked) cells[t.Y * Width + t.X] = Edge;
            foreach (var site in data.Structures)
                foreach (var t in site.Tiles) cells[t.Y * Width + t.X] = Structure;
        }

        public double TileM { get; }
        /// <summary>Tile (0, 0)'s south-west corner, ground-plane metres.</summary>
        public double OriginX { get; }
        public double OriginY { get; }
        public int Width { get; }
        public int Height { get; }
        public Tile HeroStart { get; }

        /// <summary>May the hero enter tile (x, y)? Not outside the arena, on its dressing, or on a standing structure.</summary>
        public bool IsOpen(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height && cells[y * Width + x] == Open;

        /// <summary>A tile's centre, ground-plane metres.</summary>
        public double CentreX(int x) => OriginX + (x + 0.5) * TileM;
        public double CentreY(int y) => OriginY + (y + 0.5) * TileM;

        /// <summary>A structure's tiles turn impassable as it regrows, or open as it breaks.</summary>
        internal void SetStructure(Tile[] tiles, bool standing)
        {
            foreach (var t in tiles) cells[t.Y * Width + t.X] = standing ? Structure : Open;
        }
    }
}
