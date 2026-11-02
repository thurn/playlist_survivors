using System;

namespace Champions.Sim
{
    /// <summary>What a beat or a state change did to a <see cref="Cadence"/>.</summary>
    [Flags]
    public enum CadenceStep
    {
        None = 0,
        /// <summary>The attack fires now.</summary>
        Strike = 1,
        /// <summary>Its period changed: a new beat count, or to or from the seconds pulse.</summary>
        Changed = 2,
    }

    /// <summary>
    /// An attack's cadence (design.md Decision 9, "Rules every attack keeps"): when it fires.
    ///
    /// - <b>On the beat</b>: every <see cref="Beats"/> beats, the count among
    ///   <see cref="BeatRules.CadenceBeats"/> whose period is nearest the attack's target in seconds,
    ///   ties to the longer (<see cref="Choose"/>). 128 BPM: every 2 beats (0.94 s); 174 BPM: every 2
    ///   (0.69 s); 80 BPM: every beat (0.75 s); 90 BPM, a tie: every 2.
    /// - <b>Hysteresis</b>: the count is chosen when the cadence takes a reliable beat and kept while the
    ///   tempo (<see cref="BeatServices.TempoAt"/>) stays within <see cref="BeatRules.CadenceHoldWithin"/>
    ///   of the tempo it was chosen at (<see cref="LockPeriod"/>). Outside it the count is chosen again,
    ///   and only on a downbeat (on any beat while no lock has given the song bars), so a song near a
    ///   boundary never flickers.
    /// - Strikes fall on the beats a whole number of periods from <see cref="Anchor"/>, the beat the
    ///   count was chosen on; one closer than half a period to the previous strike is skipped, so a
    ///   change of count or a return from the pulse never strikes twice in a breath.
    /// - <b>Beatless</b> (no reliable beat): a pulse every <see cref="BeatRules.CadenceFallbackS"/>,
    ///   from the previous strike, until a reliable beat fires again.
    ///
    /// The owner drives it in sim order (<see cref="OnBeat"/> at each beat fired, <see cref="Beatless"/>
    /// when the beat state leaves reliable, <see cref="FirePulse"/> at <see cref="NextPulse"/>), so it
    /// reads only what the sim knew then.
    /// </summary>
    public sealed class Cadence
    {
        readonly BeatServices beats;
        readonly BeatRules rules;
        double nextPulse;

        public Cadence(BeatServices beats, double targetS)
        {
            this.beats = beats;
            rules = beats.Rules;
            TargetS = targetS;
        }

        /// <summary>The period it aims for, seconds.</summary>
        public double TargetS { get; }

        /// <summary>The period in beats; 0 on the seconds pulse.</summary>
        public int Beats { get; private set; }

        /// <summary>The tempo (a beat period, s) the count was chosen at; NaN on the pulse.</summary>
        public double LockPeriod { get; private set; } = double.NaN;

        /// <summary>The beat the count was chosen on, which strikes count from; -1 on the pulse.</summary>
        public int Anchor { get; private set; } = -1;

        /// <summary>The latest strike's time.</summary>
        public double LastStrike { get; private set; } = double.NegativeInfinity;

        /// <summary>The period now, seconds: the count at the lock tempo, or the pulse's.</summary>
        public double PeriodS => Beats > 0 ? Beats * LockPeriod : rules.CadenceFallbackS;

        /// <summary>
        /// The count among <paramref name="choices"/> (ascending) whose period at beat period
        /// <paramref name="period"/> is nearest <paramref name="targetS"/>; ties go to the longer.
        /// </summary>
        public static int Choose(double period, double targetS, int[] choices)
        {
            int best = choices[0];
            double bestOff = Math.Abs(best * period - targetS);
            for (int i = 1; i < choices.Length; i++)
            {
                double off = Math.Abs(choices[i] * period - targetS);
                // Ties to the longer: 90 BPM's 1 and 2 beats are 1/3 s off either way, give or take a bit.
                if (off <= bestOff + 1e-9)
                {
                    best = choices[i];
                    bestOff = Math.Min(off, bestOff);
                }
            }
            return best;
        }

        /// <summary>Starts on the seconds pulse at song time <paramref name="t"/>: the first strike one pulse later.</summary>
        internal void Begin(double t) => nextPulse = t + rules.CadenceFallbackS;

        /// <summary>
        /// The next strike at or after <paramref name="s"/> as planned now: on the beat, the next beat of
        /// the count's grid after the latest fired (predicted past the beats revealed); on the pulse, its
        /// next time. A downbeat's change of count may move it: ask again.
        /// </summary>
        public double NextStrike(double s)
        {
            if (Beats == 0) return Math.Max(s, nextPulse);
            int from = Math.Max(beats.LastFired + 1, Anchor);
            int k = from + ((Anchor - from) % Beats + Beats) % Beats;
            for (; ; k += Beats)
            {
                double t = beats.PredictBeat(k);
                if (double.IsNaN(t)) return double.NaN;
                if (t >= s - 1e-9) return t;
            }
        }

        /// <summary>The seconds pulse's next strike, or infinity on the beat.</summary>
        internal double NextPulse => Beats == 0 ? nextPulse : double.PositiveInfinity;

        /// <summary>The pulse strikes at <paramref name="t"/>.</summary>
        internal void FirePulse(double t)
        {
            LastStrike = t;
            nextPulse = t + rules.CadenceFallbackS;
        }

        /// <summary>The beat state left reliable at <paramref name="t"/>: back to the pulse, from the latest strike.</summary>
        internal CadenceStep Beatless(double t)
        {
            if (Beats == 0) return CadenceStep.None;
            Beats = 0;
            Anchor = -1;
            LockPeriod = double.NaN;
            nextPulse = Math.Max(t, LastStrike + rules.CadenceFallbackS);
            return CadenceStep.Changed;
        }

        /// <summary>Beat <paramref name="i"/> fired at <paramref name="t"/>, reliable or not as the sim judged it then.</summary>
        internal CadenceStep OnBeat(int i, double t, bool reliable)
        {
            if (!reliable) return Beatless(t);
            var step = CadenceStep.None;
            double tempo = beats.TempoAt(i);
            if (Beats == 0)
            {
                Beats = Choose(tempo, TargetS, rules.CadenceBeats);
                LockPeriod = tempo;
                Anchor = i;
                step = CadenceStep.Changed;
            }
            else if (Math.Abs(tempo / LockPeriod - 1) > rules.CadenceHoldWithin + 1e-9 && (!beats.HasBars || beats.IsDownbeat(i)))
            {
                int count = Choose(tempo, TargetS, rules.CadenceBeats);
                LockPeriod = tempo;
                if (count != Beats)
                {
                    Beats = count;
                    Anchor = i;
                    step = CadenceStep.Changed;
                }
            }
            if ((i - Anchor) % Beats == 0 && t - LastStrike >= 0.5 * Beats * tempo - 1e-9)
            {
                LastStrike = t;
                step |= CadenceStep.Strike;
            }
            return step;
        }
    }
}
