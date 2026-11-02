using System;
using System.Collections.Generic;
using UnityEngine;

namespace Champions.Render
{
    /// <summary>
    /// The zone key on the floor (design.md, The World &gt; Zones; Decision 23): the colour family the grid's lines (and
    /// the structures' fills) take. A change crossfades from the old key to the new behind a front swept out from the
    /// hero's feet over its fade (a beat), reaching <see cref="ReachM"/> (past the frame's corners) as the fade ends, and
    /// the beat wave carries it: the change sends a ring of its own along the front (<see cref="BeatWave.Add"/>). The
    /// keys are the palette's zones (sRGB; <see cref="SetKeys"/>); until the palette lands, <see cref="ProvisionalKeys"/>.
    /// A function of the song time, the changes added and the hero's path, so fixed-time frames reproduce.
    /// </summary>
    public sealed class ZoneSweep
    {
        /// <summary>The front's reach when the fade ends (m): past the farthest floor in frame, the duel's eased-out view too.</summary>
        public const float ReachM = 36f;
        /// <summary>The crossfade's width across the front (m).</summary>
        public const float FeatherM = 4f;
        /// <summary>The change's ring along the front, against a downbeat's.</summary>
        public const float RingStrength = 0.8f;

        /// <summary>
        /// Two cool, a violet and a dim warm (the dragon's), from the archived ion palette's gaps
        /// (docs/archive/design/ion-palette-gaps.md), until the palette bead's zones replace them.
        /// </summary>
        public static readonly Color[] ProvisionalKeys =
        {
            new Color32(0x00, 0xb2, 0xc8, 0xff), // teal
            new Color32(0x2a, 0x5c, 0xff, 0xff), // cobalt
            new Color32(0x6a, 0x2c, 0xff, 0xff), // violet
            new Color32(0xff, 0xaa, 0x33, 0xff), // amber, dim on the floor as every key is
        };

        static readonly int FromId = Shader.PropertyToID("_RC_ZoneFrom");
        static readonly int ToId = Shader.PropertyToID("_RC_ZoneTo");
        static readonly int SweepId = Shader.PropertyToID("_RC_ZoneSweep");

        struct Pending
        {
            public double Start;
            public int Key;
            public float Speed;
        }

        readonly List<Pending> _due = new List<Pending>(4);
        Color[] _keys = (Color[])ProvisionalKeys.Clone();
        int _from, _to;
        double _start = double.NegativeInfinity;
        float _speed = 1;
        Vector2 _at;

        public ZoneSweep(int key = 0)
        {
            _from = _to = Clamp(key);
        }

        /// <summary>The key now (the latest change that has set out).</summary>
        public int Key => _to;

        /// <summary>The key before the latest change.</summary>
        public int Previous => _from;

        /// <summary>The front's radius at the latest publish (m; infinite once it has passed the frame).</summary>
        public float Radius { get; private set; } = float.PositiveInfinity;

        public Vector2 Centre => _at;

        /// <summary>The zones' colours (sRGB), in key order.</summary>
        public void SetKeys(Color[] srgb)
        {
            if (srgb == null || srgb.Length == 0) throw new ArgumentException("at least one zone key");
            _keys = (Color[])srgb.Clone();
            _from = Clamp(_from);
            _to = Clamp(_to);
        }

        public Color KeyColour(int key) => _keys[Clamp(key)];

        /// <summary>The key moves to <paramref name="key"/> at song time <paramref name="t"/>, swept out over <paramref name="fadeS"/>; its ring goes into <paramref name="wave"/>.</summary>
        public void Change(double t, int key, double fadeS, BeatWave wave)
        {
            float fade = (float)Math.Max(1e-3, fadeS);
            var c = new Pending { Start = t, Key = Clamp(key), Speed = ReachM / fade };
            int k = _due.Count;
            while (k > 0 && _due[k - 1].Start > t) k--;
            _due.Insert(k, c);
            wave?.Add(t, c.Speed, fade, RingStrength);
        }

        /// <summary>Sets out the changes due by song time <paramref name="now"/> (from <paramref name="heroFeet"/>, ground x, z) and hands shaders the keys and the front.</summary>
        public void Publish(double now, Vector2 heroFeet)
        {
            while (_due.Count > 0 && _due[0].Start <= now)
            {
                var c = _due[0];
                _due.RemoveAt(0);
                // A change over one still sweeping starts from the key it was taking.
                _from = _to;
                _to = c.Key;
                _start = c.Start;
                _speed = c.Speed;
                _at = heroFeet;
            }
            float radius = (float)((now - _start) * _speed);
            Radius = _from == _to || radius > ReachM + FeatherM ? float.PositiveInfinity : radius;
            var from = _keys[_from].linear;
            var to = _keys[_to].linear;
            Shader.SetGlobalVector(FromId, new Vector4(from.r, from.g, from.b, 1));
            Shader.SetGlobalVector(ToId, new Vector4(to.r, to.g, to.b, 1));
            Shader.SetGlobalVector(SweepId, new Vector4(_at.x, _at.y, float.IsPositiveInfinity(Radius) ? 1e6f : Radius, FeatherM));
        }

        /// <summary>How far ground point <paramref name="xz"/> has taken the key now, as the latest publish drew it (World.hlsl's ZoneNew, for tests).</summary>
        public float NewAt(Vector2 xz)
        {
            if (float.IsPositiveInfinity(Radius)) return 1;
            return Mathf.Clamp01((Radius - Vector2.Distance(xz, _at)) / FeatherM + 0.5f);
        }

        int Clamp(int key) => Mathf.Clamp(key, 0, (_keys?.Length ?? ProvisionalKeys.Length) - 1);
    }
}
