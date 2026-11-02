using System;
using System.Collections.Generic;
using Champions.Render.Crowd;
using UnityEngine;
using UnityEngine.Rendering;

namespace Champions.Render.Structures
{
    /// <summary>
    /// The standing structures on screen (design.md, The World &gt; Structures; plan.md, Rendering: StructureFill and
    /// StructureLines): each type's instances in one buffer, drawn with one indirect draw for its fills and one for its
    /// edges, all reading their pieces at rest (the identity block). The presenter adds and removes structures by the
    /// sim's ids as their chunks load and unload and they break, and flashes them when hit; a structure's buffer is
    /// uploaded only on a frame that changed it.
    /// </summary>
    public sealed class StructureRenderer : IDisposable
    {
        const int ArgsInts = 5;
        static readonly int IdInstances = Shader.PropertyToID("_StructureInstances");
        static readonly int IdPieces = Shader.PropertyToID("_StructurePieces");

        /// <summary>The look the next <see cref="Draw"/> applies.</summary>
        public StructureLook Look = StructureLook.Default;

        readonly StructureLibrary _lib;
        readonly TypeState[] _types;
        readonly int _capacity;
        readonly GraphicsBuffer _identity;
        readonly Dictionary<int, (int Type, int Slot)> _where = new Dictionary<int, (int, int)>();

        sealed class TypeState
        {
            public StructureType Type;
            public Material Fill, Lines;
            public MaterialPropertyBlock Block;
            public GraphicsBuffer Instances, Args;
            public StructureInstance[] Cpu;
            public int[] Ids;
            public int Count, Drawn;
            public bool Dirty;
        }

        /// <summary>Every type in the library, each with room for <paramref name="capacityPerType"/> standing structures.</summary>
        public StructureRenderer(StructureLibrary lib, int capacityPerType)
        {
            _lib = lib;
            _capacity = capacityPerType;
            _identity = Identity(lib.MaxPieces);
            _types = new TypeState[lib.Types.Length];
            for (int i = 0; i < _types.Length; i++) _types[i] = Create(lib.Types[i]);
        }

        public StructureLibrary Library => _lib;

        public int Standing => _where.Count;

        /// <summary>A buffer of <paramref name="pieces"/> identity transforms, three rows each: pieces at rest.</summary>
        internal static GraphicsBuffer Identity(int pieces)
        {
            var rows = new Vector4[3 * pieces];
            for (int p = 0; p < pieces; p++)
            {
                rows[3 * p] = new Vector4(1, 0, 0, 0);
                rows[3 * p + 1] = new Vector4(0, 1, 0, 0);
                rows[3 * p + 2] = new Vector4(0, 0, 1, 0);
            }
            var b = new GraphicsBuffer(GraphicsBuffer.Target.Structured, rows.Length, 16) { name = "structures_rest" };
            b.SetData(rows);
            return b;
        }

        TypeState Create(StructureType t)
        {
            var s = new TypeState
            {
                Type = t,
                Cpu = new StructureInstance[_capacity],
                Ids = new int[_capacity],
                Instances = new GraphicsBuffer(GraphicsBuffer.Target.Structured, _capacity, StructureInstance.Stride) { name = t.Id + "_structures" },
                Args = Args(t, t.Id + "_structure_args"),
                Fill = new Material(_lib.Fill) { name = t.Id + "_fill", enableInstancing = false },
                Lines = new Material(_lib.Lines) { name = t.Id + "_lines", enableInstancing = false },
                Block = new MaterialPropertyBlock(),
            };
            s.Block.SetBuffer(IdInstances, s.Instances);
            s.Block.SetBuffer(IdPieces, _identity);
            s.Block.SetFloat(IdPass.Id, IdPass.Structure);
            return s;
        }

        /// <summary>A type's indirect commands, fills then lines, with no instances yet.</summary>
        internal static GraphicsBuffer Args(StructureType t, string name)
        {
            var args = new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments | GraphicsBuffer.Target.Raw, 2 * ArgsInts, 4) { name = name };
            args.SetData(new uint[]
            {
                t.Fill.GetIndexCount(0), 0, t.Fill.GetIndexStart(0), t.Fill.GetBaseVertex(0), 0,
                t.Lines.GetIndexCount(0), 0, t.Lines.GetIndexStart(0), t.Lines.GetBaseVertex(0), 0,
            });
            return args;
        }

