using UnityEngine;

namespace Demo3D.Runtime.Playback
{
    /// <summary>
    /// The inputs the player builds its timeline from, baked (PlaybackBaker) into
    /// Assets/Baked/Playback/Resources/ because it references the baked song map and music.
    /// </summary>
    public sealed class PlaybackData : ScriptableObject
    {
        public const string ResourcePath = "Demo3DPlaybackData";

        public TextAsset songMap;
        public AudioClip music;
        public TextAsset level;
        /// <summary>The `--roster` sets' level variants (action-level-NAME.json).</summary>
        public TextAsset[] levels;
        /// <summary>The `--intensity` overlays (intensity-NAME.json).</summary>
        public TextAsset[] intensities;
        public TextAsset annotations;
        public TextAsset roster;
        public TextAsset footprints;
        public TextAsset[] layouts;
        /// <summary>ps-bop: the environments beside the arena (environment-NAME.json), each extending its layout.</summary>
        public TextAsset[] environments;
        public TextAsset[] stressScripts;

        public static PlaybackData Load() =>
            Resources.Load<PlaybackData>(ResourcePath)
            ?? throw new System.InvalidOperationException("PlaybackData: not baked (run demo3d unity BakeAll)");

        /// <summary>The level script for a `--roster` set: action-level.json, or the set's variant.</summary>
        public TextAsset Level(RosterSet set) => set.IsDefault ? level
            : System.Array.Find(levels ?? new TextAsset[0], l => l.name == set.LevelName)
              ?? throw new System.InvalidOperationException($"PlaybackData: no {set.LevelName}.json baked (run demo3d unity BakeAll)");

        /// <summary>The overlay JSON for an `--intensity` (null for normal), merged over the level script by <see cref="PS.Choreography.LevelScript.Parse"/>.</summary>
        public string Overlay(Intensity intensity) => intensity.IsNormal ? null
            : (System.Array.Find(intensities ?? new TextAsset[0], i => i.name == intensity.OverlayName)
               ?? throw new System.InvalidOperationException($"PlaybackData: no {intensity.OverlayName}.json baked (run demo3d unity BakeAll)")).text;

        /// <summary>The clip annotations for a `--roster` set (<see cref="RosterSet.Annotations"/>).</summary>
        public string Annotations(RosterSet set) => set.Annotations(annotations.text);

        public TextAsset Layout(string name) => System.Array.Find(layouts, l => l.name == name + "-layout");

        /// <summary>The environments' names: the forest (the arena's own layout) first.</summary>
        public string[] EnvironmentNames()
        {
            var names = new System.Collections.Generic.List<string> { PS.Choreography.LayoutEnvironment.Default };
            foreach (var e in environments ?? new TextAsset[0]) names.Add(e.name.Substring(EnvironmentPrefix.Length));
            return names.ToArray();
        }

        public const string EnvironmentPrefix = "environment-";

        /// <summary>
        /// An environment's whole layout (ps-bop): the arena's for the forest (or null), else
        /// environment-NAME.json resolved over the layout it extends. An unknown name fails, listing the known ones.
        /// </summary>
        public string EnvironmentLayout(string name)
        {
            if (string.IsNullOrEmpty(name) || name == PS.Choreography.LayoutEnvironment.Default) return Layout("arena").text;
            var file = System.Array.Find(environments ?? new TextAsset[0], e => e.name == EnvironmentPrefix + name)
                ?? throw new System.ArgumentException($"--environment: unknown environment '{name}' (one of {string.Join(", ", EnvironmentNames())})");
            return PS.Choreography.LayoutEnvironment.Resolve(file.text, n => Layout(n)?.text, name);
        }

        public TextAsset Stress(string scene) => System.Array.Find(stressScripts, s => s.name == "stress-" + scene.ToLowerInvariant());
    }
}
