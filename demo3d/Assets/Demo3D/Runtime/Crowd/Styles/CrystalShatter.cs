using System;
using System.Collections.Generic;
using PS.Choreography;
using UnityEngine;
using UnityEngine.Rendering;

namespace Demo3D.Runtime.Crowd
{
    /// <summary>
    /// The crystal shatter (crowd.md, Monster styles; epic ps-2iq, reworked to break at the blow):
    /// wherever the Crystal shader draws a type, its deaths break into the baked shards
    /// (<see cref="CrowdShards"/>) instead of playing a death clip.
    ///
    /// - The killing blow's frame (TimelineEvaluator.State.Broken: the death plays no clip, so up to
    ///   the blow the monster walks, attacks or flinches as before) hides the crowd instance and
    ///   places the type's chunks from a pool, posed as the crowd drew the monster on that frame: the
    ///   chunks are cut on the rest pose with their skin weights, and <see cref="CrowdPose"/> skins
    ///   their corners on the CPU from the same bone texture rows, crossfade and placement as the
    ///   crowd's shader, so the swap doesn't show. Each chunk then moves rigidly.
    /// - On that same frame they become PhysX rigidbodies, kicked outward from where the blow landed
    ///   (on the body, facing the hero, at chest height: <see cref="HitPoint"/>) and along the blow
    ///   (the hero to the monster), with wide variance: some slump where they stand, most scatter, a
    ///   few fly far, small ones spinning fastest. They flash white as the crowd's hit flash does,
    ///   fading over <see cref="FlashSeconds"/>. A monster's own shards ignore each other for
    ///   <see cref="SelfIgnoreSeconds"/>, as their colliders overlap while assembled; other shards and
    ///   the floor collide from the start. Colliders are boxes fitted to each posed chunk, aligned to
    ///   the bone that carries most of it.
    /// - Gravity is <see cref="GravityScale"/> times Earth's, so they clack down rather than float;
    ///   they bounce once or twice, skitter on the floor's friction (a box under y = 0) and settle into
    ///   a pile, and after <see cref="LingerSeconds"/> sink into it over <see cref="SinkSeconds"/>, then
    ///   go back to the pool.
    /// - Physics runs in script mode on the song clock, in fixed steps of <see cref="Step"/>, so a
    ///   `--fixed-time` run gives the same shards every time; a seek backwards clears every shard, and
    ///   deaths before a run's first frame are not reconstructed (their instance stays hidden).
    /// </summary>
    public sealed class CrystalShatter : IDisposable
    {
        public const float Step = 1f / 120f;
        public const int MaxStepsPerFrame = 12; // a long hitch drops time rather than stalling
        public const float LingerSeconds = 1.2f, SinkSeconds = 0.6f; // short, so piles never bury the hero
        public const float SelfIgnoreSeconds = 0.35f;
        public const float Density = 2600f; // kg/m³, glass
        public const float GravityScale = 2.2f;
        /// <summary>The shards' white flash at the break fades over this long (the crowd's hit flash, ClipSchedule.FlashSeconds).</summary>
        public const float FlashSeconds = ClipSchedule.FlashSeconds;
        /// <summary>Where the blow lands, as shares of the monster's height: up from its feet, and back toward the hero.</summary>
        public const float HitHeight = 0.6f, HitDepth = 0.12f;

        /// <summary>
        /// The break's kick in m/s for a 1.8 m monster (scaled by the square root of its height):
        /// outward from the hit point, along the blow, and up; and its spin. Each shard's share of it
        /// varies widely (see Launch).
        /// </summary>
        public float OutwardSpeed = 2.8f, BlowSpeed = 5.0f, UpSpeed = 1.6f, SpinRadPerS = 12f;

        /// <summary>
        /// Above 0, at most this many deaths fly shards at once (an intensity's blow feel, epic ps-5pd
        /// Decision 7): <see cref="Breaks"/> gives them to deaths in the view, in time order, while fewer
        /// are live, counting each from its break to its sinking's end. The rest, and every death out
        /// of view, play their death clip and break by the Crystal shader's own shatter, with the stock
        /// death burst and sound. 0, the default: every crystal death shatters. Set it before
        /// <see cref="Breaks"/>.
        /// </summary>
        public int Cap;

        /// <summary>`--shatter-log`: a line per break, to find deaths for filmstrips.</summary>
        readonly bool _log = Args.Has("--shatter-log");

        /// <summary>The game camera, for the log's view positions.</summary>
        public Camera View;

