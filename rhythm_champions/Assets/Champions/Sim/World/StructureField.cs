using System;
using System.Collections.Generic;

namespace Champions.Sim
{
    /// <summary>What broke a structure (<see cref="SimEvent.StructureBreak"/>'s I2).</summary>
    public enum BreakCause : byte
    {
        /// <summary>The hero's blow (any damage it deals: a dash, an attack, a mod's effect).</summary>
        Blow = 0,
        /// <summary>A boss's attack.</summary>
        Boss = 1,
        /// <summary>A boss's entrance, shattering everything on screen.</summary>
        Entrance = 2,
    }

    /// <summary>One structure the field holds, standing or broken (its slot is kept until its chunk unloads).</summary>
    public struct Structure
    {
        public double X, Y;
        /// <summary>Its angle (radians from +x, anticlockwise) and that angle's cosine and sine.</summary>
        public double Angle, Cos, Sin;
        /// <summary>Its kind (<see cref="StructureRules.Kinds"/>).</summary>
        public int Kind;
        /// <summary>The hero's blows it takes still.</summary>
        public int Hits;
        /// <summary>Its chunk's key (<see cref="StructureChunks.Key"/>) and its index in the chunk's template.</summary>
        public long Chunk;
        public int Index;
        public bool Standing;
    }

    /// <summary>
    /// The world's structures (design.md, The World &gt; The infinite arena and Structures, Decision 18; plan.md, The Sim
    /// &gt; Systems): the arena's chunks (<see cref="StructureChunks"/>) loaded round the hero, and the structures in them,
    /// which block bodies and break under the hero's blows.
    ///
    /// - <b>Chunks</b> load once any of their square comes within <see cref="StructureRules.LoadM"/> of the hero (off
    ///   screen), rows then columns, and unload once all of it is beyond <see cref="StructureRules.UnloadM"/>. A
    ///   structure's footprint lies inside its chunk's lanes, so only the chunks a question's ground overlaps are asked.
    /// - <b>Breaks are remembered</b> per chunk: a broken structure stays broken when its chunk unloads and loads again.
    /// - <b>Blocking</b> (<see cref="Move"/>): a body, a circle, moves until it meets a standing footprint, then slides
    ///   along it; a body deep inside one (a structure loaded where a monster stood) walks out of it freely.
    /// - <b>Breaking</b>: every blow of the hero's that reaches a structure is one hit, whatever its damage; a small one
    ///   breaks in one, a building in two or three (<see cref="StructureKind.Hits"/>); a shattering blow, a boss's attack or
    ///   its entrance breaks it outright (<see cref="Shatter"/>). A break is thrown away from the blow
    ///   (<see cref="SimEventKind.StructureBreak"/>). Structures give nothing but spectacle.
    /// - <b>Duels</b> (<see cref="Duel"/>): a chunk that loads while a boss duel lasts stands empty until it unloads, so
    ///   no new structure comes near the duel wherever the boss chases the hero (design.md: none within 40 m; chunks load
    ///   at <see cref="StructureRules.LoadM"/>, farther, and the hero could walk up to them).
    /// </summary>
    public sealed class StructureField : IStructures
    {
        /// <summary>A body this close to a footprint touches it (m).</summary>
        const double Skin = 1e-4;
        /// <summary>A body deeper than this inside a footprint walks out of it freely (m).</summary>
        const double Deep = 0.25;
        /// <summary>The slides one move makes at most (a corner between two structures stops it).</summary>
        const int Slides = 3;

        sealed class Chunk
        {
            public int X, Y;
            public long Key;
            public bool Empty;
            public readonly StructList<int> Ids = new StructList<int>(StructureRules.MaxPerChunk);
        }

