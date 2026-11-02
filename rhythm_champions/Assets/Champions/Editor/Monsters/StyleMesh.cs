using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Champions.EditorTools.Monsters
{
    /// <summary>
    /// The monster baker's unweld step (plan.md, Rendering &gt; The edge look), the frozen look package's StyleMesh,
    /// which made the neon edge trailer's meshes, ported by review and unchanged: a welded crowd mesh made into the style
    /// mesh MonsterEdge draws, and the shard cutter cuts (<see cref="MonsterShardBaker"/>).
    /// </summary>
    public static class StyleMesh
    {
        /// <summary>
        /// A welded crowd mesh unwelded: every triangle gets its own three vertices, so a look can facet, edge, move
        /// or colour each one alone. It keeps the positions (the rest pose in metres), normals, UV0 where the mesh
        /// has it and the bone data (UV1/UV2), and adds the face normal in the tangent, skinned like a normal, and
        /// barycentric coordinates plus a random value per triangle in UV3, and its feature edges in UV5
        /// (<see cref="FeatureEdges"/>). Submeshes and triangle order are kept.
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
            var oEdges = new List<Vector3>(n);
            var all = new List<int>(n);
            for (int sm = 0; sm < src.subMeshCount; sm++) all.AddRange(src.GetIndices(sm));
            var edges = FeatureEdges(pos, all);
            int tri = 0;
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
                    var e = edges[tri++];
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
                        oEdges.Add(e);
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
            m.SetUVs(5, oEdges);
            m.subMeshCount = subs.Count;
            for (int sm = 0; sm < subs.Count; sm++) m.SetTriangles(subs[sm], sm, false);
            m.bounds = src.bounds;
            return m;
        }

        /// <summary>
        /// The dihedral angle, in degrees, over which an edge two faces share is a feature edge (the trailer's). The
        /// vendor models' low-poly bodies are flat facets cut into triangles (0 inside a facet); 24 outlines the elites'
        /// blocky facets and leaves a mushroom's smooth cap a few strokes rather than rings of lines.
        /// </summary>
        public const float FeatureAngleDeg = 24f;

        /// <summary>
        /// Each triangle's feature edges: for triangle t of
        /// <paramref name="tris"/> (three indices each, over all submeshes), component k is 1 when the edge opposite its
        /// corner k is a real edge of the model, not the triangulation inside a facet. An edge is a feature when the
        /// faces either side of it turn by more than <paramref name="angleDeg"/>, or when it is open (one face), or
        /// shared by more than two (overlapping shells). Corners are welded by position (0.1 mm), so UV and material
        /// seams don't count; a degenerate triangle has none and is left out. Unwelded, a corner k has barycentric k
        /// 1 and the edge opposite it is where that barycentric is 0, so a shader draws a line where a flagged
        /// barycentric is small.
        /// </summary>
        public static Vector3[] FeatureEdges(IReadOnlyList<Vector3> pos, IReadOnlyList<int> tris, float angleDeg = FeatureAngleDeg)
        {
            int n = tris.Count / 3;
            var weld = new Dictionary<(long, long, long), int>();
            var ids = new int[n * 3];
            for (int k = 0; k < ids.Length; k++)
            {
                var v = pos[tris[k]];
                var key = ((long)Mathf.Round(v.x * 1e4f), (long)Mathf.Round(v.y * 1e4f), (long)Mathf.Round(v.z * 1e4f));
                if (!weld.TryGetValue(key, out int id)) weld[key] = id = weld.Count;
                ids[k] = id;
            }
            var face = new Vector3[n];
            var sides = new Dictionary<(int, int), List<int>>(); // welded edge -> 3t + the corner opposite it
            for (int t = 0; t < n; t++)
            {
                var cross = Vector3.Cross(pos[tris[3 * t + 1]] - pos[tris[3 * t]], pos[tris[3 * t + 2]] - pos[tris[3 * t]]);
                float len = cross.magnitude;
                if (len <= 1e-12f) continue;
                face[t] = cross / len;
                for (int k = 0; k < 3; k++)
                {
                    int a = ids[3 * t + (k + 1) % 3], b = ids[3 * t + (k + 2) % 3];
                    if (a == b) continue;
                    var edge = a < b ? (a, b) : (b, a);
                    if (!sides.TryGetValue(edge, out var list)) sides[edge] = list = new List<int>(2);
                    list.Add(3 * t + k);
                }
            }
            float cos = Mathf.Cos(angleDeg * Mathf.Deg2Rad);
            var flags = new Vector3[n];
            foreach (var list in sides.Values)
            {
                bool feature = list.Count != 2 || Vector3.Dot(face[list[0] / 3], face[list[1] / 3]) < cos;
                if (!feature) continue;
                foreach (int c in list) flags[c / 3][c % 3] = 1;
            }
            return flags;
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
