using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using PS.Game.Sim.Content;

namespace PS.Game.Sim.Tests
{
    /// <summary>
    /// The sword (unity-game.md, The Sword and "Attacks and Steps Land on the Beat"): impacts on
    /// beats and their subdivisions, wind-ups emitted ahead of them, heavy strikes on accents, the
    /// aim, damage against native's rank table, deaths and drops, the cleave wave, the slam, the
    /// pulse with no beat, and the hero's health.
    /// </summary>
    public class SwordTests
    {
        const double Windup = 0.32;
        /// <summary>Accelerando 100 to 140 BPM, the first 30 s: strong onsets, accent forecasts and structure changes.</summary>
        const string Accelerando = "accelerando-100-140-30s.jsonl";

        static GameContent Content => HordeFixture.Content;

        /// <summary>Beats and ticks only (no onsets, forecasts or changes), at their normal known-at times.</summary>
        static List<KnownEntry> Plain(double bpm, double until, double evidence = 0.9, double from = 0) =>
            Drive.Known(new FixtureSong(3).Beats(from, until, bpm, evidence: evidence).Entries()
                .Where(e => e.Kind == EntryKind.Beat || e.Kind == EntryKind.Tick));

        static Simulation Start(SimConfig config, IEnumerable<KnownEntry> known)
        {
            var sim = new Simulation(config);
            foreach (var k in known) sim.AddKnown(k.Entry, k.KnownAt);
            return sim;
        }

        static SimConfig Config(int rank = 1, Stats? stats = null, bool heroStands = true)
        {
            var config = HordeFixture.Config(minimum: 0, roster: new string[0], heroStands: heroStands);
            config.Sword = new SwordConfig { Content = Content, Rank = rank, Stats = stats };
            return config;
        }

        static List<AttackEvent> HeroAttacks(Simulation sim) =>
            Drive.Events(sim, SimEventKind.Attack).Select(e => e.AsAttack).Where(a => a.Attacker == -1).ToList();

        static List<HitEvent> EnemyHits(Simulation sim) =>
            Drive.Events(sim, SimEventKind.Hit).Select(e => e.AsHit).Where(h => h.Target >= 0).ToList();

        static double[] BeatTimes(Simulation sim) => sim.Stream.Beats.ToArray().Select(b => b.T).ToArray();

        static int Place(Simulation sim, string role, double dx, double dy) =>
            HordeFixture.Place(sim, role, sim.State.Hero.X + dx, sim.State.Hero.Y + dy);

        static double Reach(int rank, bool heavy = false) =>
            Content.Weapon("sword").Shape.RadiusM * Content.Weapon("sword").Rank(rank).Area * (heavy ? 1.3 : 1);

        // ---------------------------------------------------------------- timing

