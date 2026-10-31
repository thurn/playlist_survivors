using System;
using PS.Game.Sim.Content;

namespace PS.Game.Sim
{
    /// <summary>
    /// Chain Lightning (unity-game.md, The Other Weapons; native's chain in engine/combat.rs, fired
    /// by its survivors' shot): bolts arcing between enemies.
    ///
    /// - A shot is a chain of <see cref="Weapon.Amount"/> jumps: the first from the hero on the
    ///   shot's grid point, each next on the sixteenth after the one before
    ///   (<see cref="BeatClock.SixteenthAfter"/>).
    /// - A jump goes to the live enemy the chain hasn't hit whose centre is nearest where it starts
    ///   (the hero, then the enemy before), closer than its range: <c>shape radius x rank area x
    ///   Area</c> for the first, <c>jump range x rank area x Area</c> after. Every enemy whose body
    ///   touches the circle of <c>hit radius x rank area x Area</c> around it, and that the chain
    ///   hasn't hit, takes <c>rank power x Might</c>, struck along the jump (knocked away from the hero,
    ///   as every blow is), and is hit for the rest of the chain. With nobody in range the chain ends.
    ///   A chain keeps its shot's power, ranges and hit radius (native's chain state), and knows the
    ///   enemies it hit by life (<see cref="EnemyState.Life"/>).
    /// - Jumps move, hold through a freeze and wait out a fall as Lightning Ring's strikes do
    ///   (<see cref="LightningRing"/>).
    /// </summary>
    internal sealed class ChainLightning : Weapon
    {
        /// <summary>Chains in flight: one a shot, each over within a beat or two, so a few at most.</summary>
        const int Capacity = 8;

        struct Chain
        {
            public bool Active;
            /// <summary>Its next jump's time, place in the chain and the chain's length.</summary>
            public double T;
            public int Jump, Jumps;
            /// <summary>Where the next jump starts: the hero, then the enemy last jumped to.</summary>
            public double X, Y;
            /// <summary>Its shot's power, first and later ranges, and hit radius.</summary>
            public double Damage, First, Range, Radius;
        }

        readonly BeatClock clock;
        readonly SimState state;
        readonly Horde horde;
        readonly Chain[] chains = new Chain[Capacity];
        /// <summary>Per chain, by enemy slot, the life of the enemy it hit there (0: none).</summary>
        readonly int[][] hit = new int[Capacity][];
        readonly int[] reached;

        public ChainLightning(Arsenal arsenal, WeaponDef def, int index, BeatClock clock, SimState state, Horde horde)
            : base(arsenal, state, def, index)
        {
            if (def.Shape.Kind != ShapeKind.Chain) throw new ArgumentException(def.Id + " is not a chain");
            this.clock = clock;
            this.state = state;
            this.horde = horde;
            int cap = state.EnemyStore.Length;
            reached = new int[cap];
            for (int c = 0; c < Capacity; c++) hit[c] = new int[cap];
        }

        double Area => Numbers.Area * Stats.Area;
        /// <summary>How far the first jump reaches from the hero, and each later one from the enemy before.</summary>
        public double FirstRange => Def.Shape.RadiusM * Area;
        public double JumpRange => Def.Shape.JumpRangeM * Area;
        /// <summary>The circle a jump hits around its enemy.</summary>
        public double HitRadius => Def.Shape.HitRadiusM * Area;

        /// <summary>The chain whose jump is next, the earliest (the lowest slot on ties), or -1.</summary>
        int Next()
        {
            int next = -1;
            for (int c = 0; c < Capacity; c++)
                if (chains[c].Active && (next < 0 || chains[c].T < chains[next].T)) next = c;
            return next;
        }

        internal override double NextTime(double floor)
        {
            int c = Next();
            return c < 0 || Holding ? double.PositiveInfinity : Math.Max(chains[c].T, floor);
        }

        /// <summary>A shot at <paramref name="t"/>: its attack event, and its chain's first jump.</summary>
        internal override void Shoot(Simulation sim, double t)
        {
            base.Shoot(sim, t);
            int c = 0;
            while (c < Capacity && chains[c].Active) c++;
            // Shots come at least half a second apart and a chain lasts a few sixteenths: a full set is never reached.
            if (c == Capacity) return;
            state.Hero.PositionAt(t, out double hx, out double hy);
            chains[c] = new Chain
            {
                Active = true, Jumps = Amount, X = hx, Y = hy, Damage = Power, First = FirstRange, Range = JumpRange, Radius = HitRadius,
            };
            Array.Clear(hit[c], 0, hit[c].Length);
            Jump(sim, c, t);
        }

        internal override void Fire(Simulation sim, double t)
        {
            int c = Next();
            double at = clock.SixteenthAtOrAfter(chains[c].T);
            if (at > t + 1e-7)
            {
                chains[c].T = at;
                return;
            }
            Jump(sim, c, t);
        }


        /// <summary>Chain <paramref name="c"/>'s next jump at <paramref name="t"/>, and the one after planned.</summary>
        void Jump(Simulation sim, int c, double t)
        {
            ref var ch = ref chains[c];
            int target = horde != null ? Nearest(c, t, ch.X, ch.Y, ch.Jump == 0 ? ch.First : ch.Range) : -1;
            if (target < 0)
            {
                ch.Active = false;
                return;
            }
            var enemies = state.EnemyStore;
            enemies[target].PositionAt(t, out double x, out double y);
            double radius = ch.Radius;
            sim.Emit(SimEvent.Bolt(t, Index, BoltKind.Jump, ch.Jump, ch.X, ch.Y, x, y, radius));
            double dx = x - ch.X, dy = y - ch.Y, d = Math.Sqrt(dx * dx + dy * dy);
            if (d > 1e-9)
            {
                dx /= d;
                dy /= d;
            }
            else
            {
                dx = 0;
                dy = 1;
            }
            double damage = ch.Damage, launch = Launch(1);
            var chainHit = hit[c];
            chainHit[target] = enemies[target].Life;
            horde.Damage(sim, target, t, damage, Index, WeaponHit.Bolt, dx, dy, Numbers.Knockback, launch);
            int n = horde.InReach(t, x, y, radius, reached);
            for (int k = 0; k < n; k++)
            {
                int i = reached[k];
                if (chainHit[i] == enemies[i].Life) continue;
                chainHit[i] = enemies[i].Life;
                horde.Damage(sim, i, t, damage, Index, WeaponHit.Bolt, dx, dy, Numbers.Knockback, launch);
            }
            ch.X = x;
            ch.Y = y;
            if (++ch.Jump < ch.Jumps) ch.T = clock.SixteenthAfter(t);
            else ch.Active = false;
        }

        /// <summary>The live enemy chain <paramref name="c"/> hasn't hit whose centre is nearest (x, y), closer than <paramref name="range"/>; -1 for none.</summary>
        int Nearest(int c, double t, double x, double y, double range)
        {
            var enemies = state.EnemyStore;
            var chainHit = hit[c];
            int best = -1;
            double bestD = range;
            for (int i = 0; i < enemies.Length; i++)
            {
                if (!enemies[i].Alive || chainHit[i] == enemies[i].Life) continue;
                enemies[i].PositionAt(t, out double ex, out double ey);
                double d = Math.Sqrt((ex - x) * (ex - x) + (ey - y) * (ey - y));
                if (d < bestD)
                {
                    bestD = d;
                    best = i;
                }
            }
            return best;
        }
    }
}
