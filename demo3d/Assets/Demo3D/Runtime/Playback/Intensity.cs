using System;
using System.Collections.Generic;

namespace Demo3D.Runtime.Playback
{
    /// <summary>
    /// The choreography's intensity, `--intensity <name>` (choreography.md, Intensity overlays): normal
    /// plays the roster's level script as it is; any other name merges its overlay,
    /// Choreography/Data/intensity-NAME.json, over that script before the choreographer runs. It only
    /// changes the choreography, so it combines with every look and cast flag.
    /// </summary>
    public sealed class Intensity
    {
        public const string Flag = "--intensity";

        /// <summary>Every name the flag takes: normal and one per overlay file.</summary>
        public static readonly string[] Names = { "normal", "overdrive" };

        public readonly string Name;
        public bool IsNormal => Name == "normal";

        /// <summary>The overlay's name (PlaybackData): intensity-NAME, or null for normal.</summary>
        public string OverlayName => IsNormal ? null : "intensity-" + Name;

        Intensity(string name) { Name = name; }

        public static readonly Intensity Normal = new Intensity("normal");

        /// <summary>An intensity by name (null: normal). Any other name fails, listing the known ones.</summary>
        public static Intensity Parse(string name)
        {
            if (name == null) return Normal;
            if (Array.IndexOf(Names, name) < 0)
                throw new ArgumentException($"{Flag}: unknown intensity '{name}' (one of {string.Join(", ", Names)})");
            return name == "normal" ? Normal : new Intensity(name);
        }

        /// <summary>The intensity command-line arguments ask for: `--intensity name`, else normal.</summary>
        public static Intensity FromArgs(IReadOnlyList<string> args)
        {
            string name = null;
            for (int i = 0; i < args.Count; i++)
            {
                if (args[i] != Flag) continue;
                name = i + 1 < args.Count && !args[i + 1].StartsWith("--", StringComparison.Ordinal) ? args[i + 1]
                    : throw new ArgumentException($"{Flag} needs an intensity (one of {string.Join(", ", Names)})");
            }
            return Parse(name);
        }

        static Intensity _active;

        /// <summary>The player's intensity, from its command line (an editor's is normal).</summary>
        public static Intensity Active => _active ??= FromArgs(Environment.GetCommandLineArgs());
    }
}
