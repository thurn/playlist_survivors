namespace PS.Look
{
    public static partial class LookAxes
    {
        /// <summary>The motion layers a preset's <c>motion</c> object switches on or off (neon-look.md, The Axes &gt; Motion).</summary>
        public static readonly string[] MotionLayers = { "springs", "swarm", "trails", "beatRipple" };

        /// <summary>
        /// The motion axis: its built options are the <see cref="MotionLayers"/> built so far. It has no flag of its
        /// own; <c>--motion</c> sets the layers and <c>--beat-ripple</c> switches beatRipple on.
        /// </summary>
        public static readonly LookAxis Motion = new LookAxis("motion", null, "springs", "swarm", "beatRipple");
    }
}
