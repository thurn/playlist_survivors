using System;
using System.Collections.Generic;
using System.Numerics;

namespace PS.Choreography
{
    /// <summary>
    /// The obstacle map and wall distance field for one placement list (choreography.md,
    /// "Navigation around structures"). The blocking footprints are kept as exact world shapes
    /// (circles and convex polygons), bucketed on a 2 m grid, and sampled onto a 0.5 m grid over the
    /// arena: each cell's clearance is the exact distance from its centre to the nearest footprint or
    /// the arena's edge (negative inside one), capped at <see cref="MaxClearance"/>. A cell is
    /// blocked when its clearance is under half a cell. Built once per layout, at load. A run with
    /// structure breaks navigates its own <see cref="Clone"/>, from which <see cref="Remove"/> takes
    /// the broken pieces' footprints (choreography.md, "Structure breaks").
    /// </summary>
    public sealed class NavGrid
    {
        public const float CellM = 0.5f;
        /// <summary>The field is exact below this distance and reads "at least this" above it.</summary>
        public const float MaxClearance = 2.5f;
        const float BucketM = 2f;

        public readonly int W, H;
        public readonly Vector2 Min, Max;
        public readonly float[] Clearance;
        /// <summary>
        /// Each cell centre's distance to the nearest blocked cell's centre, uncapped (an exact Euclidean distance
        /// transform of the obstacle map). After <see cref="Remove"/> it holds the old distances, which only grow
        /// when footprints leave, so they stay lower bounds, until <see cref="StepOpen"/> finishes its rebuild.
        /// </summary>
        public float[] Open { get; private set; }
        public readonly int ShapeCount;
        public int BlockedCells { get; private set; }

        readonly int[] shapeOf;   // per placement, its footprint's shape, or -1
        readonly bool[] removed;  // per shape: taken out by a break
        readonly bool[] isCircle;
        readonly Vector2[] centre;
        readonly float[] radius;
        readonly int[] polyStart;
        readonly Vector2[] polyPts;
        readonly int bw, bh;
        readonly int[] bucketStart, bucketItems;
        readonly Dictionary<float, bool[]> passable = new Dictionary<float, bool[]>();

        public NavGrid(PlacementList list)
        {
            Min = -list.Size / 2;
            Max = list.Size / 2;
            W = (int)Math.Ceiling(list.Size.X / CellM);
            H = (int)Math.Ceiling(list.Size.Y / CellM);

            // World shapes of every blocking placement.
            var circ = new List<bool>();
            var cen = new List<Vector2>();
            var rad = new List<float>();
            var starts = new List<int> { 0 };
            var pts = new List<Vector2>();
            shapeOf = new int[list.Items.Length];
            for (int k = 0; k < list.Items.Length; k++)
            {
                Placement p = list.Items[k];
                Footprint f = list.Props[p.Prop];
                shapeOf[k] = -1;
                if (!f.Blocks) continue;
                if (f.Shape == FootprintShape.Circle)
                {
                    circ.Add(true); cen.Add(p.ToWorld(f.Center)); rad.Add(f.Radius * p.Scale);
                }
                else if (f.Shape == FootprintShape.Polygon)
                {
                    var w = new Vector2[f.Points.Length];
                    Vector2 c = Vector2.Zero;
                    for (int i = 0; i < w.Length; i++) { w[i] = p.ToWorld(f.Points[i]); c += w[i]; }
                    c /= w.Length;
                    float area = 0;
                    for (int i = 0; i < w.Length; i++) area += Cross(w[i], w[(i + 1) % w.Length]);
                    if (area < 0) Array.Reverse(w);   // counter-clockwise, so outward normals are (ey, -ex)
                    float r = 0;
                    foreach (Vector2 q in w) r = Math.Max(r, Vector2.Distance(q, c));
                    circ.Add(false); cen.Add(c); rad.Add(r);
                    pts.AddRange(w);
                }
                else continue;
                shapeOf[k] = circ.Count - 1;
                starts.Add(pts.Count);
            }
            isCircle = circ.ToArray();
            centre = cen.ToArray();
            radius = rad.ToArray();
            polyStart = starts.ToArray();
            polyPts = pts.ToArray();
            ShapeCount = isCircle.Length;
            removed = new bool[ShapeCount];

            // Buckets: each lists the shapes within MaxClearance of any point in it.
            bw = (int)Math.Ceiling(list.Size.X / BucketM);
            bh = (int)Math.Ceiling(list.Size.Y / BucketM);
            bucketStart = new int[bw * bh + 1];
            var cells = new List<int>[bw * bh];
            for (int s = 0; s < ShapeCount; s++)
            {
                float reach = radius[s] + MaxClearance;
                int x0 = Math.Max(0, (int)Math.Floor((centre[s].X - reach - Min.X) / BucketM)), x1 = Math.Min(bw - 1, (int)Math.Floor((centre[s].X + reach - Min.X) / BucketM));
                int y0 = Math.Max(0, (int)Math.Floor((centre[s].Y - reach - Min.Y) / BucketM)), y1 = Math.Min(bh - 1, (int)Math.Floor((centre[s].Y + reach - Min.Y) / BucketM));
                for (int y = y0; y <= y1; y++)
                    for (int x = x0; x <= x1; x++)
                    {
                        int b = y * bw + x;
                        (cells[b] ?? (cells[b] = new List<int>())).Add(s);
                    }
            }
            var items = new List<int>();
            for (int b = 0; b < cells.Length; b++)
            {
                bucketStart[b] = items.Count;
                if (cells[b] != null) items.AddRange(cells[b]);
            }
            bucketStart[cells.Length] = items.Count;
            bucketItems = items.ToArray();

            Clearance = new float[W * H];
            int blocked = 0;
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    float d = Distance(CellCenter(x, y));
                    Clearance[y * W + x] = d;
                    if (d < CellM / 2) blocked++;
                }
            BlockedCells = blocked;
            Open = DistanceTransform();
        }

