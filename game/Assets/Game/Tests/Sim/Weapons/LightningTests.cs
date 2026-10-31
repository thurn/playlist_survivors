using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using PS.Game.Sim.Content;

namespace PS.Game.Sim.Tests
{
    /// <summary>
    /// Lightning Ring, Thunder Loop and Chain Lightning (unity-game.md, The Other Weapons; native's
    /// engine/lightning.rs and its chain): volleys and chains on the sixteenths, their targets, their
    /// reach and damage by native's numbers, and freezes.
    /// </summary>
    public class LightningTests
    {
        const double Eps = 1e-9;
        /// <summary>A brute with 50 health and no knockback: it stands up to many bolts and is never thrown.</summary>
        const string Brute = "xlmummy";

        static GameContent Content => HordeFixture.Content;

        static SimConfig Config((string, int)[] weapons, Stats? stats = null)
        {
            var config = HordeFixture.Config(minimum: 0, roster: new string[0], heroStands: true);
            config.Arsenal = new ArsenalConfig { Weapons = weapons, Stats = stats, Cards = false };
            return config;
        }

        static Simulation Start(SimConfig config, FixtureSong song)
        {
            var sim = new Simulation(config);
            foreach (var k in Drive.Known(song.Entries().Where(e => e.Kind == EntryKind.Beat || e.Kind == EntryKind.Tick)))
                sim.AddKnown(k.Entry, k.KnownAt);
            return sim;
        }

        static Stats BaseStats => Content.Progression.Hero.Stats;

        static int Place(Simulation sim, double dx, double dy, string role = Brute) =>
            HordeFixture.Place(sim, role, sim.State.Hero.X + dx, sim.State.Hero.Y + dy);

        static List<BoltEvent> Bolts(Simulation sim, BoltKind? kind = null) =>
            Drive.Events(sim, SimEventKind.Bolt).Select(e => e.AsBolt).Where(b => kind == null || b.Kind == kind).ToList();

        static List<HitEvent> HitsAt(Simulation sim, double t) =>
            Drive.Events(sim, SimEventKind.Hit).Select(e => e.AsHit).Where(h => !h.OnHero && Math.Abs(h.T - t) < Eps).ToList();

        static List<double> Shots(Simulation sim, string id) =>
            Drive.Events(sim, SimEventKind.Attack).Select(e => e.AsAttack)
                .Where(a => a.ByHero && a.Weapon == Content.WeaponIndex(id)).Select(a => a.T).ToList();

        /// <summary>Where <paramref name="t"/> falls on the sixteenths of <paramref name="beats"/>: a whole number on the grid.</summary>
        static double Sixteenths(List<double> beats, double t)
        {
            int b = beats.FindLastIndex(x => x <= t + Eps);
            return b * 4 + (t - beats[b]) / (beats[b + 1] - beats[b]) * 4;
        }

        // ---------------------------------------------------------------- Lightning Ring

