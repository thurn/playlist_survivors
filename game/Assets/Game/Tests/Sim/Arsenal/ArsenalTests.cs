using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using PS.Game.Sim.Content;

namespace PS.Game.Sim.Tests
{
    /// <summary>
    /// The arsenal (unity-game.md, The Other Weapons and Pickups, Level-ups and Chests): slots and
    /// ranks, the passives' stats, shots on the beat grid, gems and experience, the scripted offers
    /// and the level-up's cards.
    /// </summary>
    public class ArsenalTests
    {
        const double Eps = 1e-9;

        static GameContent Content => HordeFixture.Content;

        static SimConfig Config(ArsenalConfig arsenal) => new SimConfig { Content = Content, Arsenal = arsenal };

        static Simulation Start(SimConfig config, FixtureSong song)
        {
            var sim = new Simulation(config);
            if (song != null)
                foreach (var k in Drive.Known(song.Entries().Where(e => e.Kind == EntryKind.Beat || e.Kind == EntryKind.Tick)))
                    sim.AddKnown(k.Entry, k.KnownAt);
            return sim;
        }

        static void Tap(Simulation sim, Key key)
        {
            sim.Press(key, sim.Now);
            sim.Release(key, sim.Now);
        }

        static string Name(OfferCard c) =>
            c.Item == CardItem.Heal ? "heal" : c.Item == CardItem.Weapon ? Content.Weapons[c.Index].Id : Content.Passives[c.Index].Id;

        // ---------------------------------------------------------------- slots, ranks and stats

        [Test]
        public void SixWeaponSlotsBoundTheArsenalAndAnOwnedPickRanksUpInPlace()
        {
            var sim = new Simulation(Config(new ArsenalConfig()));
            var arsenal = sim.Arsenal;
            string[] six = { "garlic", "magic-wand", "king-bible", "lightning-ring", "chain-lightning" };
            foreach (string id in six) arsenal.Learn(Content.WeaponIndex(id));
            Assert.IsTrue(arsenal.WeaponSlotsFull);
            Assert.Throws<InvalidOperationException>(() => arsenal.Learn(Content.WeaponIndex("fire-trail")), "a seventh weapon");
            arsenal.Learn(Content.WeaponIndex("sword"));
            Assert.AreEqual(6, sim.State.WeaponSlots.Length, "a rank, not a slot");
            Assert.AreEqual((Content.WeaponIndex("sword"), 2), (sim.State.WeaponSlots[0].Index, sim.State.WeaponSlots[0].Rank));
            for (int r = 2; r < 8; r++) arsenal.Learn(Content.WeaponIndex("sword"));
            Assert.Throws<InvalidOperationException>(() => arsenal.Learn(Content.WeaponIndex("sword")), "past rank 8");

            int duplicator = Content.PassiveIndex("duplicator");
            arsenal.LearnPassive(sim.Now, duplicator);
            arsenal.LearnPassive(sim.Now, duplicator);
            Assert.Throws<InvalidOperationException>(() => arsenal.LearnPassive(sim.Now, duplicator), "Duplicator stops at 2");
            Assert.AreEqual(2, sim.State.Stats.Amount);
        }

        [Test]
        public void EveryPassiveRankChangesTheStatsByNativesNumbers()
        {
            var cases = new (string passive, Func<Stats, double> stat, double[] ranks)[]
            {
                ("spinach", s => s.Might, new[] { 1.1, 1.2, 1.3, 1.4, 1.5 }),
                ("hollow-heart", s => s.MaxHp, new[] { 144.0, 172.8, 207.36, 248.832, 298.5984 }),
                ("pummarola", s => s.Recovery, new[] { 0.2, 0.4, 0.6, 0.8, 1.0 }),
                ("empty-tome", s => s.Cooldown, new[] { 0.92, 0.84, 0.76, 0.68, 0.6 }),
                ("spellbinder", s => s.Duration, new[] { 1.1, 1.2, 1.3, 1.4, 1.5 }),
                ("duplicator", s => s.Amount, new[] { 1.0, 2.0 }),
                ("magnet", s => s.Magnet, new[] { 1.3, 1.6, 1.9, 2.2, 2.5 }),
                ("armour", s => s.Armor, new[] { 2.0, 3.0, 4.0, 5.0, 6.0 }),
            };
            foreach (var (passive, stat, ranks) in cases)
            {
                var sim = new Simulation(Config(new ArsenalConfig()));
                for (int r = 0; r < ranks.Length; r++)
                {
                    sim.Arsenal.LearnPassive(sim.Now, Content.PassiveIndex(passive));
                    Assert.AreEqual(ranks[r], stat(sim.State.Stats), 1e-9, $"{passive} rank {r + 1}");
                }
            }
        }

