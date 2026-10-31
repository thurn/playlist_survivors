using System;
using PS.Game.Sim;
using UnityEngine;

namespace Game.Client.Look
{
    /// <summary>
    /// The game camera (unity-game.md, "Post-processing and Camera"): <see cref="PitchDeg"/> down,
    /// looking north, at a <see cref="FovDeg"/> vertical field of view, over a focus that follows the
    /// hero on a critically damped spring (<see cref="FollowTimeConstant"/>, the choreography sim's
    /// exact step) toward a point <see cref="LeadM"/> ahead along its heading. A spring alone trails a
    /// moving target by 2τ × its speed (5 m behind a hero striding 6 m/s), so the lead also carries the
    /// hero's velocity, smoothed over the same time constant, times 2τ: the focus keeps the hero at the
    /// lead while it walks, and glides between its strides. The view's width across
    /// the ground through the focus follows the music's loudness (28 m quiet, 30 m medium, 34 m loud),
    /// changing level only when the last 2 s's mean is clearly at another one, and eased over two bars
    /// (smoothstep) from the change, starting from wherever it is. On top: the feel's punch (a zoom-in)
    /// and shake (a bounded offset). Stepped once a frame by frameSong's advance, so a fixed-time run
    /// frames the same frames. Sim ground (x, y) is Unity's (x, 0, y).
    /// </summary>
    public sealed class CameraRig
    {
        public const float PitchDeg = 55f, FovDeg = 30f;
        public const double FollowTimeConstant = 0.4, LeadM = 1.5;
        public const float QuietWidthM = 28f, MediumWidthM = 30f, LoudWidthM = 34f;
        /// <summary>The width eases over this many bars of two beats.</summary>
        public const double EaseBars = 2, BeatsPerBar = 2;
        /// <summary>The level changes when the loudness mean is this far from the current level.</summary>
        public const double LevelHysteresis = 0.75;
        /// <summary>
        /// How far the focus may stray from the hero, for the published footprint: the lead, and the
        /// spring's transients as the hero starts, stops and turns (3.5 m at a square corner at 6 m/s).
        /// </summary>
        public const double FocusSlackM = 4;
        /// <summary>A hero moving further than this in one step jumped (a respawn, a seek): the focus cuts to it.</summary>
        public const double CutM = 4;

        double _fx, _fy, _vx, _vy;
        double _hx, _hy, _hvx, _hvy; // the hero's last position and smoothed velocity
        bool _placed;
        int _level = 1;
        float _widthFrom = MediumWidthM, _widthTo = MediumWidthM;
        double _easeStart, _easeLength;

        /// <summary>The focus on the ground (x, y), and the level the width is easing to (0 quiet, 1 medium, 2 loud).</summary>
        public double FocusX => _fx;
        public double FocusY => _fy;
        public int Level => _level;

        public static float WidthOf(int level) => level <= 0 ? QuietWidthM : level == 1 ? MediumWidthM : LoudWidthM;

        /// <summary>A unit heading on the ground for a facing.</summary>
        public static void Heading(Direction facing, out double x, out double y)
        {
            x = facing == Direction.Right ? 1 : facing == Direction.Left ? -1 : 0;
            y = facing == Direction.Up ? 1 : facing == Direction.Down ? -1 : 0;
        }

        /// <summary>
        /// Steps to song time <paramref name="s"/>, <paramref name="dt"/> after the previous step (the
        /// first step, and a hero that jumped, places the focus on its target): the spring toward the
        /// hero's position plus the lead along its heading and its smoothed velocity's, and the width
        /// toward the loudness <paramref name="level"/> (the fast lane's mean over 2 s; below 0, none
        /// known: it holds), eased over two bars of the beat <paramref name="period"/>.
        /// </summary>
        public void Step(double s, double dt, double heroX, double heroY, double headingX, double headingY, double level, double period)
        {
            if (_placed && Math.Abs(heroX - _hx) + Math.Abs(heroY - _hy) > CutM) _placed = false;
            if (!_placed) _hvx = _hvy = 0;
            else if (dt > 0)
            {
                double k = 1 - Math.Exp(-dt / FollowTimeConstant);
                _hvx += ((heroX - _hx) / dt - _hvx) * k;
                _hvy += ((heroY - _hy) / dt - _hvy) * k;
            }
            _hx = heroX;
            _hy = heroY;
            double tx = heroX + LeadM * headingX + 2 * FollowTimeConstant * _hvx;
            double ty = heroY + LeadM * headingY + 2 * FollowTimeConstant * _hvy;
            if (!_placed)
            {
                _fx = tx;
                _fy = ty;
                _vx = _vy = 0;
                _placed = true;
            }
            else if (dt > 0)
            {
                // The exact step of x'' = -2ωx' - ω²x (critically damped), as the choreography sim's.
                double omega = 1 / FollowTimeConstant;
                double e = Math.Exp(-omega * dt);
                double x = _fx - tx, y = _fy - ty;
                double cx = _vx + omega * x, cy = _vy + omega * y;
                _fx = tx + (x + cx * dt) * e;
                _fy = ty + (y + cy * dt) * e;
                _vx = (_vx - omega * cx * dt) * e;
                _vy = (_vy - omega * cy * dt) * e;
            }
            if (level >= 0 && Math.Abs(level - _level) >= LevelHysteresis)
            {
                int to = (int)Math.Round(Math.Max(0, Math.Min(2, level)));
                if (to != _level)
                {
                    _widthFrom = Width(s);
                    _widthTo = WidthOf(to);
                    _easeStart = s;
                    _easeLength = EaseBars * BeatsPerBar * period;
                    _level = to;
                }
            }
        }