        static readonly int IdFlash = Shader.PropertyToID("_ShardFlash");

        /// <summary>One chunk's rest-pose data, which the break skins (CrowdShards.Shard).</summary>
        sealed class Rest
        {
            public Vector3[] Position, Normal;
            public Vector4[] Bones, Weights;
            public List<Vector2> Inside = new List<Vector2>();
            public List<Vector4> Bary = new List<Vector4>();
            public int SkinCorners; // corners before the inside faces
            public float Volume, Size;
            public int Bone;
        }

        sealed class TypeData
        {
            public string Id;
            public CrowdPose Pose;
            public Matrix4x4[] Bones;
            public Rest[] Shards;
            public Vector3[] Posed, Normals, Local; // scratch, the largest chunk's corners
            public Material Material;               // the crystal look
            public float CenterY, Height;
            public float Largest;                   // the largest chunk's size
        }

        readonly Timeline _tl;
        readonly TypeData[] _types;          // per crowd type, null where the type doesn't shatter
        readonly Material _motion;           // the opaque-queue twin that writes motion vectors (CrystalShard.shader)
        readonly Color[] _gem;               // per crowd type: its gem colour (linear), for the burst and its light
        readonly List<Group>[] _free;
        readonly List<Group> _live = new List<Group>();
        readonly Dictionary<int, Group> _byEntity = new Dictionary<int, Group>();
        readonly Dictionary<int, BreakEvent> _breaks = new Dictionary<int, BreakEvent>(); // the deaths that shatter, by entity
        readonly HashSet<int> _played = new HashSet<int>(); // entities whose shards have come and gone
        readonly GameObject _root;
        readonly PhysicsMaterial _physics;
        readonly Vector3 _gravity; // the project's, restored on Dispose
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
            public Transform[] Transforms;
            public Rigidbody[] Bodies;
            public BoxCollider[] Colliders;
            public Renderer[] Renderers;
            public Mesh[] Meshes;
            public MaterialPropertyBlock Block;
            public float[] SinkFrom, SinkDepth;
            public bool Sinking, SelfCollides, Flashing;
            public double BrokeAt;         // the song time of its first frame
            public double FlewAt;          // physics time it began to fly
            public Vector3 Hit, Blow;      // where the blow landed (world); the blow's direction on the ground
            public float Scale;
            public uint Seed;
        }

