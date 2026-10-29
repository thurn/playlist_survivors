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
