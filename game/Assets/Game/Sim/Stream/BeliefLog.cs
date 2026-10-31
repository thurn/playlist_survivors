using System;

namespace PS.Game.Sim
{
    /// <summary>
    /// The belief stream as the sim holds it: entries that have arrived but are not yet known, and
    /// per kind, the entries revealed so far. Causality lives here (unity-game.md, "The Sim's
    /// Contract"): an entry is accepted as soon as it arrives and revealed only when the sim reaches
    /// its <see cref="StreamEntry.KnownAt"/>. Rules read only the revealed lists.
    ///
    /// Revealed entries keep their reveal order, which is content order in practice: the plugin
    /// stamps in emission order. Committed beats and ticks must be revealed in increasing
    /// <see cref="StreamEntry.T"/>, since the rules index them as grids; one that is not is dropped
    /// and counted in <see cref="Dropped"/>.
    /// </summary>
    public sealed class BeliefLog
    {
        struct Pending
        {
            public StreamEntry Entry;
            public long Seq;
        }

        const int KindCount = (int)EntryKind.Timbre + 1;

        readonly StructList<Pending> heap;
        readonly StructList<StreamEntry> arrived;
        readonly StructList<StreamEntry>[] revealed = new StructList<StreamEntry>[KindCount];
        long seq;

        public BeliefLog(SimConfig config)
        {
            heap = new StructList<Pending>(config.EntryCapacity);
            arrived = new StructList<StreamEntry>(256);
            for (int k = 1; k < KindCount; k++)
            {
                int cap = (EntryKind)k == EntryKind.Tick ? config.TickCapacity
                    : (EntryKind)k == EntryKind.Beat || (EntryKind)k == EntryKind.Onset ? config.BeatCapacity
                    : 256;
                revealed[k] = new StructList<StreamEntry>(cap);
            }
        }

        /// <summary>Entries dropped because they were revealed out of content order (beats and ticks).</summary>
        public int Dropped { get; private set; }

        /// <summary>Entries accepted but not yet known.</summary>
        public int PendingCount => heap.Count + arrived.Count;

        /// <summary>The earliest known-at time among the pending entries (infinity when none).</summary>
        public double NextKnownAt => heap.Count > 0 ? heap[0].Entry.KnownAt : double.PositiveInfinity;

        public ReadOnlySpan<StreamEntry> Revealed(EntryKind kind) => revealed[Index(kind)].AsSpan();
        public int Count(EntryKind kind) => revealed[Index(kind)].Count;
        public ref readonly StreamEntry Get(EntryKind kind, int i) => ref revealed[Index(kind)][i];

        public ReadOnlySpan<StreamEntry> Beats => revealed[(int)EntryKind.Beat].AsSpan();
        public ReadOnlySpan<StreamEntry> Ticks => revealed[(int)EntryKind.Tick].AsSpan();

        static int Index(EntryKind kind)
        {
            int k = (int)kind;
            if (k < 1 || k >= KindCount) throw new ArgumentOutOfRangeException(nameof(kind), kind, "not a stream kind");
            return k;
        }

        static void Validate(in StreamEntry e)
        {
            Index(e.Kind);
            if (double.IsNaN(e.T) || double.IsInfinity(e.T)) throw new ArgumentException("an entry's t must be finite");
        }

        /// <summary>An arriving entry, known from <c>max(Stamp + B, frontier)</c> once its frame settles.</summary>
        internal void Arrive(in StreamEntry e)
        {
            Validate(e);
            if (double.IsNaN(e.Stamp) || double.IsInfinity(e.Stamp)) throw new ArgumentException("an entry's stamp must be finite");
            arrived.Add(e);
        }

        /// <summary>A replayed entry with its recorded known-at time, which must not be in the past.</summary>
        internal void AddKnown(in StreamEntry e, double now)
        {
            Validate(e);
            if (double.IsNaN(e.KnownAt)) throw new ArgumentException("a replayed entry needs its known_at");
            if (e.KnownAt < now - 1e-9)
                throw new ArgumentException($"entry known at {e.KnownAt} added after the sim reached {now}");
            Push(e);
        }

        /// <summary>
        /// Settles the frame's arrivals: each is known from <c>max(Stamp + budget, frontierEnd)</c>,
        /// <paramref name="frontierEnd"/> being the frontier at the end of the frame they arrived in.
        /// </summary>
        internal void SettleArrivals(double budget, double frontierEnd)
        {
            for (int i = 0; i < arrived.Count; i++)
            {
                var e = arrived[i];
                e.KnownAt = Math.Max(e.Stamp + budget, frontierEnd);
                Push(e);
            }
            arrived.Clear();
        }

        /// <summary>Reveals every pending entry known at or before <paramref name="upTo"/>; returns how many.</summary>
        internal int Reveal(double upTo)
        {
            int n = 0;
            while (heap.Count > 0 && heap[0].Entry.KnownAt <= upTo)
            {
                var e = heap[0].Entry;
                PopTop();
                var list = revealed[(int)e.Kind];
                bool grid = e.Kind == EntryKind.Beat || e.Kind == EntryKind.Tick;
                if (grid && list.Count > 0 && e.T <= list.Last.T + 1e-9)
                {
                    Dropped++;
                    continue;
                }
                list.Add(e);
                n++;
            }
            return n;
        }

        // A binary min-heap on (KnownAt, arrival order).

        static bool Before(in Pending a, in Pending b) =>
            a.Entry.KnownAt < b.Entry.KnownAt || (a.Entry.KnownAt == b.Entry.KnownAt && a.Seq < b.Seq);

        void Push(in StreamEntry e)
        {
            heap.Add(new Pending { Entry = e, Seq = seq++ });
            int i = heap.Count - 1;
            while (i > 0)
            {
                int parent = (i - 1) >> 1;
                if (!Before(heap[i], heap[parent])) break;
                (heap[i], heap[parent]) = (heap[parent], heap[i]);
                i = parent;
            }
        }

        void PopTop()
        {
            int last = heap.Count - 1;
            heap[0] = heap[last];
            heap.RemoveLast();
            int i = 0;
            int n = heap.Count;
            while (true)
            {
                int l = 2 * i + 1, r = l + 1, m = i;
                if (l < n && Before(heap[l], heap[m])) m = l;
                if (r < n && Before(heap[r], heap[m])) m = r;
                if (m == i) break;
                (heap[i], heap[m]) = (heap[m], heap[i]);
                i = m;
            }
        }
    }
}
