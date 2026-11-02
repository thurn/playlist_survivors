using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Champions.Render
{
    /// <summary>
    /// The hero's sparks on the GPU (plan.md, Rendering &gt; Pipeline: Particle). Each burst takes the next slots of one
    /// ring; on the frame it is due, one dispatch (a thread group per burst) seeds its sparks (Sparks.compute), and one
    /// procedural draw (Spark.shader) shows the ring's live span, each spark placed in closed form from its birth. So a
    /// burst costs the CPU one record, whatever its count, and a frame with no new bursts no compute. Every spark lives
    /// at most <see cref="MaxLife"/>; a ring full of live sparks gives up its oldest.
    /// </summary>
    public sealed class Sparks : IDisposable
    {
        public const int Capacity = 16384, MaxBursts = 256;
        public const float MaxLife = 1.5f;
        const int SparkStride = 48;

        static readonly int IdBursts = Shader.PropertyToID("_Bursts");
        static readonly int IdSparks = Shader.PropertyToID("_Sparks");
        static readonly int IdCapacity = Shader.PropertyToID("_Capacity");
        static readonly int IdFirst = Shader.PropertyToID("_First");
        static readonly int IdNow = Shader.PropertyToID("_EffectNow");

        readonly ComputeShader _cs;
        readonly int _kSeed;
        readonly GraphicsBuffer _bursts, _sparks;
        readonly Material _material;
        readonly MaterialPropertyBlock _block = new MaterialPropertyBlock();
        readonly CommandBuffer _cmd = new CommandBuffer { name = "Sparks" };
        readonly List<SparkBurst> _pending = new List<SparkBurst>();
        // Live bursts in the order they took their slots: (first slot, count, end).
        readonly Queue<(int First, int Count, double End)> _live = new Queue<(int, int, double)>();
        int _next;
        uint _seed = 1;

        /// <summary>Sparks seeded so far, and the seed dispatches that did it.</summary>
        public int Seeded { get; private set; }
        public int Dispatches { get; private set; }

        /// <summary>The span of the ring the latest frame drew.</summary>
        public int Drawn { get; private set; }

        public Sparks(ComputeShader seed, Shader draw, Color core, Color edge)
        {
            _cs = seed;
            _kSeed = seed.FindKernel("Seed");
            _bursts = new GraphicsBuffer(GraphicsBuffer.Target.Structured, MaxBursts, SparkBurst.Stride) { name = "SparkBursts" };
            _sparks = new GraphicsBuffer(GraphicsBuffer.Target.Structured, Capacity, SparkStride) { name = "Sparks" };
            // Unseeded slots must never draw: born in the far future.
            var empty = new float[Capacity * SparkStride / 4];
            for (int i = 0; i < Capacity; i++) empty[i * 12 + 3] = float.MaxValue;
            _sparks.SetData(empty);
            _material = new Material(draw) { name = "Sparks" };
            _material.SetColor("_CoreColor", core.linear);
            _material.SetColor("_EdgeColor", edge.linear);
            _material.SetFloat(IdPass.Id, IdPass.Effect);
            _block.SetBuffer(IdSparks, _sparks);
            _block.SetInt(IdCapacity, Capacity);
        }

        /// <summary>
        /// A burst of <paramref name="count"/> sparks from <paramref name="origin"/> (world) at song time <paramref name="born"/>,
        /// thrown within <paramref name="spread"/> radians of <paramref name="direction"/>.
        /// </summary>
        public void Burst(Vector3 origin, double born, Vector3 direction, float spread, int count, Vector2 speed, Vector2 life,
            float size, float drag, float gravity, float up, float brightness)
        {
            count = Mathf.Clamp(count, 0, Capacity / 4);
            if (count == 0 || _pending.Count == MaxBursts) return;
            _pending.Add(new SparkBurst
            {
                Origin = origin, Born = (float)born, Direction = direction.sqrMagnitude > 1e-8f ? direction.normalized : Vector3.up,
                Spread = spread, Count = count, First = _next, Seed = _seed++ * 2654435761u % 16777216u, Brightness = brightness,
                Speed = speed, Life = new Vector2(Mathf.Min(life.x, MaxLife), Mathf.Min(life.y, MaxLife)), Size = size, Drag = drag,
                Gravity = gravity, Up = up,
            });
            _live.Enqueue((_next, count, born + Mathf.Min(life.y, MaxLife)));
            _next = (_next + count) % Capacity;
        }

        /// <summary>Seeds the frame's bursts and queues the draw of every live spark at song time <paramref name="now"/>.</summary>
        public void Present(double now, Camera camera)
        {
            if (_pending.Count > 0)
            {
                _cmd.Clear();
                _cmd.SetBufferData(_bursts, _pending);
                _cmd.SetComputeIntParam(_cs, IdCapacity, Capacity);
                _cmd.SetComputeBufferParam(_cs, _kSeed, IdBursts, _bursts);
                _cmd.SetComputeBufferParam(_cs, _kSeed, IdSparks, _sparks);
                _cmd.DispatchCompute(_cs, _kSeed, _pending.Count, 1, 1);
                Graphics.ExecuteCommandBuffer(_cmd);
                foreach (var b in _pending) Seeded += (int)b.Count;
                Dispatches++;
                _pending.Clear();
            }
            while (_live.Count > 0 && _live.Peek().End < now) _live.Dequeue();
            int span = 0, first = 0;
            if (_live.Count > 0)
            {
                first = _live.Peek().First;
                span = (_next - first + Capacity) % Capacity;
                if (span == 0) span = Capacity; // the ring is full
            }
            Drawn = span;
            _block.SetInt(IdFirst, first);
            _block.SetFloat(IdNow, (float)now);
            if (camera != null) Queue(camera);
        }

        /// <summary>Queues the draw again (a second drawing of the same frame: the stills' ID pass).</summary>
        public void Queue(Camera camera)
        {
            if (Drawn == 0) return;
            var rp = Effects.Params(_material, _block, camera);
            Graphics.RenderPrimitives(rp, MeshTopology.Triangles, 6, Drawn);
        }

        public void Clear()
        {
            _pending.Clear();
            _live.Clear();
            Drawn = 0;
        }

        public void Dispose()
        {
            _cmd.Release();
            _bursts.Dispose();
            _sparks.Dispose();
            Effects.Destroy(_material);
        }
    }
}
