using System;
using System.Numerics;

namespace PS.Choreography
{
    /// <summary>
    /// A uniform grid of 1 m cells around a moving centre, rebuilt from scratch by counting sort
    /// (no allocation after the first build). Points outside the grid are clamped into its border
    /// cells, so queries stay correct, only slower, far from the centre.
    /// </summary>
    public sealed class SpatialHash
    {
        public const float CellSize = 1f;
        public const int Dim = 128;

        readonly int[] cellStart = new int[Dim * Dim + 1];
        int[] items = new int[1024];
        int[] cellOf = new int[1024];
        int originX, originY;   // world cell coordinates of grid cell (0, 0)
        int count;

        public int Count => count;

        public void Build(int[] ids, int n, Vector2[] positions, Vector2 centre)
        {
            originX = (int)Math.Floor(centre.X / CellSize) - Dim / 2;
            originY = (int)Math.Floor(centre.Y / CellSize) - Dim / 2;
            if (items.Length < n)
            {
                int cap = Math.Max(n, items.Length * 2);
                items = new int[cap];
                cellOf = new int[cap];
            }
            Array.Clear(cellStart, 0, cellStart.Length);
            for (int i = 0; i < n; i++)
            {
                int c = Cell(positions[ids[i]]);
                cellOf[i] = c;
                cellStart[c + 1]++;
            }
            for (int c = 0; c < Dim * Dim; c++) cellStart[c + 1] += cellStart[c];
            // Place in id order within each cell: walk the ids in order using a running cursor.
            for (int i = 0; i < n; i++)
            {
                int c = cellOf[i];
                items[cellStart[c]++] = ids[i];
            }
            // cellStart[c] now holds the end of cell c; shift back.
            for (int c = Dim * Dim; c > 0; c--) cellStart[c] = cellStart[c - 1];
            cellStart[0] = 0;
            count = n;
        }

        int Cell(Vector2 p)
        {
            int cx = (int)Math.Floor(p.X / CellSize) - originX;
            int cy = (int)Math.Floor(p.Y / CellSize) - originY;
            if (cx < 0) cx = 0; else if (cx >= Dim) cx = Dim - 1;
            if (cy < 0) cy = 0; else if (cy >= Dim) cy = Dim - 1;
            return cy * Dim + cx;
        }

        /// <summary>
        /// Writes the ids in every cell overlapping the square of half-size <paramref name="r"/>
        /// around <paramref name="c"/> into <paramref name="buffer"/> (growing it), in cell order
        /// then id order. Returns how many. Callers filter by exact distance.
        /// </summary>
        public int Gather(Vector2 c, float r, ref int[] buffer)
        {
            int x0 = (int)Math.Floor((c.X - r) / CellSize) - originX, x1 = (int)Math.Floor((c.X + r) / CellSize) - originX;
            int y0 = (int)Math.Floor((c.Y - r) / CellSize) - originY, y1 = (int)Math.Floor((c.Y + r) / CellSize) - originY;
            if (x1 < 0 || y1 < 0 || x0 >= Dim || y0 >= Dim)
            {
                // Entirely outside: everything clamped to the border could still be near.
                x0 = Math.Max(0, Math.Min(Dim - 1, x0)); x1 = Math.Max(0, Math.Min(Dim - 1, x1));
                y0 = Math.Max(0, Math.Min(Dim - 1, y0)); y1 = Math.Max(0, Math.Min(Dim - 1, y1));
            }
            if (x0 < 0) x0 = 0;
            if (y0 < 0) y0 = 0;
            if (x1 >= Dim) x1 = Dim - 1;
            if (y1 >= Dim) y1 = Dim - 1;
            int n = 0;
            for (int y = y0; y <= y1; y++)
            {
                int row = y * Dim;
                int s = cellStart[row + x0], e = cellStart[row + x1 + 1];
                if (n + (e - s) > buffer.Length) Array.Resize(ref buffer, Math.Max(buffer.Length * 2, n + (e - s)));
                Array.Copy(items, s, buffer, n, e - s);
                n += e - s;
            }
            return n;
        }
    }
}
