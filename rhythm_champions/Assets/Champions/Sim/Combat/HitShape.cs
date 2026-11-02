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
