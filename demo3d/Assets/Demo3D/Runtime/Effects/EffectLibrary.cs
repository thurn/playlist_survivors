using System;
using UnityEngine;

namespace Demo3D.Runtime.Effects
{
    /// <summary>
    /// The converter's output (effects.md, The converter): every converted effect's emitter
    /// definitions, packed for the GPU runtime, with their lookup rows, texture slices and meshes.
    /// One asset in the gitignored Assets/Baked/Effects/Resources/, since it is vendor-derived; the
    /// runtime's shaders are referenced here so a build includes them.
    /// </summary>
    public sealed class EffectLibrary : ScriptableObject
    {
        public const string ResourcePath = "EffectLibrary";

        public EffectEntry[] Effects = Array.Empty<EffectEntry>();
        /// <summary><see cref="EmitterSlots.Stride"/> float4s per emitter definition.</summary>
        public Vector4[] Emitters = Array.Empty<Vector4>();
        /// <summary>CPU-side emission per emitter (bursts and rates), in emitter order.</summary>
        public EmitterEmission[] Emission = Array.Empty<EmitterEmission>();
        /// <summary>Every emitter's curves and gradients: <see cref="EmitterSlots.LutRows"/> rows of 64 samples each.</summary>
        public Texture2D Lut;
        /// <summary>Every texture the materials use, resampled into one array (slice per texture).</summary>
        public Texture2DArray Textures;
        /// <summary>Particle meshes, expanded to triangle lists: per vertex (pos.xyz, u), (normal.xyz, v), color.</summary>
        public Vector4[] MeshVertices = Array.Empty<Vector4>();
        public MeshRange[] Meshes = Array.Empty<MeshRange>();
        public string[] TextureNames = Array.Empty<string>();
        /// <summary>The selection (Data/effects.json) it was converted from: the catalog mapping the client reads.</summary>
        [TextArea] public string Selection;

        public ComputeShader Simulate;
        public ComputeShader Sort;
        public Shader Draw;

        /// <summary>Stock prefabs by effect, for the A/B gallery only (a local build references vendor prefabs; nothing is committed).</summary>
        public GameObject[] StockPrefabs = Array.Empty<GameObject>();

        public int EmitterCount => Emission.Length;

        public static EffectLibrary Load() =>
            Resources.Load<EffectLibrary>(ResourcePath)
            ?? throw new InvalidOperationException(
                "EffectLibrary: Assets/Baked/Effects/Resources/EffectLibrary.asset is missing (run `demo3d unity BakeEffects`)");

        public static EffectLibrary TryLoad() => Resources.Load<EffectLibrary>(ResourcePath);

        public int IndexOf(string name)
        {
            for (int i = 0; i < Effects.Length; i++)
                if (Effects[i].Name == name) return i;
            return -1;
        }
    }

    /// <summary>One converted effect: a range of emitters, its light cue and its timing.</summary>
    [Serializable]
    public sealed class EffectEntry
    {
        public string Name;
        public string Source;        // manifest path of the stock prefab
        public int FirstEmitter, EmitterCount;
        /// <summary>The effect's own scale for an event's Scale of 1 (ring radii, laser lengths: Scale / ScaleRef).</summary>
        public float ScaleRef = 1f;
        /// <summary>Seconds after which every emitter has finished emitting and every particle is dead.</summary>
        public float Lifetime;
        /// <summary>Loops of a looping system per spawn, when the request gives no duration.</summary>
        public float Duration;
        public LightCue Light;
        /// <summary>A/B gallery placement: the height of the effect's origin and the view width that frames it.</summary>
        public float GalleryHeight = 1f, GalleryViewWidth = 8f;
        public string Notes;
    }

    /// <summary>
    /// An effect's light cue (effects.md, Effect lights): colour, range and an intensity curve over
    /// the cue's duration, 16 samples. Intensity 0 means no cue.
    /// </summary>
    [Serializable]
    public struct LightCue
    {
        public Color Color;
        public float Range, Intensity, Duration;
        public float[] Curve;

        public bool Active => Intensity > 0 && Range > 0 && Duration > 0;

