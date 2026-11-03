using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace Champions.Sim.Tests
{
    /// <summary>
    /// The progression (design.md, "Level-ups and Treasure"; Decisions 10-13 and 15): experience from kills, levels
    /// held for a quiet moment, the offers, the card moment, treasure and the run's start after the first offer.
    /// </summary>
    public class ProgressionTests
    {
        const double Tick = Simulation.TickSeconds;
        static readonly GameContent Content = Arena.Content;

        /// <summary>A sim on the content with <paramref name="build"/> (no attacks by default), given <paramref name="song"/>'s entries (none: no music, so quiet from 0.75 s).</summary>
        static Simulation Sim(FixtureSong song = null, Action<SimConfig> tweak = null, RunStart start = RunStart.Immediately, ulong seed = 1,
            params string[] build)
        {
            var config = new RunInputs { Seed = seed, Start = start, Loadout = Arena.Build(build) }.Build(Content);
            config.Dashes.LegacyForms = true;
            tweak?.Invoke(config);
            var sim = new Simulation(config);
            if (song != null)
                foreach (var k in Drive.Known(song.Entries())) sim.AddKnown(k.Entry, k.KnownAt);
            return sim;
        }

        static void EveryKillALevel(SimConfig config)
        {
            config.Progression.Levels = new[] { 1.0 };
            config.Progression.Growth = 1;
        }

        static int TypeOf(string id) => Array.FindIndex(Content.Enemies.Cast, c => c.Id == id) + 1;

        /// <summary>Kills <paramref name="count"/> enemies of type <paramref name="id"/> now, far from the hero (a dummy for null).</summary>
        static void Kill(Simulation sim, string id, int count = 1)
        {
            // Offer/curve tests inject collected value; actual death-position drops are tested in PickupTests.
            if (id != null) sim.Progression.CollectExperience(sim, Content.Enemies.Cast[TypeOf(id) - 1].Experience * count, sim.Now);
        }

        static void Press(Simulation sim, Key key) => sim.Press(key, sim.Now - 0.03);

        static List<SimEvent> Offers(Simulation sim) => Drive.Events(sim, SimEventKind.Offer);

        static int Rank(Simulation sim, in Card c) =>
            c.Kind == CardKind.Attack ? sim.Arsenal.Rank(c.Attack)
            : c.Kind == CardKind.Mod ? sim.Arsenal.Rank(c.Mod)
            : sim.State.Hero.Dash == c.Id ? sim.State.Hero.DashRank : 0;

        static int Top(in Card c) =>
            c.Kind == CardKind.Attack ? Upgrade.AttackTop : c.Kind == CardKind.Mod ? Upgrade.ModTop : Content.Dashes.Kinds[c.Id].TopRank;

        // ---- Experience ----

        [Test]
        public void CollectedValuesUseTheirTypesExperience()
        {
            var sim = Sim();
            sim.StepTo(1);
            Kill(sim, "skeleton", 3);
            Kill(sim, "bat", 2);
            Assert.AreEqual(4.0, sim.State.Experience, 1e-9, "fodder 1, bats 0.5, on the kill's frame");
            Kill(sim, "bigOrk");
            Assert.AreEqual(14.0, sim.State.Experience, 1e-9, "elites 10");
            Kill(sim, null, 4);
            Assert.AreEqual(14.0, sim.State.Experience, 1e-9, "a placed dummy gives none");
            Assert.AreEqual(1, sim.State.Level);
            Assert.AreEqual(Content.Progression.Levels[0], sim.State.ExperienceNeeded);
        }

        [Test]
        public void ALevelIsReachedWhenTheCurveSaysAndTheRestCarriesOn()
        {
            var sim = Sim(tweak: c =>
            {
                c.Progression.Levels = new[] { 5.0, 8.0 };
                c.Progression.Growth = 2;
            });
            sim.StepTo(0.5);
            Kill(sim, "troll");
            Assert.AreEqual(2, sim.State.Level);
            Assert.AreEqual(5.0, sim.State.Experience, 1e-9, "10 - 5 carried");
            Assert.AreEqual(8.0, sim.State.ExperienceNeeded);
            Assert.AreEqual(1, sim.State.HeldLevels);
            Kill(sim, "troll", 2);
            // 25: level 3 at 8 (17 left), level 4 at 16 (1 left), the curve past its list doubling.
            Assert.AreEqual(4, sim.State.Level);
            Assert.AreEqual(1.0, sim.State.Experience, 1e-9);
            Assert.AreEqual(32.0, sim.State.ExperienceNeeded);
            Assert.AreEqual(3, sim.State.HeldLevels);
            var ups = Drive.Events(sim, SimEventKind.LevelUp);
            CollectionAssert.AreEqual(new[] { 2, 3, 4 }, ups.Select(e => e.I0));
            CollectionAssert.AreEqual(new[] { 1, 2, 3 }, ups.Select(e => e.I1));
            Assert.IsTrue(ups.All(e => Math.Abs(e.T - 0.5) < 1e-9), "on the kill's frame");
            Assert.IsEmpty(Offers(sim), "held, not shown: the music hasn't been quiet yet");
        }

        [Test]
        public void TheContentsCurveRisesAndTheDefaultsRepeatIt()
        {
            var rules = Content.Progression;
            for (int l = 1; l < 12; l++) Assert.Greater(rules.Needed(l + 1), rules.Needed(l), $"level {l}");
            Assert.AreEqual(60, rules.HeldMaxS, "Held for a quiet moment: at most 60 s");
            var defaults = new ProgressionRules();
            CollectionAssert.AreEqual(rules.Levels, defaults.Levels);
            Assert.AreEqual(rules.Growth, defaults.Growth);
            Assert.AreEqual(rules.HeldMaxS, defaults.HeldMaxS);
        }

        // ---- Held for a quiet moment ----

        /// <summary>The time of the first event of <paramref name="kind"/> matching <paramref name="where"/>, or NaN.</summary>
        static double First(Simulation sim, SimEventKind kind, Func<SimEvent, bool> where = null) =>
            Drive.Events(sim, kind).Where(where ?? (_ => true)).Select(e => e.T).DefaultIfEmpty(double.NaN).First();

        [Test]
        public void AHeldLevelShowsAtTheFirstGap()
        {
            var sim = Sim(new FixtureSong().Beats(0, 30, 128).Gap(30, 34, silent: true).Beats(34, 50, 128), EveryKillALevel);
            sim.StepTo(5);
            Kill(sim, "skeleton");
            sim.StepTo(30);
            Assert.IsFalse(sim.State.Offer.Up, "held through the beats");
            Assert.AreEqual(1, sim.State.HeldLevels);
            sim.StepTo(36);
            double gap = First(sim, SimEventKind.Quiet, e => e.I0 == (int)QuietReason.Silence && e.T > 20);
            Assert.AreEqual(gap, First(sim, SimEventKind.Offer), 1e-9, "the cards open as 0.75 s of silence makes a gap");
            Assert.AreEqual(OfferKind.LevelUp, sim.State.Offer.Kind);
            Assert.AreEqual(0, sim.State.HeldLevels);
        }

        [Test]
        public void AHeldLevelShowsAtABeatlessStretch()
        {
            var sim = Sim(new FixtureSong().Beats(0, 30, 128).Gap(30, 40).Beats(40, 50, 128), EveryKillALevel);
            sim.StepTo(5);
            Kill(sim, "skeleton");
            sim.StepTo(40);
            double beatless = First(sim, SimEventKind.Quiet, e => e.I0 == (int)QuietReason.Beatless && e.T > 20);
            Assert.Greater(beatless, 30);
            Assert.AreEqual(beatless, First(sim, SimEventKind.Offer), 1e-9, "the cards open once 2 s pass without a reliable beat");
        }

        [Test]
        public void AHeldLevelShowsAfterSixtySecondsOnTheNextDownbeat()
        {
            var sim = Sim(new FixtureSong().Beats(0, 120, 128), EveryKillALevel);
            sim.StepTo(9.5);
            Kill(sim, "skeleton");
            sim.StepTo(68);
            Assert.IsFalse(sim.State.Offer.Up);
            sim.StepTo(80);
            double offer = First(sim, SimEventKind.Offer);
            double due = 9.5 + 60;
            var downbeat = Drive.Events(sim, SimEventKind.Beat).First(e => e.Downbeat && e.T >= due - 1e-9);
            Assert.AreEqual(Math.Ceiling(downbeat.T / Tick - 1e-6) * Tick, offer, 1e-9, "at the end of the downbeat's tick");
            Assert.Less(offer - due, 4 * 60 / 128.0 + Tick, "within a bar of the 60 s");
        }

        [Test]
        public void AnOverdueLevelShowsAtOnceWhenTheBeatGoes()
        {
            // Overdue at 69.5 s, as the beat goes: before 2 s beatless makes a quiet moment.
            var sim = Sim(new FixtureSong().Beats(0, 69.6, 128).Gap(69.6, 80), EveryKillALevel);
            sim.StepTo(9.5);
            Kill(sim, "skeleton");
            sim.StepTo(80);
            double lost = First(sim, SimEventKind.BeatState, e => e.I0 != (int)Reliability.Reliable && e.T > 60);
            double offer = First(sim, SimEventKind.Offer);
            Assert.That(offer, Is.InRange(lost, lost + Tick + 1e-9), "at the end of the tick the beat went in");
            Assert.Less(offer, First(sim, SimEventKind.Quiet, e => e.I0 == (int)QuietReason.Beatless), "before the quiet moment");
        }

        [Test]
        public void SeveralHeldLevelsShowOneAfterAnother()
        {
            var sim = Sim(tweak: EveryKillALevel);
            sim.StepTo(0.1);
            Kill(sim, "skeleton", 3);
            Assert.AreEqual(3, sim.State.HeldLevels);
            sim.StepTo(1);
            Assert.IsTrue(sim.State.Offer.Up, "no music: quiet from 0.75 s");
            for (int held = 2; held >= 0; held--)
            {
                Assert.AreEqual(held, sim.State.HeldLevels);
                double at = sim.Now;
                Press(sim, Key.One);
                Assert.AreEqual(held > 0, sim.State.Offer.Up, "the next opens on the pick");
                Assert.AreEqual(held > 0, sim.State.HeldFor(HoldReason.Cards));
                if (held > 0) Assert.AreEqual(at, Offers(sim).Last().T, 1e-9);
                sim.StepTo(sim.Now + 0.5);
            }
            var holds = Drive.Events(sim, SimEventKind.Hold).Where(e => e.I0 == (int)HoldReason.Cards).ToList();
            Assert.AreEqual(2, holds.Count, "the world held once, through all three");
            Assert.AreEqual(3, Drive.Events(sim, SimEventKind.Pick).Count);
        }

        /// <summary>A Fat Ogre duel from the start, no music, the hero unhurt and without attacks.</summary>
        static Simulation OgreDuel(Action<SimConfig> tweak = null) => BossTests.Duel("fatOgre", tweak: c =>
        {
            c.Hero.Health = 1e9;
            tweak?.Invoke(c);
        });

        /// <summary>The hero's blow killing the boss now.</summary>
        static void KillTheBoss(Simulation sim)
        {
            ref readonly var boss = ref sim.Enemies[sim.Bosses.Id];
            sim.Strikes.Land(sim, new Blow
            {
                Shape = HitShape.Circle(boss.X, boss.Y, 0.1), T = sim.Now, Damage = 1e9, Tags = HitTags.Strike, Source = BlowSource.SwordSlash, Owner = -1, Part = -1,
            });
        }

        [Test]
        public void DuringADuelHeldLevelsWaitForTheOgresTreasure()
        {
            var sim = OgreDuel(EveryKillALevel);
            while (!sim.Bosses.InDuel) sim.StepTo(sim.Now + Tick);
            Kill(sim, "skeleton", 2);
            sim.StepTo(sim.Now + 75);
            Assert.IsEmpty(Offers(sim), "neither the quiet nor 60 s shows a level in a duel");
            Assert.AreEqual(2, sim.State.HeldLevels);
            KillTheBoss(sim);
            Assert.IsFalse(sim.State.Offer.Up, "its blow lands first");
            sim.StepTo(sim.Now + Tick);
            Assert.AreEqual(OfferKind.Treasure, sim.State.Offer.Kind, "the treasure first, at the end of the tick it died in");
            Assert.AreEqual(First(sim, SimEventKind.BossDeath), First(sim, SimEventKind.Offer), Tick + 1e-9);
            Press(sim, Key.One);
            Assert.AreEqual(OfferKind.LevelUp, sim.State.Offer.Kind, "then the held levels");
            Press(sim, Key.One);
            Assert.AreEqual(OfferKind.LevelUp, sim.State.Offer.Kind);
            Press(sim, Key.One);
            Assert.IsFalse(sim.State.Offer.Up);
            Assert.IsFalse(sim.State.Held);
        }

        [Test]
        public void TheDragonLeavesNoTreasure()
        {
            var sim = BossTests.Duel("dragon", tweak: c => c.Hero.Health = 1e9);
            while (!sim.Bosses.InDuel) sim.StepTo(sim.Now + Tick);
            KillTheBoss(sim);
            sim.StepTo(sim.Now + 1);
            Assert.IsEmpty(Offers(sim));
            Assert.AreEqual(RunEnd.Victory, sim.State.End);
        }

        [Test]
        public void LegacyEntranceShatterLeavesCollectibleExperience()
        {
            var sim = OgreDuel();
            for (int i = 0; i < 4; i++) sim.Enemies.Spawn(sim, 2 + i, 2, 0.4, Arena.Tough, TypeOf("skeleton"));
            sim.Enemies.Spawn(sim, -3, 1, 0.9, Arena.Tough, TypeOf("troll"));
            while (!sim.Bosses.InDuel) sim.StepTo(sim.Now + Tick);
            Assert.AreEqual(5, Drive.Events(sim, SimEventKind.Kill).Count(e => e.I1 == (int)BlowSource.Boss));
            Assert.AreEqual(14.0, sim.Pickups.OutstandingExperience + sim.State.Experience, 1e-9, "legacy entrance kills leave conserved collectible value");
        }

        // ---- Offers ----

        [Test]
        public void TheFirstOfferHoldsANewDashANewAttackAndANewMod()
        {
            var dashes = new HashSet<int>();
            var attacks = new HashSet<int>();
            var mods = new HashSet<int>();
            for (ulong seed = 1; seed <= 40; seed++)
            {
                var sim = Sim(start: RunStart.FirstOffer, seed: seed, build: "swordSlash:1");
                var offer = sim.State.Offer;
                Assert.AreEqual(OfferKind.First, offer.Kind);
                Assert.AreEqual(3, offer.Count);
                Assert.AreEqual(RunPhase.Waiting, sim.State.Phase, "the world empty and held");
                Assert.IsTrue(sim.State.HeldFor(HoldReason.Cards));
                Assert.AreEqual(new[] { CardKind.Dash, CardKind.Attack, CardKind.Mod }, new[] { offer[0].Kind, offer[1].Kind, offer[2].Kind });
                for (int i = 0; i < 3; i++)
                {
                    Assert.IsTrue(offer[i].New, offer[i].ToString());
                    Assert.AreEqual(1, offer[i].Rank);
                }
                Assert.IsTrue(Content.Dashes.Kinds[offer[0].Id].Ranks[0].Damaging, "a damaging dash");
                Assert.AreNotEqual(AttackKind.SwordSlash, offer[1].Attack, "an attack the hero doesn't have");
                dashes.Add(offer[0].Id);
                attacks.Add(offer[1].Id);
                mods.Add(offer[2].Id);
            }
            Assert.AreEqual(4, dashes.Count, "every damaging dash comes up");
            Assert.AreEqual(4, attacks.Count);
            Assert.Greater(mods.Count, 6);
        }

        /// <summary>A level-up's offer holds three different cards the build can take, at least one ranking up something owned when anything can.</summary>
        static void AssertTakeable(Simulation sim, in Offer offer)
        {
            Assert.AreEqual(OfferKind.LevelUp, offer.Kind);
            bool canGrow = sim.State.Hero.DashRank < Content.Dashes.Kinds[sim.State.Hero.Dash].TopRank;
            for (int a = 0; a < Upgrade.AttackCount; a++) canGrow |= sim.Arsenal.Rank((AttackKind)a) is int r && r > 0 && r < Upgrade.AttackTop;
            for (int m = 0; m < Upgrade.ModCount; m++) canGrow |= sim.Arsenal.Rank((ModKind)m) == 1;
            Assert.AreEqual(3, offer.Count);
            bool grows = false;
            for (int i = 0; i < offer.Count; i++)
            {
                var c = offer[i];
                for (int j = 0; j < i; j++) Assert.IsFalse(c.SameThing(offer[j]), $"three different cards: {c}, {offer[j]}");
                Assert.AreEqual(Rank(sim, c), c.From, $"{c} raises what the build holds");
                Assert.AreEqual(c.From + 1, c.Rank, c.ToString());
                Assert.LessOrEqual(c.Rank, Top(c), $"{c}: no rank past the top, no evolution");
                grows |= !c.New;
                if (!c.New) continue;
                if (c.Kind == CardKind.Attack) Assert.Less(sim.Arsenal.Attacks, Upgrade.AttackSlots, $"{c}: a new attack needs a free slot");
                if (c.Kind == CardKind.Mod) Assert.Less(sim.Arsenal.Mods, Upgrade.ModSlots, $"{c}: a new mod needs a free slot");
                if (c.Kind == CardKind.Dash) Assert.IsTrue(Content.Dashes.Kinds[c.Id].Ranks[0].Damaging, $"{c}: only damaging dashes");
            }
            Assert.AreEqual(canGrow, grows, "a card builds on something owned whenever one can");
        }

        [Test]
        public void LevelUpsOfferWhatTheBuildCanTakeAndPicksKeepTheSlots([Values(1UL, 2UL, 3UL, 4UL, 5UL, 6UL, 7UL, 8UL)] ulong seed)
        {
            var sim = Sim(tweak: EveryKillALevel, seed: seed, build: "swordSlash:1");
            var choose = new Random((int)seed);
            sim.StepTo(1);
            for (int n = 0; n < 30; n++)
            {
                Kill(sim, "skeleton");
                sim.StepTo(sim.Now + Tick);
                var offer = sim.State.Offer;
                Assert.IsTrue(offer.Up, $"offer {n}");
                AssertTakeable(sim, offer);
                // Taking new attacks and mods first fills the slots, so the later offers meet a full build.
                int slot = choose.Next(offer.Count);
                for (int i = 0; i < offer.Count; i++)
                    if (offer[i].New && offer[i].Kind != CardKind.Dash) slot = i;
                var card = offer[slot];
                int dashesBefore = sim.State.Hero.Dash;
                Press(sim, (Key)((int)Key.One + slot));
                Assert.AreEqual(card.Rank, Rank(sim, card), $"{card} taken");
                if (card.Kind == CardKind.Dash) Assert.AreEqual(card.Id, sim.State.Hero.Dash, "a new dash replaces the one held");
                else Assert.AreEqual(dashesBefore, sim.State.Hero.Dash);
                Assert.LessOrEqual(sim.Arsenal.Attacks, Upgrade.AttackSlots);
                Assert.LessOrEqual(sim.Arsenal.Mods, Upgrade.ModSlots);
            }
            Assert.AreEqual(Upgrade.ModSlots, sim.Arsenal.Mods, "the slots filled");
            Assert.AreEqual(Upgrade.AttackSlots, sim.Arsenal.Attacks);
        }

        [Test]
        public void AFullBuildAtItsTopIsOfferedOnlyOtherDashes()
        {
            var sim = Sim(tweak: EveryKillALevel, build: new[] { "swordSlash:3", "orbitBlades:3", "phantom:3", "wideArcs:2", "tempo:2", "keenEdge:2", "encore:2" });
            Arena.Equip(sim, "leapSlam", 3);
            sim.StepTo(1);
            Kill(sim, "skeleton");
            sim.StepTo(sim.Now + Tick);
            var offer = sim.State.Offer;
            Assert.AreEqual(3, offer.Count);
            for (int i = 0; i < 3; i++)
            {
                Assert.AreEqual(CardKind.Dash, offer[i].Kind);
                Assert.IsTrue(offer[i].New);
                Assert.AreNotEqual(Content.Dashes.IndexOf("leapSlam"), offer[i].Id);
                Assert.AreNotEqual(Content.Dashes.IndexOf("dash"), offer[i].Id);
            }
        }

        // ---- Treasure ----

        static Offer Treasure(Simulation sim)
        {
            sim.StepTo(sim.Now + 0.5);
            sim.Progression.Treasure(sim);
            Assert.AreEqual(OfferKind.Treasure, sim.State.Offer.Kind);
            return sim.State.Offer;
        }

        [Test]
        public void TreasureOffersTheEvolutionsOfRankThreeAttacksAndTheDash()
        {
            var sim = Sim(build: new[] { "swordSlash:3", "orbitBlades:3", "bladeRain:2", "wideArcs:1" });
            Arena.Equip(sim, "bladeRush", 3);
            var offer = Treasure(sim);
            CollectionAssert.AreEquivalent(
                new[] { Card.Of(AttackKind.SwordSlash, 4, 3), Card.Of(AttackKind.OrbitBlades, 4, 3), Card.Dash(Content.Dashes.IndexOf("bladeRush"), 4, 3) },
                new[] { offer[0], offer[1], offer[2] });
            Press(sim, Key.Three);
            Assert.AreEqual(4, Rank(sim, offer[2]), "the evolution taken");

            // Four qualify: three of them.
            var four = Sim(seed: 3, build: new[] { "swordSlash:3", "orbitBlades:3", "bladeRain:3" });
            Arena.Equip(four, "leapSlam", 3);
            offer = Treasure(four);
            for (int i = 0; i < 3; i++) Assert.IsTrue(offer[i].Evolution, offer[i].ToString());
        }

        [Test]
        public void TreasureThenRaisesTheHighestRankedToRankThree()
        {
            var sim = Sim(build: new[] { "swordSlash:3", "orbitBlades:2", "crescent:1", "keenEdge:1" });
            Arena.Equip(sim, "blinkStrike", 1);
            var offer = Treasure(sim);
            Assert.AreEqual(Card.Of(AttackKind.SwordSlash, 4, 3), offer[0], "the evolution first");
            Assert.AreEqual(Card.Of(AttackKind.OrbitBlades, 3, 2), offer[1], "then the highest-ranked below 3");
            Assert.AreEqual(3, offer[2].Rank, "then a rank 1 raised to 3");
            Assert.AreEqual(1, offer[2].From);
            Assert.That(offer[2].Kind, Is.EqualTo(CardKind.Attack).Or.EqualTo(CardKind.Dash));
        }

        [Test]
        public void TreasureFillsWithModRanksThenNewMods()
        {
            var sim = Sim(build: new[] { "swordSlash:1", "wideArcs:1", "tempo:2" });
            var offer = Treasure(sim);
            Assert.AreEqual(Card.Of(AttackKind.SwordSlash, 3, 1), offer[0], "the plain Dash has no ranks to raise");
            Assert.AreEqual(Card.Of(ModKind.WideArcs, 2, 1), offer[1]);
            Assert.AreEqual(CardKind.Mod, offer[2].Kind);
            Assert.IsTrue(offer[2].New);
            Assert.AreEqual(0, sim.Arsenal.Rank(offer[2].Mod));
        }

        // ---- The card moment ----

        [Test]
        public void TheWorldHoldsWhileCardsAreUpAndPlayResumesOnThePick()
        {
            var sim = Sim(new FixtureSong().Beats(0, 30, 128), build: "swordSlash:1", tweak: c => c.Dummies = new[] { new Dummy(0, 2, Arena.Tough) });
            sim.StepTo(4.8);
            Press(sim, Key.Right);
            sim.StepTo(5);
            int swings = Drive.Events(sim, SimEventKind.Swing).Count;
            Assert.Greater(swings, 0);
            sim.Progression.Treasure(sim);
            double clock = sim.State.RunClock, x = sim.State.Hero.X;
            int events = sim.EventCount;
            sim.StepTo(10);
            Assert.AreEqual(clock, sim.State.RunClock, "the run clock stops");
            Assert.AreEqual(x, sim.State.Hero.X, 1e-9, "the hero stands");
            Assert.IsFalse(sim.EventsSince(events).ToArray().Any(e => e.Kind == SimEventKind.Swing || e.Kind == SimEventKind.Strike), "nothing attacks");
            Assert.IsTrue(sim.EventsSince(events).ToArray().Any(e => e.Kind == SimEventKind.Beat), "the music goes on");
            double pick = sim.Now;
            Press(sim, Key.Two);
            var hold = Drive.Events(sim, SimEventKind.Hold).Last();
            Assert.AreEqual(0, hold.I1, "let go");
            Assert.AreEqual(pick, hold.T, 1e-9, "on the frame of the pick");
            sim.StepTo(13);
            Assert.AreEqual(clock + 3, sim.State.RunClock, 1e-6);
            Assert.Greater(Drive.Events(sim, SimEventKind.Swing).Count, swings, "the attacks go on");
        }

        [Test]
        public void KeysHeldWhenTheCardsOpenedAreIgnoredUntilReleased()
        {
            var sim = Sim();
            sim.StepTo(1);
            Press(sim, Key.Right);
            sim.StepTo(2);
            Assert.Greater(sim.State.Hero.X, 5);
            sim.Progression.Treasure(sim);
            Press(sim, Key.Up);
            Press(sim, Key.Left);
            Press(sim, Key.Enter);
            Assert.IsFalse(sim.State.Offer.Up, "Left focused, Enter picked");
            double x = sim.State.Hero.X, y = sim.State.Hero.Y;
            sim.StepTo(3);
            Assert.AreEqual(x, sim.State.Hero.X, 1e-9, "Right, held since before the cards, walks nothing");
            Assert.AreEqual(y, sim.State.Hero.Y, 1e-9, "nor Up, pressed while they were up");
            sim.Release(Key.Right, sim.Now - 0.02);
            Press(sim, Key.Right);
            sim.StepTo(4);
            Assert.Greater(sim.State.Hero.X, x + 5, "pressed again, it walks");
            Assert.AreEqual(y, sim.State.Hero.Y, 1e-9);
        }

        [Test]
        public void CardsArePickedByNumberOrByTheArrowsAndEnterOrSpace()
        {
            var sim = Sim(build: "swordSlash:1");
            sim.StepTo(1);
            sim.Progression.Treasure(sim);
            var offer = sim.State.Offer;
            Press(sim, Key.Space);
            Assert.IsTrue(sim.State.Offer.Up, "nothing is focused yet: a dash pressed as the cards open picks nothing");
            Assert.AreEqual(0, sim.State.Hero.Dashes);
            Press(sim, Key.Right);
            Assert.AreEqual(1, sim.State.Offer.Focus, "the first arrow focuses the middle card");
            Press(sim, Key.Right);
            Press(sim, Key.Right);
            Assert.AreEqual(2, sim.State.Offer.Focus, "and stops at the end");
            Press(sim, Key.Down);
            Assert.AreEqual(2, sim.State.Offer.Focus, "Up and Down move nothing");
            Press(sim, Key.Left);
            CollectionAssert.AreEqual(new[] { 1, 2, 1 }, Drive.Events(sim, SimEventKind.CardFocus).Select(e => e.I0));
            Press(sim, Key.Space);
            var pick = Drive.Events(sim, SimEventKind.Pick).Single();
            Assert.AreEqual(1, pick.I0);
            Assert.AreEqual(offer[1], Card.FromCode(pick.I1));
            Assert.AreEqual((int)OfferKind.Treasure, pick.I2);

            sim.StepTo(2);
            sim.Progression.Treasure(sim);
            offer = sim.State.Offer;
            Press(sim, Key.Three);
            Assert.AreEqual(offer[2], Card.FromCode(Drive.Events(sim, SimEventKind.Pick).Last().I1), "3 picks the third (a click on it, too)");
        }

        [Test]
        public void ThePausedCardsTakeNoPicks()
        {
            var sim = Sim();
            sim.StepTo(1);
            sim.Progression.Treasure(sim);
            sim.Control(RunControl.Pause, sim.Now);
            Press(sim, Key.One);
            Assert.IsTrue(sim.State.Offer.Up);
            sim.Control(RunControl.Resume, sim.Now);
            Assert.IsTrue(sim.State.Held, "the cards still hold the world");
            Press(sim, Key.One);
            Assert.IsFalse(sim.State.Held);
        }

        [Test]
        public void LevelUpAndTreasurePicksDoNotHeal()
        {
            var sim = Sim(tweak: EveryKillALevel);
            sim.StepTo(0.2);
            sim.Health.Hurt(sim, new HeroHit { T = sim.Now, Damage = 60, Owner = -1, Beat = double.NaN });
            sim.StepTo(0.5);
            Assert.AreEqual(40, sim.State.Hero.Health, 1e-9);
            Kill(sim, "skeleton");
            sim.StepTo(1);
            Press(sim, Key.One);
            Assert.AreEqual(40, sim.State.Hero.Health, 1e-9, "level-ups do not heal");
            Assert.IsEmpty(Drive.Events(sim, SimEventKind.HeroHeal));
            sim.Progression.Treasure(sim);
            Press(sim, Key.One);
            Assert.AreEqual(40, sim.State.Hero.Health, 1e-9, "the boss kill heals, not its treasure");
        }

        // ---- The run's start ----

        [Test]
        public void TheRunStartsOnTheFirstDownbeatAfterTheFirstPick()
        {
            var sim = Sim(new FixtureSong().Beats(0, 30, 128), start: RunStart.FirstOffer, build: "swordSlash:1");
            sim.StepTo(10);
            Assert.AreEqual(RunPhase.Waiting, sim.State.Phase);
            Assert.AreEqual(0, sim.State.RunClock);
            Assert.AreEqual(Reliability.Reliable, sim.State.Beat);
            double pick = sim.Now;
            Press(sim, Key.Two);
            Assert.AreEqual(RunPhase.Waiting, sim.State.Phase, "the pick waits for a downbeat");
            Assert.IsFalse(sim.State.HeldFor(HoldReason.Cards));
            sim.StepTo(13);
            var downbeat = Drive.Events(sim, SimEventKind.Beat).First(e => e.Downbeat && e.T > pick);
            Assert.AreEqual(downbeat.T, First(sim, SimEventKind.RunStart), 1e-9);
            Assert.AreEqual(downbeat.T, sim.State.StartedAt, 1e-9);
            Assert.Greater(sim.State.RunClock, 0);
            Assert.AreEqual(2, sim.Arsenal.Attacks, "the first pick's attack");
        }

        [Test]
        public void WithNoBeatTheRunStartsOnTheFirstPick()
        {
            var sim = Sim(start: RunStart.FirstOffer);
            sim.StepTo(1.5);
            Press(sim, Key.One);
            Assert.AreEqual(RunPhase.Playing, sim.State.Phase);
            Assert.AreEqual(sim.Now, sim.State.StartedAt, 1e-9);
            Assert.IsFalse(sim.State.Held);
            Assert.AreEqual(1, Drive.Events(sim, SimEventKind.RunStart).Count);
        }

        // ---- Sessions ----

        /// <summary>
        /// Level 1 from the first offer, as the client drives it, through 128 BPM with a gap between songs: the player
        /// picks the first offer by its number, walks a ring dashing, and picks each later offer with the arrows and
        /// Enter as it opens.
        /// </summary>
        public static (RunInputs Run, Simulation Sim, List<StreamEntry> Fed) PicksRun()
        {
            var entries = new FixtureSong(21).Beats(0, 120, 128, jitter: 0.003).Gap(120, 124, silent: true).Beats(124, 200, 128).Entries();
            var run = new RunInputs { Seed = 21, Start = RunStart.FirstOffer, Level = true };
            var sim = new Simulation(run.Build(Content));
            double angle = 0, nextDash = 3;
            int fed = Drive.Feed(sim, entries, 200, eachFrame: s =>
            {
                if (s.State.Phase == RunPhase.Ended) return;
                var offer = s.State.Offer;
                if (offer.Kind == OfferKind.First) s.Press(Key.Two, s.Now - 0.05);
                else if (offer.Up) s.Press(offer.Focus < 0 ? Key.Right : Key.Enter, s.Now - 0.04);
                if (s.State.Held) return;
                angle += 6 * (1 / 60.0) / 12;
                s.Target(12 * Math.Cos(angle), 12 * Math.Sin(angle), s.Now - 0.05);
                if (s.Now < nextDash) return;
                s.Press(Key.Space, s.Now - 0.04);
                s.Release(Key.Space, s.Now - 0.03);
                nextDash = s.Now + 1.7;
            });
            return (run, sim, entries.GetRange(0, fed));
        }

        [Test]
        public void AScriptedSessionWithPicksReplaysToItsHash()
        {
            var (run, live, fed) = PicksRun();
            var picks = Drive.Events(live, SimEventKind.Pick);
            Assert.AreEqual((int)OfferKind.First, picks[0].I2, "the first offer");
            Assert.IsTrue(picks.Any(e => e.I2 == (int)OfferKind.LevelUp), "and a level-up's");
            Assert.Greater(live.Arsenal.Kills, 100);
            string dir = Path.Combine(Path.GetTempPath(), "champions-picks-" + Guid.NewGuid().ToString("N"));
            try
            {
                var record = SessionRecord.Of(run, live);
                record.Write(dir);
                SessionRecord.WriteEntries(dir, fed);
                var read = SessionRecord.Read(dir);
                Assert.AreEqual(RunStart.FirstOffer, read.Run.Start);
                var replay = new SessionReplay(read);
                foreach (var e in SessionRecord.LoadEntries(dir)) replay.Supply(e);
                var sim = new Simulation(read.Run.Build(Content));
                Assert.IsTrue(replay.StepTo(sim, double.PositiveInfinity));
                Assert.IsTrue(replay.Matches(sim), $"replayed {sim.Hash:X16} ({sim.EventCount} events), recorded {record.Hash:X16} ({record.Events})");
                Assert.AreEqual(live.State.Level, sim.State.Level);
                Assert.AreEqual(live.Progression.Picks, sim.Progression.Picks);
            }
            finally
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
            }
        }
    }
}
