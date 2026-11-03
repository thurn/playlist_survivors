namespace Champions.Sim
{
    /// <summary>
    /// A telegraph's timing (<see cref="Telegraphs"/>): it starts filling at <see cref="Start"/> and its
    /// attack lands at <see cref="Land"/>. On the beat both are beats (<see cref="StartBeat"/>,
    /// <see cref="LandBeat"/>); beatless they are -1.
    /// </summary>
    public readonly struct Telegraph
    {
        public readonly double Start, Land;
        public readonly int StartBeat, LandBeat;
        /// <summary>A promised deadline, unchanged by later tempo/confidence revisions.</summary>
        public readonly bool Fixed;

        public Telegraph(double start, double land, int startBeat, int landBeat, bool fixedTime = false)
        {
            Start = start;
            Land = land;
            StartBeat = startBeat;
            LandBeat = landBeat;
            Fixed = fixedTime;
        }

        /// <summary>Planned on the beat (whole beats), or beatless (seconds).</summary>
        public bool OnBeat => LandBeat >= 0;

        /// <summary>The whole beats it fills over on the beat; 0 beatless.</summary>
        public int Beats => OnBeat ? LandBeat - StartBeat : 0;
    }

    /// <summary>
    /// Telegraph timing (design.md, "Threats" and "Every boss"; plan.md, "Beat services"): a telegraph
    /// fills over whole beats and lands on a beat; with no reliable beat it takes
    /// <see cref="BeatRules.TelegraphBeatlessBeatS"/> per beat.
    ///
    /// <code>
    /// var t = sim.Telegraphs.Plan(now, 2);          // a big ork's charge: two beats, lands on the next
    /// var s = sim.Telegraphs.PlanToDownbeat(now, 2); // a stomp: at least two beats, lands on a downbeat
    /// double land = sim.Telegraphs.LandTime(t);      // the landing beat's time as known now
    /// </code>
    ///
    /// On the beat, the start is the first beat at or after the asked time, and the landing beat is
    /// predicted past the beats revealed (<see cref="BeatServices.PredictBeat"/>); the owner re-reads
    /// <see cref="LandTime"/> as beats arrive, and the attack lands when that beat fires.
    /// </summary>
    public sealed class Telegraphs
    {
        readonly BeatServices beats;
        readonly BeatRules rules;

        public Telegraphs(BeatServices beats)
        {
            this.beats = beats;
            rules = beats.Rules;
        }

        /// <summary>A telegraph asked for at <paramref name="from"/>, filling over <paramref name="count"/> beats.</summary>
        public Telegraph Plan(double from, int count)
        {
            if (beats.Reliable(from))
            {
                double start = beats.NextBeat(from, out int i);
                if (!double.IsNaN(start))
                {
                    double land = beats.PredictBeat(i + count);
                    if (!double.IsNaN(land)) return new Telegraph(start, land, i, i + count);
                }
            }
            return new Telegraph(from, from + count * rules.TelegraphBeatlessBeatS, -1, -1);
        }

        /// <summary>
        /// A telegraph asked for at <paramref name="from"/>, filling over at least <paramref name="minBeats"/>
        /// beats and landing on a downbeat; on the beat with no bars yet, it lands after exactly that many.
        /// </summary>
        public Telegraph PlanToDownbeat(double from, int minBeats)
        {
            var plain = Plan(from, minBeats);
            if (!plain.OnBeat || !beats.HasBars) return plain;
            for (int k = plain.LandBeat; k < plain.LandBeat + rules.BeatsPerBar; k++)
            {
                if (!beats.IsDownbeat(k)) continue;
                double land = beats.PredictBeat(k);
                if (!double.IsNaN(land)) return new Telegraph(plain.Start, land, plain.StartBeat, k);
            }
            return plain;
        }

        internal Telegraph Freeze(in Telegraph telegraph) => new Telegraph(telegraph.Start, LandTime(telegraph), telegraph.StartBeat, telegraph.LandBeat, telegraph.Fixed);

        /// <summary>Preserve the captured reaction window, rounding the restored landing forward onto the future grid.</summary>
        internal Telegraph Resume(in Telegraph telegraph, double elapsed)
        {
            double start = telegraph.Start + elapsed, land = telegraph.Land + elapsed;
            if (beats.Reliable(land))
            {
                double next = beats.NextBeat(land, out int index);
                if (!double.IsNaN(next)) return new Telegraph(start, next, index - telegraph.Beats, index, telegraph.Fixed);
            }
            return new Telegraph(start, land, -1, -1, telegraph.Fixed);
        }

        /// <summary>The current landing beat prediction, or the seconds fallback.</summary>
        public double LandTime(in Telegraph telegraph)
        {
            if (telegraph.Fixed || !telegraph.OnBeat) return telegraph.Land;
            double t = beats.PredictBeat(telegraph.LandBeat);
            return double.IsNaN(t) ? telegraph.Land : t;
        }
    }
}
