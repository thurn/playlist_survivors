namespace Champions.Sim
{
    /// <summary>
    /// A run's fixed inputs besides the stream and the player's inputs: made by <see cref="RunInputs.Build"/>
    /// from the launch's flags and the content, so a session's replay builds exactly what the run did. The
    /// capacities size the sim's storage up front so a whole level allocates nothing per tick.
    /// </summary>
    public sealed class SimConfig
    {
        /// <summary>Seeds the run's one random generator.</summary>
        public ulong Seed = 1;

        /// <summary>The song time the sim starts at.</summary>
        public double StartTime;

        /// <summary>B: the budget the game adds to the plugin's stamp (more than the sim's lead).</summary>
        public double KnownAtBudget = 0.5;

        /// <summary>How the run starts: at once (tests, scripted runs) or on <see cref="RunControl.Start"/>.</summary>
        public RunStart Start;

        /// <summary>The beat services' numbers (Content/beat.json).</summary>
        public BeatRules Beat = new BeatRules();

        /// <summary>The hero's numbers (Content/hero.json).</summary>
        public HeroRules Hero = new HeroRules();

        // The storage's sizes up front: a list past its size doubles, a large copy and garbage mid-run.
        /// <summary>The ticks kept (20 a second), and the locks' per-tick record.</summary>
        public int TickCapacity = 40_000;
        /// <summary>The beats kept, and as many onsets.</summary>
        public int BeatCapacity = 8_192;
        /// <summary>The hindsight windows' grid beats kept.</summary>
        public int HindsightCapacity = 256;
        /// <summary>Each other stream kind's entries kept (hindsight windows, timbre, changes, jumps, forecasts, ends).</summary>
        public int SparseCapacity = 256;
        public int EntryCapacity = 4_096;
        public int EventCapacity = 65_536;
        public int InputCapacity = 4_096;
    }
}
