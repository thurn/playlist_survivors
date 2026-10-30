using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace StyleLab.Runtime
{
    /// <summary>
    /// The set, all generated so nothing but the monsters comes from vendors: a dark cave floor,
    /// glowing crystal clusters and rocks round the fight, moonlight, the hero's torch, the
    /// crystals' coloured lights, fog, and the post stack each treatment tunes.
    /// </summary>
    public sealed class Stage
    {
        public readonly List<(Vector3 at, float radius)> Obstacles = new List<(Vector3, float)>();
        public readonly Light Torch;
        readonly Bloom _bloom;
        readonly Tonemapping _tonemap;
        readonly ColorAdjustments _color;
        readonly Vignette _vignette;
        readonly ChromaticAberration _chroma;

        static readonly Vector3[] Crystals =
        {
            new Vector3(-9, 0, 6.5f), new Vector3(10.5f, 0, 8), new Vector3(-12.5f, 0, -6.5f),
            new Vector3(7, 0, -11), new Vector3(16, 0, -1.5f), new Vector3(-3.5f, 0, 13.5f),
        };
        static readonly Vector3[] Rocks =
        {
            new Vector3(-6, 0, -12.5f), new Vector3(13, 0, 13), new Vector3(-16, 0, 2), new Vector3(3.5f, 0, 16),
            new Vector3(17, 0, -9.5f), new Vector3(-11.5f, 0, 12), new Vector3(-17, 0, -12), new Vector3(20, 0, 5),
        };

        public Stage(StyleLabData data, Transform parent)
        {
            var root = new GameObject("Stage").transform;
            root.SetParent(parent, false);

            var ground = new GameObject("Ground");
            ground.transform.SetParent(root, false);
            ground.AddComponent<MeshFilter>().sharedMesh = GroundMesh(80f);
            ground.AddComponent<MeshRenderer>().sharedMaterial = new Material(data.Shader("StyleLab/Ground"));

            var prop = data.Shader("StyleLab/Prop");
            var rng = new System.Random(11);
            var violet = new Color(0.55f, 0.3f, 1f);
            var cyan = new Color(0.25f, 0.85f, 1f);
            for (int i = 0; i < Crystals.Length; i++)
            {
                var tint = i % 2 == 0 ? violet : cyan;
                var mat = new Material(prop) { name = "Crystal prop" };
                mat.SetVector("_Color", new Color(0.12f, 0.08f, 0.2f) + tint * 0.08f);
                mat.SetVector("_Glow", tint * 1.6f);
                mat.SetFloat("_GlowHeight", 2.6f);
                var go = MakeProp("Crystals", root, Crystals[i], CrystalCluster(rng), mat);
                go.transform.rotation = Quaternion.Euler(0, (float)rng.NextDouble() * 360f, 0);
                Obstacles.Add((Crystals[i], 1.3f));
                var light = new GameObject("Crystal light").AddComponent<Light>();
                light.transform.SetParent(root, false);
                light.transform.position = Crystals[i] + Vector3.up * 1.8f;
                light.type = LightType.Point;
                light.color = tint;
                light.intensity = 5f;
                light.range = 8f;
                light.shadows = LightShadows.None;
            }
            var rockMat = new Material(prop) { name = "Rock" };
            rockMat.SetVector("_Color", new Color(0.07f, 0.055f, 0.08f));
            rockMat.SetVector("_Glow", Color.black);
            foreach (var at in Rocks)
            {
                float size = 1.0f + (float)rng.NextDouble() * 0.9f;
                var go = MakeProp("Rock", root, at, RockMesh(rng), rockMat);
                go.transform.localScale = new Vector3(size, size * 0.7f, size);
                go.transform.rotation = Quaternion.Euler(0, (float)rng.NextDouble() * 360f, 0);
                Obstacles.Add((at, size * 1.05f));
            }

            var moon = new GameObject("Moon").AddComponent<Light>();
            moon.transform.SetParent(root, false);
            moon.type = LightType.Directional;
            moon.transform.rotation = Quaternion.Euler(52f, -35f, 0);
            moon.color = new Color(0.62f, 0.7f, 1f);
            moon.intensity = 0.95f;
            moon.shadows = LightShadows.Soft;
            moon.shadowStrength = 0.85f;
            RenderSettings.sun = moon;

            Torch = new GameObject("Torch").AddComponent<Light>();
            Torch.transform.SetParent(root, false);
            Torch.transform.position = new Vector3(0, 2.6f, 0);
            Torch.type = LightType.Point;
            Torch.color = new Color(1f, 0.62f, 0.32f);
            Torch.intensity = 9f;
            Torch.range = 12f;
            Torch.shadows = LightShadows.None;

            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.2f, 0.2f, 0.36f);
            RenderSettings.ambientEquatorColor = new Color(0.12f, 0.1f, 0.18f);
            RenderSettings.ambientGroundColor = new Color(0.05f, 0.04f, 0.06f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogDensity = 0.018f;
            RenderSettings.fogColor = new Color(0.03f, 0.02f, 0.05f);
            RenderSettings.skybox = null;

            var volume = new GameObject("Post").AddComponent<Volume>();
            volume.transform.SetParent(root, false);
            volume.isGlobal = true;
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            _bloom = profile.Add<Bloom>(true);
            _tonemap = profile.Add<Tonemapping>(true);
            _color = profile.Add<ColorAdjustments>(true);
            _vignette = profile.Add<Vignette>(true);
            _chroma = profile.Add<ChromaticAberration>(true);
            volume.sharedProfile = profile;
        }

        public void ApplyPost(PostLook p)
        {
            _bloom.threshold.Override(p.bloomThreshold);
            _bloom.intensity.Override(p.bloom);
            _bloom.scatter.Override(0.68f);
            _bloom.highQualityFiltering.Override(true);
            _tonemap.mode.Override(p.neutral ? TonemappingMode.Neutral : TonemappingMode.ACES);
            _color.postExposure.Override(p.exposure);
            _color.contrast.Override(p.contrast);
            _color.saturation.Override(p.saturation);
            _vignette.intensity.Override(p.vignette);
            _vignette.smoothness.Override(0.45f);
            _chroma.intensity.Override(p.chroma);
        }

        static GameObject MakeProp(string name, Transform parent, Vector3 at, Mesh mesh, Material mat)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = at;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
            return go;
        }

        static Mesh GroundMesh(float half)
        {
            var m = new Mesh { name = "Ground" };
            m.vertices = new[] { new Vector3(-half, 0, -half), new Vector3(-half, 0, half), new Vector3(half, 0, half), new Vector3(half, 0, -half) };
            m.normals = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up };
            m.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            m.RecalculateBounds();
            return m;
        }

        // Flat-shaded meshes: every triangle gets its own vertices and face normal.
        sealed class FlatMesh
        {
            readonly List<Vector3> _v = new List<Vector3>();
            readonly List<Vector3> _n = new List<Vector3>();
            // Wound so the face points away from `inside`.
            public void Tri(Vector3 a, Vector3 b, Vector3 c, Vector3 inside)
            {
                var n = Vector3.Cross(b - a, c - a).normalized;
                if (Vector3.Dot(n, (a + b + c) / 3f - inside) < 0) { (b, c) = (c, b); n = -n; }
                _v.Add(a); _v.Add(b); _v.Add(c);
                _n.Add(n); _n.Add(n); _n.Add(n);
            }
            public Mesh Build(string name)
            {
                var m = new Mesh { name = name };
                m.SetVertices(_v);
                m.SetNormals(_n);
                var idx = new int[_v.Count];
                for (int i = 0; i < idx.Length; i++) idx[i] = i;
                m.SetTriangles(idx, 0);
                m.RecalculateBounds();
                return m;
            }
        }

        static Mesh CrystalCluster(System.Random rng)
        {
            var f = new FlatMesh();
            int count = 5 + rng.Next(4);
            for (int c = 0; c < count; c++)
            {
                float r = 0.18f + (float)rng.NextDouble() * 0.28f;
                float h = 1.0f + (float)rng.NextDouble() * 2.0f;
                if (c == 0) { r = 0.45f; h = 3.0f; }
                var basePos = c == 0 ? Vector3.zero : new Vector3((float)rng.NextDouble() - 0.5f, 0, (float)rng.NextDouble() - 0.5f) * 1.6f;
                var rot = Quaternion.Euler(((float)rng.NextDouble() - 0.5f) * 50f, (float)rng.NextDouble() * 360f, ((float)rng.NextDouble() - 0.5f) * 50f);
                const int sides = 6;
                var ring = new Vector3[sides];
                var top = new Vector3[sides];
                for (int s = 0; s < sides; s++)
                {
                    float a = s * Mathf.PI * 2 / sides;
                    var d = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * r;
                    ring[s] = basePos + rot * (d + Vector3.down * 0.3f);
                    top[s] = basePos + rot * (d * 0.85f + Vector3.up * h * 0.75f);
                }
                var tip = basePos + rot * (Vector3.up * h);
                var inside = basePos + rot * (Vector3.up * h * 0.4f);
                for (int s = 0; s < sides; s++)
                {
                    int n = (s + 1) % sides;
                    f.Tri(ring[s], top[s], top[n], inside);
                    f.Tri(ring[s], top[n], ring[n], inside);
                    f.Tri(top[s], tip, top[n], inside);
                }
            }
            return f.Build("Crystal cluster");
        }

        static Mesh RockMesh(System.Random rng)
        {
            // A lumpy octahedron, subdivided twice.
            var tris = new List<(Vector3, Vector3, Vector3)>();
            Vector3[] o = { Vector3.up, Vector3.down, Vector3.left, Vector3.right, Vector3.forward, Vector3.back };
            int[,] faces = { { 0, 4, 3 }, { 0, 3, 5 }, { 0, 5, 2 }, { 0, 2, 4 }, { 1, 3, 4 }, { 1, 5, 3 }, { 1, 2, 5 }, { 1, 4, 2 } };
            for (int i = 0; i < 8; i++) tris.Add((o[faces[i, 0]], o[faces[i, 1]], o[faces[i, 2]]));
            for (int k = 0; k < 2; k++)
            {
                var next = new List<(Vector3, Vector3, Vector3)>();
                foreach (var (a, b, c) in tris)
                {
                    var ab = ((a + b) * 0.5f).normalized;
                    var bc = ((b + c) * 0.5f).normalized;
                    var ca = ((c + a) * 0.5f).normalized;
                    next.Add((a, ab, ca)); next.Add((ab, b, bc)); next.Add((ca, bc, c)); next.Add((ab, bc, ca));
                }
                tris = next;
            }
            float seed = (float)rng.NextDouble() * 100f;
            Vector3 Lump(Vector3 p)
            {
                float n = Mathf.PerlinNoise(p.x * 1.7f + seed, p.z * 1.7f + p.y) * 0.5f + 0.75f;
                var q = p * n;
                q.y = Mathf.Max(q.y, -0.25f);
                return q;
            }
            var f = new FlatMesh();
            foreach (var (a, b, c) in tris) f.Tri(Lump(a), Lump(b), Lump(c), Vector3.zero);
            return f.Build("Rock");
        }
    }
}
