using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;

namespace StyleLab.EditorTools
{
    /// <summary>
    /// The style mesh: a vendor mesh unwelded so every triangle has its own three vertices,
    /// carrying what the treatments need (StyleLabCommon.hlsl, SLAttributes): barycentric
    /// coordinates and a random value per triangle (uv2), the bind-pose position in metres in
    /// the type's root space (uv3), and the face normal in the tangent, which skinning turns
    /// with the bones. Skin weights and bind poses carry over.
    /// </summary>
    public static class StyleMesh
    {
        public static Mesh Build(Mesh src, Vector3[] rest, int seed)
        {
            var verts = src.vertices;
            var normals = src.normals;
            var uvs = src.uv;
            bool skinned = src.bindposes.Length > 0;
            NativeArray<byte> perVertex = default;
            NativeArray<BoneWeight1> weights = default;
            var starts = new int[verts.Length];
            if (skinned)
            {
                perVertex = src.GetBonesPerVertex();
                weights = src.GetAllBoneWeights();
                int at = 0;
                for (int i = 0; i < verts.Length; i++) { starts[i] = at; at += perVertex[i]; }
            }

            var pos = new List<Vector3>();
            var nor = new List<Vector3>();
            var tan = new List<Vector4>();
            var uv0 = new List<Vector2>();
            var uv2 = new List<Vector4>();
            var uv3 = new List<Vector4>();
            var outPerVertex = new List<byte>();
            var outWeights = new List<BoneWeight1>();
            var subTris = new List<List<int>>();
            var rng = new System.Random(seed);
            var bary = new[] { new Vector3(1, 0, 0), new Vector3(0, 1, 0), new Vector3(0, 0, 1) };

            for (int s = 0; s < src.subMeshCount; s++)
            {
                var tris = src.GetTriangles(s);
                var outTris = new List<int>(tris.Length);
                for (int t = 0; t + 2 < tris.Length; t += 3)
                {
                    int a = tris[t], b = tris[t + 1], c = tris[t + 2];
                    var face = Vector3.Cross(verts[b] - verts[a], verts[c] - verts[a]).normalized;
                    float r = (float)rng.NextDouble();
                    int k = 0;
                    foreach (int v in new[] { a, b, c })
                    {
                        outTris.Add(pos.Count);
                        pos.Add(verts[v]);
                        nor.Add(normals.Length > 0 ? normals[v] : face);
                        tan.Add(new Vector4(face.x, face.y, face.z, 1));
                        uv0.Add(uvs.Length > 0 ? uvs[v] : Vector2.zero);
                        uv2.Add(new Vector4(bary[k].x, bary[k].y, bary[k].z, r));
                        uv3.Add(rest != null && v < rest.Length ? (Vector4)rest[v] : Vector4.zero);
                        if (skinned)
                        {
                            outPerVertex.Add(perVertex[v]);
                            for (int w = 0; w < perVertex[v]; w++) outWeights.Add(weights[starts[v] + w]);
                        }
                        k++;
                    }
                }
                subTris.Add(outTris);
            }

            var mesh = new Mesh { name = src.name + " (style)" };
            mesh.indexFormat = pos.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.SetVertices(pos);
            mesh.SetNormals(nor);
            mesh.SetTangents(tan);
            mesh.SetUVs(0, uv0);
            mesh.SetUVs(2, uv2);
            mesh.SetUVs(3, uv3);
            mesh.subMeshCount = subTris.Count;
            for (int s = 0; s < subTris.Count; s++) mesh.SetTriangles(subTris[s], s);
            if (skinned)
            {
                mesh.bindposes = src.bindposes;
                using var pv = new NativeArray<byte>(outPerVertex.ToArray(), Allocator.Temp);
                using var bw = new NativeArray<BoneWeight1>(outWeights.ToArray(), Allocator.Temp);
                mesh.SetBoneWeights(pv, bw);
            }
            mesh.bounds = src.bounds;
            return mesh;
        }
    }
}
