using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;
using Stopwatch = System.Diagnostics.Stopwatch;

namespace Game.Client
{
    /// <summary>
    /// `--gpu-probe-at t1,t2,... --gpu-probe-out FILE.json` (batch mode, for windowless checks on the
    /// Mac; demo3d's BatchGpuProbe, docs/tech-demo-3d/mac.md): a batch-mode player draws no frames, so
    /// at each song time it hand-draws the camera for a few consecutive frames into a 1080p target with
    /// the game's MSAA (<see cref="Stills.Target"/>), and times each draw through GPU completion (an
    /// AsyncGPUReadback waited on). That is one frame's CPU submission and GPU work in series, an upper
    /// bound on the frame a windowed player pipelines; the HUD's panel isn't in it. Each time's first
    /// frame is a warm-up and isn't counted. Only a handful of times: hand-drawn frames hold URP's
    /// pooled targets until a frame the player loop draws. It writes the JSON after each time's frames.
    /// </summary>
    public sealed class GpuProbe : IDisposable
    {
        /// <summary>Frames drawn at each time: a warm-up, then the counted ones.</summary>
        public const int Frames = 9;

        readonly List<double> _at = new List<double>();
        readonly List<(double at, double t, int alive, double ms)> _samples = new List<(double, double, int, double)>();
        readonly string _out;
        readonly RenderTexture _target;
        int _next, _left;

        GpuProbe(string at, string outPath)
        {
            foreach (var s in at.Split(','))
                _at.Add(double.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture));
            _at.Sort();
            _out = outPath;
            _target = Stills.Target("GpuProbe");
        }

        /// <summary>The probe the launch asks for, or null: none asked, or not in batch mode, or no graphics device (`--headless`).</summary>
        public static GpuProbe FromOptions(LaunchOptions options)
        {
            if (options.GpuProbeAt == null) return null;
            if (!Application.isBatchMode || SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
            {
                Debug.LogWarning("GpuProbe: --gpu-probe-at needs batch mode with a graphics device (not --headless)");
                return null;
            }
            return new GpuProbe(options.GpuProbeAt,
                options.Outputs.GpuProbe ?? Path.Combine(Application.persistentDataPath, "gpu-probe.json"));
        }

        /// <summary>
        /// After the frame's presenters; draws and times the camera when a probe is due, at song time
        /// <paramref name="t"/> with <paramref name="alive"/> enemies.
        /// </summary>
        public void Frame(double t, Camera camera, int alive)
        {
            if (_next >= _at.Count) return;
            if (_left == 0)
            {
                if (t < _at[_next]) return;
                _left = Frames;
            }
            var previous = camera.targetTexture;
            camera.targetTexture = _target;
            long start = Stopwatch.GetTimestamp();
            camera.Render();
            AsyncGPUReadback.Request(_target, 0, 0, 1, 0, 1, 0, 1).WaitForCompletion();
            double ms = (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;
            camera.targetTexture = previous;
            if (_left < Frames) _samples.Add((_at[_next], t, alive, ms));
            if (--_left == 0)
            {
                _next++;
                Write();
            }
        }

        void Write()
        {
            var ci = CultureInfo.InvariantCulture;
            var all = _samples.Select(s => s.ms).OrderBy(x => x).ToList();
            var sb = new StringBuilder("{\n");
            sb.AppendFormat(ci, "  \"graphicsDeviceName\": \"{0}\",\n  \"size\": [{1}, {2}],\n  \"msaa\": {3},\n",
                SystemInfo.graphicsDeviceName, Stills.Width, Stills.Height, _target.antiAliasing);
            sb.AppendFormat(ci, "  \"args\": \"{0}\",\n", string.Join(" ", Environment.GetCommandLineArgs().Skip(1)).Replace("\\", "\\\\").Replace("\"", "\\\""));
            sb.AppendFormat(ci, "  \"frames\": {0},\n  \"p50Ms\": {1:F2},\n  \"p99Ms\": {2:F2},\n  \"maxMs\": {3:F2},\n  \"points\": [\n",
                all.Count, Pct(all, 0.5), Pct(all, 0.99), all[all.Count - 1]);
            var points = _samples.GroupBy(s => s.at).ToList();
            for (int i = 0; i < points.Count; i++)
            {
                var ms = points[i].Select(s => s.ms).OrderBy(x => x).ToList();
                // frames: each counted frame in order, [song time, alive, draw ms].
                sb.AppendFormat(ci, "    {{\"at\": {0:F3}, \"alive\": {1}, \"medianMs\": {2:F2}, \"maxMs\": {3:F2}, \"frames\": [{4}]}}{5}\n",
                    points[i].Key, points[i].Max(s => s.alive), Pct(ms, 0.5), ms[ms.Count - 1],
                    string.Join(", ", points[i].Select(s => string.Format(ci, "[{0:F4}, {1}, {2:F2}]", s.t, s.alive, s.ms))),
                    i + 1 < points.Count ? "," : "");
            }
            sb.Append("  ]\n}\n");
            File.WriteAllText(_out, sb.ToString());
            var latest = _samples.Where(s => s.at == _at[_next - 1]).Select(s => s.ms).OrderBy(x => x).ToList();
            Debug.Log(string.Format(ci, "GpuProbe: {0} frames at {1:F3} s, median {2:F2} ms, max {3:F2} ms -> {4}",
                latest.Count, _at[_next - 1], Pct(latest, 0.5), latest[latest.Count - 1], _out));
        }

        static double Pct(List<double> sorted, double p) => sorted[(int)Math.Round(p * (sorted.Count - 1))];

        public void Dispose()
        {
            if (_target != null) _target.Release();
        }
    }
}
