using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using PS.Game.Sim.Content;
using PS.Game.Sim.Level;

namespace PS.Game.Sim.Tests
{
    /// <summary>
    /// The zen mode (docs/design/game-modes.md): the free hero's walk with the sword on the beat by itself, every
    /// beat, every half beat on slow songs or every other beat on fast ones; the loudness scaling its strikes; and
    /// the horde reeling to the music, thrown further by heavy blows and staggered by a zone change.
    /// </summary>
    public class ZenModeTests
    {
        static GameContent Content => HordeFixture.Content;

        /// <summary>Beats and ticks only, the ticks at <paramref name="level"/>, at their normal known-at times.</summary>
        static List<KnownEntry> Plain(double bpm, double until, LoudnessLevel level = LoudnessLevel.Medium, double evidence = 0.9) =>
            Drive.Known(new FixtureSong(3).Beats(0, until, bpm, evidence: evidence, level: level).Entries()
                .Where(e => e.Kind == EntryKind.Beat || e.Kind == EntryKind.Tick));

        static Simulation Start(SimConfig config, IEnumerable<KnownEntry> known, GameMode mode = GameMode.Zen)
        {
            config.Mode = mode;
            var sim = new Simulation(config);
            foreach (var k in known) sim.AddKnown(k.Entry, k.KnownAt);
            return sim;
        }

        /// <summary>A hero with the sword at rank 1 (or none) and no horde of its own.</summary>
        static SimConfig Config(bool sword = true)
        {
            var config = HordeFixture.Config(minimum: 0, roster: new string[0], heroStands: true);
            if (sword) config.Arsenal = new ArsenalConfig { Weapons = new[] { ("sword", 1) }, Cards = false };
            return config;
        }

        static List<AttackEvent> Swings(Simulation sim) =>
            Drive.Events(sim, SimEventKind.Attack).Select(e => e.AsAttack).Where(a => a.IsSword).ToList();

        static List<double> Beats(IEnumerable<KnownEntry> known) =>
            known.Where(k => k.Entry.Kind == EntryKind.Beat).Select(k => k.Entry.T).OrderBy(t => t).ToList();

        static double Reach(bool heavy = false) =>
            Content.Weapon("sword").Shape.RadiusM * Content.Weapon("sword").Rank(1).Area * (heavy ? Content.Weapon("sword").Shape.HeavyReachScale : 1);

        static int Place(Simulation sim, string role, double dx, double dy) =>
            HordeFixture.Place(sim, role, sim.State.Hero.X + dx, sim.State.Hero.Y + dy);

        // ---------------------------------------------------------------- the mode

        [Test]
        public void ZenWalksFreelyAndLeavesTheSwordToTheBeat()
        {
            Assert.AreEqual(GameMode.Zen, GameModes.Parse("zen"));
            var rules = GameModes.Of(GameMode.Zen);
            Assert.AreEqual(HeroMovement.Free, rules.Movement);
            Assert.AreEqual(SwordTrigger.Beat, rules.Sword);
            Assert.IsTrue(rules.Pointer, "a click sets the hero's target");
            Assert.IsFalse(rules.Bots, "the bots steer a grid hero");

            var known = Plain(120, 14);
            var beats = Beats(known);
            var sim = Start(Config(), known);
            sim.StepTo(5);
            sim.Press(Key.Right, 5);
            sim.StepTo(8);
            sim.Press(Key.Attack, 8);
            sim.Release(Key.Attack, 8.02);
            sim.StepTo(12);
            Assert.Greater(Drive.Events(sim, SimEventKind.HeroGlide).Count, 0, "the hero walked");
            Assert.AreEqual(0, Drive.Events(sim, SimEventKind.HeroStep).Count, "with no hop");
            var impacts = Swings(sim).Where(a => a.Impact > 5 && a.Impact < 12).ToList();
            CollectionAssert.AreEqual(beats.Where(t => t > 5 && t < 12).ToList(), impacts.Select(a => a.Impact).ToList(),
                "a strike on every beat while walking, and none on the attack key");
            Assert.IsTrue(impacts.All(a => !a.Pressed), "nothing is graded");
        }

        // ---------------------------------------------------------------- on the beat

