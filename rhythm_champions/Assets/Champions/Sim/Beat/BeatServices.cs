using System;

namespace Champions.Sim
{
    /// <summary>The beat's three states (plan.md, "Beat services"); only <see cref="Reliable"/> is a reliable beat.</summary>
    public enum Reliability
    {
        /// <summary>No committed beat ahead: beatless.</summary>
        NoBeatAhead = 0,
        /// <summary>Beats known ahead, but below the gate or the music unclear: beatless.</summary>
        BelowGate = 1,
        /// <summary>A reliable beat.</summary>
        Reliable = 2,
    }

    /// <summary>
    /// Every rhythm question in one place (plan.md, "Beat services"): the reliable-beat state, the
    /// next beats, half beats and downbeats, bars and strong onsets, all from the entries revealed
    /// now. The judgement, the cadence, quiet moments and telegraphs build on it
    /// (<see cref="Judgement"/>, <see cref="Cadence"/>, <see cref="QuietMoments"/>, <see cref="Telegraphs"/>).
    ///
    /// Ported from game/'s BeatClock:
    /// - A committed beat's interval to the next is a <b>beat interval</b> when the next is revealed
    ///   and no more than <see cref="TrackingGap"/> times its period away; a longer one is a tracking
    ///   gap, not a slow beat.
    /// - <b>Clear</b> at time s: the latest revealed tick at or before s is at most
    ///   <see cref="ClearTickAge"/> old and clear (<see cref="BeatLocks"/>).
    /// - The latest beat revealed <b>continues</b> while its interval is not yet known, for
    ///   <see cref="TrackingGap"/> times its period: committed beats come from hindsight in batches,
    ///   and a beat whose successor is merely late is not a gap.
    /// - The state at s: <see cref="Reliability.NoBeatAhead"/> unless s lies in a beat interval or a
    ///   continuing beat; else <see cref="Reliability.Reliable"/> when clear and the interval's closing
    ///   beat (the continuing beat itself) passes the gate; else <see cref="Reliability.BelowGate"/>.
    /// - <b>Bars</b> are <see cref="BeatRules.BeatsPerBar"/> beats long, counted from the first
    ///   committed beat at or after the latest lock; a <b>downbeat</b> is a bar's first beat. The
    ///   analyzer marks no metre, so this is the game's count, as game/'s two-beat bars were.
    ///
    /// Every public query is a pure function of the entries revealed now: the client may ask at any
    /// song time without changing the run. Asked about a past time, it answers from what is known now,
    /// which may differ from what the sim decided then (the events and <see cref="SimState"/> hold that).
    /// </summary>
    public sealed class BeatServices
    {
        public const double TrackingGap = 1.5;
        public const double ClearTickAge = 0.25;
        /// <summary>How far out of content order a revealed onset may be, for the searches over them.</summary>
        const double OnsetSlack = 1.0;

        readonly BeliefLog log;
        int fireCursor;

        public BeatServices(BeliefLog log, BeatRules rules, int tickCapacity)
        {
            this.log = log;
            Rules = rules;
            Locks = new BeatLocks(rules.Gate, tickCapacity);
        }

        public BeatRules Rules { get; }
        public BeatLocks Locks { get; }

        /// <summary>Committed beats revealed only after their own time: never fired.</summary>
        public int Missed { get; private set; }

        /// <summary>The latest beat fired, or -1.</summary>
        public int LastFired { get; private set; } = -1;

        /// <summary>The committed beats revealed so far.</summary>
        public ReadOnlySpan<StreamEntry> Revealed => log.Beats;

        internal void TakeTicks() => Locks.Extend(log.Ticks);

        // ---- Intervals, periods and tempo ----

        /// <summary>Does a beat interval start at beat <paramref name="i"/>?</summary>
        public bool StartsInterval(int i)
        {
            var beats = log.Beats;
            if (i < 0 || i + 1 >= beats.Length) return false;
            double gap = beats[i + 1].T - beats[i].T;
            return gap > 0 && gap <= TrackingGap * beats[i + 1].V0 + 1e-9;
        }

        /// <summary>Is beat <paramref name="i"/> the latest revealed, still continuing at song time <paramref name="s"/>?</summary>
        public bool Continues(int i, double s)
        {
            var beats = log.Beats;
            return i >= 0 && i == beats.Length - 1 && s <= beats[i].T + TrackingGap * beats[i].V0 + 1e-9;
        }

        /// <summary>The beat's period: the interval from the previous beat when it is a plausible one, else the analyzer's.</summary>
        public double PeriodOf(int i)
        {
            var beats = log.Beats;
            double period = beats[i].V0;
            if (i == 0) return period;
            double observed = beats[i].T - beats[i - 1].T;
            return observed >= 0.5 * period && observed <= 1.5 * period ? observed : period;
        }

