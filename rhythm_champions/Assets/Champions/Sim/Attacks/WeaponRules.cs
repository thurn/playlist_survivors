using System;
using System.Globalization;
using System.Text;

namespace Champions.Sim
{
    // These IDs are independent of the archived AttackKind/ModKind wire values.
    public enum WeaponKind : byte { Sword, Hammer, Spear, Bow, OrbitBlades, Lightning, EmberField, Sentinel }
    public enum SupportKind : byte { Might, Tempo, Reach, Multishot, Persistence, Pierce, Split, Echo, Attraction, Stride, Vitality, Armor, Regeneration, LandingBlast }

    [Flags]
    public enum WeaponCapabilities
    {
        None = 0, Damage = 1, Automatic = 2, Area = 4, Projectile = 8,
        Duration = 16, Pierce = 32, Split = 64, Echo = 128,
        Melee = 256, Orbit = 512, Ground = 1024, Chain = 2048, Summon = 4096,
    }

    public enum StatFamily { None, Damage, Strikes, Area, Projectiles, Duration, Pierce, Split, Echo, Attraction, Stride, Vitality, Armor, Regeneration, LandingBlast }
    /// <summary>One target hit per activation, independent strike, or authored persistent pulse.</summary>
    public enum HitWindow { Activation, Strike, Pulse }

    public sealed class WeaponForm
    {
        public readonly int StartLevel;
        public readonly WeaponCapabilities Capabilities;
        public readonly HitWindow HitWindow;
        public bool Persistent => HitWindow == HitWindow.Pulse;
        public WeaponForm(int startLevel, WeaponCapabilities capabilities, HitWindow hitWindow)
        { StartLevel = startLevel; Capabilities = capabilities; HitWindow = hitWindow; }
    }

    public sealed class WeaponDefinition
    {
        public readonly WeaponKind Kind;
        public readonly double DamagePerLevel;
        readonly WeaponForm[] forms;
        public WeaponDefinition(WeaponKind kind, double damagePerLevel, WeaponForm[] forms)
        {
            if (forms == null || forms.Length != 4) throw new ContentException(CombatRules.File + ": four weapon forms required");
            Kind = kind; DamagePerLevel = damagePerLevel; this.forms = (WeaponForm[])forms.Clone();
        }
        public WeaponForm Form(int level) => forms[CombatRules.FormIndex(level)];
        public string FormId(int level) => CombatRules.Id(Kind) + ":form" + (CombatRules.FormIndex(level) + 1);
    }

    public sealed class SupportDefinition
    {
        public readonly SupportKind Kind;
        public readonly int MaxLevel;
        public readonly WeaponCapabilities Requires;
        public readonly StatFamily Family;
        public readonly double PerLevel;
        public SupportDefinition(SupportKind kind, int maxLevel, WeaponCapabilities requires, StatFamily family, double perLevel)
        { Kind = kind; MaxLevel = maxLevel; Requires = requires; Family = family; PerLevel = perLevel; }
        // None denotes a hero-wide support, rather than a weapon stat.
        public bool AppliesTo(WeaponCapabilities caps) => Requires != WeaponCapabilities.None && (caps & Requires) == Requires;
    }