        readonly StructureRules rules;
        readonly ulong seed;
        readonly StructList<Structure> slots = new StructList<Structure>(1024);
        // The slots freed longest ago are taken first, so a slot a blow may still remember is reused last.
        readonly Queue<int> free = new Queue<int>(1024);
        readonly List<Chunk> loaded = new List<Chunk>();
        readonly Dictionary<long, Chunk> byKey = new Dictionary<long, Chunk>();
        readonly Stack<Chunk> pool = new Stack<Chunk>();
        // Per chunk ever touched: the template indices of its structures that broke.
        readonly Dictionary<long, ulong> broken = new Dictionary<long, ulong>();
        readonly SimRandom chunkRandom = new SimRandom(0);
        readonly StructList<Placed> placed = new StructList<Placed>(StructureRules.MaxPerChunk);
        readonly StructList<int> near = new StructList<int>(256);
        readonly StructList<int> shattered = new StructList<int>(256);

        internal StructureField(Simulation sim, StructureRules rules, ulong seed)
        {
            this.rules = rules;
            this.seed = seed;
            Tick(sim, sim.Now);
        }

        public StructureRules Rules => rules;

        /// <summary>How many slots have ever been used: ids run below it.</summary>
        public int Count => slots.Count;

        /// <summary>Structure <paramref name="id"/>, standing or not.</summary>
        public ref readonly Structure this[int id] => ref slots[id];

        /// <summary>The structures standing now, and every break this run.</summary>
        public int Standing { get; private set; }
        public int Breaks { get; private set; }

        public int ChunksLoaded => loaded.Count;

        /// <summary>A boss duel lasts: chunks that load stand empty.</summary>
        public bool InDuel { get; private set; }

        public bool IsLoaded(int cx, int cy) => byKey.ContainsKey(StructureChunks.Key(cx, cy));

        /// <summary>Did structure <paramref name="index"/> of chunk (<paramref name="cx"/>, <paramref name="cy"/>)'s template break this run?</summary>
        public bool WasBroken(int cx, int cy, int index) =>
            broken.TryGetValue(StructureChunks.Key(cx, cy), out ulong mask) && (mask >> index & 1) != 0;

        /// <summary>The ids of the structures in loaded chunk (<paramref name="cx"/>, <paramref name="cy"/>), standing or broken (none if it isn't loaded).</summary>
        public ReadOnlySpan<int> InChunk(int cx, int cy) =>
            byKey.TryGetValue(StructureChunks.Key(cx, cy), out var c) ? c.Ids.AsSpan() : ReadOnlySpan<int>.Empty;

        /// <summary>Loads the chunks the hero nears and unloads those it left, at the tick's end.</summary>
        internal void Tick(Simulation sim, double end)
        {
            sim.HeroWas(end, out double hx, out double hy);
            for (int i = 0; i < loaded.Count; i++)
            {
                if (SquareDistance(loaded[i].X, loaded[i].Y, hx, hy) <= rules.UnloadM) continue;
                Unload(sim, end, loaded[i]);
                loaded.RemoveAt(i--);
            }
            int reach = (int)Math.Floor(rules.LoadM / rules.ChunkM) + 1;
            StructureChunks.ChunkOf(rules, hx, hy, out int hcx, out int hcy);
            for (int cy = hcy - reach; cy <= hcy + reach; cy++)
                for (int cx = hcx - reach; cx <= hcx + reach; cx++)
                    if (!byKey.ContainsKey(StructureChunks.Key(cx, cy)) && SquareDistance(cx, cy, hx, hy) <= rules.LoadM)
                        Load(sim, end, cx, cy);
        }

        /// <summary>A boss duel begins or ends (the bosses' bead).</summary>
        internal void Duel(bool on) => InDuel = on;

        /// <summary>
        /// Breaks outright every standing structure <paramref name="shape"/> reaches at <paramref name="t"/>, thrown away
        /// from it: a boss's attack, or its entrance's shockwave. Returns how many broke.
        /// </summary>
        internal int Shatter(Simulation sim, in HitShape shape, double t, BreakCause cause)
        {
            shattered.Clear();
            Reach(shape, t, shattered);
            for (int i = 0; i < shattered.Count; i++)
            {
                ref var s = ref slots[shattered[i]];
                shape.Away(s.X, s.Y, out double dx, out double dy);
                Break(sim, shattered[i], t, cause, dx, dy, 0);
            }
            return shattered.Count;
        }

