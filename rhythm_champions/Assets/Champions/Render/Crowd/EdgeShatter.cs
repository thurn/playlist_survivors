using System;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;

namespace Champions.Render.Crowd
{
    /// <summary>
    /// The GPU monster shatter (design.md, The Look &gt; Shatter): game/'s MonsterShatter, ported by review for edge-cut
    /// pieces. Each type's cut comes pre-broken into 20 to 40 pieces (<see cref="EdgeCutType.Pieces"/>, a piece index
    /// on every body and line vertex). On a death's frame the crowd blends the dying monster's palette one last time
    /// without drawing it (CrowdFlags.Dying), and <see cref="Frame"/>, after the crowd's draw:
    /// - seeds each of the frame's deaths: its palette frozen into a ring slot and its pieces placed at their pivots
    ///   on that pose, thrown hard away from the blow and up (EdgeShards.compute), so they start exactly where the
    ///   body was, on the frame it disappears;
    /// - integrates every live piece to the frame's song time: gravity, tumble, ground bounce, then a sink after
    ///   <see cref="LingerSeconds"/> over <see cref="SinkSeconds"/>;
    /// - draws each type's live deaths with the cut's own body and line meshes in their shard variant (EDGE_SHARD),
    ///   skinned from the frozen palettes and moved with their pieces.
    /// No cracks, delay or twinkle: the pieces fly on the impact frame. No GameObjects and no main-thread posing: per
    /// type a frame costs one upload and two dispatches for its deaths, one integration dispatch and two indirect
    /// draws, however many die. Each type keeps a ring of death slots: every death lives the same time, so they end
    /// in the order they began; a ring full of live deaths gives up its oldest. Time is the song clock, so a
    /// fixed-time run gives the same pieces every time; a seek backwards clears them.
    /// </summary>
    public sealed class EdgeShatter : IDisposable
    {
        public const float LingerSeconds = 1.2f, SinkSeconds = 0.6f; // EdgeShards.compute's LINGER and SINK
        public const double LifeSeconds = LingerSeconds + SinkSeconds;
        /// <summary>Deaths a type can show at once.</summary>
        public const int RingCapacity = 320;
        /// <summary>Where a blow lands on a standing monster, as shares of its height: up from its feet, and back toward the hero.</summary>
        public const float HitHeight = 0.6f, HitDepth = 0.12f;
        public const int ShardStride = 80; // EdgeShards.hlsl's Shard

        /// <summary>One death as the GPU passes read it: 64 bytes, matching EdgeShards.hlsl's Death.</summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct Death
        {
            public Vector3 Position;
            public float Yaw;
            public Vector3 Hit;
            public float Scale;
            public Vector2 Blow;
            public float Force;
            public float Time;
            public uint Tint;
            public uint Seed;
            public uint CrowdSlot;
            public uint Slot;

            public const int Stride = 64;
        }

        /// <summary>The GPU timings' marker (a development player records them).</summary>
        public static readonly ProfilingSampler Sampler = new ProfilingSampler("EdgeShatter.Seed+Integrate");

        static readonly int IdSeeds = Shader.PropertyToID("_Seeds");
        static readonly int IdDeaths = Shader.PropertyToID("_Deaths");
        static readonly int IdPalette = Shader.PropertyToID("_Palette");
        static readonly int IdFrozen = Shader.PropertyToID("_Frozen");
        static readonly int IdShards = Shader.PropertyToID("_Shards");
        static readonly int IdPieceRest = Shader.PropertyToID("_PieceRest");
        static readonly int IdSeedCount = Shader.PropertyToID("_SeedCount");
        static readonly int IdBoneCount = Shader.PropertyToID("_BoneCount");
        static readonly int IdPieceCount = Shader.PropertyToID("_PieceCount");
        static readonly int IdRingStart = Shader.PropertyToID("_RingStart");
        static readonly int IdRingCount = Shader.PropertyToID("_RingCount");
        static readonly int IdRingCapacity = Shader.PropertyToID("_RingCapacity");
        static readonly int IdNow = Shader.PropertyToID("_Now");
        static readonly int IdHeightM = Shader.PropertyToID("_HeightM");
        static readonly int IdLargest = Shader.PropertyToID("_Largest");
        const string ShardKeyword = "EDGE_SHARD";

        readonly EdgeCutLibrary _lib;
        readonly Func<int, GraphicsBuffer> _palette;
        readonly ComputeShader _cs;
        readonly int _kSeedPalette, _kSeedShards, _kIntegrate;
        readonly TypeState[] _types;
        readonly CommandBuffer _cmd = new CommandBuffer { name = "EdgeShatter" };
        readonly uint[] _args = new uint[10];
        double _last = double.NegativeInfinity;

        sealed class TypeState
        {
            public EdgeCutType Type;
            public int Bones, Pieces;
            public float Largest;
            public GraphicsBuffer Seeds, Deaths, Frozen, Shards, Rest, Args;
            public Material Body, Lines;
            public MaterialPropertyBlock Block;
            public readonly Death[] Pending = new Death[RingCapacity];
            public int PendingCount;
            public readonly double[] Times = new double[RingCapacity]; // each ring slot's break
            public int Start, Count, DrawnCount = -1;
        }

