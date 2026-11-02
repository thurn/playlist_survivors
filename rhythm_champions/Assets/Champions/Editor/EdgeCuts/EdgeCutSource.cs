using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using UnityEngine;

namespace Champions.EditorTools.EdgeCuts
{
    /// <summary>
    /// A spawned model's active renderers combined into one mesh in root space at its height, as game/'s crowd baker
    /// combined them (ported by review): each skin's bones and each rigid attachment become an entry, a transform with
    /// the bind matrix that maps root-space vertices into its space, and every vertex keeps its 4 strongest entries.
    /// <see cref="Write"/> exports it for the recipe as source.json/.bytes (<see cref="ArrayFile"/>); the baker
    /// combines the same spawn again to sample the clips onto the same entries.
    /// </summary>
    public sealed class EdgeCutSource
    {
        public sealed class Entry
        {
            public Transform Bone;
            public Matrix4x4 Bind;
            public string Path;
        }

        public readonly GameObject Root;
        public readonly List<Entry> Entries = new List<Entry>();
        public readonly List<Vector3> Positions = new List<Vector3>();
        public readonly List<Vector3> Normals = new List<Vector3>();
        public readonly List<Vector4> BoneIndices = new List<Vector4>();
        public readonly List<Vector4> BoneWeights = new List<Vector4>();
        public readonly List<int> Indices = new List<int>();
        /// <summary>Per triangle: its renderer (into <see cref="Renderers"/>) and the submesh it came from.</summary>
        public readonly List<int> TriangleRenderer = new List<int>(), TriangleSubmesh = new List<int>();
        public readonly List<Renderer> Renderers = new List<Renderer>();

        public EdgeCutSource(GameObject root)
        {
            Root = root;
            Combine();
        }

        /// <summary>The root's rotation and translation (not its scale: the mesh is at the model's height).</summary>
        public Matrix4x4 RootTR => Matrix4x4.TRS(Root.transform.position, Root.transform.rotation, Vector3.one);

        int EntryIndex(Transform bone, Matrix4x4 bind)
        {
            for (int i = 0; i < Entries.Count; i++)
                if (Entries[i].Bone == bone && Near(Entries[i].Bind, bind)) return i;
            Entries.Add(new Entry { Bone = bone, Bind = bind, Path = PathOf(bone) });
            return Entries.Count - 1;
        }

        static bool Near(Matrix4x4 a, Matrix4x4 c)
        {
            for (int i = 0; i < 16; i++)
                if (Mathf.Abs(a[i] - c[i]) > 1e-3f) return false; // 1 mm: the same bone seen from two skins
            return true;
        }

        string PathOf(Transform t)
        {
            var parts = new List<string>();
            for (var x = t; x != null && x != Root.transform; x = x.parent) parts.Add(x.name);
            parts.Reverse();
            return string.Join("/", parts);
        }

        void Combine()
        {
            var rootInv = RootTR.inverse;
            foreach (var r in Root.GetComponentsInChildren<Renderer>(false))
            {
                if (!r.enabled) continue;
                Mesh mesh;
                Transform[] bones = null;
                Matrix4x4[] bindposes = null;
                if (r is SkinnedMeshRenderer smr)
                {
                    mesh = smr.sharedMesh;
                    bones = smr.bones;
                    bindposes = mesh != null ? mesh.bindposes : null;
                }
                else if (r is MeshRenderer) mesh = r.GetComponent<MeshFilter>()?.sharedMesh;
                else continue;
                if (mesh == null) continue;
                bool rigid = bones == null || bones.Length == 0 || bindposes == null || bindposes.Length == 0;
                // Vertices move from the renderer's mesh space into root space at the reference pose; each bind
                // absorbs the inverse, so skinning is unchanged exactly.
                var toRoot = rootInv * r.transform.localToWorldMatrix;
                var fromRoot = toRoot.inverse;
                int[] entryOf;
                if (rigid) entryOf = new[] { EntryIndex(r.transform, fromRoot) };
                else
                {
                    entryOf = new int[bones.Length];
                    for (int i = 0; i < bones.Length; i++)
                        entryOf[i] = bones[i] == null ? -1 : EntryIndex(bones[i], bindposes[i] * fromRoot);
                }

                int renderer = Renderers.Count;
                Renderers.Add(r);
                int baseVertex = Positions.Count;
                var verts = mesh.vertices;
                var normals = mesh.normals;
                var perVertex = rigid ? default : mesh.GetBonesPerVertex();
                var weights = rigid ? default : mesh.GetAllBoneWeights();
                int w0 = 0;
                for (int v = 0; v < verts.Length; v++)
                {
                    Positions.Add(toRoot.MultiplyPoint3x4(verts[v]));
                    Normals.Add(normals.Length > v ? toRoot.MultiplyVector(normals[v]).normalized : Vector3.up);
                    var idx = Vector4.zero;
                    var wt = Vector4.zero;
                    if (rigid) { idx.x = entryOf[0]; wt.x = 1; }
                    else
                    {
                        // The 4 strongest influences (Unity sorts them by weight), renormalized.
                        int count = perVertex[v];
                        float sum = 0;
                        int k = 0;
                        for (int i = 0; i < count && k < 4; i++)
                        {
                            var bw = weights[w0 + i];
                            int e = bw.boneIndex < entryOf.Length ? entryOf[bw.boneIndex] : -1;
                            if (e < 0 || bw.weight <= 0) continue;
                            idx[k] = e;
                            wt[k] = bw.weight;
                            sum += bw.weight;
                            k++;
                        }
                        w0 += count;
                        if (sum > 0) wt /= sum;
                        else { idx.x = EntryIndex(r.transform, fromRoot); wt = new Vector4(1, 0, 0, 0); }
                    }
                    BoneIndices.Add(idx);
                    BoneWeights.Add(wt);
                }
                for (int s = 0; s < mesh.subMeshCount; s++)
                {
                    var tris = mesh.GetTriangles(s);
                    foreach (var i in tris) Indices.Add(baseVertex + i);
                    for (int t = 0; t < tris.Length / 3; t++)
                    {
                        TriangleRenderer.Add(renderer);
                        TriangleSubmesh.Add(s);
                    }
                }
            }
            if (Positions.Count == 0) throw new Exception($"EdgeCutSource: {Root.name} has no active renderers");
        }

