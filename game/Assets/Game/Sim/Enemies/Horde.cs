using System;
using PS.Game.Sim.Content;
using PS.Game.Sim.Level;

namespace PS.Game.Sim
{
    /// <summary>
    /// The enemies (unity-game.md, Enemies, "Speeds Follow the Tempo" and "Attacks and Steps Land
    /// on the Beat"). Positions are continuous, and all travel is on main beats: the committed
    /// beats, never the stride grid's subdivisions.
    ///
    /// Each main beat at time T, period p, goes through three exact times inside the ticks:
    /// - <b>Notice</b> at <c>T - p/2</c>: with the beat reliable, an enemy within lunge range of
    ///   the hero (contact, plus its own step and the hero's travel in half a beat) poses and arms
    ///   a contact lunge for T (an attack event, impact T).
    /// - <b>Decide</b> at <c>T - min(100 ms, 20% of p)</c>: with the beat reliable, every enemy
    ///   whose gait steps on this beat decides its step down the flow field, resolves separation
    ///   inside the step, and emits it (from, to, move start, footplant T). With the hero in reach
    ///   and nothing between them, it steps straight at the hero instead, stopping at contact.
    /// - <b>Land</b> at T: steps plant; with the beat still reliable, armed enemies in contact
    ///   (along the whole step for one that stepped) hit the hero.
    /// A beat revealed late takes what is left of these times; one revealed after T is skipped.
    /// Without a reliable beat, or while the world is frozen, enemies hold and lunges stop; on
    /// relock or the thaw they resume on the next main beat, with no catch-up. A decided step is final, as every settled event is: it plants
    /// even if the beat's state drops inside its move window (native cancels it instead).
    ///
    /// Knockback (<see cref="Knock"/>) is the one travel between beats: immediate, a fixed
    /// distance, and the enemy decides no step until it ends. Spawns (the slice's test schedule)
    /// land on a ring just outside the camera's footprint, inside the walkable arena; enemies far
    /// out of sight are recycled onto it. At most the schedule's cap are alive.
    /// </summary>
    internal sealed class Horde
    {
        public const double HeroHurtRadiusM = 10.0 / 27.0;
        public const double ContactGapM = 4.0 / 27.0;
        public const double BiteSlackM = 2.0 / 27.0;
        /// <summary>Native's knockback, 10 px per unit of role times weapon knockback, over 0.12 s.</summary>
        public const double KnockbackM = 10.0 / 27.0;
        public const double KnockSeconds = 0.12;
        public const double MaxMoveWindow = 0.1;
        public const double MoveWindowShare = 0.2;
        /// <summary>Two enemies keep apart by this share of their radii's sum (native's).</summary>
        public const double Separation = 0.8;
        const int SeparationPasses = 2;
        /// <summary>Enemies this many view widths from the hero are recycled onto the ring (native's).</summary>
        public const double RecycleViews = 1.5;
        public const double SpawnMarginM = 2;
        public const double SpawnSpreadM = 1.5;
        const int SpawnTries = 16;
        /// <summary>Flow-field cells settled per tick while a rebuild runs.</summary>
        public const int FieldBudget = 4096;
        const double HashCellM = 2;

        enum Phase { Notice, Decide, Land }

        readonly BeliefLog log;
        readonly BeatClock clock;
        readonly StrideGrid strides;
        readonly SimState state;
        readonly EnemyRole[] roles;
        readonly double heroReferenceMps;
        readonly double tileM;
        readonly int[] roster;
        readonly double spawnInterval;
        readonly int minimum;
        readonly double[] footprint;
        readonly double viewWidth;
        readonly EnemyState[] enemies;
        readonly int[] free;
        int freeCount;
        FlowField field, next;

        // Scratch for a beat's decisions: the step before separation, after it, and its cap.
        readonly double[] baseX, baseY, wantX, wantY, capLen;
        readonly bool[] stepping;
        // A spatial hash over the arena for separation.
        readonly int hashW, hashH;
        readonly int[] hashHead, hashStamp, hashNext;
        int hashGeneration;

        int beat;
        Phase phase;
        double nextSpawn;

