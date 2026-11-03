namespace Champions.Sim
{
    /// <summary>
    /// The keys the sim takes (design.md, "Controls"): the four movement directions (WASD or the arrows),
    /// Space (the dash, or a card's pick), F (in-place cleave), Enter, and 1-3 (the cards; a card click is its number). The
    /// client maps devices to these; Esc is a run control (<see cref="RunControl"/>).
    /// </summary>
    public enum Key : byte { Up, Down, Left, Right, Space, Enter, One, Two, Three, F }

    /// <summary>What an input is (<see cref="InputRecord"/>).</summary>
    public enum InputKind : byte
    {
        Press = 0,
        Release = 1,
        /// <summary>A click target: a ground point to walk to.</summary>
        Target = 2,
        /// <summary>A run control.</summary>
        Control = 3,
    }

    /// <summary>
    /// An input as the sim recorded it (plan.md, "Inputs and heard time"): <see cref="Heard"/> is the song
    /// time the player heard when making it, and <see cref="Effective"/> the sim's time when it took it,
    /// the previous frame's frontier. A session records both, in the order the sim took them; a replay
    /// gives each at <see cref="Effective"/>, in that order.
    /// </summary>
    public struct InputRecord
    {
        public InputKind Kind;
        /// <summary>The <see cref="Sim.Key"/> of a press or release, the <see cref="RunControl"/> of a control.</summary>
        public byte Code;
        /// <summary>A click target's ground point.</summary>
        public double X, Y;
        public double Heard;
        public double Effective;

        public Key Key => (Key)Code;
        public RunControl Control => (RunControl)Code;
    }
}
