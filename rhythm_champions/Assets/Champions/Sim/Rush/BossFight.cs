using System;
using System.Collections.Generic;

namespace Champions.Sim
{
    /// <summary>Where a Boss Rush fight stands (<see cref="BossFight.Stage"/>).</summary>
    public enum FightStage : byte
    {
        None = 0,
        /// <summary>Started: the arena forms, the boss's looks warm, and it arrives on a downbeat.</summary>
        Coming = 1,
        /// <summary>Arrived: its entrance, before its first move.</summary>
        Entrance = 2,
        Fighting = 3,
        /// <summary>A phase transition: no hazards of its moves, and it takes no damage.</summary>
        Transition = 4,
        /// <summary>Every body fell.</summary>
        Dead = 5,
    }

    /// <summary>
    /// The Boss Rush fight runtime (boss-rush.md, "Fights"; <see cref="Simulation.Fight"/>): it runs one
    /// <see cref="BossModule"/> at a time over shared building blocks, separately from the main game's
    /// <see cref="Bosses"/> (whose three bosses it leaves unchanged).
    ///
    /// - <b>Bodies</b>: one or more boss enemies, each with its own health (<see cref="Bodies"/>); the fight ends when all fall.
    /// - <b>Hazards</b> (<see cref="Hazard"/>): telegraphs counting whole beats (<see cref="Plan"/>, never under the act's
    ///   minimum), impacts on beats or half beats, lingering, travelling, growing and spinning shapes. They draw as the
    ///   main game's boss marks (<see cref="SimEventKind.BossTelegraph"/>, <see cref="SimEventKind.BossMove"/>), so the
    ///   client's telegraph presenter fills them in the danger red.
    /// - <b>Phases</b>: below each of <see cref="BossModule.PhasesBelow"/> it calls off its hazards and is untouchable for
    ///   <see cref="BossModule.TransitionBeats"/> (at most 4; <see cref="SimEventKind.BossPhase"/>).
    /// - <b>Shedding</b>: each <see cref="BossModule.ShedThresholds"/> step of health lost raises <see cref="Shed"/> on the
    ///   next beat with the module's gem spots (the progression spawns the gems); a kill sheds what's left at once.
    /// - <b>Warm-up</b>: <see cref="Start"/> emits <see cref="SimEventKind.FightCall"/> before the boss arrives, so the
    ///   client warms its looks while the arena forms.
    ///
    /// <code>
    /// sim.Fight.Start(BossModules.Create("bloom"), sim.Now, new FightSetup { Act = 1, Health = 400 });
    /// sim.Fight.Died += (s, f) => ...;     // the mode shell's reward and next arena
    /// sim.Fight.Shed += (s, f, burst, spots) => ...; // the progression's gems
    /// </code>
    /// </summary>
    public sealed class BossFight
    {
        /// <summary>The fights' marks are numbered from here, clear of the main game's bosses' and the elites'.</summary>
        public const int FirstMarkId = 1 << 24;
        /// <summary>The ticks of positions kept per body for the client (<see cref="At"/>).</summary>
        public const int History = 128;
        /// <summary>The shortest a telegraph warns at any tempo (boss-rush.md, "Fair fights").</summary>
        public const double MinTelegraphS = 0.6;

        readonly Simulation sim;
        readonly EnemyRules cast;
        readonly BossRules rules;
        readonly double heroRadius, heroSpeed, beatlessBeatS;
        readonly StructList<Hazard> hazards = new StructList<Hazard>(32);
        readonly List<GemSpot> spots = new List<GemSpot>(16);
        FightBody[] bodies = new FightBody[0];
        Motion[] motions = new Motion[0];
        Track[] tracks = new Track[0];
        double[] phasesBelow = new double[0];
        BodySpec[] specs = new BodySpec[0];
        int nextMark = FirstMarkId;
        int shedDone, shedDue;
        double calledClock, arrivedClock, centreX, centreY, setupHealth;
        Telegraph entrance, transition;

