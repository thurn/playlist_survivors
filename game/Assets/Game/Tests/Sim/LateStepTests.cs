using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using NUnit.Framework;

namespace PS.Game.Sim.Tests
{
    /// <summary>
    /// Late steps (unity-game.md, "Key Presses"): the sim takes every key the lead after the player heard
    /// it, so it judges moves in heard time and draws them from then. This measures what the player sees
    /// against native's classic mode (a tap moves one tile on its key-down frame, a turn turns at once),
    /// for keys at every phase of the stride step, at the speakers' lead (90 ms) and a Bluetooth lead
    /// (250 ms), at 100, 128 and 170 BPM. Keys reach the sim as the client gives them: each at the start of
    /// the first frame after it was heard (60 fps), at the previous frame's frontier, so a key's lag varies
    /// by up to a frame with where in its frame it fell. Before each scenario the player has pressed Space
    /// sixteen times, at sixteenths of a frame, as any run has keys before the one measured: the held
    /// strides wait for the recent keys' longest lag.
    /// - <b>Tap</b>: a 80 ms tap from rest. It should move one tile, drawn from the key-down.
    /// - <b>Turn</b>: Up held, then Right pressed and Up released together. Right should set off at once
    ///   and no Up stride should start after the turn was heard (a ghost: the hero hops diagonally),
    ///   unless the press takes its place (taken back: the sim judged it before the press arrived).
    ///   Up's press falls on a frame or just after one, the smallest and the largest lag it can have.
    /// - <b>Roll</b>: the same turn with Right pressed 30 ms before Up is released (a rollover).
    /// - <b>Double tap</b>: Right tapped, then Up tapped 120 ms after: each a tile, the second on its key-down
    ///   unless the first took its step (one press stride per step), when it waits for the next step.
    /// The cut is how much of the stride in progress a press cuts short, and a press's own stride is 110 ms
    /// unless the next step that may stride comes sooner. Held taken is how long after its step the sim took
    /// the held stride before a turn: the lead at most, so the client has it about a display latency after
    /// its step (and draws it from the frame it arrives, HeroMotion).
    /// </summary>
    public class LateStepTests
    {
        const double Frame = 1 / 60.0;
        const int Phases = 48;
        static readonly double[] Bpms = { 100, 128, 170 };
        static readonly double[] Leads = { 0.09, 0.25 };

        struct KeyChange
        {
            public double Heard;
            public Key Key;
            public bool Down;
            public KeyChange(double heard, Key key, bool down) { Heard = heard; Key = key; Down = down; }
        }

        /// <summary>What one scenario at one phase showed.</summary>
        struct Outcome
        {
            /// <summary>The response set off at once, drawn from its key-down (each key's, for a double tap).</summary>
            public bool AtOnce;
            /// <summary>The response's lateness against its key-down (s): 0 at once, about a step when late.</summary>
            public double Late;
            /// <summary>The hero ended where classic mode puts it.</summary>
            public bool Placed;
            /// <summary>A stride in the old direction set off after the turn was heard, and the hero kept it.</summary>
            public bool Ghost;
            /// <summary>Such a stride, which the turn's press took the place of (the sim had judged it before the press arrived).</summary>
            public bool TakenBack;
            /// <summary>The press stride's drawn length (s), and how much of a stride in progress it cut short (s).</summary>
            public double Stride, Cut;
            /// <summary>How long after its step the sim took the latest held stride (s): the client gets it then.</summary>
            public double HeldTaken;
        }

        /// <summary>
        /// Plays <paramref name="keys"/> (in heard order) on <paramref name="bpm"/>'s beats known well ahead,
        /// frame by frame: frame n's start hears song n / 60, takes the keys heard since the frame before at
        /// the sim's time then (the previous frontier), then steps the sim to its own frontier, the lead on.
        /// </summary>
        static Simulation Play(double bpm, double lead, KeyChange[] scenario, double until)
        {
            var sim = new Simulation(new SimConfig());
            foreach (var k in Drive.Known(new FixtureSong().Beats(0, 30, bpm).Entries())) sim.AddKnown(k.Entry, k.KnownAt);
            var keys = new List<KeyChange>();
            for (int i = 0; i < 16; i++)
            {
                double at = 1 + 0.3 * i + Frame * i / 16;
                keys.Add(new KeyChange(at, Key.Space, true));
                keys.Add(new KeyChange(at + 0.05, Key.Space, false));
            }
            keys.AddRange(scenario);
            for (int i = 1; i < keys.Count; i++)
                if (keys[i].Heard < keys[i - 1].Heard) throw new ArgumentException("keys out of heard order");
            int next = 0;
            for (int n = 0; n * Frame < until; n++)
            {
                double heard = n * Frame;
                for (; next < keys.Count && keys[next].Heard <= heard + 1e-12; next++)
                {
                    if (keys[next].Down) sim.Press(keys[next].Key, keys[next].Heard);
                    else sim.Release(keys[next].Key, keys[next].Heard);
                }
                sim.StepTo(heard + lead);
            }
            return sim;
        }

