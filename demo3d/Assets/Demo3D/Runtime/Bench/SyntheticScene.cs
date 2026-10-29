using System;
using System.Collections.Generic;
using Demo3D.Bench;
using UnityEngine;
using UnityEngine.Rendering;

namespace Demo3D.Runtime.Bench
{
    /// <summary>
    /// TD7's synthetic load, until TD4's stress scenes exist: N instances of one procedural capsule
    /// (monster-sized, 0.4 m radius, 1.8 m tall), GPU-instanced with RenderMeshInstanced, casting and
    /// receiving the sun's shadows, each walking a small circle around an anchor spread over the
    /// view's visible ground. No sim, so no tick timing. A nominal 120 bpm gives it a downbeat every
    /// 2 s for the beat-frame overdraw counter.
    ///
    /// sceneOptions: <c>triangles</c> (the capsule's triangle count, default 4000), <c>glow</c>
    /// (an additive camera-facing quad over each instance, to exercise the transparent counter;
    /// default false), <c>seed</c> (default 7).
    /// </summary>
    public sealed class SyntheticScene : IBenchScene
    {
        public const float Radius = 0.4f, Height = 1.8f;
        const int Chunk = 1023;
        const float WalkRadius = 0.6f, WalkSpeed = 1.2f, DownbeatSeconds = 2f;

        BenchContext _ctx;
        Mesh _mesh, _quad, _ground;
        Matrix4x4 _groundMatrix;
        int _triangles = 4000, _seed = 7;
        bool _glow;
        int _n;
        Vector2[] _anchors = new Vector2[0];
        float[] _phase = new float[0];
        readonly List<Matrix4x4[]> _chunks = new List<Matrix4x4[]>();
        readonly List<Matrix4x4[]> _glowChunks = new List<Matrix4x4[]>();
        readonly Plane[] _planes = new Plane[6];
        Bounds _bounds;

        public double? PackingPerM2 => null;

        public void Load(BenchContext ctx)
        {
            _ctx = ctx;
            if (ctx.Options.TryGetValue("triangles", out var t) && t is double td) _triangles = (int)td;
            if (ctx.Options.TryGetValue("glow", out var g) && g is bool gb) _glow = gb;
            if (ctx.Options.TryGetValue("seed", out var s) && s is double sd) _seed = (int)sd;
            _mesh = BuildCapsule(_triangles);
            _quad = BuildQuad();
            _ground = BuildQuad();
            _groundMatrix = Matrix4x4.TRS(Vector3.zero, Quaternion.Euler(90, 0, 0), new Vector3(600, 600, 1));
            var ground = new GameObject("Ground");
            ground.transform.SetParent(ctx.Root, false);
            ground.AddComponent<MeshFilter>().sharedMesh = _ground;
            var mr = ground.AddComponent<MeshRenderer>();
            mr.sharedMaterial = ctx.Assets.Ground;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = true;
            ground.transform.localRotation = Quaternion.Euler(90, 0, 0);
            ground.transform.localScale = new Vector3(600, 600, 1);
        }

        public void Configure(Point point) { }

        public SimTickStats Rebuild(int n)
        {
            _n = n;
            _anchors = SpreadOver(_ctx.Footprint, n, _seed);
            var rng = new System.Random(_seed + 1);
            _phase = new float[n];
            for (int i = 0; i < n; i++) _phase[i] = (float)(rng.NextDouble() * Math.PI * 2);
            Resize(_chunks, n);
            Resize(_glowChunks, _glow ? n : 0);
            float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
            foreach (var a in _anchors)
            {
                minX = Mathf.Min(minX, a.x); maxX = Mathf.Max(maxX, a.x);
                minZ = Mathf.Min(minZ, a.y); maxZ = Mathf.Max(maxZ, a.y);
            }
            if (n == 0) minX = maxX = minZ = maxZ = 0;
            float m = WalkRadius + Radius + 1;
            _bounds = new Bounds(new Vector3((minX + maxX) / 2, Height / 2 + 0.5f, (minZ + maxZ) / 2),
                                 new Vector3(maxX - minX + 2 * m, Height + 3, maxZ - minZ + 2 * m));
            return null;
        }

        static void Resize(List<Matrix4x4[]> chunks, int n)
        {
            chunks.Clear();
            for (int i = 0; i < n; i += Chunk) chunks.Add(new Matrix4x4[Math.Min(Chunk, n - i)]);
        }

