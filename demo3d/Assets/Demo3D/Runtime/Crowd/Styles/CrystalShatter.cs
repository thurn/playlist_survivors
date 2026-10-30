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
    /// - On the next beat (where the dissolve used to start: State.Dissolve leaves 0) they become
    ///   PhysX rigidbodies, with an outward kick from the body's centre plus the killing blow's
    ///   direction (the hero to the monster at the blow), and their inside faces show (_ShardOpen).
    ///   A monster's own shards ignore each other for <see cref="SelfIgnoreSeconds"/>, as their convex
    ///   hulls overlap while assembled; other shards and the floor collide from the start.
    /// - They land on the floor (a box under y = 0), settle, and after <see cref="LingerSeconds"/> sink
    ///   into it over <see cref="SinkSeconds"/>, then go back to the pool.
    /// - Physics runs in script mode on the song clock, in fixed steps of <see cref="Step"/>, so a
    ///   `--fixed-time` run gives the same shards every time; a seek backwards clears every shard, and
    ///   deaths before a `--start-at` point are not reconstructed (their instance stays hidden).
    /// </summary>
    public sealed class CrystalShatter : IDisposable
    {
        public const float Step = 1f / 120f;
        public const int MaxStepsPerFrame = 12; // a long hitch drops time rather than stalling
        public const float LingerSeconds = 2.5f, SinkSeconds = 1f;
        public const float SelfIgnoreSeconds = 0.35f;
        public const float Density = 2600f; // kg/m³, glass

        /// <summary>
        /// The release's kick in m/s for a 1.8 m monster (scaled by the square root of its height): outward
        /// from the body's centre, along the blow, and up; and its spin.
        /// </summary>
        public float OutwardSpeed = 1.0f, BlowSpeed = 1.3f, UpSpeed = 1.6f, SpinRadPerS = 7f;

        /// <summary>`--shatter-log`: a line per hold and release, to find deaths for filmstrips.</summary>
        readonly bool _log = Args.Has("--shatter-log");

        /// <summary>The game camera, for the log's view positions.</summary>
        public Camera View;

        static readonly int IdShardOpen = Shader.PropertyToID("_ShardOpen");

        readonly Timeline _tl;
        readonly CrowdShards.Set[] _sets;    // per crowd type, null where the type doesn't shatter
        readonly Material[] _materials;      // per crowd type: the crystal look
        readonly Material _motion;           // the opaque-queue twin that writes motion vectors (CrystalShard.shader)
        readonly float[] _centerY, _height;  // per crowd type: the body's centre height (the kick's origin) and its height
        readonly List<Group>[] _free;
        readonly List<Group> _live = new List<Group>();
        readonly Dictionary<int, Group> _byEntity = new Dictionary<int, Group>();
        readonly HashSet<int> _played = new HashSet<int>(); // entities whose shards have come and gone
        readonly GameObject _root;
        readonly PhysicsMaterial _physics;
        double _t0 = double.NaN, _last = double.NaN;
        long _steps;

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
            public bool Released, Sinking, SelfCollides;
            public double ReleasedAt;
            public Vector3 Position;
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
                foreach (var s in _sets[i].Shards) Physics.BakeMesh(s.Hull.GetEntityId(), true); // cook the hulls now, not at the first death
            }
            _motion = new Material(shader) { name = "shard_motion", renderQueue = (int)RenderQueue.GeometryLast, enableInstancing = false };
            _motion.SetShaderPassEnabled("UniversalForward", false);
            _motion.SetShaderPassEnabled("ShadowCaster", false);
            _physics = new PhysicsMaterial("shard")
            {
                bounciness = 0.25f, dynamicFriction = 0.55f, staticFriction = 0.7f,
                bounceCombine = PhysicsMaterialCombine.Average, frictionCombine = PhysicsMaterialCombine.Average,
            };
            Physics.simulationMode = SimulationMode.Script;
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
                if (!grp.Released) continue;
                double age = now - grp.ReleasedAt;
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
                _byEntity[entity] = grp;
                _live.Add(grp);
                if (_log) Log("hold", grp, inst.Position);
            }
            if (!grp.Released)
            {
                Place(grp, inst);
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
            grp.Released = grp.Sinking = false;
            grp.SelfCollides = true;
            grp.Block.SetFloat(IdShardOpen, 0);
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

        /// <summary>The assembled shards at the instance: the crowd's placement (SkinVertex) of the root-space pose.</summary>
        void Place(Group grp, in MonsterInstance inst)
        {
            grp.Position = inst.Position;
            grp.Yaw = inst.Yaw;
            grp.Scale = inst.Scale;
            var rot = Quaternion.Euler(0, inst.Yaw * Mathf.Rad2Deg, 0);
            var set = _sets[grp.Type];
            grp.Shadow.transform.SetPositionAndRotation(inst.Position, rot);
            grp.Shadow.transform.localScale = Vector3.one * inst.Scale;
            for (int i = 0; i < grp.Objects.Length; i++)
            {
                var tr = grp.Transforms[i];
                tr.SetPositionAndRotation(inst.Position + rot * (set.Shards[i].Center * inst.Scale), rot);
                tr.localScale = Vector3.one * inst.Scale;
            }
        }

        void Release(Group grp)
        {
            grp.Released = true;
            grp.ReleasedAt = _t0 + _steps * Step;
            grp.Block.SetFloat(IdShardOpen, 1);
            var blow = BlowOf(grp.Entity);
            var rng = new System.Random((int)(grp.Seed * 2654435761u) ^ grp.Entity);
            var set = _sets[grp.Type];
            var centre = grp.Position + Vector3.up * _centerY[grp.Type] * grp.Scale;
            Physics.SyncTransforms(); // the kinematic placement, before the bodies wake
            SelfCollide(grp, false);
            grp.Shadow.SetActive(false);
            for (int i = 0; i < grp.Objects.Length; i++)
            {
                var rb = grp.Bodies[i];
                rb.isKinematic = false;
                grp.Renderers[i].SetPropertyBlock(grp.Block);
                grp.Renderers[i].shadowCastingMode = ShadowCastingMode.On;
                var away = grp.Transforms[i].position - centre;
                away = away.sqrMagnitude > 1e-6f ? away.normalized : Vector3.up;
                float jitter = 0.6f + 0.8f * (float)rng.NextDouble();
                var v = (away * OutwardSpeed + blow * BlowSpeed * (0.5f + (float)rng.NextDouble()) + Vector3.up * UpSpeed) * jitter
                        * Mathf.Sqrt(grp.Scale * Mathf.Max(0.3f, _height[grp.Type] / 1.8f));
                rb.linearVelocity = v;
                rb.angularVelocity = RandomUnit(rng) * SpinRadPerS * (0.4f + (float)rng.NextDouble());
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
            _live.Clear();
            _byEntity.Clear();
        }
    }
}
