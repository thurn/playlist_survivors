using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Champions.Render.Crowd;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.Rendering;
using Debug = UnityEngine.Debug;

namespace Champions.Client.Bench
{
    /// <summary>
    /// The edge crowd's test scene (`--edge-crowd N`, plan.md Risks: the edge renderer measured on its own before
    /// anything is built on it): N edge cuts of one type (`--edge-type ID`, default skeleton) walking toward the
    /// camera in a field at the game camera's angle, each at its own phase, heading and pace, wrapping round the
    /// field; 20 a second break into shards on a blow from the field's centre. Each kill hides its slot among the
    /// spares and brings back the one that has waited longest (about 2.9 s), so exactly N stay alive. Song time is
    /// the frame time (`--fixed-time` for a reproducible run).
    ///
    /// In batch mode (`champions play --headless`) nothing draws the camera on its own, so the bench draws it by
    /// hand into a 1080p target every frame and times it: from before the crowd's passes to the GPU's completion of
    /// the frame (a 1-pixel readback), alternating blocks of 30 frames with the crowd and 30 without, so the
    /// difference is the crowd's cost with the CPU's submission included: an upper bound on its GPU time. The time
    /// the camera's Render took to return (the CPU's submission) is reported beside it. A
    /// development player also reports the GPU recorders of the crowd's and shatter's compute passes and URP's
    /// object passes (which hold only the crowd here), where the platform supports them (Metal does not). A windowed
    /// player draws the scene itself and records Unity's frame timings (FrameTimingManager's GPU time) instead. It
    /// logs percentiles after the 2 s warm-up and writes them to `--bench-out PATH` (JSON).
    /// </summary>
    public sealed class EdgeCrowdBench : IDisposable
    {
        public const float PitchDeg = 60f, FovDeg = 30f;
        const float Spacing = 0.95f, KillsPerSecond = 20f, WarmupSeconds = 2f, BlowForce = 1.85f;
        const int Spares = 58; // about 2.9 s of kills: how long a broken slot waits to come back
        const int Block = 30;
        static readonly Color32 Tint = new Color32(90, 220, 255, 255);
        static readonly string[] Recorders =
            { "EdgeCrowd.Cull+Palette", "EdgeShatter.Seed+Integrate", "DrawOpaqueObjects", "DrawTransparentObjects" };

        readonly Camera _camera;
        readonly EdgeCrowd _crowd;
        readonly EdgeShatter _shatter;
        readonly int _type, _walk, _slots, _alive;
        readonly float _walkLength, _speed, _width, _depth;
        readonly Vector3[] _home;
        readonly float[] _phase, _yaw, _pace;
        readonly bool[] _hidden;
        readonly Queue<int> _spares = new Queue<int>();
        readonly System.Random _rng = new System.Random(8);
        readonly string _out;
        readonly RenderTexture _target;
        readonly Plane[] _planes = new Plane[6];
        readonly List<double> _on = new List<double>(), _off = new List<double>(), _onCpu = new List<double>(), _offCpu = new List<double>();
        readonly Dictionary<string, List<double>> _gpu = new Dictionary<string, List<double>>();
        readonly Dictionary<string, Recorder> _recorders = new Dictionary<string, Recorder>();
        readonly List<double> _frameGpu = new List<double>();
        readonly FrameTiming[] _timings = new FrameTiming[1];
        readonly List<int> _onScreenCounts = new List<int>(), _killing = new List<int>();
        double _kills;
        int _frame, _serial, _onScreen;
        bool _reported;

