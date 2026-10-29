using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Demo3D.EditorTools
{
    /// <summary>
    /// Puts the project into the configuration every measurement uses (performance.md, Engine and
    /// Rendering Setup): Direct3D 12 only, IL2CPP, linear color, URP Forward+ with forced depth
    /// priming, HDR, MSAA 4x and soft two-cascade 2048 px sun shadows. Idempotent: run it after
    /// changing anything here, and commit the settings it writes.
    /// </summary>
    public static class ProjectSetup
    {
        public const string SettingsDir = "Assets/Demo3D/Settings";
        public const string PipelinePath = SettingsDir + "/URP-Demo.asset";
        public const string RendererPath = SettingsDir + "/URP-Demo_Renderer.asset";
        public const string EmptyScenePath = "Assets/Demo3D/Scenes/Empty.unity";

        public static void Configure()
        {
            ConfigurePlayer();
            var pipeline = ConfigurePipeline();
            GraphicsSettings.defaultRenderPipeline = pipeline;
            ConfigureQuality(pipeline);
            ConfigureEnvironmentRungs();
            EnsureEmptyScene();
            EnsureBenchMaterials();
            AssetDatabase.SaveAssets();
            Debug.Log("ProjectSetup: configured");
        }

        static void ConfigurePlayer()
        {
            var target = BuildTarget.StandaloneWindows64;
            PlayerSettings.SetUseDefaultGraphicsAPIs(target, false);
            PlayerSettings.SetGraphicsAPIs(target, new[] { GraphicsDeviceType.Direct3D12 });
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone, ScriptingImplementation.IL2CPP);
            PlayerSettings.SetApiCompatibilityLevel(NamedBuildTarget.Standalone, ApiCompatibilityLevel.NET_Standard);
            PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.defaultIsNativeResolution = false;
            PlayerSettings.defaultScreenWidth = 1920;
            PlayerSettings.defaultScreenHeight = 1080;
            PlayerSettings.resizableWindow = false;
            PlayerSettings.allowFullscreenSwitch = false;
            PlayerSettings.runInBackground = true;
            PlayerSettings.visibleInBackground = true;
            PlayerSettings.enableFrameTimingStats = true;
            // The stock baseline skins on the GPU (crowd.md, Stock baseline), batched as Unity 6 does by default.
            PlayerSettings.meshDeformation = MeshDeformation.GPUBatched;
            PlayerSettings.usePlayerLog = true;
            PlayerSettings.SplashScreen.show = false;
            PlayerSettings.SplashScreen.showUnityLogo = false;
            EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Standalone, target);
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
            Set(r, "m_RenderingMode", (int)RenderingMode.ForwardPlus);
            Set(r, "m_DepthPrimingMode", (int)DepthPrimingMode.Forced);
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
            Set(p, "m_AdditionalLightShadowsSupported", false);
            Set(p, "m_ShadowCascadeCount", 2);
            Set(p, "m_SoftShadowsSupported", true);
            Set(p, "m_RequireDepthTexture", true);
            Set(p, "m_RequireOpaqueTexture", false);
            // TD14 (E1): with GpuResidentDrawer the drawer is on in the asset, so the build keeps its
            // resources; every player starts on a drawer-off copy and the arena's rung turns it on
            // (ArenaPipeline). Off (TD14 dropped E1): the stock asset, and --grd refuses.
            Set(p, "m_GPUResidentDrawerMode", GpuResidentDrawer ? 1 : 0); // GPUResidentDrawerMode.InstancedDrawing : Disabled
            Set(p, "m_GPUResidentDrawerEnableOcclusionCullingInCameras", false);
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
                QualitySettings.vSyncCount = 1; // the showcase caps at the display rate; benchmarks turn it off
                QualitySettings.antiAliasing = 0; // URP's asset owns MSAA
                // The arena's lit scenes (TD9): static casters shadow the static set through the
                // baked shadowmask only, so the realtime cascades hold the dynamic casters.
                QualitySettings.shadowmaskMode = ShadowmaskMode.Shadowmask;
            }
            QualitySettings.SetQualityLevel(current, false);
        }

        /// <summary>
        /// TD14's rung E1, the GPU Resident Drawer, in the build. TD14 measured it and dropped it
        /// (plan.md, TD14): on top of E2 it saved nothing, and keeping it costs every lit build
        /// URP Lit's DOTS instancing variants (about 40 minutes of shader compiles more). Set this
        /// to true and run Configure to measure it again (`grd: on` in a sweep, `--grd on`).
        /// </summary>
        public const bool GpuResidentDrawer = false;

        /// <summary>
        /// TD14's rungs that need project settings: with <see cref="GpuResidentDrawer"/>, every
        /// BatchRendererGroup (DOTS instancing) variant kept in the build (E1); the kit's texture
        /// group for E5's cap (KitTextureImport).
        /// </summary>
        static void ConfigureEnvironmentRungs()
        {
            var brg = GpuResidentDrawer ? UnityEditor.Rendering.BatchRendererGroupStrippingMode.KeepAll
                : UnityEditor.Rendering.BatchRendererGroupStrippingMode.KeepIfEntitiesGraphics;
            var graphics = new SerializedObject(AssetDatabase.LoadMainAssetAtPath("ProjectSettings/GraphicsSettings.asset"));
            Set(graphics, "m_BrgStripping", (int)brg);
            graphics.ApplyModifiedPropertiesWithoutUndo();
            if (UnityEditor.Rendering.EditorGraphicsSettings.batchRendererGroupShaderStrippingMode != brg)
                throw new InvalidOperationException("ProjectSetup: BatchRendererGroup stripping isn't " + brg);
            if (!TextureMipmapLimitGroups.HasGroup(Demo3D.Runtime.Arena.ArenaPipeline.KitTextureGroup))
                TextureMipmapLimitGroups.CreateGroup(Demo3D.Runtime.Arena.ArenaPipeline.KitTextureGroup);
        }

        static void EnsureEmptyScene()
        {
            if (!File.Exists(EmptyScenePath))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(EmptyScenePath));
                var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
                EditorSceneManager.SaveScene(scene, EmptyScenePath);
            }
            var scenes = EditorBuildSettings.scenes;
            if (scenes.Length == 0 || scenes[0].path != EmptyScenePath)
                EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(EmptyScenePath, true) };
        }

        /// <summary>
        /// SSAO as a renderer feature (performance.md's post stack, from TD9): depth-normals
        /// source (its prepass primes depth), full resolution, medium samples, a 0.3 m radius.
        /// </summary>
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
            settings.FindPropertyRelative("Intensity").floatValue = 1.2f;
            settings.FindPropertyRelative("DirectLightingStrength").floatValue = 0.25f;
            settings.FindPropertyRelative("Radius").floatValue = 0.3f;
            settings.FindPropertyRelative("Samples").intValue = 1;         // Medium
            settings.FindPropertyRelative("Falloff").floatValue = 100f;
            s.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(ssao);
        }

        public const string BenchDir = "Assets/Demo3D/Resources/Bench";

        /// <summary>
        /// The benchmark player's materials (BenchAssets loads them from Resources, which also puts
        /// their shaders, with the instancing variants they enable, into the build).
        /// </summary>
        static void EnsureBenchMaterials()
        {
            Directory.CreateDirectory(BenchDir);
            var unit = BenchMaterial("BenchUnit", "Universal Render Pipeline/Lit");
            unit.SetColor("_BaseColor", new Color(0.62f, 0.52f, 0.44f));
            unit.SetFloat("_Smoothness", 0.3f);
            var ground = BenchMaterial("BenchGround", "Universal Render Pipeline/Lit");
            ground.SetColor("_BaseColor", new Color(0.30f, 0.34f, 0.26f));
            ground.SetFloat("_Smoothness", 0.1f);
            BenchMaterial("BenchGlow", "Demo3D/BenchGlow");
            BenchMaterial("BenchOverdraw", "Hidden/Demo3D/OverdrawCount");
        }

        static Material BenchMaterial(string name, string shaderName)
        {
            var shader = Shader.Find(shaderName) ?? throw new InvalidOperationException($"ProjectSetup: no shader {shaderName}");
            var path = $"{BenchDir}/{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(shader) { name = name };
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.shader = shader;
            mat.enableInstancing = true;
            EditorUtility.SetDirty(mat);
            return mat;
        }

        static void Set(SerializedObject so, string name, int value)
        {
            var prop = so.FindProperty(name) ?? throw new InvalidOperationException($"ProjectSetup: no property {name} on {so.targetObject.GetType().Name}");
            prop.intValue = value; // an enum's underlying value, not its index
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
