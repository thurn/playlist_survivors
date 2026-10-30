using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Demo3D.EditorTools.Assets
{
    /// <summary>
    /// Measures the footprint of every kit piece (TD6; choreography.md, Navigation around
    /// structures): each manifest entry whose `use` starts with "kit". The prefab is placed at the
    /// origin, unrotated and at its own scale, and its colliders (or, without colliders, its meshes)
    /// are cut at <see cref="SliceHeightM"/> above the ground. The slice's convex hull gives the
    /// footprint: a circle when its minimum-area rectangle is nearly square, else that rectangle as a
    /// polygon. Small or soft pieces (grass, flowers, ferns, debris) get no footprint and never block.
    /// The result is our own measurement, keyed by vendor GUID, in
    /// Assets/Demo3D/Choreography/Data/footprints.json.
    /// </summary>
    public static class FootprintMeasurer
    {
        public const string OutPath = "Assets/Demo3D/Choreography/Data/footprints.json";
        const string ManifestPath = "manifest.json";
        public const float SliceHeightM = 1.0f;
        public const float MinBlockHeightM = 0.5f, MinBlockRadiusM = 0.15f;
        const float CircleAspect = 1.35f;
        static readonly string[] Soft = { "_Grass_", "_Flowers_", "_Fern_", "_Small_Rocks_", "_Candle_" };

        static readonly Dictionary<string, string> Packs = new Dictionary<string, string>
        {
            { "PolygonAdventure", "adventure" }, { "PolygonDarkFantasy", "darkfantasy" },
            { "PolygonElvenRealm", "elven" }, { "PolygonFantasyKingdom", "kingdom" }, { "PolygonVikings", "vikings" },
        };

        public static void Run()
        {
            var manifest = JObject.Parse(File.ReadAllText(ManifestPath));
            var entries = manifest["assets"].Where(a => ((string)a["use"] ?? "").StartsWith("kit", StringComparison.Ordinal)).ToList();
            if (entries.Count == 0) throw new Exception("FootprintMeasurer: no kit entries in the manifest");
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var props = new List<JObject>();
            foreach (var e in entries.OrderBy(e => Id((string)e["path"]), StringComparer.Ordinal))
            {
                string guid = (string)e["guid"], src = (string)e["path"];
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var prefab = string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null) throw new Exception($"FootprintMeasurer: no prefab for {src} ({guid}); is the vendor folder synced (demo3d setup)?");
                var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                try
                {
                    go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                    props.Add(Measure(Id(src), guid, (string)e["use"], go));
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(go);
                }
            }
            var root = new JObject
            {
                ["version"] = 1,
                ["note"] = "TD6's footprint annotations: our own measurements of TD2's kit pieces, keyed by vendor GUID " +
                           "(FootprintMeasurer.cs, `demo3d unity MeasureFootprints`). Ground (x, y) is Unity's (x, z) in the " +
                           "prefab's own frame at scale 1. A footprint is the convex hull of the colliders (else the meshes) cut " +
                           $"{SliceHeightM.ToString(CultureInfo.InvariantCulture)} m above the ground: a circle when its minimum-area " +
                           $"rectangle's aspect is under {CircleAspect.ToString(CultureInfo.InvariantCulture)}, else that rectangle. " +
                           $"Pieces under {MinBlockHeightM.ToString(CultureInfo.InvariantCulture)} m tall, under " +
                           $"{MinBlockRadiusM.ToString(CultureInfo.InvariantCulture)} m in equivalent radius, or soft (grass, " +
                           "flowers, ferns, small rocks, candles) do not block. canopyRadiusM is the widest reach of any mesh " +
                           "from the origin, which the layout keeps off fighting ground.",
                ["sliceHeightM"] = SliceHeightM,
                ["props"] = new JArray(props),
            };
            File.WriteAllText(OutPath, Format(root), new System.Text.UTF8Encoding(false));
            AssetDatabase.ImportAsset(OutPath);
            Debug.Log($"FootprintMeasurer: {props.Count} props, {props.Count(p => (bool)p["blocks"])} blocking -> {OutPath}");
        }

        /// <summary>"kingdom/SM_Env_Tree_Round_01" from the manifest's source path.</summary>
        public static string Id(string sourcePath)
        {
            string pack = sourcePath.Split('/')[1];
            string short_ = Packs.TryGetValue(pack, out var s) ? s : pack.ToLowerInvariant();
            return short_ + "/" + Path.GetFileNameWithoutExtension(sourcePath);
        }

        static JObject Measure(string id, string guid, string use, GameObject go)
        {
            var slice = new List<Vector2>();
            var all = new List<Vector3>();
            var colliders = go.GetComponentsInChildren<Collider>(true).Where(c => !c.isTrigger).ToList();
            foreach (var mf in go.GetComponentsInChildren<MeshFilter>(true))
                if (mf.sharedMesh != null) AddMesh(mf.sharedMesh, mf.transform.localToWorldMatrix, all, null);
            string source;
            if (colliders.Count > 0)
            {
                source = "colliders";
                foreach (var c in colliders) AddCollider(c, slice);
            }
            else
            {
                source = "meshes";
                foreach (var mf in go.GetComponentsInChildren<MeshFilter>(true))
                    if (mf.sharedMesh != null) AddMesh(mf.sharedMesh, mf.transform.localToWorldMatrix, null, slice);
            }
            float height = all.Count > 0 ? all.Max(v => v.y) : 0;
            float canopy = all.Count > 0 ? all.Max(v => new Vector2(v.x, v.z).magnitude) : 0;
            var hull = Hull(slice);
            float area = Area(hull);
            float rEq = Mathf.Sqrt(area / Mathf.PI);
            bool soft = Soft.Any(s => id.IndexOf(s, StringComparison.Ordinal) >= 0);
            bool blocks = hull.Count >= 3 && !soft && height >= MinBlockHeightM && rEq >= MinBlockRadiusM;
            var o = new JObject
            {
                ["id"] = id, ["guid"] = guid, ["use"] = use,
                ["heightM"] = R(height), ["canopyRadiusM"] = R(canopy), ["source"] = source, ["blocks"] = blocks,
            };
            if (hull.Count < 3) { o["shape"] = "none"; return o; }
            var rect = MinAreaRect(hull, out Vector2 centre, out float aspect);
            if (aspect < CircleAspect)
            {
                float r = hull.Max(p => (p - centre).magnitude);
                o["shape"] = "circle";
                o["center"] = new JArray(R(centre.x), R(centre.y));
                o["radius"] = R(r);
            }
            else
            {
                o["shape"] = "polygon";
                o["points"] = new JArray(rect.Select(p => new JArray(R(p.x), R(p.y))));
            }
            o["equivalentRadiusM"] = R(rEq);
            return o;
        }

        static double R(float v) => Math.Round(v, 3);

        static void AddCollider(Collider c, List<Vector2> slice)
        {
            var m = c.transform.localToWorldMatrix;
            switch (c)
            {
                case MeshCollider mc when mc.sharedMesh != null:
                    AddMesh(mc.sharedMesh, m, null, slice);
                    break;
                case BoxCollider b:
                {
                    // The box as 12 triangles, so the slice cuts it like any mesh.
                    var corners = new Vector3[8];
                    for (int i = 0; i < 8; i++)
                        corners[i] = m.MultiplyPoint3x4(b.center + Vector3.Scale(b.size * 0.5f,
                            new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1)));
                    int[] tris = { 0, 1, 3, 0, 3, 2, 4, 6, 7, 4, 7, 5, 0, 4, 5, 0, 5, 1, 2, 3, 7, 2, 7, 6, 0, 2, 6, 0, 6, 4, 1, 5, 7, 1, 7, 3 };
                    for (int t = 0; t < tris.Length; t += 3) Cut(corners[tris[t]], corners[tris[t + 1]], corners[tris[t + 2]], slice);
                    break;
                }
                default:
                {
                    // Spheres and capsules: their bounds' footprint, as eight points round its ellipse.
                    var bb = c.bounds;
                    if (bb.min.y > SliceHeightM) break;
                    for (int i = 0; i < 8; i++)
                    {
                        float a = i * Mathf.PI / 4;
                        slice.Add(new Vector2(bb.center.x + Mathf.Cos(a) * bb.extents.x, bb.center.z + Mathf.Sin(a) * bb.extents.z));
                    }
                    break;
                }
            }
        }

        static void AddMesh(Mesh mesh, Matrix4x4 m, List<Vector3> all, List<Vector2> slice)
        {
            var v = mesh.vertices;
            var w = new Vector3[v.Length];
            for (int i = 0; i < v.Length; i++) w[i] = m.MultiplyPoint3x4(v[i]);
            all?.AddRange(w);
            if (slice == null) return;
            var tris = mesh.triangles;
            for (int t = 0; t + 2 < tris.Length; t += 3) Cut(w[tris[t]], w[tris[t + 1]], w[tris[t + 2]], slice);
        }

        /// <summary>Adds the part of a triangle at or below the slice: its low corners and its edges' crossings.</summary>
        static void Cut(Vector3 a, Vector3 b, Vector3 c, List<Vector2> slice)
        {
            float h = SliceHeightM;
            if (a.y > h && b.y > h && c.y > h) return;
            Edge(a, b, h, slice); Edge(b, c, h, slice); Edge(c, a, h, slice);
        }

        static void Edge(Vector3 a, Vector3 b, float h, List<Vector2> slice)
        {
            if (a.y <= h) slice.Add(new Vector2(a.x, a.z));
            if ((a.y - h) * (b.y - h) < 0)
            {
                float u = (h - a.y) / (b.y - a.y);
                Vector3 p = Vector3.Lerp(a, b, u);
                slice.Add(new Vector2(p.x, p.z));
            }
        }

        /// <summary>Andrew's monotone chain, counter-clockwise.</summary>
        static List<Vector2> Hull(List<Vector2> pts)
        {
            var p = pts.Distinct().OrderBy(q => q.x).ThenBy(q => q.y).ToList();
            if (p.Count < 3) return p;
            var h = new List<Vector2>();
            for (int pass = 0; pass < 2; pass++)
            {
                int start = h.Count;
                foreach (var q in pass == 0 ? p : Enumerable.Reverse(p))
                {
                    while (h.Count >= start + 2 && Cross(h[h.Count - 2], h[h.Count - 1], q) <= 0) h.RemoveAt(h.Count - 1);
                    h.Add(q);
                }
                h.RemoveAt(h.Count - 1);
            }
            return h;
        }

        static float Cross(Vector2 o, Vector2 a, Vector2 b) => (a.x - o.x) * (b.y - o.y) - (a.y - o.y) * (b.x - o.x);

        static float Area(List<Vector2> h)
        {
            float s = 0;
            for (int i = 0; i < h.Count; i++) s += Cross(Vector2.zero, h[i], h[(i + 1) % h.Count]);
            return Mathf.Abs(s) / 2;
        }

        /// <summary>The minimum-area rectangle around a convex hull (one side lies on a hull edge), counter-clockwise.</summary>
        static Vector2[] MinAreaRect(List<Vector2> h, out Vector2 centre, out float aspect)
        {
            float best = float.MaxValue;
            Vector2[] rect = null;
            centre = Vector2.zero;
            aspect = 1;
            for (int i = 0; i < h.Count; i++)
            {
                Vector2 e = (h[(i + 1) % h.Count] - h[i]).normalized;
                if (e.sqrMagnitude < 1e-12f) continue;
                Vector2 n = new Vector2(-e.y, e.x);
                float u0 = float.MaxValue, u1 = float.MinValue, v0 = float.MaxValue, v1 = float.MinValue;
                foreach (var p in h)
                {
                    float u = Vector2.Dot(p, e), v = Vector2.Dot(p, n);
                    u0 = Mathf.Min(u0, u); u1 = Mathf.Max(u1, u); v0 = Mathf.Min(v0, v); v1 = Mathf.Max(v1, v);
                }
                float area = (u1 - u0) * (v1 - v0);
                if (area >= best) continue;
                best = area;
                rect = new[] { e * u0 + n * v0, e * u1 + n * v0, e * u1 + n * v1, e * u0 + n * v1 };
                centre = e * (u0 + u1) / 2 + n * (v0 + v1) / 2;
                float a = u1 - u0, b = v1 - v0;
                aspect = Mathf.Max(a, b) / Mathf.Max(1e-4f, Mathf.Min(a, b));
            }
            return rect;
        }

        /// <summary>One prop per line, so diffs of a re-measurement stay readable.</summary>
        static string Format(JObject root)
        {
            var sw = new StringWriter(CultureInfo.InvariantCulture);
            sw.Write("{\n");
            var keys = root.Properties().ToList();
            for (int i = 0; i < keys.Count; i++)
            {
                var k = keys[i];
                sw.Write("  " + JsonConvert.SerializeObject(k.Name) + ": ");
                if (k.Value is JArray arr && k.Name == "props")
                {
                    sw.Write("[\n");
                    for (int j = 0; j < arr.Count; j++)
                        sw.Write("    " + arr[j].ToString(Formatting.None) + (j + 1 < arr.Count ? "," : "") + "\n");
                    sw.Write("  ]");
                }
                else sw.Write(k.Value.ToString(Formatting.None));
                sw.Write(i + 1 < keys.Count ? ",\n" : "\n");
            }
            sw.Write("}\n");
            return sw.ToString();
        }
    }
}
