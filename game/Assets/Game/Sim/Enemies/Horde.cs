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
    ///   (along the whole step for one that stepped) hit the hero (<see cref="HeroHealth"/>).
    /// A beat revealed late takes what is left of these times; one revealed after T is skipped.
    /// Without a reliable beat, or while the world is frozen, enemies hold and lunges stop; on
    /// relock or the thaw they resume on the next main beat, with no catch-up. A decided step is final, as every settled event is: it plants
    /// even if the beat's state drops inside its move window (native cancels it instead).
    ///
    /// Knockback (<see cref="Knock"/>) is the one travel between beats: immediate, a fixed
    /// distance, and the enemy decides no step until it ends. A lethal hit removes the enemy at once
    /// (<see cref="Kill"/>): its death launch is presentation's, and it drops a gem, and a boss its
    /// chest. Who spawns, when and where is the <see cref="Spawner"/>'s; the bosses' telegraphs,
    /// charges and slams are <see cref="BossMoves"/>'; the swarms, set pieces and signature minotaur are
    /// <see cref="MusicEvents"/>', whose enemies step by script (<see cref="Scripts"/>) instead of down
    /// the flow field, unbent by separation. At most the schedule's cap are alive.
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
        /// <summary>Flow-field cells settled per tick while a rebuild runs.</summary>
        public const int FieldBudget = 4096;
        /// <summary>How far out a push off a regrowing structure looks for open ground, in cells (the largest site is 5 tiles across).</summary>
        internal const int PushCells = 16;
        const double HashCellM = 2;

        enum Phase { Notice, Decide, Land }

        readonly BeliefLog log;
        readonly BeatClock clock;
        readonly StrideGrid strides;
        readonly SimState state;
        readonly EnemyRole[] roles;
        readonly double heroReferenceMps;
        readonly double tileM;
        readonly EnemyState[] enemies;
        // A boss's chest, by slot: its tier (0: none) and the minute whose treasure it holds.
        readonly int[] chestTier, chestMinute;
        // Each slot's knockback: its role's, raised by Garlic's hits (native's foe knockback).
        readonly double[] knockback;
        // The spawns so far: each enemy's life (EnemyState.Life).
        int lives;
        readonly Spawner spawner;
        readonly BossMoves bosses;
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
        /// <summary>Counts the chests dropped: a chest's item in its drop.</summary>
        int drops;
        bool navChanged;

        /// <summary>Easy's floor chickens, which a dying brute may drop; null off Easy.</summary>
        internal FloorChickens Chickens;

        public Horde(HordeConfig config, GameContent content, BeliefLog log, BeatClock clock, StrideGrid strides, ArenaGrid arena, SimState state)
        {
            if (content == null) throw new ArgumentException("the horde needs content");
            this.log = log;
            this.clock = clock;
            this.strides = strides;
            this.state = state;
            roles = content.Roles;
            heroReferenceMps = content.Progression.Hero.SpeedMps;
            tileM = arena.TileM;

            int cap = content.Schedule.Cap;
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
            chestTier = new int[cap];
            chestMinute = new int[cap];
            knockback = new double[cap];

            Nav = new NavGrid(arena);
            hashW = (int)Math.Ceiling(Nav.W * Nav.CellM / HashCellM) + 1;
            hashH = (int)Math.Ceiling(Nav.H * Nav.CellM / HashCellM) + 1;
            hashHead = new int[hashW * hashH];
            hashStamp = new int[hashW * hashH];

            spawner = new Spawner(config, content, this, log, state);
            bosses = new BossMoves(content, cap);
            Scripts = new Scripts(cap);
            Music = new MusicEvents(config, content, this, spawner, bosses, log, clock, state);
            // Paths around structures run longer than the straight line to the farthest enemy.
            double reach = 3 * Spawner.RecycleViews * spawner.ViewWidth;
            field = new FlowField(Nav, reach);
            next = new FlowField(Nav, reach);
            field.Begin(state.Hero.X, state.Hero.Y);
            field.BuildAll();
        }

        public NavGrid Nav { get; }
        public EnemyRole[] Roles => roles;
        /// <summary>The music-driven events' hold on their enemies.</summary>
        public Scripts Scripts { get; }
        public MusicEvents Music { get; }
        /// <summary>The flow field the enemies follow now (a rebuild takes over once it completes).</summary>
        public FlowField Field => field;
        /// <summary>Who spawns, when and where; its footprint says what is in view.</summary>
        public Spawner Spawner => spawner;
        /// <summary>The main beat the horde is in (the next to land).</summary>
        public int Beat => beat;
        /// <summary>Free enemy slots: how many more may spawn under the cap.</summary>
        public int FreeSlots => freeCount;

        void HeroAt(double t, out double x, out double y) => state.Hero.PositionAt(t, out x, out y);

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

        internal double ContactOf(int role) => HeroHurtRadiusM + roles[role].RadiusM + ContactGapM;

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
                if (!e.Alive || Knocked(e, t) || bosses.Busy(i, beat)) continue;
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
                sim.Emit(SimEvent.EnemyAttack(t, i, EnemyAttackKind.Lunge, t, impact, dx / d, dy / d));
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
                if (Knocked(e, t) || bosses.Busy(i, beat) || !roles[e.Role].Gait.StepsOn(beat)) continue;
                double step = StepOf(e.Role, beat, h);
                if (Scripts.Scripted(i))
                {
                    Scripts.Target(i, beat, step, hx, hy, x, y, out double sx, out double sy);
                    if (!Scripts.Flies(i)) Nav.Clamp(x, y, ref sx, ref sy);
                    StartStep(sim, i, beat, t, x, y, sx, sy, footplant);
                    continue;
                }
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
            bosses.Decide(sim, this, beat, t, footplant, hx, hy);
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
                StartStep(sim, i, beat, t, x, y, toX, toY, footplant);
            }
        }

        /// <summary>Enemy <paramref name="i"/> steps on main beat <paramref name="beat"/>, from (x, y) at <paramref name="t"/> to the footplant; a step that goes nowhere is none.</summary>
        internal void StartStep(Simulation sim, int i, int beat, double t, double x, double y, double toX, double toY, double footplant)
        {
            if (Math.Abs(toX - x) < 1e-6 && Math.Abs(toY - y) < 1e-6) return;
            ref var e = ref enemies[i];
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
            // A fallen hero is past biting.
            Bite(sim, t, reliable && !state.Hero.Fallen, hx, hy);
            bosses.Land(sim, this, beat, t, reliable, hx, hy);
            if (reliable) bosses.Telegraph(sim, this, beat, t, hx, hy);
            Music.Land(sim, beat, t, reliable, hx, hy);
        }

        /// <summary>Steps plant, and lunges armed for this beat bite a hero in contact while the beat is <paramref name="reliable"/>.</summary>
        void Bite(Simulation sim, double t, bool reliable, double hx, double hy)
        {
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
                sim.Health.Hurt(sim, t, EnemyAttackKind.Lunge, roles[e.Role].Power, dx / l, dy / l);
            }
        }

        internal static double SegmentDistance(double px, double py, double ax, double ay, double bx, double by)
        {
            double dx = bx - ax, dy = by - ay;
            double l2 = dx * dx + dy * dy;
            double u = l2 > 1e-12 ? Math.Max(0, Math.Min(1, ((px - ax) * dx + (py - ay) * dy) / l2)) : 0;
            double ox = ax + dx * u - px, oy = ay + dy * u - py;
            return Math.Sqrt(ox * ox + oy * oy);
        }

        /// <summary>
        /// Knocks enemy <paramref name="i"/> back along (dirX, dirY) at song time
        /// <paramref name="t"/>, at once: its knockback (its role's, plus Garlic's debuff) times <paramref name="power"/>
        /// (the weapon's), capped by the role's maximum, in units of <see cref="KnockbackM"/>,
        /// stopped short of blocked cells. It cancels a step in progress, and the enemy decides no
        /// step until the knock ends; pursuit resumes on a later main beat.
        /// </summary>
        internal void Knock(Simulation sim, int i, double t, double dirX, double dirY, double power)
        {
            ref var e = ref enemies[i];
            var role = roles[e.Role];
            double units = Math.Min(knockback[i] * power, role.MaxKnockback);
            double l = Math.Sqrt(dirX * dirX + dirY * dirY);
            if (!e.Alive || units <= 0 || l < 1e-9) return;
            e.PositionAt(t, out double x, out double y);
            double dist = KnockbackM * units;
            double toX = x + dirX / l * dist, toY = y + dirY / l * dist;
            Nav.Clamp(x, y, ref toX, ref toY);
            StartKnock(sim, i, t, x, y, toX, toY);
        }

        void StartKnock(Simulation sim, int i, double t, double x, double y, double toX, double toY)
        {
            ref var e = ref enemies[i];
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

        /// <summary>
        /// Fills <paramref name="into"/> with the live enemies whose bodies touch the circle of radius
        /// <paramref name="reach"/> around (x, y) at song time <paramref name="t"/>, in slot order, and
        /// returns how many; <paramref name="into"/> holds every slot.
        /// </summary>
        internal int InReach(double t, double x, double y, double reach, Span<int> into)
        {
            int n = 0;
            for (int i = 0; i < enemies.Length; i++)
            {
                if (!enemies[i].Alive) continue;
                enemies[i].PositionAt(t, out double ex, out double ey);
                double dx = ex - x, dy = ey - y;
                if (Math.Sqrt(dx * dx + dy * dy) > reach + 0.5 * roles[enemies[i].Role].RadiusM) continue;
                into[n++] = i;
            }
            return n;
        }

        /// <summary>
        /// The one way the hero's weapons hurt an enemy: enemy <paramref name="i"/> takes
        /// <paramref name="amount"/> from <paramref name="weapon"/> (content's index) at
        /// <paramref name="t"/>, struck along (dirX, dirY). The hit event comes first, then the enemy
        /// dies (<see cref="Kill"/>, thrown along the blow <paramref name="launch"/> metres) or is
        /// knocked back by <paramref name="knockback"/> away from the hero (<see cref="Knock"/>;
        /// native's survivor recoil, whatever the blow's direction; along the blow when on the hero).
        /// The weapon's tally (<see cref="RunRecap.Damage"/>) adds the whole amount; <see cref="NoWeapon"/> tallies nothing.
        /// </summary>
        internal void Damage(Simulation sim, int i, double t, double amount, int weapon, WeaponHit how,
            double dirX, double dirY, double knockback, double launch, bool critical = false)
        {
            ref var e = ref enemies[i];
            if (!e.Alive) return;
            e.PositionAt(t, out double x, out double y);
            e.Health -= amount;
            if (weapon != NoWeapon) state.Recap.Dealt(weapon, amount);
            sim.Emit(SimEvent.EnemyHit(t, i, weapon, how, amount, x, y, dirX, dirY, critical));
            if (e.Health <= 1e-9)
            {
                Kill(sim, i, t, dirX, dirY, launch);
                return;
            }
            HeroAt(t, out double hx, out double hy);
            double ax = x - hx, ay = y - hy;
            if (ax * ax + ay * ay < 1e-18)
            {
                ax = dirX;
                ay = dirY;
            }
            Knock(sim, i, t, ax, ay, knockback);
        }

        /// <summary>
        /// Garlic's debuff (native's): enemy <paramref name="i"/>, hit by the aura and alive, takes
        /// 0.3 more knockback from every later blow while below its role's maximum (the last step may
        /// overshoot it, as native's).
        /// </summary>
        internal void Weaken(int i)
        {
            if (enemies[i].Alive && knockback[i] < roles[enemies[i].Role].MaxKnockback) knockback[i] += GarlicKnockback;
        }

        /// <summary>The knockback each of Garlic's hits adds (native's).</summary>
        public const double GarlicKnockback = 0.3;

        /// <summary>Enemy <paramref name="i"/>'s knockback: its role's, plus Garlic's debuff.</summary>
        internal double KnockbackOf(int i) => knockback[i];

        /// <summary>
        /// Enemy <paramref name="i"/> dies at <paramref name="t"/>, struck along (dirX, dirY): it
        /// leaves the horde at once (its slot is free for a later spawn), with a death event whose
        /// force is <paramref name="launch"/>, the launch's distance in metres, and drops a gem of
        /// its role's experience (<see cref="Gems"/>, with an arsenal); a boss carrying a chest drops
        /// it (<see cref="Chests"/>, with an arsenal). On Easy a boss drops its floor chickens, and a brute may
        /// drop one.
        /// </summary>
        internal void Kill(Simulation sim, int i, double t, double dirX, double dirY, double launch)
        {
            if (!Remove(i, t, out double x, out double y)) return;
            ref var e = ref enemies[i];
            state.Recap.Killed();
            sim.Emit(SimEvent.Death(t, i, e.Role, x, y, dirX, dirY, launch));
            sim.Arsenal?.Gems.Drop(sim, t, x, y, roles[e.Role].Xp);
            if (chestTier[i] > 0)
            {
                if (sim.Chests != null) sim.Chests.Drop(sim, t, x, y, chestTier[i], chestMinute[i]);
                else sim.Emit(SimEvent.Chest(t, drops++, x, y, chestTier[i], chestMinute[i]));
            }
            if (e.Boss) Chickens?.BossDied(sim, t, x, y);
            else if (roles[e.Role].Brute) Chickens?.BruteDied(sim, t, x, y);
        }

        /// <summary>
        /// Enemy <paramref name="i"/> leaves the field alive at <paramref name="t"/>, out of sight: no death,
        /// no drop, its slot free for a later spawn.
        /// </summary>
        internal void Leave(Simulation sim, int i, double t)
        {
            if (Remove(i, t, out double x, out double y)) sim.Emit(SimEvent.Leave(t, i, enemies[i].Role, x, y));
        }

        /// <summary>
        /// Enemy <paramref name="i"/>, if alive, stops where it stands at <paramref name="t"/>, at
        /// (x, y), and frees its slot: the removal a death and a leaving share. False if it was gone.
        /// </summary>
        bool Remove(int i, double t, out double x, out double y)
        {
            ref var e = ref enemies[i];
            if (!e.Alive)
            {
                x = y = 0;
                return false;
            }
            e.PositionAt(t, out x, out y);
            e.Alive = false;
            e.X = x;
            e.Y = y;
            e.Motion = EnemyMotion.Still;
            e.LungeBeat = -1;
            state.EnemiesAlive--;
            free[freeCount++] = i;
            Scripts.Clear(i);
            return true;
        }

        /// <summary>
        /// Room under the cap for <paramref name="n"/> more at <paramref name="t"/>, besides the bosses
        /// waiting: the ordinary enemies (no boss, in no music-driven event) farthest from the hero at
        /// (hx, hy) leave (native's make_room). Returns how many of the n fit.
        /// </summary>
        internal int MakeRoom(Simulation sim, double t, int n, double hx, double hy)
        {
            while (freeCount - spawner.BossesWaiting < n)
            {
                int far = -1;
                double farD = -1;
                for (int i = 0; i < enemies.Length; i++)
                {
                    ref var e = ref enemies[i];
                    if (!e.Alive || e.Boss || Scripts.GroupOf(i) >= 0) continue;
                    e.PositionAt(t, out double x, out double y);
                    double d = (x - hx) * (x - hx) + (y - hy) * (y - hy);
                    if (d > farD)
                    {
                        farD = d;
                        far = i;
                    }
                }
                if (far < 0) break;
                Leave(sim, far, t);
            }
            return Math.Max(0, Math.Min(n, freeCount - spawner.BossesWaiting));
        }

        /// <summary>
        /// The finale's blast at <paramref name="t"/>, centred on the hero at (x, y): every enemy whose
        /// body touches the disc of <paramref name="radius"/> takes <paramref name="damage"/> through
        /// <see cref="Damage"/> (a <see cref="WeaponHit.Blast"/> hit, so the survivors flash and show
        /// their numbers), thrown or knocked straight out from the centre. It is no weapon's, so it
        /// tallies nothing.
        /// </summary>
        internal void Blast(Simulation sim, double t, double x, double y, double radius, double damage)
        {
            for (int i = 0; i < enemies.Length; i++)
            {
                ref var e = ref enemies[i];
                if (!e.Alive) continue;
                e.PositionAt(t, out double ex, out double ey);
                double dx = ex - x, dy = ey - y, d = Math.Sqrt(dx * dx + dy * dy);
                if (d > radius + 0.5 * roles[e.Role].RadiusM) continue;
                if (d < 1e-9)
                {
                    dx = 0;
                    dy = 1;
                }
                Damage(sim, i, t, damage, NoWeapon, WeaponHit.Blast, dx, dy, damage, RunRules.BlastForceM);
            }
        }

        /// <summary>The weapon index of a hit that is no weapon's (the finale's blast): it tallies nothing.</summary>
        public const int NoWeapon = -1;

        /// <summary>
        /// A structure's tiles turn impassable (it regrows) or open (it breaks): the nav grid is
        /// patched at once, and the flow field rebuilds over it, time-sliced, from the next tick.
        /// Until it is done the old field leads, and moves never enter a blocked cell.
        /// </summary>
        internal void PatchNav(Tile[] tiles, bool blocked)
        {
            foreach (var tile in tiles) Nav.SetTile(tile.X, tile.Y, blocked);
            navChanged = true;
        }

        /// <summary>
        /// Enemies a regrowing structure would hold are pushed out at <paramref name="t"/>, as a
        /// forced reaction (a knock): one standing in a blocked cell to the nearest open cell, one
        /// moving into one stopped short of it. The push ignores the blocked cells it crosses, so
        /// an enemy deep inside a site still leaves it.
        /// </summary>
        internal void PushOff(Simulation sim, double t)
        {
            for (int i = 0; i < enemies.Length; i++)
            {
                ref var e = ref enemies[i];
                if (!e.Alive) continue;
                e.PositionAt(t, out double x, out double y);
                double toX = x, toY = y;
                if (!Nav.Walkable(x, y))
                {
                    if (!Nav.NearestOpen(x, y, PushCells, out toX, out toY)) continue;
                }
                else if (e.Motion != EnemyMotion.Still && e.MoveEnd > t + 1e-9 && !Nav.Walkable(e.ToX, e.ToY))
                {
                    toX = e.ToX;
                    toY = e.ToY;
                    Nav.Clamp(x, y, ref toX, ref toY);
                }
                else continue;
                StartKnock(sim, i, t, x, y, toX, toY);
            }
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

            // The field leads to the hero's tile: where it stands, or is striding to. A structure
            // breaking or regrowing restarts the rebuild over the patched grid.
            double tx = state.Hero.X, ty = state.Hero.Y;
            if (navChanged || (!next.Building && Nav.CellIndex(tx, ty) != field.SourceCell))
            {
                next.Begin(tx, ty);
                navChanged = false;
            }
            if (next.Building && next.Step(FieldBudget))
            {
                var done = next;
                next = field;
                field = done;
            }

            HeroAt(end, out double hx, out double hy);
            Music.Tick(sim, end, hx, hy);
            spawner.Tick(sim, end, hx, hy);
        }

        /// <summary>The run clock jumped to <paramref name="runClock"/>: the spawner and the swarms skip the minutes passed.</summary>
        internal void Skip(double runClock)
        {
            spawner.Skip(runClock);
            Music.Swarms.Skip((int)Math.Floor(runClock / 60 + 1e-9));
        }

        /// <summary>The debug surfaces' enemy (<see cref="DebugCommand.SpawnRole"/>): one of <paramref name="role"/> in view at <paramref name="t"/>, if there is room.</summary>
        internal void SpawnInView(Simulation sim, double t, int role)
        {
            if (role < 0 || role >= roles.Length || freeCount == 0) return;
            state.Hero.PositionAt(t, out double hx, out double hy);
            spawner.InViewPoint(sim.Random, hx, hy, out double x, out double y);
            Spawn(sim, t, role, x, y);
        }

        /// <summary>At the sim's start: the lineup, if any, takes its places.</summary>
        internal void Begin(Simulation sim) => spawner.PlaceLineup(sim);

        /// <summary>On a committed downbeat at <paramref name="t"/>: bosses due land on it.</summary>
        internal void Downbeat(Simulation sim, double t) => spawner.Downbeat(sim, t);

        /// <summary>
        /// An enemy of <paramref name="role"/> enters at (x, y) at <paramref name="t"/>, in a free
        /// slot (there must be one). A boss's health, and a level-scaled role's, is its role's
        /// times the hero's level; a boss carries a chest of <paramref name="tier"/> (0 for
        /// none) from minute <paramref name="minute"/>'s treasure. Returns its slot.
        /// </summary>
        internal int Spawn(Simulation sim, double t, int role, double x, double y, bool boss = false,
            int tier = 0, int minute = -1)
        {
            int i = free[--freeCount];
            enemies[i] = new EnemyState
            {
                Alive = true,
                Life = ++lives,
                Role = role,
                X = x,
                Y = y,
                Health = roles[role].Health * (boss || roles[role].LevelScaled ? state.Hero.Level : 1),
                Boss = boss,
                LungeBeat = -1,
                SteppedBeat = -1,
            };
            chestTier[i] = tier;
            chestMinute[i] = minute;
            knockback[i] = roles[role].Knockback;
            bosses.Spawned(i, role, boss, beat);
            Scripts.Clear(i);
            state.EnemiesAlive++;
            sim.Emit(SimEvent.Spawn(t, i, role, x, y, boss: boss));
            return i;
        }
    }
}
