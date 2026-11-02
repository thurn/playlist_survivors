using System;

namespace Champions.Sim
{
    /// <summary>What a <see cref="HitShape"/> is.</summary>
    public enum ShapeKind : byte
    {
        /// <summary>A disc of <see cref="HitShape.Radius"/> around (X, Y): a burst, a shockwave.</summary>
        Circle = 0,
        /// <summary>The band between <see cref="HitShape.Inner"/> and <see cref="HitShape.Radius"/> around (X, Y): a rolling ring.</summary>
        Ring = 1,
        /// <summary>Everything within <see cref="HitShape.Radius"/> of the segment (X, Y)-(X2, Y2): a dash's path.</summary>
        Path = 2,
        /// <summary>A sector of <see cref="HitShape.Radius"/> around (X, Y), facing (DirX, DirY), <see cref="HitShape.HalfArc"/> either side.</summary>
        Cone = 3,
    }

    /// <summary>
    /// The ground a blow covers, in ground-plane metres. A target is reached when its body, a circle,
    /// touches the shape (<see cref="Touches"/>).
    /// </summary>
    public struct HitShape
    {
        public ShapeKind Kind;
        /// <summary>The centre; a path's start.</summary>
        public double X, Y;
        /// <summary>A path's end.</summary>
        public double X2, Y2;
        /// <summary>The outer radius; a path's reach either side of it.</summary>
        public double Radius;
        /// <summary>A ring's inner radius.</summary>
        public double Inner;
        /// <summary>A cone's facing, a unit vector, and its half angle (radians).</summary>
        public double DirX, DirY, HalfArc;

        public static HitShape Circle(double x, double y, double radius) =>
            new HitShape { Kind = ShapeKind.Circle, X = x, Y = y, Radius = radius };

        public static HitShape Ring(double x, double y, double inner, double radius) =>
            new HitShape { Kind = ShapeKind.Ring, X = x, Y = y, Inner = inner, Radius = radius };

        public static HitShape Path(double x, double y, double x2, double y2, double reach) =>
            new HitShape { Kind = ShapeKind.Path, X = x, Y = y, X2 = x2, Y2 = y2, Radius = reach };

        public static HitShape Cone(double x, double y, double dirX, double dirY, double radius, double halfArc) =>
            new HitShape { Kind = ShapeKind.Cone, X = x, Y = y, DirX = dirX, DirY = dirY, Radius = radius, HalfArc = halfArc };

        /// <summary>Does a body of radius <paramref name="body"/> at (<paramref name="x"/>, <paramref name="y"/>) touch the shape?</summary>
        public bool Touches(double x, double y, double body)
        {
            double ex = x - X, ey = y - Y;
            switch (Kind)
            {
                case ShapeKind.Circle:
                    return ex * ex + ey * ey <= Sq(Radius + body) + 1e-9;
                case ShapeKind.Ring:
                {
                    double d = Math.Sqrt(ex * ex + ey * ey);
                    return d <= Radius + body + 1e-9 && d >= Inner - body - 1e-9;
                }
                case ShapeKind.Path:
                    return DistanceToSegment(x, y, X, Y, X2, Y2) <= Radius + body + 1e-9;
                case ShapeKind.Cone:
                {
                    double d = Math.Sqrt(ex * ex + ey * ey);
                    if (d > Radius + body + 1e-9) return false;
                    if (d <= body + 1e-9) return true;
                    double cos = (ex * DirX + ey * DirY) / d;
                    double angle = Math.Acos(Math.Max(-1, Math.Min(1, cos)));
                    // The body's own half width, seen from the apex, widens the sector.
                    return angle <= HalfArc + Math.Asin(Math.Min(1, body / d)) + 1e-9;
                }
                default:
                    return false;
            }
        }

        /// <summary>
        /// Does the shape touch a rectangle (a structure's footprint) of half sizes (<paramref name="hx"/>,
        /// <paramref name="hy"/>) round (<paramref name="cx"/>, <paramref name="cy"/>), its x axis along
        /// (<paramref name="cos"/>, <paramref name="sin"/>)?
        /// </summary>
        public bool TouchesBox(double cx, double cy, double cos, double sin, double hx, double hy)
        {
            // In the box's own frame, where it is [-hx, hx] x [-hy, hy].
            ToBox(X, Y, cx, cy, cos, sin, out double px, out double py);
            switch (Kind)
            {
                case ShapeKind.Circle:
                    return BoxDistance(px, py, hx, hy) <= Radius + 1e-9;
                case ShapeKind.Ring:
                {
                    if (BoxDistance(px, py, hx, hy) > Radius + 1e-9) return false;
                    double fx = Math.Abs(px) + hx, fy = Math.Abs(py) + hy;
                    return fx * fx + fy * fy >= Sq(Inner) - 1e-9;
                }
                case ShapeKind.Path:
                {
                    ToBox(X2, Y2, cx, cy, cos, sin, out double qx, out double qy);
                    return SegmentBoxDistance(px, py, qx, qy, hx, hy) <= Radius + 1e-9;
                }
                case ShapeKind.Cone:
                {
                    if (Math.Abs(px) <= hx && Math.Abs(py) <= hy) return true;
                    // The box's nearest point to the apex inside the sector, or a side of the sector crossing the
                    // box: a convex box with any point in the sector has one or the other.
                    double nx = Math.Max(-hx, Math.Min(hx, px)), ny = Math.Max(-hy, Math.Min(hy, py));
                    FromBox(nx, ny, cos, sin, out double wx, out double wy);
                    if (Touches(cx + wx, cy + wy, 0)) return true;
                    ToBox(DirX, DirY, 0, 0, cos, sin, out double dx, out double dy);
                    for (int side = -1; side <= 1; side += 2)
                    {
                        double c = Math.Cos(side * HalfArc), s = Math.Sin(side * HalfArc);
                        double ex = px + (dx * c - dy * s) * Radius, ey = py + (dx * s + dy * c) * Radius;
                        if (SegmentBoxDistance(px, py, ex, ey, hx, hy) <= 1e-9) return true;
                    }
                    return false;
                }
                default:
                    return false;
            }
        }

