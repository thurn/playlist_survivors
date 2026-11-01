using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using PS.Game.Sim.Content;
using PS.Game.Sim.Level;

namespace PS.Game.Sim.Tests
{
    /// <summary>
    /// The free mode (docs/design/game-modes.md): the free hero's walk (its pace, heard-time turns, walls and
    /// pointer targets) and the sword on the attack key (only on presses, graded by the beat, the swing's length,
    /// heavy on accents), the other weapons still on the beat.
    /// </summary>
    public class FreeModeTests
    {
        const double Tile = 1.5;

        static GameContent Content => HordeFixture.Content;

        /// <summary>Beats and ticks only, at their normal known-at times.</summary>
        static List<KnownEntry> Plain(double bpm, double until) =>
            Drive.Known(new FixtureSong(3).Beats(0, until, bpm, evidence: 0.9).Entries()
                .Where(e => e.Kind == EntryKind.Beat || e.Kind == EntryKind.Tick));

        static Simulation Start(SimConfig config, IEnumerable<KnownEntry> known)
        {
            config.Mode = GameMode.Free;
            var sim = new Simulation(config);
            foreach (var k in known) sim.AddKnown(k.Entry, k.KnownAt);
            return sim;
        }

        /// <summary>A free hero alone on <paramref name="arena"/> (the open one by default), on <paramref name="bpm"/> beats.</summary>
        static Simulation Walker(double bpm = 120, ArenaData arena = null) => Start(new SimConfig { Arena = arena }, Plain(bpm, 30));

        /// <summary>A free hero with weapons (the sword at rank 1 by default) and no horde of its own.</summary>
        static Simulation Fighter(IEnumerable<KnownEntry> known, params (string, int)[] weapons)
        {
            var config = HordeFixture.Config(minimum: 0, roster: new string[0], heroStands: true);
            config.Arsenal = new ArsenalConfig { Weapons = weapons.Length > 0 ? weapons : new[] { ("sword", 1) }, Cards = false };
            return Start(config, known);
        }

        static void Position(Simulation sim, out double x, out double y) => sim.State.Hero.PositionAt(sim.Now, out x, out y);

        static List<HeroGlideEvent> Glides(Simulation sim) => Drive.Events(sim, SimEventKind.HeroGlide).Select(e => e.AsHeroGlide).ToList();

        static List<AttackEvent> Swings(Simulation sim) =>
            Drive.Events(sim, SimEventKind.Attack).Select(e => e.AsAttack).Where(a => a.IsSword).ToList();

        static double Reach(bool heavy = false) =>
            Content.Weapon("sword").Shape.RadiusM * Content.Weapon("sword").Rank(1).Area * (heavy ? Content.Weapon("sword").Shape.HeavyReachScale : 1);

        static double[] Beats(Simulation sim) => sim.Stream.Beats.ToArray().Select(b => b.T).ToArray();

        static List<double> Beats(IEnumerable<KnownEntry> known) =>
            known.Where(k => k.Entry.Kind == EntryKind.Beat).Select(k => k.Entry.T).OrderBy(t => t).ToList();

        /// <summary>Steps to just after <paramref name="heard"/>, as the sim takes a key a lead later, and presses the attack key heard then.</summary>
        static void Attack(Simulation sim, double heard)
        {
            sim.StepTo(heard + 0.03);
            sim.Press(Key.Attack, heard);
            sim.Release(Key.Attack, heard + 0.01);
        }

        // ---------------------------------------------------------------- walking

        [TestCase(120.0)]
        [TestCase(90.0)]
        public void HeldArrowsWalkATilePerStrideStepEightWays(double bpm)
        {
            var sim = Walker(bpm);
            sim.StepTo(10);
            Position(sim, out double x0, out double y0);
            double speed = Tile / sim.Strides.Period;
            sim.Press(Key.Right, 10);
            sim.StepTo(11);
            Position(sim, out double x1, out double y1);
            Assert.AreEqual(x0 + speed, x1, 1e-9, "a tile per stride step");
            Assert.AreEqual(y0, y1, 1e-9);
            sim.Press(Key.Up, 11);
            sim.StepTo(12);
            Position(sim, out double x2, out double y2);
            Assert.AreEqual(speed, Math.Sqrt((x2 - x1) * (x2 - x1) + (y2 - y1) * (y2 - y1)), 1e-9, "diagonals as fast");
            Assert.AreEqual(x2 - x1, y2 - y1, 1e-9);
            sim.Press(Key.Left, 12); // Left cancels Right
            sim.StepTo(13);
            Position(sim, out double x3, out double y3);
            Assert.AreEqual(x2, x3, 1e-9);
            Assert.AreEqual(y2 + speed, y3, 1e-9);
            Assert.AreEqual(Direction.Up, sim.State.Hero.Facing);
            CollectionAssert.IsEmpty(Drive.Events(sim, SimEventKind.HeroStep), "no grid strides");
        }

