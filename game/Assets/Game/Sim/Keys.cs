namespace PS.Game.Sim
{
    /// <summary>The keys the sim takes: four directions, and Space, which stomps.</summary>
    public enum Key { Up, Down, Left, Right, Space }

    /// <summary>
    /// A key change as the sim recorded it (unity-game.md, "Key Presses and Late Steps"):
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
    }
}
