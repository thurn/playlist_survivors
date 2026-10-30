using System;
using System.Collections.Generic;
using Demo2D.Choreography;
using PS.Choreography;
using UnityEngine;

namespace Demo2D.Runtime.Playback
{
    /// <summary>
    /// The timeline's effects (PX7): pooled CFXR instances, one per live effect event in view (and
    /// one per projectile in flight), each placed, turned and scaled by the effect library
    /// (Data/effects.json), with its light cue offered to a pooled Light2D.
    ///
    /// Which instances live is a function of the song time: the events whose lifetime covers it,
    /// inside the view, at most each kind's <c>max</c> (the newest win). How they look is too, on a
    /// still: every live instance is re-seeded with its event's seed and simulated from its event
    /// to the still's time in 1/30 s steps, following its anchor (a monster, the hero, a
    /// projectile), so a seek draws the same particles as a run from the start. In live play a new
    /// instance is simulated up to its age once and then plays on Unity's own particle update,
    /// which costs nothing on the main thread; the song clock runs in real time there, so the two
    /// agree. In the editor (the tests) nothing plays: only stills simulate.
    ///
    /// Everything draws in the world camera's pixel grid (README, Decision 10): the library may
    /// opt an entry out with a reason (<see cref="EffectSpec.FullRes"/>), and none does, so this
    /// draws no full-resolution layer.
    /// </summary>
    public sealed class EffectLayer : IDisposable
    {
        /// <summary>Over every character, ranked from here; ground effects over the tiles (from -1100) and under shadows (-1).</summary>
        public const int OverOrder = 30000, GroundOrder = -900;
        /// <summary>A still's simulation step.</summary>
        const double StepS = 1.0 / 30;
        /// <summary>Effects this far beyond the view (plus their scale) still live, so they don't pop in at the edges.</summary>
        const float MarginM = 3f;
        /// <summary>A live frame further than this from the last re-simulates everything, as a still does.</summary>
        const double JumpS = 0.25;

        internal sealed class Kind
        {
            public EffectSpec Spec;
            public GameObject Prefab;
            public readonly Stack<Instance> Free = new Stack<Instance>();
            public int Count;
        }

        /// <summary>A prefab's instance, set up to be driven by hand (<see cref="Make"/>).</summary>
        public sealed class Instance
        {
            public GameObject Go;
            public Transform T;
            public ParticleSystem[] Roots, All;
            public ParticleSystemRenderer[] Renderers;
            public int[] AuthoredOrder;
            /// <summary>The prefab's own rotation and scale, which the library's aim and scale compose with (mesh particles lie in it).</summary>
            public Quaternion AuthoredRotation;
            public Vector3 AuthoredScale;
            /// <summary>The beam's lines (glow, then core) and their materials, when the entry has a beam.</summary>
            public LineRenderer[] Beam;
            public Material[] Owned;
            internal Kind Kind;
            internal bool Paused;
        }

        struct Want
        {
            public long Key;          // the event's index, or -(entity + 1) for a flight
            public Kind Kind;
            public int Event, Entity; // -1 when not one
            public double Born;
            public uint Seed;
        }

        readonly Timeline _tl;
        readonly Kind[] _byEffect, _byProjectile;
        readonly double _maxDuration;
        readonly Dictionary<int, double> _sweeps = new Dictionary<int, double>();
        readonly string _rosterJson;
        readonly Shader _glow;
        readonly GameObject _root, _holder;
        readonly Dictionary<long, Instance> _live = new Dictionary<long, Instance>(), _kept = new Dictionary<long, Instance>();
        readonly List<Want> _wants = new List<Want>();
        readonly List<long> _order = new List<long>();
        readonly List<Instance> _made = new List<Instance>();
        double _lastT = double.NaN;

        public readonly LightPool Lights;
        public readonly EffectLibrary Library;
        public bool Enabled = true;
        /// <summary>Instances live after the last frame.</summary>
        public int Live => _live.Count;