        public Horde(HordeConfig config, BeliefLog log, BeatClock clock, StrideGrid strides, ArenaGrid arena, SimState state)
        {
            if (config.Content == null) throw new ArgumentException("the horde needs content");
            this.log = log;
            this.clock = clock;
            this.strides = strides;
            this.state = state;
            roles = config.Content.Roles;
            heroReferenceMps = config.Content.Progression.Hero.SpeedMps;
            tileM = arena.TileM;
            roster = new int[config.Roster.Length];
            for (int i = 0; i < roster.Length; i++) roster[i] = Array.IndexOf(roles, config.Content.Role(config.Roster[i]));
            if (config.SpawnIntervalS <= 0) throw new ArgumentException("a positive spawn interval");
            spawnInterval = config.SpawnIntervalS;
            minimum = config.Minimum;
            footprint = (double[])config.CameraFootprint.Clone();
            if (footprint.Length < 6 || footprint.Length % 2 != 0) throw new ArgumentException("a camera footprint of three or more points");
            double minX = double.PositiveInfinity, maxX = double.NegativeInfinity;
            for (int k = 0; k < footprint.Length; k += 2)
            {
                minX = Math.Min(minX, footprint[k]);
                maxX = Math.Max(maxX, footprint[k]);
            }
            viewWidth = maxX - minX;

            int cap = config.Content.Schedule.Cap;
            enemies = new EnemyState[cap];
            state.EnemyStore = enemies;
            free = new int[cap];
            for (int i = 0; i < cap; i++) free[freeCount++] = cap - 1 - i;
            baseX = new double[cap];
            baseY = new double[cap];
            wantX = new double[cap];
            wantY = new double[cap];
            capLen = new double[cap];
            stepping = new bool[cap];
            hashNext = new int[cap];

            Nav = new NavGrid(arena);
            hashW = (int)Math.Ceiling(Nav.W * Nav.CellM / HashCellM) + 1;
            hashH = (int)Math.Ceiling(Nav.H * Nav.CellM / HashCellM) + 1;
            hashHead = new int[hashW * hashH];
            hashStamp = new int[hashW * hashH];

            // Paths around structures run longer than the straight line to the farthest enemy.
            double reach = 3 * RecycleViews * viewWidth;
            field = new FlowField(Nav, reach);
            next = new FlowField(Nav, reach);
            field.Begin(state.Hero.X, state.Hero.Y);
            field.BuildAll();
        }

        public NavGrid Nav { get; }

        /// <summary>Where the hero is at song time <paramref name="t"/>: on its tile, or striding to it.</summary>
        void HeroAt(double t, out double x, out double y)
        {
            var h = state.Hero;
            double u = t >= h.MoveEnd || h.MoveEnd <= h.MoveStart ? 1 : Math.Max(0, (t - h.MoveStart) / (h.MoveEnd - h.MoveStart));
            x = h.FromX + (h.X - h.FromX) * u;
            y = h.FromY + (h.Y - h.FromY) * u;
        }

        public static double MoveWindow(double period) => Math.Min(MaxMoveWindow, MoveWindowShare * period);

        /// <summary>The beats from main beat <paramref name="beat"/> to the gait's next step: the ground this step covers.</summary>
        static int BeatsToNextStep(Gait g, int beat)
        {
            int n = 1;
            while (!g.StepsOn(beat + n)) n++;
            return n;
        }

        /// <summary>A role's step on main beat <paramref name="beat"/>: r tiles per hero stride (unity-game.md, "Speeds Follow the Tempo").</summary>
        double StepOf(int role, int beat, int halvings) =>
            Tempo.EnemyStepDistance(Tempo.Ratio(roles[role].RefSpeedMps, heroReferenceMps), tileM,
                Tempo.StridesPerStep(halvings, BeatsToNextStep(roles[role].Gait, beat)));

        int HalvingsFor(double period) => strides.Halvings >= 0 ? strides.Halvings : StrideGrid.FewestHalvings(period);

        double ContactOf(int role) => HeroHurtRadiusM + roles[role].RadiusM + ContactGapM;

        /// <summary>The next of the horde's exact times at or after <paramref name="floor"/>.</summary>
        internal double NextTime(double floor)
        {
            var beats = log.Beats;
            while (beat < beats.Length && beats[beat].T < floor - 1e-9)
            {
                beat++;
                phase = Phase.Notice;
            }
            if (beat >= beats.Length) return double.PositiveInfinity;
            double t = beats[beat].T;
            double p = clock.PeriodOf(beat);
            double at = phase == Phase.Notice ? t - 0.5 * p : phase == Phase.Decide ? t - MoveWindow(p) : t;
            return Math.Max(at, floor);
        }

