using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Demo3D.Runtime.Arena
{
    /// <summary>
    /// The crowd's reflection while its gloss is on (crowd.md, Surfaces; `--no-crowd-gloss` turns it off). The night has no skybox, so the
    /// global probe reflects black and metal swatches show only their direct highlights. Once the arena is
    /// ready this renders a cubemap of it from the arena's centre at eye height (the lit ground, the
    /// braziers, the walls), over a sky of the night's ambient sky colour times `--crowd-reflect-sky`
    /// (from a camera above, most metal faces look up and reflect the sky), and publishes it as
    /// <c>_Demo3DCrowdReflection</c>, which the crowd shader adds as URP's environment specular term.
    /// </summary>
    public static class CrowdReflection
    {
        public const string SkyFlag = "--crowd-reflect-sky";
        /// <summary>The swatches' own metal and gloss, and this reflection, are on unless `--no-crowd-gloss`.</summary>
        public static bool GlossOn => !Args.Has("--no-crowd-gloss");
        const int Size = 128;
        const float DefaultSky = 4f;
        static RenderTexture _cube;

        /// <summary>Captures the arena's reflection unless `--no-crowd-gloss`; otherwise does nothing.</summary>
        public static void Capture(ArenaLighting lighting, Vector3 at)
        {
            if (!GlossOn) return;
            var go = new GameObject("CrowdReflectionCamera");
            var cam = go.AddComponent<Camera>();
            cam.enabled = false;
            cam.transform.position = at;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 400f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            // Scaled in linear; the ambient colours, as the clear colour, are sRGB.
            cam.backgroundColor = (lighting.AmbientSky.linear * Args.GetFloat(SkyFlag, DefaultSky)).gamma;
            cam.allowHDR = true;
            var data = cam.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = false;
            if (_cube == null)
            {
                _cube = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGBHalf)
                {
                    name = "CrowdReflection", dimension = TextureDimension.Cube, useMipMap = true, autoGenerateMips = false,
                };
                _cube.Create();
            }
            bool ok = cam.RenderToCubemap(_cube, 63);
            _cube.GenerateMips();
            if (Application.isPlaying) Object.Destroy(go);
            else Object.DestroyImmediate(go); // an edit-mode test's arena
            Shader.SetGlobalTexture("_Demo3DCrowdReflection", _cube);
            Debug.Log($"CrowdReflection: captured at {at} ({(ok ? "ok" : "RenderToCubemap failed")}), face means {FaceMeans()}");
        }

        /// <summary>Each face's mean colour (linear; +X -X +Y -Y +Z -Z), read back from the smallest mip, for the log.</summary>
        static string FaceMeans()
        {
            int mip = _cube.mipmapCount - 1;
            var req = AsyncGPUReadback.Request(_cube, mip, 0, 1, 0, 1, 0, 6);
            req.WaitForCompletion();
            if (req.hasError) return "(readback failed)";
            var s = new System.Text.StringBuilder();
            for (int f = 0; f < 6; f++)
            {
                NativeArray<ushort> h = req.GetData<ushort>(f);
                s.Append($"({Mathf.HalfToFloat(h[0]):F3}, {Mathf.HalfToFloat(h[1]):F3}, {Mathf.HalfToFloat(h[2]):F3}) ");
            }
            return s.ToString().TrimEnd();
        }
    }
}
