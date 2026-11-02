using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace Champions.Render
{
    /// <summary>
    /// The scripted scenes' stand-in at the hero's feet until the hero's edge cut draws there: a thin flat ring of
    /// <see cref="RadiusM"/> in HDR white (Glow.shader), so the stills show where the beat wave leaves from, and its bloom
    /// and edges show a tier's bloom and MSAA; the stills' type-ID pass sees it as one of the hero's effects. White is
    /// the hero's (design.md, The Look &gt; Principles).
    /// </summary>
    public sealed class HeroMarker : IDisposable
    {
        public const string MaterialPath = "World/Glow";
        public const float RadiusM = 0.45f, WidthM = 0.07f;
        const int Segments = 48;

        readonly GameObject _go;
        readonly Mesh _mesh;
        readonly Material _material;

        public HeroMarker(int layer = 0)
        {
            var shared = Resources.Load<Material>(MaterialPath)
                         ?? throw new InvalidOperationException($"HeroMarker: no material at Resources/{MaterialPath}");
            _material = new Material(shared) { name = "Hero marker" };
            // An effect of the hero's to the type-ID pass, not the hero: a flat ring has no body for the hero's rules.
            _material.SetFloat(IdPass.Id, IdPass.Effect);
            _mesh = Ring();
            _go = new GameObject("Hero marker") { layer = layer };
            _go.AddComponent<MeshFilter>().sharedMesh = _mesh;
            var r = _go.AddComponent<MeshRenderer>();
            r.sharedMaterial = _material;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = LightProbeUsage.Off;
            r.reflectionProbeUsage = ReflectionProbeUsage.Off;
        }

        /// <summary>Stands it at the hero's feet (ground x, z).</summary>
        public void Place(Vector2 feet) => _go.transform.position = new Vector3(feet.x, 0.02f, feet.y);

        static Mesh Ring()
        {
            var vertices = new Vector3[Segments * 2];
            var indices = new int[Segments * 6];
            float inner = RadiusM - WidthM / 2, outer = RadiusM + WidthM / 2;
            for (int i = 0; i < Segments; i++)
            {
                float a = 2 * Mathf.PI * i / Segments;
                var d = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                vertices[2 * i] = d * inner;
                vertices[2 * i + 1] = d * outer;
                int j = (i + 1) % Segments, k = 6 * i;
                indices[k] = 2 * i; indices[k + 1] = 2 * j; indices[k + 2] = 2 * i + 1;
                indices[k + 3] = 2 * i + 1; indices[k + 4] = 2 * j; indices[k + 5] = 2 * j + 1;
            }
            var mesh = new Mesh { name = "Hero marker" };
            mesh.vertices = vertices;
            mesh.triangles = indices;
            mesh.RecalculateBounds();
            return mesh;
        }

        public void Dispose()
        {
            foreach (var o in new UnityEngine.Object[] { _go, _mesh, _material })
            {
                if (o == null) continue;
                if (Application.isPlaying) UnityEngine.Object.Destroy(o);
                else UnityEngine.Object.DestroyImmediate(o);
            }
        }
    }
}