        struct Motion
        {
            public bool On, Eased;
            public double FromX, FromY, ToX, ToY, Start, End;
        }

        sealed class Track
        {
            public readonly double[] T = new double[History], X = new double[History], Y = new double[History], F = new double[History];
            public int Head, Count;
        }

        internal BossFight(Simulation sim, SimConfig config)
        {
            this.sim = sim;
            cast = config.Enemies;
            rules = config.Bosses;
            heroRadius = config.Hero.RadiusM;
            heroSpeed = config.Hero.SpeedMps;
            beatlessBeatS = config.Beat.TelegraphBeatlessBeatS;
        }

        /// <summary>The module fighting now (or last), or null.</summary>
        public BossModule Module { get; private set; }
        public FightStage Stage { get; private set; }
        /// <summary>Fights started this run.</summary>
        public int Started { get; private set; }
        /// <summary>The act it belongs to (its telegraph minimums).</summary>
        public int Act { get; private set; } = 1;
        /// <summary>Its phase: 1, then one more below each of the module's thresholds.</summary>
        public int Phase { get; private set; } = 1;
        /// <summary>The arena's centre.</summary>
        public double CentreX => centreX;
        public double CentreY => centreY;

        public bool Active => Stage != FightStage.None && Stage != FightStage.Dead;
        public bool InFight => Stage >= FightStage.Entrance && Stage < FightStage.Dead;
        /// <summary>
        /// Run-clock seconds since the boss arrived (the Boss Rush clock's; it freezes with cards and pauses), 0 outside a fight.
        /// </summary>
        public double Elapsed => InFight ? Math.Max(0, sim.State.RunClock - arrivedClock) : 0;
        /// <summary>In a phase transition, blows take nothing.</summary>
        public bool Untouchable => Stage == FightStage.Transition;
        internal double DamageScale => Untouchable ? 0 : 1;

        public ReadOnlySpan<FightBody> Bodies => bodies;
        /// <summary>The hazards filling or still dangerous.</summary>
        public ReadOnlySpan<Hazard> Hazards => hazards.AsSpan();

        /// <summary>The first standing body (the last to fall once all have).</summary>
        public ref readonly FightBody Lead
        {
            get
            {
                for (int i = 0; i < bodies.Length; i++) if (bodies[i].Alive) return ref bodies[i];
                return ref bodies[bodies.Length - 1];
            }
        }

        public double Health
        {
            get
            {
                double h = 0;
                foreach (var b in bodies) if (b.Alive) h += sim.Enemies[b.Enemy].Health;
                return h;
            }
        }

        public double MaxHealth
        {
            get
            {
                double h = 0;
                foreach (var b in bodies) h += b.MaxHealth;
                return h;
            }
        }

        /// <summary>The health left as a share of the whole, 1 before the arrival.</summary>
        public double HealthShare => bodies.Length == 0 || Stage < FightStage.Entrance ? 1 : Health / MaxHealth;

        /// <summary>The fight's generator: the sim's one.</summary>
        public SimRandom Random => sim.Random;
        public Simulation Sim => sim;
        /// <summary>The main game's boss content (moves a module reuses, as <see cref="OgreModule"/> does).</summary>
        public BossRules BossRules => rules;
        public EnemyRules Cast => cast;
        public double Now => sim.Now;
        public double HeroRadius => heroRadius;
        /// <summary>The hero's base walk (no Stride): what "walking can't outrun it" means.</summary>
        public double HeroSpeedMps => heroSpeed;

        /// <summary>Every body fell (the mode shell's reward and next arena).</summary>
        public event Action<Simulation, BossFight> Died;
        /// <summary>An XP burst: its number (0 first) and the module's gem spots, valid during the call, at <see cref="ShedAt"/>.</summary>
        public event Action<Simulation, BossFight, int, IReadOnlyList<GemSpot>> Shed;