        /// <summary>A point's coordinates in a box's frame, its middle (cx, cy) and its x axis (cos, sin); a vector's with the middle at 0.</summary>
        public static void ToBox(double x, double y, double cx, double cy, double cos, double sin, out double lx, out double ly)
        {
            double ox = x - cx, oy = y - cy;
            lx = ox * cos + oy * sin;
            ly = -ox * sin + oy * cos;
        }

        /// <summary>A vector in a box's frame turned back to the ground's.</summary>
        public static void FromBox(double x, double y, double cos, double sin, out double wx, out double wy)
        {
            wx = x * cos - y * sin;
            wy = x * sin + y * cos;
        }

        /// <summary>The distance from (<paramref name="x"/>, <paramref name="y"/>) to the rectangle [-hx, hx] x [-hy, hy] (0 inside).</summary>
        public static double BoxDistance(double x, double y, double hx, double hy)
        {
            double dx = Math.Max(0, Math.Abs(x) - hx), dy = Math.Max(0, Math.Abs(y) - hy);
            return Math.Sqrt(dx * dx + dy * dy);
        }

        /// <summary>The distance from the segment (ax, ay)-(bx, by) to the rectangle [-hx, hx] x [-hy, hy] (0 when they meet).</summary>
        public static double SegmentBoxDistance(double ax, double ay, double bx, double by, double hx, double hy)
        {
            // Clipped against the slabs: a piece left inside means they meet.
            double t0 = 0, t1 = 1, dx = bx - ax, dy = by - ay;
            if (Clip(-dx, ax + hx, ref t0, ref t1) && Clip(dx, hx - ax, ref t0, ref t1) &&
                Clip(-dy, ay + hy, ref t0, ref t1) && Clip(dy, hy - ay, ref t0, ref t1))
                return 0;
            // Apart, two convex shapes are nearest at a corner of one.
            double d = Math.Min(BoxDistance(ax, ay, hx, hy), BoxDistance(bx, by, hx, hy));
            for (int i = 0; i < 4; i++)
                d = Math.Min(d, DistanceToSegment((i & 1) == 0 ? -hx : hx, (i & 2) == 0 ? -hy : hy, ax, ay, bx, by));
            return d;
        }

        /// <summary>Liang-Barsky's step: keeps the part of [t0, t1] where p t &lt;= q; false when none is left.</summary>
        static bool Clip(double p, double q, ref double t0, ref double t1)
        {
            if (Math.Abs(p) < 1e-15) return q >= 0;
            double r = q / p;
            if (p < 0)
            {
                if (r > t1) return false;
                if (r > t0) t0 = r;
            }
            else
            {
                if (r < t0) return false;
                if (r < t1) t1 = r;
            }
            return true;
        }

        /// <summary>The middle of the shape and a radius around it holding all of it.</summary>
        public void Bounds(out double cx, out double cy, out double radius)
        {
            if (Kind != ShapeKind.Path)
            {
                cx = X;
                cy = Y;
                radius = Radius;
                return;
            }
            cx = 0.5 * (X + X2);
            cy = 0.5 * (Y + Y2);
            radius = 0.5 * Math.Sqrt((X2 - X) * (X2 - X) + (Y2 - Y) * (Y2 - Y)) + Radius;
        }

        /// <summary>
        /// The way the shape throws a body at (<paramref name="x"/>, <paramref name="y"/>), a unit vector: square off
        /// a path's line (along it when on the line), else away from the centre (a cone's facing, or +x, at the
        /// centre). A path is swept a tick at a time, so its throw follows the line, not the tick's piece of it.
        /// </summary>
        public void Away(double x, double y, out double dx, out double dy)
        {
            double fx = Kind == ShapeKind.Cone ? DirX : 1, fy = Kind == ShapeKind.Cone ? DirY : 0, ox = x - X, oy = y - Y;
            if (Kind == ShapeKind.Path)
            {
                double sx = X2 - X, sy = Y2 - Y, len = Math.Sqrt(sx * sx + sy * sy);
                if (len >= 1e-9)
                {
                    fx = sx / len;
                    fy = sy / len;
                    double along = ox * fx + oy * fy;
                    ox -= fx * along;
                    oy -= fy * along;
                }
            }
            double d = Math.Sqrt(ox * ox + oy * oy);
            dx = d > 1e-9 ? ox / d : fx;
            dy = d > 1e-9 ? oy / d : fy;
        }

        /// <summary>The shape grown by <paramref name="factor"/>: its radii and reach (an on-beat dash's 1.5x area, "half again as wide").</summary>
        public HitShape Scaled(double factor)
        {
            var s = this;
            s.Radius *= factor;
            s.Inner *= factor;
            return s;
        }

        /// <summary>The distance from (<paramref name="x"/>, <paramref name="y"/>) to the segment (ax, ay)-(bx, by).</summary>
        public static double DistanceToSegment(double x, double y, double ax, double ay, double bx, double by)
        {
            double sx = bx - ax, sy = by - ay, len2 = sx * sx + sy * sy;
            double u = len2 < 1e-18 ? 0 : Math.Max(0, Math.Min(1, ((x - ax) * sx + (y - ay) * sy) / len2));
            double dx = x - (ax + sx * u), dy = y - (ay + sy * u);
            return Math.Sqrt(dx * dx + dy * dy);
        }

        static double Sq(double v) => v * v;
    }
}
