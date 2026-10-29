using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Demo2D.EditorTools
{
    /// <summary>
    /// Batch-mode entry points, run cold (`Unity -batchmode -executeMethod
    /// Demo2D.EditorTools.Tasks.X -quit`, or `demo2d unity X`). A task throws on failure, which a
    /// cold launch turns into exit code 1.
    /// </summary>
    public static class Tasks
    {
        /// <summary>Nothing: opens the project, imports and compiles (setup's license check).</summary>
        public static void Noop()
        {
            if (EditorUtility.scriptCompilationFailed)
                throw new Exception("Tasks.Noop: scripts have compile errors");
            Debug.Log("Tasks.Noop: project opened, Unity " + Application.unityVersion);
        }

        /// <summary>Applies the project configuration (ProjectSetup).</summary>
        public static void Configure() => ProjectSetup.Configure();

        /// <summary>
        /// PX2's asset evidence into `-out &lt;dir&gt;` (default evidence/px2): a contact sheet per
        /// character, the tile test map, the swaying trees and the CFXR gallery, all tagged renders.
        /// </summary>
        public static void AssetStills()
        {
            Assets.AssetStills.Write(TaskArgs.Get("-out", Path.Combine("evidence", "px2")));
        }

        /// <summary>PX3: bakes the clip library's frames into Assets/Baked/Characters (SpriteBaker).</summary>
        public static void BakeSprites() => Assets.SpriteBaker.Run();

        /// <summary>
        /// PX3: bakes, then rewrites the clip library (Data/clip-annotations.json) from the frames
        /// and writes its contact sheets into `-out &lt;dir&gt;` (default evidence/px3).
        /// </summary>
        public static void AnnotateClips() =>
            Assets.ClipAnnotator.Run(Assets.SpriteBaker.Run(), TaskArgs.Get("-out", Path.Combine("evidence", "px3")));

        /// <summary>PX3's lineup and crowd stills from the baked atlases into `-out &lt;dir&gt;` (default evidence/px3).</summary>
        public static void SpriteStills() => Assets.SpriteStills.Write(TaskArgs.Get("-out", Path.Combine("evidence", "px3")));

        // URP's build preprocessing rewrites these tracked assets for the build's target (the
        // pipeline's shader prefilters, the global settings' runtime list). BuildPlayer puts them
        // back, so a build leaves the checkout clean.
        static readonly string[] RestoredAfterBuild = { ProjectSetup.PipelinePath, ProjectSetup.GlobalSettingsPath };

        /// <summary>
        /// Builds the Mac player, an Apple silicon app drawing with Metal: `-backend il2cpp|mono`
        /// (default il2cpp), `-buildOut &lt;dir&gt;` (default Builds/mac/&lt;backend&gt;), `-development`,
        /// `-scene &lt;path&gt;` (repeatable; default: the build settings' scenes).
        /// </summary>
        public static void BuildPlayer()
        {
            if (TaskArgs.Get("-platform", "mac").ToLowerInvariant() != "mac")
                throw new Exception("Tasks.BuildPlayer: demo2d builds only the Mac player");
            var backend = TaskArgs.Get("-backend", "il2cpp").ToLowerInvariant();
            var impl = backend == "mono" ? ScriptingImplementation.Mono2x : ScriptingImplementation.IL2CPP;
            var outDir = TaskArgs.Get("-buildOut", Path.Combine("Builds", "mac", backend));
            var scenes = TaskArgs.All("-scene");
            if (scenes.Count == 0)
                scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToList();
            if (scenes.Count == 0) throw new Exception("Tasks.BuildPlayer: no scenes to build");

            var kept = RestoredAfterBuild.Where(File.Exists).ToDictionary(p => p, File.ReadAllBytes);
            var previous = PlayerSettings.GetScriptingBackend(NamedBuildTarget.Standalone);
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone, impl);
            try
            {
                var options = new BuildPlayerOptions
                {
                    scenes = scenes.ToArray(),
                    locationPathName = Path.Combine(outDir, "PlaylistSurvivors2D.app"),
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
                AssetDatabase.SaveAssets(); // write any rewrite still pending, then undo it on disk and in memory
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
