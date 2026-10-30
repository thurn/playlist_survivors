using System.Collections.Generic;
using UnityEngine;

namespace StyleLab.Runtime
{
    /// <summary>A monster material family: one shader (and its shell, if any).</summary>
    public enum Style { Baseline, Painted, Fire, Crystal, Synth, Hologram, Chrome, Void }

    /// <summary>The post stack a treatment wants (Stage.ApplyPost).</summary>
    public struct PostLook
    {
        public bool neutral;   // neutral tonemapping (Demo3D's) rather than ACES
        public float exposure, contrast, saturation, bloom, bloomThreshold, chroma, vignette;

        public static PostLook Stylized(float bloom = 0.9f, float chroma = 0f) => new PostLook
        {
            exposure = 0.25f, contrast = 12f, saturation = 15f, bloom = bloom, bloomThreshold = 1.0f,
            chroma = chroma, vignette = 0.3f,
        };
    }

    public sealed class Treatment
    {
        public string name;
        public string blurb;
        public Style? style; // null: the showcase mix, a style per species
        public PostLook post;
    }

    /// <summary>
    /// The treatments the lab cycles through (keys 1 to 9), and the showcase mix's choice per
    /// species (stylelab.md, The treatments).
    /// </summary>
    public static class Treatments
    {
        public static readonly Treatment[] All =
        {
            new Treatment
            {
                name = "Demo3D today", style = Style.Baseline,
                blurb = "The crowd shader as it is: vendor albedo, PBR at smoothness 0.2, the night's cool rim, neutral tonemapping.",
                post = new PostLook { neutral = true, exposure = 0.35f, contrast = 6f, saturation = 4f, bloom = 1f, bloomThreshold = 0.9f, vignette = 0.25f },
            },
            new Treatment
            {
                name = "Polished painted", style = Style.Painted, post = PostLook.Stylized(),
                blurb = "A colour strip per species, shadows that turn saturated not grey, a lacquer sheen, complementary rims, beat-pulsing glow spots.",
            },
            new Treatment
            {
                name = "Living flame", style = Style.Fire, post = PostLook.Stylized(1.0f),
                blurb = "Charcoal bodies split by molten cracks inside licking additive flames; a fire colour per species; embers rise.",
            },
            new Treatment
            {
                name = "Living crystal", style = Style.Crystal, post = PostLook.Stylized(1.0f),
                blurb = "Faceted gems refracting the ground with dispersion; edges catch the light, facets glint on the beat; death shatters.",
            },
            new Treatment
            {
                name = "Spectrum", style = Style.Synth, post = PostLook.Stylized(1.2f, 0.12f),
                blurb = "Black lacquer with a thin-film rainbow sweeping out from the hero, neon wireframe, equalizer bars, bass swell.",
            },
            new Treatment
            {
                name = "Hologram", style = Style.Hologram, post = PostLook.Stylized(1.35f, 0.35f),
                blurb = "Additive projections: fresnel edges, scanlines, a rolling band, colour fringes, glitch slices on the off-beats.",
            },
            new Treatment
            {
                name = "Liquid chrome", style = Style.Chrome, post = PostLook.Stylized(1.0f),
                blurb = "Mirror metal reflecting a computed synthwave world: sunset, striped sun, studio strips, neon grid. Death melts.",
            },
            new Treatment
            {
                name = "Void", style = Style.Void, post = PostLook.Stylized(1.3f),
                blurb = "Holes cut into the night: a screen-fixed starfield and nebula inside each body, ringed by a flaring edge.",
            },
            new Treatment
            {
                name = "Showcase mix", style = null, post = PostLook.Stylized(1.1f, 0.05f),
                blurb = "Painted fodder; crystal slimes, spectrum bats and fire demons; chrome orks, void trolls and a fire dragon.",
            },
        };

        public static Style StyleFor(int treatment, string typeId)
        {
            var t = All[treatment];
            if (typeId == "hero") return t.style == Style.Baseline ? Style.Baseline : Style.Painted;
            if (t.style.HasValue) return t.style.Value;
            switch (typeId)
            {
                case "slime": return Style.Crystal;
                case "bat": return Style.Synth;
                case "demon": return Style.Fire;
                case "bigork": return Style.Chrome;
                case "troll": return Style.Void;
                case "dragon": return Style.Fire;
                default: return Style.Painted;
            }
        }
    }