    /// <summary>The survivor combat schema. Immutable and shared safely by content, config and runs.</summary>
    public sealed class CombatRules
    {
        public const string File = "combat.json";
        public const int WeaponSlots = 6, SupportSlots = 6, MaxWeaponLevel = 8;
        public const int WeaponCount = 8, SupportCount = 14;
        static readonly string[] weaponIds = { "sword", "hammer", "spear", "bow", "orbitBlades", "lightning", "emberField", "sentinel" };
        static readonly string[] supportIds = { "might", "tempo", "reach", "multishot", "persistence", "pierce", "split", "echo", "attraction", "stride", "vitality", "armor", "regeneration", "landingBlast" };
        readonly WeaponDefinition[] weapons;
        readonly SupportDefinition[] supports;
        public CombatRules(WeaponDefinition[] weapons, SupportDefinition[] supports)
        {
            this.weapons = (WeaponDefinition[])weapons.Clone();
            this.supports = (SupportDefinition[])supports.Clone();
            Validate();
        }
        public static CombatRules Default { get; } = CreateDefault();
        static CombatRules CreateDefault() => new CombatRules(new[]
        {
            DefaultWeapon(WeaponKind.Sword, WeaponCapabilities.Melee | WeaponCapabilities.Echo, HitWindow.Strike),
            DefaultWeapon(WeaponKind.Hammer, WeaponCapabilities.Melee | WeaponCapabilities.Echo, HitWindow.Strike),
            DefaultWeapon(WeaponKind.Spear, WeaponCapabilities.Melee | WeaponCapabilities.Projectile | WeaponCapabilities.Pierce | WeaponCapabilities.Split | WeaponCapabilities.Echo, HitWindow.Strike),
            DefaultWeapon(WeaponKind.Bow, WeaponCapabilities.Projectile | WeaponCapabilities.Pierce | WeaponCapabilities.Split | WeaponCapabilities.Echo, HitWindow.Activation),
            DefaultWeapon(WeaponKind.OrbitBlades, WeaponCapabilities.Orbit | WeaponCapabilities.Echo, HitWindow.Pulse),
            DefaultWeapon(WeaponKind.Lightning, WeaponCapabilities.Chain | WeaponCapabilities.Projectile | WeaponCapabilities.Pierce | WeaponCapabilities.Split | WeaponCapabilities.Echo, HitWindow.Activation),
            DefaultWeapon(WeaponKind.EmberField, WeaponCapabilities.Ground | WeaponCapabilities.Duration | WeaponCapabilities.Echo, HitWindow.Pulse),
            DefaultWeapon(WeaponKind.Sentinel, WeaponCapabilities.Summon | WeaponCapabilities.Duration | WeaponCapabilities.Echo, HitWindow.Pulse),
        }, new[]
        {
            new SupportDefinition(SupportKind.Might, 5, WeaponCapabilities.Damage, StatFamily.Damage, 0.1),
            new SupportDefinition(SupportKind.Tempo, 5, WeaponCapabilities.Automatic, StatFamily.Strikes, 0.1),
            new SupportDefinition(SupportKind.Reach, 5, WeaponCapabilities.Area, StatFamily.Area, 0.1),
            new SupportDefinition(SupportKind.Multishot, 5, WeaponCapabilities.Projectile, StatFamily.Projectiles, 1),
            new SupportDefinition(SupportKind.Persistence, 5, WeaponCapabilities.Duration, StatFamily.Duration, 0.15),
            new SupportDefinition(SupportKind.Pierce, 5, WeaponCapabilities.Pierce, StatFamily.Pierce, 1),
            new SupportDefinition(SupportKind.Split, 5, WeaponCapabilities.Split, StatFamily.Split, 0.1),
            new SupportDefinition(SupportKind.Echo, 5, WeaponCapabilities.Echo, StatFamily.Echo, 0.1),
            new SupportDefinition(SupportKind.Attraction, 5, WeaponCapabilities.None, StatFamily.Attraction, 0.2),
            new SupportDefinition(SupportKind.Stride, 5, WeaponCapabilities.None, StatFamily.Stride, 0.05),
            new SupportDefinition(SupportKind.Vitality, 5, WeaponCapabilities.None, StatFamily.Vitality, 0.1),
            new SupportDefinition(SupportKind.Armor, 5, WeaponCapabilities.None, StatFamily.Armor, 0.1),
            new SupportDefinition(SupportKind.Regeneration, 5, WeaponCapabilities.None, StatFamily.Regeneration, 0.5),
            new SupportDefinition(SupportKind.LandingBlast, 5, WeaponCapabilities.None, StatFamily.LandingBlast, 0.2),
        });
        static WeaponDefinition DefaultWeapon(WeaponKind kind, WeaponCapabilities caps, HitWindow window)
        {
            caps |= WeaponCapabilities.Damage | WeaponCapabilities.Automatic | WeaponCapabilities.Area;
            return new WeaponDefinition(kind, 0.1, new[]
            {
                new WeaponForm(1, caps, window), new WeaponForm(3, caps, window),
                new WeaponForm(5, caps, window), new WeaponForm(8, caps, window),
            });
        }
        public static string Id(WeaponKind kind) => weaponIds[WeaponIndex(kind)];
        public static string Id(SupportKind kind) => supportIds[SupportIndex(kind)];
        public static bool TryWeapon(string id, out WeaponKind kind)
        { int i = Array.IndexOf(weaponIds, id); kind = (WeaponKind)Math.Max(0, i); return i >= 0; }
        public static bool TrySupport(string id, out SupportKind kind)
        { int i = Array.IndexOf(supportIds, id); kind = (SupportKind)Math.Max(0, i); return i >= 0; }
        internal static int WeaponIndex(WeaponKind kind)
        { int i = (int)kind; if (i < 0 || i >= WeaponCount) throw new ArgumentOutOfRangeException(nameof(kind)); return i; }
        internal static int SupportIndex(SupportKind kind)
        { int i = (int)kind; if (i < 0 || i >= SupportCount) throw new ArgumentOutOfRangeException(nameof(kind)); return i; }
        public WeaponDefinition Weapon(WeaponKind kind) => weapons[WeaponIndex(kind)];
        public SupportDefinition Support(SupportKind kind) => supports[SupportIndex(kind)];
        public static int FormIndex(int level)
        {
            if (level < 1 || level > MaxWeaponLevel) throw new ArgumentOutOfRangeException(nameof(level));
            return level >= 8 ? 3 : level >= 5 ? 2 : level >= 3 ? 1 : 0;
        }
        static bool KnownCaps(WeaponCapabilities caps) => ((int)caps & ~8191) == 0;
        static bool WeaponFamily(StatFamily family) => family >= StatFamily.Damage && family <= StatFamily.Echo;
        void Validate()
        {
            if (weapons.Length != WeaponCount || supports.Length != SupportCount) throw new ContentException(File + ": all reserved weapon/support IDs required");
            int[] boundaries = { 1, 3, 5, 8 };
            for (int i = 0; i < WeaponCount; i++)
            {
                var w = weapons[i];
                if (w == null || (int)w.Kind != i || !Nonnegative(w.DamagePerLevel)) throw new ContentException(File + ": invalid weapon order or level damage");
                WeaponCapabilities prior = WeaponCapabilities.Damage | WeaponCapabilities.Automatic | WeaponCapabilities.Area;
                for (int f = 0; f < 4; f++)
                {
                    var form = w.Form(boundaries[f]);
                    if (form == null || form.StartLevel != boundaries[f] || (form.Capabilities & prior) != prior || !KnownCaps(form.Capabilities) || !Enum.IsDefined(typeof(HitWindow), form.HitWindow))
                        throw new ContentException(File + ": forms at 1/3/5/8 must retain all prior capabilities");
                    prior = form.Capabilities;
                }
            }
            for (int i = 0; i < SupportCount; i++)
            {
                var s = supports[i];
                if (s == null || (int)s.Kind != i || s.Family == StatFamily.None || s.MaxLevel < 1 || s.MaxLevel > 255 || !Nonnegative(s.PerLevel) || !Enum.IsDefined(typeof(StatFamily), s.Family) || !KnownCaps(s.Requires) ||
                    (s.Requires != WeaponCapabilities.None) != WeaponFamily(s.Family))
                    throw new ContentException(File + ": invalid support order, cap or bonus");
            }
        }
        internal static bool Nonnegative(double n) => n >= 0 && !double.IsInfinity(n);
        static T Named<T>(string text, string path) where T : struct
        {
            if (text.Length > 0 && char.IsLetter(text[0]) && Enum.TryParse<T>(text, false, out var value) && Enum.IsDefined(typeof(T), value)) return value;
            throw ContentReader.Fail(path, "unknown " + typeof(T).Name);
        }
        static WeaponCapabilities Caps(ContentReader r, string key)
        {
            var caps = WeaponCapabilities.None;
            foreach (string name in r.Strings(key)) caps |= Named<WeaponCapabilities>(name, r.Child(key));
            return caps;
        }
        public static CombatRules Read(JsonValue value, string path = File)
        {
            var r = new ContentReader(value, path);
            var weapons = r.Objects("weapons", w =>
            {
                string id = w.String("id"); int index = Array.IndexOf(weaponIds, id);
                if (index < 0) throw w.Fail("unknown weapon ID");
                double damage = w.Number("damagePerLevel");
                var forms = w.Objects("forms", f => new WeaponForm(f.Int("startLevel"), Caps(f, "capabilities"), Named<HitWindow>(f.String("hitWindow"), f.Path)));
                if (forms.Length != 4) throw w.Fail("four weapon forms required");
                return new WeaponDefinition((WeaponKind)index, damage, forms);
            });
            var supports = r.Objects("supports", s =>
            {
                string id = s.String("id"); int index = Array.IndexOf(supportIds, id);
                if (index < 0) throw s.Fail("unknown support ID");
                return new SupportDefinition((SupportKind)index, s.Int("maxLevel"), Caps(s, "requires"), Named<StatFamily>(s.String("family"), s.Path), s.Number("perLevel"));
            });
            r.End();
            return new CombatRules(weapons, supports);
        }
        /// <summary>Canonical JSON in reserved ID order, invariant across locales and runs.</summary>
        public string ToJson()
        {
            var b = new StringBuilder("{\"weapons\":[");
            for (int i = 0; i < WeaponCount; i++)
            {
                if (i > 0) b.Append(',');
                var w = weapons[i];
                b.Append("{\"id\":\"").Append(Id(w.Kind)).Append("\",\"damagePerLevel\":").Append(w.DamagePerLevel.ToString("R", CultureInfo.InvariantCulture)).Append(",\"forms\":[");
                for (int f = 0; f < 4; f++)
                {
                    if (f > 0) b.Append(',');
                    var form = w.Form(new[] { 1, 3, 5, 8 }[f]);
                    b.Append("{\"startLevel\":").Append(form.StartLevel).Append(",\"capabilities\":"); WriteCaps(b, form.Capabilities);
                    b.Append(",\"hitWindow\":\"").Append(form.HitWindow).Append("\"}");
                }
                b.Append("]}");
            }
            b.Append("],\"supports\":[");
            for (int i = 0; i < SupportCount; i++)
            {
                if (i > 0) b.Append(','); var s = supports[i];
                b.Append("{\"id\":\"").Append(Id(s.Kind)).Append("\",\"maxLevel\":").Append(s.MaxLevel).Append(",\"requires\":"); WriteCaps(b, s.Requires);
                b.Append(",\"family\":\"").Append(s.Family).Append("\",\"perLevel\":").Append(s.PerLevel.ToString("R", CultureInfo.InvariantCulture)).Append('}');
            }
            return b.Append("]}").ToString();
        }
        static void WriteCaps(StringBuilder b, WeaponCapabilities caps)
        {
            b.Append('['); bool first = true;
            foreach (WeaponCapabilities c in Enum.GetValues(typeof(WeaponCapabilities)))
                if (c != WeaponCapabilities.None && (caps & c) != 0)
                { if (!first) b.Append(','); first = false; b.Append('"').Append(c).Append('"'); }
            b.Append(']');
        }
    }
}
