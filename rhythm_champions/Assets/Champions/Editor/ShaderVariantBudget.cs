using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.Rendering;

namespace Champions.EditorTools
{
    /// <summary>
    /// The shader variant budget (plan.md, Rendering > Pipeline). After URP's own stripping it removes the
    /// variants of every feature the lean pipeline turns off (shadows, lightmaps, probes, SSAO, decals, cookies,
    /// light layers, Forward+, fog, LOD cross-fades, the debug display, unused post) and the shaders of features it
    /// never uses, counts what is left per shader, pass and stage, and writes the count beside the player as
    /// shader-variants.json. A build that keeps more than <see cref="Budget"/> variants fails.
    ///
    /// An incremental build that reuses the last build's shaders processes none of them, so it counts only what
    /// it processed on top of the counts the last build left (Library/ChampionsShaderVariants.tsv): a shader it
    /// processed replaces its old counts. A build with a clean build cache (`champions check --variants`) counts
    /// from scratch.
    /// </summary>
    public sealed class ShaderVariantBudget : IPreprocessShaders, IPreprocessBuildWithReport, IPostprocessBuildWithReport
    {
        public const int Budget = 300;
        public const string ReportName = "shader-variants.json";

        /// <summary>Keywords of the features the pipeline never enables: a variant with any of them is dead weight.</summary>
        static readonly HashSet<string> StrippedKeywords = new HashSet<string>
        {
            // Shadows.
            "_MAIN_LIGHT_SHADOWS", "_MAIN_LIGHT_SHADOWS_CASCADE", "_MAIN_LIGHT_SHADOWS_SCREEN", "_ADDITIONAL_LIGHT_SHADOWS",
            "_SHADOWS_SOFT", "_SHADOWS_SOFT_LOW", "_SHADOWS_SOFT_MEDIUM", "_SHADOWS_SOFT_HIGH", "SHADOWS_SHADOWMASK",
            "_CASTING_PUNCTUAL_LIGHT_SHADOW",
            // Lightmaps and mixed lighting.
            "LIGHTMAP_ON", "DYNAMICLIGHTMAP_ON", "DIRLIGHTMAP_COMBINED", "LIGHTMAP_SHADOW_MIXING", "_MIXED_LIGHTING_SUBTRACTIVE",
            "LIGHTMAP_BICUBIC_SAMPLING",
            // Probes.
            "PROBE_VOLUMES_L1", "PROBE_VOLUMES_L2", "_REFLECTION_PROBE_BLENDING", "_REFLECTION_PROBE_BOX_PROJECTION",
            "_REFLECTION_PROBE_ATLAS",
            // SSAO, decals, cookies, light layers.
            "_SCREEN_SPACE_OCCLUSION", "_DBUFFER_MRT1", "_DBUFFER_MRT2", "_DBUFFER_MRT3", "_DECAL_LAYERS", "DECALS_3RT",
            "DECALS_4RT", "_DECAL_NORMAL_BLEND_LOW", "_DECAL_NORMAL_BLEND_MEDIUM", "_DECAL_NORMAL_BLEND_HIGH", "_LIGHT_COOKIES",
            "_LIGHT_LAYERS", "_WRITE_RENDERING_LAYERS",
            // Forward+ (the renderer is Forward), fog (the void has none), cross-fades, the debug display.
            "_FORWARD_PLUS", "_CLUSTER_LIGHT_LOOP", "FOG_LINEAR", "FOG_EXP", "FOG_EXP2", "LOD_FADE_CROSSFADE", "DEBUG_DISPLAY",
            // Post the cameras never ask for: FXAA and FSR (MSAA is the tiers' antialiasing), film grain, dithering, and
            // the tonemapping keywords, which HDR grading bakes into its LUT instead.
            "_FXAA", "_RCAS", "_EASU_RCAS_AND_HDR_INPUT", "_FILM_GRAIN", "_DITHERING", "_TONEMAP_ACES", "_TONEMAP_NEUTRAL",
            // UI Toolkit's gamma path: the project draws in linear.
            "_UIE_FORCE_GAMMA",
        };

        /// <summary>
        /// Shaders (by name prefix) of features the pipeline never uses, stripped whole: other antialiasing than MSAA,
        /// screen-space reflections, upscaling, 360 capture, XR, the debug views, 2D sprites and physics, and IMGUI
        /// (the game's UI is UI Toolkit).
        /// </summary>
        static readonly string[] StrippedShaders =
        {
            "Hidden/Universal Render Pipeline/SubpixelMorphologicalAntialiasing", "Hidden/Universal Render Pipeline/TemporalAA",
            "Hidden/Universal Render Pipeline/ComputeScreenSpaceReflection", "Hidden/Universal Render Pipeline/Scaling Setup",
            "Hidden/Universal Render Pipeline/Edge Adaptive Spatial Upsampling", "Hidden/Internal-ODSWorldTexture",
            "Hidden/Internal-CubemapToEquirect", "Hidden/VR/", "Hidden/Universal Render Pipeline/XR", "Hidden/Internal-DebugPattern",
            "Hidden/Universal/HDRDebugView", "Hidden/Core/VrsVisualization", "Hidden/Universal Render Pipeline/Debug/",
            "Hidden/BlitCopyHDRTonemap", "Sprites/", "Hidden/Physics2D/", "Hidden/Internal-GUI",
        };