        /// <summary>The view's width across the ground at song time s, before the punch.</summary>
        public float Width(double s)
        {
            if (_easeLength <= 0 || s >= _easeStart + _easeLength) return _widthTo;
            float x = (float)Math.Max(0, (s - _easeStart) / _easeLength);
            return _widthFrom + (_widthTo - _widthFrom) * x * x * (3 - 2 * x);
        }

        /// <summary>
        /// Frames <paramref name="camera"/> at song time s over the focus for a view of
        /// <paramref name="aspect"/>, the width times <paramref name="punch"/>, offset by the shake of
        /// amplitude <paramref name="shake"/> metres; <paramref name="widthM"/> in place of the music's width when over 0.
        /// </summary>
        public void Apply(Camera camera, float aspect, double s, float punch, float shake, float widthM = 0)
        {
            var focus = new Vector3((float)_fx, 0, (float)_fy);
            Frame(camera, focus, (widthM > 0 ? widthM : Width(s)) * punch, aspect);
            if (shake > 0) camera.transform.position += Shake(s, shake);
        }

        /// <summary>
        /// The game camera over <paramref name="focus"/>: <see cref="PitchDeg"/> down, looking north,
        /// at <see cref="FovDeg"/>, back along its view axis so the view is <paramref name="widthM"/>
        /// wide across the ground through the focus.
        /// </summary>
        public static void Frame(Camera camera, Vector3 focus, float widthM, float aspect)
        {
            camera.fieldOfView = FovDeg;
            Pose(focus, widthM, aspect, out var position, out var rotation);
            camera.transform.SetPositionAndRotation(position, rotation);
        }

        public static void Pose(Vector3 focus, float widthM, float aspect, out Vector3 position, out Quaternion rotation)
        {
            rotation = Quaternion.Euler(PitchDeg, 0, 0);
            position = focus - rotation * Vector3.forward * Distance(widthM, aspect);
        }

        /// <summary>The camera's distance from the focus along its view axis for a view <paramref name="widthM"/> wide there.</summary>
        public static float Distance(float widthM, float aspect) =>
            widthM * 0.5f / (Mathf.Tan(FovDeg * 0.5f * Mathf.Deg2Rad) * aspect);

        /// <summary>
        /// A deterministic shake offset of at most <paramref name="amplitude"/> metres at song time s:
        /// a few incommensurate sines (CameraFeel's), mostly on the ground plane.
        /// </summary>
        public static Vector3 Shake(double s, float amplitude)
        {
            float t = (float)(s % 1000.0) * 6.2831853f;
            float ox = Mathf.Sin(t * 31f) * 0.6f + Mathf.Sin(t * 47f + 0.3f) * 0.4f;
            float oz = Mathf.Sin(t * 37f + 2.9f) * 0.6f + Mathf.Sin(t * 53f + 1.1f) * 0.4f;
            return Vector3.ClampMagnitude(new Vector3(ox, 0.3f * ox * oz, oz) * amplitude, amplitude);
        }

        /// <summary>
        /// The ground the camera can see relative to the hero, for the sim's spawn ring
        /// (<see cref="HordeConfig.CameraFootprint"/>): the loud view's trapezoid on the ground around
        /// its focus (wider at the far, north edge), grown by <see cref="FocusSlackM"/> on every side,
        /// since the focus leads and lags the hero. A convex polygon, x0, y0, x1, y1, ...,
        /// counter-clockwise.
        /// </summary>
        public static double[] Footprint(float aspect)
        {
            Pose(Vector3.zero, LoudWidthM, aspect, out var position, out var rotation);
            float tanV = Mathf.Tan(FovDeg * 0.5f * Mathf.Deg2Rad), tanH = tanV * aspect;
            var corners = new[] { new Vector2(-1, -1), new Vector2(1, -1), new Vector2(1, 1), new Vector2(-1, 1) };
            var poly = new double[8];
            for (int i = 0; i < 4; i++)
            {
                var ray = rotation * new Vector3(corners[i].x * tanH, corners[i].y * tanV, 1);
                float k = -position.y / ray.y; // every corner ray looks down at 55° ± 15°
                var g = position + ray * k;
                poly[2 * i] = g.x + Math.Sign(g.x) * FocusSlackM;
                poly[2 * i + 1] = g.z + Math.Sign(g.z) * FocusSlackM;
            }
            return poly;
        }
    }
}