        /// <summary>Whether enemy <paramref name="enemy"/> is one of this fight's bodies.</summary>
        public bool Owns(int enemy) => BodyOf(enemy) >= 0;

        int BodyOf(int enemy)
        {
            if (Stage < FightStage.Entrance) return -1;
            for (int i = 0; i < bodies.Length; i++) if (bodies[i].Enemy == enemy) return i;
            return -1;
        }

        // ---- Starting and arriving ----

        /// <summary>
        /// Starts <paramref name="module"/> at <paramref name="t"/>: it arrives on the first downbeat at least
        /// <see cref="BossRules.WaitS"/> on (beatless then, or at <see cref="BossRules.WindowS"/> whatever the beat).
        /// </summary>
        public void Start(BossModule module, double t, FightSetup setup = null)
        {
            if (module == null) throw new ArgumentNullException(nameof(module));
            if (Active) throw new InvalidOperationException($"a fight ({Module.Id}) is on");
            setup = setup ?? new FightSetup();
            if (setup.Act < 1 || setup.Act > 5) throw new ArgumentException("act 1 to 5");
            if (module.TransitionBeats < 1 || module.TransitionBeats > 4) throw new ArgumentException($"{module.Id}: a transition of 1 to 4 beats");
            if (module.ShedThresholds < 0) throw new ArgumentException($"{module.Id}: shed thresholds at least 0");
            specs = module.Bodies;
            if (specs.Length == 0) throw new ArgumentException($"{module.Id}: at least one body");
            phasesBelow = (double[])module.PhasesBelow.Clone();
            for (int i = 0; i < phasesBelow.Length; i++)
                if (!(phasesBelow[i] > 0 && phasesBelow[i] < 1) || (i > 0 && !(phasesBelow[i] < phasesBelow[i - 1])))
                    throw new ArgumentException($"{module.Id}: phase thresholds descending within (0, 1)");
            foreach (var s in specs)
            {
                int c = cast.IndexOf(s.Cast);
                if (c < 0 || !cast.Cast[c].IsBoss) throw new ArgumentException($"{module.Id}: {s.Cast} is no boss in {EnemyRules.File}");
                if (!(s.Share > 0)) throw new ArgumentException($"{module.Id}: body shares positive");
            }
            Module = module;
            Act = setup.Act;
            Phase = 1;
            shedDone = shedDue = 0;
            hazards.Clear();
            sim.HeroWas(t, out double hx, out double hy);
            centreX = double.IsNaN(setup.CentreX) ? hx : setup.CentreX;
            centreY = double.IsNaN(setup.CentreY) ? hy : setup.CentreY;
            setupHealth = setup.Health;
            bodies = new FightBody[0];
            Stage = FightStage.Coming;
            Started++;
            calledClock = sim.State.RunClock;
            sim.Emit(SimEvent.FightCall(t, cast.IndexOf(specs[0].Cast), specs.Length, centreX, centreY));
        }

        /// <summary>Starts boss <paramref name="id"/> (<see cref="BossModules"/>).</summary>
        public void Start(string id, double t, FightSetup setup = null) => Start(BossModules.Create(id), t, setup);

        double Waited(double t) => sim.State.RunClock + Math.Max(0, t - sim.Now) - calledClock;