        [Test]
        public void HollowHeartHealsWhatItAddsAndArmourAndMagnetReachTheirRules()
        {
            var sim = new Simulation(Config(new ArsenalConfig()));
            var hero = sim.State.Hero;
            sim.Health.Hurt(sim, sim.Now, EnemyAttackKind.Lunge, 50, 1, 0);
            Assert.AreEqual(120 - 49, hero.Health, Eps, "power 50 less the base Armor 1");
            sim.Arsenal.LearnPassive(sim.Now, Content.PassiveIndex("hollow-heart"));
            Assert.AreEqual(144, hero.MaxHealth, Eps);
            Assert.AreEqual(71 + 24, hero.Health, Eps, "the 24 added are healed");
            for (int r = 0; r < 4; r++) sim.Arsenal.LearnPassive(sim.Now, Content.PassiveIndex("armour"));
            sim.StepTo(1);
            double before = hero.Health;
            sim.Health.Hurt(sim, sim.Now, EnemyAttackKind.Lunge, 10, 1, 0);
            Assert.AreEqual(before - 5, hero.Health, Eps, "power 10 less Armor 5");
            sim.Arsenal.LearnPassive(sim.Now, Content.PassiveIndex("magnet"));
            Assert.AreEqual(4.4444 * 1.3, sim.State.PickupRadius, 1e-9);
        }

        [Test]
        public void PummarolaHealsItsRecoveryEverySecondOfRunClockButNotInAFreeze()
        {
            var sim = new Simulation(Config(new ArsenalConfig { Passives = new[] { ("pummarola", 5) } }));
            sim.Health.Hurt(sim, sim.Now, EnemyAttackKind.Lunge, 51, 1, 0);
            Assert.AreEqual(70, sim.State.Hero.Health, Eps);
            sim.StepTo(10);
            Assert.AreEqual(80, sim.State.Hero.Health, 1e-6, "1 a second for 10 s");
            sim.Control(RunControl.Pause);
            sim.StepTo(20);
            Assert.AreEqual(80, sim.State.Hero.Health, 1e-6, "nothing while frozen");
        }

        // ---------------------------------------------------------------- shots on the grid

        /// <summary>The shots of weapon <paramref name="id"/>: its attack events' times.</summary>
        static List<double> Shots(Simulation sim, string id)
        {
            int w = Content.WeaponIndex(id);
            return Drive.Events(sim, SimEventKind.Attack).Select(e => e.AsAttack).Where(a => a.ByHero && a.Weapon == w).Select(a => a.T).ToList();
        }

        [Test]
        public void SnappedShotsKeepTheirIntervalsThroughputOnTheGridAtThreeTempos()
        {
            const double Seconds = 600;
            foreach (double bpm in new[] { 70.0, 128.0, 174.0 })
            {
                var song = new FixtureSong(5).Beats(0, Seconds + 2, bpm);
                foreach (var (id, rank, cooldown) in new[] { ("garlic", 1, 1.0), ("garlic", 8, 1.0), ("garlic", 8, 0.6), ("holy-wand", 1, 0.6) })
                {
                    var stats = Content.Progression.Hero.Stats;
                    stats.Cooldown = cooldown;
                    var sim = Start(Config(new ArsenalConfig { Weapons = new[] { (id, rank) }, Stats = stats }), song);
                    sim.StepTo(Seconds - Eps);
                    var def = Content.Weapon(id);
                    double interval = def.Rank(rank).IntervalS * cooldown;
                    var shots = Shots(sim, id);
                    double expected = Seconds / interval;
                    TestContext.Progress.WriteLine($"{bpm} BPM {id} rank {rank}, interval {interval:F3} s: {shots.Count} shots in {Seconds} s, expected {expected:F1}");
                    Assert.AreEqual(1, shots.Count / expected, 0.01, $"{bpm} BPM {id} rank {rank} at cooldown {cooldown}: throughput");
                    double step = 60 / bpm * (def.Grid == FireGrid.Eighth ? 0.5 : 1);
                    var beats = song.BeatTimes();
                    for (int k = 0; k < shots.Count; k++)
                    {
                        double ready = k * interval;
                        Assert.GreaterOrEqual(shots[k], ready - 1e-6, $"shot {k} before it was ready");
                        Assert.Less(shots[k] - ready, step + 1e-6, $"{bpm} BPM {id}: shot {k} waited a whole grid step");
                        int b = beats.FindLastIndex(x => x <= shots[k] + 1e-9);
                        double phase = (shots[k] - beats[b]) / (beats[b + 1] - beats[b]) * 4;
                        Assert.AreEqual(Math.Round(phase), phase, 1e-6, $"shot {k} at {shots[k]} is on a sixteenth");
                    }
                    if (id == "holy-wand" && bpm == 70)
                        Assert.IsTrue(shots.Any(t => Math.Abs(t / (step / 2) - Math.Round(t / (step / 2))) < 1e-6 &&
                                                     Math.Abs(t / step - Math.Round(t / step)) > 1e-6),
                            "shots crowding one eighth move to the sixteenth after it");
                }
            }
        }

