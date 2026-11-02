using System;
using Newtonsoft.Json.Linq;

namespace PS.Look
{
    /// <summary>
    /// One option of the post axis (neon-look.md, Post-processing stack): a file Resources/Look/Post/NAME.json naming
    /// every stage's settings, which <see cref="PostStack"/> builds. Every group and value may be left out (its default
    /// is off, or URP's neutral); an unknown group or value fails, so a typo never silently draws the default.
    /// <code>
    /// { "name": "neon-bloom",
    ///   "tonemap": "filmic", "shoulder": 0.6, "whiten": 0.5,
    ///   "void":   { "knee": 0.25, "power": 2.2 },
    ///   "colour": { "exposure": 0, "contrast": 12, "saturation": 10 },
    ///   "bloom":  { "threshold": 0.9, "intensity": 0.8, "scatter": 0.6 },
    ///   "halo":   { "threshold": 0.5, "intensity": 0.6, "saturation": 1.5 },
    ///   "lens":   { "chromatic": 0, "vignette": 0.3, "smoothness": 0.45, "grain": 0.1, "dirt": 6 },
    ///   "retro":  { "scanlines": 0, "mask": 0 },
    ///   "feel":   { "beat": { ... }, "flash": { ... }, "hit": { ... } } }
    /// </code>
    /// </summary>
    public sealed class PostOption
    {
        public enum Tonemaps { Neutral, Aces, Filmic }

        /// <summary>The beat pulses and impact flashes (<see cref="PostOffsets"/>), today's PostFeel amounts.</summary>
        public sealed class FeelAmounts
        {
            /// <summary>A beat's pulse: exposure (stops), bloom intensity, vignette, and how far the bloom tints toward <c>grid.pulse</c> (0 to 1).</summary>
            public float BeatExposure, BeatBloom, BeatVignette, BeatTint;
            /// <summary>A full flash: exposure (stops), chromatic aberration and saturation; the bloom tints to <c>spark</c> with it.</summary>
            public float FlashExposure, FlashChromatic, FlashSaturation;
            /// <summary>
            /// A flash outside a special moment (neon-look.md, Restraint and special moments), as a share of its own:
            /// structure breaks, finishers and drops flash in full, accents, strikes and hits at this (1: all in full).
            /// </summary>
            public float FlashCalm = 1f;
            /// <summary>The hero's attacks at or above this strength flash as hits, at this share of their strength (above 1: none).</summary>
            public float HitStrength = 2f, HitFlash;
        }

        public readonly string Name;
        public Tonemaps Tonemap = Tonemaps.Filmic;
        /// <summary>Filmic: where the curve's shoulder starts (linear, of the brightest channel), and how far a core run past white goes white (0 to 1).</summary>
        public float Shoulder = 0.6f, Whiten = 0.5f;
        /// <summary>
        /// The void's toe: light under the knee (linear, of the brightest channel) sinks by this power toward black, hue
        /// kept, so a lit world falls to the void while emitters over the knee stay as they are (knee 0: none).
        /// </summary>
        public float VoidKnee, VoidPower = 2f;
        /// <summary>Colour adjustments: exposure (stops), contrast and saturation (URP's -100 to 100).</summary>
        public float Exposure, Contrast, Saturation;
        /// <summary>URP's bloom, the tight core layer.</summary>
        public float BloomThreshold = 1f, BloomIntensity, BloomScatter = 0.6f;
        /// <summary>The wide soft halo layer (<see cref="PostStack"/>'s pass): threshold, intensity and its glow's saturation (1 the source's).</summary>
        public float HaloThreshold = 0.5f, HaloIntensity, HaloSaturation = 1f;
        /// <summary>Lens: chromatic aberration (0 to 1), vignette intensity and smoothness, film grain, and lens dirt on the bloom.</summary>
        public float Chromatic, Vignette, VignetteSmoothness = 0.45f, Grain, Dirt;
        /// <summary>Retro: the scanlines' darkening at their darkest (0 to 1) and the soft CRT mask's (0 to 1).</summary>
        public float Scanlines, Mask;
        public readonly FeelAmounts Feel = new FeelAmounts();

        PostOption(string name) => Name = name;

