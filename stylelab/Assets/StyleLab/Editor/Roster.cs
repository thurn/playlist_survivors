using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;

namespace StyleLab.EditorTools
{
    /// <summary>
    /// The roster (Data/roster.json, a copy of demo3d's minus its rejected boss): each type's
    /// vendor prefab, weapons and clips by GUID. A clip name of "" means the file's longest clip
    /// (Synty's packs split attacks into parts beside the whole).
    /// </summary>
    public static class Roster
    {
        public const string DataPath = "Assets/StyleLab/Data/roster.json";

        public sealed class Ref { public string guid; public string path; }
        public sealed class ClipRef { public string guid; public string clip; }
        public sealed class WeaponRef { public string guid; public string path; public string bone; }

        public sealed class Type
        {
            public string id;
            public string kind;
            public string rig;
            public float radius;
            public float heightM;
            public float hoverM;
            public Ref prefab;
            public Dictionary<string, ClipRef> clips = new Dictionary<string, ClipRef>();
            public List<WeaponRef> weapons = new List<WeaponRef>();
        }

        sealed class File
        {
            public int version;
            public List<Type> types;
        }

        public static List<Type> Load()
        {
            var file = JsonConvert.DeserializeObject<File>(System.IO.File.ReadAllText(DataPath));
            if (file == null || file.version != 1) throw new Exception("Roster: unknown version");
            return file.types;
        }

        public static string PathOf(string guid)
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            if (string.IsNullOrEmpty(path))
                throw new Exception($"Roster: no asset with GUID {guid}; is the vendor folder synced (stylelab setup)?");
            return path;
        }

        public static GameObject LoadPrefab(string guid) =>
            AssetDatabase.LoadAssetAtPath<GameObject>(PathOf(guid))
            ?? throw new Exception($"Roster: {PathOf(guid)} is not a prefab");

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
    }
}