        [Test]
        public void TheWalkFollowsTheTempo()
        {
            double Walked(double bpm)
            {
                var sim = Walker(bpm);
                sim.StepTo(10);
                Position(sim, out double x0, out _);
                sim.Press(Key.Right, 10);
                sim.StepTo(11);
                Position(sim, out double x1, out _);
                return x1 - x0;
            }
            Assert.AreEqual(Tile / 0.25, Walked(120), 1e-9, "120 BPM strides each 0.25 s");
            Assert.Less(Walked(100), Walked(120));
        }

        [Test]
        public void AKeyHeardBeforeTheSimTookItWalksFromWhenItWasHeard()
        {
            var sim = Walker();
            sim.StepTo(10.08); // 10.075 in whole ticks
            Position(sim, out double x0, out double y0);
            sim.Press(Key.Right, 10.02);
            var glide = Glides(sim).Single(g => g.T >= 10.07);
            Assert.AreEqual(10.075, glide.T, 1e-9, "emitted when the sim took it");
            Assert.AreEqual(10.02, glide.Start, 1e-9, "drawn from when it was heard");
            Assert.AreEqual(x0, glide.FromX, 1e-9);
            Assert.AreEqual(6, glide.VX, 1e-9);
            sim.StepTo(10.5);
            sim.Release(Key.Right, 10.5);
            sim.StepTo(11);
            Position(sim, out double x1, out double y1);
            Assert.AreEqual(x0 + 6 * 0.48, x1, 1e-9);
            Assert.AreEqual(y0, y1, 1e-9);
            Assert.AreEqual(0, Glides(sim).Last().VX, "it stopped on the release");
        }

        [Test]
        public void TheHeroSlidesAlongWhatBlocksItAndNeverEntersIt()
        {
            // A 5 x 5 arena: a wall up column 2 but its top row; the hero west of it, held up and right.
            var arena = ArenaData.Parse(@"{ ""tileM"": 1.5, ""origin"": [0, 0], ""size"": [5, 5], ""heroStart"": [1, 2],
                ""blocked"": [[2, 0], [2, 1], [2, 2], [2, 3]], ""structures"": [] }");
            var sim = Walker(arena: arena);
            sim.StepTo(10);
            sim.Press(Key.Up, 10);
            sim.Press(Key.Right, 10);
            double r = FreeHero.HalfWidthM;
            for (double t = 10; t < 10.5; t += Simulation.TickSeconds)
            {
                sim.StepTo(t);
                Position(sim, out double x, out double y);
                Assert.IsTrue(x + r <= 3.0 || y - r >= 6.0, $"inside the wall at {t}: ({x}, {y})");
            }
            Position(sim, out double sx, out double sy);
            Assert.AreEqual(3.0 - r, sx, 1e-6, "pressed against the wall's face");
            Assert.Greater(sy, 3.75 + 1, "still sliding up it");
            sim.StepTo(13);
            Position(sim, out double ex, out double ey);
            Assert.AreEqual(7.5 - r, ex, 1e-6, "round the wall's end, to the arena's east edge");
            Assert.AreEqual(7.5 - r, ey, 1e-6, "and its north edge");
        }

        [Test]
        public void ATargetWalksTheHeroStraightThereAndAnArrowDropsIt()
        {
            var sim = Walker();
            sim.StepTo(10);
            Position(sim, out double x0, out double y0);
            sim.Target(x0 + 3, y0 + 4, 10);
            sim.StepTo(10.5);
            Position(sim, out double xm, out double ym);
            Assert.AreEqual(0.75, (xm - x0) / (ym - y0), 1e-9, "straight at it");
            sim.StepTo(11.5); // 5 m at 6 m/s
            Position(sim, out double x1, out double y1);
            Assert.AreEqual(x0 + 3, x1, 1e-9);
            Assert.AreEqual(y0 + 4, y1, 1e-9);
            Assert.AreEqual(0, Glides(sim).Last().VX);
            Assert.AreEqual(0, Glides(sim).Last().VY);

            sim.Target(x1 - 20, y1, 11.5);
            sim.StepTo(11.6);
            sim.Press(Key.Down, 11.6);
            sim.StepTo(11.7);
            sim.Release(Key.Down, 11.7);
            sim.StepTo(13);
            Position(sim, out double x2, out double y2);
            sim.StepTo(14);
            Position(sim, out double x3, out double y3);
            Assert.AreEqual(x2, x3, "the arrow dropped the target");
            Assert.AreEqual(y2, y3);
            Assert.Less(y2, y1);
        }

