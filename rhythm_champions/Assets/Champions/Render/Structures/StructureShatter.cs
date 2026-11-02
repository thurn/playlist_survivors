using System;
using System.Collections.Generic;
using Champions.Render.Crowd;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Champions.Render.Structures
{
    /// <summary>
    /// Structures breaking (design.md, The Look &gt; Shatter: "structures break the same way in their fill colour"; plan.md,
    /// What Carries Over), ported by review from the look package's StructureShatter (its breaks only: no regrowth,
    /// glints or shadows, and no crack front):
    /// - <see cref="Launch"/> on the impact frame: the structure's edge-cut pieces become PhysX boxes in a local physics
    ///   scene of their own, all flying at once, thrown away from the blow and up, the nearest the hardest, tumbling. The
    ///   fill and the edges are drawn on them by the structure shaders, the fill in the zone's colour where each piece
    ///   flies, flashing on the break.
    /// - The physics steps in fixed steps of <see cref="Step"/> on the song clock, one frame ahead of it
    ///   (<see cref="MotionLead"/>), so the impact frame already shows a frame's flight and `--fixed-time` runs repeat.
    ///   The pieces clatter down, and after <see cref="LingerSeconds"/> sink into the ground and go. Nothing blocks: the
    ///   sim's structure is gone.
    /// - At most the quality tier's shard pieces fly (<see cref="QualityTiers"/>): a break that would pass it retires the
    ///   oldest first.
    /// </summary>
    public sealed class StructureShatter : IDisposable
    {
        public const float Step = 1f / 120f;
        public const int MaxStepsPerFrame = 12;
        /// <summary>Pieces are posed this far ahead of the frame's song time: the impact frame shows a frame of flight.</summary>
        public const double MotionLead = 1.0 / 60;
        public const float GravityScale = 2.2f, Density = 900f; // kg/m³: timber and plaster, stone and leaves
        public const float LingerSeconds = 2.0f, SinkSeconds = 1.0f;
        /// <summary>The flight at full power: outward from the blow, along it and up (m/s), and the tumble (rad/s).</summary>
        public const float Outward = 3.4f, Blow = 6.5f, Up = 3.2f, Spin = 6f;

        static readonly int IdInstances = Shader.PropertyToID("_StructureInstances");
        static readonly int IdPieces = Shader.PropertyToID("_StructurePieces");

        sealed class Piece
        {
            public GameObject Object;
            public Transform Transform;
            public Rigidbody Body;
            public BoxCollider Collider;
            public int Index;
            public float SinkFrom, SinkDepth;
        }

        sealed class Live
        {
            public int Type, Slot;
            public double At;
            public bool Sinking;
            public readonly List<Piece> Pieces = new List<Piece>();
        }

        sealed class TypeShards
        {
            public StructureType Type;
            public Material Fill, Lines;
            public MaterialPropertyBlock Block;
            public GraphicsBuffer Instances, Args;
            public StructureInstance[] Cpu;
            public int Count, Drawn;
        }

        readonly StructureLibrary _lib;
        readonly int _maxPieces, _slots;
        readonly GraphicsBuffer _pieceRows;
        readonly Vector4[] _rows;
        readonly TypeShards[] _types;
        readonly Live[] _bySlot;
        readonly List<Live> _live = new List<Live>();
        readonly Stack<Piece> _pool = new Stack<Piece>();
        readonly Scene _scene;
        readonly bool _ownScene;
        readonly PhysicsScene _physics;
        readonly GameObject _root;
        readonly PhysicsMaterial _material;
        double _physicsT = double.NaN;
        int _launchedSinceStep;

        /// <summary>
        /// Shatters <paramref name="lib"/>'s structures, up to <paramref name="slots"/> breaks flying at once, in a local
        /// physics scene of their own (play mode alone creates scenes) or in <paramref name="physicsScene"/>, which the
        /// caller then owns.
        /// </summary>
        public StructureShatter(StructureLibrary lib, int slots = 48, Scene? physicsScene = null)
        {
            _lib = lib;
            _slots = slots;
            _maxPieces = lib.MaxPieces;
            _rows = new Vector4[3 * _maxPieces * slots];
            _pieceRows = new GraphicsBuffer(GraphicsBuffer.Target.Structured, _rows.Length, 16) { name = "structure_shards" };
            _bySlot = new Live[slots];
            _types = new TypeShards[lib.Types.Length];
            for (int i = 0; i < _types.Length; i++)
            {
                var t = lib.Types[i];
                var s = new TypeShards
                {
                    Type = t,
                    Cpu = new StructureInstance[slots],
                    Instances = new GraphicsBuffer(GraphicsBuffer.Target.Structured, slots, StructureInstance.Stride) { name = t.Id + "_shards" },
                    Args = StructureRenderer.Args(t, t.Id + "_shard_args"),
                    Fill = new Material(lib.Fill) { name = t.Id + "_shard_fill", enableInstancing = false },
                    Lines = new Material(lib.Lines) { name = t.Id + "_shard_lines", enableInstancing = false },
                    Block = new MaterialPropertyBlock(),
                };
                s.Block.SetBuffer(IdInstances, s.Instances);
                s.Block.SetBuffer(IdPieces, _pieceRows);
                s.Block.SetFloat(IdPass.Id, IdPass.Effect);
                _types[i] = s;
            }
            _ownScene = physicsScene == null;
            _scene = physicsScene ?? SceneManager.CreateScene("StructureShatter", new CreateSceneParameters(LocalPhysicsMode.Physics3D));
            _physics = _scene.GetPhysicsScene();
            _root = new GameObject("StructureShatter");
            SceneManager.MoveGameObjectToScene(_root, _scene);
            _material = new PhysicsMaterial("rubble")
            {
                bounciness = 0.2f, dynamicFriction = 0.55f, staticFriction = 0.7f,
                bounceCombine = PhysicsMaterialCombine.Average, frictionCombine = PhysicsMaterialCombine.Average,
            };
            var floor = new GameObject("RubbleFloor");
            floor.transform.SetParent(_root.transform, false);
            var box = floor.AddComponent<BoxCollider>();
            box.size = new Vector3(4000, 1, 4000);
            box.center = new Vector3(0, -0.5f, 0);
            box.sharedMaterial = _material;
        }

        /// <summary>Breaks flying or sinking now, their pieces, and every break so far.</summary>
        public int Flying => _live.Count;
        public int Pieces { get; private set; }
        public int Breaks { get; private set; }

        /// <summary>
        /// Library type <paramref name="type"/>'s structure, standing at <paramref name="at"/> turned <paramref name="angle"/>
        /// (<see cref="StructureInstance"/>'s), breaks now, at song time <paramref name="t"/>: thrown along
        /// <paramref name="blow"/> (ground x, z, a unit vector) at <paramref name="power"/> (1 a strong blow's; held to 0.5 to
        /// 1.3). Call it on the frame the break shows.
        /// </summary>
        public void Launch(int type, Vector3 at, float angle, double t, Vector2 blow, float power)
        {
            var shards = _types[type];
            var pieces = shards.Type.Pieces;
            int cap = QualityTiers.Settings.ShardPieces;
            while (_live.Count > 0 && (Pieces + pieces.Length > cap || _live.Count >= _slots)) Retire(_live[0]);
            int slot = Array.IndexOf(_bySlot, null);
            var live = new Live { Type = type, Slot = slot, At = t };
            _bySlot[slot] = live;
            var rotation = Quaternion.Euler(0, -angle * Mathf.Rad2Deg, 0);
            var dir = new Vector3(blow.x, 0, blow.y);
            dir = dir.sqrMagnitude > 1e-6f ? dir.normalized : Vector3.zero;
            float strength = Mathf.Clamp(power, 0.5f, 1.3f);
            // The blow lands on the side it came from, a third of the way up: the pieces nearest it go hardest.
            float height = Mathf.Max(0.5f, shards.Type.HeightM);
            float reach = Mathf.Max(0.5f, 0.5f * shards.Type.ExtentM.magnitude);
            var strike = at - dir * (0.5f * reach) + Vector3.up * (0.33f * height);
            var rng = new System.Random(type * 7919 + (int)(t * 1000) ^ (int)(at.x * 31 + at.z * 17));
            for (int i = 0; i < pieces.Length; i++)
            {
                var src = pieces[i];
                var rest = at + rotation * src.Pivot;
                var p = Take();
                p.Index = i;
                p.Object.SetActive(true);
                p.Transform.SetPositionAndRotation(rest, rotation);
                p.Body.position = rest;
                p.Body.rotation = rotation;
                p.Collider.size = Vector3.Max(2 * src.HalfSize * 0.85f, Vector3.one * 0.05f);
                p.Collider.center = Vector3.zero;
                p.Body.mass = Mathf.Max(0.5f, src.Volume * Density);
                var away = rest - strike;
                float dist = away.magnitude;
                away = dist > 1e-4f ? away / dist : dir;
                away.y = Mathf.Max(away.y, -0.2f);
                float near = 1f - Mathf.Clamp01(dist / (reach + height));
                // Most fly; one in five slumps (never near the blow), one in eight flies far.
                double u = rng.NextDouble();
                float share = u < 0.2 && near < 0.6f ? 0.2f + 0.25f * (float)rng.NextDouble()
                            : u > 0.875 ? 1.4f + 0.6f * (float)rng.NextDouble()
                            : 0.6f + 0.5f * (float)rng.NextDouble();
                share *= (0.5f + near) * strength;
                p.Body.isKinematic = false;
                p.Collider.enabled = true;
                p.Body.linearVelocity = (away * Outward + dir * (Blow * (0.5f + (float)rng.NextDouble())) + Vector3.up * Up) * share;
                p.Body.angularVelocity = RandomUnit(rng) * Spin * (0.4f + 0.8f * (float)rng.NextDouble());
                live.Pieces.Add(p);
            }
            _live.Add(live);
            Pieces += pieces.Length;
            Breaks++;
            _launchedSinceStep++;
        }

        /// <summary>Brings the pieces to song time <paramref name="song"/> (and a step ahead); call once a frame, before <see cref="Draw"/>.</summary>
        public void Present(double song)
        {
            double now = song + MotionLead;
            if (double.IsNaN(_physicsT)) _physicsT = now;
            int n = 0;
            while (_physicsT + Step <= now + 1e-9)
            {
                if (n++ >= MaxStepsPerFrame)
                {
                    _physicsT = now; // a long stall: drop the steps it missed rather than catch up over many frames
                    break;
                }
                Advance(_physicsT);
                _physics.Simulate(Step);
                _physicsT += Step;
            }
            if (n == 0 && _launchedSinceStep > 0)
            {
                // A frame shorter than a step that brought a break: one step now, so its impact frame shows flight.
                Advance(_physicsT);
                _physics.Simulate(Step);
                _physicsT += Step;
            }
            _launchedSinceStep = 0;
            Pose(now);
        }

        /// <summary>Before a physics step at physics time <paramref name="now"/>: gravity, and the sinking once a break has lingered.</summary>
        void Advance(double now)
        {
            var g = Vector3.down * (9.81f * GravityScale);
            foreach (var live in _live)
            {
                if (!live.Sinking && now - live.At >= LingerSeconds)
                {
                    live.Sinking = true;
                    foreach (var p in live.Pieces)
                    {
                        p.Body.isKinematic = true;
                        p.Collider.enabled = false;
                        p.SinkFrom = p.Transform.position.y;
                        p.SinkDepth = 2f * p.Collider.size.magnitude + Mathf.Max(0, p.SinkFrom);
                    }
                }
                if (live.Sinking) continue;
                foreach (var p in live.Pieces) p.Body.AddForce(g, ForceMode.Acceleration);
            }
        }

        /// <summary>The pieces' transforms and each type's breaks at physics time <paramref name="now"/>; spent breaks go back to the pool.</summary>
        void Pose(double now)
        {
            foreach (var s in _types) s.Count = 0;
            for (int i = _live.Count - 1; i >= 0; i--)
            {
                var live = _live[i];
                float sink = live.Sinking ? Mathf.Clamp01((float)(now - live.At - LingerSeconds) / SinkSeconds) : 0;
                if (sink >= 1f)
                {
                    Retire(live);
                    continue;
                }
                var pieces = _types[live.Type].Type.Pieces;
                int baseRow = 3 * live.Slot * _maxPieces;
                foreach (var p in live.Pieces)
                {
                    if (live.Sinking)
                    {
                        var at = p.Transform.position;
                        p.Transform.position = new Vector3(at.x, p.SinkFrom - sink * sink * p.SinkDepth, at.z);
                    }
                    var m = Matrix4x4.TRS(p.Transform.position, p.Transform.rotation, Vector3.one) * Matrix4x4.Translate(-pieces[p.Index].Pivot);
                    int r = baseRow + 3 * p.Index;
                    _rows[r] = m.GetRow(0);
                    _rows[r + 1] = m.GetRow(1);
                    _rows[r + 2] = m.GetRow(2);
                }
                var shards = _types[live.Type];
                shards.Cpu[shards.Count++] = new StructureInstance
                {
                    Position = Vector3.zero, Angle = 0, FlashAt = (float)live.At, Flash = 1, PieceBase = (uint)(live.Slot * _maxPieces), Fade = 1 - sink,
                };
            }
            if (_live.Count > 0) _pieceRows.SetData(_rows);
            foreach (var s in _types)
            {
                if (s.Count > 0) s.Instances.SetData(s.Cpu, 0, 0, s.Count);
                if (s.Count != s.Drawn) StructureRenderer.SetCount(s.Args, s.Count);
                s.Drawn = s.Count;
            }
        }

        void Retire(Live live)
        {
            foreach (var p in live.Pieces)
            {
                p.Object.SetActive(false);
                p.Body.isKinematic = true;
                _pool.Push(p);
            }
            Pieces -= live.Pieces.Count;
            live.Pieces.Clear();
            _bySlot[live.Slot] = null;
            _live.Remove(live);
        }

        /// <summary>Queues the flying pieces' draws for the camera (again, for a second drawing of the frame).</summary>
        public void Draw(Camera camera)
        {
            var bounds = new Bounds(Vector3.zero, Vector3.one * 100000f);
            foreach (var s in _types)
            {
                if (s.Drawn == 0) continue;
                Graphics.RenderMeshIndirect(EdgeCrowd.Params(s.Fill, s.Block, bounds, camera), s.Type.Fill, s.Args, 1, 0);
                Graphics.RenderMeshIndirect(EdgeCrowd.Params(s.Lines, s.Block, bounds, camera), s.Type.Lines, s.Args, 1, 1);
            }
        }

        Piece Take()
        {
            if (_pool.Count > 0) return _pool.Pop();
            var go = new GameObject("StructurePiece");
            go.transform.SetParent(_root.transform, false);
            var col = go.AddComponent<BoxCollider>();
            col.sharedMaterial = _material;
            var rb = go.AddComponent<Rigidbody>();
            rb.useGravity = false;
            rb.interpolation = RigidbodyInterpolation.None;
            rb.maxDepenetrationVelocity = 2f;
            go.SetActive(false);
            return new Piece { Object = go, Transform = go.transform, Body = rb, Collider = col };
        }

        static Vector3 RandomUnit(System.Random rng)
        {
            float z = 2f * (float)rng.NextDouble() - 1f, a = 2f * Mathf.PI * (float)rng.NextDouble();
            float r = Mathf.Sqrt(1f - z * z);
            return new Vector3(r * Mathf.Cos(a), z, r * Mathf.Sin(a));
        }

        public void Dispose()
        {
            _pieceRows.Dispose();
            foreach (var s in _types)
            {
                s.Instances.Dispose();
                s.Args.Dispose();
                EdgeCrowd.Destroy(s.Fill);
                EdgeCrowd.Destroy(s.Lines);
            }
            EdgeCrowd.Destroy(_root);
            EdgeCrowd.Destroy(_material);
            if (_ownScene && _scene.IsValid() && _scene.isLoaded) SceneManager.UnloadSceneAsync(_scene);
        }
    }
}