        /// <summary>
        /// The uncapped wall distance field: a Euclidean distance transform of the blocked cells
        /// (Felzenszwalb and Huttenlocher's two passes of 1D lower envelopes), in metres.
        /// </summary>
        float[] DistanceTransform()
        {
            BeginOpenRebuild();
            for (int line = 0; line < W + H; line++) TransformLine(line);
            float[] result = dtOut;
            dtStep = -1;
            dtOut = null;
            dtG = dtF = dtD = dtZ = null;   // a run that breaks structures allocates its own
            dtV = null;
            return result;
        }

        const double DtInf = 1e9;   // far above any squared distance in cells, and exact in a double
        double[] dtG, dtF, dtD, dtZ;
        int[] dtV;
        float[] dtOut;
        int dtStep = -1;

        /// <summary>Is the uncapped field being rebuilt (<see cref="StepOpen"/>)?</summary>
        public bool RebuildingOpen => dtStep >= 0;

        /// <summary>Starts rebuilding <see cref="Open"/> from the obstacle map as it is now, to be advanced by <see cref="StepOpen"/>.</summary>
        public void BeginOpenRebuild()
        {
            int n = Math.Max(W, H);
            if (dtG == null) { dtG = new double[W * H]; dtF = new double[n]; dtD = new double[n]; dtV = new int[n]; dtZ = new double[n + 1]; }
            for (int i = 0; i < dtG.Length; i++) dtG[i] = IsBlocked(i) ? 0 : DtInf;
            dtOut = new float[W * H];
            dtStep = 0;
        }

        /// <summary>Advances the rebuild by <paramref name="lines"/> of its W columns and then H rows; once done, <see cref="Open"/> is the new field. True when done.</summary>
        public bool StepOpen(int lines)
        {
            if (dtStep < 0) return true;
            for (int k = 0; k < lines && dtStep < W + H; k++) TransformLine(dtStep++);
            if (dtStep < W + H) return false;
            Open = dtOut;
            dtOut = null;
            dtStep = -1;
            return true;
        }

        /// <summary>One line of the two passes: columns 0..W-1, then rows.</summary>
        void TransformLine(int line)
        {
            double[] g = dtG, f = dtF, d = dtD, z = dtZ;
            if (line < W)
            {
                int x = line;
                for (int y = 0; y < H; y++) f[y] = g[y * W + x];
                Envelope(f, H, d, dtV, z);
                for (int y = 0; y < H; y++) g[y * W + x] = d[y];
                return;
            }
            int row = line - W;
            for (int x = 0; x < W; x++) f[x] = g[row * W + x];
            Envelope(f, W, d, dtV, z);
            for (int x = 0; x < W; x++) dtOut[row * W + x] = (float)(Math.Sqrt(Math.Min(d[x], DtInf)) * CellM);
        }

