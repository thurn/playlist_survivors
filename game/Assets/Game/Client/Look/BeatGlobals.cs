using System;
using UnityEngine;

namespace Game.Client.Look
{
    /// <summary>
    /// The shaders' beat globals (demo3d's StyleBeat, after stylelab's Beat.cs), shared by the crowd
    /// and the structures (Client/Arena/Shaders/StructureCommon.hlsl declares them), from the fast
    /// lane instead of a song map: song time, the beat's index and phase, a sharp pulse on each beat
    /// (the facets' sparkle), a slower one on each bar's downbeat, the energy, the hero's feet and the
    /// beat wave's speed. Beside them, the lane's envelopes for shaders that want the drums:
    /// <c>_SL_Drums</c> (kick, snare, hat, bass) and <c>_SL_Rise</c> (a drop's build-up, the kick's
    /// anticipation). A function of the lane's frame, so fixed-time frames reproduce.
    /// </summary>
    public static class BeatGlobals
    {
        static readonly int TimeId = Shader.PropertyToID("_SL_Time");
        static readonly int BeatId = Shader.PropertyToID("_SL_Beat");
        static readonly int PulseId = Shader.PropertyToID("_SL_Pulse");
        static readonly int BarPulseId = Shader.PropertyToID("_SL_BarPulse");
        static readonly int EnergyId = Shader.PropertyToID("_SL_Energy");
        static readonly int HeroPosId = Shader.PropertyToID("_SL_HeroPos");
        static readonly int WaveSpeedId = Shader.PropertyToID("_SL_WaveSpeed");
        static readonly int RingGainId = Shader.PropertyToID("_SL_RingGain");
        static readonly int DrumsId = Shader.PropertyToID("_SL_Drums");
        static readonly int RiseId = Shader.PropertyToID("_SL_Rise");

        /// <summary>The beat wave's speed, metres per beat (the lab's).</summary>
        public const float WaveSpeed = 11f;

        /// <summary>The globals at one moment: (beat, pulse, bar pulse, energy), as StyleBeat.At.</summary>
        public static Vector4 At(in LaneFrame lane)
        {
            float beat = (float)Math.Max(0, lane.Beat);
            float pulse = (float)Math.Exp(-lane.SinceBeat * 7);
            float barPulse = (float)Math.Exp(-lane.SinceDownbeat * 4);
            return new Vector4(beat, pulse, barPulse, (float)lane.Energy);
        }

        public static void Publish(double song, in LaneFrame lane, Vector3 heroFeet)
        {
            var g = At(lane);
            Shader.SetGlobalFloat(TimeId, (float)song);
            Shader.SetGlobalFloat(BeatId, g.x);
            Shader.SetGlobalFloat(PulseId, g.y);
            Shader.SetGlobalFloat(BarPulseId, g.z);
            Shader.SetGlobalFloat(EnergyId, g.w);
            Shader.SetGlobalVector(HeroPosId, heroFeet);
            Shader.SetGlobalFloat(WaveSpeedId, WaveSpeed);
            Shader.SetGlobalFloat(RingGainId, 0f);
            Shader.SetGlobalVector(DrumsId, new Vector4((float)lane.Kick, (float)lane.Snare, (float)lane.Hat, (float)lane.Bass));
            Shader.SetGlobalFloat(RiseId, (float)Math.Max(lane.Rise, lane.KickPre));
        }
    }
}
