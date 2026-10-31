using PS.Game.Sim.Content;

namespace PS.Game.Sim.Level
{
    /// <summary>
    /// The cinematic slice's level (unity-game.md, Build Order step 2): the game's arena, a horde
    /// of a few roles in gem colours across the spectrum, and the arsenal from the starting sword,
    /// with its gems and level-ups. M3's run replaces it with the full schedule and its cast.
    ///
    /// The hero stands (<see cref="SimConfig.HeroStands"/>): a fall needs the run's card, which
    /// comes with M3.
    /// </summary>
    public static class SliceLevel
    {
        /// <summary>
        /// Early-run roles in five crystal colours (violet bats, pink slimes, red demons, cyan skeletons,
        /// green goblins) that the starting sword kills in a hit, but for the goblins' seven. Tougher roles
        /// stall at the edge of its knockback for half a minute, and the flower barely moves.
        /// </summary>
        public static readonly string[] Roster = { "bat1", "zombie", "ghost", "skeleton", "mudman1" };
        /// <summary>
        /// A roster a second, and whole rosters while fewer than 220 are alive: the sword kills about
        /// 20 a second on house-128, so a steady stream reaches it from every side, well under the 300 cap.
        /// </summary>
        public const double SpawnIntervalS = 1;
        public const int Minimum = 220;

        /// <summary>
        /// The slice's config: <paramref name="footprint"/> is the camera's widest view on the
        /// ground around the hero (<see cref="HordeConfig.CameraFootprint"/>), so nothing spawns in view.
        /// </summary>
        public static SimConfig Config(GameContent content, ArenaData arena, double[] footprint, ulong seed = 1) =>
            new SimConfig
            {
                Seed = seed,
                Arena = arena,
                Content = content,
                Horde = new HordeConfig
                {
                    CameraFootprint = footprint,
                    Roster = Roster,
                    SpawnIntervalS = SpawnIntervalS,
                    Minimum = Minimum,
                },
                Arsenal = new ArsenalConfig(),
                HeroStands = true,
            };
    }
}
