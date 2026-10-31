using System;
using PS.Game.Sim.Content;
using PS.Game.Sim.Level;

namespace PS.Game.Sim
{
    /// <summary>
    /// The game's deterministic simulation (unity-game.md, "The Simulation"). The client feeds it
    /// entries, keys, run controls and time, and reads its state and event log; it never calls the client.
    ///
    /// <code>
    /// sim.Add(entry);              // held until its known_at
    /// sim.Press(key, heard);       // recorded at the sim's current time
    /// sim.StepTo(frontier);        // fixed 1/120 s ticks
    /// sim.EventsSince(cursor); sim.State;
    /// </code>
    ///
    /// Only <see cref="Add"/>, <see cref="AddKnown"/>, <see cref="Press"/>, <see cref="Release"/>,
    /// <see cref="Command"/>, <see cref="Control"/>, <see cref="StepTo"/> and <see cref="DiscardEventsBefore"/> change the sim. Everything else
    /// public here and on what it exposes (<see cref="State"/>, <see cref="Stream"/>,
    /// <see cref="Beats"/>, <see cref="Strides"/>) is a read the client may make at any time, as
    /// often as it likes, without changing the run.
    ///
    /// Tick k covers song time (k/120, (k+1)/120]. Inside it, events with exact times fire at those
    /// times, earliest first, and at one time in a fixed order: entries known then are revealed,
    /// then the run's own moments (the run clock reaching its duration, the victory card:
    /// <see cref="RunRules"/>), then the beat (the run's start, a resume or the finale's blast on it, then on a downbeat, structures a zone change marked regrow, then a zone change
    /// may land, then bosses due land: <see cref="Structures"/>, <see cref="Zones"/>, <see cref="Horde"/>), then the stride step (the music stopping on it) and the
    /// hero's move on it, then the enemies' beat phases (<see cref="Horde"/>), then the arsenal's times in slot order: the sword's impacts, cleave bands, plans and wind-ups
    /// (<see cref="Sword"/>), so a strike lands after the steps and lunges on its beat, the other weapons' shots, then the gems' arrivals
    /// (<see cref="Arsenal"/>). The beat's
    /// state is updated before each of those and at the tick's end, after which the per-tick
    /// systems run, the hero's held strides first (judged in heard time, a lead after their steps:
    /// <see cref="Hero"/>). A press moves the hero at once (a press stride), at the frontier, drawn
    /// from when the press was heard. Nothing simulated is ever revised.
    /// </summary>
    public sealed class Simulation
    {
        public const int TicksPerSecond = 120;
        public const double TickSeconds = 1.0 / TicksPerSecond;
        /// <summary>How far from the hero <see cref="DebugCommand.BreakStructures"/> reaches: the heavy strike's cleave wave's outer edge.</summary>
        public const double DebugBreakReachM = 15;
        /// <summary>The debug break's throw, metres: the starting sword's heavy strike's.</summary>
        public const double DebugBreakForceM = 4.63;

        readonly StructList<KeyRecord> keys;
        readonly StructList<DebugRecord> commands;
        readonly StructList<ControlRecord> controls;
        readonly EventLog events;
        readonly Hero hero;
        readonly double knownAtBudget;
        double cursor;
        int inputs;

        public Simulation(SimConfig config)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            knownAtBudget = config.KnownAtBudget;
            Random = new SimRandom(config.Seed);
            Stream = new BeliefLog(config);
            Beats = new BeatClock(Stream, config);
            Strides = new StrideGrid(Stream, Beats, config.StartTime);
            events = new EventLog(config.EventCapacity);
            keys = new StructList<KeyRecord>(config.PressCapacity);
            commands = new StructList<DebugRecord>(16);
            controls = new StructList<ControlRecord>(256);
            State = new SimState
            {
                Tick = (long)Math.Floor(config.StartTime * TicksPerSecond + 1e-9),
            };
            State.Now = State.Tick * TickSeconds;
            cursor = State.Now;
            var arena = config.Arena ?? ArenaData.Open();
            Arena = new ArenaGrid(arena);
            hero = new Hero(State.Hero, Arena, Strides);
            var content = config.Content;
            if (config.Horde != null) Horde = new Horde(config.Horde, content, Stream, Beats, Strides, Arena, State);
            Health = new HeroHealth(State.Hero, config, content);
            Structures = new Structures(arena, Arena, State, Horde);
            if (content != null) Zones = new Zones(content.Schedule.Zones, Stream, State, Structures, State.Now);
            Run = new RunRules(config, content, Stream, State);
            if (config.Arsenal != null)
            {
                double windup = config.Arsenal.SwordWindupS;
                Arsenal = new Arsenal(config.Arsenal, content, State, Stream, Beats, Run, Health, (arsenal, def, index) =>
                    def.Shape.Kind == ShapeKind.Sword
                        ? new Sword(arsenal, def, index, windup, content, Stream, Beats, State, Horde, Structures, Zones)
                        : new Weapon(arsenal, State, def, index));
            }
            if (config.Easy && content != null)
            {
                Chickens = new FloorChickens(content.Schedule.Easy, State, Health);
                if (Horde != null) Horde.Chickens = Chickens;
            }
            Horde?.Begin(this);
        }

