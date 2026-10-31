using System;
using PS.Game.Sim.Content;

namespace PS.Game.Sim
{
    /// <summary>The hero's attacks, the kind in its attack events (unity-game.md, The Sword's presentation).</summary>
    public enum HeroAttackKind
    {
        /// <summary>Ordinary main-beat strikes alternate left and right slashes.</summary>
        SlashLeft = 0,
        SlashRight = 1,
        /// <summary>A subdivision strike: a quick follow-up from the previous swing's follow-through.</summary>
        FollowUp = 2,
        /// <summary>A heavy strike, on an accent.</summary>
        Spin = 3,
        /// <summary>The first downbeat after a section change; heavy too when it falls on an accent.</summary>
        Slam = 4,
    }

    /// <summary>What dealt a hit to an enemy, the source in its hit events.</summary>
    public enum WeaponHit
    {
        Strike = 0,
        HeavyStrike = 1,
        Cleave = 2,
    }

    /// <summary>What a dying enemy drops.</summary>
    public enum ItemKind
    {
        Gem = 0,
    }

    /// <summary>
    /// The sword (unity-game.md, The Sword and "Attacks and Steps Land on the Beat").
    ///
    /// - <b>Strikes</b>: one on every committed beat, plus one more for each point of Amount, on
    ///   the beat's even subdivisions: <c>n = max(1, floor(rank amount + Amount))</c> strikes at
    ///   <c>T + k * p / n</c>, p being the interval to the next beat when it is known, else the
    ///   beat's period. With no beat known ahead, the same pulse repeats every rank interval times
    ///   Cooldown, counted from the latest pulse.
    /// - <b>Planning</b> at <c>T - windup</c> (or when the beat is revealed, if later; a beat
    ///   revealed at or after its time is missed): the beat's strikes are queued, each winding up at
    ///   <c>max(impact - windup, the previous impact)</c>, so strikes closer than a wind-up follow on.
    /// - <b>Wind-up</b>: the strike's kind and heaviness are decided, its direction is chosen from
    ///   the enemies there then, and the attack event is emitted with the impact time, always ahead
    ///   of it. A main strike is <b>heavy</b> when the music is clear and a revealed strong onset or
    ///   accent forecast lies within <see cref="AccentWindow"/> of its beat; the decision is per
    ///   beat, so a forecast and its onset make one heavy strike.
    /// - <b>Impact</b>: every enemy whose body reaches the circle of radius
    ///   <c>shape radius * rank area * Area</c> around the hero (x 1.3 heavy) takes
    ///   <c>rank power * Might</c> (x 2.5 heavy), and is knocked back or dies. A heavy strike sends
    ///   out a cleave wave, its bands one sixteenth apart from the next eighth, hitting each enemy it
    ///   passes once at the wave's damage. Strikes and bands hit crystal structures in reach too
    ///   (<see cref="Structures"/>): a heavy strike and its wave break them, others only glint.
    /// - Nothing new winds up while the world is frozen or the hero has fallen; an attack already
    ///   emitted lands, as every settled event is final, unless the hero has fallen.
    /// </summary>
    internal sealed class Sword
    {
        /// <summary>A strong onset or a forecast this close to a beat makes its strike heavy (native's FORECAST_WINDOW).</summary>
        public const double AccentWindow = 0.05;
        /// <summary>The client plays a wind-up up to this much faster, so this is the shortest it fits.</summary>
        public const double FastestRetime = 4.0 / 3.0;
        public const int AimDirections = 16;
        /// <summary>The arc the aim scores enemies in: native's light swing, 150 degrees.</summary>
        public static readonly double AimHalfAngle = 75 * Math.PI / 180;
        /// <summary>Native's aim weights: a boss outweighs a crowd of hundreds.</summary>
        public const double BossWeight = 1000, BruteWeight = 2;
        const int QueueCapacity = 64;
        const int WaveCapacity = 4;

        struct Strike
        {
            public double Impact, Windup, Period;
            public int Beat;
            public bool Main, Wound, Cancelled, Heavy;
            public double Reach, DirX, DirY;
        }

        struct Wave
        {
            public bool Active;
            public int Id, Band, Bands;
            public double X, Y, Inner, Outer, First, Every, Damage;
        }

