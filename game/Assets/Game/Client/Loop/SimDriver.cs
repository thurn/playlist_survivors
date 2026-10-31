using System.Diagnostics;
using PS.Game.Sim;

namespace Game.Client.Loop
{
    /// <summary>
    /// The main thread's frame loop around the sim (unity-game.md, Architecture), without Unity
    /// objects so tests can drive it. A frame, in order:
    /// <code>
    /// driver.Add(entry)            // each new plugin entry, used from max(stamp + B, frontier)
    /// keys.Drain(sim, clock)       // each new key, heard at keySong, at the sim's current time
    /// driver.Step(now)             // frontier = songAtDsp(dspNow) + margin; sim.StepTo(frontier);
    ///                              // record the state; dispatch new events; present at frameSong
    /// </code>
    /// Nothing is allocated per frame.
    /// </summary>
    public sealed class SimDriver
    {
        readonly double _knownAtBudget;

        public SimDriver(SimConfig config, IGameClock clock)
        {
            _knownAtBudget = config.KnownAtBudget;
            Sim = new Simulation(config);
            Clock = clock;
        }

        public Simulation Sim { get; }
        public IGameClock Clock { get; }
        public StateHistory History { get; } = new StateHistory();
        public EventDispatch Events { get; } = new EventDispatch();

        /// <summary>The synthetic event source dispatched beside the sim's, or null.</summary>
        public ScriptedEvents Scripted { get; set; }

        /// <summary>The latest frame's times, and what it presented.</summary>
        public FrameTimes Times { get; private set; }
        public PresentFrame Presented { get; private set; }
        public int Frames { get; private set; }

        /// <summary>Frames that showed the frontier's state because frameSong was past it.</summary>
        public int FramesAtFrontier { get; private set; }

        /// <summary>Frames whose frameSong was behind the previous frame's (the clock guarantees none).</summary>
        public int BackwardFrames { get; private set; }

        /// <summary>The latest frame's sim step and whole step (including dispatch and presenting), in ms.</summary>
        public double StepMs { get; private set; }
        public double FrameMs { get; private set; }

        /// <summary>
        /// Settled entries that arrived after the sim had passed stamp + B, so were known late.
        /// A fixed-time run's waiting makes this 0; anything else would make its frames depend on timing.
        /// </summary>
        public int LateEntries { get; private set; }

        /// <summary>A live entry from the plugin: known from max(stamp + B, frontier) (<see cref="Simulation.Add"/>).</summary>
        public void Add(in StreamEntry e) => Sim.Add(e);

        /// <summary>
        /// An entry whose arrival is guaranteed in time, known exactly from stamp + B (from the
        /// sim's start for the stream's first, whose stamps precede it). A --fixed-time run feeds
        /// entries this way, once the plugin's analysis covers the next frontier, so its frames
        /// don't depend on when the plugin's thread delivered them.
        /// </summary>
        public void AddSettled(in StreamEntry e)
        {
            double knownAt = e.Stamp + _knownAtBudget;
            if (knownAt < Sim.Now)
            {
                if (Frames > 0) LateEntries++;
                knownAt = Sim.Now;
            }
            Sim.AddKnown(e, knownAt);
        }

        /// <summary>The latest stamp whose entries must have arrived before stepping to `frontier`.</summary>
        public double StampNeededFor(double frontier) => frontier - _knownAtBudget;

        /// <summary>
        /// Steps the sim to this frame's frontier, records its state, dispatches the events that
        /// settled and presents frameSong. Returns false, doing nothing, until the clock is ready.
        /// </summary>
        public bool Step(in ClockReadings now)
        {
            if (!Clock.Ready) return false;
            long start = Stopwatch.GetTimestamp();
            var times = Clock.Frame(now);
            if (Frames > 0 && times.FrameSong < Times.FrameSong) BackwardFrames++;
            Times = times;

            long stepStart = Stopwatch.GetTimestamp();
            Sim.StepTo(times.Frontier);
            StepMs = Ms(Stopwatch.GetTimestamp() - stepStart);
            History.Record(Sim.State);

            Events.DrainSim(Sim);
            Scripted?.DispatchUpTo(Sim.Now, Events);

            var frame = new PresentFrame { Song = times.FrameSong, Frontier = Sim.Now };
            frame.AtFrontier = !History.Sample(times.FrameSong, out frame.State);
            if (frame.AtFrontier) FramesAtFrontier++;
            Presented = frame;
            Events.Present(frame);
            Frames++;
            FrameMs = Ms(Stopwatch.GetTimestamp() - start);
            return true;
        }

        static double Ms(long ticks) => ticks * 1000.0 / Stopwatch.Frequency;
    }
}
