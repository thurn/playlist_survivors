using System;
using UnityEngine;

namespace Champions.Render.Crowd
{
    /// <summary>
    /// Every baked edge cut and the shaders that draw them, one asset in the ignored
    /// Assets/Baked/EdgeCuts/Resources/ so a player loads it with <see cref="Load"/> (and a build includes the
    /// shaders). The edge-cut baker (Champions.EditorTools.EdgeCuts) rewrites it after each model's bake.
    /// </summary>
    public sealed class EdgeCutLibrary : ScriptableObject
    {
        public const string ResourcePath = "EdgeCuts";
        /// <summary>The bone textures' rate (game/'s crowd's, which demo3d measured).</summary>
        public const float ClipRate = 30f;

        public EdgeCutType[] Types = Array.Empty<EdgeCutType>();
        public Shader Body, Lines;
        public ComputeShader Cull, Palette, Shards;

        /// <summary>The baked library, or null when there is none (a checkout without the vendor files).</summary>
        public static EdgeCutLibrary Load() => Resources.Load<EdgeCutLibrary>(ResourcePath);

        public int IndexOf(string id)
        {
            for (int i = 0; i < Types.Length; i++)
                if (Types[i] != null && Types[i].Id == id) return i;
            return -1;
        }
    }
}
