using System;
using System.Collections.Generic;
using Demo3D.Bench;
using Demo3D.Choreography;
using Demo3D.Runtime.Bench;
using Demo3D.Runtime.Crowd;
using Demo3D.Runtime.Effects;
using UnityEngine;

namespace Demo3D.Runtime.Playback
{
    /// <summary>
    /// The stress scenes S1 to S6 for the benchmark harness (TD4 registers them with TD7's
    /// runner): each rebuilds the choreographer's stress timeline for N (timing its ticks, as at
    /// load), then plays it through the same evaluation and crowd as the demo. The camera keeps
    /// the harness's height and pitch, and follows the timeline's framing focus.
    /// sceneOptions: <c>crowd</c> (bone, stock or baseline; default bone); <c>arena</c> (the
    /// arena's rung, "e2" by default, or "e0"; "off" for TD4's flat ground) and <c>density</c>
    /// (default 1). From TD9 the scenes run in the arena (benchmarks.md, Stress scenes), when
    /// it is baked; S1 to S5 hold its opening clearing and S6 its walled courtyard.
    /// sceneOptions: <c>crowd</c> (the crowd renderer, default bone), <c>lightPool</c> (pooled effect
    /// lights, default EffectLights.DefaultPool), <c>effectPool</c> (particles). The stress script's
    /// <c>draw</c> list picks the systems: S4 hides the crowd and draws effects and lights only.
    /// </summary>
    public sealed class TimelineScene : IBenchScene
    {
        readonly string _scene;
        BenchContext _ctx;
        PlaybackData _data;
        RosterAssets _roster;
        ChoreographyData _choreo;
        StressScript _script;
        Point _point;
        Timeline _tl;
        TimelineEvaluator _eval;
        ICrowdRenderer _crowd;
        string[] _typeIds;
        RosterAssets.TypeAssets[] _assets;
        float[] _stride, _walkLength;
        readonly List<int>[] _byType = new List<int>[0];
        List<int>[] _lists;
        List<int>[] _slotOf;     // each listed entity's stable crowd slot
        CrowdSlots _slots;
        int _onScreenAlive, _onScreenDying, _offScreen;
        Demo3D.Runtime.Arena.ArenaRuntime _arena;
        Demo3D.Runtime.Arena.RenderStats _stats;
        EffectSystem _fx;
        EffectDriver _fxDriver;
        ISpawnSink _altSink; // the runtime spike's VFX Graph arm (sceneOptions runtime: vfx)
        bool _drawCrowd = true, _drawEffects, _drawLights;
        double _fxLast = double.NaN;
        float _fxLogAt;
        readonly System.Diagnostics.Stopwatch _fxWatch = new System.Diagnostics.Stopwatch();
        readonly List<double> _beatCost = new List<double>();
        readonly List<int> _beatSpawns = new List<int>();
        double _t;
        // Covers warm-ups, probes, full runs and noisy repeats at one N; longer runs (holds, long
        // repeat waits) loop the timeline's settled part, and the downbeat check loops with it.
        const double Seconds = 120;
        const int Capacity = 16384; // crowd slots per type

        static double Loop(double t) => t < Seconds - 1 ? t : 10 + (t - 10) % (Seconds - 11);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Register()
        {
            for (int i = 1; i <= 6; i++)
            {
                string id = "S" + i;
                BenchScenes.Register(id, () => new TimelineScene(id));
            }
        }

        public TimelineScene(string scene) { _scene = scene; }

        public double? PackingPerM2 => null;

