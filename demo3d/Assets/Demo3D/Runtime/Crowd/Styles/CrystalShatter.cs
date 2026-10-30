using System;
using System.Collections.Generic;
using PS.Choreography;
using UnityEngine;
using UnityEngine.Rendering;

namespace Demo3D.Runtime.Crowd
{
    /// <summary>
    /// The crystal shatter (crowd.md, Monster styles; epic ps-2iq): wherever the Crystal shader draws a
    /// type, its deaths break into the baked shards (<see cref="CrowdShards"/>) instead of flying apart
    /// triangle by triangle.
    ///
    /// - At the start of the death hold (the death clip's last pose, TimelineEvaluator.State.Held) the
    ///   client hides the crowd instance and this places the type's shards from a pool, assembled, with
    ///   the instance's position, yaw and scale: the shards' skin is the style mesh at that pose, so the
    ///   swap doesn't show; until the release the shadow is cast by the shadow proxy's mesh at that pose
    ///   (CrowdShards.Set.HoldShadow), as the crowd's was. (The instance's tint is not drawn by Crystal,
    ///   so the shards ignore it too.) They follow the instance until the release.
    /// - Over the hold, glowing cracks spread along the cuts between shards from where the blow landed
    ///   (on the body, facing the hero), covering it by <see cref="CrackSpread"/> of the hold; from
    ///   <see cref="SwellFrom"/> the shards swell apart a little and tremble, harder toward the beat.
    /// - On the next beat (where the dissolve used to start: State.Dissolve leaves 0) the shards pop a
    ///   few millimetres apart, their inside faces show (_ShardOpen) and the cracks flare, and they
    ///   hold there for <see cref="HitStopSeconds"/> (hit-stop on the dying monster only; the world
    ///   keeps moving). Then they become PhysX rigidbodies, kicked outward from the hit point and
    ///   along the killing blow (the hero to the monster at the blow), with wide variance: some slump
    ///   where they stand, most scatter, a few fly far, small ones spinning fastest. The cracks' heat
    ///   cools over the first half second of flight. A monster's own shards ignore each other for
    ///   <see cref="SelfIgnoreSeconds"/>, as their convex hulls overlap while assembled; other shards
    ///   and the floor collide from the start.
    /// - Gravity is <see cref="GravityScale"/> times Earth's, so they clack down rather than float;
    ///   they bounce once or twice, skitter on the floor's friction (a box under y = 0) and settle into
    ///   a pile, and after <see cref="LingerSeconds"/> sink into it over <see cref="SinkSeconds"/>, then
    ///   go back to the pool.
    /// - Physics runs in script mode on the song clock, in fixed steps of <see cref="Step"/>, so a
    ///   `--fixed-time` run gives the same shards every time; a seek backwards clears every shard, and
    ///   deaths before a `--start-at` point are not reconstructed (their instance stays hidden).
    /// </summary>
    public sealed class CrystalShatter : IDisposable
    {
        public const float Step = 1f / 120f;
        public const int MaxStepsPerFrame = 12; // a long hitch drops time rather than stalling
        public const float LingerSeconds = 5f, SinkSeconds = 1.2f;
        public const float SelfIgnoreSeconds = 0.35f;
        public const float Density = 2600f; // kg/m³, glass
        public const float GravityScale = 2.2f;
        /// <summary>The hold's share in which the cracks cover the body, and the share from which the shards swell and tremble.</summary>
        public const float CrackSpread = 0.8f, SwellFrom = 0.55f;
        /// <summary>The release's hit-stop: the popped shards hold still for 4 frames at 60 fps before they fly.</summary>
        public const float HitStopSeconds = 4f / 60f;
        /// <summary>The cracks' heat: at the hold's start and end, at the release's flare, and how fast it cools in flight (s).</summary>
        public const float HeatFrom = 0.6f, HeatTo = 1.6f, HeatFlare = 2.5f, HeatCooling = 0.18f;

        /// <summary>
        /// The release's kick in m/s for a 1.8 m monster (scaled by the square root of its height):
        /// outward from the hit point, along the blow, and up; and its spin. Each shard's share of it
        /// varies widely (see Launch).
        /// </summary>
        public float OutwardSpeed = 2.4f, BlowSpeed = 3.0f, UpSpeed = 3.0f, SpinRadPerS = 10f;

