using System;

namespace PS.Game.Sim
{
    /// <summary>
    /// When the music is clear, tick by tick, and when the beat locks (native's
    /// <c>reactive/judging.rs</c>). A tick is judged clear once the ticks' evidence has passed the
    /// gate for <see cref="JudgeAfter"/>; the first tick below the gate ends it. A run of clear
    /// ticks that starts at least <see cref="LockGap"/> after the previous run ended is a lock, and
    /// bars count from the first committed beat at or after it.
    ///
    /// One change from native: the first clear run of a song is a lock too (native's first run
    /// within 4 s of song time 0 is not, since its bars come from the song map instead).
    ///
    /// Each tick's answer reads only ticks at or before it, so extending it as ticks are revealed
    /// gives the same answers whatever the reveal timing.
    /// </summary>
    public sealed class Judging
    {
        /// <summary>How long the evidence must pass the gate before ticks are clear.</summary>
        public const double JudgeAfter = 1.0;
        /// <summary>A stretch without clear ticks this long or longer makes the next clear run a lock.</summary>
        public const double LockGap = 4.0;

        readonly double gate;
        readonly StructList<bool> judged;
        readonly StructList<double> locks = new StructList<double>(64);
        double clearFrom = double.NaN;
        double lastJudged = double.NegativeInfinity;

        public Judging(double gate, int capacity)
        {
            this.gate = gate;
            judged = new StructList<bool>(capacity);
        }

        /// <summary>How many ticks have been judged.</summary>
        public int Count => judged.Count;

        /// <summary>The lock times, ascending.</summary>
        public ReadOnlySpan<double> Locks => locks.AsSpan();

        /// <summary>Is tick <paramref name="i"/> clear?</summary>
        public bool JudgedAt(int i) => i >= 0 && judged[i];

        /// <summary>Takes in the ticks after the ones already judged (the revealed ticks, in order).</summary>
        internal void Extend(ReadOnlySpan<StreamEntry> ticks)
        {
            for (int j = judged.Count; j < ticks.Length; j++)
            {
                double t = ticks[j].T;
                // A clear run ends at the next tick's time.
                if (j > 0 && judged[j - 1]) lastJudged = t;
                bool clear = false;
                if (ticks[j].V0 < gate)
                {
                    clearFrom = double.NaN;
                }
                else
                {
                    if (double.IsNaN(clearFrom)) clearFrom = t;
                    if (t - clearFrom >= JudgeAfter - 1e-9)
                    {
                        clear = true;
                        bool run = j > 0 && judged[j - 1];
                        if (!run && t - lastJudged >= LockGap - 1e-9) locks.Add(t);
                    }
                }
                judged.Add(clear);
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