        void Arrive(double t)
        {
            double shares = 0;
            foreach (var s in specs) shares += s.Share;
            bodies = new FightBody[specs.Length];
            motions = new Motion[specs.Length];
            tracks = new Track[specs.Length];
            sim.HeroWas(t, out double hx, out double hy);
            for (int i = 0; i < specs.Length; i++)
            {
                var s = specs[i];
                int c = cast.IndexOf(s.Cast);
                var type = cast.Cast[c];
                double health = double.IsNaN(setupHealth) ? type.Health : setupHealth * s.Share / shares;
                double x = centreX + s.X, y = centreY + s.Y;
                int enemy = sim.Enemies.Spawn(sim, x, y, type.RadiusM, health, c + 1, boss: true);
                bodies[i] = new FightBody
                {
                    Enemy = enemy, Cast = c, X = x, Y = y, Facing = SimMath.Atan2(hy - y, hx - x), MaxHealth = health, Alive = true,
                };
                tracks[i] = new Track();
                sim.Emit(SimEvent.BossArrive(t, enemy, c, 0, x, y, bodies[i].Facing, hx, hy));
            }
            for (int i = 0; i < bodies.Length; i++) Record(i, t);
            Stage = FightStage.Entrance;
            arrivedClock = sim.State.RunClock;
            entrance = sim.Telegraphs.Plan(t, Module.EntranceBeats);
            Module.Begin(this, t);
        }

        // ---- The tick and the beat ----

        internal void Beat(double t, int beat, bool downbeat)
        {
            if (sim.State.Held) return;
            if (Stage == FightStage.Coming)
            {
                if (downbeat && sim.State.Beat == Reliability.Reliable && Waited(t) >= rules.WaitS - 1e-9) Arrive(t);
                return;
            }
            if (!InFight) return;
            ShedDue(t);
            Module.Beat(this, t, beat, downbeat);
        }

        internal void Tick(double end)
        {
            for (int i = 0; i < bodies.Length; i++) bodies[i].Walking = false;
            if (sim.State.Held || Stage == FightStage.None || Stage == FightStage.Dead) return;
            if (Stage == FightStage.Coming)
            {
                double waited = Waited(end);
                if (waited >= rules.WaitS - 1e-9 && (sim.State.Beat != Reliability.Reliable || waited >= rules.WindowS - 1e-9)) Arrive(end);
                return;
            }
            if (sim.State.Beat != Reliability.Reliable) ShedDue(end);
            if (Stage == FightStage.Entrance && entrance.Land <= end + 1e-9) Stage = FightStage.Fighting;
            if (Stage == FightStage.Transition && transition.Land <= end + 1e-9) Stage = FightStage.Fighting;
            if (Stage == FightStage.Fighting) Module.Tick(this, end);
            if (Stage == FightStage.Dead) return;
            LandHazards(end);
            Move(end);
            for (int i = 0; i < bodies.Length; i++)
            {
                if (!bodies[i].Alive) continue;
                ref var e = ref sim.Enemies.Edit(bodies[i].Enemy);
                if (e.X != bodies[i].X || e.Y != bodies[i].Y)
                {
                    e.X = bodies[i].X;
                    e.Y = bodies[i].Y;
                    sim.Enemies.Moved();
                }
                Record(i, end);
            }
        }

        void LandHazards(double end)
        {
            int kept = 0;
            for (int i = 0; i < hazards.Count; i++)
            {
                var h = hazards[i];
                double land = h.Telegraph.Land;
                if (!h.Landed && land <= end + 1e-9)
                {
                    h.Landed = true;
                    double bx = h.Body >= 0 ? bodies[h.Body].X : h.Shape.X, by = h.Body >= 0 ? bodies[h.Body].Y : h.Shape.Y;
                    if (h.BodyMove == HazardBodyMove.AtLand && h.Body >= 0) MoveBody(h.Body, h.ToX, h.ToY, land, land + h.MoveS, true);
                    var mark = h.Mark;
                    sim.Emit(SimEvent.BossMove(land, mark, false, bx, by, h.BodyMove != HazardBodyMove.None ? h.ToX : bx,
                        h.BodyMove != HazardBodyMove.None ? h.ToY : by, Math.Max(h.ActiveS, h.MoveS), 0));
                    if (Reaches(h.ShapeAt(land), land)) Hurt(ref h, land, land);
                    sim.StructureField?.Shatter(sim, h.Shape, land, BreakCause.Boss);
                    hazards[i] = h;
                    Module.Landed(this, h, land);
                    if (Stage == FightStage.Dead) return;
                    h = hazards[i];
                }
                if (h.Landed && h.ActiveS > 0 && !h.HitHero)
                {
                    double at = Math.Min(end, h.Until);
                    if (at > land + 1e-9 && Reaches(h.ShapeAt(at), at)) Hurt(ref h, at, double.NaN);
                }
                if (!h.Landed || end < h.Until - 1e-9) hazards[kept++] = h;
            }
            while (hazards.Count > kept) hazards.RemoveLast();
        }

