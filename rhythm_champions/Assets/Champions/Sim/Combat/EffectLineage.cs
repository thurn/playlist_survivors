using System;
using System.Collections.Generic;

namespace Champions.Sim
{
    public enum EffectTrigger { Split, Echo, Kill }

    /// <summary>
    /// Per-activation proc tree: split, then echo, then kill when simultaneous. Each trigger may occur
    /// once along a branch, with at most 2 split children, 1 echo and 1 kill effect per node.
    /// The shared 32-node budget bounds breadth across siblings as well as depth (at most three).
    /// Ordinary strikes/projectiles are roots, not procs; stat growth is not clamped by this budget.
    /// </summary>
    public sealed class EffectLineage
    {
        sealed class Budget { public int Left = 31; }
        readonly Budget budget;
        readonly int used;
        int splitChildren, echoChildren, killChildren;
        public int Generation { get; }
        public int Remaining => budget.Left;
        public EffectLineage() : this(new Budget(), 0, 0) { }
        EffectLineage(Budget budget, int used, int generation)
        { this.budget = budget; this.used = used; Generation = generation; }
        public bool TryChild(EffectTrigger trigger, out EffectLineage child)
        {
            if (!Enum.IsDefined(typeof(EffectTrigger), trigger)) throw new ArgumentOutOfRangeException(nameof(trigger));
            child = null; int bit = 1 << (int)trigger;
            if (Generation >= 3 || budget.Left == 0 || (used & bit) != 0) return false;
            switch (trigger)
            {
                case EffectTrigger.Split: if (splitChildren >= 2) return false; splitChildren++; break;
                case EffectTrigger.Echo: if (echoChildren >= 1) return false; echoChildren++; break;
                case EffectTrigger.Kill: if (killChildren >= 1) return false; killChildren++; break;
            }
            budget.Left--;
            child = new EffectLineage(budget, used | bit, Generation + 1);
            return true;
        }
    }

    /// <summary>
    /// Reusable ledger for one activation/persistent effect. Target IDs must include spawn generation.
    /// For Activation, overlapping volley members share a window. Strike/Pulse use monotonically
    /// increasing authored indices, allowing one hit per target in each independent strike/pulse.
    /// Call BeginWindow before querying targets; restarting a window never permits another hit.
    /// </summary>
    public sealed class TargetHitLedger
    {
        readonly HitWindow policy;
        readonly HashSet<long> hit;
        long window = -1;
        public TargetHitLedger(HitWindow policy, int capacity = 2048)
        {
            if (!Enum.IsDefined(typeof(HitWindow), policy)) throw new ArgumentOutOfRangeException(nameof(policy));
            this.policy = policy; hit = new HashSet<long>(capacity);
        }
        public void BeginWindow(long index)
        {
            if (index < 0) throw new ArgumentOutOfRangeException(nameof(index));
            long next = policy == HitWindow.Activation ? 0 : index;
            if (next < window) throw new InvalidOperationException("hit windows cannot go backward");
            if (next != window) { hit.Clear(); window = next; }
        }
        internal bool Contains(long target) => hit.Contains(target);
        public bool TryHit(long target)
        {
            if (window < 0) throw new InvalidOperationException("begin a hit window first");
            return hit.Add(target);
        }
    }
}
