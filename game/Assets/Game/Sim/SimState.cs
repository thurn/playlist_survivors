namespace PS.Game.Sim
{
    /// <summary>
    /// The latest settled snapshot the client reads (unity-game.md, "The Sim's Contract"). The
    /// skeleton holds time, the run clock and the beat; the rule beads add the hero, enemies,
    /// structures and items as dense arrays here. The client copies what it needs into its own
    /// short history to show <c>frameSong</c>, which trails the frontier.
    /// </summary>
    public sealed class SimState
    {
        /// <summary>The frontier: the song time the sim has simulated to.</summary>
        public double Now;
        public long Tick;

        /// <summary>Playing time: advances with song time except while the world is frozen.</summary>
        public double RunClock;
        public bool Frozen;

        public Reliability Beat;
        /// <summary>The latest committed beat fired, or -1.</summary>
        public int LastBeat = -1;
        /// <summary>The latest beat's bar (-1 before any lock), and whether it was a downbeat.</summary>
        public int Bar = -1;
        public bool Downbeat;

        /// <summary>The latest stride step's time, its planned period and halvings (-1 on the free pulse).</summary>
        public double StrideStep = double.NegativeInfinity;
        public double StridePeriod = StrideGrid.FreePeriod;
        public int StrideHalvings = -1;
    }
}
