using System;
using System.Collections.Generic;
using UnityEngine;

namespace Champions.Render
{
    /// <summary>What sent an event ring: its shape (<see cref="ImpulseField.Ring"/>) and whether it is a special moment.</summary>
    public enum RingKind
    {
        /// <summary>An on-beat dash that deals damage.</summary>
        DashOnBeat,
        /// <summary>A slam: Leap Slam's landing, an elite's or a boss's.</summary>
        Slam,
        /// <summary>A boss's entrance, which shatters the horde.</summary>
        BossEntrance,
        BossDeath,
        /// <summary>A boss's Break bar filling.</summary>
        Break,
        /// <summary>Many kills at one instant.</summary>
        MassKill,
        StructureBreak,
        /// <summary>An evolved attack's or dash's hit.</summary>
        Evolved,
    }

    /// <summary>
    /// One event ring: it sets out from <see cref="At"/> (ground x, z) at song time <see cref="Start"/>, its front
    /// travelling at <see cref="Speed"/> until it has gone <see cref="Reach"/>, its strength falling to nothing on the
    /// way, in <see cref="Colour"/> (sRGB).
    /// </summary>
    public struct Impulse
    {
        public RingKind Kind;
        public Vector2 At;
        public double Start;
        public float Strength, Speed, Reach, Width;
        public Color Colour;
        /// <summary>A special moment's ring: drawn at full strength, lighting <c>_RC_Special</c>; any other at <see cref="ImpulseField.CalmRing"/> of it.</summary>
        public bool Special;
        /// <summary>The strengths of the rings merged into it, uncapped (0: none merged yet), which weigh its centre and colour.</summary>
        internal float Merged;

        /// <summary>How long the ring runs (s).</summary>
        public float Life => Reach / Mathf.Max(1e-3f, Speed);

        /// <summary>The front's radius <paramref name="age"/> seconds after it set out.</summary>
        public float RadiusAt(double age) => (float)Math.Max(0, age) * Speed;

        /// <summary>Its strength <paramref name="age"/> seconds after it set out: full at first, falling as the square of what is left of its run.</summary>
        public float StrengthAt(double age)
        {
            if (age < 0) return 0;
            float left = 1f - (float)(age / Life);
            return left <= 0 ? 0 : Strength * left * left;
        }
    }

    /// <summary>
    /// The event rings (design.md, The Look &gt; The beat wave, Restraint), ported by review from the look package's
    /// impulse field: every on-beat dash, slam, boss entrance or death, Break, mass kill and structure break is a ring
    /// expanding from a point (<see cref="Impulse"/>), scheduled at its own song time, so the presenter adds it as soon
    /// as the sim knows it and it sets out on the first frame that reaches its time. <see cref="Publish"/> hands the
    /// strongest <see cref="Max"/> rings alive to every shader as globals (World.hlsl), a function of the song time and
    /// the rings added, so fixed-time frames reproduce.
    /// - Restraint: a ring that isn't <see cref="Impulse.Special"/> is drawn at <see cref="CalmRing"/> of its strength,
    ///   under the grid's knee, so only special moments bend the whole grid; the special rings alive light
    ///   <c>_RC_Special</c> (<see cref="SpecialAt"/>, gone within a quarter second), which every flash reads, and
    ///   <see cref="Moments"/> hands the camera and the post each special moment once.
    /// - Same-instant rings of one kind within <see cref="MergeM"/> merge (strengths in quadrature up to
    ///   <see cref="MergedCap"/>): a mass kill is a few strong rings, not hundreds.
    /// </summary>
    public sealed class ImpulseField
    {
        /// <summary>The rings handed to shaders at once: World.hlsl's IMPULSE_MAX.</summary>
        public const int Max = 16;
        public const float MergeM = 3f;
        public const float MergedCap = 1.6f;
        const double SameS = 1e-4;
        /// <summary>A calm ring's drawn share of its strength: under the grid's knee, so it barely shows.</summary>
        public const float CalmRing = 0.4f;
        /// <summary>How long a special moment lights <c>_RC_Special</c> (s): its flash is gone within a quarter second.</summary>
        public const float SpecialSeconds = 0.25f;