        [Test]
        public void ARingVolleyStrikesEnemiesInViewOnTheShotsBeatThenEachSixteenth()
        {
            // Rank 1: 2 strikes of 1.5 every 4.5 s, 0.4571 m across, at 120 BPM (sixteenths 0.125 s).
            var sim = Start(Config(new[] { ("lightning-ring", 1) }), new FixtureSong(3).Beats(0, 20, 120));
            int[] inView = { Place(sim, 4, 0), Place(sim, -5, 3), Place(sim, 0, -6) };
            int far = Place(sim, 45, 0);
            sim.StepTo(12);

            var shots = Shots(sim, "lightning-ring");
            Assert.AreEqual(3, shots.Count, "every 4.5 s");
            for (int k = 1; k < shots.Count; k++) Assert.AreEqual(4.5, shots[k] - shots[k - 1], 0.5 + Eps, "4.5 s apart, snapped to a beat");
            foreach (double s in shots) Assert.AreEqual(Math.Round(s / 0.5), s / 0.5, 1e-6, $"the shot at {s} is on a beat");

            var bolts = Bolts(sim);
            Assert.AreEqual(2 * shots.Count, bolts.Count, "two strikes a volley");
            Assert.IsTrue(bolts.All(b => b.Kind == BoltKind.Strike && b.Weapon == Content.WeaponIndex("lightning-ring")), "no returns before the evolution");
            var targets = new HashSet<int>();
            for (int k = 0; k < bolts.Count; k++)
            {
                var b = bolts[k];
                Assert.AreEqual(k % 2, b.Index);
                Assert.AreEqual(shots[k / 2] + 0.125 * (k % 2), b.T, 1e-6, $"strike {k}: the shot's beat, then its next sixteenth");
                Assert.AreEqual((b.X, b.Y), (b.FromX, b.FromY), "from the sky");
                Assert.AreEqual(0.4571, b.Radius, 1e-9);
                var hits = HitsAt(sim, b.T);
                var target = hits.Single(h => Math.Abs(h.X - b.X) < 1e-9 && Math.Abs(h.Y - b.Y) < 1e-9);
                CollectionAssert.Contains(inView, target.Target, "it lands on an enemy in view");
                targets.Add(target.Target);
                Assert.IsTrue(hits.All(h => h.Damage == 1.5 && h.WeaponHit == WeaponHit.Bolt), "rank power x Might");
            }
            Assert.Greater(targets.Count, 1, "each strike draws its own enemy");
            Assert.IsFalse(Drive.Events(sim, SimEventKind.Hit).Any(e => e.AsHit.Target == far), "nothing out of view is struck");
            Assert.AreEqual(1.5 * bolts.Count, sim.State.WeaponDamage[Content.WeaponIndex("lightning-ring")], 1e-9, "its tally");

            // Alone in the 34 x 22 m footprint but beyond the 0.6 of it every zoom shows, a flower
            // (it barely moves) is never struck: the volleys land nowhere.
            var lone = Start(Config(new[] { ("lightning-ring", 1) }), new FixtureSong(3).Beats(0, 20, 120));
            Place(lone, 15, 0, "flower");
            lone.StepTo(12);
            Assert.AreEqual(3, Shots(lone, "lightning-ring").Count);
            Assert.AreEqual(0, Bolts(lone).Count, "nobody in view");
        }

        [Test]
        public void AStrikeHitsEveryBodyInItsCircleByRankAreaAndMightAndAmountLengthensTheVolley()
        {
            // Rank 8: power 6.5, area 4, amount 6. Might 1.5, Area 1.25 and Amount 1 make it 9.75 a hit,
            // 0.4571 x 4 x 1.25 = 2.2855 m across, 7 strikes a volley.
            var stats = BaseStats;
            stats.Might = 1.5;
            stats.Area = 1.25;
            stats.Amount = 1;
            var sim = Start(Config(new[] { ("lightning-ring", 8) }, stats), new FixtureSong(3).Beats(0, 10, 120));
            // Minotaurs (175 health) outlast the volley.
            int[] cluster = { Place(sim, 6, 0, "minotaur"), Place(sim, 6.8, 0.6, "minotaur"), Place(sim, 5.4, -0.7, "minotaur") };
            sim.StepTo(4);

            var bolts = Bolts(sim);
            Assert.AreEqual(7, bolts.Count, "one volley of 7");
            for (int k = 1; k < bolts.Count; k++) Assert.AreEqual(0.125, bolts[k].T - bolts[k - 1].T, 1e-6, "a sixteenth apart");
            foreach (var b in bolts)
            {
                Assert.AreEqual(2.2855, b.Radius, 1e-9);
                var hits = HitsAt(sim, b.T);
                CollectionAssert.AreEquivalent(cluster, hits.Select(h => h.Target), "the whole cluster is in its circle");
                Assert.IsTrue(hits.All(h => Math.Abs(h.Damage - 9.75) < 1e-9));
            }
        }

