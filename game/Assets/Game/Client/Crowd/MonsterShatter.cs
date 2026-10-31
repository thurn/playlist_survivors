using System;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;

namespace Game.Client.Crowd
{
    /// <summary>One death's break, as its burst (the chips and dust the effects draw) needs it.</summary>
    public readonly struct ShatterBurst
    {
        /// <summary>The death's song time: the killing blow's impact.</summary>
        public readonly double Time;
        /// <summary>Where the blow landed on the body (world): the shards fly from it.</summary>
        public readonly Vector3 Hit;
        /// <summary>The blow's direction on the ground (unit), or zero.</summary>
        public readonly Vector3 Blow;
        /// <summary>The death's launch distance in metres (the sim's: 1.85 for a strike, 4.63 for a heavy one).</summary>
        public readonly float Force;
        /// <summary>The monster's standing height in metres (its cast scale applied).</summary>
        public readonly float Height;
        /// <summary>Its gem colour, linear.</summary>
        public readonly Color Gem;
        /// <summary>The sim's role, and the chunks it broke into.</summary>
        public readonly int Role, Shards;

        public ShatterBurst(double time, Vector3 hit, Vector3 blow, float force, float height, Color gem, int role, int shards)
        {
            Time = time;
            Hit = hit;
            Blow = blow;
            Force = force;
            Height = height;
            Gem = gem;
            Role = role;
            Shards = shards;
        }
    }

    /// <summary>
    /// The GPU monster shatter (unity-game.md, Shatter), which replaces demo3d's CrystalShatter (CPU
    /// posing and PhysX rigid bodies). Each type's rest pose is cut offline into 20 to 40 chunks
    /// (<see cref="CrowdType.ShardMesh"/>). On a death's frame the crowd blends the dying monster's
    /// palette one last time without drawing it (CrowdFlags.Dying), and <see cref="Frame"/>, after
    /// the crowd's draw:
    /// - seeds each of the frame's deaths: its palette frozen into a ring slot and its chunks placed
    ///   at their pivots on that pose, kicked by the blow (MonsterShards.compute), so they start
    ///   exactly where the body was, on the same frame it disappears;
    /// - integrates every live shard to the frame's song time: gravity, tumble, ground bounce, then
    ///   a sink after <see cref="LingerSeconds"/> over <see cref="SinkSeconds"/>;
    /// - draws each type's live deaths as instances of its chunk mesh with the crystal surface
    ///   (MonsterShard.shader), skinned from the frozen palettes and moved with their chunks.
    /// No GameObjects and no main-thread posing: per type a frame costs one upload and two
    /// dispatches for its deaths, one integration dispatch and one indirect draw, however many die.
    /// Each type keeps a ring of death slots: every death lives the same time, so they end in the
    /// order they began; a ring full of live deaths gives up its oldest. Time is the song clock, so
    /// a fixed-time run gives the same shards every time; a seek backwards clears them.
    /// <see cref="Broke"/> is the hook for each death's burst.
    /// </summary>
    public sealed class MonsterShatter : IDisposable
    {
        public const float LingerSeconds = 1.2f, SinkSeconds = 0.6f; // MonsterShards.compute's LINGER and SINK
        public const double LifeSeconds = LingerSeconds + SinkSeconds;
        /// <summary>Deaths a type can show at once.</summary>
        public const int RingCapacity = 320;
        /// <summary>Where a blow lands on a standing monster, as shares of its height: up from its feet, and back toward the hero.</summary>
        public const float HitHeight = 0.6f, HitDepth = 0.12f;
        public const int ShardStride = 80; // MonsterShards.hlsl's Shard

        /// <summary>One death as the GPU passes read it: 64 bytes, matching MonsterShards.hlsl's Death.</summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct Death
        {
            public Vector3 Position;   // the body's placement on its break (its MonsterInstance's)
            public float Yaw;
            public Vector3 Hit;        // where the blow landed (world)
            public float Scale;
            public Vector2 Blow;       // the blow on the ground, unit or zero
            public float Force;        // its launch distance (m)
            public float Time;         // the break's song time
            public uint Tint;          // the gem colour (MonsterInstance.Tint)
            public uint Seed;          // the chunks' random kicks
            public uint CrowdSlot;     // the slot of the crowd's palette it froze
            public uint Slot;          // its ring slot

            public const int Stride = 64;
        }

