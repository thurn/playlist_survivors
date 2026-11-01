using System;

namespace PS.Game.Sim
{
    /// <summary>
    /// The committed beats as the sim knows them: which beat comes next, bars and downbeats, and the
    /// beat's state (unity-game.md, "Beats, Bars and Reliability").
    ///
    /// - A committed beat's interval to the next is a <b>beat interval</b> when the next is revealed
    ///   and no more than <see cref="TrackingGap"/> times its period away; a longer one is a tracking
    ///   gap, not a slow beat.
    /// - <b>Clear</b> at time s: the latest revealed tick at or before s is at most
    ///   <see cref="ClearTickAge"/> old and judged clear (<see cref="Judging"/>).
    /// - The latest beat revealed <b>continues</b> while its interval is not yet known, for
    ///   <see cref="TrackingGap"/> times its period: committed beats come from hindsight in batches,
    ///   often less than a beat ahead, and a beat whose successor is merely late is not a gap.
    /// - The state at s: <see cref="Reliability.NoBeatAhead"/> unless s lies in a beat interval or
    ///   a continuing beat; else <see cref="Reliability.Reliable"/> when clear and the interval's
    ///   closing beat (the continuing beat itself) passes the gate (native's <c>enemy_reliable</c>
    ///   and main window); else <see cref="Reliability.BelowGate"/>.
    /// - Bars are two beats long, counted from the first committed beat at or after the latest
    ///   lock; a downbeat is a bar's first beat.
    ///
    /// Everything reads only revealed entries, and the answers the sim acts on are taken at fixed
    /// song times (a beat's own time, a tick's end), so they are a function of the stream's
    /// known-at times. Every public query is a pure function of the entries revealed now: the
    /// client may ask at any song time without changing the run. Asked about a past time, it
    /// answers from what is known now, which may differ from what the sim decided then (the
    /// <see cref="SimEventKind.BeatState"/> events and <see cref="SimState"/> hold that).
    /// </summary>
    public sealed class BeatClock
    {
        public const double TrackingGap = 1.5;
        public const double ClearTickAge = 0.25;

        readonly BeliefLog log;
        readonly double gate;
        int fireCursor;

        public BeatClock(BeliefLog log, SimConfig config)
        {
            this.log = log;
            gate = config.Gate;
            Judging = new Judging(config.Gate, config.TickCapacity);
        }

        public Judging Judging { get; }

        /// <summary>Committed beats revealed only after their own time: never fired.</summary>
        public int Missed { get; private set; }

        /// <summary>The latest beat fired, or -1.</summary>
        public int LastBeat { get; private set; } = -1;

        internal void TakeTicks() => Judging.Extend(log.Ticks);

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

        /// <summary>
        /// The beat's bar (-1 before any lock) and whether it is a downbeat, from the locks known now.
        /// </summary>
        public int BarOf(int i, out bool downbeat)
        {
            downbeat = false;
            var beats = log.Beats;
            double lockT = Judging.LockAtOrBefore(beats[i].T);
            if (double.IsNaN(lockT)) return -1;
            int lo = 0, hi = beats.Length;
            while (lo < hi)
            {
                int mid = (lo + hi) >> 1;
                if (beats[mid].T < lockT - 1e-9) lo = mid + 1;
                else hi = mid;
            }
            int since = i - lo;
            if (since < 0) return -1;
            downbeat = (since & 1) == 0;
            return since >> 1;
        }

        public bool IsDownbeat(int i)
        {
            BarOf(i, out bool downbeat);
            return downbeat;
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

        /// <summary>Is the music clear at song time <paramref name="s"/>?</summary>
        public bool ClearAt(double s)
        {
            var ticks = log.Ticks;
            int i = LastAtOrBefore(ticks, s);
            if (i < 0) return false;
            return s - ticks[i].T <= ClearTickAge + 1e-9 && Judging.JudgedAt(i);
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
            return ClearAt(s) && beats[closing].V1 >= gate ? Reliability.Reliable : Reliability.BelowGate;
        }

        /// <summary>
        /// The first point of a grid <paramref name="step"/> beats apart (1 the beats, 0.5 the
        /// eighths, 0.25 the sixteenths) at or after <paramref name="s"/>, from the beats revealed
        /// now, with its fractional beat index in <paramref name="beat"/>; NaN when none is known
        /// (<see cref="GridPoint"/>): past the latest beat's continuation.
        /// </summary>
        internal double GridAtOrAfter(double s, double step, out double beat)
        {
            var beats = log.Beats;
            int last = beats.Length - 1;
            for (int i = Math.Max(0, LastAtOrBefore(beats, s)); i <= last; i++)
                for (int k = 0; k * step < (i == last ? TrackingGap : 1) - 1e-9; k++)
                {
                    beat = i + k * step;
                    double t = GridPoint(beat);
                    // Past the latest beat's continuation, or a tracking gap: on to its closing beat.
                    if (double.IsNaN(t)) break;
                    if (t >= s - 1e-9) return t;
                }
            beat = double.NaN;
            return double.NaN;
        }

        /// <summary>
        /// The song time of fractional beat <paramref name="beat"/>: a revealed beat's own time, a
        /// point inside a beat interval (<see cref="StartsInterval"/>) by its fraction, or, from
        /// the latest beat revealed on, a point of its period while it continues
        /// (<see cref="Continues"/>: the next beat is coming, as the stride grid carries its step);
        /// NaN otherwise.
        /// </summary>
        internal double GridPoint(double beat)
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
        /// The first sixteenth strictly after <paramref name="s"/> among the beats revealed now
        /// (<see cref="GridAtOrAfter"/>), or with none known, the next point of a pulse a quarter of
        /// <see cref="StrideGrid.FreePeriod"/> apart from the latest beat at or before s (native's
        /// <c>next_enemy_sixteenth</c>).
        /// </summary>
        internal double SixteenthAfter(double s)
        {
            double t = GridAtOrAfter(s + 1e-8, 0.25, out _);
            if (!double.IsNaN(t)) return t;
            const double Period = StrideGrid.FreePeriod / 4;
            int last = LastAtOrBefore(log.Beats, s);
            double anchor = last >= 0 ? log.Beats[last].T : 0;
            return anchor + (Math.Floor((s - anchor) / Period + 1e-8) + 1) * Period;
        }

        /// <summary>
        /// The first sixteenth at or after <paramref name="s"/> among the beats revealed now, or s with
        /// none known. A time planned on a beat's continuation moves to the sixteenth a beat committed
        /// since says (native's lightning deferral).
        /// </summary>
        internal double SixteenthAtOrAfter(double s)
        {
            double t = GridAtOrAfter(s, 0.25, out _);
            return double.IsNaN(t) ? s : t;
        }

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

        internal void Fire(Simulation sim, double t, Reliability state)
        {
            int i = fireCursor++;
            int bar = BarOf(i, out bool downbeat);
            sim.Emit(SimEvent.Beat(t, i, bar, downbeat, state, PeriodOf(i), log.Beats[i].V1));
            LastBeat = i;
        }
    }
}
