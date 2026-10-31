using System;
using PS.Game.Sim.Content;

namespace PS.Game.Sim
{
    /// <summary>The music-driven events, the kind in set-piece events.</summary>
    public enum SetPieceKind
    {
        BatSwarm = 0,
        GhostSwarm = 1,
        FlowerWall = 2,
        Stampede = 3,
        KickLine = 4,
        Wedge = 5,
        Ring = 6,
        Signature = 7,
    }

    /// <summary>A music-driven event's enemies in play: one swarm or set piece (<see cref="MusicEvents"/>).</summary>
    internal sealed class MusicGroup
    {
        public int Id = -1;
        public SetPieceKind Kind;
        public readonly int[] Members;
        public int Count;
        /// <summary>The run clock it ends at.</summary>
        public double Until;
        /// <summary>A set piece's phase (<see cref="Pieces"/>), and the reliable main beats it has been in it.</summary>
        public int Phase, Beats;
        /// <summary>The stampede's heading along x: 1 east, -1 west.</summary>
        public double Dir;

        public MusicGroup(int capacity) => Members = new int[capacity];
        public bool Active => Id >= 0;
    }

    /// <summary>
    /// The music-driven events (unity-game.md, Music-Driven Events): the minutes' swarms
    /// (<see cref="Swarms"/>), the musical set pieces (<see cref="Pieces"/>) and the signature minotaur
    /// (<see cref="SignatureBoss"/>). The zones are <see cref="Zones"/>'; the camera's zoom by loudness
    /// is the client's, from the stream's ticks (its fast lane).
    ///
    /// They play in a run (<see cref="HordeConfig.MusicEvents"/>); the debug commands
    /// (<see cref="DebugCommand.Stampede"/> and on) start one in any run with enemies. Everything happens
    /// on main beats (the horde's land time, after its steps plant) and at tick ends, never while the
    /// world is frozen, and nothing starts after the run clock's end.
    ///
    /// Their enemies are the horde's, within its cap: a start makes room by sending the ordinary enemies
    /// farthest from the hero away (<see cref="Horde.MakeRoom"/>). They move by script (<see cref="Scripts"/>)
    /// until their event ends; then those out of sight leave (<see cref="SimEventKind.Leave"/>) and those in
    /// sight join the horde's pursuit, so nothing vanishes on screen.
    /// </summary>
    internal sealed class MusicEvents
    {
        /// <summary>A set piece waiting for its beat gives up this long after it was due.</summary>
        public const double PendingS = 2;

        readonly MusicGroup[] groups;
        int nextId;

        public MusicEvents(HordeConfig config, GameContent content, Horde horde, Spawner spawner, BossMoves bosses, BeliefLog log, BeatClock clock, SimState state)
        {
            Horde = horde;
            Spawner = spawner;
            Log = log;
            Clock = clock;
            State = state;
            Content = content;
            Schedule = content.Schedule;
            Natural = config.MusicEvents;
            int largest = Schedule.SetPieces.GroupCap;
            foreach (var m in Schedule.Minutes)
                foreach (var e in m.Events)
                    largest = Math.Max(largest, e.Count);
            // The swarms overlap (minute 13's ghosts every 1.2 s for 10 s each), and one set piece and the minotaur.
            groups = new MusicGroup[24];
            for (int i = 0; i < groups.Length; i++) groups[i] = new MusicGroup(largest);
            Swarms = new Swarms(this);
            Pieces = new Pieces(this);
            Signature = new SignatureBoss(this, bosses);
        }

        public Horde Horde { get; }
        public Spawner Spawner { get; }
        public BeliefLog Log { get; }
        public BeatClock Clock { get; }
        public SimState State { get; }
        public GameContent Content { get; }
        public Schedule Schedule { get; }
        /// <summary>The events come of the music and the run clock (a run's); otherwise only when forced.</summary>
        public bool Natural { get; }
        public Swarms Swarms { get; }
        public Pieces Pieces { get; }
        public SignatureBoss Signature { get; }

        /// <summary>The run plays on: not frozen, its clock short of its end.</summary>
        public bool Playing => !State.Frozen && RunClock < Schedule.DurationS - 1e-9;

        /// <summary>The run clock now: at the beat or tick end being simulated.</summary>
        public double RunClock { get; private set; }

