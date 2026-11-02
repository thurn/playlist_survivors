namespace Champions.Sim
{
    /// <summary>
    /// The beat services' numbers (Content/beat.json; design.md Decisions 6 and 9, "Held for a quiet
    /// moment" and "Every boss"). The defaults are the design's; beat.json holds the same, and an
    /// executor may change a recommended default there with a recorded reason, never a decision.
    /// </summary>
    public sealed class BeatRules
    {
        public const string File = "beat.json";

        /// <summary>
        /// A reliable beat's and a clear tick's minimum evidence: the analyzer's gate
        /// (plugin/models/manifest.json's <c>gate</c>); a clear track's evidence is about 0.5.
        /// </summary>
        public double Gate = 0.2;

        /// <summary>
        /// A bar's beats. The analyzer marks no metre, so bars are counted from the first committed
        /// beat at or after the latest lock (<see cref="BeatServices"/>); 4 is the design's
        /// ("lands on beat 4", "every fourth slash").
        /// </summary>
        public int BeatsPerBar = 4;

        /// <summary>A strong onset this close to a beat makes it a strong beat (a boss enters on a strong downbeat).</summary>
        public double StrongBeatWindowS = 0.05;

        // The judgement (Decision 6): binary windows either side, in heard time.
        /// <summary>A press this close to a beat is on the beat.</summary>
        public double BeatWindowS = 0.08;
        /// <summary>A press this close to a half beat the music plays is on the beat.</summary>
        public double HalfBeatWindowS = 0.06;
        /// <summary>A press this close to a strong onset is on the beat.</summary>
        public double StrongOnsetWindowS = 0.06;
        /// <summary>The music plays a half beat when an onset lies this close to it.</summary>
        public double PlayedWithinS = 0.05;

        // The attack cadence (Decision 9).
        /// <summary>An attack's period is the beat count nearest this many seconds.</summary>
        public double CadenceTargetS = 1.0;
        /// <summary>The beat counts it chooses among, ascending; ties go to the longer.</summary>
        public int[] CadenceBeats = { 1, 2, 4 };
        /// <summary>The chosen count holds while the tempo stays within this share of the tempo it was chosen at.</summary>
        public double CadenceHoldWithin = 0.08;
        /// <summary>With no reliable beat, attacks fire on a pulse this many seconds apart.</summary>
        public double CadenceFallbackS = 1.0;

        // Quiet moments ("Held for a quiet moment").
        /// <summary>A tick's energy under this is silence (the energy is 0 to 1 over 30 dB under the running peak).</summary>
        public double SilenceEnergy = 0.1;
        /// <summary>Silence this long is a quiet moment (a gap between songs).</summary>
        public double SilenceS = 0.75;
        /// <summary>No reliable beat for this long is a quiet moment.</summary>
        public double BeatlessS = 2.0;

        /// <summary>With no reliable beat, a telegraph takes this long per beat.</summary>
        public double TelegraphBeatlessBeatS = 0.5;

        /// <summary>Reads beat.json's object.</summary>
        public static BeatRules Read(JsonValue json, string path)
        {
            var r = new ContentReader(json, path);
            var rules = new BeatRules
            {
                Gate = r.Number("gate"),
                BeatsPerBar = r.Int("beatsPerBar"),
                StrongBeatWindowS = r.Number("strongBeatWindowS"),
            };
            var judgement = r.Object("judgement");
            rules.BeatWindowS = judgement.Number("beatS");
            rules.HalfBeatWindowS = judgement.Number("halfBeatS");
            rules.StrongOnsetWindowS = judgement.Number("strongOnsetS");
            rules.PlayedWithinS = judgement.Number("playedWithinS");
            judgement.End();
            var cadence = r.Object("cadence");
            rules.CadenceTargetS = cadence.Number("targetS");
            rules.CadenceBeats = cadence.Ints("beats");
            rules.CadenceHoldWithin = cadence.Number("holdWithin");
            rules.CadenceFallbackS = cadence.Number("fallbackS");
            cadence.End();
            var quiet = r.Object("quiet");
            rules.SilenceEnergy = quiet.Number("silenceEnergy");
            rules.SilenceS = quiet.Number("silenceS");
            rules.BeatlessS = quiet.Number("beatlessS");
            quiet.End();
            var telegraph = r.Object("telegraph");
            rules.TelegraphBeatlessBeatS = telegraph.Number("beatlessBeatS");
            telegraph.End();
            r.End();
            rules.Validate(path);
            return rules;
        }

        void Validate(string path)
        {
            if (BeatsPerBar < 1) throw ContentReader.Fail(path + ".beatsPerBar", "at least 1");
            if (CadenceBeats.Length == 0) throw ContentReader.Fail(path + ".cadence.beats", "at least one beat count");
            for (int i = 0; i < CadenceBeats.Length; i++)
                if (CadenceBeats[i] < 1 || (i > 0 && CadenceBeats[i] <= CadenceBeats[i - 1]))
                    throw ContentReader.Fail(path + ".cadence.beats", "positive and ascending");
            if (!(CadenceFallbackS > 0) || !(CadenceTargetS > 0)) throw ContentReader.Fail(path + ".cadence", "positive seconds");
            if (!(TelegraphBeatlessBeatS > 0)) throw ContentReader.Fail(path + ".telegraph.beatlessBeatS", "positive");
        }

        /// <summary>A copy, so a run's config never shares the content's.</summary>
        public BeatRules Clone()
        {
            var copy = (BeatRules)MemberwiseClone();
            copy.CadenceBeats = (int[])CadenceBeats.Clone();
            return copy;
        }
    }
}
