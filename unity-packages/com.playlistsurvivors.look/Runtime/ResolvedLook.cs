using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace PS.Look
{
    /// <summary>
    /// A look preset (neon-look.md, Look presets) with the look flags over it: its palette, one option per axis
    /// (<see cref="LookAxes"/>) and its motion layers. An option or layer that isn't built yet draws today's look:
    /// <see cref="Option"/> is null for it and <see cref="Apply"/> warns. A preset file is Resources/Look/Looks/NAME.json:
    /// its name, a palette, a string for every axis's key and a <c>motion</c> object of layers.
    /// </summary>
    public sealed class ResolvedLook
    {
        /// <summary>The preset a launch without `--look` draws, only so it runs: no preset is the default.</summary>
        public const string Fallback = "edge";

        /// <summary>The look the player applied (<see cref="Apply"/>), or null before.</summary>
        public static ResolvedLook Active { get; private set; }

        /// <summary>The preset's name.</summary>
        public readonly string Name;
        /// <summary>Whether `--look` named the preset (else it is <see cref="Fallback"/>).</summary>
        public readonly bool Named;
        public readonly Palette Palette;
        readonly string _paletteFlag;
        readonly Dictionary<LookAxis, (string Option, string Flag)> _options = new Dictionary<LookAxis, (string, string)>();
        readonly Dictionary<string, (bool On, string Flag)> _motion = new Dictionary<string, (bool, string)>();

        ResolvedLook(string name, bool named, Palette palette, string paletteFlag)
        {
            Name = name;
            Named = named;
            Palette = palette;
            _paletteFlag = paletteFlag;
        }

        /// <summary>The option <paramref name="axis"/> draws, or null for today's look: none asked for is built yet.</summary>
        public string Option(LookAxis axis) => axis.IsBuilt(Requested(axis)) ? Requested(axis) : null;

        /// <summary>The option the preset or its flag asks <paramref name="axis"/> for, built or not.</summary>
        public string Requested(LookAxis axis) => _options[axis].Option;

        /// <summary>Whether motion layer <paramref name="layer"/> is on: asked for and built.</summary>
        public bool MotionOn(string layer) => MotionRequested(layer) && LookAxes.Motion.IsBuilt(layer);

        public bool MotionRequested(string layer) => _motion.TryGetValue(layer, out var m) && m.On;

        /// <summary>What was asked for and isn't built yet, so draws today's look: "monsters wireframe", "motion springs".</summary>
        public IReadOnlyList<string> NotBuilt
        {
            get
            {
                var missing = new List<string>();
                foreach (var axis in LookAxes.All)
                    if (Option(axis) == null) missing.Add($"{axis.Key} {Requested(axis)}");
                foreach (var layer in LookAxes.MotionLayers)
                    if (MotionRequested(layer) && !MotionOn(layer)) missing.Add($"motion {layer}");
                return missing;
            }
        }

        /// <summary>
        /// One line: the preset, its palette, every axis's option and the motion layers on, each a flag chose marked
        /// with it: "look edge, palette electric; monsters wireframe (--monster-look), structures edge-glass, ...".
        /// </summary>
        public string Describe()
        {
            static string By(string value, string flag) => flag != null ? $"{value} ({flag})" : value;
            var axes = LookAxes.All.Select(a => $"{a.Key} {By(Requested(a), _options[a].Flag)}");
            var layers = LookAxes.MotionLayers.Where(MotionRequested).Select(l => By(l, _motion[l].Flag)).ToList();
            return $"look {Name}{(Named ? "" : $" (no {LookFlags.LookFlag}: the fallback)")}, palette {By(Palette.Name, _paletteFlag)}; " +
                   string.Join(", ", axes) + "; motion " + (layers.Count > 0 ? string.Join(", ", layers) : "none");
        }

        /// <summary>Makes this the active look: the palette's roles set as shader globals, the look logged, and a warning for what isn't built.</summary>
        public void Apply()
        {
            Palette.SetShaderGlobals();
            Active = this;
            Debug.Log("Look: " + Describe());
            var missing = NotBuilt;
            if (missing.Count > 0) Debug.LogWarning($"Look: not built yet, so today's look: {string.Join(", ", missing)}");
        }

        /// <summary>
        /// The preset <paramref name="flags"/> pick from <paramref name="library"/> (the package's by default), the
        /// flags over it. Throws <see cref="ArgumentException"/> on an unknown preset and <see cref="FormatException"/>
        /// on a malformed preset or palette file.
        /// </summary>
        public static ResolvedLook Resolve(LookFlags flags, LookLibrary library = null)
        {
            library ??= LookLibrary.Shared;
            var name = flags.Look ?? Fallback;
            if (!library.HasPreset(name))
                throw new ArgumentException($"{LookFlags.LookFlag}: no preset {name} (have: {string.Join(", ", library.Presets)})");
            var preset = JObject.Parse(library.PresetJson(name));
            if ((string)preset["name"] != name) throw new FormatException($"look {name}: its file names it {preset["name"]}");

            var paletteName = flags.LookPalette ?? (string)preset["palette"] ?? throw new FormatException($"look {name}: no palette");
            if (!library.HasPalette(paletteName))
                throw new FormatException($"look {name}: no palette {paletteName} (have: {string.Join(", ", library.Palettes)})");
            var look = new ResolvedLook(name, flags.Look != null, Palette.Parse(library.PaletteJson(paletteName)),
                flags.LookPalette != null ? LookFlags.PaletteFlag : null);

            foreach (var kv in preset)
            {
                if (kv.Key == "name" || kv.Key == "palette" || kv.Key == "description") continue;
                if (kv.Key == "motion")
                {
                    if (!(kv.Value is JObject layers)) throw new FormatException($"look {name}: motion is not an object of layers");
                    foreach (var layer in layers)
                    {
                        if (Array.IndexOf(LookAxes.MotionLayers, layer.Key) < 0 || layer.Value.Type != JTokenType.Boolean)
                            throw new FormatException($"look {name}: motion {layer.Key} is not one of {string.Join(", ", LookAxes.MotionLayers)} switched true or false");
                        look._motion[layer.Key] = ((bool)layer.Value, null);
                    }
                    continue;
                }
                var axis = LookAxes.Of(kv.Key) ?? throw new FormatException($"look {name}: {kv.Key} is not an axis ({string.Join(", ", LookAxes.All)})");
                if (kv.Value.Type != JTokenType.String) throw new FormatException($"look {name}: {kv.Key} is not an option's name");
                look._options[axis] = ((string)kv.Value, null);
            }
            foreach (var axis in LookAxes.All)
            {
                if (flags.Overrides.TryGetValue(axis, out var option)) look._options[axis] = (option, axis.Flag);
                else if (!look._options.ContainsKey(axis)) throw new FormatException($"look {name}: no {axis.Key}");
            }
            if (flags.BeatRipple) look._motion["beatRipple"] = (true, LookFlags.BeatRippleFlag);
            return look;
        }
    }
}
