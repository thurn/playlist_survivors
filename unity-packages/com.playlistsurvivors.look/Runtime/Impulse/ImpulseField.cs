using System;
using System.Collections.Generic;
using UnityEngine;

namespace PS.Look
{
    /// <summary>What wrote an impulse, for its default shape and the beat audit's trace.</summary>
    public enum ImpulseKind { Blow, Death, Shatter, Beat }

    /// <summary>
    /// One ring of the impulse field: it sets out from <see cref="At"/> (ground x, z) at song time <see cref="Start"/>,
    /// its front travelling at <see cref="Speed"/> until it has gone <see cref="Reach"/>, its strength falling to
    /// nothing on the way, in <see cref="Colour"/> (sRGB, as the palette's roles).
    /// </summary>
    public struct Impulse
    {
        public ImpulseKind Kind;
        public Vector2 At;
        public double Start;
        public float Strength, Speed, Reach, Width;
        public Color Colour;
        /// <summary>The strengths of the rings merged into it, uncapped (0: none merged yet), which weigh its centre and colour.</summary>
        internal float Merged;

        /// <summary>How long the ring runs (s).</summary>
        public float Life => Reach / Mathf.Max(1e-3f, Speed);

        /// <summary>The front's radius <paramref name="age"/> seconds after it set out.</summary>
        public float RadiusAt(double age) => (float)Math.Max(0, age) * Speed;

        /// <summary>
        /// Its strength <paramref name="age"/> seconds after it set out: full on its first frame, falling as the
        /// square of what is left of its run, nothing before it sets out or after it ends.
        /// </summary>
        public float StrengthAt(double age)
        {
            if (age < 0) return 0;
            float left = 1f - (float)(age / Life);
            return left <= 0 ? 0 : Strength * left * left;
        }
    }

    /// <summary>
    /// The impulse field (neon-look.md, The impulse field): the one shared service the floor grid, the crowd and the
    /// post read. Every blow, death, shatter and beat a player feeds it is a ring expanding from a point
    /// (<see cref="Impulse"/>), scheduled at its own song time, so a player can add it as soon as it knows it (the game
    /// ahead of frameSong, demo3d a whole shot at load) and it sets out on the first frame that reaches its time.
    /// <see cref="Publish"/> hands the strongest <see cref="Max"/> rings alive at a song time to every shader as globals
    /// (Impulse.hlsl), a function of the song time and the rings added, so fixed-time frames reproduce.
    /// - Rings of one kind setting out at the same instant within <see cref="MergeM"/> of each other are one ring (their
    ///   strengths add in quadrature, up to <see cref="MergedCap"/>, and it runs farther in the same time): a mass kill
    ///   is a few strong rings, out past its shards, not hundreds.
    /// - <see cref="Shown"/> hears each ring on the first frame it is published, with its own time (the beat audit).
    /// - <see cref="Strongest"/> is the strongest ring alive, for the post's screen ripple at big impacts.
    /// The field is the players' one instance, <see cref="Active"/>; shaders read nothing until it is published.
    /// </summary>
    public sealed class ImpulseField
    {
        /// <summary>The rings handed to shaders at once: Impulse.hlsl's IMPULSE_MAX.</summary>
        public const int Max = 32;
        /// <summary>How close same-instant rings of one kind merge (m).</summary>
        public const float MergeM = 3f;
        /// <summary>A merged ring's strongest.</summary>
        public const float MergedCap = 1.6f;
        /// <summary>Same instant: the times of one event's rings (the deaths a blow makes land on its impact).</summary>
        const double SameS = 1e-4;

        /// <summary>The players' field, or null before one makes it.</summary>
        public static ImpulseField Active { get; set; }

        static readonly int RingId = Shader.PropertyToID("_ImpulseRing");
        static readonly int ColourId = Shader.PropertyToID("_ImpulseColor");
        static readonly int CountId = Shader.PropertyToID("_ImpulseCount");
        static readonly int StrongestId = Shader.PropertyToID("_ImpulseStrongest");

        // Sorted by start, so a frame stops at the first that hasn't set out.
        readonly List<Impulse> _rings = new List<Impulse>(256);
        readonly List<bool> _shown = new List<bool>(256);
        readonly Vector4[] _ring = new Vector4[Max], _colour = new Vector4[Max];
        readonly float[] _strength = new float[Max];

        /// <summary>Called with a ring's kind and start on the first frame it is published (null: no one listens).</summary>
        public Action<ImpulseKind, double> Shown;

        /// <summary>The rings added and not yet forgotten.</summary>
        public int Count => _rings.Count;

        /// <summary>The rings the latest <see cref="Publish"/> handed to shaders.</summary>
        public int Published { get; private set; }

        /// <summary>The strongest ring alive at the latest publish (centre x, z, radius, strength now); strength 0 for none.</summary>
        public Vector4 Strongest { get; private set; }

        /// <summary>Ring <paramref name="i"/> in start order.</summary>
        public Impulse this[int i] => _rings[i];

