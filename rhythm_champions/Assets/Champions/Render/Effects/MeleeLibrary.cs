using UnityEngine;

namespace Champions.Render
{
    /// <summary>Private baked rigid weapons, handle at zero and tip/head along +Y, unit length.</summary>
    public sealed class MeleeLibrary : ScriptableObject
    {
        public Mesh Sword, Hammer;
        public Shader Edge;
        public static MeleeLibrary Load() => Resources.Load<MeleeLibrary>("MeleeWeapons");
    }
}
