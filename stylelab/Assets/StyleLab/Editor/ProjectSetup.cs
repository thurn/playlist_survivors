using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace StyleLab.EditorTools
{
    /// <summary>
    /// The lab's project configuration: the Mac player (Metal, IL2CPP, 1920×1080 windowed),
    /// linear colour, URP Forward with HDR, MSAA 4x, soft two-cascade shadows, the depth and
    /// opaque textures (the crystal refracts the opaque scene) and SSAO, and the one empty scene
    /// StyleLabMain boots into. Idempotent: run it after changing anything here, and commit the
    /// settings it writes.
    /// </summary>
    public static class ProjectSetup
    {
        public const string SettingsDir = "Assets/StyleLab/Settings";
        public const string PipelinePath = SettingsDir + "/URP-StyleLab.asset";
        public const string RendererPath = SettingsDir + "/URP-StyleLab_Renderer.asset";
        public const string GlobalSettingsPath = "Assets/UniversalRenderPipelineGlobalSettings.asset";
        public const string ScenePath = "Assets/StyleLab/Scenes/StyleLab.unity";

        public static void Configure()
        {
            ConfigurePlayer();
            var pipeline = ConfigurePipeline();
            GraphicsSettings.defaultRenderPipeline = pipeline;
            ConfigureQuality(pipeline);
            ConfigureGraphics();
            EnsureScene();
            AssetDatabase.SaveAssets();
            Debug.Log("ProjectSetup: configured");
        }

        static void ConfigurePlayer()
        {
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.StandaloneOSX, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.StandaloneOSX, new[] { GraphicsDeviceType.Metal });
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone, ScriptingImplementation.IL2CPP);
            PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.productName = "Playlist Survivors Style Lab";
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.defaultIsNativeResolution = false;
            PlayerSettings.defaultScreenWidth = 1920;
            PlayerSettings.defaultScreenHeight = 1080;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.allowFullscreenSwitch = true;
            PlayerSettings.runInBackground = true;
            PlayerSettings.visibleInBackground = true;
            PlayerSettings.meshDeformation = MeshDeformation.GPU;
            PlayerSettings.usePlayerLog = true;
            PlayerSettings.SplashScreen.show = false;
            PlayerSettings.SplashScreen.showUnityLogo = false;
            if (Application.platform == RuntimePlatform.OSXEditor)
                EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Standalone, BuildTarget.StandaloneOSX);
        }

        static UniversalRenderPipelineAsset ConfigurePipeline()
        {
            Directory.CreateDirectory(SettingsDir);
            var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath);
            if (renderer == null)
            {
                renderer = ScriptableObject.CreateInstance<UniversalRendererData>();
                AssetDatabase.CreateAsset(renderer, RendererPath);
            }
            var r = new SerializedObject(renderer);
            Set(r, "m_RenderingMode", (int)RenderingMode.Forward);
            Set(r, "m_DepthPrimingMode", (int)DepthPrimingMode.Disabled);
            Set(r, "m_CopyDepthMode", 0); // after opaques
            r.ApplyModifiedPropertiesWithoutUndo();
            EnsureSsao(renderer);
            EditorUtility.SetDirty(renderer);

            var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(PipelinePath);
            if (pipeline == null)
            {
                pipeline = UniversalRenderPipelineAsset.Create(renderer);
                AssetDatabase.CreateAsset(pipeline, PipelinePath);
            }
            var p = new SerializedObject(pipeline);
            Set(p, "m_SupportsHDR", true);
            Set(p, "m_MSAA", 4);
            Set(p, "m_RenderScale", 1f);
            Set(p, "m_MainLightRenderingMode", 1); // per pixel
            Set(p, "m_MainLightShadowsSupported", true);
            Set(p, "m_MainLightShadowmapResolution", 2048);
            Set(p, "m_AdditionalLightsRenderingMode", 1); // per pixel
            Set(p, "m_AdditionalLightsPerObjectLimit", 8);
            Set(p, "m_AdditionalLightShadowsSupported", false);
            Set(p, "m_ShadowDistance", 60f);
            Set(p, "m_ShadowCascadeCount", 2);
            Set(p, "m_SoftShadowsSupported", true);
            Set(p, "m_RequireDepthTexture", true);
            Set(p, "m_RequireOpaqueTexture", true);
            Set(p, "m_OpaqueDownsampling", 0); // none: the crystal refracts it at full resolution
            p.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(pipeline);
            return pipeline;
        }

        static void ConfigureQuality(RenderPipelineAsset pipeline)
        {
            int current = QualitySettings.GetQualityLevel();
            for (int i = 0; i < QualitySettings.names.Length; i++)
            {
                QualitySettings.SetQualityLevel(i, false);
                QualitySettings.renderPipeline = pipeline;
                QualitySettings.vSyncCount = 1;
                QualitySettings.antiAliasing = 0; // URP's asset owns MSAA
                QualitySettings.skinWeights = SkinWeights.FourBones;
            }
            QualitySettings.SetQualityLevel(current, false);
        }

        /// <summary>
        /// Keeps the exponential-squared fog variants: Stage turns fog on at runtime, so the empty
        /// scene's (fogless) settings would otherwise strip them from the build.
        /// </summary>
        static void ConfigureGraphics()
        {
            var graphics = new SerializedObject(AssetDatabase.LoadMainAssetAtPath("ProjectSettings/GraphicsSettings.asset"));
            Set(graphics, "m_FogStripping", 1); // custom
            Set(graphics, "m_FogKeepLinear", false);
            Set(graphics, "m_FogKeepExp", false);
            Set(graphics, "m_FogKeepExp2", true);
            graphics.ApplyModifiedPropertiesWithoutUndo();
        }

        static void EnsureScene()
        {
            if (!File.Exists(ScenePath))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
                var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                EditorSceneManager.SaveScene(scene, ScenePath);
            }
            var scenes = EditorBuildSettings.scenes;
            if (scenes.Length != 1 || scenes[0].path != ScenePath)
                EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        }

        /// <summary>SSAO from depth-normals at full resolution: the dark contact between packed monsters.</summary>
        static void EnsureSsao(UniversalRendererData renderer)
        {
            var ssao = renderer.rendererFeatures.OfType<ScreenSpaceAmbientOcclusion>().FirstOrDefault();
            if (ssao == null)
            {
                ssao = ScriptableObject.CreateInstance<ScreenSpaceAmbientOcclusion>();
                ssao.name = "SSAO";
                AssetDatabase.AddObjectToAsset(ssao, renderer);
                AssetDatabase.TryGetGUIDAndLocalFileIdentifier(ssao, out string _, out long localId);
                var so = new SerializedObject(renderer);
                var features = so.FindProperty("m_RendererFeatures");
                var map = so.FindProperty("m_RendererFeatureMap");
                features.arraySize++;
                features.GetArrayElementAtIndex(features.arraySize - 1).objectReferenceValue = ssao;
                map.arraySize++;
                map.GetArrayElementAtIndex(map.arraySize - 1).longValue = localId;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
            ssao.SetActive(true);
            var s = new SerializedObject(ssao);
            var settings = s.FindProperty("m_Settings") ?? throw new InvalidOperationException("ProjectSetup: SSAO has no m_Settings");
            settings.FindPropertyRelative("Downsample").boolValue = false;
            settings.FindPropertyRelative("AfterOpaque").boolValue = false;
            settings.FindPropertyRelative("Source").intValue = 1;          // DepthNormals
            settings.FindPropertyRelative("NormalSamples").intValue = 1;   // Medium
            settings.FindPropertyRelative("Intensity").floatValue = 1.6f;
            settings.FindPropertyRelative("DirectLightingStrength").floatValue = 0.25f;
            settings.FindPropertyRelative("Radius").floatValue = 0.35f;
            settings.FindPropertyRelative("Samples").intValue = 1;         // Medium
            settings.FindPropertyRelative("Falloff").floatValue = 100f;
            s.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(ssao);
        }

        static void Set(SerializedObject so, string name, int value)
        {
            var prop = so.FindProperty(name) ?? throw new InvalidOperationException($"ProjectSetup: no property {name} on {so.targetObject.GetType().Name}");
            prop.intValue = value;
        }

        static void Set(SerializedObject so, string name, bool value)
        {
            var prop = so.FindProperty(name) ?? throw new InvalidOperationException($"ProjectSetup: no property {name} on {so.targetObject.GetType().Name}");
            prop.boolValue = value;
        }

        static void Set(SerializedObject so, string name, float value)
        {
            var prop = so.FindProperty(name) ?? throw new InvalidOperationException($"ProjectSetup: no property {name} on {so.targetObject.GetType().Name}");
            prop.floatValue = value;
        }
    }
}