        public void Load(BenchContext ctx)
        {
            _ctx = ctx;
            Cuts.Active = Cuts.FromOptions(ctx.Options); // TD11's cuts: sceneOptions cuts, else --cuts
            Debug.Log($"TimelineScene: cuts {Cuts.Active}");
            _data = PlaybackData.Load();
            _roster = RosterAssets.Load();
            _script = StressScript.Parse(_data.Stress(_scene).text);
            var layout = _data.Layout(_script.Layout ?? "arena") ?? _data.Layout("arena");
            _choreo = ChoreographyData.FromJson(_data.annotations.text, _data.roster.text, layout.text, _data.footprints.text);
            _drawCrowd = _script.Draw.Length == 0 || Array.IndexOf(_script.Draw, "crowd") >= 0;
            _drawEffects = Array.IndexOf(_script.Draw, "effects") >= 0;
            _drawLights = Array.IndexOf(_script.Draw, "lights") >= 0;
            if (_drawEffects)
            {
                int Opt(string key, int fallback) =>
                    ctx.Options != null && ctx.Options.TryGetValue(key, out var v) && v != null ? Convert.ToInt32(v, System.Globalization.CultureInfo.InvariantCulture) : fallback;
                if (ctx.Options != null && ctx.Options.TryGetValue("runtime", out var rt) && rt is string runtime && runtime != "own")
                    _altSink = SpawnSinks.Create(runtime, EffectLibrary.Load(), ctx.Camera);
                else
                {
                    _fx = new EffectSystem(EffectLibrary.Load(), Opt("effectPool", EffectSystem.DefaultPool), _drawLights ? Opt("lightPool", EffectLights.DefaultPool) : 0);
                    _fx.AddCamera(ctx.Camera);
                }
            }
            string rung = ctx.Options.TryGetValue("arena", out var a) && a is string s ? s : Demo3D.Runtime.Arena.ArenaIndex.E2;
            float density = ctx.Options.TryGetValue("density", out var d) && d is double dd ? (float)dd : 1f;
            if (rung != "off" && (_script.Layout ?? "arena") == "arena")
            {
                if (!Demo3D.Runtime.Arena.ArenaRuntime.Available(rung, density))
                    throw new InvalidOperationException($"TimelineScene: no arena for rung {rung} at {density}x (baked: " +
                                                        $"{Demo3D.Runtime.Arena.ArenaRuntime.Describe()}); bake it or pass sceneOptions arena: off");
                _arena = Demo3D.Runtime.Arena.ArenaScene.LoadInto(ctx, rung, density);
                _stats = new Demo3D.Runtime.Arena.RenderStats();
                return;
            }
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            UnityEngine.Object.Destroy(ground.GetComponent<Collider>());
            ground.transform.SetParent(ctx.Root, false);
            ground.transform.localScale = new Vector3(60, 1, 60);
            ground.GetComponent<Renderer>().sharedMaterial = ctx.Assets.Ground;
        }

        public void Configure(Point point) => _point = point;

        public SimTickStats Rebuild(int n)
        {
            var script = _script.With(n, (float)(_point?.ViewWidthM ?? 0), _point?.Load, Seconds);
            _tl = Choreographer.RunStress(script, _choreo);
            _eval = new TimelineEvaluator(_tl, _choreo.Clips);
            MapTypes();
            _t = 0;
            _slots = new CrowdSlots(_typeIds.Length, Capacity);
            if (_fx != null || _altSink != null)
            {
                var lib = _fx != null ? _fx.Library : EffectLibrary.Load();
                _fxDriver = new EffectDriver(_tl, lib, (ISpawnSink)_fx ?? _altSink, t => _assets[t].kind == "elite" || _assets[t].kind == "boss");
                if (_ctx.Options != null && _ctx.Options.TryGetValue("spikeEffect", out var spike) && spike is string name)
                    _fxDriver.OverrideAll = lib.IndexOf(name) >= 0 ? lib.IndexOf(name) : throw new ArgumentException("no effect " + name);
                if (_ctx.Options != null && _ctx.Options.TryGetValue("spikeCopies", out var copies) && copies != null)
                    _fxDriver.OverrideCopies = Convert.ToInt32(copies, System.Globalization.CultureInfo.InvariantCulture);
                _fxLast = double.NaN;
            }
            var s = _tl.TickTiming.Summarize();
            int offScreen = (int)Math.Round(n * script.ReserveFactor);
            return new SimTickStats
            {
                P99Ms = s.P99Ms, MaxMs = s.MaxMs, Ticks = s.Ticks,
                FlowRebuildSlices = _tl.TickTiming.FlowRebuildSlices, OffScreen = offScreen,
            };
        }