        internal void Fire(Simulation sim, double t)
        {
            // A frozen world holds as a beat that can't be trusted does: no travel, no lunges.
            bool reliable = state.Beat == Reliability.Reliable && !state.Frozen;
            switch (phase)
            {
                case Phase.Notice:
                    if (reliable) Notice(sim, t);
                    phase = Phase.Decide;
                    break;
                case Phase.Decide:
                    if (reliable) Decide(sim, t);
                    phase = Phase.Land;
                    break;
                default:
                    Land(sim, t, reliable);
                    beat++;
                    phase = Phase.Notice;
                    break;
            }
        }

        bool Knocked(in EnemyState e, double t) => e.Motion == EnemyMotion.Knock && e.MoveEnd > t + 1e-9;

        void Notice(Simulation sim, double t)
        {
            double impact = log.Beats[beat].T;
            int h = HalvingsFor(clock.PeriodOf(beat));
            double heroReach = tileM * Math.Max(1, (1 << h) / 2);
            HeroAt(t, out double hx, out double hy);
            for (int i = 0; i < enemies.Length; i++)
            {
                ref var e = ref enemies[i];
                if (!e.Alive || Knocked(e, t)) continue;
                double step = roles[e.Role].Gait.StepsOn(beat) ? StepOf(e.Role, beat, h) : 0;
                double reach = ContactOf(e.Role) + BiteSlackM + heroReach + step;
                e.PositionAt(t, out double x, out double y);
                double dx = hx - x, dy = hy - y;
                double d = Math.Sqrt(dx * dx + dy * dy);
                if (d > reach) continue;
                e.LungeBeat = beat;
                if (d < 1e-9)
                {
                    dx = 1;
                    d = 1;
                }
                sim.Emit(SimEvent.Attack(t, i, (int)EnemyAttackKind.Lunge, t, impact, dx / d, dy / d));
            }
        }

        void Decide(Simulation sim, double t)
        {
            double footplant = log.Beats[beat].T;
            int h = HalvingsFor(clock.PeriodOf(beat));
            HeroAt(t, out double hx, out double hy);
            int any = 0;
            for (int i = 0; i < enemies.Length; i++)
            {
                ref var e = ref enemies[i];
                stepping[i] = false;
                if (!e.Alive) continue;
                e.PositionAt(t, out double x, out double y);
                baseX[i] = wantX[i] = x;
                baseY[i] = wantY[i] = y;
                if (Knocked(e, t) || !roles[e.Role].Gait.StepsOn(beat)) continue;
                double step = StepOf(e.Role, beat, h);
                double dx = hx - x, dy = hy - y;
                double d = Math.Sqrt(dx * dx + dy * dy);
                double contact = ContactOf(e.Role);
                double toX, toY;
                if (d <= step + contact + Nav.CellM && Nav.Reach(x, y, hx, hy) >= 1)
                {
                    // The hero is in reach, with nothing between: straight at it, stopping at contact.
                    double len = Math.Max(0, Math.Min(step, d - contact));
                    toX = d > 1e-9 ? x + dx / d * len : x;
                    toY = d > 1e-9 ? y + dy / d * len : y;
                }
                else if (field.Direction(x, y, out _, out _)) FollowField(x, y, step, out toX, out toY);
                else
                {
                    // Beyond the field's reach: straight at the hero.
                    toX = x + dx / d * step;
                    toY = y + dy / d * step;
                }
                baseX[i] = wantX[i] = toX;
                baseY[i] = wantY[i] = toY;
                capLen[i] = step;
                stepping[i] = true;
                any++;
            }
            if (any == 0) return;
            for (int pass = 0; pass < SeparationPasses; pass++) Separate();
            for (int i = 0; i < enemies.Length; i++)
            {
                if (!stepping[i]) continue;
                ref var e = ref enemies[i];
                e.PositionAt(t, out double x, out double y);
                // Separation bends the step by at most one step's length.
                double ox = wantX[i] - baseX[i], oy = wantY[i] - baseY[i];
                double ol = Math.Sqrt(ox * ox + oy * oy);
                double scale = ol > capLen[i] ? capLen[i] / ol : 1;
                double toX = baseX[i] + ox * scale, toY = baseY[i] + oy * scale;
                Nav.Clamp(x, y, ref toX, ref toY);
                if (Math.Abs(toX - x) < 1e-6 && Math.Abs(toY - y) < 1e-6) continue;
                e.Motion = EnemyMotion.Step;
                e.FromX = x;
                e.FromY = y;
                e.ToX = toX;
                e.ToY = toY;
                e.MoveStart = t;
                e.MoveEnd = footplant;
                e.SteppedBeat = beat;
                sim.Emit(SimEvent.EnemyStep(t, i, x, y, toX, toY, t, footplant));
            }
        }

