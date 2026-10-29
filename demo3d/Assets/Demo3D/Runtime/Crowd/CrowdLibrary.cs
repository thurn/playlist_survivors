using System;
using UnityEngine;

namespace Demo3D.Runtime.Crowd
{
    /// <summary>
    /// The bone-texture baker's output (crowd.md, Bone-texture path), one asset in the gitignored
    /// Assets/Baked/Crowd/Resources/ so a player loads it with <see cref="Load"/>. It holds every
    /// drawn roster type in roster order, plus the shaders the crowd needs, so a build includes
    /// them.
    /// </summary>
    public sealed class CrowdLibrary : ScriptableObject
    {
        public const string ResourcePath = "CrowdLibrary";
        /// <summary>The default clip rate; 30 Hz is a cut (TD11, <c>clips30</c>), baked beside it.</summary>
        public const float ClipRate = 60f;
        public const float CutClipRate = 30f;

        public CrowdType[] Types = Array.Empty<CrowdType>();
        public Shader CrowdShader;
        public ComputeShader CullShader;
        public ComputeShader PaletteShader;
        public Material Ground;
        public Shader BufferDebug;

        public static CrowdLibrary Load() =>
            Resources.Load<CrowdLibrary>(ResourcePath)
            ?? throw new InvalidOperationException(
                "CrowdLibrary: Assets/Baked/Crowd/Resources/CrowdLibrary.asset is missing (run `demo3d unity BakeCrowd`)");

        public int IndexOf(string id)
        {
            for (int i = 0; i < Types.Length; i++)
                if (Types[i].Id == id) return i;
            return -1;
        }
    }

    /// <summary>One baked roster type.</summary>
    [Serializable]
    public sealed class CrowdType
    {
        public string Id;
        public string Kind;
        /// <summary>The combined mesh at demo scale: bone indices in UV1, weights in UV2 (float4).</summary>
        public Mesh Mesh;
        /// <summary>
        /// TD11's decimated LODs of <see cref="Mesh"/> (Unity's MeshLodUtility, the same vertices and
        /// streams): LOD1 about half its triangles, LOD2 about a quarter. Null if not baked.
        /// </summary>
        public Mesh Lod1, Lod2;
        public int Lod1Triangles, Lod2Triangles;
        /// <summary>One material per submesh (one per albedo texture).</summary>
        public Material[] Materials;
        /// <summary>Skinning matrices: bone b's row r of frame f is texel (3b + r, f).</summary>
        public Texture2D Bones;
        /// <summary>The same clips baked at 30 Hz (TD11's clips30 cut): rows from each clip's FirstRow30.</summary>
        public Texture2D Bones30;
        public int BoneCount;
        public CrowdClip[] Clips;
        /// <summary>Yaw-invariant bounds at instance scale 1: a sphere around (0, CenterY, 0).</summary>
        public float CenterY, Radius, HeightM;
        public float RadiusM; // separation radius from the roster
        public float HoverM;  // the client adds it to Position.y (bats)
        public int Vertices, Triangles;
        /// <summary>The stock baseline's prefab: one SkinnedMeshRenderer and an Animator.</summary>
        public GameObject StockPrefab;

        public int Clip(string role)
        {
            for (int i = 0; i < Clips.Length; i++)
                if (Clips[i].Role == role) return i;
            return -1;
        }
    }

    /// <summary>One clip in a type's bone texture, in roster order.</summary>
    [Serializable]
    public struct CrowdClip
    {
        public string Role;
        public string Name;
        public int FirstRow;
        public int Frames;
        /// <summary>The clip's rows in the 30 Hz texture (<see cref="CrowdType.Bones30"/>).</summary>
        public int FirstRow30;
        public int Frames30;
        public float Length;
        public bool Loop;
        /// <summary>From the clip annotations (TD2); negative where absent.</summary>
        public float Impact, End, StrideM;
        /// <summary>Horizontal root travel removed from a looping clip, in metres.</summary>
        public float DriftRemovedM;
        /// <summary>This clip's own bounding sphere at instance scale 1 (see CrowdType).</summary>
        public float CenterY, Radius, HeightM;

        /// <summary>Frames a clip of this length bakes to at 60 Hz (or <paramref name="rate"/>): both ends included.</summary>
        public static int FrameCount(float length, float rate = CrowdLibrary.ClipRate) => Math.Max(2, (int)Math.Ceiling(length * rate - 1e-3f) + 1);

        /// <summary>The sample time of frame f at 60 Hz (or <paramref name="rate"/>; the last frame is the clip's end).</summary>
        public float FrameTime(int f, float rate = CrowdLibrary.ClipRate) => Math.Min(Length, f / rate);
    }
}