        bool Reaches(in HitShape shape, double t)
        {
            sim.HeroWas(t, out double hx, out double hy);
            return shape.Touches(hx, hy, heroRadius);
        }

        void Hurt(ref Hazard h, double t, double beat)
        {
            h.HitHero = true;
            int owner = h.Body >= 0 ? bodies[h.Body].Enemy : -1;
            double fx = h.Body >= 0 ? bodies[h.Body].X : h.Shape.X, fy = h.Body >= 0 ? bodies[h.Body].Y : h.Shape.Y;
            sim.Health.Hurt(sim, new HeroHit { T = t, Damage = h.Damage, Source = HitSource.Boss, Owner = owner, Beat = beat, FromX = fx, FromY = fy });
        }

        void Move(double end)
        {
            for (int i = 0; i < bodies.Length; i++)
            {
                ref var m = ref motions[i];
                if (!m.On || !bodies[i].Alive) continue;
                double u = m.End > m.Start ? Math.Max(0, Math.Min(1, (end - m.Start) / (m.End - m.Start))) : 1;
                if (end < m.Start - 1e-9) continue;
                double f = m.Eased ? Horde.Ease(u) : u;
                bodies[i].X = m.FromX + (m.ToX - m.FromX) * f;
                bodies[i].Y = m.FromY + (m.ToY - m.FromY) * f;
                bodies[i].Walking = true;
                if (u >= 1) m.On = false;
            }
        }

        void Record(int i, double t)
        {
            var tr = tracks[i];
            tr.T[tr.Head] = t;
            tr.X[tr.Head] = bodies[i].X;
            tr.Y[tr.Head] = bodies[i].Y;
            tr.F[tr.Head] = bodies[i].Facing;
            tr.Head = (tr.Head + 1) % History;
            if (tr.Count < History) tr.Count++;
        }

        /// <summary>Where body <paramref name="body"/> was at song time <paramref name="t"/>, up to <see cref="History"/> ticks back, and its facing.</summary>
        public void At(int body, double t, out double x, out double y, out double facing)
        {
            var tr = tracks[body];
            x = bodies[body].X;
            y = bodies[body].Y;
            facing = bodies[body].Facing;
            if (tr.Count == 0) return;
            int j = (tr.Head - 1 + History) % History;
            for (int k = 1; k < tr.Count; k++)
            {
                int i = (j - 1 + History) % History;
                if (tr.T[i] <= t + 1e-9)
                {
                    if (t >= tr.T[j]) break;
                    double u = (t - tr.T[i]) / (tr.T[j] - tr.T[i]);
                    x = tr.X[i] + (tr.X[j] - tr.X[i]) * u;
                    y = tr.Y[i] + (tr.Y[j] - tr.Y[i]) * u;
                    facing = u < 0.5 ? tr.F[i] : tr.F[j];
                    return;
                }
                j = i;
            }
            x = tr.X[j];
            y = tr.Y[j];
            facing = tr.F[j];
        }

        /// <summary>The body index of enemy <paramref name="enemy"/>, or -1 (the client's).</summary>
        public int IndexOf(int enemy) => BodyOf(enemy);

        // ---- Building blocks for the modules ----

        /// <summary>The act's least telegraph: 3 beats in acts 1 and 2, 2 later (boss-rush.md, "Fair fights").</summary>
        public int MinTelegraphBeats => Act <= 2 ? 3 : 2;