        readonly BeliefLog log;
        readonly BeatClock clock;
        readonly SimState state;
        readonly Horde horde;
        readonly Structures structures;
        readonly Zones zones;
        readonly EnemyRole[] roles;
        readonly double[] aimWeight;
        readonly WeaponDef weapon;
        readonly int weaponIndex;
        readonly double windup;
        int rank;
        Stats stats;

        readonly Strike[] queue = new Strike[QueueCapacity];
        int head, count;
        int planBeat;
        double lastImpact = double.NegativeInfinity;
        double lastPulse = double.NegativeInfinity;
        int slashes;
        bool forcedAccent;
        int onsetFrom, forecastFrom;

        readonly Wave[] waves = new Wave[WaveCapacity];
        readonly bool[][] waveHit = new bool[WaveCapacity][];
        int waveCount;

        // The enemies in a hit's reach, and the aim's distances and angles to them.
        readonly int[] reached;
        readonly double[] aimAngle, aimDist;

        public Sword(SwordConfig config, BeliefLog log, BeatClock clock, SimState state, Horde horde, Structures structures, Zones zones)
        {
            if (config.Content == null) throw new ArgumentException("the sword needs content");
            weapon = config.Content.Weapon(config.Weapon);
            weaponIndex = config.Content.WeaponIndex(config.Weapon);
            if (weapon.Shape.Kind != ShapeKind.Sword) throw new ArgumentException(config.Weapon + " is not a sword");
            if (!(config.WindupS > 0)) throw new ArgumentException("a positive wind-up");
            this.log = log;
            this.clock = clock;
            this.state = state;
            this.horde = horde;
            this.structures = structures;
            this.zones = zones;
            windup = config.WindupS;
            rank = config.Rank;
            stats = config.Stats ?? config.Content.Progression.Hero.Stats;
            roles = config.Content.Roles;
            aimWeight = new double[roles.Length];
            for (int i = 0; i < roles.Length; i++)
                aimWeight[i] = roles[i].Id.StartsWith("boss-", StringComparison.Ordinal) || roles[i].Id == "minotaur" ? BossWeight
                    : roles[i].Brute ? BruteWeight : 1;
            int cap = state.EnemyStore.Length;
            reached = new int[cap];
            aimAngle = new double[cap];
            aimDist = new double[cap];
            for (int w = 0; w < WaveCapacity; w++) waveHit[w] = new bool[cap];
        }

        WeaponRank Numbers => weapon.Rank(rank);

        /// <summary>Strikes per beat: one, and one more per point of Amount.</summary>
        public int StrikesPerBeat => Math.Max(1, (int)Math.Floor(Numbers.Amount + stats.Amount + 1e-9));

        /// <summary>A strike's damage before the heavy multiplier: the rank's power times Might, at any tempo.</summary>
        public double Power => Numbers.Power * stats.Might;

        public double Reach(bool heavy) =>
            weapon.Shape.RadiusM * Numbers.Area * stats.Area * (heavy ? weapon.Shape.HeavyReachScale : 1);

        /// <summary>The pulse's interval with no beat known ahead.</summary>
        public double Interval => Numbers.IntervalS * stats.Cooldown;

        /// <summary>A death launch's distance in metres: native's knockback distance times the weapon's, longer for a heavier hit.</summary>
        double Launch(double weight) => Horde.KnockbackM * Numbers.Knockback * weight;

        /// <summary>Committed beats revealed too late to wind up for.</summary>
        public int Missed { get; private set; }

        bool Holding => state.Frozen || state.Hero.Fallen;

        ref Strike At(int k) => ref queue[(head + k) % QueueCapacity];

        /// <summary>The next of the sword's exact times at or after <paramref name="floor"/>.</summary>
        internal double NextTime(double floor)
        {
            double t = PlanTime(floor);
            if (count > 0) t = Math.Min(t, At(0).Impact);
            for (int k = 0; k < count; k++)
            {
                if (At(k).Wound) continue;
                t = Math.Min(t, Math.Max(At(k).Windup, floor));
                break;
            }
            for (int w = 0; w < WaveCapacity; w++)
                if (waves[w].Active) t = Math.Min(t, Math.Max(BandTime(w), floor));
            return t;
        }