    /// <summary>Makes and caches every material: per style, type and albedo texture.</summary>
    public sealed class MaterialBank
    {
        readonly StyleLabData _data;
        readonly Dictionary<(Style, string, Texture2D), Material> _bodies = new Dictionary<(Style, string, Texture2D), Material>();
        readonly Dictionary<(Style, string), Material> _shells = new Dictionary<(Style, string), Material>();
        readonly Dictionary<string, Texture2D> _ramps = new Dictionary<string, Texture2D>();

        public bool Outlines = true;

        public MaterialBank(StyleLabData data) { _data = data; }

        public Material Body(Style style, TypeData type, Texture2D albedo)
        {
            if (_bodies.TryGetValue((style, type.id, albedo), out var m)) return m;
            var look = Look.Of(type.id);
            m = new Material(_data.Shader(ShaderOf(style))) { name = $"{style} {type.id}" };
            if (m.HasProperty("_BaseMap") && albedo != null) m.SetTexture("_BaseMap", albedo);
            switch (style)
            {
                case Style.Painted:
                    m.SetTexture("_Ramp", Ramp(type.id));
                    m.SetVector("_LumRange", type.lumRange);
                    m.SetVector("_ShadowColor", look.shadow);
                    m.SetVector("_RimColor", look.rim);
                    m.SetVector("_SpotColor", look.spot);
                    m.SetFloat("_SpotAmount", look.spotAmount);
                    m.SetFloat("_Gloss", type.id == "hero" ? 64f : 48f);
                    break;
                case Style.Fire:
                    SetFire(m, look);
                    break;
                case Style.Crystal:
                    m.SetVector("_Tint", look.gem);
                    break;
                case Style.Synth:
                    m.SetFloat("_Hue", look.synthHue);
                    break;
                case Style.Hologram:
                    m.SetVector("_Color", look.holo);
                    break;
                case Style.Chrome:
                    m.SetVector("_Tint", look.chrome);
                    break;
                case Style.Void:
                    m.SetVector("_RimColor", look.voidRim);
                    m.SetVector("_NebulaA", look.nebulaA);
                    m.SetVector("_NebulaB", look.nebulaB);
                    break;
            }
            _bodies[(style, type.id, albedo)] = m;
            return m;
        }

        /// <summary>The shell renderer's material: the fire's flames, the painted look's ink line, else null.</summary>
        public Material Shell(Style style, TypeData type)
        {
            if (style == Style.Painted && !Outlines) return null;
            if (style != Style.Fire && style != Style.Painted) return null;
            if (_shells.TryGetValue((style, type.id), out var m)) return m;
            var look = Look.Of(type.id);
            if (style == Style.Fire)
            {
                m = new Material(_data.Shader("StyleLab/FireShell")) { name = "FireShell " + type.id };
                SetFire(m, look);
                m.SetFloat("_Thickness", Mathf.Clamp(0.05f * type.heightM, 0.04f, 0.12f));
                m.SetFloat("_Lick", Mathf.Clamp(0.32f * type.heightM, 0.18f, 0.7f));
                if (type.kind == "boss") m.SetFloat("_Intensity", 0.45f); // its wings are acres of flame
            }
            else
            {
                m = new Material(_data.Shader("StyleLab/Outline")) { name = "Outline " + type.id };
                m.SetVector("_OutlineColor", look.ramp[0] * 0.35f);
                m.SetFloat("_Width", type.id == "hero" ? 0.018f : 0.022f);
            }
            _shells[(style, type.id)] = m;
            return m;
        }

        static void SetFire(Material m, Look look)
        {
            m.SetVector("_Ember", look.ember);
            m.SetVector("_Flame", look.flame);
            m.SetVector("_Core", look.core);
        }

        static string ShaderOf(Style s)
        {
            switch (s)
            {
                case Style.Baseline: return "StyleLab/Baseline";
                case Style.Painted: return "StyleLab/Painted";
                case Style.Fire: return "StyleLab/FireBody";
                case Style.Crystal: return "StyleLab/Crystal";
                case Style.Synth: return "StyleLab/Synth";
                case Style.Hologram: return "StyleLab/Hologram";
                case Style.Chrome: return "StyleLab/Chrome";
                default: return "StyleLab/Void";
            }
        }

        /// <summary>A species' colour strip as a 256×1 linear texture, the stops evenly spaced.</summary>
        public Texture2D Ramp(string id)
        {
            if (_ramps.TryGetValue(id, out var tex)) return tex;
            var stops = Look.Of(id).ramp;
            tex = new Texture2D(256, 1, TextureFormat.RGBAHalf, false, true)
            {
                name = "Ramp " + id, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear,
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
            _ramps[id] = tex;
            return tex;
        }
    }
}
