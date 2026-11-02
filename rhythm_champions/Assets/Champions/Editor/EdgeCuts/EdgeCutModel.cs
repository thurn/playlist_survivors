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
    /// GUID (manifest.json), the model's height at scale 1, the prefab children it never shows, the weapons put in
    /// its hands, the clip role whose first frame the recipe also sees it in (`pose`, for a T-posed rig) and the clip
    /// roles its review draws. A model without clips is <see cref="Static"/>: a structure, drawn by the structure field
    /// rather than the crowd.
    /// </summary>
    [Serializable]
    public sealed class EdgeCutModel
    {
        public string id;
        public string prefab;
        public float heightM;
        public float hoverM;
        public string[] hide = Array.Empty<string>();
        public Weapon[] weapons = Array.Empty<Weapon>();
        public string pose = "";
        public Clip[] clips = Array.Empty<Clip>();
        public string[] reviewPoses = { "idle", "walk", "attack" };

        /// <summary>
        /// A clip the crowd plays: its role (idle, walk, attack, hit, ...), source and annotations (negative where
        /// absent), and the metres the whole clip is raised (a flyer's clips authored on the ground, played in the air).
        /// </summary>
        [Serializable]
        public sealed class Clip
        {
            public string role;
            public string guid;
            public string clip;
            public bool loop;
            public float impact = -1, end = -1, strideM = -1;
            public float liftM;
        }

        /// <summary>
        /// A weapon prefab held in a humanoid's right or left <see cref="Fist"/>, then moved by its grip: `palm` metres
        /// (at the prefab's own scale) from the knuckles toward the palm, turned by `pitch`, `yaw` and `roll` degrees
        /// about its own axes, as Quaternion.Euler takes them.
        /// </summary>
        [Serializable]
        public sealed class Weapon
        {
            public string prefab;
            public string hand = "right";
            public float palm, pitch, yaw, roll;
        }

        public const string BakedRoot = "Assets/Baked/EdgeCuts";

        public static string Dir(string id) => $"{BakedRoot}/{id}";

        /// <summary>A structure: no clips and no rig, its renderers rigid (design.md, The World &gt; Structures).</summary>
        public bool Static => clips.Length == 0;

        /// <summary>The prefab's own height, measured by the latest <see cref="Spawn"/>.</summary>
        [NonSerialized] public float NativeHeightM;

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
        /// controller, its weapons in its fists, scaled to <see cref="heightM"/> (<see cref="MeasureHeight"/>).
        /// </summary>
        public GameObject Spawn()
        {
            var go = Instantiate(prefab);
            foreach (var name in hide)
            {
                var child = go.GetComponentsInChildren<Transform>(true).FirstOrDefault(x => x.name == name)
                    ?? throw new Exception($"EdgeCutModel: {id}'s prefab has no child '{name}' to hide");
                UnityEngine.Object.DestroyImmediate(child.gameObject);
            }
            var animator = go.GetComponentInChildren<Animator>();
            if (animator == null && !Static) throw new Exception($"EdgeCutModel: {id} has no Animator");
            foreach (var w in weapons) Hold(w, animator);
            foreach (var t in go.GetComponentsInChildren<Transform>(true))
                GameObjectUtility.RemoveMonoBehavioursWithMissingScript(t.gameObject);
            if (animator != null)
            {
                animator.applyRootMotion = false;
                animator.runtimeAnimatorController = null;
            }
            float native = MeasureHeight(go, Static);
            NativeHeightM = native;
            if (native > 0.05f) go.transform.localScale *= heightM / native;
            return go;
        }

        static GameObject Instantiate(string guid)
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(PathOf(guid))
                ?? throw new Exception($"EdgeCutModel: {PathOf(guid)} is not a prefab");
            var go = (GameObject)PrefabUtility.InstantiatePrefab(asset);
            PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            return go;
        }

        /// <summary>Puts a weapon in its fist, a child of the hand bone (game/'s Roster.Hold, ported by review).</summary>
        void Hold(Weapon w, Animator animator)
        {
            if (w.hand != "right" && w.hand != "left")
                throw new Exception($"EdgeCutModel: {id}'s weapon {w.prefab} is for hand '{w.hand}', not right or left");
            var fist = Fist.Of(animator, w.hand == "right");
            var weapon = Instantiate(w.prefab).transform;
            weapon.SetParent(fist.Hand, false);
            float scale = fist.Hand.lossyScale.x; // at the prefab's own scale (Spawn scales the whole after)
            weapon.localRotation = fist.Turn * Quaternion.Euler(w.pitch, w.yaw, w.roll);
            weapon.localPosition = fist.Knuckles + fist.Palm * (w.palm / scale);
            // At the character's own scale, not the hand bone's: a rig whose bones carry a scale (Polygon Knights' hands
            // are about 1/100) would hold a weapon that small.
            weapon.localScale = Vector3.one * (fist.Hand.root.lossyScale.x / scale);
        }

        /// <summary>
        /// The skins' height in their bind pose, from their vertices as the export places them (Magic Pig's meshes
        /// carry bounds up to a third taller than their vertices); a static model's over every renderer, from the ground,
        /// its origin, up (Synty's buildings reach below the ground on their foundations).
        /// </summary>
        static float MeasureHeight(GameObject go, bool isStatic)
        {
            float top = float.MinValue, bottom = float.MaxValue;
            foreach (var r in go.GetComponentsInChildren<Renderer>(false))
            {
                var mesh = r is SkinnedMeshRenderer smr ? smr.sharedMesh
                    : isStatic && r is MeshRenderer ? r.GetComponent<MeshFilter>()?.sharedMesh : null;
                if (mesh == null) continue;
                foreach (var v in mesh.vertices)
                {
                    float y = r.transform.TransformPoint(v).y;
                    top = Mathf.Max(top, y);
                    bottom = Mathf.Min(bottom, y);
                }
            }
            float ground = go.transform.position.y;
            return top == float.MinValue ? 0 : top - (isStatic ? ground : Mathf.Min(bottom, ground));
        }
    }
}
