using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Champions.Render;
using Champions.Render.Crowd;
using UnityEngine;
using UnityEngine.Rendering;
using Debug = UnityEngine.Debug;

namespace Champions.Client.Bench
{
    /// <summary>
    /// The crowd's timing scene (`--crowd-bench N`, plan.md Risks: the ported edge look timed at 1,000 monsters before
    /// anything is built on it): N monsters of the horde's eight mob types in equal shares, in `--look`'s drawing, walking
    /// toward the hero across a field a little larger than the game camera's view, the world's grid and beat wave at 120
    /// BPM under them; 20 a second break into shards on a blow from the hero, and each comes back about 3 s later, so N
    /// stay alive. Song time is the frame time (`--fixed-time` for a reproducible run).
    ///
    /// In batch mode (`champions play --headless`) the bench draws the camera by hand into a 1080p target every frame and
    /// times it to the GPU's completion of the frame (a 1-pixel readback), alternating blocks of 30 frames with the crowd
    /// and 30 without, so the difference is the crowd's cost with the CPU's submission included: an upper bound on its
    /// GPU time (Metal has no GPU recorders). A windowed player records Unity's frame timings (FrameTimingManager's GPU
    /// time) instead. It logs percentiles after the 2 s warm-up and writes them to `--bench-out PATH` (JSON).
    /// </summary>
    public sealed class CrowdBench : IDisposable
    {
        static readonly string[] Mobs = { "skeleton", "slime", "bat", "demon", "goblin", "mushroom", "bigOrk", "troll" };
        const float KillsPerSecond = 20f, WarmupSeconds = 2f, BlowForce = 1.85f, Bpm = 120f, HalfWidthM = 16f, HalfDepthM = 11f;
        const int Block = 30, Spares = 60;

        sealed class Mob
        {
            public int Type, Walk;
            public float WalkLength, Stride, Hover, Height, Phase, Speed;
            public Vector2 Home;
            public bool Hidden;
            public uint Seed;
        }

        readonly Camera _camera;
        readonly World _world;
        readonly MonsterCrowd _crowd;
        readonly MonsterShatter _shatter;
        readonly List<Mob>[] _byType;
        readonly Queue<Mob> _spares = new Queue<Mob>();
        readonly List<Mob> _all = new List<Mob>();
        readonly HashSet<Mob> _killing = new HashSet<Mob>();
        readonly System.Random _rng = new System.Random(8);
        readonly RenderTexture _target;
        readonly string _out, _look;
        readonly int _alive;
        readonly List<double> _on = new List<double>(), _off = new List<double>(), _onCpu = new List<double>(), _offCpu = new List<double>();
        readonly List<double> _frameGpu = new List<double>();
        readonly FrameTiming[] _timings = new FrameTiming[1];
        double _lastBeat = double.NegativeInfinity;
        float _kills;
        int _frame, _serial;

        CrowdBench(Camera camera, MonsterLibrary lib, Palette palette, int alive, string outPath)
        {
            _camera = camera;
            _alive = alive;
            _out = outPath;
            _look = MonsterLooks.Name(lib.Look);
            _world = new World(camera, palette);
            _crowd = new MonsterCrowd(lib, alive / Mobs.Length + Spares, palette);
            _shatter = new MonsterShatter(lib, _crowd.Palette, palette);
            _byType = new List<Mob>[lib.Types.Length];
            for (int t = 0; t < _byType.Length; t++) _byType[t] = new List<Mob>();
            var types = Mobs.Select(lib.IndexOf).ToArray();
            for (int i = 0; i < alive + Spares; i++)
            {
                var type = lib.Types[types[i % types.Length]];
                int walk = Math.Max(0, type.Clip("walk"));
                var m = new Mob
                {
                    Type = types[i % types.Length], Walk = walk, WalkLength = type.Clips[walk].Length,
                    Stride = type.Clips[walk].StrideM > 0 ? type.Clips[walk].StrideM : 1f, Hover = type.HoverM, Height = type.HeightM,
                    Phase = (float)_rng.NextDouble(), Speed = 0.8f + 0.6f * (float)_rng.NextDouble(), Seed = (uint)i * 7919u + 1,
                    Home = new Vector2(((float)_rng.NextDouble() * 2 - 1) * HalfWidthM, ((float)_rng.NextDouble() * 2 - 1) * HalfDepthM),
                    Hidden = i >= alive,
                };
                _all.Add(m);
                _byType[m.Type].Add(m);
                if (m.Hidden) _spares.Enqueue(m);
            }
            if (Application.isBatchMode)
            {
                _target = new RenderTexture(1920, 1080, 24, RenderTextureFormat.ARGB32) { name = "CrowdBench" };
                _target.Create();
            }
            Debug.Log($"CrowdBench: {alive} monsters of {string.Join(", ", Mobs)} in the {_look} look, {KillsPerSecond} kills a second, " +
                      (_target != null ? "timed by hand into 1080p, crowd and empty blocks alternating" : "windowed, FrameTimingManager's GPU time"));
        }

        /// <summary>The bench, or null with why when the build has no baked monsters.</summary>
        public static CrowdBench Start(Camera camera, int alive, MonsterLook look, Palette palette, string outPath, out string error)
        {
            var lib = MonsterLibrary.Load(look);
            var missing = lib == null ? Mobs : Mobs.Where(id => lib.IndexOf(id) < 0).ToArray();
            error = missing.Length > 0 ? $"no baked {string.Join(", ", missing)} in this build (champions monster bake all, then build)" : null;
            return error == null ? new CrowdBench(camera, lib, palette, alive, outPath) : null;
        }

