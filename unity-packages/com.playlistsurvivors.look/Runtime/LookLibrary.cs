using System.Collections.Generic;
using UnityEngine;

namespace PS.Look
{
    /// <summary>
    /// The package's look presets and palettes as JSON text by name: Resources/Look/Looks/NAME.json and
    /// Resources/Look/Palettes/NAME.json, in every build of both players (<see cref="Shared"/>).
    /// </summary>
    public sealed class LookLibrary
    {
        static LookLibrary _shared;

        /// <summary>The package's own files, loaded on first use.</summary>
        public static LookLibrary Shared => _shared ??= new LookLibrary(Load("Look/Looks"), Load("Look/Palettes"));

        readonly IReadOnlyDictionary<string, string> _presets, _palettes;

        public LookLibrary(IReadOnlyDictionary<string, string> presets, IReadOnlyDictionary<string, string> palettes)
        {
            _presets = presets;
            _palettes = palettes;
        }

        public IEnumerable<string> Presets => Sorted(_presets);
        public IEnumerable<string> Palettes => Sorted(_palettes);
        public bool HasPreset(string name) => name != null && _presets.ContainsKey(name);
        public bool HasPalette(string name) => name != null && _palettes.ContainsKey(name);
        public string PresetJson(string name) => _presets[name];
        public string PaletteJson(string name) => _palettes[name];

        static Dictionary<string, string> Load(string folder)
        {
            var files = new Dictionary<string, string>();
            foreach (var asset in Resources.LoadAll<TextAsset>(folder)) files[asset.name] = asset.text;
            return files;
        }

        static List<string> Sorted(IReadOnlyDictionary<string, string> files)
        {
            var names = new List<string>(files.Keys);
            names.Sort(System.StringComparer.Ordinal);
            return names;
        }
    }
}
