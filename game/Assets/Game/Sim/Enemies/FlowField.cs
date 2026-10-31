using System;

namespace PS.Game.Sim
{
    /// <summary>
    /// A flow field toward the hero over a <see cref="NavGrid"/> (unity-game.md, Enemies,
    /// "Pathing"), copied from PS.Choreography's Navigation.cs and owned here: an 8-neighbour
    /// shortest-path search from the source cell over the open cells, with integer step costs (10
    /// straight, 14 diagonal) and no corner cutting. A bucket queue (Dial's algorithm) settles cells
    /// in cost order, so the search runs in time linear in the cells and can be time-sliced:
    /// <see cref="Step"/> settles a budget of cells per call and the result is the same however it
    /// is sliced. Cells costlier than <see cref="MaxCost"/> stay unreached.
    /// </summary>
    internal sealed class FlowField
    {
        public const int Straight = 10, Diagonal = 14;
        public const int Unreached = int.MaxValue;

        public readonly NavGrid Grid;
        public readonly int[] Cost;
        public readonly int MaxCost;
        public int SourceCell { get; private set; } = -1;
        public bool Building { get; private set; }
        public bool Complete { get; private set; }

        readonly int[] straightOff, diagOff, diagA, diagB;
        readonly int[][] queue = new int[16][];
        readonly int[] queueCount = new int[16];
        int cur, pending;

        /// <param name="maxCostM">How far from the source, in metres of path, the field reaches.</param>
        public FlowField(NavGrid grid, double maxCostM)
        {
            Grid = grid;
            MaxCost = (int)Math.Ceiling(maxCostM / grid.CellM * Straight);
            Cost = new int[grid.W * grid.H];
            int w = grid.W;
            straightOff = new[] { 1, -1, w, -w };
            diagOff = new[] { w + 1, w - 1, -w + 1, -w - 1 };
            diagA = new[] { 1, -1, 1, -1 };     // each diagonal's two straight neighbours must be open
            diagB = new[] { w, w, -w, -w };
            for (int i = 0; i < 16; i++) queue[i] = new int[1024];
            for (int i = 0; i < Cost.Length; i++) Cost[i] = Unreached;
        }

        /// <summary>Starts a search from the cell holding (x, y), or from the open cells around it when it is blocked.</summary>
        public void Begin(double x, double y)
        {
            for (int i = 0; i < Cost.Length; i++) Cost[i] = Unreached;
            for (int i = 0; i < 16; i++) queueCount[i] = 0;
            cur = 0;
            pending = 0;
            Building = true;
            Complete = false;
            int c = Grid.CellIndex(x, y);
            SourceCell = c;
            if (c < 0) return;
            if (!Grid.IsBlocked(c))
            {
                Seed(c);
                return;
            }
            for (int k = 0; k < 4; k++)
            {
                int n = c + straightOff[k];
                if (n >= 0 && n < Cost.Length && !Grid.IsBlocked(n)) Seed(n);
            }
        }

        void Seed(int cell)
        {
            if (Cost[cell] == 0) return;
            Cost[cell] = 0;
            Push(cell);
        }

        void Push(int cell)
        {
            int b = Cost[cell] & 15;
            if (queueCount[b] == queue[b].Length) Array.Resize(ref queue[b], queue[b].Length * 2);
            queue[b][queueCount[b]++] = cell;
            pending++;
        }

        /// <summary>Settles up to <paramref name="budget"/> cells. Returns true once the search is complete.</summary>
        public bool Step(int budget)
        {
            if (!Building) return true;
            int done = 0;
            while (pending > 0)
            {
                int b = cur & 15;
                if (queueCount[b] == 0)
                {
                    cur++;
                    continue;
                }
                int node = queue[b][--queueCount[b]];
                pending--;
                if (Cost[node] != cur) continue;   // a stale entry: the cell was reached more cheaply
                for (int k = 0; k < 4; k++)
                {
                    int n = node + straightOff[k];
                    if (Grid.IsBlocked(n)) continue;
                    int nc = cur + Straight;
                    if (nc < Cost[n] && nc <= MaxCost)
                    {
                        Cost[n] = nc;
                        Push(n);
                    }
                }
                for (int k = 0; k < 4; k++)
                {
                    int n = node + diagOff[k];
                    if (Grid.IsBlocked(n) || Grid.IsBlocked(node + diagA[k]) || Grid.IsBlocked(node + diagB[k])) continue;
                    int nc = cur + Diagonal;
                    if (nc < Cost[n] && nc <= MaxCost)
                    {
                        Cost[n] = nc;
                        Push(n);
                    }
                }
                if (++done >= budget && pending > 0) return false;
            }
            Building = false;
            Complete = true;
            return true;
        }

        public void BuildAll() => Step(int.MaxValue);

        /// <summary>
        /// The way downhill at (x, y): the cost's gradient, interpolated over the four cell centres
        /// around it for smooth headings; where one of them is unreached, the way to the cheapest of
        /// its own cell's eight neighbours. False where nothing is reached or at the source.
        /// </summary>
        public bool Direction(double x, double y, out double dirX, out double dirY)
        {
            dirX = 0;
            dirY = 0;
            double gx = (x - Grid.MinX) / Grid.CellM - 0.5, gy = (y - Grid.MinY) / Grid.CellM - 0.5;
            int x0 = (int)Math.Floor(gx), y0 = (int)Math.Floor(gy);
            double fx = gx - x0, fy = gy - y0;
            int w = Grid.W;
            if (x0 >= 0 && y0 >= 0 && x0 + 1 < w && y0 + 1 < Grid.H)
            {
                int i00 = y0 * w + x0;
                int c00 = Cost[i00], c10 = Cost[i00 + 1], c01 = Cost[i00 + w], c11 = Cost[i00 + w + 1];
                if (c00 != Unreached && c10 != Unreached && c01 != Unreached && c11 != Unreached)
                {
                    double dx = (c10 - c00) * (1 - fy) + (c11 - c01) * fy;
                    double dy = (c01 - c00) * (1 - fx) + (c11 - c10) * fx;
                    double l = Math.Sqrt(dx * dx + dy * dy);
                    if (l > 1e-3)
                    {
                        dirX = -dx / l;
                        dirY = -dy / l;
                        return true;
                    }
                }
            }
            int c = Grid.CellIndex(x, y);
            if (c < 0) return false;
            int best = Cost[c], bestN = -1;
            int cx = c % w, cy = c / w;
            for (int oy = -1; oy <= 1; oy++)
                for (int ox = -1; ox <= 1; ox++)
                {
                    if (ox == 0 && oy == 0) continue;
                    int nx = cx + ox, ny = cy + oy;
                    if (nx < 0 || ny < 0 || nx >= w || ny >= Grid.H) continue;
                    int n = ny * w + nx;
                    if (Cost[n] < best)
                    {
                        best = Cost[n];
                        bestN = n;
                    }
                }
            if (bestN < 0) return false;
            double ddx = Grid.CenterX(bestN) - x, ddy = Grid.CenterY(bestN) - y;
            double dl = Math.Sqrt(ddx * ddx + ddy * ddy);
            if (dl < 1e-4) return false;
            dirX = ddx / dl;
            dirY = ddy / dl;
            return true;
        }
    }
}