        void MapTypes()
        {
            int n = _tl.TypeNames.Length;
            if (_typeIds == null || _typeIds.Length != n)
            {
                _crowd?.Dispose();
                _assets = new RosterAssets.TypeAssets[n];
                _typeIds = new string[n];
                _stride = new float[n];
                _walkLength = new float[n];
                _lists = new List<int>[n];
                _slotOf = new List<int>[n];
                for (int i = 0; i < n; i++)
                {
                    var name = _tl.TypeNames[i];
                    _assets[i] = _roster.Find(name) ?? Array.Find(_roster.types, t => t.kind == "boss");
                    _typeIds[i] = _assets[i].id;
                    int walkAnn = _choreo.Clips.Find(name + " walk");
                    _stride[i] = walkAnn >= 0 ? _choreo.Clips[walkAnn].StrideM : 1f;
                    int walk = _assets[i].ClipIndex("walk");
                    _walkLength[i] = walk >= 0 ? _assets[i].clips[walk].length : 1f;
                    _lists[i] = new List<int>();
                    _slotOf[i] = new List<int>();
                }
                _crowd = CrowdFactory.Create(_ctx.Options.TryGetValue("crowd", out var c) ? c as string : null);
                _crowd.Initialize(_typeIds, Capacity);
                if (_crowd is BoneTextureCrowd bone) bone.Clock = () => (float)_t; // deterministic LOD crossfades
            }
        }

        public void Tick(double t)
        {
            // Loop the timeline's settled part for runs longer than it.
            _t = Loop(t);
            var framing = _tl.FramingAt(_t);
            FollowFocus(new Vector3(framing.Focus.X, 0, framing.Focus.Y));
            foreach (var l in _lists) l.Clear();
            for (int e = 1; e < _tl.EntityCount; e++)
            {
                var info = _tl.Entities[e];
                if (info.Kind == EntityKind.Monster && info.Spawn <= _t && info.Despawn > _t) _lists[info.Type].Add(e);
            }
            // Stable slots: a monster keeps its slot while it lives (motion vectors, the previous
            // palette and LOD state follow the slot).
            _slots.Begin();
            for (int type = 0; type < _lists.Length; type++)
            {
                _slotOf[type].Clear();
                foreach (int e in _lists[type]) _slotOf[type].Add(_slots.Assign(type, e));
            }
            _slots.End();
            for (int type = 0; _drawCrowd && type < _lists.Length; type++)
            {
                var list = _lists[type];
                var inst = _crowd.Instances(type, _slots.Count(type));
                _slots.FillHidden(type, inst);
                for (int k = 0; k < list.Count; k++)
                {
                    var st = _eval.Evaluate(list[k], _t, _stride[type], _walkLength[type], _assets[type].hoverM);
                    inst[_slotOf[type][k]] = Instance(st, _assets[type], _tl.Entities[list[k]].Seed);
                }
            }
            if (_drawCrowd) _crowd.Draw(_ctx.Camera);
            if (_altSink != null)
            {
                _fxWatch.Restart();
                _fxDriver.Frame(_t);
                _altSink.Update(_t, 0, _ctx.Camera);
                _fxWatch.Stop();
                if (_fxDriver.EventsThisFrame >= 100) { _beatCost.Add(_fxWatch.Elapsed.TotalMilliseconds); _beatSpawns.Add(_fxDriver.EventsThisFrame); }
                if (Time.realtimeSinceStartup > _fxLogAt) { _fxLogAt = Time.realtimeSinceStartup + 5; LogBeatCost(); }
            }
            if (_fx != null)
            {
                _fxWatch.Restart();
                _fx.BeginFrame();
                _fxDriver.Frame(_t);
                float dt = double.IsNaN(_fxLast) || _t < _fxLast ? 0f : (float)(_t - _fxLast);
                _fx.Update(_t, dt, _ctx.Camera);
                _fxWatch.Stop();
                // The spike's beat-frame spawn cost: CPU time of the spawn path on frames with 100 or more spawns.
                if (_fxDriver.EventsThisFrame >= 100 && !double.IsNaN(_fxLast) && _t > _fxLast)
                {
                    _beatCost.Add(_fxWatch.Elapsed.TotalMilliseconds);
                    _beatSpawns.Add(_fxDriver.EventsThisFrame);
                }
                _fxLast = _t;
                if (Time.realtimeSinceStartup > _fxLogAt)
                {
                    _fxLogAt = Time.realtimeSinceStartup + 5;
                    LogBeatCost();
                    Debug.Log($"TimelineScene: effects t {_t:F1} spawns {_fx.SpawnsTotal} requested {_fx.ParticlesRequested} emitted {_fx.ParticlesEmitted} alive {_fx.Particles} rejected {_fx.RejectedTotal} cues {_fx.Lights.ActiveCues} lights {_fx.Lights.LightsUsed}");
                }
            }
        }

