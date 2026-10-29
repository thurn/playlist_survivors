using System;
using System.Collections.Generic;
using Demo3D.Bench;
using UnityEngine;

namespace Demo3D.Runtime.Bench
{
    /// <summary>
    /// A stress scene the benchmark player can sweep (benchmarks.md, Stress scenes). The harness
    /// owns the camera (placed from the view width), the sun, the ground's lighting, the
    /// anti-aliasing mode and all timing; a scene builds its content once, rebuilds for each N,
    /// draws every frame and reports its counters.
    ///
    /// TD4 registers S0 to S6 and the song through <see cref="BenchScenes.Register"/>.
    /// </summary>
    public interface IBenchScene : IDisposable
    {
        /// <summary>Once per process: build static content (meshes, materials, the arena).</summary>
        void Load(BenchContext ctx);

        /// <summary>
        /// Apply a point's axes in place. The harness has already moved the camera to the view
        /// width and set the anti-aliasing mode; the scene applies its load preset.
        /// </summary>
        void Configure(Point point);

        /// <summary>
        /// Rebuild for N on-screen monsters: the stress timeline (timing its ticks, as at load) and
        /// the crowd. Returns the sim's tick timing, or null for a scene without a sim.
        /// </summary>
        SimTickStats Rebuild(int n);

        /// <summary>Advance to <paramref name="t"/> seconds since the last Rebuild and issue this frame's draws.</summary>
        void Tick(double t);

        /// <summary>Whether a downbeat falls in (t0, t1]; the frame after it runs the overdraw counters.</summary>
        bool DownbeatIn(double t0, double t1);

        /// <summary>
        /// This frame's counters. <paramref name="viewFrame"/> is set on counter frames (every 60th
        /// and after downbeats, excluded from timing), where expensive counts such as frustum tests
        /// belong; per-frame counts such as spawns are reported on every frame.
        /// </summary>
        void CollectCounters(SceneCounters c, bool viewFrame);

        /// <summary>Issue every opaque and transparent draw again for the overdraw counters.</summary>
        void DrawOverdraw(IOverdrawSink sink);

        /// <summary>The roster's densest packing in monsters per m², or null for the spec's value.</summary>
        double? PackingPerM2 { get; }
    }

    /// <summary>What a scene gets from the harness.</summary>
    public sealed class BenchContext
    {
        public Camera Camera;
        public Light Sun;
        public Transform Root;
        public Dictionary<string, object> Options;
        public BenchAssets Assets;

        /// <summary>The current view's ground footprint around the target (x, z pairs), from CameraGeometry.</summary>
        public double[] Footprint;
        public double ViewWidthM;
    }

    /// <summary>The sim's tick timing from a rebuild (benchmarks.md, Sim tick timing), after warm-up ticks.</summary>
    public sealed class SimTickStats
    {
        public double P99Ms, MaxMs;
        public int Ticks;
        public int? FlowRebuildSlices;
        public int? OffScreen;
    }

    /// <summary>Counters a scene reports; null means the scene has no such system yet.</summary>
    public sealed class SceneCounters
    {
        public int? OnScreenAlive, OnScreenDying, OffScreen, ShadowCasters, SkinnedVertices;
        public int? Particles, SpawnsThisFrame, SpawnsRejected, LightCues, LightsUsed;
        public double? LightDropShare;
        public string VisibleByTypeLod;
        /// <summary>The environment's counters (S0 and arena scenes: kit renderers, draws, casters), a JSON object.</summary>
        public string Environment;

        public void Clear()
        {
            OnScreenAlive = OnScreenDying = OffScreen = ShadowCasters = SkinnedVertices = null;
            Particles = SpawnsThisFrame = SpawnsRejected = LightCues = LightsUsed = null;
            LightDropShare = null;
            VisibleByTypeLod = null;
            Environment = null;
        }
    }

    /// <summary>Receives a scene's draws for the overdraw counters' debug replacement render.</summary>
    public interface IOverdrawSink
    {
        void Opaque(Mesh mesh, Matrix4x4 matrix);
        void Opaque(Mesh mesh, Matrix4x4[] matrices, int count);
        void Transparent(Mesh mesh, Matrix4x4[] matrices, int count);
        /// <summary>Transparent draws a scene issues itself (procedural particles), with a pass that writes the counter's green channel under the global _OverdrawVP.</summary>
        void Transparent(Action<UnityEngine.Rendering.CommandBuffer> issue);
    }

    /// <summary>The registry of stress scenes by id.</summary>
    public static class BenchScenes
    {
        static readonly Dictionary<string, Func<IBenchScene>> Factories = new Dictionary<string, Func<IBenchScene>>
        {
            ["synthetic"] = () => new SyntheticScene(),
        };

        public static void Register(string id, Func<IBenchScene> factory) => Factories[id] = factory;

        public static IEnumerable<string> Ids => Factories.Keys;

        public static IBenchScene Create(string id) =>
            Factories.TryGetValue(id, out var f)
                ? f()
                : throw new ArgumentException($"no bench scene '{id}' (have: {string.Join(", ", Factories.Keys)})");
    }
}
