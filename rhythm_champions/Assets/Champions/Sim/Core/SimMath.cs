using System;

namespace Champions.Sim
{
    /// <summary>
    /// The sim's trigonometry, the same to the last bit in every runtime (plan.md, Sessions and replay: a replay settles the
    /// same hash under dotnet and under Unity). System.Math's sine, cosine and arc tangents come from each runtime's own
    /// math library, which round differently in the last place (ps-6y2.21: the duel fixture replayed under Mono settled
    /// another hash). These use only IEEE-exact operations (+, -, *, / and the square root), so every runtime computes the
    /// same doubles. They are fdlibm's kernels, ported:
    ///
    /// Copyright (C) 1993 by Sun Microsystems, Inc. All rights reserved.
    /// Developed at SunPro, a Sun Microsystems, Inc. business.
    /// Permission to use, copy, modify, and distribute this software is freely granted, provided that this notice is preserved.
    ///
    /// Accurate to within about an ulp for angles up to a few million radians (the sim's are small, or a steady turn's).
    /// </summary>
    public static class SimMath
    {
        // pi/2 in three parts, each exact in a product with a reduction count below 2^20 (fdlibm's e_rem_pio2.c).
        const double InvPio2 = 6.36619772367581382433e-01;
        const double Pio2_1 = 1.57079632673412561417e+00;
        const double Pio2_2 = 6.07710050630396597660e-11;
        const double Pio2_3 = 2.02226624871116645580e-21;

        const double S1 = -1.66666666666666324348e-01, S2 = 8.33333333332248946124e-03, S3 = -1.98412698298579493134e-04,
            S4 = 2.75573137070700676789e-06, S5 = -2.50507602534068634195e-08, S6 = 1.58969099521155010221e-10;

        const double C1 = 4.16666666666666019037e-02, C2 = -1.38888888888741095749e-03, C3 = 2.48015872894767294178e-05,
            C4 = -2.75573143513906633035e-07, C5 = 2.08757232129817482790e-09, C6 = -1.13596475577881948265e-11;

        static readonly double[] AtanHi = { 4.63647609000806093515e-01, 7.85398163397448278999e-01, 9.82793723247329054082e-01, 1.57079632679489655800e+00 };
        static readonly double[] AtanLo = { 2.26987774529616870924e-17, 3.06161699786838301793e-17, 1.39033110312309984516e-17, 6.12323399573676603587e-17 };

        const double AT0 = 3.33333333333329318027e-01, AT1 = -1.99999999998764832476e-01, AT2 = 1.42857142725034663711e-01,
            AT3 = -1.11111104054623557880e-01, AT4 = 9.09088713343650656196e-02, AT5 = -7.69187620504482999495e-02,
            AT6 = 6.66107313738753120669e-02, AT7 = -5.83357013379057348645e-02, AT8 = 4.97687799461593236017e-02,
            AT9 = -3.65315727442169155270e-02, AT10 = 1.62858201153657823623e-02;

        const double Pi = 3.1415926535897931160e+00, PiLo = 1.2246467991473531772e-16, Pio2Hi = 1.57079632679489655800e+00;

        public static double Sin(double x)
        {
            int n = Reduce(x, out double y);
            switch (n & 3)
            {
                case 0: return KernelSin(y);
                case 1: return KernelCos(y);
                case 2: return -KernelSin(y);
                default: return -KernelCos(y);
            }
        }

        public static double Cos(double x)
        {
            int n = Reduce(x, out double y);
            switch (n & 3)
            {
                case 0: return KernelCos(y);
                case 1: return -KernelSin(y);
                case 2: return -KernelCos(y);
                default: return KernelSin(y);
            }
        }

        /// <summary>x less the nearest multiple n of pi/2, into [-pi/4, pi/4] (Cody and Waite's three-part reduction): n.</summary>
        static int Reduce(double x, out double y)
        {
            if (double.IsNaN(x) || double.IsInfinity(x))
            {
                y = double.NaN;
                return 0;
            }
            if (Math.Abs(x) <= 7.85398163397448278999e-01)
            {
                y = x;
                return 0;
            }
            double fn = Math.Round(x * InvPio2);
            y = x - fn * Pio2_1 - fn * Pio2_2 - fn * Pio2_3;
            return (int)(long)fn;
        }

        static double KernelSin(double x)
        {
            double z = x * x, v = z * x;
            double r = S2 + z * (S3 + z * (S4 + z * (S5 + z * S6)));
            return x + v * (S1 + z * r);
        }