        static readonly int RingId = Shader.PropertyToID("_RC_ImpulseRing");
        static readonly int ColourId = Shader.PropertyToID("_RC_ImpulseColor");
        static readonly int CountId = Shader.PropertyToID("_RC_ImpulseCount");
        static readonly int SpecialId = Shader.PropertyToID("_RC_Special");

        readonly List<Impulse> _rings = new List<Impulse>(64);
        readonly List<bool> _shown = new List<bool>(64);
        readonly Vector4[] _ring = new Vector4[Max], _colour = new Vector4[Max];
        readonly float[] _strength = new float[Max];

        /// <summary>A special ring setting out, on the first frame it is published: its kind, start, centre and strength (the camera's and post's cue).</summary>
        public Action<Impulse> Moments;

        public int Count => _rings.Count;
        public int Published { get; private set; }
        /// <summary><c>_RC_Special</c> at the latest publish: 1 on a special moment's first frame, 0 outside one.</summary>
        public float Special { get; private set; }

        public Impulse this[int i] => _rings[i];

        /// <summary>Adds a ring, merging it into a same-instant one of its kind nearby that hasn't set out.</summary>
        public void Add(Impulse ring)
        {
            if (!(ring.Strength > 0) || !(ring.Reach > 0) || !(ring.Speed > 0)) return;
            int at = Lower(ring.Start - SameS);
            for (int i = at; i < _rings.Count && _rings[i].Start <= ring.Start + SameS; i++)
            {
                var r = _rings[i];
                if (r.Kind != ring.Kind || r.Special != ring.Special || (r.At - ring.At).sqrMagnitude > MergeM * MergeM || _shown[i]) continue;
                float a = r.Merged > 0 ? r.Merged : r.Strength, b = ring.Strength, sum = a + b;
                r.At = (r.At * a + ring.At * b) / sum;
                r.Colour = (r.Colour * a + ring.Colour * b) / sum;
                r.Merged = sum;
                a = r.Strength;
                r.Strength = Mathf.Min(MergedCap, Mathf.Sqrt(a * a + b * b));
                float grow = Mathf.Min(1.5f, Mathf.Sqrt(r.Strength / Mathf.Max(a, b)));
                r.Reach = Mathf.Max(r.Reach, ring.Reach) * grow;
                r.Speed = Mathf.Max(r.Speed, ring.Speed) * grow;
                r.Width = Mathf.Max(r.Width, ring.Width);
                _rings[i] = r;
                return;
            }
            int k = Lower(ring.Start + SameS);
            _rings.Insert(k, ring);
            _shown.Insert(k, false);
        }

        public void Clear()
        {
            _rings.Clear();
            _shown.Clear();
        }

        /// <summary>Hands shaders the strongest <see cref="Max"/> rings alive at song time <paramref name="now"/> and forgets those ended.</summary>
        public void Publish(double now)
        {
            int n = 0;
            float special = 0;
            for (int i = 0; i < _rings.Count; i++)
            {
                var r = _rings[i];
                if (r.Start > now) break;
                double age = now - r.Start;
                if (age >= r.Life)
                {
                    _rings.RemoveAt(i);
                    _shown.RemoveAt(i);
                    i--;
                    continue;
                }
                if (!_shown[i])
                {
                    _shown[i] = true;
                    if (r.Special) Moments?.Invoke(r);
                }
                if (r.Special) special = Mathf.Max(special, SpecialAt(age));
                float s = r.StrengthAt(age) * (r.Special ? 1f : CalmRing);
                if (n < Max)
                {
                    Fill(n++, r, age, s);
                    continue;
                }
                int weakest = 0;
                for (int j = 1; j < Max; j++) if (_strength[j] < _strength[weakest]) weakest = j;
                if (s > _strength[weakest]) Fill(weakest, r, age, s);
            }
            for (int j = n; j < Max; j++) _ring[j] = _colour[j] = Vector4.zero;
            Published = n;
            Special = special;
            Shader.SetGlobalFloat(SpecialId, special);
            Shader.SetGlobalVectorArray(RingId, _ring);
            Shader.SetGlobalVectorArray(ColourId, _colour);
            Shader.SetGlobalFloat(CountId, n);
        }

