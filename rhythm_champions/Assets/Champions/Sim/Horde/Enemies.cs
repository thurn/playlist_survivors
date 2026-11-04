using System;

namespace Champions.Sim
{
    /// <summary>An enemy's flags (<see cref="Enemy.Flags"/>).</summary>
    [Flags]
    public enum EnemyFlags : byte
    {
        None = 0,
        Alive = 1,
        /// <summary>A boss: aimed at like any enemy and never weighted (design.md, "Rules every attack keeps").</summary>
        Boss = 2,
    }

    /// <summary>
    /// One enemy as the sim holds it. Positions are ground-plane metres; health is in Sword Slash's base hits
    /// (design.md, "The attacks": all damage is relative to it). The horde's bead adds its type, steps and
    /// moves after these.
    /// </summary>
    public struct Enemy
    {
        public double X, Y;
        /// <summary>The body's radius: a blow reaching its edge hits it.</summary>
        public double Radius;
        public double Health;
        public long Generation;
        /// <summary>The spawn's type (the horde's; 0 for a placed <see cref="Dummy"/>).</summary>
        public int Type;
        public EnemyFlags Flags;

        public readonly bool Alive => (Flags & EnemyFlags.Alive) != 0;
        public readonly bool Boss => (Flags & EnemyFlags.Boss) != 0;
    }

    /// <summary>A standing enemy placed at the start (<see cref="RunInputs.Dummies"/>): scenes for tests, stills and scripted checks.</summary>
    public struct Dummy
    {
        public double X, Y, Radius, Health;

        public Dummy(double x, double y, double health, double radius = 0.5)
        {
            X = x;
            Y = y;
            Radius = radius;
            Health = health;
        }
    }

    /// <summary>A pure eligibility predicate; it must not mutate enemies or reenter queries.</summary>
    internal interface IEnemyFilter { bool Accept(in Enemy enemy); }

    /// <summary>
    /// The enemies: dense slots that ids index (a dead enemy's slot is reused by a later spawn, which the
    /// spawn's event says), and one uniform grid over the living for every "who is near" question: aims,
    /// hits and, for the horde, separation, contact and steering (plan.md, "Systems": one spatial hash).
    /// The hero's blows land on them through <see cref="Strikes"/>: a hit takes the blow's damage from the
    /// health, and one that kills shatters the enemy on the blow's frame, thrown away from it
    /// (<see cref="SimEventKind.Hit"/>, <see cref="SimEventKind.Kill"/>; the kill mods and the kill's experience follow). A
    /// boss's hits go on to <see cref="Bosses"/> (Break's damage, phase two, its death); nothing here moves an enemy, so a
    /// boss is never knocked back.
    ///
    /// The grid stores 2 m cells in contiguous rows for local crowds, or hashed buckets when spread
    /// across the infinite arena. Bodies of radius over 1 m are checked separately. A counting sort
    /// rebuilds it lazily on the first query after
    /// <see cref="Moved"/> or a spawn: whoever moves enemies calls <see cref="Moved"/> once after.
    /// A kill leaves the grid alone: kills mark their cached entries dead. The client reads enemies through the indexer; only the sim's systems change them (<see cref="Edit"/>).
    /// </summary>
    public sealed class Enemies : IStrikeTargets
    {
        public const double CellM = 2.0;
        const int Buckets = 4096;
        const int MaxDenseCells = 16384;

        readonly StructList<Enemy> slots;
        readonly StructList<int> free;
        // A strike's candidates.
        readonly StructList<int> near;
        // Cell or bucket starts index cellItems; each cell retains id order.
        readonly int[] bucketStart = new int[Buckets + 1];
        // Boss-sized bodies must not expand every small-body search across half the arena.
        const double GridRadius = 1;
        readonly StructList<int> large = new StructList<int>(16);
        double gridRadius;
        // Compact query data avoids chasing a large enemy slot for every rejected body.
        struct CellBody { public double X, Y, Radius; public int Id, Cell; }
        CellBody[] cellItems;
        int[] cellOf;
        int[] gridIndex;
        bool gridValid;
        int denseX, denseY, denseWidth, denseHeight;
        int[] denseStart = Array.Empty<int>();
        long generation;
        struct QueryRange
        {
            public int X0, X1, Y0, Y1, Start, Count;
            public long Epoch;
        }
        readonly QueryRange[] ranges = new QueryRange[512];
        int[] rangeItems = Array.Empty<int>();
        int rangeCount;
        struct CircleResult { public double X, Y, Reach; public long Epoch, Death; public int Start, Count; }
        readonly CircleResult[] circles = new CircleResult[512];
        int[] circleItems = Array.Empty<int>();
        int circleCount;
        long deaths;
        long gridEpoch;