        /// <summary>
        /// The grid the sword's impacts must land on: every beat at ordinary tempos, every half beat under 80 BPM,
        /// every other beat over 150, each impact exactly on its point and each point struck.
        /// </summary>
        [TestCase(70.0, 2, 1)]
        [TestCase(120.0, 1, 1)]
        [TestCase(170.0, 1, 2)]
        public void SlashesLandOnTheBeatGridAtEveryTempo(double bpm, int perBeat, int everyBeats)
        {
            var known = Plain(bpm, 24);
            var beats = Beats(known);
            var sim = Start(Config(), known);
            sim.StepTo(4);
            sim.Press(Key.Up, 4);
            sim.StepTo(20);
            var swings = Swings(sim).Where(a => a.Impact > 6 && a.Impact < 18).ToList();
            var grid = new List<double>();
            for (int b = 0; b + 1 < beats.Count; b++)
                for (int k = 0; k < perBeat; k++) grid.Add(beats[b] + (beats[b + 1] - beats[b]) * k / perBeat);
            foreach (var a in swings)
                Assert.AreEqual(0, grid.Min(g => Math.Abs(g - a.Impact)), 1e-9, $"the impact at {a.Impact} is on the grid");
            var gaps = swings.Zip(swings.Skip(1), (a, b) => b.Impact - a.Impact).ToList();
            double expected = 60 / bpm * everyBeats / perBeat;
            Assert.IsTrue(gaps.All(g => Math.Abs(g - expected) < 1e-6), $"every point struck, {expected:F3} s apart: {string.Join(", ", gaps.Select(g => g.ToString("F3")))}");
            Assert.IsTrue(swings.All(a => a.SwordKind != HeroAttackKind.FollowUp), "each one a slash of its own");
        }

        [Test]
        public void TheGridModeStillStrikesOncePerBeatOnASlowSong()
        {
            var known = Plain(70, 20);
            var beats = Beats(known);
            var sim = Start(Config(), known, GameMode.Grid);
            sim.StepTo(18);
            CollectionAssert.AreEqual(beats.Where(t => t > 6 && t < 18).ToList(),
                Swings(sim).Where(a => a.Impact > 6 && a.Impact < 18).Select(a => a.Impact).ToList());
        }

        // ---------------------------------------------------------------- a real song

        /// <summary>The run's level on the game's arena in zen, house-128's first 20 s, the hero walking a square.</summary>
        static Simulation RealRun(List<KnownEntry> known, double until)
        {
            var config = new RunInputs
            {
                Seed = 3, Level = SessionLevel.Run, Arena = true, Footprint = RunLevel.GameFootprint, Mode = GameMode.Zen,
            }.Build(Content, HordeFixture.GameArena());
            var sim = new Simulation(config);
            foreach (var k in known) sim.AddKnown(k.Entry, k.KnownAt);
            var keys = new[] { Key.Right, Key.Up, Key.Left, Key.Down };
            for (int i = 0; i < 9; i++)
            {
                double at = 1 + 2 * i;
                if (at >= until) break;
                sim.StepTo(at);
                if (i > 0) sim.Release(keys[(i - 1) % 4], at);
                sim.Press(keys[i % 4], at);
            }
            sim.StepTo(until);
            return sim;
        }

        /// <summary>The dump with every entry known just before its own time, as after an analyzer stall.</summary>
        static List<KnownEntry> JustInTime(List<KnownEntry> known) =>
            known.Select(k => new KnownEntry(k.Entry, Math.Max(0, k.Entry.T - (k.Entry.Kind == EntryKind.Beat ? 0.03 : 0.01)))).ToList();

        [Test]
        public void ARealSongsSwingsEachWindUpFromTheImpactBefore([Values(false, true)] bool justInTime)
        {
            var known = Drive.Known(EntryDump.Load(EntryDump.House128, out double duration));
            if (justInTime) known = JustInTime(known);
            var swings = Swings(RealRun(known, duration));
            Assert.Greater(swings.Count, 20);
            for (int i = 1; i < swings.Count; i++)
            {
                Assert.GreaterOrEqual(swings[i].Impact, swings[i - 1].Impact, $"swing {i} lands in order");
                Assert.GreaterOrEqual(swings[i].WindupStart, swings[i - 1].Impact - 1e-9,
                    $"swing {i} winds up at {swings[i].WindupStart:F4}, before the impact at {swings[i - 1].Impact:F4} lands");
            }
        }

