using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Demo3D.Runtime.Crowd
{
    /// <summary>
    /// The horde's treatment, `--monster-style <name>` (crowd.md, Monster styles): stylelab's
    /// monster looks drawn through the bone-texture crowd's indirect draws. baseline (the default)
    /// is the baked CrowdSkinned materials and the night's post, unchanged; any other style gives
    /// every type per-submesh materials of its shader (Shaders/Styles/) and its post look, and
    /// needs <see cref="StyleBeat"/> publishing the beat. It owns the materials and textures it
    /// makes.
    /// </summary>
    public sealed class MonsterStyle : IDisposable
    {
        public const string Flag = "--monster-style";

        /// <summary>
        /// `--style-tune key=value,...`: overrides the tuned values for a run, to judge a change
        /// without a rebuild. Keys: gain and peak (every type's <see cref="Gain"/> and
        /// <see cref="Peak"/>) and the post look's exposure, contrast, saturation, bloom,
        /// threshold, chroma, vignette and neutral (0 or 1).
        /// </summary>
        public const string TuneFlag = "--style-tune";

        /// <summary>Every name the flag takes (the epic's nine); mix is the showcase mix, a style per type.</summary>
        public static readonly string[] Names = { "baseline", "painted", "fire", "crystal", "spectrum", "hologram", "chrome", "void", "mix" };

        public readonly string Name;
        public bool IsBaseline => Name == "baseline";

        /// <summary>
        /// The single style a type draws in: the style itself, or under mix the showcase's choice
        /// (stylelab's Treatments.StyleFor): painted fodder; crystal slimes, spectrum bats and fire
        /// demons; chrome orks, void trolls and a fire dragon.
        /// </summary>
        public string StyleOf(string typeId)
        {
            if (Name != "mix") return Name;
            switch (typeId)
            {
                case "slime": return "crystal";
                case "bat": return "spectrum";
                case "demon": return "fire";
                case "bigork": return "chrome";
                case "troll": return "void";
                case "dragon": return "fire";
                default: return "painted";
            }
        }

        /// <summary>
        /// Whether a type draws the unwelded style mesh (CrowdStyleMeshes) rather than the welded
        /// one: its triangles move alone (shatter), or it reads each one's barycentrics (facets,
        /// wireframe).
        /// </summary>
        public bool UsesStyleMesh(string typeId)
        {
            var s = StyleOf(typeId);
            return s == "crystal" || s == "spectrum" || s == "hologram";
        }

        /// <summary>
        /// Whether some type reads the camera's opaque texture (crystal's refraction, the mix's
        /// slimes). URP's asset leaves it off, so only these styles' camera pays for the copy (DemoMain).
        /// </summary>
        public bool NeedsOpaqueTexture => Name == "crystal" || Name == "mix";

        readonly List<UnityEngine.Object> _owned = new List<UnityEngine.Object>();
        readonly Dictionary<string, Texture2D> _ramps = new Dictionary<string, Texture2D>();

        readonly Dictionary<string, float> _tune = new Dictionary<string, float>();
        /// <summary>The tune keys, `--style-tune`'s and `--structure-tune`'s (<see cref="StructureStyle"/>).</summary>
        internal static readonly string[] TuneKeys = { "gain", "peak", "exposure", "contrast", "saturation", "bloom", "threshold", "chroma", "vignette", "neutral" };

        MonsterStyle(string name) { Name = name; }

        public static MonsterStyle Baseline() => new MonsterStyle("baseline");

        /// <summary>A style by name (null: baseline). Any other name fails, listing the nine.</summary>
        public static MonsterStyle Parse(string name)
        {
            if (name == null) return Baseline();
            if (Array.IndexOf(Names, name) < 0)
                throw new ArgumentException($"{Flag}: unknown style '{name}' (one of {string.Join(", ", Names)})");
            return new MonsterStyle(name);
        }

        /// <summary>The style command-line arguments ask for: `--monster-style name`, else baseline; and any `--style-tune`.</summary>
        public static MonsterStyle FromArgs(IReadOnlyList<string> args)
        {
            MonsterStyle style = null;
            string tune = null;
            for (int i = 0; i < args.Count; i++)
            {
                if (args[i] != Flag && args[i] != TuneFlag) continue;
                if (i + 1 >= args.Count || args[i + 1].StartsWith("--", StringComparison.Ordinal))
                    throw new ArgumentException(args[i] == Flag
                        ? $"{Flag} needs a style (one of {string.Join(", ", Names)})"
                        : $"{TuneFlag} needs key=value pairs (keys {string.Join(", ", TuneKeys)})");
                if (args[i] == Flag) style = Parse(args[i + 1]);
                else tune = args[i + 1];
            }
            style ??= Baseline();
            if (tune != null)
            {
                if (style.IsBaseline) throw new ArgumentException($"{TuneFlag} tunes a {Flag} treatment; baseline has none");
                style.Tune(tune);
            }
            return style;
        }

        /// <summary>Applies `--style-tune` pairs ("gain=0.6,exposure=0.5"); an unknown key or bad number fails.</summary>
        public void Tune(string pairs) => ParseTune(TuneFlag, pairs, _tune);

        /// <summary>Reads a tune flag's pairs into <paramref name="into"/>; an unknown key or bad number fails.</summary>
        internal static void ParseTune(string flag, string pairs, Dictionary<string, float> into)
        {
            foreach (var pair in pairs.Split(','))
            {
                var kv = pair.Split('=');
                if (kv.Length != 2 || Array.IndexOf(TuneKeys, kv[0].Trim()) < 0
                    || !float.TryParse(kv[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float v))
                    throw new ArgumentException($"{flag}: bad pair '{pair}' (keys {string.Join(", ", TuneKeys)})");
                into[kv[0].Trim()] = v;
            }
        }

        /// <summary>
        /// The scale on a type's forward colour (the shaders' _Gain, SL_Grade), from the tuning
        /// pass in demo3d's scenes (evidence/styles/all/README.md).
        /// </summary>
        public float Gain(string typeId)
        {
            if (_tune.TryGetValue("gain", out float g)) return g;
            switch (StyleOf(typeId))
            {
                case "painted": return 2f;    // its ramp reads dull under the night's grade; the peak holds the highlights
                case "chrome": return 0.5f;   // dark mirrors against the lit ground: brighter, they matched it (|dL*| near 0)
                case "void": return 0.5f;     // likewise, a hole in the ground
                default: return 1f;
            }
        }

        /// <summary>
        /// The soft limit on a type's luminance after the gain (the shaders' _Peak,
        /// SL_Grade): it keeps the effects the brightest values on screen over the self-lit
        /// treatments. From the same tuning pass. The hit flash is graded too, so it is capped
        /// with the rest; the shaders' default, 1000, is in effect no limit.
        /// </summary>
        public float Peak(string typeId)
        {
            if (_tune.TryGetValue("peak", out float p)) return p;
            switch (StyleOf(typeId))
            {
                case "fire": return 0.15f;    // dense additive flames, the pale ones the hottest
                case "hologram": return 0.1f; // additive too, and overlapping holograms sum
                case "crystal":
                case "spectrum": return 0.3f; // facet glints and wireframe
                default: return 0.4f;
            }
        }

        public static MonsterStyle FromCommandLine() => FromArgs(Environment.GetCommandLineArgs());

        /// <summary>
        /// The post stack the style wants as PostFeel's base (null: the night's own). From
        /// stylelab's Treatments.cs; the crowd-only knobs of the look pass stay the baseline's.
        /// </summary>
        public PostLook? Post => IsBaseline ? (PostLook?)null : PostOf(Name).With(_tune);

        /// <summary>A style's own post look, before any tuning (also a structure style's, <see cref="StructureStyle.Post"/>).</summary>
        internal static PostLook PostOf(string name)
        {
            switch (name)
            {
                case "painted": return PostLook.Stylized();
                case "fire": return PostLook.Stylized(1.0f);
                case "chrome": return PostLook.Stylized(1.0f);
                case "void": return PostLook.Stylized(1.3f);
                case "crystal": return PostLook.Stylized(1.0f);
                case "spectrum": return PostLook.Stylized(1.2f, 0.12f);
                case "hologram": return PostLook.Stylized(1.35f, 0.35f);
                case "mix": return PostLook.Stylized(1.1f, 0.05f);
                default: throw new InvalidOperationException(name);
            }
        }

        /// <summary>A type's materials, one per submesh: the baked ones under baseline, else this style's.</summary>
        public Material[] Materials(CrowdLibrary lib, CrowdType type)
        {
            if (IsBaseline) return type.Materials;
            var style = StyleOf(type.Id);
            var mats = new Material[type.Materials.Length];
            for (int k = 0; k < mats.Length; k++)
            {
                var baked = type.Materials[k];
                var m = new Material(lib.StyleShader(ShaderOf(style))) { name = $"{type.Id}_{style}{k}", enableInstancing = false };
                m.SetTexture("_BaseMap", baked.GetTexture("_BaseMap"));
                m.SetColor("_BaseColor", baked.GetColor("_BaseColor"));
                m.SetFloat("_HeightM", type.HeightM);
                m.SetFloat("_Gain", Gain(type.Id));
                m.SetFloat("_Peak", Peak(type.Id));
                var look = StyleLook.Of(type.Id);
                switch (style)
                {
                    case "painted":
                        m.SetTexture("_Ramp", Ramp(type.Id));
                        m.SetVector("_LumRange", type.ValueRange);
                        m.SetVector("_ShadowColor", look.shadow);
                        m.SetVector("_RimColor", look.rim);
                        m.SetVector("_SpotColor", look.spot);
                        m.SetFloat("_SpotAmount", look.spotAmount);
                        m.SetFloat("_Gloss", 48f);
                        break;
                    case "fire":
                        SetFire(m, look);
                        break;
                    case "chrome":
                        m.SetVector("_Tint", look.chrome);
                        break;
                    case "void":
                        m.SetVector("_RimColor", look.voidRim);
                        m.SetVector("_NebulaA", look.nebulaA);
                        m.SetVector("_NebulaB", look.nebulaB);
                        break;
                    case "crystal":
                        m.SetVector("_Tint", look.gem);
                        break;
                    case "spectrum":
                        m.SetFloat("_Hue", look.synthHue);
                        break;
                    case "hologram":
                        m.SetVector("_Color", look.holo);
                        break;
                }
                _owned.Add(m);
                mats[k] = m;
            }
            return mats;
        }

        /// <summary>Whether painted draws its ink line (on by default, as in the lab).</summary>
        public bool Outlines = true;

        /// <summary>
        /// A type's shell material (fire's flames, painted's ink line), else null: BoneTextureCrowd
        /// draws it as a second indirect draw per submesh and LOD, on the same instances and args.
        /// </summary>
        public Material Shell(CrowdLibrary lib, CrowdType type)
        {
            var style = StyleOf(type.Id);
            if (style != "fire" && !(style == "painted" && Outlines)) return null;
            var look = StyleLook.Of(type.Id);
            Material m;
            if (style == "fire")
            {
                m = new Material(lib.StyleShader("Demo3D/Styles/Shells/FireShell")) { name = $"{type.Id}_fireShell", enableInstancing = false };
                SetFire(m, look);
                m.SetFloat("_Thickness", Mathf.Clamp(0.05f * type.HeightM, 0.04f, 0.12f));
                m.SetFloat("_Lick", Mathf.Clamp(0.32f * type.HeightM, 0.18f, 0.7f));
                if (type.Kind == "boss") m.SetFloat("_Intensity", 0.45f); // the dragon's wings are acres of flame
            }
            else
            {
                m = new Material(lib.StyleShader("Demo3D/Styles/Shells/Outline")) { name = $"{type.Id}_outline", enableInstancing = false };
                m.SetVector("_OutlineColor", look.ramp[0] * 0.35f);
                m.SetFloat("_Width", 0.022f);
            }
            m.SetFloat("_HeightM", type.HeightM);
            m.SetFloat("_Gain", Gain(type.Id));
            m.SetFloat("_Peak", Peak(type.Id));
            _owned.Add(m);
            return m;
        }

        static void SetFire(Material m, StyleLook look)
        {
            m.SetVector("_Ember", look.ember);
            m.SetVector("_Flame", look.flame);
            m.SetVector("_Core", look.core);
        }

        static string ShaderOf(string name)
        {
            switch (name)
            {
                case "painted": return "Demo3D/Styles/Painted";
                case "fire": return "Demo3D/Styles/FireBody";
                case "chrome": return "Demo3D/Styles/Chrome";
                case "void": return "Demo3D/Styles/Void";
                case "crystal": return "Demo3D/Styles/Crystal";
                case "spectrum": return "Demo3D/Styles/Spectrum";
                case "hologram": return "Demo3D/Styles/Hologram";
                default: throw new InvalidOperationException(name);
            }
        }

        /// <summary>A type's colour strip (<see cref="RampTexture"/>), made once per type.</summary>
        Texture2D Ramp(string id)
        {
            if (_ramps.TryGetValue(id, out var tex)) return tex;
            tex = RampTexture(StyleLook.Of(id).ramp, "Ramp " + id);
            _ramps[id] = tex;
            _owned.Add(tex);
            return tex;
        }

        /// <summary>A colour strip as a 256×1 linear texture, the stops evenly spaced; the caller owns it.</summary>
        internal static Texture2D RampTexture(Color[] stops, string name)
        {
            var tex = new Texture2D(256, 1, TextureFormat.RGBAHalf, false, true)
            {
                name = name, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear,
            };
            var px = new Color[256];
            for (int i = 0; i < 256; i++)
            {
                float t = i / 255f * (stops.Length - 1);
                int a = Mathf.Min((int)t, stops.Length - 2);
                px[i] = Color.Lerp(stops[a], stops[a + 1], Mathf.SmoothStep(0, 1, t - a));
            }
            tex.SetPixels(px);
            tex.Apply(false, true);
            return tex;
        }

        public void Dispose()
        {
            foreach (var o in _owned)
                if (o != null)
                {
                    if (Application.isPlaying) UnityEngine.Object.Destroy(o);
                    else UnityEngine.Object.DestroyImmediate(o);
                }
            _owned.Clear();
            _ramps.Clear();
        }
    }

    /// <summary>
    /// A style's post stack (stylelab's PostLook): written over the night volume's values before
    /// PostFeel reads them, so its beat and section offsets ride on top.
    /// </summary>
    public struct PostLook
    {
        public bool neutral;   // neutral tonemapping (the night's) rather than ACES
        public float exposure, contrast, saturation, bloom, bloomThreshold, chroma, vignette;

        /// <summary>
        /// The lab's stylized grade with its per-treatment bloom and chroma, retuned for demo3d's
        /// arena (ps-a54.4, evidence/styles/all/README.md). The lab's ACES at exposure 0.25,
        /// contrast 12 and saturation 15 left a quarter to two thirds of demo3d's frame near
        /// black, so the tonemapper, exposure and bloom threshold are the night's (neutral, 0.8,
        /// 1.5), with contrast and saturation between the two. At the night's threshold only the
        /// hottest effects bloom, so the treatment's bloom intensity changes little.
        /// </summary>
        public static PostLook Stylized(float bloom = 0.9f, float chroma = 0f) => new PostLook
        {
            neutral = true, exposure = 0.8f, contrast = 20f, saturation = 25f, bloom = bloom, bloomThreshold = 1.5f,
            chroma = chroma, vignette = 0.3f,
        };

        /// <summary>A profile's current values as a look (the night's, for a structure style to build on).</summary>
        public static PostLook From(VolumeProfile profile)
        {
            var look = new PostLook { neutral = true };
            if (profile.TryGet(out Bloom bloom))
            {
                look.bloomThreshold = bloom.threshold.value;
                look.bloom = bloom.intensity.value;
            }
            if (profile.TryGet(out Tonemapping tonemap)) look.neutral = tonemap.mode.value != TonemappingMode.ACES;
            if (profile.TryGet(out ColorAdjustments color))
            {
                look.exposure = color.postExposure.value;
                look.contrast = color.contrast.value;
                look.saturation = color.saturation.value;
            }
            if (profile.TryGet(out Vignette v)) look.vignette = v.intensity.value;
            if (profile.TryGet(out ChromaticAberration c)) look.chroma = c.intensity.value;
            return look;
        }

        /// <summary>This look with a tune flag's post keys (exposure, contrast, saturation, bloom, threshold, chroma, vignette, neutral) over it.</summary>
        public PostLook With(IReadOnlyDictionary<string, float> tune)
        {
            var look = this;
            if (tune.TryGetValue("exposure", out float v)) look.exposure = v;
            if (tune.TryGetValue("contrast", out v)) look.contrast = v;
            if (tune.TryGetValue("saturation", out v)) look.saturation = v;
            if (tune.TryGetValue("bloom", out v)) look.bloom = v;
            if (tune.TryGetValue("threshold", out v)) look.bloomThreshold = v;
            if (tune.TryGetValue("chroma", out v)) look.chroma = v;
            if (tune.TryGetValue("vignette", out v)) look.vignette = v;
            if (tune.TryGetValue("neutral", out v)) look.neutral = v > 0.5f;
            return look;
        }

        /// <summary>Overrides the profile's grade; its LUT, vignette shape and white balance stay the night's.</summary>
        public void ApplyTo(VolumeProfile profile)
        {
            if (profile.TryGet(out Bloom bloom))
            {
                bloom.threshold.Override(bloomThreshold);
                bloom.intensity.Override(this.bloom);
            }
            if (profile.TryGet(out Tonemapping tonemap)) tonemap.mode.Override(neutral ? TonemappingMode.Neutral : TonemappingMode.ACES);
            if (profile.TryGet(out ColorAdjustments color))
            {
                color.postExposure.Override(exposure);
                color.contrast.Override(contrast);
                color.saturation.Override(saturation);
            }
            if (profile.TryGet(out Vignette v)) v.intensity.Override(vignette);
            if (profile.TryGet(out ChromaticAberration c)) c.intensity.Override(chroma);
        }
    }
}
