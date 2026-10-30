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

        /// <summary>
        /// Bakes a `--roster` set (`-roster forest|heroic`; default: today's roster, as BakeAll does)
        /// into Assets/Baked/Rosters/NAME/. Run `demo3d setup --roster NAME` first to sync its packs.
        /// </summary>
        public static void BakeRoster() => Assets.RosterBaker.Bake(TaskArgs.Get("-roster", "default"));

        /// <summary>Bakes the bone-texture crowd and the stock baseline prefabs into Assets/Baked/Crowd/ (TD5).</summary>
        public static void BakeCrowd() => Crowd.CrowdBaker.Bake();

        /// <summary>
        /// Generates the arena's scenes, materials, ground and lightmaps into Assets/Baked/Arena/
        /// (TD9), every environment's or `-environment a,b`'s (ps-bop): `[-e0 0.5,1,2,4] [-lit 1,2] [-noLightmaps]`.
        /// </summary>
        public static void BakeArena() => Arena.ArenaBaker.Bake();

        /// <summary>Proposes clip annotations and writes contact sheets (TD2).</summary>
        public static void AnnotateClips() =>
            Assets.ClipAnnotator.Run(TaskArgs.Get("-evidence", "evidence/td2"), TaskArgs.Get("-roster", "default"));

        /// <summary>Renders TD2's style stills (roster and kit at night, boss candidates).</summary>
        public static void StyleStills() =>
            Assets.StyleStill.Run(TaskArgs.Get("-evidence", "evidence/td2"));

        /// <summary>Measures the kit pieces' footprints into the choreography layer's data (TD6).</summary>
        public static void MeasureFootprints() => Assets.FootprintMeasurer.Run();

        /// <summary>Lists the effect candidates' particle modules (TD8).</summary>
        public static void EffectInventory() =>
            Effects.EffectInventory.Run(TaskArgs.Get("-out", "evidence/td8/inventory.md"));

        /// <summary>Converts the chosen effects (Data/effects.json) into Assets/Baked/Effects/ (TD8).</summary>
        public static void BakeEffects() => Effects.EffectConverter.Bake();

        /// <summary>Writes the runtime spike's VFX Graph template from the package's Simple Burst (TD8).</summary>
        public static void VfxSpikeTemplate() => Effects.VfxSpikeTemplate.Write();

        /// <summary>Runs every effect candidate through the converter and writes the report (TD8).</summary>
        public static void EffectReport() =>
            Effects.EffectConverter.Report(TaskArgs.Get("-out", "evidence/td8/converter-report.md"));

        /// <summary>
        /// Builds the player. `-platform windows|mac` (default: this machine's), `-backend
        /// il2cpp|mono` (default il2cpp), `-buildOut <dir>` (default Builds/&lt;backend&gt;, or
        /// Builds/mac/&lt;backend&gt;), `-development`, `-scene <path>` (repeatable; default: the build
        /// settings' scenes). The Mac's player is an Apple silicon app drawing with Metal
        /// (ProjectSetup).
        /// </summary>
        // URP's build preprocessing rewrites these tracked assets for the build's target (the
        // pipeline's shader prefilters, the global settings' runtime list). BuildPlayer puts them
        // back, so a build leaves the checkout clean: Tollgate won't sync a local master whose
        // checkout has changes.
        static readonly string[] RestoredAfterBuild = { ProjectSetup.PipelinePath, "Assets/UniversalRenderPipelineGlobalSettings.asset" };

        public static void BuildPlayer()
        {
            bool mac = TaskArgs.Get("-platform", Application.platform == RuntimePlatform.OSXEditor ? "mac" : "windows")
                .ToLowerInvariant() == "mac";
            var backend = TaskArgs.Get("-backend", "il2cpp").ToLowerInvariant();
            var impl = backend == "mono" ? ScriptingImplementation.Mono2x : ScriptingImplementation.IL2CPP;
            var outDir = TaskArgs.Get("-buildOut", mac ? Path.Combine("Builds", "mac", backend) : Path.Combine("Builds", backend));
            var scenes = TaskArgs.All("-scene");
            if (scenes.Count == 0)
            {
                scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToList();
                // The baked arena's scenes (TD9), every environment's (ps-bop), loaded by name at runtime. They live in
                // the gitignored Baked/, so they join the build here, not in the tracked build settings.
                if (Directory.Exists(Arena.ArenaBaker.Dir))
                    scenes.AddRange(Directory.GetFiles(Arena.ArenaBaker.Dir, "*.unity", SearchOption.AllDirectories).Select(p => p.Replace(Path.DirectorySeparatorChar, '/')).OrderBy(p => p));
            }
            if (scenes.Count == 0) throw new Exception("Tasks.BuildPlayer: no scenes to build");

            var kept = RestoredAfterBuild.Where(File.Exists).ToDictionary(p => p, File.ReadAllBytes);
            var previous = PlayerSettings.GetScriptingBackend(NamedBuildTarget.Standalone);
            int architecture = PlayerSettings.GetArchitecture(NamedBuildTarget.Standalone);
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone, impl);
            if (mac) PlayerSettings.SetArchitecture(NamedBuildTarget.Standalone, 1); // Apple silicon
            try
            {
                var options = new BuildPlayerOptions
                {
                    scenes = scenes.ToArray(),
                    locationPathName = Path.Combine(outDir, mac ? "PlaylistSurvivors3D.app" : "PlaylistSurvivors3D.exe"),
                    target = mac ? BuildTarget.StandaloneOSX : BuildTarget.StandaloneWindows64,
                    targetGroup = BuildTargetGroup.Standalone,
                    options = TaskArgs.Has("-development") ? BuildOptions.Development : BuildOptions.None,
                };
                var report = BuildPipeline.BuildPlayer(options);
                var s = report.summary;
                Debug.Log($"Tasks.BuildPlayer: {s.result} ({(mac ? "mac, " : "")}{backend}) in {s.totalTime.TotalSeconds:F1} s, " +
                          $"{s.totalSize / (1024 * 1024)} MiB, {s.totalErrors} errors -> {s.outputPath}");
                if (s.result != BuildResult.Succeeded)
                    throw new Exception($"Tasks.BuildPlayer: {s.result}");
            }
            finally
            {
                PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone, previous);
                PlayerSettings.SetArchitecture(NamedBuildTarget.Standalone, architecture);
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
