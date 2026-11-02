using System;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;

namespace Champions.Render.Crowd
{
    /// <summary>
    /// The GPU-driven crowd (plan.md, Rendering &gt; Crowd): the frozen look package's BoneTextureCrowd, which drew the
    /// neon edge trailer's horde, ported by review without its shadows (the game draws none). Each frame the presenter
    /// writes every type's instances straight into a GraphicsBuffer (LockBufferForWrite), in stable slots, and may write
    /// their motion clocks beside them (<see cref="Motion"/>); <see cref="Draw"/> unlocks them, runs the culling pass (the
    /// frustum, 8 depth bins, LOD by projected height with a dithered crossfade: LOD1 below 60 px, LOD2 below 30, as the
    /// trailer's) and the palette pass (each visible monster's bones blended once), then queues one indirect draw per
    /// type, LOD and bin in the edge look (<see cref="EdgeLook.Body"/>). A Dying instance (its death's frame) is not
    /// drawn, but its palette is blended wherever it is, for <see cref="MonsterShatter"/> to seed its shards from. The
    /// instance buffers form a ring longer than the frames in flight, so the CPU never writes one the GPU may still read;
    /// the previous frame's copy tells the culling pass when a slot changed owner.
    /// </summary>
    public sealed class MonsterCrowd : IDisposable
    {
        const int Bins = 8;
        const int ArgsInts = 5;
        const int Lods = 3;

        /// <summary>The layer the crowd draws on, so a camera can draw it alone.</summary>
        public const int Layer = 30;

        /// <summary>The GPU timings' marker (a development player records them).</summary>
        public static readonly ProfilingSampler Sampler = new ProfilingSampler("MonsterCrowd.Cull+Palette");

        static readonly int IdInstances = Shader.PropertyToID("_Instances");
        static readonly int IdPrevInstances = Shader.PropertyToID("_PrevInstances");
        static readonly int IdVisibleList = Shader.PropertyToID("_VisibleList");
        static readonly int IdLodState = Shader.PropertyToID("_LodState");
        static readonly int IdClipTable = Shader.PropertyToID("_ClipTable");
        static readonly int IdBoneTex = Shader.PropertyToID("_BoneTex");
        static readonly int IdPrevCount = Shader.PropertyToID("_PrevCount");
        static readonly int IdBoneCount = Shader.PropertyToID("_BoneCount");
        static readonly int IdLodIndex = Shader.PropertyToID("_LodIndex");
        static readonly int IdCrowdTime = Shader.PropertyToID("_CrowdTime");
        static readonly int IdPalette = Shader.PropertyToID("_Palette");
        static readonly int IdArgs = Shader.PropertyToID("_Args");
        static readonly int IdVisibleListOut = Shader.PropertyToID("_VisibleListOut");
        static readonly int IdLodStateRW = Shader.PropertyToID("_LodStateRW");
        static readonly int IdVisibleFlags = Shader.PropertyToID("_VisibleFlags");
        static readonly int IdCounters = Shader.PropertyToID("_Counters");
        static readonly int IdFrustumPlanes = Shader.PropertyToID("_FrustumPlanes");
        static readonly int IdCamPos = Shader.PropertyToID("_CamPos");
        static readonly int IdCamForward = Shader.PropertyToID("_CamForward");
        static readonly int IdBinNear = Shader.PropertyToID("_BinNear");
        static readonly int IdBinFar = Shader.PropertyToID("_BinFar");
        static readonly int IdPixelScale = Shader.PropertyToID("_PixelScale");
        static readonly int IdLodThresholds = Shader.PropertyToID("_LodThresholds");
        static readonly int IdLodCount = Shader.PropertyToID("_LodCount");
        static readonly int IdCount = Shader.PropertyToID("_Count");
        static readonly int IdCapacity = Shader.PropertyToID("_Capacity");
        static readonly int IdCommands = Shader.PropertyToID("_Commands");
        static readonly int IdClipBounds = Shader.PropertyToID("_ClipBounds");
        static readonly int IdPaletteOut = Shader.PropertyToID("_PaletteOut");
        static readonly int IdClipRate = Shader.PropertyToID("_ClipRate");
        static readonly int IdMotion = Shader.PropertyToID("_Motion");
        static readonly int IdMotionCount = Shader.PropertyToID("_MotionCount");

        /// <summary>LOD thresholds in projected pixels: LOD1 below x, LOD2 below y (the trailer's crowd's).</summary>
        static readonly Vector4 LodThresholds = new Vector4(60, 30, 0, 0);