        /// <summary>The hero's moves, without the stomps before the scenario.</summary>
        static List<HeroStepEvent> Moves(Simulation sim)
        {
            var list = new List<HeroStepEvent>();
            foreach (var e in Drive.Events(sim, SimEventKind.HeroStep))
                if (!e.AsHeroStep.Stomp) list.Add(e.AsHeroStep);
            return list;
        }

        /// <summary>The stride grid's step at or after 10 s and its period, on <paramref name="bpm"/>'s beats.</summary>
        static (double t0, double period) Grid(double bpm)
        {
            var sim = Play(bpm, 0.09, Array.Empty<KeyChange>(), 12);
            foreach (var e in Drive.Events(sim, SimEventKind.StrideStep))
                if (e.T >= 10) return (e.T, e.AsStrideStep.Period);
            throw new InvalidOperationException("no stride step after 10 s");
        }

        static Outcome Tap(double bpm, double lead, double h)
        {
            var sim = Play(bpm, lead, new[] { new KeyChange(h, Key.Right, true), new KeyChange(h + 0.08, Key.Right, false) }, h + 2);
            var moves = Moves(sim);
            var o = new Outcome { Placed = moves.Count == 1 && sim.State.Hero.TileX == sim.Arena.HeroStart.X + 1 };
            if (moves.Count == 0) return o;
            o.Late = moves[0].Start - h;
            o.AtOnce = Math.Abs(o.Late) < 1e-9;
            o.Stride = moves[0].End - moves[0].Start;
            return o;
        }

        /// <summary>Up held from a key heard on a frame (<paramref name="upOnFrame"/>) or just after one, turning Right at h.</summary>
        static Outcome Turn(double bpm, double lead, double h, bool upOnFrame, double rollover)
        {
            double up = Math.Floor((h - 2) / Frame) * Frame + (upOnFrame ? 0 : 1e-4);
            var keys = new List<KeyChange> { new KeyChange(up, Key.Up, true) };
            if (rollover > 0)
            {
                keys.Add(new KeyChange(h, Key.Right, true));
                keys.Add(new KeyChange(h + rollover, Key.Up, false));
            }
            else
            {
                keys.Add(new KeyChange(h, Key.Up, false));
                keys.Add(new KeyChange(h, Key.Right, true));
            }
            keys.Add(new KeyChange(h + 0.08, Key.Right, false));
            var sim = Play(bpm, lead, keys.ToArray(), h + 2);
            var moves = Moves(sim);
            var o = new Outcome();
            int ups = 0;
            HeroStepEvent? right = null, before = null;
            for (int i = 0; i < moves.Count; i++)
            {
                var m = moves[i];
                if (m.Facing == Direction.Up)
                {
                    if (m.Start >= h - 1e-9)
                    {
                        bool replaced = i + 1 < moves.Count && moves[i + 1].Pressed && moves[i + 1].Start <= m.Start + 1e-9;
                        if (replaced) o.TakenBack = true;
                        else o.Ghost = true;
                    }
                    else
                    {
                        ups++;
                        before = m;
                        if (!m.Pressed) o.HeldTaken = m.T - m.Start;
                    }
                }
                else if (m.Facing == Direction.Right && right == null) right = m;
            }
            if (right is HeroStepEvent r)
            {
                o.Late = r.Start - h;
                o.AtOnce = Math.Abs(o.Late) < 1e-9;
                o.Stride = r.End - r.Start;
                if (before is HeroStepEvent b && b.End > r.Start) o.Cut = b.End - r.Start;
            }
            var hero = sim.State.Hero;
            o.Placed = hero.TileX == sim.Arena.HeroStart.X + 1 && hero.TileY == sim.Arena.HeroStart.Y + ups;
            return o;
        }

        static Outcome DoubleTap(double bpm, double lead, double h)
        {
            const double gap = 0.12;
            var sim = Play(bpm, lead, new[]
            {
                new KeyChange(h, Key.Right, true), new KeyChange(h + 0.05, Key.Right, false),
                new KeyChange(h + gap, Key.Up, true), new KeyChange(h + gap + 0.05, Key.Up, false),
            }, h + 2);
            var moves = Moves(sim);
            var o = new Outcome
            {
                Placed = moves.Count == 2 && sim.State.Hero.TileX == sim.Arena.HeroStart.X + 1
                         && sim.State.Hero.TileY == sim.Arena.HeroStart.Y + 1,
            };
            if (moves.Count < 2) return o;
            o.Late = Math.Max(moves[0].Start - h, moves[1].Start - (h + gap));
            o.AtOnce = Math.Abs(o.Late) < 1e-9;
            o.Stride = moves[1].End - moves[1].Start;
            if (moves[0].End > moves[1].Start) o.Cut = moves[0].End - moves[1].Start;
            return o;
        }