        /// <summary>One frame at song time <paramref name="t"/>.</summary>
        public void Frame(double t)
        {
            double period = 60.0 / Bpm, beat = Math.Floor(t / period) * period;
            if (beat > _lastBeat)
            {
                _lastBeat = beat;
                _world.Beat(beat, period, Math.Round(beat / period) % 4 == 0, true);
            }
            _world.Present(t, Vector2.zero, Vector2.zero, Time.deltaTime);
            bool timed = _target != null;
            bool crowdOn = !timed || (_frame / Block) % 2 == 0;
            bool measuring = timed && t >= WarmupSeconds;
            var watch = Stopwatch.StartNew();
            if (crowdOn) Write((float)t);
            if (timed)
            {
                var previous = _camera.targetTexture;
                _camera.targetTexture = _target;
                try { _camera.Render(); }
                finally { _camera.targetTexture = previous; }
                double submitted = watch.Elapsed.TotalMilliseconds;
                AsyncGPUReadback.Request(_target, 0, 0, 1, 0, 1, 0, 1, TextureFormat.RGBA32).WaitForCompletion();
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
            _frame++;
        }

        /// <summary>Queues this frame's crowd and shards again for another drawing of the camera (the stills').</summary>
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
                var k = _all[_rng.Next(_all.Count)];
                if (!k.Hidden) _killing.Add(k);
            }
            _kills -= _killing.Count;
            for (int type = 0; type < _byType.Length; type++)
            {
                var mobs = _byType[type];
                if (mobs.Count == 0) continue;
                var slots = _crowd.Instances(type, mobs.Count);
                var motion = _crowd.Motion(type, mobs.Count);
                for (int i = 0; i < mobs.Count; i++)
                {
                    var m = mobs[i];
                    motion[i] = MonsterMotion.Still(m.Seed);
                    if (m.Hidden)
                    {
                        slots[i] = new MonsterInstance { Flags = (uint)CrowdFlags.Hidden };
                        continue;
                    }
                    // Toward the hero, wrapping round the field, its walk clip keeping pace with its feet.
                    var dir = -m.Home.normalized;
                    float walked = t * m.Speed;
                    var p = m.Home + dir * walked;
                    p = new Vector2(Mathf.Repeat(p.x + HalfWidthM, 2 * HalfWidthM) - HalfWidthM, Mathf.Repeat(p.y + HalfDepthM, 2 * HalfDepthM) - HalfDepthM);
                    float time = (m.Phase + walked / m.Stride) * m.WalkLength;
                    var inst = new MonsterInstance
                    {
                        Position = new Vector3(p.x, m.Hover, p.y), Yaw = Mathf.Atan2(dir.x, dir.y), ClipA = (uint)m.Walk, TimeA = time,
                        ClipB = (uint)m.Walk, TimeB = time, Blend = 1, Scale = 1, Flags = (uint)CrowdFlags.Alive,
                    };
                    if (_killing.Contains(m))
                    {
                        inst.Flags = (uint)CrowdFlags.Dying;
                        var away = p.normalized;
                        _shatter.Break(type, i, inst, ++_serial, t, away.x, away.y, BlowForce);
                        m.Hidden = true;
                        _spares.Enqueue(m);
                        _spares.Dequeue().Hidden = false; // shown from the next frame
                    }
                    slots[i] = inst;
                }
            }
            _crowd.Draw(_camera, t);
            _shatter.Frame(t, _camera);
        }

        /// <summary>Logs the percentiles and writes `--bench-out`.</summary>
        public void Report()
        {
            string F(double v) => v.ToString("0.000", CultureInfo.InvariantCulture);
            var sb = new StringBuilder();
            sb.Append("{\n  \"alive\": ").Append(_alive).Append(",\n  \"look\": \"").Append(_look)
                .Append("\",\n  \"killsPerSecond\": ").Append(KillsPerSecond)
                .Append(",\n  \"device\": \"").Append(SystemInfo.graphicsDeviceName).Append("\"")
                .Append(",\n  \"syncedFrameMs\": {\"crowd\": {\"frames\": ").Append(_on.Count).Append(", \"p50\": ").Append(F(Percentile(_on, 0.5)))
                .Append(", \"p99\": ").Append(F(Percentile(_on, 0.99))).Append("}, \"empty\": {\"frames\": ").Append(_off.Count)
                .Append(", \"p50\": ").Append(F(Percentile(_off, 0.5))).Append(", \"p99\": ").Append(F(Percentile(_off, 0.99))).Append("}}")
                .Append(",\n  \"submitMs\": {\"crowd\": {\"p50\": ").Append(F(Percentile(_onCpu, 0.5))).Append(", \"p99\": ").Append(F(Percentile(_onCpu, 0.99)))
                .Append("}, \"empty\": {\"p50\": ").Append(F(Percentile(_offCpu, 0.5))).Append("}}")
                .Append(",\n  \"windowedGpuFrameMs\": {\"frames\": ").Append(_frameGpu.Count).Append(", \"p50\": ")
                .Append(F(Percentile(_frameGpu, 0.5))).Append(", \"p99\": ").Append(F(Percentile(_frameGpu, 0.99))).Append("}\n}\n");
            Debug.Log("CrowdBench: " + sb.ToString().Replace("\n", " "));
            if (_out != null) File.WriteAllText(_out, sb.ToString());
        }

        static double Percentile(List<double> values, double q)
        {
            if (values.Count == 0) return double.NaN;
            var sorted = values.OrderBy(v => v).ToList();
            return sorted[Math.Min(sorted.Count - 1, (int)Math.Floor(q * sorted.Count))];
        }

        public void Dispose()
        {
            _shatter.Dispose();
            _crowd.Dispose();
            _world.Dispose();
            if (_target != null)
            {
                _target.Release();
                UnityEngine.Object.Destroy(_target);
            }
        }
    }
}
