using System;
using System.Collections.Generic;
using PS.Game.Sim.Content;

namespace PS.Game.Sim
{
    /// <summary>
    /// King Bible and its evolution, Unholy Vespers (unity-game.md, The Other Weapons; native's
    /// engine/bible.rs and survivors/bible.rs): crystal shards orbiting the hero, shot on beats by
    /// the arsenal.
    ///
    /// - <b>A shot</b> starts a ring of <c>max(1, floor(rank amount + Amount))</c> blades, evenly
    ///   spaced, for <c>rank duration * Duration</c>; the arsenal's interval already waits for it
    ///   (King Bible's interval depends on its duration). The ring's radius is
    ///   <c>orbit radius * min(rank area * Area, area cap)</c>, and a blade's body
    ///   <c>shape radius * (1 + slope * rank area * Area)</c>, growing in over <see cref="GrowS"/>.
    /// - <b>Speed</b>: <c>rad per s * rank speed * Speed</c>, snapped to the beat when near it
    ///   (<see cref="OrbitSpeed"/>, native's <c>bible_orbit_speed</c>), the beat being the latest
    ///   committed beat's period at the shot.
    /// - <b>Hits</b>: each tick, every blade's path since the last (in pieces of at most
    ///   <see cref="MaxStepRad"/>, around the hero as it moves) hits each live enemy whose body it
    ///   touches, <c>rank power * Might</c>, struck along the blade's way (knocked away from the hero,
    ///   as every blow is); a blade hits an enemy once until its memory clears, every rank hit delay
    ///   from the ring's start. The memory knows enemies by life (<see cref="EnemyState.Life"/>).
    /// - <b>Its end</b>: at its duration it stops hitting and its blades shrink away over
    ///   <see cref="FadeS"/>. A freeze or the hero's fall cuts it short then.
    ///
    /// Each ring is an <see cref="SimEvent.Orbit"/> event, again with its new end when cut short.
    ///
    /// <b>Unholy Vespers</b>, the evolution, keeps a permanent ring (native's bible.rs bridges each
    /// cycle into the next): its newest ring has no end (infinity) until the next shot starts one, which
    /// ends it at that shot, or at its own duration when that is later. A freeze still cuts it short.
    /// Its blades break the structures they touch within <see cref="Accents.Window"/> of an accented
    /// beat (<see cref="Weapon.BreakOnAccent"/>).
    /// </summary>
    internal sealed class KingBible : Weapon
    {
        public const double GrowS = OrbitEvent.GrowS, FadeS = OrbitEvent.FadeS;
        /// <summary>A tick's sweep is cut into pieces of at most this angle, so a chord never cuts across the ring (native's pi / 32).</summary>
        public const double MaxStepRad = Math.PI / 32;
        /// <summary>Snapping keeps the speed within this share of its own (native's 15%).</summary>
        public const double SnapShare = 0.15;

        sealed class Ring
        {
            public bool Active;
            public int Id, Blades;
            public double Born, End, Omega, Radius, Blade, Last, ClearAt;
            public bool Snapped;
            /// <summary>Each blade's hit memory: by enemy slot, the life of the enemy it hit there (0: none); kept for the next ring that takes this one.</summary>
            public int[][] Hit = new int[0][];
        }

        readonly SimState state;
        readonly BeatClock clock;
        readonly Horde horde;
        readonly EnemyRole[] roles;
        /// <summary>The rings showing, and spent ones for the next shots to take, so stepping allocates nothing once warm.</summary>
        readonly List<Ring> rings = new List<Ring>();
        int nextId;

        public KingBible(Arsenal arsenal, SimState state, WeaponDef def, int index, GameContent content, BeatClock clock, Horde horde)
            : base(arsenal, state, def, index)
        {
            if (def.Shape.Kind != ShapeKind.Orbit) throw new ArgumentException(def.Id + " is not an orbit weapon");
            this.state = state;
            this.clock = clock;
            this.horde = horde;
            roles = content.Roles;
        }

        public int Blades => Amount;

        public double DurationS => Numbers.DurationS * Stats.Duration;

