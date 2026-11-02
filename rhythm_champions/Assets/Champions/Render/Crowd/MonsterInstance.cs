using System.Runtime.InteropServices;
using UnityEngine;

namespace Champions.Render.Crowd
{
    /// <summary>
    /// One monster's state for a frame, as its presenter writes it into <see cref="MonsterCrowd.Instances"/> and the GPU
    /// passes read it: 64 bytes, matching CrowdSkinning.hlsl's struct (the frozen look package's, with its unused
    /// dissolve now the monster's own flare). Clip indices are into the type's clips (<see cref="MonsterType.Clips"/>);
    /// times are seconds into each clip.
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
        public float Scale;        // 1 is the type's baked height
        public float HitFlash;     // 0..1
        public float Flare;        // 0..1: its lines and core lift as on the beat (a telegraph's warning)
        public uint Tint;          // the palette's colour for it, sRGB RGBA8 (r in the low byte)
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
        /// <summary>Its death's frame: not drawn, but its palette is blended for <see cref="MonsterShatter"/> to seed from.</summary>
        Dying = 2,
        Hidden = 4,
    }
}
