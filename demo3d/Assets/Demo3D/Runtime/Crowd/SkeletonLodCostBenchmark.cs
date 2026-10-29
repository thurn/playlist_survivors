using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Demo3D.Runtime;
using Demo3D.Runtime.Crowd;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Rendering;
using Debug = UnityEngine.Debug;

namespace Demo3D.Runtime.Crowd
{
    /// <summary>
    /// Paired offscreen Metal cost comparison. Times CPU submission + actual GPU completion
    /// with a one-pixel readback barrier; never labels this as GPU-only or Windows cost.
    /// All candidates use identical counts, poses, camera, common bounds and cuts=none.
    /// </summary>
    public sealed class SkeletonLodCostBenchmark : MonoBehaviour
    {
        sealed class Row
        {
            public string Variant, Pose;
            public int Instances, Visible, ShadowCasters, Draws, Triangles, Vertices, Block, Order;
            public float ViewWidth;
            public double[] Milliseconds;
            public double MedianMs;
        }

        SkeletonLodPreview _preview;
        readonly List<UnityEngine.Object> _owned = new List<UnityEngine.Object>();
        readonly List<BoneTextureCrowd> _renderers = new List<BoneTextureCrowd>();
        readonly List<Row> _rows = new List<Row>();
        readonly List<double> _times = new List<double>();
        readonly Dictionary<int, int> _visibility = new Dictionary<int, int>();
        int _capacity, _samples, _blocks, _configuration, _block, _order, _frame = -8;
        (int Count, float Width, string Pose)[] _configurations;
        string _output;
        Cuts _previousCuts;
        bool _ready;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            if (!Args.Has("--lod-cost")) return;
            new GameObject("Skeleton LOD cost benchmark").AddComponent<SkeletonLodCostBenchmark>();
        }

        void Start()
        {
            try { InitializeRun(); }
            catch (Exception error) { Debug.LogException(error); Application.Quit(1); }
        }

        void InitializeRun()
        {
            if (SystemInfo.graphicsDeviceType != GraphicsDeviceType.Metal)
                throw new Exception("LOD cost comparison requires Metal");
            _preview = UnityEngine.Object.FindAnyObjectByType<SkeletonLodPreview>();
            if (_preview == null) throw new Exception("cost benchmark requires the isolated preview scene");
            _capacity = Args.Has("--cost-smoke") ? 256 : 4096;
            _samples = Args.Has("--cost-smoke") ? 6 : 24;
            _blocks = Args.Has("--cost-smoke") ? 1 : 3;
            _output = Args.Get("--cost-out", "captures/skeleton-lod-cost/results.json");
            _previousCuts = Cuts.Active;
            _preview.Initialize(); _preview.enabled = false;
            foreach (var camera in _preview.Cameras) camera.enabled = false;
            _preview.PresentationCamera.enabled = false;
            var common = CommonBounds(_preview.Library.Types);
            Cuts.Active = Cuts.Parse("none");
            foreach (var type in _preview.Library.Types)
            {
                var lib = UnityEngine.Object.Instantiate(_preview.Library); _owned.Add(lib);
                lib.CullShader = UnityEngine.Object.Instantiate(lib.CullShader); _owned.Add(lib.CullShader);
                lib.PaletteShader = UnityEngine.Object.Instantiate(lib.PaletteShader); _owned.Add(lib.PaletteShader);
                var copy = JsonUtility.FromJson<CrowdType>(JsonUtility.ToJson(type));
                copy.Clips = (CrowdClip[])type.Clips.Clone();
                for (int c = 0; c < copy.Clips.Length; c++)
                {
                    copy.Clips[c].CenterY = common[c].CenterY;
                    copy.Clips[c].Radius = common[c].Radius;
                    copy.Clips[c].HeightM = common[c].HeightM;
                }
                lib.Types = new[] { copy };
                var renderer = new BoneTextureCrowd(lib) { Clock = () => 1 };
                renderer.Initialize(new[] { copy.Id }, _capacity);
                _renderers.Add(renderer);
            }
            var ns = Args.Has("--cost-smoke") ? new[] { 256 } : new[] { 1024, 4096 };
            var poses = Args.Has("--cost-smoke") ? new[] { "walk" } : new[] { "walk", "attack", "crossfade" };
            var widths = Args.Has("--cost-smoke") ? new[] { 30f } : new[] { 30f, 55f };
            _configurations = (from n in ns from w in widths from p in poses select (n, w, p)).ToArray();
            QualitySettings.vSyncCount = 0; Application.targetFrameRate = -1;
            _ready = true;
        }

        void Update()
        {
            if (!_ready) return;
            try { Frame(); }
            catch (Exception error)
            {
                Debug.LogException(error); _ready = false; Application.Quit(1);
            }
        }

