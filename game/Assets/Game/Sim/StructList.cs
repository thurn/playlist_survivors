using System;

namespace PS.Game.Sim
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

        public int Count => count;
        public int Capacity => items.Length;

        public ref T this[int i]
        {
            get
            {
                if ((uint)i >= (uint)count) throw new IndexOutOfRangeException();
                return ref items[i];
            }
        }

        public ref T Last
        {
            get
            {
                if (count == 0) throw new InvalidOperationException("the list is empty");
                return ref items[count - 1];
            }
        }

        public void Add(in T item)
        {
            if (count == items.Length) Array.Resize(ref items, items.Length * 2);
            items[count++] = item;
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

        public ReadOnlySpan<T> AsSpan() => new ReadOnlySpan<T>(items, 0, count);
        public ReadOnlySpan<T> AsSpan(int start) => new ReadOnlySpan<T>(items, start, count - start);
    }
}
