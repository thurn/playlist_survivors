namespace PS.Game.Sim
{
    /// <summary>
    /// The enemies' flow field toward the hero (<see cref="FlowField"/>), as the debug overlay reads it: its grid of
    /// cells, each one's path cost to the hero and whether it is blocked, and the way downhill anywhere. A read, as
    /// often as the client likes; <see cref="Exists"/> is false in a run without enemies.
    /// </summary>
    public readonly struct FlowFieldView
    {
        /// <summary>A cell's cost a cell's width from the hero, straight; a cell the search didn't reach costs <see cref="Unreached"/>.</summary>
        public const int Straight = FlowField.Straight, Unreached = FlowField.Unreached;

        readonly FlowField field;

        internal FlowFieldView(FlowField field) => this.field = field;

        public bool Exists => field != null;
        public int W => field.Grid.W;
        public int H => field.Grid.H;
        /// <summary>The grid's corner and its cells' width, metres.</summary>
        public double MinX => field.Grid.MinX;
        public double MinY => field.Grid.MinY;
        public double CellM => field.Grid.CellM;
        /// <summary>The costliest cell the search reaches: its reach.</summary>
        public int MaxCost => field.MaxCost;

        public int Cost(int cx, int cy) => field.Cost[cy * W + cx];
        public bool Blocked(int cx, int cy) => field.Grid.IsBlocked(cy * W + cx);

        /// <summary>The way downhill at (x, y) (<see cref="FlowField.Direction"/>); false where nothing is reached or at the hero.</summary>
        public bool Direction(double x, double y, out double dirX, out double dirY) => field.Direction(x, y, out dirX, out dirY);
    }
}
