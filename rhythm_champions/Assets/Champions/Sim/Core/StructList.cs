using System;
using System.Runtime.CompilerServices;

namespace Champions.Sim
{
    /// <summary>
    /// A dense, growable array of plain structs: the sim's storage. Reading and writing never
    /// allocate; adding past the capacity doubles it, so a run sized up front by
    /// <see cref="SimConfig"/> allocates nothing per tick.
    /// </summary>
    public sealed class StructList<T> where T : struct
    {
        T[] items;
        int count;

        public StructList(int capacity)
        {
            items = new T[Math.Max(1, capacity)];
        }

        public int Count { [MethodImpl(MethodImplOptions.AggressiveInlining)] get => count; }
        public int Capacity => items.Length;

        public ref T this[int i]
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                if ((uint)i >= (uint)count) throw new IndexOutOfRangeException();
                return ref items[i];
            }
        }

        public ref T Last
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                if (count == 0) throw new InvalidOperationException("the list is empty");
                return ref items[count - 1];
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Add(in T item)
        {
            if (count == items.Length) Array.Resize(ref items, items.Length * 2);
            items[count++] = item;
        }

        // Bulk writers reserve their maximum result count once, fill the returned array,
        // then publish the count. Keep this internal: callers must not retain it across growth.
        internal T[] ResetForWrite(int capacity)
        {
            Clear();
            if (items.Length < capacity) Array.Resize(ref items, Math.Max(capacity, items.Length * 2));
            return items;
        }

        internal void Written(int length)
        {
            if ((uint)length > (uint)items.Length) throw new ArgumentOutOfRangeException(nameof(length));
            count = length;
        }

        public void RemoveLast()
        {
            if (count == 0) throw new InvalidOperationException("the list is empty");
            items[--count] = default;
        }

        /// <summary>Drops the first <paramref name="n"/> items, moving the rest down (no allocation).</summary>
        public void RemoveFront(int n)
        {
            if (n <= 0) return;
            if (n > count) throw new ArgumentOutOfRangeException(nameof(n));
            Array.Copy(items, n, items, 0, count - n);
            Array.Clear(items, count - n, n);
            count -= n;
        }

        public void Clear()
        {
            Array.Clear(items, 0, count);
            count = 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ReadOnlySpan<T> AsSpan() => new ReadOnlySpan<T>(items, 0, count);
        public ReadOnlySpan<T> AsSpan(int start) => new ReadOnlySpan<T>(items, start, count - start);
    }
}
