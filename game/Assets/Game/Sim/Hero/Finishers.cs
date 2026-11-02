using System;

namespace PS.Game.Sim
{
    /// <summary>
    /// The finishers (`--finishers`, docs/archive/design/game-modes.md, "Finishers"): a sparse, optional rhythm verb on top of a
    /// mode whose frequent verbs stay unjudged. A ring closes on the hero toward a phrase downbeat; Space heard in its
    /// window fires the sword's screen-clearing slam (<see cref="Sword.Finisher"/>); a miss fades with nothing lost.
    ///
    /// - <b>Phrases</b>: the beats counted from the latest lock (as bars are: <see cref="BeatClock.FirstAfterLock"/>), every
    ///   <see cref="ShortPhrase"/> beats (four 4-beat bars), or every <see cref="LongPhrase"/> where those would come under
    ///   <see cref="PhraseMinS"/> apart (over 137 BPM): rings 7 to 14 s apart from 69 BPM up.
    /// - <b>Causal</b>: the target downbeat's time is predicted from the beats revealed now, the latest's time plus its
    ///   beats on at the mean of up to <see cref="PeriodBeats"/> recent intervals (its own time once it is revealed). A
    ///   ring starts once its predicted downbeat is a <see cref="Telegraph"/> ahead (4 beats, 2 where 4 would pass
    ///   <see cref="TelegraphMaxS"/>), and only with a reliable beat (the gate), the run playing, the world unfrozen and
    ///   the hero standing. A downbeat found later than <see cref="LateStart"/> of its telegraph before it (after a hold,
    ///   a lock, the beat turning reliable) gets no ring. Rings never use accents or section changes: a song the game
    ///   hasn't heard doesn't know them ahead.
    /// - <b>Retimes</b>: until the window closes, each tick the ring takes the beat nearest the downbeat it has promised,
    ///   revealed or predicted again; when that moves it (more than <see cref="RetimeS"/>, or at all once the beat is
    ///   revealed), a <see cref="FinisherPhase.Retime"/> event carries the new beat and downbeat, and the client eases onto
    ///   it. A steady beat keeps the phrase's beat; a tracker that drops or doubles beats (dnb-174's flips between 0.34
    ///   and 0.69 s) moves the ring by less than a beat to the beat nearest its promise, off the phrase count.
    /// - <b>The press</b>: Space heard within <see cref="WindowS"/> of the downbeat, as predicted when the sim takes the
    ///   key, fires the finisher at once, in heard time. A live key reaches the sim about the lead after it was heard, so
    ///   the ring stays open to presses heard in its window for <see cref="Sword.PressBackS"/> after it, then ends as a
    ///   <see cref="FinisherPhase.Miss"/>. A press outside the window does nothing and costs nothing (the ring goes on).
    /// - <b>Holds</b>: a ring in flight when the world freezes, the hero falls or the run stops playing is dropped; a key
    ///   heard before the hold ended never hits.
    /// </summary>
    public sealed class Finishers
    {
        /// <summary>A phrase's beats: four bars of four.</summary>
        public const int ShortPhrase = 16;
        /// <summary>Eight bars of four, where <see cref="ShortPhrase"/> would come under <see cref="PhraseMinS"/>.</summary>
        public const int LongPhrase = 32;
        /// <summary>The shortest gap between short phrases' rings (s): over 137 BPM the phrase is eight bars.</summary>
        public const double PhraseMinS = 7;
        /// <summary>The ring closes over this many beats before its downbeat...</summary>
        public const int TelegraphBeats = 4;
        /// <summary>...or half as many where they would last longer than this (s, under 96 BPM).</summary>
        public const double TelegraphMaxS = 2.5;
        /// <summary>A downbeat first found with less than this share of its telegraph left gets no ring.</summary>
        public const double LateStart = 0.75;
        /// <summary>A press heard this close to the downbeat, either side, fires the finisher (s): BeatGrade's full window and a little more.</summary>
        public const double WindowS = 0.1;
        /// <summary>A prediction moving less than this keeps the ring's downbeat (s).</summary>
        public const double RetimeS = 1e-3;
        /// <summary>The recent intervals whose mean predicts the beats ahead.</summary>
        public const int PeriodBeats = 4;

        readonly BeliefLog log;
        readonly BeatClock clock;
        readonly SimState state;
        int rings;
        bool active, exact;
        int ring, beat;
        double downbeat, start;
        // The latest ring's (or skipped downbeat's) time: the next is half a short phrase later at least.
        double lastDownbeat = double.NegativeInfinity;
        // The latest tick end held (frozen, fallen or out of play): a key heard before it never hits.
        double heldUntil = double.NegativeInfinity;

        internal Finishers(BeliefLog log, BeatClock clock, SimState state)
        {
            this.log = log;
            this.clock = clock;
            this.state = state;
        }

        /// <summary>Rings started, hit and missed or dropped.</summary>
        public int Rings => rings;
        public int Hits { get; private set; }

        /// <summary>The ring's telegraph at beat period <paramref name="period"/>: 4 beats, or 2 where 4 would last over <see cref="TelegraphMaxS"/>.</summary>
        public static double Telegraph(double period) =>
            (TelegraphBeats * period <= TelegraphMaxS + 1e-9 ? TelegraphBeats : TelegraphBeats / 2) * period;

