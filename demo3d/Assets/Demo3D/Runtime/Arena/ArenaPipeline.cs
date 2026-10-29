using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Demo3D.Runtime.Arena
{
    /// <summary>
    /// The pipeline-wide switches of TD14's rungs, applied before the arena loads:
    /// - E1, the GPU Resident Drawer (measured and dropped by TD14: ProjectSetup.GpuResidentDrawer
    ///   is off, so the asset is stock and a rung asking for the drawer logs an error). With it on,
    ///   the project's URP asset has the drawer on, because a build
    ///   keeps the drawer's resources (its compute kernels) only when a build pipeline asset uses
    ///   it, and ProjectSetup keeps every BatchRendererGroup (DOTS instancing) variant. But every
    ///   player starts on a copy of the asset with the drawer off (<see cref="Boot"/>), so the stock
    ///   baseline and every scene that doesn't ask keep the stock path; a rung that wants the drawer
    ///   switches to the asset itself (or a copy with GPU occlusion culling), and URP recreates the
    ///   pipeline and the drawer before the arena's renderers register.
    /// - E5, the kit's texture cap: the kit's textures import into the <see cref="KitTextureGroup"/>
    ///   mipmap limit group (KitTextureImport, an import override; no vendor file changes), and the
    ///   cap drops that group's top mips, so they never reach the GPU.
    /// </summary>
    public static class ArenaPipeline
    {
        public const string KitTextureGroup = "Kit";
        /// <summary>The size the kit's textures import at (Unity's default maximum).</summary>
        public const int ImportedSize = 2048;

        static UniversalRenderPipelineAsset _drawerOn;   // the project's asset, drawer on
        static UniversalRenderPipelineAsset _drawerOff;  // the copy every player starts on
        static readonly MethodInfo GrdInitialized =
            typeof(GPUResidentDrawer).GetMethod("IsInitialized", BindingFlags.NonPublic | BindingFlags.Static);

        /// <summary>Whether the GPU Resident Drawer is running (null if the player can't tell).</summary>
        public static bool? GrdActive => GrdInitialized != null ? (bool)GrdInitialized.Invoke(null, null) : (bool?)null;

        /// <summary>Starts every player on the drawer-off copy of the project's pipeline asset.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Boot()
        {
            if (Application.isEditor) return; // the editor's play mode keeps the asset (and never measures)
            var asset = QualitySettings.renderPipeline as UniversalRenderPipelineAsset
                        ?? GraphicsSettings.defaultRenderPipeline as UniversalRenderPipelineAsset;
            if (asset == null || asset.gpuResidentDrawerMode == GPUResidentDrawerMode.Disabled) return;
            _drawerOn = asset;
            _drawerOff = Object.Instantiate(asset);
            _drawerOff.name = asset.name + " (GRD off)";
            _drawerOff.gpuResidentDrawerMode = GPUResidentDrawerMode.Disabled;
            QualitySettings.renderPipeline = _drawerOff;
        }

        /// <summary>Applies <paramref name="o"/>'s pipeline-wide switches (the drawer in play mode only).</summary>
        public static void Apply(ArenaOptions o)
        {
            if (Application.isPlaying) ApplyGrd(o.Grd);
            ApplyTextureCap(o.TexCap);
        }

        static void ApplyGrd(string grd)
        {
            if (grd == ArenaOptions.GrdOff)
            {
                if (_drawerOff != null) QualitySettings.renderPipeline = _drawerOff;
                return;
            }
            if (_drawerOn == null)
            {
                Debug.LogError("ArenaPipeline: the project's pipeline asset has the GPU Resident Drawer off, so the build has no drawer (run demo3d unity Configure)");
                return;
            }
            bool occlusion = grd == ArenaOptions.GrdOcclusion;
            var asset = _drawerOn;
            if (asset.gpuResidentDrawerEnableOcclusionCullingInCameras != occlusion)
            {
                asset = Object.Instantiate(_drawerOn);
                asset.name = _drawerOn.name + " (GRD " + grd + ")";
                asset.gpuResidentDrawerEnableOcclusionCullingInCameras = occlusion;
            }
            QualitySettings.renderPipeline = asset;
            Debug.Log($"ArenaPipeline: GPU Resident Drawer on, occlusion culling {(occlusion ? "on" : "off")}");
        }

        /// <summary>Caps the kit's textures at <paramref name="cap"/> px (0: as imported).</summary>
        public static void ApplyTextureCap(int cap)
        {
            int bias = 0;
            if (cap > 0)
                for (int s = ImportedSize; s > cap; s /= 2) bias++;
            QualitySettings.SetTextureMipmapLimitSettings(KitTextureGroup, new TextureMipmapLimitSettings
            {
                limitBiasMode = TextureMipmapLimitBiasMode.OverrideGlobalLimit,
                limitBias = bias,
            });
            if (bias > 0) Debug.Log($"ArenaPipeline: the kit's textures capped at {cap} px (mip limit {bias})");
        }

        /// <summary>Puts back the drawer-off pipeline and the kit's full-size textures.</summary>
        public static void Restore()
        {
            if (Application.isPlaying && _drawerOff != null) QualitySettings.renderPipeline = _drawerOff;
            ApplyTextureCap(0);
        }
    }
}