        /// <param name="prefabs">The library's prefabs, by its paths (<see cref="EffectLibrary.Prefabs"/>).</param>
        /// <param name="glow">The beam's line shader (Glow.shader).</param>
        public EffectLayer(Timeline tl, EffectLibrary library, IReadOnlyDictionary<string, GameObject> prefabs, string rosterJson, Shader glow, Transform parent)
        {
            _tl = tl;
            _glow = glow;
            Library = library;
            _rosterJson = rosterJson;
            var problems = library.Problems(tl.EffectNames, tl.ProjectileNames);
            if (problems.Count > 0) throw new InvalidOperationException("EffectLayer: " + string.Join("; ", problems));
            _root = new GameObject("Effects");
            _root.transform.SetParent(parent, false);
            // New instances are made under an inactive holder, so their vendor scripts never wake (below).
            _holder = new GameObject("Effect Holder");
            _holder.transform.SetParent(_root.transform, false);
            _holder.SetActive(false);
            Kind Make(EffectSpec s) =>
                new Kind { Spec = s, Prefab = prefabs.TryGetValue(s.Prefab, out var p) && p != null ? p : throw new InvalidOperationException("EffectLayer: no prefab " + s.Prefab + " (run demo2d unity BakePlayback)") };
            _byEffect = Array.ConvertAll(tl.EffectNames, n => Make(library.Kinds[n]));
            _byProjectile = Array.ConvertAll(tl.ProjectileNames, n => Make(library.Flights[n]));
            foreach (var k in _byEffect) _maxDuration = Math.Max(_maxDuration, k.Spec.DurationS);
            Lights = new LightPool("Effect Lights", library.LightPool, _root.transform);
        }

        /// <summary>
        /// Shows the effects live at song time t inside <paramref name="view"/> (world metres), with
        /// the projectiles among <paramref name="active"/>; <paramref name="still"/> simulates every
        /// instance exactly (a still, or any frame in the editor).
        /// </summary>
        public void Frame(double t, Rect view, List<int> active, bool still)
        {
            bool exact = still || !Application.isPlaying || double.IsNaN(_lastT) || t < _lastT || t - _lastT > JumpS;
            _lastT = t;
            _wants.Clear();
            if (Enabled) Gather(t, view, active);

            // Release what's no longer wanted, keep the rest, make the new.
            _kept.Clear();
            foreach (var w in _wants)
                if (_live.TryGetValue(w.Key, out var inst) && inst.Kind == w.Kind) { _kept[w.Key] = inst; _live.Remove(w.Key); }
            foreach (var inst in _live.Values) Release(inst);
            _live.Clear();
            _order.Clear();
            foreach (var w in _wants)
            {
                bool fresh = !_kept.TryGetValue(w.Key, out var inst);
                if (fresh) inst = Acquire(w.Kind);
                _live[w.Key] = inst;
                _order.Add(w.Key);
                if (fresh || exact) Simulate(inst, w, t);
                else Place(inst, w, t);
                if (w.Kind.Spec.Light != null)
                {
                    var s = w.Kind.Spec;
                    float scale = w.Event >= 0 ? _tl.Effects[w.Event].Scale : 1f;
                    Lights.Offer(inst.T.localPosition, new Color(s.Light.R, s.Light.G, s.Light.B), s.LightRadiusFor(scale), s.Light.At(t - w.Born), w.Key);
                }
            }
            // Live play carries on with Unity's particle update; a still (and the editor) stays where it simulated to.
            if (Application.isPlaying && !still)
                foreach (var inst in _live.Values)
                    if (inst.Paused) { foreach (var r in inst.Roots) r.Play(true); inst.Paused = false; }
            Rank();
            Lights.Commit();
        }

        void Gather(double t, Rect view, List<int> active)
        {
            foreach (var k in _byEffect) k.Count = 0;
            foreach (var k in _byProjectile) k.Count = 0;
            int first = _tl.FirstEffectAt(t - _maxDuration);
            int end = first;
            while (end < _tl.Effects.Length && _tl.Effects[end].Time <= t) end++;
            // Newest first, so each kind keeps its newest max; then back into event order.
            for (int i = end - 1; i >= first; i--)
            {
                var ev = _tl.Effects[i];
                var kind = _byEffect[ev.Effect];
                if (t - ev.Time >= kind.Spec.DurationS || kind.Count >= kind.Spec.Max) continue;
                var w = new Want { Key = i, Kind = kind, Event = i, Entity = -1, Born = ev.Time, Seed = ev.Seed };
                if (!InView(view, Position(w, t), kind.Spec.ScaleFor(ev.Scale))) continue;
                kind.Count++;
                _wants.Add(w);
            }
            _wants.Reverse();
            for (int j = active.Count - 1; j >= 0; j--)
            {
                int e = active[j];
                var info = _tl.Entities[e];
                if (info.Kind != EntityKind.Projectile) continue;
                var kind = _byProjectile[info.Type];
                if (kind.Count >= kind.Spec.Max) continue;
                var w = new Want { Key = -(e + 1L), Kind = kind, Event = -1, Entity = e, Born = info.Spawn, Seed = info.Seed };
                if (!InView(view, Position(w, t), kind.Spec.Scale)) continue;
                kind.Count++;
                _wants.Add(w);
            }
        }

