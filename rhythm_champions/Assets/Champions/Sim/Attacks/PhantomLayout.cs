using System;

namespace Champions.Sim
{
    /// <summary>Where the phantoms stand (<see cref="PhantomRules.OffsetM"/>): evenly around the hero from its left.</summary>
    internal static class PhantomLayout
    {
        public const int Max = 3;

        /// <summary>Phantom <paramref name="k"/> of <paramref name="n"/>'s offset from a hero facing (<paramref name="fx"/>, <paramref name="fy"/>).</summary>
        public static void Offset(int k, int n, double fx, double fy, double offsetM, out double dx, out double dy)
        {
            double a = 0.5 * Math.PI + 2 * Math.PI * k / n;
            double c = SimMath.Cos(a), s = SimMath.Sin(a);
            dx = (fx * c - fy * s) * offsetM;
            dy = (fx * s + fy * c) * offsetM;
        }
    }
}
