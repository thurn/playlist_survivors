using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using System.Text;
using PS.Game.Sim.Level;

namespace PS.Game.Arena
{
    /// <summary>
    /// The level data's arena part (unity-game.md, "The Arena"; the sim's <see cref="ArenaData"/>)
    /// from the layout's placement list, on the hero's grid of <see cref="ArenaLayout.TileM"/> tiles
    /// over the whole ground:
    /// - <b>blocked</b>: the forest (every tile past its fighting ground's edge band, so the arena's
    ///   bounds and its groves are impassable however the trees fall) and the tiles under every other
    ///   blocking piece: edge-band bushes and logs, landmarks;
    /// - <b>structures</b>: the crystal structures' sites, the tiles under a structure area's blocking
    ///   pieces, each set of pieces that share a tile one site, <c>large</c> from
    ///   <see cref="LargeTiles"/> tiles up;
    /// - the hero's start, the tile under the layout's <c>heroStart</c>.
    /// A piece covers a tile when its footprint reaches into the tile's middle, the tile less
    /// <see cref="InsetM"/> on every side, so a wall grazing a tile's edge leaves it open.
    /// The editor's bake writes it as game/Content/arena.json (<see cref="ToJson"/>), and the scene
    /// groups each site's pieces as <see cref="SiteOf"/> says.
    /// </summary>
    public sealed class LevelExport
    {
        /// <summary>How far a footprint may reach into a tile, from any side, and leave it open.</summary>
        public const float InsetM = 0.25f;

        /// <summary>A site of at least this many tiles is large: it takes two or three breaking hits.</summary>
        public const int LargeTiles = 6;

        public readonly ArenaData Data;

        /// <summary>For each placement, the index of the structure site it belongs to, or -1.</summary>
        public readonly int[] SiteOf;

        LevelExport(ArenaData data, int[] siteOf)
        {
            Data = data;
            SiteOf = siteOf;
        }

        public static LevelExport From(ArenaLayout layout, PlacementList list)
        {
            float tile = layout.TileM;
            int w = (int)Math.Round(layout.Size.X / tile), h = (int)Math.Round(layout.Size.Y / tile);
            Vector2 min = layout.Min;
            Vector2 Centre(int x, int y) => min + new Vector2((x + 0.5f) * tile, (y + 0.5f) * tile);

            var blocked = new bool[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    Vector2 c = Centre(x, y);
                    blocked[y * w + x] = layout.FightingDistance(c, out _) > layout.EdgeWidth(c);
                }

            // Structure pieces sharing a tile are one site (union-find over placements).
            var parent = new int[list.Items.Length];
            for (int i = 0; i < parent.Length; i++) parent[i] = i;
            int Root(int i)
            {
                while (parent[i] != i) i = parent[i] = parent[parent[i]];
                return i;
            }
            var pieceTiles = new List<int>[list.Items.Length];
            var claimedBy = new Dictionary<int, int>();
            for (int i = 0; i < list.Items.Length; i++)
            {
                Placement p = list.Items[i];
                Footprint f = list.Props[p.Prop];
                if (!f.Blocks) continue;
                List<int> tiles = TilesUnder(f, p, min, tile, w, h);
                bool site = p.Area >= 0 && layout.Areas[p.Area].Type == AreaType.Structure;
                if (!site)
                {
                    foreach (int t in tiles) blocked[t] = true;
                    continue;
                }
                pieceTiles[i] = tiles;
                foreach (int t in tiles)
                {
                    if (claimedBy.TryGetValue(t, out int other)) parent[Root(i)] = Root(other);
                    else claimedBy[t] = i;
                }
            }

            var siteOf = new int[list.Items.Length];
            var siteIndex = new Dictionary<int, int>();
            var siteTiles = new List<SortedSet<int>>();
            for (int i = 0; i < list.Items.Length; i++)
            {
                siteOf[i] = -1;
                if (pieceTiles[i] == null || pieceTiles[i].Count == 0) continue;
                int r = Root(i);
                if (!siteIndex.TryGetValue(r, out int s))
                {
                    siteIndex[r] = s = siteTiles.Count;
                    siteTiles.Add(new SortedSet<int>());
                }
                siteOf[i] = s;
                siteTiles[s].UnionWith(pieceTiles[i]);
            }
            foreach (var tiles in siteTiles)
                foreach (int t in tiles) blocked[t] = false;

            var blockedTiles = new List<Tile>();
            for (int t = 0; t < blocked.Length; t++)
                if (blocked[t]) blockedTiles.Add(new Tile(t % w, t / w));
            var sites = new StructureSite[siteTiles.Count];
            for (int s = 0; s < sites.Length; s++)
            {
                var tiles = new List<Tile>();
                foreach (int t in siteTiles[s]) tiles.Add(new Tile(t % w, t / w));
                sites[s] = new StructureSite { Tiles = tiles.ToArray(), Large = tiles.Count >= LargeTiles };
            }
            var data = new ArenaData
            {
                TileM = tile,
                OriginX = min.X,
                OriginY = min.Y,
                Width = w,
                Height = h,
                HeroStart = new Tile((int)Math.Floor((layout.HeroStart.X - min.X) / tile), (int)Math.Floor((layout.HeroStart.Y - min.Y) / tile)),
                Blocked = blockedTiles.ToArray(),
                Structures = sites,
            };
            data.Check(layout.Name);
            return new LevelExport(data, siteOf);
        }

