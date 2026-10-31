using System;
using PS.Game.Sim.Content;

namespace PS.Game.Sim
{
    /// <summary>
    /// Magic Wand and its evolution, Holy Wand (unity-game.md, The Other Weapons; native's
    /// engine/weapons.rs and missiles.rs): crystal bolts at the nearest enemy, shot on eighths by the
    /// arsenal.
    ///
    /// - <b>A shot</b> is a burst of <c>max(1, floor(rank amount + Amount))</c> bolts: the first on
    ///   the shot's grid point, each next one <c>rank repeat / (rank speed * Speed)</c> after the last
    ///   on the run clock, as native's Weapon.PSpeedRepeatInterval spaces them; a freeze holds the
    ///   rest of a burst, and they follow the resume as far apart (native's held events).
    /// - <b>A bolt</b> leaves the hero for the nearest live enemy, flying at
    ///   <c>shape speed * rank speed * Speed</c> metres a second, and arrives on the first sixteenth
    ///   at or after its straight flight would (at that flight with no beat known that far ahead). It
    ///   is aimed where its enemy will stand then, as the enemy's motion goes now, and hits that enemy
    ///   at the arrival wherever it stands: <c>rank power * Might</c>, knocked back by the rank's
    ///   knockback. A hit that doesn't kill uses a point of pierce; with pierce left, or after a kill
    ///   (which uses none, as native's), the bolt flies on from there to the nearest enemy it hasn't
    ///   hit within the rank's hit delay. A bolt whose enemy dies before it arrives turns, at the end
    ///   of that tick, for the nearest other. Bolts know their enemies by life
    ///   (<see cref="EnemyState.Life"/>), so a spawn into a dead enemy's slot is never the one aimed at.
    /// - <b>It ends</b> with no enemy left to fly at, past <see cref="MaxTravelM"/> of flight or
    ///   <see cref="MaxFlightS"/> of song time, when the world freezes or the hero falls. With no enemy
    ///   at launch it flies <see cref="AimlessS"/> along the hero's facing and fades.
    ///
    /// Each leg is a <see cref="SimEvent.Projectile"/> event (native homes each frame; a leg per
    /// target lets every hit land on the grid and the client draw the flight from its events).
    /// </summary>
    internal sealed class MagicWand : Weapon
    {
        /// <summary>A bolt's whole flight: native's MAX_TRAVEL, 2400 px at 27 px a metre.</summary>
        public const double MaxTravelM = 2400.0 / 27;
        public const double MaxFlightS = 12;
        public const double AimlessS = 0.6;
        const int BoltCapacity = 128;
        const int BurstCapacity = 64;
        /// <summary>The enemies a bolt remembers hitting: its pierce at most.</summary>
        const int MemoryCapacity = 8;

        struct Bolt
        {
            public bool Active;
            public int Id, Target, TargetLife, Left;
            public double FromX, FromY, ToX, ToY, Start, Arrive, Born, Travel;
            public int Remembered;
        }

        readonly SimState state;
        readonly RunRules run;
        readonly BeatClock clock;
        readonly Horde horde;
        readonly EnemyRole[] roles;
        readonly Bolt[] bolts = new Bolt[BoltCapacity];
        // Each bolt's memory: the lives of the enemies it hit, and until when it passes them by.
        readonly int[] hitEnemy = new int[BoltCapacity * MemoryCapacity];
        readonly double[] hitUntil = new double[BoltCapacity * MemoryCapacity];
        // The bursts' next bolts, on the run clock.
        readonly double[] burst = new double[BurstCapacity];
        int bursts, nextId;

        public MagicWand(Arsenal arsenal, SimState state, RunRules run, WeaponDef def, int index, GameContent content, BeatClock clock, Horde horde)
            : base(arsenal, state, def, index)
        {
            if (def.Shape.Kind != ShapeKind.Missile) throw new ArgumentException(def.Id + " is not a missile weapon");
            this.state = state;
            this.run = run;
            this.clock = clock;
            this.horde = horde;
            roles = content.Roles;
        }

        /// <summary>Bolts a shot fires.</summary>
        public int Bolts => Amount;

        public double SpeedMps => Def.Shape.SpeedMps * Numbers.Speed * Stats.Speed;

        /// <summary>The run clock between a burst's bolts.</summary>
        public double Repeat => Numbers.RepeatS / Math.Max(1e-3, Numbers.Speed * Stats.Speed);

        internal override void Shoot(Simulation sim, double t)
        {
            state.Hero.PositionAt(t, out double hx, out double hy);
            int target = Nearest(t, hx, hy, -1);
            double dx, dy;
            if (target >= 0)
            {
                state.EnemyStore[target].PositionAt(t, out double ex, out double ey);
                Normalize(ex - hx, ey - hy, out dx, out dy);
            }
            else Sword.Facing(state.Hero.Facing, out dx, out dy);
            sim.Emit(SimEvent.HeroAttack(t, Index, Def.Shape.Kind, 0, t, t, dx, dy, false, Radius));
            Launch(sim, t, hx, hy, target);
            double now = run.ClockAt(t);
            for (int k = 1; k < Bolts && bursts < BurstCapacity; k++) burst[bursts++] = now + k * Repeat;
        }