        /// <summary>
        /// The tempo at beat <paramref name="i"/> as a period (s): the mean of the beat intervals ending
        /// at it, up to a bar's worth, so a beat's jitter never moves the cadence; the analyzer's period
        /// with none.
        /// </summary>
        public double TempoAt(int i)
        {
            var beats = log.Beats;
            double sum = 0;
            int n = 0;
            for (int k = i; k >= 1 && n < Rules.BeatsPerBar && StartsInterval(k - 1); k--, n++)
                sum += beats[k].T - beats[k - 1].T;
            return n > 0 ? sum / n : beats[i].V0;
        }

        // ---- State ----

        /// <summary>Is the music clear at song time <paramref name="s"/>?</summary>
        public bool ClearAt(double s)
        {
            var ticks = log.Ticks;
            int i = LastAtOrBefore(ticks, s);
            if (i < 0) return false;
            return s - ticks[i].T <= ClearTickAge + 1e-9 && Locks.ClearAt(i);
        }

        /// <summary>The beat's state at song time <paramref name="s"/>.</summary>
        public Reliability StateAt(double s)
        {
            var beats = log.Beats;
            int i = LastAtOrBefore(beats, s);
            int closing;
            if (StartsInterval(i)) closing = i + 1;
            else if (Continues(i, s)) closing = i;
            else return Reliability.NoBeatAhead;
            return ClearAt(s) && beats[closing].V1 >= Rules.Gate ? Reliability.Reliable : Reliability.BelowGate;
        }

        /// <summary>Is there a reliable beat at song time <paramref name="s"/>? Without one the game plays beatless.</summary>
        public bool Reliable(double s) => StateAt(s) == Reliability.Reliable;

        // ---- Bars ----

        /// <summary>
        /// Beat <paramref name="i"/>'s bar (-1 before any lock) and whether it is a downbeat, from the
        /// locks known now. <paramref name="i"/> may be a beat not yet revealed (a prediction's).
        /// </summary>
        public int BarOf(int i, out bool downbeat)
        {
            downbeat = false;
            var beats = log.Beats;
            double t = i < beats.Length ? beats[i].T : double.PositiveInfinity;
            int first = FirstAfterLock(t);
            if (first == int.MaxValue || i < first) return -1;
            int since = i - first;
            downbeat = since % Rules.BeatsPerBar == 0;
            return since / Rules.BeatsPerBar;
        }

        public bool IsDownbeat(int i)
        {
            BarOf(i, out bool downbeat);
            return downbeat;
        }

        /// <summary>Are there bars at all: has the beat ever locked?</summary>
        public bool HasBars => Locks.Locks.Length > 0;

        /// <summary>
        /// The first committed beat at or after the latest lock at or before song time <paramref name="s"/>,
        /// which bars count from (it may not be revealed yet: the beats' count then); int.MaxValue before
        /// any lock.
        /// </summary>
        internal int FirstAfterLock(double s)
        {
            var beats = log.Beats;
            double lockT = Locks.LockAtOrBefore(s);
            if (double.IsNaN(lockT)) return int.MaxValue;
            int lo = 0, hi = beats.Length;
            while (lo < hi)
            {
                int mid = (lo + hi) >> 1;
                if (beats[mid].T < lockT - 1e-9) lo = mid + 1;
                else hi = mid;
            }
            return lo;
        }

        // ---- Beat times ----

        /// <summary>
        /// The song time of fractional beat <paramref name="beat"/>: a revealed beat's own time, a point
        /// inside a beat interval by its fraction, or from the latest beat revealed on, a point of its
        /// period while it continues (<see cref="Continues"/>); NaN otherwise.
        /// </summary>
        public double BeatTime(double beat)
        {
            var beats = log.Beats;
            int last = beats.Length - 1;
            int i = (int)Math.Floor(beat + 1e-9);
            if (i < 0 || last < 0) return double.NaN;
            if (i >= last)
            {
                double t = beats[last].T + (beat - last) * PeriodOf(last);
                return t <= beats[last].T + TrackingGap * beats[last].V0 + 1e-9 ? t : double.NaN;
            }
            double f = beat - i;
            if (f < 1e-9) return beats[i].T;
            return StartsInterval(i) ? beats[i].T + (beats[i + 1].T - beats[i].T) * f : double.NaN;
        }

        /// <summary>
        /// Beat <paramref name="index"/>'s time, predicted past the beats revealed at the latest beat's
        /// tempo (<see cref="TempoAt"/>), however far; NaN with no beat revealed or before the first. For
        /// planning ahead (a telegraph's landing): ask again once the beat is revealed.
        /// </summary>
        public double PredictBeat(int index)
        {
            var beats = log.Beats;
            int last = beats.Length - 1;
            if (index < 0 || last < 0) return double.NaN;
            if (index <= last) return beats[index].T;
            return beats[last].T + (index - last) * TempoAt(last);
        }

