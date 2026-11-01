using System;
using System.Collections.Generic;

namespace PS.Look
{
    /// <summary>
    /// The look flags both players take (neon-look.md, Flags), parsed once here:
    /// - `--look NAME`: the preset (<see cref="ResolvedLook.Fallback"/> without one);
    /// - each axis's flag (`--monster-look`, `--structure-look`, `--world`, `--post`, `--vfx`, `--hero-look`):
    ///   that axis's option in place of the preset's;
    /// - `--palette NAME`: when NAME is one of the package's palettes, the look's palette in place of the preset's;
    ///   any other NAME is the arena's zone palette as before (<see cref="ArenaPalette"/>), until the void world
    ///   replaces the arenas;
    /// - `--beat-ripple`: the beat ripple motion layer on, whatever the preset says.
    /// </summary>
    public sealed class LookFlags
    {
        public const string LookFlag = "--look", PaletteFlag = "--palette", BeatRippleFlag = "--beat-ripple";

        static LookFlags _current;

        /// <summary>This launch's look flags, parsed from its command line on first use.</summary>
        public static LookFlags Current => _current ??= Parse(Environment.GetCommandLineArgs());

        /// <summary>`--look`, or null.</summary>
        public string Look;
        /// <summary>`--palette` when it names a look palette, else null.</summary>
        public string LookPalette;
        /// <summary>`--palette` when it names no look palette (an arena's zone palette, or "" for its night as baked), else null.</summary>
        public string ArenaPalette;
        public bool BeatRipple;
        /// <summary>The axis flags given: the option each names, by axis.</summary>
        public readonly Dictionary<LookAxis, string> Overrides = new Dictionary<LookAxis, string>();

        /// <summary>Whether any flag chooses something about the look (`--palette` only when it names a look palette).</summary>
        public bool Any => Look != null || LookPalette != null || BeatRipple || Overrides.Count > 0;

        /// <summary>Every look flag that takes a value, for a player that claims the flags it reads.</summary>
        public static IEnumerable<string> ValuedFlags
        {
            get
            {
                yield return LookFlag;
                yield return PaletteFlag;
                foreach (var axis in LookAxes.All)
                    if (axis.Flag != null) yield return axis.Flag;
            }
        }

        /// <summary>Every look flag that takes no value.</summary>
        public static IEnumerable<string> Switches
        {
            get { yield return BeatRippleFlag; }
        }

        /// <summary>
        /// The look flags in <paramref name="args"/>, as <see cref="Environment.GetCommandLineArgs"/> gives them
        /// (other flags ignored), `--palette` judged against <paramref name="library"/>'s palettes (the package's by
        /// default). A flag's value is the argument after it; the first of a repeated flag wins.
        /// </summary>
        public static LookFlags Parse(string[] args, LookLibrary library = null)
        {
            args ??= new string[0];
            string Get(string name)
            {
                for (int i = 0; i < args.Length - 1; i++)
                    if (args[i] == name) return args[i + 1];
                return null;
            }
            var flags = new LookFlags { Look = Get(LookFlag), BeatRipple = Array.IndexOf(args, BeatRippleFlag) >= 0 };
            foreach (var axis in LookAxes.All)
                if (axis.Flag != null && Get(axis.Flag) is string option) flags.Overrides[axis] = option;
            if (Get(PaletteFlag) is string palette)
            {
                if ((library ?? LookLibrary.Shared).HasPalette(palette)) flags.LookPalette = palette;
                else flags.ArenaPalette = palette;
            }
            return flags;
        }
    }
}
