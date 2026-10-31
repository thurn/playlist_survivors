using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace Game.Client.Effects
{
    /// <summary>
    /// The sword's crystal waves (unity-game.md, The Sword: the visible strike shows its radius): a
    /// flat ring of crystal light on the ground that rings out from the hero to the strike's reach
    /// in every direction, and a heavy strike's cleave wave following its bands outward. Each wave
    /// is a radius over song time, piecewise linear through its keys, drawn as one annulus mesh
    /// whose radius and width the shader (Game/CrystalWave) takes per draw. The front is exactly at
    /// the radius the sim hits, so the ring shows what the strike reaches.
    /// </summary>
    public sealed class CrystalWaves : IDisposable
    {
        public const int Segments = 128, Capacity = 64, MaxKeys = 10;
        /// <summary>How long a strike's ring takes to reach its radius, and how long a wave fades after its last key.</summary>
        public const double RingSeconds = 0.12, FadeSeconds = 0.18;
        public const float Height = 0.06f;

        struct Wave
        {
            public Vector2 Centre;
            public int Keys;
            public float Width, Intensity;
        }

        // Wave i's keys are at i * MaxKeys in the key arrays.
        readonly Wave[] _waves = new Wave[Capacity];
        readonly double[] _times = new double[Capacity * MaxKeys];
        readonly float[] _radii = new float[Capacity * MaxKeys];
        int _count;
        readonly Mesh _mesh;
        readonly Material _material;
        readonly MaterialPropertyBlock _block = new MaterialPropertyBlock();
        readonly Camera _camera;
        static readonly int RadiusId = Shader.PropertyToID("_Radius"), WidthId = Shader.PropertyToID("_Width"),
            IntensityId = Shader.PropertyToID("_Intensity"), AgeId = Shader.PropertyToID("_Age");

        public int Active => _count;

        public CrystalWaves(Camera camera, Shader shader)
        {
            _camera = camera;
            _material = new Material(shader) { name = "Crystal wave" };
            _mesh = Annulus();
        }

        /// <summary>A strike's ring: from the hero at the impact to <paramref name="reach"/> over <see cref="RingSeconds"/>.</summary>
        public void Strike(double impact, Vector2 centre, float reach, bool heavy)
        {
            Span<double> t = stackalloc double[] { impact, impact + RingSeconds };
            Span<float> r = stackalloc float[] { 0, reach };
            Add(centre, t, r, heavy ? 1.0f : 0.7f, heavy ? 1.2f : 0.8f);
        }

        /// <summary>
        /// A cleave wave: from <paramref name="inner"/> as the strike's ring gets there, through each
        /// band's radius at its time.
        /// </summary>
        public void Cleave(double impact, Vector2 centre, float inner, float outer, int bands, double firstBand, double bandSeconds)
        {
            int n = Math.Min(bands, MaxKeys - 1);
            Span<double> t = stackalloc double[n + 1];
            Span<float> r = stackalloc float[n + 1];
            t[0] = impact + RingSeconds;
            r[0] = inner;
            float band = (outer - inner) / Math.Max(1, bands);
            for (int k = 0; k < n; k++)
            {
                t[k + 1] = firstBand + k * bandSeconds;
                r[k + 1] = Math.Min(inner + (k + 1) * band, outer);
            }
            Add(centre, t, r, 0.9f, 0.8f);
        }

        void Add(Vector2 centre, ReadOnlySpan<double> t, ReadOnlySpan<float> r, float width, float intensity)
        {
            if (_count == Capacity) Remove(0); // the oldest
            int i = _count++;
            _waves[i] = new Wave { Centre = centre, Keys = t.Length, Width = width, Intensity = intensity };
            for (int k = 0; k < t.Length; k++)
            {
                _times[i * MaxKeys + k] = t[k];
                _radii[i * MaxKeys + k] = r[k];
            }
        }

        /// <summary>Draws every wave alive at song time <paramref name="now"/>, and forgets those done.</summary>
        public void Present(double now)
        {
            for (int i = 0; i < _count;)
            {
                double start = _times[i * MaxKeys], end = _times[i * MaxKeys + _waves[i].Keys - 1];
                if (now >= end + FadeSeconds) { Remove(i); continue; }
                if (now >= start) Draw(i, now, start, end);
                i++;
            }
        }

        void Draw(int i, double now, double start, double end)
        {
            var w = _waves[i];
            float radius = RadiusAt(i, now);
            // Brightest as it sets out; it fades through its travel and after its last key.
            float travel = (float)((now - start) / Math.Max(1e-3, end + FadeSeconds - start));
            _block.SetFloat(RadiusId, radius);
            _block.SetFloat(WidthId, Mathf.Min(w.Width, radius));
            _block.SetFloat(IntensityId, w.Intensity * (1f - travel) * (1f - travel));
            _block.SetFloat(AgeId, (float)(now - start));
            var rp = new RenderParams(_material)
            {
                camera = _camera,
                matProps = _block,
                shadowCastingMode = ShadowCastingMode.Off,
                receiveShadows = false,
                worldBounds = new Bounds(new Vector3(w.Centre.x, Height, w.Centre.y), Vector3.one * (2 * radius + 2)),
            };
            Graphics.RenderMesh(rp, _mesh, 0, Matrix4x4.Translate(new Vector3(w.Centre.x, Height, w.Centre.y)));
        }

        /// <summary>Wave <paramref name="i"/>'s radius at song time <paramref name="t"/>: linear between its keys, held after the last.</summary>
        public float RadiusAt(int i, double t)
        {
            int at = i * MaxKeys, keys = _waves[i].Keys;
            if (t <= _times[at]) return _radii[at];
            for (int k = 1; k < keys; k++)
            {
                double ta = _times[at + k - 1], tb = _times[at + k];
                if (t <= tb) return _radii[at + k - 1] + (_radii[at + k] - _radii[at + k - 1]) * (float)((t - ta) / Math.Max(1e-6, tb - ta));
            }
            return _radii[at + keys - 1];
        }

        void Remove(int i)
        {
            int last = --_count;
            _waves[i] = _waves[last];
            Array.Copy(_times, last * MaxKeys, _times, i * MaxKeys, MaxKeys);
            Array.Copy(_radii, last * MaxKeys, _radii, i * MaxKeys, MaxKeys);
        }

        /// <summary>
        /// A ring of <see cref="Segments"/> quads: each vertex's (cos, sin) of its angle in the
        /// position's xz and 0 (inner edge) or 1 (outer edge) in its y; uv = (angle / 2pi, edge).
        /// The shader places it at the draw's radius and width.
        /// </summary>
        static Mesh Annulus()
        {
            var verts = new Vector3[(Segments + 1) * 2];
            var uv = new Vector2[verts.Length];
            var tris = new int[Segments * 6];
            for (int i = 0; i <= Segments; i++)
            {
                float a = 2 * Mathf.PI * i / Segments;
                for (int e = 0; e < 2; e++)
                {
                    verts[i * 2 + e] = new Vector3(Mathf.Cos(a), e, Mathf.Sin(a));
                    uv[i * 2 + e] = new Vector2((float)i / Segments, e);
                }
                if (i == Segments) break;
                int b = i * 2, q = i * 6;
                tris[q] = b; tris[q + 1] = b + 1; tris[q + 2] = b + 2;
                tris[q + 3] = b + 2; tris[q + 4] = b + 1; tris[q + 5] = b + 3;
            }
            var mesh = new Mesh { name = "Crystal wave ring", vertices = verts, uv = uv, triangles = tris };
            mesh.bounds = new Bounds(Vector3.zero, new Vector3(2, 2, 2));
            return mesh;
        }

        public void Dispose()
        {
            if (Application.isPlaying)
            {
                UnityEngine.Object.Destroy(_material);
                UnityEngine.Object.Destroy(_mesh);
            }
            else
            {
                UnityEngine.Object.DestroyImmediate(_material);
                UnityEngine.Object.DestroyImmediate(_mesh);
            }
        }
    }
}
