namespace PS.Look
{
    public static partial class LookAxes
    {
        /// <summary>How monsters are drawn (neon-look.md, The Axes &gt; Monsters); the presets name dark-core-edges, solid-emissive, wireframe, refractive-gem; wireframe-all is wireframe on every triangle edge.</summary>
        public static readonly LookAxis Monsters = new LookAxis("monsters", "--monster-look", Crowd.EdgeLook.Option, Crowd.SolidLook.Option,
            Crowd.HologramLook.Option, Crowd.HologramLook.AllEdgesOption);
    }
}