        static bool InView(Rect view, Vector2 p, float scale)
        {
            float m = MarginM + 2 * scale;
            return p.x > view.xMin - m && p.x < view.xMax + m && p.y > view.yMin - m && p.y < view.yMax + m;
        }

        /// <summary>Where a wanted effect is at song time τ: its anchor (a projectile 1 m up, an entity plus the event's offset, or the event's point), moved along its yaw.</summary>
        Vector2 Position(in Want w, double tau)
        {
            if (w.Entity >= 0)
            {
                var p = _tl.PositionAt(w.Entity, tau);
                return new Vector2(p.X, p.Y + SpriteCrowd.ProjectileHeightM);
            }
            var ev = _tl.Effects[w.Event];
            // The sim's 3D offsets: x and z on the ground, y up, which the side view draws as up the screen.
            var at = new Vector2(ev.Position.X, ev.Position.Z + ev.Position.Y);
            if (ev.AttachTo >= 0)
            {
                var p = _tl.PositionAt(ev.AttachTo, tau);
                at += new Vector2(p.X, p.Y);
            }
            var spec = w.Kind.Spec;
            if (spec.ReachM > 0)
            {
                var d = Easing.Dir(Yaw(w, tau));
                at += new Vector2(d.X, d.Y) * (spec.ReachM * spec.ScaleFor(ev.Scale) / spec.Scale);
            }
            return at;
        }

        float Yaw(in Want w, double tau)
        {
            var ev = _tl.Effects[w.Event];
            if (!w.Kind.Spec.Sweep) return ev.Yaw;
            return ev.Yaw + (float)(2 * Math.PI * (tau - ev.Time) / SweepOf(w.Event));
        }

        double SweepOf(int e)
        {
            if (!_sweeps.TryGetValue(e, out var dur))
                _sweeps[e] = dur = EffectLibrary.SweepSeconds(_tl.Map, _rosterJson, _tl.EffectNames[_tl.Effects[e].Effect], _tl.Effects[e].Time);
            return dur;
        }

        /// <summary>
        /// Lays an instance's beam from <paramref name="origin"/> along <paramref name="yaw"/>: a
        /// line of the entry's segments, each inner point pushed sideways by a jitter drawn from the
        /// seed and the 1/30 s step of <paramref name="tau"/>, so it crackles yet a still is exact.
        /// </summary>
        public static void DrawBeam(Instance inst, EffectSpec spec, Vector2 origin, float yaw, float length, float alpha, uint seed, double tau)
        {
            var b = spec.Beam;
            var d = Easing.Dir(yaw);
            var dir = new Vector2(d.X, d.Y);
            var side = new Vector2(-dir.y, dir.x);
            uint step = (uint)Math.Max(0, Math.Floor(tau * 30));
            var glow = inst.Beam[0];
            var core = inst.Beam[1];
            glow.positionCount = core.positionCount = b.Segments + 1;
            for (int i = 0; i <= b.Segments; i++)
            {
                float f = (float)i / b.Segments;
                float j = i == 0 || i == b.Segments ? 0 : ((Rng.Hash(seed, step, (uint)i) & 0xFFFF) / 65535f - 0.5f) * 2 * b.JitterM * Mathf.Min(1, 4 * f);
                var p = origin + dir * (length * f) + side * j;
                var v = new Vector3(p.x, p.y, 0);
                glow.SetPosition(i, v);
                core.SetPosition(i, v);
            }
            glow.widthMultiplier = b.GlowWidthPx / CharacterAtlas.PixelsPerMetre;
            core.widthMultiplier = b.CoreWidthPx / CharacterAtlas.PixelsPerMetre;
            var white = new Color(1, 1, 1, alpha);
            glow.startColor = glow.endColor = white;
            core.startColor = core.endColor = white;
        }

        void Place(Instance inst, in Want w, double tau)
        {
            var p = Position(w, tau);
            inst.T.localPosition = new Vector3(p.x, p.y, 0);
            var spec = w.Kind.Spec;
            float scale = w.Event >= 0 ? spec.ScaleFor(_tl.Effects[w.Event].Scale) : spec.Scale;
            inst.T.localScale = inst.AuthoredScale * scale;
            var aim = Quaternion.identity;
            if (spec.Aim != EffectAim.None && w.Event >= 0)
            {
                var d = Easing.Dir(Yaw(w, tau));
                aim = spec.Aim == EffectAim.Side
                    ? (d.X < 0 ? Quaternion.Euler(0, 180, 0) : Quaternion.identity)   // the side view mirrors a blow to the left
                    : Quaternion.Euler(0, 0, Mathf.Atan2(d.Y, d.X) * Mathf.Rad2Deg - 90); // the beam: its +y axis along the yaw
            }
            inst.T.localRotation = aim * inst.AuthoredRotation;
            if (inst.Beam != null)
            {
                var ev = _tl.Effects[w.Event];
                double age = tau - ev.Time, sweep = SweepOf(w.Event);
                float alpha = age < sweep ? 1f : Mathf.Clamp01(1f - (float)((age - sweep) / spec.Beam.FadeS));
                DrawBeam(inst, spec, p, Yaw(w, tau), spec.BeamLengthFor(ev.Scale), alpha, ev.Seed, tau);
            }
        }