        /// <summary>The distance from (<paramref name="x"/>, <paramref name="y"/>) to chunk (<paramref name="cx"/>, <paramref name="cy"/>)'s square (0 inside).</summary>
        double SquareDistance(int cx, int cy, double x, double y)
        {
            double size = rules.ChunkM, x0 = cx * size, y0 = cy * size;
            double dx = Math.Max(0, Math.Max(x0 - x, x - (x0 + size))), dy = Math.Max(0, Math.Max(y0 - y, y - (y0 + size)));
            return Math.Sqrt(dx * dx + dy * dy);
        }

        void Load(Simulation sim, double t, int cx, int cy)
        {
            var chunk = pool.Count > 0 ? pool.Pop() : new Chunk();
            chunk.X = cx;
            chunk.Y = cy;
            chunk.Key = StructureChunks.Key(cx, cy);
            chunk.Empty = InDuel;
            chunk.Ids.Clear();
            loaded.Add(chunk);
            byKey[chunk.Key] = chunk;
            if (chunk.Empty) return;
            broken.TryGetValue(chunk.Key, out ulong gone);
            StructureChunks.Generate(rules, seed, cx, cy, chunkRandom, placed);
            for (int i = 0; i < placed.Count; i++)
            {
                var p = placed[i];
                if ((gone >> p.Index & 1) != 0) continue;
                var kind = rules.Kinds[p.Kind];
                var s = new Structure
                {
                    X = p.X, Y = p.Y, Angle = p.Angle, Cos = SimMath.Cos(p.Angle), Sin = SimMath.Sin(p.Angle), Kind = p.Kind, Hits = kind.Hits,
                    Chunk = chunk.Key, Index = p.Index, Standing = true,
                };
                int id;
                if (free.Count > 0)
                {
                    id = free.Dequeue();
                    slots[id] = s;
                }
                else
                {
                    id = slots.Count;
                    slots.Add(s);
                }
                chunk.Ids.Add(id);
                Standing++;
                sim.Emit(SimEvent.StructureSpawn(t, id, p.Kind, kind.Hits, p.X, p.Y, p.Angle));
            }
        }

        void Unload(Simulation sim, double t, Chunk chunk)
        {
            for (int i = 0; i < chunk.Ids.Count; i++)
            {
                int id = chunk.Ids[i];
                ref var s = ref slots[id];
                if (s.Standing)
                {
                    s.Standing = false;
                    Standing--;
                    sim.Emit(SimEvent.StructureGone(t, id, s.X, s.Y));
                }
                free.Enqueue(id);
            }
            byKey.Remove(chunk.Key);
            pool.Push(chunk);
        }

        void Break(Simulation sim, int id, double t, BreakCause cause, double dx, double dy, double power)
        {
            ref var s = ref slots[id];
            if (!s.Standing) return;
            s.Standing = false;
            s.Hits = 0;
            Standing--;
            Breaks++;
            broken.TryGetValue(s.Chunk, out ulong mask);
            broken[s.Chunk] = mask | 1UL << s.Index;
            sim.Emit(SimEvent.StructureBreak(t, id, s.Kind, cause, dx, dy, power, s.X, s.Y));
        }

        // ---- What the sim asks of structures ----

        /// <summary>Adds to <paramref name="into"/> (cleared first) every standing structure in the chunks the rectangle overlaps, in a fixed order.</summary>
        void Near(double x0, double y0, double x1, double y1, StructList<int> into)
        {
            into.Clear();
            if (Standing == 0) return;
            StructureChunks.ChunkOf(rules, x0, y0, out int cx0, out int cy0);
            StructureChunks.ChunkOf(rules, x1, y1, out int cx1, out int cy1);
            if ((long)(cx1 - cx0 + 1) * (cy1 - cy0 + 1) > loaded.Count)
            {
                // A shape wider than the loaded ground: every loaded chunk, in load order.
                foreach (var c in loaded) Add(c, into);
                return;
            }
            for (int cy = cy0; cy <= cy1; cy++)
                for (int cx = cx0; cx <= cx1; cx++)
                    if (byKey.TryGetValue(StructureChunks.Key(cx, cy), out var c)) Add(c, into);
        }

