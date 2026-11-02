using System;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;

namespace Champions.Render.Crowd
{
    /// <summary>
    /// The edge crowd (plan.md, Rendering &gt; Crowd): game/'s bone-texture crowd, ported by review and cut down for
    /// edge cuts (no LODs, depth bins or shadows). Each frame the presenter writes every type's instances straight
    /// into a GraphicsBuffer (LockBufferForWrite), in stable slots; <see cref="Draw"/> unlocks them, runs the culling
    /// pass (the frustum; one visible list per type) and the palette pass (each visible or dying monster's bones
    /// blended once), then queues two indirect draws per type: its bodies (EdgeBody) and its lines (EdgeLines), both
    /// reading the visible list. A Dying instance (its death's frame) is not drawn but its palette is blended, for
    /// <see cref="EdgeShatter"/> to seed its pieces from. The instance buffers form a ring longer than the frames in
    /// flight, so the CPU never writes one the GPU may still read.
    /// </summary>
    public sealed class EdgeCrowd : IDisposable
    {
        const int ArgsInts = 5;

        /// <summary>The layer the crowd draws on, so a camera can draw it alone.</summary>
        public const int Layer = 30;

        static readonly int IdInstances = Shader.PropertyToID("_Instances");
        static readonly int IdVisibleList = Shader.PropertyToID("_VisibleList");
        static readonly int IdVisibleListOut = Shader.PropertyToID("_VisibleListOut");
        static readonly int IdVisibleFlags = Shader.PropertyToID("_VisibleFlags");
        static readonly int IdClipTable = Shader.PropertyToID("_ClipTable");
        static readonly int IdClipBounds = Shader.PropertyToID("_ClipBounds");
        static readonly int IdBoneTex = Shader.PropertyToID("_BoneTex");
        static readonly int IdBoneCount = Shader.PropertyToID("_BoneCount");
        static readonly int IdPalette = Shader.PropertyToID("_Palette");
        static readonly int IdPaletteOut = Shader.PropertyToID("_PaletteOut");
        static readonly int IdArgs = Shader.PropertyToID("_Args");
        static readonly int IdFrustumPlanes = Shader.PropertyToID("_FrustumPlanes");
        static readonly int IdCount = Shader.PropertyToID("_Count");
        static readonly int IdClipRate = Shader.PropertyToID("_ClipRate");
        static readonly int IdHeightM = Shader.PropertyToID("_HeightM");

        /// <summary>The GPU timings' marker (a development player records them).</summary>
        public static readonly ProfilingSampler Sampler = new ProfilingSampler("EdgeCrowd.Cull+Palette");

        /// <summary>The look the next <see cref="Draw"/> applies.</summary>
        public EdgeLook Look = EdgeLook.Default;

        /// <summary>Monsters written this frame, across every type.</summary>
        public int Written { get; private set; }

        readonly EdgeCutLibrary _lib;
        readonly ComputeShader _cull, _palette;
        readonly int _kClear, _kCull, _kBlend;
        readonly Vector4[] _planes = new Vector4[6];
        readonly Plane[] _planeScratch = new Plane[6];
        readonly TypeState[] _types;
        readonly int _capacity;
        readonly int _ring = Mathf.Max(2, QualitySettings.maxQueuedFrames) + 2;
        readonly CommandBuffer _cmd = new CommandBuffer { name = "EdgeCrowd" };
        uint _frame = 1;

        sealed class TypeState
        {
            public EdgeCutType Type;
            public Material Body, Lines;
            public GraphicsBuffer[] Instances; // a ring: the CPU never writes one the GPU may still read
            public GraphicsBuffer VisibleList, VisibleFlags, Args, ClipTable, ClipBounds, Palette;
            public MaterialPropertyBlock Block;
            public int Count, DrawnCount;
            public bool Locked;
        }

        /// <summary>Every type in the library, each with room for <paramref name="capacityPerType"/> monsters.</summary>
        public EdgeCrowd(EdgeCutLibrary lib, int capacityPerType)
        {
            _lib = lib;
            _capacity = capacityPerType;
            _cull = lib.Cull ? lib.Cull : throw new InvalidOperationException("EdgeCutLibrary has no cull shader");
            _palette = lib.Palette ? lib.Palette : throw new InvalidOperationException("EdgeCutLibrary has no palette shader");
            _kClear = _cull.FindKernel("ClearArgs");
            _kCull = _cull.FindKernel("Cull");
            _kBlend = _palette.FindKernel("Blend");
            _types = new TypeState[lib.Types.Length];
            for (int i = 0; i < _types.Length; i++) _types[i] = Create(lib.Types[i]);
        }