        /// <summary>`--shatter-log`: a line per hold and release, to find deaths for filmstrips.</summary>
        readonly bool _log = Args.Has("--shatter-log");

        /// <summary>The game camera, for the log's view positions.</summary>
        public Camera View;

        static readonly int IdShardOpen = Shader.PropertyToID("_ShardOpen");
        static readonly int IdCrack = Shader.PropertyToID("_Crack");
        static readonly int IdCrackHeat = Shader.PropertyToID("_CrackHeat");

        readonly Timeline _tl;
        readonly CrowdShards.Set[] _sets;    // per crowd type, null where the type doesn't shatter
        readonly Material[] _materials;      // per crowd type: the crystal look
        readonly Material _motion;           // the opaque-queue twin that writes motion vectors (CrystalShard.shader)
        readonly float[] _centerY, _height;  // per crowd type: the standing body's centre height (for logs) and its height
        readonly Vector3[] _poseCentre;      // per crowd type: the held pose's centre (root space): shards' centres by volume
        readonly Vector3[] _poseExtent;      // per crowd type: the held pose's half extents about it (root space)
        readonly List<Group>[] _free;
        readonly List<Group> _live = new List<Group>();
        readonly Dictionary<int, Group> _byEntity = new Dictionary<int, Group>();
        readonly HashSet<int> _played = new HashSet<int>(); // entities whose shards have come and gone
        readonly GameObject _root;
        readonly PhysicsMaterial _physics;
        readonly Vector3 _gravity; // the project's, restored on Dispose
        double _t0 = double.NaN, _last = double.NaN;
        long _steps, _frame;

        /// <summary>Live shard groups and shards, deaths shattered so far and the most shards live at once, for logs.</summary>
        public int Groups => _live.Count;
        public int Shards { get; private set; }
        public int Deaths { get; private set; }
        public int PeakShards { get; private set; }

        sealed class Group
        {
            public int Type, Entity;
            public GameObject[] Objects;
            public GameObject Shadow; // the hold's shadow: the shadow proxy's mesh at the pose (CrowdShards.Set.HoldShadow)
            public Transform[] Transforms;
            public Rigidbody[] Bodies;
            public Collider[] Colliders;
            public Renderer[] Renderers;
            public MaterialPropertyBlock Block;
            public float[] SinkFrom, SinkDepth;
            public bool Released, Flying, Sinking, SelfCollides, Hot;
            public double HeldAt, Beat;    // the hold's first frame and its length (one beat)
            public double ReleasedAt;      // the pop (the beat); it flies after the hit-stop
            public double FlewAt;          // physics time it began to fly
            public long Seen; // the last frame Dying placed it
            public Vector3 Position, Hit;  // the instance's position; where the blow landed (world)
            public Vector3 Blow;           // the killing blow's direction on the ground
            public float Yaw, Scale;
            public uint Seed;
        }

