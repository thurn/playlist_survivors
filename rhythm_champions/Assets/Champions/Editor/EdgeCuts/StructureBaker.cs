using System;
using System.IO;
using System.Linq;
using Champions.Render.Structures;
using UnityEditor;
using UnityEngine;

namespace Champions.EditorTools.EdgeCuts
{
    /// <summary>
    /// Bakes a structure's cut (the recipe's cut.json/.bytes for a model without clips) into the structure field's assets
    /// under the ignored Assets/Baked/EdgeCuts/&lt;model&gt;/ (plan.md, Rendering &gt; Edge cuts: "structures use the same
    /// pipeline, static, plus their fill"): the fill mesh, flat-shaded, the line mesh, the shard pieces with their boxes,
    /// and the <see cref="StructureType"/> asset; then the library in Resources that lists every baked structure with the
    /// structure shaders. A structure's cut is rigid on its prefab's renderers, which stand as the prefab does, so the
    /// skins are dropped.
    /// </summary>
    public static class StructureBaker
    {
        public const string LibraryPath = EdgeCutModel.BakedRoot + "/Resources/" + StructureLibrary.ResourcePath + ".asset";
        const string ShaderDir = "Assets/Champions/Render/Structures/Shaders";

        public static StructureType Bake(EdgeCutModel model)
        {
            string dir = EdgeCutModel.Dir(model.id);
            var cut = ArrayFile.Read($"{dir}/cut.json");
            var bodyPositions = cut.Floats("body_positions");
            var bodyPiece = cut.Ints("body_piece");
            var tris = cut.Ints("body_triangles");
            var linePositions = cut.Floats("line_positions");
            var segments = cut.Ints("line_segments");
            var linePiece = cut.Ints("line_piece");
            var pivots = cut.Floats("piece_pivots");
            int pieceCount = cut.Info("piece_sizes").shape[0];

            var fill = FillMesh(model.id, bodyPositions, bodyPiece, tris);
            var lines = LineMesh(model.id, linePositions, segments, cut.Floats("line_importance"), linePiece);
            var pieces = new StructurePiece[pieceCount];
            var lo = new Vector3[pieceCount];
            var hi = new Vector3[pieceCount];
            for (int i = 0; i < pieceCount; i++)
            {
                lo[i] = Vector3.positiveInfinity;
                hi[i] = Vector3.negativeInfinity;
            }
            void Grow(int piece, Vector3 p)
            {
                lo[piece] = Vector3.Min(lo[piece], p);
                hi[piece] = Vector3.Max(hi[piece], p);
            }
            for (int v = 0; v < bodyPiece.Length; v++) Grow(bodyPiece[v], V3(bodyPositions, v));
            for (int s = 0; s < linePiece.Length; s++)
            {
                Grow(linePiece[s], V3(linePositions, segments[2 * s]));
                Grow(linePiece[s], V3(linePositions, segments[2 * s + 1]));
            }
            for (int i = 0; i < pieceCount; i++)
            {
                var pivot = new Vector3(pivots[3 * i], pivots[3 * i + 1], pivots[3 * i + 2]);
                var half = Vector3.Max(Vector3.Max(hi[i] - pivot, pivot - lo[i]), Vector3.one * 0.05f);
                // Its box is a loose fit round a hull's wedge: a little over half of it is solid.
                pieces[i] = new StructurePiece { Pivot = pivot, HalfSize = half, Volume = 0.55f * 8 * half.x * half.y * half.z };
            }

            Directory.CreateDirectory(dir);
            Replace(fill, $"{dir}/{model.id}_fill.mesh");
            Replace(lines, $"{dir}/{model.id}_lines.mesh");
            var type = ScriptableObject.CreateInstance<StructureType>();
            type.Id = model.id;
            type.Fill = fill;
            type.Lines = lines;
            type.Pieces = pieces;
            var b = fill.bounds;
            type.HeightM = b.max.y;
            type.ExtentM = new Vector2(b.size.x, b.size.z);
            type.Triangles = tris.Length / 3;
            type.Segments = segments.Length / 2;
            Replace(type, $"{dir}/{model.id}.asset");
            WriteLibrary();
            Debug.Log($"StructureBaker: {model.id}: {type.Triangles} fill triangles, {type.Segments} line segments, {pieces.Length} pieces, " +
                      $"{type.ExtentM.x:F2} x {type.ExtentM.y:F2} m on the ground, {type.HeightM:F2} m tall");
            return type;
        }