        /// <summary>A copy whose obstacle map a run may change (<see cref="Remove"/>) without touching this one's.</summary>
        public NavGrid Clone() => new NavGrid(this);

        NavGrid(NavGrid o)
        {
            W = o.W; H = o.H; Min = o.Min; Max = o.Max;
            Clearance = (float[])o.Clearance.Clone();
            Open = o.Open;   // replaced whole by a rebuild, never written in place
            ShapeCount = o.ShapeCount; BlockedCells = o.BlockedCells;
            shapeOf = o.shapeOf; removed = (bool[])o.removed.Clone();
            isCircle = o.isCircle; centre = o.centre; radius = o.radius; polyStart = o.polyStart; polyPts = o.polyPts;
            bw = o.bw; bh = o.bh; bucketStart = o.bucketStart; bucketItems = o.bucketItems;
        }

        /// <summary>Does placement <paramref name="placement"/> block (have a footprint still in the map)?</summary>
        public bool Blocks(int placement) => shapeOf[placement] >= 0 && !removed[shapeOf[placement]];

        /// <summary>
        /// Takes the placements' footprints out of the obstacle map (a structure break): the exact shapes at once,
        /// and the capped field and every cached passable set (so each flow field's) in the cells those shapes'
        /// reach could change. <see cref="Open"/> keeps its old lower bounds until a rebuild (<see cref="BeginOpenRebuild"/>).
        /// Returns, per placement, the largest clearance now found in the cells its footprint covered (the opening's
        /// half width there, capped; +∞ for one without a footprint or already removed).
        /// </summary>
        public float[] Remove(IReadOnlyList<int> placements)
        {
            var covered = new List<int>[placements.Count];
            for (int pi = 0; pi < placements.Count; pi++)
            {
                covered[pi] = new List<int>();
                int s = shapeOf[placements[pi]];
                if (s < 0 || removed[s]) continue;
                removed[s] = true;
                float reach = radius[s] + MaxClearance + CellM;
                int x0 = Math.Max(0, (int)Math.Floor((centre[s].X - reach - Min.X) / CellM)), x1 = Math.Min(W - 1, (int)Math.Floor((centre[s].X + reach - Min.X) / CellM));
                int y0 = Math.Max(0, (int)Math.Floor((centre[s].Y - reach - Min.Y) / CellM)), y1 = Math.Min(H - 1, (int)Math.Floor((centre[s].Y + reach - Min.Y) / CellM));
                for (int y = y0; y <= y1; y++)
                    for (int x = x0; x <= x1; x++)
                    {
                        int i = y * W + x;
                        if (ShapeDistance(s, CellCenter(x, y), out _, true) < CellM / 2) covered[pi].Add(i);
                        float before = Clearance[i];
                        float d = Distance(CellCenter(x, y));
                        if (d == before) continue;
                        Clearance[i] = d;
                        if (before < CellM / 2 && d >= CellM / 2) BlockedCells--;
                        if (x == 0 || y == 0 || x == W - 1 || y == H - 1) continue;
                        foreach (KeyValuePair<float, bool[]> kv in passable) kv.Value[i] = d > kv.Key;
                    }
            }
            var widest = new float[placements.Count];
            for (int pi = 0; pi < placements.Count; pi++)
            {
                widest[pi] = covered[pi].Count == 0 ? float.PositiveInfinity : 0;
                foreach (int i in covered[pi]) widest[pi] = Math.Max(widest[pi], Clearance[i]);
            }
            return widest;
        }

        /// <summary>The point of placement <paramref name="placement"/>'s footprint nearest <paramref name="p"/> (<paramref name="p"/> itself if it has none).</summary>
        public Vector2 NearestPoint(int placement, Vector2 p)
        {
            int s = shapeOf[placement];
            if (s < 0) return p;
            float d = ShapeDistance(s, p, out Vector2 outward, true);
            return p - outward * d;
        }

