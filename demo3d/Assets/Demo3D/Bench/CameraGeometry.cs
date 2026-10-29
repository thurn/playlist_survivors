using System;

namespace Demo3D.Bench
{
    /// <summary>
    /// The game camera's geometry (camera-and-sound.md, Camera): 55° pitch, 30° vertical field of
    /// view, 16:9, with the distance set by the view width, the ground's width visible across the
    /// middle of the screen. The camera looks at a target on the ground (y = 0) from -z.
    /// </summary>
    public static class CameraGeometry
    {
        public const double PitchDeg = 55;
        public const double VFovDeg = 30;
        public const double Aspect = 16.0 / 9.0;

        const double Deg = Math.PI / 180;

        public static double HFovDeg => 2 * Math.Atan(Math.Tan(VFovDeg / 2 * Deg) * Aspect) / Deg;

        /// <summary>The distance from the camera to its ground target along the view axis.</summary>
        public static double Distance(double viewWidthM) => viewWidthM / (2 * Math.Tan(HFovDeg / 2 * Deg));

        public static double Height(double viewWidthM) => Distance(viewWidthM) * Math.Sin(PitchDeg * Deg);

        /// <summary>The camera's position relative to its target: (0, height, -distance cos pitch).</summary>
        public static void Position(double viewWidthM, out double y, out double z)
        {
            y = Height(viewWidthM);
            z = -Distance(viewWidthM) * Math.Cos(PitchDeg * Deg);
        }

        /// <summary>
        /// The visible ground: the four corners where the frustum's corner rays meet y = 0, as
        /// (x, z) pairs relative to the target, in the order bottom-left, bottom-right, top-right,
        /// top-left.
        /// </summary>
        public static double[] GroundFootprint(double viewWidthM)
        {
            Position(viewWidthM, out var cy, out var cz);
            double p = PitchDeg * Deg;
            double ty = Math.Tan(VFovDeg / 2 * Deg), tx = ty * Aspect;
            // forward (0, -sin p, cos p), up (0, cos p, sin p), right (1, 0, 0)
            double fy = -Math.Sin(p), fz = Math.Cos(p), uy = Math.Cos(p), uz = Math.Sin(p);
            var corners = new[] { (-1, -1), (1, -1), (1, 1), (-1, 1) };
            var result = new double[8];
            for (int i = 0; i < 4; i++)
            {
                double dx = corners[i].Item1 * tx;
                double dy = fy + corners[i].Item2 * ty * uy;
                double dz = fz + corners[i].Item2 * ty * uz;
                if (dy >= 0) throw new InvalidOperationException("CameraGeometry: a corner ray misses the ground");
                double t = -cy / dy;
                result[2 * i] = dx * t;
                result[2 * i + 1] = cz + dz * t;
            }
            return result;
        }

        /// <summary>The visible ground's area in m² (the footprint's shoelace area).</summary>
        public static double VisibleGroundM2(double viewWidthM)
        {
            var c = GroundFootprint(viewWidthM);
            double area = 0;
            for (int i = 0; i < 4; i++)
            {
                int j = (i + 1) % 4;
                area += c[2 * i] * c[2 * j + 1] - c[2 * j] * c[2 * i + 1];
            }
            return Math.Abs(area) / 2;
        }

        /// <summary>The view's packing capacity: its visible ground times the densest packing.</summary>
        public static int PackingCapacity(double viewWidthM, double perM2) =>
            (int)Math.Floor(VisibleGroundM2(viewWidthM) * perM2);
    }
}
