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
        public TextAsset annotations;
        public TextAsset roster;
        public TextAsset footprints;
        public TextAsset[] layouts;
        public TextAsset[] stressScripts;

        public static PlaybackData Load() =>
            Resources.Load<PlaybackData>(ResourcePath)
            ?? throw new System.InvalidOperationException("PlaybackData: not baked (run demo3d unity BakeAll)");

        public TextAsset Layout(string name) => System.Array.Find(layouts, l => l.name == name + "-layout");

        public TextAsset Stress(string scene) => System.Array.Find(stressScripts, s => s.name == "stress-" + scene.ToLowerInvariant());
    }
}