        [Test]
        public void ShotsStayOnTheGridWhileTheNextBeatIsNotYetCommitted()
        {
            // Committed beats come from hindsight, often less than a beat ahead: here each is known
            // only 0.05 s before its own time, so the next beat is unknown for nearly every interval.
            // The latest beat continues at its period, and shots keep landing on its sixteenths.
            const double Seconds = 120;
            foreach (double bpm in new[] { 70.0, 128.0 })
            {
                var stats = Content.Progression.Hero.Stats;
                stats.Cooldown = 0.6;
                var sim = new Simulation(Config(new ArsenalConfig { Weapons = new[] { ("holy-wand", 1) }, Stats = stats }));
                var song = new FixtureSong(5).Beats(0, Seconds + 2, bpm);
                foreach (var e in song.Entries().Where(e => e.Kind == EntryKind.Beat || e.Kind == EntryKind.Tick))
                    sim.AddKnown(e, Math.Max(0, e.Kind == EntryKind.Beat ? e.T - 0.05 : e.T));
                sim.StepTo(Seconds - Eps);
                var shots = Shots(sim, "holy-wand");
                Assert.AreEqual(Seconds / 0.3, shots.Count, 1, $"{bpm} BPM: throughput");
                var beats = song.BeatTimes();
                for (int k = 0; k < shots.Count; k++)
                {
                    if (k > 0) Assert.Greater(shots[k], shots[k - 1] + 1e-6, $"{bpm} BPM: shot {k} shares its point");
                    int b = beats.FindLastIndex(x => x <= shots[k] + 1e-9);
                    double phase = (shots[k] - beats[b]) / (beats[b + 1] - beats[b]) * 4;
                    Assert.AreEqual(Math.Round(phase), phase, 1e-6, $"{bpm} BPM: shot {k} at {shots[k]} is on a sixteenth");
                }
            }
        }

        [Test]
        public void KingBiblesIntervalWaitsForItsDurationTimesDuration()
        {
            // native's interval_depends_on_duration: 3 s x Cooldown + 3 s x Duration, on its raw interval with no beat.
            var stats = Content.Progression.Hero.Stats;
            stats.Cooldown = 0.5;
            stats.Duration = 1.5;
            var sim = new Simulation(Config(new ArsenalConfig { Weapons = new[] { ("king-bible", 1) }, Stats = stats }));
            sim.StepTo(20);
            Assert.AreEqual(new[] { 0, 6.0, 12.0, 18.0 }, Shots(sim, "king-bible").Select(t => Math.Round(t, 6)).ToArray());
        }

        [Test]
        public void WithNoBeatAWeaponShootsOnItsRawInterval()
        {
            var stats = Content.Progression.Hero.Stats;
            stats.Cooldown = 0.6;
            var sim = new Simulation(Config(new ArsenalConfig { Weapons = new[] { ("holy-wand", 1) }, Stats = stats }));
            sim.StepTo(2.99);
            var shots = Shots(sim, "holy-wand");
            Assert.AreEqual(10, shots.Count);
            for (int k = 0; k < shots.Count; k++) Assert.AreEqual(k * 0.3, shots[k], 1e-9);
        }

        [Test]
        public void AFreezeHoldsAReadyShotUntilTheResumeBeatWithoutSongTimeDebt()
        {
            // Garlic every 1.3 s of run clock at 120 BPM: ready at 1.3, it waits for the beat at 1.5.
            var sim = Start(Config(new ArsenalConfig { Weapons = new[] { ("garlic", 1) } }), new FixtureSong(3).Beats(0, 40, 120));
            sim.StepTo(1.4);
            sim.Control(RunControl.Pause);
            sim.StepTo(20.4);
            sim.Control(RunControl.Resume);
            sim.StepTo(23);
            var shots = Shots(sim, "garlic");
            Assert.AreEqual(new[] { 0, 20.5, 22.0 }, shots.Take(3).Select(t => Math.Round(t, 6)).ToArray(),
                "the held shot on the resume beat, the next 1.3 s of run clock later (21.7) on the beat after");
        }

        // ---------------------------------------------------------------- gems and experience

