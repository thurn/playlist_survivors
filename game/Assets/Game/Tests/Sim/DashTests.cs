using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using PS.Game.Sim.Content;
using PS.Game.Sim.Level;

namespace PS.Game.Sim.Tests
{
    /// <summary>
    /// The dash (`--dash`, docs/design/game-modes.md, "The dash"): Space on the beat lunges far, striking and throwing
    /// aside what it passes, and makes the sword's next beat strike a spin; off the beat it hops a tile; either way the
    /// hero can't be hurt through it. Walls stop it, mashing dashes no faster than the sword's pulse, a held or fallen
    /// hero doesn't dash, and a press the sim takes late dashes from when it was heard.
    /// </summary>
    public class DashTests
    {
        static GameContent Content => HordeFixture.Content;

        /// <summary>Beats and ticks only, at their normal known-at times.</summary>
        static List<KnownEntry> Plain(double bpm, double until, double evidence = 0.9) =>
            Drive.Known(new FixtureSong(3).Beats(0, until, bpm, evidence: evidence).Entries()
                .Where(e => e.Kind == EntryKind.Beat || e.Kind == EntryKind.Tick));

        /// <summary>A zen hero that never falls, with the sword at rank 1 (or none) and no horde of its own, and the dash.</summary>
        static SimConfig Config(bool sword = true, ArenaData arena = null, GameMode mode = GameMode.Zen)
        {
            var config = HordeFixture.Config(minimum: 0, arena: arena, roster: new string[0], heroStands: true);
            if (sword) config.Arsenal = new ArsenalConfig { Weapons = new[] { ("sword", 1) }, Cards = false };
            config.Mode = mode;
            config.Dash = true;
            return config;
        }

        static Simulation Start(IEnumerable<KnownEntry> known, SimConfig config = null)
        {
            var sim = new Simulation(config ?? Config());
            foreach (var k in known) sim.AddKnown(k.Entry, k.KnownAt);
            return sim;
        }

        static List<double> Beats(IEnumerable<KnownEntry> known) =>
            known.Where(k => k.Entry.Kind == EntryKind.Beat).Select(k => k.Entry.T).OrderBy(t => t).ToList();

        static List<HeroDashEvent> Dashes(Simulation sim) => Drive.Events(sim, SimEventKind.HeroDash).Select(e => e.AsHeroDash).ToList();

        static List<HitEvent> LungeHits(Simulation sim) =>
            Drive.Events(sim, SimEventKind.Hit).Select(e => e.AsHit).Where(h => !h.OnHero && h.WeaponHit == WeaponHit.Dash).ToList();

        static List<AttackEvent> Swings(Simulation sim) =>
            Drive.Events(sim, SimEventKind.Attack).Select(e => e.AsAttack).Where(a => a.IsSword).ToList();

        static int Place(Simulation sim, string role, double dx, double dy) =>
            HordeFixture.Place(sim, role, sim.State.Hero.X + dx, sim.State.Hero.Y + dy);

        /// <summary>How far the hero went over the dash (where it was at its end less at its start): asked inside the path the hero keeps.</summary>
        static (double x, double y) Travel(Simulation sim, HeroDashEvent dash)
        {
            Assert.Less(sim.Now - dash.Start, FreeHero.History * Simulation.TickSeconds, "inside the kept path");
            sim.HeroWas(dash.Start, out double x0, out double y0);
            sim.HeroWas(dash.End, out double x1, out double y1);
            return (x1 - x0, y1 - y0);
        }

        /// <summary>Steps to <paramref name="at"/> and presses Space heard at the sim's time then, released a little after.</summary>
        static double Space(Simulation sim, double at)
        {
            sim.StepTo(at);
            double heard = sim.Now;
            sim.Press(Key.Space, heard);
            sim.Release(Key.Space, heard);
            return heard;
        }

        // ---------------------------------------------------------------- the flag

        [Test]
        public void TheDashNeedsAFreeHeroAndWithoutItSpaceDoesNothing()
        {
            Assert.Throws<ArgumentException>(() => new Simulation(Config(mode: GameMode.Grid)), "the grid hero has no dash");
            Assert.DoesNotThrow(() => new Simulation(Config(mode: GameMode.Free)), "the free mode may dash too");

            var config = Config();
            config.Dash = false;
            var sim = Start(Plain(120, 12), config);
            sim.StepTo(5);
            sim.State.Hero.PositionAt(sim.Now, out double x0, out double y0);
            Space(sim, 6);
            sim.StepTo(8);
            sim.State.Hero.PositionAt(sim.Now, out double x1, out double y1);
            Assert.AreEqual(0, Dashes(sim).Count);
            Assert.AreEqual(x0, x1);
            Assert.AreEqual(y0, y1, "Space neither dashes nor walks a zen hero without the dash");
        }