        /// <summary>The latest count of monsters on screen, read back from the GPU (a frame or two late).</summary>
        public int OnScreen { get; private set; }

        /// <summary>Monsters written this frame, across every type.</summary>
        public int Written { get; private set; }

        public readonly MonsterLibrary Library;
        readonly ComputeShader _cull, _palette;
        readonly int _kClear, _kCull, _kBlend;
        readonly Vector4[] _planes = new Vector4[6];
        readonly Plane[] _planeScratch = new Plane[6];
        readonly TypeState[] _types;
        readonly int _capacity;
        readonly CommandBuffer _cmd = new CommandBuffer { name = "MonsterCrowd" };
        uint _frame = 1;
        readonly int _ring = Mathf.Max(2, QualitySettings.maxQueuedFrames) + 2;

        sealed class TypeState
        {
            public MonsterType Type;
            public Material Material;
            public GraphicsBuffer[] Instances; // a ring: the CPU never writes one the GPU may still read
            public GraphicsBuffer[] Motions;   // the motion clocks, in the same ring
            public GraphicsBuffer VisibleList, LodState, VisibleFlags, Counters, Args, ClipTable, ClipBounds, Palette;
            public MaterialPropertyBlock[] Main; // per LOD (a queued draw keeps its block)
            public int Count, PrevCount, MotionCount, OnScreen;
            public bool Locked, MotionLocked;
            public Action<AsyncGPUReadbackRequest> OnReadback;
        }

        /// <summary>Every type in the library, each with room for <paramref name="capacityPerType"/> monsters, in the library's look and the palette's colours.</summary>
        public MonsterCrowd(MonsterLibrary lib, int capacityPerType, Palette palette)
        {
            Library = lib;
            _capacity = capacityPerType;
            _cull = lib.Cull;
            _palette = lib.Palette;
            _kClear = _cull.FindKernel("ClearArgs");
            _kCull = _cull.FindKernel("Cull");
            _kBlend = _palette.FindKernel("Blend");
            _types = new TypeState[lib.Types.Length];
            for (int i = 0; i < _types.Length; i++) _types[i] = Create(lib.Types[i], palette);
        }

        public int TypeCount => _types.Length;

        /// <summary>
        /// The type's palette, 3 float4 rows per bone by slot, as this frame's <see cref="Draw"/> blends it for the
        /// visible slots and the dying ones (the GPU shatter's seed).
        /// </summary>
        public GraphicsBuffer Palette(int type) => _types[type].Palette;

        /// <summary>The type's code in the stills' type-ID pass (<see cref="IdPass.Monster"/>).</summary>
        public void SetIdCode(int type, int code)
        {
            foreach (var mpb in _types[type].Main) mpb.SetFloat(IdPass.Id, code);
        }

        TypeState Create(MonsterType t, Palette palette)
        {
            var s = new TypeState { Type = t };
            s.OnReadback = r =>
            {
                if (!r.hasError && r.layerDataSize >= 4) s.OnScreen = r.GetData<int>()[0];
            };
            int cap = _capacity;
            s.Instances = new GraphicsBuffer[_ring];
            s.Motions = new GraphicsBuffer[_ring];
            for (int i = 0; i < _ring; i++)
            {
                s.Instances[i] = new GraphicsBuffer(GraphicsBuffer.Target.Structured, GraphicsBuffer.UsageFlags.LockBufferForWrite,
                    cap, MonsterInstance.Stride) { name = t.Id + "_instances" + i };
                s.Motions[i] = new GraphicsBuffer(GraphicsBuffer.Target.Structured, GraphicsBuffer.UsageFlags.LockBufferForWrite,
                    cap, MonsterMotion.Stride) { name = t.Id + "_motion" + i };
            }
            s.VisibleList = new GraphicsBuffer(GraphicsBuffer.Target.Structured, cap * Bins * Lods, 4) { name = t.Id + "_visible" };
            s.LodState = new GraphicsBuffer(GraphicsBuffer.Target.Structured, cap, 16) { name = t.Id + "_lodState" };
            s.LodState.SetData(new uint[cap * 4]);
            s.VisibleFlags = new GraphicsBuffer(GraphicsBuffer.Target.Structured, cap, 4) { name = t.Id + "_flags" };
            s.Counters = new GraphicsBuffer(GraphicsBuffer.Target.Raw, 1, 4) { name = t.Id + "_counters" };
            s.Palette = new GraphicsBuffer(GraphicsBuffer.Target.Structured, cap * t.BoneCount * 3, 16) { name = t.Id + "_palette" };

            // Commands: [lod][bin]. Each LOD is one submesh.
            var args = new uint[Lods * Bins * ArgsInts];
            for (int l = 0; l < Lods; l++)
            {
                var d = t.Lods[l].GetSubMesh(0);
                for (int b = 0; b < Bins; b++)
                {
                    int at = (l * Bins + b) * ArgsInts;
                    args[at] = (uint)d.indexCount;
                    args[at + 2] = (uint)d.indexStart;
                    args[at + 3] = (uint)d.baseVertex;
                    args[at + 4] = (uint)((l * Bins + b) * cap);
                }
            }
            s.Args = new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments | GraphicsBuffer.Target.Raw, args.Length, 4) { name = t.Id + "_args" };
            s.Args.SetData(args);