        double Area => Numbers.Area * Stats.Area;

        public double RingRadius => Def.Shape.OrbitRadiusM * Math.Min(Area, Def.Shape.OrbitAreaCap);

        public double BladeRadius => Def.Shape.RadiusM * (1 + Def.Shape.BladeAreaSlope * Area);

        /// <summary>The rings still showing, for tests.</summary>
        internal int Rings
        {
            get
            {
                int n = 0;
                foreach (var ring in rings)
                    if (ring.Active) n++;
                return n;
            }
        }

        /// <summary>
        /// An orbit's angular speed for raw speed <paramref name="speed"/> (radians a second) and
        /// <paramref name="blades"/> evenly spaced blades on a beat of <paramref name="period"/>
        /// seconds, and whether it is snapped (native's <c>bible_orbit_speed</c>): the nearest speed
        /// at which every blade stands on a quarter turn on beats (integer quarter turns a beat for
        /// counts dividing four; otherwise a quarter may span N beats, 4N divisible by the count),
        /// when within <see cref="SnapShare"/> of its own; else its own.
        /// </summary>
        public static double OrbitSpeed(double raw, double period, int blades, out bool snapped)
        {
            snapped = false;
            raw = Math.Max(0, raw);
            if (raw <= 0 || double.IsNaN(period) || double.IsInfinity(period) || period <= 0 || blades <= 0) return raw;
            int gcd = blades % 4 == 0 ? 4 : blades % 4 == 2 ? 2 : 1;
            double stride = blades / gcd;
            double quarter = Math.PI / 2;
            double n = quarter / (raw * period) / stride;
            double slow = quarter / (Math.Max(1, Math.Ceiling(n)) * stride * period);
            double fast = quarter / (Math.Max(1, Math.Floor(n)) * stride * period);
            double best = Math.Abs(slow - raw) < Math.Abs(fast - raw) ? slow : fast;
            if (4 % blades == 0)
            {
                double k = Math.Max(1, Math.Round(raw * period / quarter, MidpointRounding.AwayFromZero));
                double direct = k * quarter / period;
                if (Math.Abs(direct - raw) < Math.Abs(best - raw)) best = direct;
            }
            if (Math.Abs(best / raw - 1) > SnapShare + 1e-12) return raw;
            snapped = true;
            return best;
        }

        internal override void Shoot(Simulation sim, double t)
        {
            int blades = Blades;
            int beat = clock.LastBeat;
            double period = beat >= 0 ? clock.PeriodOf(beat) : 0;
            double omega = OrbitSpeed(Def.Shape.RadPerS * Numbers.Speed * Stats.Speed, period, blades, out bool snapped);
            var ring = rings.Find(r => !r.Active);
            if (ring == null) rings.Add(ring = new Ring());
            if (ring.Hit.Length < blades)
            {
                int cap = state.EnemyStore.Length;
                var hit = new int[blades][];
                for (int k = 0; k < blades; k++) hit[k] = k < ring.Hit.Length ? ring.Hit[k] : new int[cap];
                ring.Hit = hit;
            }
            for (int k = 0; k < blades; k++) Array.Clear(ring.Hit[k], 0, ring.Hit[k].Length);
            if (Def.IsEvolution)
                foreach (var other in rings)
                    if (other.Active && double.IsPositiveInfinity(other.End))
                    {
                        other.End = Math.Max(t, other.Born + DurationS);
                        Announce(sim, other, t);
                    }
            ring.Active = true;
            ring.Id = nextId++;
            ring.Blades = blades;
            ring.Born = ring.Last = t;
            ring.End = Def.IsEvolution ? double.PositiveInfinity : t + DurationS;
            ring.Omega = omega;
            ring.Radius = RingRadius;
            ring.Blade = BladeRadius;
            ring.ClearAt = t + Numbers.HitDelayS;
            ring.Snapped = snapped;
            sim.Emit(SimEvent.HeroAttack(t, Index, Def.Shape.Kind, 0, t, t, 1, 0, false, ring.Radius + ring.Blade));
            Announce(sim, ring, t);
        }