        // ---------------------------------------------------------------- on and off the beat

        [Test]
        public void OnTheBeatTheHeroLungesFarStrikesWhatItPassesAndTheNextBeatStrikeSpins()
        {
            var known = Plain(120, 20);
            var beats = Beats(known);
            var sim = Start(known);
            sim.StepTo(beats[8]);
            sim.Press(Key.Right, sim.Now);
            // After the beat's strike has landed, so only the lunge reaches them before the next.
            sim.StepTo(beats[10] + 0.005);
            int near = Place(sim, "werewolf", 2.5, 0.5), wide = Place(sim, "werewolf", 2.5, -3);
            double heard = Space(sim, beats[10] + 0.012);
            sim.StepTo(heard + 0.2);

            var dash = Dashes(sim).Single();
            Assert.IsTrue(dash.OnBeat, "heard within the window of the beat");
            Assert.AreEqual(heard, dash.Start, 1e-12, "from the key, at once");
            Assert.AreEqual(Dash.LongS, dash.End - dash.Start, 1e-12);
            Assert.AreEqual((1.0, 0.0), (dash.DirX, dash.DirY), "along the held arrow");
            var (dx, dy) = Travel(sim, dash);
            Assert.AreEqual(Dash.LongM, dx, 1e-9, "three tiles");
            Assert.AreEqual(0, dy, 1e-9);
            sim.StepTo(beats[12] + 0.01);

            var hit = LungeHits(sim).Single();
            Assert.AreEqual(near, hit.Target, "the enemy on its path, not the one beside it");
            Assert.AreEqual(Content.Weapon("sword").Rank(1).Power * Content.Weapon("sword").Shape.HeavyDamage, hit.Damage, 1e-9, "a heavy strike's blow");
            Assert.That(hit.T, Is.InRange(dash.Start, dash.End));
            var knock = Drive.Events(sim, SimEventKind.EnemyKnock).Select(e => e.AsEnemyKnock).Single(k => k.Enemy == near && k.Start == hit.T);
            Assert.Greater(knock.ToY - knock.FromY, 0.5, "thrown aside, to its side of the path");
            Assert.AreEqual(knock.FromX, knock.ToX, 1e-9, "square to the path");
            Assert.IsFalse(LungeHits(sim).Any(h => h.Target == wide));

            var after = Swings(sim).Where(a => a.Impact > dash.End).ToList();
            Assert.AreEqual(beats[11], after[0].Impact, 1e-9);
            Assert.AreEqual(HeroAttackKind.Spin, after[0].SwordKind, "the next beat's strike spins");
            Assert.IsTrue(after[0].Heavy);
            Assert.IsFalse(after[1].Heavy, "once per dash");
        }

        [Test]
        public void OffTheBeatTheHeroHopsATileStrikingNothingAndNoSpinFollows()
        {
            var known = Plain(120, 20);
            var beats = Beats(known);
            var sim = Start(known);
            sim.StepTo(beats[8]);
            sim.Press(Key.Up, sim.Now);
            sim.StepTo(beats[10] + 0.005);
            int ahead = Place(sim, "werewolf", 0, 1.5);
            // On the half beat: off the beat.
            double heard = Space(sim, 0.5 * (beats[10] + beats[11]));
            sim.StepTo(heard + 0.2);

            var dash = Dashes(sim).Single();
            Assert.IsFalse(dash.OnBeat);
            Assert.AreEqual(Dash.ShortS, dash.End - dash.Start, 1e-12);
            var (dx, dy) = Travel(sim, dash);
            Assert.AreEqual(0, dx, 1e-9);
            Assert.AreEqual(Dash.ShortM, dy, 1e-9, "a tile up");
            sim.StepTo(beats[13] + 0.01);
            Assert.AreEqual(0, LungeHits(sim).Count, "the hop strikes nothing");
            Assert.IsTrue(Drive.Events(sim, SimEventKind.Hit).Any(e => e.AsHit.Target == ahead), "though the beat's strikes reach it");
            Assert.IsFalse(Swings(sim).Any(a => a.Impact > dash.End && a.Heavy), "and no spin follows");
        }

