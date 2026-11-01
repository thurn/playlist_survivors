using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
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
    ///
    /// Each frame also records its CPU share: the camera's Render call, before the wait for the GPU.
    ///
    /// With `--gpu-probe-ablate` each time's frames cycle, for <see cref="AblateRounds"/> rounds, through
    /// the full frame and the frame with one pass turned off (<see cref="Ablations"/>), so each pass's
    /// saving is measured against full frames drawn beside it, under the same load; each kind's first
    /// frame is its warm-up. The JSON's stats are the full frames'; `ablations` has each pass's median,
    /// its saving against them, and its frames beside the full frame drawn just before each (`pairs`):
    /// under a loaded machine's bursts a pass's cost shows best in its low percentiles and its pairs.
    ///
    /// `--gpu-probe-without PASS,...` turns those passes off in every probe frame: for a moment too
    /// short for the cycle, such as a mass kill's first frames, compared with a run that keeps them, since
    /// a fixed-time run reaches the same frames each time.
    /// </summary>
    public sealed class GpuProbe : IDisposable
    {
        /// <summary>Frames drawn at each time: a warm-up, then the counted ones.</summary>
        public const int Frames = 9;
        /// <summary>Rounds of every frame kind at each time with `--gpu-probe-ablate`: a probe frame varies by 2x under load, so more.</summary>
        public const int AblateRounds = 25;

        /// <summary>The passes `--gpu-probe-ablate` turns off, one at a time: what each costs in the frame.</summary>
        public static readonly string[] Ablations = { "ssao", "post", "bloom", "shadows", "opaque-texture", "msaa", "crowd", "effects", "effect-lights" };

        readonly List<double> _at = new List<double>();
        readonly List<(double at, double t, int alive, double ms, double cpuMs, int kind)> _samples = new List<(double, double, int, double, double, int)>();
        readonly string _out;
        readonly RenderTexture _target;
        RenderTexture _single;
        // Frame kinds: 0 the full frame, then one per ablation.
        readonly int _kinds, _rounds;
        // `--gpu-probe-without`: off in every frame.
        readonly string[] _without;
        int _next, _left;

        GpuProbe(string at, string outPath, bool ablate, string without)
        {
            _without = without?.Split(',') ?? new string[0];
            foreach (var pass in _without)
                if (!Ablations.Contains(pass)) throw new ArgumentException($"GpuProbe: no pass {pass} (one of {string.Join(", ", Ablations)})");
            _kinds = ablate ? 1 + Ablations.Length : 1;
            _rounds = ablate ? AblateRounds : Frames;
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
                options.Outputs.GpuProbe ?? Path.Combine(Application.persistentDataPath, "gpu-probe.json"), options.GpuProbeAblate,
                options.GpuProbeWithout);
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
                _left = _rounds * _kinds;
            }
            int drawn = _rounds * _kinds - _left, kind = drawn % _kinds;
            var off = kind == 0 ? _without : _without.Append(Ablations[kind - 1]).ToArray();
            var target = off.Contains("msaa") ? Single() : _target;
            var restores = off.Select(pass => Ablate(pass, camera)).ToList();
            var previous = camera.targetTexture;
            camera.targetTexture = target;
            long start = Stopwatch.GetTimestamp();
            camera.Render();
            long submitted = Stopwatch.GetTimestamp();
            AsyncGPUReadback.Request(target, 0, 0, 1, 0, 1, 0, 1).WaitForCompletion();
            long done = Stopwatch.GetTimestamp();
            camera.targetTexture = previous;
            restores.ForEach(restore => restore?.Invoke());
            if (drawn >= _kinds)
                _samples.Add((_at[_next], t, alive, (done - start) * 1000.0 / Stopwatch.Frequency,
                    (submitted - start) * 1000.0 / Stopwatch.Frequency, kind));
            if (--_left == 0)
            {
                _next++;
                Write();
            }
        }

        /// <summary>A 1080p target without MSAA, for the "msaa" ablation.</summary>
        RenderTexture Single()
        {
            if (_single == null)
            {
                _single = new RenderTexture(Stills.Width, Stills.Height, 24, RenderTextureFormat.ARGB32) { name = "GpuProbe1x" };
                _single.Create();
            }
            return _single;
        }

        /// <summary>Turns <paramref name="pass"/> off for <paramref name="camera"/>'s next draw; returns what turns it back on.</summary>
        static Action Ablate(string pass, Camera camera)
        {
            var data = camera.GetComponent<UniversalAdditionalCameraData>();
            switch (pass)
            {
                case "ssao":
                    var features = GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset urp
                        ? urp.rendererDataList.ToArray().SelectMany(d => d.rendererFeatures).OfType<ScreenSpaceAmbientOcclusion>().Where(f => f.isActive).ToList()
                        : new List<ScreenSpaceAmbientOcclusion>();
                    features.ForEach(f => f.SetActive(false));
                    return () => features.ForEach(f => f.SetActive(true));
                case "post":
                    bool post = data.renderPostProcessing;
                    data.renderPostProcessing = false;
                    return () => data.renderPostProcessing = post;
                case "bloom":
                    var blooms = VolumeManager.instance.GetVolumes(~0)
                        .Select(v => v.sharedProfile != null && v.sharedProfile.TryGet<Bloom>(out var b) ? b : null)
                        .Where(b => b != null && b.active).ToList();
                    blooms.ForEach(b => b.active = false);
                    return () => blooms.ForEach(b => b.active = true);
                case "shadows":
                    bool shadows = data.renderShadows;
                    data.renderShadows = false;
                    return () => data.renderShadows = shadows;
                case "opaque-texture":
                    var color = data.requiresColorOption;
                    data.requiresColorOption = CameraOverrideOption.Off;
                    return () => data.requiresColorOption = color;
                case "msaa":
                    return null; // the 1x target (Single)
                case "crowd": // the monsters and their shatter's shards
                    int mask = camera.cullingMask;
                    camera.cullingMask &= ~(1 << Crowd.BoneTextureCrowd.Layer);
                    return () => camera.cullingMask = mask;
                case "effects": // the particle pool
                    Effects.EffectSystem.Hidden = true;
                    return () => Effects.EffectSystem.Hidden = false;
                case "effect-lights": // the effects' pooled point lights
                    var root = GameObject.Find("EffectLights");
                    var lit = root == null ? new List<Light>() : root.GetComponentsInChildren<Light>().Where(l => l.enabled).ToList();
                    lit.ForEach(l => l.enabled = false);
                    return () => lit.ForEach(l => l.enabled = true);
                default:
                    throw new ArgumentException($"GpuProbe: no ablation {pass}");
            }
        }

        void Write()
        {
            var ci = CultureInfo.InvariantCulture;
            var full = _samples.Where(s => s.kind == 0).ToList();
            var all = full.Select(s => s.ms).OrderBy(x => x).ToList();
            var cpu = full.Select(s => s.cpuMs).OrderBy(x => x).ToList();
            var sb = new StringBuilder("{\n");
            sb.AppendFormat(ci, "  \"graphicsDeviceName\": \"{0}\",\n  \"size\": [{1}, {2}],\n  \"msaa\": {3},\n",
                SystemInfo.graphicsDeviceName, Stills.Width, Stills.Height, _target.antiAliasing);
            if (_without.Length > 0) sb.AppendFormat(ci, "  \"without\": [{0}],\n", string.Join(", ", _without.Select(w => $"\"{w}\"")));
            sb.AppendFormat(ci, "  \"args\": \"{0}\",\n", string.Join(" ", Environment.GetCommandLineArgs().Skip(1)).Replace("\\", "\\\\").Replace("\"", "\\\""));
            sb.AppendFormat(ci, "  \"frames\": {0},\n  \"p50Ms\": {1:F2},\n  \"p99Ms\": {2:F2},\n  \"maxMs\": {3:F2},\n",
                all.Count, Pct(all, 0.5), Pct(all, 0.99), all[all.Count - 1]);
            // The CPU's share: the Render call, before the wait for the GPU.
            sb.AppendFormat(ci, "  \"cpuP50Ms\": {0:F2},\n  \"cpuP99Ms\": {1:F2},\n", Pct(cpu, 0.5), Pct(cpu, 0.99));
            if (_kinds > 1)
            {
                // Each pass off: its frames' median, and its saving against the full frames' median.
                sb.Append("  \"ablations\": [");
                for (int k = 1; k < _kinds; k++)
                {
                    var ms = _samples.Where(s => s.kind == k).Select(s => s.ms).OrderBy(x => x).ToList();
                    var cpuK = _samples.Where(s => s.kind == k).Select(s => s.cpuMs).OrderBy(x => x).ToList();
                    // Each frame of this kind with the full frame drawn k frames before it, in the same round.
                    var pairs = _samples.Select((s, i) => (s, i)).Where(x => x.s.kind == k && x.i >= k && _samples[x.i - k].kind == 0)
                        .Select(x => string.Format(ci, "[{0:F2}, {1:F2}]", _samples[x.i - k].ms, x.s.ms));
                    sb.Append(k > 1 ? ",\n    " : "\n    ").AppendFormat(ci, "{{\"name\": \"{0}\", \"medianMs\": {1:F2}, \"savedMs\": {2:F2}, \"cpuSavedMs\": {3:F2}, \"pairs\": [{4}]",
                        Ablations[k - 1], Pct(ms, 0.5), Pct(all, 0.5) - Pct(ms, 0.5), Pct(cpu, 0.5) - Pct(cpuK, 0.5), string.Join(", ", pairs)).Append('}');
                }
                sb.Append("],\n");
            }
            sb.Append("  \"points\": [\n");
            var points = full.GroupBy(s => s.at).ToList();
            for (int i = 0; i < points.Count; i++)
            {
                var ms = points[i].Select(s => s.ms).OrderBy(x => x).ToList();
                // frames: each counted frame in order, [song time, alive, draw ms, its CPU ms].
                sb.AppendFormat(ci, "    {{\"at\": {0:F3}, \"alive\": {1}, \"medianMs\": {2:F2}, \"maxMs\": {3:F2}, \"frames\": [{4}]}}{5}\n",
                    points[i].Key, points[i].Max(s => s.alive), Pct(ms, 0.5), ms[ms.Count - 1],
                    string.Join(", ", points[i].Select(s => string.Format(ci, "[{0:F4}, {1}, {2:F2}, {3:F2}]", s.t, s.alive, s.ms, s.cpuMs))),
                    i + 1 < points.Count ? "," : "");
            }
            sb.Append("  ]\n}\n");
            File.WriteAllText(_out, sb.ToString());
            var latest = full.Where(s => s.at == _at[_next - 1]).Select(s => s.ms).OrderBy(x => x).ToList();
            Debug.Log(string.Format(ci, "GpuProbe: {0} frames at {1:F3} s, median {2:F2} ms, max {3:F2} ms -> {4}",
                latest.Count, _at[_next - 1], Pct(latest, 0.5), latest[latest.Count - 1], _out));
        }

        static double Pct(List<double> sorted, double p) => sorted[(int)Math.Round(p * (sorted.Count - 1))];

        public void Dispose()
        {
            if (_target != null) _target.Release();
            if (_single != null) _single.Release();
        }
    }
}