        /// <summary>
        /// The causality test (CausalityTests) on a zen run, sword, loudness and reeling horde: truncating or perturbing
        /// the stream after a cut changes nothing settled before it.
        /// </summary>
        [Test]
        public void AZenRunIsCausal([Values(false, true)] bool justInTime)
        {
            var known = Drive.Known(EntryDump.Load(EntryDump.House128, out double duration));
            if (justInTime) known = JustInTime(known);
            for (double cut = 2.85; cut < duration; cut += 3.1)
            {
                double at = Math.Floor(cut * 120) / 120;
                var full = RealRun(known, at);
                Assert.Greater(Swings(full).Count, 0);
                foreach (var (what, changed) in new[]
                {
                    ("truncated", known.Where(k => k.KnownAt <= at).ToList()),
                    ("perturbed", Perturbed(known, at, (ulong)(cut * 10))),
                })
                {
                    var other = RealRun(changed, at);
                    var a = full.EventsSince(0);
                    var b = other.EventsSince(0);
                    Assert.AreEqual(a.Length, b.Length, $"{what}: event count at the cut {at}");
                    for (int i = 0; i < a.Length; i++) Assert.AreEqual(a[i], b[i], $"{what}: event {i} at the cut {at}");
                    Assert.AreEqual(full.Hash, other.Hash, $"{what}: hash at the cut {at}");
                }
            }
        }

        /// <summary>After <paramref name="cut"/>, entries' times, values and known-at times moved and a fifth dropped.</summary>
        static List<KnownEntry> Perturbed(List<KnownEntry> known, double cut, ulong seed)
        {
            var rng = new SimRandom(seed);
            var list = new List<KnownEntry>();
            foreach (var k in known)
            {
                if (k.KnownAt <= cut)
                {
                    list.Add(k);
                    continue;
                }
                if (rng.NextDouble() < 0.2) continue;
                var p = k.Entry;
                p.T += rng.Range(-0.3, 0.3);
                p.V0 = rng.NextDouble();
                p.V1 = rng.NextDouble();
                list.Add(new KnownEntry(p, cut + (k.KnownAt - cut) * rng.Range(1e-3, 2)));
            }
            return list;
        }

        // ---------------------------------------------------------------- loudness

        [TestCase(LoudnessLevel.Quiet, 0.75, 1)]
        [TestCase(LoudnessLevel.Medium, 1.0, 1)]
        [TestCase(LoudnessLevel.Loud, 1.25, 2)]
        public void LoudnessScalesTheStrikesAndALoudBeatStrikesOnItsEighthToo(LoudnessLevel level, double scale, int perBeat)
        {
            var known = Plain(120, 16, level);
            var beats = Beats(known);
            var sim = Start(Config(), known);
            sim.StepTo(14);
            double from = beats[12], to = beats[24];
            var swings = Swings(sim).Where(a => a.Impact >= from && a.Impact < to).ToList();
            Assert.IsTrue(swings.All(a => Math.Abs(a.Reach - Reach() * scale) < 1e-9), "the reach follows the loudness");
            Assert.AreEqual(12 * perBeat, swings.Count);
            foreach (var a in swings.Where(a => a.SwordKind == HeroAttackKind.FollowUp))
                Assert.AreEqual(0, beats.Min(t => Math.Abs(t + 0.25 - a.Impact)), 1e-6, "a loud beat's extra strike on its eighth");

            var grid = Start(Config(), Plain(120, 16, level), GameMode.Grid);
            grid.StepTo(14);
            Assert.IsTrue(Swings(grid).All(a => Math.Abs(a.Reach - Reach()) < 1e-9), "the grid mode's strikes are as ever");
        }

