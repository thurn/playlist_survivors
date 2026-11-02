namespace Champions.Sim
{
    /// <summary>The hero's numbers (Content/hero.json; design.md, "The Hero" and Decision 4).</summary>
    public sealed class HeroRules
    {
        public const string File = "hero.json";

        /// <summary>The walking speed, always (Decision 4): speeds never follow the tempo.</summary>
        public double SpeedMps = 6;

        /// <summary>Reads hero.json's object.</summary>
        public static HeroRules Read(JsonValue json, string path)
        {
            var r = new ContentReader(json, path);
            var rules = new HeroRules { SpeedMps = r.Number("speedMps") };
            r.End();
            if (!(rules.SpeedMps > 0)) throw ContentReader.Fail(path + ".speedMps", "positive");
            return rules;
        }

        public HeroRules Clone() => (HeroRules)MemberwiseClone();
    }
}
