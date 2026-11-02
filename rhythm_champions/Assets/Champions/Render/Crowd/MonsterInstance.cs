using System.Runtime.InteropServices;
using UnityEngine;

namespace Champions.Render.Crowd
{
    /// <summary>
    /// One monster's state for a frame, as the crowd's presenter writes it and the GPU passes read it: 64 bytes,
    /// matching CrowdSkinning.hlsl's struct (game/'s, ported). Clip indices are into the type's clip table
    /// (<see cref="EdgeCutType.Clips"/>); times are seconds into each clip.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct MonsterInstance
    {
        public Vector3 Position;   // world metres; y includes a flyer's hover
        public float Yaw;          // radians
        public uint ClipA;         // the current clip
        public float TimeA;
        public uint ClipB;         // the clip it is leaving (crossfade), or ClipA
        public float TimeB;
        public float Blend;        // weight of ClipA, 0..1
        public float Scale;        // 1 is the model's height
        public float HitFlash;     // 0..1
        public float Flare;        // its own line flare on top of the beat wave's (0 none, 1 doubled)
        public uint Tint;          // its line colour, sRGB RGBA8 (r in the low byte)
        public uint Flags;         // CrowdFlags
        public Vector2 Pad;

        public const int Stride = 64;

        /// <summary>A colour packed for <see cref="Tint"/>.</summary>
        public static uint Pack(Color32 c) => c.r | (uint)c.g << 8 | (uint)c.b << 16 | (uint)c.a << 24;
    }

    [System.Flags]
    public enum CrowdFlags : uint
    {
        None = 0,
        Alive = 1,
        /// <summary>Its death's frame: not drawn, but its pose blended for the shards to start from.</summary>
        Dying = 2,
        Hidden = 4,
    }
}