        void Add(Chunk c, StructList<int> into)
        {
            for (int i = 0; i < c.Ids.Count; i++)
                if (slots[c.Ids[i]].Standing) into.Add(c.Ids[i]);
        }

        public void Reach(in HitShape shape, double t, StructList<int> into)
        {
            shape.Bounds(out double cx, out double cy, out double r);
            Near(cx - r, cy - r, cx + r, cy + r, near);
            for (int i = 0; i < near.Count; i++)
            {
                ref readonly var s = ref slots[near[i]];
                var k = rules.Kinds[s.Kind];
                bool touches = k.Shape == FootprintShape.Circle
                    ? shape.Touches(s.X, s.Y, k.Radius)
                    : shape.TouchesBox(s.X, s.Y, s.Cos, s.Sin, k.HalfX, k.HalfY);
                if (touches) into.Add(near[i]);
            }
        }

        public bool Strike(Simulation sim, int id, in Blow blow)
        {
            ref var s = ref slots[id];
            if (!s.Standing) return false;
            blow.Shape.Away(s.X, s.Y, out double dx, out double dy);
            if (!blow.Shatter && --s.Hits > 0)
            {
                sim.Emit(SimEvent.StructureHit(blow.T, id, s.Hits, blow.Source, dx, dy));
                return false;
            }
            Break(sim, id, blow.T, BreakCause.Blow, dx, dy, blow.Damage);
            return true;
        }

        public bool Breakable(int id) => id >= 0 && id < slots.Count && slots[id].Standing;

        public void Move(double x, double y, double radius, double dx, double dy, out double nx, out double ny, out int blocker)
        {
            blocker = -1;
            for (int pass = 0; pass < Slides; pass++)
            {
                double len = Math.Sqrt(dx * dx + dy * dy);
                if (len < 1e-12) break;
                Near(Math.Min(x, x + dx) - radius, Math.Min(y, y + dy) - radius, Math.Max(x, x + dx) + radius, Math.Max(y, y + dy) + radius, near);
                double best = double.PositiveInfinity, hnx = 0, hny = 0;
                int hit = -1;
                for (int i = 0; i < near.Count; i++)
                {
                    ref readonly var s = ref slots[near[i]];
                    var k = rules.Kinds[s.Kind];
                    if (HitShape.DistanceToSegment(s.X, s.Y, x, y, x + dx, y + dy) > k.Bound + radius + Skin) continue;
                    double gap = Distance(s, k, x, y, radius, out double ox, out double oy), at;
                    if (gap < -Deep) continue;
                    if (gap <= Skin)
                    {
                        // Touching: only a move into it is stopped.
                        if ((dx * ox + dy * oy) / len >= -1e-9) continue;
                        at = 0;
                    }
                    else
                    {
                        at = Sweep(s, k, x, y, radius, dx, dy);
                        if (!(at <= 1)) continue;
                        Distance(s, k, x + dx * at, y + dy * at, radius, out ox, out oy);
                    }
                    if (at < best)
                    {
                        best = at;
                        hit = near[i];
                        hnx = ox;
                        hny = oy;
                    }
                }
                if (hit < 0)
                {
                    x += dx;
                    y += dy;
                    break;
                }
                if (blocker < 0) blocker = hit;
                double go = Math.Max(0, best - Skin / len);
                x += dx * go;
                y += dy * go;
                // Slide: what is left of the move, less its part into the structure.
                double rx = dx * (1 - go), ry = dy * (1 - go), into = rx * hnx + ry * hny;
                if (into < 0)
                {
                    rx -= hnx * into;
                    ry -= hny * into;
                }
                dx = rx;
                dy = ry;
            }
            nx = x;
            ny = y;
        }

