using System;
using System.Collections.Generic;

namespace Demo3D.Runtime.Playback
{
    /// <summary>
    /// The horde's monster set, `--roster <name>` (crowd.md, Roster sets): default is today's
    /// roster; forest and heroic are the MoreAssets sets. A set keeps the nine slot ids, so the
    /// choreography is unchanged; it swaps in its own baked roster (RosterBaker, under
    /// Assets/Baked/Rosters/) and level variant (Choreography/Data/action-level-NAME.json), and
    /// draws through the stock crowd with the prefabs' own materials. It can't be combined with a
    /// `--monster-style` other than baseline, which needs the bone crowd.
    /// </summary>
    public sealed class RosterSet
    {
        public const string Flag = "--roster";
        const string StyleFlag = "--monster-style";

        /// <summary>Every name the flag takes.</summary>
        public static readonly string[] Names = { "default", "forest", "heroic" };

        public readonly string Name;
        public bool IsDefault => Name == "default";

        /// <summary>The baked RosterAssets index's resource name (the default's is unchanged).</summary>
        public string AssetsResource => IsDefault ? RosterAssets.ResourcePath : RosterAssets.ResourcePath + "-" + Name;

        /// <summary>The level script's name (PlaybackData): action-level, or the set's variant.</summary>
        public string LevelName => IsDefault ? "action-level" : "action-level-" + Name;

        RosterSet(string name) { Name = name; }

        public static readonly RosterSet Default = new RosterSet("default");

        /// <summary>A set by name (null: default). Any other name fails, listing the three.</summary>
        public static RosterSet Parse(string name)
        {
            if (name == null) return Default;
            if (Array.IndexOf(Names, name) < 0)
                throw new ArgumentException($"{Flag}: unknown roster '{name}' (one of {string.Join(", ", Names)})");
            return name == "default" ? Default : new RosterSet(name);
        }

        /// <summary>The set command-line arguments ask for: `--roster name`, else default.</summary>
        public static RosterSet FromArgs(IReadOnlyList<string> args)
        {
            string name = null, style = null;
            for (int i = 0; i < args.Count; i++)
            {
                if (args[i] != Flag && args[i] != StyleFlag) continue;
                string value = i + 1 < args.Count && !args[i + 1].StartsWith("--", StringComparison.Ordinal) ? args[i + 1] : null;
                if (args[i] == StyleFlag) { style = value; continue; }
                name = value ?? throw new ArgumentException($"{Flag} needs a roster (one of {string.Join(", ", Names)})");
            }
            var set = Parse(name);
            if (!set.IsDefault && style != null && style != "baseline")
                throw new ArgumentException($"{Flag} {set.Name} draws through the stock crowd, so it can't take {StyleFlag} {style} (only baseline)");
            return set;
        }

        static RosterSet _active;

        /// <summary>The player's set, from its command line (an editor's is the default).</summary>
        public static RosterSet Active => _active ??= FromArgs(Environment.GetCommandLineArgs());
    }
}
