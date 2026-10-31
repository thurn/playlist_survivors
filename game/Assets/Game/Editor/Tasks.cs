using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Game.EditorTools
{
    /// <summary>
    /// Batch-mode entry points, run cold (`game unity X`). A task throws on failure, which a cold
    /// launch turns into exit code 1.
    /// </summary>
    public static class Tasks
    {
        public const string PipelinePath = "Assets/Game/Settings/URP-Game.asset";
        public const string RendererPath = "Assets/Game/Settings/URP-Game_Renderer.asset";
        public const string GlobalSettingsPath = "Assets/UniversalRenderPipelineGlobalSettings.asset";
        public const string BootScenePath = "Assets/Game/Scenes/Boot.unity";
        public const string AppName = "PlaylistSurvivors.app";

        /// <summary>Nothing: opens the project, imports and compiles (setup's license check).</summary>
        public static void Noop()
        {
            if (EditorUtility.scriptCompilationFailed)
                throw new Exception("Tasks.Noop: scripts have compile errors");
            Debug.Log("Tasks.Noop: project opened, Unity " + Application.unityVersion);
        }

        /// <summary>
        /// Generates the arena (Arena.ArenaBaker): its scene, materials, ground and lightmaps into the
        /// gitignored Assets/Baked/Arena/, and the level data into Content/arena.json. `[-noLightmaps]`.
        /// </summary>
        public static void BakeArena() => Arena.ArenaBaker.Bake();

        /// <summary>Bakes the hero (Hero.HeroBaker): the knight, its materials, clips and blade shader into the gitignored Assets/Baked/Hero/.</summary>
        public static void BakeHero() => Hero.HeroBaker.Bake();

        /// <summary>
        /// Bakes the crowd (Crowd.CrowdBaker): the nine monster models' style meshes and bone
        /// textures and the cast into the gitignored Assets/Baked/Crowd/.
        /// </summary>
        public static void BakeCrowd() => Crowd.CrowdBaker.Bake();

        /// <summary>
        /// Converts the effect selection (Content/effects.json: Effects.EffectConverter) into the gitignored
        /// Assets/Baked/Effects/Resources/EffectLibrary.asset.
        /// </summary>
        public static void BakeEffects() => Effects.EffectConverter.Bake();

        /// <summary>The sound bank (Content/sounds.json: Sound.SoundBaker) into the gitignored Assets/Baked/Sounds/.</summary>
        public static void BakeSounds() => Sound.SoundBaker.Bake();

        /// <summary>
        /// Cuts the baked arena's structure meshes into shards (Structures.StructureShardBaker) into the
        /// gitignored Assets/Baked/Structures/; the arena must be baked first.
        /// </summary>
        public static void BakeStructureShards() => Structures.StructureShardBaker.Bake();

        /// <summary>
        /// Copies the sim's content and the level data (Level.LevelBaker) into the gitignored
        /// Assets/Baked/Level/; after the arena, which writes Content/arena.json.
        /// </summary>
        public static void BakeLevel() => Level.LevelBaker.Bake();

        /// <summary>
        /// Every baker, for the tools (setup.py's GAME_BAKER_INPUTS): the arena, the crowd, the hero, the
        /// effects, the sounds, the structures' shards (after the arena, whose sites they cut) and the
        /// level (after the arena, whose level data it copies). Each
        /// baker that finishes appends its name to Assets/Baked/.bakeall-ran, which the tools read back
        /// to record the inputs it baked from. `-only NAME` (repeatable) runs just those.
        /// </summary>
        public static void BakeAll()
        {
            var args = Environment.GetCommandLineArgs();
            var only = args.Where((a, i) => i > 0 && args[i - 1] == "-only").ToList();
            Directory.CreateDirectory(BakedRoot);
            foreach (var (name, run) in new (string, Action)[] { ("Arena", BakeArena), ("Crowd", BakeCrowd), ("Hero", BakeHero),
                         ("Effects", BakeEffects), ("Sounds", BakeSounds),
                         ("StructureShards", BakeStructureShards), ("Level", BakeLevel) })
            {
                if (only.Count > 0 && !only.Contains(name)) continue;
                Debug.Log("Tasks.BakeAll: running " + name);
                run();
                File.AppendAllText(Path.Combine(BakedRoot, ".bakeall-ran"), name + "\n");
            }
        }

        /// <summary>The stale bakers, then the player, in one launch (`game build` when the bake's inputs changed).</summary>
        public static void BakeAndBuild()
        {
            BakeAll();
            BuildPlayer();
        }

        public const string BakedRoot = "Assets/Baked";

        // URP's build preprocessing rewrites these tracked assets for the build's target. BuildPlayer
        // puts them back, so a build leaves the checkout clean.
        static readonly string[] RestoredAfterBuild = { PipelinePath, RendererPath, GlobalSettingsPath };

        /// <summary>
        /// Builds the Mac player, an Apple silicon app drawing with Metal: `-backend il2cpp|mono`
        /// (default il2cpp), `-buildOut &lt;dir&gt;` (default Builds/mac/&lt;backend&gt;), `-development`,
        /// `-scene &lt;path&gt;` (repeatable; default the build settings' scenes). Windows comes with M5.
        /// </summary>
        public static void BuildPlayer()
        {
            if (TaskArgs.Get("-platform", "mac").ToLowerInvariant() != "mac")
                throw new Exception("Tasks.BuildPlayer: the game builds only the Mac player so far");
            var backend = TaskArgs.Get("-backend", "il2cpp").ToLowerInvariant();
            if (backend != "il2cpp" && backend != "mono")
                throw new Exception($"Tasks.BuildPlayer: unknown backend {backend}");
            var impl = backend == "mono" ? ScriptingImplementation.Mono2x : ScriptingImplementation.IL2CPP;
            var outDir = TaskArgs.Get("-buildOut", Path.Combine("Builds", "mac", backend));
            var args = Environment.GetCommandLineArgs();
            var scenes = args.Where((a, i) => i > 0 && args[i - 1] == "-scene").ToArray();
            if (scenes.Length == 0)
            {
                // The baked arena's scene, loaded by name at run time: it lives in the gitignored
                // Baked/, so it joins the build here, not in the tracked build settings.
                scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
                if (File.Exists(Arena.ArenaBaker.ScenePath)) scenes = scenes.Append(Arena.ArenaBaker.ScenePath).ToArray();
            }
            if (scenes.Length == 0) throw new Exception("Tasks.BuildPlayer: no scenes in the build settings");

            var kept = RestoredAfterBuild.Where(File.Exists).ToDictionary(p => p, File.ReadAllBytes);
            var previous = PlayerSettings.GetScriptingBackend(NamedBuildTarget.Standalone);
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone, impl);
            try
            {
                var options = new BuildPlayerOptions
                {
                    scenes = scenes,
                    locationPathName = Path.Combine(outDir, AppName),
                    target = BuildTarget.StandaloneOSX,
                    targetGroup = BuildTargetGroup.Standalone,
                    options = TaskArgs.Has("-development") ? BuildOptions.Development : BuildOptions.None,
                };
                var report = BuildPipeline.BuildPlayer(options);
                var s = report.summary;
                Debug.Log($"Tasks.BuildPlayer: {s.result} (mac, {backend}) in {s.totalTime.TotalSeconds:F1} s, " +
                          $"{s.totalSize / (1024 * 1024)} MiB, {s.totalErrors} errors -> {s.outputPath}");
                if (s.result != BuildResult.Succeeded)
                    throw new Exception($"Tasks.BuildPlayer: {s.result}");
            }
            finally
            {
                PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone, previous);
                AssetDatabase.SaveAssets();
                foreach (var kv in kept)
                {
                    if (File.ReadAllBytes(kv.Key).SequenceEqual(kv.Value)) continue;
                    File.WriteAllBytes(kv.Key, kv.Value);
                    AssetDatabase.ImportAsset(kv.Key, ImportAssetOptions.ForceUpdate);
                    Debug.Log("Tasks.BuildPlayer: restored " + kv.Key);
                }
            }
        }
    }
}