        /// <summary>
        /// source.json/.bytes for the recipe: positions, normals, bones (entry indices) and weights per vertex,
        /// triangles with their renderer and submesh, and in the header each entry's name, path and bind (row-major),
        /// and each renderer's name, path, kind and materials.
        /// </summary>
        public void Write(string json, EdgeCutModel model)
        {
            var b = new ArrayFile.Builder();
            int n = Positions.Count;
            b.Add("positions", Flatten(Positions), n, 3);
            b.Add("normals", Flatten(Normals), n, 3);
            b.Add("bones", BoneIndices.SelectMany(v => new[] { (int)v.x, (int)v.y, (int)v.z, (int)v.w }).ToArray(), n, 4);
            b.Add("weights", BoneWeights.SelectMany(v => new[] { v.x, v.y, v.z, v.w }).ToArray(), n, 4);
            b.Add("triangles", Indices.ToArray(), Indices.Count / 3, 3);
            b.Add("triangle_renderer", TriangleRenderer.ToArray(), TriangleRenderer.Count);
            b.Add("triangle_submesh", TriangleSubmesh.ToArray(), TriangleSubmesh.Count);
            b.Add("binds", Entries.SelectMany(e => Enumerable.Range(0, 16).Select(i => e.Bind[i / 4, i % 4])).ToArray(), Entries.Count, 4, 4);

            var sb = new StringBuilder();
            sb.Append("{\n  \"version\": 1,\n  \"model\": ").Append(Quote(model.id))
                .Append(",\n  \"heightM\": ").Append(model.heightM.ToString("R", CultureInfo.InvariantCulture))
                .Append(",\n  \"entries\": [");
            for (int i = 0; i < Entries.Count; i++)
                sb.Append(i == 0 ? "\n" : ",\n").Append("    {\"name\": ").Append(Quote(Entries[i].Bone.name))
                    .Append(", \"path\": ").Append(Quote(Entries[i].Path)).Append('}');
            sb.Append("\n  ],\n  \"renderers\": [");
            for (int i = 0; i < Renderers.Count; i++)
            {
                var r = Renderers[i];
                sb.Append(i == 0 ? "\n" : ",\n").Append("    {\"name\": ").Append(Quote(r.name))
                    .Append(", \"path\": ").Append(Quote(PathOf(r.transform)))
                    .Append(", \"skinned\": ").Append(r is SkinnedMeshRenderer ? "true" : "false")
                    .Append(", \"materials\": [").Append(string.Join(", ", r.sharedMaterials.Select(m => Quote(m != null ? m.name : ""))))
                    .Append("]}");
            }
            sb.Append("\n  ],\n  \"arrays\": [");
            var infos = b.Infos;
            for (int i = 0; i < infos.Length; i++)
                sb.Append(i == 0 ? "\n    " : ",\n    ").Append(JsonUtility.ToJson(infos[i]));
            sb.Append("\n  ]\n}\n");
            b.Write(json, sb.ToString());
        }

        static float[] Flatten(List<Vector3> v) => v.SelectMany(p => new[] { p.x, p.y, p.z }).ToArray();

        static string Quote(string s)
        {
            var sb = new StringBuilder("\"");
            foreach (var c in s)
            {
                if (c == '"' || c == '\\') sb.Append('\\').Append(c);
                else if (c < ' ') sb.Append("\\u").Append(((int)c).ToString("x4"));
                else sb.Append(c);
            }
            return sb.Append('"').ToString();
        }
    }
}