        CrystalShatter(Timeline tl, MonsterStyle style, CrowdLibrary lib, CrowdType[] types)
        {
            _tl = tl;
            var shards = CrowdShards.Load();
            var shader = lib.StyleShader("Demo3D/Styles/CrystalShard");
            _sets = new CrowdShards.Set[types.Length];
            _materials = new Material[types.Length];
            _centerY = new float[types.Length];
            _height = new float[types.Length];
            _poseCentre = new Vector3[types.Length];
            _poseExtent = new Vector3[types.Length];
            _free = new List<Group>[types.Length];
            for (int i = 0; i < types.Length; i++)
            {
                _free[i] = new List<Group>();
                if (style.StyleOf(types[i].Id) != "crystal") continue;
                _sets[i] = shards.Of(types[i].Id);
                // The crowd's own crystal material for the type, so the shards wear exactly its look.
                var crowd = style.Materials(lib, types[i])[0];
                _materials[i] = new Material(shader) { name = types[i].Id + "_shard", enableInstancing = false };
                _materials[i].CopyMatchingPropertiesFromMaterial(crowd);
                _centerY[i] = types[i].CenterY;
                _height[i] = types[i].HeightM;
                // Most death poses lie on the floor, so the swell, the hit point and the kick work from
                // the pose's own centre, not the standing body's.
                float vol = 0;
                var lo = Vector3.positiveInfinity;
                var hi = Vector3.negativeInfinity;
                foreach (var sh in _sets[i].Shards)
                {
                    _poseCentre[i] += sh.Center * sh.Volume;
                    vol += sh.Volume;
                    lo = Vector3.Min(lo, sh.Center);
                    hi = Vector3.Max(hi, sh.Center);
                }
                _poseCentre[i] /= Mathf.Max(vol, 1e-9f);
                _poseExtent[i] = Vector3.Max(hi - _poseCentre[i], _poseCentre[i] - lo);
                foreach (var s in _sets[i].Shards) Physics.BakeMesh(s.Hull.GetEntityId(), true); // cook the hulls now, not at the first death
            }
            _motion = new Material(shader) { name = "shard_motion", renderQueue = (int)RenderQueue.GeometryLast, enableInstancing = false };
            _motion.SetShaderPassEnabled("UniversalForward", false);
            _motion.SetShaderPassEnabled("ShadowCaster", false);
            _physics = new PhysicsMaterial("shard")
            {
                bounciness = 0.35f, dynamicFriction = 0.4f, staticFriction = 0.7f,
                bounceCombine = PhysicsMaterialCombine.Average, frictionCombine = PhysicsMaterialCombine.Average,
            };
            Physics.simulationMode = SimulationMode.Script;
            _gravity = Physics.gravity;
            Physics.gravity = new Vector3(0, -9.81f * GravityScale, 0);
            _root = new GameObject("CrystalShatter");
            var floor = new GameObject("ShardFloor");
            floor.transform.SetParent(_root.transform, false);
            var box = floor.AddComponent<BoxCollider>();
            box.size = new Vector3(4000, 1, 4000);
            box.center = new Vector3(0, -0.5f, 0);
            box.sharedMaterial = _physics;
            Debug.Log($"CrystalShatter: shards for {string.Join(", ", Array.FindAll(Array.ConvertAll(types, t => t.Id), id => style.StyleOf(id) == "crystal"))}");
        }

        /// <summary>The shatter for a style, or null if no type of it is crystal.</summary>
        public static CrystalShatter Create(Timeline tl, MonsterStyle style, CrowdLibrary lib, string[] typeIds)
        {
            if (style == null || Array.TrueForAll(typeIds, id => style.StyleOf(id) != "crystal")) return null;
            var types = Array.ConvertAll(typeIds, id => lib.Types[lib.IndexOf(id)]);
            return new CrystalShatter(tl, style, lib, types);
        }

        public bool Shatters(int type) => _sets[type] != null;

        /// <summary>
        /// Brings the shards to song time t: a seek backwards clears them; forward, physics steps on
        /// the song clock and settled shards sink and go back to the pool. Call once a frame, before
        /// <see cref="Dying"/>.
        /// </summary>
        public void Frame(double t)
        {
            if (double.IsNaN(_t0) || t < _last)
            {
                Clear();
                _t0 = t;
                _steps = 0;
            }
            _last = t;
            _frame++;
            // Popped shards whose hit-stop is over fly from this frame's steps.
            foreach (var grp in _live)
                if (grp.Released && !grp.Flying && t >= grp.ReleasedAt + HitStopSeconds - 1e-4) Launch(grp);
            long target = (long)Math.Floor((t - _t0) / Step + 1e-6);
            int n = 0;
            while (_steps < target)
            {
                if (n++ >= MaxStepsPerFrame) { _steps = target; break; } // drop the rest of a hitch
                Physics.Simulate(Step);
                _steps++;
            }
            double now = _t0 + _steps * Step;
            for (int g = _live.Count - 1; g >= 0; g--)
            {
                var grp = _live[g];
                if (!grp.Released)
                {
                    // Its monster left the timeline before a frame saw its dissolve begin (a hitch, or a
                    // beat longer than the time from the release to the despawn): release it now
                    // rather than leave a statue.
                    if (grp.Seen < _frame - 1) Release(grp);
                    continue;
                }
                if (!grp.Flying) continue;
                double age = now - grp.FlewAt;
                if (grp.Hot)
                {
                    float heat = HeatFlare * Mathf.Exp(-(float)age / HeatCooling);
                    if (heat < 0.01f) heat = 0;
                    grp.Hot = heat > 0;
                    grp.Block.SetFloat(IdCrackHeat, heat);
                    foreach (var r in grp.Renderers) r.SetPropertyBlock(grp.Block);
                }
                if (!grp.SelfCollides && age >= SelfIgnoreSeconds) SelfCollide(grp, true);
                if (age < LingerSeconds) continue;
                if (!grp.Sinking) StartSinking(grp);
                float u = (float)((age - LingerSeconds) / SinkSeconds);
                if (u >= 1f)
                {
                    Recycle(grp);
                    _live.RemoveAt(g);
                    continue;
                }
                // Each shard sinks by twice its own size, so even the largest ends under the floor.
                for (int i = 0; i < grp.Objects.Length; i++)
                {
                    var p = grp.Transforms[i].position;
                    grp.Transforms[i].position = new Vector3(p.x, grp.SinkFrom[i] - u * u * grp.SinkDepth[i], p.z);
                }
            }
        }

