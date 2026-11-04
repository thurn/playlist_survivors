using System;
using System.Collections.Generic;

namespace Champions.Sim
{
    /// <summary>The game camera's fixed shape (the client's WorldCamera): pitched down, never turning, looking up the screen (+y on the ground).</summary>
    public struct CameraModel
    {
        public double PitchDeg, FovDeg, Aspect, DistanceM;
    }

    /// <summary>Where the camera looks for an arena and from how far; whether it scrolls with the hero.</summary>
    public struct ArenaFrame
    {
        public double FocusX, FocusY, Distance;
        public bool Scrolls;
    }

    /// <summary>
    /// The arena's camera framing (boss-rush.md, Arenas: "the camera frames the whole arena where it can; a larger arena may
    /// scroll, but the boss and every live telegraph stay in frame"). Pure, for the client's camera and the sim's tests.
    ///
    /// With the camera's pitch p and half view angle h (tan h = T, aspect A), a ground point (dx, dy) off the focus at
    /// distance d is in frame when |dy sin p| ≤ (d + dy cos p) T and |dx| ≤ (d + dy cos p) T A: linear in the focus and the
    /// distance, so the frame that holds a set of points is found by bisecting the distance.
    /// - <b>Fixed</b>: the whole outline (and <see cref="ArenaCamera.MarginM"/> round it) fits within
    ///   <see cref="ArenaCamera.MaxScale"/> of the normal distance: the view holds still on it, never nearer than normal.
    /// - <b>Scrolls</b>: at the farthest it may be, the view follows the hero's focus, kept from looking past the walls where
    ///   the arena is wider than the view, and always holding the hero, the boss and the live telegraphs (eased out
    ///   farther still if they need it).
    /// </summary>
    public sealed class ArenaFraming
    {
        const int Samples = 64;
        const int Bisections = 40;

        readonly CameraModel cam;
        readonly double sinP, cosP, t, near, far;
        readonly List<double> xs = new List<double>(256), ys = new List<double>(256);

        public ArenaFraming(CameraModel camera)
        {
            cam = camera;
            double p = camera.PitchDeg * Math.PI / 180;
            sinP = Math.Sin(p);
            cosP = Math.Cos(p);
            t = Math.Tan(camera.FovDeg * Math.PI / 360);
            if (!(sinP > t * cosP)) throw new ArgumentException("the camera must look down more steeply than half its view angle");
            // How far ahead of and behind the focus the frame reaches on the ground, per metre of distance.
            far = t / (sinP - t * cosP);
            near = t / (sinP + t * cosP);
        }

        /// <summary>Is ground point (<paramref name="px"/>, <paramref name="py"/>) in the frame of a view on (<paramref name="fx"/>, <paramref name="fy"/>) from <paramref name="d"/>?</summary>
        public bool InFrame(double fx, double fy, double d, double px, double py)
        {
            double dx = px - fx, dy = py - fy, depth = d + dy * cosP;
            return depth > 0 && Math.Abs(dy * sinP) <= depth * t + 1e-9 && Math.Abs(dx) <= depth * t * cam.Aspect + 1e-9;
        }

        /// <summary>
        /// The frame for <paramref name="arena"/> (its definition placed where it stands, forming or retracting), the view
        /// wanting to look at (<paramref name="wantX"/>, <paramref name="wantY"/>) (the hero's feet plus the lead) and having
        /// to hold every circle of <paramref name="keep"/> (x, y, radius: the hero, the boss, live telegraphs).
        /// </summary>
        public ArenaFrame Frame(BossArena arena, double wantX, double wantY, IReadOnlyList<(double X, double Y, double R)> keep)
        {
            var def = arena.Def ?? throw new InvalidOperationException("no arena to frame");
            double margin = def.Camera.MarginM, normal = cam.DistanceM, most = normal * def.Camera.MaxScale;
            xs.Clear();
            ys.Clear();
            var o = def.Outline;
            for (int i = 0; i < o.Count; i++) Square(arena.OriginX + o.X[i], arena.OriginY + o.Y[i], margin);
            double fit = Fitting(normal);
            if (fit <= most)
            {
                double d = Math.Max(normal, fit);
                Centre(d, out double fx, out double fy);
                return new ArenaFrame { FocusX = fx, FocusY = fy, Distance = d };
            }
            o.Bounds(out double x0, out double y0, out double x1, out double y1);
            x0 += arena.OriginX - margin;
            x1 += arena.OriginX + margin;
            y0 += arena.OriginY - margin;
            y1 += arena.OriginY + margin;
            xs.Clear();
            ys.Clear();
            foreach (var k in keep) Square(k.X, k.Y, k.R);
            double dist = xs.Count == 0 ? most : Math.Max(most, Fitting(most));
            // Short of the walls where the arena is wider than the view, centred on it where it isn't.
            double hw = dist * t * cam.Aspect;
            double fxw = Clamp(wantX, x0 + hw, x1 - hw), fyw = Clamp(wantY, y0 + near * dist, y1 - far * dist);
            if (xs.Count > 0 && Feasible(dist, out double lo, out double hi))
            {
                fyw = Clamp(fyw, lo, hi);
                XRange(dist, fyw, out double xlo, out double xhi);
                fxw = Clamp(fxw, xlo, xhi);
            }
            return new ArenaFrame { FocusX = fxw, FocusY = fyw, Distance = dist, Scrolls = true };
        }

