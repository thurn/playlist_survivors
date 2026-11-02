namespace Champions.Sim
{
    /// <summary>
    /// The progression's numbers (Content/progression.json; design.md, "Level-ups and Treasure", Decision 10):
    /// the experience curve and how long a level waits for its quiet moment. Each kill's experience is its type's
    /// (<see cref="EnemyType.Experience"/> in enemies.json); the quiet moments are the beat services'
    /// (<see cref="BeatRules.SilenceS"/>, <see cref="BeatRules.BeatlessS"/>); the level-up's heal is the hero's
    /// (<see cref="HeroRules.LevelUpHeal"/>).
    /// </summary>
    public sealed class ProgressionRules
    {
        public const string File = "progression.json";

        /// <summary>
        /// The experience from each level to the next, from level 1: tuned so a good build levels about every two
        /// minutes from about 1:30 (design.md, "Experience"), killing faster levels faster.
        /// </summary>
        public double[] Levels = { 150, 220, 350, 700, 1200, 1800, 2500 };

        /// <summary>Past <see cref="Levels"/>, each level needs this many times the one before.</summary>
        public double Growth = 1.35;

        /// <summary>A held level shows at most this long after it was reached (run clock), then on the next downbeat.</summary>
        public double HeldMaxS = 60;

        /// <summary>The experience from level <paramref name="level"/> (1 up) to the next.</summary>
        public double Needed(int level)
        {
            if (level <= Levels.Length) return Levels[level - 1];
            double need = Levels[Levels.Length - 1];
            for (int l = Levels.Length; l < level; l++) need *= Growth;
            return need;
        }

        public ProgressionRules Clone()
        {
            var c = (ProgressionRules)MemberwiseClone();
            c.Levels = (double[])Levels.Clone();
            return c;
        }

        /// <summary>Reads progression.json's object.</summary>
        public static ProgressionRules Read(JsonValue json, string path)
        {
            var r = new ContentReader(json, path);
            var experience = r.Object("experience");
            var held = r.Object("held");
            var rules = new ProgressionRules
            {
                Levels = experience.Numbers("levels"),
                Growth = experience.Number("growth"),
                HeldMaxS = held.Number("maxS"),
            };
            experience.End();
            held.End();
            r.End();
            if (rules.Levels.Length == 0) throw ContentReader.Fail(path + ".experience.levels", "at least one level");
            foreach (double l in rules.Levels)
                if (!(l > 0)) throw ContentReader.Fail(path + ".experience.levels", "positive");
            if (!(rules.Growth >= 1)) throw ContentReader.Fail(path + ".experience.growth", "at least 1");
            if (!(rules.HeldMaxS > 0)) throw ContentReader.Fail(path + ".held.maxS", "positive");
            return rules;
        }
    }
}
