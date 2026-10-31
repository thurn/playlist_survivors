using System;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;

namespace Game.Client.Crowd
{
    /// <summary>
    /// The GPU-driven crowd (unity-game.md, The Crowd): demo3d's BoneTextureCrowd, copied and owned
    /// for the crystal style with demo3d's default cuts made fixed (30 Hz clips, LOD1 below 60
    /// projected pixels and LOD2 below 30, a LOD2 shadow proxy). Each frame the presenter writes
    /// every type's instances straight into a GraphicsBuffer (LockBufferForWrite), in stable slots;
    /// <see cref="Draw"/> unlocks them, runs the culling pass (the frustum plus the moon's shadow
    /// extension, 8 depth bins, LOD by projected height with a dithered crossfade) and the palette
    /// pass (each visible monster's bones blended once), then issues a flat set of
    /// RenderMeshIndirect calls: per type, one per LOD for the main draw (no shadows) and one for
    /// the shadow proxy. A Dying instance (a death's frame) is not drawn, but its palette is blended
    /// wherever it is, for the GPU shatter to seed its shards from. The instance buffers form a ring
    /// longer than the frames in flight, so the CPU never writes one the GPU may still read; the
    /// previous frame's copy tells the culling pass when a slot changed owner.
    /// </summary>
    public sealed class BoneTextureCrowd : IDisposable
    {
        const int Bins = CrowdCulling.Bins;
        const int ArgsInts = 5;
        const int Lods = 3;
        const int ShadowLod = 2;

        /// <summary>The layer the crowd draws on, so a camera can draw it alone (the stills' ID render).</summary>
        public const int Layer = 30;

        static readonly int IdInstances = Shader.PropertyToID("_Instances");
        static readonly int IdPrevInstances = Shader.PropertyToID("_PrevInstances");
        static readonly int IdVisibleList = Shader.PropertyToID("_VisibleList");
        static readonly int IdLodState = Shader.PropertyToID("_LodState");
        static readonly int IdClipTable = Shader.PropertyToID("_ClipTable");
        static readonly int IdBoneTex = Shader.PropertyToID("_BoneTex");
        static readonly int IdPrevCount = Shader.PropertyToID("_PrevCount");
        static readonly int IdBoneCount = Shader.PropertyToID("_BoneCount");
        static readonly int IdLodIndex = Shader.PropertyToID("_LodIndex");
        static readonly int IdLodDither = Shader.PropertyToID("_LodDither");
        static readonly int IdCrowdTime = Shader.PropertyToID("_CrowdTime");
        static readonly int IdPalette = Shader.PropertyToID("_Palette");
        static readonly int IdArgs = Shader.PropertyToID("_Args");
        static readonly int IdShadowArgs = Shader.PropertyToID("_ShadowArgs");
        static readonly int IdVisibleListOut = Shader.PropertyToID("_VisibleListOut");
        static readonly int IdShadowListOut = Shader.PropertyToID("_ShadowListOut");
        static readonly int IdLodStateRW = Shader.PropertyToID("_LodStateRW");
        static readonly int IdVisibleFlags = Shader.PropertyToID("_VisibleFlags");
        static readonly int IdCounters = Shader.PropertyToID("_Counters");
        static readonly int IdFrustumPlanes = Shader.PropertyToID("_FrustumPlanes");
        static readonly int IdShadowDir = Shader.PropertyToID("_ShadowDir");
        static readonly int IdShadowSinElev = Shader.PropertyToID("_ShadowSinElev");
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
        static readonly int IdHeightM = Shader.PropertyToID("_HeightM");

        /// <summary>LOD thresholds in projected pixels: LOD1 below x, LOD2 below y (TD11's, which passed its A/B check).</summary>
        static readonly Vector4 LodThresholds = new Vector4(60, 30, 0, 0);

        /// <summary>The latest count of monsters on screen, read back from the GPU (a frame or two late).</summary>
        public int OnScreen { get; private set; }

        /// <summary>Monsters written this frame, across every type.</summary>
        public int Written { get; private set; }

        readonly CrowdLibrary _lib;
        readonly ComputeShader _cull, _palette;
        readonly int _kClear, _kCull, _kBlend;
        readonly int[] _kernels;
        readonly Vector4[] _planes = new Vector4[6];
        readonly Plane[] _planeScratch = new Plane[6];
        readonly TypeState[] _types;
        readonly int _capacity;
        uint _frame = 1;
        readonly int _ring = Mathf.Max(2, QualitySettings.maxQueuedFrames) + 2;

