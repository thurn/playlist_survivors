using System;
using System.Globalization;
using System.IO;

namespace Demo3D.Choreography
{
    /// <summary>
    /// The choreographer's per-tick wall times (benchmarks.md, "Sim tick timing"): one 60 Hz tick
    /// each, single-threaded, timed with Stopwatch. Summaries exclude the first 5 s of ticks.
    /// It is measured where the timeline is built (the dotnet tests, or the player at load), and
    /// never part of the timeline's dump.
    /// </summary>
    public sealed class SimTickTiming
    {
        public const double DefaultWarmupSeconds = 5;

        public readonly float[] TickMs;
        public readonly double TickRate;
        /// <summary>Ticks that advanced a flow-field rebuild toward the hero (TD6), over the whole run; 0 on open ground.</summary>
        public readonly int FlowRebuildSlices;
        /// <summary>Rebuilds of the field toward the hero over the whole run, the first (built whole at load) included.</summary>
        public readonly int FlowRebuilds;

        public SimTickTiming(float[] tickMs, double tickRate, int flowRebuildSlices = 0, int flowRebuilds = 0)
        {
            TickMs = tickMs; TickRate = tickRate; FlowRebuildSlices = flowRebuildSlices; FlowRebuilds = flowRebuilds;
        }

        public readonly struct Summary
        {
            public readonly int Ticks;
            public readonly double P50Ms, P99Ms, MaxMs, MeanMs, TotalMs;
            public Summary(int ticks, double p50, double p99, double max, double mean, double total)
            {
                Ticks = ticks; P50Ms = p50; P99Ms = p99; MaxMs = max; MeanMs = mean; TotalMs = total;
            }
        }

        /// <summary>Percentiles over the ticks after <paramref name="warmupSeconds"/>.</summary>
        public Summary Summarize(double warmupSeconds = DefaultWarmupSeconds)
        {
            int skip = Math.Min(TickMs.Length, (int)Math.Round(warmupSeconds * TickRate));
            int n = TickMs.Length - skip;
            if (n <= 0) return new Summary(0, 0, 0, 0, 0, 0);
            var sorted = new float[n];
            Array.Copy(TickMs, skip, sorted, 0, n);
            Array.Sort(sorted);
            double total = 0;
            for (int i = 0; i < n; i++) total += sorted[i];
            return new Summary(n, Percentile(sorted, 0.50), Percentile(sorted, 0.99), sorted[n - 1], total / n, total);
        }

        static double Percentile(float[] sorted, double q)
        {
            // Nearest rank.
            int idx = (int)Math.Ceiling(q * sorted.Length) - 1;
            return sorted[Math.Max(0, Math.Min(sorted.Length - 1, idx))];
        }

        /// <summary>
        /// The fields TD7's result rows take (benchmarks.md): simTickP50Ms, simTickP99Ms,
        /// simTickMaxMs, simTickMeanMs, simTicks, simWarmupS, flowRebuildSlices and flowRebuilds (TD6).
        /// </summary>
        public void WriteJsonFields(JsonOut o, double warmupSeconds = DefaultWarmupSeconds)
        {
            Summary s = Summarize(warmupSeconds);
            o.Field("simTickP50Ms", s.P50Ms, 4).Field("simTickP99Ms", s.P99Ms, 4).Field("simTickMaxMs", s.MaxMs, 4)
             .Field("simTickMeanMs", s.MeanMs, 4).Field("simTicks", s.Ticks).Field("simWarmupS", warmupSeconds, 2)
             .Field("flowRebuildSlices", FlowRebuildSlices).Field("flowRebuilds", FlowRebuilds);
        }

        public string ToJson(double warmupSeconds = DefaultWarmupSeconds)
        {
            var sw = new StringWriter(CultureInfo.InvariantCulture);
            var o = new JsonOut(sw);
            o.BeginObject();
            WriteJsonFields(o, warmupSeconds);
            o.EndObject();
            return sw.ToString();
        }
    }
}
