using System;
using System.Collections.Generic;
using Demo3D.Runtime.Playback;
using PS.Choreography;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace Demo3D.Runtime.Arena
{
    /// <summary>
    /// The structure breaks on screen (environment.md, Breaks on screen; epic ps-suy, Decision 8). At each moment of
    /// `--structure-breaks` (<see cref="BreakMoments"/>: the hero's bolt strikes on the grid and the pieces leave the
    /// obstacle map), the struck pieces' renderers hide and their baked shards (<see cref="StructureShards"/>) take their
    /// place, in the piece's own materials (the kit's under baseline, the `--structure-style`'s otherwise), and fly.
    ///
    /// - Each shard is a PhysX rigidbody in its own physics scene, stepped in fixed steps of <see cref="Step"/> on the
    ///   song clock (so `--fixed-time` runs give the same debris, and the crystal shatter's scene steps apart from it),
    ///   kicked outward from the strike, along the bolt and up, the nearest hardest, with wide variance; under
    ///   <see cref="GravityScale"/> g they clatter down, settle, and after <see cref="LingerSeconds"/> sink into the
    ///   ground. The debris never blocks: the horde walks the opening from the moment it opens (choreography.md).
    /// - The break spreads from the strike as a crack front at <see cref="FrontSpeed"/>: the shards it reaches flash and
    ///   fly. The strike's own shards fly on the impact frame; nothing builds up before it.
    /// - Per style (a structure shader's _SL_PieceFx: x the hit flash, y the death, z 1 on a shard, with its rest
    ///   position <c>_SL_Rest</c>, so a flying shard keeps the patterns it had in the wall): crystal's front is slower
    ///   (<see cref="CrystalFrontSpeed"/>), so its seams open and glow across the wall (each shard steps apart as the
    ///   front passes, baring its lit inside faces) a moment before they burst; fire's wall chars and collapses, its
    ///   shards slumping more than flying, molten inside and burning out from the base; hologram's glitch and scan out,
    ///   drifting under light gravity. The other styles and baseline share the flash and the flight.
    /// - The ghost shadows go with the pieces (<see cref="ArenaRuntime.Break"/>), and a seek backwards puts every piece
    ///   back. Breaks before a run's first frame are not replayed: their pieces are simply gone.
    /// </summary>
    public sealed class StructureShatter : IDisposable
    {
        public const float Step = 1f / 120f;
        public const int MaxStepsPerFrame = 12;
        public const float GravityScale = 2.2f, Density = 2400f; // kg/m³, stone
        public const float FrontSpeed = 70f, CrystalFrontSpeed = 22f; // m/s
        public const float LingerSeconds = 2.4f, SinkSeconds = 1f;
        public const float FlashSeconds = 0.2f;
        /// <summary>Crystal: how far a shard steps out of the wall as the crack front passes (m), and how long it holds there.</summary>
        public const float CrackOpenM = 0.08f, CrackHoldSeconds = 0.07f;

        static readonly int PieceFxId = Shader.PropertyToID("_SL_PieceFx");
        static readonly int RestId = Shader.PropertyToID("_SL_Rest");

        /// <summary>A style's flight and treatment.</summary>
        struct Manner
        {
            public float Front, Outward, Blow, Up, Spin, Gravity;
            public float DeathFrom, DeathSeconds; // the structure shader's death ramp from this age (s), over this long; 0: none
            public float GlowSeconds;             // the break glow's fade (_SL_PieceFx.w: the shard blooms past the set's grade)
            public float Flash;                   // the white flash as the front passes (_SL_PieceFx.x)
            public bool Crack;                    // crystal's crack opening before the flight
        }

        static Manner MannerOf(string style)
        {
            switch (style)
            {
                case "crystal": return new Manner { Front = CrystalFrontSpeed, Outward = 3.4f, Blow = 6.5f, Up = 2.6f, Spin = 7f, Gravity = GravityScale, Crack = true, GlowSeconds = 0.7f, Flash = 0.25f };
                case "fire": return new Manner { Front = FrontSpeed, Outward = 1.3f, Blow = 2.4f, Up = 1.2f, Spin = 3f, Gravity = GravityScale, DeathFrom = 0.35f, DeathSeconds = 1.8f, GlowSeconds = 1.6f, Flash = 0.6f };
                case "hologram": return new Manner { Front = FrontSpeed, Outward = 1.8f, Blow = 3.2f, Up = 1.6f, Spin = 2.5f, Gravity = 0.5f, DeathFrom = 0.05f, DeathSeconds = 0.9f, GlowSeconds = 0.6f, Flash = 1f };
                default: return new Manner { Front = FrontSpeed, Outward = 2.8f, Blow = 5.5f, Up = 2.2f, Spin = 6f, Gravity = GravityScale, GlowSeconds = 0.35f, Flash = 1f };
            }
        }

        /// <summary>One piece: its scene object's renderers (the kit's, a style's flames) and the shards of each mesh renderer.</summary>
        sealed class Piece
        {
            public int Placement;
            public Transform Root;
            public Renderer[] Renderers;
            public Bounds Bounds;
            public bool Broken;
            public readonly List<(MeshRenderer renderer, StructureShards.Set set)> Cut = new List<(MeshRenderer, StructureShards.Set)>();
        }

        sealed class Shard
        {
            public GameObject Object;
            public Mesh Mesh;
            public Transform Transform;
            public Rigidbody Body;
            public MeshRenderer Renderer;
            public MaterialPropertyBlock Block;
            public bool Styled;
            public float Delay;            // when the crack front reaches it, after the strike (s)
            public bool Flying, Sinking;
            public Vector3 Rest;           // where it stood in the wall
            public Vector3 Away;           // from the strike, unit
            public Vector3 Velocity, Spin; // its launch
            public float SinkFrom, SinkDepth;
        }

        sealed class Live
        {
            public int Moment;
            public double At;     // the strike's song time
            public double Start;  // physics time of the first frame it was live
            public readonly List<Shard> Shards = new List<Shard>();
        }

        readonly BreakMoments _moments;
        readonly ArenaRuntime _arena;
        readonly GroundLayout _ground;
        readonly StructureShards _shards;
        readonly Shader _patch;
        readonly Manner _manner;
        readonly string _style;
        readonly Scene _scene;
        readonly PhysicsScene _physics;
        readonly GameObject _root;
        readonly PhysicsMaterial _material;
        readonly Dictionary<int, Piece> _pieces = new Dictionary<int, Piece>();
        readonly List<Live> _live = new List<Live>();
        readonly Dictionary<Mesh, List<Shard>> _pool = new Dictionary<Mesh, List<Shard>>();
        bool _resolved;
        int _next; // the next moment to break
        double _t0 = double.NaN, _last = double.NaN;
        long _steps;

        public int Shards { get; private set; }
        public int PeakShards { get; private set; }

        /// <summary>
        /// The breaks of <paramref name="moments"/> on <paramref name="arena"/>, whose scene places
        /// <paramref name="ground"/>'s placements (the layout the timeline ran on), in <paramref name="style"/>'s manner (the
        /// resolved `--structure-style`).
        /// </summary>
        public StructureShatter(BreakMoments moments, ArenaRuntime arena, GroundLayout ground, string style)
        {
            _moments = moments;
            _arena = arena;
            _ground = ground;
            _style = style;
            _manner = MannerOf(style);
            _shards = StructureShards.Load();
            _patch = _shards.ShadowmaskPatch != null ? _shards.ShadowmaskPatch
                : throw new InvalidOperationException("StructureShatter: StructureShards has no shadowmask patch shader (re-run `demo3d unity BakeAll -only StructureShards`)");
            _scene = SceneManager.CreateScene("StructureShatter", new CreateSceneParameters(LocalPhysicsMode.Physics3D));
            _physics = _scene.GetPhysicsScene();
            _root = new GameObject("StructureShatter");
            SceneManager.MoveGameObjectToScene(_root, _scene);
            _material = new PhysicsMaterial("rubble")
            {
                bounciness = 0.2f, dynamicFriction = 0.6f, staticFriction = 0.8f,
                bounceCombine = PhysicsMaterialCombine.Average, frictionCombine = PhysicsMaterialCombine.Average,
            };
            var floor = new GameObject("RubbleFloor");
            floor.transform.SetParent(_root.transform, false);
            var box = floor.AddComponent<BoxCollider>();
            box.size = new Vector3(4000, 1, 4000);
            box.center = new Vector3(0, -0.5f, 0);
            box.sharedMaterial = _material;
            Debug.Log($"StructureShatter: {moments.Count} breaks in the {style} manner");
        }

        /// <summary>
        /// Finds each broken placement's scene object: the piece the arena baker placed for it, under Static/Structures or
        /// Static/Landmarks at its position, and the shards of its meshes. A piece missing from the scene, or a mesh with
        /// no shards, fails loudly.
        /// </summary>
        void Resolve()
        {
            _resolved = true;
            var stat = _arena.Ground != null ? _arena.Ground.transform.parent.Find("Static") : null;
            if (stat == null) throw new InvalidOperationException("StructureShatter: the arena has no Static group");
            var candidates = new List<Transform>();
            foreach (var group in new[] { "Structures", "Landmarks" })
            {
                var g = stat.Find(group);
                if (g != null) foreach (Transform t in g) candidates.Add(t);
            }
            var list = _ground.Placements;
            int cut = 0;
            foreach (var m in _moments.Moments)
                foreach (var mp in m.Pieces)
                {
                    if (_pieces.ContainsKey(mp.Placement)) continue;
                    var p = list.Items[mp.Placement];
                    var at = new Vector3(p.Position.X, p.Lift, p.Position.Y);
                    Transform best = null;
                    float bestD = 0.05f;
                    foreach (var t in candidates)
                    {
                        float d = (t.position - at).magnitude;
                        if (d < bestD) { best = t; bestD = d; }
                    }
                    if (best == null)
                        throw new InvalidOperationException(FormattableString.Invariant(
                            $"StructureShatter: no piece of {_arena.SceneName} at ({at.x:F2}, {at.z:F2}) for {m.Target} ({list.Props[p.Prop].Id})"));
                    var piece = new Piece { Placement = mp.Placement, Root = best, Renderers = best.GetComponentsInChildren<Renderer>(true) };
                    bool first = true;
                    foreach (var r in piece.Renderers)
                    {
                        if (r is ParticleSystemRenderer) continue;
                        if (first) { piece.Bounds = r.bounds; first = false; }
                        else piece.Bounds.Encapsulate(r.bounds);
                        if (r is MeshRenderer mr && r.TryGetComponent<MeshFilter>(out var mf) && r.gameObject.name != "Flames")
                        {
                            var set = _shards.Of(mf.sharedMesh)
                                ?? throw new InvalidOperationException($"StructureShatter: no shards for {mf.sharedMesh?.name} of {list.Props[p.Prop].Id} " +
                                                                       $"(only structures break; re-run `demo3d unity BakeAll -only StructureShards`)");
                            piece.Cut.Add((mr, set));
                            cut += set.Shards.Length;
                        }
                    }
                    _pieces[mp.Placement] = piece;
                }
            Debug.Log($"StructureShatter: {_pieces.Count} pieces in {_arena.SceneName}, {cut} shards");
        }

        /// <summary>Brings the breaks to song time t; call once a frame once the arena is ready. A seek backwards puts every piece back.</summary>
        public void Frame(double t)
        {
            if (!_arena.Ready) return;
            if (!_resolved) Resolve();
            if (double.IsNaN(_t0) || t < _last)
            {
                Reset();
                _t0 = t;
                _steps = 0;
            }
            _last = t;
            bool changed = false;
            while (_next < _moments.Count && _moments.Moments[_next].Time <= t)
            {
                var m = _moments.Moments[_next];
                foreach (var mp in m.Pieces) Hide(_pieces[mp.Placement], true);
                // A break before the run's first frame isn't replayed: its pieces are simply gone.
                if (m.Time >= _t0 - 1e-6) Strike(_next);
                _next++;
                changed = true;
            }
            if (changed) ShadowsFollow();

            long target = (long)Math.Floor((t - _t0) / Step + 1e-6);
            int n = 0;
            while (_steps < target)
            {
                if (n++ >= MaxStepsPerFrame) { _steps = target; break; }
                Advance(_t0 + _steps * Step);
                _physics.Simulate(Step);
                _steps++;
            }
            Present(t);
        }

        /// <summary>The broken pieces' bounds to the arena, so its baked shadows follow (ArenaRuntime.Break).</summary>
        void ShadowsFollow()
        {
            var boxes = new List<Bounds>();
            foreach (var p in _pieces.Values) if (p.Broken) boxes.Add(p.Bounds);
            _arena.Break(boxes, _patch);
        }

        void Hide(Piece p, bool broken)
        {
            p.Broken = broken;
            foreach (var r in p.Renderers) if (r != null) r.enabled = !broken;
        }

        /// <summary>A moment's shards, placed where their pieces stood, in the pieces' materials, waiting for the crack front.</summary>
        void Strike(int moment)
        {
            var m = _moments.Moments[moment];
            var live = new Live { Moment = moment, At = m.Time, Start = _t0 + _steps * Step };
            var rng = new System.Random(moment * 7919 + 17);
            // The strike: on the nearest piece, at the bolt's height but inside its bounds. The front spreads from it
            // along the whole run, and the kick is hardest near it.
            var hit = m.Hit;
            float nearest = float.MaxValue, reach = 0.5f;
            foreach (var mp in m.Pieces)
            {
                var b = _pieces[mp.Placement].Bounds;
                var c = b.ClosestPoint(m.Hit);
                float d = (c - m.Hit).sqrMagnitude;
                if (d < nearest) { nearest = d; hit = c; }
            }
            foreach (var mp in m.Pieces)
            {
                var b = _pieces[mp.Placement].Bounds;
                reach = Mathf.Max(reach, (b.center - hit).magnitude + b.extents.magnitude);
            }
            foreach (var mp in m.Pieces)
            {
                var piece = _pieces[mp.Placement];
                foreach (var (renderer, set) in piece.Cut)
                {
                    var l2w = renderer.localToWorldMatrix;
                    var rot = renderer.transform.rotation;
                    var scale = renderer.transform.lossyScale;
                    float volumeScale = Mathf.Abs(scale.x * scale.y * scale.z);
                    var mats = renderer.sharedMaterials;
                    bool styled = mats.Length > 0 && mats[0] != null && mats[0].HasProperty("_HeightM");
                    var block = new MaterialPropertyBlock();
                    if (styled && renderer.HasPropertyBlock()) renderer.GetPropertyBlock(block);
                    foreach (var src in set.Shards)
                    {
                        var s = Take(src.Mesh);
                        s.Renderer.sharedMaterials = mats;
                        s.Renderer.shadowCastingMode = ShadowCastingMode.On;
                        s.Transform.SetPositionAndRotation(l2w.MultiplyPoint3x4(src.Center), rot);
                        s.Transform.localScale = scale;
                        s.Rest = s.Transform.position;
                        s.Styled = styled;
                        s.Block.Clear();
                        if (styled)
                        {
                            s.Block.SetVector("_SL_Piece", block.GetVector("_SL_Piece"));
                            s.Block.SetFloat("_HeightM", block.GetFloat("_HeightM"));
                            s.Block.SetMatrix(RestId, s.Transform.localToWorldMatrix);
                            s.Block.SetVector(PieceFxId, new Vector4(0, 0, 1, 0));
                        }
                        s.Renderer.SetPropertyBlock(s.Block);
                        var away = s.Transform.position - hit;
                        float dist = away.magnitude;
                        s.Away = dist > 1e-4f ? away / dist : m.Blow;
                        s.Delay = dist / _manner.Front;
                        s.Flying = s.Sinking = false;
                        s.Body.isKinematic = true;
                        s.Body.mass = Mathf.Max(0.5f, src.Volume * volumeScale * Density);
                        // Its share of the kick: the nearest hardest, one in five slumping, one in eight flying far.
                        float near = 1f - Mathf.Clamp01(dist / reach);
                        double u = rng.NextDouble();
                        float share = u < 0.2 ? 0.15f + 0.25f * (float)rng.NextDouble()
                                    : u > 0.875 ? 1.5f + 0.7f * (float)rng.NextDouble()
                                    : 0.55f + 0.6f * (float)rng.NextDouble();
                        share *= 0.45f + 1.1f * near;
                        var outward = s.Away;
                        outward.y = Mathf.Max(outward.y, -0.2f);
                        s.Velocity = (outward * _manner.Outward + m.Blow * _manner.Blow * (0.5f + (float)rng.NextDouble()) + Vector3.up * _manner.Up) * share;
                        s.Spin = RandomUnit(rng) * _manner.Spin * (0.4f + 0.8f * (float)rng.NextDouble());
                        s.Object.SetActive(true);
                        live.Shards.Add(s);
                    }
                }
            }
            _live.Add(live);
            Shards += live.Shards.Count;
            PeakShards = Math.Max(PeakShards, Shards);
            Debug.Log(FormattableString.Invariant($"StructureShatter: {m.Target} breaks at t {m.Time:F3} ({m.Pieces.Length} pieces, {live.Shards.Count} shards, size {m.SizeM:F1} m)"));
        }

        /// <summary>Before each physics step at physics time <paramref name="now"/>: launches the shards the front has reached, adds gravity, starts the sinking.</summary>
        void Advance(double now)
        {
            var g = Vector3.down * (9.81f * _manner.Gravity);
            foreach (var live in _live)
            {
                double age = now - live.Start;
                foreach (var s in live.Shards)
                {
                    if (!s.Flying)
                    {
                        float launch = s.Delay + (_manner.Crack ? CrackHoldSeconds : 0f);
                        if (age < launch) continue;
                        s.Flying = true;
                        s.Body.isKinematic = false;
                        s.Body.linearVelocity = s.Velocity;
                        s.Body.angularVelocity = s.Spin;
                    }
                    if (s.Sinking) continue;
                    if (age >= LingerSeconds + s.Delay)
                    {
                        s.Sinking = true;
                        s.Body.isKinematic = true;
                        s.SinkFrom = s.Transform.position.y;
                        s.SinkDepth = 2f * s.Renderer.bounds.extents.magnitude + Mathf.Max(0, s.SinkFrom);
                        continue;
                    }
                    s.Body.AddForce(g, ForceMode.Acceleration);
                }
            }
        }

        /// <summary>The shards at song time t: crystal's crack opening, the flash and the style's death, the sinking; spent breaks go back to the pool.</summary>
        void Present(double t)
        {
            for (int i = _live.Count - 1; i >= 0; i--)
            {
                var live = _live[i];
                float age = (float)(t - live.At);
                bool spent = true;
                foreach (var s in live.Shards)
                {
                    float since = age - s.Delay; // since the front reached it
                    if (s.Sinking)
                    {
                        float u = (age - LingerSeconds - s.Delay) / SinkSeconds;
                        var p = s.Transform.position;
                        s.Transform.position = new Vector3(p.x, s.SinkFrom - Mathf.Clamp01(u) * Mathf.Clamp01(u) * s.SinkDepth, p.z);
                        if (u < 1f) spent = false;
                        else s.Object.SetActive(false);
                    }
                    else spent = false;
                    if (_manner.Crack && !s.Flying && since >= 0)
                    {
                        // The seam opens: the shard steps out of the wall along its way from the strike, baring its lit inside faces.
                        float open = Mathf.Clamp01(since / CrackHoldSeconds);
                        s.Transform.position = s.Rest + s.Away * (CrackOpenM * open);
                    }
                    if (!s.Styled) continue;
                    float flash = since >= 0 ? Mathf.Clamp01(1f - since / FlashSeconds) : 0f;
                    float death = _manner.DeathSeconds > 0 ? Mathf.Clamp01((since - _manner.DeathFrom) / _manner.DeathSeconds) : 0f;
                    float glow = since >= 0 ? Mathf.Clamp01(1f - since / _manner.GlowSeconds) : 0f;
                    s.Block.SetVector(PieceFxId, new Vector4(flash * flash * _manner.Flash, death, 1, glow * glow));
                    s.Renderer.SetPropertyBlock(s.Block);
                }
                if (spent)
                {
                    foreach (var s in live.Shards) Recycle(s);
                    Shards -= live.Shards.Count;
                    _live.RemoveAt(i);
                }
            }
        }

        Shard Take(Mesh mesh)
        {
            if (!_pool.TryGetValue(mesh, out var free)) _pool[mesh] = free = new List<Shard>();
            if (free.Count > 0)
            {
                var s = free[free.Count - 1];
                free.RemoveAt(free.Count - 1);
                return s;
            }
            var go = new GameObject(mesh.name);
            go.transform.SetParent(_root.transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.receiveShadows = true;
            r.motionVectorGenerationMode = MotionVectorGenerationMode.Object;
            r.lightProbeUsage = LightProbeUsage.BlendProbes;
            r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            var col = go.AddComponent<BoxCollider>();
            col.sharedMaterial = _material;
            col.center = mesh.bounds.center;
            col.size = Vector3.Max(mesh.bounds.size * 0.85f, Vector3.one * 0.02f);
            var rb = go.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;
            rb.interpolation = RigidbodyInterpolation.None;
            rb.maxDepenetrationVelocity = 2f;
            go.SetActive(false);
            return new Shard { Object = go, Mesh = mesh, Transform = go.transform, Body = rb, Renderer = r, Block = new MaterialPropertyBlock() };
        }

        void Recycle(Shard s)
        {
            s.Object.SetActive(false);
            s.Body.isKinematic = true;
            _pool[s.Mesh].Add(s);
        }

        void Reset()
        {
            foreach (var live in _live)
                foreach (var s in live.Shards) Recycle(s);
            _live.Clear();
            Shards = 0;
            bool any = false;
            foreach (var p in _pieces.Values)
                if (p.Broken)
                {
                    Hide(p, false);
                    any = true;
                }
            if (any) ShadowsFollow();
            _next = 0;
        }

        static Vector3 RandomUnit(System.Random rng)
        {
            float z = 2f * (float)rng.NextDouble() - 1f, a = 2f * Mathf.PI * (float)rng.NextDouble();
            float r = Mathf.Sqrt(1f - z * z);
            return new Vector3(r * Mathf.Cos(a), z, r * Mathf.Sin(a));
        }

        public void Dispose()
        {
            if (_root != null) UnityEngine.Object.Destroy(_root);
            if (_material != null) UnityEngine.Object.Destroy(_material);
            if (_scene.IsValid() && _scene.isLoaded) SceneManager.UnloadSceneAsync(_scene);
        }
    }
}