        [Test]
        public void GemsInThePickupRadiusTimesMagnetFlyInAndArriveOnASixteenth()
        {
            var sim = Start(Config(new ArsenalConfig { Weapons = new (string, int)[0], Cards = false }), new FixtureSong(3).Beats(0, 30, 120));
            HordeFixture.PutHero(sim, 0, 0);
            sim.StepTo(5);
            var gems = sim.Arsenal.Gems;
            gems.Drop(sim, sim.Now, 3, 0, 20);
            gems.Drop(sim, sim.Now, 0, 8, 20);
            var drops = Drive.Events(sim, SimEventKind.Drop).Select(e => e.AsDrop).ToList();
            Assert.AreEqual(2, drops.Count, "worth at least 10 each");
            sim.StepTo(8);
            var flights = Drive.Events(sim, SimEventKind.GemFlight).Select(e => e.AsGemFlight).ToList();
            Assert.AreEqual(new[] { drops[0].Item }, flights.Select(f => f.Gem).ToArray(), "only the gem within 4.44 m and its body");
            var pickup = Drive.Events(sim, SimEventKind.Pickup).Single().AsPickup;
            Assert.AreEqual(drops[0].Item, pickup.Item);
            Assert.Greater(pickup.T, flights[0].T + Gems.NeverLateS, "the outward kick, the pull, then the margin");
            Assert.Less(pickup.T, flights[0].T + 1.5);
            Assert.AreEqual(Math.Round(pickup.T * 8), pickup.T * 8, 1e-9, "on a sixteenth of the 120 BPM grid");
            Assert.AreEqual(drops[0].Value, sim.State.Experience, Eps, "its value, at Growth 1");

            for (int r = 0; r < 5; r++) sim.Arsenal.LearnPassive(sim.Now, Content.PassiveIndex("magnet"));
            sim.StepTo(10);
            Assert.AreEqual(2, Drive.Events(sim, SimEventKind.Pickup).Count, "Magnet 2.5 reaches the gem 8 m away");
            Assert.AreEqual(drops[0].Value + drops[1].Value, sim.State.Experience, Eps);
        }

        [Test]
        public void AtTheCapADropGrowsTheNearestGem()
        {
            var sim = new Simulation(Config(new ArsenalConfig { Weapons = new (string, int)[0], Cards = false }));
            HordeFixture.PutHero(sim, 0, 0);
            var gems = sim.Arsenal.Gems;
            int cap = Content.Progression.Gems.Cap;
            for (int i = 0; gems.OnField < cap; i++) gems.Drop(sim, sim.Now, 50 + i, 50, 20);
            int before = sim.EventCount;
            gems.Drop(sim, sim.Now, 50.2, 50, 20);
            Assert.AreEqual(cap, gems.OnField);
            var grown = sim.EventsSince(before).ToArray().Single().AsDrop;
            Assert.AreEqual((50.0, 50.0), (grown.X, grown.Y), "the nearest gem, where it lies");
            Assert.AreEqual(0, grown.Item);
            Assert.Greater(grown.Value, 20, "its value and the new one's");
        }

        [Test]
        public void ExperienceFollowsTheCurveAndGrowthDoublesAtLevelTwenty()
        {
            var sim = Start(Config(new ArsenalConfig { Cards = false }), null);
            var levels = sim.Arsenal.LevelUps;
            var curve = Content.Progression.Experience;
            levels.Gain(sim, sim.Now, curve.Total(1) - 0.1);
            Assert.AreEqual(1, sim.State.Level);
            levels.Gain(sim, sim.Now, 0.1 + Eps);
            Assert.AreEqual(2, sim.State.Level, "the curve's total at level 1 for level 2");
            levels.Gain(sim, sim.Now, curve.Total(2) - sim.State.Experience + Eps);
            Assert.AreEqual(3, sim.State.Level);
            levels.Gain(sim, sim.Now, curve.Total(19) - sim.State.Experience + Eps);
            Assert.AreEqual(20, sim.State.Level);
            Assert.AreEqual(19, sim.State.PendingLevels, "no cards: every level waits");
            double before = sim.State.Experience;
            levels.Gain(sim, sim.Now, 1);
            Assert.AreEqual(before + 2, sim.State.Experience, Eps, "Growth 2 while at level 20");
            Assert.AreEqual(curve.Total(20), sim.State.ExperienceNext, Eps);
        }

        // ---------------------------------------------------------------- offers and cards

