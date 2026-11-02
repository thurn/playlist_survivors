using System;
using System.Collections.Generic;
using UnityEngine;

namespace Champions.Render
{
    /// <summary>
    /// The beat wave (design.md, The Look &gt; The beat wave; Decision 20): on every reliable beat a ring leaves the hero's
    /// feet, its front travelling <see cref="MetresPerBeat"/> a beat, a sharp leading edge and a wake that a point has
    /// shed within half a beat of the front passing it (<see cref="WakeM"/>), its strength falling linearly over its
    /// <see cref="LifeBeats"/>, so it still shows as it crosses the frame. A downbeat's ring is at full strength, any other beat's at
    /// <see cref="BeatStrength"/>. The grid's lines brighten and bend as it passes, the monsters' lines flare and bob,
    /// the structures' edges flare (World.hlsl's <c>BeatWave</c>, <c>BeatWaveCrowd</c>). The caller adds only reliable
    /// beats: with no reliable beat there are no beat rings.
    ///
    /// A ring is added as soon as its beat is known (the sim runs ahead of the speakers) and sets out on the first frame
    /// whose song time reaches it, centred on the hero's feet in that frame. <see cref="Publish"/> is a function of the
    /// song time, the rings added and the hero's path, so fixed-time frames reproduce.
    /// </summary>
    public sealed class BeatWave
    {
        /// <summary>The rings handed to shaders at once: World.hlsl's BEAT_RING_MAX.</summary>
        public const int Max = 4;
        public const float MetresPerBeat = 11f;
        /// <summary>How long a ring runs, in beats: it crosses the frame's floor (about 22 m) before it is gone.</summary>
        public const float LifeBeats = 2f;
        /// <summary>A beat's ring against a downbeat's (1).</summary>
        public const float BeatStrength = 0.6f;
        /// <summary>The front's width (m): sharp.</summary>
        public const float FrontM = 0.6f;
        /// <summary>
        /// The wake's width (m): half a beat behind the front (5.5 m) the band is under 1e-4 of its peak, so a point has
        /// settled back to ground light within half a beat of the front passing it, at any tempo.
        /// </summary>
        public const float WakeM = 0.16f * MetresPerBeat;
        /// <summary>A monster's bob at a full-strength front (m).</summary>
        public const float BobM = 0.08f;

        static readonly int RingId = Shader.PropertyToID("_RC_BeatRing");
        static readonly int CountId = Shader.PropertyToID("_RC_BeatRingCount");
        static readonly int ShapeId = Shader.PropertyToID("_RC_BeatShape");
        static readonly int ClockId = Shader.PropertyToID("_RC_Clock");

        struct Ring
        {
            public double Start;
            public float Speed, Life, Strength;
            public bool Placed;
            public Vector2 At;
        }

        // Sorted by start, so a frame stops at the first that hasn't set out.
        readonly List<Ring> _rings = new List<Ring>(16);
        readonly Vector4[] _published = new Vector4[Max];
        // The latest two beats (their times and periods; NaN period: beatless from then), for the shaders' beat clock: the
        // sim hands a beat over a little before it is heard.
        double _beat = double.NegativeInfinity, _period = double.NaN, _beatBefore = double.NegativeInfinity, _periodBefore = double.NaN;

        /// <summary>The rings the latest <see cref="Publish"/> handed to shaders.</summary>
        public int Published { get; private set; }

        /// <summary>Slot <paramref name="i"/> as the latest publish handed it: centre x, z, the front's radius, its strength now.</summary>
        public Vector4 PublishedRing(int i) => _published[i];

        /// <summary>The rings added and not yet run out.</summary>
        public int Count => _rings.Count;

        /// <summary>A reliable beat at song time <paramref name="t"/>, <paramref name="period"/> seconds long: its ring.</summary>
        public void Beat(double t, double period, bool downbeat)
        {
            if (!(period > 0)) return;
            Add(t, (float)(MetresPerBeat / period), (float)(LifeBeats * period), downbeat ? 1f : BeatStrength);
            Clock(t, period);
        }