        /// <summary>The beat's length now, or the half-second beatless fallback.</summary>
        public double BeatS
        {
            get
            {
                int last = sim.State.LastBeat;
                return sim.State.Beat == Reliability.Reliable && last >= 0 ? sim.Beats.TempoAt(last) : beatlessBeatS;
            }
        }

        /// <summary>
        /// A telegraph filling over <paramref name="beats"/> whole beats from the first beat at or after
        /// <paramref name="from"/> (NaN: now), landing on that beat, half a beat later, or on the next downbeat. Never fewer
        /// beats than the act's minimum or <see cref="MinTelegraphS"/>; its landing is promised (fixed) when planned.
        /// </summary>
        public Telegraph Plan(int beats, Landing landing = Landing.Beat, double from = double.NaN)
        {
            if (double.IsNaN(from)) from = sim.Now;
            double beatS = BeatS;
            beats = Math.Max(beats, Math.Max(MinTelegraphBeats, (int)Math.Ceiling(MinTelegraphS / beatS - 1e-9)));
            var p = landing == Landing.Downbeat ? sim.Telegraphs.PlanToDownbeat(from, beats) : sim.Telegraphs.Plan(from, beats);
            double land = p.Land;
            if (landing == Landing.HalfBeat)
            {
                double next = p.OnBeat ? sim.Beats.PredictBeat(p.LandBeat + 1) : double.NaN;
                land += double.IsNaN(next) || next <= land ? beatS / 2 : (next - land) / 2;
            }
            return new Telegraph(p.Start, land, p.StartBeat, p.LandBeat, true);
        }

        /// <summary>A fresh attack number: give one to every hazard of one attack (the fairness harness checks attacks whole).</summary>
        public int NextAttack() => nextAttack++;
        int nextAttack;

        /// <summary>Lays <paramref name="h"/> (its <see cref="Hazard.Id"/> set here) and returns its id.</summary>
        public int Add(Hazard h)
        {
            if (!InFight) throw new InvalidOperationException("no fight to lay a hazard in");
            if (h.Body >= bodies.Length || (h.Body >= 0 && !bodies[h.Body].Alive)) throw new ArgumentException("no such standing body");
            if (h.Move == BossMove.None) h.Move = BossMove.Hazard;
            h.Id = nextMark++;
            h.Landed = h.HitHero = false;
            if (h.BodyMove == HazardBodyMove.DuringTelegraph && h.Body >= 0)
                MoveBody(h.Body, h.ToX, h.ToY, h.Telegraph.Start, h.Telegraph.Land, false);
            hazards.Add(h);
            sim.Emit(SimEvent.BossTelegraph(h.Mark));
            return h.Id;
        }

        /// <summary>Whether any hazard of attack <paramref name="attack"/> is still filling or dangerous.</summary>
        public bool Pending(int attack)
        {
            for (int i = 0; i < hazards.Count; i++) if (hazards[i].Attack == attack) return true;
            return false;
        }

        /// <summary>The latest end of danger among the hazards laid, or NaN with none.</summary>
        public double BusyUntil
        {
            get
            {
                double until = double.NaN;
                for (int i = 0; i < hazards.Count; i++) until = double.IsNaN(until) ? hazards[i].Until : Math.Max(until, hazards[i].Until);
                return until;
            }
        }

        /// <summary>Calls off at <paramref name="t"/> every hazard not yet landed (of body <paramref name="body"/> alone, unless -2).</summary>
        public void CallOff(double t, int body = -2)
        {
            int kept = 0;
            for (int i = 0; i < hazards.Count; i++)
            {
                var h = hazards[i];
                if (body == -2 || h.Body == body)
                {
                    if (!h.Landed) sim.Emit(SimEvent.BossMove(t, h.Mark, true, h.Shape.X, h.Shape.Y, h.Shape.X, h.Shape.Y, 0, 0));
                    continue;
                }
                hazards[kept++] = h;
            }
            while (hazards.Count > kept) hazards.RemoveLast();
            for (int i = 0; i < motions.Length; i++) if (body == -2 || i == body) motions[i].On = false;
        }