        /// <summary>
        /// A crystal monster in its death hold (State.Dying and State.Held): places its shards at the
        /// hold's start and releases them when the dissolve begins. Returns true if the crowd instance
        /// should be hidden, which it is for the whole hold and dissolve.
        /// </summary>
        public bool Dying(int entity, int type, in MonsterInstance inst, uint seed)
        {
            if (_sets[type] == null) return false;
            if (_played.Contains(entity)) return true;
            if (!_byEntity.TryGetValue(entity, out var grp))
            {
                if (inst.Dissolve > 0) return true; // its hold began before this run's first frame: not reconstructed
                grp = Take(type);
                grp.Entity = entity;
                grp.Seed = seed;
                grp.HeldAt = _last;
                grp.Beat = BeatLength(_last);
                grp.Blow = BlowOf(entity);
                _byEntity[entity] = grp;
                _live.Add(grp);
                if (_log) Log("hold", grp, inst.Position);
            }
            grp.Seen = _frame;
            if (!grp.Released)
            {
                Place(grp, inst, (float)Math.Min(1.0, (_last - grp.HeldAt) / grp.Beat));
                if (inst.Dissolve > 0)
                {
                    Release(grp);
                    if (_log) Log("release", grp, inst.Position);
                }
            }
            return true;
        }

        /// <summary>A `--shatter-log` line: the death, its song time, world position and place in the game camera's view (1080p pixels from the top left).</summary>
        void Log(string what, Group grp, Vector3 at)
        {
            var cam = View;
            var vp = cam != null ? cam.WorldToViewportPoint(at + Vector3.up * _centerY[grp.Type] * grp.Scale) : Vector3.zero;
            Debug.Log(FormattableString.Invariant(
                $"CrystalShatter: {what} {_sets[grp.Type].Id} entity {grp.Entity} at t {_last:F3} ({at.x:F2}, {at.z:F2}), view px ({vp.x * 1920:F0}, {(1 - vp.y) * 1080:F0}){(vp.z > 0 && vp.x is > 0 and < 1 && vp.y is > 0 and < 1 ? "" : " off screen")}"));
        }

        Group Take(int type)
        {
            var free = _free[type];
            Group grp;
            if (free.Count > 0)
            {
                grp = free[free.Count - 1];
                free.RemoveAt(free.Count - 1);
            }
            else grp = Create(type);
            grp.Released = grp.Flying = grp.Sinking = false;
            grp.SelfCollides = grp.Hot = true;
            grp.Block.SetFloat(IdShardOpen, 0);
            grp.Block.SetFloat(IdCrackHeat, 0);
            for (int i = 0; i < grp.Objects.Length; i++)
            {
                grp.Bodies[i].isKinematic = true;
                grp.Renderers[i].SetPropertyBlock(grp.Block);
                grp.Renderers[i].shadowCastingMode = ShadowCastingMode.Off; // the hold shadow casts until the release
                grp.Objects[i].SetActive(true);
            }
            grp.Shadow.SetActive(true);
            Shards += grp.Objects.Length;
            PeakShards = Math.Max(PeakShards, Shards);
            Deaths++;
            return grp;
        }