        [Test]
        public void TheCardsLetGoOfTheKeysAndATargetWhileFrozenIsDropped()
        {
            var sim = Walker();
            sim.StepTo(10);
            sim.Press(Key.Right, 10);
            sim.StepTo(10.5);
            sim.LetGoOfHeldKeys();
            sim.StepTo(11);
            Position(sim, out double x0, out double y0);
            sim.StepTo(11.5);
            Position(sim, out double x1, out _);
            Assert.AreEqual(x0, x1, "let go");
            sim.Control(RunControl.Pause);
            sim.Target(x0 + 5, y0, sim.Now);
            sim.Control(RunControl.Resume);
            sim.StepTo(13);
            Position(sim, out double x2, out _);
            Assert.AreEqual(x0, x2, "the paused click walks nowhere");
        }

        // ---------------------------------------------------------------- the attack key

        [Test]
        public void TheSwordStrikesOnlyOnTheAttackKeyAndTheOtherWeaponsStayOnTheBeat()
        {
            var sim = Fighter(Plain(120, 16), ("sword", 1), ("garlic", 1));
            for (int i = 0; i < 6; i++) HordeFixture.Place(sim, "mudman2", sim.State.Hero.X + 1 + i * 0.2, sim.State.Hero.Y + 0.5);
            sim.StepTo(10);
            CollectionAssert.IsEmpty(Swings(sim), "no beat swings the sword");
            Assert.IsNotEmpty(Drive.Events(sim, SimEventKind.Hit).Where(e => e.AsHit.Target >= 0 && e.AsHit.Weapon != sim.Sword.Index),
                "the garlic still pulses by itself");
            double beat = Beats(sim).First(t => t > 10.2);
            Attack(sim, beat);
            sim.StepTo(beat + 1);
            var swing = Swings(sim).Single();
            Assert.AreEqual(beat, swing.WindupStart, 1e-9, "winds up from when the press was heard");
            Assert.That(swing.Impact, Is.InRange(beat + Sword.PressImpactS - 1e-9, beat + ImmediateS), "hits at once");
            Assert.AreEqual(Reach(), swing.Reach, 1e-9, "on the beat: full reach");
            Assert.AreEqual(1, swing.Grade, "carrying its grade");
        }

        /// <summary>A press's impact at most this long after it was heard (s): three 60 Hz frames.</summary>
        const double ImmediateS = 0.05;

        /// <summary>
        /// The beat grades the payoff, never the timing: wherever a press falls against the beat, or with no beat at all,
        /// it winds up from when it was heard and its impact, and its blow, land at once: PressImpactS after it, or the
        /// tick after the sim took it if that is later.
        /// </summary>
        [TestCase(120.0)]
        [TestCase(93.0)]
        [TestCase(0.0)]
        public void APressHitsAtOnceWhereverItFallsAgainstTheBeat(double bpm)
        {
            var known = bpm > 0 ? Plain(bpm, 30) : new List<KnownEntry>();
            var sim = Fighter(known);
            var beats = bpm > 0 ? Beats(known) : Enumerable.Range(0, 40).Select(i => 9.6 + 0.5 * i).ToList();
            int b = beats.FindIndex(t => t > 9.5);
            sim.StepTo(9);
            int boss = HordeFixture.Place(sim, "boss-werewolf", sim.State.Hero.X + 0.5, sim.State.Hero.Y, boss: true);
            // A press every beat and a bit, sweeping its offset from the beat across a whole eighth.
            var heard = Enumerable.Range(0, 12).Select(i => beats[b + 2 * i] + (beats[b + 1] - beats[b]) * i / 24.0).ToList();
            var taken = new List<double>();
            foreach (double h in heard)
            {
                Attack(sim, h);
                taken.Add(sim.Now);
            }
            sim.StepTo(beats[b + 26]);
            var swings = Swings(sim);
            Assert.AreEqual(heard.Count, swings.Count, "none waited for a swing");
            var hits = Drive.Events(sim, SimEventKind.Hit).Select(e => e.AsHit).Where(h => h.Target == boss).ToList();
            for (int i = 0; i < heard.Count; i++)
            {
                double impact = swings[i].Impact;
                Assert.AreEqual(Math.Max(heard[i] + Sword.PressImpactS, taken[i] + Simulation.TickSeconds), impact, 1e-9, $"press {i} at {heard[i]}");
                Assert.LessOrEqual(impact - heard[i], ImmediateS, $"press {i}: at once");
                Assert.AreEqual(heard[i], swings[i].WindupStart, 1e-9, $"press {i}: winds up from the press");
                Assert.IsTrue(hits.Any(h => Math.Abs(h.T - impact) < 1e-9), $"press {i}: its blow lands on the impact");
            }
        }

