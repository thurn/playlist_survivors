using System;
using System.Collections.Generic;
using Demo3D.Bench;
using Demo3D.Choreography;
using Demo3D.Runtime.Bench;
using Demo3D.Runtime.Crowd;
using UnityEngine;

namespace Demo3D.Runtime.Playback
{
    /// <summary>
    /// The stress scenes S1 to S6 for the benchmark harness (TD4 registers them with TD7's
    /// runner): each rebuilds the choreographer's stress timeline for N (timing its ticks, as at
    /// load), then plays it through the same evaluation and crowd as the demo. The camera keeps
    /// the harness's height and pitch, and follows the timeline's framing focus.
    /// sceneOptions: <c>crowd</c> ("stock" until TD5's bone path replaces it).
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
            _data = PlaybackData.Load();
            _roster = RosterAssets.Load();
            _script = StressScript.Parse(_data.Stress(_scene).text);
            var layout = _data.Layout(_script.Layout ?? "arena") ?? _data.Layout("arena");
            _choreo = ChoreographyData.FromJson(_data.annotations.text, _data.roster.text, layout.text, _data.footprints.text);
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
            for (int type = 0; type < _lists.Length; type++)
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
            _crowd.Draw(_ctx.Camera);
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
        }

        public void DrawOverdraw(IOverdrawSink sink)
        {
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
            _crowd?.Dispose();
            _crowd = null;
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
