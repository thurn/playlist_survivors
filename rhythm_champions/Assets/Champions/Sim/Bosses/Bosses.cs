using System;

namespace Champions.Sim
{
    /// <summary>Where the boss stands (<see cref="Bosses.Stage"/>).</summary>
    public enum BossStage : byte
    {
        /// <summary>No boss called yet.</summary>
        None = 0,
        /// <summary>Called: spawning stopped, it arrives on its downbeat.</summary>
        Coming = 1,
        /// <summary>Landed: it roars before its first move.</summary>
        Roaring = 2,
        /// <summary>Following the hero between moves.</summary>
        Walking = 3,
        /// <summary>A move's telegraphs fill and land, its charge runs, its dive flies, its breath burns.</summary>
        Acting = 4,
        /// <summary>A full Break staggers it: no moves, double damage.</summary>
        Staggered = 5,
        /// <summary>Killed.</summary>
        Dead = 6,
    }

    /// <summary>
    /// One telegraphed mark of a boss's move (<see cref="Bosses.Marks"/>): the ground its attack covers, filling over its
    /// telegraph and landing on its beat. A move has one (a smash's line) or several (the boulders, the fireballs).
    /// </summary>
    public struct BossMark
    {
        /// <summary>Its number this run (its events').</summary>
        public int Id;
        public BossMove Move;
        /// <summary>Which of its move's marks (a boulder's, a fireball's): 0 for the first.</summary>
        public int Part;
        public Telegraph Telegraph;
        public HitShape Shape;
    }

    /// <summary>
    /// Three retained-horde encounters on the horde clock. Calls stop ordinary spawning; arrivals add a boss without
    /// removing monsters, pickups or structures. Walking patterns and traveling fronts share fixed warning deadlines.
    /// Bosses remain damageable; first two deaths heal then hold for an owned upgrade, the third wins.
    /// LegacyForms is used only by archived combat fixtures (entrance clearing and Break).
    /// </summary>
    public sealed partial class Bosses
    {
        /// <summary>The ticks of positions kept for the client (<see cref="At"/>).</summary>
        public const int History = 128;

        readonly Simulation sim;
        readonly BossRules rules;
        readonly EnemyRules cast;
        readonly LevelBoss[] timeline;
        readonly double heroRadius, maxWalkSpeed, beatlessBeatS;
        readonly StructList<BossMark> marks = new StructList<BossMark>(8);
        readonly double[] trackT = new double[History], trackX = new double[History], trackY = new double[History], trackFacing = new double[History];
        int trackHead, trackCount;
        int nextCall;
        double calledClock;
        double x, y;
        double nextMoveAt;
        Telegraph roar, stagger;
        double lastDodgedBeat = double.NaN;

        internal Bosses(Simulation sim, SimConfig config)
        {
            this.sim = sim;
            rules = config.Bosses;
            cast = config.Enemies;
            timeline = config.Level != null ? config.Level.Bosses : new LevelBoss[0];
            heroRadius = config.Hero.RadiusM;
            var stride = config.Combat.Support(SupportKind.Stride);
            maxWalkSpeed = config.Hero.SpeedMps * (1 + stride.MaxLevel * stride.PerLevel);
            beatlessBeatS = config.Beat.TelegraphBeatlessBeatS;
            sim.Health.Settled += Settled;
            if (config.Duel < 0) return;
            for (int i = 0; i < timeline.Length; i++)
                if (timeline[i].Cast == config.Duel) nextCall = i + 1;
            Call(config.Duel, sim.Now);
        }

        public BossRules Rules => rules;

        public BossStage Stage { get; private set; }

        /// <summary>The boss now (or the latest): its rules, cast index and enemy id; null, -1 and -1 before any.</summary>
        public BossType Type { get; private set; }
        public int Cast => Type != null ? Type.Cast : -1;
        public int Id { get; private set; } = -1;

        /// <summary>Bosses called this run.</summary>
        public int Called { get; private set; }

        /// <summary>An encounter from arrival to death; XP cards may interrupt and preserve its timers.</summary>
        public bool EncounterActive => Stage != BossStage.None && Stage != BossStage.Dead;

        public bool InDuel => Stage >= BossStage.Roaring && Stage < BossStage.Dead;

        /// <summary>Its phase: 1, then 2 below <see cref="BossRules.PhaseTwoBelow"/> of its health.</summary>
        public int Phase { get; private set; } = 1;

