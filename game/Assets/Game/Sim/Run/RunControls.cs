namespace PS.Game.Sim
{
    /// <summary>Where the run is (unity-game.md, The Run).</summary>
    public enum RunPhase
    {
        /// <summary>Before the start: the world is held and the run clock stands at 0.</summary>
        Waiting = 0,
        Playing = 1,
        /// <summary>The run clock reached the schedule's duration: the finale's blast waits for the next downbeat.</summary>
        Finale = 2,
        Ended = 3,
    }

    /// <summary>Why a run ended.</summary>
    public enum RunEnd
    {
        None = 0,
        /// <summary>The victory card, the finale's <see cref="Content.Finale.VictoryCardS"/> after its blast.</summary>
        Victory = 1,
        /// <summary>The hero fell: "YOU FELL".</summary>
        Death = 2,
        /// <summary>Backspace.</summary>
        Quit = 3,
        /// <summary>The output device changed: the mapping from heard time doesn't survive it.</summary>
        DeviceChanged = 4,
    }

    /// <summary>How a run starts (<see cref="SimConfig.Start"/>).</summary>
    public enum RunStart
    {
        /// <summary>Playing from the sim's start time: tests, bots and the slice's evidence.</summary>
        Immediately = 0,
        /// <summary>On the first committed beat after <see cref="RunControl.Start"/> (Enter or Space).</summary>
        OnKey = 1,
        /// <summary>On the first committed beat after 2 s of clear beat (a headless run, as native's).</summary>
        AfterClearBeat = 2,
    }

    /// <summary>
    /// The inputs that change the run besides the keys (unity-game.md, The Run): each takes effect
    /// at the sim's time when it is given, recorded like a key (<see cref="ControlRecord"/>), so a
    /// replay gives it at the same time.
    /// </summary>
    public enum RunControl
    {
        /// <summary>Enter or Space before the start: the run starts on the next beat.</summary>
        Start = 0,
        /// <summary>Esc or focus loss: the world freezes now (<see cref="FreezeReason.Pause"/>).</summary>
        Pause = 1,
        /// <summary>Esc or a click while paused: play resumes on the next beat.</summary>
        Resume = 2,
        /// <summary>Backspace ends the run now.</summary>
        Quit = 3,
        /// <summary>The output device changed: the run ends now.</summary>
        DeviceChanged = 4,
        /// <summary>The plugin's watchdog released the tap: the music stops on the next stride step.</summary>
        Watchdog = 5,
    }

    /// <summary>A run control as the sim recorded it: a replay gives it at <see cref="Effective"/>.</summary>
    public struct ControlRecord
    {
        public RunControl Control;
        public double Effective;
    }
}
