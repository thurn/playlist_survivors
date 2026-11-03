using System.Collections.Generic;
using System.IO;
using Champions.EditorTools.Monsters;
using Champions.Render;
using UnityEditor;
using UnityEngine;

namespace Champions.EditorTools.Effects
{
    /// <summary>Authored fallback geometry for Hovl U1–U5 in combat-assets.md; no stock luminous puddles.</summary>
    public static class MagicBaker
    {
        public static void Bake()
        {
            string path = EffectBaker.Dir + "/MagicWeapons.asset";
            Directory.CreateDirectory(EffectBaker.Dir);
            AssetDatabase.DeleteAsset(path);
            var lib = ScriptableObject.CreateInstance<MagicLibrary>();
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            lib.Link = Style(Object.Instantiate(cube.GetComponent<MeshFilter>().sharedMesh), "Closed bolt prism");
            Object.DestroyImmediate(cube);
            lib.Node = Style(new Mesh { vertices = new[] { Vector3.up, Vector3.down, Vector3.right, Vector3.forward, Vector3.left, Vector3.back },
                triangles = new[] { 0,3,2, 0,4,3, 0,5,4, 0,2,5, 1,2,3, 1,3,4, 1,4,5, 1,5,2 } }, "Faceted node");
            var vertices = new List<Vector3>(); var indices = new List<int>();
            // A closed bowl shell, with a dark opening; eight sides and explicit thickness.
            for (int ring = 0; ring < 4; ring++)
                for (int k = 0; k < 8; k++)
                {
                    float angle = k * Mathf.PI / 4;
                    float r = ring == 0 ? .45f : ring == 1 ? 1 : ring == 2 ? .82f : .3f;
                    float y = ring == 1 || ring == 2 ? .3f : -.3f;
                    vertices.Add(new Vector3(Mathf.Cos(angle) * r, y, Mathf.Sin(angle) * r));
                }
            for (int ring = 0; ring < 4; ring++)
                for (int k = 0; k < 8; k++)
                {
                    int a=ring*8+k, b=ring*8+(k+1)%8, c=((ring+1)%4)*8+k, d=((ring+1)%4)*8+(k+1)%8;
                    indices.AddRange(new[] { a,b,c, b,d,c });
                }
            lib.Bowl = Style(new Mesh { vertices = vertices.ToArray(), triangles = indices.ToArray() }, "Closed brazier shell");
            lib.Edge = AssetDatabase.LoadAssetAtPath<Shader>(EffectBaker.ShaderDir + "MeleeEdge.shader");
            AssetDatabase.CreateAsset(lib, path);
            AssetDatabase.AddObjectToAsset(lib.Link, lib); AssetDatabase.AddObjectToAsset(lib.Node, lib); AssetDatabase.AddObjectToAsset(lib.Bowl, lib);
            AssetDatabase.SaveAssets();
        }
        static Mesh Style(Mesh mesh, string name)
        {
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            mesh.SetUVs(1, new List<Vector4>(new Vector4[mesh.vertexCount]));
            mesh.SetUVs(2, new List<Vector4>(new Vector4[mesh.vertexCount]));
            var result = StyleMesh.Unweld(mesh, 24); result.name = name;
            Object.DestroyImmediate(mesh); return result;
        }
    }
}