        /// <summary>Re-seeds the instance and plays it from its event to t in fixed steps, following its anchor; it stays paused there.</summary>
        void Simulate(Instance inst, Want w, double t)
        {
            SimulateTo(inst, w.Seed, Math.Max(0, t - w.Born), tau => Place(inst, w, w.Born + tau));
            inst.Paused = true;
        }

        /// <summary>
        /// Re-seeds an instance and plays it from the start to <paramref name="age"/> seconds in
        /// steps of at most 1/30 s, calling <paramref name="place"/> with each step's end (seconds
        /// from the start) before simulating it, so an instance following something trails behind
        /// it as it would have played.
        /// </summary>
        public static void SimulateTo(Instance inst, uint seed, double age, Action<double> place = null)
        {
            foreach (var ps in inst.Roots) ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            for (int i = 0; i < inst.All.Length; i++) inst.All[i].randomSeed = Rng.Hash(seed, (uint)i, 0xEFC7);
            int n = Math.Max(1, (int)Math.Ceiling(age / StepS - 1e-9));
            double dt = age / n;
            for (int k = 0; k < n; k++)
            {
                place?.Invoke((k + 1) * dt);
                foreach (var ps in inst.Roots) ps.Simulate((float)dt, true, k == 0, false);
            }
        }

        /// <summary>Draw order: ground effects, then the rest over the crowd, each in event order, keeping a prefab's own order among its parts.</summary>
        void Rank()
        {
            _order.Sort();
            int over = 0, ground = 0;
            foreach (var key in _order)
            {
                // Flights (negative keys) sort first; events by index after.
                var inst = _live[key];
                int base_ = inst.Kind.Spec.Ground ? GroundOrder + 8 * ground++ : OverOrder + 8 * over++;
                for (int r = 0; r < inst.Renderers.Length; r++) inst.Renderers[r].sortingOrder = base_ + inst.AuthoredOrder[r];
                if (inst.Beam != null) { inst.Beam[0].sortingOrder = base_ + 6; inst.Beam[1].sortingOrder = base_ + 7; }
            }
        }

        Instance Acquire(Kind kind)
        {
            Instance inst;
            if (kind.Free.Count > 0) inst = kind.Free.Pop();
            else
            {
                inst = Make(kind.Prefab, kind.Spec, _holder.transform, _root.transform, _glow);
                inst.Kind = kind;
                _made.Add(inst);
            }
            inst.Go.SetActive(true);
            return inst;
        }

        void Release(Instance inst)
        {
            foreach (var ps in inst.Roots) ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            inst.Go.SetActive(false);
            inst.Paused = false;
            inst.Kind.Free.Push(inst);
        }

