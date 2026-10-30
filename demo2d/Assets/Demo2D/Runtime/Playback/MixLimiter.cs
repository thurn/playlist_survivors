using System;
using UnityEngine;

namespace Demo2D.Runtime.Playback
{
    /// <summary>
    /// The live mix's levels and limiter, as demo3d's offline mix (`demo3d mix`) sets them: the
    /// music at <see cref="MusicLevel"/>, the effect sounds at <see cref="EffectsLevel"/> times their
    /// event gain, and a soft limiter on the final mix (on the AudioListener's object), so a dense
    /// beat (up to 9 sounds at once, 3 hits at full gain) never clips. Without it the song's mix
    /// went past full scale in 187 of its 198 seconds, peaking at 3.25 (PX10's review).
    /// </summary>
    public sealed class MixLimiter : MonoBehaviour
    {
        public const float MusicLevel = 0.8f, EffectsLevel = 0.45f, Knee = 0.8f, Ceiling = 0.95f;

        /// <summary>Unity below the knee, a smooth tanh knee above it, never past the ceiling.</summary>
        public static float Limit(float x)
        {
            float a = Math.Abs(x);
            if (a <= Knee) return x;
            return Math.Sign(x) * (Knee + (Ceiling - Knee) * (float)Math.Tanh((a - Knee) / (Ceiling - Knee)));
        }

        void OnAudioFilterRead(float[] data, int channels)
        {
            for (int i = 0; i < data.Length; i++) data[i] = Limit(data[i]);
        }
    }
}