        /// <summary>Parses a post option file; throws <see cref="FormatException"/> on a malformed one.</summary>
        public static PostOption Parse(string json)
        {
            var o = JObject.Parse(json);
            var name = (string)o["name"] ?? throw new FormatException("post option: no name");
            var p = new PostOption(name);
            string where = $"post {name}";
            foreach (var kv in o)
            {
                switch (kv.Key)
                {
                    case "name":
                    case "note":
                        break;
                    case "tonemap":
                        p.Tonemap = (string)kv.Value switch
                        {
                            "neutral" => Tonemaps.Neutral,
                            "aces" => Tonemaps.Aces,
                            "filmic" => Tonemaps.Filmic,
                            _ => throw new FormatException($"{where}: tonemap {kv.Value} is not neutral, aces or filmic"),
                        };
                        break;
                    case "shoulder": p.Shoulder = Number(kv.Value, $"{where}: shoulder"); break;
                    case "whiten": p.Whiten = Number(kv.Value, $"{where}: whiten"); break;
                    case "void":
                        Group(kv.Value, $"{where}: void", (k, v) => k switch
                        {
                            "knee" => p.VoidKnee = v,
                            "power" => p.VoidPower = v,
                            _ => (float?)null,
                        });
                        break;
                    case "colour":
                        Group(kv.Value, $"{where}: colour", (k, v) => k switch
                        {
                            "exposure" => p.Exposure = v,
                            "contrast" => p.Contrast = v,
                            "saturation" => p.Saturation = v,
                            _ => (float?)null,
                        });
                        break;
                    case "bloom":
                        Group(kv.Value, $"{where}: bloom", (k, v) => k switch
                        {
                            "threshold" => p.BloomThreshold = v,
                            "intensity" => p.BloomIntensity = v,
                            "scatter" => p.BloomScatter = v,
                            _ => (float?)null,
                        });
                        break;
                    case "halo":
                        Group(kv.Value, $"{where}: halo", (k, v) => k switch
                        {
                            "threshold" => p.HaloThreshold = v,
                            "intensity" => p.HaloIntensity = v,
                            "saturation" => p.HaloSaturation = v,
                            _ => (float?)null,
                        });
                        break;
                    case "lens":
                        Group(kv.Value, $"{where}: lens", (k, v) => k switch
                        {
                            "chromatic" => p.Chromatic = v,
                            "vignette" => p.Vignette = v,
                            "smoothness" => p.VignetteSmoothness = v,
                            "grain" => p.Grain = v,
                            "dirt" => p.Dirt = v,
                            _ => (float?)null,
                        });
                        break;
                    case "retro":
                        Group(kv.Value, $"{where}: retro", (k, v) => k switch
                        {
                            "scanlines" => p.Scanlines = v,
                            "mask" => p.Mask = v,
                            _ => (float?)null,
                        });
                        break;
                    case "feel":
                        ParseFeel(kv.Value, p.Feel, $"{where}: feel");
                        break;
                    default:
                        throw new FormatException($"{where}: {kv.Key} is not a stage");
                }
            }
            return p;
        }

        static void ParseFeel(JToken token, FeelAmounts f, string where)
        {
            if (!(token is JObject feel)) throw new FormatException($"{where}: not an object");
            foreach (var kv in feel)
            {
                switch (kv.Key)
                {
                    case "beat":
                        Group(kv.Value, $"{where}.beat", (k, v) => k switch
                        {
                            "exposure" => f.BeatExposure = v,
                            "bloom" => f.BeatBloom = v,
                            "vignette" => f.BeatVignette = v,
                            "tint" => f.BeatTint = v,
                            _ => (float?)null,
                        });
                        break;
                    case "flash":
                        Group(kv.Value, $"{where}.flash", (k, v) => k switch
                        {
                            "exposure" => f.FlashExposure = v,
                            "chromatic" => f.FlashChromatic = v,
                            "saturation" => f.FlashSaturation = v,
                            "calm" => f.FlashCalm = v,
                            _ => (float?)null,
                        });
                        break;
                    case "hit":
                        Group(kv.Value, $"{where}.hit", (k, v) => k switch
                        {
                            "minStrength" => f.HitStrength = v,
                            "flash" => f.HitFlash = v,
                            _ => (float?)null,
                        });
                        break;
                    default:
                        throw new FormatException($"{where}: {kv.Key} is not beat, flash or hit");
                }
            }
        }

        /// <summary>Sets each number of a group with <paramref name="set"/>, which returns null for a key it doesn't know.</summary>
        static void Group(JToken token, string where, Func<string, float, float?> set)
        {
            if (!(token is JObject group)) throw new FormatException($"{where}: not an object");
            foreach (var kv in group)
                if (set(kv.Key, Number(kv.Value, $"{where}.{kv.Key}")) == null) throw new FormatException($"{where}: no setting {kv.Key}");
        }

        static float Number(JToken token, string where) =>
            token.Type == JTokenType.Float || token.Type == JTokenType.Integer ? (float)token : throw new FormatException($"{where}: {token} is not a number");
    }
}
