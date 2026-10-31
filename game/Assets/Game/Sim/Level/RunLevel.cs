using PS.Game.Sim.Content;

namespace PS.Game.Sim.Level
{
    /// <summary>
    /// The run's level (unity-game.md, Build Order step 4): the game's arena, schedule.json's
    /// minutes and bosses, and the arsenal from the starting sword, with its gems and level-ups.
    /// </summary>
    public static class RunLevel
    {
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
                Horde = new HordeConfig { CameraFootprint = footprint, Schedule = true },
                Arsenal = new ArsenalConfig(),
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
