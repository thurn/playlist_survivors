using System.Runtime.InteropServices;
using Unity.Collections;
using UnityEngine;

namespace Demo3D.Runtime.Crowd
{
    /// <summary>
    /// One monster's per-frame state, as the timeline evaluation writes it and every crowd path
    /// reads it (crowd.md, Bone-texture path): 64 bytes, matching the shader's struct.
    /// Clip indices are into the type's clip table (Roster.Type.clips in roster order); times
    /// are seconds into each clip.
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
        public float Scale;        // per-instance jitter: 1 is the type's demo height (roster heightM)
        public float HitFlash;     // 0..1
        public float Dissolve;     // 0..1
        public uint Tint;          // RGBA8 from the type's colour set
        public uint Flags;         // CrowdFlags
        public Vector2 Pad;

        public const int Stride = 64;
    }

    [System.Flags]
    public enum CrowdFlags : uint
    {
        None = 0,
        Alive = 1,
        Dying = 2,
        Hidden = 4,
    }

    /// <summary>
    /// A crowd renderer: the stock baseline (TD4) and the bone-texture path (TD5) both implement
    /// it, so the harness and the client swap them freely. Each frame the client fills every
    /// type's instances, then calls Draw once.
    /// </summary>
    public interface ICrowdRenderer : System.IDisposable
    {
        /// <summary>Roster type ids in draw order; type indices below index this list.</summary>
        void Initialize(string[] typeIds, int capacityPerType);

        /// <summary>The writable instance array of a type for this frame, `count` long.</summary>
        NativeArray<MonsterInstance> Instances(int type, int count);

        /// <summary>Issues this frame's draws for the camera (culling included).</summary>
        void Draw(Camera camera);

        /// <summary>Counters for the harness: visible instances, skinned vertices, draws.</summary>
        CrowdCounters Counters { get; }
    }

    public struct CrowdCounters
    {
        public int Instances;
        public int Visible;
        public long SkinnedVertices;
        public int Draws;
        public int ShadowCasters;
    }
}
