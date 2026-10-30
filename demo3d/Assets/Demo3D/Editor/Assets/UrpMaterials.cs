using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Demo3D.EditorTools.Assets
{
    /// <summary>
    /// Swaps built-in pipeline materials (Synty's Standard, mostly) on an instance for URP Lit
    /// copies held in memory, so renders before TD9's kit conversion don't come out magenta.
    /// Nothing is saved: vendor materials stay as they are.
    /// </summary>
    public static class UrpMaterials
    {
        static readonly Dictionary<Material, Material> Cache = new Dictionary<Material, Material>();

        public static void Convert(GameObject root)
        {
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                if (r is ParticleSystemRenderer) continue;
                var mats = r.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < mats.Length; i++)
                {
                    var m = mats[i];
                    if (m == null || IsUrp(m.shader)) continue;
                    mats[i] = ToLit(m);
                    changed = true;
                }
                if (changed) r.sharedMaterials = mats;
            }
        }

        /// <summary>
        /// Swaps a prefab's built-in particle materials (the forest packs' auras, glitter and
        /// trails) for URP Particles/Unlit copies with the same texture, tint and blend, so they
        /// draw instead of coming out magenta. Only `--roster` sets call it; the default roster's
        /// particles are untouched.
        /// </summary>
        public static void ConvertParticles(GameObject root)
        {
            foreach (var r in root.GetComponentsInChildren<ParticleSystemRenderer>(true))
            {
                var mats = r.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < mats.Length; i++)
                {
                    var m = mats[i];
                    if (m == null || IsUrp(m.shader)) continue;
                    mats[i] = ToParticle(m);
                    changed = true;
                }
                if (changed) r.sharedMaterials = mats;
            }
        }

        static Material ToParticle(Material m)
        {
            if (Cache.TryGetValue(m, out var p) && p != null) return p;
            p = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit")) { name = m.name + " (URP)" };
            if (m.HasProperty("_MainTex")) p.SetTexture("_BaseMap", m.GetTexture("_MainTex"));
            // Legacy particle shaders double _TintColor (0.5 grey is white).
            var tint = m.HasProperty("_TintColor") ? m.GetColor("_TintColor") * 2f
                : m.HasProperty("_Color") ? m.GetColor("_Color") : Color.white;
            p.SetColor("_BaseColor", tint);
            bool additive = m.shader != null && m.shader.name.IndexOf("Add", System.StringComparison.OrdinalIgnoreCase) >= 0;
            p.SetFloat("_Surface", 1f);
            p.SetFloat("_Blend", additive ? 2f : 0f); // URP's Additive, else Alpha
            p.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            p.SetFloat("_DstBlend", (float)(additive ? BlendMode.One : BlendMode.OneMinusSrcAlpha));
            p.SetFloat("_SrcBlendAlpha", (float)(additive ? BlendMode.Zero : BlendMode.One));
            p.SetFloat("_DstBlendAlpha", (float)(additive ? BlendMode.One : BlendMode.OneMinusSrcAlpha));
            p.SetFloat("_ZWrite", 0f);
            p.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            p.SetOverrideTag("RenderType", "Transparent");
            p.renderQueue = (int)RenderQueue.Transparent;
            Cache[m] = p;
            return p;
        }

        static bool IsUrp(Shader s)
        {
            if (s == null) return false;
            var tag = s.FindPassTagValue(0, new ShaderTagId("LightMode"));
            return s.name.StartsWith("Universal Render Pipeline") || s.name.StartsWith("Shader Graphs")
                   || s.name.Contains("URP") || tag.name == "UniversalForward" || tag.name == "SRPDefaultUnlit";
        }

        public static Material ToLit(Material m)
        {
            if (Cache.TryGetValue(m, out var lit) && lit != null) return lit;
            lit = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = m.name + " (URP)" };
            var tex = m.HasProperty("_MainTex") ? m.GetTexture("_MainTex") : null;
            if (tex == null && m.HasProperty("_Albedo")) tex = m.GetTexture("_Albedo");
            if (tex != null) lit.SetTexture("_BaseMap", tex);
            if (m.HasProperty("_Color")) lit.SetColor("_BaseColor", m.GetColor("_Color"));
            if (m.HasProperty("_Glossiness")) lit.SetFloat("_Smoothness", m.GetFloat("_Glossiness") * 0.5f);
            if (m.HasProperty("_Metallic")) lit.SetFloat("_Metallic", m.GetFloat("_Metallic"));
            if (m.HasProperty("_EmissionMap") && m.IsKeywordEnabled("_EMISSION"))
            {
                lit.SetTexture("_EmissionMap", m.GetTexture("_EmissionMap"));
                lit.SetColor("_EmissionColor", m.GetColor("_EmissionColor"));
                lit.EnableKeyword("_EMISSION");
                lit.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            }
            if (m.HasProperty("_Mode") && m.GetFloat("_Mode") == 1f || m.IsKeywordEnabled("_ALPHATEST_ON"))
            {
                lit.SetFloat("_AlphaClip", 1f);
                lit.EnableKeyword("_ALPHATEST_ON");
                lit.SetFloat("_Cutoff", m.HasProperty("_Cutoff") ? m.GetFloat("_Cutoff") : 0.5f);
            }
            Cache[m] = lit;
            return lit;
        }
    }
}
