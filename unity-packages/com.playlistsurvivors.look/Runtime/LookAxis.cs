using System;
using System.Collections.Generic;

namespace PS.Look
{
    /// <summary>
    /// One aesthetic axis a preset picks an option on (neon-look.md, The Axes): its key in a preset file, its flag and
    /// the options built so far. A preset or flag may name an option that isn't built yet: the axis then draws today's
    /// look, with a warning (<see cref="ResolvedLook"/>), so presets can name what later work will build.
    /// Every axis also takes <see cref="Classic"/>: today's look on purpose, with no warning.
    /// </summary>
    public sealed class LookAxis
    {
        /// <summary>
        /// The option every axis takes for the look before the neon look (ps-wnj), for parity comparisons: never built, so
        /// the axis draws today's look (the Synty arenas, the crystal monsters, the night post), and never warned of.
        /// The classic preset names it on every axis; an axis flag (`--post classic`) mixes it into any preset.
        /// </summary>
        public const string Classic = "classic";

        /// <summary>The axis's key in a preset file (`monsters`).</summary>
        public readonly string Key;
        /// <summary>The flag that overrides the preset's option (`--monster-look`), or null for none.</summary>
        public readonly string Flag;
        readonly string[] _built;

        public LookAxis(string key, string flag, params string[] built)
        {
            Key = key;
            Flag = flag;
            _built = built;
        }

        /// <summary>The options built so far, the only ones that draw.</summary>
        public IReadOnlyList<string> Built => _built;

        public bool IsBuilt(string option) => Array.IndexOf(_built, option) >= 0;

        public override string ToString() => Key;
    }

    /// <summary>
    /// The registry of axes. Each axis is declared in its own file under Axes/, which lists its built options, so the
    /// work that builds an option touches only its axis's file. <see cref="All"/> lists every axis a preset holds.
    /// </summary>
    public static partial class LookAxes
    {
        /// <summary>Every axis with a key of its own in a preset file, in the order a resolved look describes them.</summary>
        public static IReadOnlyList<LookAxis> All => _all ??= new[] { Monsters, Structures, World, Post, Vfx, Hero };
        static LookAxis[] _all;

        /// <summary>The axis whose preset key is <paramref name="key"/>, or null.</summary>
        public static LookAxis Of(string key)
        {
            foreach (var axis in All)
                if (axis.Key == key) return axis;
            return null;
        }
    }
}
