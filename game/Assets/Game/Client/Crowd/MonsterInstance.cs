using System.Runtime.InteropServices;
using UnityEngine;

namespace Game.Client.Crowd
{
    /// <summary>
    /// One monster's state for a frame, as the crowd's presenter writes it and the GPU passes read
    /// it (unity-game.md, The Crowd): 64 bytes, matching CrowdSkinning.hlsl's struct. demo3d's
    /// MonsterInstance, copied and owned. Clip indices are into the type's clip table
    /// (<see cref="CrowdType.Clips"/>); times are seconds into each clip.
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
        public float Scale;        // the role's cast scale: 1 is the model's roster height
        public float HitFlash;     // 0..1
        public float Dissolve;     // unused by the crystal (deaths shatter), kept 0
        public uint Tint;          // the role's gem colour, sRGB RGBA8 (r in the low byte)
        public uint Flags;         // CrowdFlags
        public Vector2 Pad;

        public const int Stride = 64;

        /// <summary>A gem colour packed for <see cref="Tint"/>.</summary>
        public static uint Pack(Color32 c) => c.r | (uint)c.g << 8 | (uint)c.b << 16 | (uint)c.a << 24;
    }

    [System.Flags]
    public enum CrowdFlags : uint
    {
        None = 0,
        Alive = 1,
        Dying = 2,
        Hidden = 4,
    }
}
