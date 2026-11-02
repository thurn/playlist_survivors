namespace Champions.Sim
{
    /// <summary>
    /// The hero as the sim last settled it. Positions are ground-plane metres. It holds the chord of the
    /// tick ahead: from where the hero is at the tick's start to where it will be at its end, so
    /// <see cref="PositionAt"/> answers for any time in that tick.
    /// </summary>
    public sealed class HeroState
    {
        /// <summary>Where the hero will be at the tick ahead's end.</summary>
        public double X { get; internal set; }
        public double Y { get; internal set; }

        /// <summary>Where the tick ahead's chord starts, and its start and end times.</summary>
        public double FromX { get; internal set; }
        public double FromY { get; internal set; }
        public double MoveStart { get; internal set; } = double.NegativeInfinity;
        public double MoveEnd { get; internal set; } = double.NegativeInfinity;

        /// <summary>The way the hero faces, a unit vector: its latest travel's, down the screen at the start.</summary>
        public double FacingX { get; internal set; }
        public double FacingY { get; internal set; } = -1;

        /// <summary>Where the hero is at song time <paramref name="t"/> in the tick ahead (or at its end after it).</summary>
        public void PositionAt(double t, out double x, out double y)
        {
            double u = t >= MoveEnd || MoveEnd <= MoveStart ? 1 : System.Math.Max(0, (t - MoveStart) / (MoveEnd - MoveStart));
            x = FromX + (X - FromX) * u;
            y = FromY + (Y - FromY) * u;
        }
    }
}
