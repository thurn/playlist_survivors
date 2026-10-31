using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using PS.Game.Sim;
using PS.Game.Sim.Content;
using UnityEngine;
using UnityEngine.InputSystem;
using InputKey = UnityEngine.InputSystem.Key;

namespace Game.Client.Loop
{
    /// <summary>
    /// The F1 debug menu (unity-game.md, Debug Surfaces): text at the view's top right while it is open. A row's
    /// letter gives its command (<see cref="DebugCommand"/>, recorded by the sim like a key, so sessions replay it), and
    /// Shift with the letter moves a row's choice (a role, a weapon) on. Its letters are none of the hero's keys (the
    /// arrows, WASD, Space and Enter), so the run plays on with it open: nothing pauses.
    /// </summary>
    public sealed class DebugMenu
    {
        sealed class Row
        {
            public InputKey Key;
            public string Label;
            public DebugCommand Command;
            /// <summary>Its choices' names and the commands' arguments; none for a plain command.</summary>
            public string[] Names = new string[0];
            public int[] Args = new int[0];
            public int Choice;
        }

        static readonly (string Name, DebugCommand Command)[] Events =
        {
            ("stampede", DebugCommand.Stampede), ("kick line", DebugCommand.KickLine), ("wedge", DebugCommand.Wedge),
            ("ring", DebugCommand.Ring), ("bat swarm", DebugCommand.BatSwarm), ("ghost swarm", DebugCommand.GhostSwarm),
            ("flower wall", DebugCommand.FlowerWall), ("minotaur", DebugCommand.Minotaur),
        };

        readonly Camera _camera;
        readonly TextMesh _text;
        readonly List<Row> _rows = new List<Row>();
        readonly StringBuilder _sb = new StringBuilder(1024);

        public DebugMenu(Camera camera, GameContent content)
        {
            _camera = camera;
            _text = ViewText.Make(camera, "Debug menu", right: true, new Color(1f, 0.85f, 0.55f));
            _text.gameObject.SetActive(false);
            Add(InputKey.J, "jump the clock to", DebugCommand.JumpClock);
            if (content != null)
            {
                var bosses = new List<int>();
                foreach (var minute in content.Schedule.Minutes)
                    foreach (var id in minute.Bosses)
                        if (!bosses.Contains(content.RoleIndex(id))) bosses.Add(content.RoleIndex(id));
                var weapons = new List<int>();
                var evolutions = new List<int>();
                for (int w = 0; w < content.Weapons.Length; w++) (content.Weapons[w].IsEvolution ? evolutions : weapons).Add(w);
                var roles = new int[content.Roles.Length];
                for (int r = 0; r < roles.Length; r++) roles[r] = r;
                var passives = new int[content.Passives.Length];
                for (int p = 0; p < passives.Length; p++) passives[p] = p;
                Add(InputKey.M, "spawn", DebugCommand.SpawnRole, roles, r => content.Roles[r].Id);
                Add(InputKey.B, "boss", DebugCommand.SpawnBoss, bosses.ToArray(), r => content.Roles[r].Id);
                Add(InputKey.G, "weapon", DebugCommand.GrantWeapon, weapons.ToArray(), w => content.Weapons[w].Id);
                Add(InputKey.P, "passive", DebugCommand.GrantPassive, passives, p => content.Passives[p].Id);
                Add(InputKey.E, "evolve", DebugCommand.GrantEvolution, evolutions.ToArray(), w => content.Weapons[w].Id);
            }
            var events = Add(InputKey.N, "event", DebugCommand.Stampede);
            events.Names = Array.ConvertAll(Events, e => e.Name);
            events.Args = Array.ConvertAll(Events, e => (int)e.Command);
            Add(InputKey.C, "section change", DebugCommand.SectionChange);
            Add(InputKey.X, "accent", DebugCommand.Accent);
            Add(InputKey.F, "finale", DebugCommand.Finale);
            Add(InputKey.T, "chest ahead", DebugCommand.Chest);
            Add(InputKey.K, "break structures", DebugCommand.BreakStructures);
            Add(InputKey.R, "regrow structures", DebugCommand.RegrowStructures);
            Add(InputKey.I, "invulnerable", DebugCommand.Invulnerable);
        }

        Row Add(InputKey key, string label, DebugCommand command, int[] args = null, Func<int, string> name = null)
        {
            var row = new Row { Key = key, Label = label, Command = command };
            if (args != null)
            {
                row.Args = args;
                row.Names = Array.ConvertAll(args, a => name(a));
            }
            _rows.Add(row);
            return row;
        }

        public bool Open => _text.gameObject.activeSelf;

        /// <summary>F1 opens or closes the menu; while it is open, the rows' letters pressed this frame.</summary>
        public void Apply(Simulation sim, DebugCommands commands)
        {
            var keyboard = Keyboard.current;
            if (keyboard == null) return;
            if (keyboard.f1Key.wasPressedThisFrame) _text.gameObject.SetActive(!Open);
            if (!Open) return;
            bool shift = keyboard.shiftKey.isPressed;
            foreach (var row in _rows)
                if (keyboard[row.Key].wasPressedThisFrame) Press(row.Key, shift, sim, commands);
        }

        /// <summary>A row's letter: its command now, or with Shift its next choice. False for a letter no row has.</summary>
        internal bool Press(InputKey key, bool shift, Simulation sim, DebugCommands commands)
        {
            var row = _rows.Find(r => r.Key == key);
            if (row == null) return false;
            if (shift)
            {
                if (row.Args.Length > 0) row.Choice = (row.Choice + 1) % row.Args.Length;
                return true;
            }
            if (row.Command == DebugCommand.JumpClock) commands.Give(sim, row.Command, NextMinute(sim) * 60);
            else if (row.Command == DebugCommand.Invulnerable) commands.Give(sim, row.Command, sim.State.Hero.Invulnerable ? 0 : 1);
            else if (row.Args.Length == 0) commands.Give(sim, row.Command);
            else if (row.Key == InputKey.N) commands.Give(sim, (DebugCommand)row.Args[row.Choice]);
            else commands.Give(sim, row.Command, row.Args[row.Choice]);
            return true;
        }

        static int NextMinute(Simulation sim) => (int)Math.Floor(sim.State.RunClock / 60 + 1e-9) + 1;

        /// <summary>The menu's text, while it is open.</summary>
        public void Present(Simulation sim)
        {
            if (!Open) return;
            ViewText.Place(_camera, _text);
            var c = CultureInfo.InvariantCulture;
            _sb.Clear();
            _sb.Append("DEBUG   F1 closes   letter: give   shift+letter: next\n");
            foreach (var row in _rows)
            {
                _sb.Append(row.Label);
                if (row.Command == DebugCommand.JumpClock) _sb.AppendFormat(c, " {0}:00", NextMinute(sim));
                else if (row.Command == DebugCommand.Invulnerable) _sb.Append(sim.State.Hero.Invulnerable ? ": on" : ": off");
                else if (row.Args.Length > 0) _sb.Append("  < ").Append(row.Names[row.Choice]).Append(" >");
                _sb.Append("   ").Append(row.Key.ToString().ToUpperInvariant()).Append('\n');
            }
            _text.text = _sb.ToString();
        }
    }
}
