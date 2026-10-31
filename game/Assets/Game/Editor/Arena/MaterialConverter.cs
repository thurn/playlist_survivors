using System;
using System.Collections.Generic;
using System.Linq;
using PS.Game.Arena;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Game.EditorTools.Arena
{
    /// <summary>
    /// The kit's materials converted to URP and saved under the gitignored Baked/ (demo3d's
    /// MaterialConverter, copied and owned): Standard to Game/LitStaticShadow (URP Lit's inputs as
    /// Unity's converter maps them: albedo, colour, normal map, metallic-gloss map, emission, alpha
    /// clip; lit by probes, with the static shadow map), the candles' flame particles to URP's unlit
    /// additive particles, and Elven Realm's triplanar rock to Game/RockTriplanar.
    /// </summary>
    public sealed class MaterialConverter
    {
        readonly string _dir;
        readonly JsonValue _kitValue;
        readonly Dictionary<Material, Material> _done = new Dictionary<Material, Material>();
        public int Count => _done.Count;

        /// <summary>Converts into <paramref name="dir"/>, scaling base colours by the layout's <c>lighting.kitValue</c>.</summary>
        public MaterialConverter(string dir, JsonValue kitValue)
        {
            _dir = dir;
            _kitValue = kitValue;
        }

        public void Convert(GameObject go)
        {
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < mats.Length; i++)
                {
                    if (mats[i] == null) continue;
                    var c = Converted(mats[i]);
                    if (c != mats[i]) { mats[i] = c; changed = true; }
                }
                if (changed) r.sharedMaterials = mats;
            }
        }

        Material Converted(Material m)
        {
            if (_done.TryGetValue(m, out var c)) return c;
            var path = $"{_dir}/{Safe(m.name)}.mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            c = Build(m, existing, _kitValue);
            if (existing == null) AssetDatabase.CreateAsset(c, path);
            else EditorUtility.SetDirty(c);
            _done[m] = c;
            return c;
        }

        static Material Build(Material m, Material into, JsonValue kitValue)
        {
            string shader = m.shader != null ? m.shader.name : "";
            if (shader.StartsWith("Particles/")) return Particle(m, into);
            if (shader == "SyntyStudios_RockTriplanar") return Triplanar(m, into, kitValue);
            var lit = Target(into, "Game/LitStaticShadow", m.name);
            var tex = m.HasProperty("_MainTex") ? m.GetTexture("_MainTex") : null;
            lit.SetTexture("_BaseMap", tex);
            lit.SetTextureScale("_BaseMap", m.HasProperty("_MainTex") ? m.GetTextureScale("_MainTex") : Vector2.one);
            lit.SetColor("_BaseColor", ArenaDressing.KitValue(kitValue, m.HasProperty("_Color") ? m.GetColor("_Color") : Color.white, m.name));
            float gloss = m.HasProperty("_Glossiness") ? m.GetFloat("_Glossiness") : 0.5f;
            var mg = m.HasProperty("_MetallicGlossMap") ? m.GetTexture("_MetallicGlossMap") : null;
            if (mg != null && m.IsKeywordEnabled("_METALLICGLOSSMAP"))
            {
                lit.SetTexture("_MetallicGlossMap", mg);
                lit.EnableKeyword("_METALLICSPECGLOSSMAP");
                lit.SetFloat("_Smoothness", m.HasProperty("_GlossMapScale") ? m.GetFloat("_GlossMapScale") : 1f);
            }
            else
            {
                lit.SetFloat("_Smoothness", gloss);
                lit.SetFloat("_Metallic", m.HasProperty("_Metallic") ? m.GetFloat("_Metallic") : 0f);
            }
            var bump = m.HasProperty("_BumpMap") ? m.GetTexture("_BumpMap") : null;
            if (bump != null)
            {
                lit.SetTexture("_BumpMap", bump);
                lit.SetFloat("_BumpScale", m.HasProperty("_BumpScale") ? m.GetFloat("_BumpScale") : 1f);
                lit.EnableKeyword("_NORMALMAP");
            }
            if (m.IsKeywordEnabled("_EMISSION") && m.HasProperty("_EmissionColor"))
            {
                var e = m.GetColor("_EmissionColor");
                if (e.maxColorComponent > 0.001f)
                {
                    lit.SetTexture("_EmissionMap", m.HasProperty("_EmissionMap") ? m.GetTexture("_EmissionMap") : null);
                    lit.SetColor("_EmissionColor", e);
                    lit.EnableKeyword("_EMISSION");
                    lit.globalIlluminationFlags = MaterialGlobalIlluminationFlags.BakedEmissive;
                }
            }
            if ((m.HasProperty("_Mode") && Mathf.Approximately(m.GetFloat("_Mode"), 1f)) || m.IsKeywordEnabled("_ALPHATEST_ON"))
            {
                lit.SetFloat("_AlphaClip", 1f);
                lit.EnableKeyword("_ALPHATEST_ON");
                lit.SetFloat("_Cutoff", m.HasProperty("_Cutoff") ? m.GetFloat("_Cutoff") : 0.5f);
                lit.renderQueue = (int)RenderQueue.AlphaTest;
                lit.SetOverrideTag("RenderType", "TransparentCutout");
            }
            return lit;
        }

        /// <summary>Elven Realm's triplanar rock to Game/RockTriplanar, its properties mapped onto URP Lit's names as the shader's header lists.</summary>
        static Material Triplanar(Material m, Material into, JsonValue kitValue)
        {
            var t = Target(into, "Game/RockTriplanar", m.name);
            Texture Tex(string p) => m.HasProperty(p) ? m.GetTexture(p) : null;
            float F(string p, float d) => m.HasProperty(p) ? m.GetFloat(p) : d;
            Color C(string p) => m.HasProperty(p) ? m.GetColor(p) : Color.white;
            t.SetTexture("_BaseMap", Tex("_TriplanarAlbedo"));
            float bottom = F("_TilingBottom", 0.2f), top = F("_TilingTop", 0.2f);
            t.SetTextureScale("_BaseMap", new Vector2(bottom, bottom));
            t.SetTexture("_BumpMap", Tex("_Normal"));
            t.SetFloat("_BumpScale", 1f);
            t.SetTexture("_DetailAlbedoMap", Tex("_TopAlbedo"));
            t.SetTextureScale("_DetailAlbedoMap", new Vector2(top, top));
            t.SetTexture("_DetailNormalMap", Tex("_TopNormal"));
            t.SetFloat("_DetailNormalMapScale", 1f);
            var bc = C("_BottomColourShift");
            var tc = C("_TopColourShift");
            t.SetColor("_BaseColor", ArenaDressing.KitValue(kitValue, new Color(bc.r, bc.g, bc.b, 1), m.name));
            t.SetColor("_SpecColor", ArenaDressing.KitValue(kitValue, new Color(tc.r, tc.g, tc.b, 1), m.name));
            t.SetFloat("_ClearCoatMask", F("_CoverageAmount", 0f));
            t.SetFloat("_ClearCoatSmoothness", F("_CoverageFalloff", 0.5f));
            t.SetFloat("_Metallic", F("_Metallic", 0.02f));
            t.SetFloat("_Smoothness", F("_Smoothness", 0.2f));
            return t;
        }

        static Material Particle(Material m, Material into)
        {
            var p = Target(into, "Universal Render Pipeline/Particles/Unlit", m.name);
            p.SetTexture("_BaseMap", m.HasProperty("_MainTex") ? m.GetTexture("_MainTex") : null);
            var col = m.HasProperty("_Color") ? m.GetColor("_Color") : m.HasProperty("_TintColor") ? m.GetColor("_TintColor") : Color.white;
            p.SetColor("_BaseColor", col);
            // Transparent, additive (the flames' glow), no depth write.
            p.SetFloat("_Surface", 1f);
            p.SetFloat("_Blend", 2f);
            p.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            p.SetFloat("_DstBlend", (float)BlendMode.One);
            p.SetFloat("_ZWrite", 0f);
            p.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            p.SetOverrideTag("RenderType", "Transparent");
            p.renderQueue = (int)RenderQueue.Transparent;
            return p;
        }

        static Material Target(Material into, string shaderName, string name)
        {
            var shader = Shader.Find(shaderName) ?? throw new Exception("MaterialConverter: no shader " + shaderName);
            if (into == null) return new Material(shader) { name = name + " (URP)" };
            into.shader = shader;
            into.shaderKeywords = new string[0];
            return into;
        }

        static string Safe(string name) => string.Concat(name.Select(ch => char.IsLetterOrDigit(ch) || ch == '_' ? ch : '_'));
    }
}
