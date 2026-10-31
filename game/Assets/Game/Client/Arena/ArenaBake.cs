using UnityEngine;

namespace Game.Client.Arena
{
    /// <summary>
    /// What the editor's arena bake leaves for the player (ArenaBaker, in the gitignored
    /// Assets/Baked/Arena/Resources/): the scene to load, the layout it was generated from (its
    /// lighting, palettes and shots) and the crystal structures' shader, referenced here so the build
    /// keeps it although no scene material uses it.
    /// </summary>
    public sealed class ArenaBake : ScriptableObject
    {
        public const string ResourcePath = "GameArena";

        public string scene;
        [TextArea] public string layoutJson;
        public Shader crystal;
        public int sites;

        /// <summary>The bake, or null when this build has none (run `game unity BakeArena`, or `game build`).</summary>
        public static ArenaBake Load() => Resources.Load<ArenaBake>(ResourcePath);
    }
}
