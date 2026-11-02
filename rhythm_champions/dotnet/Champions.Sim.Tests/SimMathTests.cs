using System;
using NUnit.Framework;

namespace Champions.Sim.Tests
{
    /// <summary>
    /// <see cref="SimMath"/>, the sim's trigonometry the same in every runtime, stays within a couple of ulps of
    /// System.Math over the angles and ratios the sim meets, keeps its quadrants, and answers the special cases.
    /// </summary>
    public class SimMathTests
    {
        static void Near(double expected, double actual, string what) =>
            Assert.That(actual, Is.EqualTo(expected).Within(4e-16 * Math.Max(1, Math.Abs(expected))), what);

        [Test]
        public void SineAndCosineMatchTheRuntimesToAnUlpOrTwo()
        {
            var rng = new Random(5);
            for (int i = 0; i < 20000; i++)
            {
                double x = (rng.NextDouble() - 0.5) * (i < 10000 ? 20 : 20000);
                Near(Math.Sin(x), SimMath.Sin(x), $"sin {x:R}");
                Near(Math.Cos(x), SimMath.Cos(x), $"cos {x:R}");
            }
            Assert.AreEqual(0.0, SimMath.Sin(0.0));
            Assert.AreEqual(1.0, SimMath.Cos(0.0));
            Assert.IsNaN(SimMath.Sin(double.PositiveInfinity));
        }

        [Test]
        public void JsonNumbersReadAsDotNetReadsThemExactly()
        {
            var rng = new Random(7);
            var c = System.Globalization.CultureInfo.InvariantCulture;
            for (int i = 0; i < 50000; i++)
            {
                double x = (rng.NextDouble() - 0.5) * Math.Pow(10, rng.Next(-12, 12));
                foreach (string text in new[] { x.ToString("R", c), x.ToString("G17", c), x.ToString("E16", c), x.ToString("F6", c), x.ToString("G5", c) })
                {
                    Assert.IsTrue(ExactDouble.TryParse(text, 0, text.Length, out double v), text);
                    Assert.AreEqual(BitConverter.DoubleToInt64Bits(double.Parse(text, c)), BitConverter.DoubleToInt64Bits(v), text);
                }
            }
            foreach (string text in new[] { "0", "-0", "1", "0.1", "123456789012345678901234567890", "1e300", "2.2250738585072014e-308", "0.30000000000000004" })
            {
                Assert.IsTrue(ExactDouble.TryParse(text, 0, text.Length, out double v), text);
                Assert.AreEqual(BitConverter.DoubleToInt64Bits(double.Parse(text, c)), BitConverter.DoubleToInt64Bits(v), text);
            }
            Assert.IsFalse(ExactDouble.TryParse("1e", 0, 2, out _));
            Assert.IsFalse(ExactDouble.TryParse("-", 0, 1, out _));
        }

        [Test]
        public void ArcTangentsKeepTheirQuadrants()
        {
            var rng = new Random(6);
            for (int i = 0; i < 20000; i++)
            {
                double y = (rng.NextDouble() - 0.5) * Math.Pow(10, rng.Next(-6, 6)), x = (rng.NextDouble() - 0.5) * Math.Pow(10, rng.Next(-6, 6));
                Near(Math.Atan2(y, x), SimMath.Atan2(y, x), $"atan2 {y:R} {x:R}");
                Near(Math.Atan(y), SimMath.Atan(y), $"atan {y:R}");
            }
            for (int i = 0; i <= 200; i++)
            {
                double c = -1 + i / 100.0;
                Assert.That(SimMath.Acos(c), Is.EqualTo(Math.Acos(c)).Within(2e-15), $"acos {c}");
                Assert.That(SimMath.Asin(c), Is.EqualTo(Math.Asin(c)).Within(2e-15), $"asin {c}");
            }
            Assert.AreEqual(Math.PI / 2, SimMath.Atan2(1, 0), 1e-16);
            Assert.AreEqual(-Math.PI / 2, SimMath.Atan2(-1, 0), 1e-16);
            Assert.AreEqual(Math.PI, SimMath.Atan2(0, -1), 1e-16);
            Assert.AreEqual(0.0, SimMath.Atan2(0, 1));
        }
    }
}
