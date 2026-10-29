using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace Demo3D.Runtime.Playback
{
    /// <summary>
    /// The `--overlay` (qa.md): song time and section, on-screen counts against the level
    /// script's target, and rolling 20 s p99 frame times against the Cygnus pass lines
    /// (performance.md: GPU 4.2 ms, main and render threads 10.4 ms).
    /// </summary>
    public sealed class DemoOverlay : MonoBehaviour
    {
        public DemoMain Main;
        const double WindowSeconds = 20;
        readonly Queue<(double t, float gpu, float main, float render)> _frames = new Queue<(double, float, float, float)>();
        readonly FrameTiming[] _timing = new FrameTiming[1];
        GUIStyle _style;
        float _maxFrame;

        void Update()
        {
            FrameTimingManager.CaptureFrameTimings();
            if (FrameTimingManager.GetLatestTimings(1, _timing) == 0) return;
            double now = Time.realtimeSinceStartupAsDouble;
            _frames.Enqueue((now, (float)_timing[0].gpuFrameTime, (float)_timing[0].cpuMainThreadFrameTime, (float)_timing[0].cpuRenderThreadFrameTime));
            while (_frames.Count > 0 && now - _frames.Peek().t > WindowSeconds) _frames.Dequeue();
            _maxFrame = Mathf.Max(_maxFrame, (float)_timing[0].cpuFrameTime);
        }

        static float P99(List<float> v)
        {
            if (v.Count == 0) return 0;
            v.Sort();
            return v[Mathf.Min(v.Count - 1, (int)(0.99f * v.Count))];
        }

        void OnGUI()
        {
            if (Main == null || Main.Timeline == null) return;
            _style ??= new GUIStyle(GUI.skin.label) { fontSize = 20, normal = { textColor = Color.white } };
            var gpu = new List<float>(); var main = new List<float>(); var render = new List<float>();
            foreach (var f in _frames) { if (f.gpu > 0) gpu.Add(f.gpu); main.Add(f.main); render.Add(f.render); }
            var tl = Main.Timeline;
            int section = tl.Map != null ? tl.Map.SectionIndexAt(Main.SongTime) : 0;
            var stats = tl.Sections != null && section >= 0 && section < tl.Sections.Length ? tl.Sections[section] : null;
            var ci = CultureInfo.InvariantCulture;
            string text = string.Format(ci,
                "t {0:F2}  {1}\non screen {2} alive + {3} dying = {4}   target {5}\n" +
                "p99 20 s  GPU {6:F2}/4.2  main {7:F2}/10.4  render {8:F2}/10.4 ms",
                Main.SongTime, stats?.Label ?? tl.Name, Main.OnScreenAlive, Main.OnScreenDying,
                Main.OnScreenAlive + Main.OnScreenDying, stats?.TargetOnScreen ?? 0, P99(gpu), P99(main), P99(render));
            GUI.Label(new Rect(20, 20, 1200, 120), text, _style);
        }
    }
}