        CrystalShatter(Timeline tl, MonsterStyle style, CrowdLibrary lib, CrowdType[] types)
        {
            _tl = tl;
            var shards = CrowdShards.Load();
            var shader = lib.StyleShader("Demo3D/Styles/CrystalShard");
            _types = new TypeData[types.Length];
            _gem = new Color[types.Length];
            _free = new List<Group>[types.Length];
            for (int i = 0; i < types.Length; i++)
            {
                _free[i] = new List<Group>();
                if (style.StyleOf(types[i].Id) != "crystal") continue;
                var set = shards.Of(types[i].Id);
                // The crowd's own crystal material for the type, so the shards wear exactly its look.
                var crowd = style.Materials(lib, types[i])[0];
                var material = new Material(shader) { name = types[i].Id + "_shard", enableInstancing = false };
                material.CopyMatchingPropertiesFromMaterial(crowd);
                var td = new TypeData
                {
                    Id = types[i].Id, Pose = CrowdPose.Of(types[i]), Bones = new Matrix4x4[types[i].BoneCount],
                    Shards = new Rest[set.Shards.Length], Material = material, CenterY = types[i].CenterY, Height = types[i].HeightM,
                };
                int most = 0;
                for (int s = 0; s < set.Shards.Length; s++)
                {
                    var sh = set.Shards[s];
                    var m = sh.Mesh;
                    var r = new Rest
                    {
                        Position = m.vertices, Normal = m.normals, Volume = sh.Volume, Size = sh.Size, Bone = sh.Bone,
                    };
                    var bones = new List<Vector4>(); m.GetUVs(4, bones);
                    var weights = new List<Vector4>(); m.GetUVs(5, weights);
                    r.Bones = bones.ToArray();
                    r.Weights = weights.ToArray();
                    m.GetUVs(1, r.Inside);
                    m.GetUVs(3, r.Bary);
                    while (r.SkinCorners < r.Inside.Count && r.Inside[r.SkinCorners].x < 0.5f) r.SkinCorners++;
                    td.Shards[s] = r;
                    td.Largest = Mathf.Max(td.Largest, sh.Size);
                    most = Mathf.Max(most, r.Position.Length);
                }
                td.Posed = new Vector3[most];
                td.Normals = new Vector3[most];
                td.Local = new Vector3[most];
                _types[i] = td;
                _gem[i] = StyleLook.Of(types[i].Id).gem;
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

        public bool Shatters(int type) => _types[type] != null;

        /// <summary>One crystal death's break, known from the timeline ahead of time: its burst, light and sound.</summary>
        public struct BreakEvent
        {
            public double Time;     // the killing blow's impact
            public int Entity, Type;
            public Vector3 Hit;     // where the blow lands on the body (world, HitPoint)
            public Vector3 Blow;    // the blow's direction on the ground (the hero to the monster), or zero
            public float Scale;     // the instance's scale
            public float Height;    // the type's standing height in metres, times the scale
            public Color Gem;       // the type's gem colour (linear)
            public uint Seed;
        }

        /// <summary>
        /// Where a blow lands on a standing monster: <see cref="HitHeight"/> of its height up from its
        /// feet, <see cref="HitDepth"/> of it back toward the hero. The shards fly from it and the
        /// burst sprays from it.
        /// </summary>
        public static Vector3 HitPoint(Vector3 feet, Vector3 blow, float height) =>
            feet + Vector3.up * (HitHeight * height) - blow * (HitDepth * height);

        /// <summary>
        /// Every crystal death's break in the timeline, by time: the killing blow's impact, where the
        /// client hides the monster and <see cref="Breaking"/> places its shards. The client's
        /// TimelineEvaluator.BreaksAtImpact takes their entities. <paramref name="scaleOf"/> is the
        /// client's instance scale for an entity's seed, <paramref name="hoverOf"/> a type's height
        /// above the ground (bats).
        /// </summary>
        public BreakEvent[] Breaks(Func<uint, float> scaleOf, Func<int, float> hoverOf)
        {
            var list = new List<BreakEvent>();
            for (int i = 0; i < _tl.Actions.Length; i++)
            {
                var a = _tl.Actions[i];
                if (a.Kind != ActionKind.Death) continue;
                var info = _tl.Entities[a.Entity];
                if (info.Kind != EntityKind.Monster || info.Type < 0 || info.Type >= _types.Length || _types[info.Type] == null) continue;
                var p = _tl.PositionAt(a.Entity, a.Impact);
                float scale = scaleOf(info.Seed);
                float height = _types[info.Type].Height * scale;
                var blow = BlowOf(a.Entity, a.Impact);
                list.Add(new BreakEvent
                {
                    Time = a.Impact, Entity = a.Entity, Type = info.Type, Blow = blow, Scale = scale, Height = height,
                    Hit = HitPoint(new Vector3(p.X, hoverOf(info.Type), p.Y), blow, height), Gem = _gem[info.Type], Seed = info.Seed,
                });
            }
            list.Sort((x, y) => { int c = x.Time.CompareTo(y.Time); return c != 0 ? c : x.Entity.CompareTo(y.Entity); });
            if (Cap > 0) list = Capped(list);
            _breaks.Clear();
            foreach (var b in list) _breaks[b.Entity] = b;
            return list.ToArray();
        }

        /// <summary>The breaks <see cref="Cap"/> keeps (see there).</summary>
        List<BreakEvent> Capped(List<BreakEvent> breaks)
        {
            var kept = new List<BreakEvent>(breaks.Count);
            var ends = new List<double>(); // live sets' ends (any order)
            foreach (var r in breaks)
            {
                ends.RemoveAll(e => e <= r.Time);
                var f = _tl.FramingAt(r.Time);
                bool inView = new ViewGeometry(f.ViewWidth).Contains(f.Focus, new System.Numerics.Vector2(r.Hit.x, r.Hit.z), 1f);
                if (!inView || ends.Count >= Cap) continue;
                ends.Add(r.Time + LingerSeconds + SinkSeconds + 0.5);
                kept.Add(r);
            }
            Debug.Log($"CrystalShatter: cap {Cap} live sets keeps {kept.Count} of {breaks.Count} breaks");
            return kept;
        }

        /// <summary>
        /// Brings the shards to song time t: a seek backwards clears them; forward, physics steps on
        /// the song clock, the break's flash fades, and settled shards sink and go back to the pool.
        /// Call once a frame, before <see cref="Breaking"/>.
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
                if (grp.Flashing)
                {
                    float flash = Mathf.Clamp01(1f - (float)((t - grp.BrokeAt) / FlashSeconds));
                    grp.Flashing = flash > 0;
                    grp.Block.SetFloat(IdFlash, flash);
                    foreach (var r in grp.Renderers) r.SetPropertyBlock(grp.Block);
                }
                double age = now - grp.FlewAt;
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
        /// A crystal monster from its killing blow on (TimelineEvaluator.State.Broken), with the
        /// instance the crowd would draw this frame: the first call breaks it into its shards at that
        /// pose and launches them. Returns true if the crowd instance should be hidden, which it is from
        /// the blow on.
        /// </summary>
        public bool Breaking(int entity, int type, in MonsterInstance inst)
        {
            if (_types[type] == null || !_breaks.TryGetValue(entity, out var brk)) return false;
            if (_played.Contains(entity) || _byEntity.ContainsKey(entity)) return true;
            if (brk.Time < _t0 - 1e-6) return true; // it broke before this run's first frame: not reconstructed
            var grp = Take(type);
            grp.Entity = entity;
            grp.Seed = _tl.Entities[entity].Seed;
            grp.Scale = inst.Scale;
            grp.Blow = brk.Blow;
            grp.Hit = brk.Hit;
            grp.BrokeAt = _last;
            _byEntity[entity] = grp;
            _live.Add(grp);
            Pose(grp, inst);
            Launch(grp);
            if (_log) Log(grp, inst.Position);
            return true;
        }

        /// <summary>A `--shatter-log` line: the death, its song time, world position and place in the game camera's view (1080p pixels from the top left).</summary>
        void Log(Group grp, Vector3 at)
        {
            var cam = View;
            var vp = cam != null ? cam.WorldToViewportPoint(at + Vector3.up * _types[grp.Type].CenterY * grp.Scale) : Vector3.zero;
            Debug.Log(FormattableString.Invariant(
                $"CrystalShatter: break {_types[grp.Type].Id} entity {grp.Entity} at t {_last:F3} ({at.x:F2}, {at.z:F2}), view px ({vp.x * 1920:F0}, {(1 - vp.y) * 1080:F0}){(vp.z > 0 && vp.x is > 0 and < 1 && vp.y is > 0 and < 1 ? "" : " off screen")}"));
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
            grp.Sinking = false;
            grp.SelfCollides = grp.Flashing = true;
            grp.Block.SetFloat(IdFlash, 1);
            for (int i = 0; i < grp.Objects.Length; i++)
            {
                grp.Bodies[i].isKinematic = true;
                grp.Renderers[i].SetPropertyBlock(grp.Block);
                grp.Objects[i].SetActive(true);
            }
            Shards += grp.Objects.Length;
            PeakShards = Math.Max(PeakShards, Shards);
            Deaths++;
            return grp;
        }

        Group Create(int type)
        {
            var td = _types[type];
            int n = td.Shards.Length;
            var grp = new Group
            {
                Type = type, Objects = new GameObject[n], Transforms = new Transform[n], Bodies = new Rigidbody[n],
                Colliders = new BoxCollider[n], Renderers = new Renderer[n], Meshes = new Mesh[n], Block = new MaterialPropertyBlock(),
                SinkFrom = new float[n], SinkDepth = new float[n],
            };
            var mats = new[] { td.Material, _motion };
            for (int i = 0; i < n; i++)
            {
                var rest = td.Shards[i];
                var go = new GameObject(td.Id + "_shard" + i);
                go.transform.SetParent(_root.transform, false);
                // The group's own mesh, rewritten at each break: the chunk at the pose it broke in.
                var mesh = new Mesh { name = go.name };
                mesh.MarkDynamic();
                mesh.SetVertices(rest.Position);
                mesh.SetNormals(rest.Normal);
                mesh.SetUVs(1, rest.Inside);
                mesh.SetUVs(3, rest.Bary);
                var indices = new int[rest.Position.Length];
                for (int k = 0; k < indices.Length; k++) indices[k] = k;
                if (indices.Length > 65535) mesh.indexFormat = IndexFormat.UInt32;
                mesh.SetTriangles(indices, 0, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterials = mats; // the second draws the mesh again: its motion vectors only
                r.shadowCastingMode = ShadowCastingMode.On;
                r.receiveShadows = true;
                r.motionVectorGenerationMode = MotionVectorGenerationMode.Object;
                r.lightProbeUsage = LightProbeUsage.Off;
                r.reflectionProbeUsage = ReflectionProbeUsage.Off;
                var col = go.AddComponent<BoxCollider>();
                col.sharedMaterial = _physics;
                var rb = go.AddComponent<Rigidbody>();
                rb.isKinematic = true;
                rb.interpolation = RigidbodyInterpolation.None;
                rb.collisionDetectionMode = CollisionDetectionMode.Discrete;
                rb.maxDepenetrationVelocity = 1.5f;
                go.SetActive(false);
                grp.Objects[i] = go;
                grp.Transforms[i] = go.transform;
                grp.Bodies[i] = rb;
                grp.Colliders[i] = col;
                grp.Renderers[i] = r;
                grp.Meshes[i] = mesh;
            }
            return grp;
        }

        /// <summary>
        /// Poses every chunk as the crowd draws the instance (CrowdPose: the instance's bones, each
        /// corner skinned by its weights and placed), then makes it a rigid piece: its frame turns
        /// with the bone that carries most of it, its pivot is the centre of its box in that frame,
        /// and its mesh is rewritten relative to them. The box collider is that box, a little
        /// smaller, as the chunk tapers to its apex.
        /// </summary>
        void Pose(Group grp, in MonsterInstance inst)
        {
            var td = _types[grp.Type];
            td.Pose.Bones(inst, td.Bones);
            var yaw = Quaternion.Euler(0, inst.Yaw * Mathf.Rad2Deg, 0);
            for (int i = 0; i < td.Shards.Length; i++)
            {
                var rest = td.Shards[i];
                int n = rest.Position.Length;
                for (int v = 0; v < n; v++)
                {
                    var skin = CrowdPose.Blend(td.Bones, rest.Bones[v], rest.Weights[v]);
                    td.Posed[v] = CrowdPose.Place(skin, rest.Position[v], inst);
                    if (v < rest.SkinCorners) td.Normals[v] = CrowdPose.Turn(skin, rest.Normal[v], inst);
                }
                // Inside faces: flat, from their posed corners.
                for (int v = rest.SkinCorners; v + 2 < n; v += 3)
                {
                    var f = Vector3.Cross(td.Posed[v + 1] - td.Posed[v], td.Posed[v + 2] - td.Posed[v]);
                    var nor = f.sqrMagnitude > 1e-20f ? f.normalized : Vector3.up;
                    td.Normals[v] = td.Normals[v + 1] = td.Normals[v + 2] = nor;
                }
                var bone = td.Bones[Mathf.Clamp(rest.Bone, 0, td.Bones.Length - 1)];
                Vector3 fwd = bone.GetColumn(2), up = bone.GetColumn(1);
                var rot = yaw * (fwd.sqrMagnitude > 1e-8f && up.sqrMagnitude > 1e-8f && Vector3.Cross(fwd, up).sqrMagnitude > 1e-10f
                    ? Quaternion.LookRotation(fwd, up) : Quaternion.identity);
                var inv = Quaternion.Inverse(rot);
                var origin = td.Posed[0];
                Vector3 lo = Vector3.positiveInfinity, hi = Vector3.negativeInfinity;
                for (int v = 0; v < n; v++)
                {
                    var l = inv * (td.Posed[v] - origin);
                    td.Local[v] = l;
                    lo = Vector3.Min(lo, l);
                    hi = Vector3.Max(hi, l);
                }
                var centre = (lo + hi) * 0.5f;
                for (int v = 0; v < n; v++)
                {
                    td.Local[v] -= centre;
                    td.Normals[v] = inv * td.Normals[v];
                }
                var mesh = grp.Meshes[i];
                mesh.SetVertices(td.Local, 0, n);
                mesh.SetNormals(td.Normals, 0, n);
                mesh.RecalculateBounds();
                grp.Transforms[i].SetPositionAndRotation(origin + rot * centre, rot);
                grp.Transforms[i].localScale = Vector3.one;
                grp.Colliders[i].center = Vector3.zero;
                grp.Colliders[i].size = Vector3.Max((hi - lo) * 0.85f, Vector3.one * 0.01f);
            }
        }

        /// <summary>A shard's random number in [0, 1) for a death.</summary>
        static float Hash(uint seed, int i)
        {
            uint x = seed * 747796405u + (uint)i * 2891336453u;
            x ^= x >> 16; x *= 0x7feb352du; x ^= x >> 15; x *= 0x846ca68bu; x ^= x >> 16;
            return (x & 0xffffff) / 16777216f;
        }

        /// <summary>
        /// The break: each shard becomes a rigidbody, kicked outward from the hit point (harder near
        /// it), along the blow and a little up. Its share of the kick varies widely: one in four slump
        /// where they stand (the legs fold under the body), one in eight flies far; the largest chunks
        /// get about half the smallest's share, as heavy pieces don't fly. Small shards spin faster.
        /// </summary>
        void Launch(Group grp)
        {
            grp.FlewAt = _t0 + _steps * Step;
            var rng = new System.Random((int)(grp.Seed * 2654435761u) ^ grp.Entity);
            var td = _types[grp.Type];
            float s = grp.Scale;
            float h = td.Height * s;
            float size = Mathf.Sqrt(s * Mathf.Max(0.3f, td.Height / 1.8f));
            Physics.SyncTransforms(); // the kinematic placement, before the bodies wake
            SelfCollide(grp, false);
            for (int i = 0; i < grp.Objects.Length; i++)
            {
                var rest = td.Shards[i];
                var rb = grp.Bodies[i];
                rb.isKinematic = false;
                rb.mass = Mathf.Max(0.02f, rest.Volume * s * s * s * Density);
                var away = grp.Transforms[i].position - grp.Hit;
                float near = 1f - Mathf.Clamp01(away.magnitude / h);
                away = away.sqrMagnitude > 1e-6f ? away.normalized : grp.Blow;
                away.y = Mathf.Max(away.y, -0.3f); // shards below the hit go out, not straight into the floor
                double u = rng.NextDouble();
                float share = u < 0.25 ? 0.1f + 0.25f * (float)rng.NextDouble()     // slumps
                            : u > 0.875 ? 1.6f + 0.8f * (float)rng.NextDouble()   // flies far
                            : 0.55f + 0.6f * (float)rng.NextDouble();
                share *= (0.6f + 0.8f * near) * Mathf.Lerp(1.2f, 0.55f, rest.Size / Mathf.Max(td.Largest, 1e-4f));
                var v = (away * OutwardSpeed + grp.Blow * BlowSpeed * (0.5f + (float)rng.NextDouble()) + Vector3.up * UpSpeed) * share * size;
                rb.linearVelocity = v;
                float small = Mathf.Clamp(0.12f / Mathf.Max(0.01f, rest.Size * s), 0.6f, 2.2f);
                rb.angularVelocity = RandomUnit(rng) * SpinRadPerS * (0.3f + 0.7f * Mathf.Min(2f, share)) * small * (0.6f + 0.8f * (float)rng.NextDouble());
            }
        }

        /// <summary>The killing blow's direction on the ground: from the hero to the monster at its impact.</summary>
        Vector3 BlowOf(int entity, double impact)
        {
            var m = _tl.PositionAt(entity, impact);
            var h = _tl.PositionAt(0, impact);
            var d = new Vector3(m.X - h.X, 0, m.Y - h.Y);
            return d.sqrMagnitude > 1e-6f ? d.normalized : Vector3.zero;
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
            var td = _types[grp.Type];
            for (int i = 0; i < grp.Objects.Length; i++)
            {
                grp.Bodies[i].isKinematic = true;
                grp.SinkFrom[i] = grp.Transforms[i].position.y;
                grp.SinkDepth[i] = 2f * td.Shards[i].Size * grp.Scale + Mathf.Max(0, grp.SinkFrom[i]);
            }
        }

        void Recycle(Group grp)
        {
            if (!grp.SelfCollides) SelfCollide(grp, true);
            foreach (var go in grp.Objects) go.SetActive(false);
            Shards -= grp.Objects.Length;
            _byEntity.Remove(grp.Entity);
            _played.Add(grp.Entity);
            _free[grp.Type].Add(grp);
        }

        void Clear()
        {
            foreach (var grp in _live) Recycle(grp);
            _live.Clear();
            _byEntity.Clear();
            _played.Clear();
        }

        public void Dispose()
        {
            // The shards are children of the root (the scene may have destroyed them already).
            if (_root != null) UnityEngine.Object.Destroy(_root);
            foreach (var td in _types) if (td != null) UnityEngine.Object.Destroy(td.Material);
            foreach (var free in _free)
                foreach (var grp in free)
                    foreach (var m in grp.Meshes) if (m != null) UnityEngine.Object.Destroy(m);
            foreach (var grp in _live)
                foreach (var m in grp.Meshes) if (m != null) UnityEngine.Object.Destroy(m);
            if (_motion != null) UnityEngine.Object.Destroy(_motion);
            if (_physics != null) UnityEngine.Object.Destroy(_physics);
            Physics.gravity = _gravity;
            _live.Clear();
            _byEntity.Clear();
        }
    }
}
