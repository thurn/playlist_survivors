using System.Runtime.InteropServices;
using UnityEngine;

namespace Champions.Render.Crowd
{
    /// <summary>
    /// One monster's motion clocks for a frame, as its presenter writes them beside its <see cref="MonsterInstance"/>
    /// (<see cref="MonsterCrowd.Motion"/>): the latest step and the latest kick (a lunge, a hit or a knock), from which
    /// the crowd's shaders spring the body (CrowdCommon.hlsl's CrowdMotion), and the life's seed for its swarm tilt and
    /// wobble. The trailer's motion layers, ported by review from the frozen look package's CrowdMotion.cs: only the
    /// body's shape moves, so its feet stay where the presenter put them and every step still plants on its beat. 48
    /// bytes, matching CrowdCommon.hlsl's struct.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct MonsterMotion
    {
        /// <summary>Seconds since the latest step's plant; negative while its move runs before it.</summary>
        public float StepAge;
        /// <summary>How long the step's move runs before its plant; 0 when the move follows the plant (the horde's beat steps).</summary>
        public float StepMove;
        /// <summary>The step's travel on the ground (world x, z), in the monster's own heights.</summary>
        public Vector2 Travel;
        /// <summary>Seconds since the latest kick's contact; negative through a lunge's wind-up.</summary>
        public float KickAge;
        /// <summary>The wind-up before the contact (a lunge's telegraph to its contact); 0 for a hit or a knock.</summary>
        public float KickWind;
        /// <summary>The kick's way on the ground (world x, z) times its strength (0..1): toward the target for a lunge, away from the blow for a hit.</summary>
        public Vector2 Kick;
        /// <summary>Unique to the life: its swarm tilt and wobble.</summary>
        public uint Seed;
        public float Pad0, Pad1, Pad2;

        public const int Stride = 48;

        /// <summary>An age past every spring's settling: no event.</summary>
        public const float Long = 1e4f;

        /// <summary>A kick's strength: a lunge's and a knock's, and a hit's.</summary>
        public const float LungeKick = 1f, KnockKick = 1f, HitKick = 0.7f;

        /// <summary>No step and no kick: only the swarm's tilt and wobble.</summary>
        public static MonsterMotion Still(uint seed) => new MonsterMotion { StepAge = Long, KickAge = Long, Seed = seed };
    }
}