        /// <summary>The 1D squared distance transform of <paramref name="f"/>: the lower envelope of parabolas.</summary>
        static void Envelope(double[] f, int n, double[] d, int[] v, double[] z)
        {
            int k = 0;
            v[0] = 0;
            z[0] = double.NegativeInfinity;
            z[1] = double.PositiveInfinity;
            for (int q = 1; q < n; q++)
            {
                double s = ((f[q] + (double)q * q) - (f[v[k]] + (double)v[k] * v[k])) / (2.0 * q - 2.0 * v[k]);
                while (s <= z[k])
                {
                    k--;
                    s = ((f[q] + (double)q * q) - (f[v[k]] + (double)v[k] * v[k])) / (2.0 * q - 2.0 * v[k]);
                }
                k++;
                v[k] = q;
                z[k] = s;
                z[k + 1] = double.PositiveInfinity;
            }
            k = 0;
            for (int q = 0; q < n; q++)
            {
                while (z[k + 1] < q) k++;
                double dq = q - v[k];
                d[q] = dq * dq + f[v[k]];
            }
        }

        static float Cross(Vector2 a, Vector2 b) => a.X * b.Y - a.Y * b.X;

        public Vector2 CellCenter(int x, int y) => new Vector2(Min.X + (x + 0.5f) * CellM, Min.Y + (y + 0.5f) * CellM);
        public Vector2 CellCenter(int i) => CellCenter(i % W, i / W);

        /// <summary>The cell holding <paramref name="p"/>, or -1 outside the arena.</summary>
        public int CellIndex(Vector2 p)
        {
            int x = (int)Math.Floor((p.X - Min.X) / CellM), y = (int)Math.Floor((p.Y - Min.Y) / CellM);
            if (x < 0 || y < 0 || x >= W || y >= H) return -1;
            return y * W + x;
        }

        /// <summary>The field's clearance at <paramref name="p"/>'s cell (one lookup); within 0.36 m of the exact value below the cap. Outside the arena, -1.</summary>
        public float ClearanceAt(Vector2 p)
        {
            int i = CellIndex(p);
            return i < 0 ? -1f : Clearance[i];
        }

        /// <summary>The uncapped open-distance field at <paramref name="p"/>'s cell (distance to the nearest blocked cell); 0 outside the arena.</summary>
        public float OpenAt(Vector2 p)
        {
            int i = CellIndex(p);
            return i < 0 ? 0f : Open[i];
        }

        /// <summary>A lower bound on the exact clearance at <paramref name="p"/> from one cell lookup (-1 outside the arena).</summary>
        public float ClearanceLowerBound(Vector2 p)
        {
            int i = CellIndex(p);
            return i < 0 ? -1f : Math.Max(Clearance[i] - 0.36f, Open[i] - 0.61f);
        }

        public bool IsBlocked(int cell) => Clearance[cell] < CellM / 2;

        /// <summary>
        /// The exact distance from <paramref name="p"/> to the nearest blocking footprint or the
        /// arena's edge, capped at <see cref="MaxClearance"/>; negative inside a footprint or outside the arena.
        /// </summary>
        public float Distance(Vector2 p)
        {
            float d = Math.Min(Math.Min(p.X - Min.X, Max.X - p.X), Math.Min(p.Y - Min.Y, Max.Y - p.Y));
            int b = Bucket(p);
            if (b >= 0)
                for (int k = bucketStart[b]; k < bucketStart[b + 1]; k++)
                {
                    if (removed[bucketItems[k]]) continue;
                    float ds = ShapeDistance(bucketItems[k], p, out _);
                    if (ds < d) d = ds;
                }
            return Math.Min(d, MaxClearance);
        }

        int Bucket(Vector2 p)
        {
            int x = (int)Math.Floor((p.X - Min.X) / BucketM), y = (int)Math.Floor((p.Y - Min.Y) / BucketM);
            if (x < 0 || y < 0 || x >= bw || y >= bh) return -1;
            return y * bw + x;
        }