        /// <summary>On main beat <paramref name="beat"/>'s footplant at <paramref name="t"/>, its steps planted: the events' beat.</summary>
        public void Land(Simulation sim, int beat, double t, bool reliable, double hx, double hy)
        {
            RunClock = sim.Run.ClockAt(t);
            if (!Playing) return;
            Swarms.Land(sim, beat, t, reliable, hx, hy);
            Signature.Land(sim, beat, t, reliable, hx, hy);
            Pieces.Land(sim, beat, t, reliable, hx, hy);
        }

        /// <summary>At the tick's end: the minutes' swarms arm and unlock, and events past their time end.</summary>
        public void Tick(Simulation sim, double end, double hx, double hy)
        {
            RunClock = State.RunClock;
            if (!Playing) return;
            Swarms.Tick(sim, end, hx, hy);
            Signature.Tick(sim, end, hx, hy);
            foreach (var g in groups)
                if (g.Active && RunClock >= g.Until - 1e-9) End(sim, g, end, hx, hy);
        }

        /// <summary>A debug command's event, now: it starts on the next main beat.</summary>
        public void Force(DebugCommand command, double now)
        {
            switch (command)
            {
                case DebugCommand.Stampede: Pieces.Force(SetPieceKind.Stampede, now); break;
                case DebugCommand.KickLine: Pieces.Force(SetPieceKind.KickLine, now); break;
                case DebugCommand.Wedge: Pieces.Force(SetPieceKind.Wedge, now); break;
                case DebugCommand.Ring: Pieces.Force(SetPieceKind.Ring, now); break;
                case DebugCommand.BatSwarm: Swarms.Force(SwarmKind.BatSwarm, now); break;
                case DebugCommand.GhostSwarm: Swarms.Force(SwarmKind.GhostSwarm, now); break;
                case DebugCommand.FlowerWall: Swarms.Force(SwarmKind.FlowerWall, now); break;
                case DebugCommand.Minotaur: Signature.Force(); break;
            }
        }

        /// <summary>A fresh group of <paramref name="kind"/>, ending at run clock <paramref name="until"/>, or null when all are in play.</summary>
        public MusicGroup Begin(SetPieceKind kind, double until)
        {
            foreach (var g in groups)
            {
                if (g.Active) continue;
                g.Id = nextId++;
                g.Kind = kind;
                g.Count = 0;
                g.Until = until;
                g.Phase = 0;
                g.Beats = 0;
                g.Dir = 0;
                return g;
            }
            return null;
        }

        /// <summary>Enemy <paramref name="i"/> spawned into group <paramref name="g"/>.</summary>
        public void Add(MusicGroup g, int i)
        {
            g.Members[g.Count++] = i;
            Horde.Scripts.Join(i, g.Id);
        }

        /// <summary>Is the group's member <paramref name="k"/> still its, alive, as slot <paramref name="i"/>?</summary>
        public bool Member(MusicGroup g, int k, out int i)
        {
            i = g.Members[k];
            return State.EnemyStore[i].Alive && Horde.Scripts.GroupOf(i) == g.Id;
        }

        /// <summary>Its members still alive.</summary>
        public int Alive(MusicGroup g)
        {
            int n = 0;
            for (int k = 0; k < g.Count; k++)
                if (Member(g, k, out _)) n++;
            return n;
        }

        /// <summary>Are any swarm members alive (native's: a set piece waits for the swarms to go)?</summary>
        public bool SwarmsInPlay()
        {
            foreach (var g in groups)
                if (g.Active && g.Kind <= SetPieceKind.FlowerWall && Alive(g) > 0) return true;
            return false;
        }

        /// <summary>
        /// The group ends at <paramref name="t"/>: its members out of sight leave, those in sight join the
        /// horde's pursuit.
        /// </summary>
        public void End(Simulation sim, MusicGroup g, double t, double hx, double hy)
        {
            for (int k = 0; k < g.Count; k++)
            {
                if (!Member(g, k, out int i)) continue;
                State.EnemyStore[i].PositionAt(t, out double x, out double y);
                if (Spawner.OutOfSight(x, y, hx, hy)) Horde.Leave(sim, i, t);
                else Horde.Scripts.Clear(i);
            }
            g.Id = -1;
            g.Count = 0;
        }