        /// <summary>
        /// A fresh, inactive instance of an entry's prefab under <paramref name="parent"/>: made under
        /// the inactive <paramref name="holder"/> so nothing in it wakes, stripped of CFXR's scripts
        /// (their clear behaviour destroys the instance, and their lights and camera shake are 3D)
        /// and its 3D lights, its systems set to wait to be simulated, scale with their transform and
        /// take the entry's tint.
        /// </summary>
        public static Instance Make(GameObject prefab, EffectSpec spec, Transform holder, Transform parent, Shader glow = null)
        {
            var go = UnityEngine.Object.Instantiate(prefab, holder, false);
            go.name = spec.Name;
            foreach (var mb in go.GetComponentsInChildren<MonoBehaviour>(true)) UnityEngine.Object.DestroyImmediate(mb);
            foreach (var l in go.GetComponentsInChildren<Light>(true)) UnityEngine.Object.DestroyImmediate(l);
            var all = go.GetComponentsInChildren<ParticleSystem>(true);
            var roots = new List<ParticleSystem>();
            foreach (var ps in all)
            {
                var main = ps.main;
                main.playOnAwake = false;
                main.scalingMode = ParticleSystemScalingMode.Hierarchy;
                main.stopAction = ParticleSystemStopAction.None;
                main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
                if (spec.Tint != null)
                {
                    var c = new Color(spec.Tint[0], spec.Tint[1], spec.Tint[2], spec.Tint[3]);
                    main.startColor = Tinted(main.startColor, c);
                }
                ps.useAutoRandomSeed = false;
                if (ps.transform.parent.GetComponentInParent<ParticleSystem>(true) == null) roots.Add(ps);
            }
            var renderers = go.GetComponentsInChildren<ParticleSystemRenderer>(true);
            var authored = Array.ConvertAll(renderers, r => Mathf.Clamp(r.sortingOrder, 0, 7));
            go.SetActive(false);
            go.transform.SetParent(parent, false);
            var inst = new Instance
            {
                Go = go, T = go.transform, Roots = roots.ToArray(), All = all, Renderers = renderers, AuthoredOrder = authored,
                AuthoredRotation = go.transform.localRotation, AuthoredScale = go.transform.localScale,
            };
            if (spec.Beam != null)
            {
                if (glow == null) throw new ArgumentException("EffectLayer: " + spec.Name + "'s beam needs the glow shader");
                inst.Owned = new[] { BeamMaterial(glow, spec.Beam.Glow, "Beam Glow"), BeamMaterial(glow, spec.Beam.Core, "Beam Core") };
                inst.Beam = new LineRenderer[2];
                for (int i = 0; i < 2; i++)
                {
                    var line = new GameObject(i == 0 ? "Beam Glow" : "Beam Core").AddComponent<LineRenderer>();
                    line.transform.SetParent(go.transform, false);
                    line.useWorldSpace = true;
                    line.alignment = LineAlignment.View;
                    line.numCapVertices = 0;
                    line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    line.sharedMaterial = inst.Owned[i];
                    inst.Beam[i] = line;
                }
            }
            return inst;
        }

        /// <summary>A glow material for an HDR colour: the colour scaled into 0..1 (vertex colours clamp), its brightest channel as the intensity.</summary>
        static Material BeamMaterial(Shader glow, float[] c, string name)
        {
            float k = Mathf.Max(1f, Mathf.Max(c[0], Mathf.Max(c[1], c[2])));
            var m = new Material(glow) { name = name };
            m.SetFloat("_Intensity", k * c[3]);
            // The line's vertex colour carries the fade, so the hue rides on a one-texel texture.
            var tex = new Texture2D(1, 1, TextureFormat.RGBA32, false) { name = name };
            tex.SetPixel(0, 0, new Color(c[0] / k, c[1] / k, c[2] / k, 1));
            tex.Apply(false, true);
            m.mainTexture = tex;
            return m;
        }

        static ParticleSystem.MinMaxGradient Tinted(ParticleSystem.MinMaxGradient g, Color c)
        {
            switch (g.mode)
            {
                case ParticleSystemGradientMode.Color: return new ParticleSystem.MinMaxGradient(g.color * c);
                case ParticleSystemGradientMode.TwoColors: return new ParticleSystem.MinMaxGradient(g.colorMin * c, g.colorMax * c);
                case ParticleSystemGradientMode.Gradient: return new ParticleSystem.MinMaxGradient(Tinted(g.gradient, c));
                case ParticleSystemGradientMode.TwoGradients: return new ParticleSystem.MinMaxGradient(Tinted(g.gradientMin, c), Tinted(g.gradientMax, c));
                default: return new ParticleSystem.MinMaxGradient(Tinted(g.gradient, c)) { mode = g.mode };
            }
        }

        static Gradient Tinted(Gradient g, Color c)
        {
            var colors = g.colorKeys;
            for (int i = 0; i < colors.Length; i++) colors[i].color *= new Color(c.r, c.g, c.b, 1);
            var alphas = g.alphaKeys;
            for (int i = 0; i < alphas.Length; i++) alphas[i].alpha *= c.a;
            var copy = new Gradient { mode = g.mode };
            copy.SetKeys(colors, alphas);
            return copy;
        }

        /// <summary>Particles alive in the live instances (the measurement's count; walks every system).</summary>
        public int ParticleCount()
        {
            int n = 0;
            foreach (var inst in _live.Values) foreach (var ps in inst.All) n += ps.particleCount;
            return n;
        }

        public void Dispose()
        {
            Lights.Dispose();
            foreach (var inst in _made)
            {
                SongPlayback.Destroy(inst.Go);
                if (inst.Owned == null) continue;
                foreach (var m in inst.Owned) { SongPlayback.Destroy(m.mainTexture); SongPlayback.Destroy(m); }
            }
            SongPlayback.Destroy(_root);
        }
    }
}