        [Test]
        public void WithNoReliableBeatEveryDashIsTheLunge()
        {
            var sim = Start(Plain(120, 20, evidence: 0.1));
            sim.StepTo(8);
            Space(sim, 8.23);
            Space(sim, 9.11);
            sim.StepTo(10);
            Assert.AreEqual(new[] { true, true }, Dashes(sim).Select(d => d.OnBeat).ToArray());
        }

        [Test]
        public void TheDashGoesTowardTheTargetElseTheWalkElseTheFacing()
        {
            var sim = Start(Plain(120, 30, evidence: 0.1), Config(sword: false));
            sim.StepTo(5);
            sim.State.Hero.PositionAt(sim.Now, out double x, out double y);
            sim.Target(x + 30, y + 40, sim.Now);
            Space(sim, 5.5);
            var toward = Dashes(sim).Last();
            Assert.AreEqual(0.6, toward.DirX, 1e-9, "toward the pointer's target");
            Assert.AreEqual(0.8, toward.DirY, 1e-9);

            sim.Press(Key.Left, sim.Now);
            sim.StepTo(7);
            sim.Release(Key.Left, sim.Now);
            Space(sim, 8);
            var facing = Dashes(sim).Last();
            Assert.AreEqual((-1.0, 0.0), (facing.DirX, facing.DirY), "stopped: the way it last went");
            Assert.AreEqual(Direction.Left, facing.Facing);
        }

        // ---------------------------------------------------------------- invulnerable

        [Test]
        public void TheHeroCannotBeHurtThroughEitherDash([Values(true, false)] bool onBeat)
        {
            var known = Plain(120, 20);
            var beats = Beats(known);
            var sim = Start(known);
            double heard = Space(sim, onBeat ? beats[10] : 0.5 * (beats[10] + beats[11]));
            var dash = Dashes(sim).Single();
            Assert.AreEqual(onBeat, dash.OnBeat);
            double Hurt(double at)
            {
                sim.StepTo(at);
                int before = sim.EventCount;
                sim.Health.Hurt(sim, sim.Now, EnemyAttackKind.Lunge, 10, 1, 0);
                return sim.EventsSince(before).ToArray().Single(e => e.Kind == SimEventKind.Hit).AsHit.Damage;
            }
            Assert.AreEqual(0, Hurt(heard + 0.05), "mid-dash");
            Assert.AreEqual(0, Hurt(dash.End + 0.5 * Dash.AfterS), "landing");
            Assert.Greater(Hurt(dash.End + Dash.AfterS + 0.05), 0, "and hurt again after");
        }

        // ---------------------------------------------------------------- walls and rate

        [Test]
        public void AWallOrTheArenasEdgeStopsADash()
        {
            // A 5 x 5 arena: a wall up column 2 but its top row; the hero west of it at (1, 2).
            var arena = ArenaData.Parse(@"{ ""tileM"": 1.5, ""origin"": [0, 0], ""size"": [5, 5], ""heroStart"": [1, 2],
                ""blocked"": [[2, 0], [2, 1], [2, 2], [2, 3]], ""structures"": [] }");
            var sim = Start(Plain(120, 20, evidence: 0.1), Config(sword: false, arena: arena));
            double r = FreeHero.HalfWidthM;
            sim.StepTo(5);
            sim.Press(Key.Right, sim.Now);
            Space(sim, 5);
            sim.StepTo(5.5);
            sim.State.Hero.PositionAt(sim.Now, out double x, out double y);
            Assert.AreEqual(3.0 - r, x, 1e-6, "against the wall's face, never in it");
            Assert.AreEqual(3.75, y, 1e-9);
            sim.Release(Key.Right, sim.Now);
            sim.Press(Key.Left, sim.Now);
            Space(sim, 6);
            sim.StepTo(6.5);
            sim.State.Hero.PositionAt(sim.Now, out x, out _);
            Assert.AreEqual(r, x, 1e-6, "at the arena's west edge");
        }

        [TestCase(120.0, 0.5)]
        [TestCase(170.0, 2 * 60 / 170.0)]
        [TestCase(70.0, 0.5 * 60 / 70.0)]
        public void MashingDashesNoFasterThanTheSwordsPulseAndLosesNoPress(double bpm, double period)
        {
            var sim = Start(Plain(bpm, 20), Config(sword: false));
            double last = 0;
            for (double t = 6; t < 9; t += 0.05) last = Space(sim, t);
            sim.StepTo(11);
            var dashes = Dashes(sim);
            var gaps = dashes.Zip(dashes.Skip(1), (a, b) => b.Start - a.Start).ToList();
            Assert.IsTrue(gaps.All(g => g >= period - 2 * Dash.WindowS - 1e-9), $"{period - 2 * Dash.WindowS:F3} s apart at least: {string.Join(", ", gaps.Select(g => g.ToString("F3")))}");
            Assert.LessOrEqual(dashes.Count, (int)(3 / (period - 2 * Dash.WindowS)) + 2);
            Assert.GreaterOrEqual(dashes.Last().Start, last, "the last press, waiting through the one before, still dashed");
            Assert.That(dashes.Last().Start - last, Is.LessThanOrEqualTo(period), "as soon as the pulse let it");
        }

