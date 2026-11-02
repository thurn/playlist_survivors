using System;
using UnityEngine;

namespace Champions.Render.Structures
{
    /// <summary>
    /// One structure's edge cut (design.md, The World &gt; Structures, The Look &gt; Edge cuts): its fill, the low-poly body
    /// drawn in the zone's dim fill colour, and its lines in the zone's edge colour, static, standing on its footprint's
    /// middle at the origin (x along its length, y up, z its depth), and the pieces both break into.
    /// </summary>
    public sealed class StructureType : ScriptableObject
    {
        public string Id;
        /// <summary>
        /// The fill: flat-shaded triangles (each its own three vertices, its face's normal), its shard piece in UV1.x; one
        /// submesh, drawn from both sides.
        /// </summary>
        public Mesh Fill;
        /// <summary>
        /// The lines: each segment a quad of four vertices that the shader widens to a constant pixel width. A vertex holds
        /// its own end (position) and the other end with the side ±1 in UV0, and in UV1 the segment's importance and piece.
        /// </summary>
        public Mesh Lines;
        /// <summary>The shard pieces, by the index in the meshes' piece channels.</summary>
        public StructurePiece[] Pieces = Array.Empty<StructurePiece>();
        /// <summary>Its height over the ground and its extent on the ground (x by z), m.</summary>
        public float HeightM;
        public Vector2 ExtentM;
        public int Triangles, Segments;
    }

    /// <summary>One shard piece standing as the structure does: the pivot it turns about and its box round the pivot.</summary>
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
