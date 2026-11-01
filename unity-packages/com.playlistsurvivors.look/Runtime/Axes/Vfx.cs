namespace PS.Look
{
    public static partial class LookAxes
    {
        /// <summary>
        /// The procedural effects' treatment (neon-look.md, The Axes &gt; VFX): light-streaks (crisp white-cored ribbons and
        /// streaks in the hero's trail colour), prism-streaks (softer, split into the palette's hue families). Each project's
        /// effect selection names the effects an option draws in place of others (<see cref="Effects.EffectLibrary.Variants"/>).
        /// </summary>
        public static readonly LookAxis Vfx = new LookAxis("vfx", "--vfx", "light-streaks", "prism-streaks");
    }
}