        /// <summary>A live key reaches the sim about a lead after it was heard: its impact is the tick after the sim takes it.</summary>
        [Test]
        public void APressTheSimTakesLateHitsTheTickAfter()
        {
            var sim = Fighter(Plain(120, 20));
            sim.StepTo(10.09);
            double taken = sim.Now;
            sim.Press(Key.Attack, 10.0);
            sim.StepTo(11);
            var swing = Swings(sim).Single();
            Assert.AreEqual(taken + Simulation.TickSeconds, swing.Impact, 1e-9);
            Assert.AreEqual(10.0, swing.WindupStart, 1e-9, "still winding up from when it was heard");
        }

        /// <summary>
        /// A press in the swing before's follow-through cuts it short into the next: it lands a swing after the last press,
        /// winding up from when it was heard, or from the last strike queued (a follow-up's impact) if that is later.
        /// </summary>
        [TestCase(1)]
        [TestCase(2)]
        public void APressInTheSwingBeforesFollowThroughCutsItShort(int rank)
        {
            var sim = Fighter(new KnownEntry[0], ("sword", rank));
            Attack(sim, 3.0);
            Attack(sim, 3.05);
            sim.StepTo(4);
            var swings = Swings(sim);
            double first = 3.0 + Sword.PressImpactS, next = first + Sword.SwingS;
            var expected = rank == 1 ? new[] { first, next } : new[] { first, first + Sword.SwingS / 2, next, next + Sword.SwingS / 2 };
            Assert.AreEqual(expected.Length, swings.Count);
            for (int i = 0; i < expected.Length; i++) Assert.AreEqual(expected[i], swings[i].Impact, 1e-9, $"strike {i}: the follow-ups halfway through the swing");
            Assert.AreEqual(rank == 1 ? 3.05 : first + Sword.SwingS / 2, swings[rank].WindupStart, 1e-9);
        }

        [Test]
        public void APressIsGradedByHowNearABeatOrHalfBeatItWasHeard()
        {
            var known = Plain(120, 20);
            var sim = Fighter(known);
            var beats = Beats(known);
            int b = beats.FindIndex(t => t > 9.5);
            double half = (beats[b + 1] - beats[b]) / 2;
            double worst = half / 2;
            var presses = new[]
            {
                (heard: beats[b], grade: 1.0),
                (heard: beats[b + 2] + half, grade: 1.0),                                  // a half beat
                (heard: beats[b + 4] + BeatGrade.FullWindowS - 0.005, grade: 1.0),         // inside the window
                (heard: beats[b + 6] - worst, grade: BeatGrade.Lowest),                    // a quarter beat early: the furthest off
                (heard: beats[b + 8] + 0.1,
                    grade: 1 - (1 - BeatGrade.Lowest) * (0.1 - BeatGrade.FullWindowS) / (worst - BeatGrade.FullWindowS)),
            };
            foreach (var (heard, _) in presses) Attack(sim, heard);
            sim.StepTo(beats[b + 10]);
            var swings = Swings(sim);
            Assert.AreEqual(presses.Length, swings.Count);
            for (int i = 0; i < presses.Length; i++)
                Assert.AreEqual(Reach() * presses[i].grade, swings[i].Reach, 1e-9, $"press {i}");
        }

        [Test]
        public void AnOffBeatPressDealsLessDamage()
        {
            var known = Plain(120, 20);
            var sim = Fighter(known);
            var beats = Beats(known);
            int b = beats.FindIndex(t => t > 9.5);
            sim.StepTo(9);
            int enemy = HordeFixture.Place(sim, "boss-werewolf", sim.State.Hero.X + 0.5, sim.State.Hero.Y, boss: true);
            Attack(sim, beats[b]);
            Attack(sim, beats[b + 2] + (beats[b + 3] - beats[b + 2]) / 4);
            sim.StepTo(beats[b + 4]);
            var hits = Drive.Events(sim, SimEventKind.Hit).Select(e => e.AsHit).Where(h => h.Target == enemy).ToList();
            Assert.AreEqual(2, hits.Count);
            Assert.AreEqual(hits[0].Damage * BeatGrade.Lowest, hits[1].Damage, 1e-9);
        }

