using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Demo3D.EditorTools.Assets;
using Demo3D.Runtime.Crowd;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;

namespace Demo3D.EditorTools.Crowd
{
    /// <summary>Isolated experiment: every vendor-derived output stays in Baked.</summary>
    public static class SkeletonLodExperiment
    {
        public const string Dir = "Assets/Baked/SkeletonLod";
        public const string LibraryPath = Dir + "/PreviewLibrary.asset";

        static float[][] V(IEnumerable<Vector3> xs) => xs.Select(v => new[] { v.x, v.y, v.z }).ToArray();
        static float[][] V(IEnumerable<Vector4> xs) => xs.Select(v => new[] { v.x, v.y, v.z, v.w }).ToArray();
        static float[][] V(IEnumerable<Vector2> xs) => xs.Select(v => new[] { v.x, v.y }).ToArray();
        static string PathOf(Transform t, Transform root) => t == root ? t.name : PathOf(t.parent, root) + "/" + t.name;

        public static void Export()
        {
            Directory.CreateDirectory(Dir);
            var t = Roster.Load().Single(x => x.id == "skeleton");
            // The preview exercises all actual clips in this skeleton's file, with semantic
            // roster roles first. Additional clips receive stable names, never guessed roles.
            var clipRef = t.clips["idle"];
            var available = AssetDatabase.LoadAllAssetsAtPath(Roster.PathOf(clipRef.guid))
                .OfType<AnimationClip>().Where(c => !c.name.StartsWith("__preview__"))
                .OrderBy(c => c.name, StringComparer.Ordinal);
            foreach (var c in available)
                if (!t.clips.Values.Any(x => x.guid == clipRef.guid && x.clip == c.name))
                    t.clips.Add("extra:" + c.name, new Roster.ClipRef { guid = clipRef.guid, clip = c.name });
            using var b = CrowdBaker.BakeType(t, CrowdBaker.LoadAnnotations());
            var mesh = b.BuildMesh("Original");
            Save(mesh, Dir + "/Original.asset");
            var tex = new Texture2D(b.Width, b.Height, TextureFormat.RGBAFloat, false, true)
            { name = "SkeletonPreviewBones60", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            tex.SetPixelData(b.Texels, 0);
            tex.Apply(false, false);
            Save(tex, Dir + "/Bones.asset");
            var shader = AssetDatabase.LoadAssetAtPath<Shader>(CrowdBaker.ShaderPath);
            var mats = new List<Material>();
            for (int s = 0; s < b.Submeshes.Count; s++)
            {
                var src = b.Submeshes[s].Source;
                var m = new Material(shader) { name = "SkeletonPreviewSurface" + s };
                if (src.shader.name.Contains("LPColor"))
                {
                    m.SetTexture("_BaseMap", CrowdBaker.BakeColorId(src, $"{Dir}/Albedo{s}.asset"));
                    m.SetFloat("_Smoothness", src.HasProperty("_Smoothness") ? src.GetFloat("_Smoothness") : 0.2f);
                    m.SetFloat("_Metallic", src.HasProperty("_Metal") ? src.GetFloat("_Metal") : 0);
                }
                else
                {
                    foreach (var p in new[] { "_BaseMap", "_EmissionMap" })
                        if (src.HasProperty(p)) m.SetTexture(p, src.GetTexture(p));
                    foreach (var p in new[] { "_BaseColor", "_EmissionColor" })
                        if (src.HasProperty(p)) m.SetColor(p, src.GetColor(p));
                    foreach (var p in new[] { "_Smoothness", "_Metallic" })
                        if (src.HasProperty(p)) m.SetFloat(p, src.GetFloat(p));
                }
                Save(m, $"{Dir}/Surface{s}.mat");
                mats.Add(m);
            }
            var lib = ScriptableObject.CreateInstance<CrowdLibrary>();
            lib.CrowdShader = shader;
            lib.CullShader = AssetDatabase.LoadAssetAtPath<ComputeShader>(CrowdBaker.CullPath);
            lib.PaletteShader = AssetDatabase.LoadAssetAtPath<ComputeShader>(CrowdBaker.PalettePath);
            lib.Types = new[] { new CrowdType
            {
                Id = "Original", Kind = "fodder", Mesh = mesh, Materials = mats.ToArray(), Bones = tex,
                BoneCount = b.Entries.Count, Clips = b.Clips.ToArray(), CenterY = b.CenterY,
                Radius = b.Radius, HeightM = b.HeightM, RadiusM = t.radius,
                Vertices = mesh.vertexCount, Triangles = b.Submeshes.Sum(s => s.Indices.Count) / 3,
            } };
            Save(lib, LibraryPath);
            File.WriteAllText(Dir + "/source.json", JsonConvert.SerializeObject(new
            {
                version = 1, root = "TD5 RootTR (translation/rotation only); mesh includes demo scale",
                positions = V(b.Positions), normals = V(b.Normals), tangents = V(b.Tangents), uv = V(b.Uv),
                boneIndices = V(b.BoneIndices), boneWeights = V(b.BoneWeights),
                submeshes = b.Submeshes.Select(s => s.Indices.ToArray()).ToArray(),
                materials = b.Submeshes.Select(s => new { s.Key, source = AssetDatabase.GetAssetPath(s.Source) }),
                regions = b.Ranges.Select(r => new { name = PathOf(r.Renderer.transform, b.Root.transform),
                    first = r.First, count = r.Count, mesh = AssetDatabase.GetAssetPath(r.Renderer is SkinnedMeshRenderer sk ? sk.sharedMesh : r.Renderer.GetComponent<MeshFilter>().sharedMesh) }),
                palette = b.Entries.Select((e, i) => new { index = i, name = PathOf(e.Bone, b.Root.transform),
                    bind = Enumerable.Range(0, 16).Select(k => e.Bind[k]).ToArray(),
                    origin = V(new[] { e.Bind.inverse.MultiplyPoint3x4(Vector3.zero) })[0] }),
                clips = b.Clips,
            }));
            AssetDatabase.SaveAssets();
            Debug.Log($"SkeletonLod: exported {mesh.vertexCount} vertices, {lib.Types[0].Triangles} triangles, {b.Entries.Count} palette entries, {b.Clips.Count} clips");
        }

        public sealed class MeshFile
        {
            public float[][] positions, normals, tangents, uv, boneIndices, boneWeights;
            public int[][] submeshes;
        }

        public static void ImportCandidates()
        {
            var lib = AssetDatabase.LoadAssetAtPath<CrowdLibrary>(LibraryPath);
            var original = lib.Types[0];
            var types = new List<CrowdType> { original };
            foreach (var id in new[] { "A", "B" })
            {
                var input = id == "B" ? TaskArgs.Get("-bCandidate", $"{Dir}/B.json") : $"{Dir}/{id}.json";
                var f = JsonConvert.DeserializeObject<MeshFile>(File.ReadAllText(input));
                var m = new Mesh { name = id };
                m.vertices = f.positions.Select(v => new Vector3(v[0], v[1], v[2])).ToArray();
                m.normals = f.normals.Select(v => new Vector3(v[0], v[1], v[2])).ToArray();
                m.tangents = f.tangents.Select(v => new Vector4(v[0], v[1], v[2], v[3])).ToArray();
                m.uv = f.uv.Select(v => new Vector2(v[0], v[1])).ToArray();
                m.SetUVs(1, f.boneIndices.Select(v => new Vector4(v[0], v[1], v[2], v[3])).ToList());
                m.SetUVs(2, f.boneWeights.Select(v => new Vector4(v[0], v[1], v[2], v[3])).ToList());
                m.subMeshCount = f.submeshes.Length;
                for (int s = 0; s < m.subMeshCount; s++) m.SetTriangles(f.submeshes[s], s);
                // Expand below from every vertex at every baked frame, including final frames.
                var copy = JsonUtility.FromJson<CrowdType>(JsonUtility.ToJson(original));
                copy.Id = id; copy.Mesh = m; copy.Lod1 = copy.Lod2 = null;
                copy.Vertices = m.vertexCount; copy.Triangles = f.submeshes.Sum(s => s.Length) / 3;
                ValidateAndMeasure(copy);
                Save(m, $"{Dir}/{id}.asset");
                types.Add(copy);
            }
            ValidateAndMeasure(original);
            lib.Types = types.ToArray();
            EditorUtility.SetDirty(lib);
            AssetDatabase.SaveAssets();
        }

        public static void ValidateAndMeasure(CrowdType t)
        {
            var p = t.Mesh.vertices;
            var bi = new List<Vector4>(); var bw = new List<Vector4>();
            t.Mesh.GetUVs(1, bi); t.Mesh.GetUVs(2, bw);
            if (bi.Count != p.Length || bw.Count != p.Length || t.Materials.Length != t.Mesh.subMeshCount)
                throw new Exception(t.Id + ": stream/material count mismatch");
            var uv = t.Mesh.uv; var ns = t.Mesh.normals; var ts = t.Mesh.tangents;
            for (int v = 0; v < p.Length; v++)
            {
                foreach (var n in new[] { p[v].x, p[v].y, p[v].z, ns[v].x, ns[v].y, ns[v].z,
                    ts[v].x, ts[v].y, ts[v].z, ts[v].w, uv[v].x, uv[v].y })
                    if (float.IsNaN(n) || float.IsInfinity(n)) throw new Exception(t.Id + ": nonfinite attribute");
                float sum = 0;
                for (int k = 0; k < 4; k++)
                {
                    float i = bi[v][k], w = bw[v][k];
                    if (float.IsNaN(i) || float.IsInfinity(i) || i != Mathf.Floor(i) || i < 0 || i >= t.BoneCount ||
                        float.IsNaN(w) || float.IsInfinity(w) || w < 0) throw new Exception(t.Id + ": invalid influence");
                    sum += w;
                }
                if (Mathf.Abs(sum - 1) > 1e-4f) throw new Exception(t.Id + ": nonunit weights");
            }
            foreach (var i in t.Mesh.triangles)
                if (i < 0 || i >= p.Length) throw new Exception(t.Id + ": invalid index");
            var pixels = t.Bones.GetPixels();
            var clips = (CrowdClip[])t.Clips.Clone();
            float totalMin = float.MaxValue, totalMax = float.MinValue, totalR = 0;
            for (int c = 0; c < clips.Length; c++)
            {
                var bounds = new Bounds(); bool first = true;
                var matrices = new Matrix4x4[t.BoneCount];
                var points = new List<Vector3>();
                for (int f = 0; f < clips[c].Frames; f++)
                {
                    for (int e = 0; e < matrices.Length; e++)
                    {
                        var mat = Matrix4x4.identity;
                        for (int r = 0; r < 3; r++) mat.SetRow(r, pixels[(clips[c].FirstRow + f) * t.Bones.width + e * 3 + r]);
                        matrices[e] = mat;
                    }
                    for (int v = 0; v < p.Length; v++)
                    {
                        var q = Vector3.zero;
                        for (int k = 0; k < 4; k++) q += bw[v][k] * matrices[(int)bi[v][k]].MultiplyPoint3x4(p[v]);
                        if (first) { bounds = new Bounds(q, Vector3.zero); first = false; } else bounds.Encapsulate(q);
                        points.Add(q);
                    }
                }
                float y = bounds.center.y;
                float radius = points.Max(q => (q - new Vector3(0, y, 0)).magnitude) * 1.05f + 0.02f;
                clips[c].CenterY = y; clips[c].Radius = radius; clips[c].HeightM = Mathf.Max(0.05f, bounds.max.y);
                totalMin = Mathf.Min(totalMin, bounds.min.y); totalMax = Mathf.Max(totalMax, bounds.max.y);
                totalR = Mathf.Max(totalR, radius + Mathf.Abs(y));
            }
            t.Clips = clips; t.CenterY = (totalMin + totalMax) * 0.5f;
            t.Radius = totalR + Mathf.Abs(t.CenterY); t.HeightM = totalMax;
            t.Mesh.bounds = new Bounds(new Vector3(0, t.CenterY, 0), Vector3.one * (2 * t.Radius));
            Debug.Log($"SkeletonLod validated {t.Id}: {t.Triangles} triangles / {t.Vertices} vertices; {clips.Length} complete clips, conservative radius {t.Radius:F3}");
        }

        static void Save(UnityEngine.Object o, string path)
        {
            AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(o, path);
        }
    }
}
