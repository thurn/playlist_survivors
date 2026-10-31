using PS.Game.Sim.Content;

namespace PS.Game.Sim
{
    /// <summary>
    /// What the enemies need besides the stream and the arena (unity-game.md, Enemies): the
    /// content, the camera's ground footprint and, for the slice, a test schedule of a few roles.
    /// M3 replaces the schedule with the content's full one.
    /// </summary>
    public sealed class HordeConfig
    {
        public GameContent Content;

        /// <summary>
        /// The camera's footprint on the ground relative to the hero, as a convex polygon around the
        /// origin: x0, y0, x1, y1, ... in metres. The client passes its camera's geometry; the
        /// default is a 34 m by 22 m view, the loud zoom's width.
        /// </summary>
        public double[] CameraFootprint = { -17, -11, 17, -11, 17, 11, -17, 11 };

        /// <summary>The slice's roster: every <see cref="SpawnIntervalS"/> of run clock one of each spawns, and whole rosters while fewer than <see cref="Minimum"/> are alive.</summary>
        public string[] Roster = { "bat1", "zombie", "skeleton", "mudman1" };
        public double SpawnIntervalS = 1;
        public int Minimum = 120;
    }
}
