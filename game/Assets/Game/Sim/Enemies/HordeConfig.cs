namespace PS.Game.Sim
{
    /// <summary>
    /// What the enemies need besides the stream, the arena and the content (unity-game.md, Enemies):
    /// the camera's ground footprint, and either the run's schedule (schedule.json's minutes,
    /// <see cref="Schedule"/>) or a test roster of a few roles (the slice's, and tests').
    /// </summary>
    public sealed class HordeConfig
    {
        /// <summary>
        /// The camera's footprint on the ground relative to the hero, as a convex polygon around the
        /// origin: x0, y0, x1, y1, ... in metres. The client passes its camera's geometry; the
        /// default is a 34 m by 22 m view, the loud zoom's width.
        /// </summary>
        public double[] CameraFootprint = { -17, -11, 17, -11, 17, 11, -17, 11 };

        /// <summary>Play schedule.json's minutes, bosses and boss moves; the roster, interval and minimum below are then unused.</summary>
        public bool Schedule;

        /// <summary>Play the music-driven events (<see cref="MusicEvents"/>): the minutes' swarms, the set pieces and the signature minotaur. The debug commands start them in any run.</summary>
        public bool MusicEvents;

        /// <summary>The slice's roster: every <see cref="SpawnIntervalS"/> of run clock one of each spawns, and whole rosters while fewer than <see cref="Minimum"/> are alive.</summary>
        public string[] Roster = { "bat1", "zombie", "skeleton", "mudman1" };
        public double SpawnIntervalS = 1;
        public int Minimum = 120;

        /// <summary>Roles placed in a row in front of the hero at the start (the cast's stills); schedule.json's boss-move roles come as bosses.</summary>
        public string[] Lineup = new string[0];
    }
}
