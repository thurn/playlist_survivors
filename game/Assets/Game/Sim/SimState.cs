namespace PS.Game.Sim
{
    /// <summary>
    /// The latest settled snapshot the client reads (unity-game.md, "The Sim's Contract"). The
    /// skeleton holds time, the run clock, the beat and the hero; the rule beads add enemies,
    /// structures and items as dense arrays here. The client copies what it needs into its own
    /// short history to show <c>frameSong</c>, which trails the frontier.
    ///
    /// Only the sim writes it: every property is <c>{ get; internal set; }</c>, and dense arrays
    /// are exposed as <c>ReadOnlySpan</c> getters over the sim's storage, never as arrays.
    /// </summary>
    public sealed class SimState
    {
        /// <summary>The frontier: the song time the sim has simulated to.</summary>
        public double Now { get; internal set; }
        public long Tick { get; internal set; }

        /// <summary>Playing time: advances with song time except while the world is frozen.</summary>
        public double RunClock { get; internal set; }
        public bool Frozen { get; internal set; }

        public Reliability Beat { get; internal set; }
        /// <summary>The latest committed beat fired, or -1.</summary>
        public int LastBeat { get; internal set; } = -1;
        /// <summary>The latest beat's bar (-1 before any lock), and whether it was a downbeat.</summary>
        public int Bar { get; internal set; } = -1;
        public bool Downbeat { get; internal set; }

        /// <summary>The latest stride step's time, its planned period and halvings (-1 on the free pulse).</summary>
        public double StrideStep { get; internal set; } = double.NegativeInfinity;
        public double StridePeriod { get; internal set; } = StrideGrid.FreePeriod;
        public int StrideHalvings { get; internal set; } = -1;

        public HeroState Hero { get; } = new HeroState();
    }
}
