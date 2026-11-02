namespace Champions.Sim
{
    /// <summary>Where the run is.</summary>
    public enum RunPhase
    {
        /// <summary>Before the start: the world is held and the run clock stands at 0.</summary>
        Waiting = 0,
        Playing = 1,
        Ended = 2,
    }

    /// <summary>Why a run ended.</summary>
    public enum RunEnd
    {
        None = 0,
        Victory = 1,
        Death = 2,
        /// <summary>The player ended it (End run, Esc to the title).</summary>
        Quit = 3,
    }

    /// <summary>How a run starts (<see cref="SimConfig.Start"/>).</summary>
    public enum RunStart : byte
    {
        /// <summary>Playing from the sim's start time: tests and scripted runs.</summary>
        Immediately = 0,
        /// <summary>On <see cref="RunControl.Start"/>.</summary>
        OnControl = 1,
        /// <summary>
        /// The game's (design.md, "Starting"): the first offer opens at the sim's start, the world empty and held, and the
        /// run starts on the first downbeat after its pick, or at once with no reliable beat (<see cref="Progression"/>).
        /// </summary>
        FirstOffer = 2,
    }

    /// <summary>
    /// The inputs that change the run besides the keys and click targets (design.md, "Pauses: only the
    /// player's"): each takes effect at the sim's time when it is given and is recorded like a key.
    /// </summary>
    public enum RunControl : byte
    {
        /// <summary>The run starts now (the level's start, after its first pick, gives it).</summary>
        Start = 0,
        /// <summary>Esc or losing focus: the world holds now.</summary>
        Pause = 1,
        /// <summary>Resume: play goes on now.</summary>
        Resume = 2,
        /// <summary>End run: the run ends now.</summary>
        Quit = 3,
    }

    /// <summary>What holds the world (design.md: cards and the player's pause, nothing else).</summary>
    public enum HoldReason
    {
        Pause = 0,
        /// <summary>A card moment (the progression's).</summary>
        Cards = 1,
    }

    /// <summary>
    /// The run's start, holds, clock and end (design.md, "The Run" and Terms, "Run clock"). The run clock
    /// advances with song time while the run plays and nothing holds the world; the music never stops it.
    /// The level's rules (its timeline, death and victory) end it through <see cref="End"/>; the
    /// progression holds it for cards through <see cref="Hold"/> and starts it after the first offer (<see cref="Start"/>).
    /// </summary>
    internal sealed class RunRules
    {
        readonly SimState state;

        public RunRules(SimConfig config, SimState state)
        {
            this.state = state;
            if (config.Start == RunStart.Immediately)
            {
                state.Phase = RunPhase.Playing;
                state.StartedAt = state.Now;
            }
        }

        public void Control(Simulation sim, RunControl control, double now)
        {
            switch (control)
            {
                case RunControl.Start:
                    Start(sim, now);
                    break;
                case RunControl.Pause:
                    if (state.Phase == RunPhase.Playing) Hold(sim, HoldReason.Pause, true, now);
                    break;
                case RunControl.Resume:
                    Hold(sim, HoldReason.Pause, false, now);
                    break;
                case RunControl.Quit:
                    End(sim, RunEnd.Quit, now);
                    break;
            }
        }

        /// <summary>The run starts at <paramref name="now"/>, once.</summary>
        public void Start(Simulation sim, double now)
        {
            if (state.Phase != RunPhase.Waiting) return;
            state.Phase = RunPhase.Playing;
            state.StartedAt = now;
            sim.Emit(SimEvent.RunStart(now));
        }

        /// <summary>Holds the world for <paramref name="reason"/>, or lets it go; a change emits a <see cref="SimEventKind.Hold"/>.</summary>
        public void Hold(Simulation sim, HoldReason reason, bool on, double now)
        {
            int bit = 1 << (int)reason;
            int holds = on ? state.Holds | bit : state.Holds & ~bit;
            if (holds == state.Holds) return;
            state.Holds = holds;
            sim.Emit(SimEvent.Hold(now, reason, on, holds));
        }

        /// <summary>Ends the run for <paramref name="why"/>, once.</summary>
        public void End(Simulation sim, RunEnd why, double now)
        {
            if (state.Phase == RunPhase.Ended) return;
            state.Phase = RunPhase.Ended;
            state.End = why;
            state.EndedAt = now;
            sim.Emit(SimEvent.RunEnd(now, why, state.RunClock));
        }

        /// <summary>A tick passed.</summary>
        public void Tick()
        {
            if (state.Phase == RunPhase.Playing && state.Holds == 0) state.RunClock += Simulation.TickSeconds;
        }
    }
}
