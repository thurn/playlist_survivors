using System;
using System.Collections.Generic;
using PS.Choreography;
using UnityEngine;

namespace Demo3D.Runtime.Playback
{
    /// <summary>
    /// The client's punch and shake on top of the framing track (camera-and-sound.md, Camera):
    /// - punch: a 2% zoom-in over a sixteenth note on each accent, released over a beat;
    /// - shake: per beat, amplitude = min(0.25, 0.08 × strongest + 0.02 × log2(1 + hits)) metres,
    ///   so a hundred hits on one beat never shake a hundred times harder, decaying over the beat.
    /// With an intensity's <see cref="BlowFeel"/>, each of the hero's blows punches too, by its weight,
    /// and the beat's heaviest blow adds to the shake, under the feel's raised cap. A structure break (BreakMoments)
    /// punches <see cref="BreakPunch"/> and shakes its beat by its own amplitude, past the cap: a wall coming down.
    /// Both are functions of song time, so seeks and fixed-time captures reproduce them.
    /// </summary>
    public sealed class CameraFeel
    {
        public const float PunchAmount = 0.02f, ShakeCap = 0.25f, BreakPunch = 0.035f;
        readonly SongMap _map;
        readonly double[] _punchAt;    // punches: accents and weighted blows, sorted
        readonly float[] _depth;       // per punch: its zoom-in
        readonly float[] _shake;       // per beat index: amplitude in metres

        /// <param name="breaks">Structure breaks' shakes (BreakMoments.Shakes); none without `--structure-breaks`.</param>
        public CameraFeel(Timeline tl, BlowFeel blows = null, IEnumerable<(double time, float amplitude)> breaks = null)
        {
            _map = tl.Map;
            var punches = new List<(double t, float depth)>();
            foreach (var a in _map.Accents) punches.Add((a.Time, PunchAmount));
            if (blows != null)
                for (int i = 0; i < blows.Count; i++)
                    if (blows.WeightOf(i) > 0) punches.Add((blows.Time(i), PunchAmount + blows.Feel.Punch * blows.WeightOf(i)));
            if (breaks != null) foreach (var b in breaks) punches.Add((b.time, BreakPunch));
            punches.Sort((x, y) => x.t.CompareTo(y.t));
            _punchAt = punches.ConvertAll(p => p.t).ToArray();
            _depth = punches.ConvertAll(p => p.depth).ToArray();
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
            var heaviest = new float[beats];
            if (blows != null)
                for (int i = 0; i < blows.Count; i++)
                {
                    int b = BeatOf(blows.Time(i));
                    if (b >= 0 && b < beats) heaviest[b] = Math.Max(heaviest[b], blows.WeightOf(i));
                }
            float cap = blows?.Feel.ShakeCap ?? ShakeCap, extra = blows?.Feel.Shake ?? 0f;
            _shake = new float[beats];
            for (int b = 0; b < beats; b++)
                _shake[b] = Mathf.Min(cap, 0.08f * strongest[b] + 0.02f * Mathf.Log(1 + hits[b], 2) + extra * heaviest[b]);
            if (breaks != null)
                foreach (var (time, amplitude) in breaks)
                {
                    int b = BeatOf(time);
                    if (b >= 0 && b < beats) _shake[b] = Mathf.Max(_shake[b], amplitude);
                }
        }

        int BeatOf(double t) => (int)Math.Floor(_map.BeatIndexAt(t) + 1e-6);

        /// <summary>The view width's multiplier at t (below 1 is a zoom-in).</summary>
        public float Punch(double t)
        {
            int i = Array.BinarySearch(_punchAt, t);
            if (i < 0) i = ~i - 1;
            float factor = 1f;
            // Accents closer than a punch's length overlap: take the deepest, so a new accent
            // never snaps the zoom back out (the demo systems' review).
            for (; i >= 0 && t - _punchAt[i] < 2.0; i--)
            {
                double u = t - _punchAt[i];
                double beat = BeatLength(_punchAt[i]);
                double sixteenth = beat / 4;
                float f = 1f;
                if (u < sixteenth) f = 1f - _depth[i] * (float)(u / sixteenth);
                else if (u < sixteenth + beat) f = 1f - _depth[i] * (float)(1 - (u - sixteenth) / beat);
                factor = Math.Min(factor, f);
            }
            return factor;
        }

        /// <summary>The camera's offset in world metres at t, on the ground plane and up.</summary>
        public Vector3 Shake(double t)
        {
            double x = _map.BeatIndexAt(t);
            int b = (int)Math.Floor(x + 1e-6);
            if (b < 0 || b >= _shake.Length || _shake[b] <= 0) return Vector3.zero;
            float u = (float)(x - b);
            float amp = _shake[b] * (1 - u) * (1 - u);
            // Deterministic jitter: a few incommensurate sines, phased by the beat.
            float s = (float)t * 6.2831853f;
            float ox = Mathf.Sin(s * 31f + b * 1.7f) * 0.6f + Mathf.Sin(s * 47f + b * 0.3f) * 0.4f;
            float oz = Mathf.Sin(s * 37f + b * 2.9f) * 0.6f + Mathf.Sin(s * 53f + b * 1.1f) * 0.4f;
            return Vector3.ClampMagnitude(new Vector3(ox, 0.3f * ox * oz, oz) * amp, amp);
        }

        double BeatLength(double t)
        {
            double i = Math.Floor(_map.BeatIndexAt(t));
            return _map.TimeOfBeatIndex(i + 1) - _map.TimeOfBeatIndex(i);
        }
    }
}