        [Test]
        public void TwoPressesEachOnItsBeatBothLunge()
        {
            var known = Plain(120, 20);
            var beats = Beats(known);
            var sim = Start(known, Config(sword: false));
            Space(sim, beats[10] + 0.06);
            Space(sim, beats[11] - 0.06);
            sim.StepTo(beats[12]);
            var dashes = Dashes(sim);
            Assert.AreEqual(2, dashes.Count);
            Assert.IsTrue(dashes.All(d => d.OnBeat));
            Assert.AreEqual(beats[11] - 0.06, dashes[1].Start, 0.01, "the second not held back");
        }

        // ---------------------------------------------------------------- holds

        [Test]
        public void AFrozenOrFallenHeroDoesNotDashAndAWaitingPressIsDropped()
        {
            var sim = Start(Plain(120, 20), Config(sword: false));
            sim.StepTo(5);
            sim.Control(RunControl.Pause);
            Space(sim, 5.2);
            sim.Control(RunControl.Resume);
            sim.StepTo(6);
            Assert.AreEqual(0, Dashes(sim).Count, "paused");

            Space(sim, 7);
            Space(sim, 7.1); // waits for the pulse...
            sim.Control(RunControl.Pause);
            sim.StepTo(8);
            sim.Control(RunControl.Resume);
            sim.StepTo(9);
            Assert.AreEqual(1, Dashes(sim).Count, "...and the pause drops it");

            sim.State.Hero.Fallen = true;
            Space(sim, 10);
            sim.StepTo(11);
            Assert.AreEqual(1, Dashes(sim).Count, "fallen");
        }

        // ---------------------------------------------------------------- heard time

        /// <summary>
        /// A live key reaches the sim a lead after it was heard: the dash starts from when it was heard, as a free press's
        /// strike resolves, its path and the lunge's blows the same as if the sim had taken it at once.
        /// </summary>
        [TestCase(0.090)]
        [TestCase(0.302)]
        public void ADashTheSimTakesALeadLateRunsFromWhenItWasHeard(double lead)
        {
            var known = Plain(120, 20);
            var beats = Beats(known);
            (Simulation sim, int enemy) Run(double late)
            {
                var sim = Start(known);
                sim.StepTo(beats[8]);
                sim.Press(Key.Right, sim.Now);
                sim.StepTo(beats[10] + 0.004);
                int enemy = Place(sim, "werewolf", 3, 0.3);
                double heard = sim.Now;
                sim.StepTo(heard + late);
                sim.Press(Key.Space, heard);
                sim.StepTo(heard + 0.35);
                return (sim, enemy);
            }
            var (once, a) = Run(0);
            var (later, b) = Run(lead);
            var da = Dashes(once).Single();
            var db = Dashes(later).Single();
            Assert.Greater(db.T, db.End - Dash.LongS + lead - 0.01, "taken a lead late");
            Assert.AreEqual(da.Start, db.Start, "from when it was heard");
            Assert.AreEqual(da.End, db.End);
            Assert.IsTrue(db.OnBeat);
            var (ax, ay) = Travel(once, da);
            var (bx, by) = Travel(later, db);
            Assert.AreEqual(ax, bx, 1e-9, "the same lunge");
            Assert.AreEqual(ay, by, 1e-9);
            once.State.Hero.PositionAt(once.Now, out double xa, out double ya);
            later.State.Hero.PositionAt(later.Now, out double xb, out double yb);
            Assert.AreEqual(xa, xb, 1e-9, "the same walk after");
            Assert.AreEqual(ya, yb, 1e-9);
            var ha = LungeHits(once).Single();
            var hb = LungeHits(later).Single();
            Assert.AreEqual(a, ha.Target);
            Assert.AreEqual(b, hb.Target);
            Assert.AreEqual(ha.T, hb.T, "struck at the same moment of the lunge, in heard time");
            Assert.AreEqual(ha.X, hb.X, 1e-9, "where the enemy was then");
            Assert.AreEqual(ha.Y, hb.Y, 1e-9);
        }
    }
}
