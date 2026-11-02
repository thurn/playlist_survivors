using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Champions.EditorTools.EdgeCuts
{
    /// <summary>
    /// A model's vendor sources as its recipe names them (rhythm_champions/tools/recipes/edgecut/&lt;model&gt;.py, `MODEL`),
    /// which `champions edgecut` writes to Assets/Baked/EdgeCuts/&lt;model&gt;/model.json: the prefab and clips by vendor
    /// GUID (manifest.json), the model's height at scale 1 and the prefab children it never shows.
    /// </summary>
    [Serializable]
    public sealed class EdgeCutModel
    {
        public string id;
        public string prefab;
        public float heightM;
        public float hoverM;
        public string[] hide = Array.Empty<string>();
        public Clip[] clips = Array.Empty<Clip>();

        /// <summary>A clip the crowd plays: its role (idle, walk, attack, hit, ...), source and annotations (negative where absent).</summary>
        [Serializable]
        public sealed class Clip
        {
            public string role;
            public string guid;
            public string clip;
            public bool loop;
            public float impact = -1, end = -1, strideM = -1;
        }

        public const string BakedRoot = "Assets/Baked/EdgeCuts";

        public static string Dir(string id) => $"{BakedRoot}/{id}";

        public static EdgeCutModel Load(string id)
        {
            var path = $"{Dir(id)}/model.json";
            if (!File.Exists(path)) throw new Exception($"EdgeCutModel: no {path}; run `champions edgecut {id}`");
            var m = JsonUtility.FromJson<EdgeCutModel>(File.ReadAllText(path));
            if (m.id != id) throw new Exception($"EdgeCutModel: {path} is for '{m.id}'");
            return m;
        }

        public static string PathOf(string guid)
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                throw new Exception($"EdgeCutModel: no asset with GUID {guid}; is the vendor folder synced (champions setup)?");
            return path;
        }

        public AnimationClip LoadClip(Clip c)
        {
            var path = PathOf(c.guid);
            var all = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>()
                .Where(a => !a.name.StartsWith("__preview__", StringComparison.Ordinal)).ToList();
            return all.FirstOrDefault(a => a.name == c.clip)
                ?? throw new Exception($"EdgeCutModel: no clip '{c.clip}' in {path} (has {string.Join(", ", all.Select(a => a.name))})");
        }

        /// <summary>
        /// The prefab at the origin, unpacked, without the vendor's scripts, its hidden children or its animator's
        /// controller, scaled to <see cref="heightM"/> (measured on its skins' bind-pose bounds).
        /// </summary>
        public GameObject Spawn()
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(PathOf(prefab))
                ?? throw new Exception($"EdgeCutModel: {PathOf(prefab)} is not a prefab");
            var go = (GameObject)PrefabUtility.InstantiatePrefab(asset);
            PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            foreach (var name in hide)
            {
                var child = go.GetComponentsInChildren<Transform>(true).FirstOrDefault(x => x.name == name)
                    ?? throw new Exception($"EdgeCutModel: {id}'s prefab has no child '{name}' to hide");
                UnityEngine.Object.DestroyImmediate(child.gameObject);
            }
            foreach (var t in go.GetComponentsInChildren<Transform>(true))
                GameObjectUtility.RemoveMonoBehavioursWithMissingScript(t.gameObject);
            var animator = go.GetComponentInChildren<Animator>() ?? throw new Exception($"EdgeCutModel: {id} has no Animator");
            animator.applyRootMotion = false;
            animator.runtimeAnimatorController = null;
            float native = MeasureHeight(go);
            if (native > 0.05f) go.transform.localScale *= heightM / native;
            return go;
        }

        static float MeasureHeight(GameObject go)
        {
            float top = float.MinValue, bottom = float.MaxValue;
            foreach (var r in go.GetComponentsInChildren<SkinnedMeshRenderer>(false))
            {
                if (r.sharedMesh == null) continue;
                var b = r.sharedMesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var c = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) * 2 - 1, (i >> 1 & 1) * 2 - 1, (i >> 2 & 1) * 2 - 1));
                    var w = r.transform.TransformPoint(c);
                    top = Mathf.Max(top, w.y);
                    bottom = Mathf.Min(bottom, w.y);
                }
            }
            return top == float.MinValue ? 0 : top - Mathf.Min(bottom, go.transform.position.y);
        }
    }
}