        public SimState State { get; }
        /// <summary>The run's one generator: the sim's alone, since every draw changes the rest of the run.</summary>
        internal SimRandom Random { get; }
        public BeliefLog Stream { get; }
        public BeatClock Beats { get; }
        public StrideGrid Strides { get; }
        public ArenaGrid Arena { get; }
        /// <summary>The enemies, or null in a run without them; the client reads them through <see cref="SimState.Enemies"/>.</summary>
        internal Horde Horde { get; }
        internal HeroHealth Health { get; }
        /// <summary>The hero's weapons, passives, gems and level-ups, or null in a run without them.</summary>
        internal Arsenal Arsenal { get; }
        /// <summary>The hero's sword, or null in a run without it.</summary>
        internal Sword Sword => Arsenal?.Sword;
        /// <summary>The level's crystal structures; the client reads them through <see cref="SimState.Structures"/>.</summary>
        internal Structures Structures { get; }
        /// <summary>The zones, or null in a run without content (whose schedule holds their rules).</summary>
        internal Zones Zones { get; }
        /// <summary>The run's start, clock, freezes and ends.</summary>
        internal RunRules Run { get; }
        /// <summary>Easy's floor chickens, or null off Easy.</summary>
        internal FloorChickens Chickens { get; }

        /// <summary>The frontier: the song time simulated to.</summary>
        public double Now => State.Now;

        /// <summary>The hash of the settled event log.</summary>
        public ulong Hash => events.Hash;

        /// <summary>The cursor after the latest event.</summary>
        public int EventCount => events.Count;

        /// <summary>
        /// Accepts an arriving entry. It is known from <c>max(Stamp + B, frontier)</c>, the frontier
        /// being where the next <see cref="StepTo"/> takes the sim, the end of the frame it arrived in.
        /// </summary>
        public void Add(in StreamEntry e) => Stream.Arrive(e);

        /// <summary>Accepts a replayed entry with its recorded known-at time.</summary>
        public void AddKnown(in StreamEntry e, double knownAt) => Stream.AddKnown(e, knownAt, Now);

        /// <summary>
        /// Records a key press heard at song time <paramref name="heard"/>; it takes effect now, as
        /// a press stride or, at most one a step, queued (<see cref="Hero"/>). A replay presses at
        /// the recorded effective time.
        /// </summary>
        public void Press(Key key, double heard)
        {
            keys.Add(new KeyRecord { Key = key, Down = true, Heard = heard, Effective = Now, Order = inputs++ });
            bool cards = Arsenal != null && Arsenal.LevelUps.Press(this, key, Now);
            hero.Press(this, key, heard, cards);
        }

        /// <summary>Records a key release heard at song time <paramref name="heard"/>; it takes effect now.</summary>
        public void Release(Key key, double heard)
        {
            keys.Add(new KeyRecord { Key = key, Down = false, Heard = heard, Effective = Now, Order = inputs++ });
            Arsenal?.LevelUps.Release(key);
            hero.Release(this, key, heard);
        }

        /// <summary>The cards opened: the hero lets go of its held keys, so a key held through them never walks it after.</summary>
        internal void LetGoOfHeldKeys() => hero.LetGo();

        /// <summary>Every key change recorded, in order.</summary>
        public ReadOnlySpan<KeyRecord> Keys => keys.AsSpan();

