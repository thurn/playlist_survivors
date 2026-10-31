using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Debug = UnityEngine.Debug;

namespace Game.Client.Crowd
{
    /// <summary>
    /// The GPU shatter's windowless bench (`--shatter-bench OUT.json`, with `--fixed-time`): what 1,
    /// 50 and 300 simultaneous deaths cost (unity-game.md, Shatter: fifty in one frame should cost
    /// about what one does). The cast's monsters stand on a grid in the game camera's framing, under
    /// a shadowing sun. Each case first draws the N monsters alive for a second (what their bodies
    /// cost), then runs trials in which they die together, and their shards fly, sink and end over
    /// the next two seconds; a baseline case draws nothing.
    ///
    /// Every frame it measures, with the GPU drained before and after each part (an
    /// AsyncGPUReadback waited on), the wall time of:
    /// - the shatter's frame (<see cref="MonsterShatter.Frame"/>: the seed on the break frame, then
    ///   the integration), from submission to the GPU finishing it;
    /// - the camera's draw of the frame into a 1920×1080 target, whose shard draws are its cost over
    ///   the baseline's (from the 10th percentiles), beside the same monsters' bodies drawn alive.
    /// Where the GPU recorder is supported (a development player, not on the Mac's Metal), it also
    /// reads the shatter's GPU markers (MonsterShatter.SeedSampler and IntegrateSampler, three frames
    /// late): the compute passes' own GPU time.
    /// The JSON has each case's 10th, 50th and 90th percentiles; the log has one line per case.
    /// </summary>
    public sealed class ShatterBench : MonoBehaviour
    {
        static readonly int[] Cases = { 0, 1, 50, 300 };
        const int Trials = 31;
        const int TrialFrames = 120;  // two seconds: the shards' whole life and a gap
        const int FlightFrames = 60;  // the first second, while every shard is in flight
        const int AliveFrames = 60;   // the bodies drawn alive, before the trials
        const float Spacing = 1.2f; // 300 fill the medium view's 30 m

        Camera _camera;
        string _out;
        CrowdLibrary _lib;
        BoneTextureCrowd _crowd;
        MonsterShatter _shatter;
        RenderTexture _target;
        GraphicsBuffer _fence;
        Light _sun;
        int _case, _trial, _frame, _frames, _alive;
        bool _done;

        readonly Stopwatch _clock = new Stopwatch();
        readonly Dictionary<int, Samples> _samples = new Dictionary<int, Samples>();

        sealed class Samples
        {
            public readonly List<double> SeedMs = new List<double>(), UpdateMs = new List<double>(), RenderMs = new List<double>(),
                FlightRenderMs = new List<double>(), BodyRenderMs = new List<double>(), SeedGpuMs = new List<double>(),
                IntegrateGpuMs = new List<double>();
            public int PeakShards;
        }

        public static ShatterBench Begin(GameObject host, Camera camera, string outPath)
        {
            var bench = host.AddComponent<ShatterBench>();
            bench._camera = camera;
            bench._out = outPath;
            bench.Setup();
            return bench;
        }

        void Setup()
        {
            if (!SystemInfo.supportsComputeShaders)
            {
                Finish(1, "needs a GPU with compute (not a -nographics player)");
                return;
            }
            _lib = CrowdLibrary.Load();
            if (_lib == null)
            {
                Finish(1, "no baked crowd (run `game unity BakeCrowd`)");
                return;
            }
            _crowd = new BoneTextureCrowd(_lib, MonsterShatter.RingCapacity);
            _shatter = new MonsterShatter(_lib, _crowd.Palette);
            _target = new RenderTexture(Stills.Width, Stills.Height, 24, RenderTextureFormat.ARGB32)
            {
                antiAliasing = GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset urp ? Math.Max(1, urp.msaaSampleCount) : 1,
                name = "ShatterBench",
            };
            _target.Create();
            _fence = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 1, 4) { name = "ShatterBench fence" };
            var sun = new GameObject("Sun");
            _sun = sun.AddComponent<Light>();
            _sun.type = LightType.Directional;
            _sun.shadows = LightShadows.Soft;
            sun.transform.rotation = Quaternion.Euler(50, -30, 0);
            RenderSettings.sun = _sun;
            Look.CameraRig.Frame(_camera, Vector3.zero, Look.CameraRig.MediumWidthM, (float)Stills.Width / Stills.Height);
            MonsterShatter.SeedSampler.enableRecording = true;
            MonsterShatter.IntegrateSampler.enableRecording = true;
            foreach (int n in Cases) _samples[n] = new Samples();
            Debug.Log($"ShatterBench: {Cases.Length} cases x {Trials} trials x {TrialFrames} frames, {_lib.Types.Length} types, " +
                      $"GPU recorder {(SystemInfo.supportsGpuRecorder ? "supported" : "unsupported")}, development {Debug.isDebugBuild}");
        }