        /// <summary>
        /// Places an enemy of <paramref name="role"/> at (x, y) at <paramref name="t"/> into <paramref name="g"/>,
        /// a walker on the nearest open ground; returns its slot, or -1 with no room or ground.
        /// </summary>
        public int Spawn(Simulation sim, MusicGroup g, double t, int role, double x, double y, bool flier)
        {
            if (Horde.FreeSlots - Spawner.BossesWaiting <= 0) return -1;
            if (!flier && !Horde.Nav.Walkable(x, y) && !Horde.Nav.NearestOpen(x, y, Horde.PushCells, out x, out y)) return -1;
            int i = Horde.Spawn(sim, t, role, x, y);
            Add(g, i);
            return i;
        }

        /// <summary>The hero's heading as a ground vector.</summary>
        public void Heading(out double x, out double y) => Sword.Facing(State.Hero.Facing, out x, out y);
    }

    /// <summary>
    /// The musical set pieces (native's engine/musical.rs; schedule.json's <c>setPieces</c>): one at a time,
    /// each from its run-clock start (<c>fromS</c>), at least <c>gapS</c> after the previous one began, none
    /// while a swarm's members or a boss due are about. On reliable main beats:
    /// - <b>the stampede</b>: a loudness jump of <c>jumpDb</c> or more after <c>quietS</c> of quiet (at most
    ///   5% of its ticks loud) starts it on the first main beat at or after the jump, so it charges in with
    ///   the jump: two rows of goblins from beyond one side of the view, a gap in the
    ///   middle, across at <c>speedScale</c> times their pace. It ends once they are all past the far side.
    /// - <b>the kick line</b>: after <c>mediumBeats</c> main beats at medium loudness, a line of skeletons
    ///   across the hero's heading marches in from ahead over <c>entranceBeats</c>, then kicks in turn
    ///   (alternate members pose a lunge each beat) until a strong onset after <c>minDanceBeats</c>, or
    ///   <c>maxDanceBeats</c>, and charges as the horde.
    /// - <b>the wedge</b>: every <c>everyBeats</c> (the beat's index) in quiet passages, a V of bees flies in
    ///   ahead of the hero over <c>entranceBeats</c>, holds <c>danceBeats</c> and breaks on the hero.
    /// - <b>the ring</b>: every <c>everyBeats</c> in loud passages, goblins on a circle beyond the view
    ///   around the hero, with <c>gaps</c> exits, hold <c>entranceBeats</c>, then close to <c>closeToM</c>
    ///   over 8 beats, and hold until 20 s after the close began.
    /// Loudness is the mean level over the run's <c>levelSmoothS</c>. A piece lives at most <c>lifeS</c>.
    /// Native's pieces enter after their cue with 0.35 s of notice; the lookahead lets these start on the
    /// cue's beat. The ring and kick line stop short of structures; the bees fly over them.
    /// </summary>
    internal sealed class Pieces
    {
        /// <summary>The kick line and the wedge stand this far ahead of the hero, metres.</summary>
        public const double KickAheadM = 170.0 / 27, WedgeAheadM = 7;
        /// <summary>Beyond the view's edge where pieces enter (native's 64 px), and the stampede's second row's lag (48 px).</summary>
        public const double MarginM = 64.0 / 27, RowM = 48.0 / 27;
        /// <summary>The ring closes over this many main beats, and holds until this long after the close began.</summary>
        public const int CloseBeats = 8;
        public const double HoldS = 20;
        /// <summary>The stampede's quiet: the first tick within this of the window's start, at most this share loud.</summary>
        const double QuietEdgeS = 0.1, LoudShare = 0.05;

        enum Phase { Entering, Dancing, Closing, Released }

        readonly MusicEvents m;
        readonly SetPieces rules;
        // The piece in play: its group, held by id, since a group ended at a tick end is reused.
        MusicGroup active;
        int activeId = -1, jumpFrom, medium;
        double next = double.NegativeInfinity;
        bool pending, forced;
        SetPieceKind pendingKind;
        double pendingAt, pendingCause, pendingKnown;

        public Pieces(MusicEvents m)
        {
            this.m = m;
            rules = m.Schedule.SetPieces;
        }

        double FromS(SetPieceKind kind) =>
            kind == SetPieceKind.Stampede ? rules.Stampede.FromS : kind == SetPieceKind.KickLine ? rules.KickLine.FromS
            : kind == SetPieceKind.Wedge ? rules.Wedge.FromS : rules.Ring.FromS;

        bool CanStage(SetPieceKind kind) =>
            m.RunClock >= FromS(kind) - 1e-9 && m.RunClock >= next - 1e-9 && active == null && !pending
            && !m.SwarmsInPlay() && m.Spawner.BossesWaiting == 0;

