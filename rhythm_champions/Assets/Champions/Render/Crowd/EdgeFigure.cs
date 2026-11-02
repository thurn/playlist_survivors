using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace Champions.Render.Crowd
{
    /// <summary>
    /// A few figures of one edge cut posed on the CPU (the hero, by <see cref="RigPose"/>): the crowd's EdgeBody and
    /// EdgeLines shaders read the same instance, visible-list and palette buffers they read from the crowd, filled here
    /// from the CPU each frame instead of by the culling and palette passes. <see cref="Set"/> each figure's instance
    /// and palette, then <see cref="Draw"/> uploads them and queues one indirect draw for the bodies and one for the
    /// lines. Its palette buffer can seed <see cref="EdgeShatter"/> (the hero's death) as the crowd's does. It applies no
    /// <see cref="EdgeLook"/>: the crowd's draw does, or a scene without one.
    /// </summary>
    public sealed class EdgeFigure : IDisposable
    {
        const int ArgsInts = 5;

        static readonly int IdInstances = Shader.PropertyToID("_Instances");
        static readonly int IdVisibleList = Shader.PropertyToID("_VisibleList");
        static readonly int IdPalette = Shader.PropertyToID("_Palette");
        static readonly int IdBoneCount = Shader.PropertyToID("_BoneCount");
        static readonly int IdHeightM = Shader.PropertyToID("_HeightM");

        readonly EdgeCutType _type;
        readonly GraphicsBuffer _instances, _visible, _palette, _args;
        readonly MonsterInstance[] _slots;
        readonly Vector4[] _rows;
        readonly uint[] _argData = new uint[2 * ArgsInts];
        readonly Material _body, _lines;
        readonly MaterialPropertyBlock _block = new MaterialPropertyBlock();
        int _count, _drawn;

        /// <summary>Room for <paramref name="capacity"/> figures of the library's type <paramref name="type"/>.</summary>
        public EdgeFigure(EdgeCutLibrary lib, int type, int capacity = 1)
        {
            _type = lib.Types[type];
            int bones = _type.BoneCount;
            _slots = new MonsterInstance[capacity];
            _rows = new Vector4[capacity * bones * 3];
            _instances = new GraphicsBuffer(GraphicsBuffer.Target.Structured, capacity, MonsterInstance.Stride) { name = _type.Id + "_figures" };
            _visible = new GraphicsBuffer(GraphicsBuffer.Target.Structured, capacity, 4) { name = _type.Id + "_figureList" };
            var list = new uint[capacity];
            for (int i = 0; i < capacity; i++) list[i] = (uint)i;
            _visible.SetData(list);
            _palette = new GraphicsBuffer(GraphicsBuffer.Target.Structured, capacity * bones * 3, 16) { name = _type.Id + "_figurePalette" };
            _args = new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments, 2 * ArgsInts, 4) { name = _type.Id + "_figureArgs" };
            _body = new Material(lib.Body) { name = _type.Id + "_figureBody", enableInstancing = false };
            _lines = new Material(lib.Lines) { name = _type.Id + "_figureLines", enableInstancing = false };
            _block.SetBuffer(IdInstances, _instances);
            _block.SetBuffer(IdVisibleList, _visible);
            _block.SetBuffer(IdPalette, _palette);
            _block.SetInt(IdBoneCount, bones);
            _block.SetFloat(IdHeightM, _type.HeightM);
        }

        public EdgeCutType Type => _type;

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
        /// Uploads the first <paramref name="count"/> figures and queues their draws for the camera (none for 0, or when
        /// not <paramref name="visible"/>: a figure about to shatter, whose palette the shatter seeds from).
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
                    Command(_type.Body, count, 0);
                    Command(_type.Lines, count, ArgsInts);
                    _args.SetData(_argData);
                    _drawn = count;
                }
            }
            Queue(camera);
        }

        void Command(Mesh mesh, int count, int at)
        {
            _argData[at] = mesh.GetIndexCount(0);
            _argData[at + 1] = (uint)count;
            _argData[at + 2] = mesh.GetIndexStart(0);
            _argData[at + 3] = mesh.GetBaseVertex(0);
            _argData[at + 4] = 0;
        }

        /// <summary>Queues the latest draws for the camera again (a second draw of the same frame: the stills' ID pass).</summary>
        public void Queue(Camera camera)
        {
            if (_count == 0) return;
            var bounds = new Bounds(Vector3.zero, Vector3.one * 100000f);
            Graphics.RenderMeshIndirect(EdgeCrowd.Params(_body, _block, bounds, camera), _type.Body, _args, 1, 0);
            Graphics.RenderMeshIndirect(EdgeCrowd.Params(_lines, _block, bounds, camera), _type.Lines, _args, 1, 1);
        }

        public void Dispose()
        {
            _instances?.Dispose();
            _visible?.Dispose();
            _palette?.Dispose();
            _args?.Dispose();
            EdgeCrowd.Destroy(_body);
            EdgeCrowd.Destroy(_lines);
        }
    }
}