        sealed class TypeState
        {
            public CrowdType Type;
            public Material Material;
            public GraphicsBuffer[] Instances; // a ring: the CPU never writes one the GPU may still read
            public GraphicsBuffer VisibleList, ShadowList, LodState, VisibleFlags, Counters, Args, ShadowArgs, ClipTable, ClipBounds;
            public GraphicsBuffer Palette;
            public MaterialPropertyBlock[] Main; // per LOD (a queued draw keeps its block)
            public MaterialPropertyBlock Shadow;
            public int Count, PrevCount;
            public bool Locked;
            public int OnScreen;
            public Action<AsyncGPUReadbackRequest> OnReadback;
        }

        /// <summary>Every type in the library, each with room for <paramref name="capacityPerType"/> monsters.</summary>
        public BoneTextureCrowd(CrowdLibrary lib, int capacityPerType)
        {
            _lib = lib;
            _capacity = capacityPerType;
            _cull = lib.CullShader ? lib.CullShader : throw new InvalidOperationException("CrowdLibrary has no cull shader");
            _kClear = _cull.FindKernel("ClearArgs");
            _kCull = _cull.FindKernel("Cull");
            _kernels = new[] { _kClear, _kCull };
            _palette = lib.PaletteShader ? lib.PaletteShader : throw new InvalidOperationException("CrowdLibrary has no palette shader");
            _kBlend = _palette.FindKernel("Blend");
            _types = new TypeState[lib.Types.Length];
            for (int i = 0; i < _types.Length; i++) _types[i] = Create(lib.Types[i]);
        }

        public int TypeCount => _types.Length;

        /// <summary>
        /// The type's palette, 3 float4 rows per bone by slot, as this frame's <see cref="Draw"/>
        /// blends it for the camera- and shadow-visible slots and the dying ones (the GPU shatter's
        /// seed, <see cref="MonsterShatter"/>).
        /// </summary>
        public GraphicsBuffer Palette(int type) => _types[type].Palette;

        TypeState Create(CrowdType t)
        {
            var s = new TypeState { Type = t };
            s.OnReadback = r =>
            {
                if (!r.hasError && r.layerDataSize >= 4) s.OnScreen = r.GetData<int>()[0];
            };
            int cap = _capacity;
            s.Instances = new GraphicsBuffer[_ring];
            for (int i = 0; i < _ring; i++)
                s.Instances[i] = new GraphicsBuffer(GraphicsBuffer.Target.Structured, GraphicsBuffer.UsageFlags.LockBufferForWrite,
                    cap, MonsterInstance.Stride) { name = t.Id + "_instances" + i };
            s.VisibleList = new GraphicsBuffer(GraphicsBuffer.Target.Structured, cap * Bins * Lods, 4) { name = t.Id + "_visible" };
            s.ShadowList = new GraphicsBuffer(GraphicsBuffer.Target.Structured, cap, 4) { name = t.Id + "_shadowList" };
            s.LodState = new GraphicsBuffer(GraphicsBuffer.Target.Structured, cap, 16) { name = t.Id + "_lodState" };
            s.LodState.SetData(new uint[cap * 4]);
            s.VisibleFlags = new GraphicsBuffer(GraphicsBuffer.Target.Structured, cap, 4) { name = t.Id + "_flags" };
            s.Counters = new GraphicsBuffer(GraphicsBuffer.Target.Raw, 4, 4) { name = t.Id + "_counters" };
            s.Palette = new GraphicsBuffer(GraphicsBuffer.Target.Structured, cap * t.BoneCount * 3, 16) { name = t.Id + "_palette" };

            // Commands: [lod][bin] for the main draw, one for the shadow proxy. Each LOD is one submesh.
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
            var sd = t.Lods[ShadowLod].GetSubMesh(0);
            var shadowArgs = new uint[] { (uint)sd.indexCount, 0, (uint)sd.indexStart, (uint)sd.baseVertex, 0 };
            s.Args = new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments | GraphicsBuffer.Target.Raw, args.Length, 4) { name = t.Id + "_args" };
            s.Args.SetData(args);
            s.ShadowArgs = new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments | GraphicsBuffer.Target.Raw, shadowArgs.Length, 4) { name = t.Id + "_shadowArgs" };
            s.ShadowArgs.SetData(shadowArgs);

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

