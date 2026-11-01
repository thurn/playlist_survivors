namespace PS.Look
{
    public static partial class LookAxes
    {
        /// <summary>
        /// How the void world's crystal props and formations are drawn (neon-look.md, The Axes &gt; World), after the monster
        /// looks; the presets name edge-glass, solid-glass, wireframe, refractive-gem (<see cref="StructureLook"/>).
        /// </summary>
        public static readonly LookAxis Structures = new LookAxis("structures", "--structure-look",
            StructureLook.EdgeGlass, StructureLook.SolidGlass, StructureLook.Wireframe, StructureLook.RefractiveGem);
    }
}