        [TestCase(EntryDump.House128, 1)]
        [TestCase(EntryDump.House128, 2)]
        [TestCase(Accelerando, 1)]
        [TestCase(Accelerando, 2)]
        public void ImpactsLandExactlyOnBeatsAndSubdivisionsOfARealDump(string dump, int rank)
        {
            var entries = EntryDump.Load(dump, out double duration);
            var config = HordeFixture.Config(heroStands: true, sword: rank);
            var sim = new Simulation(config);
            Drive.Feed(sim, entries, duration);
            var beats = BeatTimes(sim);
            var attacks = HeroAttacks(sim);
            int n = rank == 1 ? 1 : 2;
            Assert.AreEqual(0, sim.Sword.Missed, "every beat is known before its time");
            Assert.Greater(attacks.Count, n * (beats.Length - 2));

            var mains = attacks.Where(a => a.Kind != (int)HeroAttackKind.FollowUp).Select(a => a.Impact).ToList();
            // Every beat but those still to come at the end has its one main strike, exactly on it.
            var struck = beats.Where(t => t - Windup <= sim.Now + 1e-9).ToList();
            CollectionAssert.AreEqual(struck, mains, "one main strike on each committed beat, at its time");
            double previous = double.NegativeInfinity;
            foreach (var a in attacks)
            {
                Assert.AreEqual(a.T, a.WindupStart);
                Assert.Less(a.WindupStart, a.Impact, "the wind-up starts before its impact");
                Assert.LessOrEqual(a.Impact - a.WindupStart, Windup + 1e-9);
                Assert.GreaterOrEqual(a.WindupStart, previous - 1e-9, "a wind-up never starts before the previous impact");
                previous = a.Impact;
                if (a.Kind != (int)HeroAttackKind.FollowUp) continue;
                // A follow-up is the beat's half: halfway to the next beat, or by the beat's period when the next wasn't known yet.
                int b = Array.FindLastIndex(beats, t => t < a.Impact);
                Assert.GreaterOrEqual(b, 0);
                double half = a.Impact - beats[b];
                bool toNext = b + 1 < beats.Length && Math.Abs(half - 0.5 * (beats[b + 1] - beats[b])) < 1e-9;
                bool byPeriod = Math.Abs(half - 0.5 * sim.Beats.PeriodOf(b)) < 1e-9;
                Assert.IsTrue(toNext || byPeriod, $"a follow-up {half:F4} s after beat {b}");
            }
            int followUps = attacks.Count(a => a.Kind == (int)HeroAttackKind.FollowUp);
            if (n == 1) Assert.AreEqual(0, followUps);
            else Assert.That(followUps, Is.InRange(mains.Count - 1, mains.Count), "a follow-up for each beat at Amount 2");

            // The sword's hits land at its impacts or its cleave bands, and every attack is in the log before its hits.
            var impacts = new HashSet<double>(attacks.Select(a => a.Impact));
            var bands = new HashSet<double>();
            foreach (var c in Drive.Events(sim, SimEventKind.Cleave).Select(e => e.AsCleave))
                for (int k = 0; k < c.Bands; k++) bands.Add(c.FirstBand + k * c.BandSeconds);
            var log = sim.EventsSince(0).ToArray();
            var announced = new HashSet<double>();
            foreach (var e in log)
            {
                if (e.Kind == SimEventKind.Attack && e.AsAttack.Attacker == -1) announced.Add(e.AsAttack.Impact);
                if (e.Kind != SimEventKind.Hit || e.AsHit.Target < 0) continue;
                if ((WeaponHit)e.AsHit.Source == WeaponHit.Cleave) Assert.IsTrue(bands.Contains(e.T), $"a cleave hit at {e.T} on no band");
                else Assert.IsTrue(announced.Contains(e.T) && impacts.Contains(e.T), $"a strike hit at {e.T} announced by no attack");
            }
            Assert.Greater(EnemyHits(sim).Count, 5);
        }

        [TestCase(190, 0)]
        [TestCase(240, 2)]
        public void AttacksAreAnnouncedAheadOfTheirImpactsAtFastTempos(double bpm, double amount)
        {
            var stats = Content.Progression.Hero.Stats;
            stats.Amount = amount;
            var sim = Start(Config(rank: 2, stats: stats), Plain(bpm, 20));
            sim.StepTo(20);
            var beats = BeatTimes(sim);
            var attacks = HeroAttacks(sim);
            int n = 2 + (int)amount;
            Assert.AreEqual(n, sim.Sword.StrikesPerBeat);
            Assert.Greater(attacks.Count, n * (beats.Length - 2));
            double previous = double.NegativeInfinity;
            foreach (var a in attacks)
            {
                Assert.Less(a.WindupStart, a.Impact);
                Assert.GreaterOrEqual(a.WindupStart, previous - 1e-9, "a strike closer than a wind-up follows on from the previous impact");
                previous = a.Impact;
                int b = Array.FindLastIndex(beats, t => t <= a.Impact + 1e-9);
                double gap = b + 1 < beats.Length ? beats[b + 1] - beats[b] : sim.Beats.PeriodOf(b);
                double k = (a.Impact - beats[b]) / (gap / n);
                Assert.AreEqual(Math.Round(k), k, 1e-6, "on an even subdivision of the beat");
                Assert.AreEqual(k < 0.5, a.Kind != (int)HeroAttackKind.FollowUp);
            }
        }

