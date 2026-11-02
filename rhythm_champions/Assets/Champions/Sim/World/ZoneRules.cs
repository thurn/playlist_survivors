namespace Champions.Sim
{
    /// <summary>
    /// The zone keys' rules (Content/zones.json; design.md, The World &gt; Zones, Decision 23): the floor's colour
    /// family moves on to the next key at strong section changes, or after <see cref="EveryS"/> without one, and
    /// turns warm for good at the dragon's entrance. The keys' colours are the palette's; the sim only counts them.
    /// </summary>
    public sealed class ZoneRules
    {
        public const string File = "zones.json";

        /// <summary>The level's zone keys: two cool, a violet, a dim warm.</summary>
        public int Keys = 4;

        /// <summary>The keys play moves through, in order (the warm one is the dragon's alone).</summary>
        public int[] Cycle = { 0, 1, 2 };

        /// <summary>The dragon's key: the floor turns to it at the dragon's entrance and stays.</summary>
        public int Warm = 3;

        /// <summary>A section change this strong or stronger moves the key on (the analyzer's novelty; 0: every change it reports).</summary>
        public double SectionMinStrength = 0;

        /// <summary>At least this much run clock between two keys a section change brings, so a busy song doesn't flicker.</summary>
        public double SectionMinGapS = 30;

        /// <summary>Without a section change for this much run clock, the key moves on anyway.</summary>
        public double EveryS = 180;

        /// <summary>With no reliable beat, a change crossfades over this long at once (a beat's fallback, as telegraphs').</summary>
        public double BeatlessFadeS = 0.5;

        /// <summary>Reads zones.json's object.</summary>
        public static ZoneRules Read(JsonValue json, string path)
        {
            var r = new ContentReader(json, path);
            var rules = new ZoneRules
            {
                Keys = r.Int("keys"),
                Cycle = r.Ints("cycle"),
                Warm = r.Int("warm"),
                EveryS = r.Number("everyS"),
                BeatlessFadeS = r.Number("beatlessFadeS"),
            };
            var section = r.Object("section");
            rules.SectionMinStrength = section.Number("minStrength");
            rules.SectionMinGapS = section.Number("minGapS");
            section.End();
            r.End();
            if (rules.Keys < 1) throw ContentReader.Fail(path + ".keys", "at least 1");
            if (rules.Cycle.Length < 1) throw ContentReader.Fail(path + ".cycle", "at least one key");
            foreach (int k in rules.Cycle)
                if (k < 0 || k >= rules.Keys) throw ContentReader.Fail(path + ".cycle", $"keys 0 to {rules.Keys - 1}");
            if (rules.Warm < 0 || rules.Warm >= rules.Keys) throw ContentReader.Fail(path + ".warm", $"a key 0 to {rules.Keys - 1}");
            if (!(rules.EveryS > 0)) throw ContentReader.Fail(path + ".everyS", "positive");
            if (!(rules.BeatlessFadeS >= 0)) throw ContentReader.Fail(path + ".beatlessFadeS", "at least 0");
            if (!(rules.SectionMinGapS >= 0)) throw ContentReader.Fail(path + ".section.minGapS", "at least 0");
            return rules;
        }

        public ZoneRules Clone()
        {
            var c = (ZoneRules)MemberwiseClone();
            c.Cycle = (int[])Cycle.Clone();
            return c;
        }
    }
}