        static Vector3 V3(float[] a, int i) => new Vector3(a[3 * i], a[3 * i + 1], a[3 * i + 2]);

        /// <summary>Each triangle its own three vertices with its face's normal (flat shading), and its piece.</summary>
        static Mesh FillMesh(string id, float[] positions, int[] piece, int[] tris)
        {
            int n = tris.Length;
            var pos = new Vector3[n];
            var normals = new Vector3[n];
            var pieces = new Vector4[n];
            var indices = new int[n];
            for (int t = 0; t < n / 3; t++)
            {
                var a = V3(positions, tris[3 * t]);
                var b = V3(positions, tris[3 * t + 1]);
                var c = V3(positions, tris[3 * t + 2]);
                var normal = Vector3.Cross(b - a, c - a).normalized;
                for (int k = 0; k < 3; k++)
                {
                    int v = 3 * t + k;
                    pos[v] = V3(positions, tris[v]);
                    normals[v] = normal;
                    pieces[v] = new Vector4(piece[tris[v]], 0, 0, 0);
                    indices[v] = v;
                }
            }
            var m = new Mesh { name = id + "_fill" };
            m.SetVertices(pos);
            m.SetNormals(normals);
            m.SetUVs(1, pieces);
            m.SetTriangles(indices, 0, false);
            m.RecalculateBounds();
            return m;
        }

        /// <summary>Each segment a quad: per corner its own end and the other with the side, and the segment's importance and piece.</summary>
        static Mesh LineMesh(string id, float[] positions, int[] seg, float[] importance, int[] piece)
        {
            int n = importance.Length;
            var pos = new Vector3[4 * n];
            var other = new Vector4[4 * n];
            var info = new Vector4[4 * n];
            var tris = new int[6 * n];
            for (int s = 0; s < n; s++)
            {
                int a = seg[2 * s], c = seg[2 * s + 1];
                for (int k = 0; k < 4; k++)
                {
                    int v = 4 * s + k;
                    // Corners 0, 1 at end a and 2, 3 at end c; the side flips with the end, as the crowd's lines'.
                    float side = (k == 0 || k == 3) ? -1 : 1;
                    pos[v] = V3(positions, k < 2 ? a : c);
                    var o = V3(positions, k < 2 ? c : a);
                    other[v] = new Vector4(o.x, o.y, o.z, side);
                    info[v] = new Vector4(importance[s], piece[s], 0, 0);
                }
                int q = 4 * s;
                tris[6 * s] = q; tris[6 * s + 1] = q + 1; tris[6 * s + 2] = q + 2;
                tris[6 * s + 3] = q; tris[6 * s + 4] = q + 2; tris[6 * s + 5] = q + 3;
            }
            var m = new Mesh { name = id + "_lines" };
            m.SetVertices(pos);
            m.SetUVs(0, other);
            m.SetUVs(1, info);
            m.SetTriangles(tris, 0, false);
            m.RecalculateBounds();
            return m;
        }

        /// <summary>Lists every baked structure in the library asset, with the structure shaders.</summary>
        public static void WriteLibrary()
        {
            var types = AssetDatabase.FindAssets("t:" + nameof(StructureType), new[] { EdgeCutModel.BakedRoot })
                .Select(g => AssetDatabase.LoadAssetAtPath<StructureType>(AssetDatabase.GUIDToAssetPath(g)))
                .Where(t => t != null).OrderBy(t => t.Id, StringComparer.Ordinal).ToArray();
            var lib = ScriptableObject.CreateInstance<StructureLibrary>();
            lib.Types = types;
            lib.Fill = Load<Shader>($"{ShaderDir}/StructureFill.shader");
            lib.Lines = Load<Shader>($"{ShaderDir}/StructureLines.shader");
            Directory.CreateDirectory(Path.GetDirectoryName(LibraryPath));
            Replace(lib, LibraryPath);
            AssetDatabase.SaveAssets();
        }

        static T Load<T>(string path) where T : UnityEngine.Object =>
            AssetDatabase.LoadAssetAtPath<T>(path) ?? throw new Exception("StructureBaker: no " + path);

        static void Replace(UnityEngine.Object asset, string path)
        {
            AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(asset, path);
        }
    }
}
