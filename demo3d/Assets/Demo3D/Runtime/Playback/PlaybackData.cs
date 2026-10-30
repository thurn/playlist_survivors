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
        public TextAsset annotations;
        public TextAsset roster;
        public TextAsset footprints;
        public TextAsset[] layouts;
        public TextAsset[] stressScripts;

        public static PlaybackData Load() =>
            Resources.Load<PlaybackData>(ResourcePath)
            ?? throw new System.InvalidOperationException("PlaybackData: not baked (run demo3d unity BakeAll)");

        /// <summary>The level script for a `--roster` set: action-level.json, or the set's variant.</summary>
        public TextAsset Level(RosterSet set) => set.IsDefault ? level
            : System.Array.Find(levels ?? new TextAsset[0], l => l.name == set.LevelName)
              ?? throw new System.InvalidOperationException($"PlaybackData: no {set.LevelName}.json baked (run demo3d unity BakeAll)");

        /// <summary>The clip annotations for a `--roster` set (<see cref="RosterSet.Annotations"/>).</summary>
        public string Annotations(RosterSet set) => set.Annotations(annotations.text);

        public TextAsset Layout(string name) => System.Array.Find(layouts, l => l.name == name + "-layout");

        public TextAsset Stress(string scene) => System.Array.Find(stressScripts, s => s.name == "stress-" + scene.ToLowerInvariant());
    }
}
