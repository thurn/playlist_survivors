using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace Demo3D.Runtime
{
    /// <summary>
    /// TD1's player spike: with `--frame-probe`, records FrameTimingManager's GPU and CPU
    /// times and the backbuffer size for `--seconds` (after `--warmup`), writes a JSON summary to
    /// `--out`, and quits.
    /// </summary>
    public sealed class FrameTimingProbe : MonoBehaviour
    {
        readonly List<double> _gpu = new List<double>();
        readonly List<double> _main = new List<double>();
        readonly List<double> _render = new List<double>();
        readonly FrameTiming[] _timings = new FrameTiming[1];
        float _warmup, _seconds, _started;
        string _out;
        int _frames;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            if (!Args.Has("--frame-probe")) return;
            var go = new GameObject("FrameTimingProbe");
            DontDestroyOnLoad(go);
            go.AddComponent<FrameTimingProbe>();
        }

        void Start()
        {
            _warmup = Args.GetFloat("--warmup", 3f);
            _seconds = Args.GetFloat("--seconds", 10f);
            _out = Args.Get("--out", Path.Combine(Application.persistentDataPath, "frame-probe.json"));
            _started = Time.realtimeSinceStartup;
            QualitySettings.vSyncCount = Args.Has("--vsync") ? 1 : 0;
            Application.targetFrameRate = -1;
        }

        void Update()
        {
            FrameTimingManager.CaptureFrameTimings();
            float elapsed = Time.realtimeSinceStartup - _started;
            if (elapsed < _warmup) return;
            if (FrameTimingManager.GetLatestTimings(1, _timings) > 0)
            {
                _gpu.Add(_timings[0].gpuFrameTime);
                _main.Add(_timings[0].cpuMainThreadFrameTime);
                _render.Add(_timings[0].cpuRenderThreadFrameTime);
            }
            _frames++;
            if (elapsed >= _warmup + _seconds) Finish();
        }

        void Finish()
        {
            var sb = new StringBuilder();
            var ci = CultureInfo.InvariantCulture;
            sb.Append("{\n");
            sb.AppendFormat(ci, "  \"graphicsDeviceType\": \"{0}\",\n", SystemInfo.graphicsDeviceType);
            sb.AppendFormat(ci, "  \"graphicsDeviceName\": \"{0}\",\n", SystemInfo.graphicsDeviceName);
            sb.AppendFormat(ci, "  \"screen\": [{0}, {1}],\n", Screen.width, Screen.height);
            sb.AppendFormat(ci, "  \"display\": [{0}, {1}],\n", Display.main.systemWidth, Display.main.systemHeight);
            sb.AppendFormat(ci, "  \"renderingSize\": [{0}, {1}],\n", Display.main.renderingWidth, Display.main.renderingHeight);
            sb.AppendFormat(ci, "  \"fullScreenMode\": \"{0}\",\n", Screen.fullScreenMode);
            sb.AppendFormat(ci, "  \"dpi\": {0},\n", Screen.dpi);
            sb.AppendFormat(ci, "  \"frames\": {0},\n", _frames);
            sb.AppendFormat(ci, "  \"timedFrames\": {0},\n", _gpu.Count);
            sb.AppendFormat(ci, "  \"seconds\": {0},\n", _seconds);
            Stats(sb, "gpuMs", _gpu, ci);
            sb.Append(",\n");
            Stats(sb, "mainThreadMs", _main, ci);
            sb.Append(",\n");
            Stats(sb, "renderThreadMs", _render, ci);
            sb.Append("\n}\n");
            File.WriteAllText(_out, sb.ToString());
            Debug.Log("FrameTimingProbe wrote " + _out + "\n" + sb);
            Application.Quit(0);
            enabled = false;
        }

        static void Stats(StringBuilder sb, string name, List<double> values, CultureInfo ci)
        {
            values.Sort();
            double sum = 0;
            foreach (var v in values) sum += v;
            double P(double q) => values.Count == 0 ? 0 : values[System.Math.Min(values.Count - 1, (int)(q * values.Count))];
            sb.AppendFormat(ci, "  \"{0}\": {{ \"mean\": {1:F4}, \"p50\": {2:F4}, \"p99\": {3:F4}, \"max\": {4:F4} }}",
                name, values.Count == 0 ? 0 : sum / values.Count, P(0.5), P(0.99), values.Count == 0 ? 0 : values[values.Count - 1]);
        }
    }
}