        public Enemies(int capacity)
        {
            slots = new StructList<Enemy>(capacity);
            free = new StructList<int>(capacity);
            near = new StructList<int>(capacity);
            cellItems = new CellBody[Math.Max(1, capacity)];
            cellOf = new int[Math.Max(1, capacity)];
            gridIndex = new int[Math.Max(1, capacity)];
        }

        /// <summary>How many slots have ever been used: ids run below it.</summary>
        public int Count => slots.Count;

        /// <summary>The enemies alive now.</summary>
        public int Alive { get; private set; }

        /// <summary>The largest body radius ever spawned: what a query's reach adds to find every body it touches.</summary>
        public double MaxRadius { get; private set; }

        /// <summary>Enemy <paramref name="id"/>, alive or not.</summary>
        public ref readonly Enemy this[int id] => ref slots[id];

        /// <summary>Enemy <paramref name="id"/> to change (the systems'; after moving enemies, call <see cref="Moved"/>).</summary>
        internal ref Enemy Edit(int id) => ref slots[id];

        /// <summary>Every slot, alive or not.</summary>
        public ReadOnlySpan<Enemy> All => slots.AsSpan();

        /// <summary>A new enemy, alive at (<paramref name="x"/>, <paramref name="y"/>); emits <see cref="SimEventKind.EnemySpawn"/>.</summary>
        internal int Spawn(Simulation sim, double x, double y, double radius, double health, int type = 0, bool boss = false)
        {
            if (!(radius > 0) || !(health > 0)) throw new ArgumentOutOfRangeException(nameof(health), "an enemy needs a body and health");
            var e = new Enemy
            {
                X = x, Y = y, Radius = radius, Health = health, Type = type, Generation = ++generation,
                Flags = EnemyFlags.Alive | (boss ? EnemyFlags.Boss : 0),
            };
            int id;
            if (free.Count > 0)
            {
                id = free.Last;
                free.RemoveLast();
                slots[id] = e;
            }
            else
            {
                id = slots.Count;
                slots.Add(e);
                if (cellItems.Length < slots.Capacity)
                {
                    Array.Resize(ref cellItems, slots.Capacity);
                    Array.Resize(ref cellOf, slots.Capacity);
                    Array.Resize(ref gridIndex, slots.Capacity);
                }
            }
            Alive++;
            MaxRadius = Math.Max(MaxRadius, radius);
            gridValid = false;
            sim.Emit(SimEvent.EnemySpawn(sim.Now, id, type, boss, x, y, radius, health));
            return id;
        }

        /// <summary>Enemy <paramref name="id"/> dies: its slot is free for a later spawn.</summary>
        internal void Kill(int id)
        {
            ref var e = ref slots[id];
            if (!e.Alive) return;
            // No rebuild for a death: a negative cached id skips the body without an
            // indirect living-state read on every successful neighbourhood candidate.
            if (gridValid && e.Radius <= GridRadius) cellItems[gridIndex[id]].Id = -1;
            deaths++;
            e.Flags &= ~EnemyFlags.Alive;
            e.Health = 0;
            free.Add(id);
            Alive--;
        }

        /// <summary>Positions changed: the grid is rebuilt at the next query.</summary>
        internal void Moved() => gridValid = false;

        /// <summary>
        /// Adds to <paramref name="into"/> (cleared first) every living enemy whose body comes within
        /// <paramref name="reach"/> of (<paramref name="x"/>, <paramref name="y"/>), in a fixed order.
        /// </summary>
        public void Near(double x, double y, double reach, StructList<int> into) => Near(x, y, reach, into, true);