        public float At(float t)
        {
            if (t < 0 || t >= Duration || Curve == null || Curve.Length == 0) return 0;
            float x = t / Duration * (Curve.Length - 1);
            int i = Mathf.Min((int)x, Curve.Length - 2);
            if (i < 0) return Curve[0] * Intensity;
            return Mathf.Lerp(Curve[i], Curve[i + 1], x - i) * Intensity;
        }
    }

    /// <summary>A particle mesh's range in <see cref="EffectLibrary.MeshVertices"/> (3 float4 per vertex).</summary>
    [Serializable]
    public struct MeshRange
    {
        public string Name;
        public int FirstVertex, VertexCount;
    }

    /// <summary>One burst of an emitter: at Time, Count particles each cycle, Cycles times, Interval apart.</summary>
    [Serializable]
    public struct Burst
    {
        public float Time, CountMin, CountMax, Interval, Probability;
        public int Cycles;
    }

    /// <summary>What the CPU needs to count an emitter's particles and their birth times.</summary>
    [Serializable]
    public struct EmitterEmission
    {
        public float Duration, DelayMin, DelayMax;
        public bool Loop;
        public float RateOverTime;          // per second over the duration
        public float RateOverDistanceMin, RateOverDistanceMax; // per metre of emitter travel
        public Burst[] Bursts;
        public int MaxParticles;
        public float LifeMax;
        /// <summary>The emitter whose seed and births this one shares (a trail drawn over its head's particles).</summary>
        public int SeedEmitter;
        /// <summary>A large soft glow quad (the selection's <c>glow</c> list): TD11's <c>drop-glow</c> cut leaves its halo to bloom.</summary>
        public bool Glow;
    }

    /// <summary>
    /// The emitter definition's layout on the GPU: <see cref="Stride"/> float4s, mirrored by
    /// EffectCommon.hlsl. Flags are stored as uint bits in TIME.w.
    /// </summary>
    public static class EmitterSlots
    {
        public const int Stride = 26, LutRows = 14, LutWidth = 64;
        public const int Time = 0, Life = 1, SizeMin = 2, SizeMax = 3, RotMin = 4, RotMax = 5, ColA = 6, ColB = 7,
            Shape0 = 8, Shape1 = 9, Shape2 = 10, ShapePos = 11, ShapeRot = 12, ChildPos = 13, ChildRot = 14, ChildScale = 15,
            Noise = 16, Render0 = 17, Render1 = 18, Tex = 19, Mat0 = 20, Mat1 = 21, Mat2 = 22, Mod = 23, ShapeScale = 24, Orbital = 25;

        // LUT rows, relative to the emitter's first row (ChildScale.w).
        public const int RowColor = 0, RowColor2 = 1, RowSize = 2, RowSize2 = 3, RowVel = 4, RowVel2 = 5,
            RowForce = 6, RowForce2 = 7, RowMisc = 8, RowCustom = 9, RowStartColor = 10, RowMisc2 = 11, RowRot3 = 12, RowOrbital = 13;

        [Flags]
        public enum Flags : uint
        {
            Loop = 1, LocalSpace = 2, Size3D = 4, Rot3D = 8, Velocity = 16, Force = 32, Limit = 64, Noise = 128,
            Color2 = 256, SizeAxes = 512, VelocityWorld = 1024, ForceWorld = 2048, ColorOverLife = 4096,
            SizeOverLife = 8192, RotOverLife = 16384, SheetAnim = 32768, SizeBySpeed = 65536, Custom = 131072,
            StartColorTwo = 262144, StartColorGradient = 524288, Vel2 = 1u << 20, Force2 = 1u << 21, Size2 = 1u << 22,
            ShapeScaleOnly = 1u << 23, SoftParticles = 1u << 24, NoiseTex = 1u << 25, RandomRotDir = 1u << 26,
            Beam = 1u << 27, SheetRandomRow = 1u << 28, Orbital = 1u << 29, RotAxes = 1u << 30,
        }

        public enum RenderMode { Billboard = 0, Stretch = 1, Horizontal = 2, Vertical = 3, Mesh = 4, Beam = 5 }
        public enum Alignment { View = 0, World = 1, Local = 2 }
        public enum Blend { Additive = 0, Alpha = 1, Premultiplied = 2 }
        public enum ShapeType { None = 0, Sphere = 1, Hemisphere = 2, Cone = 3, ConeVolume = 4, Circle = 5, Box = 6, Edge = 7 }
    }
}
