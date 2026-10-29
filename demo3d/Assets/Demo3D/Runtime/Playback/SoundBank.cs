using System;
using UnityEngine;

namespace Demo3D.Runtime.Playback
{
    /// <summary>
    /// The timeline's sounds, as a bank of clips per sound name (`category/variant`), baked by
    /// SoundBaker into Assets/Baked/Sounds/Resources/ from the vendor sound packs.
    /// </summary>
    public sealed class SoundBank : ScriptableObject
    {
        public const string ResourcePath = "Demo3DSoundBank";
        public string[] names;
        public AudioClip[] clips;

        public AudioClip Find(string name)
        {
            int i = Array.IndexOf(names, name);
            return i >= 0 ? clips[i] : null;
        }

        public static SoundBank Load() => Resources.Load<SoundBank>(ResourcePath);
    }
}
