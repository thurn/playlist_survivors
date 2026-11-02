using System;
using UnityEngine;

namespace Champions.Client.Sound
{
    /// <summary>
    /// The game's sounds: every category's clips (`category/variant`), each with its onset (the seconds of near silence
    /// before its attack, which a voice starts early by), level, and the mix's limit, from rhythm_champions/Content/
    /// sounds.json. Baked by the editor's SoundBaker into the ignored Assets/Baked/Sounds/Resources/, since it references
    /// vendor clips (game/'s, ported by review).
    /// </summary>
    public sealed class SoundBank : ScriptableObject
    {
        public const string ResourcePath = "SoundBank";

        public string[] Names = Array.Empty<string>();
        public AudioClip[] Clips = Array.Empty<AudioClip>();
        public float[] Onsets = Array.Empty<float>();
        /// <summary>Each clip's level: its category's gain in sounds.json.</summary>
        public float[] Levels = Array.Empty<float>();
        /// <summary>The mix's limit: the effects' level at full gain, under the music.</summary>
        public float Limit = 1f;

        /// <summary>The baked bank, or null when there is none (a checkout without the vendor files).</summary>
        public static SoundBank Load() => Resources.Load<SoundBank>(ResourcePath);

        public int IndexOf(string name) => Array.IndexOf(Names, name);
    }
}
