using System;

namespace PS.Game.Sim
{
    /// <summary>The hero's four directions on the grid; the same values as the matching <see cref="Key"/>s.</summary>
    public enum Direction { Up, Down, Left, Right }

    /// <summary>
    /// The hero as the sim last settled it (unity-game.md, "Hero Movement on the Grid"): the tile it
    /// stands on, or is striding to, and its latest move. Positions are ground-plane metres.
    /// </summary>
    public sealed class HeroState
    {
        /// <summary>The hero's tile: the one it stands on, or is striding to since its move started.</summary>
        public int TileX { get; internal set; }
        public int TileY { get; internal set; }
        /// <summary>The tile's centre.</summary>
        public double X { get; internal set; }
        public double Y { get; internal set; }
        /// <summary>Follows travel; a bump faces the tile it couldn't enter.</summary>
        public Direction Facing { get; internal set; } = Direction.Down;

        /// <summary>Where the latest move (stride, bump or stomp) started, and its start and end times.</summary>
        public double FromX { get; internal set; }
        public double FromY { get; internal set; }
        public double MoveStart { get; internal set; } = double.NegativeInfinity;
        public double MoveEnd { get; internal set; } = double.NegativeInfinity;

        /// <summary>Health, from progression.json's maxHp; contact lunges take it (<see cref="HeroHealth"/>).</summary>
        public double Health { get; internal set; }
        public double MaxHealth { get; internal set; }
        /// <summary>The hero fell ("YOU FELL"): it no longer moves, strikes or takes hits.</summary>
        public bool Fallen { get; internal set; }

        /// <summary>Where the hero is at song time <paramref name="t"/>: on its tile, or striding to it.</summary>
        internal void PositionAt(double t, out double x, out double y)
        {
            double u = t >= MoveEnd || MoveEnd <= MoveStart ? 1 : Math.Max(0, (t - MoveStart) / (MoveEnd - MoveStart));
            x = FromX + (X - FromX) * u;
            y = FromY + (Y - FromY) * u;
        }
    }
}
