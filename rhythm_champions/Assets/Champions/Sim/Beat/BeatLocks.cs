using System;

namespace Champions.Sim
{
    /// <summary>
    /// When the music is clear, tick by tick, and when the beat locks (game/'s Judging, from native's
    /// <c>reactive/judging.rs</c>). A tick is clear once the ticks' evidence has passed the gate for
    /// <see cref="ClearAfter"/>; the first tick below the gate ends it. A run of clear ticks that starts
    /// at least <see cref="LockGap"/> after the previous run ended is a lock (the first run of all is
    /// one too), and bars count from the first committed beat at or after it.
    ///
    /// Each tick's answer reads only ticks at or before it, so extending it as ticks are revealed
    /// gives the same answers whatever the reveal timing.
    /// </summary>
    public sealed class BeatLocks
    {
        /// <summary>How long the evidence must pass the gate before ticks are clear.</summary>
        public const double ClearAfter = 1.0;
        /// <summary>A stretch without clear ticks this long or longer makes the next clear run a lock.</summary>
        public const double LockGap = 4.0;

        readonly double gate;
        readonly StructList<bool> clear;
        readonly StructList<double> locks = new StructList<double>(64);
        double clearFrom = double.NaN;
        double lastClear = double.NegativeInfinity;

        public BeatLocks(double gate, int capacity)
        {
            this.gate = gate;
            clear = new StructList<bool>(capacity);
        }

        /// <summary>How many ticks have been taken in.</summary>
        public int Count => clear.Count;

        /// <summary>The lock times, ascending.</summary>
        public ReadOnlySpan<double> Locks => locks.AsSpan();

        /// <summary>Is tick <paramref name="i"/> clear?</summary>
        public bool ClearAt(int i) => i >= 0 && clear[i];

        /// <summary>Takes in the ticks after the ones already taken (the revealed ticks, in order).</summary>
        internal void Extend(ReadOnlySpan<StreamEntry> ticks)
        {
            for (int j = clear.Count; j < ticks.Length; j++)
            {
                double t = ticks[j].T;
                // A clear run ends at the next tick's time.
                if (j > 0 && clear[j - 1]) lastClear = t;
                bool now = false;
                if (ticks[j].V0 < gate)
                {
                    clearFrom = double.NaN;
                }
                else
                {
                    if (double.IsNaN(clearFrom)) clearFrom = t;
                    if (t - clearFrom >= ClearAfter - 1e-9)
                    {
                        now = true;
                        bool run = j > 0 && clear[j - 1];
                        if (!run && t - lastClear >= LockGap - 1e-9) locks.Add(t);
                    }
                }
                clear.Add(now);
            }
        }

        /// <summary>The latest lock at or before song time <paramref name="t"/>, or NaN.</summary>
        public double LockAtOrBefore(double t)
        {
            int lo = 0, hi = locks.Count;
            while (lo < hi)
            {
                int mid = (lo + hi) >> 1;
                if (locks[mid] <= t + 1e-9) lo = mid + 1;
                else hi = mid;
            }
            return lo == 0 ? double.NaN : locks[lo - 1];
        }
    }
}