            var clips = new Vector4[t.Clips.Length];
            var spheres = new Vector4[t.Clips.Length];
            for (int i = 0; i < clips.Length; i++)
            {
                var c = t.Clips[i];
                clips[i] = new Vector4(c.FirstRow, c.Frames, c.Length, c.Loop ? 1 : 0);
                spheres[i] = new Vector4(c.CenterY, c.Radius, c.HeightM, 0);
            }
            s.ClipTable = new GraphicsBuffer(GraphicsBuffer.Target.Structured, clips.Length, 16) { name = t.Id + "_clips" };
            s.ClipTable.SetData(clips);
            s.ClipBounds = new GraphicsBuffer(GraphicsBuffer.Target.Structured, spheres.Length, 16) { name = t.Id + "_clipBounds" };
            s.ClipBounds.SetData(spheres);

            s.Material = EdgeLook.Body(t, Library.Look, palette);
            s.Main = new MaterialPropertyBlock[Lods];
            for (int l = 0; l < Lods; l++)
            {
                var mpb = s.Main[l] = new MaterialPropertyBlock();
                mpb.SetTexture(IdBoneTex, t.Bones);
                mpb.SetBuffer(IdClipTable, s.ClipTable);
                mpb.SetBuffer(IdClipBounds, s.ClipBounds);
                mpb.SetBuffer(IdLodState, s.LodState);
                mpb.SetBuffer(IdPalette, s.Palette);
                mpb.SetBuffer(IdVisibleList, s.VisibleList);
                mpb.SetInt(IdBoneCount, t.BoneCount);
                mpb.SetFloat(IdLodIndex, l);
            }
            return s;
        }

        /// <summary>
        /// The type's instance slots for this frame, mapped GPU memory: write every field of every slot (never read it).
        /// A type not requested in a frame draws nothing that frame.
        /// </summary>
        public NativeArray<MonsterInstance> Instances(int type, int count)
        {
            var s = _types[type];
            if (count > _capacity) throw new ArgumentOutOfRangeException(nameof(count), $"capacity is {_capacity}");
            if (s.Locked) throw new InvalidOperationException("MonsterCrowd: Instances called twice for a type in one frame");
            s.Count = count;
            s.Locked = true;
            return s.Instances[_frame % _ring].LockBufferForWrite<MonsterInstance>(0, count);
        }

        /// <summary>
        /// The type's motion clocks for this frame, by the same slots as its <see cref="Instances"/> (the first
        /// <paramref name="count"/>), mapped GPU memory: write every slot (never read it). Optional: a type whose
        /// presenter doesn't ask for them in a frame draws without springs or swarm that frame.
        /// </summary>
        public NativeArray<MonsterMotion> Motion(int type, int count)
        {
            var s = _types[type];
            if (count > _capacity) throw new ArgumentOutOfRangeException(nameof(count), $"capacity is {_capacity}");
            if (s.MotionLocked) throw new InvalidOperationException("MonsterCrowd: Motion called twice for a type in one frame");
            s.MotionCount = count;
            s.MotionLocked = true;
            return s.Motions[_frame % _ring].LockBufferForWrite<MonsterMotion>(0, count);
        }

