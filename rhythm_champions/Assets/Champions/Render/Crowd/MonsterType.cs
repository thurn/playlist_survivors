using System;
using UnityEngine;

namespace Champions.Render.Crowd
{
    /// <summary>
    /// One monster type as `champions monster bake` writes it for one look (plan.md, Rendering &gt; The edge look), an
    /// asset in the ignored Assets/Baked/Monsters/Resources/MonsterTypes/LOOK/ beside its meshes and bone texture in
    /// Assets/Baked/Monsters/TYPE/ (MonsterBaker). The trailer's is always the vendor model; the default's is the
    /// monster's recipe's mesh when it has one, else the trailer's own meshes. Ported by review from the frozen look
    /// package's CrowdType.
    /// </summary>
    public sealed class MonsterType : ScriptableObject
    {
        public string Id;
        /// <summary>What the mesh was baked from: "vendor", or "recipe" (tools/recipes/monster/ID.py).</summary>
        public string Source;
        /// <summary>
        /// LOD0 to LOD2 as style meshes (StyleMesh.Unweld): every triangle its own vertices, one submesh, the rest pose in
        /// root space at the type's height, bone indices in UV1 and weights in UV2, barycentrics plus a value per triangle
        /// in UV3 and its feature edges in UV5. LOD1 has about half LOD0's triangles, LOD2 about a quarter.
        /// </summary>
        public Mesh[] Lods = Array.Empty<Mesh>();
        /// <summary>Skinning matrices at <see cref="MonsterLibrary.ClipRate"/>: bone b's row r of frame f is texel (3b + r, f).</summary>
        public Texture2D Bones;
        public int BoneCount;
        public MonsterClip[] Clips = Array.Empty<MonsterClip>();
        /// <summary>Yaw-invariant bounds at instance scale 1: a sphere around (0, CenterY, 0), and the standing height.</summary>
        public float CenterY, Radius, HeightM;
        /// <summary>A flyer's height over the ground at scale 1 (the bat), added to its Position.y by its presenter.</summary>
        public float HoverM;
        /// <summary>
        /// LOD0's rest pose cut into 20 to 40 closed chunks (MonsterShardBaker), joined: the style mesh's own triangles
        /// (normals and UV3 kept, bones in UV1, weights in UV2) closed by inside faces to an apex in the body, UV4 (inside
        /// face 0 or 1, the chunk's index into <see cref="Shards"/>), UV5 the feature and crack edges.
        /// </summary>
        public Mesh ShardMesh;
        public MonsterShard[] Shards = Array.Empty<MonsterShard>();

        /// <summary>The clip with this role ("idle", "walk", "attack", "hit", a boss's own), or -1.</summary>
        public int Clip(string role)
        {
            for (int i = 0; i < Clips.Length; i++)
                if (Clips[i].Role == role) return i;
            return -1;
        }
    }

    /// <summary>One clip in a type's bone texture.</summary>
    [Serializable]
    public struct MonsterClip
    {
        public string Role;
        public string Name;
        public int FirstRow;
        public int Frames;
        public float Length;
        public bool Loop;
        /// <summary>From the type's annotations (Editor/Monsters/monsters.json); negative where absent.</summary>
        public float Impact, End, StrideM;
        /// <summary>How far the clip is raised off the ground (a flyer's flight), baked into its bones.</summary>
        public float LiftM;
        /// <summary>This clip's own bounding sphere at instance scale 1 (see MonsterType).</summary>
        public float CenterY, Radius, HeightM;

        /// <summary>Frames a clip of this length bakes to at the clip rate: both ends included.</summary>
        public static int FrameCount(float length) => Math.Max(2, (int)Math.Ceiling(length * MonsterLibrary.ClipRate - 1e-3f) + 1);

        /// <summary>The sample time of frame f (the last frame is the clip's end).</summary>
        public float FrameTime(int f) => Math.Min(Length, f / MonsterLibrary.ClipRate);
    }

    /// <summary>
    /// One chunk of <see cref="MonsterType.ShardMesh"/>, on the rest pose at instance scale 1: its pivot (the centroid it
    /// turns about) with the bones and weights that pose it, its largest distance from the pivot, and 1 if it is of
    /// something the monster carries (a weapon, a shield), which the shatter flings with the body. 64 bytes, matching
    /// MonsterShards.hlsl's ShardRest.
    /// </summary>
    [Serializable]
    public struct MonsterShard
    {
        public Vector3 Pivot;
        public float Size;
        public Vector4 Bones, Weights;
        public float Carried, Pad0, Pad1, Pad2;

        public const int Stride = 64;
    }
}