        Group Create(int type)
        {
            var set = _sets[type];
            int n = set.Shards.Length;
            var grp = new Group
            {
                Type = type, Objects = new GameObject[n], Transforms = new Transform[n], Bodies = new Rigidbody[n],
                Colliders = new Collider[n], Renderers = new Renderer[n], Block = new MaterialPropertyBlock(),
                SinkFrom = new float[n], SinkDepth = new float[n],
            };
            grp.Shadow = new GameObject(set.Id + "_holdShadow");
            grp.Shadow.transform.SetParent(_root.transform, false);
            grp.Shadow.AddComponent<MeshFilter>().sharedMesh = set.HoldShadow;
            var sr = grp.Shadow.AddComponent<MeshRenderer>();
            sr.sharedMaterial = _materials[type];
            sr.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
            sr.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            sr.lightProbeUsage = LightProbeUsage.Off;
            sr.reflectionProbeUsage = ReflectionProbeUsage.Off;
            grp.Shadow.SetActive(false);
            var mats = new[] { _materials[type], _motion };
            for (int i = 0; i < n; i++)
            {
                var s = set.Shards[i];
                var go = new GameObject(set.Id + "_shard" + i);
                go.transform.SetParent(_root.transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = s.Mesh;
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterials = mats; // the second draws the mesh again: its motion vectors only
                r.shadowCastingMode = ShadowCastingMode.On;
                r.receiveShadows = true;
                r.motionVectorGenerationMode = MotionVectorGenerationMode.Object;
                r.lightProbeUsage = LightProbeUsage.Off;
                r.reflectionProbeUsage = ReflectionProbeUsage.Off;
                var col = go.AddComponent<MeshCollider>();
                col.convex = true;
                col.sharedMesh = s.Hull;
                col.sharedMaterial = _physics;
                var rb = go.AddComponent<Rigidbody>();
                rb.isKinematic = true;
                rb.interpolation = RigidbodyInterpolation.None;
                rb.collisionDetectionMode = CollisionDetectionMode.Discrete;
                rb.maxDepenetrationVelocity = 1.5f;
                rb.mass = Mathf.Max(0.02f, s.Volume * Density);
                go.SetActive(false);
                grp.Objects[i] = go;
                grp.Transforms[i] = go.transform;
                grp.Bodies[i] = rb;
                grp.Colliders[i] = col;
                grp.Renderers[i] = r;
            }
            return grp;
        }

        /// <summary>
        /// The assembled shards at the instance (the crowd's placement, SkinVertex, of the root-space
        /// pose) at <paramref name="hold"/> (0..1) through the hold: the cracks spread from the hit
        /// point, and toward the beat the shards swell apart and tremble.
        /// </summary>
        void Place(Group grp, in MonsterInstance inst, float hold)
        {
            grp.Position = inst.Position;
            grp.Yaw = inst.Yaw;
            grp.Scale = inst.Scale;
            var set = _sets[grp.Type];
            var (centre, rot) = Placement(_poseCentre[grp.Type], inst);
            // Where the blow landed: on the pose toward the hero (its extent that way), on its top.
            float h = _height[grp.Type] * inst.Scale;
            var ext = _poseExtent[grp.Type] * inst.Scale;
            var local = Quaternion.Inverse(rot) * grp.Blow;
            grp.Hit = centre - rot * Vector3.Scale(local, new Vector3(ext.x, 0, ext.z)) * 0.8f + Vector3.up * ext.y * 0.5f;
            grp.Shadow.transform.SetPositionAndRotation(inst.Position, rot);
            grp.Shadow.transform.localScale = Vector3.one * inst.Scale;
            float swell = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(SwellFrom, 1, hold));
            float tremble = 0.012f * h * swell * swell;
            float phase = (float)(_last * 2 * Math.PI);
            for (int i = 0; i < grp.Objects.Length; i++)
            {
                var (pos, _) = Placement(set.Shards[i].Center, inst);
                if (swell > 0)
                {
                    float a = Hash(grp.Seed, i) * 6.283f, b = Hash(grp.Seed, i + 977) * 6.283f;
                    pos += Outward(pos - centre) * (0.03f * swell)
                           + new Vector3(Mathf.Sin(phase * 23 + a), 0.5f * Mathf.Sin(phase * 29 + b), Mathf.Sin(phase * 31 + a + b)) * tremble;
                }
                grp.Transforms[i].SetPositionAndRotation(pos, rot);
                grp.Transforms[i].localScale = Vector3.one * inst.Scale;
            }
            // The cracks race out at a steady pace, so the spread reads through the hold; their heat builds to the beat.
            float spread = Mathf.Clamp01(hold / CrackSpread);
            grp.Block.SetVector(IdCrack, new Vector4(grp.Hit.x, grp.Hit.y, grp.Hit.z, spread * 1.2f * h));
            grp.Block.SetFloat(IdCrackHeat, Mathf.Lerp(HeatFrom, HeatTo, hold * hold));
            foreach (var r in grp.Renderers) r.SetPropertyBlock(grp.Block);
        }