        /// <summary>Adds a ring (its colour sRGB, as the palette's roles), merging it into a same-instant one of its kind nearby.</summary>
        public void Add(Impulse ring)
        {
            if (!(ring.Strength > 0) || !(ring.Reach > 0) || !(ring.Speed > 0)) return;
            int at = Lower(ring.Start - SameS);
            for (int i = at; i < _rings.Count && _rings[i].Start <= ring.Start + SameS; i++)
            {
                var r = _rings[i];
                if (r.Kind != ring.Kind || (r.At - ring.At).sqrMagnitude > MergeM * MergeM || _shown[i]) continue;
                float a = r.Merged > 0 ? r.Merged : r.Strength, b = ring.Strength, sum = a + b;
                r.At = (r.At * a + ring.At * b) / sum;
                r.Colour = (r.Colour * a + ring.Colour * b) / sum;
                r.Merged = sum;
                a = r.Strength;
                r.Strength = Mathf.Min(MergedCap, Mathf.Sqrt(a * a + b * b));
                // A merged ring runs farther in the same time (the factors multiply to at most sqrt(cap / the first's strength)).
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

        /// <summary>Forgets every ring (a trailer's cut).</summary>
        public void Clear()
        {
            _rings.Clear();
            _shown.Clear();
        }

        /// <summary>
        /// Hands shaders the strongest <see cref="Max"/> rings alive at song time <paramref name="now"/> and forgets those
        /// ended before it. Time runs forward between clears: a ring forgotten doesn't come back on a seek.
        /// </summary>
        public void Publish(double now)
        {
            int n = 0;
            float strongest = 0;
            Strongest = Vector4.zero;
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
                    Shown?.Invoke(r.Kind, r.Start);
                }
                float s = r.StrengthAt(age);
                if (s > strongest)
                {
                    strongest = s;
                    Strongest = new Vector4(r.At.x, r.At.y, r.RadiusAt(age), s);
                }
                // The strongest Max, kept unsorted: a new one replaces the weakest held.
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
            Shader.SetGlobalVectorArray(RingId, _ring);
            Shader.SetGlobalVectorArray(ColourId, _colour);
            Shader.SetGlobalFloat(CountId, n);
            Shader.SetGlobalVector(StrongestId, Strongest);
        }

        void Fill(int slot, in Impulse r, double age, float s)
        {
            _strength[slot] = s;
            _ring[slot] = new Vector4(r.At.x, r.At.y, r.RadiusAt(age), r.Width);
            var c = r.Colour.linear; // the palette's sRGB roles; a vector array isn't converted as a colour is
            _colour[slot] = new Vector4(c.r * s, c.g * s, c.b * s, s);
        }

        /// <summary>Ring slot <paramref name="slot"/> as the latest publish handed it (x, z, radius, width), for tests.</summary>
        public Vector4 PublishedRing(int slot) => _ring[slot];

        /// <summary>Ring slot <paramref name="slot"/>'s colour times its strength, and its strength, for tests.</summary>
        public Vector4 PublishedColour(int slot) => _colour[slot];

        /// <summary>Clears the shaders' rings (a player leaving the look).</summary>
        public static void Unpublish()
        {
            Shader.SetGlobalFloat(CountId, 0);
            Shader.SetGlobalVector(StrongestId, Vector4.zero);
        }

        /// <summary>The first ring starting at or after <paramref name="t"/>.</summary>
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

        // The rings each event writes, in a colour from the look's palette.

        /// <summary>
        /// The ring of the hero's blow of reach <paramref name="reach"/> (m), <paramref name="weight"/> 0 (a light strike:
        /// faint, so the swing reads as the blade's cut) to 1 (a slam, a smash). Rings run at the style guide's pace:
        /// across a 35 m frame in about a beat, gone in about one and a half.
        /// </summary>
        public static Impulse Blow(Vector2 at, double t, float reach, float weight, Color colour) => new Impulse
        {
            Kind = ImpulseKind.Blow, At = at, Start = t, Colour = colour,
            Strength = Mathf.Lerp(0.25f, 1.2f, weight), Speed = Mathf.Lerp(26f, 36f, weight),
            Reach = Mathf.Clamp(reach * Mathf.Lerp(1.6f, 3f, weight), 8f, 30f), Width = Mathf.Lerp(1.4f, 2.6f, weight),
        };

        /// <summary>A death's small ring, at the body, in its gem.</summary>
        public static Impulse Death(Vector2 at, double t, Color colour) => new Impulse
        {
            Kind = ImpulseKind.Death, At = at, Start = t, Colour = colour, Strength = 0.4f, Speed = 14f, Reach = 6f, Width = 1f,
        };

        /// <summary>A shatter's (a structure's or a crystal formation's break) big ring, <paramref name="sizeM"/> the break's size (m, about 1 to 6).</summary>
        public static Impulse Shatter(Vector2 at, double t, float sizeM, Color colour) => new Impulse
        {
            Kind = ImpulseKind.Shatter, At = at, Start = t, Colour = colour,
            Strength = Mathf.Clamp(0.6f + 0.15f * sizeM, 0.7f, 1.5f), Speed = 34f,
            Reach = Mathf.Clamp(10f + 5f * sizeM, 16f, 30f), Width = Mathf.Clamp(1.2f + 0.3f * sizeM, 1.5f, 3f),
        };

        /// <summary>A beat's faint ring from the hero, stronger on a bar's downbeat.</summary>
        public static Impulse Beat(Vector2 at, double t, bool downbeat, Color colour) => new Impulse
        {
            Kind = ImpulseKind.Beat, At = at, Start = t, Colour = colour,
            Strength = downbeat ? 0.16f : 0.07f, Speed = 16f, Reach = downbeat ? 14f : 9f, Width = 1.2f,
        };
    }
}