        public void Tick(double t)
        {
            float w = WalkSpeed / WalkRadius;
            var camRot = _ctx.Camera.transform.rotation;
            for (int i = 0; i < _n; i++)
            {
                float a = _phase[i] + w * (float)t;
                float c = Mathf.Cos(a), s = Mathf.Sin(a);
                var pos = new Vector3(_anchors[i].x + WalkRadius * c, Height / 2, _anchors[i].y + WalkRadius * s);
                // Facing the direction of travel: the circle's tangent (-sin, cos).
                var rot = Quaternion.LookRotation(new Vector3(-s, 0, c));
                _chunks[i / Chunk][i % Chunk] = Matrix4x4.TRS(pos, rot, Vector3.one);
                if (_glow)
                    _glowChunks[i / Chunk][i % Chunk] =
                        Matrix4x4.TRS(pos + new Vector3(0, Height * 0.6f, 0), camRot, new Vector3(1.4f, 1.4f, 1));
            }
            var rp = new RenderParams(_ctx.Assets.Unit)
            {
                worldBounds = _bounds,
                shadowCastingMode = ShadowCastingMode.On,
                receiveShadows = true,
            };
            foreach (var chunk in _chunks) Graphics.RenderMeshInstanced(rp, _mesh, 0, chunk, chunk.Length);
            if (_glow)
            {
                var gp = new RenderParams(_ctx.Assets.Glow)
                {
                    worldBounds = _bounds,
                    shadowCastingMode = ShadowCastingMode.Off,
                    receiveShadows = false,
                };
                foreach (var chunk in _glowChunks) Graphics.RenderMeshInstanced(gp, _quad, 0, chunk, chunk.Length);
            }
        }

        public bool DownbeatIn(double t0, double t1) => Math.Floor(t1 / DownbeatSeconds) > Math.Floor(t0 / DownbeatSeconds);

        public void CollectCounters(SceneCounters c, bool viewFrame)
        {
            c.ShadowCasters = _n;
            c.OnScreenDying = 0;
            if (!viewFrame) return;
            GeometryUtility.CalculateFrustumPlanes(_ctx.Camera, _planes);
            int visible = 0;
            var size = new Vector3(2 * Radius, Height, 2 * Radius);
            for (int i = 0; i < _n; i++)
            {
                var m = _chunks[i / Chunk][i % Chunk];
                if (GeometryUtility.TestPlanesAABB(_planes, new Bounds(m.GetColumn(3), size))) visible++;
            }
            c.OnScreenAlive = visible;
            c.OffScreen = _n - visible;
        }

        public void DrawOverdraw(IOverdrawSink sink)
        {
            sink.Opaque(_ground, _groundMatrix);
            foreach (var chunk in _chunks) sink.Opaque(_mesh, chunk, chunk.Length);
            if (_glow)
                foreach (var chunk in _glowChunks) sink.Transparent(_quad, chunk, chunk.Length);
        }

        public void Dispose()
        {
            if (_mesh != null) UnityEngine.Object.Destroy(_mesh);
            if (_quad != null) UnityEngine.Object.Destroy(_quad);
            if (_ground != null) UnityEngine.Object.Destroy(_ground);
        }

        /// <summary>
        /// n anchors spread over a convex footprint (x, z pairs) by a jittered grid, so neighbours
        /// keep apart as monsters do, with the walk circle kept inside the footprint where it fits.
        /// </summary>
        public static Vector2[] SpreadOver(double[] footprint, int n, int seed)
        {
            var result = new Vector2[n];
            if (n == 0) return result;
            var poly = new Vector2[4];
            for (int i = 0; i < 4; i++) poly[i] = new Vector2((float)footprint[2 * i], (float)footprint[2 * i + 1]);
            // Shrink toward the centroid by the walk radius, so walkers stay on screen.
            var centroid = (poly[0] + poly[1] + poly[2] + poly[3]) / 4;
            for (int i = 0; i < 4; i++)
            {
                var d = poly[i] - centroid;
                poly[i] = centroid + d * Mathf.Max(0.5f, 1 - (WalkRadius + Radius) / d.magnitude);
            }
            float minX = Mathf.Min(poly[0].x, poly[3].x), maxX = Mathf.Max(poly[1].x, poly[2].x);
            float minZ = Mathf.Min(poly[0].y, poly[1].y), maxZ = Mathf.Max(poly[2].y, poly[3].y);
            double area = 0;
            for (int i = 0; i < 4; i++)
            {
                int j = (i + 1) % 4;
                area += poly[i].x * poly[j].y - poly[j].x * poly[i].y;
            }
            area = Math.Abs(area) / 2;
            var rng = new System.Random(seed);
            double spacing = Math.Sqrt(area / n);
            var cells = new List<Vector2>();
            for (int attempt = 0; attempt < 40; attempt++)
            {
                cells.Clear();
                for (double z = minZ + spacing / 2; z < maxZ; z += spacing)
                for (double x = minX + spacing / 2; x < maxX; x += spacing)
                {
                    var p = new Vector2((float)(x + (rng.NextDouble() - 0.5) * spacing * 0.6),
                                        (float)(z + (rng.NextDouble() - 0.5) * spacing * 0.6));
                    if (Inside(poly, p)) cells.Add(p);
                }
                if (cells.Count >= n) break;
                spacing *= 0.97;
            }
            // Keep a random n of the cells (Fisher-Yates prefix), so the crowd fills the whole view.
            for (int i = 0; i < n && i < cells.Count; i++)
            {
                int j = i + rng.Next(cells.Count - i);
                (cells[i], cells[j]) = (cells[j], cells[i]);
                result[i] = cells[i];
            }
            for (int i = cells.Count; i < n; i++) result[i] = centroid; // unreachable in practice
            return result;
        }

