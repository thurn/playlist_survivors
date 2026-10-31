using System;
using PS.Game.Sim;

namespace Game.Client.Hud
{
    /// <summary>The beat around one song time, as the HUD shows it (<see cref="HeardBeats.At"/>).</summary>
    public struct HeardBeat
    {
        /// <summary>A beat known by then is at or before it.</summary>
        public bool Has;
        /// <summary>The latest such beat: its index, time and period, and whether it is a downbeat.</summary>
        public int Last;
        public double LastT, Period;
        public bool LastDown;
        /// <summary>The next beat: the next one known, else the latest one's period on while it continues (NaN past that).</summary>
        public double NextT;
        public bool NextDown;

        /// <summary>From the latest beat to the next, 0 to 1 (1 past the continuation).</summary>
        public double Phase(double s) =>
            !Has ? 1 : double.IsNaN(NextT) ? 1 : Math.Max(0, Math.Min(1, (s - LastT) / Math.Max(1e-6, NextT - LastT)));
    }

    /// <summary>
    /// The beats the HUD shows at a song time: only those known by then (each kind's revealed list
    /// is in known-at order, so they are a prefix), so a frame never depends on how far the frontier
    /// ran ahead. Bars and downbeats are the sim's (<see cref="BeatClock.BarOf"/>), never a local count.
    /// </summary>
    public static class HeardBeats
    {
        /// <summary>How many beats are known by song time <paramref name="s"/>.</summary>
        public static int KnownBy(BeliefLog log, double s)
        {
            int lo = 0, hi = log.Count(EntryKind.Beat);
            while (lo < hi)
            {
                int mid = (lo + hi) >> 1;
                if (log.KnownAt(EntryKind.Beat, mid) <= s + 1e-9) lo = mid + 1;
                else hi = mid;
            }
            return lo;
        }

        /// <summary>The beat at song time <paramref name="s"/> from the beats known by then.</summary>
        public static HeardBeat At(BeliefLog log, BeatClock clock, double s)
        {
            var beats = log.Beats;
            int known = KnownBy(log, s);
            int lo = 0, hi = known;
            while (lo < hi)
            {
                int mid = (lo + hi) >> 1;
                if (beats[mid].T <= s + 1e-9) lo = mid + 1;
                else hi = mid;
            }
            int last = lo - 1;
            var b = new HeardBeat { Last = last, NextT = double.NaN };
            if (last < 0) return b;
            b.Has = true;
            b.LastT = beats[last].T;
            b.Period = clock.PeriodOf(last);
            b.LastDown = clock.IsDownbeat(last);
            if (last + 1 < known)
            {
                b.NextT = beats[last + 1].T;
                b.NextDown = clock.IsDownbeat(last + 1);
            }
            else if (s <= b.LastT + BeatClock.TrackingGap * beats[last].V0 + 1e-9)
            {
                // Not yet known, so neither is its bar: a plain beat.
                b.NextT = b.LastT + b.Period;
            }
            return b;
        }
    }
}