        public int TypeCount => _types.Length;

        public EdgeCutType Type(int type) => _types[type].Type;

        /// <summary>
        /// The type's palette, 3 float4 rows per bone by slot, as this frame's <see cref="Draw"/> blends it for the
        /// visible and dying slots (the shatter's seed).
        /// </summary>
        public GraphicsBuffer Palette(int type) => _types[type].Palette;

        /// <summary>
        /// The type's code in the stills' type-ID pass (<see cref="IdPass.Monster"/> of its cast index): its presenter
        /// sets it, and a type without one is missing from the pass.
        /// </summary>
        public void SetIdCode(int type, int code) => _types[type].Block.SetFloat(IdPass.Id, code);

        /// <summary>The type's indirect commands, bodies then lines (for the tests).</summary>
        internal GraphicsBuffer Args(int type) => _types[type].Args;

        TypeState Create(EdgeCutType t)
        {
            var s = new TypeState { Type = t };
            int cap = _capacity;
            s.Instances = new GraphicsBuffer[_ring];
            for (int i = 0; i < _ring; i++)
                s.Instances[i] = new GraphicsBuffer(GraphicsBuffer.Target.Structured, GraphicsBuffer.UsageFlags.LockBufferForWrite,
                    cap, MonsterInstance.Stride) { name = t.Id + "_instances" + i };
            s.VisibleList = new GraphicsBuffer(GraphicsBuffer.Target.Structured, cap, 4) { name = t.Id + "_visible" };
            s.VisibleFlags = new GraphicsBuffer(GraphicsBuffer.Target.Structured, cap, 4) { name = t.Id + "_flags" };
            s.Palette = new GraphicsBuffer(GraphicsBuffer.Target.Structured, cap * t.BoneCount * 3, 16) { name = t.Id + "_palette" };
            s.Args = new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments | GraphicsBuffer.Target.Raw, 2 * ArgsInts, 4) { name = t.Id + "_args" };
            s.Args.SetData(new uint[]
            {
                t.Body.GetIndexCount(0), 0, t.Body.GetIndexStart(0), t.Body.GetBaseVertex(0), 0,
                t.Lines.GetIndexCount(0), 0, t.Lines.GetIndexStart(0), t.Lines.GetBaseVertex(0), 0,
            });
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
            s.Body = new Material(_lib.Body) { name = t.Id + "_body", enableInstancing = false };
            s.Lines = new Material(_lib.Lines) { name = t.Id + "_lines", enableInstancing = false };
            s.Block = new MaterialPropertyBlock();
            s.Block.SetBuffer(IdPalette, s.Palette);
            s.Block.SetBuffer(IdVisibleList, s.VisibleList);
            s.Block.SetInt(IdBoneCount, t.BoneCount);
            s.Block.SetFloat(IdHeightM, t.HeightM);
            return s;
        }

        /// <summary>
        /// The type's instance slots for this frame, mapped GPU memory: write every field of every slot (never read
        /// it). A type not requested in a frame draws nothing that frame.
        /// </summary>
        public NativeArray<MonsterInstance> Instances(int type, int count)
        {
            var s = _types[type];
            if (count > _capacity) throw new ArgumentOutOfRangeException(nameof(count), $"capacity is {_capacity}");
            if (s.Locked) throw new InvalidOperationException("EdgeCrowd: Instances called twice for a type in one frame");
            s.Count = count;
            s.Locked = true;
            return s.Instances[_frame % _ring].LockBufferForWrite<MonsterInstance>(0, count);
        }

