using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Game.Client.Weapons
{
    /// <summary>
    /// One weapon's crystals for a frame, drawn instanced (Resources/Weapons: WeaponCrystal.shader,
    /// and WeaponCrystal.mat with instancing on): the crystals themselves, opaque, and their
    /// afterimages, additive and dimmer, so a moving crystal leaves a streak of light. Each frame the
    /// view adds its matrices, then <see cref="Draw"/> draws and clears them.
    /// </summary>
    public sealed class CrystalBatch : IDisposable
    {
        const int Batch = 1023;
        static readonly int ColorId = Shader.PropertyToID("_Color");
        static readonly int GlowId = Shader.PropertyToID("_Glow");

        readonly Mesh _mesh;
        readonly Material _core, _trail;
        readonly Matrix4x4[] _cores = new Matrix4x4[Batch], _trails = new Matrix4x4[Batch];
        int _coreCount, _trailCount;

        public CrystalBatch(Mesh mesh, Color colour, string name, float trailGlow = 0.45f)
        {
            _mesh = mesh;
            // An asset with instancing on, so the player keeps the shader's instanced variant.
            var crystal = Resources.Load<Material>("Weapons/WeaponCrystal");
            _core = new Material(crystal) { name = name };
            _core.SetColor(ColorId, colour);
            _trail = new Material(crystal) { name = name + "_trail", renderQueue = (int)RenderQueue.Transparent };
            _trail.SetColor(ColorId, colour);
            _trail.SetFloat(GlowId, trailGlow);
            _trail.SetFloat("_SrcBlend", (float)BlendMode.One);
            _trail.SetFloat("_DstBlend", (float)BlendMode.One);
            _trail.SetFloat("_ZWrite", 0);
        }

        public void Crystal(in Matrix4x4 m)
        {
            if (_coreCount < Batch) _cores[_coreCount++] = m;
        }

        public void Afterimage(in Matrix4x4 m)
        {
            if (_trailCount < Batch) _trails[_trailCount++] = m;
        }

        public void Draw(Camera camera, Bounds bounds)
        {
            Draw(_core, _cores, _coreCount, camera, bounds);
            Draw(_trail, _trails, _trailCount, camera, bounds);
            _coreCount = _trailCount = 0;
        }

        void Draw(Material material, Matrix4x4[] matrices, int count, Camera camera, Bounds bounds)
        {
            if (count == 0) return;
            var rp = new RenderParams(material)
            {
                camera = camera,
                shadowCastingMode = ShadowCastingMode.Off,
                receiveShadows = false,
                worldBounds = bounds,
            };
            Graphics.RenderMeshInstanced(rp, _mesh, 0, matrices, count);
        }

        /// <summary>
        /// A faceted crystal along +z with flat facets (a vertex per face corner): a ring of
        /// <paramref name="sides"/> corners, <paramref name="width"/> across in x and
        /// <paramref name="height"/> in y, pointed <paramref name="front"/> ahead and
        /// <paramref name="back"/> behind.
        /// </summary>
        public static Mesh Build(string name, int sides, float width, float height, float front, float back)
        {
            var tip = new Vector3(0, 0, front);
            var tail = new Vector3(0, 0, -back);
            var ring = new Vector3[sides];
            for (int k = 0; k < sides; k++)
            {
                float a = 2 * Mathf.PI * k / sides;
                ring[k] = new Vector3(Mathf.Cos(a) * width / 2, Mathf.Sin(a) * height / 2, 0);
            }
            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var triangles = new List<int>();
            void Face(Vector3 a, Vector3 b, Vector3 c)
            {
                var n = Vector3.Cross(b - a, c - a).normalized;
                for (int k = 0; k < 3; k++)
                {
                    triangles.Add(vertices.Count);
                    normals.Add(n);
                    vertices.Add(k == 0 ? a : k == 1 ? b : c);
                }
            }
            for (int k = 0; k < sides; k++)
            {
                var a = ring[k];
                var b = ring[(k + 1) % sides];
                Face(tip, a, b);
                Face(tail, b, a);
            }
            var mesh = new Mesh { name = name };
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        public void Dispose()
        {
            Destroy(_core);
            Destroy(_trail);
        }

        /// <summary>Destroys a runtime object, at once outside play mode (EditMode tests).</summary>
        internal static void Destroy(UnityEngine.Object o)
        {
            if (Application.isPlaying) UnityEngine.Object.Destroy(o);
            else UnityEngine.Object.DestroyImmediate(o);
        }
    }
}
