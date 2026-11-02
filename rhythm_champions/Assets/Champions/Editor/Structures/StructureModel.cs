using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Champions.EditorTools.Structures
{
    /// <summary>
    /// A structure kind's Synty model (structures.json beside this file): its prefab, height, hidden children, pieces and
    /// the default look's change to it, which <see cref="StructureBaker"/> bakes. Vendor assets are found by GUID in
    /// the synced vendor folder (`champions setup`).
    /// </summary>
    [Serializable]
    public sealed class StructureModel
    {
        public const string File = "Assets/Champions/Editor/Structures/structures.json";

        public string id;
        public string prefab;
        public float heightM;
        public string[] hide = Array.Empty<string>();
        public int pieces = 24;
        /// <summary>Where the default look departs from the model as `--look trailer` draws it; nowhere when <see cref="DefaultLook.why"/> is empty.</summary>
        public DefaultLook defaultLook = new DefaultLook();

        /// <summary>
        /// The default look's change to the model, judged by eye at game scale (design.md, Changing the look): its own
        /// crease angle, over the edge look's 24 degrees where its lines turn to noise, so only its larger turns draw
        /// ("detail that turns to noise at game scale can go"), or under it, down to 1 (every edge but a flat quad's
        /// diagonal: a true wireframe, design.md, The aim), where a few big facets are the drawing. <see cref="why"/> says
        /// what it fixes.
        /// </summary>
        [Serializable]
        public sealed class DefaultLook
        {
            public string why = "";
            public float edgeAngleDeg;

            public bool Any => !string.IsNullOrEmpty(why);
        }

        [Serializable]
        sealed class Table
        {
            public string note;
            public StructureModel[] kinds = Array.Empty<StructureModel>();
        }

        /// <summary>Every kind in structures.json, in its order.</summary>
        public static StructureModel[] All() => JsonUtility.FromJson<Table>(System.IO.File.ReadAllText(File)).kinds;

        public static StructureModel Of(string id) =>
            All().FirstOrDefault(m => m.id == id)
            ?? throw new Exception($"StructureModel: no kind '{id}' in {File} (has {string.Join(", ", All().Select(m => m.id))})");

        /// <summary>The entry as structures.json holds it, for the bake's key.</summary>
        public string Json() => JsonUtility.ToJson(this);

        /// <summary>Whether the prefab is in the vendor folder.</summary>
        public bool VendorPresent()
        {
            var path = AssetDatabase.GUIDToAssetPath(prefab);
            return !string.IsNullOrEmpty(path) && System.IO.File.Exists(path);
        }

        /// <summary>The prefab at the origin, unpacked, its hidden children gone.</summary>
        public GameObject Spawn()
        {
            var path = AssetDatabase.GUIDToAssetPath(prefab);
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path)
                ?? throw new Exception($"StructureModel: {id}: no prefab with GUID {prefab}; is the vendor folder synced (champions setup)?");
            var go = (GameObject)PrefabUtility.InstantiatePrefab(asset);
            PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            foreach (var name in hide)
            {
                var child = go.GetComponentsInChildren<Transform>(true).FirstOrDefault(x => x.name == name)
                    ?? throw new Exception($"StructureModel: {id}'s prefab has no child '{name}' to hide");
                UnityEngine.Object.DestroyImmediate(child.gameObject);
            }
            return go;
        }

        /// <summary>
        /// The model as one welded mesh in the structure's own space: every active renderer's mesh as the prefab places it
        /// (each prefab lies along x as its kind's footprint does), scaled uniformly so its top stands <see cref="heightM"/> over the ground (the prefab's y = 0), its footprint's middle (the box round what
        /// stands above the ground) moved to the origin; triangles wholly under the ground (Synty's foundations) left out.
        /// </summary>
        public Mesh Combine(GameObject root)
        {
            var positions = new List<Vector3>();
            var tris = new List<int>();
            foreach (var r in root.GetComponentsInChildren<MeshRenderer>(false))
            {
                var mesh = r.enabled ? r.GetComponent<MeshFilter>()?.sharedMesh : null;
                if (mesh == null) continue;
                var toRoot = root.transform.worldToLocalMatrix * r.transform.localToWorldMatrix;
                int first = positions.Count;
                foreach (var v in mesh.vertices) positions.Add(toRoot.MultiplyPoint3x4(v));
                for (int s = 0; s < mesh.subMeshCount; s++)
                    foreach (int i in mesh.GetTriangles(s)) tris.Add(first + i);
            }
            if (tris.Count == 0) throw new Exception($"StructureModel: {id} has no active mesh renderers");

            float top = positions.Max(p => p.y);
            if (top <= 0.01f) throw new Exception($"StructureModel: {id} stands no higher than the ground");
            var above = positions.Where(p => p.y >= 0).ToList();
            var middle = new Vector3(0.5f * (above.Min(p => p.x) + above.Max(p => p.x)), 0, 0.5f * (above.Min(p => p.z) + above.Max(p => p.z)));
            float scale = heightM / top;
            for (int i = 0; i < positions.Count; i++) positions[i] = (positions[i] - middle) * scale;

            var kept = new List<int>(tris.Count);
            for (int t = 0; t < tris.Count; t += 3)
            {
                if (positions[tris[t]].y < 0 && positions[tris[t + 1]].y < 0 && positions[tris[t + 2]].y < 0) continue;
                kept.Add(tris[t]);
                kept.Add(tris[t + 1]);
                kept.Add(tris[t + 2]);
            }
            var m = new Mesh { name = id + "_welded", indexFormat = positions.Count > 65535 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16 };
            m.SetVertices(positions);
            m.SetTriangles(kept, 0);
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }
    }
}
