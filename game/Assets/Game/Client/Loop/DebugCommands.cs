using System;
using System.Collections.Generic;
using System.Globalization;
using PS.Game.Sim;
using UnityEngine.InputSystem;

namespace Game.Client.Loop
{
    /// <summary>
    /// The slice's debug hooks (unity-game.md, Debug Surfaces; M3's F1 menu takes them over): F5
    /// forces a section change, F6 an accent, F7 breaks the structures near the hero and F8 regrows
    /// the broken ones (<see cref="DebugCommand"/>). `--debug "change@20,accent@12.5,break@15,regrow@30"`
    /// gives them at song times instead, for headless runs and stills: each when the sim first
    /// reaches its time at a frame's start, so a fixed-time rerun gives it on the same tick.
    /// </summary>
    public sealed class DebugCommands
    {
        static readonly (string name, DebugCommand command)[] Names =
        {
            ("change", DebugCommand.SectionChange), ("accent", DebugCommand.Accent),
            ("break", DebugCommand.BreakStructures), ("regrow", DebugCommand.RegrowStructures),
        };

        readonly List<(double at, DebugCommand command)> _scheduled;
        int _next;

        public DebugCommands(string schedule) => _scheduled = Parse(schedule);

        /// <summary>Commands given so far.</summary>
        public int Given { get; private set; }

        /// <summary>`name@seconds`, comma-separated, in any order.</summary>
        public static List<(double at, DebugCommand command)> Parse(string schedule)
        {
            var list = new List<(double, DebugCommand)>();
            if (string.IsNullOrWhiteSpace(schedule)) return list;
            foreach (var item in schedule.Split(','))
            {
                var parts = item.Trim().Split('@');
                int i = parts.Length == 2 ? Array.FindIndex(Names, n => n.name == parts[0].Trim()) : -1;
                if (i < 0 || !double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double at))
                    throw new ArgumentException($"--debug: '{item}' is not one of change, accent, break or regrow @ seconds");
                list.Add((at, Names[i].command));
            }
            list.Sort((a, b) => a.Item1.CompareTo(b.Item1));
            return list;
        }

        /// <summary>Gives the sim the commands due by its time now and the function keys pressed this frame.</summary>
        public void Apply(Simulation sim)
        {
            for (; _next < _scheduled.Count && sim.Now >= _scheduled[_next].at - 1e-9; _next++) Give(sim, _scheduled[_next].command);
            var keyboard = Keyboard.current;
            if (keyboard == null) return;
            if (keyboard.f5Key.wasPressedThisFrame) Give(sim, DebugCommand.SectionChange);
            if (keyboard.f6Key.wasPressedThisFrame) Give(sim, DebugCommand.Accent);
            if (keyboard.f7Key.wasPressedThisFrame) Give(sim, DebugCommand.BreakStructures);
            if (keyboard.f8Key.wasPressedThisFrame) Give(sim, DebugCommand.RegrowStructures);
        }

        void Give(Simulation sim, DebugCommand command)
        {
            sim.Command(command);
            Given++;
        }
    }
}
