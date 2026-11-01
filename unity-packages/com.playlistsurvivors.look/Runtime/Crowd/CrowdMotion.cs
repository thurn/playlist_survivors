using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace PS.Look.Crowd
{
    /// <summary>
    /// One monster's motion clocks for a frame (neon-look.md, The Axes &gt; Motion), as its feeder writes them beside its
    /// <see cref="MonsterInstance"/> (<see cref="BoneTextureCrowd.Motion"/>): the latest step and the latest kick (a lunge,
    /// a hit or a knock), from which the crowd's shaders spring the body (CrowdCommon.hlsl's CrowdMotion), and the life's
    /// seed for its swarm tilt and wobble. 48 bytes, matching CrowdCommon.hlsl's struct.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct MonsterMotion
    {
        /// <summary>Seconds since the latest step's plant (its footplant, on the beat); negative while its move runs before it.</summary>
        public float StepAge;
        /// <summary>How long the step's move runs before its plant (the game's); 0 when the move follows the plant (the trailer's gait steps).</summary>
        public float StepMove;
        /// <summary>The step's travel on the ground (world x, z), in the monster's own heights.</summary>
        public Vector2 Travel;
        /// <summary>Seconds since the latest kick's contact; negative through a lunge's wind-up.</summary>
        public float KickAge;
        /// <summary>The wind-up before the contact (a lunge's notice to its contact); 0 for a hit or a knock.</summary>
        public float KickWind;
        /// <summary>The kick's way on the ground (world x, z) times its strength (0..1): toward the target for a lunge, away from the blow for a hit or knock.</summary>
        public Vector2 Kick;
        /// <summary>Unique to the life: its swarm tilt and wobble.</summary>
        public uint Seed;
        public float Pad0, Pad1, Pad2;

        public const int Stride = 48;

        /// <summary>An age past every spring's settling: no event.</summary>
        public const float Long = 1e4f;

        /// <summary>No step and no kick: only the swarm's tilt and wobble.</summary>
        public static MonsterMotion Still(uint seed) => new MonsterMotion { StepAge = Long, KickAge = Long, Seed = seed };
    }

    /// <summary>
    /// The crowd's motion layers (neon-look.md, The Axes &gt; Motion), on top of the baked clips:
    /// - **springs**: per instance on the GPU (CrowdCommon.hlsl's CrowdMotion, in SkinVertex, so every look, pass and
    ///   LOD sees it), from the feeder's <see cref="MonsterMotion"/>. A step stretches and leans the body into its travel
    ///   while it moves and snaps it into a squash on its plant, the lean overshooting back and settling; a lunge crouches
    ///   and leans back through its wind-up, then reaches into the contact and springs back; a hit or knock throws the
    ///   body back and squashes it on its own frame, then settles. Only the body's shape moves: its feet stay where the
    ///   feeder put them, so every step still plants on its beat's frame.
    /// - **swarm**: each life a little off the sim's spot, facing and size (<see cref="Place"/>, on the CPU so the
    ///   shatter seeds from where the body drew), with a slight standing tilt and a slow wobble of its own on the GPU.
    /// - **beatRipple** (`--beat-ripple`, off in every preset): each step delayed by its distance from the hero
    ///   (<see cref="RippleDelay"/>), applied by the feeder to the step's times.
    /// The hero's hops take the same step spring on the CPU (<see cref="StepSpring"/>).
    /// The amounts are restrained on purpose (the operator, 2026-11-01: subtle secondary motion by default, flourishes
    /// kept for special moments): a step reads as weight, not as rubber.
    /// </summary>
    public static class CrowdMotion
    {
        /// <summary>A step's lean into its travel at full size (the shear's tangent), its stretch through the move and its squash on the plant (shares of the height).</summary>
        public const float StepLean = 0.2f, StepStretch = 0.07f, StepSquash = 0.13f;
        /// <summary>A kick's lean and its squash on contact.</summary>
        public const float KickLean = 0.3f, KickStretch = 0.08f, KickSquash = 0.1f;
        /// <summary>A step is full size when it travels this share of the body's height or more.</summary>
        public const float StepFull = 0.3f;
        /// <summary>The springs' periods and decays (seconds): the plant's squash, the lean after a plant, and a kick's lean and squash.</summary>
        public const float SquashPeriod = 0.26f, SquashDecay = 0.08f, LeanPeriod = 0.36f, LeanDecay = 0.11f;
        public const float KickPeriod = 0.4f, KickDecay = 0.12f, KickSquashPeriod = 0.24f, KickSquashDecay = 0.07f;
        /// <summary>The swarm's furthest offset off the sim's spot (metres at cast scale 1), its turn off the facing (radians) and its size's spread (share).</summary>
        public const float JitterM = 0.2f, YawJitter = 0.12f, ScaleJitter = 0.1f;
        /// <summary>A kick's strength: a lunge's and a knock's, and a hit's.</summary>
        public const float LungeKick = 1f, KnockKick = 1f, HitKick = 0.7f;
        /// <summary>The ripple reaches its longest delay, a sixteenth note, this far from the hero (metres).</summary>
        public const double RippleReachM = 24;

        static readonly int SpringsId = Shader.PropertyToID("_LookSprings");
        static readonly int SwarmId = Shader.PropertyToID("_LookSwarm");

        /// <summary>The applied look's layers (<see cref="SetShaderGlobals"/>); all off before a look is applied.</summary>
        public static bool Springs { get; private set; }
        public static bool Swarm { get; private set; }
        public static bool Ripple { get; private set; }

        /// <summary>Takes <paramref name="look"/>'s motion layers: the flags above and the crowd shaders' globals.</summary>
        public static void SetShaderGlobals(ResolvedLook look)
        {
            Springs = look.MotionOn("springs");
            Swarm = look.MotionOn("swarm");
            Ripple = look.MotionOn("beatRipple");
            Shader.SetGlobalFloat(SpringsId, Springs ? 1 : 0);
            Shader.SetGlobalFloat(SwarmId, Swarm ? 1 : 0);
        }

        /// <summary>A stable value in [0, 1) per seed and salt (CrowdCommon.hlsl's MotionHash).</summary>
        public static float Hash(uint seed, uint salt)
        {
            uint h = seed * 0x9E3779B1u + salt * 0x85EBCA77u;
            h ^= h >> 15; h *= 0x2C1B3C6Du; h ^= h >> 12; h *= 0x297A2D39u; h ^= h >> 15;
            return (h & 0xFFFFFF) / 16777216f;
        }

        /// <summary>
        /// The swarm's placement of one life (with <see cref="Swarm"/> on; the feeder checks): up to <see cref="JitterM"/>
        /// off the sim's spot, turned up to <see cref="YawJitter"/> off its facing and sized within
        /// <see cref="ScaleJitter"/> of its cast scale, the same for its whole life. A dying body is placed alike, so the
        /// shatter seeds where the body drew.
        /// </summary>
        public static void Place(ref MonsterInstance m, uint seed)
        {
            float a = Hash(seed, 1) * 2 * Mathf.PI, r = JitterM * Mathf.Sqrt(Hash(seed, 2)) * m.Scale;
            m.Position.x += r * Mathf.Cos(a);
            m.Position.z += r * Mathf.Sin(a);
            m.Yaw += YawJitter * (2 * Hash(seed, 3) - 1);
            m.Scale *= 1 + ScaleJitter * (2 * Hash(seed, 4) - 1);
        }

        /// <summary>
        /// The ripple's delay (seconds) of a step planting <paramref name="distance"/> metres from the hero, the beat
        /// <paramref name="beat"/> seconds long: none at the hero, growing to a sixteenth note (a quarter of the beat) at
        /// <see cref="RippleReachM"/> and beyond.
        /// </summary>
        public static double RippleDelay(double distance, double beat) =>
            Math.Min(1, Math.Max(0, distance) / RippleReachM) * Math.Max(0, beat) / 4;

        /// <summary>A step's size: its travel beside <see cref="StepFull"/> of the body's height, 0..1.</summary>
        public static float StepSize(float travelHeights) => Mathf.Clamp01(travelHeights / StepFull);

        /// <summary>
        /// The step spring (CrowdCommon.hlsl's CrowdMotion step, on the CPU for the hero): at <paramref name="age"/>
        /// seconds from the plant, the move having run <paramref name="move"/> seconds before it (0: the move follows
        /// the plant), for a step of <paramref name="size"/> (<see cref="StepSize"/>): the stretch (a share of the height,
        /// negative squashed) and the lean into the travel (the shear's tangent, negative leaning back).
        /// </summary>
        public static void StepSpring(float age, float move, float size, out float stretch, out float lean)
        {
            stretch = lean = 0;
            if (age < 0)
            {
                if (move <= 0 || age < -move) return;
                float f = 1 + age / move;
                stretch = StepStretch * size * f;
                lean = StepLean * size * f * f * (3 - 2 * f);
                return;
            }
            if (age > 1.5f) return;
            stretch = -StepSquash * size * SpringCos(age, SquashPeriod, SquashDecay);
            lean = StepLean * size * (move > 0 ? SpringCos(age, LeanPeriod, LeanDecay) : 1.6f * SpringSin(age, 0.5f, LeanDecay));
        }

        /// <summary>A damped spring let go from 1 at age 0: e^(-age/decay) cos(2 pi age/period).</summary>
        public static float SpringCos(float age, float period, float decay) =>
            Mathf.Exp(-age / decay) * Mathf.Cos(2 * Mathf.PI * age / period);

        /// <summary>A damped spring kicked from rest at age 0: e^(-age/decay) sin(2 pi age/period).</summary>
        public static float SpringSin(float age, float period, float decay) =>
            Mathf.Exp(-age / decay) * Mathf.Sin(2 * Mathf.PI * age / period);
    }
}
