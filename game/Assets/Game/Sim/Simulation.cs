using System;

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
    /// then the beat, then the stride step. The beat's state is updated before each of those and at
    /// the tick's end, after which the per-tick systems run. Nothing simulated is ever revised.
    /// </summary>
    public sealed class Simulation
    {
        public const int TicksPerSecond = 120;
        public const double TickSeconds = 1.0 / TicksPerSecond;

        readonly StructList<KeyRecord> keys;
        readonly EventLog events;
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
        }

        public SimState State { get; }
        /// <summary>The run's one generator: the sim's alone, since every draw changes the rest of the run.</summary>
        internal SimRandom Random { get; }
        public BeliefLog Stream { get; }
        public BeatClock Beats { get; }
        public StrideGrid Strides { get; }

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

        /// <summary>Records a key press heard at song time <paramref name="heard"/>; it takes effect now.</summary>
        public void Press(Key key, double heard) =>
            keys.Add(new KeyRecord { Key = key, Down = true, Heard = heard, Effective = Now });

        /// <summary>Records a key release heard at song time <paramref name="heard"/>; it takes effect now.</summary>
        public void Release(Key key, double heard) =>
            keys.Add(new KeyRecord { Key = key, Down = false, Heard = heard, Effective = Now });

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
                double next = Math.Min(beat, step);
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
                if (beat <= step)
                {
                    Beats.Fire(this, beat, State.Beat);
                    int i = Beats.LastBeat;
                    State.LastBeat = i;
                    State.Bar = Beats.BarOf(i, out bool downbeat);
                    State.Downbeat = downbeat;
                }
                else
                {
                    Strides.Fire(this, step);
                    State.StrideStep = step;
                    State.StridePeriod = Strides.Period;
                    State.StrideHalvings = Strides.Halvings;
                }
            }
            cursor = end;
            State.Tick++;
            State.Now = end;
            UpdateBeatState(end);
            if (!State.Frozen) State.RunClock += TickSeconds;
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
