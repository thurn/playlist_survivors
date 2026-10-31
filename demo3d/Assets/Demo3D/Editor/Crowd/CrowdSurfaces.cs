using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace Demo3D.EditorTools.Crowd
{
    /// <summary>
    /// Per-swatch surfaces (crowd.md, Surfaces): Data/crowd-surfaces.json gives a type's swatch
    /// colours their own metallic and smoothness, so armour shines beside matte cloth. The baker
    /// writes a surface map beside each albedo (R metallic, G smoothness; every other texel keeps the
    /// material's own values), which the crowd shader reads under `--crowd-gloss`. It also writes
    /// surfaces-report.md beside the library: each submesh's swatches with their share of the
    /// triangles and the bones those triangles ride, which is how the file's colours were picked.
    /// </summary>
    public static class CrowdSurfaces
    {
        const string DataPath = "Assets/Demo3D/Data/crowd-surfaces.json";
        public const string ReportPath = CrowdBaker.OutDir + "/surfaces-report.md";

        sealed class Swatch
        {
            public Color32 Color;
            public float Metallic, Smoothness;
        }

        static JObject _data;
        static readonly StringBuilder Report = new StringBuilder();

        public static void Begin()
        {
            _data = File.Exists(DataPath) ? JObject.Parse(File.ReadAllText(DataPath)) : new JObject();
            Report.Clear();
            Report.AppendLine("# Crowd swatches (CrowdSurfaces)").AppendLine();
            Report.AppendLine("Each submesh's albedo colours by share of its triangles (the colour under each triangle's first corner), with the bones those triangles ride most. A * marks a colour crowd-surfaces.json lists.").AppendLine();
        }

        public static void End() => File.WriteAllText(ReportPath, Report.ToString());

        static List<Swatch> SwatchesOf(string id)
        {
            var list = new List<Swatch>();
            if (!(_data?["types"]?[id] is JArray a)) return list;
            foreach (var s in a)
            {
                ColorUtility.TryParseHtmlString((string)s["color"], out var c);
                list.Add(new Swatch { Color = c, Metallic = (float?)s["metallic"] ?? 0, Smoothness = (float?)s["smoothness"] ?? 0.2f });
            }
            return list;
        }

        static int Tolerance => (int?)_data?["tolerance"] ?? 10;

        static Swatch Match(List<Swatch> swatches, Color32 c)
        {
            int tol = Tolerance;
            foreach (var s in swatches)
                if (Mathf.Abs(s.Color.r - c.r) <= tol && Mathf.Abs(s.Color.g - c.g) <= tol && Mathf.Abs(s.Color.b - c.b) <= tol)
                    return s;
            return null;
        }

        static string Hex(Color32 c) => $"#{c.r:x2}{c.g:x2}{c.b:x2}";

        /// <summary>
        /// The submesh's surface map, or null when the type lists no swatches. The map matches the
        /// albedo's size and filtering; unlisted texels keep <paramref name="metallic"/> and
        /// <paramref name="smoothness"/>, the material's own.
        /// </summary>
        public static Texture2D Bake(string id, Texture2D albedo, Color32[] pixels, float metallic, float smoothness, string path)
        {
            var swatches = SwatchesOf(id);
            if (swatches.Count == 0 || albedo == null) return null;
            var output = new Color32[pixels.Length];
            byte m0 = (byte)Mathf.RoundToInt(metallic * 255), s0 = (byte)Mathf.RoundToInt(smoothness * 255);
            int hits = 0;
            for (int p = 0; p < pixels.Length; p++)
            {
                var s = Match(swatches, pixels[p]);
                if (s != null) hits++;
                output[p] = s == null ? new Color32(m0, s0, 0, 255)
                    : new Color32((byte)Mathf.RoundToInt(s.Metallic * 255), (byte)Mathf.RoundToInt(s.Smoothness * 255), 0, 255);
            }
            var tex = new Texture2D(albedo.width, albedo.height, TextureFormat.RGBA32, false, true)
            {
                name = Path.GetFileNameWithoutExtension(path), filterMode = albedo.filterMode, wrapMode = albedo.wrapMode,
            };
            tex.SetPixels32(output);
            tex.Apply(false, true);
            AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(tex, path);
            Debug.Log($"CrowdSurfaces: {id}: {path}, {hits * 100f / pixels.Length:F1}% of texels listed");
            return tex;
        }

        /// <summary>Appends the submesh's swatches to the report.</summary>
        public static void Describe(CrowdBaker.TypeBake b, int submesh, Texture2D albedo, Color32[] pixels, Color baseColor)
        {
            var sub = b.Submeshes[submesh];
            var swatches = SwatchesOf(b.Type.id);
            var groups = new Dictionary<int, (Color32 Color, int Count, Dictionary<string, int> Bones)>();
            int triangles = sub.Indices.Count / 3;
            for (int t = 0; t < sub.Indices.Count; t += 3)
            {
                int v = sub.Indices[t];
                Color32 c = baseColor;
                if (pixels != null)
                {
                    var uv = b.Uv[v];
                    int x = Mathf.Clamp((int)((uv.x - Mathf.Floor(uv.x)) * albedo.width), 0, albedo.width - 1);
                    int y = Mathf.Clamp((int)((uv.y - Mathf.Floor(uv.y)) * albedo.height), 0, albedo.height - 1);
                    c = pixels[y * albedo.width + x];
                }
                int key = (c.r >> 3) << 10 | (c.g >> 3) << 5 | (c.b >> 3);
                if (!groups.TryGetValue(key, out var g)) g = (c, 0, new Dictionary<string, int>());
                var w = b.BoneWeights[v];
                var idx = b.BoneIndices[v];
                int top = w.x >= w.y && w.x >= w.z && w.x >= w.w ? (int)idx.x : w.y >= w.z && w.y >= w.w ? (int)idx.y : w.z >= w.w ? (int)idx.z : (int)idx.w;
                string bone = top < b.Entries.Count && b.Entries[top].Bone != null ? b.Entries[top].Bone.name : "?";
                g.Bones[bone] = (g.Bones.TryGetValue(bone, out int n) ? n : 0) + 1;
                groups[key] = (g.Color, g.Count + 1, g.Bones);
            }
            Report.AppendLine($"## {b.Type.id} submesh {submesh} ({sub.Source?.name}, {albedo?.name ?? "no texture"}, {triangles} triangles)").AppendLine();
            Report.AppendLine("| colour | triangles | bones |").AppendLine("|---|---:|---|");
            foreach (var g in groups.Values.OrderByDescending(g => g.Count))
            {
                if (g.Count * 200 < triangles) continue; // under half a percent
                string bones = string.Join(", ", g.Bones.OrderByDescending(kv => kv.Value).Take(4).Select(kv => $"{kv.Key} {kv.Value * 100 / g.Count}%"));
                string mark = Match(swatches, g.Color) != null ? " *" : "";
                Report.AppendLine($"| {Hex(g.Color)}{mark} | {g.Count * 100f / triangles:F1}% | {bones} |");
            }
            Report.AppendLine();
        }
    }
}
