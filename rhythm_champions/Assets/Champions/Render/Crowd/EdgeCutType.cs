using System;
using UnityEngine;

namespace Champions.Render.Crowd
{
    /// <summary>
    /// One model's edge cut (design.md, The Look &gt; Edge cuts): its dark body and its lines, skinned to the
    /// entries of its bone texture, and the pieces both break into.
    /// </summary>
    public sealed class EdgeCutType : ScriptableObject
    {
        public string Id;
        /// <summary>
        /// The body: rest pose in root space at <see cref="HeightM"/>, entry indices in UV1, weights in UV2, its
        /// shard piece in UV3.x; one submesh of triangles, drawn from both sides.
        /// </summary>
        public Mesh Body;
        /// <summary>
        /// The lines: each segment a quad of four vertices that the shader widens to a constant pixel width.
        /// A vertex holds its own end (position, entries in UV1, weights in UV2) and the other end (position and
        /// side ±1 in UV0, entries in UV3, weights in UV4), and in UV5 the segment's importance and piece.
        /// </summary>
        public Mesh Lines;
        /// <summary>Skinning matrices at <see cref="EdgeCutLibrary.ClipRate"/>: entry b's row r of frame f is texel (3b + r, f).</summary>
        public Texture2D Bones;
        public int BoneCount;
        public EdgeClip[] Clips = Array.Empty<EdgeClip>();
        /// <summary>Yaw-invariant bounds at scale 1 over every clip: a sphere around (0, CenterY, 0).</summary>
        public float CenterY, Radius;
        /// <summary>The standing height at scale 1 (the recipe's).</summary>
        public float HeightM;
        /// <summary>A flyer's height over the ground at scale 1, added to its position's y.</summary>
        public float HoverM;
        /// <summary>The shard pieces, by the index in the meshes' piece channels.</summary>
        public EdgeShardPiece[] Pieces = Array.Empty<EdgeShardPiece>();
        public int Triangles, Segments;

        public int Clip(string role)
        {
            for (int i = 0; i < Clips.Length; i++)
                if (Clips[i].Role == role) return i;
            return -1;
        }
    }

    /// <summary>One clip in a type's bone texture.</summary>
    [Serializable]
    public struct EdgeClip
    {
        public string Role;
        public string Name;
        public int FirstRow;
        public int Frames;
        public float Length;
        public bool Loop;
        /// <summary>From the recipe; negative where absent.</summary>
        public float Impact, End, StrideM;
        /// <summary>This clip's own bounding sphere at scale 1.</summary>
        public float CenterY, Radius, HeightM;

        /// <summary>Frames a clip of this length bakes to at the clip rate: both ends included.</summary>
        public static int FrameCount(float length) => Math.Max(2, (int)Math.Ceiling(length * EdgeCutLibrary.ClipRate - 1e-3f) + 1);

        /// <summary>The sample time of frame f (the last frame is the clip's end).</summary>
        public float FrameTime(int f) => Math.Min(Length, f / EdgeCutLibrary.ClipRate);
    }

    /// <summary>
    /// One shard piece on the rest pose at scale 1: the pivot it turns about, the entries and weights that pose
    /// the pivot, and its largest distance from it. 48 bytes, matching EdgeShards.hlsl's PieceRest.
    /// </summary>
    [Serializable]
    public struct EdgeShardPiece
    {
        public Vector3 Pivot;
        public float Size;
        public Vector4 Bones, Weights;

        public const int Stride = 48;
    }
}