        /// <summary>When the next committed beat's strikes are planned; beats too late to wind up for are skipped.</summary>
        double PlanTime(double floor)
        {
            var beats = log.Beats;
            while (planBeat < beats.Length)
            {
                double t = beats[planBeat].T;
                if (t > floor + 1e-9 && t > lastImpact + 1e-9) return Math.Max(t - windup, floor);
                if (t <= floor + 1e-9) Missed++;
                planBeat++;
            }
            return double.PositiveInfinity;
        }

        double BandTime(int w) => waves[w].First + waves[w].Band * waves[w].Every;

        /// <summary>
        /// One of the sword's exact times (<see cref="NextTime"/>'s): an impact, a cleave band, a
        /// beat's plan or a wind-up, in that order at one time. Times compare exactly, so each
        /// happens at its own time, never at a neighbour's a rounding away.
        /// </summary>
        internal void Fire(Simulation sim, double t)
        {
            if (count > 0 && At(0).Impact <= t)
            {
                var s = At(0);
                head = (head + 1) % QueueCapacity;
                count--;
                Impact(sim, s, t);
                return;
            }
            for (int w = 0; w < WaveCapacity; w++)
                if (waves[w].Active && BandTime(w) <= t)
                {
                    Band(sim, w, t);
                    return;
                }
            if (PlanTime(t) <= t)
            {
                int b = planBeat++;
                if (Holding) return;
                var beats = log.Beats;
                double period = clock.StartsInterval(b) ? beats[b + 1].T - beats[b].T : clock.PeriodOf(b);
                Pulse(t, beats[b].T, period, b);
                return;
            }
            for (int k = 0; k < count; k++)
            {
                ref var s = ref At(k);
                if (s.Wound) continue;
                if (s.Windup <= t) WindUp(sim, ref s, t);
                return;
            }
        }

        /// <summary>The per-tick system, at the tick's end: with no beat known ahead, the pulse on the sword's raw interval.</summary>
        internal void Tick(double end)
        {
            if (Holding || count > 0) return;
            double interval = Interval;
            double due = lastPulse + interval;
            if (end < due - windup - 1e-9 || clock.StateAt(end) != Reliability.NoBeatAhead) return;
            double shortest = windup / FastestRetime;
            double impact = Math.Max(due, end + shortest);
            // A revealed beat close after it takes over.
            double plan = PlanTime(end);
            if (plan < double.PositiveInfinity && log.Beats[planBeat].T < impact + interval * 0.5) return;
            Pulse(end, impact, interval, -1);
        }

        /// <summary>Queues a pulse's strikes from <paramref name="t0"/>, <paramref name="period"/> long, planned at <paramref name="now"/>.</summary>
        void Pulse(double now, double t0, double period, int beat)
        {
            int n = StrikesPerBeat;
            lastPulse = t0;
            for (int k = 0; k < n && count < QueueCapacity; k++)
            {
                double impact = t0 + period * k / n;
                At(count++) = new Strike
                {
                    Impact = impact,
                    Windup = Math.Max(impact - windup, Math.Max(lastImpact, now)),
                    Period = period,
                    Beat = beat,
                    Main = k == 0,
                };
                lastImpact = impact;
            }
        }

        void WindUp(Simulation sim, ref Strike s, double t)
        {
            s.Wound = true;
            if (Holding)
            {
                s.Cancelled = true;
                return;
            }
            bool onBeat = s.Main && s.Beat >= 0;
            bool heavy = onBeat && (forcedAccent || Accented(s.Impact, t));
            if (onBeat) forcedAccent = false;
            bool slam = onBeat && Slams(s.Beat, s.Impact);
            var kind = slam ? HeroAttackKind.Slam
                : heavy ? HeroAttackKind.Spin
                : !s.Main ? HeroAttackKind.FollowUp
                : (slashes++ & 1) == 0 ? HeroAttackKind.SlashLeft : HeroAttackKind.SlashRight;
            s.Heavy = heavy;
            s.Reach = Reach(heavy);
            state.Hero.PositionAt(t, out double hx, out double hy);
            Aim(t, hx, hy, s.Reach, out s.DirX, out s.DirY);
            sim.Emit(SimEvent.SwordAttack(t, weaponIndex, kind, t, s.Impact, s.DirX, s.DirY, heavy, s.Reach));
        }