        /// <summary>
        /// Signed distance to shape <paramref name="s"/>, with the outward direction from it at <paramref name="p"/>; beyond
        /// <see cref="MaxClearance"/> of a polygon's bounding circle, the circle's, unless <paramref name="exact"/>.
        /// </summary>
        float ShapeDistance(int s, Vector2 p, out Vector2 outward, bool exact = false)
        {
            if (isCircle[s])
            {
                Vector2 d = p - centre[s];
                float l = d.Length();
                outward = l > 1e-6f ? d / l : new Vector2(0, 1);
                return l - radius[s];
            }
            // Quick reject by the bounding circle.
            Vector2 dc = p - centre[s];
            float lc = dc.Length();
            if (!exact && lc - radius[s] > MaxClearance) { outward = lc > 1e-6f ? dc / lc : new Vector2(0, 1); return lc - radius[s]; }
            int a0 = polyStart[s], n = polyStart[s + 1] - a0;
            bool inside = true;
            float best = float.MaxValue, bestSide = float.MinValue;
            Vector2 closest = p, sideNormal = new Vector2(0, 1);
            for (int i = 0; i < n; i++)
            {
                Vector2 a = polyPts[a0 + i], b = polyPts[a0 + (i + 1) % n];
                Vector2 e = b - a;
                float el = e.Length();
                if (el < 1e-6f) continue;
                var nrm = new Vector2(e.Y / el, -e.X / el);
                float side = Vector2.Dot(p - a, nrm);
                if (side > 0) inside = false;
                if (side > bestSide) { bestSide = side; sideNormal = nrm; }
                float u = Math.Max(0, Math.Min(1, Vector2.Dot(p - a, e) / (el * el)));
                Vector2 q = a + e * u;
                float dq = Vector2.Distance(p, q);
                if (dq < best) { best = dq; closest = q; }
            }
            if (inside) { outward = sideNormal; return -best; }
            Vector2 o = p - closest;
            outward = best > 1e-6f ? o / best : sideNormal;
            return best;
        }

        /// <summary>
        /// Moves <paramref name="p"/> out of every footprint and the arena's edge until it stands at
        /// least <paramref name="r"/> from all of them (choreography.md: "pushed out along the
        /// field's gradient"). Returns false if a few pushes could not clear it.
        /// </summary>
        public bool PushOut(ref Vector2 p, float r)
        {
            for (int it = 0; it < 6; it++)
            {
                float worst = 0;
                Vector2 dir = Vector2.Zero;
                int b = Bucket(p);
                if (b >= 0)
                    for (int k = bucketStart[b]; k < bucketStart[b + 1]; k++)
                    {
                        if (removed[bucketItems[k]]) continue;
                        float ds = ShapeDistance(bucketItems[k], p, out Vector2 o);
                        float ex = ds - r;
                        if (ex < worst) { worst = ex; dir = o; }
                    }
                p = new Vector2(Math.Max(Min.X + r, Math.Min(Max.X - r, p.X)), Math.Max(Min.Y + r, Math.Min(Max.Y - r, p.Y)));
                if (worst >= 0) return true;
                p += dir * (-worst + 1e-3f);
            }
            return Distance(p) >= r - 1e-3f;
        }

        /// <summary>Does the straight line from <paramref name="a"/> to <paramref name="b"/> keep at least <paramref name="minClear"/> from every footprint, by the field?</summary>
        public bool LineClear(Vector2 a, Vector2 b, float minClear = 0.05f)
        {
            float len = Vector2.Distance(a, b);
            int n = Math.Max(1, (int)Math.Ceiling(len / (CellM * 0.5f)));
            for (int i = 0; i <= n; i++)
            {
                int c = CellIndex(Vector2.Lerp(a, b, (float)i / n));
                if (c < 0 || Clearance[c] < minClear) return false;
            }
            return true;
        }

        /// <summary>
        /// Can a body of radius <paramref name="r"/> move straight from <paramref name="a"/> to
        /// <paramref name="b"/> without touching a footprint? Conservative: it marches along the
        /// line by the field's clearance (less the 0.36 m a point can lie from its cell's centre), so
        /// it costs a handful of lookups in the open and refuses where the field cannot tell.
        /// </summary>
        public bool Clear(Vector2 a, Vector2 b, float r)
        {
            Vector2 d = b - a;
            float len = d.Length();
            if (len > 1e-4f) d /= len;
            float s = 0;
            for (int guard = 0; guard < 4096; guard++)
            {
                int cell = CellIndex(a + d * s);
                if (cell < 0) return false;
                // Two lower bounds on the exact clearance there: the capped field (less the 0.36 m a point lies
                // from its cell's centre), and the uncapped distance to a blocked cell (less that and the 0.25 m
                // a blocked cell's centre may lie inside the footprint's edge).
                float c = Math.Max(Clearance[cell] - 0.36f, Open[cell] - 0.61f);
                if (c < r) return false;
                if (s >= len) return true;
                s = Math.Min(len, s + Math.Max(0.1f, c - r));
            }
            return false;
        }

