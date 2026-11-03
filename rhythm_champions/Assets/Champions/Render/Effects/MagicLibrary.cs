using UnityEngine;

namespace Champions.Render
{
    /// <summary>Locally baked closed facet primitives for the Hovl-derived choreography recipes.</summary>
    public sealed class MagicLibrary : ScriptableObject
    {
        public Mesh Link, Node, Bowl;
        public Shader Edge;
        public static MagicLibrary Load() => Resources.Load<MagicLibrary>("MagicWeapons");
    }
}
