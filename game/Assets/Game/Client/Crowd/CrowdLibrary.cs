using System;
using UnityEngine;

namespace Game.Client.Crowd
{
    /// <summary>
    /// The crowd baker's output (unity-game.md, The Crowd), one asset in the gitignored
    /// Assets/Baked/Crowd/Resources/ so a player loads it with <see cref="Load"/>: the nine monster
    /// models in roster order, the cast (each role's model, scale and gem colour, from
    /// game/Content/cast.json at bake time) and the shaders the crowd needs, so a build includes
    /// them. demo3d's CrowdLibrary, copied and owned for the crystal style alone.
    /// </summary>
    public sealed class CrowdLibrary : ScriptableObject
    {
        public const string ResourcePath = "GameCrowd";
        /// <summary>The bone textures' rate: demo3d's default (TD11's clips30 cut).</summary>
        public const float ClipRate = 30f;

        public CrowdType[] Types = Array.Empty<CrowdType>();
        /// <summary>Cast[i] is the sim's role i's look (GameContent.Cast's order).</summary>
        public CastLook[] Cast = Array.Empty<CastLook>();
        public Shader Crystal;
        public ComputeShader CullShader;
        public ComputeShader PaletteShader;

        /// <summary>The baked library, or null when the bake is missing (a checkout without the vendor packs).</summary>
        public static CrowdLibrary Load() => Resources.Load<CrowdLibrary>(ResourcePath);

        public int IndexOf(string id)
        {
            for (int i = 0; i < Types.Length; i++)
                if (Types[i].Id == id) return i;
            return -1;
        }
    }

    /// <summary>One baked monster model.</summary>
    [Serializable]
    public sealed class CrowdType
    {
        public string Id;
        /// <summary>
        /// LOD0 to LOD2 as style meshes: unwelded (every triangle its own vertices), one submesh,
        /// the rest pose in root space at roster height, bone indices in UV1 and weights in UV2,
        /// the face normal in the tangent and barycentrics plus a value per triangle in UV3.
        /// LOD1 has about half LOD0's triangles, LOD2 about a quarter.
        /// </summary>
        public Mesh[] Lods = Array.Empty<Mesh>();
        /// <summary>Skinning matrices at <see cref="CrowdLibrary.ClipRate"/>: bone b's row r of frame f is texel (3b + r, f).</summary>
        public Texture2D Bones;
        public int BoneCount;
        public CrowdClip[] Clips = Array.Empty<CrowdClip>();
        /// <summary>Yaw-invariant bounds at instance scale 1: a sphere around (0, CenterY, 0).</summary>
        public float CenterY, Radius, HeightM;
        /// <summary>A flyer's height over the ground at scale 1 (the bat), added to Position.y.</summary>
        public float HoverM;

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
        /// <summary>This clip's own bounding sphere at instance scale 1 (see CrowdType).</summary>
        public float CenterY, Radius, HeightM;

        /// <summary>Frames a clip of this length bakes to at the clip rate: both ends included.</summary>
        public static int FrameCount(float length) => Math.Max(2, (int)Math.Ceiling(length * CrowdLibrary.ClipRate - 1e-3f) + 1);

        /// <summary>The sample time of frame f (the last frame is the clip's end).</summary>
        public float FrameTime(int f) => Math.Min(Length, f / CrowdLibrary.ClipRate);
    }

    /// <summary>A role's look (cast.json): its model, scale and gem colour.</summary>
    [Serializable]
    public struct CastLook
    {
        public string Role;
        /// <summary>Into <see cref="CrowdLibrary.Types"/>.</summary>
        public int Type;
        public float Scale;
        /// <summary>sRGB.</summary>
        public Color32 Gem;
    }
}