        [Test]
        public void ABeatRevealedLateWindsUpAtOnceAndOneRevealedAfterItsTimeIsMissed()
        {
            var known = Plain(120, 12);
            var beats = known.Where(k => k.Entry.Kind == EntryKind.Beat).Select(k => k.Entry.T).OrderBy(t => t).ToList();
            double late = beats[12], missed = beats[16];
            for (int i = 0; i < known.Count; i++)
            {
                var k = known[i];
                if (k.Entry.Kind != EntryKind.Beat) continue;
                // Later beats wait behind the late one: beats are revealed in order.
                if (k.Entry.T >= late && k.Entry.T < missed) k.KnownAt = Math.Max(k.KnownAt, late - 0.05);
                if (k.Entry.T >= missed) k.KnownAt = Math.Max(k.KnownAt, missed + 0.01);
                known[i] = k;
            }
            var plain = Start(Config(), Plain(120, 12));
            plain.StepTo(12);
            var sim = Start(Config(), known);
            sim.StepTo(12);
            var attacks = HeroAttacks(sim);
            var a = attacks.Single(x => x.Impact == late);
            Assert.AreEqual(late - 0.05, a.WindupStart, 1e-9, "a late beat winds up as soon as it is known");
            Assert.IsFalse(attacks.Any(x => x.Impact == missed));
            Assert.AreEqual(plain.Sword.Missed + 1, sim.Sword.Missed, "the beat at the sim's start, and the late one");
        }

        // ---------------------------------------------------------------- heavy strikes

        [Test]
        public void AStrongOnsetWithin50MsOrAnAccentForecastMakesTheBeatHeavy()
        {
            var known = Plain(120, 16);
            var beats = known.Where(k => k.Entry.Kind == EntryKind.Beat).Select(k => k.Entry.T).OrderBy(t => t).ToList();
            void Add(StreamEntry e, double knownAt) => known.Add(new KnownEntry(e, knownAt));
            double Early(double t) => t - 2;
            // Beat 8: a strong onset 49 ms after it. Beat 10: 52 ms after. Beat 12: a weak onset on it.
            Add(StreamEntry.Onset(beats[8] + 0.049, 0, 300, true), Early(beats[8]));
            Add(StreamEntry.Onset(beats[10] + 0.052, 0, 300, true), Early(beats[10]));
            Add(StreamEntry.Onset(beats[12], 0, 300, false), Early(beats[12]));
            // Beat 14: a forecast 30 ms early. Beat 16: a forecast and its strong onset.
            Add(StreamEntry.Forecast(beats[14] - 0.03, 0, 4, 0.75), Early(beats[14]));
            Add(StreamEntry.Forecast(beats[16] + 0.01, 0, 4, 0.75), Early(beats[16]));
            Add(StreamEntry.Onset(beats[16] - 0.02, 0, 300, true), Early(beats[16]));
            // Beat 18: a strong onset known only after its wind-up started. Beat 19's subdivision: a strong onset on it.
            Add(StreamEntry.Onset(beats[18], 0, 300, true), beats[18] - 0.1);
            Add(StreamEntry.Onset(beats[19] + 0.25, 0, 300, true), Early(beats[19]));
            var sim = Start(Config(rank: 2), known);
            sim.StepTo(16);

            var attacks = HeroAttacks(sim);
            var heavy = attacks.Where(a => a.Heavy).Select(a => a.Impact).ToList();
            CollectionAssert.AreEqual(new[] { beats[8], beats[14], beats[16] }, heavy);
            foreach (var a in attacks.Where(a => a.Heavy))
            {
                Assert.AreEqual((int)HeroAttackKind.Spin, a.Kind);
                Assert.AreEqual(Reach(2, heavy: true), a.Reach, 1e-9);
            }
            Assert.AreEqual(Reach(2), attacks.First(a => !a.Heavy).Reach, 1e-9);
            // A forecast and its onset: one heavy strike, one cleave wave.
            Assert.AreEqual(1, attacks.Count(a => a.Impact == beats[16]));
            var cleaves = Drive.Events(sim, SimEventKind.Cleave);
            CollectionAssert.AreEqual(heavy, cleaves.Select(e => e.T));
        }

