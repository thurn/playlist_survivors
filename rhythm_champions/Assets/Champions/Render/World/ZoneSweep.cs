using System;
using System.Collections.Generic;
using UnityEngine;

namespace Champions.Render
{
    /// <summary>
    /// The zone key on the floor (design.md, The World &gt; Zones; Decision 23): the palette's zone (docs/champions/style.md,
    /// Zones) whose colours the grid's lines (at rest and under the wave) and the structures' fills and edges take. A
    /// change crossfades from the old zone to the new behind a front swept out from the hero's feet over its fade (a
    /// beat), reaching <see cref="ReachM"/> (past the frame's corners) as the fade ends, and the beat wave carries it: the
    /// change sends a ring of its own along the front (<see cref="BeatWave.Add"/>). Shaders read the two zones' colours
    /// by role (World.hlsl's ZoneColour). A function of the song time, the changes added and the hero's path, so
    /// fixed-time frames reproduce.
    /// </summary>
    public sealed class ZoneSweep
    {
        /// <summary>The front's reach when the fade ends (m): past the farthest floor in frame, the duel's eased-out view too.</summary>
        public const float ReachM = 36f;
        /// <summary>The crossfade's width across the front (m).</summary>
        public const float FeatherM = 4f;
        /// <summary>The change's ring along the front, against a downbeat's.</summary>
        public const float RingStrength = 0.8f;

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
        readonly Vector4[] _fromColours = new Vector4[4], _toColours = new Vector4[4];
        readonly Palette.Zone[] _zones;
        int _from, _to;
        double _start = double.NegativeInfinity;
        float _speed = 1;
        Vector2 _at;

        /// <summary>The sweep over the palette's <paramref name="zones"/>, starting on <paramref name="key"/>.</summary>
        public ZoneSweep(Palette.Zone[] zones, int key = 0)
        {
            if (zones == null || zones.Length == 0) throw new ArgumentException("at least one zone");
            _zones = zones;
            _from = _to = Clamp(key);
        }

        /// <summary>The key now (the latest change that has set out).</summary>
        public int Key => _to;

        /// <summary>The key before the latest change.</summary>
        public int Previous => _from;

        /// <summary>The front's radius at the latest publish (m; infinite once it has passed the frame).</summary>
        public float Radius { get; private set; } = float.PositiveInfinity;

        public Vector2 Centre => _at;

        public Palette.Zone Zone(int key) => _zones[Clamp(key)];

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
            Fill(_fromColours, _zones[_from]);
            Fill(_toColours, _zones[_to]);
            Shader.SetGlobalVectorArray(FromId, _fromColours);
            Shader.SetGlobalVectorArray(ToId, _toColours);
            Shader.SetGlobalVector(SweepId, new Vector4(_at.x, _at.y, float.IsPositiveInfinity(Radius) ? 1e6f : Radius, FeatherM));
        }

        /// <summary>How far ground point <paramref name="xz"/> has taken the key now, as the latest publish drew it (World.hlsl's ZoneNew, for tests).</summary>
        public float NewAt(Vector2 xz)
        {
            if (float.IsPositiveInfinity(Radius)) return 1;
            return Mathf.Clamp01((Radius - Vector2.Distance(xz, _at)) / FeatherM + 0.5f);
        }

        /// <summary>A zone's colours by World.hlsl's roles (ZONE_LINE, ZONE_PULSE, ZONE_FILL, ZONE_EDGE), linear.</summary>
        static void Fill(Vector4[] into, Palette.Zone z)
        {
            into[0] = Linear(z.Line);
            into[1] = Linear(z.Pulse);
            into[2] = Linear(z.Fill);
            into[3] = Linear(z.Edge);
        }

        static Vector4 Linear(Color srgb)
        {
            var c = srgb.linear;
            return new Vector4(c.r, c.g, c.b, 1);
        }

        int Clamp(int key) => Mathf.Clamp(key, 0, _zones.Length - 1);
    }
}
