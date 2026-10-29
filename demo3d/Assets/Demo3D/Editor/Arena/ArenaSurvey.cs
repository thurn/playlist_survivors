using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Demo3D.Choreography;
using UnityEditor;
using UnityEngine;

namespace Demo3D.EditorTools.Arena
{
    /// <summary>
    /// Lists what the kit's prefabs contain (shaders and their properties, components, meshes'
    /// lightmap UVs and triangles, LOD groups), for the material converter and the generator.
    /// `demo3d unity Demo3D.EditorTools.Arena.ArenaSurvey.Run -out <file>`.
    /// </summary>
    public static class ArenaSurvey
    {
        public static void Run()
        {
            var outPath = TaskArgs.Get("-out", "Logs/arena-survey.txt");
            var fp = FootprintTable.Parse(File.ReadAllText(ArenaBaker.FootprintsPath));
            var sb = new StringBuilder();
            var shaders = new Dictionary<string, HashSet<string>>();
            foreach (var f in fp.All)
            {
                var path = AssetDatabase.GUIDToAssetPath(f.Guid);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null) { sb.AppendLine($"{f.Id}: MISSING {f.Guid}"); continue; }
                var comps = prefab.GetComponentsInChildren<Component>(true).Select(c => c == null ? "missing-script" : c.GetType().Name)
                    .GroupBy(n => n).Select(g => g.Count() > 1 ? $"{g.Key}×{g.Count()}" : g.Key);
                long tris = 0;
                bool uv2 = true;
                foreach (var mf in prefab.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (mf.sharedMesh == null) continue;
                    for (int s = 0; s < mf.sharedMesh.subMeshCount; s++) tris += mf.sharedMesh.GetIndexCount(s) / 3;
                    uv2 &= mf.sharedMesh.uv2 != null && mf.sharedMesh.uv2.Length > 0;
                }
                var mats = prefab.GetComponentsInChildren<Renderer>(true).SelectMany(r => r.sharedMaterials).Where(m => m != null).Distinct();
                sb.AppendLine($"{f.Id}: {tris} tris, uv2 {uv2}, scale {prefab.transform.localScale.x:F2}; {string.Join(", ", comps)}");
                foreach (var m in mats)
                {
                    var sh = m.shader != null ? m.shader.name : "null";
                    sb.AppendLine($"    {m.name} [{sh}] kw {string.Join(" ", m.shaderKeywords)}");
                    if (!shaders.TryGetValue(sh, out var set)) shaders[sh] = set = new HashSet<string>();
                    var so = new SerializedObject(m);
                    foreach (var name in new[] { "m_SavedProperties.m_TexEnvs", "m_SavedProperties.m_Colors", "m_SavedProperties.m_Floats" })
                    {
                        var arr = so.FindProperty(name);
                        if (arr == null) continue;
                        for (int i = 0; i < arr.arraySize; i++)
                        {
                            var el = arr.GetArrayElementAtIndex(i);
                            var key = el.FindPropertyRelative("first").stringValue;
                            if (name.EndsWith("TexEnvs"))
                            {
                                var tex = el.FindPropertyRelative("second.m_Texture").objectReferenceValue;
                                if (tex != null) set.Add(key + "=" + tex.name);
                            }
                            else if (name.EndsWith("Colors")) set.Add(key);
                        }
                    }
                }
            }
            sb.AppendLine().AppendLine("Shaders:");
            foreach (var kv in shaders) sb.AppendLine($"  {kv.Key}: {string.Join(", ", kv.Value.OrderBy(x => x).Take(60))}");
            Directory.CreateDirectory(Path.GetDirectoryName(outPath));
            File.WriteAllText(outPath, sb.ToString());
            Debug.Log("ArenaSurvey: wrote " + outPath);
        }
    }
}