        /// <summary>Sets both commands' instance counts.</summary>
        internal static void SetCount(GraphicsBuffer args, int count)
        {
            var n = new[] { (uint)count };
            args.SetData(n, 0, 1, 1);
            args.SetData(n, 0, ArgsInts + 1, 1);
        }

        /// <summary>Structure <paramref name="id"/> of library type <paramref name="type"/> stands at <paramref name="at"/> (ground x, z), turned <paramref name="angle"/>.</summary>
        public void Add(int id, int type, Vector2 at, float angle)
        {
            Remove(id);
            var s = _types[type];
            if (s.Count >= _capacity) throw new InvalidOperationException($"StructureRenderer: more than {_capacity} {s.Type.Id}s standing");
            int slot = s.Count++;
            s.Cpu[slot] = new StructureInstance { Position = new Vector3(at.x, 0, at.y), Angle = angle, FlashAt = -1e9f, Fade = 1 };
            s.Ids[slot] = id;
            s.Dirty = true;
            _where[id] = (type, slot);
        }

        /// <summary>Takes structure <paramref name="id"/> away (unloaded, or broken: its shards take its place); false if it wasn't standing.</summary>
        public bool Remove(int id)
        {
            if (!_where.TryGetValue(id, out var w)) return false;
            _where.Remove(id);
            var s = _types[w.Type];
            int last = --s.Count;
            if (w.Slot != last)
            {
                s.Cpu[w.Slot] = s.Cpu[last];
                s.Ids[w.Slot] = s.Ids[last];
                _where[s.Ids[w.Slot]] = (w.Type, w.Slot);
            }
            s.Dirty = true;
            return true;
        }

        /// <summary>Where structure <paramref name="id"/> stands and what it is, if it stands.</summary>
        public bool TryGet(int id, out int type, out StructureInstance instance)
        {
            if (!_where.TryGetValue(id, out var w))
            {
                type = -1;
                instance = default;
                return false;
            }
            type = w.Type;
            instance = _types[w.Type].Cpu[w.Slot];
            return true;
        }

        /// <summary>Structure <paramref name="id"/> was hit at song time <paramref name="t"/>: it flashes at <paramref name="strength"/>.</summary>
        public void Flash(int id, double t, float strength)
        {
            if (!_where.TryGetValue(id, out var w)) return;
            var s = _types[w.Type];
            s.Cpu[w.Slot].FlashAt = (float)t;
            s.Cpu[w.Slot].Flash = strength;
            s.Dirty = true;
        }

        /// <summary>Uploads what changed and queues this frame's draws for the camera.</summary>
        public void Draw(Camera camera)
        {
            Look.Apply();
            foreach (var s in _types)
            {
                if (!s.Dirty) continue;
                s.Dirty = false;
                if (s.Count > 0) s.Instances.SetData(s.Cpu, 0, 0, s.Count);
                if (s.Count != s.Drawn) SetCount(s.Args, s.Count);
                s.Drawn = s.Count;
            }
            Queue(camera);
        }

        /// <summary>Queues the latest draws for the camera again (a hand-drawn camera's second drawing in a frame: the stills' ID pass).</summary>
        public void Queue(Camera camera)
        {
            var bounds = new Bounds(Vector3.zero, Vector3.one * 100000f);
            foreach (var s in _types)
            {
                if (s.Drawn == 0) continue;
                Graphics.RenderMeshIndirect(EdgeCrowd.Params(s.Fill, s.Block, bounds, camera), s.Type.Fill, s.Args, 1, 0);
                Graphics.RenderMeshIndirect(EdgeCrowd.Params(s.Lines, s.Block, bounds, camera), s.Type.Lines, s.Args, 1, 1);
            }
        }

        public void Dispose()
        {
            _identity.Dispose();
            foreach (var s in _types)
            {
                s.Instances.Dispose();
                s.Args.Dispose();
                EdgeCrowd.Destroy(s.Fill);
                EdgeCrowd.Destroy(s.Lines);
            }
        }
    }
}
