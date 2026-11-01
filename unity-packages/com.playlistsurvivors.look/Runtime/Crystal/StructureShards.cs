using System;
using UnityEngine;

namespace PS.Look
{
    /// <summary>
    /// Shard sets for <see cref="StructureShatter"/>: each source mesh cut into thick chunks that together are the mesh.
    /// The crystal kit's props and formations make theirs in code (<see cref="CrystalKit"/>); the game's bake cuts the
    /// kit meshes its arena places (its StructureShardBaker), which, derived from vendor models, live only in the
    /// game's gitignored Assets/Baked/Structures/Resources/ (<see cref="Load"/>).
    /// </summary>
    public sealed class StructureShards : ScriptableObject
    {
        public const string ResourcePath = "StructureShards";

        public Set[] Meshes = Array.Empty<Set>();

        /// <summary>One source mesh's shards.</summary>
        [Serializable]
        public sealed class Set
        {
            /// <summary>The mesh they were cut from (a site renderer's shared mesh).</summary>
            public Mesh Source;
            public Shard[] Shards = Array.Empty<Shard>();
        }

        /// <summary>
        /// One chunk: the source mesh's faces that fell in it (its skin, drawn by the site's materials as they draw the
        /// piece), closed by inside faces (UV1.x 1). Its corners are relative to <see cref="Center"/>, a point in the
        /// source mesh's object space.
        /// </summary>
        [Serializable]
        public sealed class Shard
        {
            public Mesh Mesh;
            public Vector3 Center;
            /// <summary>Its volume (m³) and its largest distance from its centre (m), in the source mesh's space.</summary>
            public float Volume, Size;
        }

        /// <summary>The baked shards, or null when the build has none (no bake, or the bake failed).</summary>
        public static StructureShards Load() => Resources.Load<StructureShards>(ResourcePath);

        /// <summary>The shards cut from <paramref name="mesh"/>, or null if none were baked for it.</summary>
        public Set Of(Mesh mesh)
        {
            if (mesh == null) return null;
            foreach (var set in Meshes)
                if (set.Source == mesh) return set;
            return null;
        }
    }
}