        static readonly string[] AlwaysFirst =
        {
            "magic-wand:0 garlic:0 sword:1 0",
            "empty-tome:0 chain-lightning:0 hollow-heart:0 0",
            "spinach:0 fire-trail:0 hollow-heart:0 0",
            "hollow-heart:0 sword:1 magnet:0 0",
            "garlic:0 sword:1 armour:0 0",
            "sword:1 spinach:1 hollow-heart:1 0",
            "sword:2 spinach:1 hollow-heart:1 0",
            "sword:3 chain-lightning:0 hollow-heart:1 0",
            "sword:4 fire-trail:0 hollow-heart:1 0",
            "sword:5 spinach:1 hollow-heart:1 0",
            "sword:6 spinach:1 hollow-heart:1 0",
            "sword:7 spinach:1 magnet:0 0",
            "pummarola:0 spinach:1 armour:0 0",
            "garlic:1 spinach:1 hollow-heart:1 0",
            "garlic:2 spinach:1 hollow-heart:1 0",
            "garlic:3 spinach:1 hollow-heart:1 0",
            "garlic:4 chain-lightning:0 hollow-heart:1 0",
            "garlic:5 fire-trail:0 hollow-heart:1 0",
            "garlic:6 spinach:1 hollow-heart:1 0",
            "garlic:7 spinach:1 hollow-heart:1 0",
            "king-bible:0 spinach:1 magnet:0 0",
            "lightning-ring:0 spinach:1 armour:0 0",
            "magic-wand:1 spinach:1 hollow-heart:1 0",
            "magic-wand:2 spinach:1 hollow-heart:1 0",
            "magic-wand:3 spinach:1 hollow-heart:1 0",
            "magic-wand:4 chain-lightning:0 hollow-heart:1 0",
            "magic-wand:5 fire-trail:0 hollow-heart:1 0",
            "magic-wand:6 spinach:1 hollow-heart:1 0",
            "magic-wand:7 spinach:1 hollow-heart:1 0",
            "spellbinder:0 spinach:1 magnet:0 0",
            "king-bible:1 spinach:1 armour:0 0",
            "king-bible:2 spinach:1 hollow-heart:1 0",
            "king-bible:3 spinach:1 hollow-heart:1 0",
            "king-bible:4 spinach:1 hollow-heart:1 0",
            "king-bible:5 spinach:1 hollow-heart:1 0",
            "king-bible:6 spinach:1 hollow-heart:1 0",
            "king-bible:7 spinach:1 hollow-heart:1 0",
            "lightning-ring:1 spinach:1 hollow-heart:1 0",
            "lightning-ring:2 spinach:1 hollow-heart:1 0",
            "lightning-ring:3 spinach:1 hollow-heart:1 0",
            "lightning-ring:4 spinach:1 hollow-heart:1 0",
            "lightning-ring:5 spinach:1 hollow-heart:1 0",
            "lightning-ring:6 spinach:1 hollow-heart:1 0",
            "lightning-ring:7 spinach:1 hollow-heart:1 0",
            "duplicator:0 spinach:1 hollow-heart:1 0",
            "spinach:1 hollow-heart:1 pummarola:1 0",
            "spinach:2 hollow-heart:1 pummarola:1 0",
            "spinach:3 hollow-heart:1 pummarola:1 0",
            "spinach:4 hollow-heart:1 pummarola:1 0",
            "hollow-heart:1 empty-tome:1 pummarola:1 0",
            "hollow-heart:2 empty-tome:1 pummarola:1 0",
            "hollow-heart:3 empty-tome:1 pummarola:1 0",
            "hollow-heart:4 empty-tome:1 pummarola:1 0",
            "pummarola:1 empty-tome:1 spellbinder:1 0",
            "pummarola:2 empty-tome:1 spellbinder:1 0",
            "pummarola:3 empty-tome:1 spellbinder:1 0",
            "pummarola:4 empty-tome:1 spellbinder:1 0",
            "empty-tome:1 spellbinder:1 duplicator:1 0",
            "empty-tome:2 spellbinder:1 duplicator:1 0",
            "empty-tome:3 spellbinder:1 duplicator:1 0",
            "empty-tome:4 spellbinder:1 duplicator:1 0",
            "spellbinder:1 duplicator:1 chain-lightning:0 0",
            "spellbinder:2 duplicator:1 chain-lightning:0 0",
            "spellbinder:3 duplicator:1 chain-lightning:0 0",
            "spellbinder:4 duplicator:1 chain-lightning:0 0",
            "duplicator:1 chain-lightning:0 fire-trail:0 0",
            "chain-lightning:0 fire-trail:0 chain-lightning:0 0",
            "chain-lightning:1 chain-lightning:1 chain-lightning:1 0",
            "chain-lightning:2 chain-lightning:2 chain-lightning:2 0"
        };

