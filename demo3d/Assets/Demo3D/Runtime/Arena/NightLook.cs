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
            // `--crowd-fill f`, `--crowd-key k`, `--crowd-rim r,g,b` and `--crowd-flash r,g,b` override them in a player, for tuning against the readability measure.
            float fill = Args.GetFloat("--crowd-fill", l.CrowdFill);
            var rim = new Vector4(l.CrowdRim.r, l.CrowdRim.g, l.CrowdRim.b, l.CrowdRimPower);
            var rimArg = Args.Get("--crowd-rim");
            if (!string.IsNullOrEmpty(rimArg))
            {
                var v = rimArg.Split(',');
                rim = new Vector4(float.Parse(v[0], System.Globalization.CultureInfo.InvariantCulture), float.Parse(v[1], System.Globalization.CultureInfo.InvariantCulture),
                    float.Parse(v[2], System.Globalization.CultureInfo.InvariantCulture), l.CrowdRimPower);
            }
            Shader.SetGlobalFloat("_Demo3DCrowdFill", fill);
            Shader.SetGlobalFloat("_Demo3DCrowdKeyBoost", Args.GetFloat("--crowd-key", l.CrowdKey) - 1);
            Shader.SetGlobalVector("_Demo3DCrowdRim", rim);
            var flash = l.CrowdFlash;
            var flashArg = Args.Get("--crowd-flash");
            if (!string.IsNullOrEmpty(flashArg))
            {
                var v = flashArg.Split(',');
                flash = new Color(float.Parse(v[0], System.Globalization.CultureInfo.InvariantCulture), float.Parse(v[1], System.Globalization.CultureInfo.InvariantCulture),
                    float.Parse(v[2], System.Globalization.CultureInfo.InvariantCulture), 1);
            }
            Shader.SetGlobalVector("_Demo3DCrowdFlash", flash);
            // `--crowd-shoulder k` overrides it, as the other crowd terms.
            Shader.SetGlobalFloat("_Demo3DCrowdShoulder", Args.GetFloat("--crowd-shoulder", l.CrowdShoulder));
        }

        /// <summary>The focus distance of the song's widest view (30 m; camera-and-sound.md), for cameras that look at no ground.</summary>
        public const float NominalFocusM = 31.5f;
        static ArenaLighting _fogLighting;

        /// <summary>Linear fog's start and end view depths for a camera focused <paramref name="focusM"/> away.</summary>
        public static Vector2 FogRange(ArenaLighting l, float focusM) =>
            new Vector2(l.FogStartAtFocus * focusM, l.FogEndAtFocus * focusM);

        /// <summary>The share of fog colour at view depth <paramref name="depthM"/>, as URP's fog computes it.</summary>
        public static float FogWeight(ArenaLighting l, float focusM, float depthM)
        {
            if (l.FogMode == FogMode.Exponential) return 1 - Mathf.Exp(-l.FogDensity * depthM);
            var r = FogRange(l, focusM);
            return Mathf.Clamp01((depthM - r.x) / (r.y - r.x));
        }

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

        /// <summary>The camera clears to the fog's colour, so the arena's far edge fades into it.</summary>
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
            // The bases are the lighting's post (a roster set's look may change them); `--post-exposure`,
            // `--post-contrast`, `--post-saturation`, `--bloom-threshold` and `--tonemap neutral|aces`
            // override them in a player, for tuning against the look set.
            bloom.threshold.Override(Args.GetFloat("--bloom-threshold", l.BloomThreshold));
            bloom.intensity.Override(l.BloomIntensity);
            bloom.scatter.Override(l.BloomScatter);
            profile.Add<Tonemapping>(true).mode.Override(Args.Get("--tonemap", l.Tonemap) == "aces" ? TonemappingMode.ACES : TonemappingMode.Neutral);
            var color = profile.Add<ColorAdjustments>(true);
            color.postExposure.Override(Args.GetFloat("--post-exposure", l.PostExposure));
            color.contrast.Override(Args.GetFloat("--post-contrast", l.PostContrast));
            color.saturation.Override(Args.GetFloat("--post-saturation", l.PostSaturation));
            var lookup = profile.Add<ColorLookup>(true);
            lookup.texture.Override(Lut());
            lookup.contribution.Override(1f);
            var vignette = profile.Add<Vignette>(true);
            // ps-e8r.5: with the hero's light, the vignette darkens the frame's edges around the lit stage.
            vignette.intensity.Override(Args.GetFloat("--vignette", l.VignetteIntensity));
            vignette.smoothness.Override(l.VignetteSmoothness);
            vignette.color.Override(l.VignetteColor);
            // The post feel (ps-e8r.18) adds its white-balance and aberration offsets to these bases
            // (the night's temperature is 0, inert; a roster set's look may cool it).
            profile.Add<WhiteBalance>(true).temperature.Override(l.PostTemperature);
            profile.Add<ChromaticAberration>(true).intensity.Override(0f);
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
        /// The night grade, applied to display-referred (sRGB-encoded) colour after tonemapping (ps-e8r.2,
        /// epic Decision 1: a warm stage with cool accents): shadows held near black and leaning a touch
        /// cool, with no lift; midtones warmed, so the lit ground and the horde read warm; highlights near
        /// neutral. Saturated colours (effects, magic, hit flashes) keep their own hue: the tint fades out
        /// as saturation rises. Then an S-curve on luminance, scaling the channels together so it adds
        /// contrast without turning hues.
        /// </summary>
        public static Color Grade(Color c)
        {
            float lum = Luma(c.r, c.g, c.b);
            float w = Smooth(0.05f, 0.85f, lum);                 // 0 in shadows, 1 in highlights
            float mid = 4 * w * (1 - w);                         // 1 in the midtones
            var shadowTint = new Vector3(0.97f, 0.99f, 1.05f);
            var midTint = new Vector3(1.06f, 1.0f, 0.90f);
            var highTint = new Vector3(1.02f, 1.0f, 0.97f);
            var tint = Vector3.Lerp(Vector3.Lerp(shadowTint, highTint, w), midTint, mid);
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
