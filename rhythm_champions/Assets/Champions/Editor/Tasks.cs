using System;
using System.IO;
using System.Linq;
using Champions.EditorTools.Effects;
using Champions.EditorTools.Monsters;
using Champions.EditorTools.Sound;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Champions.EditorTools
{
    /// <summary>
    /// Batch-mode entry points (`champions unity X`), run cold or through the warm editor. A task
    /// throws on failure, which a cold launch turns into exit code 1.
    /// </summary>
    public static class Tasks
    {
        public const string PipelinePath = "Assets/Champions/Settings/URP-Champions.asset";
        public const string RendererPath = "Assets/Champions/Settings/URP-Champions_Renderer.asset";
        public const string GlobalSettingsPath = "Assets/UniversalRenderPipelineGlobalSettings.asset";
        public const string BootScenePath = "Assets/Champions/Scenes/Boot.unity";
        public const string AppName = "RhythmChampions.app";

        /// <summary>Nothing: opens the project, imports and compiles (setup's license check).</summary>
        public static void Noop()
        {
            if (EditorUtility.scriptCompilationFailed)
                throw new Exception("Tasks.Noop: scripts have compile errors");
            Debug.Log("Tasks.Noop: project opened, Unity " + Application.unityVersion);
        }

        /// <summary>
        /// The libraries a player loads that only reference vendor files, into the ignored Assets/Baked/: the weapon
        /// effects' Hovl meshes (<see cref="EffectBaker"/>), the sound bank (<see cref="SoundBaker"/>) and every monster
        /// type whose bake is out of date (<see cref="MonsterBaker"/>, from a recipe's mesh where `champions monster bake`
        /// left one). Each skips, with a warning, when its vendor files are missing.
        /// </summary>
        public static void Bake()
        {
            EffectBaker.Bake();
            SoundBaker.Bake();
            MonsterBaker.BakeAll();
        }

        // URP's build preprocessing rewrites these tracked assets for the build's target. BuildPlayer
        // puts them back, so a build leaves the checkout clean.
        static readonly string[] RestoredAfterBuild = { PipelinePath, RendererPath, GlobalSettingsPath };

        /// <summary>
        /// Builds the Mac player, an Apple silicon app drawing with Metal: `-backend mono|il2cpp`
        /// (default mono, plan.md Build Policy), `-buildOut &lt;dir&gt;` (default Builds/mac/&lt;backend&gt;),
        /// `-development`, `-scene &lt;path&gt;` (repeatable; default the build settings' scenes). The
        /// shader variant budget (<see cref="ShaderVariantBudget"/>) fails a build over it. It bakes the libraries that
        /// only reference vendor files first (<see cref="Bake"/>), so a build always has the current ones.
        /// </summary>
        public static void BuildPlayer()
        {
            if (TaskArgs.Get("-platform", "mac").ToLowerInvariant() != "mac")
                throw new Exception("Tasks.BuildPlayer: Rhythm Champions builds only the Mac player");
            var backend = TaskArgs.Get("-backend", "mono").ToLowerInvariant();
            if (backend != "il2cpp" && backend != "mono")
                throw new Exception($"Tasks.BuildPlayer: unknown backend {backend}");
            var impl = backend == "mono" ? ScriptingImplementation.Mono2x : ScriptingImplementation.IL2CPP;
            var outDir = TaskArgs.Get("-buildOut", Path.Combine("Builds", "mac", backend));
            var scenes = TaskArgs.All("-scene").ToArray();
            if (scenes.Length == 0) scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
            if (scenes.Length == 0) throw new Exception("Tasks.BuildPlayer: no scenes in the build settings");

            Bake();
            var kept = RestoredAfterBuild.Where(File.Exists).ToDictionary(p => p, File.ReadAllBytes);
            var previous = PlayerSettings.GetScriptingBackend(NamedBuildTarget.Standalone);
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone, impl);
            UnityEditor.OSXStandalone.UserBuildSettings.architecture = OSArchitecture.ARM64;
            try
            {
                var options = new BuildPlayerOptions
                {
                    scenes = scenes,
                    locationPathName = Path.Combine(outDir, AppName),
                    target = BuildTarget.StandaloneOSX,
                    targetGroup = BuildTargetGroup.Standalone,
                    options = (TaskArgs.Has("-development") ? BuildOptions.Development : BuildOptions.None)
                              | (TaskArgs.Has("-cleanBuildCache") ? BuildOptions.CleanBuildCache : BuildOptions.None),
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