        /// <summary>Deaths broken so far.</summary>
        public int Breaks { get; private set; }

        /// <summary>Deaths showing pieces, and their pieces.</summary>
        public int LiveDeaths { get; private set; }
        public int LivePieces { get; private set; }

        /// <summary>
        /// The shatter for the library's types, seeding from <paramref name="palette"/>: a type's palette buffer, as
        /// the crowd's palette pass writes it (<see cref="EdgeCrowd.Palette"/>).
        /// </summary>
        public EdgeShatter(EdgeCutLibrary lib, Func<int, GraphicsBuffer> palette)
        {
            _lib = lib;
            _palette = palette;
            _cs = lib.Shards ? lib.Shards : throw new InvalidOperationException("EdgeCutLibrary has no shard compute");
            _kSeedPalette = _cs.FindKernel("SeedPalette");
            _kSeedShards = _cs.FindKernel("SeedShards");
            _kIntegrate = _cs.FindKernel("Integrate");
            _types = new TypeState[lib.Types.Length];
            for (int i = 0; i < _types.Length; i++) _types[i] = Create(lib.Types[i]);
        }

        TypeState Create(EdgeCutType t)
        {
            if (t.Pieces.Length == 0) throw new InvalidOperationException($"EdgeShatter: {t.Id} has no pieces (re-run `champions edgecut {t.Id}`)");
            var s = new TypeState { Type = t, Bones = t.BoneCount, Pieces = t.Pieces.Length };
            foreach (var p in t.Pieces) s.Largest = Mathf.Max(s.Largest, p.Size);
            s.Seeds = new GraphicsBuffer(GraphicsBuffer.Target.Structured, RingCapacity, Death.Stride) { name = t.Id + "_shardSeeds" };
            s.Deaths = new GraphicsBuffer(GraphicsBuffer.Target.Structured, RingCapacity, Death.Stride) { name = t.Id + "_deaths" };
            s.Frozen = new GraphicsBuffer(GraphicsBuffer.Target.Structured, RingCapacity * t.BoneCount * 3, 16) { name = t.Id + "_frozen" };
            s.Shards = new GraphicsBuffer(GraphicsBuffer.Target.Structured, RingCapacity * s.Pieces, ShardStride) { name = t.Id + "_shards" };
            s.Rest = new GraphicsBuffer(GraphicsBuffer.Target.Structured, s.Pieces, EdgeShardPiece.Stride) { name = t.Id + "_pieceRest" };
            s.Rest.SetData(t.Pieces);
            s.Args = new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments, 10, 4) { name = t.Id + "_shardArgs" };
            s.Body = new Material(_lib.Body) { name = t.Id + "_shardBody", enableInstancing = false };
            s.Body.EnableKeyword(ShardKeyword);
            s.Lines = new Material(_lib.Lines) { name = t.Id + "_shardLines", enableInstancing = false };
            s.Lines.EnableKeyword(ShardKeyword);
            s.Block = new MaterialPropertyBlock();
            s.Block.SetBuffer(IdDeaths, s.Deaths);
            s.Block.SetBuffer(IdShards, s.Shards);
            s.Block.SetBuffer(IdPalette, s.Frozen);
            s.Block.SetInt(IdBoneCount, t.BoneCount);
            s.Block.SetInt(IdPieceCount, s.Pieces);
            s.Block.SetInt(IdRingCapacity, RingCapacity);
            s.Block.SetFloat(IdHeightM, t.HeightM);
            s.Block.SetFloat(IdPass.Id, IdPass.Effect); // shards are debris in the ID pass, not a monster's silhouette
            return s;
        }

        /// <summary>
        /// Where a blow lands on a standing monster: <see cref="HitHeight"/> of its height up from its feet,
        /// <see cref="HitDepth"/> of it back toward the hero.
        /// </summary>
        public static Vector3 HitPoint(Vector3 feet, Vector3 blow, float height) =>
            feet + Vector3.up * (HitHeight * height) - blow * (HitDepth * height);

        /// <summary>
        /// A death on this frame (call before <see cref="Frame"/>, after the crowd's instances): the monster written
        /// Dying at <paramref name="crowdSlot"/> as <paramref name="body"/>, killed at song time <paramref name="time"/>
        /// by a blow along (dirX, dirY) on the ground with the sim's launch distance <paramref name="force"/>.
        /// <paramref name="serial"/> seeds its throws. False when the type's ring had no room left this frame.
        /// </summary>
        public bool Break(int type, int crowdSlot, in MonsterInstance body, int serial, double time, double dirX, double dirY, double force)
        {
            var s = _types[type];
            if (s.PendingCount == RingCapacity) return false;
            if (s.Count == RingCapacity)
            {
                s.Start = (s.Start + 1) % RingCapacity; // give up the oldest
                s.Count--;
            }
            int slot = (s.Start + s.Count) % RingCapacity;
            s.Count++;
            Breaks++;
            s.Times[slot] = time;
            double len = Math.Sqrt(dirX * dirX + dirY * dirY);
            var blow = len > 1e-9 ? new Vector2((float)(dirX / len), (float)(dirY / len)) : Vector2.zero;
            float height = s.Type.HeightM * body.Scale;
            s.Pending[s.PendingCount++] = new Death
            {
                Position = body.Position, Yaw = body.Yaw, Hit = HitPoint(body.Position, new Vector3(blow.x, 0, blow.y), height),
                Scale = body.Scale, Blow = blow, Force = (float)force, Time = (float)time, Tint = body.Tint,
                Seed = (uint)serial * 2654435761u + 0x9e37u, CrowdSlot = (uint)crowdSlot, Slot = (uint)slot,
            };
            return true;
        }

