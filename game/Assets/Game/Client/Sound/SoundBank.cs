using System;
using UnityEngine;

namespace Game.Client.Sound
{
    /// <summary>
    /// The game's sounds: every category's clips (`category/variant`), each with its onset (the
    /// seconds of near silence before its attack, which a voice starts early by), level and root
    /// note, and the mix's limit, from game/Content/sounds.json. Baked by
    /// Editor/Sound/SoundBaker into the gitignored Assets/Baked/Sounds/Resources/, since it references
    /// vendor clips.
    /// </summary>
    public sealed class SoundBank : ScriptableObject
    {
        public const string ResourcePath = "GameSoundBank";

        public string[] Names = Array.Empty<string>();
        public AudioClip[] Clips = Array.Empty<AudioClip>();
        public float[] Onsets = Array.Empty<float>();
        /// <summary>Each clip's level: its category's in sounds.json, times the clip's own.</summary>
        public float[] Levels = Array.Empty<float>();
        /// <summary>Each clip's root note (MIDI), for pitched categories such as the gem chime; 0 elsewhere.</summary>
        public float[] RootNotes = Array.Empty<float>();
        /// <summary>The mix's limit: the effects' level at the follower's full gain.</summary>
        public float Limit = 1f;

        public static SoundBank Load() => Resources.Load<SoundBank>(ResourcePath);

        public int IndexOf(string name) => Array.IndexOf(Names, name);
    }
}