        void Stage(SetPieceKind kind, double at, double cause, double known, bool force)
        {
            pending = true;
            forced = force;
            pendingKind = kind;
            pendingAt = at;
            pendingCause = cause;
            pendingKnown = known;
        }

        public void Force(SetPieceKind kind, double now) => Stage(kind, now, now, now, true);

        bool InPlay => active != null && active.Id == activeId;

        /// <summary>The piece in play, if any, ends now (a swarm or the minotaur takes the stage), and none waits.</summary>
        public void Retire(Simulation sim, double t, double hx, double hy)
        {
            if (InPlay) m.End(sim, active, t, hx, hy);
            active = null;
            if (!forced) pending = false;
        }

        public void Land(Simulation sim, int beat, double t, bool reliable, double hx, double hy)
        {
            if (!InPlay) active = null; // ended at a tick end
            if (!reliable) return;
            if (active != null) Advance(sim, beat, t, hx, hy);
            if (m.Natural) Cue(beat, t);
            if (!pending) return;
            if (t > pendingAt + MusicEvents.PendingS + 1e-9)
            {
                pending = false;
                return;
            }
            if (t < pendingAt - 1e-9) return;
            pending = false;
            if (forced) Retire(sim, t, hx, hy);
            else if (active != null || m.SwarmsInPlay() || m.Spawner.BossesWaiting > 0) return;
            Start(sim, pendingKind, beat, t, hx, hy, pendingCause, pendingKnown);
        }

        /// <summary>The music's cues on this beat: jumps after quiet, and the loudness's run.</summary>
        void Cue(int beat, double t)
        {
            // A jump is judged on the first main beat at or after it, known ahead, so the stampede starts on it.
            var jumps = m.Log.Revealed(EntryKind.Jump);
            double period = m.Clock.PeriodOf(beat);
            for (; jumpFrom < jumps.Length && jumps[jumpFrom].T <= t + 1e-9; jumpFrom++)
            {
                var j = new JumpEntry(jumps[jumpFrom]);
                if (j.Db < rules.Stampede.JumpDb - 1e-9 || j.T < rules.Stampede.QuietS || j.T < t - period - 1e-9) continue;
                if (Quiet(j.T) && CanStage(SetPieceKind.Stampede))
                    Stage(SetPieceKind.Stampede, t, j.T, m.Log.KnownAt(EntryKind.Jump, jumpFrom), false);
            }
            double level = m.Spawner.MeanLevel(t);
            if (level > 1.5)
            {
                medium = 0;
                if (beat % rules.Ring.EveryBeats == 0 && CanStage(SetPieceKind.Ring)) Stage(SetPieceKind.Ring, t, t, t, false);
            }
            else if (level < 0.5)
            {
                medium = 0;
                if (beat % rules.Wedge.EveryBeats == 0 && CanStage(SetPieceKind.Wedge)) Stage(SetPieceKind.Wedge, t, t, t, false);
            }
            else if (++medium >= rules.KickLine.MediumBeats)
            {
                medium = 0;
                if (CanStage(SetPieceKind.KickLine)) Stage(SetPieceKind.KickLine, t, t, t, false);
            }
        }

        /// <summary>Was the music quiet for the stampede's window before <paramref name="t"/>, by the ticks known now?</summary>
        bool Quiet(double t)
        {
            var ticks = m.Log.Ticks;
            double t0 = t - rules.Stampede.QuietS;
            int lo = 0, hi = ticks.Length;
            while (lo < hi)
            {
                int mid = (lo + hi) >> 1;
                if (ticks[mid].T < t0 - 1e-9) lo = mid + 1;
                else hi = mid;
            }
            if (lo >= ticks.Length || ticks[lo].T > t0 + QuietEdgeS) return false;
            int all = 0, loud = 0;
            for (int i = lo; i < ticks.Length && ticks[i].T <= t + 1e-9; i++)
            {
                all++;
                if (new TickEntry(ticks[i]).Level == LoudnessLevel.Loud) loud++;
            }
            return loud <= LoudShare * all;
        }

