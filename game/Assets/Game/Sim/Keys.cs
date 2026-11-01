namespace PS.Game.Sim
{
    /// <summary>
    /// The keys the sim takes: four directions, Space, which stomps, Enter, and Attack (Z), which strikes in a mode
    /// whose sword waits for it (<see cref="SwordTrigger.Press"/>). During a level-up Left, Up and Right select its
    /// cards and Space or Enter confirms (<see cref="LevelUps"/>).
    /// </summary>
    public enum Key { Up, Down, Left, Right, Space, Enter, Attack }

    /// <summary>
    /// A key change as the sim recorded it (unity-game.md, "Key Presses"):
    /// <see cref="Heard"/> is the song time the player heard when pressing (<c>keySong</c>), and
    /// <see cref="Effective"/> the sim's time when it took the key, the previous frame's frontier.
    /// A session records both; a replay applies the key at <see cref="Effective"/>.
    /// </summary>
    public struct KeyRecord
    {
        public Key Key;
        /// <summary>True for a press, false for a release.</summary>
        public bool Down;
        public double Heard;
        public double Effective;
        /// <summary>Its place among the run's key changes, debug commands and run controls, which a replay keeps.</summary>
        public int Order;
    }

    /// <summary>
    /// A pointer's target as the sim recorded it (<see cref="Simulation.Target"/>): the ground point a click or a held
    /// button asked the hero to walk to, heard and taken as a key is.
    /// </summary>
    public struct TargetRecord
    {
        public double X, Y;
        public double Heard;
        public double Effective;
        public int Order;
    }
}