        /// <summary>
        /// Gives a debug command (<see cref="DebugCommand"/>); it takes effect now, recorded like a
        /// key. A command whose system the run lacks (no zones, no sword) does nothing.
        /// </summary>
        public void Command(DebugCommand command)
        {
            commands.Add(new DebugRecord { Command = command, Effective = Now, Order = inputs++ });
            switch (command)
            {
                case DebugCommand.SectionChange:
                    Zones?.Force(Now);
                    break;
                case DebugCommand.Accent:
                    Sword?.ForceAccent();
                    break;
                case DebugCommand.BreakStructures:
                    State.Hero.PositionAt(Now, out double x, out double y);
                    Structures.Strike(this, Now, x, y, DebugBreakReachM, StructureHit.Debug, DebugBreakForceM);
                    break;
                case DebugCommand.RegrowStructures:
                    Structures.ZoneChanged();
                    break;
                default:
                    Horde?.Music.Force(command, Now);
                    break;
            }
        }

        /// <summary>Every debug command given, in order.</summary>
        public ReadOnlySpan<DebugRecord> Commands => commands.AsSpan();

        /// <summary>
        /// Gives a run control (<see cref="RunControl"/>: the start, a pause or resume, an end, the
        /// watchdog); it takes effect now, recorded like a key.
        /// </summary>
        public void Control(RunControl control)
        {
            controls.Add(new ControlRecord { Control = control, Effective = Now, Order = inputs++ });
            Run.Control(this, control, Now);
        }

        /// <summary>Every run control given, in order.</summary>
        public ReadOnlySpan<ControlRecord> Controls => controls.AsSpan();

        public ReadOnlySpan<SimEvent> EventsSince(int eventCursor) => events.Since(eventCursor);

        /// <summary>Frees the events before <paramref name="eventCursor"/> once the client has read them.</summary>
        public void DiscardEventsBefore(int eventCursor) => events.DiscardBefore(eventCursor);

        internal void Emit(in SimEvent e) => events.Add(e);

        /// <summary>Simulates whole ticks up to song time <paramref name="songTime"/>.</summary>
        public void StepTo(double songTime)
        {
            long target = (long)Math.Floor(songTime * TicksPerSecond + 1e-9);
            Stream.SettleArrivals(knownAtBudget, Math.Max(Now, target * TickSeconds), Now);
            while (State.Tick < target) RunTick();
        }

        void RunTick()
        {
            double end = (State.Tick + 1) * TickSeconds;
            while (true)
            {
                double reveal = Stream.NextKnownAt;
                double beat = Beats.NextTime(cursor);
                double step = Strides.NextTime(cursor);
                double horde = Horde?.NextTime(cursor) ?? double.PositiveInfinity;
                double arsenal = Arsenal?.NextTime(cursor) ?? double.PositiveInfinity;
                double run = Run.NextTime();
                double next = Math.Min(Math.Min(Math.Min(beat, step), Math.Min(horde, arsenal)), run);
                if (reveal <= next && reveal <= end)
                {
                    cursor = Math.Max(cursor, reveal);
                    Stream.Reveal(cursor);
                    Beats.TakeTicks();
                    Run.Reveal();
                    continue;
                }
                if (next > end) break;
                cursor = next;
                UpdateBeatState(next);
                if (run == next)
                {
                    Run.Fire(this, run);
                }
                else if (beat == next)
                {
                    Beats.Fire(this, beat, State.Beat);
                    int i = Beats.LastBeat;
                    State.LastBeat = i;
                    State.Bar = Beats.BarOf(i, out bool downbeat);
                    State.Downbeat = downbeat;
                    Run.Beat(this, beat, i);
                    if (downbeat)
                    {
                        Structures.Downbeat(this, beat, Beats.PeriodOf(i));
                        Zones?.Downbeat(this, beat);
                        Horde?.Downbeat(this, beat);
                    }
                }
                else if (step == next)
                {
                    Strides.Fire(this, step);
                    Run.Step(this, step);
                    hero.Step(this, Strides.Count - 1, step, Strides.Period);
                    State.StrideStep = step;
                    State.StridePeriod = Strides.Period;
                    State.StrideHalvings = Strides.Halvings;
                }
                else if (horde == next)
                {
                    Horde.Fire(this, horde);
                }
                else
                {
                    Arsenal.Fire(this, arsenal);
                }
            }
            cursor = end;
            State.Tick++;
            State.Now = end;
            UpdateBeatState(end);
            hero.Tick(this, end);
            Run.Tick(end);
            Chickens?.Tick(this, end);
            Horde?.Tick(this, end);
            Arsenal?.Tick(this, end);
            Zones?.Tick(this, end);
        }

        void UpdateBeatState(double t)
        {
            var now = Beats.StateAt(t);
            if (now == State.Beat) return;
            Emit(SimEvent.BeatState(t, now, State.Beat));
            State.Beat = now;
            Run.BeatState(t, now);
        }
    }
}
