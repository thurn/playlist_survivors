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

        /// <summary>Every name the flag takes (the epic's nine); mix is the showcase mix, a style per type.</summary>
        public static readonly string[] Names = { "baseline", "painted", "fire", "crystal", "spectrum", "hologram", "chrome", "void", "mix" };

        public readonly string Name;
        public bool IsBaseline => Name == "baseline";

        /// <summary>
        /// Whether the style draws the unwelded style mesh (CrowdStyleMeshes) rather than the
        /// welded one: its triangles move alone (shatter), or it reads each one's barycentrics
        /// (facets, wireframe).
        /// </summary>
        public bool UsesStyleMesh => Name == "crystal" || Name == "spectrum" || Name == "hologram";

        /// <summary>
        /// Whether the style reads the camera's opaque texture (crystal's refraction). URP's asset
        /// leaves it off, so only this style's camera pays for the copy (DemoMain).
        /// </summary>
        public bool NeedsOpaqueTexture => Name == "crystal";

        readonly List<UnityEngine.Object> _owned = new List<UnityEngine.Object>();
        readonly Dictionary<string, Texture2D> _ramps = new Dictionary<string, Texture2D>();

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

        /// <summary>The style command-line arguments ask for: `--monster-style name`, else baseline.</summary>
        public static MonsterStyle FromArgs(IReadOnlyList<string> args)
        {
            for (int i = 0; i < args.Count; i++)
            {
                if (args[i] != Flag) continue;
                if (i + 1 >= args.Count || args[i + 1].StartsWith("--", StringComparison.Ordinal))
                    throw new ArgumentException($"{Flag} needs a style (one of {string.Join(", ", Names)})");
                return Parse(args[i + 1]);
            }
            return Baseline();
        }

        public static MonsterStyle FromCommandLine() => FromArgs(Environment.GetCommandLineArgs());

        /// <summary>
        /// The post stack the style wants as PostFeel's base (null: the night's own). From
        /// stylelab's Treatments.cs; the crowd-only knobs of the look pass stay the baseline's.
        /// </summary>
        public PostLook? Post
        {
            get
            {
                switch (Name)
                {
                    case "baseline": return null;
                    case "painted": return PostLook.Stylized();
                    case "fire": return PostLook.Stylized(1.0f);
                    case "chrome": return PostLook.Stylized(1.0f);
                    case "void": return PostLook.Stylized(1.3f);
                    case "crystal": return PostLook.Stylized(1.0f);
                    case "spectrum": return PostLook.Stylized(1.2f, 0.12f);
                    case "hologram": return PostLook.Stylized(1.35f, 0.35f);
                    default: throw NotPorted();
                }
            }
        }

        /// <summary>A type's materials, one per submesh: the baked ones under baseline, else this style's.</summary>
        public Material[] Materials(CrowdLibrary lib, CrowdType type)
        {
            if (IsBaseline) return type.Materials;
            var mats = new Material[type.Materials.Length];
            for (int k = 0; k < mats.Length; k++)
            {
                var baked = type.Materials[k];
                var m = new Material(lib.StyleShader(ShaderOf(Name))) { name = $"{type.Id}_{Name}{k}", enableInstancing = false };
                m.SetTexture("_BaseMap", baked.GetTexture("_BaseMap"));
                m.SetColor("_BaseColor", baked.GetColor("_BaseColor"));
                m.SetFloat("_HeightM", type.HeightM);
                var look = StyleLook.Of(type.Id);
                switch (Name)
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
            if (Name != "fire" && !(Name == "painted" && Outlines)) return null;
            var look = StyleLook.Of(type.Id);
            Material m;
            if (Name == "fire")
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
            _owned.Add(m);
            return m;
        }

        static void SetFire(Material m, StyleLook look)
        {
            m.SetVector("_Ember", look.ember);
            m.SetVector("_Flame", look.flame);
            m.SetVector("_Core", look.core);
        }

        string ShaderOf(string name)
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
                default: throw NotPorted();
            }
        }

        Exception NotPorted() => new NotSupportedException($"{Flag} {Name}: not ported to demo3d yet (epic ps-a54)");

        /// <summary>A type's colour strip as a 256×1 linear texture, the stops evenly spaced.</summary>
        Texture2D Ramp(string id)
        {
            if (_ramps.TryGetValue(id, out var tex)) return tex;
            var stops = StyleLook.Of(id).ramp;
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
            _owned.Add(tex);
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

        public static PostLook Stylized(float bloom = 0.9f, float chroma = 0f) => new PostLook
        {
            exposure = 0.25f, contrast = 12f, saturation = 15f, bloom = bloom, bloomThreshold = 1.0f,
            chroma = chroma, vignette = 0.3f,
        };

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
