using System;
using UnityEngine;

namespace Game.Client.Structures
{
    /// <summary>
    /// The crystal structures' shard sets (unity-game.md, "Shatter": structure shards keep PhysX),
    /// demo3d's StructureShards copied and owned: each kit mesh a structure site places, cut at
    /// bake time into thick chunks that together are the mesh (StructureShardBaker), which
    /// <see cref="StructureShatter"/> flies when the sim breaks the site. Derived from vendor models,
    /// so it lives only in the gitignored Assets/Baked/Structures/Resources/.
    /// </summary>
    public sealed class StructureShards : ScriptableObject
    {
        public const string ResourcePath = "StructureShards";

        public Set[] Meshes = Array.Empty<Set>();

        /// <summary>One kit mesh's shards.</summary>
        [Serializable]
        public sealed class Set
        {
            /// <summary>The kit mesh they were cut from (a site renderer's shared mesh).</summary>
            public Mesh Source;
            public Shard[] Shards = Array.Empty<Shard>();
        }

        /// <summary>
        /// One chunk: the kit mesh's triangles that fell in it (its skin, with the kit's UVs and
        /// submeshes, so the site's crystal materials draw it as they draw the piece), closed by
        /// inside faces (UV1.x 1) to an apex inside the piece. Its corners are relative to
        /// <see cref="Center"/>, a point in the source mesh's object space.
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
        public Set Of(Mesh mesh) => mesh == null ? null : Array.Find(Meshes, s => s.Source == mesh);
    }
}
