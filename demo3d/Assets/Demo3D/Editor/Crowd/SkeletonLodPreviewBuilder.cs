using System;
using System.IO;
using System.Linq;
using Demo3D.Runtime.Crowd;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace Demo3D.EditorTools.Crowd
{
    public static class SkeletonLodPreviewBuilder
    {
        public const string ScenePath = SkeletonLodExperiment.Dir + "/SkeletonLodPreview.unity";

        public static void Scene()
        {
            SkeletonLodExperiment.ImportCandidates();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var preview = new GameObject("Skeleton LOD experiment").AddComponent<SkeletonLodPreview>();
            preview.Library = AssetDatabase.LoadAssetAtPath<CrowdLibrary>(SkeletonLodExperiment.LibraryPath);
            preview.CandidateLabel = TaskArgs.Get("-bLabel", "B");
            EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene(), ScenePath);
            Debug.Log("SkeletonLodPreview scene: " + ScenePath);
        }

        /// <summary>Explicit Mac/Metal/Mono route. Does not invoke Windows Configure/BuildPlayer.</summary>
        public static void BuildMac()
        {
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.StandaloneOSX)
                throw new Exception("Start Unity with -buildTarget OSXUniversal for the Mac preview");
            var target = NamedBuildTarget.Standalone;
            var backend = PlayerSettings.GetScriptingBackend(target);
            var apis = PlayerSettings.GetGraphicsAPIs(BuildTarget.StandaloneOSX);
            bool defaults = PlayerSettings.GetUseDefaultGraphicsAPIs(BuildTarget.StandaloneOSX);
            int architecture = PlayerSettings.GetArchitecture(target);
            try
            {
                PlayerSettings.SetScriptingBackend(target, ScriptingImplementation.Mono2x);
                PlayerSettings.SetArchitecture(target, 1); // Apple Silicon; Mono module installed on this Mac
                PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.StandaloneOSX, false);
                PlayerSettings.SetGraphicsAPIs(BuildTarget.StandaloneOSX, new[] { GraphicsDeviceType.Metal });
                var path = TaskArgs.Get("-previewBuild", "Builds/SkeletonLodPreview.app");
                var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
                {
                    scenes = new[] { ScenePath }, locationPathName = path,
                    target = BuildTarget.StandaloneOSX, targetGroup = BuildTargetGroup.Standalone,
                    options = BuildOptions.None,
                });
                if (report.summary.result != BuildResult.Succeeded)
                    throw new Exception("Mac preview build: " + report.summary.result);
                Debug.Log($"Mac preview build: {report.summary.outputPath}; {report.summary.totalTime.TotalSeconds:F1}s; Mono/Metal non-development");
            }
            finally
            {
                PlayerSettings.SetScriptingBackend(target, backend);
                PlayerSettings.SetArchitecture(target, architecture);
                PlayerSettings.SetGraphicsAPIs(BuildTarget.StandaloneOSX, apis);
                PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.StandaloneOSX, defaults);
            }
        }

        /// <summary>Matched offscreen detail views, using the same presets as the UI.</summary>
        public static void CaptureDetails()
        {
            if (SystemInfo.graphicsDeviceType != GraphicsDeviceType.Metal)
                throw new Exception("Detail captures require Metal");
            EditorSceneManager.OpenScene(ScenePath);
            var preview = UnityEngine.Object.FindAnyObjectByType<SkeletonLodPreview>();
            try
            {
                preview.Initialize();
                preview.Playing = false;
                preview.ClipIndex = preview.Library.Types[0].Clip("idle");
                preview.ClipTime = 0.4f; preview.Blend = 1;
                var dir = TaskArgs.Get("-previewCaptures", "captures/skeleton-lod-details");
                foreach (var shot in new[] { ("head", 1.5f, 0.8f), ("torso", 1.1f, 1.2f),
                    ("feet", 0.15f, 0.8f), ("closest", 1.5f, SkeletonLodPreview.MinViewWidth) })
                {
                    preview.Inspect(shot.Item2, shot.Item3);
                    for (int frame = 0; frame < 8; frame++) preview.DrawFrame(true);
                    preview.CaptureSet(Path.Combine(dir, shot.Item1));
                }
                Debug.Log("Preview detail captures complete: head, torso, feet and minimum zoom");
            }
            finally
            {
                preview.DisposePreview();
                UnityEngine.Object.DestroyImmediate(preview.gameObject);
            }
        }

        /// <summary>Offscreen real-renderer exercise: every clip, endpoints, crossfades, all variants.</summary>
        public static void Capture()
        {
            if (SystemInfo.graphicsDeviceType != GraphicsDeviceType.Metal)
                throw new Exception("Preview captures require Metal (do not use -nographics)");
            EditorSceneManager.OpenScene(ScenePath);
            var preview = UnityEngine.Object.FindAnyObjectByType<SkeletonLodPreview>();
            preview.Initialize();
            preview.Playing = false;
            var dir = TaskArgs.Get("-previewCaptures", "captures/skeleton-lod");
            Directory.CreateDirectory(dir);
            // Bring URP and the shadow atlas up before the first kept image.
            for (int frame = 0; frame < 8; frame++) preview.DrawFrame(true);
            var log = new System.Collections.Generic.List<object>();
            foreach (int ci in TaskArgs.Has("-previewSmoke") ? preview.ClipOrder.Take(1) : preview.ClipOrder)
            {
                var c = preview.Library.Types[0].Clips[ci];
                foreach (float phase in new[] { 0f, 0.25f, 0.5f, 0.75f, 0.9999f })
                {
                    preview.ClipIndex = ci; preview.PreviousClip = ci;
                    preview.ClipTime = c.Length * phase; preview.Blend = 1; preview.ViewWidth = 5;
                    for (int frame = 0; frame < 3; frame++) preview.DrawFrame(true);
                    preview.CaptureSet(Path.Combine(dir, $"clip-{ci:D02}-{phase:F2}"));
                    log.Add(new { clip = c.Name, time = preview.ClipTime, blend = 1, width = 5 });
                }
                int at = Array.IndexOf(preview.ClipOrder, ci);
                preview.PreviousClip = preview.ClipOrder[(at + preview.ClipOrder.Length - 1) % preview.ClipOrder.Length];
                preview.PreviousTime = preview.Library.Types[0].Clips[preview.PreviousClip].Length * 0.5f;
                preview.ClipTime = 0.1f;
                foreach (float blend in new[] { 0f, 0.5f, 1f })
                {
                    preview.Blend = blend;
                    for (int frame = 0; frame < 3; frame++) preview.DrawFrame(true);
                    preview.CaptureSet(Path.Combine(dir, $"crossfade-{ci:D02}-{blend:F1}"));
                    log.Add(new { clip = c.Name, time = 0.1f, blend, width = 5 });
                }
            }
            preview.ClipIndex = preview.Library.Types[0].Clip("idle"); preview.ClipTime = 0.4f; preview.Blend = 1;
            foreach (bool overhead in new[] { false, true })
            foreach (float width in new[] { 5f, 30f, 40f, 55f })
            {
                preview.ViewWidth = width; preview.Overhead = overhead;
                for (int frame = 0; frame < 3; frame++) preview.DrawFrame(true);
                preview.CaptureSet(Path.Combine(dir, $"idle-{(overhead ? 90 : 55)}deg-{width}m"));
            }
            File.WriteAllText(Path.Combine(dir, "render-exercise.json"), Newtonsoft.Json.JsonConvert.SerializeObject(log, Newtonsoft.Json.Formatting.Indented));
            int exercisedClips = TaskArgs.Has("-previewSmoke") ? 1 : preview.ClipOrder.Length;
            Debug.Log($"Preview capture exercised {exercisedClips} clips and their preceding transitions; Metal device {SystemInfo.graphicsDeviceName}; {dir}");
            preview.DisposePreview();
            UnityEngine.Object.DestroyImmediate(preview.gameObject);
        }
    }
}