        /// <summary>
        /// Culls, blends and queues this frame's draws for the camera; <paramref name="time"/> (the song's) clocks the
        /// LOD crossfades and the swarm's wobble.
        /// </summary>
        public void Draw(Camera camera, double time)
        {
            int icur = (int)(_frame % _ring), iprev = (int)((_frame - 1) % _ring);
            GeometryUtility.CalculateFrustumPlanes(camera, _planeScratch);
            for (int i = 0; i < 6; i++)
                _planes[i] = new Vector4(_planeScratch[i].normal.x, _planeScratch[i].normal.y, _planeScratch[i].normal.z, _planeScratch[i].distance);
            var bins = BinRange(camera, 5f, 2f);
            _cmd.Clear();
            using (new ProfilingScope(_cmd, Sampler))
            {
                _cmd.SetComputeVectorArrayParam(_cull, IdFrustumPlanes, _planes);
                _cmd.SetComputeVectorParam(_cull, IdCamPos, camera.transform.position);
                _cmd.SetComputeVectorParam(_cull, IdCamForward, camera.transform.forward);
                _cmd.SetComputeFloatParam(_cull, IdBinNear, bins.x);
                _cmd.SetComputeFloatParam(_cull, IdBinFar, bins.y);
                _cmd.SetComputeFloatParam(_cull, IdPixelScale, 2f * Mathf.Tan(camera.fieldOfView * Mathf.Deg2Rad * 0.5f) / Mathf.Max(1, camera.pixelHeight));
                _cmd.SetComputeVectorParam(_cull, IdLodThresholds, LodThresholds);
                _cmd.SetComputeFloatParam(_cull, IdCrowdTime, (float)time);

                int instances = 0;
                foreach (var s in _types)
                {
                    if (s.Locked) s.Instances[icur].UnlockBufferAfterWrite<MonsterInstance>(s.Count);
                    else s.Count = 0;
                    s.Locked = false;
                    if (s.MotionLocked) s.Motions[icur].UnlockBufferAfterWrite<MonsterMotion>(s.MotionCount);
                    else s.MotionCount = 0;
                    s.MotionLocked = false;
                    instances += s.Count;
                    if (s.Count > 0)
                    {
                        Cull(s, icur, iprev);
                        Blend(s, icur);
                        foreach (var mpb in s.Main)
                        {
                            mpb.SetBuffer(IdInstances, s.Instances[icur]);
                            mpb.SetBuffer(IdMotion, s.Motions[icur]);
                            mpb.SetInt(IdMotionCount, Mathf.Min(s.MotionCount, s.Count));
                            mpb.SetFloat(IdCrowdTime, (float)time);
                        }
                    }
                    else s.OnScreen = 0;
                    s.PrevCount = s.Count;
                }
                Written = instances;
            }
            Graphics.ExecuteCommandBuffer(_cmd);
            int seen = 0;
            foreach (var s in _types)
            {
                if (s.Count > 0) AsyncGPUReadback.Request(s.Counters, s.OnReadback);
                seen += s.OnScreen;
            }
            OnScreen = seen;
            Queue(camera);
            _frame++;
        }

        /// <summary>
        /// Queues the latest culled draws for the camera again: a hand-drawn camera spends the draws queued in a frame,
        /// so a second drawing of the same frame (the stills' type-ID pass) needs them queued anew.
        /// </summary>
        public void Queue(Camera camera)
        {
            var bounds = new Bounds(Vector3.zero, Vector3.one * 100000f);
            foreach (var s in _types)
            {
                if (s.PrevCount == 0) continue;
                for (int l = 0; l < Lods; l++)
                {
                    var rp = new RenderParams(s.Material)
                    {
                        worldBounds = bounds,
                        matProps = s.Main[l],
                        shadowCastingMode = ShadowCastingMode.Off,
                        receiveShadows = false,
                        motionVectorMode = MotionVectorGenerationMode.ForceNoMotion,
                        lightProbeUsage = LightProbeUsage.Off,
                        reflectionProbeUsage = ReflectionProbeUsage.Off,
                        camera = camera,
                        layer = Layer,
                    };
                    Graphics.RenderMeshIndirect(rp, s.Type.Lods[l], s.Args, Bins, l * Bins);
                }
            }
        }