        void Clock(double t, double period)
        {
            if (t < _beat) return;
            _beatBefore = _beat;
            _periodBefore = _period;
            _beat = t;
            _period = period;
        }

        /// <summary>Any other ring from the hero's feet (a zone change's sweep): its front's speed (m/s), life (s) and strength.</summary>
        public void Add(double t, float speed, float life, float strength)
        {
            if (!(speed > 0) || !(life > 0) || !(strength > 0)) return;
            var ring = new Ring { Start = t, Speed = speed, Life = life, Strength = strength };
            int k = _rings.Count;
            while (k > 0 && _rings[k - 1].Start > t) k--;
            _rings.Insert(k, ring);
        }

        /// <summary>The beat clock stops at <paramref name="t"/> (no reliable beat): the shaders' phase reads 0 until the next beat.</summary>
        public void Beatless(double t) => Clock(t, double.NaN);

        /// <summary>Forgets every ring.</summary>
        public void Clear()
        {
            _rings.Clear();
            _beat = _beatBefore = double.NegativeInfinity;
            _period = _periodBefore = double.NaN;
        }

        /// <summary>A ring's front radius and strength <paramref name="age"/> seconds after it set out: falling linearly over its life, 0 outside it.</summary>
        public static void At(double age, float speed, float life, float strength, out float radius, out float now)
        {
            radius = (float)Math.Max(0, age) * speed;
            float left = age < 0 ? 0 : 1f - (float)(age / life);
            now = left <= 0 ? 0 : strength * left;
        }

        /// <summary>
        /// Hands shaders the rings alive at song time <paramref name="now"/> (the strongest <see cref="Max"/>), placing each
        /// that sets out this frame on <paramref name="heroFeet"/> (ground x, z), and forgets those run out.
        /// </summary>
        public void Publish(double now, Vector2 heroFeet)
        {
            int n = 0;
            for (int i = 0; i < _rings.Count; i++)
            {
                var r = _rings[i];
                if (r.Start > now) break;
                double age = now - r.Start;
                if (age >= r.Life)
                {
                    _rings.RemoveAt(i--);
                    continue;
                }
                if (!r.Placed)
                {
                    r.Placed = true;
                    r.At = heroFeet;
                    _rings[i] = r;
                }
                At(age, r.Speed, r.Life, r.Strength, out float radius, out float strength);
                var v = new Vector4(r.At.x, r.At.y, radius, strength);
                if (n < Max)
                {
                    _published[n++] = v;
                    continue;
                }
                int weakest = 0;
                for (int j = 1; j < Max; j++) if (_published[j].w < _published[weakest].w) weakest = j;
                if (strength > _published[weakest].w) _published[weakest] = v;
            }
            for (int j = n; j < Max; j++) _published[j] = Vector4.zero;
            Published = n;
            Shader.SetGlobalVectorArray(RingId, _published);
            Shader.SetGlobalFloat(CountId, n);
            Shader.SetGlobalVector(ShapeId, new Vector4(FrontM, WakeM, BobM, 0));
            double beat = now >= _beat ? _beat : _beatBefore, period = now >= _beat ? _period : _periodBefore;
            double since = now - beat;
            bool beating = period > 0 && since >= 0 && since < 2 * period;
            Shader.SetGlobalVector(ClockId, new Vector4((float)now, beating ? (float)(since / period % 1.0) : 0,
                beating ? (float)since : 0, beating ? (float)period : 0));
        }

        /// <summary>The wave's strength at ground point <paramref name="xz"/> as the latest publish drew it (World.hlsl's BeatWave, for tests).</summary>
        public float StrengthAt(Vector2 xz)
        {
            float s = 0;
            for (int i = 0; i < Published; i++)
            {
                var r = _published[i];
                float x = Vector2.Distance(xz, new Vector2(r.x, r.y)) - r.z;
                float w = x > 0 ? FrontM : WakeM;
                s += r.w * Mathf.Exp(-x * x / (w * w));
            }
            return Mathf.Clamp01(s);
        }
    }
}
