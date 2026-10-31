using PS.Game.Sim.Level;

namespace PS.Game.Sim
{
    /// <summary>
    /// A run's fixed inputs besides the stream and keys. The defaults are unity-game.md's; the
    /// capacities size the sim's storage up front so a 30-minute run allocates nothing per tick.
    /// </summary>
    public sealed class SimConfig
    {
        /// <summary>Seeds the run's one random generator.</summary>
        public ulong Seed = 1;

        /// <summary>The song time the sim starts at.</summary>
        public double StartTime;

        /// <summary>The level's evidence gate: a reliable beat's and a clear tick's minimum evidence.</summary>
        public double Gate = 0.5;

        /// <summary>B: the budget the game adds to the plugin's stamp (more than the sim's lead).</summary>
        public double KnownAtBudget = 0.5;

        /// <summary>The level's arena; null plays an open one (<see cref="ArenaData.Open"/>).</summary>
        public ArenaData Arena;

        public int TickCapacity = 40_000;
        public int BeatCapacity = 8_192;
        public int EntryCapacity = 4_096;
        public int EventCapacity = 65_536;
        public int PressCapacity = 4_096;
    }
}
