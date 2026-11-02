namespace Champions.Sim
{
    /// <summary>The hero's numbers (Content/hero.json; design.md, "The Hero", Decisions 4 and 15).</summary>
    public sealed class HeroRules
    {
        public const string File = "hero.json";

        /// <summary>The walking speed, always (Decision 4): speeds never follow the tempo.</summary>
        public double SpeedMps = 6;

        /// <summary>The hero's body: what structures stop and a dash's path covers.</summary>
        public double RadiusM = 0.4;

        /// <summary>Full health.</summary>
        public double Health = 100;

        /// <summary>After a hit lands, the hero can't be hurt for this long.</summary>
        public double HurtInvulnerableS = 0.5;

        /// <summary>
        /// How long after it lands a hit on the hero settles until an input has shown the sim's lead over
        /// the speakers (<see cref="HeroHealth"/>): a dash heard before the hit, taken after it, still dodges.
        /// </summary>
        public double HitSettleS = 0.1;

        /// <summary>The share of full health a level-up heals, and a boss kill (Decision 15).</summary>
        public double LevelUpHeal = 0.25;
        public double BossKillHeal = 1.0;

        /// <summary>Reads hero.json's object.</summary>
        public static HeroRules Read(JsonValue json, string path)
        {
            var r = new ContentReader(json, path);
            var rules = new HeroRules
            {
                SpeedMps = r.Number("speedMps"),
                RadiusM = r.Number("radiusM"),
                Health = r.Number("health"),
                HurtInvulnerableS = r.Number("hurtInvulnerableS"),
                HitSettleS = r.Number("hitSettleS"),
            };
            var heals = r.Object("heals");
            rules.LevelUpHeal = heals.Number("levelUp");
            rules.BossKillHeal = heals.Number("bossKill");
            heals.End();
            r.End();
            if (!(rules.SpeedMps > 0)) throw ContentReader.Fail(path + ".speedMps", "positive");
            if (!(rules.RadiusM > 0)) throw ContentReader.Fail(path + ".radiusM", "positive");
            if (!(rules.Health > 0)) throw ContentReader.Fail(path + ".health", "positive");
            if (!(rules.HurtInvulnerableS >= 0)) throw ContentReader.Fail(path + ".hurtInvulnerableS", "at least 0");
            if (!(rules.HitSettleS >= 0)) throw ContentReader.Fail(path + ".hitSettleS", "at least 0");
            if (!(rules.LevelUpHeal >= 0 && rules.LevelUpHeal <= 1)) throw ContentReader.Fail(path + ".heals.levelUp", "0 to 1");
            if (!(rules.BossKillHeal >= 0 && rules.BossKillHeal <= 1)) throw ContentReader.Fail(path + ".heals.bossKill", "0 to 1");
            return rules;
        }

        public HeroRules Clone() => (HeroRules)MemberwiseClone();
    }
}
