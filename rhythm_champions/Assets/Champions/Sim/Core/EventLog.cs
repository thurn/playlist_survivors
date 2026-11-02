using System;

namespace Champions.Sim
{
    /// <summary>
    /// The settled event log and its running hash. Every event is final once emitted (the sim never
    /// revises what it has simulated), so the hash folds each event in as it is appended; replays
    /// compare it. Cursors are absolute: <see cref="DiscardBefore"/> frees consumed events without
    /// changing them or the hash.
    /// </summary>
    public sealed class EventLog
    {
        readonly StructList<SimEvent> events;
        int discarded;

        public EventLog(int capacity)
        {
            events = new StructList<SimEvent>(capacity);
            Hash = Hashing.Offset;
        }

        /// <summary>The hash of every event emitted so far, in order.</summary>
        public ulong Hash { get; private set; }

        /// <summary>The cursor after the latest event: how many have ever been emitted.</summary>
        public int Count => discarded + events.Count;

        /// <summary>The events from <paramref name="cursor"/> on (at least the oldest kept).</summary>
        public ReadOnlySpan<SimEvent> Since(int cursor)
        {
            if (cursor < discarded) throw new ArgumentOutOfRangeException(nameof(cursor), "those events were discarded");
            if (cursor >= Count) return ReadOnlySpan<SimEvent>.Empty;
            return events.AsSpan(cursor - discarded);
        }

        /// <summary>Frees the events before <paramref name="cursor"/>; their hash stays.</summary>
        public void DiscardBefore(int cursor)
        {
            int n = Math.Min(cursor, Count) - discarded;
            if (n <= 0) return;
            events.RemoveFront(n);
            discarded += n;
        }

        internal void Add(in SimEvent e)
        {
            events.Add(e);
            ulong h = Hash;
            h = Hashing.Fold(h, (ulong)e.Kind);
            h = Hashing.Fold(h, Hashing.Bits(e.T));
            h = Hashing.Fold(h, (ulong)(uint)e.I0);
            h = Hashing.Fold(h, (ulong)(uint)e.I1);
            h = Hashing.Fold(h, (ulong)(uint)e.I2);
            h = Hashing.Fold(h, Hashing.Bits(e.D0));
            h = Hashing.Fold(h, Hashing.Bits(e.D1));
            h = Hashing.Fold(h, Hashing.Bits(e.D2));
            h = Hashing.Fold(h, Hashing.Bits(e.D3));
            h = Hashing.Fold(h, Hashing.Bits(e.D4));
            h = Hashing.Fold(h, Hashing.Bits(e.D5));
            Hash = h;
        }
    }

    /// <summary>The sim's hashing: a fold of 64-bit words, and doubles as their bits.</summary>
    internal static class Hashing
    {
        public const ulong Offset = 0xCBF29CE484222325UL;

        /// <summary>A double's bits; NaNs hash alike whatever their payload, and -0 as 0.</summary>
        public static ulong Bits(double d) =>
            double.IsNaN(d) ? 0x7FF8000000000000UL : d == 0 ? 0UL : (ulong)BitConverter.DoubleToInt64Bits(d);

        public static ulong Fold(ulong h, ulong v)
        {
            v *= 0x9E3779B97F4A7C15UL;
            v ^= v >> 29;
            h ^= v;
            h *= 0x100000001B3UL;
            return (h << 23) | (h >> 41);
        }
    }
}