        EdgeCrowdBench(Camera camera, EdgeCutLibrary lib, int type, int alive, string outPath)
        {
            _camera = camera;
            _type = type;
            _alive = alive;
            _out = outPath;
            var t = lib.Types[type];
            _walk = t.Clip("walk") >= 0 ? t.Clip("walk") : 0;
            _walkLength = t.Clips[_walk].Length;
            _speed = t.Clips[_walk].StrideM > 0 ? t.Clips[_walk].StrideM / _walkLength : 1.5f;
            _slots = alive + Spares;
            _crowd = new EdgeCrowd(lib, _slots);
            _shatter = new EdgeShatter(lib, _crowd.Palette);
            int cols = Mathf.CeilToInt(Mathf.Sqrt(_slots * 1.15f));
            int rows = Mathf.CeilToInt((float)_slots / cols);
            _width = cols * Spacing;
            _depth = rows * Spacing;
            _home = new Vector3[_slots];
            _phase = new float[_slots];
            _yaw = new float[_slots];
            _pace = new float[_slots];
            _hidden = new bool[_slots];
            for (int i = 0; i < _slots; i++)
            {
                float jx = (float)_rng.NextDouble() - 0.5f, jz = (float)_rng.NextDouble() - 0.5f;
                _home[i] = new Vector3(((i % cols) + 0.5f + 0.4f * jx) * Spacing - 0.5f * _width, 0,
                    ((i / cols) + 0.5f + 0.4f * jz) * Spacing - 0.5f * _depth);
                _phase[i] = (float)_rng.NextDouble() * _walkLength;
                _yaw[i] = Mathf.PI + 0.35f * ((float)_rng.NextDouble() - 0.5f);
                _pace[i] = 0.9f + 0.2f * (float)_rng.NextDouble();
                // The spares, spread through the field (a stride coprime with the slot count), wait for the first kills.
                _hidden[i] = (long)i * 7919 % _slots >= alive;
                if (_hidden[i]) _spares.Enqueue(i);
            }
            // The game camera's angle over the field, far enough to hold most of it.
            float distance = 0.95f * Mathf.Max(_width, _depth) / (2 * Mathf.Tan(0.5f * FovDeg * Mathf.Deg2Rad) * 16f / 9f) * 1.6f;
            camera.fieldOfView = FovDeg;
            camera.transform.rotation = Quaternion.Euler(PitchDeg, 0, 0);
            camera.transform.position = new Vector3(0, 0.9f, 0) - camera.transform.forward * distance;
            camera.farClipPlane = Mathf.Max(camera.farClipPlane, distance * 3);
            if (Application.isBatchMode)
            {
                _target = new RenderTexture(1920, 1080, 24, RenderTextureFormat.ARGB32) { name = "EdgeCrowdBench" };
                _target.Create();
                foreach (var name in Recorders)
                {
                    var r = Recorder.Get(name);
                    r.enabled = true;
                    _recorders[name] = r;
                    _gpu[name] = new List<double>();
                }
            }
            Debug.Log($"EdgeCrowdBench: {alive} {t.Id} alive in {_slots} slots, a {_width:F0} x {_depth:F0} m field, camera {distance:F1} m away; " +
                      $"{t.Triangles} body triangles and {t.Segments} line segments each; GPU recorder {(SystemInfo.supportsGpuRecorder ? "supported" : "unsupported")}");
        }

        /// <summary>The bench for `--edge-crowd N`, or null with why (no baked library, no such type).</summary>
        public static EdgeCrowdBench Start(Camera camera, int alive, string typeId, string outPath, out string error)
        {
            error = null;
            var lib = EdgeCutLibrary.Load();
            if (lib == null || lib.Types.Length == 0)
            {
                error = "no baked edge cuts in this build (run `champions edgecut skeleton`, then build)";
                return null;
            }
            int type = lib.IndexOf(typeId ?? "skeleton");
            if (type < 0)
            {
                error = $"no edge cut '{typeId ?? "skeleton"}' (baked: {string.Join(", ", lib.Types.Select(x => x.Id))})";
                return null;
            }
            return new EdgeCrowdBench(camera, lib, type, alive, outPath);
        }

        /// <summary>One frame at song time <paramref name="t"/>.</summary>
        public void Frame(double t)
        {
            bool timed = _target != null;
            bool crowdOn = !timed || (_frame / Block) % 2 == 0;
            bool measuring = timed && t >= WarmupSeconds;
            if (measuring) ReadRecorders(crowdOn);
            var watch = Stopwatch.StartNew();
            if (crowdOn) Write((float)t);
            if (timed)
            {
                var previous = _camera.targetTexture;
                _camera.targetTexture = _target;
                try { _camera.Render(); }
                finally { _camera.targetTexture = previous; }
                double submitted = watch.Elapsed.TotalMilliseconds;
                var req = AsyncGPUReadback.Request(_target, 0, 0, 1, 0, 1, 0, 1, TextureFormat.RGBA32);
                req.WaitForCompletion();
                if (measuring)
                {
                    (crowdOn ? _on : _off).Add(watch.Elapsed.TotalMilliseconds);
                    (crowdOn ? _onCpu : _offCpu).Add(submitted);
                }
                if (crowdOn) Requeue();
            }
            else if (t >= WarmupSeconds)
            {
                FrameTimingManager.CaptureFrameTimings();
                if (FrameTimingManager.GetLatestTimings(1, _timings) > 0 && _timings[0].gpuFrameTime > 0) _frameGpu.Add(_timings[0].gpuFrameTime);
            }
            if (t >= WarmupSeconds && crowdOn) _onScreenCounts.Add(_onScreen);
            _frame++;
        }

        /// <summary>Queues this frame's crowd and shards again for another draw of the camera (the stills').</summary>
        public void Requeue()
        {
            _crowd.Queue(_camera);
            _shatter.Queue(_camera);
        }