        /// <summary>Culls, blends and queues this frame's draws for the camera.</summary>
        public void Draw(Camera camera)
        {
            int icur = (int)(_frame % _ring);
            Look.Apply();
            GeometryUtility.CalculateFrustumPlanes(camera, _planeScratch);
            for (int i = 0; i < 6; i++)
                _planes[i] = new Vector4(_planeScratch[i].normal.x, _planeScratch[i].normal.y, _planeScratch[i].normal.z, _planeScratch[i].distance);
            _cmd.Clear();
            int written = 0;
            using (new ProfilingScope(_cmd, Sampler))
            {
                _cmd.SetComputeVectorArrayParam(_cull, IdFrustumPlanes, _planes);
                _cmd.SetComputeFloatParam(_palette, IdClipRate, EdgeCutLibrary.ClipRate);
                foreach (var s in _types)
                {
                    if (s.Locked) s.Instances[icur].UnlockBufferAfterWrite<MonsterInstance>(s.Count);
                    else s.Count = 0;
                    s.Locked = false;
                    written += s.Count;
                    if (s.Count == 0) continue;
                    var instances = s.Instances[icur];
                    _cmd.SetComputeIntParam(_cull, IdCount, s.Count);
                    _cmd.SetComputeBufferParam(_cull, _kClear, IdArgs, s.Args);
                    _cmd.SetComputeBufferParam(_cull, _kCull, IdArgs, s.Args);
                    _cmd.SetComputeBufferParam(_cull, _kCull, IdInstances, instances);
                    _cmd.SetComputeBufferParam(_cull, _kCull, IdClipBounds, s.ClipBounds);
                    _cmd.SetComputeBufferParam(_cull, _kCull, IdVisibleListOut, s.VisibleList);
                    _cmd.SetComputeBufferParam(_cull, _kCull, IdVisibleFlags, s.VisibleFlags);
                    _cmd.DispatchCompute(_cull, _kClear, 1, 1, 1);
                    _cmd.DispatchCompute(_cull, _kCull, (s.Count + 63) / 64, 1, 1);
                    _cmd.SetComputeIntParam(_palette, IdCount, s.Count);
                    _cmd.SetComputeIntParam(_palette, IdBoneCount, s.Type.BoneCount);
                    _cmd.SetComputeBufferParam(_palette, _kBlend, IdInstances, instances);
                    _cmd.SetComputeBufferParam(_palette, _kBlend, IdVisibleFlags, s.VisibleFlags);
                    _cmd.SetComputeBufferParam(_palette, _kBlend, IdClipTable, s.ClipTable);
                    _cmd.SetComputeTextureParam(_palette, _kBlend, IdBoneTex, s.Type.Bones);
                    _cmd.SetComputeBufferParam(_palette, _kBlend, IdPaletteOut, s.Palette);
                    _cmd.DispatchCompute(_palette, _kBlend, (s.Count * s.Type.BoneCount + 63) / 64, 1, 1);
                    s.Block.SetBuffer(IdInstances, instances);
                }
            }
            Graphics.ExecuteCommandBuffer(_cmd);
            foreach (var s in _types) s.DrawnCount = s.Count;
            Written = written;
            Queue(camera);
            _frame++;
        }

        /// <summary>
        /// Queues the latest culled draws for the camera again: a hand-drawn camera spends the draws queued in a
        /// frame, so a second draw of the same frame (the stills' mask) needs them queued anew.
        /// </summary>
        public void Queue(Camera camera)
        {
            var bounds = new Bounds(Vector3.zero, Vector3.one * 100000f);
            foreach (var s in _types)
            {
                if (s.DrawnCount == 0) continue;
                Graphics.RenderMeshIndirect(Params(s.Body, s.Block, bounds, camera), s.Type.Body, s.Args, 1, 0);
                Graphics.RenderMeshIndirect(Params(s.Lines, s.Block, bounds, camera), s.Type.Lines, s.Args, 1, 1);
            }
        }

        internal static RenderParams Params(Material material, MaterialPropertyBlock block, Bounds bounds, Camera camera) =>
            new RenderParams(material)
            {
                worldBounds = bounds,
                matProps = block,
                shadowCastingMode = ShadowCastingMode.Off,
                receiveShadows = false,
                motionVectorMode = MotionVectorGenerationMode.ForceNoMotion,
                lightProbeUsage = LightProbeUsage.Off,
                reflectionProbeUsage = ReflectionProbeUsage.Off,
                camera = camera,
                layer = Layer,
            };

        public void Dispose()
        {
            _cmd.Release();
            foreach (var s in _types)
            {
                if (s.Locked) s.Instances[_frame % _ring].UnlockBufferAfterWrite<MonsterInstance>(0);
                foreach (var b in s.Instances) b?.Dispose();
                s.VisibleList?.Dispose();
                s.VisibleFlags?.Dispose();
                s.Args?.Dispose();
                s.ClipTable?.Dispose();
                s.ClipBounds?.Dispose();
                s.Palette?.Dispose();
                Destroy(s.Body);
                Destroy(s.Lines);
            }
        }

        internal static void Destroy(UnityEngine.Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(o);
            else UnityEngine.Object.DestroyImmediate(o);
        }
    }
}