        /// <summary>Its Break bar, 0 to 1.</summary>
        public double Break { get; private set; }

        /// <summary>Its stagger's span (NaN when not staggered): from the Break filling to its end as known now.</summary>
        public double StaggerFrom => Stage == BossStage.Staggered ? stagger.Start : double.NaN;
        public double StaggerUntil => Stage == BossStage.Staggered ? sim.Telegraphs.LandTime(stagger) : double.NaN;

        /// <summary>Its health and full health (0 before any).</summary>
        public double Health => Id >= 0 ? sim.Enemies[Id].Health : 0;
        public double MaxHealth { get; private set; }

        /// <summary>The way it faces now (radians from +x).</summary>
        public double Facing { get; private set; } = -Math.PI / 2;

        /// <summary>It walked this tick (the client's walk or idle clip).</summary>
        public bool Walking { get; private set; }

        /// <summary>The move in hand while <see cref="Stage"/> is <see cref="BossStage.Acting"/>.</summary>
        public BossMove Move => move?.Move ?? BossMove.None;

        /// <summary>The marks filling now, not yet landed.</summary>
        public ReadOnlySpan<BossMark> Marks => marks.AsSpan();

        /// <summary>The boss's death (the progression opens the Ogre's treasure; its event says it too).</summary>
        internal event Action<Simulation, BossType> Died;

        /// <summary>Where the boss was at song time <paramref name="t"/>, up to <see cref="History"/> ticks back, and the way it faced.</summary>
        public void At(double t, out double px, out double py, out double facing)
        {
            if (trackCount == 0)
            {
                px = x;
                py = y;
                facing = Facing;
                return;
            }
            // Back from the newest sample to the pair round t; past either end, that end's.
            int j = (trackHead - 1 + History) % History;
            for (int k = 1; k < trackCount; k++)
            {
                int i = (j - 1 + History) % History;
                if (trackT[i] <= t + 1e-9)
                {
                    if (t >= trackT[j]) break;
                    double u = (t - trackT[i]) / (trackT[j] - trackT[i]);
                    px = trackX[i] + (trackX[j] - trackX[i]) * u;
                    py = trackY[i] + (trackY[j] - trackY[i]) * u;
                    facing = u < 0.5 ? trackFacing[i] : trackFacing[j];
                    return;
                }
                j = i;
            }
            px = trackX[j];
            py = trackY[j];
            facing = trackFacing[j];
        }

        /// <summary>Archived fixtures alone can stagger; current encounters always take ordinary damage.</summary>
        internal double DamageScale => Stage == BossStage.Staggered ? rules.BreakDamageScale : 1;

        // ---- Calling and arriving ----

        /// <summary>Calls boss <paramref name="castIndex"/>: the spawner stops and the boss comes.</summary>
        internal void Call(int castIndex, double t)
        {
            var type = rules.Of(castIndex);
            if (type == null) throw new ArgumentException($"cast type {castIndex} has no duel in {BossRules.File}");
            Type = type;
            Stage = BossStage.Coming;
            Id = -1;
            Phase = 1;
            turn = fanTurn = 0;
            Break = 0;
            Called++;
            calledClock = sim.State.RunClock;
            sim.Horde.Spawner?.Stop();
            sim.Emit(SimEvent.BossCall(t, castIndex, calledClock));
        }

        /// <summary>Beat <paramref name="beat"/> fired at <paramref name="t"/>: a called boss arrives on its downbeat.</summary>
        internal void Beat(double t, int beat, bool downbeat)
        {
            if (Stage != BossStage.Coming || sim.State.Held || !downbeat || sim.State.Beat != Reliability.Reliable) return;
            double waited = Waited(t);
            if (waited < rules.WaitS - 1e-9) return;
            if (!sim.Dashes.Rules.LegacyForms || waited >= rules.WindowS - 1e-9 || sim.Beats.StrongBeat(beat)) Arrive(t);
        }

        /// <summary>
        /// The run clock since the call at song time <paramref name="t"/> in the tick being simulated (the clock runs with song
        /// time while nothing holds the world; it was last set at the previous tick's end).
        /// </summary>
        double Waited(double t) => sim.State.RunClock + Math.Max(0, t - sim.Now) - calledClock;