        static readonly string[] Patterned =
        {
            "magic-wand:0 garlic:0 sword:1 2",
            "empty-tome:0 chain-lightning:0 hollow-heart:0 1",
            "spinach:0 fire-trail:0 hollow-heart:0 2",
            "hollow-heart:1 sword:2 magnet:0 0",
            "garlic:0 sword:2 armour:0 2",
            "sword:2 spinach:0 hollow-heart:2 0",
            "sword:3 spinach:0 hollow-heart:2 1",
            "sword:3 chain-lightning:1 hollow-heart:2 0",
            "sword:4 fire-trail:0 hollow-heart:2 1",
            "sword:4 spinach:1 hollow-heart:2 2",
            "sword:4 spinach:1 hollow-heart:3 1",
            "sword:4 spinach:2 magnet:0 2",
            "pummarola:0 sword:4 armour:1 0",
            "garlic:0 sword:4 hollow-heart:3 2",
            "garlic:0 sword:4 hollow-heart:4 0",
            "garlic:1 sword:4 hollow-heart:4 1",
            "garlic:1 chain-lightning:1 hollow-heart:4 0",
            "garlic:2 fire-trail:1 hollow-heart:4 1",
            "garlic:2 sword:5 hollow-heart:4 2",
            "garlic:2 sword:5 pummarola:1 1",
            "king-bible:0 sword:6 magnet:1 2",
            "lightning-ring:0 sword:6 armour:1 0",
            "magic-wand:0 sword:6 pummarola:1 2",
            "magic-wand:0 sword:6 pummarola:2 0",
            "magic-wand:1 sword:6 pummarola:2 1",
            "magic-wand:1 chain-lightning:1 pummarola:2 0",
            "magic-wand:2 fire-trail:2 pummarola:2 1",
            "magic-wand:2 sword:7 pummarola:2 2",
            "magic-wand:2 sword:7 pummarola:3 1",
            "spellbinder:0 spinach:2 magnet:2 2",
            "lightning-ring:1 spinach:2 armour:1 0",
            "chain-lightning:1 spinach:2 pummarola:3 2",
            "chain-lightning:1 spinach:2 pummarola:4 0",
            "garlic:2 spinach:2 pummarola:4 1",
            "garlic:2 spinach:3 pummarola:4 0",
            "magic-wand:2 spinach:3 pummarola:4 1",
            "magic-wand:2 spinach:4 pummarola:4 2",
            "lightning-ring:2 spinach:4 magic-wand:2 1",
            "lightning-ring:2 garlic:3 magic-wand:2 2",
            "lightning-ring:2 garlic:3 magic-wand:3 0",
            "lightning-ring:3 garlic:3 magic-wand:3 2",
            "lightning-ring:3 garlic:3 magic-wand:4 0",
            "lightning-ring:4 garlic:3 magic-wand:4 1",
            "lightning-ring:4 garlic:4 magic-wand:4 0",
            "duplicator:0 garlic:4 magic-wand:4 1",
            "chain-lightning:2 garlic:5 magic-wand:4 2",
            "chain-lightning:2 garlic:5 magic-wand:5 1",
            "chain-lightning:2 garlic:6 magic-wand:5 2",
            "chain-lightning:2 garlic:6 magic-wand:6 0",
            "chain-lightning:3 garlic:6 magic-wand:6 2",
            "chain-lightning:3 garlic:6 magic-wand:7 0",
            "fire-trail:3 garlic:6 magic-wand:7 1",
            "fire-trail:3 garlic:7 magic-wand:7 0",
            "chain-lightning:4 garlic:7 magic-wand:7 1",
            "chain-lightning:4 fire-trail:4 magic-wand:7 2",
            "chain-lightning:4 fire-trail:4 lightning-ring:5 1",
            "chain-lightning:4 lightning-ring:5 fire-trail:5 2",
            "empty-tome:0 chain-lightning:4 lightning-ring:5 0",
            "empty-tome:1 chain-lightning:4 lightning-ring:5 2",
            "empty-tome:1 chain-lightning:4 lightning-ring:6 0",
            "empty-tome:2 chain-lightning:4 lightning-ring:6 1",
            "chain-lightning:5 lightning-ring:6 fire-trail:6 0",
            "lightning-ring:6 chain-lightning:6 fire-trail:6 1",
            "lightning-ring:6 fire-trail:6 chain-lightning:7 2",
            "lightning-ring:6 fire-trail:6 armour:1 1",
            "lightning-ring:6 fire-trail:7 armour:1 2",
            "lightning-ring:6 fire-trail:7 empty-tome:2 0",
            "lightning-ring:7 fire-trail:7 empty-tome:2 2",
            "lightning-ring:7 fire-trail:7 armour:2 0"
        };

