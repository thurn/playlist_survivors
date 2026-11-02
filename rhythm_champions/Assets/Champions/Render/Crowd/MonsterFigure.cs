using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace Champions.Render.Crowd
{
    /// <summary>
    /// A few figures of one type posed on the CPU (the hero, by <see cref="RigPose"/> from its <see cref="MonsterRig"/>):
    /// the crowd's skinning front end (MonsterCommon.hlsl) reads the same instance, visible-list and palette buffers it reads
    /// in the crowd, filled here from the CPU each frame instead of by the culling and palette passes, and draws the type's
    /// LOD0 in the material given (<see cref="EdgeLook.Hero"/>). <see cref="Set"/> each figure's instance and palette, then
    /// <see cref="Draw"/> uploads them and queues one indirect draw. Its palette buffer can seed a
    /// <see cref="MonsterShatter"/> (the hero's death) as the crowd's does. Ported from the deleted edge cuts' EdgeFigure
    /// (git b243e08b^).
    /// </summary>
    public sealed class MonsterFigure : IDisposable
    {
        const int ArgsInts = 5;

        static readonly int IdInstances = Shader.PropertyToID("_Instances");
        static readonly int IdVisibleList = Shader.PropertyToID("_VisibleList");
        static readonly int IdLodState = Shader.PropertyToID("_LodState");
        static readonly int IdPalette = Shader.PropertyToID("_Palette");
        static readonly int IdBoneCount = Shader.PropertyToID("_BoneCount");
        static readonly int IdMotion = Shader.PropertyToID("_Motion");
        static readonly int IdMotionCount = Shader.PropertyToID("_MotionCount");

        readonly MonsterType _type;
        readonly GraphicsBuffer _instances, _visible, _lodState, _palette, _motion, _args;
        readonly MonsterInstance[] _slots;
        readonly Vector4[] _rows;
        readonly uint[] _argData = new uint[ArgsInts];
        readonly Material _material;
        readonly MaterialPropertyBlock _block = new MaterialPropertyBlock();
        int _count, _drawn;

        /// <summary>Room for <paramref name="capacity"/> figures of <paramref name="type"/>, drawn in <paramref name="material"/> (the figure owns it).</summary>
        public MonsterFigure(MonsterType type, Material material, int capacity = 1)
        {
            _type = type;
            _material = material;
            int bones = type.BoneCount;
            _slots = new MonsterInstance[capacity];
            _rows = new Vector4[capacity * bones * 3];
            _instances = new GraphicsBuffer(GraphicsBuffer.Target.Structured, capacity, MonsterInstance.Stride) { name = type.Id + "_figures" };
            _visible = new GraphicsBuffer(GraphicsBuffer.Target.Structured, capacity, 4) { name = type.Id + "_figureList" };
            var list = new uint[capacity];
            for (int i = 0; i < capacity; i++) list[i] = (uint)i;
            _visible.SetData(list);
            // Every figure at LOD0, never crossfading (CrowdLodFade reads equal LODs as settled).
            _lodState = new GraphicsBuffer(GraphicsBuffer.Target.Structured, capacity, 16) { name = type.Id + "_figureLods" };
            _lodState.SetData(new uint[capacity * 4]);
            _palette = new GraphicsBuffer(GraphicsBuffer.Target.Structured, capacity * bones * 3, 16) { name = type.Id + "_figurePalette" };
            // No motion layers: a stand-in buffer the shaders skip (_MotionCount 0).
            _motion = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 1, MonsterMotion.Stride) { name = type.Id + "_figureMotion" };
            _motion.SetData(new[] { MonsterMotion.Still(0) });
            _args = new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments, ArgsInts, 4) { name = type.Id + "_figureArgs" };
            _block.SetBuffer(IdInstances, _instances);
            _block.SetBuffer(IdVisibleList, _visible);
            _block.SetBuffer(IdLodState, _lodState);
            _block.SetBuffer(IdPalette, _palette);
            _block.SetBuffer(IdMotion, _motion);
            _block.SetInt(IdMotionCount, 0);
            _block.SetInt(IdBoneCount, bones);
        }

        public MonsterType Type => _type;

        /// <summary>The figures' palettes, 3 float4 rows per bone by slot, as the latest <see cref="Draw"/> uploaded them.</summary>
        public GraphicsBuffer Palette => _palette;

        /// <summary>The figures' code in the stills' type-ID pass (<see cref="IdPass"/>).</summary>
        public void SetIdCode(int code) => _block.SetFloat(IdPass.Id, code);

        /// <summary>
        /// Figure <paramref name="slot"/> this frame: its instance (position, yaw, scale, tint, flash and flare; the clip
        /// fields are unused) and its palette, 3 rows per bone (<see cref="RigPose.Palette"/>). Slots from 0 up to the
        /// count given to <see cref="Draw"/> are drawn.
        /// </summary>
        public void Set(int slot, in MonsterInstance instance, Vector4[] palette)
        {
            _slots[slot] = instance;
            Array.Copy(palette, 0, _rows, slot * _type.BoneCount * 3, _type.BoneCount * 3);
        }

        /// <summary>
        /// Uploads the first <paramref name="count"/> figures and queues their draw for the camera (none for 0, or when not
        /// <paramref name="visible"/>: a figure about to shatter, whose palette the shatter seeds from).
        /// </summary>
        public void Draw(Camera camera, int count, bool visible = true)
        {
            _count = visible ? count : 0;
            if (count > 0)
            {
                _instances.SetData(_slots, 0, 0, count);
                _palette.SetData(_rows, 0, 0, count * _type.BoneCount * 3);
                if (count != _drawn)
                {
                    var lod = _type.Lods[0];
                    _argData[0] = lod.GetIndexCount(0);
                    _argData[1] = (uint)count;
                    _argData[2] = lod.GetIndexStart(0);
                    _argData[3] = lod.GetBaseVertex(0);
                    _argData[4] = 0;
                    _args.SetData(_argData);
                    _drawn = count;
                }
            }
            Queue(camera);
        }

        /// <summary>Queues the latest draw again (a second draw of the same frame: the stills' ID pass).</summary>
        public void Queue(Camera camera)
        {
            if (_count == 0) return;
            var rp = new RenderParams(_material)
            {
                worldBounds = new Bounds(Vector3.zero, Vector3.one * 100000f),
                matProps = _block,
                shadowCastingMode = ShadowCastingMode.Off,
                receiveShadows = false,
                motionVectorMode = MotionVectorGenerationMode.ForceNoMotion,
                lightProbeUsage = LightProbeUsage.Off,
                reflectionProbeUsage = ReflectionProbeUsage.Off,
                camera = camera,
                layer = MonsterCrowd.Layer,
            };
            Graphics.RenderMeshIndirect(rp, _type.Lods[0], _args, 1, 0);
        }

        public void Dispose()
        {
            _instances?.Dispose();
            _visible?.Dispose();
            _lodState?.Dispose();
            _palette?.Dispose();
            _motion?.Dispose();
            _args?.Dispose();
            MonsterCrowd.Destroy(_material);
        }
    }
}