        internal void Near(double x, double y, double reach, StructList<int> into, bool cacheCircle)
        {
            var output = into.ResetForWrite(Alive);
            if (Alive == 0) return;
            int found = 0;
            if (!gridValid) Rebuild();
            // Repeated chain/volley queries share exact geometry until movement or spawning.
            // Cache values, never the caller's buffer; deaths filter ids without changing their order.
            int circleSlot = -1;
            if (cacheCircle)
            {
                long bits = BitConverter.DoubleToInt64Bits(x) ^ (BitConverter.DoubleToInt64Bits(y) * 397) ^ (BitConverter.DoubleToInt64Bits(reach) * 7919);
                int circleHash = unchecked((int)(bits ^ (bits >> 32)));
                circleSlot = circleHash & (circles.Length - 1);
                ref var circle = ref circles[circleSlot];
                if (circle.Epoch == gridEpoch && circle.X == x && circle.Y == y && circle.Reach == reach)
                {
                    if (circle.Death == deaths)
                    {
                        Array.Copy(circleItems, circle.Start, output, 0, circle.Count);
                        found = circle.Count;
                    }
                    else
                        for (int k = circle.Start; k < circle.Start + circle.Count; k++)
                        {
                            int id = circleItems[k];
                            if (slots[id].Alive) output[found++] = id;
                        }
                    into.Written(found);
                    return;
                }
            }

            double r = reach + gridRadius;
            int cx0 = Cell(x - r), cx1 = Cell(x + r), cy0 = Cell(y - r), cy1 = Cell(y + r);
            if (denseWidth > 0)
            {
                int x0 = Math.Max(cx0, denseX) - denseX, x1 = Math.Min(cx1, denseX + denseWidth - 1) - denseX;
                int y0 = Math.Max(cy0, denseY), y1 = Math.Min(cy1, denseY + denseHeight - 1);
                if (x0 <= x1)
                    for (int cy = y0; cy <= y1; cy++)
                    {
                        int row = (cy - denseY) * denseWidth;
                        // Row/cell/id order is contiguous, including any empty cells between.
                        int start = denseStart[row + x0], stop = denseStart[row + x1 + 1];
                        var rowBodies = new ReadOnlySpan<CellBody>(cellItems, start, stop - start);
                        foreach (ref readonly var body in rowBodies)
                        {
                            if (body.Id < 0) continue;
                            double dx = body.X - x, dy = body.Y - y, within = reach + body.Radius;
                            if (dx * dx + dy * dy <= within * within) output[found++] = body.Id;
                        }
                    }
            }
            else
            {
                // A synchronized movement batch asks many nearby questions against the same
                // grid. Cache the ordered cell contents, then do each query's exact circle test.
                // The cache is direct-mapped and bounded; a collision or full buffer just misses.
                int hash = unchecked(cx0 * 73856093 ^ cx1 * 19349663 ^ cy0 * 83492791 ^ cy1 * 15731);
                ref var range = ref ranges[hash & (ranges.Length - 1)];
                if (range.Epoch == gridEpoch && range.X0 == cx0 && range.X1 == cx1 && range.Y0 == cy0 && range.Y1 == cy1)
                {
                    int stop = range.Start + range.Count;
                    for (int i = range.Start; i < stop; i++)
                    {
                        ref readonly var body = ref cellItems[rangeItems[i]];
                        if (body.Id < 0) continue;
                        double dx = body.X - x, dy = body.Y - y, within = reach + body.Radius;
                        if (dx * dx + dy * dy <= within * within) output[found++] = body.Id;
                    }
                }
                else
                {
                    int start = rangeCount;
                    bool cache = rangeItems.Length - start >= slots.Count;
                    for (int cy = cy0; cy <= cy1; cy++)
                    {
                        for (int cx = cx0; cx <= cx1; cx++)
                        {
                            int key = Key(cx, cy), b = Bucket(key);
                            int cellStop = bucketStart[b + 1];
                            for (int k = bucketStart[b]; k < cellStop; k++)
                            {
                                ref readonly var body = ref cellItems[k];
                                if (body.Cell != key) continue;
                                if (cache) rangeItems[rangeCount++] = k;
                                if (body.Id < 0) continue;
                                double dx = body.X - x, dy = body.Y - y, within = reach + body.Radius;
                                if (dx * dx + dy * dy <= within * within) output[found++] = body.Id;
                            }
                        }
                    }
                    if (cache) range = new QueryRange { Epoch = gridEpoch, X0 = cx0, X1 = cx1, Y0 = cy0, Y1 = cy1,
                        Start = start, Count = rangeCount - start };
                }
            }
            // Insert the few large bodies in the original row/cell/id order. Collision pushes,
            // equal-distance targets and proc budgets all depend on that order.
            for (int i = 0; i < large.Count; i++)
            {
                int id = large[i];
                ref readonly var e = ref slots[id];
                if (!e.Alive) continue;
                double dx = e.X - x, dy = e.Y - y, within = reach + e.Radius;
                if (dx * dx + dy * dy > within * within) continue;
                int lo = 0, hi = found;
                while (lo < hi)
                {
                    int mid = lo + (hi - lo) / 2;
                    if (Before(id, output[mid])) hi = mid;
                    else lo = mid + 1;
                }
                Array.Copy(output, lo, output, lo + 1, found - lo);
                output[lo] = id;
                found++;
            }
            if (cacheCircle && circleItems.Length - circleCount >= found)
            {
                Array.Copy(output, 0, circleItems, circleCount, found);
                circles[circleSlot] = new CircleResult { X = x, Y = y, Reach = reach, Epoch = gridEpoch, Death = deaths, Start = circleCount, Count = found };
                circleCount += found;
            }
            into.Written(found);
        }