        void LogBeatCost()
        {
            if (_beatCost.Count == 0) return;
            _beatCost.Sort();
            double sum = 0;
            foreach (var s in _beatSpawns) sum += s;
            Debug.Log($"TimelineScene: beat-frame spawn cost over {_beatCost.Count} frames ({sum / _beatSpawns.Count:F0} spawns mean): " +
                      $"p50 {_beatCost[_beatCost.Count / 2]:F3} ms, p99 {_beatCost[(int)(_beatCost.Count * 0.99)]:F3} ms, max {_beatCost[_beatCost.Count - 1]:F3} ms");
            _beatCost.Clear();
            _beatSpawns.Clear();
        }

        internal static MonsterInstance Instance(in TimelineEvaluator.State st, RosterAssets.TypeAssets asset, uint seed,
            Func<int, int> clipToRoster = null)
        {
            int walk = Math.Max(0, asset.ClipIndex("walk"));
            uint Clip(TimelineEvaluator.Slot s) => (uint)(s.Clip < 0 ? walk : Math.Max(0, clipToRoster != null ? clipToRoster(s.Clip) : walk));
            return new MonsterInstance
            {
                Position = st.Position, Yaw = st.Yaw,
                ClipA = Clip(st.A), TimeA = st.A.Time, ClipB = Clip(st.B), TimeB = st.B.Time, Blend = st.Blend,
                Scale = 0.92f + 0.16f * TimelineEvaluator.Jitter(seed, 1),
                HitFlash = st.HitFlash, Dissolve = st.Dissolve,
                Flags = (uint)(st.Dying ? CrowdFlags.Dying : CrowdFlags.Alive),
            };
        }

        MonsterInstance Instance(in TimelineEvaluator.State st, RosterAssets.TypeAssets asset, uint seed) =>
            Instance(st, asset, seed, c => asset.ClipIndex(_eval.RoleOf(c)));

        void FollowFocus(Vector3 focus)
        {
            var cam = _ctx.Camera.transform;
            var f = cam.forward;
            if (f.y > -1e-3f) return;
            var target = cam.position - f * (cam.position.y / f.y);
            cam.position += new Vector3(focus.x - target.x, 0, focus.z - target.z);
        }

        public bool DownbeatIn(double t0, double t1)
        {
            var map = _tl?.Map;
            if (map == null) return false;
            double a = Loop(t0), b = Loop(t1);
            if (b < a) return DownbeatInRaw(map, a, Seconds - 1) || DownbeatInRaw(map, 10, b); // wrapped
            return DownbeatInRaw(map, a, b);
        }

        static bool DownbeatInRaw(SongMap map, double t0, double t1)
        {
            int bpb = map.MeterBeatsPerBar.Length > 0 ? map.MeterBeatsPerBar[0] : 4;
            for (int b = map.FirstDownbeatIndex; b < map.Beats.Length; b += bpb)
                if (map.Beats[b] > t0 && map.Beats[b] <= t1) return true;
            return false;
        }