        /// <summary>A phrase's beats at beat period <paramref name="period"/>.</summary>
        public static int PhraseBeats(double period) => ShortPhrase * period >= PhraseMinS - 1e-9 ? ShortPhrase : LongPhrase;

        /// <summary>The tick's end: a ring starts, retimes, ends or is dropped.</summary>
        internal void Tick(Simulation sim, double end)
        {
            if (Weapon.Rests(state))
            {
                heldUntil = end;
                if (!active) return;
                active = false;
                lastDownbeat = downbeat;
                sim.Emit(SimEvent.Finisher(end, ring, FinisherPhase.Drop, beat, downbeat, start, WindowS));
                return;
            }
            if (active)
            {
                if (end > downbeat + WindowS + Sword.PressBackS + 1e-9)
                {
                    active = false;
                    lastDownbeat = downbeat;
                    sim.Emit(SimEvent.Finisher(end, ring, FinisherPhase.Miss, beat, downbeat, start, WindowS));
                    return;
                }
                if (end > downbeat + WindowS + 1e-9) return;
                // The beat nearest the downbeat the ring has promised: a tracker that skips or adds beats moves it by
                // less than a beat, not by the beats it miscounted.
                int k = Nearest(downbeat);
                double now = Predict(k, out _);
                bool revealed = k < log.Beats.Length && (!exact || k != beat);
                if (k != beat || Math.Abs(now - downbeat) > RetimeS || revealed && now != downbeat)
                {
                    beat = k;
                    downbeat = now;
                    exact = k < log.Beats.Length;
                    sim.Emit(SimEvent.Finisher(end, ring, FinisherPhase.Retime, beat, downbeat, start, WindowS));
                }
                else exact |= revealed;
                return;
            }
            if (sim.Sword == null || state.Beat != Reliability.Reliable || !Next(end, out int j, out double t, out double period)) return;
            double telegraph = Telegraph(period);
            if (t - end > telegraph + 1e-9) return;
            if (t - end < LateStart * telegraph)
            {
                lastDownbeat = t;
                return;
            }
            active = true;
            ring = rings++;
            beat = j;
            exact = j < log.Beats.Length;
            downbeat = t;
            start = t - telegraph;
            sim.Emit(SimEvent.Finisher(end, ring, FinisherPhase.Ring, beat, downbeat, start, WindowS));
        }

        /// <summary>
        /// Space heard at <paramref name="heard"/>: in the ring's window, the finisher fires at once (in heard time) and the
        /// press is the finisher's alone (true); otherwise nothing happens.
        /// </summary>
        internal bool Press(Simulation sim, double heard)
        {
            if (!active || heard < heldUntil - 1e-9 || Math.Abs(heard - downbeat) > WindowS + 1e-9 || sim.Sword == null) return false;
            active = false;
            lastDownbeat = downbeat;
            Hits++;
            double impact = sim.Sword.Finisher(sim, heard);
            sim.Emit(SimEvent.Finisher(sim.Now, ring, FinisherPhase.Hit, beat, downbeat, start, WindowS, impact, heard));
            return true;
        }

        /// <summary>
        /// The next phrase downbeat after <paramref name="after"/>, as the beats revealed now predict it: its beat index,
        /// time and the beat period there. False with no lock or no beat since it, or none half a short phrase past the last.
        /// </summary>
        bool Next(double after, out int j, out double t, out double period)
        {
            j = -1;
            t = period = double.NaN;
            int last = log.Beats.Length - 1;
            int first = clock.FirstAfterLock(after);
            if (last < 0 || first > last) return false;
            Predict(last + 1, out period);
            int phrase = PhraseBeats(period);
            j = first + (last - first) / phrase * phrase;
            for (t = Predict(j, out _); t <= after + 1e-9 || t < lastDownbeat + 0.5 * PhraseMinS; t = Predict(j, out _)) j += phrase;
            return true;
        }

        /// <summary>The beat, revealed or predicted (<see cref="Predict"/>), nearest song time <paramref name="t"/>.</summary>
        int Nearest(double t)
        {
            var beats = log.Beats;
            int last = beats.Length - 1;
            if (t >= beats[last].T)
            {
                Predict(last + 1, out double period);
                return last + (int)Math.Round((t - beats[last].T) / period);
            }
            int i = BeatClock.LastAtOrBefore(beats, t);
            if (i < 0) return 0;
            return beats[i + 1].T - t < t - beats[i].T ? i + 1 : i;
        }

        /// <summary>
        /// Beat <paramref name="j"/>'s time from the beats revealed now: its own once revealed, else the latest's plus its
        /// beats on at the <paramref name="period"/> the latest intervals give (the mean of up to <see cref="PeriodBeats"/>
        /// beat intervals, or the latest beat's period).
        /// </summary>
        double Predict(int j, out double period)
        {
            var beats = log.Beats;
            int last = beats.Length - 1;
            int k = last;
            while (k > 0 && last - k < PeriodBeats && clock.StartsInterval(k - 1)) k--;
            period = k < last ? (beats[last].T - beats[k].T) / (last - k) : clock.PeriodOf(last);
            return j <= last ? beats[j].T : beats[last].T + (j - last) * period;
        }
    }
}
