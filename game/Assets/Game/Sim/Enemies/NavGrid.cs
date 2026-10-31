using System;
using PS.Game.Sim.Level;

namespace PS.Game.Sim
{
    /// <summary>
    /// The enemies' obstacle map (unity-game.md, Enemies, "Pathing"): the arena at twice the hero
    /// grid's resolution (0.75 m cells for 1.5 m tiles), each cell blocked when its tile is, inside
    /// a ring of blocked cells around the arena so neighbour lookups never leave the grid.
    /// </summary>
    internal sealed class NavGrid
    {
        public const int CellsPerTile = 2;

        public readonly int W, H;
        public readonly double MinX, MinY, CellM;
        readonly bool[] blocked;

        public NavGrid(ArenaGrid arena)
        {
            CellM = arena.TileM / CellsPerTile;
            W = arena.Width * CellsPerTile + 2;
            H = arena.Height * CellsPerTile + 2;
            MinX = arena.OriginX - CellM;
            MinY = arena.OriginY - CellM;
            blocked = new bool[W * H];
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                    blocked[y * W + x] = !arena.IsOpen(Tile(x), Tile(y));
        }

        /// <summary>The hero tile holding cell column or row <paramref name="c"/> (the ring's are outside the arena).</summary>
        static int Tile(int c) => c == 0 ? -1 : (c - 1) / CellsPerTile;

        static int Floor(double v) => (int)Math.Floor(v);

        /// <summary>The cell holding (x, y), or -1 outside the grid.</summary>
        public int CellIndex(double x, double y)
        {
            int cx = Floor((x - MinX) / CellM), cy = Floor((y - MinY) / CellM);
            return cx < 0 || cy < 0 || cx >= W || cy >= H ? -1 : cy * W + cx;
        }

        public bool IsBlocked(int cell) => blocked[cell];

        public bool Walkable(double x, double y)
        {
            int c = CellIndex(x, y);
            return c >= 0 && !blocked[c];
        }

        public double CenterX(int cell) => MinX + (cell % W + 0.5) * CellM;
        public double CenterY(int cell) => MinY + (cell / W + 0.5) * CellM;

        /// <summary>
        /// How far along the move from a to b (0 to 1) a body gets before its centre would enter a
        /// blocked cell, marching a quarter cell at a time. 1 when the whole move is walkable.
        /// </summary>
        public double Reach(double ax, double ay, double bx, double by)
        {
            double dx = bx - ax, dy = by - ay;
            double len = Math.Sqrt(dx * dx + dy * dy);
            int n = Math.Max(1, (int)Math.Ceiling(len / (0.25 * CellM)));
            for (int k = 1; k <= n; k++)
            {
                double u = (double)k / n;
                if (!Walkable(ax + dx * u, ay + dy * u)) return (double)(k - 1) / n;
            }
            return 1;
        }

        /// <summary>
        /// Shortens the move from a to b to where it stays walkable, sliding along a wall when the
        /// straight move is stopped: of the move cut short, its x part alone and its y part alone,
        /// the one that gets farthest.
        /// </summary>
        public void Clamp(double ax, double ay, ref double bx, ref double by)
        {
            double u = Reach(ax, ay, bx, by);
            if (u >= 1) return;
            double sx = ax + (bx - ax) * u, sy = ay + (by - ay) * u;
            double best = (sx - ax) * (sx - ax) + (sy - ay) * (sy - ay);
            double ux = Reach(ax, ay, bx, ay), uy = Reach(ax, ay, ax, by);
            double dx = (bx - ax) * ux, dy = (by - ay) * uy;
            if (dx * dx > best && dx * dx >= dy * dy)
            {
                sx = ax + dx;
                sy = ay;
            }
            else if (dy * dy > best)
            {
                sx = ax;
                sy = ay + dy;
            }
            bx = sx;
            by = sy;
        }
    }
}