        void Start(Simulation sim, SetPieceKind kind, int beat, double t, double hx, double hy, double cause, double known)
        {
            int count = Math.Min(rules.GroupCap, kind == SetPieceKind.Stampede ? rules.Stampede.Count
                : kind == SetPieceKind.KickLine ? rules.KickLine.Count : kind == SetPieceKind.Wedge ? rules.Wedge.Count : rules.Ring.Count);
            count = m.Horde.MakeRoom(sim, t, count, hx, hy);
            if (count == 0) return;
            var g = m.Begin(kind, m.RunClock + rules.LifeS);
            if (g == null) return;
            var s = m.Spawner;
            double x0 = hx + s.ViewMinX, x1 = hx + s.ViewMaxX, y0 = hy + s.ViewMinY, y1 = hy + s.ViewMaxY;
            m.Heading(out double fx, out double fy);
            double px = -fy, py = fx;
            switch (kind)
            {
                case SetPieceKind.Stampede:
                {
                    int role = m.Content.RoleIndex(rules.Stampede.Role);
                    g.Dir = sim.Random.NextDouble() < 0.5 ? 1 : -1;
                    int rows = Math.Max(1, rules.Stampede.Rows), perRow = (count + rows - 1) / rows, places = perRow + 3, gap = places / 2;
                    for (int k = 0; k < count; k++)
                    {
                        int row = k % rows, place = k / rows;
                        if (place >= gap) place += 3; // a gap in the middle for the hero
                        double x = g.Dir > 0 ? x0 - MarginM - row * RowM : x1 + MarginM + row * RowM;
                        double y = y0 + (y1 - y0) * (place + 0.5) / places;
                        int i = m.Spawn(sim, g, t, role, x, y, flier: false);
                        if (i >= 0) m.Horde.Scripts.Line(i, g.Dir, 0, rules.Stampede.SpeedScale, flier: false);
                    }
                    break;
                }
                case SetPieceKind.KickLine:
                {
                    var r = rules.KickLine;
                    int role = m.Content.RoleIndex(r.Role);
                    double enter = s.FootprintEdge(fx, fy) + MarginM - KickAheadM;
                    for (int k = 0; k < count; k++)
                    {
                        double across = (k - (count - 1) / 2.0) * r.SpacingM;
                        double ox = fx * KickAheadM + px * across, oy = fy * KickAheadM + py * across;
                        double sx = ox + fx * (enter + k * 0.2), sy = oy + fy * (enter + k * 0.2);
                        int i = m.Spawn(sim, g, t, role, hx + sx, hy + sy, flier: false);
                        if (i >= 0) m.Horde.Scripts.Slot(i, sx, sy, ox, oy, beat, r.EntranceBeats, flier: false);
                    }
                    break;
                }
                case SetPieceKind.Wedge:
                {
                    var r = rules.Wedge;
                    int role = m.Content.RoleIndex(r.Role);
                    double enter = s.FootprintEdge(fx, fy) + MarginM;
                    for (int k = 0; k < count; k++)
                    {
                        int arm = (k + 1) / 2;
                        double side = k % 2 == 0 ? 1 : -1;
                        double ox = fx * (WedgeAheadM - arm * r.SpacingM) + px * side * arm * r.SpacingM;
                        double oy = fy * (WedgeAheadM - arm * r.SpacingM) + py * side * arm * r.SpacingM;
                        double sx = ox + fx * enter, sy = oy + fy * enter;
                        int i = m.Spawn(sim, g, t, role, hx + sx, hy + sy, flier: true);
                        if (i >= 0) m.Horde.Scripts.Slot(i, sx, sy, ox, oy, beat, r.EntranceBeats, flier: true);
                    }
                    break;
                }
                default:
                {
                    var r = rules.Ring;
                    int role = m.Content.RoleIndex(r.Role);
                    int places = count + r.Gaps * r.GapPlaces;
                    double radius = 0.6 * Math.Sqrt((x1 - x0) * (x1 - x0) + (y1 - y0) * (y1 - y0)) + MarginM;
                    double theta = sim.Random.Range(0, 2 * Math.PI);
                    for (int k = 0, placed = 0; k < places && placed < count; k++)
                    {
                        bool inGap = false;
                        for (int gi = 0; gi < r.Gaps; gi++)
                            inGap |= k >= gi * places / r.Gaps && k < gi * places / r.Gaps + r.GapPlaces;
                        if (inGap) continue;
                        placed++;
                        double a = theta + k * 2 * Math.PI / places, c = Math.Cos(a), sn = Math.Sin(a);
                        int i = m.Spawn(sim, g, t, role, hx + c * radius, hy + sn * radius, flier: false);
                        if (i >= 0) m.Horde.Scripts.Ring(i, hx, hy, c, sn, radius, flier: false);
                    }
                    break;
                }
            }
            if (g.Count == 0)
            {
                g.Id = -1;
                return;
            }
            active = g;
            activeId = g.Id;
            next = m.RunClock + rules.GapS;
            sim.Emit(SimEvent.SetPiece(t, kind, g.Count, cause, known));
        }