        const string StatePath = "Library/ChampionsShaderVariants.tsv";

        // This build's processed snippets, and whether it started from a clean build cache.
        static readonly Dictionary<(string shader, string pass, ShaderType stage), int> Processed =
            new Dictionary<(string, string, ShaderType), int>();
        static bool _fresh;

        public int callbackOrder => int.MaxValue; // after URP's stripping, and the build's other postprocessing

        public void OnPreprocessBuild(BuildReport report)
        {
            Processed.Clear();
            _fresh = (report.summary.options & BuildOptions.CleanBuildCache) != 0;
        }

        public void OnProcessShader(Shader shader, ShaderSnippetData snippet, IList<ShaderCompilerData> data)
        {
            if (StrippedShaders.Any(prefix => shader.name.StartsWith(prefix, StringComparison.Ordinal))) data.Clear();
            for (int i = data.Count - 1; i >= 0; i--)
                if (Strips(data[i].shaderKeywordSet)) data.RemoveAt(i);
            var key = (shader.name, snippet.passName, snippet.shaderType);
            Processed[key] = (Processed.TryGetValue(key, out var n) ? n : 0) + data.Count;
        }

        static bool Strips(ShaderKeywordSet set)
        {
            foreach (var keyword in set.GetShaderKeywords())
                if (StrippedKeywords.Contains(keyword.name)) return true;
            return false;
        }

        public void OnPostprocessBuild(BuildReport report)
        {
            var kept = _fresh ? new Dictionary<(string shader, string pass, ShaderType stage), int>() : Load();
            var processed = new HashSet<string>(Processed.Keys.Select(k => k.shader));
            foreach (var k in kept.Keys.Where(k => processed.Contains(k.shader)).ToList()) kept.Remove(k);
            foreach (var e in Processed) kept[e.Key] = e.Value;
            Save(kept);
            int total = kept.Values.Sum();
            var largest = kept.Where(e => e.Value > 0).OrderByDescending(e => e.Value).ThenBy(e => e.Key.shader)
                .ThenBy(e => e.Key.pass).ThenBy(e => e.Key.stage).ToList();
            var dir = Path.GetDirectoryName(report.summary.outputPath);
            File.WriteAllText(Path.Combine(dir, ReportName), ReportJson(total, largest));
            Debug.Log($"ShaderVariantBudget: {total} compiled variants in this build (budget {Budget}; " +
                      $"{(_fresh ? "a clean build cache" : $"{processed.Count} shaders processed, the rest as the last build left them")})");
            if (total > Budget)
                throw new BuildFailedException($"ShaderVariantBudget: {total} compiled variants, over the budget of {Budget} " +
                                               $"(the largest: {string.Join(", ", largest.Take(5).Select(e => $"{e.Key.shader} {e.Key.pass} {e.Value}"))})");
        }

        static Dictionary<(string shader, string pass, ShaderType stage), int> Load()
        {
            var kept = new Dictionary<(string, string, ShaderType), int>();
            if (!File.Exists(StatePath)) return kept;
            foreach (var line in File.ReadAllLines(StatePath))
            {
                var f = line.Split('\t');
                if (f.Length == 4 && Enum.TryParse(f[2], out ShaderType stage) && int.TryParse(f[3], out var n))
                    kept[(f[0], f[1], stage)] = n;
            }
            return kept;
        }

        static void Save(Dictionary<(string shader, string pass, ShaderType stage), int> kept) =>
            File.WriteAllLines(StatePath, kept.Select(e => $"{e.Key.shader}\t{e.Key.pass}\t{e.Key.stage}\t{e.Value}"));

        static string ReportJson(int total, List<KeyValuePair<(string shader, string pass, ShaderType stage), int>> largest)
        {
            var sb = new StringBuilder();
            sb.Append("{\n  \"budget\": ").Append(Budget).Append(",\n  \"total\": ").Append(total)
                .Append(",\n  \"fresh\": ").Append(_fresh ? "true" : "false").Append(",\n  \"snippets\": [");
            bool first = true;
            foreach (var e in largest)
            {
                sb.Append(first ? "\n" : ",\n").Append("    {\"shader\": \"").Append(Escape(e.Key.shader)).Append("\", \"pass\": \"")
                    .Append(Escape(e.Key.pass)).Append("\", \"stage\": \"").Append(e.Key.stage.ToString().ToLowerInvariant())
                    .Append("\", \"variants\": ").Append(e.Value.ToString(CultureInfo.InvariantCulture)).Append('}');
                first = false;
            }
            return sb.Append("\n  ]\n}\n").ToString();
        }

        static string Escape(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }
}
