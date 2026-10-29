using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Demo3D.Bench
{
    /// <summary>
    /// The full song's pass rule (performance.md): the song has no steady state, so its run passes
    /// when every 20 s window, starting every 5 s from 5 s into the run, plus one more ending at the
    /// run's last frame, meets the pass lines at p99 and keeps the effect-light drop share's p99
    /// within its limit; no frame after the 5 s warm-up exceeds twice its line (the warm-up's largest
    /// frames are reported: the first frames load and compile); and no effect spawn was rejected.
    /// Frames without a GPU time count only on the CPU lines; over 0.5% of them marks the run noisy,
    /// as the harness does, and a window where under half its frames have a GPU time fails (no GPU
    /// data is no pass). A GPU time over a second is a garbage reading: counted, then treated as
    /// missing. Times are song seconds.
    /// </summary>
    public sealed class SongWindows
    {
        public const double Window = 20, Step = 5, Warmup = 5, MaxZeroGpuShare = 0.005, MinGpuShare = 0.5, GarbageMs = 1000;

        public struct Frame
        {
            public double T, GpuMs, MainMs, RenderMs;
        }

        public sealed class WindowResult
        {
            public double From;
            public int Frames, GpuFrames;
            public double GpuP99, MainP99, RenderP99, DropP99;
            public bool Pass;
        }

        public readonly List<WindowResult> Windows = new List<WindowResult>();
        public int Frames, GpuZeroFrames, GpuGarbageFrames, SpawnsRejected;
        public double First, Last;
        public double MaxGpu, MaxMain, MaxRender, WarmupMaxGpu, WarmupMaxMain, WarmupMaxRender;
        public bool NoFrameOverTwice, Noisy, Pass;
        /// <summary>Extra JSON members for the verdict (the run's context), written as given.</summary>
        public readonly List<KeyValuePair<string, string>> Context = new List<KeyValuePair<string, string>>();

        public static SongWindows Evaluate(IReadOnlyList<Frame> input, IReadOnlyList<(double t, double share)> drops,
                                           int spawnsRejected, Lines lines, double dropLimit)
        {
            var r = new SongWindows { SpawnsRejected = spawnsRejected };
            var frames = new List<Frame>(input.Count);
            foreach (var f in input)
            {
                var g = f;
                if (g.GpuMs > GarbageMs) { r.GpuGarbageFrames++; g.GpuMs = 0; }
                frames.Add(g);
            }
            r.Frames = frames.Count;
            if (frames.Count == 0) return r;
            r.First = double.PositiveInfinity;
            r.Last = double.NegativeInfinity;
            foreach (var f in frames) { r.First = Math.Min(r.First, f.T); r.Last = Math.Max(r.Last, f.T); }
            foreach (var f in frames)
            {
                if (f.T < r.First + Warmup)
                {
                    r.WarmupMaxGpu = Math.Max(r.WarmupMaxGpu, f.GpuMs);
                    r.WarmupMaxMain = Math.Max(r.WarmupMaxMain, f.MainMs);
                    r.WarmupMaxRender = Math.Max(r.WarmupMaxRender, f.RenderMs);
                }
                else
                {
                    r.MaxGpu = Math.Max(r.MaxGpu, f.GpuMs);
                    r.MaxMain = Math.Max(r.MaxMain, f.MainMs);
                    r.MaxRender = Math.Max(r.MaxRender, f.RenderMs);
                }
                if (f.GpuMs <= 0) r.GpuZeroFrames++;
            }
            r.NoFrameOverTwice = r.MaxGpu <= lines.MaxFactor * lines.GpuMs && r.MaxMain <= lines.MaxFactor * lines.MainMs &&
                                 r.MaxRender <= lines.MaxFactor * lines.RenderMs;
            // Window starts: every 5 s from the warm-up's end while a window fits, then one ending at
            // the last frame, so the run's end is always judged (a run shorter than warm-up plus one
            // window gets one short window).
            var starts = new List<double>();
            double first = r.First + Warmup, lastStart = Math.Max(first, r.Last - Window);
            for (double w = first; w <= lastStart + 1e-9; w += Step) starts.Add(w);
            if (lastStart - starts[starts.Count - 1] > 1e-6) starts.Add(lastStart);
            bool windowsPass = true;
            foreach (double w in starts)
            {
                bool final = Math.Abs(w - lastStart) < 1e-6;
                double end = w + Window;
                var gpu = new List<double>();
                var main = new List<double>();
                var render = new List<double>();
                var drop = new List<double>();
                foreach (var f in frames)
                {
                    if (f.T < w || (final ? f.T > end : f.T >= end)) continue;
                    if (f.GpuMs > 0) gpu.Add(f.GpuMs);
                    main.Add(f.MainMs);
                    render.Add(f.RenderMs);
                }
                foreach (var (t, share) in drops)
                    if (t >= w && (final ? t <= end : t < end)) drop.Add(share);
                var res = new WindowResult
                {
                    From = w, Frames = main.Count, GpuFrames = gpu.Count,
                    GpuP99 = P99(gpu), MainP99 = P99(main), RenderP99 = P99(render), DropP99 = P99(drop),
                };
                res.Pass = res.Frames > 0 && res.GpuFrames >= MinGpuShare * res.Frames && res.GpuP99 <= lines.GpuMs &&
                           res.MainP99 <= lines.MainMs && res.RenderP99 <= lines.RenderMs && res.DropP99 <= dropLimit;
                windowsPass &= res.Pass;
                r.Windows.Add(res);
            }
            r.Noisy = r.GpuZeroFrames > MaxZeroGpuShare * r.Frames;
            r.Pass = windowsPass && r.NoFrameOverTwice && spawnsRejected == 0;
            return r;
        }

        static double P99(List<double> v)
        {
            if (v.Count == 0) return 0;
            v.Sort();
            return Stats.Percentile(v, 0.99);
        }

        /// <summary>The verdict as JSON; window times are song seconds.</summary>
        public string ToJson()
        {
            var ci = CultureInfo.InvariantCulture;
            var sb = new StringBuilder("{\n  \"windows\": [\n");
            for (int i = 0; i < Windows.Count; i++)
            {
                var w = Windows[i];
                sb.AppendFormat(ci, "    {{\"song\": {0:F2}, \"frames\": {1}, \"gpuFrames\": {2}, \"gpuP99Ms\": {3:F3}, \"mainP99Ms\": {4:F3}, \"renderP99Ms\": {5:F3}, \"lightDropShareP99\": {6:F4}, \"pass\": {7}}}{8}\n",
                    w.From, w.Frames, w.GpuFrames, w.GpuP99, w.MainP99, w.RenderP99, w.DropP99, B(w.Pass), i + 1 < Windows.Count ? "," : "");
            }
            sb.Append("  ],\n");
            sb.AppendFormat(ci, "  \"songFrom\": {0:F2}, \"songTo\": {1:F2}, \"frames\": {2}, \"gpuZeroFrames\": {3}, \"gpuGarbageFrames\": {4},\n",
                First, Last, Frames, GpuZeroFrames, GpuGarbageFrames);
            sb.AppendFormat(ci, "  \"maxGpuMs\": {0:F3}, \"maxMainMs\": {1:F3}, \"maxRenderMs\": {2:F3}, \"noFrameOverTwiceItsLine\": {3},\n",
                MaxGpu, MaxMain, MaxRender, B(NoFrameOverTwice));
            sb.AppendFormat(ci, "  \"warmupMaxGpuMs\": {0:F3}, \"warmupMaxMainMs\": {1:F3}, \"warmupMaxRenderMs\": {2:F3}, \"noisy\": {3},\n",
                WarmupMaxGpu, WarmupMaxMain, WarmupMaxRender, B(Noisy));
            foreach (var kv in Context) sb.Append("  \"").Append(kv.Key).Append("\": ").Append(kv.Value).Append(",\n");
            sb.AppendFormat(ci, "  \"spawnsRejected\": {0},\n  \"pass\": {1}\n}}\n", SpawnsRejected, B(Pass));
            return sb.ToString();
        }

        /// <summary>A JSON string literal.</summary>
        public static string Quote(string s)
        {
            var sb = new StringBuilder("\"");
            foreach (char c in s ?? "")
            {
                if (c == '"' || c == '\\') sb.Append('\\').Append(c);
                else if (c < ' ') sb.AppendFormat(CultureInfo.InvariantCulture, "\\u{0:x4}", (int)c);
                else sb.Append(c);
            }
            return sb.Append('"').ToString();
        }

        static string B(bool b) => b ? "true" : "false";
    }
}
