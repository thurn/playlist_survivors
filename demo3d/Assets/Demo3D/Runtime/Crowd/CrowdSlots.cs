using System;
using System.Collections.Generic;
using Unity.Collections;

namespace Demo3D.Runtime.Crowd
{
    /// <summary>
    /// Stable crowd slots (crowd.md, Instance data): per type, each live entity keeps its slot for
    /// its whole life, so the previous frame's copy of a slot is the same monster (motion vectors,
    /// the previous palette, LOD state). A slot freed by a despawn is written Hidden for at least
    /// one frame before another entity takes it, which tells the crowd the slot changed owner.
    ///
    /// Each frame: <see cref="Begin"/>; <see cref="Assign"/> every live entity; <see cref="End"/>;
    /// then call the renderer's Instances(type, <see cref="Count"/>), <see cref="FillHidden"/> it
    /// and write each live entity at its slot.
    /// </summary>
    public sealed class CrowdSlots
    {
        public static readonly MonsterInstance Hidden = new MonsterInstance { Flags = (uint)CrowdFlags.Hidden };

        sealed class TypeSlots
        {
            public readonly Dictionary<int, int> SlotOf = new Dictionary<int, int>();
            public int[] Owner = Array.Empty<int>();    // entity, or -1
            public int[] Seen = Array.Empty<int>();     // the frame the owner was last assigned
            public readonly Queue<(int slot, int freedAt)> Free = new Queue<(int, int)>();
            public int HighWater;
        }

        readonly TypeSlots[] _types;
        readonly int _capacity;
        int _frame;

        public CrowdSlots(int types, int capacityPerType)
        {
            _capacity = capacityPerType;
            _types = new TypeSlots[types];
            for (int i = 0; i < types; i++) _types[i] = new TypeSlots();
        }

        public void Begin() => _frame++;

        /// <summary>The entity's slot this frame: its own, a slot freed in an earlier frame, or a new one.</summary>
        public int Assign(int type, int entity)
        {
            var t = _types[type];
            if (!t.SlotOf.TryGetValue(entity, out int slot))
            {
                if (t.Free.Count > 0 && t.Free.Peek().freedAt < _frame) slot = t.Free.Dequeue().slot;
                else
                {
                    if (t.HighWater >= _capacity) throw new InvalidOperationException($"CrowdSlots: type {type} is over its {_capacity} slots");
                    slot = t.HighWater++;
                    if (slot >= t.Owner.Length)
                    {
                        int n = Math.Min(_capacity, Math.Max(64, t.Owner.Length * 2));
                        Array.Resize(ref t.Owner, n);
                        Array.Resize(ref t.Seen, n);
                    }
                }
                t.SlotOf[entity] = slot;
                t.Owner[slot] = entity;
            }
            t.Seen[slot] = _frame;
            return slot;
        }

        /// <summary>Frees the slots of entities not assigned this frame (they are written Hidden now).</summary>
        public void End()
        {
            foreach (var t in _types)
                for (int s = 0; s < t.HighWater; s++)
                {
                    int e = t.Owner[s];
                    if (e < 0 || t.Seen[s] == _frame) continue;
                    t.SlotOf.Remove(e);
                    t.Owner[s] = -1;
                    t.Free.Enqueue((s, _frame));
                }
        }

        /// <summary>The instance count to request: the type's slot high-water mark.</summary>
        public int Count(int type) => _types[type].HighWater;

        public bool Live(int type, int slot)
        {
            var t = _types[type];
            return slot < t.HighWater && t.Owner[slot] >= 0 && t.Seen[slot] == _frame;
        }

        /// <summary>Writes Hidden into every slot without a live owner (the array is mapped memory: write all).</summary>
        public void FillHidden(int type, NativeArray<MonsterInstance> instances)
        {
            for (int s = 0; s < instances.Length; s++)
                if (!Live(type, s)) instances[s] = Hidden;
        }
    }
}