        [Test]
        public void HeavyStrikesFallOnARealDumpsAccents()
        {
            var entries = EntryDump.Load(Accelerando, out double duration);
            var sim = new Simulation(Config());
            Drive.Feed(sim, entries, duration);
            var attacks = HeroAttacks(sim);
            var log = sim.Stream;
            bool Accent(AttackEvent a)
            {
                if (!sim.Beats.ClearAt(a.WindupStart)) return false;
                var onsets = log.Revealed(EntryKind.Onset);
                for (int i = 0; i < onsets.Length; i++)
                    if (new OnsetEntry(onsets[i]).Strong && Math.Abs(onsets[i].T - a.Impact) <= 0.05 + 1e-9
                        && log.KnownAt(EntryKind.Onset, i) <= a.WindupStart + 1e-9) return true;
                var forecasts = log.Revealed(EntryKind.Forecast);
                for (int i = 0; i < forecasts.Length; i++)
                    if (Math.Abs(forecasts[i].T - a.Impact) <= 0.05 + 1e-9 && log.KnownAt(EntryKind.Forecast, i) <= a.WindupStart + 1e-9) return true;
                return false;
            }
            var expected = attacks.Where(Accent).Select(a => a.Impact).ToList();
            var heavy = attacks.Where(a => a.Heavy).Select(a => a.Impact).ToList();
            CollectionAssert.AreEqual(expected, heavy);
            Assert.GreaterOrEqual(heavy.Count, 7, "the dump's forecasts and strong onsets on beats");
            int fromForecasts = heavy.Count(t => log.Revealed(EntryKind.Forecast).ToArray().Any(f => Math.Abs(f.T - t) <= 0.05));
            Assert.GreaterOrEqual(fromForecasts, 5, "the accent forecasts make heavy strikes");
        }

        // ---------------------------------------------------------------- aim

        /// <summary>A sim at 120 BPM with beats below the gate (enemies hold, the sword still strikes), stepped to just after beat 8's impact.</summary>
        static Simulation AimSim(out double nextBeat)
        {
            var known = Plain(120, 12, evidence: 0.1);
            var sim = Start(Config(), known);
            var beats = known.Where(k => k.Entry.Kind == EntryKind.Beat).Select(k => k.Entry.T).OrderBy(t => t).ToList();
            sim.StepTo(beats[8] + 0.01);
            nextBeat = beats[9];
            return sim;
        }

        static AttackEvent SwingAfterPlacing(Simulation sim, double nextBeat)
        {
            int cursor = sim.EventCount;
            sim.StepTo(nextBeat - Windup + 0.005);
            return sim.EventsSince(cursor).ToArray().Select(e => e.Kind == SimEventKind.Attack ? e.AsAttack : default)
                .Single(a => a.Impact == nextBeat && a.Attacker == -1);
        }

        static double AngleTo(AttackEvent a, double dx, double dy) =>
            Math.Abs(Math.Atan2(a.DirX * dy - a.DirY * dx, a.DirX * dx + a.DirY * dy));

        [Test]
        public void TheSwingTurnsTowardTheCrowd()
        {
            var sim = AimSim(out double next);
            for (int k = 0; k < 4; k++) Place(sim, "skeleton", 5, -0.6 + 0.4 * k);
            Place(sim, "skeleton", -2, 0);
            var a = SwingAfterPlacing(sim, next);
            Assert.LessOrEqual(AngleTo(a, 1, 0), Sword.AimHalfAngle + 0.1, "the crowd east is in the swing's arc");
            Assert.Greater(AngleTo(a, -1, 0), Sword.AimHalfAngle, "the lone skeleton west is not");
        }

        [Test]
        public void ABossOutweighsACrowd()
        {
            var sim = AimSim(out double next);
            for (int k = 0; k < 4; k++) Place(sim, "skeleton", 5, -0.6 + 0.4 * k);
            Place(sim, "boss-werewolf", -4, 0);
            var a = SwingAfterPlacing(sim, next);
            Assert.LessOrEqual(AngleTo(a, -1, 0), Sword.AimHalfAngle + 1e-9);
        }

        [Test]
        public void EqualTargetsGoToTheNearest()
        {
            var sim = AimSim(out double next);
            Place(sim, "skeleton", 0, 3);
            Place(sim, "skeleton", 0, -5);
            var a = SwingAfterPlacing(sim, next);
            Assert.AreEqual(0, a.DirX, 1e-9);
            Assert.AreEqual(1, a.DirY, 1e-9);
        }

