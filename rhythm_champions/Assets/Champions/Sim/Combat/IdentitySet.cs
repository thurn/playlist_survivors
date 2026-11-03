using System;
using System.Runtime.CompilerServices;

namespace Champions.Sim
{
    // Membership only: no iteration or removals within an activation/window.
    internal sealed class IdentitySet
    {
        const int PageShift = 10, PageMask = (1 << PageShift) - 1;
        readonly int initialSize;
        long[] keys;
        ulong[] bits;
        long page = -1;
        int count;
        bool zero;
        public IdentitySet(int capacity = 0)
        {
            if (capacity < 0) throw new ArgumentOutOfRangeException(nameof(capacity));
            int size = capacity == 0 ? 0 : 8;
            while (size > 0 && size < capacity * 2L) size = checked(size * 2);
            initialSize = Math.Max(8, size);
            keys = Array.Empty<long>();
        }
        static int Hash(long key) => unchecked((int)((uint)key ^ (uint)(key >> 32) * 0x9E3779B9u));
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Contains(long key)
        {
            if (key == 0) return zero;
            if (key > 0 && (key >> PageShift) == page)
            {
                int bit = (int)(key & PageMask);
                return (bits[bit >> 6] & (1UL << (bit & 63))) != 0;
            }
            var table = keys;
            if (table.Length == 0) return false;
            int mask = table.Length - 1, slot = Hash(key) & mask;
            while (table[slot] != 0)
            {
                if (table[slot] == key) return true;
                slot = (slot + 1) & mask;
            }
            return false;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Add(long key)
        {
            if (key == 0) { if (zero) return false; zero = true; return true; }
            // Generations near the first positive identity share a compact page. Other
            // generations and negative receiver keys keep the complete hash fallback.
            if (key > 0)
            {
                long targetPage = key >> PageShift;
                if (page < 0)
                {
                    if (bits == null) bits = new ulong[(PageMask + 1) / 64];
                    page = targetPage;
                }
                if (page == targetPage)
                {
                    int bit = (int)(key & PageMask), word = bit >> 6;
                    ulong bitMask = 1UL << (bit & 63), prior = bits[word];
                    if ((prior & bitMask) != 0) return false;
                    bits[word] = prior | bitMask; return true;
                }
            }
            if (keys.Length == 0) keys = new long[initialSize];
            var table = keys;
            int mask = table.Length - 1, slot = Hash(key) & mask;
            while (table[slot] != 0)
            {
                if (table[slot] == key) return false;
                slot = (slot + 1) & mask;
            }
            if (count * 2 >= table.Length)
            {
                Grow(); table = keys; mask = table.Length - 1;
                slot = Hash(key) & mask;
                while (table[slot] != 0) slot = (slot + 1) & mask;
            }
            table[slot] = key; count++; return true;
        }
        void Grow()
        {
            var previous = keys;
            var table = new long[checked(previous.Length * 2)];
            int mask = table.Length - 1;
            foreach (long key in previous)
            {
                if (key == 0) continue;
                int slot = Hash(key) & mask;
                while (table[slot] != 0) slot = (slot + 1) & mask;
                table[slot] = key;
            }
            keys = table;
        }
        public void Clear()
        {
            if (count > 0) Array.Clear(keys, 0, keys.Length);
            count = 0; zero = false;
            if (page >= 0)
            {
                Array.Clear(bits, 0, bits.Length);
                page = -1;
            }
        }
    }
}
