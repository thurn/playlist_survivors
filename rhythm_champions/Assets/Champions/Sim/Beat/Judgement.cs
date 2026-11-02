using System;

namespace Champions.Sim
{
    /// <summary>Why a press was judged on the beat or not (<see cref="Judgement"/>).</summary>
    public enum JudgedBy
    {
        /// <summary>Off the beat: outside every window.</summary>
        Off = 0,
        /// <summary>No reliable beat when it was heard: every press is on the beat.</summary>
        Beatless = 1,
        /// <summary>Within the beat window of a beat.</summary>
        Beat = 2,
        /// <summary>Within the half-beat window of a half beat the music plays.</summary>
        HalfBeat = 3,
        /// <summary>Within the onset window of a strong onset.</summary>
        StrongOnset = 4,
    }

    /// <summary>A press's judgement: on the beat or not, why, and the moment it was measured from.</summary>
    public readonly struct Judged
    {
        public readonly JudgedBy By;
        /// <summary>The beat, half beat or onset it was measured from; the press itself when beatless or off.</summary>
        public readonly double Point;

        public Judged(JudgedBy by, double point)
        {
            By = by;
            Point = point;
        }

        public bool OnBeat => By != JudgedBy.Off;
    }

    /// <summary>
    /// The on-beat judgement (design.md Decision 6, "On the beat"): binary and forgiving, in heard
    /// time. A press heard at h is on the beat when, as known when the sim takes it:
    /// <list type="bullet">
    /// <item>no beat is reliable at h (<see cref="BeatServices.Reliable"/>): every press is; else</item>
    /// <item>h is within <see cref="BeatRules.BeatWindowS"/> of a beat: the latest at or before h, or the next
    /// (revealed, or the latest's continuation); or</item>
    /// <item>within <see cref="BeatRules.HalfBeatWindowS"/> of a half beat that the music plays (an onset within
    /// <see cref="BeatRules.PlayedWithinS"/> of it); or</item>
    /// <item>within <see cref="BeatRules.StrongOnsetWindowS"/> of a strong onset.</item>
    /// </list>
    /// It reads only revealed entries, so it is a function of the stream's known-at times. The music runs
    /// seconds ahead of the speakers in the stream, so the onsets near a press are long known when it is taken.
    /// </summary>
    public sealed class Judgement
    {
        readonly BeatServices beats;
        readonly BeatRules rules;

        public Judgement(BeatServices beats)
        {
            this.beats = beats;
            rules = beats.Rules;
        }

        /// <summary>Judges a press heard at song time <paramref name="heard"/>.</summary>
        public Judged Judge(double heard)
        {
            if (!beats.Reliable(heard)) return new Judged(JudgedBy.Beatless, heard);
            var revealed = beats.Revealed;
            int b = BeatServices.LastAtOrBefore(revealed, heard);
            if (b < 0) return new Judged(JudgedBy.Beatless, heard);
            double before = revealed[b].T;
            double after = beats.BeatTime(b + 1);
            if (double.IsNaN(after)) after = before + beats.PeriodOf(b);
            double nearest = heard - before <= after - heard ? before : after;
            if (Math.Abs(heard - nearest) <= rules.BeatWindowS + 1e-9) return new Judged(JudgedBy.Beat, nearest);
            double half = (before + after) / 2;
            if (Math.Abs(heard - half) <= rules.HalfBeatWindowS + 1e-9 && beats.PlayedNear(half, rules.PlayedWithinS))
                return new Judged(JudgedBy.HalfBeat, half);
            if (beats.StrongOnsetNear(heard, rules.StrongOnsetWindowS)) return new Judged(JudgedBy.StrongOnset, heard);
            return new Judged(JudgedBy.Off, heard);
        }

        /// <summary>Is a press heard at <paramref name="heard"/> on the beat?</summary>
        public bool OnBeat(double heard) => Judge(heard).OnBeat;
    }
}