        [Test]
        public void WithNobodyInReachTheHeroKeepsFacing()
        {
            var sim = AimSim(out double next);
            Place(sim, "skeleton", 15, 0);
            var a = SwingAfterPlacing(sim, next);
            Assert.AreEqual(Direction.Down, sim.State.Hero.Facing);
            Assert.AreEqual(0, a.DirX, 1e-9);
            Assert.AreEqual(-1, a.DirY, 1e-9);
        }

        // ---------------------------------------------------------------- damage, deaths and drops

        [Test]
        public void DamageFollowsNativesRankTableTimesMight()
        {
            // Native's weapons.yaml for the sword, resolved rank by rank.
            double[] power = { 1, 1, 1.5, 2, 2.5, 3, 3.5, 4 };
            int[] strikes = { 1, 2, 2, 2, 2, 2, 2, 2 };
            double[] area = { 1, 1, 1, 1.1, 1.1, 1.2, 1.2, 1.2 };
            var stats = Content.Progression.Hero.Stats;
            stats.Might = 1.5;
            stats.Area = 1.25;
            for (int rank = 1; rank <= 8; rank++)
            {
                var sim = AimSimAt(rank, stats, out double next);
                Place(sim, "minotaur", 3, 0);
                Assert.AreEqual(strikes[rank - 1], sim.Sword.StrikesPerBeat, $"rank {rank}");
                int cursor = sim.EventCount;
                sim.StepTo(next + 0.01);
                var hit = sim.EventsSince(cursor).ToArray().Where(e => e.Kind == SimEventKind.Hit).Select(e => e.AsHit).First();
                Assert.AreEqual(power[rank - 1] * 1.5, hit.Damage, 1e-12, $"rank {rank}");
                Assert.AreEqual(WeaponHit.Strike, (WeaponHit)hit.Source);
                var a = HeroAttacks(sim).Last(x => x.Impact == next);
                Assert.AreEqual(190.0 / 27 * area[rank - 1] * 1.25, a.Reach, 0.001, $"rank {rank}");
            }
        }

        static Simulation AimSimAt(int rank, Stats stats, out double nextBeat)
        {
            var known = Plain(120, 12, evidence: 0.1);
            var sim = Start(Config(rank, stats), known);
            var beats = known.Where(k => k.Entry.Kind == EntryKind.Beat).Select(k => k.Entry.T).OrderBy(t => t).ToList();
            sim.StepTo(beats[8] + 0.01);
            nextBeat = beats[9];
            return sim;
        }

        [Test]
        public void TheCircleCatchesBodiesThatReachIt()
        {
            var sim = AimSim(out double next);
            double r = Content.Role("minotaur").RadiusM;
            int inside = Place(sim, "minotaur", 0, Reach(1) + 0.5 * r - 0.02);
            int outside = Place(sim, "minotaur", 0, -(Reach(1) + 0.5 * r + 0.02));
            sim.StepTo(next + 0.01);
            var hits = EnemyHits(sim).Select(h => h.Target).ToList();
            CollectionAssert.Contains(hits, inside);
            CollectionAssert.DoesNotContain(hits, outside);
        }

