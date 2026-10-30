using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;

namespace Demo3D.Runtime.Playback
{
    /// <summary>
    /// `--gpu-probe-at t1,t2,...` (batch mode, for windowless checks on the Mac): a batch-mode
    /// player draws no frames, so at each song time it hand-draws the camera for a few frames into
    /// a 1080p target, and times each draw through GPU completion (an AsyncGPUReadback waited on).
    /// That is one frame's CPU submission and GPU work in series, an upper bound on the frame a
    /// windowed player pipelines. Only a handful: hand-drawn frames hold URP's pooled targets
    /// until a frame the player loop draws (CaptureTool). Writes `--gpu-probe-out` (JSON) after
    /// each time's frames; each time's first frame is a warm-up and isn't counted.
    /// </summary>
    public sealed class BatchGpuProbe : IDisposable
    {
        const int Frames = 4;

        readonly List<double> _at = new List<double>();
        readonly List<(double at, double t, int alive, double ms, double switchMs)> _samples = new List<(double, double, int, double, double)>();
        readonly string _out;
        readonly RenderTexture _target;
        int _next, _left;

        BatchGpuProbe(string at, string outPath, Camera camera)
        {
            foreach (var s in at.Split(','))
                if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var t)) _at.Add(t);
            _at.Sort();
            _out = outPath;
            _target = Target(camera, "GpuProbe");
        }

        /// <summary>A 1080p target for hand-drawn frames of <paramref name="camera"/>, with the pipeline's MSAA.</summary>
        public static RenderTexture Target(Camera camera, string name)
        {
            int msaa = GraphicsSettings.currentRenderPipeline is UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset urp
                       && camera.allowMSAA ? Math.Max(1, urp.msaaSampleCount) : 1;
            var target = new RenderTexture(1920, 1080, 24, RenderTextureFormat.ARGB32) { antiAliasing = msaa, name = name };
            target.Create();
            return target;
        }

        /// <summary>The probe the command line asks for, or null (also outside batch mode, where frames are drawn anyway).</summary>
        public static BatchGpuProbe FromArgs(Camera camera)
        {
            var at = Args.Get("--gpu-probe-at");
            if (at == null) return null;
            if (!Application.isBatchMode)
            {
                UnityEngine.Debug.LogWarning("BatchGpuProbe: --gpu-probe-at is for batch mode; a windowed player has --frame-probe");
                return null;
            }
            return new BatchGpuProbe(at, Args.Get("--gpu-probe-out", Path.Combine(Application.persistentDataPath, "gpu-probe.json")), camera);
        }

        /// <summary>
        /// After the frame's crowd and effects are updated; draws and times the camera when a probe is due.
        /// <paramref name="switchMs"/> is the frame's environment switch (--resident), recorded beside its draw.
        /// </summary>
        public void Frame(double t, Camera camera, int alive, double switchMs = 0)
        {
            if (_next >= _at.Count) return;
            if (_left == 0)
            {
                if (t < _at[_next]) return;
                _left = Frames;
            }
            var previous = camera.targetTexture;
            camera.targetTexture = _target;
            var watch = Stopwatch.StartNew();
            camera.Render();
            AsyncGPUReadback.Request(_target, 0, 0, 1, 0, 1, 0, 1).WaitForCompletion();
            watch.Stop();
            camera.targetTexture = previous;
            if (_left < Frames) _samples.Add((_at[_next], t, alive, watch.Elapsed.TotalMilliseconds, switchMs));
            if (--_left == 0)
            {
                _next++;
                Write();
            }
        }

        void Write()
        {
            var ci = CultureInfo.InvariantCulture;
            var sb = new StringBuilder("{\n");
            sb.AppendFormat(ci, "  \"graphicsDeviceName\": \"{0}\",\n  \"size\": [1920, 1080],\n  \"msaa\": {1},\n", SystemInfo.graphicsDeviceName, _target.antiAliasing);
            sb.AppendFormat(ci, "  \"args\": \"{0}\",\n  \"points\": [\n", string.Join(" ", Environment.GetCommandLineArgs().Skip(1)).Replace("\\", "\\\\").Replace("\"", "\\\""));
            var points = _samples.GroupBy(s => s.at).ToList();
            for (int i = 0; i < points.Count; i++)
            {
                var ms = points[i].Select(s => s.ms).OrderBy(x => x).ToList();
                // frames: each counted frame in order, [song time, draw ms, switch ms].
                sb.AppendFormat(ci, "    {{\"at\": {0:F2}, \"alive\": {1}, \"medianMs\": {2:F2}, \"maxMs\": {3:F2}, \"ms\": [{4}], \"frames\": [{5}]}}{6}\n",
                    points[i].Key, points[i].Max(s => s.alive), ms[ms.Count / 2], ms[ms.Count - 1],
                    string.Join(", ", ms.Select(x => x.ToString("F2", ci))),
                    string.Join(", ", points[i].Select(s => string.Format(ci, "[{0:F4}, {1:F2}, {2:F2}]", s.t, s.ms, s.switchMs))),
                    i + 1 < points.Count ? "," : "");
            }
            sb.Append("  ]\n}\n");
            File.WriteAllText(_out, sb.ToString());
        }

        public void Dispose()
        {
            if (_target != null) _target.Release();
        }
    }
}