        [Test]
        public void ThunderLoopStrikesEachPointAgainOnTheFirstSixteenthAtOrAfterOneAndAHalfSeconds()
        {
            // At 128 BPM 1.5 s is 12.8 sixteenths: the return waits for the 13th.
            var song = new FixtureSong(3).Beats(0, 12, 128);
            var sim = Start(Config(new[] { ("thunder-loop", 1) }), song);
            Place(sim, 4, 1);
            Place(sim, -3, -4);
            sim.StepTo(4);

            var beats = song.BeatTimes();
            var strikes = Bolts(sim, BoltKind.Strike);
            var returns = Bolts(sim, BoltKind.Return);
            Assert.AreEqual(6, strikes.Count, "Thunder Loop's 6 strikes");
            Assert.AreEqual(strikes.Count, returns.Count, "each struck again");
            for (int k = 0; k < strikes.Count; k++)
            {
                var s = strikes[k];
                var r = returns[k];
                Assert.AreEqual((s.Index, s.X, s.Y), (r.Index, r.X, r.Y), "the same point, wherever its enemy went");
                Assert.AreEqual(Math.Ceiling(Sixteenths(beats, s.T + LightningRing.ReturnS) - 1e-6), Sixteenths(beats, r.T), 1e-6,
                    $"return {k}: the first sixteenth at or after 1.5 s");
            }
        }

        // ---------------------------------------------------------------- Chain Lightning

        [Test]
        public void AChainJumpsFromTheHeroToTheNearestEnemiesInRangeAndEndsWithNoneLeft()
        {
            // Rank 1: 3 jumps of 1.2, the first within 9.63 m of the hero, the rest within 5.19 m of the
            // enemy before, each hitting a circle of 0.59 m.
            var sim = Start(Config(new[] { ("chain-lightning", 1) }), new FixtureSong(3).Beats(0, 10, 120));
            var hero = (sim.State.Hero.X, sim.State.Hero.Y);
            int a = Place(sim, 6, 0);
            int b = Place(sim, 6, 4);
            int beside = Place(sim, 6.6, 4.3); // in b's circle, farther from a than b
            Place(sim, -12, 0); // beyond the first jump's reach
            Place(sim, 6, 11); // beyond a jump's reach from b
            sim.StepTo(0.9);

            var shot = Shots(sim, "chain-lightning").First();
            var chain = Bolts(sim).Where(x => x.T < shot + 0.5).ToList();
            Assert.AreEqual(2, chain.Count, "nobody in reach of b but enemies already hit: the chain ends short of its 3");
            Assert.IsTrue(chain.All(x => x.Kind == BoltKind.Jump), "jumps");
            Assert.AreEqual((0, shot, hero.Item1, hero.Item2), (chain[0].Index, chain[0].T, chain[0].FromX, chain[0].FromY), "the first from the hero on the shot");
            Assert.AreEqual((1, shot + 0.125, chain[0].X, chain[0].Y), (chain[1].Index, chain[1].T, chain[1].FromX, chain[1].FromY),
                "the next from the enemy before, a sixteenth later");
            Assert.AreEqual(0.5926, chain[0].Radius, 1e-9);

            var first = HitsAt(sim, chain[0].T);
            Assert.AreEqual(new[] { a }, first.Select(h => h.Target).ToArray());
            var second = HitsAt(sim, chain[1].T);
            Assert.AreEqual(b, second[0].Target, "the target first");
            CollectionAssert.AreEquivalent(new[] { b, beside }, second.Select(h => h.Target), "and whoever its circle catches");
            Assert.IsTrue(first.Concat(second).All(h => h.Damage == 1.2 && h.WeaponHit == WeaponHit.Bolt));
            var along = (chain[1].X - chain[1].FromX, chain[1].Y - chain[1].FromY);
            double l = Math.Sqrt(along.Item1 * along.Item1 + along.Item2 * along.Item2);
            Assert.AreEqual(along.Item1 / l, second[1].DirX, 1e-9, "struck along the jump");
        }