        /// <summary>Where the hero was at <paramref name="t"/> (the re-walked path).</summary>
        public void HeroAt(double t, out double x, out double y) => sim.HeroWas(t, out x, out y);

        /// <summary>Moves body <paramref name="body"/> to (<paramref name="x"/>, <paramref name="y"/>) from <paramref name="start"/> to <paramref name="end"/>.</summary>
        public void MoveBody(int body, double x, double y, double start, double end, bool eased = false)
        {
            motions[body] = new Motion
            {
                On = true, Eased = eased, FromX = bodies[body].X, FromY = bodies[body].Y, ToX = x, ToY = y, Start = start, End = end,
            };
            double dx = x - bodies[body].X, dy = y - bodies[body].Y;
            if (dx * dx + dy * dy > 1e-12) bodies[body].Facing = SimMath.Atan2(dy, dx);
        }

        /// <summary>Whether body <paramref name="body"/> is mid-move.</summary>
        public bool Moving(int body) => motions[body].On;

        /// <summary>Turns body <paramref name="body"/> to face (<paramref name="x"/>, <paramref name="y"/>).</summary>
        public void Face(int body, double x, double y)
        {
            double dx = x - bodies[body].X, dy = y - bodies[body].Y;
            if (dx * dx + dy * dy > 1e-12) bodies[body].Facing = SimMath.Atan2(dy, dx);
        }

        /// <summary>
        /// Walks body <paramref name="body"/> a tick toward the hero, at its cast speed, until <paramref name="keepM"/> lies
        /// between their bodies; returns whether it is that close.
        /// </summary>
        public bool Follow(int body, double end, double keepM)
        {
            ref var b = ref bodies[body];
            if (!b.Alive || motions[body].On) return false;
            sim.HeroWas(end, out double hx, out double hy);
            double dx = hx - b.X, dy = hy - b.Y, d = Math.Sqrt(dx * dx + dy * dy);
            if (d < 1e-9) return true;
            b.Facing = SimMath.Atan2(dy, dx);
            var type = cast.Cast[b.Cast];
            double keep = type.RadiusM + heroRadius + keepM;
            if (d <= keep + 1e-9) return true;
            double step = Math.Min(type.SpeedMps * Simulation.TickSeconds, d - keep);
            b.X += dx / d * step;
            b.Y += dy / d * step;
            b.Walking = true;
            return d - step <= keep + 1e-9;
        }

        /// <summary>Body <paramref name="body"/>'s cast row.</summary>
        public EnemyType TypeOf(int body) => cast.Cast[bodies[body].Cast];

        /// <summary>Body <paramref name="body"/> to change (a module's own motion).</summary>
        public ref FightBody Body(int body) => ref bodies[body];

        // ---- Damage, phases, shedding and death ----

        /// <summary>A hero's blow at <paramref name="t"/> took some of body enemy <paramref name="enemy"/>'s health.</summary>
        internal void Struck(int enemy, double t)
        {
            if (BodyOf(enemy) < 0) return;
            Thresholds();
            if (Stage == FightStage.Fighting || Stage == FightStage.Entrance)
                if (Phase - 1 < phasesBelow.Length && HealthShare < phasesBelow[Phase - 1]) Transition(t);
        }

        void Transition(double t)
        {
            CallOff(t);
            Phase++;
            Stage = FightStage.Transition;
            var p = sim.Telegraphs.Plan(t, Module.TransitionBeats);
            transition = new Telegraph(p.Start, p.Land, p.StartBeat, p.LandBeat, true);
            sim.Emit(SimEvent.BossPhase(t, Lead.Enemy, Phase));
            Module.PhaseBegins(this, Phase, t, transition.Land);
        }