        void Fill(int slot, in Impulse r, double age, float s)
        {
            _strength[slot] = s;
            _ring[slot] = new Vector4(r.At.x, r.At.y, r.RadiusAt(age), r.Width);
            var c = r.Colour.linear;
            _colour[slot] = new Vector4(c.r * s, c.g * s, c.b * s, s);
        }

        /// <summary>A special moment's light <paramref name="age"/> seconds after it: 1 at first, (1 - u)² over <see cref="SpecialSeconds"/>.</summary>
        public static float SpecialAt(double age)
        {
            if (age < 0 || age >= SpecialSeconds) return 0f;
            float left = 1f - (float)(age / SpecialSeconds);
            return left * left;
        }

        public Vector4 PublishedRing(int slot) => _ring[slot];
        public Vector4 PublishedColour(int slot) => _colour[slot];

        int Lower(double t)
        {
            int lo = 0, hi = _rings.Count;
            while (lo < hi)
            {
                int mid = (lo + hi) >> 1;
                if (_rings[mid].Start < t) lo = mid + 1; else hi = mid;
            }
            return lo;
        }

        /// <summary>
        /// The ring a <paramref name="kind"/> of event sends from <paramref name="at"/> at song time <paramref name="t"/>, in
        /// <paramref name="colour"/> (sRGB; the hero's are white), <paramref name="weight"/> 0 to 1 scaling a mass kill's
        /// by its count. Every kind the design lists is a special moment; a boss's entrance and death cross the whole
        /// frame, a dash's ring only its neighbourhood.
        /// </summary>
        public static Impulse Ring(RingKind kind, Vector2 at, double t, Color colour, float weight = 1f)
        {
            float w = Mathf.Clamp01(weight);
            var r = new Impulse { Kind = kind, At = at, Start = t, Colour = colour, Special = true };
            switch (kind)
            {
                case RingKind.DashOnBeat: r.Strength = 0.7f; r.Speed = 30f; r.Reach = 14f; r.Width = 1.6f; break;
                case RingKind.Slam: r.Strength = 1.0f; r.Speed = 32f; r.Reach = 22f; r.Width = 2.2f; break;
                case RingKind.BossEntrance: r.Strength = 1.4f; r.Speed = 40f; r.Reach = 40f; r.Width = 3.0f; break;
                case RingKind.BossDeath: r.Strength = 1.5f; r.Speed = 36f; r.Reach = 40f; r.Width = 3.2f; break;
                case RingKind.Break: r.Strength = 1.0f; r.Speed = 34f; r.Reach = 24f; r.Width = 2.4f; break;
                case RingKind.MassKill:
                    r.Strength = Mathf.Lerp(0.6f, 1.2f, w); r.Speed = 28f; r.Reach = Mathf.Lerp(14f, 24f, w); r.Width = Mathf.Lerp(1.6f, 2.4f, w);
                    break;
                case RingKind.StructureBreak: r.Strength = 0.8f; r.Speed = 30f; r.Reach = 16f; r.Width = 1.8f; break;
                case RingKind.Evolved: r.Strength = 0.9f; r.Speed = 30f; r.Reach = 18f; r.Width = 2.0f; break;
                default: throw new ArgumentOutOfRangeException(nameof(kind), kind, null);
            }
            return r;
        }
    }
}
