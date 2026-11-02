using System;

namespace Champions.Sim
{
    /// <summary>
    /// The attacks' beat grid (design.md, "Rules every attack keeps"): every attack fires on beats, half
    /// beats or downbeats of the committed beats, and with no reliable beat on a fixed pulse.
    ///
    /// - It steps every half beat. A step's <see cref="Step.Pos"/> counts half beats into the bar: 0 is the
    ///   downbeat, even steps are beats. An attack firing every n half beats (1, 2, 4 or 8) fires on the
    ///   steps whose position n divides, so its strikes keep their place in the bar.
    /// - <b>Reliable beat</b>: a step on each committed beat as the sim fires it, its place in the bar from
    ///   the beat services' bars (counted from the first reliable beat until a lock gives bars), and one on
    ///   the half beat between it and the next.
    /// - <b>Beatless</b>: a step every half of <see cref="AttackRules.BeatlessBeatS"/>, its positions
    ///   carrying on from the last step, in bars of the same length.
    ///
    /// The owner feeds it in sim order (<see cref="Beat"/>, <see cref="Reliable"/>) and takes the steps due
    /// (<see cref="Flush"/>, <see cref="Next"/>, <see cref="Take"/>); it reads only what the sim knew then.
    /// </summary>
    internal sealed class AttackGrid
    {
        public struct Step
        {
            public double T;
            public int Pos;
        }

        readonly BeatServices beats;
        readonly int beatsPerBar;
        readonly double beatlessS;
        readonly StructList<Step> queue = new StructList<Step>(16);
        int head;
        bool reliable;
        // The latest committed beat the grid took, its tempo and its place in the bar.
        double beatT = double.NegativeInfinity, period;
        int beatIndex = -1, beatInBar = -1;
        // The steps it makes itself: the half beat ahead, the beatless pulse's next.
        double nextHalf = double.PositiveInfinity, nextPulse = double.PositiveInfinity;
        int nextHalfPos, nextPulsePos;
        // The latest step queued.
        double queuedT = double.NegativeInfinity;
        int queuedPos = -1;

        public AttackGrid(BeatServices beats, double beatlessS)
        {
            this.beats = beats;
            beatsPerBar = beats.Rules.BeatsPerBar;
            this.beatlessS = beatlessS;
        }

        /// <summary>Half beats in a bar.</summary>
        public int HalfBar => 2 * beatsPerBar;

        /// <summary>A beat's length now: the tempo on a reliable beat (once one has stepped the grid), the pulse's beat beatless.</summary>
        public double Period => reliable && period > 0 ? period : beatlessS;

        /// <summary>The latest beat step taken (a downbeat's too), and the latest downbeat's.</summary>
        public double LastBeatT { get; private set; } = double.NegativeInfinity;
        public double LastDownbeatT { get; private set; } = double.NegativeInfinity;

        /// <summary>How far through its beat song time <paramref name="t"/> is, 0 to 1.</summary>
        public double Phase(double t)
        {
            if (double.IsNegativeInfinity(LastBeatT)) return 0;
            double u = (t - LastBeatT) / Period;
            return u - Math.Floor(u);
        }

        /// <summary>Is song time <paramref name="t"/> the latest downbeat's?</summary>
        public bool OnDownbeat(double t) => Math.Abs(t - LastDownbeatT) <= 1e-3;

        /// <summary>Starts beatless at song time <paramref name="t"/>, a downbeat four beats on.</summary>
        public void Begin(double t)
        {
            nextPulse = t + 0.5 * beatlessS;
            nextPulsePos = 1;
        }

        /// <summary>Committed beat <paramref name="index"/> fired at <paramref name="t"/> on a reliable beat, a downbeat or not.</summary>
        public void Beat(double t, int index, bool downbeat, int bar)
        {
            Flush(t - 1e-9);
            nextHalf = double.PositiveInfinity;
            if (bar >= 0) beatInBar = ((index - beats.FirstAfterLock(beats.Revealed[index].T)) % beatsPerBar + beatsPerBar) % beatsPerBar;
            else beatInBar = beatInBar < 0 ? 0 : (beatInBar + 1) % beatsPerBar;
            Queue(t, 2 * beatInBar);
            beatT = t;
            beatIndex = index;
            period = beats.TempoAt(index);
            nextHalf = beats.StartsInterval(index) ? beats.BeatTime(index + 0.5) : t + 0.5 * period;
            nextHalfPos = 2 * beatInBar + 1;
        }

        /// <summary>The beat became reliable at <paramref name="t"/> (its beats step the grid from now), or stopped being so.</summary>
        public void Reliable(double t, bool now)
        {
            Flush(t);
            if (now == reliable) return;
            reliable = now;
            if (now)
            {
                nextPulse = double.PositiveInfinity;
                return;
            }
            nextHalf = double.PositiveInfinity;
            beatInBar = -1;
            nextPulse = Math.Max(t, queuedT + 0.5 * beatlessS);
            nextPulsePos = (queuedPos + 1) % HalfBar;
        }

        /// <summary>Queues the steps the grid makes itself up to song time <paramref name="until"/>.</summary>
        public void Flush(double until)
        {
            while (true)
            {
                double t = Math.Min(nextHalf, nextPulse);
                if (t > until) return;
                if (nextHalf <= nextPulse)
                {
                    Queue(nextHalf, nextHalfPos);
                    nextHalf = double.PositiveInfinity;
                }
                else
                {
                    Queue(nextPulse, nextPulsePos);
                    nextPulsePos = (nextPulsePos + 1) % HalfBar;
                    nextPulse += 0.5 * beatlessS;
                }
            }
        }

        /// <summary>The next queued step's time, or infinity.</summary>
        public double Next => head < queue.Count ? queue[head].T : double.PositiveInfinity;

        /// <summary>Takes the next queued step.</summary>
        public Step Take()
        {
            var s = queue[head++];
            if (head == queue.Count)
            {
                queue.Clear();
                head = 0;
            }
            if (s.Pos % 2 == 0) LastBeatT = s.T;
            if (s.Pos == 0) LastDownbeatT = s.T;
            return s;
        }

        /// <summary>
        /// The song time <paramref name="n"/> beats after <paramref name="t"/>: on the committed beats where
        /// they are known (so a beat after a beat is the next beat), else at the tempo now.
        /// </summary>
        public double After(double t, double n)
        {
            if (reliable && beatIndex >= 0)
            {
                double at = beats.BeatTime(beatIndex + (t - beatT) / Period + n);
                if (!double.IsNaN(at) && at > t) return at;
            }
            return t + n * Period;
        }

        /// <summary>The first beat strictly after song time <paramref name="t"/>.</summary>
        public double NextBeatAfter(double t)
        {
            if (reliable)
            {
                double next = beats.NextBeat(t + 1e-6, out _);
                if (!double.IsNaN(next)) return next;
                return beatIndex >= 0 ? beatT + (Math.Floor((t - beatT) / Period + 1e-9) + 1) * Period : t + Period;
            }
            double v = nextPulse;
            int pos = nextPulsePos;
            while (v <= t + 1e-9 || pos % 2 != 0)
            {
                v += 0.5 * beatlessS;
                pos = (pos + 1) % HalfBar;
            }
            return v;
        }

        void Queue(double t, int pos)
        {
            queue.Add(new Step { T = t, Pos = pos });
            queuedT = t;
            queuedPos = pos;
        }
    }
}