        internal override double NextTime(double floor)
        {
            double t = double.PositiveInfinity;
            for (int k = 0; k < bursts; k++) t = Math.Min(t, run.SongAt(burst[k]));
            for (int b = 0; b < BoltCapacity; b++)
                if (bolts[b].Active) t = Math.Min(t, bolts[b].Arrive);
            return Math.Max(t, floor);
        }

        /// <summary>A bolt's arrival, else a burst's next bolt, the earliest first.</summary>
        internal override void Fire(Simulation sim, double t)
        {
            int due = -1;
            for (int b = 0; b < BoltCapacity; b++)
                if (bolts[b].Active && bolts[b].Arrive <= t && (due < 0 || bolts[b].Arrive < bolts[due].Arrive)) due = b;
            if (due >= 0)
            {
                Arrive(sim, due, t);
                return;
            }
            int first = 0;
            for (int k = 1; k < bursts; k++)
                if (burst[k] < burst[first]) first = k;
            burst[first] = burst[--bursts];
            state.Hero.PositionAt(t, out double hx, out double hy);
            Launch(sim, t, hx, hy, Nearest(t, hx, hy, -1));
        }

        /// <summary>The per-tick system, at the tick's end: bolts end in a freeze or a fall, and turn from enemies that died.</summary>
        internal override void Tick(Simulation sim, double end)
        {
            for (int b = 0; b < BoltCapacity; b++)
            {
                ref var bolt = ref bolts[b];
                if (!bolt.Active) continue;
                if (Holding)
                {
                    At(bolt, end, out double x, out double y);
                    Finish(sim, b, end, x, y);
                    continue;
                }
                if (bolt.Target < 0 || Holds(bolt)) continue;
                At(bolt, end, out double fx, out double fy);
                Retarget(sim, b, end, fx, fy);
            }
        }

        void Launch(Simulation sim, double t, double hx, double hy, int target)
        {
            int b = 0;
            while (b < BoltCapacity && bolts[b].Active) b++;
            if (b == BoltCapacity) return;
            bolts[b] = new Bolt { Active = true, Id = nextId++, Born = t, Left = Math.Max(1, Numbers.Pierce) };
            if (target >= 0)
            {
                Leg(ref bolts[b], t, hx, hy, target);
            }
            else
            {
                Sword.Facing(state.Hero.Facing, out double dx, out double dy);
                double d = SpeedMps * AimlessS;
                bolts[b].FromX = hx;
                bolts[b].FromY = hy;
                bolts[b].ToX = hx + dx * d;
                bolts[b].ToY = hy + dy * d;
                bolts[b].Start = t;
                bolts[b].Arrive = t + AimlessS;
                bolts[b].Target = -1;
            }
            Emit(sim, b, t, ProjectilePhase.Launch);
        }

        /// <summary>Aims a leg from (x, y) at <paramref name="t"/> for enemy <paramref name="target"/>: its arrival on the sixteenths, its aim where the enemy will stand then.</summary>
        void Leg(ref Bolt bolt, double t, double x, double y, int target)
        {
            ref var e = ref state.EnemyStore[target];
            e.PositionAt(t, out double ex, out double ey);
            double d = Math.Sqrt((ex - x) * (ex - x) + (ey - y) * (ey - y));
            double raw = t + d / Math.Max(1e-6, SpeedMps);
            double arrive = clock.GridAtOrAfter(raw, 0.25, out _);
            if (double.IsNaN(arrive)) arrive = raw;
            e.PositionAt(arrive, out double ax, out double ay);
            bolt.Target = target;
            bolt.TargetLife = e.Life;
            bolt.FromX = x;
            bolt.FromY = y;
            bolt.ToX = ax;
            bolt.ToY = ay;
            bolt.Start = t;
            bolt.Arrive = arrive;
        }

        void Arrive(Simulation sim, int b, double t)
        {
            ref var bolt = ref bolts[b];
            double x = bolt.ToX, y = bolt.ToY;
            bolt.Travel += Dist(bolt.FromX, bolt.FromY, x, y);
            if (Holding || bolt.Target < 0)
            {
                Finish(sim, b, t, x, y);
                return;
            }
            int i = bolt.Target;
            ref var e = ref state.EnemyStore[i];
            if (Holds(bolt))
            {
                e.PositionAt(t, out x, out y);
                Normalize(x - bolt.FromX, y - bolt.FromY, out double dx, out double dy);
                horde.Damage(sim, i, t, Power, Index, WeaponHit.Strike, dx, dy, Numbers.Knockback, Launch(1));
                if (e.Alive)
                {
                    Remember(b, e.Life, t);
                    if (--bolt.Left == 0)
                    {
                        Finish(sim, b, t, x, y);
                        return;
                    }
                }
            }
            Retarget(sim, b, t, x, y);
        }

