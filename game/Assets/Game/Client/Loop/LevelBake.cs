using System;
using PS.Game.Sim.Content;
using PS.Game.Sim.Level;
using UnityEngine;

namespace Game.Client.Loop
{
    /// <summary>
    /// What the editor's level bake leaves for the player (LevelBaker, in the gitignored
    /// Assets/Baked/Level/Resources/): the sim's content files (<see cref="GameContent.Files"/>) and
    /// the level data's arena (arena.json), copied from game/Content as they were at bake time.
    /// </summary>
    public sealed class LevelBake : ScriptableObject
    {
        public const string ResourcePath = "GameLevel";
        public const string ArenaFile = "arena.json";

        public string[] names;
        [TextArea] public string[] texts;

        /// <summary>The bake, or null when this build has none (run `game unity BakeLevel`, or `game build`).</summary>
        public static LevelBake Load() => Resources.Load<LevelBake>(ResourcePath);

        public string Text(string name)
        {
            int i = Array.IndexOf(names, name);
            return i >= 0 ? texts[i] : throw new ArgumentException($"the level bake has no {name}; rebake it");
        }

        public GameContent Content() => GameContent.Parse(Text);

        public ArenaData Arena() => ArenaData.Parse(Text(ArenaFile), ArenaFile);
    }
}