        /// <summary>The cells a flow field may cross: clearance above <paramref name="minClear"/>, never the grid's outer ring.</summary>
        public bool[] PassableFor(float minClear)
        {
            if (passable.TryGetValue(minClear, out bool[] p)) return p;
            p = new bool[W * H];
            for (int y = 1; y < H - 1; y++)
                for (int x = 1; x < W - 1; x++)
                    p[y * W + x] = Clearance[y * W + x] > minClear;
            passable[minClear] = p;
            return p;
        }
    }

    /// <summary>
    /// A flow field over a <see cref="NavGrid"/>: an 8-neighbour shortest-path search from one or
    /// more source cells over the cells whose clearance exceeds <see cref="MinClearance"/>, with
    /// integer step costs (10 straight, 14 diagonal, so 0.05 m a unit) and no corner cutting. A
    /// bucket queue (Dial's algorithm) settles cells in cost order, so the search runs in time
    /// linear in the cells and can be time-sliced: <see cref="Step"/> settles a budget of cells per
    /// call and the result is the same however it is sliced.
    /// </summary>
    public sealed class FlowField
    {
        public const int Straight = 10, Diagonal = 14;
        public const float MetresPerCost = NavGrid.CellM / Straight;
        public const int Unreached = int.MaxValue;

        public readonly NavGrid Grid;
        public readonly float MinClearance;
        public readonly int[] Cost;
        public Vector2 Source { get; private set; }
        public int SourceCell { get; private set; } = -1;
        public bool Building { get; private set; }
        public bool Complete { get; private set; }
        /// <summary>Cells settled by the last (or current) build.</summary>
        public int Settled { get; private set; }
        /// <summary>Cells costlier than this stay unreached (int.MaxValue: no limit).</summary>
        public int MaxCost = int.MaxValue;

        readonly bool[] passable;
        readonly int[] straightOff, diagOff, diagA, diagB;
        readonly int[][] queue = new int[16][];
        readonly int[] queueCount = new int[16];
        int cur, pending;

        public FlowField(NavGrid grid, float minClearance)
        {
            Grid = grid;
            MinClearance = minClearance;
            passable = grid.PassableFor(minClearance);
            Cost = new int[grid.W * grid.H];
            int w = grid.W;
            straightOff = new[] { 1, -1, w, -w };
            diagOff = new[] { w + 1, w - 1, -w + 1, -w - 1 };
            diagA = new[] { 1, -1, 1, -1 };     // each diagonal's two straight neighbours must be open
            diagB = new[] { w, w, -w, -w };
            for (int i = 0; i < 16; i++) queue[i] = new int[256];
            for (int i = 0; i < Cost.Length; i++) Cost[i] = Unreached;
        }

        public bool IsPassable(int cell) => cell >= 0 && passable[cell];

        /// <summary>Starts a search from the open cells nearest <paramref name="source"/> (its own cell, or the open cells within 1 m).</summary>
        public void Begin(Vector2 source)
        {
            Reset();
            Source = source;
            int c = Grid.CellIndex(source);
            SourceCell = c;
            if (c >= 0 && passable[c]) Seed(c);
            else SeedWithin(source, 1f);
        }

        /// <summary>Starts a search from every open cell within <paramref name="r"/> of <paramref name="centre"/> (a goal area), widening to 1 m if none.</summary>
        public void BeginArea(Vector2 centre, float r)
        {
            Reset();
            Source = centre;
            SourceCell = Grid.CellIndex(centre);
            SeedWithin(centre, r);
            if (pending == 0) SeedWithin(centre, r + 1f);
        }

        void Reset()
        {
            for (int i = 0; i < Cost.Length; i++) Cost[i] = Unreached;
            for (int i = 0; i < 16; i++) queueCount[i] = 0;
            cur = 0; pending = 0; Settled = 0;
            Building = true;
            Complete = false;
        }

        void SeedWithin(Vector2 c, float r)
        {
            int x0 = (int)Math.Floor((c.X - r - Grid.Min.X) / NavGrid.CellM), x1 = (int)Math.Floor((c.X + r - Grid.Min.X) / NavGrid.CellM);
            int y0 = (int)Math.Floor((c.Y - r - Grid.Min.Y) / NavGrid.CellM), y1 = (int)Math.Floor((c.Y + r - Grid.Min.Y) / NavGrid.CellM);
            for (int y = Math.Max(1, y0); y <= Math.Min(Grid.H - 2, y1); y++)
                for (int x = Math.Max(1, x0); x <= Math.Min(Grid.W - 2, x1); x++)
                {
                    int i = y * Grid.W + x;
                    if (passable[i] && Vector2.Distance(Grid.CellCenter(x, y), c) <= r) Seed(i);
                }
        }

        void Seed(int cell)
        {
            if (Cost[cell] == 0) return;
            Cost[cell] = 0;
            Push(0, cell);
        }

        void Push(int cost, int cell)
        {
            int b = cost & 15;
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
                if (queueCount[b] == 0) { cur++; continue; }
                int node = queue[b][--queueCount[b]];
                pending--;
                if (Cost[node] != cur) continue;   // a stale entry: the cell was reached more cheaply
                for (int k = 0; k < 4; k++)
                {
                    int n = node + straightOff[k];
                    if (!passable[n]) continue;
                    int nc = cur + Straight;
                    if (nc < Cost[n] && nc <= MaxCost) { Cost[n] = nc; Push(nc, n); }
                }
                for (int k = 0; k < 4; k++)
                {
                    int n = node + diagOff[k];
                    if (!passable[n] || !passable[node + diagA[k]] || !passable[node + diagB[k]]) continue;
                    int nc = cur + Diagonal;
                    if (nc < Cost[n] && nc <= MaxCost) { Cost[n] = nc; Push(nc, n); }
                }
                Settled++;
                if (++done >= budget && pending > 0) return false;
            }
            Building = false;
            Complete = true;
            return true;
        }

        public void BuildAll() => Step(int.MaxValue);

        /// <summary>The cost from <paramref name="p"/>'s cell to the nearest source, in metres; +∞ if unreached.</summary>
        public float CostAt(Vector2 p)
        {
            int c = Grid.CellIndex(p);
            return c < 0 || Cost[c] == Unreached ? float.PositiveInfinity : Cost[c] * MetresPerCost;
        }

        public bool Reached(Vector2 p)
        {
            int c = Grid.CellIndex(p);
            return c >= 0 && Cost[c] != Unreached;
        }

        /// <summary>
        /// The way downhill at <paramref name="p"/>: the cost's gradient, interpolated over the four
        /// cell centres around it for smooth headings; where one of them is unreached, the way to the
        /// cheapest of its own cell's eight neighbours. False where nothing is reached or at a source.
        /// </summary>
        public bool Direction(Vector2 p, out Vector2 dir)
        {
            dir = Vector2.Zero;
            float gx = (p.X - Grid.Min.X) / NavGrid.CellM - 0.5f, gy = (p.Y - Grid.Min.Y) / NavGrid.CellM - 0.5f;
            int x0 = (int)Math.Floor(gx), y0 = (int)Math.Floor(gy);
            float fx = gx - x0, fy = gy - y0;
            int w = Grid.W;
            if (x0 >= 0 && y0 >= 0 && x0 + 1 < w && y0 + 1 < Grid.H)
            {
                int i00 = y0 * w + x0;
                int c00 = Cost[i00], c10 = Cost[i00 + 1], c01 = Cost[i00 + w], c11 = Cost[i00 + w + 1];
                if (c00 != Unreached && c10 != Unreached && c01 != Unreached && c11 != Unreached)
                {
                    float dx = (c10 - c00) * (1 - fy) + (c11 - c01) * fy;
                    float dy = (c01 - c00) * (1 - fx) + (c11 - c10) * fx;
                    float l = (float)Math.Sqrt(dx * dx + dy * dy);
                    if (l > 1e-3f) { dir = new Vector2(-dx / l, -dy / l); return true; }
                }
            }
            int c = Grid.CellIndex(p);
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
                    if (Cost[n] < best) { best = Cost[n]; bestN = n; }
                }
            if (bestN < 0) return false;
            Vector2 d = Grid.CellCenter(bestN) - p;
            float dl = d.Length();
            if (dl < 1e-4f) return false;
            dir = d / dl;
            return true;
        }
    }
}
