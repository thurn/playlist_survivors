using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Demo3D.Bench;
using Demo3D.Choreography;
using Demo3D.Runtime.Bench;
using Demo3D.Runtime.Playback;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Demo3D.Runtime.Arena
{
    /// <summary>
    /// S0, the empty arena (benchmarks.md, Stress scenes): the environment, its lighting and the
    /// post stack, with no monsters. sceneOptions: <c>density</c> (0.5, 1, 2 or 4; default 1),
    /// <c>rung</c> ("e0", the stock baseline, by default; or "e2"), <c>holdS</c> (seconds per
    /// shot, default 3). The camera keeps the harness's height and pitch for the point's view
    /// width and tours the layout's six beauty-shot positions, so a run sees every kind of ground
    /// the level script reaches. N means nothing here: sweeps use <c>n.fixed</c>.
    /// </summary>
    public sealed class ArenaScene : IBenchScene
    {
        BenchContext _ctx;
        ArenaRuntime _arena;
        RenderStats _stats;
        LayoutShot[] _shots;
        float _holdS = 3f;
        double _t;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Register() => BenchScenes.Register("S0", () => new ArenaScene());

        public double? PackingPerM2 => null;

        public void Load(BenchContext ctx)
        {
            _ctx = ctx;
            float density = Option(ctx, "density", 1f);
            string rung = ctx.Options.TryGetValue("rung", out var r) && r is string rs ? rs : ArenaIndex.E0;
            _holdS = Option(ctx, "holdS", 3f);
            var layout = ArenaLayout.Parse(PlaybackData.Load().Layout("arena").text);
            _shots = layout.Shots;
            _arena = LoadInto(ctx, rung, density);
            _stats = new RenderStats();
        }

        /// <summary>Loads the arena under the bench rig: the rig's sun and post give way to the moon and the night's post.</summary>
        public static ArenaRuntime LoadInto(BenchContext ctx, string rung, float density)
        {
            if (ctx.Sun != null) ctx.Sun.gameObject.SetActive(false);
            foreach (var v in ctx.Root.GetComponentsInChildren<Volume>(true)) v.enabled = false;
            return ArenaRuntime.Load(rung, density, ctx.Camera, ctx.Root);
        }

        static float Option(BenchContext ctx, string key, float fallback) =>
            ctx.Options.TryGetValue(key, out var v) && v is double d ? (float)d : fallback;

        public void Configure(Point point) { }

        public SimTickStats Rebuild(int n)
        {
            _t = 0;
            return null;
        }

        public void Tick(double t)
        {
            _t = t;
            if (_shots == null || _shots.Length == 0) return;
            int i = (int)(t / _holdS) % _shots.Length;
            FollowFocus(_ctx.Camera, new Vector3(_shots[i].At.X, 0, _shots[i].At.Y));
        }

        /// <summary>Moves the camera sideways so its view centre on the ground is <paramref name="focus"/>.</summary>
        public static void FollowFocus(Camera camera, Vector3 focus)
        {
            var cam = camera.transform;
            var f = cam.forward;
            if (f.y > -1e-3f) return;
            var target = cam.position - f * (cam.position.y / f.y);
            cam.position += new Vector3(focus.x - target.x, 0, focus.z - target.z);
        }

        public bool DownbeatIn(double t0, double t1) => false;

        public void CollectCounters(SceneCounters c, bool viewFrame)
        {
            if (_arena == null || !_arena.Ready) return;
            c.LightsUsed = _arena.Lights.Count;
            if (!viewFrame) return;
            var urp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            _arena.Count(_ctx.Camera, urp != null ? urp.shadowDistance : 50f, out int visible, out int casters);
            c.ShadowCasters = casters;
            c.Environment = EnvironmentJson(_arena, _stats, visible, casters);
        }

        /// <summary>The environment's counters for a result row (one JSON object).</summary>
        public static string EnvironmentJson(ArenaRuntime arena, RenderStats stats, int visible, int casters)
        {
            var sb = new StringBuilder("{");
            void F(string k, object v)
            {
                if (v == null) return;
                if (sb.Length > 1) sb.Append(',');
                sb.Append('"').Append(k).Append("\":");
                if (v is string s) sb.Append('"').Append(s).Append('"');
                else sb.Append(Convert.ToString(v, CultureInfo.InvariantCulture));
            }
            F("scene", arena.SceneName);
            F("rung", arena.Rung);
            F("density", arena.Density);
            F("kitRenderers", arena.Static.Count);
            F("kitVisible", visible);
            F("kitShadowCasters", casters);
            F("realtimeLights", arena.Lights.Count);
            F("staticShadowCasters", arena.ShadowMap?.Casters);
            F("drawCalls", stats?.DrawCalls);
            F("batches", stats?.Batches);
            F("setPassCalls", stats?.SetPassCalls);
            F("triangles", stats?.Triangles);
            F("unityShadowCasters", stats?.ShadowCasters);
            return sb.Append('}').ToString();
        }

        public void DrawOverdraw(IOverdrawSink sink) => DrawOverdraw(_arena, _ctx.Camera, sink);

        /// <summary>The arena's opaque draws for the overdraw counters: the ground and the kit pieces in view.</summary>
        public static void DrawOverdraw(ArenaRuntime arena, Camera camera, IOverdrawSink sink)
        {
            if (arena == null || !arena.Ready) return;
            var planes = GeometryUtility.CalculateFrustumPlanes(camera);
            if (arena.Ground != null) Opaque(sink, arena.Ground);
            foreach (var r in arena.Static)
                if (r.enabled && GeometryUtility.TestPlanesAABB(planes, r.bounds)) Opaque(sink, r);
        }

        static void Opaque(IOverdrawSink sink, Renderer r)
        {
            var mf = r.GetComponent<MeshFilter>();
            if (mf != null && mf.sharedMesh != null) sink.Opaque(mf.sharedMesh, r.localToWorldMatrix);
        }

        public void Dispose()
        {
            _stats?.Dispose();
            _arena?.Dispose();
            _arena = null;
        }
    }
}
