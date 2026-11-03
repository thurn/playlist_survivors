namespace Champions.Sim
{
    /// <summary>Collected XP curve and world pickup tuning. HeldMaxS is used only by archived fixtures.</summary>
    public sealed class ProgressionRules
    {
        public const string File = "progression.json";
        public double PickupRadiusM = 3;
        public double PickupSpeedMps = 14;
        public int HealingEveryKills = 100;
        public double HealingShare = 0.1;

        /// <summary>20 XP for the first choice, then 9% growth: scripted collection samples are reported in progression evidence.</summary>
        public double[] Levels = { 20 };

        /// <summary>Past <see cref="Levels"/>, each level needs this many times the one before.</summary>
        public double Growth = 1.09;

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
            var pickups = r.Object("pickups");
            var rules = new ProgressionRules
            {
                Levels = experience.Numbers("levels"),
                Growth = experience.Number("growth"),
                HeldMaxS = held.Number("maxS"),
                PickupRadiusM = pickups.Number("radiusM"),
                PickupSpeedMps = pickups.Number("speedMps"),
                HealingEveryKills = pickups.Int("healingEveryKills"),
                HealingShare = pickups.Number("healingShare"),
            };
            pickups.End();
            if (!(rules.PickupRadiusM > 0 && rules.PickupRadiusM <= 12)) throw ContentReader.Fail(path + ".pickups.radiusM", "positive, at most 12");
            if (!(rules.PickupSpeedMps > 0)) throw ContentReader.Fail(path + ".pickups.speedMps", "positive");
            if (rules.HealingEveryKills < 1) throw ContentReader.Fail(path + ".pickups.healingEveryKills", "positive");
            if (!(rules.HealingShare > 0 && rules.HealingShare <= 1)) throw ContentReader.Fail(path + ".pickups.healingShare", "in (0, 1]");
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
