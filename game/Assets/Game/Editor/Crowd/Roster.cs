using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;

namespace Game.EditorTools.Crowd
{
    /// <summary>
    /// The crowd's roster (Editor/Crowd/roster.json): the nine monster models with their vendor
    /// prefab, weapons and clips by GUID, and each clip's annotations. demo3d's Roster, copied and
    /// owned for the crowd's types alone. A clip name of "" means the longest clip in that file
    /// (Synty's animation packs hold one whole clip per FBX beside its parts).
    /// </summary>
    public static class Roster
    {
        public const string DataPath = "Assets/Game/Editor/Crowd/roster.json";

        public sealed class Ref
        {
            public string guid;
            public string path;
        }

        public sealed class ClipRef
        {
            public string guid;
            public string clip;
            public bool loop;
            /// <summary>Seconds into the clip: an attack's contact, a hit's onset; and where it settles.</summary>
            public float? impact, end;
            /// <summary>A walk cycle's travel, metres at roster height.</summary>
            public float? strideM;
        }

        public sealed class WeaponRef
        {
            public string guid;
            public string path;
            public string bone;
        }

        public sealed class Type
        {
            public string id;
            public string rig;
            public float heightM;
            public float hoverM;
            public Ref prefab;
            /// <summary>idle, walk, attack and hit, in that order in the bone texture.</summary>
            public Dictionary<string, ClipRef> clips = new Dictionary<string, ClipRef>();
            public List<WeaponRef> weapons = new List<WeaponRef>();
            /// <summary>Prefab children Spawn removes.</summary>
            public List<string> hide = new List<string>();
        }

        sealed class File
        {
            public int version;
            public string note;
            public List<Type> types;
        }

        public static List<Type> Load()
        {
            var file = JsonConvert.DeserializeObject<File>(System.IO.File.ReadAllText(DataPath));
            if (file == null || file.version != 1) throw new Exception($"Roster: unknown version in {DataPath}");
            return file.types;
        }

        public static string PathOf(string guid)
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            if (string.IsNullOrEmpty(path))
                throw new Exception($"Roster: no asset with GUID {guid}; is the vendor folder synced (game setup)?");
            return path;
        }

        public static GameObject LoadPrefab(Ref r) =>
            AssetDatabase.LoadAssetAtPath<GameObject>(PathOf(r.guid))
            ?? throw new Exception($"Roster: {r.path} is not a prefab");

        public static AnimationClip LoadClip(ClipRef c)
        {
            var path = PathOf(c.guid);
            var clips = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>()
                .Where(a => !a.name.StartsWith("__preview__", StringComparison.Ordinal)).ToList();
            if (clips.Count == 0) throw new Exception($"Roster: no clips in {path}");
            if (string.IsNullOrEmpty(c.clip)) return clips.OrderByDescending(a => a.length).First();
            return clips.FirstOrDefault(a => a.name == c.clip)
                ?? throw new Exception($"Roster: no clip '{c.clip}' in {path} (has {string.Join(", ", clips.Select(a => a.name))})");
        }

        /// <summary>Instantiates a type's prefab at the origin, scaled to its roster height, with its weapons attached.</summary>
        public static GameObject Spawn(Type t)
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(LoadPrefab(t.prefab));
            PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            foreach (var name in t.hide)
            {
                var child = go.GetComponentsInChildren<Transform>(true).FirstOrDefault(x => x.name == name)
                    ?? throw new Exception($"Roster: {t.prefab.path} has no child '{name}' to hide");
                UnityEngine.Object.DestroyImmediate(child.gameObject);
            }
            var animator = go.GetComponentInChildren<Animator>();
            if (animator != null)
            {
                animator.applyRootMotion = false;
                animator.runtimeAnimatorController = null;
            }
            foreach (var w in t.weapons)
            {
                var bone = FindBone(go, animator, w.bone);
                if (bone == null) continue;
                var weapon = (GameObject)PrefabUtility.InstantiatePrefab(LoadPrefab(new Ref { guid = w.guid, path = w.path }));
                weapon.transform.SetParent(bone, false);
            }
            // Scale the prefab to the type's height (measured in its bind pose).
            float native = MeasureHeight(go);
            if (native > 0.05f) go.transform.localScale *= t.heightM / native;
            return go;
        }

        static float MeasureHeight(GameObject go)
        {
            float top = float.MinValue, bottom = float.MaxValue;
            foreach (var r in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (r.sharedMesh == null) continue;
                var b = r.sharedMesh.bounds;
                // The mesh's bounds in world space, through its root bone, approximate the bind pose.
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

        static Transform FindBone(GameObject go, Animator animator, string name)
        {
            if (animator != null && animator.isHuman && Enum.TryParse(name, out HumanBodyBones hb))
                return animator.GetBoneTransform(hb);
            return go.GetComponentsInChildren<Transform>(true)
                .FirstOrDefault(x => x.name.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0);
        }
    }
}