        void Update()
        {
            if (_done || _lib == null) return;
            int deaths = Cases[_case];
            var s = _samples[deaths];
            double t = Time.timeSinceLevelLoadAsDouble;
            if (deaths > 0 && _alive < AliveFrames)
            {
                Place(deaths, CrowdFlags.Alive);
                _crowd.Draw(_camera, (float)t);
                Fence();
                _clock.Restart();
                Render();
                Fence();
                if (_alive++ >= 10) s.BodyRenderMs.Add(_clock.Elapsed.TotalMilliseconds); // the first frames warm up
                _frames++;
                return;
            }

            // The markers' GPU time is three frames late: a seed's reading lands in its case's frames.
            float seedGpu = MonsterShatter.SeedSampler.gpuElapsedTime;
            if (seedGpu > 0 && deaths > 0) s.SeedGpuMs.Add(seedGpu);
            float integrateGpu = MonsterShatter.IntegrateSampler.gpuElapsedTime;
            if (integrateGpu > 0 && _frame > 4 && _frame < FlightFrames && deaths > 0) s.IntegrateGpuMs.Add(integrateGpu);

            bool breaking = _frame == 0 && deaths > 0;
            if (breaking) Kill(deaths, t);
            Fence();
            _clock.Restart();
            _shatter.Frame(t, _camera);
            Fence();
            double update = _clock.Elapsed.TotalMilliseconds;
            _clock.Restart();
            Render();
            Fence();
            double render = _clock.Elapsed.TotalMilliseconds;

            if (_trial > 0) // the first trial of each case warms up
            {
                if (breaking) s.SeedMs.Add(update);
                else if (_frame <= FlightFrames) s.UpdateMs.Add(update); // shards in flight, not the empty tail
                s.RenderMs.Add(render);
                if (_frame > 0 && _frame <= FlightFrames) s.FlightRenderMs.Add(render);
            }
            s.PeakShards = Math.Max(s.PeakShards, _shatter.LiveShards);
            _frames++;
            if (++_frame < TrialFrames) return;
            _frame = 0;
            if (++_trial < Trials) return;
            _trial = 0;
            _alive = 0;
            if (++_case < Cases.Length) return;
            Write();
            Finish(0, null);
        }

        /// <summary>The first N of the grid die together: written Dying (blended, not drawn), the crowd drawn, the shatter told.</summary>
        void Kill(int deaths, double t)
        {
            var placed = Place(deaths, CrowdFlags.Alive | CrowdFlags.Dying);
            _crowd.Draw(_camera, (float)t);
            for (int i = 0; i < placed.Count; i++)
            {
                var p = placed[i];
                var away = new Vector2(p.body.Position.x, p.body.Position.z);
                if (away.sqrMagnitude < 1e-6f) away = Vector2.up;
                away.Normalize();
                _shatter.Break(p.type, p.slot, p.body, p.role, _frames + i, t, away.x, away.y, i % 5 == 0 ? 4.63 : 1.85);
            }
        }

        /// <summary>The first N of the cast on the grid, idling, written into the crowd's instances with <paramref name="flags"/>.</summary>
        List<(int type, int slot, MonsterInstance body, int role)> Place(int count, CrowdFlags flags)
        {
            int types = _lib.Types.Length;
            var counts = new int[types];
            var placed = new List<(int type, int slot, MonsterInstance body, int role)>(count);
            int columns = (int)Math.Ceiling(Math.Sqrt(count * 16.0 / 9));
            for (int i = 0; i < count; i++)
            {
                int role = i % _lib.Cast.Length;
                var look = _lib.Cast[role];
                var type = _lib.Types[look.Type];
                int idle = type.Clip("idle");
                float x = (i % columns - (columns - 1) * 0.5f) * Spacing, z = (i / columns - (count / columns) * 0.5f) * Spacing;
                var body = new MonsterInstance
                {
                    Position = new Vector3(x, type.HoverM * look.Scale, z),
                    Yaw = i * 2.39996f,
                    ClipA = (uint)idle, TimeA = (i * 0.37f) % type.Clips[idle].Length,
                    ClipB = (uint)idle, TimeB = 0, Blend = 1,
                    Scale = look.Scale,
                    Tint = MonsterInstance.Pack(look.Gem),
                    Flags = (uint)flags,
                };
                placed.Add((look.Type, counts[look.Type]++, body, role));
            }
            for (int type = 0; type < types; type++)
            {
                if (counts[type] == 0) continue;
                var instances = _crowd.Instances(type, counts[type]);
                foreach (var p in placed)
                    if (p.type == type) instances[p.slot] = p.body;
            }
            return placed;
        }

