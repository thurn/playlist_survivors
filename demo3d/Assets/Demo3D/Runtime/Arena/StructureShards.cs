using System;
using UnityEngine;

namespace Demo3D.Runtime.Arena
{
    /// <summary>
    /// The structures' shard sets (environment.md, Breaks on screen; epic ps-suy): each kit mesh a structure (a
    /// courtyard's or a ruin wall's piece) places, cut at bake time into thick chunks that together are the mesh
    /// (StructureShardBaker), which <see cref="StructureShatter"/> flies when the fight breaks the piece. Only a player
    /// with `--structure-breaks` loads it. Derived from vendor models, so it lives only in the gitignored
    /// Assets/Baked/Arena/Resources/.
    /// </summary>
    public sealed class StructureShards : ScriptableObject
    {
        public const string ResourcePath = "StructureShards";

        public Set[] Meshes = Array.Empty<Set>();

        /// <summary>ShadowmaskPatch.shader, which a break needs (ArenaRuntime.Break): referenced here so the player has it.</summary>
        public Shader ShadowmaskPatch;

        /// <summary>One kit mesh's shards.</summary>
        [Serializable]
        public sealed class Set
        {
            /// <summary>The kit mesh they were cut from (a structure renderer's shared mesh).</summary>
            public Mesh Source;
            public Shard[] Shards = Array.Empty<Shard>();
        }

        /// <summary>
        /// One chunk: the kit mesh's triangles that fell in it (its skin, with the kit's UVs and submeshes, so the kit's
        /// or a style's materials draw it as they draw the piece), closed by inside faces (UV1.x 1) to an apex inside the
        /// piece. Its corners are relative to <see cref="Center"/>, a point in the source mesh's object space.
        /// </summary>
        [Serializable]
        public sealed class Shard
        {
            public Mesh Mesh;
            public Vector3 Center;
            /// <summary>Its volume (m³) and its largest distance from its centre (m), in the source mesh's space.</summary>
            public float Volume, Size;
        }

        public static StructureShards Load() =>
            Resources.Load<StructureShards>(ResourcePath)
            ?? throw new InvalidOperationException(
                "StructureShards: Assets/Baked/Arena/Resources/StructureShards.asset is missing (run `demo3d unity BakeAll -only StructureShards`)");

        /// <summary>The shards cut from <paramref name="mesh"/>, or null if none were baked for it.</summary>
        public Set Of(Mesh mesh) => mesh == null ? null : Array.Find(Meshes, s => s.Source == mesh);
    }
}
