using System;
using UnityEngine;

namespace Champions.Render.Structures
{
    /// <summary>
    /// One structure kind as `champions structure bake` writes it for one look (design.md, The World &gt; Structures; plan.md,
    /// Rendering &gt; The edge look), an asset in the ignored Assets/Baked/Structures/Resources/StructureTypes/LOOK/ beside
    /// its meshes in Assets/Baked/Structures/KIND/ (StructureBaker): its Synty model, static, standing on its footprint's
    /// middle at the origin (x along its length, y up, z its depth), as the edge look draws it over the zone's fill, and
    /// the pieces it shatters into. The trailer's is the vendor model unsimplified; the default's tunes it by eye where
    /// that looks better at game scale (Editor/Structures/structures.json says where and why).
    /// </summary>
    public sealed class StructureType : ScriptableObject
    {
        public string Id;
        /// <summary>What the mesh was baked from: "vendor", or "tuned" (the default look's changes to it).</summary>
        public string Source;
        /// <summary>
        /// The body as a style mesh (the monsters' StyleMesh.Unweld): every triangle its own vertices, one submesh,
        /// barycentrics plus a value per triangle in UV3 and the triangle's edges that draw in UV5 (component k 1 for the
        /// edge opposite corner k).
        /// </summary>
        public Mesh Body;
        /// <summary>
        /// The body cut into closed chunks (the monsters' MonsterShardBaker), joined: the body's own triangles closed by
        /// inside faces, UV3 as the body's, UV4 (inside face 0 or 1, the chunk's index into <see cref="Pieces"/>), UV5 the
        /// body's edges plus each chunk's crack, and every edge of an inside face.
        /// </summary>
        public Mesh ShardMesh;
        /// <summary>The chunks, by the index in UV4.y.</summary>
        public StructurePiece[] Pieces = Array.Empty<StructurePiece>();
        /// <summary>Its height over the ground and its extent on the ground (x by z), m.</summary>
        public float HeightM;
        public Vector2 ExtentM;
        /// <summary>The body's triangles and the edges it draws (diagnostics for the review).</summary>
        public int Triangles, Lines;
    }

    /// <summary>One chunk standing as the structure does: the pivot it turns about and its box round the pivot.</summary>
    [Serializable]
    public struct StructurePiece
    {
        public Vector3 Pivot;
        /// <summary>Its half extents round the pivot (its collider's), m.</summary>
        public Vector3 HalfSize;
        /// <summary>Its volume, m³ (its mass's).</summary>
        public float Volume;
    }
}
