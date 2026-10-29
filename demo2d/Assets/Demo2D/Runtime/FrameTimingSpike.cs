using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using Unity.Profiling;
using Unity.Profiling.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.Rendering;
using Debug = UnityEngine.Debug;

namespace Demo2D.Runtime
{
    /// <summary>
    /// PX1's timing spike, for PX10's song check: does a player that draws its camera by hand
    /// (as a batch-mode player must) report per-frame CPU and GPU times? With `--frame-timing
    /// &lt;out.json&gt;` it draws the main camera into a 1920×1080 target every frame for `--seconds`
    /// (after `--warmup`), records FrameTimingManager's times, the "Frame Time" profiler counters,
    /// the wall time from the draw to its GPU readback, and memory, then writes a JSON summary
    /// and quits. A windowed player (no `-batchmode`) draws its own frames and is timed the same
    /// way, for comparison.
    /// </summary>
    public sealed class FrameTimingSpike : MonoBehaviour
    {
        readonly FrameTiming[] _timings = new FrameTiming[1];
        readonly Dictionary<string, List<double>> _series = new Dictionary<string, List<double>>();
        readonly List<(string name, ProfilerRecorder recorder)> _recorders = new List<(string, ProfilerRecorder)>();
        ProfilerRecorder _systemMemory;
        RenderTexture _target;
        float _warmup, _seconds, _started;
        string _out;
        int _frames, _timed;
        long _memoryAtStart = -1;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            if (!Args.Has("--frame-timing")) return;
            var go = new GameObject("FrameTimingSpike");
            DontDestroyOnLoad(go);
            go.AddComponent<FrameTimingSpike>();
        }

        void Start()
        {
            _out = Args.Get("--frame-timing");
            _warmup = Args.GetFloat("--warmup", 2f);
            _seconds = Args.GetFloat("--seconds", 10f);
            _started = Time.realtimeSinceStartup;
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = -1;
            if (Application.isBatchMode)
                _target = new RenderTexture(StillCapture.Width, StillCapture.Height, 24, RenderTextureFormat.ARGB32) { name = "Timing" };
            var handles = new List<ProfilerRecorderHandle>();
            ProfilerRecorderHandle.GetAvailable(handles);
            foreach (var h in handles)
            {
                var d = ProfilerRecorderHandle.GetDescription(h);
                if (d.Name.Contains("Frame Time") || d.Name == "GPU Time")
                    _recorders.Add((d.Name, ProfilerRecorder.StartNew(d.Category, d.Name)));
            }
            _systemMemory = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "System Used Memory");
        }

        void Add(string name, double value)
        {
            if (!_series.TryGetValue(name, out var list)) _series[name] = list = new List<double>();
            list.Add(value);
        }

        void LateUpdate()
        {
            if (_target != null)
            {
                var camera = Camera.main;
                var sw = Stopwatch.StartNew();
                camera.targetTexture = _target;
                camera.Render();
                camera.targetTexture = null;
                var req = AsyncGPUReadback.Request(_target, 0, 0, 1, 0, 1, 0, 1, TextureFormat.RGBA32);
                req.WaitForCompletion();
                if (Time.realtimeSinceStartup - _started >= _warmup) Add("drawToReadbackMs", sw.Elapsed.TotalMilliseconds);
            }
            FrameTimingManager.CaptureFrameTimings();
            float elapsed = Time.realtimeSinceStartup - _started;
            if (elapsed < _warmup) return;
            if (_memoryAtStart < 0) _memoryAtStart = _systemMemory.LastValue;
            _frames++;
            if (FrameTimingManager.GetLatestTimings(1, _timings) > 0)
            {
                _timed++;
                Add("ftm.cpuFrameTimeMs", _timings[0].cpuFrameTime);
                Add("ftm.cpuMainThreadMs", _timings[0].cpuMainThreadFrameTime);
                Add("ftm.cpuRenderThreadMs", _timings[0].cpuRenderThreadFrameTime);
                Add("ftm.gpuFrameTimeMs", _timings[0].gpuFrameTime);
            }
            foreach (var (name, r) in _recorders)
                if (r.Valid && r.Count > 0) Add("counter." + name + " (ms)", r.LastValue * 1e-6);
            if (elapsed >= _warmup + _seconds) Finish();
        }

        void Finish()
        {
            var ci = CultureInfo.InvariantCulture;
            var sb = new StringBuilder("{\n");
            sb.AppendFormat(ci, "  \"batchMode\": {0},\n", Application.isBatchMode ? "true" : "false");
            sb.AppendFormat(ci, "  \"graphicsDeviceType\": \"{0}\",\n", SystemInfo.graphicsDeviceType);
            sb.AppendFormat(ci, "  \"graphicsDeviceName\": \"{0}\",\n", SystemInfo.graphicsDeviceName);
            sb.AppendFormat(ci, "  \"frameTimingStatsSupported\": {0},\n", FrameTimingManager.IsFeatureEnabled() ? "true" : "false");
            sb.AppendFormat(ci, "  \"sprites\": {0},\n", Args.GetInt("--spike-sprites", 0));
            sb.AppendFormat(ci, "  \"frames\": {0},\n  \"framesWithTimings\": {1},\n  \"seconds\": {2},\n", _frames, _timed, _seconds);
            sb.AppendFormat(ci, "  \"systemMemoryMiB\": {{ \"start\": {0:F1}, \"end\": {1:F1} }},\n",
                _memoryAtStart / 1048576.0, _systemMemory.LastValue / 1048576.0);
            sb.Append("  \"series\": {\n");
            int i = 0;
            foreach (var kv in _series)
            {
                var v = kv.Value;
                v.Sort();
                double sum = 0;
                int nonzero = 0;
                foreach (var x in v) { sum += x; if (x > 0) nonzero++; }
                double P(double q) => v[System.Math.Min(v.Count - 1, (int)(q * v.Count))];
                sb.AppendFormat(ci, "    \"{0}\": {{ \"n\": {1}, \"nonzero\": {2}, \"mean\": {3:F3}, \"p50\": {4:F3}, \"p99\": {5:F3}, \"max\": {6:F3} }}{7}\n",
                    kv.Key, v.Count, nonzero, sum / v.Count, P(0.5), P(0.99), v[v.Count - 1], ++i < _series.Count ? "," : "");
            }
            sb.Append("  }\n}\n");
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_out)));
            File.WriteAllText(_out, sb.ToString());
            Debug.Log("FrameTimingSpike: wrote " + _out + "\n" + sb);
            foreach (var (_, r) in _recorders) r.Dispose();
            _systemMemory.Dispose();
            Application.Quit(0);
            enabled = false;
        }
    }
}
