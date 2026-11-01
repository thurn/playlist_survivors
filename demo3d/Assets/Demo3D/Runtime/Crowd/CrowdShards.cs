using System;
using UnityEngine;

namespace Demo3D.Runtime.Crowd
{
    /// <summary>
    /// The crystal shatter's shard sets (crowd.md, Monster styles): each type's style mesh, cut at
    /// bake time into thick chunks on its rest pose, with the skin weights carried over so that
    /// <see cref="CrystalShatter"/> can pose the chunks to whatever pose a monster is in when it
    /// breaks (CrowdShardBaker). A third asset beside the library and the style meshes, which only a
    /// player whose style shatters loads; the meshes are its sub-assets. Derived from vendor models,
    /// so it lives only in the gitignored Assets/Baked/Crowd/Resources/.
    /// </summary>
    public sealed class CrowdShards : ScriptableObject
    {
        public const string ResourcePath = "CrowdShards";

        public Set[] Types = Array.Empty<Set>();

        /// <summary>One type's shards, which together are its style mesh (LOD0).</summary>
        [Serializable]
        public sealed class Set
        {
            public string Id;
            public Shard[] Shards = Array.Empty<Shard>();
        }

        /// <summary>
        /// One chunk, on the rest pose in the type's root space at instance scale 1 (as the style
        /// mesh is): never drawn itself, it is the data CrystalShatter skins at the break. Its mesh is
        /// the style mesh's triangles that fell in the chunk (the skin: the style mesh's corners, with
        /// their barycentrics and value per triangle in UV3) closed by inside faces (UV1.x 1) up to an
        /// apex inside the body. Every corner carries the style mesh's bones (UV4) and weights (UV5);
        /// an inside face's corners on the skin carry their skin corner's, and the apex the patch's
        /// area-weighted blend (its four largest).
        /// </summary>
        [Serializable]
        public sealed class Shard
        {
            public Mesh Mesh;
            /// <summary>Its volume (m³) and its largest distance from its centre (m), on the rest pose at scale 1.</summary>
            public float Volume, Size;
            /// <summary>The bone with the most weight over its skin: its collider's box is aligned to it.</summary>
            public int Bone;
        }

        public static CrowdShards Load() =>
            Resources.Load<CrowdShards>(ResourcePath)
            ?? throw new InvalidOperationException(
                "CrowdShards: Assets/Baked/Crowd/Resources/CrowdShards.asset is missing (run `demo3d unity BakeCrowd`)");

        public Set Of(string id) =>
            Array.Find(Types, s => s.Id == id)
            ?? throw new InvalidOperationException($"CrowdShards: no shards for {id} (re-run `demo3d unity BakeCrowd`)");
    }
}
