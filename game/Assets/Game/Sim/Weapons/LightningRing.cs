using System;
using PS.Game.Sim.Content;

namespace PS.Game.Sim
{
    /// <summary>What a lightning bolt is, the kind in its bolt events.</summary>
    public enum BoltKind
    {
        /// <summary>Lightning Ring's strike from the sky on an enemy in view.</summary>
        Strike = 0,
        /// <summary>Thunder Loop's second strike on a strike's point (the evolution's layer).</summary>
        Return = 1,
        /// <summary>Chain Lightning's jump from the hero, or the enemy before, to the next enemy.</summary>
        Jump = 2,
    }

    /// <summary>
    /// Lightning Ring and its evolution Thunder Loop (unity-game.md, The Other Weapons; native's
    /// engine/lightning.rs): lightning striking from the sky.
    ///
    /// - A shot is a volley of <see cref="Weapon.Amount"/> strikes: the first on the shot's grid
    ///   point, each next on the sixteenth after the one before (<see cref="BeatClock.SixteenthAfter"/>).
    /// - Each strike lands on one of the live enemies in view, drawn at random, so one enemy may
    ///   take several. Native's view is its camera's; the game's zooms with the music, so in view
    ///   is the ground every zoom shows, <see cref="ViewShare"/> of the way to the camera's
    ///   footprint (<see cref="HordeConfig.CameraFootprint"/>), and no strike is lost off screen.
    ///   Every enemy whose body touches the circle of <c>shape radius x rank area x Area</c> there
    ///   takes <c>rank power x Might</c> and is knocked out from its centre. With nobody in view the strike lands nowhere and the volley
    ///   goes on.
    /// - Thunder Loop strikes each point again on the first sixteenth at or after
    ///   <see cref="ReturnS"/> later, even when its enemy has moved or died.
    /// - A strike planned on a beat's continuation moves to the sixteenth a beat committed since
    ///   says (<see cref="BeatClock.SixteenthAtOrAfter"/>), so strikes stay on the grid. Nothing
    ///   strikes while the world is frozen or the hero has fallen: the strikes held by a freeze go on
    ///   from the beat play resumes on.
    /// </summary>
    internal sealed class LightningRing : Weapon
    {
        /// <summary>Thunder Loop's second strike on a point comes this long after the first (native's).</summary>
        public const double ReturnS = 1.5;
        /// <summary>
        /// The share of the way to the footprint's edge that every zoom shows: the footprint is the
        /// loud view (34 m wide) grown by the focus's 4 m of slack a side, the quiet view 28 m wide.
        /// </summary>
        public const double ViewShare = 0.6;
        /// <summary>Strikes still to come: a volley's next and Thunder Loop's returns, a few volleys' worth at most.</summary>
        const int Capacity = 64;

        struct Pending
        {
            public double T;
            /// <summary>A volley's next strike: its place in the volley, of how many.</summary>
            public int Index, Amount;
            /// <summary>Thunder Loop's return to (X, Y).</summary>
            public bool Return;
            public double X, Y;
        }

        readonly BeatClock clock;
        readonly SimState state;
        readonly Horde horde;
        readonly Pending[] pending = new Pending[Capacity];
        int count;
        // The enemies in view, or in a strike's reach.
        readonly int[] found;

        public LightningRing(Arsenal arsenal, WeaponDef def, int index, BeatClock clock, SimState state, Horde horde)
            : base(arsenal, state, def, index)
        {
            if (def.Shape.Kind != ShapeKind.Strike) throw new ArgumentException(def.Id + " is not a strike");
            this.clock = clock;
            this.state = state;
            this.horde = horde;
            found = new int[state.EnemyStore.Length];
        }

        /// <summary>The next pending strike's slot, the earliest (the first planned on ties), or -1.</summary>
        int Next()
        {
            int next = -1;
            for (int k = 0; k < count; k++)
                if (next < 0 || pending[k].T < pending[next].T) next = k;
            return next;
        }

        internal override double NextTime(double floor)
        {
            int k = Next();
            return k < 0 || Holding ? double.PositiveInfinity : Math.Max(pending[k].T, floor);
        }

        /// <summary>A shot at <paramref name="t"/>: its attack event, and its volley's first strike.</summary>
        internal override void Shoot(Simulation sim, double t)
        {
            base.Shoot(sim, t);
            Volley(sim, t, 0, Amount);
        }

        internal override void Fire(Simulation sim, double t)
        {
            int k = Next();
            var p = pending[k];
            double at = clock.SixteenthAtOrAfter(p.T);
            if (at > t + 1e-7)
            {
                pending[k].T = at;
                return;
            }
            Array.Copy(pending, k + 1, pending, k, count - k - 1);
            count--;
            if (p.Return) Strike(sim, t, BoltKind.Return, p.Index, p.X, p.Y);
            else Volley(sim, t, p.Index, p.Amount);
        }


        /// <summary>Strike <paramref name="index"/> of a volley of <paramref name="amount"/>, and the next planned.</summary>
        void Volley(Simulation sim, double t, int index, int amount)
        {
            if (Target(sim, t, out double x, out double y))
            {
                Strike(sim, t, BoltKind.Strike, index, x, y);
                if (Def.IsEvolution) Add(new Pending { T = clock.SixteenthAtOrAfter(t + ReturnS), Index = index, Return = true, X = x, Y = y });
            }
            if (index + 1 < amount) Add(new Pending { T = clock.SixteenthAfter(t), Index = index + 1, Amount = amount });
        }

        void Add(in Pending p)
        {
            // Volleys come seconds apart and last a beat or two: a full set is never reached.
            if (count < Capacity) pending[count++] = p;
        }

        /// <summary>A live enemy in view at <paramref name="t"/>, drawn at random; false with none.</summary>
        bool Target(Simulation sim, double t, out double x, out double y)
        {
            x = y = 0;
            if (horde == null) return false;
            state.Hero.PositionAt(t, out double hx, out double hy);
            var enemies = state.EnemyStore;
            int n = 0;
            for (int i = 0; i < enemies.Length; i++)
            {
                if (!enemies[i].Alive) continue;
                enemies[i].PositionAt(t, out double ex, out double ey);
                if (horde.Spawner.InView(ex, ey, hx, hy, ViewShare)) found[n++] = i;
            }
            if (n == 0) return false;
            enemies[found[sim.Random.Below(n)]].PositionAt(t, out x, out y);
            return true;
        }

        /// <summary>A strike at (x, y): its bolt, then every enemy in its reach hit, knocked out from its centre.</summary>
        void Strike(Simulation sim, double t, BoltKind kind, int index, double x, double y)
        {
            double radius = Radius;
            sim.Emit(SimEvent.Bolt(t, Index, kind, index, x, y, x, y, radius));
            if (horde == null) return;
            state.Hero.PositionAt(t, out double hx, out double hy);
            double damage = Power, launch = Launch(1);
            var enemies = state.EnemyStore;
            int n = horde.InReach(t, x, y, radius, found);
            for (int k = 0; k < n; k++)
            {
                int i = found[k];
                enemies[i].PositionAt(t, out double ex, out double ey);
                double dx = ex - x, dy = ey - y;
                // One right under the bolt is thrown away from the hero.
                if (dx * dx + dy * dy < 1e-18)
                {
                    dx = x - hx;
                    dy = y - hy;
                }
                double d = Math.Sqrt(dx * dx + dy * dy);
                horde.Damage(sim, i, t, damage, Index, WeaponHit.Bolt, d > 1e-9 ? dx / d : 0, d > 1e-9 ? dy / d : 1,
                    Numbers.Knockback, launch);
            }
        }
    }
}
