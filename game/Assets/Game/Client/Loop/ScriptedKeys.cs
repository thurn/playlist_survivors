using System;
using System.Collections.Generic;
using System.Globalization;
using PS.Game.Sim;

namespace Game.Client.Loop
{
    /// <summary>
    /// Held keys at song times, for windowless stills and headless runs of a walking hero:
    /// `--walk "right@9-11,up@11-12.5"` holds Right from 9 s to 11 s, then Up to 12.5 s. Each key goes
    /// down and up when the sim first reaches its time at a frame's start, heard then, as
    /// <see cref="DebugCommands"/> gives its commands, so a fixed-time rerun walks the same way.
    /// The keys are up, down, left, right and space; a session records them as any key.
    /// </summary>
    public sealed class ScriptedKeys
    {
        static readonly (string name, Key key)[] Names =
        {
            ("up", Key.Up), ("down", Key.Down), ("left", Key.Left), ("right", Key.Right), ("space", Key.Space),
        };

        readonly List<(double at, Key key, bool down)> _changes;
        int _next;

        public ScriptedKeys(string schedule) => _changes = Parse(schedule);

        /// <summary>`key@from-to`, comma-separated, in any order: a press at from and its release at to.</summary>
        public static List<(double at, Key key, bool down)> Parse(string schedule)
        {
            var list = new List<(double, Key, bool)>();
            if (string.IsNullOrWhiteSpace(schedule)) return list;
            foreach (var item in schedule.Split(','))
            {
                var parts = item.Trim().Split('@');
                int i = parts.Length == 2 ? Array.FindIndex(Names, n => n.name == parts[0].Trim()) : -1;
                var span = i >= 0 ? parts[1].Split('-') : null;
                if (span == null || span.Length != 2
                    || !double.TryParse(span[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double from)
                    || !double.TryParse(span[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double to) || to < from)
                    throw new ArgumentException($"--walk: '{item}' is not one of {string.Join(", ", Array.ConvertAll(Names, n => n.name))} @ from-to seconds");
                list.Add((from, Names[i].key, true));
                list.Add((to, Names[i].key, false));
            }
            list.Sort((a, b) => a.Item1.CompareTo(b.Item1));
            return list;
        }

        /// <summary>Gives the sim the key changes due by its time now.</summary>
        public void Apply(Simulation sim)
        {
            for (; _next < _changes.Count && sim.Now >= _changes[_next].at - 1e-9; _next++)
            {
                var (_, key, down) = _changes[_next];
                if (down) sim.Press(key, sim.Now);
                else sim.Release(key, sim.Now);
            }
        }
    }
}
