using System;
using System.Collections.Generic;
using System.Diagnostics;
using Game.Client.Arena;
using Game.Client.Look;
using Game.Client.Loop;
using PS.Game.Sim;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Debug = UnityEngine.Debug;

namespace Game.Client.Structures
{
    /// <summary>
    /// The crystal structures' breaks, regrowths and glints on screen (unity-game.md, "Crystal
    /// Structures" and "Shatter"), demo3d's StructureShatter copied and owned for the sim's events:
    /// - <b>A break</b> (StructureBreak): on the first frame at or after its song time the site's
    ///   renderers hide and their baked shards (<see cref="StructureShards"/>) take their place, in
    ///   the site's crystal materials and gem colour, and fly outward from the strike point, along the
    ///   blow and up, the nearest hardest. There is no crack front: the shards nearest the strike
    ///   launch on the impact frame and the farthest within <see cref="LaunchSpreadSeconds"/>, so the
    ///   whole structure is in flight within 50 ms, and nothing shows before the pieces move. Each
    ///   shard flashes and its fresh inside faces glow as it launches.
    /// - Shards are PhysX rigid bodies in their own local physics scene, colliding from their launch (a shard
    ///   still waiting would be a wall to its flying neighbours), stepped in fixed steps of
    ///   <see cref="Step"/> on the song clock, one frame ahead of it (<see cref="MotionLead"/>), so the
    ///   impact frame already shows a frame's flight and `--fixed-time` runs repeat. They clatter down,
    ///   and after <see cref="LingerSeconds"/> sink into the ground. The debris never blocks: the sim
    ///   opened the tiles at the hit.
    /// - <b>A regrowth</b> (Regrowth): the site erupts from the ground over its beat, from the event's
    ///   downbeat (the structure shader's <c>_SL_Sink</c>, overshooting a little before it settles),
    ///   flashing and glowing as it comes, with a burst of small shards thrown up from its footprint
    ///   and <see cref="Erupted"/> for the effects' burst and sound.
    /// - <b>A glint</b> (Glint, a hit that doesn't break): the site flashes, brighter for a hit that
    ///   counts toward a large structure's break.
    /// - The static shadow map re-renders where the site stands after a break and once a regrowth is whole
    ///   (<see cref="ArenaRuntime.RefreshShadows"/>); the shards and the growing crystal cast realtime
    ///   shadows meanwhile, and light probes light both.
    /// The flash, punch and shake are the look's (FeelMoments reads the same events), and the break's
    /// burst and glass are the effects' and the sound's. Events arrive ahead of frameSong; each waits
    /// for its frame. Nothing shows until <see cref="Attach"/> gives it the arena's sites.
    /// </summary>
    public sealed class StructureShatter : IPresenter, IDisposable
    {
        public const float Step = 1f / 120f;
        public const int MaxStepsPerFrame = 12;
        /// <summary>Shards are posed this far ahead of the frame's song time: the impact frame shows a frame of flight.</summary>
        public const double MotionLead = 1.0 / 60;
        public const float GravityScale = 2.2f, Density = 2650f; // kg/m³, quartz
        /// <summary>
        /// The farthest shard launches this long after the strike; the nearest <see cref="LaunchTogether"/> of the
        /// structure (by distance from the strike) launch on it together.
        /// </summary>
        public const float LaunchSpreadSeconds = 0.04f, LaunchTogether = 0.25f;
        public const float LingerSeconds = 2.4f, SinkSeconds = 1f;
        /// <summary>A launching shard's white flash and the glow of its fresh inside faces (_SL_PieceFx.x and w): their peaks and fades.</summary>
        public const float FlashSeconds = 0.2f, GlowSeconds = 0.45f, Flash = 0.25f, Glow = 0.5f;
        /// <summary>The flight at a heavy strike's throw (FeelMoments.HeavyThrowM): outward from the strike, along the blow and up (m/s), and the tumble (rad/s).</summary>
        public const float Outward = 3.4f, Blow = 6.5f, Up = 2.6f, Spin = 7f;
        /// <summary>A glint's flash and how long it fades; a hit toward a large structure's break flashes at full.</summary>
        public const float GlintSeconds = 0.3f, GlintFlash = 0.5f;
        /// <summary>The eruption's overshoot (easeOutBack's constant) and how deep a site starts (its height times this).</summary>
        public const float GrowOvershoot = 1.3f, GrowDepth = 1.08f;
        /// <summary>The eruption's burst: every this-many shard of the site, at this scale, thrown up this fast (m/s), lingering this long.</summary>
        public const int BurstEvery = 3;
        public const float BurstScale = 0.45f, BurstUp = 7f, BurstOut = 2.5f, BurstLingerSeconds = 0.7f, BurstSinkSeconds = 0.4f;