        /// <summary>The next main strike to wind up on a beat is heavy (<see cref="DebugCommand.Accent"/>).</summary>
        internal void ForceAccent() => forcedAccent = true;

        /// <summary>Is the beat at <paramref name="impact"/> accented: a strong onset or a forecast within the window, known now, the music clear?</summary>
        bool Accented(double impact, double now)
        {
            if (!clock.ClearAt(now)) return false;
            return Near(EntryKind.Onset, ref onsetFrom, impact, true) || Near(EntryKind.Forecast, ref forecastFrom, impact, false);
        }

        bool Near(EntryKind kind, ref int from, double impact, bool strongOnly)
        {
            var list = log.Revealed(kind);
            // Entries are revealed in known-at order, close to time order: anything a second
            // before this beat can matter to no later one.
            while (from < list.Length && list[from].T < impact - 1.0) from++;
            for (int i = from; i < list.Length; i++)
            {
                if (Math.Abs(list[i].T - impact) > AccentWindow + 1e-9) continue;
                if (!strongOnly || new OnsetEntry(list[i]).Strong) return true;
            }
            return false;
        }

        /// <summary>
        /// Does the main strike on beat <paramref name="beat"/> slam: is it the first downbeat after a
        /// section change, the zone change's own downbeat when that is known at the wind-up
        /// (<see cref="Zones.Slams"/>)?
        /// </summary>
        bool Slams(int beat, double impact) => clock.IsDownbeat(beat) && zones != null && zones.Slams(impact);

        /// <summary>
        /// The swing's direction (native's aim.rs): of 16 directions, the one whose arc holds the
        /// most weight of enemies in reach, weighted toward bosses and brutes, the one nearest the
        /// nearest enemy on ties. With nobody's centre in reach, the nearest enemy whose body is;
        /// with nobody, the hero's facing.
        /// </summary>
        void Aim(double t, double hx, double hy, double reach, out double dirX, out double dirY)
        {
            var enemies = state.EnemyStore;
            int n = horde != null ? horde.InReach(t, hx, hy, reach, reached) : 0;
            int nearest = -1;
            double nearestD = double.PositiveInfinity;
            for (int k = 0; k < n; k++)
            {
                enemies[reached[k]].PositionAt(t, out double x, out double y);
                double d = Math.Sqrt((x - hx) * (x - hx) + (y - hy) * (y - hy));
                aimDist[k] = d;
                aimAngle[k] = Math.Atan2(y - hy, x - hx);
                if (d < nearestD)
                {
                    nearestD = d;
                    nearest = k;
                }
            }
            if (nearest < 0)
            {
                Facing(state.Hero.Facing, out dirX, out dirY);
                return;
            }
            double toNearest = aimAngle[nearest];
            double bestScore = 0, bestDir = 0, bestOff = double.PositiveInfinity;
            for (int j = 0; j < AimDirections; j++)
            {
                double dir = Wrap(2 * Math.PI * j / AimDirections);
                double score = 0;
                for (int k = 0; k < n; k++)
                    if (aimDist[k] <= reach && Math.Abs(Wrap(aimAngle[k] - dir)) <= AimHalfAngle + 1e-12)
                        score += aimWeight[enemies[reached[k]].Role];
                if (score <= 0) continue;
                double off = Math.Abs(Wrap(dir - toNearest));
                if (score > bestScore + 1e-9 || (Math.Abs(score - bestScore) <= 1e-9 && off < bestOff - 1e-9))
                {
                    bestScore = score;
                    bestDir = dir;
                    bestOff = off;
                }
            }
            double a = bestScore > 0 ? bestDir : toNearest;
            dirX = Math.Cos(a);
            dirY = Math.Sin(a);
        }

        static double Wrap(double a)
        {
            a %= 2 * Math.PI;
            if (a > Math.PI) a -= 2 * Math.PI;
            else if (a <= -Math.PI) a += 2 * Math.PI;
            return a;
        }

