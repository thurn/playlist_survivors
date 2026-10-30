using System;
using UnityEngine;

namespace Demo3D.Runtime.Crowd
{
    /// <summary>
    /// The crystal shatter's shard sets (crowd.md, Monster styles): each type's death pose, cut at
    /// bake time into thick chunks (CrowdShardBaker). A third asset beside the library and the style
    /// meshes, which only a player whose style shatters loads (<see cref="CrystalShatter"/>); the
    /// meshes are its sub-assets. Derived from vendor models, so it lives only in the gitignored
    /// Assets/Baked/Crowd/Resources/.
    /// </summary>
    public sealed class CrowdShards : ScriptableObject
    {
        public const string ResourcePath = "CrowdShards";

        public Set[] Types = Array.Empty<Set>();

        /// <summary>One type's shards, which together are its style mesh (LOD0) at the held death pose.</summary>
        [Serializable]
        public sealed class Set
        {
            public string Id;
            /// <summary>The death clip's time the pose was taken at (TimelineEvaluator holds the death there).</summary>
            public float HoldTime;
            public Shard[] Shards = Array.Empty<Shard>();
            /// <summary>
            /// The style mesh's LOD2 at the same pose, in root space: the crowd's shadow proxy (the
            /// default cuts' shadow-proxy), which casts the assembled shards' shadow through the hold so
            /// the swap leaves the shadow as it was.
            /// </summary>
            public Mesh HoldShadow;
        }

        /// <summary>
        /// One chunk, in the type's root space at instance scale 1 (as the crowd's meshes are),
        /// relative to <see cref="Center"/>. Its mesh is the style mesh's triangles that fell in the
        /// chunk (the skin, with the style mesh's barycentrics and value per triangle in UV3) closed
        /// by inside faces (UV1.x 1) up to a point inside the body; <see cref="Hull"/> is the points
        /// of its convex collider.
        /// </summary>
        [Serializable]
        public sealed class Shard
        {
            public Mesh Mesh;
            public Mesh Hull;
            public Vector3 Center;
            /// <summary>Its volume (m³) and its largest distance from the centre (m), at scale 1.</summary>
            public float Volume, Size;
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
