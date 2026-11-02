using System;
using System.Collections.Generic;

namespace Champions.Sim.Tests
{
    /// <summary>Standing targets for the hero's blows: points with a body, recording every strike.</summary>
    internal sealed class Dummies : IStrikeTargets
    {
        public readonly List<(double X, double Y)> At = new List<(double, double)>();
        public readonly List<(int Id, Blow Blow)> Struck = new List<(int, Blow)>();
        public double Body;

        public int Add(double x, double y)
        {
            At.Add((x, y));
            return At.Count - 1;
        }

        public void Reach(in HitShape shape, double t, StructList<int> into)
        {
            for (int i = 0; i < At.Count; i++)
                if (shape.Touches(At[i].X, At[i].Y, Body)) into.Add(i);
        }

        public bool Strike(Simulation sim, int id, in Blow blow)
        {
            Struck.Add((id, blow));
            return false;
        }

        /// <summary>The blows target <paramref name="id"/> took, in order.</summary>
        public List<Blow> On(int id) => Struck.FindAll(s => s.Id == id).ConvertAll(s => s.Blow);
    }

    /// <summary>
    /// Axis-aligned walls that block a body as a box of its radius (each axis in turn, so a blocked move
    /// slides along the other) and break after their count of hits.
    /// </summary>
    internal sealed class Walls : IStructures
    {
        public sealed class Wall
        {
            public double X0, Y0, X1, Y1;
            public int Hits;
            public bool Broken;
            public readonly List<Blow> Struck = new List<Blow>();
        }

        public readonly List<Wall> All = new List<Wall>();

        public Wall Add(double x0, double y0, double x1, double y1, int hits)
        {
            var w = new Wall { X0 = x0, Y0 = y0, X1 = x1, Y1 = y1, Hits = hits };
            All.Add(w);
            return w;
        }

        public void Move(double x, double y, double radius, double dx, double dy, out double nx, out double ny, out int blocker)
        {
            blocker = -1;
            nx = Axis(x, y, radius, dx, true, ref blocker);
            ny = Axis(y, nx, radius, dy, false, ref blocker);
        }

        double Axis(double p, double q, double r, double d, bool alongX, ref int blocker)
        {
            if (d == 0) return p;
            double result = p + d;
            for (int i = 0; i < All.Count; i++)
            {
                var w = All[i];
                if (w.Broken) continue;
                double lo = alongX ? w.Y0 : w.X0, hi = alongX ? w.Y1 : w.X1;
                if (q + r <= lo + 1e-9 || q - r >= hi - 1e-9) continue;
                double a0 = alongX ? w.X0 : w.Y0, a1 = alongX ? w.X1 : w.Y1;
                if (d > 0 && p + r <= a0 + 1e-9 && p + d + r > a0 && a0 - r < result)
                {
                    result = a0 - r;
                    blocker = i;
                }
                if (d < 0 && p - r >= a1 - 1e-9 && p + d - r < a1 && a1 + r > result)
                {
                    result = a1 + r;
                    blocker = i;
                }
            }
            return result;
        }

        public void Reach(in HitShape shape, double t, StructList<int> into)
        {
            for (int i = 0; i < All.Count; i++)
            {
                var w = All[i];
                if (w.Broken) continue;
                double cx = (w.X0 + w.X1) / 2, cy = (w.Y0 + w.Y1) / 2;
                if (shape.Touches(cx, cy, Math.Min(w.X1 - w.X0, w.Y1 - w.Y0) / 2)) into.Add(i);
            }
        }

        public bool Strike(Simulation sim, int id, in Blow blow)
        {
            var w = All[id];
            w.Struck.Add(blow);
            if (--w.Hits <= 0 || blow.Shatter) w.Broken = true;
            return w.Broken;
        }
    }
}