        static void Facing(Direction d, out double x, out double y)
        {
            x = d == Direction.Right ? 1 : d == Direction.Left ? -1 : 0;
            y = d == Direction.Up ? 1 : d == Direction.Down ? -1 : 0;
        }

        void Impact(Simulation sim, in Strike s, double t)
        {
            if (s.Cancelled || !s.Wound || state.Hero.Fallen) return;
            state.Hero.PositionAt(t, out double hx, out double hy);
            int wave = s.Heavy ? StartWave(sim, s, t, hx, hy) : -1;
            double launch = Launch(s.Heavy ? weapon.Shape.HeavyDamage : 1);
            structures.Strike(sim, t, hx, hy, s.Reach, s.Heavy ? StructureHit.HeavyStrike : StructureHit.Strike, launch,
                wave >= 0 ? waves[wave].Id : -1);
            if (horde == null) return;
            var enemies = state.EnemyStore;
            double damage = Power * (s.Heavy ? weapon.Shape.HeavyDamage : 1);
            var how = s.Heavy ? WeaponHit.HeavyStrike : WeaponHit.Strike;
            int n = horde.InReach(t, hx, hy, s.Reach, reached);
            for (int k = 0; k < n; k++)
            {
                int i = reached[k];
                enemies[i].PositionAt(t, out double x, out double y);
                double dx = x - hx, dy = y - hy;
                double d = Math.Sqrt(dx * dx + dy * dy);
                if (wave >= 0) waveHit[wave][i] = true;
                horde.Damage(sim, i, t, damage, weaponIndex, how, d > 1e-9 ? dx / d : s.DirX, d > 1e-9 ? dy / d : s.DirY,
                    Numbers.Knockback, launch);
            }
        }

        /// <summary>A heavy strike's cleave wave from (hx, hy): its first band on the next eighth, then one each sixteenth.</summary>
        int StartWave(Simulation sim, in Strike s, double t, double hx, double hy)
        {
            int w = 0;
            while (w < WaveCapacity && waves[w].Active) w++;
            // Waves last two beats and heavy strikes come a beat apart at most: a full set is never reached.
            if (w == WaveCapacity) return -1;
            var shape = weapon.Shape;
            int bands = (int)Math.Ceiling(shape.WaveBeyondM / shape.WaveBandM - 1e-9);
            waves[w] = new Wave
            {
                Active = true,
                Id = waveCount,
                Bands = bands,
                X = hx,
                Y = hy,
                Inner = s.Reach,
                Outer = s.Reach + shape.WaveBeyondM,
                First = t + 0.5 * s.Period,
                Every = 0.25 * s.Period,
                Damage = shape.WaveDamage * Power,
            };
            Array.Clear(waveHit[w], 0, waveHit[w].Length);
            sim.Emit(SimEvent.Cleave(t, waveCount++, bands, hx, hy, waves[w].Inner, waves[w].Outer, waves[w].First, waves[w].Every));
            return w;
        }

        void Band(Simulation sim, int w, double t)
        {
            ref var wv = ref waves[w];
            double outer = Math.Min(wv.Inner + weapon.Shape.WaveBandM * (wv.Band + 1), wv.Outer);
            if (++wv.Band >= wv.Bands) wv.Active = false;
            if (state.Hero.Fallen) return;
            double launch = Launch(1);
            structures.Strike(sim, t, wv.X, wv.Y, outer, StructureHit.Cleave, launch, wv.Id);
            if (horde == null) return;
            var enemies = state.EnemyStore;
            var hit = waveHit[w];
            int n = horde.InReach(t, wv.X, wv.Y, outer, reached);
            for (int k = 0; k < n; k++)
            {
                int i = reached[k];
                if (hit[i]) continue;
                enemies[i].PositionAt(t, out double x, out double y);
                double dx = x - wv.X, dy = y - wv.Y;
                double d = Math.Sqrt(dx * dx + dy * dy);
                if (d + 0.5 * roles[enemies[i].Role].RadiusM <= wv.Inner) continue;
                hit[i] = true;
                horde.Damage(sim, i, t, wv.Damage, weaponIndex, WeaponHit.Cleave, d > 1e-9 ? dx / d : 1, d > 1e-9 ? dy / d : 0,
                    Numbers.Knockback, launch);
            }
        }
    }
}