        /// <summary>Nearest eligible centre in body reach; ties use id, independently of cell traversal.</summary>
        internal int Nearest<T>(double x, double y, double reach, T filter) where T : struct, IEnemyFilter
        {
            if (Alive == 0) return -1;
            if (!gridValid) Rebuild();
            int best = -1; double distance = double.PositiveInfinity;
            double rings = (reach + gridRadius) / CellM;
            if (denseWidth == 0 || !(rings >= 0 && rings <= 16))
            {
                Near(x, y, reach, near);
                var ids = near.AsSpan(); var all = slots.AsSpan();
                foreach (int id in ids)
                {
                    ref readonly var e = ref all[id];
                    double dx = e.X - x, dy = e.Y - y, d = dx * dx + dy * dy;
                    if (d > distance || d == distance && id > best || !filter.Accept(e)) continue;
                    best = id; distance = d;
                }
                return best;
            }
            // Large bodies are outside the ordinary grid. The nearest metric is centre
            // distance; a body edge only determines whether it is in reach.
            for (int i = 0; i < large.Count; i++)
            {
                int id = large[i]; ref readonly var e = ref slots[id];
                if (!e.Alive) continue;
                double dx = e.X - x, dy = e.Y - y, d = dx * dx + dy * dy, within = reach + e.Radius;
                if (d > within * within || d > distance || d == distance && id > best || !filter.Accept(e)) continue;
                best = id; distance = d;
            }
            int cx = Cell(x), cy = Cell(y);
            double radius = reach + gridRadius;
            int last = Math.Max(Math.Max(cx - Cell(x - radius), Cell(x + radius) - cx),
                Math.Max(cy - Cell(y - radius), Cell(y + radius) - cy));
            for (int ring = 0; ring <= last; ring++)
            {
                int left = cx - ring, right = cx + ring, bottom = cy - ring, top = cy + ring;
                for (int row = bottom; row <= top; row++)
                {
                    if (row == bottom || row == top)
                        for (int col = left; col <= right; col++) NearestCell(col, row, x, y, reach, ref filter, ref best, ref distance);
                    else
                    {
                        NearestCell(left, row, x, y, reach, ref filter, ref best, ref distance);
                        NearestCell(right, row, x, y, reach, ref filter, ref best, ref distance);
                    }
                }
                double edge = Math.Min(Math.Min(x - left * CellM, (right + 1) * CellM - x),
                    Math.Min(y - bottom * CellM, (top + 1) * CellM - y));
                // Remaining cells begin outside the covered square. Keep a conservative
                // numerical band at its edge, including equal-distance/id ties.
                if (edge * edge > distance + 1e-9) break;
            }
            return best;
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        void NearestCell<T>(int cx, int cy, double x, double y, double reach, ref T filter,
            ref int best, ref double distance) where T : struct, IEnemyFilter
        {
            int col = cx - denseX, row = cy - denseY;
            if ((uint)col >= (uint)denseWidth || (uint)row >= (uint)denseHeight) return;
            double left = cx * CellM, bottom = cy * CellM;
            double dx = x < left ? left - x : x > left + CellM ? x - (left + CellM) : 0;
            double dy = y < bottom ? bottom - y : y > bottom + CellM ? y - (bottom + CellM) : 0;
            if (dx * dx + dy * dy > distance + 1e-9) return;
            int cell = row * denseWidth + col, stop = denseStart[cell + 1];
            for (int k = denseStart[cell]; k < stop; k++)
            {
                ref readonly var body = ref cellItems[k];
                int id = body.Id;
                if (id < 0) continue;
                double ex = body.X - x, ey = body.Y - y, d = ex * ex + ey * ey, within = reach + body.Radius;
                if (d > distance || d == distance && id > best || d > within * within || !filter.Accept(slots[id])) continue;
                best = id; distance = d;
            }
        }

        bool Before(int a, int b)
        {
            int ac = cellOf[a], bc = cellOf[b];
            int ay = (short)(ac >> 16), by = (short)(bc >> 16);
            if (ay != by) return ay < by;
            int ax = (short)ac, bx = (short)bc;
            return ax != bx ? ax < bx : a < b;
        }

        void Rebuild()
        {
            gridEpoch++;
            rangeCount = 0;
            circleCount = 0;
            int capacity = Math.Max(512, slots.Count * 32);
            if (rangeItems.Length < capacity) Array.Resize(ref rangeItems, Math.Max(capacity, rangeItems.Length * 2));
            if (circleItems.Length < capacity) Array.Resize(ref circleItems, Math.Max(capacity, circleItems.Length * 2));
            Array.Clear(bucketStart, 0, bucketStart.Length);
            large.Clear(); gridRadius = 0;
            int n = slots.Count, minX = int.MaxValue, maxX = int.MinValue, minY = int.MaxValue, maxY = int.MinValue;
            for (int id = 0; id < n; id++)
            {
                ref var e = ref slots[id];
                if (!e.Alive) continue;
                int cx = Cell(e.X), cy = Cell(e.Y), key = Key(cx, cy);
                cellOf[id] = key;
                if (e.Radius > GridRadius) { large.Add(id); continue; }
                gridRadius = Math.Max(gridRadius, e.Radius);
                minX = Math.Min(minX, cx); maxX = Math.Max(maxX, cx);
                minY = Math.Min(minY, cy); maxY = Math.Max(maxY, cy);
                bucketStart[Bucket(key) + 1]++;
            }
            // Keep local crowds in row/cell/id order so a rectangle scans contiguous row spans.
            // Widely separated populations retain the bounded spatial hash.
            denseWidth = 0;
            long width = (long)maxX - minX + 1, height = (long)maxY - minY + 1;
            if (width > 0 && height > 0 && width <= MaxDenseCells && height <= MaxDenseCells && width * height <= MaxDenseCells &&
                minX >= short.MinValue && maxX <= short.MaxValue && minY >= short.MinValue && maxY <= short.MaxValue)
            {
                denseX = minX; denseY = minY; denseWidth = (int)width; denseHeight = (int)height;
                int cells = denseWidth * denseHeight;
                if (denseStart.Length < cells + 1) Array.Resize(ref denseStart, Math.Max(cells + 1, denseStart.Length * 2));
                Array.Clear(denseStart, 0, cells + 1);
                for (int id = 0; id < n; id++)
                    if (slots[id].Alive && slots[id].Radius <= GridRadius) denseStart[DenseCell(cellOf[id]) + 1]++;
            }
            var starts = denseWidth > 0 ? denseStart : bucketStart;
            int count = denseWidth > 0 ? denseWidth * denseHeight : Buckets;
            for (int b = 0; b < count; b++) starts[b + 1] += starts[b];
            for (int id = 0; id < n; id++)
            {
                ref readonly var e = ref slots[id];
                if (!e.Alive || e.Radius > GridRadius) continue;
                int index = starts[denseWidth > 0 ? DenseCell(cellOf[id]) : Bucket(cellOf[id])]++;
                gridIndex[id] = index;
                cellItems[index] = new CellBody { X = e.X, Y = e.Y, Radius = e.Radius, Id = id, Cell = cellOf[id] };
            }
            for (int b = count; b > 0; b--) starts[b] = starts[b - 1];
            starts[0] = 0;
            gridValid = true;
        }

        int DenseCell(int key) => ((short)(key >> 16) - denseY) * denseWidth + (short)key - denseX;

        void IStrikeTargets.Reach(in HitShape shape, double t, StructList<int> into)
        {
            shape.Bounds(out double cx, out double cy, out double radius);
            Near(cx, cy, radius, near);
            if (shape.Kind == ShapeKind.Cone)
            {
                var cone = new HitShape.ConeQuery(shape);
                for (int i = 0; i < near.Count; i++)
                {
                    ref readonly var e = ref slots[near[i]];
                    if (cone.Touches(e.X, e.Y, e.Radius)) into.Add(near[i]);
                }
            }
            else
            {
                for (int i = 0; i < near.Count; i++)
                {
                    ref readonly var e = ref slots[near[i]];
                    if (shape.Touches(e.X, e.Y, e.Radius)) into.Add(near[i]);
                }
            }
        }

        bool IStrikeTargets.Strike(Simulation sim, int id, in Blow blow)
        {
            ref var e = ref slots[id];
            if (!e.Alive) return false;
            double dx, dy;
            if (blow.Throw == ThrowKind.Along && blow.Shape.Kind == ShapeKind.Path)
            {
                double sx = blow.Shape.X2 - blow.Shape.X, sy = blow.Shape.Y2 - blow.Shape.Y, len = Math.Sqrt(sx * sx + sy * sy);
                dx = len > 1e-9 ? sx / len : 1;
                dy = len > 1e-9 ? sy / len : 0;
            }
            else blow.Shape.Away(e.X, e.Y, out dx, out dy);
            // A boss takes Break's extra damage while staggered and never breaks outright; a shattering blow kills a mob.
            bool boss = e.Boss, fight = boss && sim.Fight.Owns(id);
            double damage = boss ? blow.Damage * (fight ? sim.Fight.DamageScale : sim.Bosses.DamageScale) : blow.Shatter ? Math.Max(blow.Damage, e.Health) : blow.Damage;
            e.Health -= damage;
            if (e.Health > 1e-9)
            {
                sim.Emit(SimEvent.Hit(blow.T, id, blow.Source, blow.Flags, dx, dy, damage, e.Health));
                if (fight) sim.Fight.Struck(id, blow.T);
                else if (boss) sim.Bosses.Struck(blow.T);
                return false;
            }
            double x = e.X, y = e.Y;
            int type = e.Type;
            Kill(id);
            sim.Emit(SimEvent.Kill(blow.T, id, blow.Source, blow.Flags, dx, dy, damage, x, y));
            sim.Arsenal.Killed(sim, blow, x, y);
            if (fight) sim.Fight.Killed(id, blow.T, dx, dy);
            else if (boss) sim.Bosses.Killed(blow.T, dx, dy);
            sim.Progression.Killed(sim, type, blow.T, x, y);
            return true;
        }

        static int Cell(double v) => (int)Math.Floor(v / CellM);

        // Cells within ±32767 of the origin (65 km) pack into one int.
        static int Key(int cx, int cy) => (cx & 0xFFFF) | (cy << 16);

        static int Bucket(int key)
        {
            uint h = (uint)key * 0x9E3779B1u;
            return (int)(h >> 20);
        }
    }
}
