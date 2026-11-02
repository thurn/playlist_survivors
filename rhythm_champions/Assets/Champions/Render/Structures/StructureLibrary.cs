using System;
using UnityEngine;

namespace Champions.Render.Structures
{
    /// <summary>
    /// Every baked structure and the shaders that draw them, one asset in the ignored Assets/Baked/EdgeCuts/Resources/ so a
    /// player loads it with <see cref="Load"/> (and a build includes the shaders). The structure baker
    /// (Champions.EditorTools.EdgeCuts.StructureBaker) rewrites it after each structure's bake.
    /// </summary>
    public sealed class StructureLibrary : ScriptableObject
    {
        public const string ResourcePath = "Structures";

        public StructureType[] Types = Array.Empty<StructureType>();
        public Shader Fill, Lines;

        /// <summary>The baked library, or null when there is none (a checkout without the vendor files).</summary>
        public static StructureLibrary Load() => Resources.Load<StructureLibrary>(ResourcePath);

        public int IndexOf(string id)
        {
            for (int i = 0; i < Types.Length; i++)
                if (Types[i] != null && Types[i].Id == id) return i;
            return -1;
        }

        /// <summary>The most pieces any type breaks into.</summary>
        public int MaxPieces
        {
            get
            {
                int n = 1;
                foreach (var t in Types)
                    if (t != null) n = Math.Max(n, t.Pieces.Length);
                return n;
            }
        }
    }
}