        /// <summary>The level's timeline: the next boss is called at its run clock, once the one before it is dead.</summary>
        void Timeline(double t)
        {
            if (nextCall >= timeline.Length || (Stage != BossStage.None && Stage != BossStage.Dead)) return;
            if ((sim.Dashes.Rules.LegacyForms ? sim.State.RunClock : sim.State.HordeClock) < timeline[nextCall].CallAtS - 1e-9) return;
            Call(timeline[nextCall++].Cast, t);
        }

        void Arrive(double t)
        {
            var type = cast.Cast[Type.Cast];
            sim.HeroWas(t, out double hx, out double hy);
            x = hx;
            y = hy + rules.DistanceM;
            Facing = SimMath.Atan2(hy - y, hx - x);
            int shattered = sim.Dashes.Rules.LegacyForms ? ShatterScreen(t, hx, hy, BreakCause.Entrance) : 0;
            if (sim.Dashes.Rules.LegacyForms) sim.StructureField?.Duel(true);
            Id = sim.Enemies.Spawn(sim, x, y, type.RadiusM, type.Health, Type.Cast + 1, boss: true);
            MaxHealth = type.Health;
            Stage = BossStage.Roaring;
            roar = Plan(t, rules.RoarBeats);
            if (!roar.OnBeat) roar = new Telegraph(t, t + rules.RoarS, -1, -1);
            trackCount = 0;
            Track(t);
            sim.Emit(SimEvent.BossArrive(t, Id, Type.Cast, shattered, x, y, Facing, hx, hy));
            if (Type.WarmZone) sim.Zones.Warm(sim, t);
        }

        /// <summary>
        /// The entrance's shockwave (or the finale's) at <paramref name="t"/>, the hero at (<paramref name="hx"/>,
        /// <paramref name="hy"/>): every mob on screen shatters outward from the boss, the rest go unseen, every elite move
        /// and spore is called off, and the structures within the screen's reach of the boss break outward from it. Returns
        /// the mobs shattered.
        /// </summary>
        int ShatterScreen(double t, double hx, double hy, BreakCause cause)
        {
            // The boss lands up the screen from the hero: this far from it reaches the screen's bottom edge.
            sim.StructureField?.Shatter(sim, HitShape.Circle(x, y, rules.DistanceM + rules.ScreenHalfHeightM), t, cause);
            var enemies = sim.Enemies;
            var horde = sim.Horde;
            horde.Moves.CallOff(t);
            int shattered = 0, n = enemies.Count;
            for (int id = 0; id < n; id++)
            {
                ref readonly var e = ref enemies[id];
                if (!e.Alive || e.Boss) continue;
                horde.PositionAt(id, t, out double ex, out double ey);
                double sx = (ex - hx) / rules.ScreenHalfWidthM, sy = (ey - hy) / rules.ScreenHalfHeightM;
                if (sx * sx + sy * sy > 1)
                {
                    if (horde.Owns(id)) horde.Despawn(id, t);
                    else
                    {
                        enemies.Kill(id);
                        sim.Emit(SimEvent.EnemyGone(t, id, ex, ey));
                    }
                    continue;
                }
                double dx = ex - x, dy = ey - y, d = Math.Sqrt(dx * dx + dy * dy);
                if (d > 1e-9)
                {
                    dx /= d;
                    dy /= d;
                }
                else
                {
                    dx = 0;
                    dy = -1;
                }
                enemies.Kill(id);
                sim.Emit(SimEvent.Kill(t, id, BlowSource.Boss, BlowFlags.None, dx, dy, rules.EntrancePower, ex, ey));
                sim.Progression.Killed(sim, e.Type, t, ex, ey);
                shattered++;
            }
            horde.Moved();
            return shattered;
        }

        // ---- The tick ----