        /// <summary>
        /// native's offers (sim/tests, its offer_table().cards along the same picks, at 8aefa93b): each
        /// line is a level's three cards as item:rank-before, then the card picked. Every level from
        /// 2 to 70 is reached at once and its cards are taken through the keys, one offer after another.
        /// </summary>
        [TestCase(0)]
        [TestCase(1)]
        public void OffersMatchNativesScriptedTableAlongAPickedPath(int path)
        {
            var golden = path == 0 ? AlwaysFirst : Patterned;
            var sim = Start(Config(new ArsenalConfig()), new FixtureSong(3).Beats(0, 200, 120));
            sim.StepTo(1);
            sim.Arsenal.LevelUps.Gain(sim, sim.Now, Content.Progression.Experience.Total(69));
            Assert.AreEqual(70, sim.State.Level);
            for (int level = 2; level <= 70; level++)
            {
                var offer = sim.State.Offer;
                Assert.IsTrue(offer.Open, $"level {level}'s offer");
                Assert.AreEqual(level, offer.Level);
                string[] want = golden[level - 2].Split(' ');
                string got = string.Join(" ", Enumerable.Range(0, 3).Select(k => Name(offer.Card(k)) + ":" + offer.Card(k).Before));
                Assert.AreEqual(string.Join(" ", want.Take(3)), got, $"level {level}");
                int pick = int.Parse(want[3]);
                Tap(sim, pick == 0 ? Key.Left : pick == 1 ? Key.Up : Key.Right);
                sim.StepTo(offer.Reveal2);
                Tap(sim, Key.Enter);
            }
            Assert.IsFalse(sim.State.Offer.Open);
            Assert.AreEqual(0, sim.State.PendingLevels);
            int ranks = 0;
            foreach (var w in sim.State.WeaponSlots) ranks += w.Rank;
            foreach (var p in sim.State.PassiveSlots) ranks += p.Rank;
            Assert.AreEqual(70, ranks, "the sword's first rank and one a level");
        }

        [Test]
        public void ALevelUpFreezesFlipsItsCardsInOnBeatsAndResumesOnTheNextBeatAfterTheConfirm()
        {
            var sim = Start(Config(new ArsenalConfig()), new FixtureSong(3).Beats(0, 30, 120));
            sim.StepTo(10.01);
            double t = sim.Now;
            sim.Arsenal.LevelUps.Gain(sim, t, 5);
            Assert.IsTrue(sim.State.FrozenFor(FreezeReason.LevelUp));
            var up = Drive.Events(sim, SimEventKind.LevelUp).Single().AsLevelUp;
            Assert.AreEqual((2, 0), (up.Level, up.Offer));
            Assert.AreEqual(new[] { 10.5, 11.0, 11.5 }, Enumerable.Range(0, 3).Select(k => Math.Round(up.Reveal(k), 6)).ToArray(),
                "the next three beats");
            Assert.AreEqual(new[] { "magic-wand", "garlic", "sword" }, Enumerable.Range(0, 3).Select(k => Name(up.Card(k))).ToArray());
            Assert.AreEqual((1, 2, 1), (up.Card(2).Before, up.Card(2).After, up.Card(0).After), "sword 1 to 2, a new wand");

            Tap(sim, Key.Right);
            Tap(sim, Key.Enter);
            Assert.IsTrue(sim.State.Offer.Open, "a confirm before the third card flips in does nothing");
            sim.StepTo(11.6);
            Tap(sim, Key.Space);
            Assert.IsFalse(sim.State.Offer.Open);
            Assert.AreEqual(2, sim.Arsenal.WeaponRank(Content.WeaponIndex("sword")));
            var cards = Drive.Events(sim, SimEventKind.Card).Select(e => e.AsCard).ToList();
            Assert.AreEqual(new[] { (2, false), (2, true) }, cards.Select(c => (c.Card, c.Confirmed)).ToArray());
            Assert.IsTrue(sim.State.Frozen, "the world waits for the beat");
            sim.StepTo(12.1);
            Assert.IsFalse(sim.State.Frozen);
            var resume = Drive.Events(sim, SimEventKind.Freeze).Select(e => e.AsFreeze).Last();
            Assert.AreEqual((12.0, false), (Math.Round(resume.T, 6), resume.Starts));
            Assert.AreEqual(t + (sim.Now - 12.0), sim.State.RunClock, 1e-6, "the run clock stood still through the cards");
        }