        static readonly int PieceFxId = Shader.PropertyToID("_SL_PieceFx");
        static readonly int PieceId = Shader.PropertyToID("_SL_Piece");
        static readonly int TintId = Shader.PropertyToID("_SL_Tint");
        static readonly int HeightId = Shader.PropertyToID("_HeightM");
        static readonly int RestId = Shader.PropertyToID("_SL_Rest");
        static readonly int SinkId = Shader.PropertyToID("_SL_Sink");

        enum Kind { Break, Regrow, Glint }

        struct Pending
        {
            public Kind Kind;
            public double T;
            public int Site;
            public Vector3 Point, Blow; // the strike's point and direction on the ground
            public float Throw;         // the hit's throw (m) for a break; the glint's strength
            public double End;          // a regrowth's end
        }

        /// <summary>A site: its renderers and their property blocks (each carries the crystal swap's piece values and tint).</summary>
        sealed class Site
        {
            public Renderer[] Renderers;
            public MaterialPropertyBlock[] Blocks;
            public Bounds Bounds;
            public bool Broken;
            public double GlintT = double.NegativeInfinity;
            public float GlintFlash;
            public double GrowStart = double.NegativeInfinity, GrowEnd = double.NegativeInfinity;
            public bool Growing, Lit; // Lit: its blocks hold a glint or growth to clear
        }

        sealed class Shard
        {
            public GameObject Object;
            public Mesh Mesh;
            public Transform Transform;
            public Rigidbody Body;
            public BoxCollider Collider;
            public MeshRenderer Renderer;
            public MaterialPropertyBlock Block = new MaterialPropertyBlock();
            public float Delay;            // its launch after the strike (s)
            public bool Flying, Sinking;
            public Vector3 Rest;           // where it stood
            public Vector3 Velocity, Spin; // its launch
            public float SinkFrom, SinkDepth;
        }

        sealed class Live
        {
            public int Site;
            public bool Burst;                 // an eruption's burst, not a break
            public double At;                  // the strike's or eruption's song time
            public float Linger, Sink;         // its shards' linger and sink (s)
            public readonly List<Shard> Shards = new List<Shard>();
        }

        readonly List<Pending> _pending = new List<Pending>();
        readonly StructureShards _shards;
        Action<IReadOnlyList<Bounds>> _refreshShadows;
        Site[] _sites;
        Scene _scene;
        bool _ownScene;
        PhysicsScene _physics;
        GameObject _root;
        PhysicsMaterial _material;
        readonly List<Live> _live = new List<Live>();
        readonly Dictionary<Mesh, List<Shard>> _pool = new Dictionary<Mesh, List<Shard>>();
        readonly Stopwatch _watch = new Stopwatch();
        double _physicsT = double.NaN; // the physics scene's time: song time plus MotionLead
        readonly List<Bounds> _shadowsDirty = new List<Bounds>(); // sites whose shadows changed this frame

        /// <summary>
        /// A regrowth erupting at song time t: its site's index, its footprint's centre on the ground, its
        /// size (m: the square root of its footprint's area, about 2.6 for a small site and 4.5 or more for a large one) and gem colour.
        /// </summary>
        public event Action<double, int, Vector3, float, Color> Erupted;

