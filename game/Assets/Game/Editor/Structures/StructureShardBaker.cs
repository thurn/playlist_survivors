using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Game.Client.Structures;
using Game.EditorTools.Arena;
using Game.EditorTools.Crowd;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace Game.EditorTools.Structures
{
    /// <summary>
    /// The structures' shard sets (unity-game.md, "Shatter"), demo3d's StructureShardBaker copied
    /// and owned: every kit mesh the baked arena's structure sites draw (Static/Structures/"Site N"),
    /// cut into thick chunks that <see cref="StructureShatter"/> flies when the sim breaks the site.
    /// CrowdShardBaker's method on a static mesh:
    /// - The kit's walls are a few large triangles, so the surface is first split (longest edge in half) until no edge is
    ///   longer than a fraction of a chunk's size; the chunks then follow seeds, not the kit's triangles.
    /// - Seeds spread over the surface (CrowdShardBaker.Cut: best-of-12, area-weighted, a few grown large), their count
    ///   scaled by the piece's surface area (<see cref="ShardCount"/>); each patch closed by inside faces (UV1.x 1) to an
    ///   apex inside the piece, so every chunk is a solid with real thickness, its broken faces drawn brighter.
    /// - Each chunk keeps the kit's UVs and submeshes, so the site's crystal materials draw it as they draw the piece. Its
    ///   corners are relative to its centre, where its rigidbody turns.
    /// It reads the arena scene, so it runs after the arena's bake. Derived from vendor meshes, so it
    /// lives in the gitignored Assets/Baked/Structures/Resources/.
    /// </summary>
    public static class StructureShardBaker
    {
        public const string Dir = Tasks.BakedRoot + "/Structures";
        public const string AssetPath = Dir + "/Resources/" + StructureShards.ResourcePath + ".asset";
        public const int MinShards = 10, MaxShards = 48;

        /// <summary>Chunks for a piece of <paramref name="areaM2"/> square metres of surface: about one per 1.2 m².</summary>
        public static int ShardCount(float areaM2) => Mathf.Clamp(Mathf.RoundToInt(6 + areaM2 / 1.2f), MinShards, MaxShards);

        public static void Bake()
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            if (!File.Exists(ArenaBaker.ScenePath)) throw new Exception($"StructureShardBaker: no {ArenaBaker.ScenePath} (bake the arena first)");
            var scene = EditorSceneManager.OpenScene(ArenaBaker.ScenePath, OpenSceneMode.Single);
            // Every site's LOD 0 meshes, each at the scale its first renderer draws it at.
            var meshes = new List<(Mesh mesh, float scale, string site)>();
            var seen = new HashSet<Mesh>();
            foreach (var root in scene.GetRootGameObjects())
            {
                var structures = root.transform.Find("Static/Structures");
                if (structures == null) continue;
                foreach (Transform site in structures)
                    foreach (var mf in Lod0Filters(site.gameObject))
                    {
                        var mesh = mf.sharedMesh;
                        if (mesh == null || !seen.Add(mesh)) continue;
                        var s = mf.transform.lossyScale;
                        meshes.Add((mesh, (Mathf.Abs(s.x) + Mathf.Abs(s.y) + Mathf.Abs(s.z)) / 3f, site.name));
                    }
            }
            if (meshes.Count == 0) throw new Exception("StructureShardBaker: the arena scene has no structure sites");
            Directory.CreateDirectory(Path.GetDirectoryName(AssetPath));
            AssetDatabase.DeleteAsset(AssetPath);
            var asset = ScriptableObject.CreateInstance<StructureShards>();
            AssetDatabase.CreateAsset(asset, AssetPath);
            var sets = new List<StructureShards.Set>();
            int shards = 0;
            foreach (var (mesh, scale, site) in meshes)
            {
                var set = Bake(mesh, scale, Seed(mesh.name));
                foreach (var sh in set.Shards) AssetDatabase.AddObjectToAsset(sh.Mesh, asset);
                sets.Add(set);
                shards += set.Shards.Length;
                Debug.Log($"StructureShardBaker: {mesh.name} (first in {site}): {set.Shards.Length} shards");
            }
            asset.Meshes = sets.ToArray();
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssets();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single); // leave no open scene behind
            Debug.Log($"StructureShardBaker: {sets.Count} meshes, {shards} shards, in {sw.Elapsed.TotalSeconds:F1} s");
        }

        /// <summary>A site's mesh filters, without any an LOD group draws only past LOD 0.</summary>
        static IEnumerable<MeshFilter> Lod0Filters(GameObject site)
        {
            var skip = new HashSet<Renderer>();
            foreach (var g in site.GetComponentsInChildren<LODGroup>(true))
            {
                var lods = g.GetLODs();
                for (int i = 1; i < lods.Length; i++) foreach (var r in lods[i].renderers) if (r != null) skip.Add(r);
                if (lods.Length > 0) foreach (var r in lods[0].renderers) if (r != null) skip.Remove(r);
            }
            foreach (var mf in site.GetComponentsInChildren<MeshFilter>(true))
                if (mf.TryGetComponent<MeshRenderer>(out var r) && !skip.Contains(r)) yield return mf;
        }

        static int Seed(string key)
        {
            unchecked
            {
                int h = (int)2166136261;
                foreach (char c in key) h = (h ^ c) * 16777619;
                return h;
            }
        }

        // ---------------------------------------------------------------------------------
        // One mesh.

        /// <summary>The kit mesh's triangles, split and indexed per corner as CrowdShardBaker's Posed, plus UVs and submeshes.</summary>
        sealed class Surface
        {
            public readonly List<Vector3> Position = new List<Vector3>(), Normal = new List<Vector3>();
            public readonly List<Vector2> Uv = new List<Vector2>();
            public readonly List<int> Sub = new List<int>(); // per triangle
            public int Triangles => Sub.Count;

            public void Add(Vector3 a, Vector3 b, Vector3 c, Vector3 na, Vector3 nb, Vector3 nc, Vector2 ua, Vector2 ub, Vector2 uc, int sub)
            {
                Position.Add(a); Position.Add(b); Position.Add(c);
                Normal.Add(na); Normal.Add(nb); Normal.Add(nc);
                Uv.Add(ua); Uv.Add(ub); Uv.Add(uc);
                Sub.Add(sub);
            }
        }

        /// <summary>
        /// A mesh's shard set. <paramref name="scale"/> is its renderer's scale in the prefab, so the chunks are sized in
        /// the metres the arena draws it at.
        /// </summary>
        public static StructureShards.Set Bake(Mesh mesh, float scale, int seed)
        {
            scale = Mathf.Max(scale, 1e-4f);
            var verts = mesh.vertices;
            var normals = mesh.normals;
            if (normals.Length != verts.Length) { var copy = UnityEngine.Object.Instantiate(mesh); copy.RecalculateNormals(); normals = copy.normals; UnityEngine.Object.DestroyImmediate(copy); }
            var uvs = mesh.uv;
            if (uvs.Length != verts.Length) uvs = new Vector2[verts.Length];
            var src = new Surface();
            float area = 0;
            for (int sm = 0; sm < mesh.subMeshCount; sm++)
            {
                if (mesh.GetTopology(sm) != MeshTopology.Triangles) continue;
                var tris = mesh.GetTriangles(sm);
                for (int t = 0; t + 2 < tris.Length; t += 3)
                {
                    int a = tris[t], b = tris[t + 1], c = tris[t + 2];
                    float ar = 0.5f * Vector3.Cross(verts[b] - verts[a], verts[c] - verts[a]).magnitude;
                    if (ar <= 1e-12f) continue;
                    area += ar;
                    src.Add(verts[a], verts[b], verts[c], normals[a], normals[b], normals[c], uvs[a], uvs[b], uvs[c], sm);
                }
            }
            float areaM2 = area * scale * scale;
            int count = ShardCount(areaM2);
            // Split until a chunk spans a few triangles across: edges under a third of a chunk's radius.
            float maxEdge = Mathf.Sqrt(area / count) / 3f;
            var surface = Split(src, maxEdge);
            var posed = new CrowdShardBaker.Posed
            {
                Position = surface.Position.ToArray(), Normal = surface.Normal.ToArray(),
                Bary = new Vector4[surface.Position.Count], Bones = new Vector4[surface.Position.Count], Weights = new Vector4[surface.Position.Count],
            };
            var pieces = CrowdShardBaker.Cut(posed, count, new System.Random(seed));
            var ids = CrowdShardBaker.WeldIds(posed);
            var shards = new StructureShards.Shard[pieces.Count];
            for (int i = 0; i < pieces.Count; i++) shards[i] = Build(surface, posed, ids, pieces[i], mesh.subMeshCount, $"{mesh.name}_shard{i}");
            return new StructureShards.Set { Source = mesh, Shards = shards };
        }

        /// <summary>Splits each triangle at its longest edge's midpoint until no edge is longer than <paramref name="maxEdge"/>.</summary>
        static Surface Split(Surface s, float maxEdge)
        {
            var o = new Surface();
            var stack = new Stack<(Vector3, Vector3, Vector3, Vector3, Vector3, Vector3, Vector2, Vector2, Vector2)>();
            for (int t = 0; t < s.Triangles; t++)
            {
                int k = 3 * t;
                stack.Push((s.Position[k], s.Position[k + 1], s.Position[k + 2], s.Normal[k], s.Normal[k + 1], s.Normal[k + 2], s.Uv[k], s.Uv[k + 1], s.Uv[k + 2]));
                while (stack.Count > 0)
                {
                    var (a, b, c, na, nb, nc, ua, ub, uc) = stack.Pop();
                    float ab = (b - a).sqrMagnitude, bc = (c - b).sqrMagnitude, ca = (a - c).sqrMagnitude;
                    float longest = Mathf.Max(ab, Mathf.Max(bc, ca));
                    if (longest <= maxEdge * maxEdge || o.Triangles + stack.Count > 60000)
                    {
                        o.Add(a, b, c, na, nb, nc, ua, ub, uc, s.Sub[t]);
                        continue;
                    }
                    // Rotate so the longest edge is a→b, then halve it at m.
                    if (bc == longest) { (a, b, c) = (b, c, a); (na, nb, nc) = (nb, nc, na); (ua, ub, uc) = (ub, uc, ua); }
                    else if (ca == longest) { (a, b, c) = (c, a, b); (na, nb, nc) = (nc, na, nb); (ua, ub, uc) = (uc, ua, ub); }
                    var m = (a + b) * 0.5f;
                    var nm = (na + nb).normalized;
                    var um = (ua + ub) * 0.5f;
                    stack.Push((a, m, c, na, nm, nc, ua, um, uc));
                    stack.Push((m, b, c, nm, nb, nc, um, ub, uc));
                }
            }
            return o;
        }

        /// <summary>
        /// One chunk: its patch of the surface, closed by inside faces from every boundary edge to an apex under the
        /// patch's centre (CrowdShardBaker's depth rule: no deeper than 0.45 of the piece's thickness there), its corners
        /// relative to its centroid.
        /// </summary>
        static StructureShards.Shard Build(Surface s, CrowdShardBaker.Posed p, int[] ids, List<int> tris, int subMeshes, string name)
        {
            Vector3 c = Vector3.zero, nbar = Vector3.zero;
            Vector2 uvMean = Vector2.zero;
            float areaSum = 0;
            var subArea = new float[subMeshes];
            foreach (int t in tris)
            {
                var a = p.Position[3 * t];
                var b = p.Position[3 * t + 1];
                var d = p.Position[3 * t + 2];
                var cross = Vector3.Cross(b - a, d - a);
                float ar = 0.5f * cross.magnitude;
                c += (a + b + d) / 3f * ar;
                uvMean += (s.Uv[3 * t] + s.Uv[3 * t + 1] + s.Uv[3 * t + 2]) / 3f * ar;
                nbar += cross * 0.5f;
                areaSum += ar;
                subArea[s.Sub[t]] += ar;
            }
            if (areaSum > 0) { c /= areaSum; nbar /= areaSum; uvMean /= areaSum; }
            else c = p.Position[3 * tris[0]];
            float radius = 0;
            foreach (int t in tris)
                for (int k = 0; k < 3; k++) radius = Mathf.Max(radius, (p.Position[3 * t + k] - c).magnitude);
            radius = Mathf.Max(radius, 1e-3f);
            int insideSub = Array.IndexOf(subArea, subArea.Max());

            var inward = nbar.sqrMagnitude > 1e-12f ? -nbar.normalized : Vector3.down;
            float far = CrowdShardBaker.Thickness(p, c - inward * 0.002f, inward);
            float depth = float.IsInfinity(far) ? 0.35f * radius : Mathf.Min(0.6f * radius, 0.45f * far);
            var apex = c + inward * depth * Mathf.Clamp01(nbar.magnitude);
            if ((apex - c).magnitude < 0.08f * radius) apex = c + inward * 0.08f * radius;

            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var uv0 = new List<Vector2>();
            var inside = new List<Vector2>();
            var indices = new List<int>[subMeshes];
            for (int i = 0; i < subMeshes; i++) indices[i] = new List<int>();
            void Corner(Vector3 pos, Vector3 nor, Vector2 uv, float flag, int sub)
            {
                indices[sub].Add(vertices.Count);
                vertices.Add(pos);
                normals.Add(nor);
                uv0.Add(uv);
                inside.Add(new Vector2(flag, 0));
            }
            var directed = new Dictionary<(int, int), int>();
            foreach (int t in tris)
                for (int k = 0; k < 3; k++)
                {
                    int at = 3 * t + k;
                    int a = ids[at], b = ids[3 * t + (k + 1) % 3];
                    if (a == b) continue;
                    if (directed.ContainsKey((b, a))) directed.Remove((b, a));
                    else directed[(a, b)] = at;
                }
            foreach (int t in tris)
                for (int k = 0; k < 3; k++)
                {
                    int at = 3 * t + k;
                    Corner(p.Position[at], p.Normal[at], s.Uv[at], 0, s.Sub[t]);
                }
            double volume = 0;
            foreach (int t in tris)
            {
                var a = p.Position[3 * t] - apex;
                var b = p.Position[3 * t + 1] - apex;
                var d = p.Position[3 * t + 2] - apex;
                volume += Vector3.Dot(a, Vector3.Cross(b, d)) / 6.0;
            }
            foreach (var kv in directed.OrderBy(kv => kv.Value))
            {
                int at = kv.Value, t = at / 3, k = at % 3;
                int bt = 3 * t + (k + 1) % 3;
                var a = p.Position[at];
                var b = p.Position[bt];
                var face = Vector3.Cross(a - b, apex - b);
                var nor = face.sqrMagnitude > 0 ? face.normalized : -inward;
                Corner(b, nor, s.Uv[bt], 1, insideSub);
                Corner(a, nor, s.Uv[at], 1, insideSub);
                Corner(apex, nor, uvMean, 1, insideSub);
            }
            // Relative to the centroid of its corners' box: the rigidbody's pivot and the collider's centre.
            Vector3 lo = Vector3.positiveInfinity, hi = Vector3.negativeInfinity;
            foreach (var v in vertices) { lo = Vector3.Min(lo, v); hi = Vector3.Max(hi, v); }
            var centre = (lo + hi) * 0.5f;
            for (int i = 0; i < vertices.Count; i++) vertices[i] -= centre;

            var mesh = new Mesh { name = name };
            if (vertices.Count > 65535) mesh.indexFormat = IndexFormat.UInt32;
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uv0);
            mesh.SetUVs(1, inside);
            mesh.subMeshCount = subMeshes;
            for (int i = 0; i < subMeshes; i++) mesh.SetTriangles(indices[i], i, false);
            mesh.RecalculateBounds();
            return new StructureShards.Shard
            {
                Mesh = mesh, Center = centre, Volume = (float)Math.Abs(volume), Size = vertices.Max(v => v.magnitude),
            };
        }
    }
}