        /// <summary>The GPU timings' markers (a development player records them).</summary>
        public static readonly ProfilingSampler SeedSampler = new ProfilingSampler("MonsterShatter.Seed");
        public static readonly ProfilingSampler IntegrateSampler = new ProfilingSampler("MonsterShatter.Integrate");

        static readonly int IdSeeds = Shader.PropertyToID("_Seeds");
        static readonly int IdDeaths = Shader.PropertyToID("_Deaths");
        static readonly int IdPalette = Shader.PropertyToID("_Palette");
        static readonly int IdFrozen = Shader.PropertyToID("_Frozen");
        static readonly int IdShards = Shader.PropertyToID("_Shards");
        static readonly int IdShardRest = Shader.PropertyToID("_ShardRest");
        static readonly int IdSeedCount = Shader.PropertyToID("_SeedCount");
        static readonly int IdBoneCount = Shader.PropertyToID("_BoneCount");
        static readonly int IdShardCount = Shader.PropertyToID("_ShardCount");
        static readonly int IdRingStart = Shader.PropertyToID("_RingStart");
        static readonly int IdRingCount = Shader.PropertyToID("_RingCount");
        static readonly int IdRingCapacity = Shader.PropertyToID("_RingCapacity");
        static readonly int IdNow = Shader.PropertyToID("_Now");
        static readonly int IdHeightM = Shader.PropertyToID("_HeightM");
        static readonly int IdLargest = Shader.PropertyToID("_Largest");

        /// <summary>
        /// Raised on each death's break (in <see cref="Break"/>, on its first frame): the hook for its
        /// coherent crystal burst of chips and dust, which the effects draw.
        /// </summary>
        public event Action<ShatterBurst> Broke;

        readonly CrowdLibrary _lib;
        readonly Func<int, GraphicsBuffer> _palette;
        readonly ComputeShader _cs;
        readonly int _kSeedPalette, _kSeedShards, _kIntegrate;
        readonly TypeState[] _types;
        readonly CommandBuffer _cmd = new CommandBuffer { name = "MonsterShatter" };
        readonly uint[] _args = new uint[5];
        double _last = double.NegativeInfinity;

        sealed class TypeState
        {
            public CrowdType Type;
            public int Bones, ShardCount;
            public float Largest;
            public GraphicsBuffer Seeds, Deaths, Frozen, Shards, Rest, Args;
            public Material Material;
            public MaterialPropertyBlock Block;
            public readonly Death[] Pending = new Death[RingCapacity];
            public int PendingCount;
            public readonly double[] Times = new double[RingCapacity]; // each ring slot's break
            public int Start, Count, DrawnCount = -1;
        }

        /// <summary>Deaths showing shards, and their shards.</summary>
        public int LiveDeaths { get; private set; }
        public int LiveShards { get; private set; }

        /// <summary>
        /// The shatter for the library's types, seeding from <paramref name="palette"/>: a type's palette
        /// buffer, as the crowd's palette pass writes it (<see cref="BoneTextureCrowd.Palette"/>).
        /// </summary>
        public MonsterShatter(CrowdLibrary lib, Func<int, GraphicsBuffer> palette)
        {
            _lib = lib;
            _palette = palette;
            _cs = lib.ShardCompute ? lib.ShardCompute : throw new InvalidOperationException("CrowdLibrary has no shard compute (re-run `game unity BakeCrowd`)");
            if (!lib.ShardShader) throw new InvalidOperationException("CrowdLibrary has no shard shader (re-run `game unity BakeCrowd`)");
            _kSeedPalette = _cs.FindKernel("SeedPalette");
            _kSeedShards = _cs.FindKernel("SeedShards");
            _kIntegrate = _cs.FindKernel("Integrate");
            _types = new TypeState[lib.Types.Length];
            for (int i = 0; i < _types.Length; i++) _types[i] = Create(lib.Types[i]);
        }

