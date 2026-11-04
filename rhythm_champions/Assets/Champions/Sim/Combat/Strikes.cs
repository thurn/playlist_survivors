using System.Collections.Generic;

namespace Champions.Sim
{
    /// <summary>
    /// Something the hero's blows land on: the horde, the structures, a boss. Each registers with
    /// <see cref="Strikes.Add"/>.
    /// </summary>
    internal interface IStrikeTargets
    {
        /// <summary>
        /// Appends to <paramref name="into"/> the ids of the targets whose bodies <paramref name="shape"/>
        /// touches as the world was at song time <paramref name="t"/> (up to the sim's lead back: a heard
        /// dash's blows resolve at their own times).
        /// </summary>
        void Reach(in HitShape shape, double t, StructList<int> into);

        /// <summary>Lands <paramref name="blow"/> on target <paramref name="id"/>; true when that broke it (killed, shattered).</summary>
        bool Strike(Simulation sim, int id, in Blow blow);
    }

    /// <summary>
    /// The one way the hero's damage reaches the world (dashes and attacks): a blow lands on every
    /// registered target its shape reaches. The mods that change blows by tag apply here
    /// (<see cref="Arsenal.Modify"/> before, <see cref="Arsenal.Landed"/> after: echoes, repeats, shards).
    /// </summary>
    public sealed class Strikes
    {
        readonly List<IStrikeTargets> targets = new List<IStrikeTargets>();
        readonly StructList<int> reached;
        readonly List<StructList<int>> weaponReached = new List<StructList<int>>();
        int weaponDepth;
        readonly List<(int Receiver, int Id, double Distance)> supportedCandidates = new List<(int, int, double)>(64);

        public Strikes(int capacity)
        {
            reached = new StructList<int>(capacity);
        }

        /// <summary>Blows landed, and the strikes they made (a target reached), this run.</summary>
        public int Blows { get; private set; }
        public int Struck { get; private set; }

        /// <summary>Registers what blows land on (the horde, the structures, the bosses).</summary>
        internal void Add(IStrikeTargets target)
        {
            if (!targets.Contains(target)) targets.Add(target);
        }

        /// <summary>
        /// Lands <paramref name="blow"/> on every target it reaches, skipping those in <paramref name="struck"/>
        /// and adding those it strikes (so a path swept tick by tick strikes each target once). Returns the
        /// strikes made.
        /// </summary>
        internal int Land(Simulation sim, in Blow blow, StructList<long> struck = null)
        {
            var landed = blow;
            sim.Arsenal.Modify(ref landed);
            Blows++;
            int n = 0;
            for (int r = 0; r < targets.Count; r++)
            {
                var target = targets[r];
                reached.Clear();
                target.Reach(landed.Shape, landed.T, reached);
                for (int k = 0; k < reached.Count; k++)
                {
                    int id = reached[k];
                    if (!Claim(struck, Key(r, id))) continue;
                    target.Strike(sim, id, landed);
                    n++;
                }
            }
            Struck += n;
            sim.Arsenal.Landed(sim, blow, landed, n);
            return n;
        }

        /// <summary>
        /// Lands <paramref name="blow"/> on one target (a dash running into a structure), unless
        /// <paramref name="struck"/> holds it; true when it broke.
        /// </summary>
        internal bool StrikeOne(Simulation sim, IStrikeTargets target, int id, in Blow blow, StructList<long> struck)
        {
            int r = targets.IndexOf(target);
            if (r < 0 || !Claim(struck, Key(r, id))) return false;
            var landed = blow;
            sim.Arsenal.Modify(ref landed);
            Blows++;
            Struck++;
            bool broke = target.Strike(sim, id, landed);
            sim.Arsenal.Landed(sim, blow, landed, 1);
            return broke;
        }

        /// <summary>Survivor geometry uses its captured stats and lineage, bypassing archived mods.</summary>
        internal int LandWeapon(Simulation sim, in Blow blow, WeaponEffect effect)
        {
            Blows++;
            int depth = weaponDepth++;
            if (weaponReached.Count <= depth) weaponReached.Add(new StructList<int>(reached.Capacity));
            var candidates = weaponReached[depth];
            int n = 0;
            try
            {
                for (int r = 0; r < targets.Count; r++)
                {
                    var target = targets[r];
                    candidates.Clear();
                    target.Reach(blow.Shape, blow.T, candidates);
                    for (int k = 0; k < candidates.Count; k++)
                    {
                        int id = candidates[k];
                        long key = target is Enemies enemies ? enemies[id].Generation : Key(r + 1, id) | long.MinValue;
                        if (!sim.Weapons.Supports.Impact(effect, key, blow.T)) continue;
                        target.Strike(sim, id, blow);
                        n++;
                    }
                }
            }
            finally { weaponDepth--; }
            Struck += n;
            return n;
        }

