using System;
using System.IO;
using System.Linq;
using Champions.Render;
using UnityEditor;
using UnityEngine;
namespace Champions.EditorTools.Effects
{
    public static class RangedBaker
    {
        public static void Bake()
        {
            const string dir = "Assets/Baked/Ranged/Resources";
            Directory.CreateDirectory(dir);
            AssetDatabase.Refresh();
            var lib = ScriptableObject.CreateInstance<RangedLibrary>();
            lib.Shader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/Champions/Render/Effects/Shaders/RangedFacet.shader");
            var paths = new[] { "Assets/Vendor/PolygonDungeon/Models/SM_Wep_Spear_01.fbx",
                "Assets/Vendor/PolygonBowCrossbow/Models/Rigged_Bow_Testing.fbx",
                "Assets/Vendor/PolygonBowCrossbow/Models/SM_Arrow_01.fbx" };
            var meshes = new Mesh[3];
            for (int i = 0; i < paths.Length; i++)
            {
                var sources = AssetDatabase.LoadAllAssetsAtPath(paths[i]).OfType<Mesh>().ToArray();
                if (sources.Length == 0) { Debug.LogWarning("RangedBaker: source missing: " + paths[i]); return; }
                // Rigged_Bow_Testing contains a bow and an arrow: select the bow, not the arrow submesh.
                var source = i == 1 ? sources.FirstOrDefault(m => m.name.IndexOf("bow", StringComparison.OrdinalIgnoreCase) >= 0) ?? sources[0] : sources[0];
                meshes[i] = Facets(source, i == 1);
                string path = dir + "/" + new[] { "Spear", "Bow", "Arrow" }[i] + ".asset";
                AssetDatabase.DeleteAsset(path); AssetDatabase.CreateAsset(meshes[i], path);
                Debug.Log($"RangedBaker: {source.name} -> {path} ({meshes[i].vertexCount / 3} facets)");
            }
            lib.Spear = meshes[0]; lib.Bow = meshes[1]; lib.Arrow = meshes[2];
            string asset = dir + "/RangedWeapons.asset";
            AssetDatabase.DeleteAsset(asset); AssetDatabase.CreateAsset(lib, asset); AssetDatabase.SaveAssets();
        }
        static Mesh Facets(Mesh source, bool bow)
        {
            var vertices = source.vertices; var triangles = source.triangles;
            var output = new Vector3[triangles.Length]; var bary = new Vector3[output.Length];
            var indices = new int[output.Length];
            var size = source.bounds.size; var centre = source.bounds.center;
            int longest = size.x >= size.y && size.x >= size.z ? 0 : size.y >= size.z ? 1 : 2;
            int thin = size.x <= size.y && size.x <= size.z ? 0 : size.y <= size.z ? 1 : 2;
            int third = 3 - longest - thin;
            if (longest == thin) throw new Exception("RangedBaker: degenerate source bounds");
            for (int i = 0; i < output.Length; i++)
            {
                var p = (vertices[triangles[i]] - centre) / size[longest];
                // Spear/arrow length along +z, bow limb height along +y, shallow axis forward.
                output[i] = bow ? new Vector3(p[third], p[longest], p[thin])
                    : new Vector3(p[third], p[thin], p[longest] - 0.5f);
                bary[i] = i % 3 == 0 ? Vector3.right : i % 3 == 1 ? Vector3.up : Vector3.forward;
                indices[i] = i;
            }
            var mesh = new Mesh { name = source.name + " closed facets", vertices = output, triangles = indices };
            mesh.SetUVs(1, bary); mesh.RecalculateNormals(); mesh.RecalculateBounds(); return mesh;
        }
    }
}
