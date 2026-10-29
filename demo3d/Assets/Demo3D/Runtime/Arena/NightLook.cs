using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Demo3D.Runtime.Arena
{
    /// <summary>
    /// The arena's night look: ambient and fog from the lighting parameters, and the post stack of
    /// performance.md's Engine and Rendering Setup (bloom, neutral tonemapping, colour grading
    /// with the night LUT, vignette; SSAO is the renderer's feature, set by ProjectSetup). Built
    /// in code, the LUT included, so nothing is baked or vendor-derived.
    /// </summary>
    public static class NightLook
    {
        public const int LutSize = 32;
        static Texture2D _lut;

        public static void ApplyRenderSettings(ArenaLighting l)
        {
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = l.AmbientSky;
            RenderSettings.ambientEquatorColor = l.AmbientEquator;
            RenderSettings.ambientGroundColor = l.AmbientGround;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Exponential;
            RenderSettings.fogColor = l.FogColor;
            RenderSettings.fogDensity = l.FogDensity;
            RenderSettings.skybox = null;
        }

        /// <summary>The camera clears to the fog's colour, so the arena's far edge fades into it.</summary>
        public static void ApplyCamera(Camera camera, ArenaLighting l)
        {
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = l.FogColor;
            var data = camera.GetComponent<UniversalAdditionalCameraData>();
            if (data != null) data.renderPostProcessing = true;
        }

        /// <summary>A global volume with the night's post stack, under <paramref name="parent"/>.</summary>
        public static Volume CreateVolume(Transform parent)
        {
            var go = new GameObject("NightPost");
            go.transform.SetParent(parent, false);
            var volume = go.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 10;
            volume.sharedProfile = CreateProfile();
            return volume;
        }

        public static VolumeProfile CreateProfile()
        {
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            profile.name = "NightPost";
            var bloom = profile.Add<Bloom>(true);
            bloom.threshold.Override(0.9f);
            bloom.intensity.Override(1.0f);
            bloom.scatter.Override(0.65f);
            profile.Add<Tonemapping>(true).mode.Override(TonemappingMode.Neutral);
            var color = profile.Add<ColorAdjustments>(true);
            color.postExposure.Override(0.35f);
            color.contrast.Override(6f);
            color.saturation.Override(4f);
            var lookup = profile.Add<ColorLookup>(true);
            lookup.texture.Override(Lut());
            lookup.contribution.Override(1f);
            var vignette = profile.Add<Vignette>(true);
            vignette.intensity.Override(0.26f);
            vignette.smoothness.Override(0.45f);
            vignette.color.Override(new Color(0.01f, 0.015f, 0.04f));
            return profile;
        }

        /// <summary>The night LUT as URP's 2D strip (size² × size, linear texture).</summary>
        public static Texture2D Lut()
        {
            if (_lut != null) return _lut;
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
                px[g * n * n + b * n + r] = Grade(new Color(r / (n - 1f), g / (n - 1f), b / (n - 1f)));
            tex.SetPixels(px);
            tex.Apply(false, false);
            _lut = tex;
            return tex;
        }

        /// <summary>
        /// The night grade, applied to display-referred (sRGB-encoded) colour after tonemapping:
        /// shadows pulled toward blue-violet and lifted a touch, midtones cooled, highlights left
        /// warm and saturated so combat effects (warm, white-hot) stand apart from the cool set
        /// (environment.md, Color separation), and a gentle S-curve for contrast.
        /// </summary>
        public static Color Grade(Color c)
        {
            float lum = 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b;
            float w = Smooth(0.05f, 0.85f, lum);                 // 0 in shadows, 1 in highlights
            var shadowTint = new Vector3(0.90f, 0.97f, 1.12f);
            var highTint = new Vector3(1.03f, 1.0f, 0.95f);
            var tint = Vector3.Lerp(shadowTint, highTint, w);
            float lift = (1 - w) * (1 - w);
            float r = c.r * tint.x + lift * 0.004f, g = c.g * tint.y + lift * 0.008f, b = c.b * tint.z + lift * 0.022f;
            // Highly saturated colours (effects, hit flashes) keep their own hue: blend back toward the input.
            float max = Mathf.Max(c.r, Mathf.Max(c.g, c.b)), min = Mathf.Min(c.r, Mathf.Min(c.g, c.b));
            float sat = max > 1e-4f ? (max - min) / max : 0;
            float keep = Smooth(0.55f, 0.95f, sat) * w;
            r = Mathf.Lerp(r, c.r, keep); g = Mathf.Lerp(g, c.g, keep); b = Mathf.Lerp(b, c.b, keep);
            return new Color(Curve(r), Curve(g), Curve(b), 1);
        }

        static float Curve(float x)
        {
            x = Mathf.Clamp01(x);
            float s = x * x * (3 - 2 * x);               // smoothstep S-curve
            return Mathf.Clamp01(Mathf.Lerp(x, s, 0.18f));
        }

        static float Smooth(float a, float b, float x)
        {
            float t = Mathf.Clamp01((x - a) / (b - a));
            return t * t * (3 - 2 * t);
        }
    }
}
