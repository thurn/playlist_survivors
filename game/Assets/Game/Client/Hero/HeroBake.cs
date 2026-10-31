using UnityEngine;

namespace Game.Client.Hero
{
    /// <summary>
    /// What the editor's hero bake leaves for the player (HeroBaker, in the gitignored
    /// Assets/Baked/Hero/Resources/): the knight ready to animate (URP materials, its shield, at the
    /// hero's height, no controller), its clips in <see cref="HeroClip"/> order, the clip table it was
    /// baked from, and the blade's shader, referenced so the build keeps it.
    /// </summary>
    public sealed class HeroBake : ScriptableObject
    {
        public const string ResourcePath = "GameHero";

        public GameObject knight;
        public AnimationClip[] clips;
        [TextArea] public string clipsJson;
        public Shader blade;

        /// <summary>The bake, or null when this build has none (run `game unity BakeHero`, or `game build`).</summary>
        public static HeroBake Load() => Resources.Load<HeroBake>(ResourcePath);
    }
}
