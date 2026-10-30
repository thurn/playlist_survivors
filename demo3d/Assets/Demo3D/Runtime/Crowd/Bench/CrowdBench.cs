using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Unity.Collections;
using Unity.Jobs;
using Unity.Jobs.LowLevel.Unsafe;
using Demo3D.Runtime.Arena;
using Demo3D.Runtime.Playback;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Demo3D.Runtime.Crowd.Bench
{
    /// <summary>
    /// TD5's crowd test scene and windowed measurement player (`--crowd-bench`), until TD7's
    /// harness and TD4's stress scenes take over: flat ground, the default sun, the game camera at
    /// a view width, and the S1-like (walking) or S2-like (hits, deaths, refills) scenario on one
    /// of two arms: the bone-texture crowd (palette skinning; TD5 measured and deleted the
    /// per-vertex variant) and the stock baseline.
    ///
    /// <code>
    /// PlaylistSurvivors3D.exe -screen-width 1920 -screen-height 1080 -screen-fullscreen 0
    ///   -job-worker-count 6 --crowd-bench --arm bone|stock --scene s1|s2
    ///   [--n 2000] [--view-width 40] [--warmup 5] [--seconds 20] [--out rows.jsonl]
    ///   [--search --probes 1000,3000,6000] [--stills DIR] [--lights 0] [--aa msaa4|taa]
    /// </code>
    /// Each run writes one result row (JSON line): FrameTimingManager's p50/p99/max GPU,
    /// main-thread and render-thread times after the warm-up (zero GPU readings excluded), the
    /// on-screen counts, and the pass rule (performance.md, Performance Budget).
    ///
    /// `--scene roster --stills DIR` is the look pass's roster still (ps-e8r.8): every drawn type
    /// in a row, standing, under the game's night look, to show each type's signature colour
    /// (`--classic-monster-colors` for the roster before them). In batch mode the stills render offscreen.
    /// </summary>
    public sealed class CrowdBench : MonoBehaviour
    {
        public const float GpuLine = 4.2f, CpuLine = 10.4f;
        const float PitchDeg = 55f, VFovDeg = 30f;
        const int Capacity = 12000;

        CrowdLibrary _lib;
        ICrowdRenderer _crowd;
        Camera _camera;
        Light _sun;
        UniversalRenderPipelineAsset _pipeline;
        UniversalAdditionalCameraData _camData;
        string _arm, _scene, _out;
        float _viewWidth;
        bool _combat;
        readonly List<Light> _lights = new List<Light>();

        // The scenario: per type, its slots and motion.
        NativeArray<CrowdScenario.Slot>[] _slots = Array.Empty<NativeArray<CrowdScenario.Slot>>();
        CrowdScenario.Motion[] _motion;
        int[] _counts;
        float _t0;
        float? _timeOverride;
        bool _fixedTime;
        float _scenarioTime;
        int _frames, _framesAtRebuild;
        int _n;

        // Measurement.
        bool _measuring;
        readonly List<double> _gpu = new List<double>(), _main = new List<double>(), _render = new List<double>();
        readonly List<int> _onAlive = new List<int>(), _onDying = new List<int>();
        readonly FrameTiming[] _timings = new FrameTiming[1];
        int _zeroGpu;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            if (!Args.Has("--crowd-bench")) return;
            var go = new GameObject("CrowdBench");
            DontDestroyOnLoad(go);
            go.AddComponent<CrowdBench>();
        }

        void Start()
        {
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = -1;
            _lib = CrowdLibrary.Load();
            _arm = Args.Get("--arm", "bone");
            _scene = Args.Get("--scene", "s1").ToLowerInvariant();
            _combat = _scene == "s2";
            _fixedTime = Args.Has("--fixed-time");
            _viewWidth = Args.GetFloat("--view-width", 40);
            _out = Args.Get("--out", Path.Combine(Application.persistentDataPath, "crowd-bench.jsonl"));
            BuildRig();
            if (_scene == "roster") NightRig();
            SetAntiAliasing(Args.Get("--aa", "msaa4"));
            if (Application.isBatchMode)
                _camera.targetTexture = new RenderTexture(1920, 1080, 24, RenderTextureFormat.ARGB32) { antiAliasing = Mathf.Max(1, _pipeline.msaaSampleCount), name = "BenchStill" };
            SetViewWidth(_viewWidth);
            AddLights(Args.GetInt("--lights", 0));
            _crowd = CreateArm(_arm);
            _crowd.Initialize(_lib.Types.Select(t => t.Id).ToArray(), Capacity);
            if (_crowd is BoneTextureCrowd btc)
            {
                var ablate = Args.Get("--ablate", "");
                btc.DrawMain = ablate != "main";
                btc.DrawShadows = ablate != "shadows";
                btc.ShadowExtension = ablate != "extension";
                Shader.SetGlobalFloat("_CrowdNoSkin", ablate == "skin" ? 1 : 0);
                btc.SkipPalette = ablate == "skin"; // the rest pose reads no palette, so the ablation drops its pass too
                btc.Clock = () => _scenarioTime; // the scenario's clock, so fixed-time stills repeat
                Shader.SetGlobalVector("_CrowdDebugMotion", Args.Has("--debug-motion") ? new Vector4(0.02f, 0, 0, 0) : Vector4.zero);
            }
            Debug.Log($"CrowdBench: arm {_arm}, scene {_scene}, {Screen.width}x{Screen.height}, " +
                      $"{SystemInfo.graphicsDeviceType}, job workers {JobsUtility.JobWorkerCount}");
            if (Args.Has("--show-motion") || Args.Has("--show-normals"))
            {
                _debugPass = new BufferDebugPass(_lib.BufferDebug, Args.Has("--show-motion") ? BufferDebugPass.Buffer.Motion : BufferDebugPass.Buffer.Normals);
                RenderPipelineManager.beginCameraRendering += EnqueueMotion;
            }
            StartCoroutine(Plan());
        }

        ICrowdRenderer CreateArm(string arm) => arm switch
        {
            // "palette" is the surviving skinning variant's name in TD5's result rows.
            "bone" or "palette" => new BoneTextureCrowd(_lib),
            "stock" => new BaselineCrowd(_lib, transform),
            _ => throw new ArgumentException("--arm bone|stock (TD5 deleted the per-vertex variant)"),
        };

        // ---------------------------------------------------------------------------------
        // The rig: game camera, sun, ambient, ground and post-processing (benchmarks.md, the
        // bench scene; TD7's BenchRig makes the same one).

        void BuildRig()
        {
            foreach (var c in FindObjectsByType<Camera>(FindObjectsInactive.Include)) Destroy(c.gameObject);
            foreach (var l in FindObjectsByType<Light>(FindObjectsInactive.Include)) Destroy(l.gameObject);
            _pipeline = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset
                        ?? throw new InvalidOperationException("CrowdBench: URP is not the active pipeline");
            var camGo = new GameObject("CrowdCamera");
            _camera = camGo.AddComponent<Camera>();
            _camera.fieldOfView = VFovDeg;
            _camera.nearClipPlane = 1f;
            _camera.farClipPlane = 400f;
            _camera.allowHDR = true;
            _camera.allowMSAA = true;
            _camera.tag = "MainCamera";
            _camData = camGo.AddComponent<UniversalAdditionalCameraData>();
            _camData.renderPostProcessing = true;
            _camData.renderShadows = true;

            var sunGo = new GameObject("Sun");
            sunGo.transform.rotation = Quaternion.Euler(Args.GetFloat("--sun-elevation", 50), Args.GetFloat("--sun-yaw", 30), 0);
            _sun = sunGo.AddComponent<Light>();
            _sun.type = LightType.Directional;
            _sun.intensity = 1.2f;
            _sun.color = new Color(1f, 0.96f, 0.88f);
            _sun.shadows = LightShadows.Soft;
            RenderSettings.sun = _sun;
            var sh = new SphericalHarmonicsL2();
            sh.AddAmbientLight(new Color(0.36f, 0.40f, 0.48f));
            RenderSettings.ambientMode = AmbientMode.Custom;
            RenderSettings.ambientProbe = sh;

            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            Destroy(ground.GetComponent<Collider>());
            ground.transform.localScale = new Vector3(60, 1, 60);
            ground.GetComponent<MeshRenderer>().sharedMaterial = _lib.Ground;

            var vol = new GameObject("Post").AddComponent<Volume>();
            vol.isGlobal = true;
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            profile.Add<Bloom>(true).intensity.Override(0.6f);
            profile.Add<Tonemapping>(true).mode.Override(TonemappingMode.Neutral);
            var color = profile.Add<ColorAdjustments>(true);
            color.contrast.Override(8f);
            color.saturation.Override(5f);
            profile.Add<Vignette>(true).intensity.Override(0.22f);
            vol.sharedProfile = profile;
        }

        /// <summary>The roster still's light: the song's night look (the arena layout's moon, ambient, fog and grade).</summary>
        void NightRig()
        {
            var l = ArenaLighting.Parse(PlaybackData.Load().Layout("arena").text);
            NightLook.ApplyRenderSettings(l);
            NightLook.ApplyCamera(_camera, l);
            _sun.transform.rotation = l.MoonRotation;
            _sun.color = l.MoonColor;
            _sun.intensity = l.MoonIntensity;
            Destroy(FindAnyObjectByType<Volume>().gameObject);
            NightLook.CreateVolume(transform, l);
        }

        void SetViewWidth(float viewWidth)
        {
            _viewWidth = viewWidth;
            float hfov = 2 * Mathf.Atan(Mathf.Tan(VFovDeg * 0.5f * Mathf.Deg2Rad) * 16f / 9f);
            float distance = viewWidth / (2 * Mathf.Tan(hfov / 2));
            var rot = Quaternion.Euler(PitchDeg, 0, 0);
            _camera.transform.SetPositionAndRotation(-(rot * Vector3.forward) * distance, rot);
            float far = 0;
            foreach (var c in CrowdScenario.Footprint(_camera))
                far = Mathf.Max(far, Vector3.Distance(_camera.transform.position, new Vector3(c.x, 0, c.y)));
            _pipeline.shadowDistance = far + 5; // fitted to the view
        }

        void SetAntiAliasing(string aa)
        {
            if (aa == "taa")
            {
                _pipeline.msaaSampleCount = 1;
                _camData.antialiasing = AntialiasingMode.TemporalAntiAliasing;
            }
            else
            {
                _pipeline.msaaSampleCount = 4;
                _camData.antialiasing = AntialiasingMode.None;
            }
        }

        /// <summary>Unshadowed point lights orbiting over the crowd (S1 and S2 have none).</summary>
        void AddLights(int n)
        {
            var rng = new Unity.Mathematics.Random(7);
            for (int i = 0; i < n; i++)
            {
                var l = new GameObject("EffectLight" + i).AddComponent<Light>();
                l.type = LightType.Point;
                l.range = 6;
                l.intensity = 4;
                l.color = Color.HSVToRGB(rng.NextFloat(0.02f, 0.12f), 0.8f, 1f);
                l.shadows = LightShadows.None;
                _lights.Add(l);
            }
        }

        void MoveLights(float t)
        {
            for (int i = 0; i < _lights.Count; i++)
            {
                float a = t * 0.4f + i * 2.399f;
                float r = 3 + (i * 7 % 13);
                _lights[i].transform.position = new Vector3(Mathf.Cos(a) * r, 1.5f, Mathf.Sin(a) * r * 0.6f);
            }
        }

        // ---------------------------------------------------------------------------------
        // The scenario.

        void Rebuild(int n)
        {
            _n = n;
            foreach (var s in _slots) if (s.IsCreated) s.Dispose();
            if (_scene == "roster")
            {
                RosterRow();
                return;
            }
            _counts = CrowdScenario.Mix(_lib, n);
            _motion = _lib.Types.Select(CrowdScenario.MotionOf).ToArray();
            var footprint = CrowdScenario.Footprint(_camera);
            int off = Mathf.RoundToInt(n * 0.1f); // just off screen, for the shadow extension
            var all = CrowdScenario.Place(n, off, footprint, 6f, 12345u + (uint)n);
            // Deal the on-screen and off-screen slots to types in the same proportions.
            _slots = new NativeArray<CrowdScenario.Slot>[_lib.Types.Length];
            var offCounts = CrowdScenario.Mix(_lib, off);
            int onAt = 0, offAt = n;
            for (int i = 0; i < _slots.Length; i++)
            {
                var list = new List<CrowdScenario.Slot>();
                for (int k = 0; k < _counts[i]; k++) list.Add(all[onAt++]);
                for (int k = 0; k < offCounts[i] && offAt < all.Length; k++) list.Add(all[offAt++]);
                _slots[i] = new NativeArray<CrowdScenario.Slot>(list.ToArray(), Allocator.Persistent);
            }
            _t0 = Time.time;
            _framesAtRebuild = _frames;
        }

        /// <summary>
        /// The roster scene: each type a column of three (the boss one), left to right in roster
        /// order, facing the camera; with time held at 0 each stands at its slot's walk phase.
        /// </summary>
        void RosterRow()
        {
            _counts = new int[_lib.Types.Length];
            _motion = _lib.Types.Select(CrowdScenario.MotionOf).ToArray();
            _slots = new NativeArray<CrowdScenario.Slot>[_lib.Types.Length];
            var widths = _lib.Types.Select(t => Mathf.Max(1.3f, 2.6f * t.RadiusM)).ToArray();
            float x = -widths.Sum() / 2;
            const float FarRadius = 1000f; // a walk circle so large the slot is a straight line; at time 0 it stands at Center + (R, 0)
            for (int i = 0; i < _slots.Length; i++)
            {
                var t = _lib.Types[i];
                int k = t.Kind == "boss" ? 1 : 3;
                var list = new CrowdScenario.Slot[k];
                for (int j = 0; j < k; j++)
                    list[j] = new CrowdScenario.Slot
                    {
                        Center = new Unity.Mathematics.float2(x + widths[i] / 2 - FarRadius, (1 - j) * widths[i] - 1f),
                        Radius = FarRadius, Direction = -1, Theta0 = 0, Phase = 0.37f * (i + j), Scale = 1, Tint = 0,
                        Seed = (uint)(i * 7 + j),
                    };
                _counts[i] = k;
                _slots[i] = new NativeArray<CrowdScenario.Slot>(list, Allocator.Persistent);
                x += widths[i];
            }
            _t0 = Time.time;
            _framesAtRebuild = _frames;
        }

        void Update()
        {
            if (_slots.Length == 0) return;
            // --fixed-time: 1/60 s per frame, so motion between frames is a game's (for TAA stills).
            _frames++;
            float t = _timeOverride ?? (_fixedTime ? (_frames - _framesAtRebuild) / 60f : Time.time - _t0);
            _scenarioTime = t;
            MoveLights(t);
            var handles = new NativeArray<JobHandle>(_slots.Length, Allocator.Temp);
            for (int i = 0; i < _slots.Length; i++)
            {
                var output = _crowd.Instances(i, _slots[i].Length);
                handles[i] = new CrowdScenario.Job
                {
                    Slots = _slots[i], Out = output, M = _motion[i], Time = t, Combat = _combat,
                }.Schedule(_slots[i].Length, 64);
            }
            JobHandle.CompleteAll(handles);
            handles.Dispose();
            _crowd.Draw(_camera);
            if (_camera.targetTexture != null) _camera.Render(); // batch mode renders no frames of its own

            FrameTimingManager.CaptureFrameTimings();
            if (!_measuring) return;
            if (FrameTimingManager.GetLatestTimings(1, _timings) > 0)
            {
                if (_timings[0].gpuFrameTime > 0) _gpu.Add(_timings[0].gpuFrameTime);
                else _zeroGpu++;
                _main.Add(_timings[0].cpuMainThreadFrameTime);
                _render.Add(_timings[0].cpuRenderThreadFrameTime);
            }
            OnScreen(out int alive, out int dying);
            _onAlive.Add(alive);
            _onDying.Add(dying);
        }

        void OnScreen(out int alive, out int dying)
        {
            switch (_crowd)
            {
                case BoneTextureCrowd b: alive = b.OnScreenAlive; dying = b.OnScreenDying; break;
                case BaselineCrowd s: alive = s.OnScreenAlive; dying = s.OnScreenDying; break;
                default: alive = dying = 0; break;
            }
        }

        // ---------------------------------------------------------------------------------
        // Runs and the search.

        public sealed class Row
        {
            public int N;
            public double GpuP50, GpuP99, GpuMax, MainP50, MainP99, MainMax, RenderP50, RenderP99, RenderMax;
            public double OnScreenAlive, OnScreenDying;
            public int Frames, ZeroGpu;
            public bool Pass;
            public string Limit;
        }

        IEnumerator Plan()
        {
            yield return null;
            var stills = Args.Get("--stills");
            if (stills != null)
            {
                yield return Stills(stills);
                Quit();
                yield break;
            }
            float warmup = Args.GetFloat("--warmup", 5), seconds = Args.GetFloat("--seconds", 20);
            if (Args.Has("--search"))
            {
                yield return Search(warmup, seconds);
                Quit();
                yield break;
            }
            var ns = (Args.Get("--n", "2000")).Split(',').Select(s => int.Parse(s, CultureInfo.InvariantCulture)).ToList();
            bool first = true;
            foreach (var n in ns)
            {
                Row row = null;
                yield return Run(n, first ? warmup : 3, seconds, r => row = r);
                first = false;
            }
            Quit();
        }

        IEnumerator Run(int n, float warmup, float seconds, Action<Row> done)
        {
            Rebuild(n);
            float until = Time.realtimeSinceStartup + warmup;
            while (Time.realtimeSinceStartup < until) yield return null;
            _gpu.Clear(); _main.Clear(); _render.Clear(); _onAlive.Clear(); _onDying.Clear();
            _zeroGpu = 0;
            _measuring = true;
            until = Time.realtimeSinceStartup + seconds;
            while (Time.realtimeSinceStartup < until) yield return null;
            _measuring = false;
            var row = Summarize(n, seconds);
            Write(row, seconds);
            done(row);
        }

        Row Summarize(int n, float seconds)
        {
            var r = new Row { N = n, Frames = _main.Count, ZeroGpu = _zeroGpu };
            (r.GpuP50, r.GpuP99, r.GpuMax) = Stats(_gpu);
            (r.MainP50, r.MainP99, r.MainMax) = Stats(_main);
            (r.RenderP50, r.RenderP99, r.RenderMax) = Stats(_render);
            r.OnScreenAlive = _onAlive.Count == 0 ? 0 : _onAlive.Average();
            r.OnScreenDying = _onDying.Count == 0 ? 0 : _onDying.Average();
            bool gpu = r.GpuP99 <= GpuLine && r.GpuMax <= 2 * GpuLine;
            bool main = r.MainP99 <= CpuLine && r.MainMax <= 2 * CpuLine;
            bool render = r.RenderP99 <= CpuLine && r.RenderMax <= 2 * CpuLine;
            r.Pass = gpu && main && render && _gpu.Count > 0;
            // The limiting resource: the one closest to (or furthest past) its line at p99.
            var ratios = new[] { ("gpu", r.GpuP99 / GpuLine), ("main", r.MainP99 / CpuLine), ("render", r.RenderP99 / CpuLine) };
            r.Limit = ratios.OrderByDescending(x => x.Item2).First().Item1;
            return r;
        }

        static (double, double, double) Stats(List<double> v)
        {
            if (v.Count == 0) return (0, 0, 0);
            var s = v.OrderBy(x => x).ToList();
            double P(double q) => s[Math.Min(s.Count - 1, (int)(q * s.Count))];
            return (P(0.5), P(0.99), s[s.Count - 1]);
        }

        void Write(Row r, float seconds)
        {
            var ci = CultureInfo.InvariantCulture;
            var sb = new StringBuilder();
            sb.Append('{');
            sb.AppendFormat(ci, "\"task\": \"td5\", \"scene\": \"{0}\", \"arm\": \"{1}\", \"n\": {2}, \"viewWidthM\": {3}, ",
                _scene.ToUpperInvariant(), _arm, r.N, _viewWidth);
            sb.AppendFormat(ci, "\"aa\": \"{0}\", \"seconds\": {1}, \"frames\": {2}, \"zeroGpuFrames\": {3}, ",
                _pipeline.msaaSampleCount > 1 ? "msaa4" : "taa", seconds, r.Frames, r.ZeroGpu);
            sb.AppendFormat(ci, "\"gpuP50Ms\": {0:F3}, \"gpuP99Ms\": {1:F3}, \"gpuMaxMs\": {2:F3}, ", r.GpuP50, r.GpuP99, r.GpuMax);
            sb.AppendFormat(ci, "\"mainP50Ms\": {0:F3}, \"mainP99Ms\": {1:F3}, \"mainMaxMs\": {2:F3}, ", r.MainP50, r.MainP99, r.MainMax);
            sb.AppendFormat(ci, "\"renderP50Ms\": {0:F3}, \"renderP99Ms\": {1:F3}, \"renderMaxMs\": {2:F3}, ", r.RenderP50, r.RenderP99, r.RenderMax);
            var c = _crowd.Counters;
            sb.AppendFormat(ci, "\"onScreenAlive\": {0:F0}, \"onScreenDying\": {1:F0}, \"instances\": {2}, \"shadowCasters\": {3}, \"draws\": {4}, \"skinnedVertices\": {5}, ",
                r.OnScreenAlive, r.OnScreenDying, c.Instances, c.ShadowCasters, c.Draws, c.SkinnedVertices);
            sb.AppendFormat(ci, "\"ablate\": \"" + Args.Get("--ablate", "") + "\", \"lights\": {0}, \"api\": \"{1}\", \"gpu\": \"{2}\", \"backbuffer\": [{3}, {4}], \"jobWorkers\": {5}, ",
                _lights.Count, SystemInfo.graphicsDeviceType, SystemInfo.graphicsDeviceName, Screen.width, Screen.height, JobsUtility.JobWorkerCount);
            sb.AppendFormat(ci, "\"limit\": \"{0}\", \"pass\": {1}", r.Limit, r.Pass ? "true" : "false");
            sb.Append("}\n");
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_out)));
            File.AppendAllText(_out, sb.ToString());
            Debug.Log("CrowdBench: " + sb);
        }

        /// <summary>
        /// The largest N that passes (benchmarks.md, The sweep): three short probes fit a line per
        /// resource; the lowest crossing of its line is the candidate; full-length runs confirm it
        /// passes and that 5% more fails, bisecting where they disagree.
        /// </summary>
        IEnumerator Search(float warmup, float seconds)
        {
            var probes = Args.Get("--probes", "1000,3000,6000").Split(',').Select(s => int.Parse(s, CultureInfo.InvariantCulture)).ToArray();
            int ceiling = Args.GetInt("--ceiling", Capacity);
            var pts = new List<Row>();
            bool first = true;
            foreach (var n in probes)
            {
                Row r = null;
                yield return Run(n, first ? warmup : 3, Args.GetFloat("--probe-seconds", 8), x => r = x);
                first = false;
                pts.Add(r);
            }
            double Crossing(Func<Row, double> f, double line)
            {
                double mx = pts.Average(p => p.N), my = pts.Average(f);
                double sxy = pts.Sum(p => (p.N - mx) * (f(p) - my)), sxx = pts.Sum(p => (p.N - mx) * (p.N - mx));
                double slope = sxx > 0 ? sxy / sxx : 0;
                if (slope <= 1e-9) return double.MaxValue;
                return mx + (line - my) / slope;
            }
            double cand = Math.Min(Crossing(p => p.GpuP99, GpuLine), Math.Min(Crossing(p => p.MainP99, CpuLine), Crossing(p => p.RenderP99, CpuLine)));
            int n0 = (int)Math.Max(100, Math.Min(ceiling, cand));
            Debug.Log($"CrowdBench: search candidate {n0} from probes {string.Join(",", probes)}");
            int lo = 0, hi = int.MaxValue;
            int next = n0;
            Row best = null;
            for (int guard = 0; guard < 12; guard++)
            {
                Row r = null;
                yield return Run(next, 3, seconds, x => r = x);
                if (r.Pass) { lo = next; best = r; }
                else hi = next;
                if (lo >= ceiling) break;
                if (hi != int.MaxValue && lo > 0 && hi <= lo * 1.05 + 1) break;
                if (hi == int.MaxValue) next = Math.Min(ceiling, (int)Math.Ceiling(lo * (lo == n0 ? 1.05 : 1.15)));
                else if (lo == 0) next = (int)(hi / (hi == n0 ? 1.05 : 1.15));
                else next = (int)Math.Round(Math.Sqrt((double)lo * hi));
                if (next < 100) break;
            }
            var ci = CultureInfo.InvariantCulture;
            File.AppendAllText(_out, string.Format(ci,
                "{{\"task\": \"td5\", \"search\": true, \"scene\": \"{0}\", \"arm\": \"{1}\", \"viewWidthM\": {2}, \"maxN\": {3}, \"firstFail\": {4}, \"candidate\": {5}, \"limit\": \"{6}\", \"onScreenAtMax\": {7:F0}, \"ceiling\": {8}}}\n",
                _scene.ToUpperInvariant(), _arm, _viewWidth, lo, hi == int.MaxValue ? -1 : hi, n0, best?.Limit ?? "none",
                best == null ? 0 : best.OnScreenAlive + best.OnScreenDying, ceiling));
        }

        // ---------------------------------------------------------------------------------
        // Stills.

        IEnumerator Stills(string dir)
        {
            var widths = Args.Get("--still-widths", "30,40,55").Split(',').Select(s => float.Parse(s, CultureInfo.InvariantCulture));
            float density = Args.GetFloat("--density", 1.6f);
            float at = Args.GetFloat("--time", 9.3f);
            foreach (var w in widths)
            {
                SetViewWidth(w);
                int n = _scene == "roster" ? 0 : Mathf.RoundToInt(CrowdScenario.Area(CrowdScenario.Footprint(_camera)) * density);
                Rebuild(n);
                _timeOverride = null;
                if (Args.Has("--live"))
                {
                    // In motion: run to the shot's time (in fixed time, `at` s of frames).
                    while ((_fixedTime ? (_frames - _framesAtRebuild) / 60f : Time.time - _t0) < at) yield return null;
                }
                else
                {
                    // Settle a few frames (LOD state, previous buffers), then freeze time for the shot.
                    for (int i = 0; i < 30; i++) yield return null;
                    _timeOverride = at;
                    for (int i = 0; i < 4; i++) yield return null;
                }
                yield return new WaitForEndOfFrame();
                var shot = Grab();
                var small = Downscale(shot, Args.GetInt("--still-width", 1280));
                var tag = Args.Get("--ablate", "");
                var dye = Args.Get("--type-dye");
                var name = $"crowd-{_arm}-{_scene}-{w:0}m{(CrowdType.ClassicColors ? "-classic" : dye != null ? "-dye" + dye : "")}{(tag.Length > 0 ? "-no" + tag : "")}{(_lights.Count > 0 ? "-lights" : "")}{(_pipeline.msaaSampleCount == 1 ? "-taa" : "")}{(Args.Has("--show-motion") ? "-motion" : Args.Has("--show-normals") ? "-normals" : "")}.jpg";
                RenderTag.Save(small, Path.Combine(dir, name), $"td5 crowd bench still: {_arm} {_scene} {w} m, n {n}, t {at}", 82);
                if (Args.Has("--crop"))
                {
                    // A 100% crop around the screen centre, for detail.
                    var crop = new Texture2D(800, 450, TextureFormat.RGB24, false);
                    bool bottom = Args.Get("--crop") == "bottom"; // the bottom edge, where off-screen monsters' shadows enter
                    crop.SetPixels(shot.GetPixels(shot.width / 2 - 400, bottom ? 0 : shot.height / 2 - 225, 800, 450));
                    crop.Apply();
                    RenderTag.Save(crop, Path.Combine(dir, name.Replace(".jpg", bottom ? "-crop-bottom.jpg" : "-crop.jpg")), $"td5 crowd bench still (100% crop): {_arm} {_scene} {w} m, n {n}, t {at}", 85);
                    Destroy(crop);
                }
                Debug.Log($"CrowdBench: still {name} ({n} placed, {shot.width}x{shot.height})");
                Destroy(shot);
                Destroy(small);
            }
            _timeOverride = null;
        }

        /// <summary>
        /// The frame just rendered: the screen, or in batch mode (no window) the camera's offscreen
        /// target, which <see cref="Start"/> gives it there and <see cref="Update"/> renders into.
        /// </summary>
        Texture2D Grab()
        {
            var target = _camera.targetTexture;
            if (target == null) return ScreenCapture.CaptureScreenshotAsTexture();
            var resolved = RenderTexture.GetTemporary(target.width, target.height, 0, RenderTextureFormat.ARGB32);
            Graphics.Blit(target, resolved);
            var prev = RenderTexture.active;
            RenderTexture.active = resolved;
            var tex = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(resolved);
            return tex;
        }

        static Texture2D Downscale(Texture2D src, int width)
        {
            int height = Mathf.RoundToInt(width * src.height / (float)src.width);
            var rt = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            Graphics.Blit(src, rt);
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(width, height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            return tex;
        }

        void Quit()
        {
            Debug.Log("CrowdBench: done");
            Application.Quit(0);
        }

        BufferDebugPass _debugPass;

        void EnqueueMotion(ScriptableRenderContext ctx, Camera cam)
        {
            if (cam == _camera) _camData.scriptableRenderer.EnqueuePass(_debugPass);
        }

        void OnDestroy()
        {
            if (_debugPass != null)
            {
                RenderPipelineManager.beginCameraRendering -= EnqueueMotion;
                _debugPass.Dispose();
            }
            _crowd?.Dispose();
            foreach (var s in _slots) if (s.IsCreated) s.Dispose();
        }
    }
}