        void Render()
        {
            var previous = _camera.targetTexture;
            _camera.targetTexture = _target;
            try { _camera.Render(); }
            finally { _camera.targetTexture = previous; }
        }

        /// <summary>Waits until the GPU has finished everything submitted so far.</summary>
        void Fence()
        {
            var r = AsyncGPUReadback.Request(_fence);
            r.WaitForCompletion();
        }

        void Write()
        {
            var json = new StringBuilder();
            json.Append("{\n");
            json.Append($"  \"device\": \"{SystemInfo.graphicsDeviceName}\", \"api\": \"{SystemInfo.graphicsDeviceType}\", " +
                        $"\"unity\": \"{Application.unityVersion}\", \"development\": {(Debug.isDebugBuild ? "true" : "false")},\n");
            json.Append($"  \"trials\": {Trials - 1}, \"trialFrames\": {TrialFrames}, \"target\": [{Stills.Width}, {Stills.Height}], \"msaa\": {_target.antiAliasing},\n");
            json.Append("  \"cases\": [\n");
            // The draws' cost over the empty frame, from the 10th percentiles (the GPU's clocks ramp
            // and other work on the machine lands in the upper ones).
            double baseline = Pct(_samples[0].FlightRenderMs, 0.1);
            for (int k = 0; k < Cases.Length; k++)
            {
                int n = Cases[k];
                var s = _samples[n];
                string shardDraw = Ms(n > 0 ? Pct(s.FlightRenderMs, 0.1) - baseline : 0);
                string bodyDraw = Ms(n > 0 ? Pct(s.BodyRenderMs, 0.1) - baseline : 0);
                json.Append($"    {{\"deaths\": {n}, \"peakShards\": {s.PeakShards}, \"seedFrameMs\": {Stat(s.SeedMs)}, " +
                            $"\"integrateFrameMs\": {Stat(s.UpdateMs)}, \"renderInFlightMs\": {Stat(s.FlightRenderMs)}, " +
                            $"\"renderAliveMs\": {Stat(s.BodyRenderMs)}, \"shardDrawMs\": {shardDraw}, \"bodyDrawMs\": {bodyDraw}, " +
                            $"\"seedGpuMs\": {Stat(s.SeedGpuMs)}, \"integrateGpuMs\": {Stat(s.IntegrateGpuMs)}}}" +
                            (k + 1 < Cases.Length ? "," : "") + "\n");
                Debug.Log($"ShatterBench: {n} deaths ({s.PeakShards} shards): seed frame p10 {Ms(Pct(s.SeedMs, 0.1))} ms, " +
                          $"integrate frame p10 {Ms(Pct(s.UpdateMs, 0.1))} ms, shard draws {shardDraw} ms (the bodies alive {bodyDraw} ms), " +
                          $"GPU markers seed {Ms(Pct(s.SeedGpuMs, 0.5))} ms ({s.SeedGpuMs.Count}), integrate {Ms(Pct(s.IntegrateGpuMs, 0.5))} ms ({s.IntegrateGpuMs.Count})");
            }
            json.Append("  ]\n}\n");
            File.WriteAllText(Args.Path(_out), json.ToString());
        }

        static string Ms(double ms) => ms.ToString("F3", CultureInfo.InvariantCulture);

        /// <summary>A sample's 10th, 50th and 90th percentiles: the 10th is the least disturbed by other work on the machine.</summary>
        static string Stat(List<double> xs) =>
            $"{{\"p10\": {Ms(Pct(xs, 0.1))}, \"p50\": {Ms(Pct(xs, 0.5))}, \"p90\": {Ms(Pct(xs, 0.9))}, \"n\": {xs.Count}}}";

        static double Pct(List<double> xs, double q)
        {
            if (xs.Count == 0) return 0;
            var sorted = xs.OrderBy(x => x).ToArray();
            return sorted[Math.Min(sorted.Length - 1, (int)(q * (sorted.Length - 1) + 0.5))];
        }

        void Finish(int code, string error)
        {
            _done = true;
            if (error != null) Debug.LogError("ShatterBench: " + error);
            else Debug.Log($"ShatterBench: done in {_frames} frames -> {_out}");
            Application.Quit(code);
        }

        void OnDestroy()
        {
            _shatter?.Dispose();
            _crowd?.Dispose();
            _fence?.Dispose();
            if (_target != null) _target.Release();
        }
    }
}
