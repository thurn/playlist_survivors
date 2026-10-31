using System.IO;
using System.Linq;
using Game.Client.Loop;
using PS.Game.Sim.Content;
using UnityEditor;
using UnityEngine;

namespace Game.EditorTools.Level
{
    /// <summary>
    /// Copies the sim's content (game/Content: <see cref="GameContent.Files"/>) and the level data's
    /// arena (Content/arena.json, which the arena's bake writes) into the player's
    /// <see cref="LevelBake"/>, the gitignored Assets/Baked/Level/Resources/GameLevel.asset. It
    /// parses them first, so a bad file fails the bake instead of the run.
    /// </summary>
    public static class LevelBaker
    {
        public const string Dir = "Assets/Baked/Level";
        const string ContentDir = "Content";

        public static void Bake()
        {
            var names = GameContent.Files.Append(LevelBake.ArenaFile).ToArray();
            var bake = ScriptableObject.CreateInstance<LevelBake>();
            bake.names = names;
            bake.texts = names.Select(n => File.ReadAllText(Path.Combine(ContentDir, n))).ToArray();
            var content = bake.Content();
            var arena = bake.Arena();

            Directory.CreateDirectory(Dir + "/Resources");
            var path = $"{Dir}/Resources/{LevelBake.ResourcePath}.asset";
            AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(bake, path);
            AssetDatabase.SaveAssets();
            Debug.Log($"LevelBaker: {content.Roles.Length} roles, {content.Weapons.Length} weapons and a " +
                      $"{arena.Width} x {arena.Height} tile arena with {arena.Structures.Length} structures into {path}");
        }
    }
}
