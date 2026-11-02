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
    }
}
