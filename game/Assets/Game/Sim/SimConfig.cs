using PS.Game.Sim.Content;
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

        /// <summary>
        /// The run clock's minute the run starts at (the debug surfaces' `--start-minute`): the clock starts there as
        /// if the run had played to it (<see cref="RunRules.JumpTo"/>), and the hero is built for it, at progression.json's
        /// start level for the minute with each level's first card (<see cref="LevelUps.Reach"/>). 0 starts at 0:00.
        /// </summary>
        public int StartMinute;

        /// <summary>
        /// The level's evidence gate: a reliable beat's and a clear tick's minimum evidence. Native's,
        /// the analyzer's (native/models/manifest.json's <c>gate</c>); a clear track's evidence is about 0.5.
        /// </summary>
        public double Gate = 0.2;

        /// <summary>B: the budget the game adds to the plugin's stamp (more than the sim's lead).</summary>
        public double KnownAtBudget = 0.5;

        /// <summary>The level's arena; null plays an open one (<see cref="ArenaData.Open"/>).</summary>
        public ArenaData Arena;

        /// <summary>The game's content (game/Content), which the horde and the arsenal need; null runs without either.</summary>
        public GameContent Content;

        /// <summary>The enemies' camera footprint and schedule; null runs without enemies.</summary>
        public HordeConfig Horde;

        /// <summary>The hero's starting weapons and passives, its gems and level-ups; null runs without them.</summary>
        public ArsenalConfig Arsenal;

        /// <summary>How the run starts: at once (the default, for tests and bots), on a key, or after clear beat.</summary>
        public RunStart Start;

        /// <summary>Easy: enemy contact damage at half, and floor chickens that heal (unity-game.md, The Run).</summary>
        public bool Easy;

        /// <summary>The hero never falls: it keeps a tenth of its health, its hits still landing (bots and evidence runs).</summary>
        public bool HeroStands;

        // The storage's sizes up front: a list past its size doubles, a large copy and garbage mid-run. The defaults
        // suit tests and short levels; the run's level sizes them for a whole run (RunLevel.Config).
        /// <summary>The ticks kept, and the judging's per-tick record.</summary>
        public int TickCapacity = 40_000;
        /// <summary>The beats kept, and as many onsets.</summary>
        public int BeatCapacity = 8_192;
        /// <summary>The hindsight windows' grid beats kept.</summary>
        public int HindsightCapacity = 256;
        /// <summary>Each other stream kind's entries kept (hindsight windows, timbre, changes, jumps, forecasts, ends).</summary>
        public int SparseCapacity = 256;
        public int EntryCapacity = 4_096;
        public int EventCapacity = 65_536;
        public int PressCapacity = 4_096;
    }
}
