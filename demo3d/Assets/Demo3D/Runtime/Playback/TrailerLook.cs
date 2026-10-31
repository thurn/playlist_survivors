using System;
using System.Collections.Generic;
using PS.Choreography;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Demo3D.Runtime.Playback
{
    /// <summary>
    /// The trailer's look on top of each environment's own (trailer.md, "Look"), besides the per-shot grade (the post
    /// feel's <see cref="PostFeel.ShotGrade"/>):
    /// - film grain and the lens dirt (<see cref="LensDirt"/>) on every environment's night volume (<see cref="Dress"/>);
    /// - the impact lens hits (<see cref="LensHitFeature"/>): every slam, ring blast and mass kill hits the lens at its
    ///   screen position, with a strength from the blow's weight (BlowFeel), held on the impact's frame and gone within
    ///   an eighth note. Overlapping hits add, to the same cap.
    /// Like the post feel it is a function of song time, so fixed-time stills reproduce. `--no-trailer-look` leaves it
    /// all out.
    /// </summary>
    public sealed class TrailerLook
    {
        /// <summary>One lens hit: its impact, the end of its shot (a cut clears it), where it lands and its peak strength (0 to 1).</summary>
        public struct Hit
        {
            public double Time, End;
            public Vector3 Position;
            public float Strength;
        }

        /// <summary>A hero's blow hits the lens as a mass kill from this weight (BlowFeel: about 22 kills under overdrive's feel).</summary>
        public const float MassKillWeight = 0.5f;
        /// <summary>A slam's or ring's strength at weight 0 (it rises to 1 at weight 1): every one hits the lens a little.</summary>
        public const float ImpactFloor = 0.3f;
        /// <summary>The film grain's intensity at 1× (each supersampling factor's averaging takes it down; <see cref="Dress"/> compensates) and response.</summary>
        public const float GrainIntensity = 0.18f, GrainResponse = 0.8f;
        /// <summary>The lens dirt's intensity on the bloom.</summary>
        public const float DirtIntensity = 12f;

        readonly Hit[] _hits; // sorted by time
        readonly SongMap _map;
        readonly float _supersample;

        public IReadOnlyList<Hit> Hits => _hits;

        public TrailerLook(SongMap map, IEnumerable<Hit> hits, float supersample)
        {
            _map = map;
            _supersample = supersample;
            var all = new List<Hit>(hits);
            all.Sort((a, b) => a.Time.CompareTo(b.Time));
            _hits = all.ToArray();
        }

        /// <summary>
        /// The lens hits of <paramref name="tl"/> from <paramref name="from"/> to before <paramref name="to"/>: its slam
        /// shocks and rings (spells and final slams included), and the hero's blows of at least <see cref="MassKillWeight"/>.
        /// Hits within an eighth note of an earlier one are one hit, at the stronger's place and strength, so an impact
        /// re-triggers at most once an eighth.
        /// </summary>
        public static List<Hit> HitsOf(Timeline tl, BlowFeel.Amounts feel, double from, double to)
        {
            feel ??= new BlowFeel.Amounts(); // a base overlay without a feel: the default crisp and heavy counts
            var kills = new Dictionary<double, int>();
            foreach (var a in tl.Actions)
                if (a.Kind == ActionKind.Death) kills[a.Impact] = kills.TryGetValue(a.Impact, out int n) ? n + 1 : 1;
            float WeightAt(double t) => BlowFeel.Weight(kills.TryGetValue(t, out int k) ? k : 0, feel);

            var raw = new List<Hit>();
            foreach (var e in tl.Effects)
            {
                if (e.Time < from || e.Time >= to || e.Effect < 0 || e.Effect >= tl.EffectNames.Length) continue;
                string name = tl.EffectNames[e.Effect];
                if (name != "slam-shock" && name != "ring") continue;
                var p = e.Position;
                if (e.AttachTo >= 0)
                {
                    var g = tl.PositionAt(e.AttachTo, e.Time);
                    p = new System.Numerics.Vector3(g.X + p.X, p.Y, g.Y + p.Z);
                }
                raw.Add(new Hit { Time = e.Time, End = to, Position = new Vector3(p.X, p.Y, p.Z), Strength = ImpactFloor + (1 - ImpactFloor) * WeightAt(e.Time) });
            }
            foreach (var a in tl.Actions)
            {
                if (a.Kind != ActionKind.Attack || a.Entity != 0 || a.Impact < from || a.Impact >= to) continue;
                float w = WeightAt(a.Impact);
                if (w < MassKillWeight) continue;
                var g = tl.PositionAt(0, a.Impact);
                raw.Add(new Hit { Time = a.Impact, End = to, Position = new Vector3(g.X, 0, g.Y), Strength = ImpactFloor + (1 - ImpactFloor) * w });
            }
            raw.Sort((x, y) => x.Time.CompareTo(y.Time));
            var hits = new List<Hit>();
            foreach (var h in raw)
            {
                int last = hits.Count - 1;
                if (last >= 0 && h.Time - hits[last].Time < Eighth(tl.Map, hits[last].Time))
                {
                    var kept = hits[last];
                    if (h.Strength > kept.Strength) { kept.Position = h.Position; kept.Strength = h.Strength; }
                    hits[last] = kept;
                }
                else hits.Add(h);
            }
            return hits;
        }

        /// <summary>
        /// The lens hits' strength at song time t, each its strength times <see cref="Envelope"/> over its eighth note (or
        /// 0 from its shot's end), summed and capped at 1; and their strength-weighted position.
        /// </summary>
        public float StrengthAt(double t, out Vector3 position)
        {
            float sum = 0;
            position = Vector3.zero;
            int i = Array.BinarySearch(_hits, new Hit { Time = t }, ByTime.Instance);
            if (i < 0) i = ~i - 1;
            else while (i + 1 < _hits.Length && _hits[i + 1].Time <= t) i++;
            for (; i >= 0 && t - _hits[i].Time < 1.0; i--)
            {
                var h = _hits[i];
                if (t >= h.End) continue;
                double eighth = Eighth(_map, h.Time);
                if (t - h.Time >= eighth) continue;
                float s = h.Strength * Envelope(t - h.Time, eighth);
                sum += s;
                position += s * h.Position;
            }
            if (sum <= 0) return 0f;
            position /= sum;
            return Mathf.Min(1f, sum);
        }

        /// <summary>A hit's full strength holds this long from its impact, so the frame drawn on the impact peaks however the frames fall.</summary>
        public const double HoldS = 1.0 / 60;

        /// <summary>
        /// A hit's shape <paramref name="dt"/> after its impact: 1 for <see cref="HoldS"/>, then a punch's fast fall,
        /// e^(−10x)·(1 − x) over the rest of the eighth (x from 0 to 1): a fifth left two frames on, nothing at the eighth.
        /// </summary>
        public static float Envelope(double dt, double eighth)
        {
            if (dt < 0 || dt >= eighth) return 0f;
            if (dt < HoldS) return 1f;
            double x = (dt - HoldS) / (eighth - HoldS);
            return (float)(Math.Exp(-10 * x) * (1 - x));
        }

        /// <summary>Sets the frame's lens hit for song time t, its centre where <paramref name="camera"/> sees it.</summary>
        public void Frame(double t, Camera camera)
        {
            float s = StrengthAt(t, out var p);
            var vp = s > 0 ? camera.WorldToViewportPoint(p) : Vector3.zero;
            if (vp.z <= 0) s = 0; // behind the camera: no centre
            LensHitFeature.Strength = s;
            LensHitFeature.Centre = new Vector2(Mathf.Clamp(vp.x, -0.5f, 1.5f), Mathf.Clamp(vp.y, -0.5f, 1.5f));
        }

        /// <summary>No lens hit (before the first frame, from the cut to black).</summary>
        public static void Off() => LensHitFeature.Strength = 0f;

        /// <summary>
        /// Film grain and the lens dirt on an environment's night volume. Grain is a pixel's size at the render
        /// resolution, so the downsample averages it away: its intensity grows with the supersampling factor.
        /// </summary>
        public void Dress(VolumeProfile profile)
        {
            if (!profile.TryGet(out FilmGrain grain)) grain = profile.Add<FilmGrain>(true);
            grain.type.Override(FilmGrainLookup.Thin2);
            grain.intensity.Override(Mathf.Min(1f, GrainIntensity * _supersample));
            grain.response.Override(GrainResponse);
            if (profile.TryGet(out Bloom bloom))
            {
                bloom.dirtTexture.Override(LensDirt.Texture);
                bloom.dirtIntensity.Override(DirtIntensity);
            }
        }

        static double Eighth(SongMap map, double t)
        {
            double i = Math.Floor(map.BeatIndexAt(t));
            return 0.5 * (map.TimeOfBeatIndex(i + 1) - map.TimeOfBeatIndex(i));
        }

        sealed class ByTime : IComparer<Hit>
        {
            public static readonly ByTime Instance = new ByTime();
            public int Compare(Hit a, Hit b) => a.Time.CompareTo(b.Time);
        }
    }
}