        [Test]
        public void WithNoReliableBeatEveryPressIsFull()
        {
            var sim = Fighter(new KnownEntry[0]);
            Attack(sim, 3.0);
            Attack(sim, 3.37);
            sim.StepTo(5);
            Assert.AreEqual(new[] { Reach(), Reach() }, Swings(sim).Select(s => s.Reach).ToArray());
        }

        /// <summary>
        /// Mashing swings no faster than the swing: a press waits for the swing before to end, and one while a waiting swing
        /// winds up (before its impact, so it shows) is dropped. Rank 2's Amount 2 adds a follow-up inside each swing,
        /// which never slows the next press.
        /// </summary>
        [TestCase(1)]
        [TestCase(2)]
        public void MashingSwingsNoFasterThanTheSwing(int rank)
        {
            var sim = Fighter(new KnownEntry[0], ("sword", rank));
            Attack(sim, 3.0);
            Attack(sim, 3.02); // waits for the swing
            Attack(sim, 3.04); // the next is already waiting: dropped
            Attack(sim, 3.2);  // waits for that one
            sim.StepTo(5);
            var swings = Swings(sim);
            var mains = swings.Where(s => s.SwordKind != HeroAttackKind.FollowUp).Select(s => s.Impact).ToArray();
            double first = 3.0 + Sword.PressImpactS;
            Assert.AreEqual(3, mains.Length, "the third press dropped");
            for (int i = 0; i < 3; i++) Assert.AreEqual(first + i * Sword.SwingS, mains[i], 1e-9, $"press {i}: a swing after the one before");
            Assert.AreEqual(3 * rank, swings.Count, "each press's follow-ups");
        }

        [Test]
        public void AFullPressOnAnAccentedBeatIsHeavyOncePerBeat()
        {
            var known = Plain(120, 20);
            var beats = Beats(known);
            int b = beats.FindIndex(t => t > 10);
            known.Add(new KnownEntry(StreamEntry.Onset(beats[b] + 0.01, 0, 300, true), beats[b] - 2));
            known.Add(new KnownEntry(StreamEntry.Onset(beats[b + 4] + 0.01, 0, 300, true), beats[b + 4] - 2));
            var sim = Fighter(known);
            Attack(sim, beats[b] - 0.06);
            Attack(sim, beats[b] + 0.06);       // the same beat: not again
            Attack(sim, beats[b + 2]);          // no accent
            Attack(sim, beats[b + 4] + 0.1);    // the accent, but off it
            sim.StepTo(beats[b + 6]);
            var swings = Swings(sim);
            Assert.AreEqual(new[] { true, false, false, false }, swings.Select(s => s.Heavy).ToArray());
            Assert.AreEqual(HeroAttackKind.Spin, swings[0].SwordKind);
            Assert.AreEqual(Reach(heavy: true), swings[0].Reach, 1e-9);
            var cleave = Drive.Events(sim, SimEventKind.Cleave).Single().AsCleave;
            Assert.That(swings[0].Impact - (beats[b] - 0.06), Is.InRange(Sword.PressImpactS - 1e-9, ImmediateS), "the spin hits at once, on the accent");
            Assert.AreEqual(swings[0].Impact + Sword.SwingS / 2, cleave.FirstBand, 1e-9, "its cleave wave's bands spread over the swing");
            Assert.AreEqual(Sword.SwingS / 4, cleave.BandSeconds, 1e-9);

            // Faster, a second full press from the accented beat: still not heavy.
            known = Plain(180, 20);
            beats = Beats(known);
            b = beats.FindIndex(t => t > 10);
            known.Add(new KnownEntry(StreamEntry.Onset(beats[b] + 0.01, 0, 300, true), beats[b] - 2));
            sim = Fighter(known);
            Attack(sim, beats[b] - 0.06);
            Attack(sim, beats[b] + 0.06);
            sim.StepTo(beats[b + 4]);
            Assert.AreEqual(new[] { true, false }, Swings(sim).Select(s => s.Heavy).ToArray(), "once per beat");
        }
    }
}