        void Cull(TypeState s, int icur, int iprev)
        {
            const int commands = Lods * Bins;
            _cmd.SetComputeIntParam(_cull, IdLodCount, Lods);
            _cmd.SetComputeIntParam(_cull, IdCount, s.Count);
            _cmd.SetComputeIntParam(_cull, IdCapacity, _capacity);
            _cmd.SetComputeIntParam(_cull, IdCommands, commands);
            _cmd.SetComputeIntParam(_cull, IdPrevCount, s.PrevCount);
            foreach (int k in new[] { _kClear, _kCull })
            {
                _cmd.SetComputeBufferParam(_cull, k, IdArgs, s.Args);
                _cmd.SetComputeBufferParam(_cull, k, IdCounters, s.Counters);
            }
            _cmd.SetComputeBufferParam(_cull, _kCull, IdInstances, s.Instances[icur]);
            _cmd.SetComputeBufferParam(_cull, _kCull, IdPrevInstances, s.Instances[iprev]);
            _cmd.SetComputeBufferParam(_cull, _kCull, IdClipBounds, s.ClipBounds);
            _cmd.SetComputeBufferParam(_cull, _kCull, IdVisibleListOut, s.VisibleList);
            _cmd.SetComputeBufferParam(_cull, _kCull, IdLodStateRW, s.LodState);
            _cmd.SetComputeBufferParam(_cull, _kCull, IdVisibleFlags, s.VisibleFlags);
            _cmd.DispatchCompute(_cull, _kClear, (commands + 63) / 64, 1, 1);
            _cmd.DispatchCompute(_cull, _kCull, (s.Count + 63) / 64, 1, 1);
        }

        void Blend(TypeState s, int icur)
        {
            _cmd.SetComputeIntParam(_palette, IdCount, s.Count);
            _cmd.SetComputeIntParam(_palette, IdBoneCount, s.Type.BoneCount);
            _cmd.SetComputeFloatParam(_palette, IdClipRate, MonsterLibrary.ClipRate);
            _cmd.SetComputeBufferParam(_palette, _kBlend, IdInstances, s.Instances[icur]);
            _cmd.SetComputeBufferParam(_palette, _kBlend, IdVisibleFlags, s.VisibleFlags);
            _cmd.SetComputeBufferParam(_palette, _kBlend, IdClipTable, s.ClipTable);
            _cmd.SetComputeTextureParam(_palette, _kBlend, IdBoneTex, s.Type.Bones);
            _cmd.SetComputeBufferParam(_palette, _kBlend, IdPaletteOut, s.Palette);
            _cmd.DispatchCompute(_palette, _kBlend, (s.Count * s.Type.BoneCount + 63) / 64, 1, 1);
        }

        /// <summary>
        /// The depth range the bins span: the camera depths where the view's corner rays meet the ground and a plane
        /// <paramref name="top"/> metres above it, padded by <paramref name="pad"/>.
        /// </summary>
        static Vector2 BinRange(Camera camera, float top, float pad)
        {
            var t = camera.transform;
            float near = float.MaxValue, far = 0;
            for (int i = 0; i < 4; i++)
            {
                var ray = camera.ViewportPointToRay(new Vector3(i & 1, i >> 1, 0));
                for (int k = 0; k < 2; k++)
                {
                    float h = k == 0 ? 0f : top;
                    float depth;
                    if (ray.direction.y < -1e-4f && ray.origin.y > h)
                    {
                        var hit = ray.origin + ray.direction * ((ray.origin.y - h) / -ray.direction.y);
                        depth = Vector3.Dot(hit - t.position, t.forward);
                    }
                    else depth = camera.farClipPlane;
                    near = Mathf.Min(near, depth);
                    far = Mathf.Max(far, depth);
                }
            }
            return new Vector2(Mathf.Max(camera.nearClipPlane, near - pad), Mathf.Min(camera.farClipPlane, far + pad));
        }

        public void Dispose()
        {
            _cmd.Release();
            foreach (var s in _types)
            {
                if (s.Locked) s.Instances[_frame % _ring].UnlockBufferAfterWrite<MonsterInstance>(0);
                if (s.MotionLocked) s.Motions[_frame % _ring].UnlockBufferAfterWrite<MonsterMotion>(0);
                foreach (var b in s.Instances) b?.Dispose();
                foreach (var b in s.Motions) b?.Dispose();
                s.VisibleList?.Dispose();
                s.LodState?.Dispose();
                s.VisibleFlags?.Dispose();
                s.Counters?.Dispose();
                s.Args?.Dispose();
                s.ClipTable?.Dispose();
                s.ClipBounds?.Dispose();
                s.Palette?.Dispose();
                Destroy(s.Material);
            }
        }

        internal static void Destroy(UnityEngine.Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(o);
            else UnityEngine.Object.DestroyImmediate(o); // the EditMode tests and reviews
        }
    }
}
