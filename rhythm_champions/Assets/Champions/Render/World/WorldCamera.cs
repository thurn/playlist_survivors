using System;
using System.Collections.Generic;
using UnityEngine;

namespace Champions.Render
{
    /// <summary>
    /// The camera (design.md, The Look &gt; Camera, Restraint; Decision 25): the reference trailer's high angle,
    /// <see cref="PitchDeg"/> down, never rotating, following the hero with a slight lead the way it moves
    /// (<see cref="LeadS"/> of its velocity, at most <see cref="MaxLeadM"/>, eased in and out), easing out
    /// <see cref="DuelScale"/> for boss duels (<see cref="Duel"/>). It punches and shakes only on special moments
    /// (<see cref="Special"/>, the impulse field's cue), each gone within <see cref="KickS"/>. A function of the frames'
    /// song times, steps and the hero's path, so fixed-time frames reproduce.
    /// </summary>
    public sealed class WorldCamera
    {
        public const float PitchDeg = 60f, DistanceM = 23f, FovDeg = 40f;
        /// <summary>
        /// The lead: this many seconds of the hero's velocity ahead, at most <see cref="MaxLeadM"/>, eased over
        /// <see cref="LeadEaseS"/>; the focus's trail (<see cref="FollowS"/>) takes back about 0.7 m of it at walking
        /// speed, so a walk leads by about 1.8 m.
        /// </summary>
        public const float LeadS = 0.45f, MaxLeadM = 2.5f, LeadEaseS = 0.5f;
        /// <summary>The focus trails the hero's feet (plus the lead) by about this long: a dash's 4 m in 0.15 s doesn't jerk the frame.</summary>
        public const float FollowS = 0.12f;
        /// <summary>A duel's view: this much farther out, eased over <see cref="DuelEaseS"/>.</summary>
        public const float DuelScale = 1.2f, DuelEaseS = 1.5f;
        /// <summary>A special moment's kick: a shake of up to <see cref="ShakeM"/> and a punch in of <see cref="PunchShare"/> of the distance, both falling as the square of what is left of <see cref="KickS"/>.</summary>
        public const float ShakeM = 0.12f, PunchShare = 0.04f, KickS = 0.25f;

        struct Kick
        {
            public double Start;
            public float Strength;
        }

        readonly Camera _camera;
        readonly List<Kick> _kicks = new List<Kick>(8);
        Vector2 _focus, _focusVelocity, _lead, _leadVelocity;
        float _duel;
        bool _placed, _duelOn;

        public WorldCamera(Camera camera)
        {
            _camera = camera;
            if (_camera != null) _camera.fieldOfView = FovDeg;
        }

        public Camera Camera => _camera;

        /// <summary>Where the view is centred on the ground (x, z): the hero's feet plus the lead, smoothed.</summary>
        public Vector2 Focus => _focus;

        public Vector2 Lead => _lead;

        /// <summary>How far eased out for a duel (0: none, 1: <see cref="DuelScale"/>).</summary>
        public float DuelEase => Smooth(_duel);

        /// <summary>The distance from the focus now, before kicks.</summary>
        public float Distance => DistanceM * Mathf.Lerp(1f, DuelScale, DuelEase);

        /// <summary>Eases out for a boss duel, or back in after it.</summary>
        public void Duel(bool on) => _duelOn = on;

        /// <summary>A special moment at song time <paramref name="t"/>: a shake and a punch, <paramref name="strength"/> 0 to 1.5.</summary>
        public void Special(double t, float strength)
        {
            if (!(strength > 0)) return;
            _kicks.Add(new Kick { Start = t, Strength = Mathf.Min(1.5f, strength) });
        }

        /// <summary>
        /// Places the camera for the frame at song time <paramref name="now"/>, <paramref name="dt"/> seconds after the last,
        /// the hero's feet at <paramref name="hero"/> (ground x, z) moving at <paramref name="velocity"/> (m/s).
        /// </summary>
        public void Update(double now, Vector2 hero, Vector2 velocity, float dt)
        {
            dt = Mathf.Max(0f, dt);
            var lead = Vector2.ClampMagnitude(velocity * LeadS, MaxLeadM);
            if (!_placed)
            {
                _placed = true;
                _lead = lead;
                _focus = hero + lead;
            }
            else if (dt > 0)
            {
                _lead = SmoothDamp(_lead, lead, ref _leadVelocity, LeadEaseS, dt);
                _focus = SmoothDamp(_focus, hero + _lead, ref _focusVelocity, FollowS, dt);
            }
            _duel = Mathf.MoveTowards(_duel, _duelOn ? 1f : 0f, dt / DuelEaseS);

            float distance = Distance;
            Vector2 shake = Vector2.zero;
            float punch = 0;
            for (int i = 0; i < _kicks.Count; i++)
            {
                var k = _kicks[i];
                double age = now - k.Start;
                if (age >= KickS)
                {
                    _kicks.RemoveAt(i--);
                    continue;
                }
                if (age < 0) continue;
                float left = 1f - (float)(age / KickS), e = k.Strength * left * left;
                // A shake that doesn't depend on the frame rate: a few incommensurate sines, phased by the moment's start.
                float seed = (float)(k.Start % 97.0);
                float a = (float)age;
                shake += e * ShakeM * new Vector2(Mathf.Sin(2 * Mathf.PI * 23f * a + seed * 3.1f) + 0.5f * Mathf.Sin(2 * Mathf.PI * 41f * a + seed),
                    Mathf.Cos(2 * Mathf.PI * 19f * a + seed * 1.7f) + 0.5f * Mathf.Sin(2 * Mathf.PI * 37f * a + seed * 2.3f)) / 1.5f;
                punch = Mathf.Max(punch, e * PunchShare);
            }
            Place(_focus + shake, distance * (1f - punch));
        }

        void Place(Vector2 focus, float distance)
        {
            if (_camera == null) return;
            var rotation = Quaternion.Euler(PitchDeg, 0f, 0f);
            var t = _camera.transform;
            t.rotation = rotation;
            t.position = new Vector3(focus.x, 0f, focus.y) - rotation * Vector3.forward * distance;
        }

        static float Smooth(float u) => u * u * (3f - 2f * u);

        static Vector2 SmoothDamp(Vector2 current, Vector2 target, ref Vector2 velocity, float time, float dt)
        {
            float vx = velocity.x, vy = velocity.y;
            var r = new Vector2(Mathf.SmoothDamp(current.x, target.x, ref vx, time, Mathf.Infinity, dt),
                Mathf.SmoothDamp(current.y, target.y, ref vy, time, Mathf.Infinity, dt));
            velocity = new Vector2(vx, vy);
            return r;
        }
    }
}