        void Announce(Simulation sim, Ring ring, double t) =>
            sim.Emit(SimEvent.Orbit(t, Index, ring.Id, ring.Blades, ring.Snapped, ring.Born, ring.End, ring.Omega, ring.Radius, ring.Blade));

        /// <summary>A blade's body at <paramref name="t"/>: growing in, then shrinking away after the ring's end.</summary>
        static double Size(Ring ring, double t)
        {
            double grow = Math.Min(1, Math.Max(0, t - ring.Born) / GrowS);
            double fade = Math.Max(0, 1 - Math.Max(0, t - ring.End) / FadeS);
            return ring.Blade * grow * fade;
        }

        /// <summary>Blade <paramref name="k"/>'s angle at <paramref name="t"/>.</summary>
        static double Angle(Ring ring, int k, double t) => ring.Omega * (t - ring.Born) + 2 * Math.PI * k / ring.Blades;

        internal override void Tick(Simulation sim, double end)
        {
            foreach (var ring in rings)
            {
                if (!ring.Active) continue;
                if (end >= ring.End + FadeS - 1e-9)
                {
                    ring.Active = false;
                    continue;
                }
                if (end > ring.End + 1e-9 && ring.Last >= ring.End - 1e-9) continue;
                if (Holding)
                {
                    if (ring.End > end)
                    {
                        ring.End = end;
                        Announce(sim, ring, end);
                    }
                    ring.Last = end;
                    continue;
                }
                double to = Math.Min(end, ring.End);
                Sweep(sim, ring, ring.Last, to);
                ring.Last = to;
            }
        }

        /// <summary>Every blade's path from <paramref name="from"/> to <paramref name="to"/>, hitting at <paramref name="to"/> what it touches.</summary>
        void Sweep(Simulation sim, Ring ring, double from, double to)
        {
            if (horde == null || to <= from) return;
            while (to >= ring.ClearAt - 1e-9)
            {
                for (int k = 0; k < ring.Blades; k++) Array.Clear(ring.Hit[k], 0, ring.Hit[k].Length);
                ring.ClearAt += Math.Max(1e-3, Numbers.HitDelayS);
            }
            int steps = Math.Max(1, (int)Math.Ceiling((to - from) * Math.Abs(ring.Omega) / MaxStepRad - 1e-9));
            var enemies = state.EnemyStore;
            for (int k = 0; k < ring.Blades; k++)
            {
                var hit = ring.Hit[k];
                for (int s = 0; s < steps; s++)
                {
                    double t0 = from + (to - from) * s / steps, t1 = from + (to - from) * (s + 1) / steps;
                    BladeAt(ring, k, t0, out double x0, out double y0);
                    BladeAt(ring, k, t1, out double x1, out double y1);
                    double body = Size(ring, t1);
                    double a = Angle(ring, k, t1);
                    double dirX = -Math.Sin(a) * Math.Sign(ring.Omega), dirY = Math.Cos(a) * Math.Sign(ring.Omega);
                    for (int i = 0; i < enemies.Length; i++)
                    {
                        if (!enemies[i].Alive || hit[i] == enemies[i].Life) continue;
                        enemies[i].PositionAt(to, out double ex, out double ey);
                        if (Horde.SegmentDistance(ex, ey, x0, y0, x1, y1) > body + 0.5 * roles[enemies[i].Role].RadiusM) continue;
                        hit[i] = enemies[i].Life;
                        horde.Damage(sim, i, to, Power, Index, WeaponHit.Strike, dirX, dirY, Numbers.Knockback, Launch(1));
                    }
                }
                if (!Def.IsEvolution) continue;
                BladeAt(ring, k, to, out double bx, out double by);
                BreakOnAccent(sim, to, bx, by, Size(ring, to));
            }
        }

        void BladeAt(Ring ring, int k, double t, out double x, out double y)
        {
            state.Hero.PositionAt(t, out double hx, out double hy);
            double a = Angle(ring, k, t);
            x = hx + Math.Cos(a) * ring.Radius;
            y = hy + Math.Sin(a) * ring.Radius;
        }
    }
}