        /// <summary>One scenario's outcomes over every phase of the step, as a row.</summary>
        sealed class Row
        {
            public string Scenario;
            public double Bpm, Lead;
            public readonly List<Outcome> Outcomes = new List<Outcome>();

            public int AtOnce => Outcomes.FindAll(o => o.AtOnce).Count;
            public int Placed => Outcomes.FindAll(o => o.Placed).Count;
            public int Ghosts => Outcomes.FindAll(o => o.Ghost).Count;
            public int TakenBack => Outcomes.FindAll(o => o.TakenBack).Count;
            public double MaxLate => Outcomes.ConvertAll(o => o.Late).TrueForAll(_ => true) ? Max(o => o.Late) : 0;
            public double MinStride => -Max(o => -o.Stride);
            public double MaxCut => Max(o => o.Cut);
            public double MaxHeldTaken => Max(o => o.HeldTaken);

            double Max(Func<Outcome, double> f)
            {
                double m = double.NegativeInfinity;
                foreach (var o in Outcomes) m = Math.Max(m, f(o));
                return m;
            }

            public override string ToString() => string.Format(CultureInfo.InvariantCulture,
                "| {0} | {1:F0} | {2:F0} | {3}/{4} | {5:F0} | {6}/{4} | {7} | {11} | {8:F0} | {9:F0} | {10} |",
                Scenario, Bpm, Lead * 1000, AtOnce, Outcomes.Count, MaxLate * 1000, Placed, Ghosts, MinStride * 1000, MaxCut * 1000,
                MaxHeldTaken > 0 ? (MaxHeldTaken * 1000).ToString("F0", CultureInfo.InvariantCulture) : "", TakenBack);
        }

        static List<Row> Measure()
        {
            var rows = new List<Row>();
            foreach (double bpm in Bpms)
            {
                var (t0, period) = Grid(bpm);
                foreach (double lead in Leads)
                {
                    var tap = new Row { Scenario = "tap", Bpm = bpm, Lead = lead };
                    var turn = new Row { Scenario = "turn", Bpm = bpm, Lead = lead };
                    var roll = new Row { Scenario = "roll", Bpm = bpm, Lead = lead };
                    var twice = new Row { Scenario = "double tap", Bpm = bpm, Lead = lead };
                    for (int i = 0; i < Phases; i++)
                    {
                        double h = t0 + period * i / Phases;
                        tap.Outcomes.Add(Tap(bpm, lead, h));
                        foreach (bool onFrame in new[] { true, false })
                        {
                            turn.Outcomes.Add(Turn(bpm, lead, h, onFrame, 0));
                            roll.Outcomes.Add(Turn(bpm, lead, h, onFrame, 0.03));
                        }
                        twice.Outcomes.Add(DoubleTap(bpm, lead, h));
                    }
                    rows.Add(tap);
                    rows.Add(turn);
                    rows.Add(roll);
                    rows.Add(twice);
                }
            }
            return rows;
        }

        static string Table(List<Row> rows)
        {
            var sb = new StringBuilder();
            sb.AppendLine("| scenario | BPM | lead (ms) | at once | latest (ms) | placed | ghosts | taken back | shortest stride (ms) | largest cut (ms) | held taken (ms) |");
            sb.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|");
            foreach (var r in rows) sb.AppendLine(r.ToString());
            return sb.ToString();
        }

        [Test]
        public void TapsAndTurnsAnswerAtOnceAtEveryPhaseLeadAndTempo()
        {
            var rows = Measure();
            TestContext.Out.WriteLine(Table(rows));
            foreach (var r in rows)
            {
                Assert.AreEqual(r.Outcomes.Count, r.AtOnce, $"{r.Scenario} at {r.Bpm} BPM, lead {r.Lead}: set off at once");
                Assert.AreEqual(r.Outcomes.Count, r.Placed, $"{r.Scenario} at {r.Bpm} BPM, lead {r.Lead}: where classic mode puts it");
                Assert.AreEqual(0, r.Ghosts, $"{r.Scenario} at {r.Bpm} BPM, lead {r.Lead}: no stride the old way after the turn");
                // A held stride is taken the recent keys' longest lag after its step: at most the lead, and a tick.
                Assert.That(r.MaxHeldTaken, Is.LessThanOrEqualTo(r.Lead + Simulation.TickSeconds + 1e-9), $"{r.Scenario}: held strides taken by the lead");
            }
        }
    }
}
