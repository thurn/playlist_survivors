using System;
using NUnit.Framework;

namespace Champions.Sim.Tests
{
    public class HitShapeQueryTests
    {
        [Test]
        public void PreparedConesKeepBodyAndAngularBoundaryVerdicts()
        {
            var random = new Random(9173);
            foreach (double arc in new[] { 0, .2, Math.PI * .42, Math.PI / 2 - 1e-9, Math.PI / 2, 2.0, Math.PI })
            foreach (double body in new[] { .001, .4, .9, 3.5 })
            foreach (double distance in new[] { 0, .001, .4, 1, 3, 5, 8 })
            {
                double heading = random.NextDouble() * Math.PI * 2;
                var shape = HitShape.Cone(-3.7, 2.1, SimMath.Cos(heading), SimMath.Sin(heading), 5, arc);
                var query = new HitShape.ConeQuery(shape);
                double edge = arc + SimMath.Asin(Math.Min(1, body / Math.Max(distance, 1e-30))) + 1e-9;
                foreach (double offset in new[] { 0, edge, -edge, edge - 1e-12, edge + 1e-12,
                    edge - 1e-8, edge + 1e-8, Math.PI, random.NextDouble() * Math.PI * 2 })
                {
                    double x = shape.X + distance * SimMath.Cos(heading + offset);
                    double y = shape.Y + distance * SimMath.Sin(heading + offset);
                    Assert.That(query.Touches(x, y, body), Is.EqualTo(shape.Touches(x, y, body)),
                        $"arc={arc}, body={body}, distance={distance}, offset={offset}");
                }
                // The radial edge has its own tolerance, independent of angular preparation.
                foreach (double delta in new[] { -1e-8, 0, 1e-9, 1.01e-9, 1e-8 })
                {
                    double x = shape.X + (shape.Radius + body + delta) * shape.DirX;
                    double y = shape.Y + (shape.Radius + body + delta) * shape.DirY;
                    Assert.That(query.Touches(x, y, body), Is.EqualTo(shape.Touches(x, y, body)));
                }
            }
        }
    }
}
