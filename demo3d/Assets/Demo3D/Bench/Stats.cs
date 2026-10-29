using System;
using System.Collections.Generic;

namespace Demo3D.Bench
{
    public sealed class Summary
    {
        public int Count;
        public double Mean, P50, P99, Max;
    }

    public static class Stats
    {
        /// <summary>Nearest-rank percentile of an ascending list (q in 0..1).</summary>
        public static double Percentile(IReadOnlyList<double> sorted, double q)
        {
            if (sorted.Count == 0) return double.NaN;
            int idx = (int)Math.Ceiling(q * sorted.Count) - 1;
            return sorted[Math.Max(0, Math.Min(sorted.Count - 1, idx))];
        }

        public static Summary Summarize(IEnumerable<double> values)
        {
            var v = new List<double>(values);
            v.Sort();
            var s = new Summary { Count = v.Count };
            if (v.Count == 0)
            {
                s.Mean = s.P50 = s.P99 = s.Max = double.NaN;
                return s;
            }
            double sum = 0;
            foreach (var x in v) sum += x;
            s.Mean = sum / v.Count;
            s.P50 = Percentile(v, 0.5);
            s.P99 = Percentile(v, 0.99);
            s.Max = v[v.Count - 1];
            return s;
        }

        /// <summary>Least-squares line y = a + b x. False with fewer than two distinct x.</summary>
        public static bool FitLine(IReadOnlyList<double> x, IReadOnlyList<double> y, out double a, out double b)
        {
            a = b = 0;
            int n = x.Count;
            if (n < 2) return false;
            double mx = 0, my = 0;
            for (int i = 0; i < n; i++) { mx += x[i]; my += y[i]; }
            mx /= n;
            my /= n;
            double sxx = 0, sxy = 0;
            for (int i = 0; i < n; i++)
            {
                sxx += (x[i] - mx) * (x[i] - mx);
                sxy += (x[i] - mx) * (y[i] - my);
            }
            if (sxx <= 0) return false;
            b = sxy / sxx;
            a = my - b * mx;
            return true;
        }

        /// <summary>
        /// Other processes' share of total CPU between two samples, in percent: system busy time
        /// (kernel + user - idle, across all cores) minus our own process's, over total time.
        /// </summary>
        public static double OtherCpuPct(long idle0, long kernel0, long user0, long own0,
                                         long idle1, long kernel1, long user1, long own1)
        {
            long total = (kernel1 - kernel0) + (user1 - user0); // kernel time includes idle
            if (total <= 0) return 0;
            long busy = total - (idle1 - idle0);
            long other = busy - (own1 - own0);
            return Math.Max(0, 100.0 * other / total);
        }
    }

    /// <summary>One run's summary as the search and the pass rule see it.</summary>
    public sealed class RunResult
    {
        public int N;
        public double GpuP99, MainP99, RenderP99;
        public double GpuMax, MainMax, RenderMax;
        /// <summary>Null where the scene has no effect system yet (a null counts as passing).</summary>
        public int? SpawnsRejected;
        public bool? LightDropWithinLimit;
        public bool Noisy;

        // Filled by PassRule.Evaluate.
        public bool Pass;
        public string Worst;
        public double WorstRatio;
    }

    /// <summary>The pass rule (performance.md): p99 lines, twice-the-line maxima, spawns and lights.</summary>
    public static class PassRule
    {
        public static void Evaluate(RunResult r, Lines l)
        {
            double gpu = Math.Max(r.GpuP99 / l.GpuMs, r.GpuMax / (l.GpuMs * l.MaxFactor));
            double main = Math.Max(r.MainP99 / l.MainMs, r.MainMax / (l.MainMs * l.MaxFactor));
            double render = Math.Max(r.RenderP99 / l.RenderMs, r.RenderMax / (l.RenderMs * l.MaxFactor));
            if (double.IsNaN(gpu)) gpu = double.PositiveInfinity; // no GPU timings at all can't pass
            if (double.IsNaN(main)) main = double.PositiveInfinity;
            if (double.IsNaN(render)) render = double.PositiveInfinity;
            r.Worst = "gpu";
            r.WorstRatio = gpu;
            if (main > r.WorstRatio) { r.Worst = "main"; r.WorstRatio = main; }
            if (render > r.WorstRatio) { r.Worst = "render"; r.WorstRatio = render; }
            bool timings = r.WorstRatio <= 1.0;
            bool spawns = r.SpawnsRejected == null || r.SpawnsRejected.Value == 0;
            bool lights = r.LightDropWithinLimit == null || r.LightDropWithinLimit.Value;
            if (timings && !spawns) r.Worst = "spawns";
            else if (timings && !lights) r.Worst = "lights";
            r.Pass = timings && spawns && lights;
        }

        /// <summary>
        /// The attempt a search uses when every attempt of a run was noisy: the median by worst
        /// ratio (the upper median of an even count), so one lucky or unlucky attempt can't decide.
        /// </summary>
        public static int MedianAttempt(IReadOnlyList<RunResult> attempts)
        {
            var order = new List<int>();
            for (int i = 0; i < attempts.Count; i++) order.Add(i);
            order.Sort((a, b) => attempts[a].WorstRatio.CompareTo(attempts[b].WorstRatio));
            return order[order.Count / 2];
        }
    }
}
