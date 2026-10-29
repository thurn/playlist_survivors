using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using PS.Choreography;
using UnityEditor;
using UnityEngine;

namespace Demo2D.EditorTools.Assets
{
    /// <summary>
    /// The hero's and the roster's Pixel Cattle Games packs as synced into Assets/Vendor: each
    /// pack's Spine-exported frames (`&lt;prefix&gt;_png/&lt;prefix&gt;-&lt;clip&gt;_&lt;n&gt;.png`) and its
    /// Spine clips (`&lt;prefix&gt;/&lt;clip&gt;.anim`, keyed at 30 fps). The packs and prefixes come
    /// from the catalog (Data/roster.json).
    /// </summary>
    public static class CharacterPacks
    {
        /// <summary>
        /// The frames' rate. Every pack exported its frames at 15 fps from the start of the clip
        /// through its end: a clip of length L has ⌈15·L⌉ + 1 frames (PX2 measured all 196 clips).
        /// </summary>
        public const int FrameRate = 15;

        /// <summary>How many frames the export gives a clip of this length.</summary>
        public static int ExportedFrames(float length) => Mathf.CeilToInt(length * FrameRate - 0.01f) + 1;

        public const string VendorDir = "Assets/Vendor";
        public const string DataDir = "Assets/Demo2D/Data";

        public sealed class Character
        {
            public string Name, Pack, Prefix, Rank;
            public int GameHeightPx;
            public string FramesDir => $"{VendorDir}/{Pack}/{Prefix}_png";
            public string ClipsDir => $"{VendorDir}/{Pack}/{Prefix}";

            /// <summary>Every clip's frame files, in frame order, by clip name ("walk").</summary>
            public SortedDictionary<string, List<string>> Frames()
            {
                var clips = new SortedDictionary<string, List<(int n, string path)>>(StringComparer.Ordinal);
                if (Directory.Exists(FramesDir))
                    foreach (var path in Directory.GetFiles(FramesDir, "*.png"))
                    {
                        var name = Path.GetFileNameWithoutExtension(path);
                        int cut = name.LastIndexOf('_');
                        if (!name.StartsWith(Prefix + "-", StringComparison.Ordinal) || cut < 0 ||
                            !int.TryParse(name.Substring(cut + 1), NumberStyles.None, CultureInfo.InvariantCulture, out int n))
                            continue;
                        var clip = name.Substring(Prefix.Length + 1, cut - Prefix.Length - 1);
                        if (!clips.TryGetValue(clip, out var list)) clips[clip] = list = new List<(int, string)>();
                        list.Add((n, path.Replace('\\', '/')));
                    }
                var result = new SortedDictionary<string, List<string>>(StringComparer.Ordinal);
                foreach (var kv in clips) result[kv.Key] = kv.Value.OrderBy(f => f.n).Select(f => f.path).ToList();
                return result;
            }

            /// <summary>The frame indices a clip's files carry, in order (0..n-1 when complete).</summary>
            public static List<int> Indices(IEnumerable<string> frames) =>
                frames.Select(p => Path.GetFileNameWithoutExtension(p))
                      .Select(n => int.Parse(n.Substring(n.LastIndexOf('_') + 1), CultureInfo.InvariantCulture)).ToList();

            public AnimationClip Clip(string clip) => AssetDatabase.LoadAssetAtPath<AnimationClip>($"{ClipsDir}/{clip}.anim");
        }

        /// <summary>The hero, then the 13 roster types in the catalog's order.</summary>
        public static List<Character> Load()
        {
            var roster = JsonValue.Parse(File.ReadAllText(Path.Combine(DataDir, "roster.json")));
            var list = new List<Character> { Read("knight", "hero", roster["hero"]) };
            foreach (var m in roster["monsters"].Items) list.Add(Read(m["name"].AsString(), m["rank"].AsString(), m));
            return list;
        }

        static Character Read(string name, string rank, JsonValue v) => new Character
        {
            Name = name,
            Rank = rank,
            Pack = v["pack"].AsString() ?? throw new FormatException(name + ": no pack"),
            Prefix = v["clipPrefix"].AsString() ?? throw new FormatException(name + ": no clipPrefix"),
            GameHeightPx = v["gameHeightPx"].AsInt(),
        };

        /// <summary>
        /// The clip library's clips: "&lt;prefix&gt;-&lt;clip&gt;" and frame count for each annotation,
        /// from PX3's measured annotations once they exist, else PX4's placeholders.
        /// </summary>
        public static List<(string clip, int frames)> LibraryClips()
        {
            var measured = Path.Combine(DataDir, "clip-annotations.json");
            var file = File.Exists(measured) ? measured : Path.Combine(DataDir, "placeholder-annotations.json");
            return JsonValue.Parse(File.ReadAllText(file))["annotations"].Items
                .Select(a => (a["clip"].AsString(), a["frames"].AsInt())).ToList();
        }

        /// <summary>Reads a PNG's pixels straight from its file (vendor imports aren't readable).</summary>
        public static Texture2D ReadPng(string path)
        {
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
            if (!tex.LoadImage(File.ReadAllBytes(path))) throw new IOException("not a PNG: " + path);
            return tex;
        }
    }
}
