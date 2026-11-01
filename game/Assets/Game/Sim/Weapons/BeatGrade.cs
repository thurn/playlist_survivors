using System;

namespace PS.Game.Sim
{
    /// <summary>
    /// How well a press follows the beat (<see cref="SwordTrigger.Press"/>, docs/design/game-modes.md): a grade from
    /// <see cref="Lowest"/> to 1 that scales the strike's reach and damage. It is generous: full unless the press is
    /// clearly off the beat.
    ///
    /// - With no reliable beat at the press (<see cref="Reliability"/> short of Reliable: no beat ahead, below the
    ///   gate or the music unclear), there is nothing to follow: full.
    /// - Else the press is measured, in heard time, from the nearest beat or half beat of the beat interval it falls
    ///   in (from the latest revealed beat at or before it, to the next when that is revealed, else a period on).
    ///   Within <see cref="FullWindowS"/> it is full; from there it falls linearly to <see cref="Lowest"/> at a quarter
    ///   beat off, the furthest a press can be from that grid. A beat too fast for any press to fall outside the
    ///   window grades every press full.
    ///
    /// It reads only revealed entries and the beat's state as known at the sim's time, so it is a function of the
    /// stream's known-at times, as everything the sim decides is.
    /// </summary>
    internal static class BeatGrade
    {
        /// <summary>A press this close to a beat or half beat (s) grades full: about the spread of a good player's taps.</summary>
        public const double FullWindowS = 0.07;
        /// <summary>The grade of a press a quarter beat off: half the reach and damage.</summary>
        public const double Lowest = 0.5;

        /// <summary>
        /// The grade of a press heard at <paramref name="heard"/>, and the beat or half beat it was measured from
        /// (<paramref name="point"/>, a beat if <paramref name="onBeat"/>); with no reliable beat, the press itself
        /// and false.
        /// </summary>
        public static double Of(BeliefLog log, BeatClock clock, double heard, out double point, out bool onBeat)
        {
            point = heard;
            onBeat = false;
            if (clock.StateAt(heard) != Reliability.Reliable) return 1;
            var beats = log.Beats;
            int b = BeatClock.LastAtOrBefore(beats, heard);
            if (b < 0) return 1;
            double start = beats[b].T;
            double half = (clock.StartsInterval(b) ? beats[b + 1].T - start : clock.PeriodOf(b)) / 2;
            if (!(half > 0)) return 1;
            int k = (int)Math.Round((heard - start) / half);
            point = start + k * half;
            onBeat = k % 2 == 0;
            double off = Math.Abs(heard - point), worst = half / 2;
            if (off <= FullWindowS || worst <= FullWindowS) return 1;
            return 1 - (1 - Lowest) * Math.Min(1, (off - FullWindowS) / (worst - FullWindowS));
        }

        /// <summary>
        /// Is a press heard at <paramref name="heard"/> on the beat (the dash's judgement, <see cref="Dash"/>): within
        /// <paramref name="window"/> of the nearest beat of the beat interval it falls in, measured as <see cref="Of"/>
        /// measures (half beats don't count)? With no reliable beat, every press is.
        /// </summary>
        public static bool OnBeat(BeliefLog log, BeatClock clock, double heard, double window)
        {
            if (clock.StateAt(heard) != Reliability.Reliable) return true;
            var beats = log.Beats;
            int b = BeatClock.LastAtOrBefore(beats, heard);
            if (b < 0) return true;
            double start = beats[b].T;
            double next = clock.StartsInterval(b) ? beats[b + 1].T : start + clock.PeriodOf(b);
            return Math.Min(heard - start, next - heard) <= window + 1e-9;
        }
    }
}
