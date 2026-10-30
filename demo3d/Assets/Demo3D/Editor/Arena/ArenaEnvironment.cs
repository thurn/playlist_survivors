using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Demo3D.Runtime.Arena;
using PS.Choreography;

namespace Demo3D.EditorTools.Arena
{
    /// <summary>
    /// One environment as the arena's bakers see it (ps-bop; environment.md, Environments): its
    /// whole layout (the forest's arena-layout.json, or environment-NAME.json resolved over the
    /// layout it extends) and where its derived files go: the forest's in Assets/Baked/Arena/ as
    /// before environments, another's in Assets/Baked/Arena/NAME/.
    /// </summary>
    public sealed class ArenaEnvironment
    {
        public const string FilePrefix = "environment-";

        public readonly string Name;
        /// <summary>The whole layout, as ArenaLayout.Parse and ArenaLighting.Parse take it.</summary>
        public readonly string Json;
        public readonly JsonValue Root;
        public readonly string Dir;

        public bool IsDefault => Name == LayoutEnvironment.Default;
        public string MaterialDir => Dir + "/Materials";
        public string MergedDir => Dir + "/Merged";
        public string ScenePath(string rung, float density, bool merged = false) =>
            $"{Dir}/{ArenaIndex.SceneName(Name, rung, density, merged)}.unity";

        ArenaEnvironment(string name, string json)
        {
            Name = name;
            Json = json;
            Root = JsonValue.Parse(json);
            Dir = DirOf(name);
        }

        /// <summary>The derived files' directory of the named environment ("" or null: the forest's).</summary>
        public static string DirOf(string name) =>
            string.IsNullOrEmpty(name) || name == LayoutEnvironment.Default ? ArenaBaker.Dir : ArenaBaker.Dir + "/" + name;

        /// <summary>The environments in the data directory: the forest first, then environment-NAME.json in name order.</summary>
        public static List<string> Names()
        {
            var names = new List<string> { LayoutEnvironment.Default };
            names.AddRange(Directory.GetFiles(ArenaBaker.DataDir, FilePrefix + "*.json")
                .Select(p => Path.GetFileNameWithoutExtension(p).Substring(FilePrefix.Length)).OrderBy(n => n, StringComparer.Ordinal));
            return names;
        }

        /// <summary>The named environment; an unknown name fails, listing the known ones.</summary>
        public static ArenaEnvironment Load(string name)
        {
            if (string.IsNullOrEmpty(name) || name == LayoutEnvironment.Default)
                return new ArenaEnvironment(LayoutEnvironment.Default, File.ReadAllText(ArenaBaker.LayoutPath));
            var path = $"{ArenaBaker.DataDir}/{FilePrefix}{name}.json";
            if (!File.Exists(path)) throw new ArgumentException($"ArenaEnvironment: unknown environment '{name}' (one of {string.Join(", ", Names())})");
            return new ArenaEnvironment(name, LayoutEnvironment.Resolve(File.ReadAllText(path), LayoutText, name));
        }

        static string LayoutText(string layout)
        {
            var path = $"{ArenaBaker.DataDir}/{layout}-layout.json";
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }
    }
}
