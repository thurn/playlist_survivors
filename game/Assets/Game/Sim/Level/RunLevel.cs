using PS.Game.Sim.Content;

namespace PS.Game.Sim.Level
{
    /// <summary>
    /// The run's level (unity-game.md, Build Order step 4): the game's arena, schedule.json's
    /// minutes and bosses, the music-driven events, and the arsenal from the starting sword, with its gems and level-ups.
    /// </summary>
    public static class RunLevel
    {
        /// <summary>
        /// The game camera's footprint at 16:9 (the client's <c>CameraRig.Footprint(16 / 9)</c>, which
        /// LookTests holds it to), for runs with no camera: the sweeps and the sim's tests.
        /// </summary>
        public static double[] GameFootprint => new[] { -18.31, -13.83, 18.31, -13.83, 24.93, 18.37, -24.93, 18.37 };

        /// <summary>
        /// The run's config: <paramref name="footprint"/> is the camera's widest view on the ground
        /// around the hero (<see cref="HordeConfig.CameraFootprint"/>), so nothing spawns in view.
        /// </summary>
        public static SimConfig Config(GameContent content, ArenaData arena, double[] footprint, ulong seed = 1) =>
            new SimConfig
            {
                Seed = seed,
                Arena = arena,
                Content = content,
                Horde = new HordeConfig { CameraFootprint = footprint, Schedule = true, MusicEvents = true },
                Arsenal = new ArsenalConfig(),
                // The stream of a whole run, sized for 40 minutes of song (30 of run clock, its freezes, a slow start)
                // from a full run's 2080 s on jarvis, with room for denser music: 50 ms ticks; 5.4 onsets and 1.7 beats
                // a second; 33 hindsight grid beats a second, more at faster tempos; a hindsight window and a timbre a
                // second; 6080 key changes by the bot (ps-msz.8.9). About 35 MB at the start, instead of doubling to 20 MB with 45 MB of garbage.
                TickCapacity = 48_000,
                BeatCapacity = 24_576,
                HindsightCapacity = 120_000,
                SparseCapacity = 3_000,
                PressCapacity = 16_384,
            };

        /// <summary>
        /// The cast's evidence level: <paramref name="roles"/> stand in a row in front of the hero
        /// from the start (<see cref="HordeConfig.Lineup"/>), the bosses' roles as bosses, and
        /// nothing else spawns. The hero stands and has no sword, so they stay to be seen.
        /// </summary>
        public static SimConfig Lineup(GameContent content, ArenaData arena, double[] footprint, string[] roles, ulong seed = 1) =>
            new SimConfig
            {
                Seed = seed,
                Arena = arena,
                Content = content,
                Horde = new HordeConfig
                {
                    CameraFootprint = footprint, Roster = new string[0], Minimum = 0, Lineup = roles,
                },
                HeroStands = true,
            };
    }
}
