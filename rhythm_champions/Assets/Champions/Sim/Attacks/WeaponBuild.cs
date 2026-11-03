using System;

namespace Champions.Sim
{
    /// <summary>Additive bonuses within each family. Only damage and strikes multiply direct output.</summary>
    public struct CombatStats
    {
        public double DamageBonus, StrikeBonus, AreaBonus, ProjectileBonus, DurationBonus;
        public double PierceBonus, SplitBonus, EchoBonus;
        public double DamageFactor => 1 + DamageBonus;
        public double StrikeFactor => 1 + StrikeBonus;
        public double AreaFactor => 1 + AreaBonus;
        public double RadiusFactor => Math.Sqrt(AreaFactor);
        public double DurationFactor => 1 + DurationBonus;
        public double ConnectedOutputFactor => DamageFactor * StrikeFactor;
        public void Add(StatFamily family, double bonus)
        {
            if (!CombatRules.Nonnegative(bonus)) throw new ArgumentOutOfRangeException(nameof(bonus));
            switch (family)
            {
                case StatFamily.Damage: DamageBonus += bonus; break;
                case StatFamily.Strikes: StrikeBonus += bonus; break;
                case StatFamily.Area: AreaBonus += bonus; break;
                case StatFamily.Projectiles: ProjectileBonus += bonus; break;
                case StatFamily.Duration: DurationBonus += bonus; break;
                case StatFamily.Pierce: PierceBonus += bonus; break;
                case StatFamily.Split: SplitBonus += bonus; break;
                case StatFamily.Echo: EchoBonus += bonus; break;
                default: throw new ArgumentException("not a weapon stat family", nameof(family));
            }
        }
    }

    /// <summary>State for one owned weapon, retained across form changes and holds.</summary>
    public sealed class WeaponState
    {
        public WeaponKind Kind { get; }
        public int Level { get; internal set; }
        /// <summary>Fractional extra-strike progress; cadence consumes it, holds do not.</summary>
        public double StrikeCarry { get; internal set; }
        public long ActivationSequence { get; internal set; }
        public long PulseSequence { get; internal set; }
        public bool PersistentActive { get; internal set; }
        public double PersistentUntil { get; internal set; } = double.NaN;
        internal WeaponState(WeaponKind kind, int level) { Kind = kind; Level = level; }
    }

    /// <summary>A six-weapon/six-support build. Set is for loadouts; progression calls Upgrade.</summary>
    public sealed class WeaponBuild
    {
        public CombatRules Rules { get; }
        readonly WeaponState[] weapons = new WeaponState[CombatRules.WeaponCount];
        readonly int[] supports = new int[CombatRules.SupportCount];
        public int WeaponCount { get; private set; }
        public int SupportCount { get; private set; }
        public WeaponBuild(CombatRules rules) { Rules = rules ?? throw new ArgumentNullException(nameof(rules)); }
        public WeaponState Weapon(WeaponKind kind) => weapons[CombatRules.WeaponIndex(kind)];
        public int Level(WeaponKind kind) => Weapon(kind)?.Level ?? 0;
        public int Level(SupportKind kind) => supports[CombatRules.SupportIndex(kind)];
        public void Set(WeaponKind kind, int level)
        {
            CombatRules.FormIndex(level); int i = CombatRules.WeaponIndex(kind);
            if (weapons[i] == null)
            {
                if (WeaponCount == CombatRules.WeaponSlots) throw new InvalidOperationException("six weapon slots occupied");
                weapons[i] = new WeaponState(kind, level); WeaponCount++;
            }
            else weapons[i].Level = level;
        }
        public void Set(SupportKind kind, int level)
        {
            var rule = Rules.Support(kind); int i = CombatRules.SupportIndex(kind);
            if (level < 1 || level > rule.MaxLevel) throw new ArgumentOutOfRangeException(nameof(level));
            if (supports[i] == 0)
            {
                if (SupportCount == CombatRules.SupportSlots) throw new InvalidOperationException("six support slots occupied");
                SupportCount++;
            }
            supports[i] = level;
        }
        public bool CanUpgrade(WeaponKind kind) => Level(kind) < CombatRules.MaxWeaponLevel && (Level(kind) > 0 || WeaponCount < CombatRules.WeaponSlots);
        public bool CanUpgrade(SupportKind kind) => Level(kind) < Rules.Support(kind).MaxLevel && (Level(kind) > 0 || SupportCount < CombatRules.SupportSlots && BenefitsBuild(kind));
        public void Upgrade(WeaponKind kind)
        {
            if (!CanUpgrade(kind)) throw new InvalidOperationException("weapon capped or slots full");
            Set(kind, Level(kind) + 1);
        }
        public void Upgrade(SupportKind kind)
        {
            if (!CanUpgrade(kind)) throw new InvalidOperationException("support capped, incompatible or slots full");
            Set(kind, Level(kind) + 1);
        }
        public bool BenefitsBuild(SupportKind kind)
        {
            var s = Rules.Support(kind);
            if (s.Requires == WeaponCapabilities.None) return true;
            foreach (var w in weapons)
                if (w != null && s.AppliesTo(Rules.Weapon(w.Kind).Form(w.Level).Capabilities)) return true;
            return Level(SupportKind.LandingBlast) > 0 && s.AppliesTo(LandingBlastCapabilities);
        }
        public const WeaponCapabilities LandingBlastCapabilities = WeaponCapabilities.Damage | WeaponCapabilities.Area;
        public CombatStats Stats(WeaponKind kind)
        {
            var w = Weapon(kind) ?? throw new InvalidOperationException("weapon not owned");
            var definition = Rules.Weapon(kind);
            var stats = Stats(definition.Form(w.Level).Capabilities);
            stats.Add(StatFamily.Damage, (w.Level - 1) * definition.DamagePerLevel);
            return stats;
        }
        public CombatStats Stats(WeaponCapabilities capabilities)
        {
            var stats = new CombatStats();
            for (int i = 0; i < supports.Length; i++)
            {
                var s = Rules.Support((SupportKind)i);
                if (supports[i] > 0 && s.AppliesTo(capabilities)) stats.Add(s.Family, supports[i] * s.PerLevel);
            }
            return stats;
        }
        /// <summary>Hero-wide bonuses, in their family's units (regen is health/s, armor is rating).</summary>
        public double HeroBonus(StatFamily family)
        {
            double bonus = 0;
            for (int i = 0; i < supports.Length; i++)
            {
                var s = Rules.Support((SupportKind)i);
                if (s.Requires == WeaponCapabilities.None && s.Family == family) bonus += supports[i] * s.PerLevel;
            }
            return bonus;
        }
        public const double AuraSeconds = 3;
        public double AuraUntil { get; private set; } = double.NegativeInfinity;
        public double AreaAt(double combatTime) => combatTime < AuraUntil ? 2 : 1;
        public void Empower(double combatTime = 0) => AuraUntil = combatTime + AuraSeconds;
        /// <summary>Every activation samples shared timed coverage; no weapon banks a hit.</summary>
        public double BeginActivation(WeaponKind kind, bool hasTarget, double combatTime = 0)
        {
            var w = Weapon(kind) ?? throw new InvalidOperationException("weapon not owned");
            if (!hasTarget) return 1;
            w.ActivationSequence++;
            return AreaAt(combatTime);
        }
    }
}