        [Test]
        public void AChainHasAmountJumpsAndNeverReturnsToAnEnemyItHit()
        {
            var stats = BaseStats;
            stats.Amount = 1;
            // Rank 1's 3 jumps and Amount 1: 4 jumps down a line of six brutes 3 m apart.
            var sim = Start(Config(new[] { ("chain-lightning", 1) }, stats), new FixtureSong(3).Beats(0, 10, 120));
            var line = Enumerable.Range(1, 6).Select(k => Place(sim, 3 * k, 0)).ToArray();
            sim.StepTo(0.9);
            var shot = Shots(sim, "chain-lightning").First();
            var chain = Bolts(sim).Where(x => x.T < shot + 0.9).ToList();
            Assert.AreEqual(new[] { 0, 1, 2, 3 }, chain.Select(x => x.Index).ToArray());
            Assert.AreEqual(line.Take(4).ToArray(), chain.Select(x => HitsAt(sim, x.T).Single().Target).ToArray(), "down the line, nearest first");

            // Two brutes alone: back and forth would be nearest, but the chain has hit them both.
            sim = Start(Config(new[] { ("chain-lightning", 1) }, stats), new FixtureSong(3).Beats(0, 10, 120));
            Place(sim, 3, 0);
            Place(sim, 5, 0);
            sim.StepTo(0.9);
            shot = Shots(sim, "chain-lightning").First();
            Assert.AreEqual(2, Bolts(sim).Count(x => x.T < shot + 0.9));
        }

        // ---------------------------------------------------------------- the grid and freezes

        [Test]
        public void StrikesAndJumpsStayOnTheSixteenthsWhileTheNextBeatIsNotYetCommitted()
        {
            // Each committed beat known only 0.05 s before its time: a volley's later strikes and a
            // chain's jumps are planned on the latest beat's continuation, and Thunder Loop's returns,
            // past it, move onto the sixteenth the beats committed by then say.
            const double Seconds = 60;
            foreach (double bpm in new[] { 70.0, 128.0 })
            {
                var stats = BaseStats;
                stats.Cooldown = 0.6;
                var sim = new Simulation(Config(new[] { ("thunder-loop", 1), ("chain-lightning", 8) }, stats));
                var song = new FixtureSong(5).Beats(0, Seconds + 2, bpm);
                foreach (var e in song.Entries().Where(e => e.Kind == EntryKind.Beat || e.Kind == EntryKind.Tick))
                    sim.AddKnown(e, Math.Max(0, e.Kind == EntryKind.Beat ? e.T - 0.05 : e.T));
                // Fresh brutes every 5 s, as the bolts wear the last ones down.
                for (int k = 0; k < 72; k++)
                {
                    if (k % 6 == 0) sim.StepTo(5 * (k / 6));
                    Place(sim, 9 * Math.Cos(k * 0.7), 7 * Math.Sin(k * 1.3));
                }
                sim.StepTo(Seconds);
                var beats = song.BeatTimes();
                var bolts = Bolts(sim);
                Assert.Greater(bolts.Count(b => b.Kind == BoltKind.Strike), 100, $"{bpm} BPM: strikes");
                Assert.Greater(bolts.Count(b => b.Kind == BoltKind.Return), 100, $"{bpm} BPM: returns");
                Assert.Greater(bolts.Count(b => b.Kind == BoltKind.Jump), 100, $"{bpm} BPM: jumps");
                foreach (var b in bolts)
                {
                    double q = Sixteenths(beats, b.T);
                    Assert.AreEqual(Math.Round(q), q, 1e-6, $"{bpm} BPM: a {b.Kind} at {b.T} is on a sixteenth");
                }
            }
        }