        /// <summary>The bursts the health lost so far has earned.</summary>
        void Thresholds()
        {
            int n = Module.ShedThresholds;
            if (n == 0) return;
            double lost = 1 - HealthShare;
            int due = Math.Min(n, (int)Math.Floor(lost * (n + 1) + 1e-9));
            if (due > shedDue) shedDue = due;
        }

        /// <summary>The song time of the burst <see cref="Shed"/> is raising (its beat, or the kill's).</summary>
        public double ShedAt { get; private set; }

        void ShedDue(double t)
        {
            ShedAt = t;
            while (shedDone < shedDue)
            {
                spots.Clear();
                Module.ShedSpots(this, shedDone, t, spots);
                Shed?.Invoke(sim, this, shedDone, spots);
                shedDone++;
            }
        }

        /// <summary>A hero's blow at <paramref name="t"/>, thrown (<paramref name="dx"/>, <paramref name="dy"/>), felled body enemy <paramref name="enemy"/>.</summary>
        internal void Killed(int enemy, double t, double dx, double dy)
        {
            int i = BodyOf(enemy);
            if (i < 0) return;
            ref var b = ref bodies[i];
            b.Alive = false;
            b.Walking = false;
            CallOff(t, i);
            sim.Emit(SimEvent.BossDeath(t, enemy, b.Cast, BossDeath.Treasure, b.X, b.Y, dx, dy));
            foreach (var other in bodies)
                if (other.Alive)
                {
                    Thresholds();
                    Module.BodyDied(this, i, t);
                    return;
                }
            shedDue = Module.ShedThresholds;
            ShedDue(t);
            CallOff(t);
            Stage = FightStage.Dead;
            sim.BossRush?.BossDied(t, b.X, b.Y);
            Died?.Invoke(sim, this);
        }

        // ---- Holds ----

        /// <summary>Shifts every pending time by the hold's <paramref name="elapsed"/>, each landing kept on the beat grid.</summary>
        internal void Resume(double elapsed)
        {
            if (!Active) return;
            entrance = Shift(entrance, elapsed, false);
            transition = Shift(transition, elapsed, false);
            for (int i = 0; i < hazards.Count; i++)
            {
                var h = hazards[i];
                // A hazard already landed keeps travelling from where it froze; a future one rounds onto the grid.
                h.Telegraph = Shift(h.Telegraph, elapsed, !h.Landed);
                hazards[i] = h;
            }
            for (int i = 0; i < motions.Length; i++)
            {
                motions[i].Start += elapsed;
                motions[i].End += elapsed;
            }
        }

        Telegraph Shift(in Telegraph tel, double elapsed, bool snap)
        {
            double start = tel.Start + elapsed, land = tel.Land + elapsed;
            if (snap) land = OnGrid(land);
            return new Telegraph(start, land, tel.StartBeat, tel.LandBeat, true);
        }

        /// <summary>The first beat or half beat at or after <paramref name="t"/> (itself beatless).</summary>
        double OnGrid(double t)
        {
            if (!sim.Beats.Reliable(t)) return t;
            double next = sim.Beats.NextBeat(t, out int i);
            if (double.IsNaN(next)) return t;
            double prev = i > 0 ? sim.Beats.PredictBeat(i - 1) : double.NaN;
            if (!double.IsNaN(prev) && prev < next)
            {
                double mid = (prev + next) / 2;
                if (mid >= t - 1e-9) return mid;
            }
            return next;
        }

        internal ulong Fold(ulong h)
        {
            h = Hashing.Fold(h, (ulong)Stage | (ulong)(uint)Phase << 8 | (ulong)(uint)hazards.Count << 16 | (ulong)(uint)Started << 32);
            h = Hashing.Fold(h, Hashing.Bits(Stage >= FightStage.Entrance ? Health : 0));
            for (int i = 0; i < bodies.Length; i++)
            {
                h = Hashing.Fold(h, Hashing.Bits(bodies[i].X));
                h = Hashing.Fold(h, Hashing.Bits(bodies[i].Y));
            }
            return h;
        }
    }
}
