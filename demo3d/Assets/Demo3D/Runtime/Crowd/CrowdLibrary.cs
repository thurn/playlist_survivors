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
        public const float ClipRate = 60f;

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
        /// <summary>One material per submesh (one per albedo texture).</summary>
        public Material[] Materials;
        /// <summary>Skinning matrices: bone b's row r of frame f is texel (3b + r, f).</summary>
        public Texture2D Bones;
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
        public float Length;
        public bool Loop;
        /// <summary>From the clip annotations (TD2); negative where absent.</summary>
        public float Impact, End, StrideM;
        /// <summary>Horizontal root travel removed from a looping clip, in metres.</summary>
        public float DriftRemovedM;
        /// <summary>This clip's own bounding sphere at instance scale 1 (see CrowdType).</summary>
        public float CenterY, Radius, HeightM;

        /// <summary>Frames a clip of this length bakes to at 60 Hz: both ends included.</summary>
        public static int FrameCount(float length) => Math.Max(2, (int)Math.Ceiling(length * ClipRateF - 1e-3f) + 1);

        const float ClipRateF = CrowdLibrary.ClipRate;

        /// <summary>The sample time of frame f (the last frame is the clip's end).</summary>
        public float FrameTime(int f) => Math.Min(Length, f / CrowdLibrary.ClipRate);
    }
}
