using System;
using UnityEngine;

namespace PS.Look
{
    /// <summary>
    /// The shaders' beat globals (after stylelab's Beat.cs), shared by the crowd and the structures
    /// (CrystalCommon.hlsl declares them): song time, the beat's index and phase, a sharp pulse on each
    /// beat (the facets' sparkle), a slower one on each bar's downbeat, the energy, the hero's feet and
    /// the beat wave's speed. Beside them, the drums' envelopes for shaders that want them:
    /// <c>_SL_Drums</c> (kick, snare, hat, bass) and <c>_SL_Rise</c> (a drop's build-up, the kick's
    /// anticipation). Each player feeds them from its own clock: the game from its fast lane, demo3d
    /// from its song map (StyleBeat). A function of the values passed, so fixed-time frames reproduce.
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

        /// <summary>
        /// The globals at one moment, as (beat, pulse, bar pulse, energy): <paramref name="beat"/> the beat's
        /// index with its phase, and the seconds since the last beat and the last bar's downbeat (infinite
        /// before the first, so no bar pulse).
        /// </summary>
        public static Vector4 At(double beat, double sinceBeat, double sinceDownbeat, double energy)
        {
            float pulse = (float)Math.Exp(-sinceBeat * 7);
            float barPulse = (float)Math.Exp(-sinceDownbeat * 4);
            return new Vector4((float)Math.Max(0, beat), pulse, barPulse, (float)energy);
        }

        /// <summary>Sets the beat globals: song time, <paramref name="beat"/> as <see cref="At"/> gives it, and the hero's feet.</summary>
        public static void Publish(double song, Vector4 beat, Vector3 heroFeet)
        {
            Shader.SetGlobalFloat(TimeId, (float)song);
            Shader.SetGlobalFloat(BeatId, beat.x);
            Shader.SetGlobalFloat(PulseId, beat.y);
            Shader.SetGlobalFloat(BarPulseId, beat.z);
            Shader.SetGlobalFloat(EnergyId, beat.w);
            Shader.SetGlobalVector(HeroPosId, heroFeet);
            Shader.SetGlobalFloat(WaveSpeedId, WaveSpeed);
            Shader.SetGlobalFloat(RingGainId, 0f);
        }

        /// <summary>Sets the drums' envelopes: (kick, snare, hat, bass) and the rise.</summary>
        public static void PublishDrums(Vector4 drums, float rise)
        {
            Shader.SetGlobalVector(DrumsId, drums);
            Shader.SetGlobalFloat(RiseId, rise);
        }
    }
}