        /// <summary>
        /// A step of <paramref name="len"/> down the flow field from (x, y): it walks the field's
        /// path half a cell at a time and ends at the farthest point a straight move still reaches
        /// walkably, so a long step rounds the end of a wall instead of heading off along the first
        /// cell's heading.
        /// </summary>
        void FollowField(double x, double y, double len, out double toX, out double toY)
        {
            double px = x, py = y;
            for (double left = len; left > 1e-9;)
            {
                if (!field.Direction(px, py, out double dx, out double dy)) break;
                double part = Math.Min(left, 0.5 * Nav.CellM);
                double qx = px + dx * part, qy = py + dy * part;
                Nav.Clamp(px, py, ref qx, ref qy);
                if ((Math.Abs(qx - px) < 1e-9 && Math.Abs(qy - py) < 1e-9) || Nav.Reach(x, y, qx, qy) < 1) break;
                px = qx;
                py = qy;
                left -= part;
            }
            toX = px;
            toY = py;
        }

        /// <summary>One pass pushing overlapping step destinations apart; enemies not stepping hold their ground.</summary>
        void Separate()
        {
            if (++hashGeneration == int.MaxValue)
            {
                Array.Clear(hashStamp, 0, hashStamp.Length);
                hashGeneration = 1;
            }
            for (int i = 0; i < enemies.Length; i++)
            {
                if (!enemies[i].Alive) continue;
                int c = HashCell(wantX[i], wantY[i]);
                if (hashStamp[c] != hashGeneration)
                {
                    hashStamp[c] = hashGeneration;
                    hashHead[c] = -1;
                }
                hashNext[i] = hashHead[c];
                hashHead[c] = i;
            }
            for (int i = 0; i < enemies.Length; i++)
            {
                if (!enemies[i].Alive) continue;
                int cx = HashX(wantX[i]), cy = HashY(wantY[i]);
                double ri = roles[enemies[i].Role].RadiusM;
                for (int gy = Math.Max(0, cy - 1); gy <= Math.Min(hashH - 1, cy + 1); gy++)
                    for (int gx = Math.Max(0, cx - 1); gx <= Math.Min(hashW - 1, cx + 1); gx++)
                    {
                        int c = gy * hashW + gx;
                        if (hashStamp[c] != hashGeneration) continue;
                        for (int j = hashHead[c]; j >= 0; j = hashNext[j])
                        {
                            if (j <= i || (!stepping[i] && !stepping[j])) continue;
                            double lim = Separation * (ri + roles[enemies[j].Role].RadiusM);
                            double ox = wantX[i] - wantX[j], oy = wantY[i] - wantY[j];
                            double d2 = ox * ox + oy * oy;
                            if (d2 >= lim * lim) continue;
                            double d = Math.Sqrt(d2);
                            double ux = d > 1e-9 ? ox / d : 1, uy = d > 1e-9 ? oy / d : 0;
                            double wi = !stepping[i] ? 0 : !stepping[j] ? 1 : 0.5;
                            double push = lim - d;
                            wantX[i] += ux * push * wi;
                            wantY[i] += uy * push * wi;
                            wantX[j] -= ux * push * (1 - wi);
                            wantY[j] -= uy * push * (1 - wi);
                        }
                    }
            }
        }

        int HashX(double x) => Math.Max(0, Math.Min(hashW - 1, (int)Math.Floor((x - Nav.MinX) / HashCellM)));
        int HashY(double y) => Math.Max(0, Math.Min(hashH - 1, (int)Math.Floor((y - Nav.MinY) / HashCellM)));
        int HashCell(double x, double y) => HashY(y) * hashW + HashX(x);

