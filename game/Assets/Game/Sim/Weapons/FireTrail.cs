using System;
using PS.Game.Sim.Content;

namespace PS.Game.Sim
{
    /// <summary>
    /// Fire Trail (unity-game.md, The Other Weapons; native's engine/combat.rs, WeaponKind::Trail): a
    /// burning wake on the hero's path. Native's hero leaves a path of past positions; on the grid the
    /// wake is the tiles the hero stood on. Each shot is a pulse: an attack event, then each tile the
    /// hero stood on during the last duration (the rank's times Duration) of run clock, skipping its
    /// latest <see cref="WeaponShape.SkipRecentS"/>, newest first, burns: a burn event at its centre,
    /// and every live enemy whose body touches the circle there (the shape's radius times the rank's
    /// Area and the hero's) takes the rank's power times Might, once a pulse. A hero standing still
    /// burns its own tile, as native's burns the samples under it.
    ///
    /// Run clock, not song time, so a freeze doesn't age the wake. The tiles are tracked from when the
    /// weapon is learned, at each pulse and tick's end; a tile is left at the run clock the move is seen.
    /// </summary>
    internal sealed class FireTrail : Weapon
    {
        /// <summary>The tiles remembered: more than any duration can cover at the fastest stride.</summary>
        const int Capacity = 64;

        /// <summary>A tile the hero stood on, from and to run clock times (to infinity while it stands there).</summary>
        struct Visit
        {
            public int TileX, TileY;
            public double X, Y, From, To;
        }

        readonly SimState state;
        readonly Horde horde;
        readonly RunRules run;
        readonly Visit[] visits = new Visit[Capacity];
        int head, count;
        readonly int[] reached;
        // The pulse each enemy slot was last burned in.
        readonly int[] burned;
        int pulse;

        public FireTrail(Arsenal arsenal, SimState state, Horde horde, RunRules run, WeaponDef def, int index)
            : base(arsenal, state, def, index)
        {
            if (def.Shape.Kind != ShapeKind.Trail) throw new ArgumentException(def.Id + " is not a trail");
            this.state = state;
            this.horde = horde;
            this.run = run;
            int slots = state.EnemyStore?.Length ?? 0;
            reached = new int[slots];
            burned = new int[slots];
            Track(state.Now);
        }

        ref Visit Latest => ref visits[(head + count - 1) % Capacity];

        /// <summary>How long a tile burns after the hero leaves it, in run clock.</summary>
        double Duration => Numbers.DurationS * Stats.Duration;

        internal override void Shoot(Simulation sim, double t)
        {
            Track(t);
            double now = run.ClockAt(t), radius = Radius;
            sim.Emit(SimEvent.HeroAttack(t, Index, ShapeKind.Trail, 0, t, t, 0, 0, false, radius));
            pulse++;
            double newest = now - Def.Shape.SkipRecentS, oldest = now - Duration;
            for (int k = count - 1; k >= 0; k--)
            {
                ref var v = ref visits[(head + k) % Capacity];
                if (v.To < oldest) break;
                if (v.From > newest || BurnedBefore(k, v, newest)) continue;
                sim.Emit(SimEvent.Burn(t, Index, v.X, v.Y, radius));
                Burn(sim, t, v.X, v.Y, radius);
            }
        }

        internal override void Tick(Simulation sim, double end) => Track(end);

        /// <summary>
        /// A revisited tile burns once a pulse: has a newer visit than <paramref name="k"/>, one begun by
        /// <paramref name="newest"/>, burned it already?
        /// </summary>
        bool BurnedBefore(int k, in Visit v, double newest)
        {
            for (int j = count - 1; j > k; j--)
            {
                ref var w = ref visits[(head + j) % Capacity];
                if (w.From <= newest && w.TileX == v.TileX && w.TileY == v.TileY) return true;
            }
            return false;
        }

        /// <summary>The hero's tile at <paramref name="t"/>: a new visit when it has moved on.</summary>
        void Track(double t)
        {
            var hero = state.Hero;
            if (count > 0 && Latest.TileX == hero.TileX && Latest.TileY == hero.TileY) return;
            double at = run.ClockAt(t);
            if (count > 0) Latest.To = at;
            if (count == Capacity)
            {
                head = (head + 1) % Capacity;
                count--;
            }
            count++;
            Latest = new Visit
            {
                TileX = hero.TileX, TileY = hero.TileY, X = hero.X, Y = hero.Y,
                From = at, To = double.PositiveInfinity,
            };
        }

        void Burn(Simulation sim, double t, double cx, double cy, double radius)
        {
            if (horde == null) return;
            int n = horde.InReach(t, cx, cy, radius, reached);
            var enemies = state.Enemies;
            for (int k = 0; k < n; k++)
            {
                int i = reached[k];
                if (burned[i] == pulse) continue;
                burned[i] = pulse;
                enemies[i].PositionAt(t, out double x, out double y);
                double dx = x - cx, dy = y - cy;
                double d = Math.Sqrt(dx * dx + dy * dy);
                horde.Damage(sim, i, t, Power, Index, WeaponHit.Strike, d > 1e-9 ? dx / d : 0, d > 1e-9 ? dy / d : 1,
                    Numbers.Knockback, Launch(1));
            }
        }
    }
}