        TypeState Create(CrowdType t)
        {
            if (t.ShardMesh == null || t.Shards.Length == 0)
                throw new InvalidOperationException($"MonsterShatter: {t.Id} has no shards (re-run `game unity BakeCrowd`)");
            var s = new TypeState { Type = t, Bones = t.BoneCount, ShardCount = t.Shards.Length };
            foreach (var sh in t.Shards) s.Largest = Mathf.Max(s.Largest, sh.Size);
            s.Seeds = new GraphicsBuffer(GraphicsBuffer.Target.Structured, RingCapacity, Death.Stride) { name = t.Id + "_shardSeeds" };
            s.Deaths = new GraphicsBuffer(GraphicsBuffer.Target.Structured, RingCapacity, Death.Stride) { name = t.Id + "_deaths" };
            s.Frozen = new GraphicsBuffer(GraphicsBuffer.Target.Structured, RingCapacity * t.BoneCount * 3, 16) { name = t.Id + "_frozen" };
            s.Shards = new GraphicsBuffer(GraphicsBuffer.Target.Structured, RingCapacity * s.ShardCount, ShardStride) { name = t.Id + "_shards" };
            s.Rest = new GraphicsBuffer(GraphicsBuffer.Target.Structured, s.ShardCount, CrowdShard.Stride) { name = t.Id + "_shardRest" };
            s.Rest.SetData(t.Shards);
            s.Args = new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments, 5, 4) { name = t.Id + "_shardArgs" };
            s.Args.SetData(new uint[] { t.ShardMesh.GetIndexCount(0), 0, t.ShardMesh.GetIndexStart(0), t.ShardMesh.GetBaseVertex(0), 0 });
            s.Material = new Material(_lib.ShardShader) { name = t.Id + "_shards", enableInstancing = false };
            s.Block = new MaterialPropertyBlock();
            s.Block.SetBuffer(IdDeaths, s.Deaths);
            s.Block.SetBuffer(IdShards, s.Shards);
            s.Block.SetBuffer(IdPalette, s.Frozen);
            s.Block.SetInt(IdBoneCount, t.BoneCount);
            s.Block.SetInt(IdShardCount, s.ShardCount);
            s.Block.SetInt(IdRingCapacity, RingCapacity);
            return s;
        }

        /// <summary>
        /// Where a blow lands on a standing monster: <see cref="HitHeight"/> of its height up from its
        /// feet, <see cref="HitDepth"/> of it back toward the hero (demo3d's CrystalShatter.HitPoint).
        /// </summary>
        public static Vector3 HitPoint(Vector3 feet, Vector3 blow, float height) =>
            feet + Vector3.up * (HitHeight * height) - blow * (HitDepth * height);

        /// <summary>
        /// A death on this frame (call before <see cref="Frame"/>, after the crowd's instances): the
        /// monster written Dying at <paramref name="crowdSlot"/> as <paramref name="body"/>, killed at
        /// song time <paramref name="time"/> by a blow along (dirX, dirY) on the ground with the sim's
        /// launch distance <paramref name="force"/>. <paramref name="serial"/> seeds its kicks.
        /// Raises <see cref="Broke"/>.
        /// </summary>
        public void Break(int type, int crowdSlot, in MonsterInstance body, int role, int serial, double time,
                          double dirX, double dirY, double force)
        {
            var s = _types[type];
            if (s.PendingCount == RingCapacity) return; // a type's whole ring dying in one frame: the rest stay bodiless
            if (s.Count == RingCapacity)
            {
                s.Start = (s.Start + 1) % RingCapacity; // give up the oldest
                s.Count--;
            }
            int slot = (s.Start + s.Count) % RingCapacity;
            s.Count++;
            s.Times[slot] = time;
            double len = Math.Sqrt(dirX * dirX + dirY * dirY);
            var blow = len > 1e-9 ? new Vector2((float)(dirX / len), (float)(dirY / len)) : Vector2.zero;
            float height = s.Type.HeightM * body.Scale;
            var hit = HitPoint(body.Position, new Vector3(blow.x, 0, blow.y), height);
            s.Pending[s.PendingCount++] = new Death
            {
                Position = body.Position, Yaw = body.Yaw, Hit = hit, Scale = body.Scale, Blow = blow,
                Force = (float)force, Time = (float)time, Tint = body.Tint, Seed = (uint)serial * 2654435761u + 0x9e37u,
                CrowdSlot = (uint)crowdSlot, Slot = (uint)slot,
            };
            var gem = new Color32((byte)body.Tint, (byte)(body.Tint >> 8), (byte)(body.Tint >> 16), 255);
            Broke?.Invoke(new ShatterBurst(time, hit, new Vector3(blow.x, 0, blow.y), (float)force, height,
                ((Color)gem).linear, role, s.ShardCount));
        }