        void Land(Simulation sim, double t, bool reliable)
        {
            HeroAt(t, out double hx, out double hy);
            for (int i = 0; i < enemies.Length; i++)
            {
                ref var e = ref enemies[i];
                if (!e.Alive) continue;
                if (e.Motion == EnemyMotion.Step && Math.Abs(e.MoveEnd - t) < 1e-9)
                {
                    e.X = e.ToX;
                    e.Y = e.ToY;
                    e.Motion = EnemyMotion.Still;
                }
                if (e.LungeBeat != beat) continue;
                e.LungeBeat = -1;
                if (!reliable || e.Motion == EnemyMotion.Knock) continue;
                double d = e.SteppedBeat == beat
                    ? SegmentDistance(hx, hy, e.FromX, e.FromY, e.ToX, e.ToY)
                    : Math.Sqrt((hx - e.X) * (hx - e.X) + (hy - e.Y) * (hy - e.Y));
                if (d > ContactOf(e.Role) + BiteSlackM) continue;
                double dx = hx - e.X, dy = hy - e.Y;
                double l = Math.Sqrt(dx * dx + dy * dy);
                if (l < 1e-9)
                {
                    dx = 1;
                    l = 1;
                }
                sim.Emit(SimEvent.Hit(t, -1, (int)EnemyAttackKind.Lunge, roles[e.Role].Power, e.X, e.Y, dx / l, dy / l));
            }
        }

        static double SegmentDistance(double px, double py, double ax, double ay, double bx, double by)
        {
            double dx = bx - ax, dy = by - ay;
            double l2 = dx * dx + dy * dy;
            double u = l2 > 1e-12 ? Math.Max(0, Math.Min(1, ((px - ax) * dx + (py - ay) * dy) / l2)) : 0;
            double ox = ax + dx * u - px, oy = ay + dy * u - py;
            return Math.Sqrt(ox * ox + oy * oy);
        }

        /// <summary>
        /// Knocks enemy <paramref name="i"/> back along (dirX, dirY) at song time
        /// <paramref name="t"/>, at once: its role's knockback times <paramref name="power"/>
        /// (the weapon's), capped by the role's maximum, in units of <see cref="KnockbackM"/>,
        /// stopped short of blocked cells. It cancels a step in progress, and the enemy decides no
        /// step until the knock ends; pursuit resumes on a later main beat.
        /// </summary>
        internal void Knock(Simulation sim, int i, double t, double dirX, double dirY, double power)
        {
            ref var e = ref enemies[i];
            var role = roles[e.Role];
            double units = Math.Min(role.Knockback * power, role.MaxKnockback);
            double l = Math.Sqrt(dirX * dirX + dirY * dirY);
            if (!e.Alive || units <= 0 || l < 1e-9) return;
            e.PositionAt(t, out double x, out double y);
            double dist = KnockbackM * units;
            double toX = x + dirX / l * dist, toY = y + dirY / l * dist;
            Nav.Clamp(x, y, ref toX, ref toY);
            e.X = x;
            e.Y = y;
            e.Motion = EnemyMotion.Knock;
            e.FromX = x;
            e.FromY = y;
            e.ToX = toX;
            e.ToY = toY;
            e.MoveStart = t;
            e.MoveEnd = t + KnockSeconds;
            sim.Emit(SimEvent.EnemyKnock(t, i, x, y, toX, toY, t, t + KnockSeconds));
        }

        /// <summary>The per-tick system, at the tick's end: motion, the flow field's rebuild, spawns and recycling.</summary>
        internal void Tick(Simulation sim, double end)
        {
            for (int i = 0; i < enemies.Length; i++)
            {
                ref var e = ref enemies[i];
                if (!e.Alive || e.Motion == EnemyMotion.Still) continue;
                e.PositionAt(end, out double x, out double y);
                e.X = x;
                e.Y = y;
                if (end >= e.MoveEnd - 1e-9) e.Motion = EnemyMotion.Still;
            }

            // The field leads to the hero's tile: where it stands, or is striding to.
            double tx = state.Hero.X, ty = state.Hero.Y;
            if (!next.Building && Nav.CellIndex(tx, ty) != field.SourceCell) next.Begin(tx, ty);
            if (next.Building && next.Step(FieldBudget))
            {
                var done = next;
                next = field;
                field = done;
            }

            HeroAt(end, out double hx, out double hy);
            if (!state.Frozen)
                while (state.RunClock >= nextSpawn - 1e-9)
                {
                    SpawnWave(sim, end, hx, hy);
                    nextSpawn += spawnInterval;
                }
            Recycle(sim, end, hx, hy);
        }

