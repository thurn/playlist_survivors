using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Champions.EditorTools.Monsters;
using Champions.Render;
using UnityEditor;
using UnityEngine;

namespace Champions.EditorTools.Effects
{
    public static class MeleeBaker
    {
        const string HammerPath = "Assets/Vendor/PolygonDungeon/Models/SM_Wep_Hammer_Large_Metal_01.fbx";
        const string SwordPath = "Assets/Vendor/PolygonKnights/Prefabs/Weapons/SM_Wep_Broadsword_01.prefab";
        public static void Bake()
        {
            var hammer = AssetDatabase.LoadAllAssetsAtPath(HammerPath).OfType<Mesh>().FirstOrDefault();
            var sword = AssetDatabase.LoadAssetAtPath<GameObject>(SwordPath)?.GetComponentInChildren<MeshFilter>()?.sharedMesh;
            if (hammer == null || sword == null) throw new InvalidOperationException("MeleeBaker: run champions setup for the Dungeon hammer and Knight broadsword");
            string path = EffectBaker.Dir + "/MeleeWeapons.asset";
            Directory.CreateDirectory(EffectBaker.Dir);
            AssetDatabase.DeleteAsset(path);
            var lib = ScriptableObject.CreateInstance<MeleeLibrary>();
            lib.Sword = Normalize(sword, false); lib.Hammer = Normalize(hammer, true);
            lib.Edge = AssetDatabase.LoadAssetAtPath<Shader>(EffectBaker.ShaderDir + "MeleeEdge.shader");
            AssetDatabase.CreateAsset(lib, path);
            AssetDatabase.AddObjectToAsset(lib.Sword, lib); AssetDatabase.AddObjectToAsset(lib.Hammer, lib);
            AssetDatabase.SaveAssets();
        }
        static Mesh Normalize(Mesh source, bool hammer)
        {
            var m = UnityEngine.Object.Instantiate(source);
            var v = m.vertices; var size = m.bounds.size;
            int axis = size.x >= size.y && size.x >= size.z ? 0 : size.y >= size.z ? 1 : 2;
            float lo = v.Min(p => p[axis]), hi = v.Max(p => p[axis]);
            float length = hi - lo;
            int across = (axis + 1) % 3, depth = (axis + 2) % 3;
            // The broader end is the hammer head; the sword tip is its narrower end.
            float lower = 0, upper = 0;
            foreach (var p in v)
            {
                float width = new Vector2(p[across], p[depth]).magnitude;
                if (p[axis] < lo + length * .25f) lower = Mathf.Max(lower, width);
                if (p[axis] > hi - length * .25f) upper = Mathf.Max(upper, width);
            }
            bool highTip = hammer ? upper > lower : upper < lower;
            var center = m.bounds.center;
            for (int i = 0; i < v.Length; i++)
            {
                var p = v[i];
                v[i] = new Vector3((p[across] - center[across]) / length,
                    (highTip ? p[axis] - lo : hi - p[axis]) / length, (p[depth] - center[depth]) / length);
            }
            m.vertices = v; m.RecalculateNormals(); m.RecalculateBounds();
            // StyleMesh also preserves skin channels; rigid weapons use empty channels.
            m.SetUVs(1, new List<Vector4>(new Vector4[v.Length]));
            m.SetUVs(2, new List<Vector4>(new Vector4[v.Length]));
            var style = StyleMesh.Unweld(m, 7);
            style.name = hammer ? "Spectral hammer" : "Spectral broadsword";
            UnityEngine.Object.DestroyImmediate(m);
            return style;
        }
    }
}
