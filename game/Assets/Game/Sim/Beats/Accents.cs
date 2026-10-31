using System;

namespace PS.Game.Sim
{
    /// <summary>
    /// Accented beats (unity-game.md, The Sword and Crystal Structures): a beat is accented when the
    /// music is clear and a revealed strong onset or accent forecast lies within <see cref="Window"/>
    /// of it (native's FORECAST_WINDOW). The sword's heavy strikes and an evolution's structure breaks
    /// ask here.
    ///
    /// Each asker keeps its own instance: the lists are scanned from a cursor that only moves on, and
    /// entries are revealed in known-at order, close to time order, so anything a second before the
    /// beat asked about can matter to no later one.
    /// </summary>
    internal sealed class Accents
    {
        /// <summary>A strong onset or a forecast this close to a beat accents it; an evolution's hit this close to an accented beat breaks structures.</summary>
        public const double Window = 0.05;

        readonly BeliefLog log;
        readonly BeatClock clock;
        int onsetFrom, forecastFrom, beatFrom;

        public Accents(BeliefLog log, BeatClock clock)
        {
            this.log = log;
            this.clock = clock;
        }

        /// <summary>Is the beat at <paramref name="beat"/> accented, as known at <paramref name="now"/> with the music clear then?</summary>
        public bool Accented(double beat, double now)
        {
            if (!clock.ClearAt(now)) return false;
            return Near(EntryKind.Onset, ref onsetFrom, beat, true) || Near(EntryKind.Forecast, ref forecastFrom, beat, false);
        }

        /// <summary>
        /// Is a committed beat revealed now within <see cref="Window"/> of <paramref name="t"/> and
        /// accented (as known at <paramref name="t"/>)? <paramref name="index"/> is that beat's, or -1.
        /// </summary>
        public bool NearAccentedBeat(double t, out int index)
        {
            var beats = log.Beats;
            while (beatFrom < beats.Length && beats[beatFrom].T < t - 1.0) beatFrom++;
            for (int i = beatFrom; i < beats.Length && beats[i].T <= t + Window + 1e-9; i++)
            {
                if (Math.Abs(beats[i].T - t) > Window + 1e-9 || !Accented(beats[i].T, t)) continue;
                index = i;
                return true;
            }
            index = -1;
            return false;
        }

        bool Near(EntryKind kind, ref int from, double beat, bool strongOnly)
        {
            var list = log.Revealed(kind);
            while (from < list.Length && list[from].T < beat - 1.0) from++;
            for (int i = from; i < list.Length; i++)
            {
                if (Math.Abs(list[i].T - beat) > Window + 1e-9) continue;
                if (!strongOnly || new OnsetEntry(list[i]).Strong) return true;
            }
            return false;
        }
    }
}
