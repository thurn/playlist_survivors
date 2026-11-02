using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Champions.Client.Sound;
using UnityEditor;
using UnityEngine;

namespace Champions.EditorTools.Sound
{
    /// <summary>
    /// Bakes the <see cref="SoundBank"/> (into the ignored Assets/Baked/Sounds/Resources/, since it references vendor
    /// clips) from Content/sounds.json (<see cref="SoundSelection"/>): each category's clips, `category/variant`, found
    /// by file name among the synced vendor files, each at its category's level, with its onset measured from its samples
    /// (game/'s SoundBaker, ported by review; the free samples are not carried). Without the vendor clips there is no
    /// bank, and the game plays no sounds of its own. <see cref="Tasks.BuildPlayer"/> runs it before every build.
    /// </summary>
    public static class SoundBaker
    {
        public const string Dir = "Assets/Baked/Sounds/Resources";
        /// <summary>A clip's attack: its first sample at this share of its peak.</summary>
        public const float OnsetShare = 0.1f;

        public static string AssetPath => $"{Dir}/{SoundBank.ResourcePath}.asset";

        /// <summary>Bakes the bank; false, with nothing written, when a clip is missing from the vendor files.</summary>
        public static bool Bake()
        {
            var selection = SoundSelection.Load(ContentDir());
            var vendor = AssetDatabase.FindAssets("t:AudioClip", new[] { "Assets/Vendor" })
                .Select(AssetDatabase.GUIDToAssetPath)
                .GroupBy(p => Path.GetFileNameWithoutExtension(p))
                .ToDictionary(g => g.Key, g => g.First());
            var missing = selection.ClipNames().Where(n => !vendor.ContainsKey(n)).ToList();
            if (missing.Count > 0)
            {
                Debug.LogWarning($"SoundBaker: no vendor clip {string.Join(", ", missing)} (run `champions setup`); no sound bank");
                return false;
            }
            var names = new List<string>();
            var clips = new List<AudioClip>();
            var onsets = new List<float>();
            var levels = new List<float>();
            foreach (SoundKind k in Enum.GetValues(typeof(SoundKind)))
            {
                var c = selection.Categories[(int)k];
                for (int v = 0; v < c.Clips.Length; v++)
                {
                    var path = vendor[c.Clips[v]];
                    var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path) ?? throw new Exception($"SoundBaker: {path} is not an imported audio clip");
                    names.Add($"{SoundScheduler.Name(k)}/{v}");
                    clips.Add(clip);
                    onsets.Add(Onset(clip));
                    levels.Add(c.Gain);
                }
            }
            Directory.CreateDirectory(Dir);
            var bank = ScriptableObject.CreateInstance<SoundBank>();
            bank.Names = names.ToArray();
            bank.Clips = clips.ToArray();
            bank.Onsets = onsets.ToArray();
            bank.Levels = levels.ToArray();
            bank.Limit = selection.Limit;
            AssetDatabase.DeleteAsset(AssetPath);
            AssetDatabase.CreateAsset(bank, AssetPath);
            AssetDatabase.SaveAssets();
            Debug.Log($"SoundBaker: {names.Count} clips in {selection.Categories.Length} categories, onsets up to {onsets.Max() * 1000:F0} ms -> {AssetPath}");
            return true;
        }

        static string ContentDir() =>
            Champions.Sim.GameContent.Find(Application.dataPath) ?? throw new Exception($"SoundBaker: no rhythm_champions/Content above {Application.dataPath}");

        /// <summary>
        /// The seconds of near silence before a clip's attack: its first sample at <see cref="OnsetShare"/> of its peak, on
        /// any channel. 0 when its samples can't be read (a clip that isn't decompressed on load).
        /// </summary>
        public static float Onset(AudioClip clip)
        {
            if (clip.loadType != AudioClipLoadType.DecompressOnLoad || clip.samples == 0) return 0;
            if (clip.loadState != AudioDataLoadState.Loaded) clip.LoadAudioData();
            var data = new float[clip.samples * clip.channels];
            if (!clip.GetData(data, 0)) return 0;
            float peak = 0;
            foreach (var x in data) peak = Mathf.Max(peak, Mathf.Abs(x));
            if (peak <= 0) return 0;
            for (int i = 0; i < data.Length; i++)
                if (Mathf.Abs(data[i]) >= OnsetShare * peak) return (float)(i / clip.channels) / clip.frequency;
            return 0;
        }
    }
}