        /// <summary>
        /// Seeds this frame's deaths from the crowd's palettes (call after the crowd's Draw, which
        /// blended them), moves every live shard to song time <paramref name="now"/> and queues the
        /// draws for the camera.
        /// </summary>
        public void Frame(double now, Camera camera)
        {
            if (now < _last) Clear();
            _last = now;
            _cmd.Clear();
            int deaths = 0, shards = 0;
            for (int type = 0; type < _types.Length; type++)
            {
                var s = _types[type];
                while (s.Count > 0 && s.Times[s.Start] + LifeSeconds <= now)
                {
                    s.Start = (s.Start + 1) % RingCapacity;
                    s.Count--;
                }
                if (s.PendingCount > 0) Seed(type, s);
                if (s.Count > 0) Integrate(s, now);
                if (s.Count != s.DrawnCount)
                {
                    _args[0] = s.Type.ShardMesh.GetIndexCount(0);
                    _args[1] = (uint)s.Count;
                    _args[2] = s.Type.ShardMesh.GetIndexStart(0);
                    _args[3] = s.Type.ShardMesh.GetBaseVertex(0);
                    _args[4] = 0;
                    _cmd.SetBufferData(s.Args, _args);
                    s.DrawnCount = s.Count;
                }
                s.Block.SetInt(IdRingStart, s.Start);
                s.Block.SetFloat(IdNow, (float)now);
                deaths += s.Count;
                shards += s.Count * s.ShardCount;
            }
            Graphics.ExecuteCommandBuffer(_cmd);
            LiveDeaths = deaths;
            LiveShards = shards;
            if (camera != null) Queue(camera);
        }

        void Seed(int type, TypeState s)
        {
            int n = s.PendingCount;
            s.PendingCount = 0;
            using var _ = new ProfilingScope(_cmd, SeedSampler);
            _cmd.SetBufferData(s.Seeds, s.Pending, 0, 0, n);
            _cmd.SetComputeIntParam(_cs, IdSeedCount, n);
            _cmd.SetComputeIntParam(_cs, IdBoneCount, s.Bones);
            _cmd.SetComputeIntParam(_cs, IdShardCount, s.ShardCount);
            _cmd.SetComputeFloatParam(_cs, IdHeightM, s.Type.HeightM);
            _cmd.SetComputeFloatParam(_cs, IdLargest, s.Largest);
            var palette = _palette(type);
            _cmd.SetComputeBufferParam(_cs, _kSeedPalette, IdSeeds, s.Seeds);
            _cmd.SetComputeBufferParam(_cs, _kSeedPalette, IdPalette, palette);
            _cmd.SetComputeBufferParam(_cs, _kSeedPalette, IdFrozen, s.Frozen);
            _cmd.DispatchCompute(_cs, _kSeedPalette, Groups(n * s.Bones * 3), 1, 1);
            _cmd.SetComputeBufferParam(_cs, _kSeedShards, IdSeeds, s.Seeds);
            _cmd.SetComputeBufferParam(_cs, _kSeedShards, IdDeaths, s.Deaths);
            _cmd.SetComputeBufferParam(_cs, _kSeedShards, IdPalette, palette);
            _cmd.SetComputeBufferParam(_cs, _kSeedShards, IdShards, s.Shards);
            _cmd.SetComputeBufferParam(_cs, _kSeedShards, IdShardRest, s.Rest);
            _cmd.DispatchCompute(_cs, _kSeedShards, Groups(n * s.ShardCount), 1, 1);
        }

