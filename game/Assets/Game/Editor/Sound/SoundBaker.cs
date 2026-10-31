using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Game.Client.Sound;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace Game.EditorTools.Sound
{
    /// <summary>
    /// Bakes the <see cref="SoundBank"/> (into Assets/Baked/Sounds/Resources/, since it references
    /// vendor clips) from Content/sounds.json: each category's variants, `category/variant`, from
    /// demo3d's vendor packs (Assets/Vendor, by file name) or the free samples (Assets/FreeSounds,
    /// by id, with their measured onsets), each at its category's level and root note.
    /// </summary>
    public static class SoundBaker
    {
        public const string SelectionPath = "Content/sounds.json";
        const string Dir = "Assets/Baked/Sounds/Resources";
        const string FreeDir = "Assets/FreeSounds";

        public static void Bake()
        {
            var selection = JObject.Parse(File.ReadAllText(SelectionPath));
            var vendor = AssetDatabase.FindAssets("t:AudioClip", new[] { "Assets/Vendor" })
                .Select(AssetDatabase.GUIDToAssetPath)
                .GroupBy(p => Path.GetFileNameWithoutExtension(p))
                .ToDictionary(g => g.Key, g => g.First());
            var free = File.Exists(FreeDir + "/sounds.json")
                ? (JObject)JObject.Parse(File.ReadAllText(FreeDir + "/sounds.json"))["sounds"]
                : new JObject();
            var names = new List<string>();
            var clips = new List<AudioClip>();
            var onsets = new List<float>();
            var levels = new List<float>();
            var roots = new List<float>();
            var known = Enum.GetValues(typeof(SoundKind)).Cast<SoundKind>().Select(SoundScheduler.Name).ToHashSet();
            foreach (var category in (JObject)selection["categories"])
            {
                if (!known.Contains(category.Key)) throw new Exception($"SoundBaker: {SelectionPath}: no sound category '{category.Key}'");
                var c = (JObject)category.Value;
                float gain = (float?)c["gain"] ?? 1f, root = (float?)c["rootNote"] ?? 0f;
                int variant = 0;
                foreach (var clipName in c["clips"].Select(x => (string)x))
                {
                    string path;
                    float onset = 0;
                    if (clipName.Contains("/"))
                    {
                        if (!(free[clipName] is JObject entry))
                            throw new Exception($"SoundBaker: no free sample {clipName} for {category.Key} (run setup)");
                        path = FreeDir + "/" + (string)entry["file"];
                        onset = (float?)entry["onsetS"] ?? 0f;
                    }
                    else if (!vendor.TryGetValue(clipName, out path))
                        throw new Exception($"SoundBaker: no vendor clip {clipName} for {category.Key} (is it in the manifest?)");
                    var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path)
                               ?? throw new Exception($"SoundBaker: {path} is not an imported audio clip");
                    names.Add($"{category.Key}/{variant++}");
                    clips.Add(clip);
                    onsets.Add(onset);
                    levels.Add(gain);
                    roots.Add(root);
                }
            }
            Directory.CreateDirectory(Dir);
            var bank = ScriptableObject.CreateInstance<SoundBank>();
            bank.Names = names.ToArray();
            bank.Clips = clips.ToArray();
            bank.Onsets = onsets.ToArray();
            bank.Levels = levels.ToArray();
            bank.RootNotes = roots.ToArray();
            bank.Limit = (float?)selection["limit"] ?? 1f;
            var asset = $"{Dir}/{SoundBank.ResourcePath}.asset";
            AssetDatabase.DeleteAsset(asset);
            AssetDatabase.CreateAsset(bank, asset);
            AssetDatabase.SaveAssets();
            Debug.Log($"SoundBaker: {names.Count} clips in {((JObject)selection["categories"]).Count} categories -> {asset}");
        }
    }
}