        [Test]
        public void ALethalHitKillsLaunchesAndDropsAGem()
        {
            var sim = AimSim(out double next);
            int bat = Place(sim, "bat1", 3, 0);
            int skeleton = Place(sim, "skeleton", -3, 0);
            sim.StepTo(next + 0.01);
            var death = Drive.Events(sim, SimEventKind.Death).Single().AsDeath;
            Assert.AreEqual(bat, death.Enemy);
            Assert.AreEqual(next, death.T);
            Assert.AreEqual(Content.RoleIndex("bat1"), death.Role);
            Assert.AreEqual(1, death.DirX, 1e-9, "launched away from the hero");
            Assert.AreEqual(10.0 / 27 * 5, death.Force, 1e-9, "native's knockback distance times the sword's");
            var drop = Drive.Events(sim, SimEventKind.Drop).Single().AsDrop;
            Assert.AreEqual(ItemKind.Gem, drop.ItemKind);
            Assert.AreEqual(Content.Role("bat1").Xp, drop.Value);
            Assert.AreEqual(death.X, drop.X);
            Assert.IsFalse(sim.State.Enemies[bat].Alive);
            Assert.AreEqual(1, sim.State.EnemiesAlive);
            // The skeleton (1.5 health) survives one strike, knocked away from the hero, and falls to the next.
            Assert.AreEqual(0.5, sim.State.Enemies[skeleton].Health, 1e-9);
            var knock = Drive.Events(sim, SimEventKind.EnemyKnock).Single().AsEnemyKnock;
            Assert.AreEqual(skeleton, knock.Enemy);
            Assert.Less(knock.ToX, knock.FromX);
            sim.StepTo(next + 0.51);
            Assert.AreEqual(2, Drive.Events(sim, SimEventKind.Death).Count);
            Assert.AreEqual(0, sim.State.EnemiesAlive);
            // The free slot takes the next spawn.
            Assert.Contains(Place(sim, "zombie", 4, 0), new[] { bat, skeleton });
        }

        [Test]
        public void AHeavyStrikeSendsOutACleaveWave()
        {
            var known = Plain(120, 12);
            var beats = known.Where(k => k.Entry.Kind == EntryKind.Beat).Select(k => k.Entry.T).OrderBy(t => t).ToList();
            known.Add(new KnownEntry(StreamEntry.Onset(beats[9], 0, 300, true), beats[9] - 2));
            var sim = Start(Config(), known);
            sim.StepTo(beats[8] + 0.01);
            double heavyReach = Reach(1, heavy: true);
            int near = Place(sim, "werewolf", 8, 0);
            int ringed = Place(sim, "werewolf", -12, 0);
            int far = Place(sim, "werewolf", 0, 20);
            sim.StepTo(beats[9] + 1.2);

            var cleave = Drive.Events(sim, SimEventKind.Cleave).Single().AsCleave;
            double p = beats[10] - beats[9];
            Assert.AreEqual(beats[9], cleave.T);
            Assert.AreEqual(heavyReach, cleave.Inner, 1e-9);
            Assert.AreEqual(heavyReach + 160.0 / 27, cleave.Outer, 1e-3);
            Assert.AreEqual(7, cleave.Bands);
            Assert.AreEqual(beats[9] + p / 2, cleave.FirstBand, 1e-9, "the first band on the next eighth");
            Assert.AreEqual(p / 4, cleave.BandSeconds, 1e-9, "then a band each sixteenth");

            var hits = EnemyHits(sim);
            var nearHits = hits.Where(h => h.Target == near && h.T <= beats[9] + 1e-9).ToList();
            Assert.AreEqual(WeaponHit.HeavyStrike, (WeaponHit)nearHits.Last().Source);
            Assert.AreEqual(2.5, nearHits.Last().Damage, 1e-12);
            Assert.IsFalse(hits.Any(h => h.Target == near && (WeaponHit)h.Source == WeaponHit.Cleave), "the strike's targets are the wave's already");
            var wave = hits.Where(h => (WeaponHit)h.Source == WeaponHit.Cleave).ToList();
            var ringHit = wave.Single(h => h.Target == ringed);
            Assert.AreEqual(1, ringHit.Damage, 1e-12, "the wave deals the strike's power");
            int band = (int)Math.Round((ringHit.T - cleave.FirstBand) / cleave.BandSeconds);
            Assert.AreEqual(cleave.FirstBand + band * cleave.BandSeconds, ringHit.T, 1e-9);
            double d = Math.Sqrt(Math.Pow(ringHit.X - cleave.X, 2) + Math.Pow(ringHit.Y - cleave.Y, 2));
            double outer = cleave.Inner + (band + 1) * 25.0 / 27;
            Assert.LessOrEqual(d - 0.5 * Content.Role("werewolf").RadiusM, outer + 1e-9, "inside the band that hit it");
            Assert.IsFalse(hits.Any(h => h.Target == far));
        }

        // ---------------------------------------------------------------- slams and the pulse

