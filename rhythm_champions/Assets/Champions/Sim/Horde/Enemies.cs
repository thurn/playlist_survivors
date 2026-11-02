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
        /// <summary>The spawn's type (the horde's; 0 for a placed <see cref="Dummy"/>).</summary>
        public int Type;
        public EnemyFlags Flags;

        public bool Alive => (Flags & EnemyFlags.Alive) != 0;
        public bool Boss => (Flags & EnemyFlags.Boss) != 0;
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
    /// The grid hashes 2 m cells into buckets (the arena is infinite) and is rebuilt lazily, by a counting
    /// sort, on the first query after <see cref="Moved"/> or a spawn: whoever moves enemies calls
    /// <see cref="Moved"/> once after. A kill leaves the grid alone: queries skip the dead. The client reads
    /// enemies through the indexer; only the sim's systems change them (<see cref="Edit"/>).
    /// </summary>
    public sealed class Enemies : IStrikeTargets
    {
        public const double CellM = 2.0;
        const int Buckets = 4096;

        readonly StructList<Enemy> slots;
        readonly StructList<int> free;
        // A strike's candidates.
        readonly StructList<int> near;
        // The grid: bucketStart[b]..bucketStart[b + 1] index cellItems, sorted by bucket then id.
        readonly int[] bucketStart = new int[Buckets + 1];
        int[] cellItems;
        int[] cellOf;
        bool gridValid;

        public Enemies(int capacity)
        {
            slots = new StructList<Enemy>(capacity);
            free = new StructList<int>(capacity);
            near = new StructList<int>(capacity);
            cellItems = new int[Math.Max(1, capacity)];
            cellOf = new int[Math.Max(1, capacity)];
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
                X = x, Y = y, Radius = radius, Health = health, Type = type,
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
        public void Near(double x, double y, double reach, StructList<int> into)
        {
            into.Clear();
            if (Alive == 0) return;
            if (!gridValid) Rebuild();
            double r = reach + MaxRadius;
            int cx0 = Cell(x - r), cx1 = Cell(x + r), cy0 = Cell(y - r), cy1 = Cell(y + r);
            for (int cy = cy0; cy <= cy1; cy++)
            {
                for (int cx = cx0; cx <= cx1; cx++)
                {
                    int key = Key(cx, cy), b = Bucket(key);
                    for (int k = bucketStart[b]; k < bucketStart[b + 1]; k++)
                    {
                        int id = cellItems[k];
                        // Another cell sharing the bucket: it is visited (or not) as itself.
                        if (cellOf[id] != key) continue;
                        ref var e = ref slots[id];
                        if (!e.Alive) continue;
                        double dx = e.X - x, dy = e.Y - y, within = reach + e.Radius;
                        if (dx * dx + dy * dy <= within * within) into.Add(id);
                    }
                }
            }
        }

        void Rebuild()
        {
            Array.Clear(bucketStart, 0, bucketStart.Length);
            int n = slots.Count;
            for (int id = 0; id < n; id++)
            {
                ref var e = ref slots[id];
                if (!e.Alive) continue;
                int key = Key(Cell(e.X), Cell(e.Y));
                cellOf[id] = key;
                bucketStart[Bucket(key) + 1]++;
            }
            for (int b = 0; b < Buckets; b++) bucketStart[b + 1] += bucketStart[b];
            // Fill each bucket in id order, using bucketStart as cursors, then shift them back.
            for (int id = 0; id < n; id++)
            {
                if (!slots[id].Alive) continue;
                cellItems[bucketStart[Bucket(cellOf[id])]++] = id;
            }
            for (int b = Buckets; b > 0; b--) bucketStart[b] = bucketStart[b - 1];
            bucketStart[0] = 0;
            gridValid = true;
        }

        void IStrikeTargets.Reach(in HitShape shape, double t, StructList<int> into)
        {
            shape.Bounds(out double cx, out double cy, out double radius);
            Near(cx, cy, radius, near);
            for (int i = 0; i < near.Count; i++)
            {
                ref readonly var e = ref slots[near[i]];
                if (shape.Touches(e.X, e.Y, e.Radius)) into.Add(near[i]);
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
            bool boss = e.Boss;
            double damage = boss ? blow.Damage * sim.Bosses.DamageScale : blow.Shatter ? Math.Max(blow.Damage, e.Health) : blow.Damage;
            e.Health -= damage;
            if (e.Health > 1e-9)
            {
                sim.Emit(SimEvent.Hit(blow.T, id, blow.Source, blow.Flags, dx, dy, damage, e.Health));
                if (boss) sim.Bosses.Struck(blow.T);
                return false;
            }
            double x = e.X, y = e.Y;
            int type = e.Type;
            Kill(id);
            sim.Emit(SimEvent.Kill(blow.T, id, blow.Source, blow.Flags, dx, dy, damage, x, y));
            sim.Arsenal.Killed(sim, blow, x, y);
            if (boss) sim.Bosses.Killed(blow.T, dx, dy);
            sim.Progression.Killed(sim, type, blow.T);
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