        /// <summary>An offset from the pose's centre that never points into the floor.</summary>
        static Vector3 Outward(Vector3 v) => new Vector3(v.x, Mathf.Max(0, v.y), v.z);

        /// <summary>A shard's random number in [0, 1) for a death.</summary>
        static float Hash(uint seed, int i)
        {
            uint x = seed * 747796405u + (uint)i * 2891336453u;
            x ^= x >> 16; x *= 0x7feb352du; x ^= x >> 15; x *= 0x846ca68bu; x ^= x >> 16;
            return (x & 0xffffff) / 16777216f;
        }

        /// <summary>The length of the beat at song time t.</summary>
        double BeatLength(double t)
        {
            double i = Math.Floor(_tl.Map.BeatIndexAt(t));
            return Math.Max(0.05, _tl.Map.TimeOfBeatIndex(i + 1) - _tl.Map.TimeOfBeatIndex(i));
        }

        /// <summary>
        /// Where a shard whose pivot is <paramref name="center"/> (root space, scale 1) sits, assembled,
        /// on an instance: its position and rotation, with the instance's scale as its uniform scale.
        /// This is SkinVertex's placement of a root-space point (scale, then yaw, then the position).
        /// </summary>
        internal static (Vector3, Quaternion) Placement(Vector3 center, in MonsterInstance inst)
        {
            var rot = Quaternion.Euler(0, inst.Yaw * Mathf.Rad2Deg, 0);
            return (inst.Position + rot * (center * inst.Scale), rot);
        }

        /// <summary>
        /// The beat: the shards pop apart around the hit point and their inside faces show, the cracks
        /// flare, and the instance's hold shadow gives way to the shards' own; they hold still until
        /// the hit-stop ends (<see cref="Launch"/>).
        /// </summary>
        void Release(Group grp)
        {
            grp.Released = true;
            grp.ReleasedAt = _last;
            grp.Block.SetFloat(IdShardOpen, 1);
            grp.Block.SetVector(IdCrack, new Vector4(grp.Hit.x, grp.Hit.y, grp.Hit.z, 1000f));
            grp.Block.SetFloat(IdCrackHeat, HeatFlare);
            var (centre, _) = Placement(_poseCentre[grp.Type], new MonsterInstance { Position = grp.Position, Yaw = grp.Yaw, Scale = grp.Scale });
            float h = _height[grp.Type] * grp.Scale;
            grp.Shadow.SetActive(false);
            for (int i = 0; i < grp.Objects.Length; i++)
            {
                var tr = grp.Transforms[i];
                var p = tr.position;
                var away = p - grp.Hit;
                float near = 1f - Mathf.Clamp01(away.magnitude / h);
                tr.position = p + Outward(p - centre) * 0.04f + Outward(away.normalized) * (0.012f * h * near);
                grp.Renderers[i].SetPropertyBlock(grp.Block);
                grp.Renderers[i].shadowCastingMode = ShadowCastingMode.On;
            }
        }