            s.Material = new Material(_lib.Crystal) { name = t.Id + "_crystal", enableInstancing = false };
            s.Material.SetFloat(IdHeightM, t.HeightM);
            s.Main = new MaterialPropertyBlock[Lods];
            for (int l = 0; l < Lods; l++) s.Main[l] = new MaterialPropertyBlock();
            s.Shadow = new MaterialPropertyBlock();
            for (int bi = 0; bi <= Lods; bi++)
            {
                var mpb = bi < Lods ? s.Main[bi] : s.Shadow;
                mpb.SetTexture(IdBoneTex, t.Bones);
                mpb.SetBuffer(IdClipTable, s.ClipTable);
                mpb.SetBuffer(IdClipBounds, s.ClipBounds);
                mpb.SetBuffer(IdLodState, s.LodState);
                mpb.SetBuffer(IdPalette, s.Palette);
                mpb.SetInt(IdBoneCount, t.BoneCount);
                mpb.SetBuffer(IdVisibleList, bi < Lods ? s.VisibleList : s.ShadowList);
                mpb.SetFloat(IdLodDither, bi < Lods ? 1 : 0);
                mpb.SetFloat(IdLodIndex, bi < Lods ? bi : ShadowLod);
            }
            return s;
        }

        /// <summary>
        /// The type's instance slots for this frame, mapped GPU memory: write every field of every
        /// slot (never read it). A type not requested in a frame draws nothing that frame.
        /// </summary>
        public NativeArray<MonsterInstance> Instances(int type, int count)
        {
            var s = _types[type];
            if (count > _capacity) throw new ArgumentOutOfRangeException(nameof(count), $"capacity is {_capacity}");
            if (s.Locked) throw new InvalidOperationException("BoneTextureCrowd: Instances called twice for a type in one frame");
            s.Count = count;
            s.Locked = true;
            return s.Instances[_frame % _ring].LockBufferForWrite<MonsterInstance>(0, count);
        }

        /// <summary>Culls, blends and queues this frame's draws for the camera; `time` clocks the LOD crossfades (song time).</summary>
        public void Draw(Camera camera, float time)
        {
            int icur = (int)(_frame % _ring), iprev = (int)((_frame - 1) % _ring);

            CrowdCulling.Planes(camera, _planeScratch, _planes);
            var sun = RenderSettings.sun;
            var lightDir = sun != null ? sun.transform.forward : Vector3.down;
            var bins = CrowdCulling.BinRange(camera, 5f, 2f);
            _cull.SetVectorArray(IdFrustumPlanes, _planes);
            _cull.SetVector(IdShadowDir, lightDir);
            _cull.SetFloat(IdShadowSinElev, CrowdCulling.SinElevation(lightDir));
            _cull.SetVector(IdCamPos, camera.transform.position);
            _cull.SetVector(IdCamForward, camera.transform.forward);
            _cull.SetFloat(IdBinNear, bins.x);
            _cull.SetFloat(IdBinFar, bins.y);
            _cull.SetFloat(IdPixelScale, CrowdCulling.PixelScale(camera));
            _cull.SetVector(IdLodThresholds, LodThresholds);
            _cull.SetFloat(IdCrowdTime, time);

            int instances = 0, onScreen = 0;
            foreach (var s in _types)
            {
                if (s.Locked) s.Instances[icur].UnlockBufferAfterWrite<MonsterInstance>(s.Count);
                else s.Count = 0;
                s.Locked = false;
                instances += s.Count;
                if (s.Count > 0)
                {
                    Cull(s, icur, iprev);
                    Blend(s, icur);
                    for (int bi = 0; bi <= Lods; bi++)
                    {
                        var mpb = bi < Lods ? s.Main[bi] : s.Shadow;
                        mpb.SetBuffer(IdInstances, s.Instances[icur]);
                        mpb.SetFloat(IdCrowdTime, time);
                    }
                    AsyncGPUReadback.Request(s.Counters, s.OnReadback);
                }
                else s.OnScreen = 0;
                onScreen += s.OnScreen;
                s.PrevCount = s.Count;
            }
            Written = instances;
            OnScreen = onScreen;
            Queue(camera);
            _frame++;
        }

        /// <summary>
        /// Queues the latest culled draws for the camera again: a hand-drawn camera spends the
        /// draws queued in a frame, so a second draw of the same frame (the stills' ID render)
        /// needs them queued anew.
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
                        receiveShadows = true,
                        motionVectorMode = MotionVectorGenerationMode.ForceNoMotion,
                        lightProbeUsage = LightProbeUsage.Off,
                        reflectionProbeUsage = ReflectionProbeUsage.Off,
                        camera = camera,
                        layer = Layer,
                    };
                    Graphics.RenderMeshIndirect(rp, s.Type.Lods[l], s.Args, Bins, l * Bins);
                }
                var srp = new RenderParams(s.Material)
                {
                    worldBounds = bounds,
                    matProps = s.Shadow,
                    camera = camera,
                    shadowCastingMode = ShadowCastingMode.ShadowsOnly,
                    receiveShadows = false,
                    motionVectorMode = MotionVectorGenerationMode.ForceNoMotion,
                    lightProbeUsage = LightProbeUsage.Off,
                    reflectionProbeUsage = ReflectionProbeUsage.Off,
                    layer = Layer,
                };
                Graphics.RenderMeshIndirect(srp, s.Type.Lods[ShadowLod], s.ShadowArgs, 1, 0);
            }
        }

        void Cull(TypeState s, int icur, int iprev)
        {
            const int commands = Lods * Bins;
            _cull.SetInt(IdLodCount, Lods);
            _cull.SetInt(IdCount, s.Count);
            _cull.SetInt(IdCapacity, _capacity);
            _cull.SetInt(IdCommands, commands);
            foreach (var k in _kernels)
            {
                _cull.SetBuffer(k, IdArgs, s.Args);
                _cull.SetBuffer(k, IdShadowArgs, s.ShadowArgs);
                _cull.SetBuffer(k, IdCounters, s.Counters);
            }
            _cull.SetBuffer(_kCull, IdInstances, s.Instances[icur]);
            _cull.SetBuffer(_kCull, IdPrevInstances, s.Instances[iprev]);
            _cull.SetInt(IdPrevCount, s.PrevCount);
            _cull.SetBuffer(_kCull, IdClipBounds, s.ClipBounds);
            _cull.SetBuffer(_kCull, IdVisibleListOut, s.VisibleList);
            _cull.SetBuffer(_kCull, IdShadowListOut, s.ShadowList);
            _cull.SetBuffer(_kCull, IdLodStateRW, s.LodState);
            _cull.SetBuffer(_kCull, IdVisibleFlags, s.VisibleFlags);
            _cull.Dispatch(_kClear, (commands + 63) / 64, 1, 1);
            _cull.Dispatch(_kCull, (s.Count + 63) / 64, 1, 1);
        }

        void Blend(TypeState s, int icur)
        {
            _palette.SetInt(IdCount, s.Count);
            _palette.SetInt(IdBoneCount, s.Type.BoneCount);
            _palette.SetFloat(IdClipRate, CrowdLibrary.ClipRate);
            _palette.SetBuffer(_kBlend, IdInstances, s.Instances[icur]);
            _palette.SetBuffer(_kBlend, IdVisibleFlags, s.VisibleFlags);
            _palette.SetBuffer(_kBlend, IdClipTable, s.ClipTable);
            _palette.SetTexture(_kBlend, IdBoneTex, s.Type.Bones);
            _palette.SetBuffer(_kBlend, IdPaletteOut, s.Palette);
            _palette.Dispatch(_kBlend, (s.Count * s.Type.BoneCount + 63) / 64, 1, 1);
        }

        public void Dispose()
        {
            foreach (var s in _types)
            {
                if (s.Locked) s.Instances[_frame % _ring].UnlockBufferAfterWrite<MonsterInstance>(0);
                foreach (var b in s.Instances) b?.Dispose();
                s.VisibleList?.Dispose();
                s.ShadowList?.Dispose();
                s.LodState?.Dispose();
                s.VisibleFlags?.Dispose();
                s.Counters?.Dispose();
                s.Args?.Dispose();
                s.ShadowArgs?.Dispose();
                s.ClipTable?.Dispose();
                s.ClipBounds?.Dispose();
                s.Palette?.Dispose();
                if (s.Material != null) UnityEngine.Object.Destroy(s.Material);
            }
        }
    }
}
