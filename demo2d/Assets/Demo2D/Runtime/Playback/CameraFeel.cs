using System;
using PS.Choreography;
using UnityEngine;

namespace Demo2D.Runtime.Playback
{
    /// <summary>
    /// The client's punch and shake on top of the framing track (camera-and-sound.md, Camera):
    /// - punch: a 2% zoom-in over a sixteenth note on each accent, released over a beat;
    /// - shake: per beat, amplitude = min(0.25, 0.08 × strongest + 0.02 × log2(1 + hits)) metres,
    ///   so a hundred hits on one beat never shake a hundred times harder, decaying over the beat.
    /// Both are functions of song time, so seeks and fixed-time captures reproduce them. demo3d's
    /// CameraFeel (in demo3d's own runtime assembly), with the shake on the 2D ground plane.
    /// </summary>
    public sealed class CameraFeel
    {
        public const float PunchAmount = 0.02f, ShakeCap = 0.25f;
        readonly SongMap _map;
        readonly double[] _accents;
        readonly float[] _shake;       // per beat index: amplitude in metres

        public CameraFeel(Timeline tl)
        {
            _map = tl.Map;
            _accents = new double[_map.Accents.Length];
            for (int i = 0; i < _accents.Length; i++) _accents[i] = _map.Accents[i].Time;
            Array.Sort(_accents);
            int beats = _map.Beats.Length + 1;
            var hits = new int[beats];
            var strongest = new float[beats];
            foreach (var a in tl.Actions)
            {
                if (a.Kind != ActionKind.Hit && a.Kind != ActionKind.Death && !(a.Kind == ActionKind.Attack && a.Entity == 0)) continue;
                int b = BeatOf(a.Impact);
                if (b < 0 || b >= beats) continue;
                if (a.Kind != ActionKind.Attack) hits[b]++;
                strongest[b] = Math.Max(strongest[b], a.Strength);
            }
            _shake = new float[beats];
            for (int b = 0; b < beats; b++)
                _shake[b] = Mathf.Min(ShakeCap, 0.08f * strongest[b] + 0.02f * Mathf.Log(1 + hits[b], 2));
        }

        int BeatOf(double t) => (int)Math.Floor(_map.BeatIndexAt(t) + 1e-6);

        /// <summary>The view width's multiplier at t (below 1 is a zoom-in).</summary>
        public float Punch(double t)
        {
            int i = Array.BinarySearch(_accents, t);
            if (i < 0) i = ~i - 1;
            float factor = 1f;
            // Accents closer than a punch's length overlap: take the deepest, so a new accent
            // never snaps the zoom back out (the demo systems' review).
            for (; i >= 0 && t - _accents[i] < 2.0; i--)
            {
                double u = t - _accents[i];
                double beat = BeatLength(_accents[i]);
                double sixteenth = beat / 4;
                float f = 1f;
                if (u < sixteenth) f = 1f - PunchAmount * (float)(u / sixteenth);
                else if (u < sixteenth + beat) f = 1f - PunchAmount * (float)(1 - (u - sixteenth) / beat);
                factor = Math.Min(factor, f);
            }
            return factor;
        }

        /// <summary>The camera's offset in world metres at t, on the ground plane.</summary>
        public Vector2 Shake(double t)
        {
            double x = _map.BeatIndexAt(t);
            int b = (int)Math.Floor(x + 1e-6);
            if (b < 0 || b >= _shake.Length || _shake[b] <= 0) return Vector2.zero;
            float u = (float)(x - b);
            float amp = _shake[b] * (1 - u) * (1 - u);
            // Deterministic jitter: a few incommensurate sines, phased by the beat.
            float s = (float)t * 6.2831853f;
            float ox = Mathf.Sin(s * 31f + b * 1.7f) * 0.6f + Mathf.Sin(s * 47f + b * 0.3f) * 0.4f;
            float oz = Mathf.Sin(s * 37f + b * 2.9f) * 0.6f + Mathf.Sin(s * 53f + b * 1.1f) * 0.4f;
            return Vector2.ClampMagnitude(new Vector2(ox, oz) * amp, amp);
        }

        double BeatLength(double t)
        {
            double i = Math.Floor(_map.BeatIndexAt(t));
            return _map.TimeOfBeatIndex(i + 1) - _map.TimeOfBeatIndex(i);
        }
    }
}
