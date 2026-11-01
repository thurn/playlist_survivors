using System;

namespace PS.Game.Sim
{
    /// <summary>
    /// A way of playing (`--mode`, docs/design/game-modes.md): a run input, recorded in sessions, that picks the
    /// rules below. Append new modes: a session stores the number.
    /// </summary>
    public enum GameMode : byte
    {
        /// <summary>The game as designed: the hero hops a grid on the stride steps, the sword strikes on every beat by itself.</summary>
        Grid = 0,
        /// <summary>The hero walks freely; the sword strikes on Z, its reach and damage graded by the press's timing.</summary>
        Free = 1,
    }

    /// <summary>How the hero moves.</summary>
    public enum HeroMovement : byte
    {
        /// <summary>One tile per stride step, four ways (<see cref="Hero"/>).</summary>
        Grid,
        /// <summary>Continuously, eight ways or to a pointer's target, at the grid's pace (<see cref="FreeHero"/>).</summary>
        Free,
    }

    /// <summary>What sets the sword swinging.</summary>
    public enum SwordTrigger : byte
    {
        /// <summary>Every strike beat, by itself (<see cref="Sword"/>).</summary>
        Beat,
        /// <summary>The attack key, graded by how close to the beat it was heard (<see cref="BeatGrade"/>).</summary>
        Press,
    }

    /// <summary>
    /// One mode's rules: each field is one system's choice, read where that system is built (the sim's in
    /// <see cref="Simulation"/>, the client's input and presentation in the game loop). A new mode is a row in
    /// <see cref="GameModes.All"/>; a rule no mode has yet is a new field here, its default today's behaviour.
    /// </summary>
    public sealed class ModeRules
    {
        public readonly GameMode Mode;
        /// <summary>The name `--mode` takes and run.json shows.</summary>
        public readonly string Name;
        public readonly string Summary;
        public readonly HeroMovement Movement;
        public readonly SwordTrigger Sword;
        /// <summary>Whether the bots can play it: they steer a grid hero and leave the sword to the beat.</summary>
        public readonly bool Bots;

        public ModeRules(GameMode mode, string name, string summary, HeroMovement movement, SwordTrigger sword, bool bots)
        {
            Mode = mode;
            Name = name;
            Summary = summary;
            Movement = movement;
            Sword = sword;
            Bots = bots;
        }

        /// <summary>Whether a left click (or a held button) sets the hero's target.</summary>
        public bool Pointer => Movement == HeroMovement.Free;
    }

    public static class GameModes
    {
        public static readonly ModeRules[] All =
        {
            new ModeRules(GameMode.Grid, "grid", "the grid hero; the sword strikes on the beat by itself",
                HeroMovement.Grid, SwordTrigger.Beat, bots: true),
            new ModeRules(GameMode.Free, "free", "walk freely (arrows, or click to move); Z strikes, bigger on the beat",
                HeroMovement.Free, SwordTrigger.Press, bots: false),
        };

        /// <summary>The rules of <paramref name="mode"/>.</summary>
        public static ModeRules Of(GameMode mode)
        {
            foreach (var rules in All)
                if (rules.Mode == mode) return rules;
            throw new ArgumentOutOfRangeException(nameof(mode), mode, "no such mode");
        }

        /// <summary>Whether a session's stored mode is one this build knows.</summary>
        public static bool Known(byte mode) => Array.Exists(All, r => (byte)r.Mode == mode);

        /// <summary>The mode `--mode` names; an unknown name throws, listing the modes.</summary>
        public static GameMode Parse(string name)
        {
            foreach (var rules in All)
                if (rules.Name == name) return rules.Mode;
            throw new ArgumentException($"--mode: '{name}' is not one of {string.Join(", ", Array.ConvertAll(All, r => r.Name))}");
        }
    }
}