        [Test]
        public void KeysHeldWhenTheCardsOpenWaitForAFreshPressAndNeverWalkTheHeroAfter()
        {
            var sim = Start(Config(new ArsenalConfig()), new FixtureSong(3).Beats(0, 30, 120));
            HordeFixture.PutHero(sim, 0, 0);
            sim.StepTo(10.01);
            sim.Press(Key.Space, sim.Now);
            sim.Press(Key.Right, sim.Now);
            sim.StepTo(10.2);
            sim.Arsenal.LevelUps.Gain(sim, sim.Now, 5);
            sim.StepTo(11.6);
            sim.Press(Key.Up, sim.Now);
            sim.Press(Key.Right, sim.Now);
            Assert.AreEqual(1, sim.State.Offer.Selected, "Right was held when the cards opened");
            sim.Press(Key.Space, sim.Now);
            Assert.IsTrue(sim.State.Offer.Open, "so was Space");
            sim.Release(Key.Space, sim.Now);
            sim.Press(Key.Space, sim.Now);
            Assert.IsFalse(sim.State.Offer.Open, "a fresh press confirms");
            Assert.AreEqual(2, sim.Arsenal.WeaponRank(Content.WeaponIndex("garlic")) + 1, "the second card, garlic");
            double x = sim.State.Hero.X;
            sim.StepTo(15);
            Assert.AreEqual(x, sim.State.Hero.X, Eps, "Right, still held since before the cards, doesn't walk the hero after them");
            sim.Release(Key.Right, sim.Now);
            sim.Press(Key.Right, sim.Now);
            Assert.Greater(sim.State.Hero.X, x, "a press after them does");
        }

        [Test]
        public void ChainedLevelsOpenOneOfferAfterAnotherAndAHeldConfirmTakesOnlyOne()
        {
            var sim = Start(Config(new ArsenalConfig()), new FixtureSong(3).Beats(0, 30, 120));
            sim.StepTo(10.01);
            sim.Arsenal.LevelUps.Gain(sim, sim.Now, 75);
            Assert.AreEqual((4, 3), (sim.State.Level, sim.State.PendingLevels), "3.2, 22 and 70 for levels 2 to 4");
            for (int level = 2; level <= 4; level++)
            {
                Assert.AreEqual(level, sim.State.Offer.Level);
                sim.StepTo(sim.State.Offer.Reveal2);
                sim.Press(Key.Enter, sim.Now);
                Assert.AreEqual(4 - level, sim.State.PendingLevels, "one pick");
                if (level < 4)
                {
                    sim.Press(Key.Space, sim.Now);
                    Assert.AreEqual(4 - level, sim.State.PendingLevels, "not before its cards flip in");
                    sim.Release(Key.Space, sim.Now);
                }
                sim.Release(Key.Enter, sim.Now);
            }
            Assert.IsFalse(sim.State.Offer.Open);
            Assert.AreEqual(3, Drive.Events(sim, SimEventKind.Card).Count(e => e.AsCard.Confirmed));
        }

        /// <summary>
        /// Level pacing, reported: the slice's horde in an open arena, the hero walking a square
        /// (sixteen steps a side, one every other beat) through its kills and taking every first
        /// card. The slice keeps 220 enemies alive from the start, far more than the run's first
        /// minutes, so it levels much faster than native's run (about level 10 by minute 5); the
        /// run's schedule (ps-msz.6.2) is what that is measured against.
        /// </summary>
        [Test]
        public void LevelPacingOnTheSlicesHordeWithAScriptedWalk()
        {
            var song = new FixtureSong(11).Beats(0, 420, 128);
            var config = Level.SliceLevel.Config(Content, null, Level.RunLevel.GameFootprint);
            var sim = new Simulation(config);
            Key[] square = { Key.Right, Key.Up, Key.Left, Key.Down };
            int beat = 0;
            double levelAt5 = double.NaN;
            Drive.Feed(sim, song.Entries(), 420, eachFrame: s =>
            {
                AutoCards.Confirm(s);
                if (s.State.LastBeat > beat)
                {
                    beat = s.State.LastBeat;
                    if (beat % 2 == 0) Tap(s, square[beat / 32 % 4]);
                }
                if (double.IsNaN(levelAt5) && s.State.RunClock >= 300) levelAt5 = s.State.Level;
            });
            TestContext.Progress.WriteLine($"slice, square walk: level {levelAt5} by 5:00 of run clock, " +
                                           $"{Drive.Events(sim, SimEventKind.Pickup).Count} gems of {Drive.Events(sim, SimEventKind.Drop).Count} drops, " +
                                           $"{Drive.Events(sim, SimEventKind.Death).Count} deaths");
            Assert.GreaterOrEqual(levelAt5, 10, "gems and cards keep coming");
        }
    }
}
