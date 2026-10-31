using System;
using PS.Game.Sim.Level;

namespace PS.Game.Sim
{
    /// <summary>
    /// The game's deterministic simulation (unity-game.md, "The Simulation"). The client feeds it
    /// entries, keys and time, and reads its state and event log; it never calls the client.
    ///
    /// <code>
    /// sim.Add(entry);              // held until its known_at
    /// sim.Press(key, heard);       // recorded at the sim's current time
    /// sim.StepTo(frontier);        // fixed 1/120 s ticks
    /// sim.EventsSince(cursor); sim.State;
    /// </code>
    ///
    /// Only <see cref="Add"/>, <see cref="AddKnown"/>, <see cref="Press"/>, <see cref="Release"/>,
    /// <see cref="StepTo"/> and <see cref="DiscardEventsBefore"/> change the sim. Everything else
    /// public here and on what it exposes (<see cref="State"/>, <see cref="Stream"/>,
    /// <see cref="Beats"/>, <see cref="Strides"/>) is a read the client may make at any time, as
    /// often as it likes, without changing the run.
    ///
    /// Tick k covers song time (k/120, (k+1)/120]. Inside it, events with exact times fire at those
    /// times, earliest first, and at one time in a fixed order: entries known then are revealed,
    /// then the beat (on a downbeat, structures a zone change marked regrow, then a zone change
    /// may land: <see cref="Structures"/>, <see cref="Zones"/>), then the stride step and the
    /// hero's move on it, then the enemies' beat phases (<see cref="Horde"/>), then the sword's impacts, cleave bands, plans and wind-ups
    /// (<see cref="Sword"/>), so a strike lands after the steps and lunges on its beat. The beat's
    /// state is updated before each of those and at the tick's end, after which the per-tick
    /// systems run. A press may move the hero at once (a late step),
    /// at the frontier. Nothing simulated is ever revised.
    /// </summary>
    public sealed class Simulation
    {
        public const int TicksPerSecond = 120;
        public const double TickSeconds = 1.0 / TicksPerSecond;

        readonly StructList<KeyRecord> keys;
        readonly EventLog events;
        readonly Hero hero;
        readonly double knownAtBudget;
        double cursor;

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
            State = new SimState
            {
                Tick = (long)Math.Floor(config.StartTime * TicksPerSecond + 1e-9),
            };
            State.Now = State.Tick * TickSeconds;
            cursor = State.Now;
            var arena = config.Arena ?? ArenaData.Open();
            Arena = new ArenaGrid(arena);
            hero = new Hero(State.Hero, Arena, Strides);
            if (config.Horde != null) Horde = new Horde(config.Horde, Stream, Beats, Strides, Arena, State);
            var content = config.Horde?.Content ?? config.Sword?.Content;
            Health = new HeroHealth(State.Hero, config, content);
            Structures = new Structures(arena, Arena, State, Horde);
            if (content != null) Zones = new Zones(content.Schedule.Zones, Stream, State, Structures, State.Now);
            if (config.Sword != null) Sword = new Sword(config.Sword, Stream, Beats, State, Horde, Structures, Zones);
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
        /// <summary>The hero's sword, or null in a run without it.</summary>
        internal Sword Sword { get; }
        /// <summary>The level's crystal structures; the client reads them through <see cref="SimState.Structures"/>.</summary>
        internal Structures Structures { get; }
        /// <summary>The zones, or null in a run without content (whose schedule holds their rules).</summary>
        internal Zones Zones { get; }

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
        /// a queued or late step (<see cref="Hero"/>). A replay presses at the recorded effective time.
        /// </summary>
        public void Press(Key key, double heard)
        {
            keys.Add(new KeyRecord { Key = key, Down = true, Heard = heard, Effective = Now });
            hero.Press(this, key, heard);
        }

        /// <summary>Records a key release heard at song time <paramref name="heard"/>; it takes effect now.</summary>
        public void Release(Key key, double heard)
        {
            keys.Add(new KeyRecord { Key = key, Down = false, Heard = heard, Effective = Now });
            hero.Release(key);
        }

        /// <summary>Every key change recorded, in order.</summary>
        public ReadOnlySpan<KeyRecord> Keys => keys.AsSpan();

        public ReadOnlySpan<SimEvent> EventsSince(int eventCursor) => events.Since(eventCursor);

        /// <summary>Frees the events before <paramref name="eventCursor"/> once the client has read them.</summary>
        public void DiscardEventsBefore(int eventCursor) => events.DiscardBefore(eventCursor);

        internal void Emit(in SimEvent e) => events.Add(e);

        /// <summary>Simulates whole ticks up to song time <paramref name="songTime"/>.</summary>
        public void StepTo(double songTime)
        {
            long target = (long)Math.Floor(songTime * TicksPerSecond + 1e-9);
            Stream.SettleArrivals(knownAtBudget, Math.Max(Now, target * TickSeconds));
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
                double sword = Sword?.NextTime(cursor) ?? double.PositiveInfinity;
                double next = Math.Min(Math.Min(beat, step), Math.Min(horde, sword));
                if (reveal <= next && reveal <= end)
                {
                    cursor = Math.Max(cursor, reveal);
                    Stream.Reveal(cursor);
                    Beats.TakeTicks();
                    continue;
                }
                if (next > end) break;
                cursor = next;
                UpdateBeatState(next);
                if (beat == next)
                {
                    Beats.Fire(this, beat, State.Beat);
                    int i = Beats.LastBeat;
                    State.LastBeat = i;
                    State.Bar = Beats.BarOf(i, out bool downbeat);
                    State.Downbeat = downbeat;
                    if (downbeat)
                    {
                        Structures.Downbeat(this, beat, Beats.PeriodOf(i));
                        Zones?.Downbeat(this, beat);
                    }
                }
                else if (step == next)
                {
                    Strides.Fire(this, step);
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
                    Sword.Fire(this, sword);
                }
            }
            cursor = end;
            State.Tick++;
            State.Now = end;
            UpdateBeatState(end);
            if (!State.Frozen) State.RunClock += TickSeconds;
            Horde?.Tick(this, end);
            Sword?.Tick(end);
            Zones?.Tick(this, end);
        }

        void UpdateBeatState(double t)
        {
            var now = Beats.StateAt(t);
            if (now == State.Beat) return;
            Emit(SimEvent.BeatState(t, now, State.Beat));
            State.Beat = now;
        }
    }
}