        // Survivor damage is already evaluated; archived Arsenal mods must not evaluate it twice.
        internal void LandSupported(Simulation sim, in Blow blow, WeaponEffect effect)
        {
            var candidates = supportedCandidates;
            candidates.Clear();
            for (int r = 0; r < targets.Count; r++)
            {
                reached.Clear();
                targets[r].Reach(blow.Shape, blow.T, reached);
                foreach (int id in reached.AsSpan())
                {
                    double distance = id;
                    if (targets[r] == sim.Enemies)
                    {
                        ref readonly var e = ref sim.Enemies[id];
                        double dx = e.X - blow.Shape.X, dy = e.Y - blow.Shape.Y;
                        distance = dx * dx + dy * dy;
                    }
                    candidates.Add((r, id, distance));
                }
            }
            candidates.Sort((a, b) => { int c = a.Distance.CompareTo(b.Distance);
                if (c == 0) c = a.Receiver.CompareTo(b.Receiver);
                return c == 0 ? a.Id.CompareTo(b.Id) : c; });
            Blows++;
            foreach (var c in candidates)
            {
                long identity = targets[c.Receiver] == sim.Enemies ? sim.Enemies[c.Id].Generation : Key(c.Receiver + 1, c.Id) | long.MinValue;
                if (effect.ImpactTarget == identity) continue; // Do not spend a child's Pierce allowance inside its seed body.
                double x = blow.Shape.Kind == ShapeKind.Path ? blow.Shape.X2 : blow.Shape.X;
                double y = blow.Shape.Kind == ShapeKind.Path ? blow.Shape.Y2 : blow.Shape.Y;
                if (targets[c.Receiver] == sim.Enemies) { x = sim.Enemies[c.Id].X; y = sim.Enemies[c.Id].Y; }
                if (!sim.Weapons.Supports.Impact(effect, identity, blow.T, x, y)) continue;
                targets[c.Receiver].Strike(sim, c.Id, blow);
                Struck++;
            }
        }

        // A bolt selects exactly one enemy. Its visible link is not an area collider.
        internal bool StrikeWeaponEnemy(Simulation sim, int id, in Blow blow, WeaponEffect effect)
        {
            if (!sim.Enemies[id].Alive || !sim.Weapons.Supports.Impact(effect, sim.Enemies[id].Generation, blow.T, sim.Enemies[id].X, sim.Enemies[id].Y)) return false;
            Blows++; Struck++;
            ((IStrikeTargets)sim.Enemies).Strike(sim, id, blow);
            return true;
        }

        static long Key(int receiver, int id) => (long)receiver << 32 | (uint)id;

        /// <summary>Adds <paramref name="key"/> to <paramref name="struck"/>; false when it was there.</summary>
        static bool Claim(StructList<long> struck, long key)
        {
            if (struck == null) return true;
            for (int i = 0; i < struck.Count; i++)
                if (struck[i] == key) return false;
            struck.Add(key);
            return true;
        }
    }

    /// <summary>
    /// The world's structures as the hero meets them (design.md, "Structures": they block movement and
    /// break under any hero damage). The structures' bead implements it over the world's chunks and
    /// installs it with <see cref="Simulation.UseStructures"/>; with none installed nothing blocks.
    /// </summary>
    internal interface IStructures : IStrikeTargets
    {
        /// <summary>
        /// A body of <paramref name="radius"/> moved from (<paramref name="x"/>, <paramref name="y"/>) by
        /// (<paramref name="dx"/>, <paramref name="dy"/>): where it ends, stopped by the first intact structure
        /// in its way and sliding along it, and that structure's id (-1 when nothing stopped it).
        /// </summary>
        void Move(double x, double y, double radius, double dx, double dy, out double nx, out double ny, out int blocker);

        /// <summary>Can the hero's blows still break blocker <paramref name="id"/> (<see cref="Move"/>'s)? A wall can't.</summary>
        bool Breakable(int id);
    }
}