        /// <summary>
        /// The first beat at or after <paramref name="s"/> (<see cref="BeatTime"/>: revealed, or the
        /// latest one's continuation) and its index; NaN and -1 when none is known.
        /// </summary>
        public double NextBeat(double s, out int index)
        {
            int i = Math.Max(0, LastAtOrBefore(log.Beats, s));
            for (int k = i; k <= i + 2; k++)
            {
                double t = BeatTime(k);
                if (double.IsNaN(t)) break;
                if (t >= s - 1e-9)
                {
                    index = k;
                    return t;
                }
            }
            index = -1;
            return double.NaN;
        }

        /// <summary>The first half beat (the midpoint of a beat interval) at or after <paramref name="s"/>, or NaN.</summary>
        public double NextHalfBeat(double s)
        {
            int i = Math.Max(0, LastAtOrBefore(log.Beats, s));
            for (int k = i; k <= i + 2; k++)
            {
                double t = BeatTime(k + 0.5);
                if (double.IsNaN(t)) continue;
                if (t >= s - 1e-9) return t;
            }
            return double.NaN;
        }

        /// <summary>
        /// The first downbeat at or after <paramref name="s"/> and its index, among the beats revealed and,
        /// when <paramref name="predict"/>, predicted past them (<see cref="PredictBeat"/>); NaN and -1
        /// before any lock or with no beat known.
        /// </summary>
        public double NextDownbeat(double s, out int index, bool predict = true)
        {
            index = -1;
            if (!HasBars) return double.NaN;
            var beats = log.Beats;
            int i = Math.Max(0, LastAtOrBefore(beats, s));
            for (int k = i; k <= i + 2 * Rules.BeatsPerBar + 1; k++)
            {
                double t = predict ? PredictBeat(k) : BeatTime(k);
                if (double.IsNaN(t)) return double.NaN;
                if (t < s - 1e-9 || !IsDownbeat(k)) continue;
                index = k;
                return t;
            }
            return double.NaN;
        }

        // ---- Onsets ----

        /// <summary>Is a revealed strong onset within <paramref name="window"/> of song time <paramref name="t"/>?</summary>
        public bool StrongOnsetNear(double t, double window) => OnsetNear(t, window, true);

        /// <summary>Does the music play at song time <paramref name="t"/>: is a revealed onset within <paramref name="window"/> of it?</summary>
        public bool PlayedNear(double t, double window) => OnsetNear(t, window, false);

        /// <summary>Is beat <paramref name="i"/> (revealed) a strong beat: a strong onset on it?</summary>
        public bool StrongBeat(int i) => i >= 0 && i < log.Beats.Length && StrongOnsetNear(log.Beats[i].T, Rules.StrongBeatWindowS);

        bool OnsetNear(double t, double window, bool strongOnly)
        {
            var onsets = log.Revealed(EntryKind.Onset);
            // Revealed in known-at order, which is content order give or take: search with slack.
            int lo = 0, hi = onsets.Length;
            double from = t - window - OnsetSlack;
            while (lo < hi)
            {
                int mid = (lo + hi) >> 1;
                if (onsets[mid].T < from) lo = mid + 1;
                else hi = mid;
            }
            for (int i = lo; i < onsets.Length && onsets[i].T <= t + window + OnsetSlack; i++)
            {
                if (Math.Abs(onsets[i].T - t) > window + 1e-9) continue;
                if (!strongOnly || onsets[i].V1 != 0) return true;
            }
            return false;
        }

        // ---- Firing ----

        /// <summary>The latest of a grid's entries at or before <paramref name="s"/>, or -1.</summary>
        internal static int LastAtOrBefore(ReadOnlySpan<StreamEntry> grid, double s)
        {
            int lo = 0, hi = grid.Length;
            while (lo < hi)
            {
                int mid = (lo + hi) >> 1;
                if (grid[mid].T <= s + 1e-9) lo = mid + 1;
                else hi = mid;
            }
            return lo - 1;
        }

        /// <summary>The next unfired beat's time at or after <paramref name="floor"/>; beats revealed too late are skipped as missed.</summary>
        internal double NextTime(double floor)
        {
            var beats = log.Beats;
            while (fireCursor < beats.Length && beats[fireCursor].T < floor)
            {
                fireCursor++;
                Missed++;
            }
            return fireCursor < beats.Length ? beats[fireCursor].T : double.PositiveInfinity;
        }

        /// <summary>Marks the next unfired beat fired and returns its index.</summary>
        internal int Fire()
        {
            LastFired = fireCursor++;
            return LastFired;
        }
    }
}
