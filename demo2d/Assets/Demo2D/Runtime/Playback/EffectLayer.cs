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
    /// Which instances live, and where, is the engine-free <see cref="EffectSchedule"/>'s: a
    /// function of the song time. How they look is too, on a still: every live instance is
    /// re-seeded with its event's seed and simulated from its event to the still's time in 1/30 s steps, following its anchor (a monster, the hero, a
    /// projectile), so a seek draws the same particles as a run from the start. In live play a new
    /// instance is simulated up to its age once and then plays on Unity's own particle update,
    /// which costs nothing on the main thread; the song clock runs in real time there, so the two
    /// agree. In the editor (the tests) nothing plays: only stills simulate.
    ///
    /// Everything draws in the world camera's pixel grid (README, Decision 10). No entry needs a
    /// full-resolution layer, so there is none, and the library rejects an entry asking for one.
    /// </summary>
    public sealed class EffectLayer : IDisposable
    {
        /// <summary>Over every character, ranked from here; ground effects over the tiles (from -1100) and under shadows (-1).</summary>
        public const int OverOrder = 30000, GroundOrder = -900;
        /// <summary>A still's simulation step.</summary>
        const double StepS = 1.0 / 30;
        /// <summary>A live frame further than this from the last re-simulates everything, as a still does.</summary>
        const double JumpS = 0.25;

        internal sealed class Kind
        {
            public EffectSpec Spec;
            public GameObject Prefab;
            public readonly Stack<Instance> Free = new Stack<Instance>();
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

        readonly Dictionary<EffectSpec, Kind> _kinds = new Dictionary<EffectSpec, Kind>();
        readonly Shader _glow;
        readonly GameObject _root, _holder;
        readonly Dictionary<LiveEffect, Instance> _live = new Dictionary<LiveEffect, Instance>(), _kept = new Dictionary<LiveEffect, Instance>();
        readonly List<LiveEffect> _wants = new List<LiveEffect>();
        static readonly Comparison<LiveEffect> ByOrder = (a, b) => a.Order.CompareTo(b.Order);
        readonly List<Instance> _made = new List<Instance>(), _warm = new List<Instance>();
        double _lastT = double.NaN;

        public readonly LightPool Lights;
        public readonly EffectLibrary Library;
        public readonly EffectSchedule Schedule;
        public bool Enabled = true;
        /// <summary>Instances live after the last frame.</summary>
        public int Live => _live.Count;

        /// <param name="prefabs">The library's prefabs, by its paths (<see cref="EffectLibrary.Prefabs"/>).</param>
        /// <param name="glow">The beam's line shader (Glow.shader).</param>
        public EffectLayer(Timeline tl, EffectLibrary library, Roster roster, IReadOnlyDictionary<string, GameObject> prefabs, Shader glow, Transform parent)
        {
            _glow = glow;
            Library = library;
            Schedule = new EffectSchedule(tl, library, roster);
            _root = new GameObject("Effects");
            _root.transform.SetParent(parent, false);
            // New instances are made under an inactive holder, so their vendor scripts never wake (below).
            _holder = new GameObject("Effect Holder");
            _holder.transform.SetParent(_root.transform, false);
            _holder.SetActive(false);
            foreach (var s in library.All())
                _kinds[s] = new Kind { Spec = s, Prefab = prefabs.TryGetValue(s.Prefab, out var p) && p != null ? p : throw new InvalidOperationException("EffectLayer: no prefab " + s.Prefab + " (run demo2d unity BakePlayback)") };
            Lights = new LightPool("Effect Lights", library.LightPool, _root.transform);
        }

        /// <summary>
        /// Shows the effects live at song time t inside <paramref name="view"/> (world metres), with
        /// the projectiles among <paramref name="active"/>; <paramref name="still"/> simulates every
        /// instance exactly (a still, or any frame in the editor).
        /// </summary>
        public void Frame(double t, Rect view, List<int> active, bool still)
        {
            foreach (var inst in _warm) Release(inst);
            _warm.Clear();
            bool exact = still || !Application.isPlaying || double.IsNaN(_lastT) || t < _lastT || t - _lastT > JumpS;
            _lastT = t;
            _wants.Clear();
            if (Enabled) Schedule.Live(t, new Box2(view.xMin, view.yMin, view.xMax, view.yMax), active, _wants);

            // Release what's no longer wanted, keep the rest, make the new.
            _kept.Clear();
            foreach (var fx in _wants)
                if (_live.TryGetValue(fx, out var inst)) { _kept[fx] = inst; _live.Remove(fx); }
            foreach (var inst in _live.Values) Release(inst);
            _live.Clear();
            foreach (var fx in _wants)
            {
                bool fresh = !_kept.TryGetValue(fx, out var inst);
                if (fresh) inst = Acquire(_kinds[fx.Spec]);
                _live[fx] = inst;
                if (fresh || exact) Simulate(inst, fx, t);
                else Place(inst, fx, t);
                var light = fx.Spec.Light;
                if (light != null)
                    Lights.Offer(inst.T.localPosition, new Color(light.R, light.G, light.B), fx.LightRadius, Schedule.LightIntensity(fx, t), fx.Order);
            }
            // Live play carries on with Unity's particle update; a still (and the editor) stays where it simulated to.
            if (Application.isPlaying && !still)
                foreach (var inst in _live.Values)
                    if (inst.Paused) { foreach (var r in inst.Roots) r.Play(true); inst.Paused = false; }
            Rank();
            Lights.Commit();
        }

        /// <summary>
        /// Makes every kind's whole pool (its cap) now and shows one instance of each at
        /// <paramref name="at"/>, a moment into its play, until the next <see cref="Frame"/>, so a
        /// draw in between compiles their shaders. Otherwise a kind's first instance or a pool's
        /// growth stalls a frame in the song (PX10's song check: 45 ms on the first frame, 43 ms at
        /// 176 s).
        /// </summary>
        public void Prewarm(Vector2 at)
        {
            foreach (var kind in _kinds.Values)
            {
                var spare = new List<Instance>();
                for (int i = 1; i < kind.Spec.Max; i++) spare.Add(Acquire(kind));
                foreach (var inst in spare) Release(inst);
                var shown = Acquire(kind);
                shown.T.localPosition = new Vector3(at.x, at.y, 0);
                SimulateTo(shown, 1, 0.1);
                if (shown.Beam != null) DrawBeam(shown, kind.Spec, at, 0, 2, 1, 1, 0);
                _warm.Add(shown);
            }
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

        void Place(Instance inst, LiveEffect fx, double tau)
        {
            var p = Schedule.Position(fx, tau);
            inst.T.localPosition = new Vector3(p.X, p.Y, 0);
            var spec = fx.Spec;
            inst.T.localScale = inst.AuthoredScale * fx.Scale;
            var ev = fx as EventEffect;
            var aim = Quaternion.identity;
            if (spec.Aim != EffectAim.None && ev != null)
            {
                var d = Easing.Dir(Schedule.Yaw(ev, tau));
                aim = spec.Aim == EffectAim.Side
                    ? (d.X < 0 ? Quaternion.Euler(0, 180, 0) : Quaternion.identity)   // the side view mirrors a blow to the left
                    : Quaternion.Euler(0, 0, Mathf.Atan2(d.Y, d.X) * Mathf.Rad2Deg - 90); // the beam: its +y axis along the yaw
            }
            inst.T.localRotation = aim * inst.AuthoredRotation;
            if (inst.Beam != null)
                DrawBeam(inst, spec, new Vector2(p.X, p.Y), Schedule.Yaw(ev, tau), spec.BeamLengthFor(ev.Data.Scale), Schedule.BeamAlpha(ev, tau), ev.Seed, tau);
        }

        /// <summary>Re-seeds the instance and plays it from its event to t in fixed steps, following its anchor; it stays paused there.</summary>
        void Simulate(Instance inst, LiveEffect fx, double t)
        {
            SimulateTo(inst, fx.Seed, Math.Max(0, t - fx.Born), tau => Place(inst, fx, fx.Born + tau));
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
            _wants.Sort(ByOrder);
            int over = 0, ground = 0;
            foreach (var fx in _wants)
            {
                var inst = _live[fx];
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