        /// <summary>
        /// Seeds this frame's deaths from the crowd's palettes (call after the crowd's Draw, which blended them),
        /// moves every live piece to song time <paramref name="now"/> and queues the draws for the camera.
        /// </summary>
        public void Frame(double now, Camera camera)
        {
            if (now < _last) Clear();
            _last = now;
            _cmd.Clear();
            int deaths = 0, pieces = 0;
            using (new ProfilingScope(_cmd, Sampler))
            {
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
                        Command(s.Type.Body, s.Count, 0);
                        Command(s.Type.Lines, s.Count, 5);
                        _cmd.SetBufferData(s.Args, _args);
                        s.DrawnCount = s.Count;
                    }
                    s.Block.SetInt(IdRingStart, s.Start);
                    s.Block.SetFloat(IdNow, (float)now);
                    deaths += s.Count;
                    pieces += s.Count * s.Pieces;
                }
            }
            Graphics.ExecuteCommandBuffer(_cmd);
            LiveDeaths = deaths;
            LivePieces = pieces;
            if (camera != null) Queue(camera);
        }

        void Command(Mesh mesh, int count, int at)
        {
            _args[at] = mesh.GetIndexCount(0);
            _args[at + 1] = (uint)count;
            _args[at + 2] = mesh.GetIndexStart(0);
            _args[at + 3] = mesh.GetBaseVertex(0);
            _args[at + 4] = 0;
        }

        void Seed(int type, TypeState s)
        {
            int n = s.PendingCount;
            s.PendingCount = 0;
            _cmd.SetBufferData(s.Seeds, s.Pending, 0, 0, n);
            _cmd.SetComputeIntParam(_cs, IdSeedCount, n);
            _cmd.SetComputeIntParam(_cs, IdBoneCount, s.Bones);
            _cmd.SetComputeIntParam(_cs, IdPieceCount, s.Pieces);
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
            _cmd.SetComputeBufferParam(_cs, _kSeedShards, IdPieceRest, s.Rest);
            _cmd.DispatchCompute(_cs, _kSeedShards, Groups(n * s.Pieces), 1, 1);
        }

        void Integrate(TypeState s, double now)
        {
            _cmd.SetComputeIntParam(_cs, IdPieceCount, s.Pieces);
            _cmd.SetComputeIntParam(_cs, IdRingStart, s.Start);
            _cmd.SetComputeIntParam(_cs, IdRingCount, s.Count);
            _cmd.SetComputeIntParam(_cs, IdRingCapacity, RingCapacity);
            _cmd.SetComputeFloatParam(_cs, IdNow, (float)now);
            _cmd.SetComputeBufferParam(_cs, _kIntegrate, IdDeaths, s.Deaths);
            _cmd.SetComputeBufferParam(_cs, _kIntegrate, IdShards, s.Shards);
            _cmd.DispatchCompute(_cs, _kIntegrate, Groups(s.Count * s.Pieces), 1, 1);
        }

        static int Groups(int threads) => (threads + 63) / 64;

        /// <summary>Queues the live pieces' draws for the camera again (a second draw of the same frame).</summary>
        public void Queue(Camera camera)
        {
            var bounds = new Bounds(Vector3.zero, Vector3.one * 100000f);
            foreach (var s in _types)
            {
                if (s.Count == 0) continue;
                Graphics.RenderMeshIndirect(EdgeCrowd.Params(s.Body, s.Block, bounds, camera), s.Type.Body, s.Args, 1, 0);
                Graphics.RenderMeshIndirect(EdgeCrowd.Params(s.Lines, s.Block, bounds, camera), s.Type.Lines, s.Args, 1, 1);
            }
        }

        /// <summary>One piece's state as the GPU keeps it (EdgeShards.hlsl's Shard), for the tests.</summary>
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

        /// <summary>The type's piece records, [ring slot][piece], read back from the GPU (for the tests).</summary>
        internal ShardRecord[] ReadShards(int type)
        {
            var s = _types[type];
            var o = new ShardRecord[RingCapacity * s.Pieces];
            s.Shards.GetData(o);
            return o;
        }

        /// <summary>Drops every live and pending death (a seek backwards does it itself).</summary>
        public void Clear()
        {
            foreach (var s in _types) s.Start = s.Count = s.PendingCount = 0;
            _last = double.NegativeInfinity;
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
                EdgeCrowd.Destroy(s.Body);
                EdgeCrowd.Destroy(s.Lines);
            }
        }
    }
}