        /// <summary>The tick ending at <paramref name="end"/>: the timeline, arrival without a beat, and the duel.</summary>
        internal void Tick(double end)
        {
            Walking = false;
            if (sim.State.Held) return;
            Timeline(end);
            switch (Stage)
            {
                case BossStage.Coming:
                {
                    double waited = Waited(end);
                    // Beatless, at once; and if no downbeat comes at all, a bar of the beatless fallback past the window.
                    bool beatless = sim.State.Beat != Reliability.Reliable;
                    if (waited >= rules.WaitS - 1e-9 && (beatless || waited >= (sim.Dashes.Rules.LegacyForms ? rules.WindowS + 4 * beatlessBeatS * sim.Beats.Rules.BeatsPerBar : rules.WindowS)))
                        Arrive(end);
                    return;
                }
                case BossStage.Roaring:
                    if (sim.Telegraphs.LandTime(roar) <= end + 1e-9)
                    {
                        Stage = BossStage.Walking;
                        nextMoveAt = end;
                    }
                    break;
                case BossStage.Walking:
                    Follow(end);
                    if (end >= nextMoveAt - 1e-9) Choose(end);
                    break;
                case BossStage.Acting:
                    Act(end);
                    break;
                case BossStage.Staggered:
                {
                    double until = sim.Telegraphs.LandTime(stagger);
                    if (until > end + 1e-9) break;
                    sim.Emit(SimEvent.BossStagger(until, Id, false, stagger.LandBeat, until));
                    Stage = BossStage.Walking;
                    nextMoveAt = until;
                    break;
                }
                default:
                    return;
            }
            if (!InDuel) return;
            ref var e = ref sim.Enemies.Edit(Id);
            if (e.X != x || e.Y != y)
            {
                e.X = x;
                e.Y = y;
                sim.Enemies.Moved();
            }
            Track(end);
        }

        void Track(double t)
        {
            trackT[trackHead] = t;
            trackX[trackHead] = x;
            trackY[trackHead] = y;
            trackFacing[trackHead] = Facing;
            trackHead = (trackHead + 1) % History;
            if (trackCount < History) trackCount++;
        }

        /// <summary>Walks toward the hero until <see cref="BossType.KeepM"/> lies between their bodies.</summary>
        void Follow(double end)
        {
            sim.HeroWas(end, out double hx, out double hy);
            double dx = hx - x, dy = hy - y, d = Math.Sqrt(dx * dx + dy * dy);
            if (d < 1e-9) return;
            Facing = SimMath.Atan2(dy, dx);
            double keep = cast.Cast[Type.Cast].RadiusM + heroRadius + Type.KeepM;
            if (d <= keep + 1e-9) return;
            double step = Math.Min(cast.Cast[Type.Cast].SpeedMps * Simulation.TickSeconds, d - keep);
            x += dx / d * step;
            y += dy / d * step;
            Walking = true;
        }

        /// <summary>Whole-beat warnings with a minimum reaction window and a fixed promised impact time.</summary>
        Telegraph Plan(double from, int beats, bool downbeat = false)
        {
            if (sim.Dashes.Rules.LegacyForms)
                return downbeat ? sim.Telegraphs.PlanToDownbeat(from, beats) : sim.Telegraphs.Plan(from, beats);
            beats = Math.Max(beats, (int)Math.Ceiling(1.1 / BeatS()));
            var planned = downbeat ? sim.Telegraphs.PlanToDownbeat(from, beats) : sim.Telegraphs.Plan(from, beats);
            return new Telegraph(planned.Start, planned.Land, planned.StartBeat, planned.LandBeat, true);
        }

        /// <summary>The current tempo or the half-second fallback.</summary>
        double BeatS()
        {
            int last = sim.State.LastBeat;
            return sim.State.Beat == Reliability.Reliable && last >= 0 ? sim.Beats.TempoAt(last) : beatlessBeatS;
        }

        // ---- Break, phase two and death ----

        /// <summary>A hit on the hero settled: a perfect dodge of the boss's attack fills its Break, and a full bar staggers it.</summary>
        void Settled(Simulation s, HeroHit hit, HitOutcome outcome)
        {
            if (!sim.Dashes.Rules.LegacyForms) return;
            if (outcome != HitOutcome.PerfectDodge || hit.Source != HitSource.Boss || hit.Owner != Id) return;
            if (Stage != BossStage.Acting && Stage != BossStage.Walking) return;
            // One dash dodging several of a move's marks on one beat (the boulders) fills it once.
            if (hit.Beat == lastDodgedBeat) return;
            lastDodgedBeat = hit.Beat;
            double before = Break;
            Break = Math.Min(1, Break + rules.BreakPerDodge);
            double now = sim.Now;
            sim.Emit(SimEvent.BossBreak(now, Id, Break, before));
            if (Break < 1 - 1e-9) return;
            Break = 0;
            CallOff(now);
            Stage = BossStage.Staggered;
            int beat = BeatServices.LastAtOrBefore(sim.Beats.Revealed, hit.Beat + 1e-6);
            double until = beat >= 0 && sim.State.Beat == Reliability.Reliable ? sim.Beats.PredictBeat(beat + rules.StaggerBeats) : double.NaN;
            stagger = double.IsNaN(until) || until <= now
                ? new Telegraph(now, now + rules.StaggerBeatlessS, -1, -1)
                : new Telegraph(now, until, beat, beat + rules.StaggerBeats);
            sim.Emit(SimEvent.BossStagger(now, Id, true, stagger.LandBeat, stagger.Land));
        }

