using System;
using PS.Game.Sim;

namespace Game.Client.Sound
{
    /// <summary>
    /// The committed beats as a grid for sounds (native's slice/sounds.rs): beat indices, slots on
    /// beats or sixteenths, and the per-beat buckets aggregation counts in. Past the latest beat
    /// revealed, its period extends the grid; before the first, there is none.
    /// </summary>
    public static class MusicalGrid
    {
        /// <summary>Slots per beat: on the beat, or on the sixteenth.</summary>
        public const int Beat = 1, Sixteenth = 4;

        /// <summary>The (fractional) beat index at song time <paramref name="t"/>, or NaN before the first beat.</summary>
        public static double BeatIndexAt(ReadOnlySpan<StreamEntry> beats, double t)
        {
            if (beats.Length == 0 || t < beats[0].T) return double.NaN;
            int lo = 0, hi = beats.Length;
            while (lo < hi)
            {
                int mid = (lo + hi) >> 1;
                if (beats[mid].T <= t) lo = mid + 1;
                else hi = mid;
            }
            int k = lo - 1;
            double span = k + 1 < beats.Length ? beats[k + 1].T - beats[k].T : beats[k].V0;
            return k + (t - beats[k].T) / Math.Max(1e-3, span);
        }

        /// <summary>The song time of beat index <paramref name="bi"/> (0 or more).</summary>
        public static double TimeOf(ReadOnlySpan<StreamEntry> beats, double bi)
        {
            int k = Math.Min((int)Math.Floor(Math.Max(0, bi)), beats.Length - 1);
            double f = bi - k;
            double span = k + 1 < beats.Length ? beats[k + 1].T - beats[k].T : beats[k].V0;
            return beats[k].T + f * span;
        }

        /// <summary>The first slot at or after <paramref name="t"/>, <paramref name="perBeat"/> to a beat (<paramref name="t"/> itself before the first beat).</summary>
        public static double SlotAt(ReadOnlySpan<StreamEntry> beats, double t, int perBeat)
        {
            double bi = BeatIndexAt(beats, t);
            if (double.IsNaN(bi)) return t;
            return TimeOf(beats, Math.Ceiling(bi * perBeat - 1e-6) / perBeat);
        }

        /// <summary>
        /// The beat that song time <paramref name="t"/> falls in, for aggregation. Before the first
        /// beat, half seconds stand in for beats, numbered apart from the beats'.
        /// </summary>
        public static long Bucket(ReadOnlySpan<StreamEntry> beats, double t)
        {
            double bi = BeatIndexAt(beats, t);
            return double.IsNaN(bi) ? long.MinValue / 2 + (long)Math.Floor(t * 2) : (long)Math.Floor(bi + 1e-6);
        }
    }

    /// <summary>
    /// Gem chimes climbing a pentatonic scale (native's slice/sounds/chain.rs): each chime within
    /// 0.3 s of the previous one steps up, from C6 through C8, holding the top note; a pause
    /// returns it to C6. The sim's events are settled, so the chain never rewinds.
    /// </summary>
    public sealed class PentatonicChain
    {
        public const double Window = 0.3;
        static readonly int[] Notes = { 84, 86, 88, 91, 93, 96, 98, 100, 103, 105, 108 };
        double _last = double.NegativeInfinity;
        int _step;

        /// <summary>The MIDI note of a chime at song time <paramref name="s"/> (chimes in time order).</summary>
        public int Next(double s)
        {
            _step = s - _last <= Window + 1e-9 ? Math.Min(_step + 1, Notes.Length - 1) : 0;
            _last = s;
            return Notes[_step];
        }
    }
}
