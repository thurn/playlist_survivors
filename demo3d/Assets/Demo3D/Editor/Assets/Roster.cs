using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;

namespace Demo3D.EditorTools.Assets
{
    /// <summary>
    /// The roster (Assets/Demo3D/Data/roster.json): each monster type, the boss candidates and the
    /// hero, with their vendor prefab and clips by GUID. A clip name of "" means the first clip in
    /// that file (Synty's animation packs hold one clip per FBX). A `--roster` set's file
    /// (Data/roster-NAME.json) replaces the types it lists, by slot id; the rest, the hero among
    /// them, stay the default's.
    /// </summary>
    public static class Roster
    {
        public const string DataPath = "Assets/Demo3D/Data/roster.json";

        /// <summary>A `--roster` set's own file: Data/roster-NAME.json (the default's is DataPath).</summary>
        public static string SetPath(string set) => $"Assets/Demo3D/Data/roster-{set}.json";

        public sealed class Ref
        {
            public string guid;
            public string path;
        }

        public sealed class ClipRef
        {
            public string guid;
            public string clip;
            /// <summary>For the clip annotator: the impact is the peak of these bones (comma-separated names), not of the fastest end bone.</summary>
            public string impactBone;
            /// <summary>An attack's impact in seconds, read off its contact sheet where the annotator's peak speed is a wind-up.</summary>
            public float? impact;
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
            public string kind;
            public string rig;
            public float radius;
            public float heightM;
            public float hoverM;
            /// <summary>The walk's stride, where neither feet nor root travel give one (a flyer's flap loop).</summary>
            public float? strideM;
            /// <summary>The type's signature hue in HSV degrees (ps-e8r.8); null for the hero.</summary>
            public float? hue;
            /// <summary>Turns the type's whole albedo round the luma axis first, in degrees (ps-e8r.8).</summary>
            public float hueRotate;
            public Ref prefab;
            public Dictionary<string, ClipRef> clips = new Dictionary<string, ClipRef>();
            public Dictionary<string, ClipRef> rootMotionClips = new Dictionary<string, ClipRef>();
            public List<WeaponRef> weapons = new List<WeaponRef>();
            /// <summary>Prefab children Spawn removes (a pack prefab carrying every weapon at once).</summary>
            public List<string> hide = new List<string>();

            public bool Humanoid => rig == "humanoid";
        }

        /// <summary>How the crowd shader dyes a type toward its hue (roster.json's <c>typeColors</c>).</summary>
        public sealed class TypeColorSettings
        {
            public float saturation;
            public float dye;
        }

        sealed class File
        {
            public int version;
            public TypeColorSettings typeColors;
            public List<Type> types;
        }

        static File Read(string path = DataPath)
        {
            var file = JsonConvert.DeserializeObject<File>(System.IO.File.ReadAllText(path));
            if (file == null || file.version != 1) throw new Exception($"Roster: unknown version in {path}");
            return file;
        }

        public static List<Type> Load() => Read().types;

        /// <summary>A `--roster` set's types: the default's, with the set file's in their slots.</summary>
        public static List<Type> Load(string set)
        {
            var types = Load();
            if (set == "default") return types;
            foreach (var t in Read(SetPath(set)).types)
            {
                int slot = types.FindIndex(d => d.id == t.id);
                if (slot < 0 || t.id == "hero")
                    throw new Exception($"Roster: {SetPath(set)} lists '{t.id}', not one of the default roster's monster slots");
                types[slot] = t;
            }
            return types;
        }

        /// <summary>The slot ids a `--roster` set's own file lists.</summary>
        public static IEnumerable<string> SetIds(string set) => Read(SetPath(set)).types.Select(t => t.id);

        public static TypeColorSettings TypeColors() =>
            Read().typeColors ?? throw new Exception("Roster: no typeColors");

        public static string PathOf(string guid)
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            if (string.IsNullOrEmpty(path))
                throw new Exception($"Roster: no asset with GUID {guid}; is the vendor folder synced (demo3d setup)?");
            return path;
        }

        /// <summary>A Synty attack's own `_Hit_` clip start, in seconds: the vendor's impact frame.</summary>
        public static float? VendorHitTime(ClipRef c)
        {
            var hit = AssetDatabase.LoadAllAssetsAtPath(PathOf(c.guid)).OfType<AnimationClip>()
                .FirstOrDefault(a => a.name.Contains("_Hit_"));
            if (hit == null) return null;
            var importer = AssetImporter.GetAtPath(PathOf(c.guid)) as ModelImporter;
            var info = importer?.clipAnimations.FirstOrDefault(x => x.name == hit.name);
            var whole = importer?.clipAnimations.OrderByDescending(x => x.lastFrame - x.firstFrame).FirstOrDefault();
            if (info == null || whole == null) return null;
            return (info.firstFrame - whole.firstFrame) / hit.frameRate;
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
            // Synty's packs split each attack into WindUp, Hit and FollowThrough clips beside the
            // whole one; the whole one is the longest.
            if (string.IsNullOrEmpty(c.clip)) return clips.OrderByDescending(a => a.length).First();
            return clips.FirstOrDefault(a => a.name == c.clip)
                ?? throw new Exception($"Roster: no clip '{c.clip}' in {path} (has {string.Join(", ", clips.Select(a => a.name))})");
        }

        /// <summary>Instantiates a type's prefab, with URP materials and its weapons attached.</summary>
        public static GameObject Spawn(Type t, Vector3 at, float yawDeg)
        {
            var prefab = LoadPrefab(t.prefab);
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            go.transform.SetPositionAndRotation(at, Quaternion.Euler(0, yawDeg, 0));
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
            UrpMaterials.Convert(go);
            if (t.heightM > 0)
            {
                // Scale the prefab to the type's height in the demo (measured in its bind pose).
                float native = MeasureHeight(go);
                if (native > 0.05f) go.transform.localScale *= t.heightM / native;
            }
            if (t.hoverM > 0) go.transform.position += Vector3.up * t.hoverM;
            return go;
        }

        public static float MeasureHeight(GameObject go)
        {
            var renderers = go.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            if (renderers.Length == 0) return 0;
            float top = float.MinValue, bottom = float.MaxValue;
            foreach (var r in renderers)
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
            return top - Mathf.Min(bottom, go.transform.position.y);
        }

        public static Transform FindBone(GameObject go, Animator animator, string name)
        {
            if (animator != null && animator.isHuman && Enum.TryParse(name, out HumanBodyBones hb))
                return animator.GetBoneTransform(hb);
            return go.GetComponentsInChildren<Transform>(true)
                .FirstOrDefault(x => x.name.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0);
        }
    }
}