        [Test]
        public void AFreezeHoldsTheBoltsToComeAndTheResumeCarriesThemOnFromItsSixteenth()
        {
            // Rank 8's volley of 6 and chain of 6 jumps, paused just after their first.
            var sim = Start(Config(new[] { ("lightning-ring", 8), ("chain-lightning", 8) }), new FixtureSong(3).Beats(0, 20, 120));
            for (int k = 0; k < 8; k++) Place(sim, 3 + 1.5 * k, k % 2 == 0 ? 1 : -1);
            sim.StepTo(0.01);
            var before = Bolts(sim);
            Assert.AreEqual(new[] { BoltKind.Strike, BoltKind.Jump }, before.Select(b => b.Kind).OrderBy(k => k).ToArray(), "the first of each");
            double shot = before[0].T;
            sim.Control(RunControl.Pause);
            sim.StepTo(5);
            Assert.AreEqual(2, Bolts(sim).Count, "nothing while frozen");
            sim.Control(RunControl.Resume);
            sim.StepTo(8);
            double resume = Drive.Events(sim, SimEventKind.Freeze).Select(e => e.AsFreeze).Single(f => !f.Starts).T;
            Assert.Greater(resume, 5 - Eps);
            foreach (var kind in new[] { BoltKind.Strike, BoltKind.Jump })
            {
                // Chain Lightning's next shot comes 0.8 s of run clock after its first, past these.
                var rest = Bolts(sim, kind).Where(b => b.T > shot + Eps && b.T < resume + 0.7).ToList();
                Assert.AreEqual(new[] { 1, 2, 3, 4, 5 }, rest.Select(b => b.Index).ToArray(), $"{kind}: the rest of it");
                for (int k = 0; k < rest.Count; k++)
                    Assert.AreEqual(resume + 0.125 * k, rest[k].T, 1e-6, $"{kind} {k + 1}: from the resume's sixteenth on");
            }
        }
        [Test]
        public void AVolleysLaterStrikesKeepItsShotsPowerAndRadiusThroughALevelUp()
        {
            // Rank 2: three strikes of 1.5 over 0.4571 m. Rank 3 (2.5 over twice the area) learned after
            // the first: the volley's other two keep its shot's numbers, as native's pending strikes do.
            var sim = Start(Config(new[] { ("lightning-ring", 2) }), new FixtureSong(3).Beats(0, 20, 120));
            Place(sim, 4, 0);
            int ring = Content.WeaponIndex("lightning-ring");
            while (Bolts(sim).Count == 0) sim.StepTo(sim.Now + Simulation.TickSeconds);
            sim.Arsenal.Learn(ring);
            sim.StepTo(sim.Now + 1);
            var volley = Bolts(sim).Take(3).ToList();
            Assert.AreEqual(new[] { 0, 1, 2 }, volley.Select(b => b.Index).ToArray());
            double radius = Content.Weapon("lightning-ring").Shape.RadiusM;
            foreach (var b in volley)
            {
                Assert.AreEqual(radius, b.Radius, Eps, $"strike {b.Index}'s radius");
                Assert.IsTrue(HitsAt(sim, b.T).All(h => Math.Abs(h.Damage - 1.5) < Eps), $"strike {b.Index}'s power");
                Assert.IsNotEmpty(HitsAt(sim, b.T));
            }
        }
        [Test]
        public void AChainsLaterJumpsKeepItsShotsPowerThroughALevelUp()
        {
            // Rank 2: four jumps of 1.2. Rank 3 (1.5) learned after the first: the rest keep its shot's power.
            var sim = Start(Config(new[] { ("chain-lightning", 2) }), new FixtureSong(3).Beats(0, 10, 120));
            for (int k = 0; k < 4; k++) Place(sim, 4 + 3 * k, 0);
            int chain = Content.WeaponIndex("chain-lightning");
            while (Bolts(sim).Count == 0) sim.StepTo(sim.Now + Simulation.TickSeconds);
            sim.Arsenal.Learn(chain);
            sim.StepTo(sim.Now + 0.6);
            var jumps = Bolts(sim).Take(4).ToList();
            Assert.AreEqual(new[] { 0, 1, 2, 3 }, jumps.Select(b => b.Index).ToArray());
            foreach (var b in jumps)
            {
                var hits = HitsAt(sim, b.T);
                Assert.IsNotEmpty(hits);
                Assert.IsTrue(hits.All(h => Math.Abs(h.Damage - 1.2) < Eps), $"jump {b.Index}'s power");
            }
        }
    }
}