        /// <summary>Shards in flight or sinking now, and the most at once.</summary>
        public int Shards { get; private set; }
        public int PeakShards { get; private set; }
        /// <summary>This frame's shards launched, and the farthest any has moved from where it stood (m).</summary>
        public int Flying { get; private set; }
        public float MovedM { get; private set; }
        public int Breaks { get; private set; }
        /// <summary>The last frame's physics steps, their milliseconds, and the static shadow map's re-render (0 when none).</summary>
        public int FrameSteps { get; private set; }
        public double FramePhysicsMs { get; private set; }
        public double FrameShadowMs { get; private set; }

        public StructureShatter() : this(StructureShards.Load()) { }

        /// <summary>Flies <paramref name="shards"/>' sets (null: breaks only hide).</summary>
        public StructureShatter(StructureShards shards)
        {
            _shards = shards;
            if (_shards == null) Debug.LogWarning("StructureShatter: no structure shards in this build (run `game unity BakeStructureShards`): breaks only hide");
        }

        /// <summary>
        /// Shows the breaks on the arena's <paramref name="sites"/> (<see cref="ArenaRuntime.Sites"/>, once
        /// ready and in crystal: CrystalStructures.Apply), redrawing its static shadows where they change
        /// with <paramref name="refreshShadows"/> (<see cref="ArenaRuntime.RefreshShadows"/>). The shards
        /// fly in a local physics scene of their own, or in <paramref name="physicsScene"/> when given (the
        /// editor's tests pass a preview scene: play mode alone creates scenes), which the caller then owns.
        /// </summary>
        public void Attach(IReadOnlyList<Renderer[]> sites, Action<IReadOnlyList<Bounds>> refreshShadows, Scene? physicsScene = null)
        {
            _refreshShadows = refreshShadows;
            _sites = new Site[sites.Count];
            for (int i = 0; i < _sites.Length; i++)
            {
                var rs = sites[i];
                var site = new Site { Renderers = rs, Blocks = new MaterialPropertyBlock[rs.Length] };
                for (int k = 0; k < rs.Length; k++)
                {
                    site.Blocks[k] = new MaterialPropertyBlock();
                    if (rs[k].HasPropertyBlock()) rs[k].GetPropertyBlock(site.Blocks[k]);
                    if (k == 0) site.Bounds = rs[k].bounds;
                    else site.Bounds.Encapsulate(rs[k].bounds);
                }
                _sites[i] = site;
            }
            _ownScene = physicsScene == null;
            _scene = physicsScene ?? SceneManager.CreateScene("StructureShatter", new CreateSceneParameters(LocalPhysicsMode.Physics3D));
            _physics = _scene.GetPhysicsScene();
            _root = new GameObject("StructureShatter");
            SceneManager.MoveGameObjectToScene(_root, _scene);
            _material = new PhysicsMaterial("crystal rubble")
            {
                bounciness = 0.25f, dynamicFriction = 0.5f, staticFriction = 0.7f,
                bounceCombine = PhysicsMaterialCombine.Average, frictionCombine = PhysicsMaterialCombine.Average,
            };
            var floor = new GameObject("RubbleFloor");
            floor.transform.SetParent(_root.transform, false);
            var box = floor.AddComponent<BoxCollider>();
            box.size = new Vector3(4000, 1, 4000);
            box.center = new Vector3(0, -0.5f, 0);
            box.sharedMaterial = _material;
            Debug.Log($"StructureShatter: {_sites.Length} sites, {(_shards != null ? _shards.Meshes.Length : 0)} shard sets");
        }

        public void OnEvent(in SimEvent e)
        {
            switch (e.Kind)
            {
                case SimEventKind.StructureBreak:
                {
                    var b = e.AsStructureBreak;
                    Add(new Pending
                    {
                        Kind = Kind.Break, T = e.T, Site = b.Structure, Point = new Vector3((float)b.X, 0, (float)b.Y),
                        Blow = new Vector3((float)b.DirX, 0, (float)b.DirY), Throw = (float)b.Force,
                    });
                    break;
                }
                case SimEventKind.Regrowth:
                {
                    var r = e.AsRegrowth;
                    Add(new Pending { Kind = Kind.Regrow, T = r.Start, Site = r.Structure, End = r.End });
                    break;
                }
                case SimEventKind.Glint:
                {
                    var g = e.AsGlint;
                    Add(new Pending { Kind = Kind.Glint, T = e.T, Site = g.Structure, Throw = g.Hits > 0 ? 1f : GlintFlash });
                    break;
                }
            }
        }