        /// <summary>The tiles (y × width + x) whose middle the placed footprint reaches into.</summary>
        static List<int> TilesUnder(Footprint f, Placement p, Vector2 min, float tile, int w, int h)
        {
            var tiles = new List<int>();
            Vector2 lo, hi;
            Vector2 centre = Vector2.Zero;
            float radius = 0;
            Vector2[] poly = null;
            if (f.Shape == FootprintShape.Circle)
            {
                centre = p.ToWorld(f.Center);
                radius = f.Radius * p.Scale;
                lo = centre - new Vector2(radius);
                hi = centre + new Vector2(radius);
            }
            else
            {
                poly = new Vector2[f.Points.Length];
                lo = new Vector2(float.MaxValue);
                hi = new Vector2(float.MinValue);
                for (int k = 0; k < poly.Length; k++)
                {
                    poly[k] = p.ToWorld(f.Points[k]);
                    lo = Vector2.Min(lo, poly[k]);
                    hi = Vector2.Max(hi, poly[k]);
                }
            }
            float half = tile / 2 - InsetM;
            int x0 = Math.Max(0, (int)Math.Floor((lo.X - min.X) / tile)), x1 = Math.Min(w - 1, (int)Math.Floor((hi.X - min.X) / tile));
            int y0 = Math.Max(0, (int)Math.Floor((lo.Y - min.Y) / tile)), y1 = Math.Min(h - 1, (int)Math.Floor((hi.Y - min.Y) / tile));
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    var c = min + new Vector2((x + 0.5f) * tile, (y + 0.5f) * tile);
                    Vector2 bLo = c - new Vector2(half), bHi = c + new Vector2(half);
                    bool hit = poly != null ? PolygonMeetsBox(poly, bLo, bHi) : Vector2.Distance(Vector2.Clamp(centre, bLo, bHi), centre) < radius;
                    if (hit) tiles.Add(y * w + x);
                }
            return tiles;
        }

        /// <summary>Does the convex polygon overlap the box? Separating axes: the box's two and each edge's normal.</summary>
        static bool PolygonMeetsBox(Vector2[] poly, Vector2 lo, Vector2 hi)
        {
            Vector2[] box = { lo, new Vector2(hi.X, lo.Y), hi, new Vector2(lo.X, hi.Y) };
            bool Separated(Vector2 axis)
            {
                float pMin = float.MaxValue, pMax = float.MinValue, bMin = float.MaxValue, bMax = float.MinValue;
                foreach (Vector2 v in poly) { float d = Vector2.Dot(v, axis); pMin = Math.Min(pMin, d); pMax = Math.Max(pMax, d); }
                foreach (Vector2 v in box) { float d = Vector2.Dot(v, axis); bMin = Math.Min(bMin, d); bMax = Math.Max(bMax, d); }
                return pMax <= bMin || bMax <= pMin;
            }
            if (Separated(Vector2.UnitX) || Separated(Vector2.UnitY)) return false;
            for (int k = 0; k < poly.Length; k++)
            {
                Vector2 e = poly[(k + 1) % poly.Length] - poly[k];
                if (e.LengthSquared() > 1e-12f && Separated(new Vector2(-e.Y, e.X))) return false;
            }
            return true;
        }

        /// <summary>
        /// The arena's JSON as <see cref="ArenaData.Parse"/> reads it: the blocked tiles a row of the
        /// grid per line, a site per line, so a layout change shows as a readable diff.
        /// </summary>
        public string ToJson()
        {
            string N(double v) => v.ToString("R", CultureInfo.InvariantCulture);
            string T(Tile t) => "[" + t.X + ", " + t.Y + "]";
            var sb = new StringBuilder();
            sb.Append("{\n");
            sb.Append("  \"tileM\": ").Append(N(Data.TileM)).Append(",\n");
            sb.Append("  \"origin\": [").Append(N(Data.OriginX)).Append(", ").Append(N(Data.OriginY)).Append("],\n");
            sb.Append("  \"size\": [").Append(Data.Width).Append(", ").Append(Data.Height).Append("],\n");
            sb.Append("  \"heroStart\": ").Append(T(Data.HeroStart)).Append(",\n");
            sb.Append("  \"blocked\": [");
            for (int i = 0; i < Data.Blocked.Length; i++)
            {
                bool newRow = i == 0 || Data.Blocked[i].Y != Data.Blocked[i - 1].Y;
                sb.Append(i == 0 ? "\n    " : newRow ? ",\n    " : ", ").Append(T(Data.Blocked[i]));
            }
            sb.Append("\n  ],\n");
            sb.Append("  \"structures\": [");
            for (int s = 0; s < Data.Structures.Length; s++)
            {
                sb.Append(s == 0 ? "\n    " : ",\n    ").Append("{ \"tiles\": [");
                var tiles = Data.Structures[s].Tiles;
                for (int k = 0; k < tiles.Length; k++) sb.Append(k == 0 ? "" : ", ").Append(T(tiles[k]));
                sb.Append("], \"large\": ").Append(Data.Structures[s].Large ? "true" : "false").Append(" }");
            }
            sb.Append("\n  ]\n}\n");
            return sb.ToString();
        }
    }
}
