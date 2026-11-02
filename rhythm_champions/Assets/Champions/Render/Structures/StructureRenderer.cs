using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Champions.Render.Structures
{
    /// <summary>
    /// The standing structures on screen (design.md, The World &gt; Structures; plan.md, Rendering: StructureEdge): each
    /// type's instances in one buffer, drawn with one indirect draw of its body in the edge look over the zone's fill. The
    /// presenter adds and removes structures by the sim's ids as their chunks load and unload and they break, and flashes
    /// them when hit; a type's buffer is uploaded only on a frame that changed it.
    /// </summary>
    public sealed class StructureRenderer : IDisposable
    {
        const int ArgsInts = 5;
        static readonly int IdInstances = Shader.PropertyToID("_StructureInstances");

        readonly StructureLibrary _lib;
        readonly TypeState[] _types;
        readonly int _capacity;
        readonly Dictionary<int, (int Type, int Slot)> _where = new Dictionary<int, (int, int)>();

        sealed class TypeState
        {
            public StructureType Type;
            public Material Material;
            public MaterialPropertyBlock Block;
            public GraphicsBuffer Instances, Args;
            public StructureInstance[] Cpu;
            public int[] Ids;
            public int Count, Drawn;
            public bool Dirty;
        }

        /// <summary>Every type in the library in <paramref name="gains"/>, each with room for <paramref name="capacityPerType"/> standing structures.</summary>
        public StructureRenderer(StructureLibrary lib, int capacityPerType, StructureGains gains)
        {
            _lib = lib;
            _capacity = capacityPerType;
            _types = new TypeState[lib.Types.Length];
            for (int i = 0; i < _types.Length; i++)
            {
                var t = lib.Types[i];
                var s = new TypeState
                {
                    Type = t,
                    Cpu = new StructureInstance[_capacity],
                    Ids = new int[_capacity],
                    Instances = new GraphicsBuffer(GraphicsBuffer.Target.Structured, _capacity, StructureInstance.Stride) { name = t.Id + "_structures" },
                    Args = Args(t.Body, t.Id + "_structure_args"),
                    Material = gains.Material(lib.Body, t.Id, shards: false),
                    Block = new MaterialPropertyBlock(),
                };
                s.Block.SetBuffer(IdInstances, s.Instances);
                s.Block.SetFloat(IdPass.Id, IdPass.Structure);
                _types[i] = s;
            }
        }

        public StructureLibrary Library => _lib;

        public int Standing => _where.Count;

        /// <summary>A mesh's indirect command, with no instances yet.</summary>
        internal static GraphicsBuffer Args(Mesh mesh, string name)
        {
            var args = new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments | GraphicsBuffer.Target.Raw, ArgsInts, 4) { name = name };
            args.SetData(new uint[] { mesh.GetIndexCount(0), 0, mesh.GetIndexStart(0), mesh.GetBaseVertex(0), 0 });
            return args;
        }

        /// <summary>Sets the command's instance count.</summary>
        internal static void SetCount(GraphicsBuffer args, int count) => args.SetData(new[] { (uint)count }, 0, 1, 1);

        /// <summary>How the structures' draws go to a camera: opaque, unlit, no shadows or motion vectors.</summary>
        internal static RenderParams Params(Material material, MaterialPropertyBlock block, Camera camera) => new RenderParams(material)
        {
            worldBounds = new Bounds(Vector3.zero, Vector3.one * 100000f),
            matProps = block,
            shadowCastingMode = ShadowCastingMode.Off,
            receiveShadows = false,
            motionVectorMode = MotionVectorGenerationMode.ForceNoMotion,
            lightProbeUsage = LightProbeUsage.Off,
            reflectionProbeUsage = ReflectionProbeUsage.Off,
            camera = camera,
            layer = camera != null ? camera.gameObject.layer : 0,
        };

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
            foreach (var s in _types)
                if (s.Drawn > 0) Graphics.RenderMeshIndirect(Params(s.Material, s.Block, camera), s.Type.Body, s.Args);
        }

        public void Dispose()
        {
            foreach (var s in _types)
            {
                s.Instances.Dispose();
                s.Args.Dispose();
                Destroy(s.Material);
            }
        }

        /// <summary>Destroys an object in play mode or in an EditMode test.</summary>
        internal static void Destroy(UnityEngine.Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(o);
            else UnityEngine.Object.DestroyImmediate(o);
        }
    }
}