        public void CollectCounters(SceneCounters c, bool viewFrame)
        {
            if (_altSink != null) { c.SpawnsThisFrame = _fxDriver?.EventsThisFrame ?? 0; c.Particles = _altSink.Particles; c.SpawnsRejected = 0; }
            if (_fx != null)
            {
                c.Particles = _fx.Particles;
                c.SpawnsThisFrame = _fxDriver?.EventsThisFrame ?? 0;
                c.SpawnsRejected = _fx.TakeRejected();
                if (_drawLights)
                {
                    c.LightCues = _fx.Lights.ActiveCues;
                    c.LightsUsed = _fx.Lights.LightsUsed;
                    c.LightDropShare = _fx.Lights.DropShare;
                }
            }
            var cc = _crowd.Counters;
            c.SkinnedVertices = (int)Math.Min(int.MaxValue, cc.SkinnedVertices);
            c.ShadowCasters = cc.ShadowCasters;
            if (!viewFrame) return;
            _onScreenAlive = _onScreenDying = _offScreen = 0;
            var planes = GeometryUtility.CalculateFrustumPlanes(_ctx.Camera);
            for (int type = 0; type < _lists.Length; type++)
                foreach (int e in _lists[type])
                {
                    var st = _eval.Evaluate(e, _t, _stride[type], _walkLength[type], _assets[type].hoverM);
                    var b = new Bounds(st.Position + Vector3.up * _assets[type].heightM * 0.5f, new Vector3(1, _assets[type].heightM, 1));
                    if (!GeometryUtility.TestPlanesAABB(planes, b)) { _offScreen++; continue; }
                    if (st.Dying) _onScreenDying++; else _onScreenAlive++;
                }
            c.OnScreenAlive = _onScreenAlive;
            c.OnScreenDying = _onScreenDying;
            c.OffScreen = _offScreen;
            if (_arena != null && _arena.Ready)
            {
                var urp = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline as UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset;
                _arena.Count(_ctx.Camera, urp != null ? urp.shadowDistance : 50f, out int visible, out int casters);
                c.Environment = Demo3D.Runtime.Arena.ArenaScene.EnvironmentJson(_arena, _stats, visible, casters);
            }
        }

        public void DrawOverdraw(IOverdrawSink sink)
        {
            if (_fx != null) sink.Transparent(cmd => _fx.DrawOverdraw(cmd));
            Demo3D.Runtime.Arena.ArenaScene.DrawOverdraw(_arena, _ctx.Camera, sink);
            if (!_drawCrowd) return;
            // The stock crowd's bind-pose meshes at each instance stand in for the skinned draws.
            for (int type = 0; type < _lists.Length; type++)
            {
                var meshes = _assets[type].prefab.GetComponentsInChildren<SkinnedMeshRenderer>();
                foreach (int e in _lists[type])
                {
                    var p = _tl.PositionAt(e, _t, out float yaw);
                    var m = Matrix4x4.TRS(new Vector3(p.X, _assets[type].hoverM, p.Y), Quaternion.Euler(0, yaw * Mathf.Rad2Deg, 0),
                        _assets[type].prefab.transform.localScale);
                    foreach (var smr in meshes) if (smr.sharedMesh != null) sink.Opaque(smr.sharedMesh, m);
                }
            }
        }

        public void Dispose()
        {
            _fx?.Dispose();
            _fx = null;
            _altSink?.Dispose();
            _altSink = null;
            _crowd?.Dispose();
            _crowd = null;
            _stats?.Dispose();
            _arena?.Dispose();
            _arena = null;
        }
    }

    /// <summary>
    /// Chooses a crowd renderer by name: "bone" (TD5's bone-texture path, the default), "stock"
    /// (TD4's stock crowd: vendor prefabs, playables at the timeline's exact clip times) or
    /// "baseline" (TD5's stock arm: the combined mesh, Animator CrossFade, as crowd.md describes;
    /// the frontier's stock comparison).
    /// </summary>
    public static class CrowdFactory
    {
        static readonly Dictionary<string, Func<ICrowdRenderer>> Renderers = new Dictionary<string, Func<ICrowdRenderer>>
        {
            ["stock"] = () => new StockCrowd(),
            ["bone"] = () => new BoneTextureCrowd(CrowdLibrary.Load()),
            ["baseline"] = () => new BaselineCrowd(CrowdLibrary.Load()), // owns and destroys its root
        };

        public static string Default = "bone";

        public static void Register(string name, Func<ICrowdRenderer> factory, bool makeDefault = false)
        {
            Renderers[name] = factory;
            if (makeDefault) Default = name;
        }

        public static ICrowdRenderer Create(string name)
        {
            if (string.IsNullOrEmpty(name) || name == "auto") name = Default;
            return Renderers.TryGetValue(name, out var f) ? f() : throw new ArgumentException("no crowd renderer " + name);
        }
    }
}