        /// <summary>The bolt at (x, y) at <paramref name="t"/> turns for the nearest enemy it may hit, or ends with none or no flight left.</summary>
        void Retarget(Simulation sim, int b, double t, double x, double y)
        {
            ref var bolt = ref bolts[b];
            if (bolt.Target >= 0 && t < bolt.Arrive) bolt.Travel += Dist(bolt.FromX, bolt.FromY, x, y);
            int next = Nearest(t, x, y, b);
            if (next < 0 || bolt.Travel >= MaxTravelM || t - bolt.Born >= MaxFlightS)
            {
                Finish(sim, b, t, x, y);
                return;
            }
            Leg(ref bolt, t, x, y, next);
            Emit(sim, b, t, ProjectilePhase.Turn);
        }

        void Finish(Simulation sim, int b, double t, double x, double y)
        {
            ref var bolt = ref bolts[b];
            bolt.FromX = bolt.ToX = x;
            bolt.FromY = bolt.ToY = y;
            bolt.Start = bolt.Arrive = t;
            bolt.Target = -1;
            Emit(sim, b, t, ProjectilePhase.End);
            bolt.Active = false;
            bolt.Remembered = 0;
        }

        void Emit(Simulation sim, int b, double t, ProjectilePhase phase)
        {
            ref var bolt = ref bolts[b];
            sim.Emit(SimEvent.Projectile(t, Index, bolt.Id, phase, bolt.Target, bolt.FromX, bolt.FromY, bolt.ToX, bolt.ToY,
                bolt.Arrive, Radius));
        }

        /// <summary>Is the bolt's enemy alive, and still the one it aimed at?</summary>
        bool Holds(in Bolt bolt)
        {
            ref var e = ref state.EnemyStore[bolt.Target];
            return e.Alive && e.Life == bolt.TargetLife;
        }

        /// <summary>Where a bolt is at <paramref name="t"/> on its leg.</summary>
        static void At(in Bolt bolt, double t, out double x, out double y)
        {
            double span = bolt.Arrive - bolt.Start;
            double u = span > 1e-12 ? Math.Max(0, Math.Min(1, (t - bolt.Start) / span)) : 1;
            x = bolt.FromX + (bolt.ToX - bolt.FromX) * u;
            y = bolt.FromY + (bolt.ToY - bolt.FromY) * u;
        }

        /// <summary>The nearest live enemy to (x, y) at <paramref name="t"/> that bolt <paramref name="b"/> (-1 none) hasn't hit within its hit delay, the lower slot on ties; -1 for none.</summary>
        int Nearest(double t, double x, double y, int b)
        {
            if (horde == null) return -1;
            var enemies = state.EnemyStore;
            int best = -1;
            double bestD = double.PositiveInfinity;
            for (int i = 0; i < enemies.Length; i++)
            {
                if (!enemies[i].Alive || (b >= 0 && Remembers(b, enemies[i].Life, t))) continue;
                enemies[i].PositionAt(t, out double ex, out double ey);
                double d = (ex - x) * (ex - x) + (ey - y) * (ey - y);
                if (d < bestD)
                {
                    bestD = d;
                    best = i;
                }
            }
            return best;
        }

        /// <summary>Bolt <paramref name="b"/> hit the enemy of life <paramref name="enemy"/> at <paramref name="t"/>: it passes it by for the rank's hit delay.</summary>
        void Remember(int b, int enemy, double t)
        {
            ref var bolt = ref bolts[b];
            int at = b * MemoryCapacity;
            double until = t + Numbers.HitDelayS;
            for (int k = 0; k < bolt.Remembered; k++)
                if (hitEnemy[at + k] == enemy || hitUntil[at + k] <= t + 1e-9)
                {
                    hitEnemy[at + k] = enemy;
                    hitUntil[at + k] = until;
                    return;
                }
            if (bolt.Remembered == MemoryCapacity) return;
            hitEnemy[at + bolt.Remembered] = enemy;
            hitUntil[at + bolt.Remembered++] = until;
        }

        bool Remembers(int b, int enemy, double t)
        {
            int at = b * MemoryCapacity;
            for (int k = 0; k < bolts[b].Remembered; k++)
                if (hitEnemy[at + k] == enemy && hitUntil[at + k] > t + 1e-9) return true;
            return false;
        }

        static double Dist(double ax, double ay, double bx, double by) => Math.Sqrt((bx - ax) * (bx - ax) + (by - ay) * (by - ay));

        static void Normalize(double x, double y, out double nx, out double ny)
        {
            double l = Math.Sqrt(x * x + y * y);
            if (l < 1e-9)
            {
                nx = 1;
                ny = 0;
                return;
            }
            nx = x / l;
            ny = y / l;
        }
    }
}