        void Frame()
        {
            var config = _configurations[_configuration];
            // Latin-square order balances warm-device/thermal/order effects.
            int v = (_order + _block) % 3;
            var type = _preview.Library.Types[v];
            var cam = _preview.Cameras[v];
            float distance = config.Width / (2 * Mathf.Tan(15 * Mathf.Deg2Rad) * (16f / 9));
            var rotation = Quaternion.Euler(55, 0, 0);
            cam.transform.SetPositionAndRotation(-(rotation * Vector3.forward) * distance, rotation);
            int a = type.Clip(config.Pose == "crossfade" ? "hit" : config.Pose), b = type.Clip("walk");
            if (a < 0 || b < 0) throw new Exception("missing benchmark clip");
            var timer = Stopwatch.StartNew();
            var data = _renderers[v].Instances(0, config.Count);
            int side = (int)Math.Ceiling(Math.Sqrt(config.Count));
            for (int i = 0; i < config.Count; i++)
            {
                float phase = ((i * 17 + Math.Max(0, _frame) * 7) % 997) / 997f;
                data[i] = new MonsterInstance
                {
                    Position = new Vector3(((i % side + .5f) / side - .5f) * 25,
                        0, ((i / side + .5f) / side - .5f) * 14),
                    Scale = .8f, Yaw = (i % 8) * Mathf.PI / 4,
                    ClipA = (uint)a, TimeA = phase * type.Clips[a].Length,
                    ClipB = (uint)b, TimeB = phase * type.Clips[b].Length,
                    Blend = config.Pose == "crossfade" ? .5f : 1,
                    Tint = 0xffffffff, Flags = (uint)CrowdFlags.Alive,
                };
            }
            _renderers[v].Draw(cam);
            cam.Render();
            var barrier = AsyncGPUReadback.Request(cam.targetTexture, 0, 0, 1, 0, 1, 0, 1);
            barrier.WaitForCompletion();
            if (barrier.hasError) throw new Exception("GPU completion barrier failed");
            AsyncGPUReadback.WaitAllRequests(); // counters must be current, not last variant's
            timer.Stop();
            if (_frame >= 0) _times.Add(timer.Elapsed.TotalMilliseconds);
            if (++_frame < _samples) return;
            var counters = _renderers[v].Counters;
            if (_visibility.TryGetValue(_block, out int expected) && counters.Visible != expected)
                throw new Exception($"unequal culling: {type.Id} {counters.Visible} vs {expected}");
            _visibility[_block] = counters.Visible;
            if (counters.Visible != config.Count || counters.ShadowCasters != config.Count)
                throw new Exception($"benchmark must draw all {config.Count} instances/shadows, got {counters.Visible}/{counters.ShadowCasters}");
            var sorted = _times.OrderBy(t => t).ToArray();
            _rows.Add(new Row { Variant = type.Id, Pose = config.Pose, Instances = config.Count,
                Visible = counters.Visible, ShadowCasters = counters.ShadowCasters,
                Draws = counters.Draws, Triangles = type.Triangles, Vertices = type.Vertices,
                ViewWidth = config.Width, Block = _block, Order = _order, Milliseconds = _times.ToArray(),
                MedianMs = (sorted[(sorted.Length - 1) / 2] + sorted[sorted.Length / 2]) / 2 });
            Write(_output, _rows);
            Debug.Log($"LOD cost: {type.Id} N={config.Count} {config.Width}m {config.Pose} block {_block}: {_rows.Last().MedianMs:F3} ms inclusive synchronized render latency");
            _times.Clear(); _frame = -8;
            if (++_order < 3) return;
            _order = 0;
            if (++_block < _blocks) return;
            _block = 0; _visibility.Clear();
            if (++_configuration < _configurations.Length) return;
            Debug.Log("LOD cost complete: " + _output);
            _ready = false; Application.Quit();
        }

        void OnDestroy()
        {
            AsyncGPUReadback.WaitAllRequests();
            foreach (var r in _renderers) r.Dispose();
            foreach (var o in _owned) UnityEngine.Object.Destroy(o);
            if (_preview != null) _preview.DisposePreview();
            if (_previousCuts != null) Cuts.Active = _previousCuts;
        }

        static CrowdClip[] CommonBounds(CrowdType[] types)
        {
            var result = (CrowdClip[])types[0].Clips.Clone();
            for (int c = 0; c < result.Length; c++)
            {
                float y = types.Average(t => t.Clips[c].CenterY);
                result[c].CenterY = y;
                result[c].Radius = types.Max(t => t.Clips[c].Radius + Mathf.Abs(y - t.Clips[c].CenterY));
                result[c].HeightM = types.Max(t => t.Clips[c].HeightM);
            }
            return result;
        }

        static void Write(string path, List<Row> rows)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            File.WriteAllText(path, JsonConvert.SerializeObject(new
            {
                Method = "CPU submission plus GPU completion (one-pixel readback); not GPU-only",
                Device = SystemInfo.graphicsDeviceName, Api = SystemInfo.graphicsDeviceType.ToString(),
                Resolution = "1920x1080, MSAA4, 55deg, FOV30; one timed render per player Update", Cuts = "none; candidate casts own shadow",
                TimingScope = "instance upload, cull, palette, real crowd draws, URP passes, GPU completion and counter drain",
                WarmupFramesPerBlock = 8, Rows = rows,
            }, Formatting.Indented));
        }
    }
}
