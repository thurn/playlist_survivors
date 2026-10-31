namespace PS.Game.Sim
{
    /// <summary>
    /// The debug surfaces' commands (unity-game.md, Debug Surfaces) that the slice needs: M3's F1
    /// menu adds the rest. Each takes effect at the sim's time when it is given, like a key.
    /// </summary>
    public enum DebugCommand
    {
        /// <summary>A section change now: it lands on the next downbeat as a detected one (a zone change, a slam, regrowth after).</summary>
        SectionChange = 0,
        /// <summary>The next main strike to wind up is heavy, as on an accent, whatever the music.</summary>
        Accent = 1,
        /// <summary>Every standing structure within <see cref="Simulation.DebugBreakReachM"/> of the hero breaks now, large ones too.</summary>
        BreakStructures = 2,
        /// <summary>The broken structures regrow on the next downbeat, as after a zone change (the 3 m rule holds).</summary>
        RegrowStructures = 3,
    }

    /// <summary>A debug command as the sim recorded it: a replay gives it at <see cref="Effective"/>.</summary>
    public struct DebugRecord
    {
        public DebugCommand Command;
        public double Effective;
    }
}
