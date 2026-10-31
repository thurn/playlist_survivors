namespace PS.Game.Sim
{
    /// <summary>
    /// The keys the sim takes: four directions, Space, which stomps, and Enter. During a level-up
    /// Left, Up and Right select its cards and Space or Enter confirms (<see cref="LevelUps"/>).
    /// </summary>
    public enum Key { Up, Down, Left, Right, Space, Enter }

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
}