        /// <summary>A reliable main beat in the piece's life: its phases, and its end.</summary>
        void Advance(Simulation sim, int beat, double t, double hx, double hy)
        {
            var g = active;
            if (m.Alive(g) == 0 || (g.Kind == SetPieceKind.Stampede && Crossed(g, t, hx)))
            {
                m.End(sim, g, t, hx, hy);
                active = null;
                return;
            }
            g.Beats++;
            var phase = (Phase)g.Phase;
            if (g.Kind == SetPieceKind.KickLine && phase == Phase.Dancing) Kick(sim, g, beat, t, hx, hy);
            bool ready = g.Kind switch
            {
                SetPieceKind.KickLine => phase == Phase.Entering ? g.Beats >= rules.KickLine.EntranceBeats
                    : phase == Phase.Dancing && (g.Beats >= rules.KickLine.MaxDanceBeats
                                                 || (g.Beats >= rules.KickLine.MinDanceBeats && StrongOnset(t, m.Clock.PeriodOf(beat)))),
                SetPieceKind.Wedge => phase == Phase.Entering ? g.Beats >= rules.Wedge.EntranceBeats
                    : phase == Phase.Dancing && g.Beats >= rules.Wedge.DanceBeats,
                SetPieceKind.Ring => phase == Phase.Entering && g.Beats >= rules.Ring.EntranceBeats,
                _ => false,
            };
            if (!ready) return;
            g.Beats = 0;
            if (g.Kind == SetPieceKind.Ring)
            {
                g.Phase = (int)Phase.Closing;
                for (int k = 0; k < g.Count; k++)
                    if (m.Member(g, k, out int i)) m.Horde.Scripts.Close(i, rules.Ring.CloseToM, beat, CloseBeats);
                g.Until = Math.Min(g.Until, m.RunClock + HoldS);
            }
            else if (phase == Phase.Entering) g.Phase = (int)Phase.Dancing;
            else
            {
                // Released: the dancers break on the hero as the horde.
                for (int k = 0; k < g.Count; k++)
                    if (m.Member(g, k, out int i)) m.Horde.Scripts.Clear(i);
                g.Phase = (int)Phase.Released;
                g.Id = -1;
                active = null;
            }
        }

        /// <summary>The kick: on each beat half the line poses a lunge toward the hero for the next beat, in turn.</summary>
        void Kick(Simulation sim, MusicGroup g, int beat, double t, double hx, double hy)
        {
            var beats = m.Log.Beats;
            if (beat + 1 >= beats.Length) return;
            double impact = beats[beat + 1].T;
            for (int k = 0; k < g.Count; k++)
            {
                if ((k + beat) % 2 != 0 || !m.Member(g, k, out int i)) continue;
                m.State.EnemyStore[i].PositionAt(t, out double x, out double y);
                double dx = hx - x, dy = hy - y, d = Math.Sqrt(dx * dx + dy * dy);
                if (d < 1e-9) continue;
                sim.Emit(SimEvent.EnemyAttack(t, i, EnemyAttackKind.Lunge, t, impact, dx / d, dy / d));
            }
        }

        bool StrongOnset(double t, double period)
        {
            var onsets = m.Log.Revealed(EntryKind.Onset);
            for (int i = onsets.Length - 1; i >= 0; i--)
            {
                var o = new OnsetEntry(onsets[i]);
                if (o.T > t + 1e-9) continue;
                if (o.T < t - period - 1e-9) return false;
                if (o.Strong) return true;
            }
            return false;
        }

        /// <summary>Are the stampede's members all past the view's far side?</summary>
        bool Crossed(MusicGroup g, double t, double hx)
        {
            double far = g.Dir > 0 ? hx + m.Spawner.ViewMaxX + MarginM : hx + m.Spawner.ViewMinX - MarginM;
            for (int k = 0; k < g.Count; k++)
            {
                if (!m.Member(g, k, out int i)) continue;
                m.State.EnemyStore[i].PositionAt(t, out double x, out _);
                if (g.Dir > 0 ? x <= far : x >= far) return false;
            }
            return true;
        }
    }
}
