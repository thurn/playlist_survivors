using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Rendering;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Demo2D.Runtime;

namespace Demo2D.EditorTools
{
    /// <summary>
    /// Puts the project into its configuration (docs/tech-demo-2d/README.md): an Apple silicon
    /// Mac player drawing with Metal, IL2CPP, linear colour, 1920×1080 windowed, frame timing
    /// stats on, and URP with its 2D renderer (sprites sorted by their feet's y, HDR for the
    /// bloom to come, no MSAA on pixel art). Also writes the empty scene the build starts from.
    /// Idempotent: run it after changing anything here, and commit the settings it writes.
    /// </summary>
    public static class ProjectSetup
    {
        public const string SettingsDir = "Assets/Demo2D/Settings";
        public const string PipelinePath = SettingsDir + "/URP-Demo2D.asset";
        public const string RendererPath = SettingsDir + "/URP-Demo2D_Renderer2D.asset";
        public const string GlobalSettingsPath = "Assets/UniversalRenderPipelineGlobalSettings.asset";
        public const string EmptyScenePath = "Assets/Demo2D/Scenes/Empty.unity";
        const string PostProcessDataPath = "Packages/com.unity.render-pipelines.universal/Runtime/Data/PostProcessData.asset";
        public const string SpriteLitPath = "Packages/com.unity.render-pipelines.universal/Runtime/Materials/Sprite-Lit-Default.mat";

        public static void Configure()
        {
            ConfigurePlayer();
            var pipeline = ConfigurePipeline();
            GraphicsSettings.defaultRenderPipeline = pipeline;
            ConfigureQuality(pipeline);
            AssetDatabase.SaveAssets();
            EnsureEmptyScene();
            AssetDatabase.SaveAssets();
            Debug.Log("ProjectSetup: configured");
        }

        static void ConfigurePlayer()
        {
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.StandaloneOSX, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.StandaloneOSX, new[] { GraphicsDeviceType.Metal });
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone, ScriptingImplementation.IL2CPP);
            PlayerSettings.SetApiCompatibilityLevel(NamedBuildTarget.Standalone, ApiCompatibilityLevel.NET_Standard);
            PlayerSettings.SetArchitecture(NamedBuildTarget.Standalone, 1); // Apple silicon
            PlayerSettings.companyName = "Playlist Survivors";
            PlayerSettings.productName = "Playlist Survivors 2D Demo";
            PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.defaultIsNativeResolution = false;
            PlayerSettings.defaultScreenWidth = 1920;
            PlayerSettings.defaultScreenHeight = 1080;
            PlayerSettings.resizableWindow = false;
            PlayerSettings.allowFullscreenSwitch = false;
            PlayerSettings.runInBackground = true;
            PlayerSettings.visibleInBackground = true;
            PlayerSettings.enableFrameTimingStats = true; // PX10's song check reads FrameTimingManager
            PlayerSettings.usePlayerLog = true;
            PlayerSettings.SplashScreen.show = false;
            PlayerSettings.SplashScreen.showUnityLogo = false;
            EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Standalone, BuildTarget.StandaloneOSX);
        }

        static UniversalRenderPipelineAsset ConfigurePipeline()
        {
            Directory.CreateDirectory(SettingsDir);
            var renderer = AssetDatabase.LoadAssetAtPath<Renderer2DData>(RendererPath);
            if (renderer == null)
            {
                // As URP's "2D Renderer" menu item does: the data, its default resources, the
                // default post-processing data.
                renderer = ScriptableObject.CreateInstance<Renderer2DData>();
                AssetDatabase.CreateAsset(renderer, RendererPath);
                ResourceReloader.ReloadAllNullIn(renderer, UniversalRenderPipelineAsset.packagePath);
            }
            var r = new SerializedObject(renderer);
            var post = AssetDatabase.LoadAssetAtPath<PostProcessData>(PostProcessDataPath)
                       ?? throw new InvalidOperationException("ProjectSetup: no " + PostProcessDataPath);
            Find(r, "m_PostProcessData").objectReferenceValue = post;
            // Characters sort by their feet's y (Vampire Survivors' convention): lower on screen, in front.
            Find(r, "m_TransparencySortMode").intValue = (int)TransparencySortMode.CustomAxis;
            Find(r, "m_TransparencySortAxis").vector3Value = new Vector3(0, 1, 0);
            r.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(renderer);

            var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(PipelinePath);
            if (pipeline == null)
            {
                pipeline = UniversalRenderPipelineAsset.Create(renderer);
                AssetDatabase.CreateAsset(pipeline, PipelinePath);
            }
            var p = new SerializedObject(pipeline);
            Find(p, "m_SupportsHDR").boolValue = true;
            Find(p, "m_MSAA").intValue = 1;
            Find(p, "m_RenderScale").floatValue = 1f;
            Find(p, "m_RequireDepthTexture").boolValue = false;
            Find(p, "m_RequireOpaqueTexture").boolValue = false;
            p.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(pipeline);
            return pipeline;
        }

        static void ConfigureQuality(RenderPipelineAsset pipeline)
        {
            int count = QualitySettings.names.Length;
            int current = QualitySettings.GetQualityLevel();
            for (int i = 0; i < count; i++)
            {
                QualitySettings.SetQualityLevel(i, false);
                QualitySettings.renderPipeline = pipeline;
                QualitySettings.vSyncCount = 1; // the showcase caps at the display rate
                QualitySettings.antiAliasing = 0; // URP's asset owns MSAA (off)
            }
            QualitySettings.SetQualityLevel(current, false);
        }

        /// <summary>
        /// The empty scene: an orthographic camera showing the base view (960×540 grid pixels at
        /// 32 per metre: 30×16.875 m), the moon's dim global light, and the test card PX1's stills
        /// draw. Written once (a rewrite would give its objects new file IDs); delete it and run
        /// Configure to rewrite it.
        /// </summary>
        static void EnsureEmptyScene()
        {
            if (!File.Exists(EmptyScenePath)) WriteEmptyScene();
            var scenes = EditorBuildSettings.scenes;
            if (scenes.Length == 0 || scenes[0].path != EmptyScenePath)
                EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(EmptyScenePath, true) };
        }

        static void WriteEmptyScene()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(EmptyScenePath));
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var cam = new GameObject("Main Camera") { tag = "MainCamera" };
            cam.transform.position = new Vector3(0, 0, -10);
            var camera = cam.AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = TestCard.BaseViewHeightM / 2;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.03f, 0.04f, 0.07f);
            camera.nearClipPlane = 0.3f;
            camera.farClipPlane = 50;
            camera.allowMSAA = false;
            cam.AddComponent<UniversalAdditionalCameraData>();
            cam.AddComponent<AudioListener>();

            var moon = new GameObject("Moonlight");
            var global = moon.AddComponent<Light2D>();
            global.lightType = Light2D.LightType.Global;
            global.color = new Color(0.55f, 0.65f, 1f);
            global.intensity = 0.35f;

            var card = new GameObject("Test Card").AddComponent<TestCard>();
            card.litMaterial = AssetDatabase.LoadAssetAtPath<Material>(SpriteLitPath)
                               ?? throw new InvalidOperationException("ProjectSetup: no " + SpriteLitPath);

            EditorSceneManager.SaveScene(scene, EmptyScenePath);
        }

        static SerializedProperty Find(SerializedObject so, string name) =>
            so.FindProperty(name) ?? throw new InvalidOperationException($"ProjectSetup: no property {name} on {so.targetObject.GetType().Name}");
    }
}