        /// <summary>Keeps the pending moments in song-time order (events settle in about that order already).</summary>
        void Add(in Pending p)
        {
            int i = _pending.Count;
            while (i > 0 && _pending[i - 1].T > p.T) i--;
            _pending.Insert(i, p);
        }

        public void Present(in PresentFrame frame)
        {
            FrameSteps = 0;
            FramePhysicsMs = FrameShadowMs = 0;
            if (_sites == null) return;
            double song = frame.Song, now = song + MotionLead;
            if (double.IsNaN(_physicsT)) _physicsT = now;
            while (_pending.Count > 0 && _pending[0].T <= song)
            {
                var p = _pending[0];
                _pending.RemoveAt(0);
                if (p.Site < 0 || p.Site >= _sites.Length) continue;
                switch (p.Kind)
                {
                    case Kind.Break: Break(p); break;
                    case Kind.Regrow: Regrow(p); break;
                    case Kind.Glint: Glint(p); break;
                }
            }

            _watch.Restart();
            int n = 0;
            while (_physicsT + Step <= now + 1e-9)
            {
                if (n++ >= MaxStepsPerFrame)
                {
                    _physicsT = now; // a long stall: drop the steps it missed rather than catching up over many frames
                    break;
                }
                Advance(_physicsT);
                _physics.Simulate(Step);
                _physicsT += Step;
            }
            FrameSteps = n;
            FramePhysicsMs = _live.Count > 0 ? _watch.Elapsed.TotalMilliseconds : 0;
            PresentShards(now);
            PresentSites(song, now);
            if (_shadowsDirty.Count > 0)
            {
                _watch.Restart();
                _refreshShadows(_shadowsDirty);
                FrameShadowMs = _watch.Elapsed.TotalMilliseconds;
                _shadowsDirty.Clear();
            }
        }

        // ---------------------------------------------------------------------------------
        // Breaks.

        /// <summary>The site's shards where its pieces stood, in their crystal, each launching by its distance from the strike.</summary>
        void Break(in Pending p)
        {
            var site = _sites[p.Site];
            if (site.Broken) return;
            Hide(site, true);
            site.Growing = false;
            _shadowsDirty.Add(site.Bounds);
            Breaks++;
            if (_shards == null) return;
            var live = new Live { Site = p.Site, At = p.T, Linger = LingerSeconds, Sink = SinkSeconds };
            var rng = new System.Random(p.Site * 7919 + (int)(p.T * 1000));
            // The strike, on the site's surface at a third of its height: the kick is hardest near it.
            var b = site.Bounds;
            var strike = b.ClosestPoint(new Vector3(p.Point.x, b.min.y + 0.33f * b.size.y, p.Point.z));
            float reach = Mathf.Max(0.5f, (b.center - strike).magnitude + b.extents.magnitude);
            var blow = p.Blow.sqrMagnitude > 1e-6f ? p.Blow.normalized : Vector3.zero;
            float power = Mathf.Clamp(p.Throw / FeelMoments.HeavyThrowM, 0.45f, 1.2f);
            for (int r = 0; r < site.Renderers.Length; r++)
                Cut(site, r, live, (s, rest) =>
                {
                    var away = rest - strike;
                    float dist = away.magnitude;
                    away = dist > 1e-4f ? away / dist : blow;
                    float near = 1f - Mathf.Clamp01(dist / reach);
                    s.Delay = dist; // its launch, once the nearest and farthest are known
                    // Its share of the kick: the nearest hardest, one in five slumping (never near the strike), one in eight flying far.
                    double u = rng.NextDouble();
                    float share = u < 0.2 && near < 0.6f ? 0.15f + 0.25f * (float)rng.NextDouble()
                                : u > 0.875 ? 1.5f + 0.7f * (float)rng.NextDouble()
                                : 0.55f + 0.6f * (float)rng.NextDouble();
                    share *= (0.45f + 1.1f * near) * power;
                    away.y = Mathf.Max(away.y, -0.2f);
                    s.Velocity = (away * Outward + blow * Blow * (0.5f + (float)rng.NextDouble()) + Vector3.up * Up) * share;
                    s.Spin = RandomUnit(rng) * Spin * (0.4f + 0.8f * (float)rng.NextDouble());
                });
            // The nearest launch on the strike, the farthest LaunchSpreadSeconds later.
            float nearest = float.MaxValue, farthest = 0;
            foreach (var s in live.Shards)
            {
                nearest = Mathf.Min(nearest, s.Delay);
                farthest = Mathf.Max(farthest, s.Delay);
            }
            foreach (var s in live.Shards)
            {
                float f = farthest > nearest ? (s.Delay - nearest) / (farthest - nearest) : 0f;
                s.Delay = LaunchSpreadSeconds * Mathf.Max(0f, (f - LaunchTogether) / (1f - LaunchTogether));
            }
            Begin(live);
        }

