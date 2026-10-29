using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Demo3D.EditorTools
{
    /// <summary>
    /// Batch-mode entry points, run cold (`Unity.exe -batchmode -executeMethod
    /// Demo3D.EditorTools.Tasks.X -quit`) or through the warm editor (`demo3d unity X`). A task
    /// throws on failure, which a cold launch turns into exit code 1, and never calls
    /// EditorApplication.Exit, which would end the warm editor.
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

        /// <summary>Runs every registered baker (TD5: "crowd").</summary>
        public static void BakeAll() => Bakers.RunStale(TaskArgs.All("-only"));

        /// <summary>Bakes the bone-texture crowd and the stock baseline prefabs into Assets/Baked/Crowd/ (TD5).</summary>
        public static void BakeCrowd() => Crowd.CrowdBaker.Bake();

        /// <summary>Proposes clip annotations and writes contact sheets (TD2).</summary>
        public static void AnnotateClips() =>
            Assets.ClipAnnotator.Run(TaskArgs.Get("-evidence", "evidence/td2"));

        /// <summary>Renders TD2's style stills (roster and kit at night, boss candidates).</summary>
        public static void StyleStills() =>
            Assets.StyleStill.Run(TaskArgs.Get("-evidence", "evidence/td2"));

        /// <summary>Measures the kit pieces' footprints into the choreography layer's data (TD6).</summary>
        public static void MeasureFootprints() => Assets.FootprintMeasurer.Run();

        /// <summary>Lists the effect candidates' particle modules (TD8).</summary>
        public static void EffectInventory() =>
            Effects.EffectInventory.Run(TaskArgs.Get("-out", "evidence/td8/inventory.md"));

        /// <summary>
        /// Builds the Windows x64 player. `-backend il2cpp|mono` (default il2cpp), `-buildOut
        /// <dir>` (default Builds/&lt;backend&gt;), `-development`, `-scene <path>` (repeatable;
        /// default: the build settings' scenes).
        /// </summary>
        public static void BuildPlayer()
        {
            var backend = TaskArgs.Get("-backend", "il2cpp").ToLowerInvariant();
            var impl = backend == "mono" ? ScriptingImplementation.Mono2x : ScriptingImplementation.IL2CPP;
            var outDir = TaskArgs.Get("-buildOut", Path.Combine("Builds", backend));
            var scenes = TaskArgs.All("-scene");
            if (scenes.Count == 0)
                scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToList();
            if (scenes.Count == 0) throw new Exception("Tasks.BuildPlayer: no scenes to build");

            var previous = PlayerSettings.GetScriptingBackend(NamedBuildTarget.Standalone);
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone, impl);
            try
            {
                var options = new BuildPlayerOptions
                {
                    scenes = scenes.ToArray(),
                    locationPathName = Path.Combine(outDir, "PlaylistSurvivors3D.exe"),
                    target = BuildTarget.StandaloneWindows64,
                    targetGroup = BuildTargetGroup.Standalone,
                    options = TaskArgs.Has("-development") ? BuildOptions.Development : BuildOptions.None,
                };
                var report = BuildPipeline.BuildPlayer(options);
                var s = report.summary;
                Debug.Log($"Tasks.BuildPlayer: {s.result} ({backend}) in {s.totalTime.TotalSeconds:F1} s, " +
                          $"{s.totalSize / (1024 * 1024)} MiB, {s.totalErrors} errors -> {s.outputPath}");
                if (s.result != BuildResult.Succeeded)
                    throw new Exception($"Tasks.BuildPlayer: {s.result}");
            }
            finally
            {
                PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone, previous);
            }
        }
    }
}
