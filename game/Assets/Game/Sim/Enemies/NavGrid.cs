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
        /// blocked cell: 1 when the whole move is walkable. It walks every cell the move crosses,
        /// and a move through a cell corner touches all three cells beyond it, so no move clips a
        /// structure's corner or slips between two blocked cells that meet at a corner (the flow
        /// field's no-corner-cutting rule). A stopped move ends just short of the blocked cell. The
        /// start cell itself isn't tested, so a body inside a structure can walk out.
        /// </summary>
        public double Reach(double ax, double ay, double bx, double by)
        {
            double gx = (ax - MinX) / CellM, gy = (ay - MinY) / CellM;
            double dx = (bx - ax) / CellM, dy = (by - ay) / CellM;
            int cx = Floor(gx), cy = Floor(gy);
            int sx = Math.Sign(dx), sy = Math.Sign(dy);
            // The move's fraction per cell crossed, and where it next crosses a column or row line.
            double stepX = sx != 0 ? 1 / Math.Abs(dx) : double.PositiveInfinity;
            double stepY = sy != 0 ? 1 / Math.Abs(dy) : double.PositiveInfinity;
            double lineX = sx > 0 ? cx + 1 : cx, lineY = sy > 0 ? cy + 1 : cy;
            double nextX = sx != 0 ? (lineX - gx) / dx : double.PositiveInfinity;
            double nextY = sy != 0 ? (lineY - gy) / dy : double.PositiveInfinity;
            while (true)
            {
                if (Math.Min(nextX, nextY) >= 1) return 1;
                bool crossX = nextX <= nextY + 1e-12, crossY = nextY <= nextX + 1e-12;
                bool stop = crossX && crossY
                    ? Blocked(cx + sx, cy) || Blocked(cx, cy + sy) || Blocked(cx + sx, cy + sy)
                    : crossX ? Blocked(cx + sx, cy) : Blocked(cx, cy + sy);
                if (stop)
                {
                    // Short of every line crossed here, so the end stays in this cell.
                    double u = 1;
                    if (crossX) u = Math.Min(u, (lineX - sx * Margin - gx) / dx);
                    if (crossY) u = Math.Min(u, (lineY - sy * Margin - gy) / dy);
                    return Math.Max(0, u);
                }
                if (crossX)
                {
                    cx += sx;
                    lineX += sx;
                    nextX += stepX;
                }
                if (crossY)
                {
                    cy += sy;
                    lineY += sy;
                    nextY += stepY;
                }
            }
        }

        /// <summary>How far short of a blocked cell's edge a stopped move ends, in cells.</summary>
        const double Margin = 1e-6;

        bool Blocked(int cx, int cy) => cx < 0 || cy < 0 || cx >= W || cy >= H || blocked[cy * W + cx];

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