        /// <summary>A hero's blow at <paramref name="t"/> took some of the boss's health: below the share, phase two.</summary>
        internal void Struck(double t)
        {
            if (Phase != 1 || Health >= rules.PhaseTwoBelow * MaxHealth) return;
            Phase = 2;
            sim.Emit(SimEvent.BossPhase(t, Id, Phase));
        }

        /// <summary>A hero's blow at <paramref name="t"/>, thrown (<paramref name="dx"/>, <paramref name="dy"/>), killed the boss.</summary>
        internal void Killed(double t, double dx, double dy)
        {
            CallOff(t);
            Stage = BossStage.Dead;
            Walking = false;
            sim.Emit(SimEvent.BossDeath(t, Id, Type.Cast, Type.Death, x, y, dx, dy));
            sim.Health.HealBossKill(sim);
            sim.StructureField?.Duel(false);
            Died?.Invoke(sim, Type);
            if (Type.Death == BossDeath.Treasure)
            {
                sim.Horde.Spawner?.Resume(secondHalf: true);
                return;
            }
            sim.HeroWas(t, out double hx, out double hy);
            if (sim.Dashes.Rules.LegacyForms) ShatterScreen(t, hx, hy, BreakCause.Boss);
            sim.End(RunEnd.Victory);
        }

        /// <summary>Restore every pending boss timer after a current-run hold.</summary>
        internal void Resume(double elapsed)
        {
            nextMoveAt += elapsed;
            roar = sim.Telegraphs.Resume(roar, elapsed);
            stagger = sim.Telegraphs.Resume(stagger, elapsed);
            for (int i = 0; i < marks.Count; i++)
            {
                var m = marks[i];
                double land = sim.Telegraphs.LandTime(m.Telegraph);
                // A front already traveling freezes in place; only future impacts may round forward to a beat.
                m.Telegraph = IsWave(m.Move) && land <= sim.Now - elapsed + 1e-9
                    ? new Telegraph(m.Telegraph.Start + elapsed, land + elapsed, -1, -1, true)
                    : sim.Telegraphs.Resume(m.Telegraph, elapsed);
                marks[i] = m;
            }
            lastTelegraph = sim.Telegraphs.Resume(lastTelegraph, elapsed);
            flightTelegraph = sim.Telegraphs.Resume(flightTelegraph, elapsed);
            fireFirst = sim.Telegraphs.Resume(fireFirst, elapsed);
            lastLand += elapsed; doneAt += elapsed; moveStart += elapsed; moveEnd += elapsed;
            burnFrom += elapsed; burnUntil += elapsed;
        }

        internal void HeldChanged()
        {
            if (!sim.Dashes.Rules.LegacyForms)
            {
                if (sim.State.Held)
                {
                    roar = sim.Telegraphs.Freeze(roar); stagger = sim.Telegraphs.Freeze(stagger);
                    lastTelegraph = sim.Telegraphs.Freeze(lastTelegraph);
                    flightTelegraph = sim.Telegraphs.Freeze(flightTelegraph);
                    fireFirst = sim.Telegraphs.Freeze(fireFirst);
                    for (int i = 0; i < marks.Count; i++)
                    { var m = marks[i]; m.Telegraph = sim.Telegraphs.Freeze(m.Telegraph); marks[i] = m; }
                }
                return;
            }
            double now = sim.Now;
            if (sim.State.Held)
            {
                if (Stage != BossStage.Acting) return;
                CallOff(now);
                Stage = BossStage.Walking;
                return;
            }
            if (Stage == BossStage.Walking) nextMoveAt = Math.Max(nextMoveAt, now + Type.Rest(Phase) * BeatS());
        }
    }
}