        [TestCase(LoudnessLevel.Medium, new[] { 8, 10, 12, 14 })]
        [TestCase(LoudnessLevel.Loud, new[] { 8, 9, 10, 11, 12, 13, 14 })]
        public void OnLoudBeatsAccentsInARowAreAllHeavy(LoudnessLevel level, int[] expected)
        {
            var known = Plain(120, 16, level);
            var beats = Beats(known);
            for (int b = 8; b <= 14; b++) known.Add(new KnownEntry(StreamEntry.Onset(beats[b], 0, 300, true), beats[b] - 2));
            var sim = Start(Config(), known);
            sim.StepTo(16);
            CollectionAssert.AreEqual(expected, Swings(sim).Where(a => a.Heavy).Select(a => beats.IndexOf(a.Impact)).ToList());
        }

        // ---------------------------------------------------------------- the horde reels

        [Test]
        public void HeavyBlowsThrowTheHordeFurther()
        {
            double Thrown(GameMode mode)
            {
                // Beats below the gate: the enemies hold and the sword still strikes.
                var known = Plain(120, 12, evidence: 0.1);
                var beats = Beats(known);
                var sim = Start(Config(), known, mode);
                sim.StepTo(beats[8] + 0.01);
                int werewolf = Place(sim, "werewolf", 3, 0);
                sim.Command(DebugCommand.Accent);
                sim.StepTo(beats[9] + 0.01);
                Assert.IsTrue(Swings(sim).Single(a => a.Impact == beats[9]).Heavy);
                var knock = Drive.Events(sim, SimEventKind.EnemyKnock).Select(e => e.AsEnemyKnock).Single(k => k.Enemy == werewolf);
                return knock.ToX - knock.FromX;
            }
            double grid = Thrown(GameMode.Grid), zen = Thrown(GameMode.Zen);
            Assert.AreEqual(Horde.KnockbackM * Content.Role("werewolf").MaxKnockback, grid, 1e-9, "capped by its role");
            Assert.AreEqual(Horde.ReelKnockScale * grid, zen, 1e-9, "the cap too");
        }

        [Test]
        public void AZoneChangeShovesTheHordeBackAndStaggersItForABeat()
        {
            (int enemy, ZoneChangeEvent change, List<EnemyStepEvent> steps, List<EnemyKnockEvent> knocks) Run(GameMode mode)
            {
                var known = Plain(120, 20);
                var sim = Start(Config(sword: false), known, mode);
                sim.StepTo(6);
                int bat = Place(sim, "bat1", 12, 0);
                sim.Command(DebugCommand.SectionChange);
                sim.StepTo(10);
                var change = Drive.Events(sim, SimEventKind.ZoneChange).Single().AsZoneChange;
                var steps = Drive.Events(sim, SimEventKind.EnemyStep).Select(e => e.AsEnemyStep).Where(s => s.Enemy == bat).ToList();
                var knocks = Drive.Events(sim, SimEventKind.EnemyKnock).Select(e => e.AsEnemyKnock).Where(k => k.Enemy == bat).ToList();
                return (bat, change, steps, knocks);
            }

            var zen = Run(GameMode.Zen);
            double t = zen.change.T;
            var shove = zen.knocks.Single();
            Assert.AreEqual(t, shove.Start, 1e-9, "shoved on the zone change's downbeat");
            Assert.Greater(shove.ToX, shove.FromX, "straight back from the hero");
            Assert.AreEqual(Horde.KnockbackM * Content.Role("bat1").MaxKnockback,
                Math.Sqrt((shove.ToX - shove.FromX) * (shove.ToX - shove.FromX) + (shove.ToY - shove.FromY) * (shove.ToY - shove.FromY)), 1e-9);
            Assert.IsFalse(zen.steps.Any(s => s.Footplant > t + 1e-9 && s.Footplant < t + 0.5 + 1e-6), "no step on the beat after");
            Assert.IsTrue(zen.steps.Any(s => Math.Abs(s.Footplant - (t + 1.0)) < 1e-6), "pursuit resumes on the beat after that");

            var grid = Run(GameMode.Grid);
            Assert.IsEmpty(grid.knocks, "the grid mode's horde doesn't reel");
            Assert.IsTrue(grid.steps.Any(s => Math.Abs(s.Footplant - (grid.change.T + 0.5)) < 1e-6));
        }
    }
}