        /// <summary>A circle's square of points: every point of it in frame keeps it there (the frame is convex).</summary>
        void Square(double x, double y, double r)
        {
            for (int i = 0; i < 4; i++)
            {
                xs.Add(x + ((i & 1) == 0 ? -r : r));
                ys.Add(y + ((i & 2) == 0 ? -r : r));
            }
        }

        static double Clamp(double v, double lo, double hi) => lo > hi ? (lo + hi) / 2 : Math.Max(lo, Math.Min(hi, v));

        /// <summary>The least distance from which some focus holds every point, searched from <paramref name="from"/> out.</summary>
        double Fitting(double from)
        {
            double lo = 0, hi = Math.Max(from, 1);
            while (!Feasible(hi, out _, out _)) hi *= 2;
            for (int i = 0; i < Bisections; i++)
            {
                double mid = (lo + hi) / 2;
                if (Feasible(mid, out _, out _)) hi = mid;
                else lo = mid;
            }
            return hi;
        }

        /// <summary>The focus that holds every point from <paramref name="d"/> with the most room: the middle of the feasible ground.</summary>
        void Centre(double d, out double fx, out double fy)
        {
            if (!Feasible(d, out double lo, out double hi)) throw new InvalidOperationException("the points don't fit from there");
            fy = (lo + hi) / 2;
            XRange(d, fy, out double xlo, out double xhi);
            fx = (xlo + xhi) / 2;
        }

        /// <summary>The foci (along y) from which every point is in frame at <paramref name="d"/>, as an interval; false if none.</summary>
        bool Feasible(double d, out double lo, out double hi)
        {
            double ymin = double.PositiveInfinity, ymax = double.NegativeInfinity;
            for (int i = 0; i < ys.Count; i++)
            {
                ymin = Math.Min(ymin, ys[i]);
                ymax = Math.Max(ymax, ys[i]);
            }
            // Up the screen a point may be far * d ahead of the focus, down it near * d behind.
            double zlo = ymax - far * d, zhi = ymin + near * d;
            lo = hi = double.NaN;
            if (zlo > zhi + 1e-9) return false;
            // The sideways room shrinks as the focus moves up the screen, so the feasible foci are an interval.
            int first = -1, last = -1;
            for (int s = 0; s <= Samples; s++)
            {
                double fy = zlo + (zhi - zlo) * s / Samples;
                XRange(d, fy, out double xlo, out double xhi);
                if (xlo > xhi + 1e-9) continue;
                if (first < 0) first = s;
                last = s;
            }
            if (first < 0) return false;
            lo = zlo + (zhi - zlo) * first / Samples;
            hi = zlo + (zhi - zlo) * last / Samples;
            return true;
        }

        /// <summary>The foci (along x) holding every point sideways, the focus at <paramref name="fy"/> up the screen.</summary>
        void XRange(double d, double fy, out double lo, out double hi)
        {
            lo = double.NegativeInfinity;
            hi = double.PositiveInfinity;
            for (int i = 0; i < xs.Count; i++)
            {
                double w = (d + (ys[i] - fy) * cosP) * t * cam.Aspect;
                lo = Math.Max(lo, xs[i] - w);
                hi = Math.Min(hi, xs[i] + w);
            }
        }
    }
}