        static bool Inside(Vector2[] poly, Vector2 p)
        {
            for (int i = 0; i < poly.Length; i++)
            {
                var a = poly[i];
                var b = poly[(i + 1) % poly.Length];
                if ((b.x - a.x) * (p.y - a.y) - (b.y - a.y) * (p.x - a.x) < 0) return false;
            }
            return true;
        }

        /// <summary>A capsule of about <paramref name="triangles"/> triangles, base at y = -Height/2.</summary>
        public static Mesh BuildCapsule(int triangles)
        {
            // Longitude segments s, latitude rings r per hemisphere: triangles ≈ 2 s (2 r + 1).
            int s = Mathf.Max(8, Mathf.RoundToInt(Mathf.Sqrt(triangles / 2f)));
            int r = Mathf.Max(2, Mathf.RoundToInt((triangles / (2f * s) - 1) / 2f));
            var verts = new List<Vector3>();
            var normals = new List<Vector3>();
            float half = Height / 2 - Radius;
            // Rows from the top pole to the bottom pole; the equator row appears twice (cylinder).
            var rows = new List<(float y, float ring, float ny, float nr)>();
            for (int i = 0; i <= r; i++)
            {
                float a = Mathf.PI / 2 * (1 - (float)i / r);
                rows.Add((half + Radius * Mathf.Sin(a), Radius * Mathf.Cos(a), Mathf.Sin(a), Mathf.Cos(a)));
            }
            for (int i = 0; i <= r; i++)
            {
                float a = -Mathf.PI / 2 * i / r;
                rows.Add((-half + Radius * Mathf.Sin(a), Radius * Mathf.Cos(a), Mathf.Sin(a), Mathf.Cos(a)));
            }
            foreach (var row in rows)
                for (int j = 0; j <= s; j++)
                {
                    float phi = 2 * Mathf.PI * j / s;
                    float cx = Mathf.Cos(phi), cz = Mathf.Sin(phi);
                    verts.Add(new Vector3(row.ring * cx, row.y, row.ring * cz));
                    normals.Add(new Vector3(row.nr * cx, row.ny, row.nr * cz).normalized);
                }
            var tris = new List<int>();
            int stride = s + 1;
            for (int i = 0; i < rows.Count - 1; i++)
                for (int j = 0; j < s; j++)
                {
                    int a = i * stride + j, b = a + 1, c = a + stride, d = c + 1;
                    if (rows[i].ring > 1e-5f) { tris.Add(a); tris.Add(b); tris.Add(c); }
                    if (rows[i + 1].ring > 1e-5f) { tris.Add(b); tris.Add(d); tris.Add(c); }
                }
            var mesh = new Mesh { name = "SyntheticCapsule", indexFormat = IndexFormat.UInt32 };
            mesh.SetVertices(verts);
            mesh.SetNormals(normals);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            mesh.UploadMeshData(true);
            return mesh;
        }

        static Mesh BuildQuad()
        {
            var mesh = new Mesh { name = "BenchQuad" };
            mesh.SetVertices(new List<Vector3>
            {
                new Vector3(-0.5f, -0.5f, 0), new Vector3(0.5f, -0.5f, 0),
                new Vector3(-0.5f, 0.5f, 0), new Vector3(0.5f, 0.5f, 0),
            });
            mesh.SetNormals(new List<Vector3> { Vector3.back, Vector3.back, Vector3.back, Vector3.back });
            mesh.SetUVs(0, new List<Vector2> { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1) });
            mesh.SetTriangles(new[] { 0, 2, 1, 2, 3, 1 }, 0);
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
