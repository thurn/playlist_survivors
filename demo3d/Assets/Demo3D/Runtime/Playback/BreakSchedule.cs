using System;
using System.Collections.Generic;

namespace Demo3D.Runtime.Playback
{
    /// <summary>
    /// The song's structure breaks, `--structure-breaks <name>` (choreography.md, Structure breaks): absent, nothing
    /// breaks; a name merges its schedule, Choreography/Data/structure-breaks-NAME.json, over the level script after
    /// any `--intensity`, so the hero's bolts break walls on the grid and the horde paths through the openings. It
    /// only changes the choreography, so it combines with every look, cast, environment and intensity flag.
    /// </summary>
    public sealed class BreakSchedule
    {
        public const string Flag = "--structure-breaks";

        /// <summary>Every name the flag takes, one per schedule file.</summary>
        public static readonly string[] Names = { "walls" };

        /// <summary>The schedule's name, or null for none.</summary>
        public readonly string Name;
        public bool IsNone => Name == null;

        /// <summary>The schedule's overlay name (PlaybackData): structure-breaks-NAME, or null for none.</summary>
        public string OverlayName => IsNone ? null : "structure-breaks-" + Name;

        BreakSchedule(string name) { Name = name; }

        public static readonly BreakSchedule None = new BreakSchedule(null);

        /// <summary>A schedule by name (null: none). Any other name fails, listing the known ones.</summary>
        public static BreakSchedule Parse(string name)
        {
            if (name == null) return None;
            if (Array.IndexOf(Names, name) < 0)
                throw new ArgumentException($"{Flag}: unknown schedule '{name}' (one of {string.Join(", ", Names)})");
            return new BreakSchedule(name);
        }

        /// <summary>The schedule command-line arguments ask for: `--structure-breaks name`, else none.</summary>
        public static BreakSchedule FromArgs(IReadOnlyList<string> args)
        {
            string name = null;
            for (int i = 0; i < args.Count; i++)
            {
                if (args[i] != Flag) continue;
                name = i + 1 < args.Count && !args[i + 1].StartsWith("--", StringComparison.Ordinal) ? args[i + 1]
                    : throw new ArgumentException($"{Flag} needs a schedule (one of {string.Join(", ", Names)})");
            }
            return Parse(name);
        }

        static BreakSchedule _active;

        /// <summary>The player's schedule, from its command line (an editor's is none).</summary>
        public static BreakSchedule Active => _active ??= FromArgs(Environment.GetCommandLineArgs());
    }
}