        [Test]
        public void TheFirstDownbeatAfterASectionChangeSlams()
        {
            // The downbeats of the plain song, to place a change revealed just inside a downbeat's wind-up.
            var plain = Start(Config(), Plain(120, 40));
            plain.StepTo(40);
            var downbeats = Drive.Events(plain, SimEventKind.Beat).Select(e => e.AsBeat).Where(b => b.Downbeat).Select(b => b.T).ToList();
            double late = downbeats.First(t => t > 28);

            var known = Plain(120, 40);
            // A loud jump known ahead; a change 5 s after it, too soon for a zone change; and a change
            // known 0.1 s before a downbeat, after that downbeat's strike wound up.
            known.Add(new KnownEntry(StreamEntry.Jump(10.1, 0, 7), 9));
            known.Add(new KnownEntry(StreamEntry.Change(15.1, 0, 0.8), 14));
            known.Add(new KnownEntry(StreamEntry.Change(25.3, 0, 0.8), late - 0.1));
            var sim = Start(Config(), known);
            sim.StepTo(40);
            var zones = Drive.Events(sim, SimEventKind.ZoneChange).Select(e => e.AsZoneChange).ToList();
            var slams = HeroAttacks(sim).Where(a => a.Kind == (int)HeroAttackKind.Slam).Select(a => a.Impact).ToList();
            CollectionAssert.AreEqual(new[] { ZoneCause.Jump, ZoneCause.Change }, zones.Select(z => z.Cause));
            Assert.AreEqual(late, zones[1].T, "the late change still lands on its downbeat");
            CollectionAssert.AreEqual(new[] { zones[0].T, downbeats.First(t => t > late) }, slams,
                "the slam shares the zone change's downbeat, or takes the next when the change came after its wind-up");
        }

        [Test]
        public void WithNoBeatAheadTheSwordStrikesOnItsInterval()
        {
            var known = Drive.Known(new FixtureSong(3).Beats(0, 10, 120).Gap(10, 20).Beats(20, 30, 120).Entries()
                .Where(e => e.Kind == EntryKind.Beat || e.Kind == EntryKind.Tick));
            var sim = Start(Config(), known);
            sim.StepTo(30);
            var beats = BeatTimes(sim);
            double lastBeat = beats.Last(t => t < 10), resumed = beats.First(t => t > 15);
            var pulses = HeroAttacks(sim).Select(a => a.Impact).Where(t => t > lastBeat + 1e-9 && t < resumed - 1e-9).ToList();
            Assert.Greater(pulses.Count, 5);
            for (int k = 0; k < pulses.Count; k++)
                Assert.AreEqual(lastBeat + 1.35 * (k + 1), pulses[k], 1e-9, "the sword's raw interval from the last beat's strike");
            Assert.Greater(resumed - pulses.Last(), 0.5 * 1.35, "a beat close after takes over from the pulse");
            CollectionAssert.IsSubsetOf(beats.Where(t => t > 15 && t < 30 - Windup), HeroAttacks(sim).Select(a => a.Impact));
        }

        // ---------------------------------------------------------------- the hero's health

        [Test]
        public void ContactLungesTakeHealthThenTheHeroFalls()
        {
            var sim = HordeFixture.Empty();
            var known = Plain(120, 20);
            foreach (var k in known) sim.AddKnown(k.Entry, k.KnownAt);
            sim.StepTo(4);
            Place(sim, "skeleton", 1.5, 0);
            Place(sim, "skeleton", -1.5, 0);
            sim.StepTo(20);
            var hits = Drive.Events(sim, SimEventKind.Hit).Select(e => e.AsHit).Where(h => h.Target == -1).ToList();
            // Both bite on most beats; the lockout lets one hurt (power 10 less armor 1).
            Assert.Greater(hits.GroupBy(h => h.T).Count(g => g.Count() == 2), 5);
            foreach (var beat in hits.GroupBy(h => h.T))
            {
                Assert.AreEqual(1, beat.Count(h => h.Damage == 9));
                Assert.IsTrue(beat.All(h => h.Damage == 9 || h.Damage == 0));
            }
            Assert.AreEqual(14, hits.Count(h => h.Damage > 0), "120 health at 9 a bite");
            var fell = Drive.Events(sim, SimEventKind.Death).Single().AsDeath;
            Assert.AreEqual(-1, fell.Enemy);
            Assert.AreEqual(hits.Last(h => h.Damage > 0).T, fell.T);
            Assert.IsTrue(sim.State.Hero.Fallen);
            Assert.AreEqual(0, sim.State.Hero.Health);
            Assert.IsFalse(hits.Any(h => h.T > fell.T + 1e-9), "a fallen hero takes no more bites");
            // A fallen hero no longer moves.
            int steps = Drive.Events(sim, SimEventKind.HeroStep).Count;
            sim.Press(Key.Right, sim.Now);
            sim.StepTo(21);
            Assert.AreEqual(steps, Drive.Events(sim, SimEventKind.HeroStep).Count);
        }