        /// <summary>
        /// Places the shards of site renderer <paramref name="r"/> where they stand in it, in its materials
        /// and piece values, each set up by <paramref name="launch"/> (given the shard and its rest
        /// position), every <paramref name="every"/>th of them, at <paramref name="scale"/>, lifted to
        /// <paramref name="lift"/> above the ground when given.
        /// </summary>
        void Cut(Site site, int r, Live live, Action<Shard, Vector3> launch, int every = 1, float scale = 1f, float lift = float.NaN)
        {
            var renderer = site.Renderers[r];
            if (!(renderer is MeshRenderer) || !renderer.TryGetComponent<MeshFilter>(out var mf)) return;
            var set = _shards.Of(mf.sharedMesh);
            if (set == null)
            {
                Debug.LogWarning($"StructureShatter: no shards for {mf.sharedMesh?.name} (re-run `game unity BakeStructureShards`)");
                return;
            }
            var l2w = renderer.localToWorldMatrix;
            var rot = renderer.transform.rotation;
            var lossy = renderer.transform.lossyScale;
            float volumeScale = Mathf.Abs(lossy.x * lossy.y * lossy.z);
            var mats = renderer.sharedMaterials;
            var block = site.Blocks[r];
            for (int i = 0; i < set.Shards.Length; i += every)
            {
                var src = set.Shards[i];
                var rest = l2w.MultiplyPoint3x4(src.Center);
                var s = Take(src.Mesh);
                launch(s, rest);
                s.Renderer.sharedMaterials = mats;
                s.Transform.SetPositionAndRotation(rest, rot);
                s.Rest = rest;
                s.Transform.localScale = lossy;
                // Its rest matrix keeps the facets it had in the wall; a burst's shrunken copy keeps them too.
                s.Block.Clear();
                s.Block.SetVector(PieceId, block.GetVector(PieceId));
                s.Block.SetVector(TintId, block.GetVector(TintId));
                s.Block.SetFloat(HeightId, block.GetFloat(HeightId));
                s.Block.SetMatrix(RestId, s.Transform.localToWorldMatrix);
                s.Block.SetVector(PieceFxId, new Vector4(0, 0, 1, 0));
                s.Renderer.SetPropertyBlock(s.Block);
                if (!float.IsNaN(lift))
                {
                    s.Transform.localScale = lossy * scale;
                    s.Transform.position = new Vector3(rest.x, lift, rest.z);
                }
                s.Flying = s.Sinking = false;
                s.Collider.enabled = false;
                s.Body.isKinematic = true;
                s.Body.mass = Mathf.Max(0.2f, src.Volume * volumeScale * scale * scale * scale * Density);
                s.Object.SetActive(true);
                live.Shards.Add(s);
            }
        }

        void Begin(Live live)
        {
            if (live.Shards.Count == 0) return;
            _live.Add(live);
            Shards += live.Shards.Count;
            PeakShards = Math.Max(PeakShards, Shards);
        }

