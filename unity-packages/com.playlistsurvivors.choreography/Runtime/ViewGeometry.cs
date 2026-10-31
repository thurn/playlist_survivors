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
    /// rectangle, the view width by the width over the aspect. A heading (<see cref="YawDeg"/>, the
    /// trailer's camera turn about the vertical, trailer.md "Camera") turns the trapezoid about the
    /// focus: the camera then looks along ground <see cref="Easing.Dir"/>(yaw) instead of +Y.
    /// </summary>
    public readonly struct ViewGeometry
    {
        public const double PitchDeg = 55, TopDownPitchDeg = 90, VerticalFovDeg = 30, Aspect = 16.0 / 9.0;

        public readonly float Width;
        /// <summary>Distances from the focus to the near and far edges, along ground Y.</summary>
        public readonly float Near, Far;
        /// <summary>Half widths of the near and far edges.</summary>
        public readonly float NearHalf, FarHalf;
        /// <summary>The camera's heading in degrees, clockwise seen from above (0: looking along ground +Y).</summary>
        public readonly float YawDeg;
        readonly float cos, sin;   // the heading's; unused at 0, so a view without one computes exactly as before

        public ViewGeometry(float width, double pitchDeg = PitchDeg, float yawDeg = 0)
        {
            Width = width;
            YawDeg = yawDeg;
            cos = (float)Math.Cos(yawDeg * Math.PI / 180);
            sin = (float)Math.Sin(yawDeg * Math.PI / 180);
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

        /// <summary>The visible ground's perimeter in m, the length <see cref="EdgePoint"/>'s fraction runs along.</summary>
        public float Perimeter => 2 * (NearHalf + FarHalf) + 2 * (float)Math.Sqrt((FarHalf - NearHalf) * (FarHalf - NearHalf) + (Near + Far) * (Near + Far));

        /// <summary>The farthest visible ground from the focus.</summary>
        public float Radius => (float)Math.Max(Math.Sqrt(Far * Far + FarHalf * FarHalf), Math.Sqrt(Near * Near + NearHalf * NearHalf));

        /// <summary>Half width of the visible ground at ground offset <paramref name="dy"/> from the focus.</summary>
        public float HalfWidthAt(float dy) => NearHalf + (FarHalf - NearHalf) * (dy + Near) / (Near + Far);

        /// <summary><paramref name="p"/> relative to the focus in the view's own axes: x to the right, y ahead.</summary>
        public Vector2 Local(Vector2 focus, Vector2 p)
        {
            Vector2 d = p - focus;
            if (YawDeg == 0) return d;
            return new Vector2(d.X * cos - d.Y * sin, d.X * sin + d.Y * cos);
        }

        /// <summary>A vector in the view's own axes (x right, y ahead) on the ground.</summary>
        public Vector2 ToWorld(Vector2 v) => YawDeg == 0 ? v : new Vector2(v.X * cos + v.Y * sin, -v.X * sin + v.Y * cos);

        /// <summary>Is <paramref name="p"/> visible, with the view grown by <paramref name="margin"/> metres on every side?</summary>
        public bool Contains(Vector2 focus, Vector2 p, float margin = 0f)
        {
            Vector2 d = Local(focus, p);
            float dy = d.Y;
            if (dy < -Near - margin || dy > Far + margin) return false;
            float dyc = Math.Max(-Near, Math.Min(Far, dy));
            return Math.Abs(d.X) <= HalfWidthAt(dyc) + margin;
        }

        /// <summary>How far outside the view <paramref name="p"/> lies (0 inside): the margin that would just contain it.</summary>
        public float OutsideDistance(Vector2 focus, Vector2 p)
        {
            Vector2 d = Local(focus, p);
            float dy = d.Y;
            float outY = Math.Max(0f, Math.Max(-Near - dy, dy - Far));
            float dyc = Math.Max(-Near, Math.Min(Far, dy));
            float outX = Math.Max(0f, Math.Abs(d.X) - HalfWidthAt(dyc));
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
            if (YawDeg == 0) return focus + p + normal * margin;
            normal = ToWorld(normal);
            return focus + ToWorld(p) + normal * margin;
        }

        /// <summary>The view's packing capacity at the roster's densest packing (performance.md: about 2.4 per m² at 0.4 m radii).</summary>
        public int PackingCapacity(float perSquareMetre = 2.4f) => (int)(Area * perSquareMetre);
    }
}
