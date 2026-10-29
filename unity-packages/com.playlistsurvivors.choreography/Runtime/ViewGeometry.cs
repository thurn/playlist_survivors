using System;
using System.Numerics;

namespace PS.Choreography
{
    /// <summary>
    /// The game camera's visible ground (camera-and-sound.md, "Camera"): by default demo3d's 55°
    /// pitch, a 30° vertical field of view at 16:9, looking along ground +Y with a fixed yaw. A
    /// pinhole camera sees a trapezoid of ground: narrow at the near (−Y) edge, wide at the far
    /// edge, and exactly the view width across its middle (the focus). Everything scales with the
    /// view width. At <see cref="TopDownPitchDeg"/> (demo2d's top-down ground) the trapezoid is a
    /// rectangle, the view width by the width over the aspect.
    /// </summary>
    public readonly struct ViewGeometry
    {
        public const double PitchDeg = 55, TopDownPitchDeg = 90, VerticalFovDeg = 30, Aspect = 16.0 / 9.0;

        public readonly float Width;
        /// <summary>Distances from the focus to the near and far edges, along ground Y.</summary>
        public readonly float Near, Far;
        /// <summary>Half widths of the near and far edges.</summary>
        public readonly float NearHalf, FarHalf;

        public ViewGeometry(float width, double pitchDeg = PitchDeg)
        {
            Width = width;
            double deg = Math.PI / 180;
            double tanHalfH = Math.Tan(VerticalFovDeg / 2 * deg) * Aspect;
            double distance = width / (2 * tanHalfH);
            double pitch = pitchDeg * deg;
            double h = distance * Math.Sin(pitch);
            double back = distance * Math.Cos(pitch);
            double nearGround = h / Math.Tan(pitch + VerticalFovDeg / 2 * deg);
            double farGround = h / Math.Tan(pitch - VerticalFovDeg / 2 * deg);
            Near = (float)(back - nearGround);
            Far = (float)(farGround - back);
            NearHalf = (float)((nearGround * Math.Cos(pitch) + h * Math.Sin(pitch)) * tanHalfH);
            FarHalf = (float)((farGround * Math.Cos(pitch) + h * Math.Sin(pitch)) * tanHalfH);
        }

        /// <summary>Visible ground in m².</summary>
        public float Area => (NearHalf + FarHalf) * (Near + Far);

        /// <summary>The farthest visible ground from the focus.</summary>
        public float Radius => (float)Math.Max(Math.Sqrt(Far * Far + FarHalf * FarHalf), Math.Sqrt(Near * Near + NearHalf * NearHalf));

        /// <summary>Half width of the visible ground at ground offset <paramref name="dy"/> from the focus.</summary>
        public float HalfWidthAt(float dy) => NearHalf + (FarHalf - NearHalf) * (dy + Near) / (Near + Far);

        /// <summary>Is <paramref name="p"/> visible, with the view grown by <paramref name="margin"/> metres on every side?</summary>
        public bool Contains(Vector2 focus, Vector2 p, float margin = 0f)
        {
            float dy = p.Y - focus.Y;
            if (dy < -Near - margin || dy > Far + margin) return false;
            float dyc = Math.Max(-Near, Math.Min(Far, dy));
            return Math.Abs(p.X - focus.X) <= HalfWidthAt(dyc) + margin;
        }

        /// <summary>How far outside the view <paramref name="p"/> lies (0 inside): the margin that would just contain it.</summary>
        public float OutsideDistance(Vector2 focus, Vector2 p)
        {
            float dy = p.Y - focus.Y;
            float outY = Math.Max(0f, Math.Max(-Near - dy, dy - Far));
            float dyc = Math.Max(-Near, Math.Min(Far, dy));
            float outX = Math.Max(0f, Math.Abs(p.X - focus.X) - HalfWidthAt(dyc));
            return Math.Max(outX, outY);
        }

        /// <summary>
        /// A point on the view's edge, grown outward by <paramref name="margin"/>, at perimeter
        /// fraction <paramref name="u"/> in [0, 1), with its outward normal.
        /// </summary>
        public Vector2 EdgePoint(Vector2 focus, float u, float margin, out Vector2 normal)
        {
            // Corners: near-left, near-right, far-right, far-left.
            var c0 = new Vector2(-NearHalf, -Near);
            var c1 = new Vector2(NearHalf, -Near);
            var c2 = new Vector2(FarHalf, Far);
            var c3 = new Vector2(-FarHalf, Far);
            float l0 = Vector2.Distance(c0, c1), l1 = Vector2.Distance(c1, c2), l2 = Vector2.Distance(c2, c3), l3 = Vector2.Distance(c3, c0);
            float d = (u - (float)Math.Floor(u)) * (l0 + l1 + l2 + l3);
            Vector2 a, b;
            if (d < l0) { a = c0; b = c1; }
            else if ((d -= l0) < l1) { a = c1; b = c2; }
            else if ((d -= l1) < l2) { a = c2; b = c3; }
            else { d -= l2; a = c3; b = c0; }
            Vector2 along = Vector2.Normalize(b - a);
            normal = new Vector2(along.Y, -along.X); // corners run counter-clockwise, so this points out
            Vector2 p = a + along * d;
            return focus + p + normal * margin;
        }

        /// <summary>The view's packing capacity at the roster's densest packing (performance.md: about 2.4 per m² at 0.4 m radii).</summary>
        public int PackingCapacity(float perSquareMetre = 2.4f) => (int)(Area * perSquareMetre);
    }
}