        /// <summary>Before the physics step at physics time <paramref name="now"/>: launches the shards due, adds gravity, starts the sinking.</summary>
        void Advance(double now)
        {
            var g = Vector3.down * (9.81f * GravityScale);
            foreach (var live in _live)
            {
                float age = (float)(now - live.At);
                foreach (var s in live.Shards)
                {
                    if (!s.Flying)
                    {
                        if (age < s.Delay) continue;
                        s.Flying = true;
                        s.Collider.enabled = true;
                        s.Body.isKinematic = false;
                        // A launch the steps passed (the break arrived as the physics was ahead of it) starts where it would be.
                        float late = Mathf.Min(age - s.Delay, 2 * Step);
                        if (late > 0) s.Body.position += s.Velocity * late;
                        s.Body.linearVelocity = s.Velocity;
                        s.Body.angularVelocity = s.Spin;
                    }
                    if (s.Sinking) continue;
                    if (age >= live.Linger + s.Delay)
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

        /// <summary>The shards at physics time <paramref name="now"/>: the launch flash and glow, the sinking; spent ones go back to the pool.</summary>
        void PresentShards(double now)
        {
            Flying = 0;
            MovedM = 0;
            for (int i = _live.Count - 1; i >= 0; i--)
            {
                var live = _live[i];
                float age = (float)(now - live.At);
                bool spent = true;
                foreach (var s in live.Shards)
                {
                    float since = age - s.Delay;
                    if (s.Sinking)
                    {
                        float u = (age - live.Linger - s.Delay) / live.Sink;
                        var p = s.Transform.position;
                        s.Transform.position = new Vector3(p.x, s.SinkFrom - Mathf.Clamp01(u) * Mathf.Clamp01(u) * s.SinkDepth, p.z);
                        if (u < 1f) spent = false;
                        else s.Object.SetActive(false);
                    }
                    else spent = false;
                    if (s.Flying)
                    {
                        Flying++;
                        MovedM = Mathf.Max(MovedM, (s.Transform.position - s.Rest).magnitude);
                    }
                    if (since > GlowSeconds + 0.05f) continue;
                    float flash = since >= 0 ? Mathf.Clamp01(1f - since / FlashSeconds) : 0f;
                    float glow = since >= 0 ? Mathf.Clamp01(1f - since / GlowSeconds) : 0f;
                    s.Block.SetVector(PieceFxId, new Vector4(flash * flash * Flash, 0, 1, glow * glow * Glow));
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

        // ---------------------------------------------------------------------------------
        // Regrowths and glints.

        /// <summary>The site back, sunk under the ground, to erupt over its beat; a burst of its shards thrown up from its footprint.</summary>
        void Regrow(in Pending p)
        {
            var site = _sites[p.Site];
            Hide(site, false);
            site.GrowStart = p.T;
            site.GrowEnd = Math.Max(p.End, p.T + 1e-3);
            site.Growing = site.Lit = true;
            SetSiteFx(site, 1f, 1f, 1f); // sunk until its first frame poses it
            var b = site.Bounds;
            var centre = new Vector3(b.center.x, 0, b.center.z);
            // A break's shards still lying there sink as it comes up through them.
            foreach (var l in _live)
                if (l.Site == p.Site && !l.Burst) l.Linger = Math.Min(l.Linger, (float)(p.T - l.At));
            var tint = site.Blocks.Length > 0 ? (Color)site.Blocks[0].GetVector(TintId) : Color.white;
            Erupted?.Invoke(p.T, p.Site, centre, Mathf.Sqrt(b.size.x * b.size.z), tint);
            if (_shards == null) return;
            var live = new Live { Site = p.Site, Burst = true, At = p.T, Linger = BurstLingerSeconds, Sink = BurstSinkSeconds };
            var rng = new System.Random(p.Site * 104729 + (int)(p.T * 1000));
            for (int r = 0; r < site.Renderers.Length; r++)
                Cut(site, r, live, (s, rest) =>
                {
                    var away = new Vector3(rest.x - centre.x, 0, rest.z - centre.z);
                    away = away.sqrMagnitude > 1e-6f ? away.normalized : RandomUnit(rng);
                    s.Delay = 0.03f * (float)rng.NextDouble();
                    float up = BurstUp * (0.6f + 0.6f * (float)rng.NextDouble());
                    s.Velocity = away * (BurstOut * (0.5f + (float)rng.NextDouble())) + Vector3.up * up;
                    s.Spin = RandomUnit(rng) * Spin * (0.5f + (float)rng.NextDouble());
                }, BurstEvery, BurstScale, 0.15f);
            Begin(live);
        }

        void Glint(in Pending p)
        {
            var site = _sites[p.Site];
            if (site.Broken) return;
            site.GlintT = p.T;
            site.GlintFlash = p.Throw;
            site.Lit = true;
        }

        /// <summary>Each lit site's glint flash and eruption at song time <paramref name="song"/> (the eruption's motion at <paramref name="now"/>).</summary>
        void PresentSites(double song, double now)
        {
            foreach (var site in _sites)
            {
                if (!site.Lit || site.Broken) continue;
                float glint = 0, glow = 0, sink = 0;
                double g = song - site.GlintT;
                if (g >= 0 && g < GlintSeconds)
                {
                    float k = 1f - (float)(g / GlintSeconds);
                    glint = site.GlintFlash * k * k;
                }
                if (site.Growing)
                {
                    float u = Mathf.Clamp01((float)((now - site.GrowStart) / (site.GrowEnd - site.GrowStart)));
                    sink = GrowDepth * (1f - EaseOutBack(u));
                    float since = (float)(song - site.GrowStart);
                    glint = Mathf.Max(glint, since >= 0 ? Mathf.Clamp01(1f - since / FlashSeconds) : 0f);
                    glow = Glow * (1f - u) * (1f - u);
                    if (u >= 1f)
                    {
                        site.Growing = false;
                        sink = 0;
                        _shadowsDirty.Add(site.Bounds);
                    }
                }
                SetSiteFx(site, glint, glow, sink);
                site.Lit = site.Growing || g < GlintSeconds;
            }
        }

        static void SetSiteFx(Site site, float flash, float glow, float sink)
        {
            for (int k = 0; k < site.Renderers.Length; k++)
            {
                var block = site.Blocks[k];
                block.SetVector(PieceFxId, new Vector4(flash, 0, 0, glow));
                block.SetFloat(SinkId, sink);
                site.Renderers[k].SetPropertyBlock(block);
            }
        }

        /// <summary>Overshoots by about a tenth before settling at 1 (<see cref="GrowOvershoot"/>).</summary>
        static float EaseOutBack(float u)
        {
            float c3 = GrowOvershoot + 1f, v = u - 1f;
            return 1f + c3 * v * v * v + GrowOvershoot * v * v;
        }

        void Hide(Site site, bool broken)
        {
            site.Broken = broken;
            foreach (var r in site.Renderers) if (r != null) r.enabled = !broken;
        }

        // ---------------------------------------------------------------------------------
        // The pool.

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
            r.shadowCastingMode = ShadowCastingMode.On;
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
            return new Shard { Object = go, Mesh = mesh, Transform = go.transform, Body = rb, Collider = col, Renderer = r };
        }

        void Recycle(Shard s)
        {
            s.Object.SetActive(false);
            s.Body.isKinematic = true;
            _pool[s.Mesh].Add(s);
        }

        static Vector3 RandomUnit(System.Random rng)
        {
            float z = 2f * (float)rng.NextDouble() - 1f, a = 2f * Mathf.PI * (float)rng.NextDouble();
            float r = Mathf.Sqrt(1f - z * z);
            return new Vector3(r * Mathf.Cos(a), z, r * Mathf.Sin(a));
        }

        public void Dispose()
        {
            Destroy(_root);
            Destroy(_material);
            if (_ownScene && _scene.IsValid() && _scene.isLoaded) SceneManager.UnloadSceneAsync(_scene);
        }

        static void Destroy(UnityEngine.Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(o);
            else UnityEngine.Object.DestroyImmediate(o);
        }
    }
}
