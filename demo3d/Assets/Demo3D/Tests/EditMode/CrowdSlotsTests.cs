using System.Linq;
using Demo3D.Runtime.Crowd;
using NUnit.Framework;
using Unity.Collections;

namespace Demo3D.Tests
{
    /// <summary>TD5's review: stable crowd slots across spawns and despawns.</summary>
    public class CrowdSlotsTests
    {
        static int[] Frame(CrowdSlots slots, params int[] entities)
        {
            slots.Begin();
            var assigned = entities.Select(e => slots.Assign(0, e)).ToArray();
            slots.End();
            return assigned;
        }

        [Test]
        public void ALiveMonsterKeepsItsSlotWhenAnEarlierOneDespawns()
        {
            var slots = new CrowdSlots(1, 16);
            Assert.That(Frame(slots, 10, 11, 12), Is.EqualTo(new[] { 0, 1, 2 }));
            // Entity 10 despawns: 11 and 12 keep their slots (a list position would shift them).
            Assert.That(Frame(slots, 11, 12), Is.EqualTo(new[] { 1, 2 }));
            Assert.That(slots.Count(0), Is.EqualTo(3), "the count is the high-water mark");
            Assert.That(slots.Live(0, 0), Is.False, "the freed slot is written Hidden");
        }

        [Test]
        public void AFreedSlotStaysHiddenForAFrameBeforeReuse()
        {
            var slots = new CrowdSlots(1, 16);
            Frame(slots, 1, 2);
            // 1 despawns and 3 spawns in the same frame: 3 can't take slot 0 yet.
            Assert.That(Frame(slots, 2, 3), Is.EqualTo(new[] { 1, 2 }));
            Assert.That(slots.Live(0, 0), Is.False);
            // A frame later, slot 0 (Hidden for one frame) goes to the next spawn.
            Assert.That(Frame(slots, 2, 3, 4), Is.EqualTo(new[] { 1, 2, 0 }));
            Assert.That(slots.Count(0), Is.EqualTo(3));
        }

        [Test]
        public void FillHiddenWritesEveryUnownedSlot()
        {
            var slots = new CrowdSlots(2, 16);
            slots.Begin();
            slots.Assign(1, 7);
            slots.Assign(1, 8);
            slots.End();
            slots.Begin();
            slots.Assign(1, 8);
            slots.End();
            var inst = new NativeArray<MonsterInstance>(slots.Count(1), Allocator.Temp);
            for (int i = 0; i < inst.Length; i++) inst[i] = new MonsterInstance { Flags = (uint)CrowdFlags.Alive, Scale = 1 };
            slots.FillHidden(1, inst);
            Assert.That(inst[0].Flags, Is.EqualTo((uint)CrowdFlags.Hidden));
            Assert.That(inst[1].Flags, Is.EqualTo((uint)CrowdFlags.Alive), "the live slot is left to its owner");
            Assert.That(slots.Count(0), Is.EqualTo(0), "types are independent");
            inst.Dispose();
        }

        [Test]
        public void SlotsAreBoundedByTheCapacity()
        {
            var slots = new CrowdSlots(1, 2);
            slots.Begin();
            slots.Assign(0, 1);
            slots.Assign(0, 2);
            Assert.Throws<System.InvalidOperationException>(() => slots.Assign(0, 3));
        }
    }
}