        void Write(float t)
        {
            _kills += KillsPerSecond * Time.deltaTime;
            _killing.Clear();
            for (int tries = 0; _killing.Count < (int)_kills && tries < 100; tries++)
            {
                int k = _rng.Next(_slots);
                if (!_hidden[k] && !_killing.Contains(k)) _killing.Add(k);
            }
            _kills -= _killing.Count;
            GeometryUtility.CalculateFrustumPlanes(_camera, _planes);
            var slots = _crowd.Instances(_type, _slots);
            int onScreen = 0;
            for (int i = 0; i < _slots; i++)
            {
                if (_hidden[i])
                {
                    slots[i] = new MonsterInstance { Flags = (uint)CrowdFlags.Hidden };
                    continue;
                }
                float walked = t * _speed * _pace[i] % _depth;
                var p = _home[i] + new Vector3(Mathf.Sin(_yaw[i]), 0, Mathf.Cos(_yaw[i])) * walked;
                p.z = Mathf.Repeat(p.z + 0.5f * _depth, _depth) - 0.5f * _depth;
                float time = _phase[i] + t * _pace[i];
                var m = new MonsterInstance
                {
                    Position = p, Yaw = _yaw[i], ClipA = (uint)_walk, TimeA = time, ClipB = (uint)_walk, TimeB = time, Blend = 1,
                    Scale = 1, Tint = MonsterInstance.Pack(Tint), Flags = (uint)CrowdFlags.Alive,
                };
                if (GeometryUtility.TestPlanesAABB(_planes, new Bounds(p + Vector3.up, new Vector3(1, 2, 1)))) onScreen++;
                if (_killing.Contains(i))
                {
                    m.Flags = (uint)CrowdFlags.Dying;
                    var away = new Vector2(p.x, p.z).normalized;
                    _shatter.Break(_type, i, m, ++_serial, t, away.x, away.y, BlowForce);
                    _hidden[i] = true;
                    _spares.Enqueue(i);
                    _hidden[_spares.Dequeue()] = false; // shown from the next frame
                }
                slots[i] = m;
            }
            _onScreen = onScreen;
            _crowd.Draw(_camera);
            _shatter.Frame(t, _camera);
        }

        void ReadRecorders(bool crowdOn)
        {
            if (!crowdOn) return; // a recorder reports the previous frame, which drew the crowd only in an "on" block
            foreach (var kv in _recorders)
                if (kv.Value.isValid && kv.Value.gpuElapsedNanoseconds > 0) _gpu[kv.Key].Add(kv.Value.gpuElapsedNanoseconds / 1e6);
        }

        static double Percentile(List<double> v, double p)
        {
            if (v.Count == 0) return double.NaN;
            var s = v.OrderBy(x => x).ToList();
            return s[Math.Min(s.Count - 1, (int)Math.Floor(p * s.Count))];
        }

        /// <summary>Logs the timings and writes `--bench-out`, once.</summary>
        public void Report()
        {
            if (_reported) return;
            _reported = true;
            string F(double x) => double.IsNaN(x) ? "null" : x.ToString("F3", CultureInfo.InvariantCulture);
            var sb = new StringBuilder();
            sb.Append("{\n  \"alive\": ").Append(_alive).Append(",\n  \"slots\": ").Append(_slots)
                .Append(",\n  \"device\": \"").Append(SystemInfo.graphicsDeviceName).Append("\",\n  \"development\": ")
                .Append(Debug.isDebugBuild ? "true" : "false")
                .Append(",\n  \"onScreenMedian\": ").Append(_onScreenCounts.Count > 0 ? _onScreenCounts.OrderBy(x => x).ElementAt(_onScreenCounts.Count / 2) : 0)
                .Append(",\n  \"breaks\": ").Append(_shatter.Breaks)
                .Append(",\n  \"syncedFrameMs\": {\"crowd\": {\"frames\": ").Append(_on.Count).Append(", \"p50\": ").Append(F(Percentile(_on, 0.5)))
                .Append(", \"p99\": ").Append(F(Percentile(_on, 0.99))).Append("}, \"empty\": {\"frames\": ").Append(_off.Count)
                .Append(", \"p50\": ").Append(F(Percentile(_off, 0.5))).Append(", \"p99\": ").Append(F(Percentile(_off, 0.99)))
                .Append("}, \"crowdP50MinusEmptyP50\": ").Append(F(Percentile(_on, 0.5) - Percentile(_off, 0.5)))
                .Append(", \"submittedP50\": {\"crowd\": ").Append(F(Percentile(_onCpu, 0.5))).Append(", \"empty\": ")
                .Append(F(Percentile(_offCpu, 0.5))).Append("}},\n  \"gpuRecordersMs\": {");
            bool first = true;
            foreach (var kv in _gpu)
            {
                sb.Append(first ? "\n    " : ",\n    ").Append('"').Append(kv.Key).Append("\": {\"frames\": ").Append(kv.Value.Count)
                    .Append(", \"p50\": ").Append(F(Percentile(kv.Value, 0.5))).Append(", \"p99\": ").Append(F(Percentile(kv.Value, 0.99))).Append('}');
                first = false;
            }
            sb.Append("\n  },\n  \"windowedGpuFrameMs\": {\"frames\": ").Append(_frameGpu.Count).Append(", \"p50\": ")
                .Append(F(Percentile(_frameGpu, 0.5))).Append(", \"p99\": ").Append(F(Percentile(_frameGpu, 0.99))).Append("}\n}\n");
            Debug.Log("EdgeCrowdBench: " + sb.ToString().Replace("\n", " "));
            if (_out != null)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_out)));
                File.WriteAllText(_out, sb.ToString());
            }
        }

        public void Dispose()
        {
            _shatter.Dispose();
            _crowd.Dispose();
            if (_target != null) _target.Release();
        }
    }
}
