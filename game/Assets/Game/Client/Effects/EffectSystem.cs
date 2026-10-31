using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace Game.Client.Effects
{
    /// <summary>
    /// The GPU effect runtime (tech-demo-3d/effects.md, The runtime): one particle pool for every effect type.
    /// A frame's spawns are expanded on the CPU into emitter instances (definition, transform,
    /// seed, emission segments) and go up in one upload; the emit, simulate and compact kernels,
    /// the back-to-front sort of alpha-blended billboards and the indirect draws (from a render
    /// pass after opaques) do the rest. A spawn that would overflow the pool is rejected whole and
    /// counted. Light cues go to <see cref="EffectLights"/>.
    /// </summary>
    public sealed class EffectSystem : IDisposable
    {
        public const int DefaultPool = 1 << 18, InstanceRing = 1 << 16, MaxPerInstance = 8192;
        const int InstanceFloat4s = 9, ParticleStride = 112;

        /// <summary>One spawn: an effect at a world transform and time.</summary>
        public struct Spawn
        {
            public int Effect;
            public Vector3 Position, Velocity;
            public float Yaw, Scale, YawRate, Duration; // Duration <= 0: the effect's own
            public uint Seed;
            public double Time;
            /// <summary>Alpha above 0: the spawn's own colour for the heat ramp (its edge and mid) and its light cue, at that weight (a crystal death's gem colour).</summary>
            public Color Tint;
            /// <summary>Above 0: the light cue's intensity scale (the crystal releases sharing one beat); 0 is the cue as authored.</summary>
            public float LightGain;
        }

        public readonly EffectLibrary Library;
        public readonly EffectLights Lights;
        public readonly int PoolSize;
        readonly int _buckets, _meshBuckets;
        readonly GraphicsBuffer _particles, _dead, _aliveA, _aliveB, _counters, _args, _instances, _frameInst, _drawList, _sortList, _emitters, _meshVerts;
        readonly ComputeShader _sim, _sort;
        readonly int _kInit, _kBegin, _kAlloc, _kEmit, _kFinish, _kSim, _kPost, _kPad, _kLocalSort, _kLocalMerge, _kGlobal;
        readonly Material _material;
        readonly MaterialPropertyBlock[] _blocks;
        readonly CommandBuffer _cmd = new CommandBuffer { name = "Game effects" };
        readonly EffectPass _pass;
        /// <summary>The GPU's Instance record (EffectCommon.hlsl), 144 bytes.</summary>
        struct InstanceData { public Vector4 PosYaw, VelScale, Timing, Seg0, Seg1, Seg2, Seg3, Alloc, Tint; }
        struct FrameInst { public uint Ring, First, Count, Pad; }
        const int MaxFrameInstances = 1 << 14;
        readonly InstanceData[] _instData = new InstanceData[InstanceRing];
        readonly FrameInst[] _frameData = new FrameInst[MaxFrameInstances];
        int _frameCount;
        int _ringHead, _frameInstCount, _frameNewParticles, _pendingFrom, _pendingCount;
        bool _aliveFlip;
        double _origin;
        int _sortSize = 2048;
        readonly HashSet<Camera> _cameras = new HashSet<Camera>();

        // Counters (benchmarks.md, Measurements), from the GPU's readback a few frames late.
        public int Particles { get; private set; }
        /// <summary>Spawns rejected whole (CPU: a frame's instance list full; GPU: the pool full), excused reset frames apart.</summary>
        public long RejectedTotal => Rejections.Total;
        public readonly RejectionLedger Rejections = new RejectionLedger();
        bool _excuseFrame;
        readonly Queue<(int epoch, bool excused)> _readbacks = new Queue<(int, bool)>();
        public int SpawnsThisFrame { get; private set; }
        public long SpawnsTotal { get; private set; }
        public int AlphaCount { get; private set; }
        /// <summary>Particles requested by spawns (CPU) and emitted on the GPU (readback), since the last reset.</summary>
        public long ParticlesRequested { get; private set; }
        public long ParticlesEmitted { get; private set; }
        public int InstancesThisFrame => _frameInstCount;
        public EffectSystem(EffectLibrary library, int poolSize = DefaultPool, int lightPool = EffectLights.DefaultPool)
        {
            Library = library;
            PoolSize = poolSize;
            _meshBuckets = library.Meshes.Length;
            _buckets = 2 + 2 * _meshBuckets;
            if (_buckets > 24) throw new InvalidOperationException("EffectSystem: too many particle meshes (" + _meshBuckets + ")");
            var structured = GraphicsBuffer.Target.Structured;
            _particles = new GraphicsBuffer(structured, poolSize, ParticleStride) { name = "Effect particles" };
            _dead = new GraphicsBuffer(structured, poolSize, 4) { name = "Effect dead list" };
            _aliveA = new GraphicsBuffer(structured, poolSize, 4) { name = "Effect alive A" };
            _aliveB = new GraphicsBuffer(structured, poolSize, 4) { name = "Effect alive B" };
            _counters = new GraphicsBuffer(structured, 32, 4) { name = "Effect counters" };
            _args = new GraphicsBuffer(GraphicsBuffer.Target.Structured | GraphicsBuffer.Target.IndirectArguments, 4 + 4 * _buckets, 4) { name = "Effect args" };
            _instances = new GraphicsBuffer(structured, InstanceRing, InstanceFloat4s * 16) { name = "Effect instances" };
            _frameInst = new GraphicsBuffer(structured, MaxFrameInstances, 16) { name = "Effect frame instances" };
            _drawList = new GraphicsBuffer(structured, poolSize * _buckets, 4) { name = "Effect draw lists" };
            _sortList = new GraphicsBuffer(structured, poolSize, 8) { name = "Effect sort list" };
            _emitters = new GraphicsBuffer(structured, Math.Max(1, library.Emitters.Length), 16) { name = "Effect emitters" };
            if (library.Emitters.Length > 0) _emitters.SetData(library.Emitters);
            _meshVerts = new GraphicsBuffer(structured, Math.Max(3, library.MeshVertices.Length), 16) { name = "Effect mesh vertices" };
            if (library.MeshVertices.Length > 0) _meshVerts.SetData(library.MeshVertices);

            var args = new uint[4 + 4 * _buckets];
            for (int b = 0; b < _buckets; b++)
                args[4 + b * 4] = b < 2 ? 6u : (uint)library.Meshes[(b - 2) / 2].VertexCount;
            _args.SetData(args);

            _sim = library.Simulate;
            _sort = library.Sort;
            _kInit = _sim.FindKernel("Init");
            _kBegin = _sim.FindKernel("Begin");
            _kAlloc = _sim.FindKernel("Allocate");
            _kEmit = _sim.FindKernel("Emit");
            _kFinish = _sim.FindKernel("FinishEmit");
            _kSim = _sim.FindKernel("Simulate");
            _kPost = _sim.FindKernel("PostSim");
            _kPad = _sim.FindKernel("Pad");
            _kLocalSort = _sort.FindKernel("LocalSort");
            _kLocalMerge = _sort.FindKernel("LocalMerge");
            _kGlobal = _sort.FindKernel("GlobalStep");

            _material = new Material(library.Draw) { name = "Effect particles" };
            _material.SetBuffer("_Particles", _particles);
            _material.SetBuffer("_Instances", _instances);
            _material.SetBuffer("_DrawList", _drawList);
            _material.SetBuffer("_SortList", _sortList);
            _material.SetBuffer("_MeshVerts", _meshVerts);
            _material.SetBuffer("_Emitters", _emitters);
            _material.SetTexture("_Lut", library.Lut);
            _material.SetTexture("_Textures", library.Textures);
            _material.SetInt("_BucketCap", poolSize);
            _blocks = new MaterialPropertyBlock[_buckets];
            for (int b = 0; b < _buckets; b++)
            {
                _blocks[b] = new MaterialPropertyBlock();
                _blocks[b].SetInt("_Bucket", b);
                _blocks[b].SetInt("_UseSort", b == 0 ? 1 : 0);
                _blocks[b].SetInt("_MeshFirst", b < 2 ? 0 : library.Meshes[(b - 2) / 2].FirstVertex);
            }

            Lights = new EffectLights(lightPool);
            _pass = new EffectPass(this);
            Reset(0);
            RenderPipelineManager.beginCameraRendering += OnBeginCamera;
        }

        /// <summary>Draw for this camera (the game or bench camera; captures use the same one).</summary>
        public void AddCamera(Camera camera) => _cameras.Add(camera);
        public void RemoveCamera(Camera camera) => _cameras.Remove(camera);

        /// <summary>Clears every particle and cue (seeks, rebuilds); <paramref name="origin"/> is the new time zero.</summary>
        public void Reset(double origin)
        {
            _origin = origin;
            _ringHead = 0;
            _frameCount = 0;
            _frameInstCount = _frameNewParticles = 0;
            _pendingCount = 0;
            Lights.Clear();
            // Readbacks issued before this reset describe the old pool; the re-spawn that follows
            // is excused from the pass rule (its overflow is logged, not counted).
            Rejections.NewEpoch();
            _excuseFrame = true;
            SetCommon(_cmd);
            _cmd.Clear();
            BindSim(_cmd, _kInit);
            _cmd.SetComputeIntParam(_sim, "_PoolSize", PoolSize);
            _cmd.DispatchCompute(_sim, _kInit, (PoolSize + 255) / 256, 1, 1);
            Graphics.ExecuteCommandBuffer(_cmd);
            _cmd.Clear();
            Particles = 0;
        }

        /// <summary>Queues one spawn for the next <see cref="Update"/>; false if the effect index is unknown.</summary>
        public bool Add(in Spawn s)
        {
            if (s.Effect < 0 || s.Effect >= Library.Effects.Length) return false;
            var fx = Library.Effects[s.Effect];
            SpawnsThisFrame++;
            SpawnsTotal++;
            float scale = s.Scale / Mathf.Max(1e-4f, fx.ScaleRef);
            // Lights take every spawn: their clustering already merges coincident cues.
            if (fx.Light.Active)
            {
                var cue = fx.Light;
                if (s.Tint.a > 0) cue.Color = Color.Lerp(cue.Color, new Color(s.Tint.r, s.Tint.g, s.Tint.b), s.Tint.a);
                if (s.LightGain > 0) cue.Intensity *= s.LightGain;
                Lights.Add(s.Position + Vector3.up * (cue.Height * scale), cue, s.Time, scale, s.Velocity);
            }
            Expand(s);
            return true;
        }

        /// <summary>A spawn's emitter instances.</summary>
        void Expand(in Spawn s)
        {
            var fx = Library.Effects[s.Effect];
            float scale = s.Scale / Mathf.Max(1e-4f, fx.ScaleRef);
            // A spawn is rejected whole: all its emitter instances fit this frame's list, or none go.
            if (_frameCount + fx.EmitterCount > MaxFrameInstances) { Rejections.AddCpu(1, _excuseFrame); return; }
            int leader = _frameCount;
            for (int k = 0; k < fx.EmitterCount; k++)
            {
                int e = fx.FirstEmitter + k;
                uint seed = Hash(s.Seed, (uint)Library.Emission[e].SeedEmitter);
                int count = Segments(Library.Emission[e], s, fx, seed, _seg);
                if (count == 0) continue;
                int ring = _ringHead;
                _ringHead = (_ringHead + 1) % InstanceRing;
                if (_pendingCount == 0) _pendingFrom = ring;
                _pendingCount++;
                _instData[ring] = new InstanceData
                {
                    PosYaw = new Vector4(s.Position.x, s.Position.y, s.Position.z, s.Yaw),
                    VelScale = new Vector4(s.Velocity.x, s.Velocity.y, s.Velocity.z, scale),
                    Timing = new Vector4((float)(s.Time - _origin), s.YawRate, BitConverter.Int32BitsToSingle(e), BitConverter.Int32BitsToSingle((int)seed)),
                    Seg0 = _seg[0], Seg1 = _seg[1], Seg2 = _seg[2], Seg3 = _seg[3],
                    Alloc = new Vector4(BitConverter.Int32BitsToSingle(count), 1, 0, s.Duration > 0 ? s.Duration : fx.Duration),
                    Tint = s.Tint,
                };
                _frameData[_frameCount++] = new FrameInst { Ring = (uint)ring, First = (uint)_frameNewParticles, Count = (uint)count };
                _frameNewParticles += count;
                ParticlesRequested += count;
            }
            // The leader carries the spawn's instance count: the GPU allocates the spawn in one go.
            if (_frameCount > leader) _frameData[leader].Pad = (uint)(_frameCount - leader);
        }

        readonly Vector4[] _seg = new Vector4[4];

        /// <summary>
        /// The emitter's particle count for one spawn and its emission segments: (first particle,
        /// particles per step, first birth in s after the spawn, step in s). Bursts, rate over time
        /// and rate over distance (from the spawn's velocity). Deterministic in the seed.
        /// </summary>
        public static int Segments(in EmitterEmission em, in Spawn s, EffectEntry fx, uint seed, Vector4[] seg)
        {
            for (int i = 0; i < 4; i++) seg[i] = Vector4.zero;
            float delay = Mathf.Lerp(em.DelayMin, em.DelayMax, Rand(seed, 1));
            float total = s.Duration > 0 ? s.Duration : em.Loop ? Mathf.Max(em.Duration, fx.Duration) : em.Duration;
            int n = 0, used = 0;
            int loops = em.Loop ? Mathf.Max(1, Mathf.CeilToInt(total / Mathf.Max(1e-3f, em.Duration) - 1e-4f)) : 1;
            if (em.Bursts != null)
                for (int b = 0; b < em.Bursts.Length && used < 4; b++)
                {
                    var burst = em.Bursts[b];
                    if (burst.Time > em.Duration + 1e-4f) continue;
                    if (burst.Probability < 1 && Rand(seed, 10 + (uint)b) >= burst.Probability) continue;
                    int per = Mathf.RoundToInt(Mathf.Lerp(burst.CountMin, burst.CountMax, Rand(seed, 20 + (uint)b)));
                    if (per <= 0) continue;
                    int cycles = Mathf.Max(1, burst.Cycles);
                    float step = cycles > 1 ? Mathf.Max(1e-3f, burst.Interval) : em.Duration;
                    int steps = cycles > 1 ? cycles * (em.Loop ? loops : 1) : loops;
                    if (cycles > 1 && em.Loop) step = burst.Interval; // approximation: cycles continue across loops
                    seg[used++] = new Vector4(n, per, delay + burst.Time, step);
                    n += per * steps;
                }
            float rate = em.RateOverTime;
            if (em.RateOverDistanceMax > 0) rate += Mathf.Lerp(em.RateOverDistanceMin, em.RateOverDistanceMax, Rand(seed, 30)) * s.Velocity.magnitude;
            if (rate > 0 && used < 4)
            {
                int count = Mathf.FloorToInt(rate * total + Rand(seed, 31));
                // Shuriken caps live particles at maxParticles; rate emission past it is dropped.
                int cap = em.MaxParticles > 0 && em.LifeMax > 0 ? Mathf.CeilToInt(em.MaxParticles * Mathf.Max(1f, total / em.LifeMax)) : count;
                count = Mathf.Min(count, cap);
                if (count > 0)
                {
                    seg[used++] = new Vector4(n, 1, delay + 0.5f / rate, 1f / rate);
                    n += count;
                }
            }
            return Mathf.Min(n, MaxPerInstance);
        }

        public static uint Hash(uint a, uint b)
        {
            uint h = a * 0x9E3779B1u ^ (b + 0x7F4A7C15u);
            h ^= h >> 16; h *= 0x85EBCA6Bu; h ^= h >> 13; h *= 0xC2B2AE35u; h ^= h >> 16;
            return h;
        }

        public static float Rand(uint seed, uint salt) => (Hash(seed, salt) >> 8) * (1f / 16777216f);

        /// <summary>Runs this frame's emit, simulate and sort on the GPU, and the light pool, for song time <paramref name="now"/>.</summary>
        public void Update(double now, float dt, Camera camera)
        {
            Lights.Update(now, camera);
            _frameInstCount = _frameCount;
            if (_pendingCount > 0)
            {
                // The ring's new instances: one upload, or two where it wraps.
                int first = _pendingFrom, count = _pendingCount;
                int head = Math.Min(count, InstanceRing - first);
                _instances.SetData(_instData, first, first, head);
                if (count > head) _instances.SetData(_instData, 0, 0, count - head);
            }
            if (_frameInstCount > 0) _frameInst.SetData(_frameData, 0, 0, _frameInstCount);
            var cmd = _cmd;
            cmd.Clear();
            SetCommon(cmd);
            SetCamera(cmd, camera);
            cmd.SetComputeFloatParam(_sim, "_Now", (float)(now - _origin));
            cmd.SetComputeFloatParam(_sim, "_DeltaTime", Mathf.Max(0, dt));
            cmd.SetComputeIntParam(_sim, "_FrameInstCount", _frameInstCount);
            cmd.SetComputeIntParam(_sim, "_NewParticles", _frameNewParticles);
            int sortSize = _sortSize;
            cmd.SetComputeIntParam(_sim, "_SortSize", sortSize);
            var aliveIn = _aliveFlip ? _aliveB : _aliveA;
            var aliveOut = _aliveFlip ? _aliveA : _aliveB;
            Dispatch(cmd, _kBegin, 1, aliveIn, aliveOut);
            if (_frameInstCount > 0)
            {
                Dispatch(cmd, _kAlloc, (_frameInstCount + 63) / 64, aliveIn, aliveOut);
                Dispatch(cmd, _kEmit, (_frameNewParticles + 63) / 64, aliveIn, aliveOut);
            }
            Dispatch(cmd, _kFinish, 1, aliveIn, aliveOut);
            BindSim(cmd, _kSim, aliveIn, aliveOut);
            cmd.DispatchCompute(_sim, _kSim, _args, 0);
            Dispatch(cmd, _kPost, 1, aliveIn, aliveOut);
            Dispatch(cmd, _kPad, (sortSize + 255) / 256, aliveIn, aliveOut);
            SortAlpha(cmd, sortSize);
            _readbacks.Enqueue((Rejections.Epoch, _excuseFrame));
            cmd.RequestAsyncReadback(_counters, OnCounters);
            Graphics.ExecuteCommandBuffer(cmd);
            cmd.Clear();
            _aliveFlip = !_aliveFlip;
            _material.SetFloat("_Now", (float)(now - _origin));
            // The draw reads the sorted list only below the size this frame sorted (the rest draw unsorted).
            _material.SetInt("_SortSize", sortSize);
            if (_excuseFrame && Rejections.ExcusedCpuThisReset > 0)
                Debug.LogWarning($"EffectSystem: {Rejections.ExcusedCpuThisReset} spawns over the frame's instance list in a reset frame (excused from the pass rule)");
            _excuseFrame = false;
            _frameCount = 0;
            _frameNewParticles = 0;
            _pendingCount = 0;
        }

        /// <summary>Starts the next frame's counts (call before this frame's spawns).</summary>
        public void BeginFrame() => SpawnsThisFrame = 0;

        /// <summary>Rejected spawns since the last call.</summary>
        public int TakeRejected() => Rejections.Take();

        void OnCounters(AsyncGPUReadbackRequest req)
        {
            var (epoch, excused) = _readbacks.Count > 0 ? _readbacks.Dequeue() : (-1, false);
            if (req.hasError || _disposed || epoch != Rejections.Epoch) return; // issued before the last Reset
            var d = req.GetData<uint>();
            Particles = (int)d[0];
            if (d[3] > 0)
            {
                Rejections.AddGpu(epoch, (int)d[3], excused);
                if (excused) Debug.LogWarning($"EffectSystem: {d[3]} spawns over the pool in a reset frame (excused from the pass rule)");
            }
            AlphaCount = (int)d[8];
            ParticlesEmitted += d[5];
            // The next sorts cover what was alive, with room to grow.
            int want = Mathf.NextPowerOfTwo(Mathf.Max(2048, (int)(AlphaCount * 1.5f) + 1));
            _sortSize = Math.Min(want, Mathf.ClosestPowerOfTwo(PoolSize));
        }

        void SortAlpha(CommandBuffer cmd, int n)
        {
            cmd.SetComputeIntParam(_sort, "_SortSize", n);
            cmd.SetComputeBufferParam(_sort, _kLocalSort, "_SortList", _sortList);
            cmd.SetComputeBufferParam(_sort, _kLocalMerge, "_SortList", _sortList);
            cmd.SetComputeBufferParam(_sort, _kGlobal, "_SortList", _sortList);
            cmd.DispatchCompute(_sort, _kLocalSort, n / 2048, 1, 1);
            for (int k = 4096; k <= n; k <<= 1)
            {
                cmd.SetComputeIntParam(_sort, "_K", k);
                for (int j = k >> 1; j >= 2048; j >>= 1)
                {
                    cmd.SetComputeIntParam(_sort, "_J", j);
                    cmd.DispatchCompute(_sort, _kGlobal, (n / 2 + 255) / 256, 1, 1);
                }
                cmd.DispatchCompute(_sort, _kLocalMerge, n / 2048, 1, 1);
            }
        }

        void SetCommon(CommandBuffer cmd)
        {
            cmd.SetComputeIntParam(_sim, "_PoolSize", PoolSize);
            cmd.SetComputeIntParam(_sim, "_BucketCap", PoolSize);
            cmd.SetComputeIntParam(_sim, "_Buckets", _buckets);
            cmd.SetComputeIntParam(_sim, "_MeshBuckets", _meshBuckets);
        }

        void SetCamera(CommandBuffer cmd, Camera cam)
        {
            if (cam == null) return;
            var tr = cam.transform;
            cmd.SetComputeVectorParam(_sim, "_CamPos", tr.position);
            cmd.SetComputeVectorParam(_sim, "_CamFwd", tr.forward);
            cmd.SetGlobalVector("_EffCamPos", tr.position);
            cmd.SetGlobalVector("_EffCamRight", tr.right);
            cmd.SetGlobalVector("_EffCamUp", tr.up);
            var f = tr.forward;
            cmd.SetGlobalVector("_EffCamFwd", new Vector4(f.x, f.y, f.z, Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad)));
        }

        void Dispatch(CommandBuffer cmd, int kernel, int groups, GraphicsBuffer aliveIn, GraphicsBuffer aliveOut)
        {
            if (groups <= 0) return;
            BindSim(cmd, kernel, aliveIn, aliveOut);
            cmd.DispatchCompute(_sim, kernel, groups, 1, 1);
        }

        void BindSim(CommandBuffer cmd, int kernel, GraphicsBuffer aliveIn = null, GraphicsBuffer aliveOut = null)
        {
            cmd.SetComputeBufferParam(_sim, kernel, "_Particles", _particles);
            cmd.SetComputeBufferParam(_sim, kernel, "_Dead", _dead);
            cmd.SetComputeBufferParam(_sim, kernel, "_AliveIn", aliveIn ?? _aliveA);
            cmd.SetComputeBufferParam(_sim, kernel, "_AliveOut", aliveOut ?? _aliveB);
            cmd.SetComputeBufferParam(_sim, kernel, "_Counters", _counters);
            cmd.SetComputeBufferParam(_sim, kernel, "_Args", _args);
            cmd.SetComputeBufferParam(_sim, kernel, "_Instances", _instances);
            cmd.SetComputeBufferParam(_sim, kernel, "_FrameInst", _frameInst);
            cmd.SetComputeBufferParam(_sim, kernel, "_DrawList", _drawList);
            cmd.SetComputeBufferParam(_sim, kernel, "_SortList", _sortList);
            cmd.SetComputeBufferParam(_sim, kernel, "_Emitters", _emitters);
            cmd.SetComputeTextureParam(_sim, kernel, "_Lut", Library.Lut);
        }

        /// <summary>Issues every bucket's indirect draw: alpha billboards (sorted), alpha meshes, then additive ones.</summary>
        internal void Draw(RasterCommandBuffer cmd)
        {
            foreach (int b in DrawOrder())
                cmd.DrawProceduralIndirect(Matrix4x4.identity, _material, 0, MeshTopology.Triangles, _args, (4 + b * 4) * 4, _blocks[b]);
        }

        IEnumerable<int> DrawOrder()
        {
            yield return 0;
            for (int m = 0; m < _meshBuckets; m++) yield return 2 + 2 * m;
            yield return 1;
            for (int m = 0; m < _meshBuckets; m++) yield return 3 + 2 * m;
        }

        void OnBeginCamera(ScriptableRenderContext context, Camera camera)
        {
            if (!_cameras.Contains(camera)) return;
            var data = camera.GetUniversalAdditionalCameraData();
            data?.scriptableRenderer?.EnqueuePass(_pass);
        }

        bool _disposed;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            RenderPipelineManager.beginCameraRendering -= OnBeginCamera;
            AsyncGPUReadback.WaitAllRequests();
            foreach (var b in new[] { _particles, _dead, _aliveA, _aliveB, _counters, _args, _instances, _frameInst, _drawList, _sortList, _emitters, _meshVerts })
                b?.Release();
            _cmd.Release();
            Lights.Dispose();
            if (_material != null) { if (Application.isPlaying) UnityEngine.Object.Destroy(_material); else UnityEngine.Object.DestroyImmediate(_material); }
        }

        /// <summary>
        /// Draws the pool after opaques (and after the depth copy, for soft particles). Enqueued per
        /// camera from beginCameraRendering, the same as a renderer feature's AddRenderPasses, so
        /// the renderer asset is unchanged.
        /// </summary>
        sealed class EffectPass : ScriptableRenderPass
        {
            readonly EffectSystem _system;

            sealed class PassData { public EffectSystem System; }

            public EffectPass(EffectSystem system)
            {
                _system = system;
                renderPassEvent = RenderPassEvent.BeforeRenderingTransparents;
                ConfigureInput(ScriptableRenderPassInput.Depth);
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                var resources = frameData.Get<UniversalResourceData>();
                using var builder = renderGraph.AddRasterRenderPass<PassData>("Game effects", out var data);
                data.System = _system;
                builder.SetRenderAttachment(resources.activeColorTexture, 0);
                builder.SetRenderAttachmentDepth(resources.activeDepthTexture, AccessFlags.Read);
                if (resources.cameraDepthTexture.IsValid()) builder.UseTexture(resources.cameraDepthTexture);
                builder.AllowPassCulling(false);
                builder.SetRenderFunc((PassData d, RasterGraphContext ctx) => d.System.Draw(ctx.cmd));
            }
        }
    }
}
