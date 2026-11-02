using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Champions.EditorTools.Monsters
{
    /// <summary>
    /// A monster type's vendor model (monsters.json beside this file): its prefab, height, hover, hidden children, held
    /// weapons and clips with their annotations, which <see cref="MonsterBaker"/> bakes and <see cref="MonsterReview"/>
    /// names; or a figure's (the hero's), posed on the CPU from the rig its bake keeps, maybe with the crystal blade in its
    /// fist. Vendor assets are found by GUID in the synced vendor folder (`champions setup`).
    /// </summary>
    [Serializable]
    public sealed class MonsterModel
    {
        public const string File = "Assets/Champions/Editor/Monsters/monsters.json";

        public string id;
        public string prefab;
        public float heightM;
        public float hoverM;
        public string[] hide = Array.Empty<string>();
        /// <summary>Renderers it carries (a sword, a shield): its shatter flings their chunks as it does the struck part's.</summary>
        public string[] carried = Array.Empty<string>();
        public Weapon[] weapons = Array.Empty<Weapon>();
        public Clip[] clips = Array.Empty<Clip>();
        public string[] reviewPoses = { "idle", "walk", "attack" };
        /// <summary>Posed on the CPU (the hero): its bake keeps a rig (<see cref="MonsterRigBaker"/>) and goes to the figures, not the crowd.</summary>
        public bool figure;
        /// <summary>The crystal blade in its right fist (<see cref="CrystalBlade"/>), as the trailer's knight held it.</summary>
        public bool blade;

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
        /// A weapon prefab held in a humanoid fist (<see cref="Fist"/>): its grip's palm offset (m at the prefab's scale) and
        /// turns (degrees); or, <see cref="onBone"/>, riding the hand bone as the prefab comes.
        /// </summary>
        [Serializable]
        public sealed class Weapon
        {
            public string prefab;
            public string hand = "right";
            public float palm, pitch, yaw, roll;
            /// <summary>On the hand bone at the prefab's own placement, not in the fist: the trailer's knight's shield.</summary>
            public bool onBone;
        }

        [Serializable]
        sealed class Table
        {
            public string note;
            public MonsterModel[] types = Array.Empty<MonsterModel>();
        }

        /// <summary>Every type in monsters.json, in its order.</summary>
        public static MonsterModel[] All() => JsonUtility.FromJson<Table>(System.IO.File.ReadAllText(File)).types;

        public static MonsterModel Of(string id) =>
            All().FirstOrDefault(m => m.id == id)
            ?? throw new Exception($"MonsterModel: no type '{id}' in {File} (has {string.Join(", ", All().Select(m => m.id))})");

        /// <summary>The entry as monsters.json holds it, for the bake's key.</summary>
        public string Json() => JsonUtility.ToJson(this);

        public static string PathOf(string guid)
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            if (string.IsNullOrEmpty(path) || !System.IO.File.Exists(path))
                throw new Exception($"MonsterModel: no asset with GUID {guid}; is the vendor folder synced (champions setup)?");
            return path;
        }

        public AnimationClip LoadClip(Clip c)
        {
            var path = PathOf(c.guid);
            var all = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>()
                .Where(a => !a.name.StartsWith("__preview__", StringComparison.Ordinal)).ToList();
            return all.FirstOrDefault(a => a.name == c.clip)
                ?? throw new Exception($"MonsterModel: no clip '{c.clip}' in {path} (has {string.Join(", ", all.Select(a => a.name))})");
        }

        /// <summary>
        /// The prefab at the origin, unpacked, its hidden children gone, its weapons (and blade) in their fists, its
        /// animator free of root motion and controller, scaled to <see cref="heightM"/> (measured on its skins' bind-pose
        /// vertices, not the meshes' bounds, which some packs leave taller than the model).
        /// </summary>
        public GameObject Spawn()
        {
            var go = Instantiate(prefab);
            foreach (var name in hide)
            {
                var child = go.GetComponentsInChildren<Transform>(true).FirstOrDefault(x => x.name == name)
                    ?? throw new Exception($"MonsterModel: {id}'s prefab has no child '{name}' to hide");
                UnityEngine.Object.DestroyImmediate(child.gameObject);
            }
            var animator = go.GetComponentInChildren<Animator>() ?? throw new Exception($"MonsterModel: {id} has no Animator");
            foreach (var w in weapons) Hold(w, animator);
            if (blade) CrystalBlade.Hold(animator);
            foreach (var t in go.GetComponentsInChildren<Transform>(true))
                GameObjectUtility.RemoveMonoBehavioursWithMissingScript(t.gameObject); // vendor scripts we don't sync
            animator.applyRootMotion = false;
            animator.runtimeAnimatorController = null;
            float native = MeasureHeight(go);
            if (native > 0.05f) go.transform.localScale *= heightM / native;
            return go;
        }

        static GameObject Instantiate(string guid)
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(PathOf(guid))
                ?? throw new Exception($"MonsterModel: {PathOf(guid)} is not a prefab");
            var go = (GameObject)PrefabUtility.InstantiatePrefab(asset);
            PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            return go;
        }

        /// <summary>Puts a weapon in its hand's fist by its grip (or on the hand bone as it comes), at the character's own scale.</summary>
        void Hold(Weapon w, Animator animator)
        {
            if (w.hand != "right" && w.hand != "left")
                throw new Exception($"MonsterModel: {id}'s weapon {w.prefab} is for hand '{w.hand}', not right or left");
            var fist = Fist.Of(animator, w.hand == "right");
            var weapon = Instantiate(w.prefab).transform;
            weapon.SetParent(fist.Hand, false);
            float scale = fist.Hand.lossyScale.x; // at the prefab's own scale (Spawn scales the whole after)
            if (!w.onBone)
            {
                weapon.localRotation = fist.Turn * Quaternion.Euler(w.pitch, w.yaw, w.roll);
                weapon.localPosition = fist.Knuckles + fist.Palm * (w.palm / scale);
            }
            // At the character's scale, not the hand bone's: a rig whose bones carry a scale would hold a weapon that small.
            weapon.localScale = Vector3.one * (fist.Hand.root.lossyScale.x / scale);
        }

        /// <summary>From the ground (or the lowest skin vertex, if lower) to the highest, over its skins at bind pose.</summary>
        static float MeasureHeight(GameObject go)
        {
            float top = float.MinValue, bottom = float.MaxValue;
            foreach (var r in go.GetComponentsInChildren<SkinnedMeshRenderer>(false))
            {
                if (r.sharedMesh == null) continue;
                foreach (var v in r.sharedMesh.vertices)
                {
                    float y = r.transform.TransformPoint(v).y;
                    top = Mathf.Max(top, y);
                    bottom = Mathf.Min(bottom, y);
                }
            }
            return top == float.MinValue ? 0 : top - Mathf.Min(bottom, go.transform.position.y);
        }
    }
}
