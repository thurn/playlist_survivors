using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace Champions.Render
{
    /// <summary>A Hovl mesh as the effects draw it: the vendor mesh and its frame (Slash.shader's _RingFrame, _RingInner).</summary>
    [Serializable]
    public struct EffectMesh
    {
        public string Name;
        public Mesh Mesh;
        /// <summary>The mesh into its unit frame: a ring round +y in the xz plane with outer radius 1, a crescent's middle
        /// along +x; a solid's length along +x, its tip at 1.</summary>
        public Matrix4x4 Frame;
        /// <summary>A ring's inner radius in that frame.</summary>
        public float Inner;
    }

    /// <summary>
    /// The weapon effects' assets, one asset in the ignored Assets/Baked/Effects/Resources/ so a player loads it with
    /// <see cref="Load"/> (and a build includes what it names): Hovl's slash meshes and noise texture, referenced from the
    /// synced vendor files and never copied, and our own shaders that draw them. The effects baker
    /// (Champions.EditorTools.EffectBaker) rewrites it before every player build.
    /// </summary>
    public sealed class EffectLibrary : ScriptableObject
    {
        public const string ResourcePath = "Effects";

        public EffectMesh[] Meshes = Array.Empty<EffectMesh>();
        public Texture2D Noise;
        public Shader Slash, Spark;
        public ComputeShader Sparks;

        /// <summary>The baked library, or null when there is none (a checkout without the vendor files).</summary>
        public static EffectLibrary Load() => Resources.Load<EffectLibrary>(ResourcePath);

        public int IndexOf(string name) => Array.FindIndex(Meshes, m => m.Name == name);
    }

    /// <summary>One drawn shape for its whole life, as Slash.shader reads it (Effects.hlsl's Shape, 96 bytes).</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct EffectShape
    {
        public Vector3 Position;
        public float Born;
        public Vector2 Facing;
        public float Radius, Arc;
        public float Life, HeadIn, Width, Brightness;
        public float Tilt, Height, Mode, Flags;
        public Vector3 Travel;
        public float Grow;
        public float Streak, Fade, TravelIn, Taper;

        public const int Stride = 96;
        public const float Arc0 = 0, Ribbon = 1, Solid = 2, Upright = 3; // Effects.hlsl's SHAPE_*
        public const float Mirrored = 1;

        public double End => Born + Life;
    }

    /// <summary>One burst the sparks' compute pass expands (Effects.hlsl's Burst, 80 bytes).</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct SparkBurst
    {
        public Vector3 Origin;
        public float Born;
        public Vector3 Direction;
        public float Spread;
        public float Count, First, Seed, Brightness;
        public Vector2 Speed, Life;
        public float Size, Drag, Gravity, Up;

        public const int Stride = 80;
    }
}
