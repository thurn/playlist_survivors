using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Game.Client.Arena
{
    /// <summary>
    /// The arena's night (unity-game.md, "The Look"), demo3d's NightLook copied and owned: ambient and
    /// fog from the lighting, the camera's clear colour, and the night's base post stack (bloom,
    /// neutral tonemapping, colour grading with a lookup table built in code, vignette, white balance
    /// and chromatic aberration at rest). The beat-driven offsets ride on top of these bases
    /// (the look bead, ps-msz.3.7). Nothing is baked or vendor-derived.
    /// </summary>
    public static class NightLook
    {
        public const int LutSize = 32;
        static readonly ArenaLighting Night = new ArenaLighting();
        static readonly Dictionary<(Vector3, Vector3, Vector3), Texture2D> Luts = new Dictionary<(Vector3, Vector3, Vector3), Texture2D>();
        static ArenaLighting _fogLighting;

        public static void ApplyRenderSettings(ArenaLighting l)
        {
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = l.AmbientSky;
            RenderSettings.ambientEquatorColor = l.AmbientEquator;
            RenderSettings.ambientGroundColor = l.AmbientGround;
            RenderSettings.fog = true;
            RenderSettings.fogMode = l.FogMode;
            RenderSettings.fogColor = l.FogColor;
            RenderSettings.fogDensity = l.FogDensity;
            var range = FogRange(l, NominalFocusM);
            RenderSettings.fogStartDistance = range.x;
            RenderSettings.fogEndDistance = range.y;
            _fogLighting = l;
            RenderPipelineManager.beginCameraRendering -= FitFogToCamera;
            if (l.FogMode == FogMode.Linear) RenderPipelineManager.beginCameraRendering += FitFogToCamera;
            RenderSettings.skybox = null;
        }

        /// <summary>The focus distance of a 30 m view at the game camera's pitch, for cameras that look at no ground.</summary>
        public const float NominalFocusM = 31.5f;

        /// <summary>Linear fog's start and end view depths for a camera focused <paramref name="focusM"/> away.</summary>
        public static Vector2 FogRange(ArenaLighting l, float focusM) =>
            new Vector2(l.FogStartAtFocus * focusM, l.FogEndAtFocus * focusM);

        /// <summary>The distance along a camera's view axis to the ground (y = 0), or the nominal focus if it looks at none.</summary>
        public static float FocusDistance(Camera camera)
        {
            var t = camera.transform;
            float down = -t.forward.y;
            return down > 0.05f && t.position.y > 0 ? t.position.y / down : NominalFocusM;
        }

        static void FitFogToCamera(ScriptableRenderContext context, Camera camera)
        {
            if (camera.cameraType != CameraType.Game || _fogLighting == null || RenderSettings.fogMode != FogMode.Linear) return;
            var range = FogRange(_fogLighting, FocusDistance(camera));
            RenderSettings.fogStartDistance = range.x;
            RenderSettings.fogEndDistance = range.y;
        }

        /// <summary>The camera clears to the fog's colour, so the arena's far edge fades into it, and draws the post stack.</summary>
        public static void ApplyCamera(Camera camera, ArenaLighting l)
        {
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = l.FogColor;
            var data = camera.GetComponent<UniversalAdditionalCameraData>();
            if (data != null) data.renderPostProcessing = true;
        }

        /// <summary>A global volume with the night's post stack, under <paramref name="parent"/>.</summary>
        public static Volume CreateVolume(Transform parent, ArenaLighting l)
        {
            var go = new GameObject("NightPost");
            go.transform.SetParent(parent, false);
            var volume = go.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 10;
            volume.sharedProfile = CreateProfile(l);
            return volume;
        }

        public static VolumeProfile CreateProfile(ArenaLighting l)
        {
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            profile.name = "NightPost";
            var bloom = profile.Add<Bloom>(true);
            bloom.threshold.Override(l.BloomThreshold);
            bloom.intensity.Override(l.BloomIntensity);
            bloom.scatter.Override(l.BloomScatter);
            profile.Add<Tonemapping>(true).mode.Override(l.Tonemap == "aces" ? TonemappingMode.ACES : TonemappingMode.Neutral);
            var color = profile.Add<ColorAdjustments>(true);
            color.postExposure.Override(l.PostExposure);
            color.contrast.Override(l.PostContrast);
            color.saturation.Override(l.PostSaturation);
            var lookup = profile.Add<ColorLookup>(true);
            lookup.texture.Override(Lut(l));
            lookup.contribution.Override(1f);
            var vignette = profile.Add<Vignette>(true);
            vignette.intensity.Override(l.VignetteIntensity);
            vignette.smoothness.Override(l.VignetteSmoothness);
            vignette.color.Override(l.VignetteColor);
            profile.Add<WhiteBalance>(true).temperature.Override(l.PostTemperature);
            profile.Add<ChromaticAberration>(true).intensity.Override(0f);
            return profile;
        }

        /// <summary>The grade's LUT for <paramref name="l"/>'s tints (the night's by default) as URP's 2D strip (size² × size, linear texture).</summary>
        public static Texture2D Lut(ArenaLighting l = null)
        {
            l = l ?? Night;
            var tints = (l.GradeShadow, l.GradeMid, l.GradeHigh);
            if (Luts.TryGetValue(tints, out var lut) && lut != null) return lut;
            int n = LutSize;
            var tex = new Texture2D(n * n, n, TextureFormat.RGBAHalf, false, true)
            {
                name = "NightLut", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.DontSave,
            };
            var px = new Color[n * n * n];
            for (int b = 0; b < n; b++)
            for (int g = 0; g < n; g++)
            for (int r = 0; r < n; r++)
                px[g * n * n + b * n + r] = Grade(new Color(r / (n - 1f), g / (n - 1f), b / (n - 1f)), l);
            tex.SetPixels(px);
            tex.Apply(false, false);
            Luts[tints] = tex;
            return tex;
        }

        /// <summary>
        /// The night grade, applied to display-referred (sRGB-encoded) colour after tonemapping: shadows
        /// held near black and leaning a touch cool, with no lift; midtones warmed; highlights near
        /// neutral. Saturated colours (effects, crystal, hit flashes) keep their own hue: the tint fades
        /// out as saturation rises. Then an S-curve on luminance, scaling the channels together so it
        /// adds contrast without turning hues.
        /// </summary>
        public static Color Grade(Color c, ArenaLighting lighting = null)
        {
            lighting = lighting ?? Night;
            float lum = Luma(c.r, c.g, c.b);
            float w = Smooth(0.05f, 0.85f, lum);                 // 0 in shadows, 1 in highlights
            float mid = 4 * w * (1 - w);                         // 1 in the midtones
            var tint = Vector3.Lerp(Vector3.Lerp(lighting.GradeShadow, lighting.GradeHigh, w), lighting.GradeMid, mid);
            float max = Mathf.Max(c.r, Mathf.Max(c.g, c.b)), min = Mathf.Min(c.r, Mathf.Min(c.g, c.b));
            float sat = max > 1e-4f ? (max - min) / max : 0;
            tint = Vector3.Lerp(tint, Vector3.one, Smooth(0.35f, 0.8f, sat));
            float r = c.r * tint.x, g = c.g * tint.y, b = c.b * tint.z;
            float l = Luma(r, g, b);
            float k = l > 1e-5f ? Curve(l) / l : 1;
            return new Color(Mathf.Clamp01(r * k), Mathf.Clamp01(g * k), Mathf.Clamp01(b * k), 1);
        }

        static float Luma(float r, float g, float b) => 0.2126f * r + 0.7152f * g + 0.0722f * b;

        static float Curve(float x)
        {
            x = Mathf.Clamp01(x);
            float s = x * x * (3 - 2 * x);               // smoothstep S-curve
            return Mathf.Clamp01(Mathf.Lerp(x, s, 0.3f));
        }

        static float Smooth(float a, float b, float x)
        {
            float t = Mathf.Clamp01((x - a) / (b - a));
            return t * t * (3 - 2 * t);
        }
    }
}