        /// <summary>
        /// One of each roster role, and whole rosters while fewer than the minimum are alive
        /// (Vampire Survivors' spawning); each role's share lands as one clump on the ring.
        /// </summary>
        void SpawnWave(Simulation sim, double t, double hx, double hy)
        {
            int shortfall = minimum - state.EnemiesAlive - roster.Length;
            int each = 1 + (shortfall > 0 ? (shortfall + roster.Length - 1) / roster.Length : 0);
            foreach (int role in roster)
            {
                int n = Math.Min(each, freeCount);
                if (n == 0) return;
                double clump = Math.Max(1.5, 0.5 * Math.Sqrt(n));
                if (!RingPoint(sim.Random, hx, hy, clump + roles[role].RadiusM, out double cx, out double cy)) continue;
                for (int k = 0; k < n; k++)
                {
                    double x = cx, y = cy;
                    for (int tries = 0; tries < 4; tries++)
                    {
                        double a = sim.Random.Range(-Math.PI, Math.PI), r = clump * Math.Sqrt(sim.Random.NextDouble());
                        double px = cx + Math.Cos(a) * r, py = cy + Math.Sin(a) * r;
                        if (!Nav.Walkable(px, py)) continue;
                        x = px;
                        y = py;
                        break;
                    }
                    Spawn(sim, t, role, x, y);
                }
            }
        }

        internal void Spawn(Simulation sim, double t, int role, double x, double y)
        {
            int i = free[--freeCount];
            enemies[i] = new EnemyState
            {
                Alive = true,
                Role = role,
                X = x,
                Y = y,
                Health = roles[role].Health,
                LungeBeat = -1,
                SteppedBeat = -1,
            };
            state.EnemiesAlive++;
            sim.Emit(SimEvent.Spawn(t, i, role, x, y));
        }

        /// <summary>Enemies standing far out of sight re-enter on the ring with their health (native's recycling).</summary>
        void Recycle(Simulation sim, double t, double hx, double hy)
        {
            double far = RecycleViews * viewWidth;
            for (int i = 0; i < enemies.Length; i++)
            {
                ref var e = ref enemies[i];
                if (!e.Alive || e.Motion != EnemyMotion.Still) continue;
                double dx = e.X - hx, dy = e.Y - hy;
                if (dx * dx + dy * dy < far * far) continue;
                if (!RingPoint(sim.Random, hx, hy, roles[e.Role].RadiusM, out double x, out double y)) continue;
                e.X = x;
                e.Y = y;
                e.LungeBeat = -1;
                sim.Emit(SimEvent.Spawn(t, i, e.Role, x, y, recycled: true));
            }
        }

        /// <summary>
        /// A point just outside the camera's footprint around the hero at (hx, hy), <paramref name="extra"/>
        /// beyond the margin, in a walkable cell: a random direction, retried when the point falls
        /// outside the arena or in a structure, so a visible arena edge leaves the in-bounds arc.
        /// </summary>
        bool RingPoint(SimRandom random, double hx, double hy, double extra, out double x, out double y)
        {
            for (int tries = 0; tries < SpawnTries; tries++)
            {
                double a = random.Range(-Math.PI, Math.PI);
                double c = Math.Cos(a), s = Math.Sin(a);
                double d = FootprintEdge(c, s) + SpawnMarginM + extra + random.Range(0, SpawnSpreadM);
                x = hx + c * d;
                y = hy + s * d;
                if (Nav.Walkable(x, y)) return true;
            }
            x = y = 0;
            return false;
        }

        /// <summary>How far the ray from the hero along (c, s) runs to the footprint's edge.</summary>
        double FootprintEdge(double c, double s)
        {
            double best = 0;
            int n = footprint.Length / 2;
            for (int k = 0; k < n; k++)
            {
                double ax = footprint[2 * k], ay = footprint[2 * k + 1];
                double ex = footprint[2 * ((k + 1) % n)] - ax, ey = footprint[2 * ((k + 1) % n) + 1] - ay;
                double denom = c * ey - s * ex;
                if (Math.Abs(denom) < 1e-12) continue;
                double r = (ax * ey - ay * ex) / denom;
                double u = (ax * s - ay * c) / denom;
                if (r > 0 && u >= -1e-9 && u <= 1 + 1e-9) best = Math.Max(best, r);
            }
            return best;
        }
    }
}
