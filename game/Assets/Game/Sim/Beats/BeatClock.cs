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
    /// known-at times.
    /// </summary>
    public sealed class BeatClock
    {
        public const double TrackingGap = 1.5;
        public const double ClearTickAge = 0.25;

        readonly BeliefLog log;
        readonly double gate;
        int fireCursor;
        int tickCursor = -1;
        int beatCursor = -1;

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

        /// <summary>Is the music clear at song time <paramref name="s"/>? (s never decreases between calls.)</summary>
        public bool ClearAt(double s)
        {
            var ticks = log.Ticks;
            while (tickCursor + 1 < ticks.Length && ticks[tickCursor + 1].T <= s + 1e-9) tickCursor++;
            if (tickCursor < 0) return false;
            return s - ticks[tickCursor].T <= ClearTickAge + 1e-9 && Judging.JudgedAt(tickCursor);
        }

        /// <summary>The beat's state at song time <paramref name="s"/>. (s never decreases between calls.)</summary>
        public Reliability StateAt(double s)
        {
            var beats = log.Beats;
            while (beatCursor + 1 < beats.Length && beats[beatCursor + 1].T <= s + 1e-9) beatCursor++;
            int closing;
            if (StartsInterval(beatCursor)) closing = beatCursor + 1;
            else if (Continues(beatCursor, s)) closing = beatCursor;
            else return Reliability.NoBeatAhead;
            return ClearAt(s) && beats[closing].V1 >= gate ? Reliability.Reliable : Reliability.BelowGate;
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
