namespace Champions.Sim
{
    /// <summary>Why the music is in a quiet moment (<see cref="QuietMoments"/>).</summary>
    public enum QuietReason
    {
        /// <summary>Not quiet.</summary>
        None = 0,
        /// <summary>A gap: silence for <see cref="BeatRules.SilenceS"/>, as between songs.</summary>
        Silence = 1,
        /// <summary>A beatless stretch: no reliable beat for <see cref="BeatRules.BeatlessS"/>.</summary>
        Beatless = 2,
    }

    /// <summary>
    /// Quiet moments (design.md, "Held for a quiet moment"; plan.md, "Beat services"), when held cards
    /// show: <see cref="BeatRules.SilenceS"/> of silence, or <see cref="BeatRules.BeatlessS"/> with no
    /// reliable beat.
    ///
    /// - <b>Silence</b> at song time s: the latest revealed tick at or before s is under
    ///   <see cref="BeatRules.SilenceEnergy"/>, or older than <see cref="BeatServices.ClearTickAge"/>,
    ///   or there is none: no music at all is silence too.
    /// - Both stretches are measured on the sim's ticks from the first tick end that found them (from the
    ///   sim's start, when it knew nothing), and the moment lasts while either holds; when both do,
    ///   silence names it.
    ///
    /// Answers come from the entries revealed at each tick's end, never revised.
    /// </summary>
    public sealed class QuietMoments
    {
        readonly BeliefLog log;
        readonly BeatRules rules;
        double silentSince = double.NaN, beatlessSince = double.NaN;

        public QuietMoments(BeliefLog log, BeatRules rules)
        {
            this.log = log;
            this.rules = rules;
        }

        public QuietReason Reason { get; private set; }

        /// <summary>When the current quiet moment began; NaN when not quiet.</summary>
        public double Since { get; private set; } = double.NaN;

        public bool Quiet => Reason != QuietReason.None;

        /// <summary>Is the music silent at song time <paramref name="s"/>, from the ticks revealed now?</summary>
        public bool SilentAt(double s)
        {
            var ticks = log.Ticks;
            int i = BeatServices.LastAtOrBefore(ticks, s);
            return i < 0 || s - ticks[i].T > BeatServices.ClearTickAge + 1e-9 || ticks[i].V1 < rules.SilenceEnergy;
        }

        /// <summary>The sim starts at <paramref name="start"/>, knowing nothing yet: silent and beatless since then.</summary>
        internal void Begin(double start)
        {
            silentSince = start;
            beatlessSince = start;
        }

        /// <summary>The sim's tick ended at <paramref name="s"/> with the beat in <paramref name="beat"/>; true if the moment changed.</summary>
        internal bool Tick(double s, Reliability beat)
        {
            silentSince = SilentAt(s) ? (double.IsNaN(silentSince) ? s : silentSince) : double.NaN;
            beatlessSince = beat != Reliability.Reliable ? (double.IsNaN(beatlessSince) ? s : beatlessSince) : double.NaN;
            var reason = !double.IsNaN(silentSince) && s - silentSince >= rules.SilenceS - 1e-9 ? QuietReason.Silence
                : !double.IsNaN(beatlessSince) && s - beatlessSince >= rules.BeatlessS - 1e-9 ? QuietReason.Beatless
                : QuietReason.None;
            if (reason == Reason) return false;
            Since = reason == QuietReason.None ? double.NaN : Reason == QuietReason.None ? s : Since;
            Reason = reason;
            return true;
        }
    }
}