        /// <summary>
        /// The burst after the hit-stop: each shard becomes a rigidbody, kicked outward from the hit point
        /// (harder near it), along the blow and up. Its share of the kick varies widely: one in four
        /// slump where they stand, one in eight flies far; the largest chunks get about half the
        /// smallest's share, as heavy pieces don't fly. Small shards spin faster.
        /// </summary>
        void Launch(Group grp)
        {
            grp.Flying = true;
            grp.FlewAt = _t0 + _steps * Step;
            var rng = new System.Random((int)(grp.Seed * 2654435761u) ^ grp.Entity);
            var set = _sets[grp.Type];
            float h = _height[grp.Type] * grp.Scale;
            float size = Mathf.Sqrt(grp.Scale * Mathf.Max(0.3f, _height[grp.Type] / 1.8f));
            float largest = 1e-4f;
            foreach (var sh in set.Shards) largest = Mathf.Max(largest, sh.Size);
            Physics.SyncTransforms(); // the kinematic placement, before the bodies wake
            SelfCollide(grp, false);
            for (int i = 0; i < grp.Objects.Length; i++)
            {
                var rb = grp.Bodies[i];
                rb.isKinematic = false;
                float s = grp.Scale;
                rb.mass = Mathf.Max(0.02f, set.Shards[i].Volume * s * s * s * Density);
                var away = grp.Transforms[i].position - grp.Hit;
                float near = 1f - Mathf.Clamp01(away.magnitude / h);
                away = away.sqrMagnitude > 1e-6f ? away.normalized : Vector3.up;
                away.y = Mathf.Max(away.y, -0.2f); // a lying body's shards go out and up, not into the floor
                double u = rng.NextDouble();
                float share = u < 0.25 ? 0.1f + 0.25f * (float)rng.NextDouble()     // slumps
                            : u > 0.875 ? 1.8f + 0.9f * (float)rng.NextDouble()   // flies far
                            : 0.55f + 0.6f * (float)rng.NextDouble();
                share *= (0.6f + 0.8f * near) * Mathf.Lerp(1.2f, 0.55f, set.Shards[i].Size / largest);
                var v = (away * OutwardSpeed + grp.Blow * BlowSpeed * (0.5f + (float)rng.NextDouble()) + Vector3.up * UpSpeed) * share * size;
                rb.linearVelocity = v;
                float small = Mathf.Clamp(0.12f / Mathf.Max(0.01f, set.Shards[i].Size * s), 0.6f, 2.2f);
                rb.angularVelocity = RandomUnit(rng) * SpinRadPerS * (0.3f + 0.7f * Mathf.Min(2f, share)) * small * (0.6f + 0.8f * (float)rng.NextDouble());
            }
        }

        /// <summary>The killing blow's direction on the ground: from the hero to the monster at its death's impact.</summary>
        Vector3 BlowOf(int entity)
        {
            var acts = _tl.ActionsOf(entity);
            for (int k = acts.Count - 1; k >= 0; k--)
            {
                var a = acts.Array[acts.Offset + k];
                if (a.Kind != ActionKind.Death) continue;
                var m = _tl.PositionAt(entity, a.Impact);
                var h = _tl.PositionAt(0, a.Impact);
                var d = new Vector3(m.X - h.X, 0, m.Y - h.Y);
                return d.sqrMagnitude > 1e-6f ? d.normalized : Vector3.zero;
            }
            return Vector3.zero;
        }

        static Vector3 RandomUnit(System.Random rng)
        {
            float z = 2f * (float)rng.NextDouble() - 1f, a = 2f * Mathf.PI * (float)rng.NextDouble();
            float r = Mathf.Sqrt(1f - z * z);
            return new Vector3(r * Mathf.Cos(a), z, r * Mathf.Sin(a));
        }

        void SelfCollide(Group grp, bool on)
        {
            grp.SelfCollides = on;
            for (int i = 0; i < grp.Colliders.Length; i++)
                for (int j = i + 1; j < grp.Colliders.Length; j++)
                    Physics.IgnoreCollision(grp.Colliders[i], grp.Colliders[j], !on);
        }

        void StartSinking(Group grp)
        {
            grp.Sinking = true;
            var set = _sets[grp.Type];
            for (int i = 0; i < grp.Objects.Length; i++)
            {
                grp.Bodies[i].isKinematic = true;
                grp.SinkFrom[i] = grp.Transforms[i].position.y;
                grp.SinkDepth[i] = 2f * set.Shards[i].Size * grp.Scale + Mathf.Max(0, grp.SinkFrom[i]);
            }
        }

        void Recycle(Group grp)
        {
            if (!grp.SelfCollides) SelfCollide(grp, true);
            foreach (var go in grp.Objects) go.SetActive(false);
            grp.Shadow.SetActive(false);
            Shards -= grp.Objects.Length;
            _byEntity.Remove(grp.Entity);
            _played.Add(grp.Entity);
            _free[grp.Type].Add(grp);
        }

        void Clear()
        {
            foreach (var grp in _live)
            {
                Recycle(grp);
            }
            _live.Clear();
            _byEntity.Clear();
            _played.Clear();
        }

        public void Dispose()
        {
            // The shards are children of the root (the scene may have destroyed them already).
            if (_root != null) UnityEngine.Object.Destroy(_root);
            foreach (var m in _materials) if (m != null) UnityEngine.Object.Destroy(m);
            if (_motion != null) UnityEngine.Object.Destroy(_motion);
            if (_physics != null) UnityEngine.Object.Destroy(_physics);
            Physics.gravity = _gravity;
            _live.Clear();
            _byEntity.Clear();
        }
    }
}
