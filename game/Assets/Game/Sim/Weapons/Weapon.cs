using System;
using PS.Game.Sim.Content;

namespace PS.Game.Sim
{
    /// <summary>
    /// One of the hero's weapons in an arsenal slot (unity-game.md, The Other Weapons): its content,
    /// its rank and the hero's stats it reads, which the <see cref="Arsenal"/> keeps current.
    ///
    /// - An <b>interval</b> weapon is shot by the arsenal: once its interval times Cooldown has
    ///   elapsed on the run clock, on the first point of its grid (<see cref="WeaponDef.Grid"/>)
    ///   after that, or at once with no beat known ahead (<see cref="Arsenal"/>), calling
    ///   <see cref="Shoot"/>.
    /// - A <b>beat</b> weapon (<see cref="OnBeats"/>, the sword) keeps its own cadence through
    ///   <see cref="NextTime"/>, <see cref="Fire"/> and <see cref="Tick"/>; any weapon may use them
    ///   for exact times of its own (a missile's flight, an orbit's pass), which the arsenal fires
    ///   in its slot order.
    ///
    /// A weapon whose rules haven't landed is this class itself: it is ranked, timed and shot, and
    /// each shot is an attack event naming it (ps-msz.4.4: the client draws nothing for it yet).
    /// </summary>
    internal class Weapon
    {
        readonly Arsenal arsenal;
        readonly SimState state;

        public Weapon(Arsenal arsenal, SimState state, WeaponDef def, int index)
        {
            this.arsenal = arsenal;
            this.state = state;
            Def = def;
            Index = index;
        }

        public WeaponDef Def { get; }
        /// <summary>Its content index, the weapon in its events.</summary>
        public int Index { get; }
        public int Rank { get; internal set; } = 1;

        /// <summary>The hero's final stats (<see cref="Arsenal.Stats"/>).</summary>
        protected Stats Stats => arsenal.Stats;

        protected WeaponRank Numbers => Def.Rank(Rank);

        /// <summary>A hit's damage: the rank's power times Might.</summary>
        public double Power => Numbers.Power * Stats.Might;

        /// <summary>Its shape's radius at the rank's Area times the hero's.</summary>
        protected double Radius => Def.Shape.RadiusM * Numbers.Area * Stats.Area;

        /// <summary>A death launch's distance in metres: native's knockback distance times the weapon's, longer for a heavier hit.</summary>
        protected double Launch(double weight) => Horde.KnockbackM * Numbers.Knockback * weight;

        /// <summary>
        /// Nothing fires while the world is frozen, the hero has fallen or the run isn't playing (native's
        /// <c>weapons_active</c>: from the run clock's end, through the finale's wait for its blast).
        /// </summary>
        protected bool Holding => Rests(state);

        internal static bool Rests(SimState state) => state.Frozen || state.Hero.Fallen || state.Phase != RunPhase.Playing;

        /// <summary>Its rank's amount plus the hero's, at least 1 (native's <c>amount(stats)</c>): strikes, jumps or projectiles a shot.</summary>
        public int Amount => Math.Max(1, (int)Math.Floor(Numbers.Amount + Stats.Amount + 1e-9));

        /// <summary>
        /// Its interval on the run clock: the rank's times Cooldown, plus its duration times
        /// Duration for a weapon whose next shot waits for the last to end (King Bible; native's
        /// <c>interval_depends_on_duration</c>).
        /// </summary>
        public double Interval =>
            Numbers.IntervalS * Stats.Cooldown + (Numbers.IntervalDependsOnDuration ? Numbers.DurationS * Stats.Duration : 0);

        /// <summary>The weapon keeps its own cadence on the beat rather than the arsenal's shots.</summary>
        public virtual bool OnBeats => false;

        /// <summary>The next of the weapon's own exact times at or after <paramref name="floor"/>.</summary>
        internal virtual double NextTime(double floor) => double.PositiveInfinity;

        /// <summary>One of the weapon's own exact times (<see cref="NextTime"/>'s).</summary>
        internal virtual void Fire(Simulation sim, double t) => throw new InvalidOperationException(Def.Id + " has no times of its own");

        /// <summary>The per-tick system, at the tick's end.</summary>
        internal virtual void Tick(Simulation sim, double end) { }

        /// <summary>A shot at <paramref name="t"/>, on its grid point: the attack event, facing the hero's way, as far as its shape reaches.</summary>
        internal virtual void Shoot(Simulation sim, double t)
        {
            var d = state.Hero.Facing;
            double x = d == Direction.Right ? 1 : d == Direction.Left ? -1 : 0;
            double y = d == Direction.Up ? 1 : d == Direction.Down ? -1 : 0;
            sim.Emit(SimEvent.HeroAttack(t, Index, Def.Shape.Kind, 0, t, t, x, y, false, Radius));
        }
    }
}
