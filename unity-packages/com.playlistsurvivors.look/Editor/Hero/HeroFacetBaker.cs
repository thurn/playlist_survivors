using System.Collections.Generic;
using PS.Look.EditorTools.Crowd;
using PS.Look.Hero;
using Unity.Collections;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace PS.Look.EditorTools.Hero
{
    /// <summary>
    /// The hero bake's faceting step (neon-look.md, The Axes &gt; Hero): every renderer of the knight gets a faceted
    /// copy of its mesh (<see cref="Faceted"/>), saved beside the bake and tagged on the renderer
    /// (<see cref="HeroFacets"/>), for a hero look to draw. Done at bake time because the vendor meshes aren't
    /// readable in a player; derived from them, so the bake's folder is gitignored. game's HeroBaker and demo3d's
    /// RosterBaker (its hero) call it on the prefab before saving it.
    /// </summary>
    public static class HeroFacetBaker
    {
        /// <summary>
        /// Facets every enabled renderer under <paramref name="hero"/> (skinned, or a mesh filter's: the shield, a
        /// weapon), each mesh once, into <paramref name="dir"/>. Returns how many renderers it tagged.
        /// </summary>
        public static int Facet(GameObject hero, string dir)
        {
            var made = new Dictionary<Mesh, Mesh>();
            int n = 0;
            foreach (var r in hero.GetComponentsInChildren<Renderer>(false))
            {
                var mesh = r is SkinnedMeshRenderer skinned ? skinned.sharedMesh
                    : r is MeshRenderer && r.TryGetComponent<MeshFilter>(out var filter) ? filter.sharedMesh : null;
                if (mesh == null) continue;
                if (!made.TryGetValue(mesh, out var faceted))
                {
                    faceted = Faceted(mesh, StyleMesh.StableSeed(mesh.name));
                    var path = AssetDatabase.GenerateUniqueAssetPath($"{dir}/{Safe(hero.name)}_{Safe(mesh.name)}_facets.asset");
                    AssetDatabase.CreateAsset(faceted, path);
                    made[mesh] = faceted;
                }
                var tag = r.GetComponent<HeroFacets>() ?? r.gameObject.AddComponent<HeroFacets>();
                tag.faceted = faceted;
                n++;
            }
            Debug.Log($"HeroFacetBaker: {n} renderers of {hero.name} faceted ({made.Count} meshes) into {dir}");
            return n;
        }

        /// <summary>
        /// A mesh unwelded as the crowd's style mesh is (StyleMesh.Unweld): every triangle its own three vertices, with
        /// its normals and UV0, its barycentrics plus a random value per triangle in UV3 and its feature edges in UV5
        /// (<see cref="StyleMesh.FeatureEdges"/>); a skinned mesh keeps its bone weights and bind poses, so the
        /// renderer skins it as before. UV4 stays free: URP's skinned motion vectors take that slot. Submeshes and
        /// triangle order are kept.
        /// </summary>
        public static Mesh Faceted(Mesh src, int seed)
        {
            var pos = src.vertices;
            var nor = src.normals;
            if (nor.Length != pos.Length) { src = Object.Instantiate(src); src.RecalculateNormals(); nor = src.normals; }
            var uv0 = new List<Vector2>(); src.GetUVs(0, uv0);
            bool hasUv0 = uv0.Count == pos.Length;
            var perVertex = src.GetBonesPerVertex();
            var weights = src.GetAllBoneWeights();
            bool skinned = perVertex.Length == pos.Length && weights.Length > 0;
            var firstWeight = new int[skinned ? pos.Length : 0];
            for (int v = 1; v < firstWeight.Length; v++) firstWeight[v] = firstWeight[v - 1] + perVertex[v - 1];

            var all = new List<int>();
            for (int sm = 0; sm < src.subMeshCount; sm++) all.AddRange(src.GetIndices(sm));
            var edges = StyleMesh.FeatureEdges(pos, all);
            int count = all.Count;
            var oPos = new List<Vector3>(count);
            var oNor = new List<Vector3>(count);
            var oUv0 = new List<Vector2>(hasUv0 ? count : 0);
            var oBary = new List<Vector4>(count);
            var oEdges = new List<Vector3>(count);
            var oPerVertex = new List<byte>(skinned ? count : 0);
            var oWeights = new List<BoneWeight1>();
            var subs = new List<int[]>();
            var rng = new System.Random(seed);
            int tri = 0;
            for (int sm = 0; sm < src.subMeshCount; sm++)
            {
                var tris = src.GetIndices(sm);
                var outTris = new int[tris.Length];
                for (int t = 0; t + 2 < tris.Length; t += 3)
                {
                    float r = (float)rng.NextDouble();
                    var e = edges[tri++];
                    for (int k = 0; k < 3; k++)
                    {
                        int v = tris[t + k];
                        outTris[t + k] = oPos.Count;
                        oPos.Add(pos[v]);
                        oNor.Add(nor[v]);
                        if (hasUv0) oUv0.Add(uv0[v]);
                        oBary.Add(new Vector4(k == 0 ? 1 : 0, k == 1 ? 1 : 0, k == 2 ? 1 : 0, r));
                        oEdges.Add(e);
                        if (!skinned) continue;
                        oPerVertex.Add(perVertex[v]);
                        for (int w = 0; w < perVertex[v]; w++) oWeights.Add(weights[firstWeight[v] + w]);
                    }
                }
                subs.Add(outTris);
            }
            var m = new Mesh { name = src.name + "_facets" };
            if (oPos.Count > 65535) m.indexFormat = IndexFormat.UInt32;
            m.SetVertices(oPos);
            m.SetNormals(oNor);
            if (hasUv0) m.SetUVs(0, oUv0);
            m.SetUVs(3, oBary);
            m.SetUVs(5, oEdges);
            m.subMeshCount = subs.Count;
            for (int sm = 0; sm < subs.Count; sm++) m.SetTriangles(subs[sm], sm, false);
            if (skinned)
            {
                using var pv = new NativeArray<byte>(oPerVertex.ToArray(), Allocator.Temp);
                using var bw = new NativeArray<BoneWeight1>(oWeights.ToArray(), Allocator.Temp);
                m.SetBoneWeights(pv, bw);
                m.bindposes = src.bindposes;
            }
            m.bounds = src.bounds;
            return m;
        }

        static string Safe(string name)
        {
            var chars = name.ToCharArray();
            for (int i = 0; i < chars.Length; i++)
                if (!char.IsLetterOrDigit(chars[i]) && chars[i] != '_' && chars[i] != '-') chars[i] = '_';
            return new string(chars);
        }
    }
}