        void Integrate(TypeState s, double now)
        {
            using var _ = new ProfilingScope(_cmd, IntegrateSampler);
            _cmd.SetComputeIntParam(_cs, IdShardCount, s.ShardCount);
            _cmd.SetComputeIntParam(_cs, IdRingStart, s.Start);
            _cmd.SetComputeIntParam(_cs, IdRingCount, s.Count);
            _cmd.SetComputeIntParam(_cs, IdRingCapacity, RingCapacity);
            _cmd.SetComputeFloatParam(_cs, IdNow, (float)now);
            _cmd.SetComputeBufferParam(_cs, _kIntegrate, IdDeaths, s.Deaths);
            _cmd.SetComputeBufferParam(_cs, _kIntegrate, IdShards, s.Shards);
            _cmd.DispatchCompute(_cs, _kIntegrate, Groups(s.Count * s.ShardCount), 1, 1);
        }

        static int Groups(int threads) => (threads + 63) / 64;

        /// <summary>
        /// Queues the live shards' draws for the camera again: a hand-drawn camera spends the draws
        /// queued in a frame, so a second draw of the same frame (the stills' ID render) needs them anew.
        /// </summary>
        public void Queue(Camera camera)
        {
            var bounds = new Bounds(Vector3.zero, Vector3.one * 100000f);
            foreach (var s in _types)
            {
                if (s.Count == 0) continue;
                var rp = new RenderParams(s.Material)
                {
                    worldBounds = bounds,
                    matProps = s.Block,
                    shadowCastingMode = ShadowCastingMode.On,
                    receiveShadows = true,
                    motionVectorMode = MotionVectorGenerationMode.ForceNoMotion,
                    lightProbeUsage = LightProbeUsage.Off,
                    reflectionProbeUsage = ReflectionProbeUsage.Off,
                    camera = camera,
                    layer = BoneTextureCrowd.Layer,
                };
                Graphics.RenderMeshIndirect(rp, s.Type.ShardMesh, s.Args, 1, 0);
            }
        }

        /// <summary>One chunk's state as the GPU keeps it (MonsterShards.hlsl's Shard), for the tests.</summary>
        [StructLayout(LayoutKind.Sequential)]
        internal struct ShardRecord
        {
            public Vector3 Position;
            public float Time;
            public Quaternion Rotation;
            public Vector3 Velocity;
            public float SinkFrom;
            public Vector3 Spin;
            public float Radius;
            public Vector3 Origin;
            public float Pad;
        }

        /// <summary>The type's shard records, [ring slot][chunk], read back from the GPU (for the tests).</summary>
        internal ShardRecord[] ReadShards(int type)
        {
            var s = _types[type];
            var o = new ShardRecord[RingCapacity * s.ShardCount];
            s.Shards.GetData(o);
            return o;
        }

        /// <summary>The type's frozen palettes, [ring slot][bone][3 rows], read back from the GPU (for the tests).</summary>
        internal Vector4[] ReadFrozen(int type)
        {
            var s = _types[type];
            var o = new Vector4[RingCapacity * s.Bones * 3];
            s.Frozen.GetData(o);
            return o;
        }

        void Clear()
        {
            foreach (var s in _types) s.Start = s.Count = s.PendingCount = 0;
        }

        public void Dispose()
        {
            _cmd.Release();
            foreach (var s in _types)
            {
                s.Seeds?.Dispose();
                s.Deaths?.Dispose();
                s.Frozen?.Dispose();
                s.Shards?.Dispose();
                s.Rest?.Dispose();
                s.Args?.Dispose();
                if (s.Material == null) continue;
                if (Application.isPlaying) UnityEngine.Object.Destroy(s.Material);
                else UnityEngine.Object.DestroyImmediate(s.Material); // the EditMode tests
            }
        }
    }
}
