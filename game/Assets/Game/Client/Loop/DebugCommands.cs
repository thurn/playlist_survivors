using System;
using System.Collections.Generic;
using System.Globalization;
using PS.Game.Sim;
using PS.Game.Sim.Content;
using UnityEngine.InputSystem;

namespace Game.Client.Loop
{
    /// <summary>
    /// The debug commands' inputs (unity-game.md, Debug Surfaces; <see cref="DebugCommand"/>): the F1 menu
    /// (<see cref="DebugMenu"/>), F5 to F8 (a section change, an accent, the structures near the hero broken, the
    /// broken ones regrown), and `--debug "change@20,spawn:werewolf@12.5,jump:25@3"`, which gives them at song times
    /// instead, for headless runs and stills: each when the sim first reaches its time at a frame's start, so a
    /// fixed-time rerun gives it on the same tick. The names: change, accent, break, regrow, finale, chest; the
    /// music-driven events stampede, kick, wedge, ring, bats, ghosts, flowers and minotaur; and those that take an
    /// argument: jump:MINUTES (the run clock to that minute), spawn:ROLE and boss:ROLE, weapon:ID, passive:ID and
    /// evolve:ID (game/Content's ids), and invulnerable (invulnerable:0 ends it).
    /// </summary>
    public sealed class DebugCommands
    {
        enum Arg { None, Minutes, Role, Weapon, Passive, Evolution, Flag }

        static readonly (string name, DebugCommand command, Arg arg)[] Names =
        {
            ("change", DebugCommand.SectionChange, Arg.None), ("accent", DebugCommand.Accent, Arg.None),
            ("break", DebugCommand.BreakStructures, Arg.None), ("regrow", DebugCommand.RegrowStructures, Arg.None),
            ("stampede", DebugCommand.Stampede, Arg.None), ("kick", DebugCommand.KickLine, Arg.None),
            ("wedge", DebugCommand.Wedge, Arg.None), ("ring", DebugCommand.Ring, Arg.None),
            ("bats", DebugCommand.BatSwarm, Arg.None), ("ghosts", DebugCommand.GhostSwarm, Arg.None),
            ("flowers", DebugCommand.FlowerWall, Arg.None), ("minotaur", DebugCommand.Minotaur, Arg.None),
            ("finale", DebugCommand.Finale, Arg.None), ("chest", DebugCommand.Chest, Arg.None),
            ("jump", DebugCommand.JumpClock, Arg.Minutes), ("spawn", DebugCommand.SpawnRole, Arg.Role),
            ("boss", DebugCommand.SpawnBoss, Arg.Role), ("weapon", DebugCommand.GrantWeapon, Arg.Weapon),
            ("passive", DebugCommand.GrantPassive, Arg.Passive), ("evolve", DebugCommand.GrantEvolution, Arg.Evolution),
            ("invulnerable", DebugCommand.Invulnerable, Arg.Flag),
        };

        readonly List<(double at, DebugCommand command, int arg)> _scheduled;
        int _next;

        public DebugCommands(string schedule, GameContent content) => _scheduled = Parse(schedule, content);

        /// <summary>Commands given so far.</summary>
        public int Given { get; private set; }

        /// <summary>`name[:argument]@seconds`, comma-separated, in any order; <paramref name="content"/> resolves the ids.</summary>
        public static List<(double at, DebugCommand command, int arg)> Parse(string schedule, GameContent content)
        {
            var list = new List<(double, DebugCommand, int)>();
            if (string.IsNullOrWhiteSpace(schedule)) return list;
            foreach (var item in schedule.Split(','))
            {
                var parts = item.Trim().Split('@');
                var head = parts[0].Trim().Split(':');
                int i = parts.Length == 2 && head.Length <= 2 ? Array.FindIndex(Names, n => n.name == head[0].Trim()) : -1;
                if (i < 0 || !double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double at))
                    throw new ArgumentException($"--debug: '{item}' is not one of {string.Join(", ", Array.ConvertAll(Names, n => n.name))} @ seconds");
                list.Add((at, Names[i].command, Argument(Names[i].arg, head.Length == 2 ? head[1].Trim() : null, content, item)));
            }
            list.Sort((a, b) => a.Item1.CompareTo(b.Item1));
            return list;
        }

        static int Argument(Arg kind, string text, GameContent content, string item)
        {
            if (kind == Arg.None)
                return text == null ? 0 : throw new ArgumentException($"--debug: '{item}' takes no argument");
            if (kind == Arg.Flag)
                return text == null || text == "1" ? 1 : text == "0" ? 0 : throw new ArgumentException($"--debug: '{item}' takes 0 or 1");
            if (text == null) throw new ArgumentException($"--debug: '{item}' needs an argument (name:argument@seconds)");
            if (kind == Arg.Minutes)
                return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double minutes) && minutes >= 0
                    ? (int)Math.Round(minutes * 60)
                    : throw new ArgumentException($"--debug: '{item}' needs minutes");
            if (content == null) throw new ArgumentException($"--debug: '{item}' names content, and this run has none");
            int index = kind == Arg.Role ? content.RoleIndex(text)
                : kind == Arg.Passive ? content.PassiveIndex(text)
                : content.WeaponIndex(text);
            bool fits = index >= 0 && (kind != Arg.Weapon || !content.Weapons[index].IsEvolution)
                                   && (kind != Arg.Evolution || content.Weapons[index].IsEvolution);
            return fits ? index : throw new ArgumentException($"--debug: '{item}': no {kind.ToString().ToLowerInvariant()} '{text}' in the content");
        }

        /// <summary>Gives the sim the commands due by its time now and the function keys pressed this frame.</summary>
        public void Apply(Simulation sim)
        {
            for (; _next < _scheduled.Count && sim.Now >= _scheduled[_next].at - 1e-9; _next++)
                Give(sim, _scheduled[_next].command, _scheduled[_next].arg);
            var keyboard = Keyboard.current;
            if (keyboard == null) return;
            if (keyboard.f5Key.wasPressedThisFrame) Give(sim, DebugCommand.SectionChange);
            if (keyboard.f6Key.wasPressedThisFrame) Give(sim, DebugCommand.Accent);
            if (keyboard.f7Key.wasPressedThisFrame) Give(sim, DebugCommand.BreakStructures);
            if (keyboard.f8Key.wasPressedThisFrame) Give(sim, DebugCommand.RegrowStructures);
        }

        /// <summary>Gives the sim a command now, as the menu does.</summary>
        public void Give(Simulation sim, DebugCommand command, int arg = 0)
        {
            sim.Command(command, arg);
            Given++;
        }
    }
}