        static double KernelCos(double x)
        {
            double z = x * x;
            double r = z * (C1 + z * (C2 + z * (C3 + z * (C4 + z * (C5 + z * C6)))));
            double hz = 0.5 * z, w = 1.0 - hz;
            return w + (((1.0 - w) - hz) + z * r);
        }

        public static double Atan(double x)
        {
            if (double.IsNaN(x)) return x;
            bool negative = x < 0;
            double ax = Math.Abs(x);
            if (ax >= 7.3786976294838206464e19) return x > 0 ? AtanHi[3] + AtanLo[3] : -AtanHi[3] - AtanLo[3]; // 2^66
            int id;
            if (ax < 0.4375)
            {
                if (ax < 1.862645149230957e-09) return x; // 2^-29
                id = -1;
            }
            else
            {
                x = ax;
                if (ax < 1.1875)
                {
                    if (ax < 0.6875)
                    {
                        id = 0;
                        x = (2.0 * x - 1.0) / (2.0 + x);
                    }
                    else
                    {
                        id = 1;
                        x = (x - 1.0) / (x + 1.0);
                    }
                }
                else if (ax < 2.4375)
                {
                    id = 2;
                    x = (x - 1.5) / (1.0 + 1.5 * x);
                }
                else
                {
                    id = 3;
                    x = -1.0 / x;
                }
            }
            double z = x * x, w = z * z;
            double s1 = z * (AT0 + w * (AT2 + w * (AT4 + w * (AT6 + w * (AT8 + w * AT10)))));
            double s2 = w * (AT1 + w * (AT3 + w * (AT5 + w * (AT7 + w * AT9))));
            if (id < 0) return x - x * (s1 + s2);
            z = AtanHi[id] - ((x * (s1 + s2) - AtanLo[id]) - x);
            return negative ? -z : z;
        }

        /// <summary>The arc tangent of y/x in (-pi, pi], by the signs of both (fdlibm's e_atan2.c).</summary>
        public static double Atan2(double y, double x)
        {
            if (double.IsNaN(x) || double.IsNaN(y)) return double.NaN;
            if (x == 1.0) return Atan(y);
            bool yNegative = y < 0 || y == 0 && double.IsNegative(y);
            bool xNegative = x < 0 || x == 0 && double.IsNegative(x);
            int m = (yNegative ? 1 : 0) | (xNegative ? 2 : 0);
            if (y == 0)
            {
                switch (m)
                {
                    case 0:
                    case 1: return y;
                    case 2: return Pi + PiLo;
                    default: return -Pi - PiLo;
                }
            }
            if (x == 0) return yNegative ? -Pio2Hi - 6.12323399573676603587e-17 : Pio2Hi + 6.12323399573676603587e-17;
            if (double.IsInfinity(x))
            {
                if (double.IsInfinity(y))
                {
                    switch (m)
                    {
                        case 0: return Pi / 4;
                        case 1: return -Pi / 4;
                        case 2: return 3 * Pi / 4;
                        default: return -3 * Pi / 4;
                    }
                }
                switch (m)
                {
                    case 0: return 0.0;
                    case 1: return -0.0;
                    case 2: return Pi + PiLo;
                    default: return -Pi - PiLo;
                }
            }
            if (double.IsInfinity(y)) return yNegative ? -Pio2Hi - 6.12323399573676603587e-17 : Pio2Hi + 6.12323399573676603587e-17;
            double ratio = Math.Abs(y / x);
            double z;
            if (ratio > 1.152921504606846976e18) z = Pio2Hi + 0.5 * PiLo; // |y/x| > 2^60
            else if (xNegative && ratio < 8.673617379884035e-19) z = 0.0;  // |y/x| < 2^-60
            else z = Atan(ratio);
            switch (m)
            {
                case 0: return z;
                case 1: return -z;
                case 2: return Pi - (z - PiLo);
                default: return (z - PiLo) - Pi;
            }
        }

        /// <summary>The arc sine, through <see cref="Atan2"/>.</summary>
        public static double Asin(double x)
        {
            if (x > 1 || x < -1) return double.NaN;
            return Atan2(x, Math.Sqrt((1 - x) * (1 + x)));
        }

        /// <summary>The arc cosine, through <see cref="Atan2"/>.</summary>
        public static double Acos(double x)
        {
            if (x > 1 || x < -1) return double.NaN;
            return Atan2(Math.Sqrt((1 - x) * (1 + x)), x);
        }
    }
}
