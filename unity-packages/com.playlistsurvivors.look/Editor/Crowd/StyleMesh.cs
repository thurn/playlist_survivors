using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace PS.Look.EditorTools.Crowd
{
    /// <summary>
    /// The crowd bakers' unweld step (neon-look.md, The Package; stylelab's StyleMesh): a welded crowd mesh made into
    /// the style mesh every monster look draws, and the shard cutter cuts (<see cref="CrowdShardBaker"/>).
    /// </summary>
    public static class StyleMesh
    {
        /// <summary>
        /// A welded crowd mesh unwelded: every triangle gets its own three vertices, so a look can facet, edge, move
        /// or colour each one alone. It keeps the positions (the rest pose in metres), normals, UV0 where the mesh
        /// has it and the bone data (UV1/UV2), and adds the face normal in the tangent, skinned like a normal, and
        /// barycentric coordinates plus a random value per triangle in UV3. Submeshes and triangle order are kept.
        /// </summary>
        public static Mesh Unweld(Mesh src, int seed)
        {
            var pos = src.vertices;
            var nor = src.normals;
            var uv0 = new List<Vector2>(); src.GetUVs(0, uv0);
            bool hasUv0 = uv0.Count == pos.Length;
            var bones = new List<Vector4>(); src.GetUVs(1, bones);
            var weights = new List<Vector4>(); src.GetUVs(2, weights);
            int n = 0;
            for (int sm = 0; sm < src.subMeshCount; sm++) n += (int)src.GetIndexCount(sm);
            var oPos = new List<Vector3>(n);
            var oNor = new List<Vector3>(n);
            var oTan = new List<Vector4>(n);
            var oUv0 = new List<Vector2>(hasUv0 ? n : 0);
            var oBones = new List<Vector4>(n);
            var oWeights = new List<Vector4>(n);
            var oBary = new List<Vector4>(n);
            var subs = new List<int[]>();
            var rng = new System.Random(seed);
            for (int sm = 0; sm < src.subMeshCount; sm++)
            {
                var tris = src.GetIndices(sm); // with the submesh's base vertex applied
                var outTris = new int[tris.Length];
                for (int t = 0; t + 2 < tris.Length; t += 3)
                {
                    // Normalized by hand: Vector3.normalized zeroes a tiny triangle's normal, which the
                    // shader would turn into NaN. A degenerate one takes its first vertex's normal.
                    var cross = Vector3.Cross(pos[tris[t + 1]] - pos[tris[t]], pos[tris[t + 2]] - pos[tris[t]]);
                    float len = cross.magnitude;
                    var face = len > 0 ? cross / len : nor[tris[t]];
                    float r = (float)rng.NextDouble();
                    for (int k = 0; k < 3; k++)
                    {
                        int v = tris[t + k];
                        outTris[t + k] = oPos.Count;
                        oPos.Add(pos[v]);
                        oNor.Add(nor[v]);
                        oTan.Add(new Vector4(face.x, face.y, face.z, 1));
                        if (hasUv0) oUv0.Add(uv0[v]);
                        oBones.Add(bones[v]);
                        oWeights.Add(weights[v]);
                        oBary.Add(new Vector4(k == 0 ? 1 : 0, k == 1 ? 1 : 0, k == 2 ? 1 : 0, r));
                    }
                }
                subs.Add(outTris);
            }
            var m = new Mesh { name = src.name + "_style" };
            if (oPos.Count > 65535) m.indexFormat = IndexFormat.UInt32;
            m.SetVertices(oPos);
            m.SetNormals(oNor);
            m.SetTangents(oTan);
            if (hasUv0) m.SetUVs(0, oUv0);
            m.SetUVs(1, oBones);
            m.SetUVs(2, oWeights);
            m.SetUVs(3, oBary);
            m.subMeshCount = subs.Count;
            for (int sm = 0; sm < subs.Count; sm++) m.SetTriangles(subs[sm], sm, false);
            m.bounds = src.bounds;
            return m;
        }

        /// <summary>A seed from a type id that is the same in every run (string.GetHashCode needn't be).</summary>
        public static int StableSeed(string id)
        {
            int h = 17;
            foreach (char c in id) h = unchecked(h * 31 + c);
            return h;
        }
    }
}
