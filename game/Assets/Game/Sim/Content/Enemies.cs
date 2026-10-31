namespace PS.Game.Sim.Content
{
    /// <summary>
    /// When a role steps: one character per main beat, cycling; 'x' steps and '.' holds. "x" steps
    /// on every main beat, "x." on every other one (unity-game.md, Enemies).
    /// </summary>
    public sealed class Gait
    {
        public string Pattern;

        public bool StepsOn(int beat) => Pattern[(beat % Pattern.Length + Pattern.Length) % Pattern.Length] == 'x';

        internal static Gait Read(ContentReader r)
        {
            var g = new Gait { Pattern = r.String("pattern") };
            if (g.Pattern.Length == 0 || g.Pattern.Trim('x', '.').Length != 0 || g.Pattern.IndexOf('x') < 0)
                throw r.Fail("a pattern of 'x' and '.' with at least one 'x'");
            return g;
        }
    }

    /// <summary>
    /// An enemy role (enemies.json): native's numbers with distances in metres. Health, power (contact
    /// damage), experience and knockback are in Vampire Survivors units.
    /// </summary>
    public sealed class EnemyRole
    {
        public string Id;
        /// <summary>The role's average speed when the hero strides at the reference period (5.19 m/s);
        /// travel scales with the hero's tempo-driven speed (unity-game.md, Speeds Follow the Tempo).</summary>
        public double RefSpeedMps;
        /// <summary>VS max HP. A level-scaled role's, and any role's spawned from a minute's bosses
        /// list, is multiplied by the hero's level at spawn (native's spawning.rs).</summary>
        public double Health;
        public double Power;
        public double Xp;
        public double RadiusM;
        /// <summary>VS knockback: how far a hit pushes it, as a multiplier of the weapon's.</summary>
        public double Knockback;
        public double MaxKnockback;
        public bool LevelScaled;
        /// <summary>Brutes may drop a floor chicken (<see cref="EasyRules.BruteChickenChance"/>).</summary>
        public bool Brute;
        public Gait Gait;

        internal static EnemyRole Read(ContentReader r)
        {
            var e = new EnemyRole
            {
                Id = r.String("id"),
                RefSpeedMps = r.Number("refSpeedMps"),
                Health = r.Number("health"),
                Power = r.Number("power"),
                Xp = r.Number("xp"),
                RadiusM = r.Number("radiusM"),
                Knockback = r.Number("knockback"),
                MaxKnockback = r.Number("maxKnockback"),
                LevelScaled = r.Bool("levelScaled"),
                Brute = r.Bool("brute"),
            };
            var gait = r.Object("gait");
            e.Gait = Gait.Read(gait);
            gait.End();
            if (e.RefSpeedMps < 0 || e.Health <= 0 || e.Power < 0 || e.Xp < 0 || e.RadiusM <= 0 || e.Knockback < 0
                || e.MaxKnockback < 0)
                throw r.Fail("a negative speed, power, experience or knockback, or a non-positive health or radius");
            return e;
        }
    }

    /// <summary>One of demo3d's monster models and its crystal gem colour (cast.json).</summary>
    public sealed class CrystalModel
    {
        public string Id;
        /// <summary>sRGB, "#rrggbb".</summary>
        public string Gem;

        internal static CrystalModel Read(ContentReader r) =>
            new CrystalModel { Id = r.String("id"), Gem = Colour(r, "gem") };

        internal static string Colour(ContentReader r, string key)
        {
            string c = r.String(key);
            bool ok = c.Length == 7 && c[0] == '#';
            for (int i = 1; ok && i < 7; i++) ok = System.Uri.IsHexDigit(c[i]);
            if (!ok) throw ContentReader.Fail(r.Child(key), "expected an sRGB colour \"#rrggbb\"");
            return c;
        }
    }

    /// <summary>How a role looks: a model, its scale and its crystal colour (unity-game.md, Enemies).</summary>
    public sealed class CastEntry
    {
        public string Role;
        public string Model;
        public double Scale;
        public string Crystal;

        internal static CastEntry Read(ContentReader r)
        {
            var c = new CastEntry
            {
                Role = r.String("role"),
                Model = r.String("model"),
                Scale = r.Number("scale"),
                Crystal = CrystalModel.Colour(r, "crystal"),
            };
            if (c.Scale <= 0) throw ContentReader.Fail(r.Child("scale"), "expected a positive scale");
            return c;
        }
    }
}
