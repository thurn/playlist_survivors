using System;
using PS.Game.Sim.Content;

namespace PS.Game.Sim
{
    /// <summary>
    /// The hero's health (unity-game.md, The Run and Enemies; native's survivors <c>hurt</c>). A
    /// contact lunge, or a boss's charge or slam, that lands takes <c>max(1, power * scale - armor)</c>, the scale being 1, or
    /// <see cref="EasyContactScale"/> on Easy, and leaves the hero unhurtable for progression.json's
    /// <c>invulnerableS</c> (native's 0.12 s lockout). At no health the hero falls: a
    /// <see cref="SimEventKind.Death"/> with enemy -1, "YOU FELL". A standing hero
    /// (<see cref="SimConfig.HeroStands"/>, for bots and evidence runs) keeps a tenth of its health,
    /// so its hits still land and show; an invulnerable one (<see cref="HeroState.Invulnerable"/>, the debug
    /// surfaces') takes none. A fall ends the run (<see cref="RunEnd.Death"/>).
    /// </summary>
    internal sealed class HeroHealth
    {
        public const double EasyContactScale = 0.5;
        public const double StandingShare = 0.1;
        /// <summary>progression.json's maxHp, for a sim without content (which no enemy can hurt).</summary>
        const double DefaultMaxHealth = 120;

        readonly HeroState hero;
        readonly double lockout;
        readonly double contactScale;
        readonly bool stands;
        double hurtableFrom = double.NegativeInfinity;

        /// <summary>What a lunge's damage loses: progression.json's base, then the arsenal's final Armor.</summary>
        public double Armor { get; set; }

        /// <summary>All the health healed this run (what each heal gained), which Soul Eater feeds on.</summary>
        public double Healed { get; private set; }

        public HeroHealth(HeroState hero, SimConfig config, GameContent content)
        {
            this.hero = hero;
            var def = content?.Progression.Hero;
            hero.MaxHealth = hero.Health = def?.Stats.MaxHp ?? DefaultMaxHealth;
            Armor = def?.Stats.Armor ?? 0;
            lockout = def?.InvulnerableS ?? 0;
            contactScale = config.Easy ? EasyContactScale : 1;
            stands = config.HeroStands;
        }

        /// <summary>
        /// An enemy's attack of <paramref name="power"/> (a contact lunge, or a boss's charge or
        /// slam) reaches the hero at <paramref name="t"/>, striking it along (dirX, dirY): a hit,
        /// with the damage it takes (0 while it can't be hurt), and its fall when that empties its
        /// health. A fallen hero takes none.
        /// </summary>
        public void Hurt(Simulation sim, double t, EnemyAttackKind kind, double power, double dirX, double dirY)
        {
            if (hero.Fallen) return;
            hero.PositionAt(t, out double hx, out double hy);
            double damage = 0;
            if (t >= hurtableFrom - 1e-9 && !hero.Invulnerable)
            {
                damage = Math.Max(1, power * contactScale - Armor);
                hurtableFrom = t + lockout;
                double floor = stands ? Math.Min(hero.Health, StandingShare * hero.MaxHealth) : 0;
                hero.Health = Math.Max(floor, hero.Health - damage);
            }
            sim.Emit(SimEvent.HeroHit(t, kind, damage, hx, hy, dirX, dirY));
            if (hero.Health > 1e-9) return;
            hero.Fallen = true;
            sim.Emit(SimEvent.Death(t, -1, -1, hx, hy, dirX, dirY, 0));
            sim.Run.End(sim, t, RunEnd.Death);
        }

        /// <summary>
        /// The hero heals <paramref name="amount"/> at <paramref name="t"/>, up to its maximum, and
        /// can't be hurt for <paramref name="immunity"/> after; returns the health it gained.
        /// </summary>
        public double Heal(double t, double amount, double immunity)
        {
            double before = hero.Health;
            hero.Health = Math.Min(hero.MaxHealth, hero.Health + amount);
            hurtableFrom = Math.Max(hurtableFrom, t + immunity);
            Healed += hero.Health - before;
            return hero.Health - before;
        }
    }
}