        /// <summary>
        /// How far a body of <paramref name="r"/> at (<paramref name="x"/>, <paramref name="y"/>) is from structure
        /// <paramref name="s"/>'s footprint (negative inside), and the way out of it at the nearest point (a unit vector).
        /// </summary>
        static double Distance(in Structure s, StructureKind k, double x, double y, double r, out double nx, out double ny)
        {
            if (k.Shape == FootprintShape.Circle)
            {
                double ex = x - s.X, ey = y - s.Y, d = Math.Sqrt(ex * ex + ey * ey);
                nx = d > 1e-12 ? ex / d : 1;
                ny = d > 1e-12 ? ey / d : 0;
                return d - k.Radius - r;
            }
            HitShape.ToBox(x, y, s.X, s.Y, s.Cos, s.Sin, out double px, out double py);
            double qx = Math.Abs(px) - k.HalfX, qy = Math.Abs(py) - k.HalfY, sx = px < 0 ? -1 : 1, sy = py < 0 ? -1 : 1;
            double lx, ly, gap;
            if (qx > 0 || qy > 0)
            {
                double ox = Math.Max(qx, 0), oy = Math.Max(qy, 0);
                gap = Math.Sqrt(ox * ox + oy * oy);
                lx = ox / gap * sx;
                ly = oy / gap * sy;
            }
            else if (qx > qy)
            {
                gap = qx;
                lx = sx;
                ly = 0;
            }
            else
            {
                gap = qy;
                lx = 0;
                ly = sy;
            }
            HitShape.FromBox(lx, ly, s.Cos, s.Sin, out nx, out ny);
            return gap - r;
        }

        /// <summary>
        /// The share of the move (<paramref name="dx"/>, <paramref name="dy"/>) at which a body of <paramref name="r"/> at
        /// (<paramref name="x"/>, <paramref name="y"/>), clear of the footprint, first touches it; infinity if it doesn't.
        /// A rectangle grown by r is two crossed rectangles and four corner discs.
        /// </summary>
        static double Sweep(in Structure s, StructureKind k, double x, double y, double r, double dx, double dy)
        {
            if (k.Shape == FootprintShape.Circle) return SweepDisc(x - s.X, y - s.Y, dx, dy, k.Radius + r);
            HitShape.ToBox(x, y, s.X, s.Y, s.Cos, s.Sin, out double px, out double py);
            HitShape.ToBox(dx, dy, 0, 0, s.Cos, s.Sin, out double ux, out double uy);
            double hx = k.HalfX, hy = k.HalfY;
            double t = Math.Min(SweepBox(px, py, ux, uy, hx + r, hy), SweepBox(px, py, ux, uy, hx, hy + r));
            for (int i = 0; i < 4; i++)
                t = Math.Min(t, SweepDisc(px - ((i & 1) == 0 ? -hx : hx), py - ((i & 2) == 0 ? -hy : hy), ux, uy, r));
            return t;
        }

        /// <summary>The share of the move at which a point at (px, py) from a disc's middle enters the disc of <paramref name="radius"/>.</summary>
        internal static double SweepDisc(double px, double py, double ux, double uy, double radius)
        {
            double a = ux * ux + uy * uy, b = px * ux + py * uy, c = px * px + py * py - radius * radius;
            if (c <= 0) return 0;
            if (b >= 0 || a < 1e-24) return double.PositiveInfinity;
            double disc = b * b - a * c;
            if (disc < 0) return double.PositiveInfinity;
            return (-b - Math.Sqrt(disc)) / a;
        }

        /// <summary>The share of the move at which a point enters the rectangle [-hx, hx] x [-hy, hy] (slabs).</summary>
        static double SweepBox(double px, double py, double ux, double uy, double hx, double hy)
        {
            double t0 = 0, t1 = double.PositiveInfinity;
            if (!Slab(px, ux, hx, ref t0, ref t1) || !Slab(py, uy, hy, ref t0, ref t1)) return double.PositiveInfinity;
            return t0;
        }

        static bool Slab(double p, double u, double h, ref double t0, ref double t1)
        {
            if (Math.Abs(u) < 1e-15) return p >= -h && p <= h;
            double a = (-h - p) / u, b = (h - p) / u;
            if (a > b) (a, b) = (b, a);
            t0 = Math.Max(t0, a);
            t1 = Math.Min(t1, b);
            return t0 <= t1;
        }
    }
}