        [Test]
        public void EasyHalvesContactDamageAndAStandingHeroNeverFalls()
        {
            var config = HordeFixture.Config(minimum: 0, roster: new string[0]);
            config.Easy = true;
            config.HeroStands = true;
            var sim = new Simulation(config);
            foreach (var k in Plain(120, 40)) sim.AddKnown(k.Entry, k.KnownAt);
            sim.StepTo(4);
            Place(sim, "skeleton", 1.5, 0);
            sim.StepTo(40);
            var hits = Drive.Events(sim, SimEventKind.Hit).Select(e => e.AsHit).Where(h => h.Target == -1).ToList();
            Assert.Greater(hits.Count, 60);
            Assert.IsTrue(hits.All(h => h.Damage == 4), "power 10 at half, less armor 1");
            Assert.IsFalse(sim.State.Hero.Fallen);
            Assert.AreEqual(0.1 * sim.State.Hero.MaxHealth, sim.State.Hero.Health, 1e-9);
            Assert.AreEqual(120, sim.State.Hero.MaxHealth);
        }

        [Test]
        public void AFallenHeroStrikesNoMore()
        {
            var known = Plain(120, 20);
            var sim = Start(Config(heroStands: false), known);
            sim.StepTo(4);
            for (int k = 0; k < 8; k++) Place(sim, "minotaur", 1.2 * Math.Cos(k), 1.2 * Math.Sin(k));
            sim.StepTo(20);
            var fell = Drive.Events(sim, SimEventKind.Death).Single(e => e.AsDeath.Enemy == -1);
            Assert.IsFalse(HeroAttacks(sim).Any(a => a.WindupStart > fell.T + 1e-9));
            Assert.IsFalse(EnemyHits(sim).Any(h => h.T > fell.T + 1e-9));
        }

        // ---------------------------------------------------------------- determinism

        [Test]
        public void TheSwordRunsTheSameWhateverTheFramePacing()
        {
            var entries = EntryDump.Load(Accelerando, out double duration);
            var a = new Simulation(HordeFixture.Config(heroStands: true, sword: 2));
            Drive.Feed(a, entries, duration, frame: 1.0 / 60);
            var b = new Simulation(HordeFixture.Config(heroStands: true, sword: 2));
            Drive.Feed(b, entries, duration, frame: 1.0 / 23.7);
            var replay = new Simulation(HordeFixture.Config(heroStands: true, sword: 2));
            foreach (var k in Drive.Known(entries)) replay.AddKnown(k.Entry, k.KnownAt);
            replay.StepTo(duration);
            Assert.Greater(Drive.Events(a, SimEventKind.Death).Count, 50);
            Assert.AreEqual(a.Hash, b.Hash);
            Assert.AreEqual(a.Hash, replay.Hash);
        }

#if !UNITY_5_3_OR_NEWER
        [Test]
        public void StrikesAllocateNothing()
        {
            Simulation sim = null;
            long allocated = Drive.LeastAllocated(() =>
            {
                sim = new Simulation(HordeFixture.Config(heroStands: true, sword: 2));
                foreach (var k in Drive.Known(new FixtureSong(4).Beats(0, 60, 128).Entries())) sim.AddKnown(k.Entry, k.KnownAt);
                sim.StepTo(5); // warm up (JIT)
                return () => sim.StepTo(60);
            });
            Assert.Greater(Drive.Events(sim, SimEventKind.Death).Count, 100);
            Assert.AreEqual(0, allocated, "bytes allocated while stepping 55 s");
        }
#endif
    }
}
