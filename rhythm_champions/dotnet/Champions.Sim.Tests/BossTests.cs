using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace Champions.Sim.Tests
{
    /// <summary>
    /// The bosses (design.md, "Bosses", Decisions 2 and 3): arrival on the first strong downbeat after the wait (beatless
    /// 4 s after spawning stops, on any downbeat after the window), the entrance's shatter, telegraphs over whole beats
    /// landing on beats, no stagger or knockback from hits, perfect dodges filling Break and a full Break's stagger, phase
    /// two, and what each boss's death sets off.
    /// </summary>
    public class BossTests
    {
        const double Tick = Simulation.TickSeconds;
        static readonly GameContent Content = DashTests.Content;

        static int Cast(string id) => Content.Enemies.IndexOf(id);

        /// <summary>
        /// A duel with <paramref name="boss"/> called at the start (null: none), on <paramref name="song"/>'s entries (null: no
        /// music), the hero with <paramref name="build"/> (none by default).
        /// </summary>
        internal static Simulation Duel(string boss, FixtureSong song = null, Action<SimConfig> tweak = null, string[] build = null,
            bool level = false)
        {
            var run = new RunInputs { Duel = boss, Level = level, Loadout = build != null ? Arena.Build(build) : new Upgrade[0] };
            var config = run.Build(Content);
            tweak?.Invoke(config);
            var sim = new Simulation(config);
            if (song != null)
                foreach (var k in Drive.Known(song.Entries())) sim.AddKnown(k.Entry, k.KnownAt);
            return sim;
        }

        static void Immortal(SimConfig c) => c.Hero.Health = 1e9;

        /// <summary>Both bosses with <paramref name="health"/>.</summary>
        static Action<SimConfig> BossHealth(double health) => c =>
        {
            c.Enemies.Cast[Cast("fatOgre")].Health = health;
            c.Enemies.Cast[Cast("dragon")].Health = health;
        };

        static Action<SimConfig> All(params Action<SimConfig>[] tweaks) => c =>
        {
            foreach (var t in tweaks) t(c);
        };

        static List<SimEvent> Events(Simulation sim, SimEventKind kind) => Drive.Events(sim, kind);

        static SimEvent Arrival(Simulation sim) => Events(sim, SimEventKind.BossArrive).Single();

        /// <summary>The hero's blow of <paramref name="damage"/> landing on the boss now, through the hero's one way in.</summary>
        static void Strike(Simulation sim, double damage)
        {
            ref readonly var e = ref sim.Enemies[sim.Bosses.Id];
            sim.Strikes.Land(sim, new Blow
            {
                Shape = HitShape.Circle(e.X, e.Y, 0.1), T = sim.Now, Damage = damage, Tags = HitTags.Strike,
                Source = BlowSource.SwordSlash, Owner = -1, Part = -1,
            });
        }

        /// <summary>Steps frames of 1/60 s to <paramref name="until"/>, calling <paramref name="each"/> after each frame.</summary>
        static void Frames(Simulation sim, double until, Action<Simulation> each)
        {
            while (sim.Now < until - 1e-9)
            {
                sim.StepTo(Math.Min(until, sim.Now + 1.0 / 60));
                each(sim);
            }
        }

        /// <summary>
        /// A player who dashes through every boss attack on its beat: on each landing it sees, Space heard then (or
        /// <c>early</c> before), released the frame after, aimed at the boss when <c>through</c>. Its other inputs are
        /// heard <see cref="Lead"/> before they reach the sim, as a live run's are, so hits wait for its presses.
        /// </summary>
        internal sealed class Dodger
        {
            /// <summary>The sim's lead over what the player hears.</summary>
            public const double Lead = 0.05;

            readonly bool through;
            readonly double early;
            int cursor;
            double pressedFor = double.NaN;
            bool down;

            /// <param name="early">How long before each landing the press is heard.</param>
            public Dodger(bool through = false, double early = 0)
            {
                this.through = through;
                this.early = early;
            }

            public int Presses { get; private set; }

            public void Frame(Simulation sim)
            {
                if (down)
                {
                    sim.Release(Key.Space, sim.Now - Lead);
                    down = false;
                }
                double land = double.NaN;
                foreach (var e in sim.EventsSince(cursor))
                    if (e.Kind == SimEventKind.BossMove && !Bosses.IsWave(e.MarkMove) && e.I2 == 0 && e.T != pressedFor) land = e.T;
                cursor = sim.EventCount;
                if (double.IsNaN(land) || sim.State.Hero.DashReadyAt > land - early + 1e-9) return;
                pressedFor = land;
                if (through && sim.Bosses.Id >= 0)
                {
                    ref readonly var b = ref sim.Enemies[sim.Bosses.Id];
                    sim.Target(b.X, b.Y, land - early);
                }
                sim.Press(Key.Space, land - early);
                down = true;
                Presses++;
            }
        }

        // ---- Content ----

        [Test]
        public void TheBossesHoldTheDesignsTables()
        {
            var b = Content.Bosses;
            Assert.AreEqual((4.0, 60.0), (b.WaitS, b.WindowS), "the first strong downbeat 4 s after spawning stops, within 60 s");
            Assert.AreEqual(0.25, b.BreakPerDodge, "a perfect dodge fills Break by a quarter");
            Assert.AreEqual(Content.Beat.BeatsPerBar, b.StaggerBeats, "a full Break staggers it a bar");
            Assert.AreEqual((2.0, 2.0), (b.StaggerBeatlessS, b.BreakDamageScale), "2 s with no beat, taking 2x damage");
            Assert.AreEqual(0.5, b.PhaseTwoBelow);
            var ogre = b.Of(Cast("fatOgre"));
            var dragon = b.Of(Cast("dragon"));
            CollectionAssert.AreEqual(new[] { BossMove.Smash, BossMove.Boulders, BossMove.Stomp, BossMove.Charge }, ogre.Moves.Select(m => m.Move));
            CollectionAssert.AreEqual(new[] { BossMove.Breath, BossMove.Fireballs, BossMove.Tail, BossMove.Dive }, dragon.Moves.Select(m => m.Move));
            // design.md's tables: the telegraphs' beats, and phase two's one-beat telegraphs (the dive keeps two to fly).
            Assert.AreEqual(2, ogre.Find(BossMove.Smash).Beats(1));
            Assert.AreEqual(3, ogre.Find(BossMove.Charge).Beats(1));
            Assert.AreEqual(2, ogre.Find(BossMove.Boulders).Beats(1));
            Assert.AreEqual(2, dragon.Find(BossMove.Breath).Beats(1));
            Assert.AreEqual(1, dragon.Find(BossMove.Breath).BurnBeats, "the breath burns for one beat");
            Assert.AreEqual(3, dragon.Find(BossMove.Fireballs).Count, "three fireballs");
            foreach (var m in ogre.Moves.Concat(dragon.Moves).Where(m => m.Move != BossMove.Dive)) Assert.AreEqual(1, m.Beats(2), m.Move.ToString());
            Assert.AreEqual(BossMove.Stomp, ogre.Find(BossMove.Smash).ChainTwo, "phase two: smash then stomp");
            Assert.AreEqual(BossMove.Dive, dragon.Find(BossMove.Dive).ChainTwo, "phase two: the dive chains twice");
            Assert.AreEqual(Math.PI, dragon.Find(BossMove.Breath).Sweep[1], 1e-9, "phase two: the breath sweeps 180 degrees");
            Assert.AreEqual((BossDeath.Treasure, BossDeath.Finale), (ogre.Death, dragon.Death));
            Assert.IsTrue(dragon.WarmZone && !ogre.WarmZone, "the warm zone is the dragon's");
            // level1.json's timeline: the Ogre at 7:00, the dragon at 13:00.
            var timeline = Content.Level.Bosses;
            Assert.AreEqual(2, timeline.Length);
            Assert.AreEqual((Cast("fatOgre"), 420.0), (timeline[0].Cast, timeline[0].CallAtS));
            Assert.AreEqual((Cast("dragon"), 780.0), (timeline[1].Cast, timeline[1].CallAtS));
        }

        // ---- Arrival ----

        /// <summary>The downbeats (reliable) a song plays, from a run with no boss.</summary>
        static List<double> Downbeats(FixtureSong song, double until)
        {
            var sim = Duel(null, song);
            sim.StepTo(until);
            return Events(sim, SimEventKind.Beat).Where(e => e.Downbeat && e.BeatReliability == Reliability.Reliable).Select(e => e.T).ToList();
        }

        [Test]
        public void ABossArrivesOnTheFirstStrongDownbeatFourSecondsAfterSpawningStops()
        {
            var downbeats = Downbeats(new FixtureSong().Beats(0, 30, 120, onsets: false), 30);
            var after = downbeats.Where(t => t >= 4).ToList();
            Assert.Greater(after.Count, 4);
            double strong = after[2];
            // Strong onsets before the wait, and off a downbeat, call nothing; the downbeats between are not strong.
            var song = new FixtureSong().Beats(0, 30, 120, onsets: false)
                .Onset(downbeats.First(), true).Onset(after[0] + 0.25, true).Onset(after[1] + 0.5, true).Onset(strong, true);
            var sim = Duel("fatOgre", song, Immortal);
            sim.StepTo(30);
            var call = Events(sim, SimEventKind.BossCall).Single();
            Assert.AreEqual((0.0, Cast("fatOgre")), (call.T, call.I0), "called at the start: spawning stops");
            var arrival = Arrival(sim);
            Assert.AreEqual(strong, arrival.T, 1e-9, "on the first strong downbeat 4 s or more after the call");
            Assert.AreEqual(Cast("fatOgre"), arrival.I1);
            Assert.AreEqual(sim.Bosses.Id, arrival.I0);
            Assert.AreEqual((0.0, Content.Bosses.DistanceM), (arrival.D0, arrival.D1), "landing up the screen from the hero");
            Assert.IsTrue(sim.Bosses.InDuel);
            var spawn = Events(sim, SimEventKind.EnemySpawn).Single();
            Assert.AreEqual((arrival.I0, 1, Content.Enemies.Cast[Cast("fatOgre")].Health), (spawn.I0, spawn.I2, spawn.D3), "a boss enemy, at its full health");
        }

        [Test]
        public void WithNoStrongDownbeatTheBossArrivesOnTheFirstDownbeatAfterTheWindow()
        {
            var song = new FixtureSong().Beats(0, 75, 120, onsets: false);
            var downbeats = Downbeats(song, 75);
            var sim = Duel("fatOgre", song, Immortal);
            sim.StepTo(75);
            Assert.AreEqual(downbeats.First(t => t >= Content.Bosses.WindowS - 1e-9), Arrival(sim).T, 1e-9);
        }

        [Test]
        public void WithNoReliableBeatTheBossArrivesFourSecondsAfterSpawningStops([Values(false, true)] bool music)
        {
            var sim = Duel("dragon", music ? new FixtureSong().Gap(0, 20) : null, Immortal);
            sim.StepTo(20);
            Assert.AreEqual(Content.Bosses.WaitS, Arrival(sim).T, 1e-9);
        }

        [Test]
        public void ABossWaitingForItsDownbeatArrivesOnceTheBeatIsLost()
        {
            var song = new FixtureSong().Beats(0, 12, 120, onsets: false).Gap(12, 30);
            var sim = Duel("fatOgre", song, Immortal);
            sim.StepTo(30);
            double lost = Events(sim, SimEventKind.BeatState).First(e => e.T > 4 && (Reliability)e.I0 != Reliability.Reliable).T;
            double arrived = Arrival(sim).T;
            Assert.GreaterOrEqual(arrived, lost - 1e-9);
            Assert.Less(arrived, lost + Tick + 1e-9, "on the tick the beat was lost");
        }

        [Test]
        public void TheLevelCallsEachBossOnItsTimelineAndTheOgresDeathResumesSpawning()
        {
            var sim = Duel(null, new FixtureSong().Beats(0, 60, 120), All(Immortal, c =>
            {
                c.Level.Bosses[0].CallAtS = 10;
                c.Level.Bosses[1].CallAtS = 40;
                c.Enemies.Cast[Cast("fatOgre")].Health = 50;
            }), level: true);
            sim.StepTo(9.9);
            Assert.AreEqual(BossStage.None, sim.Bosses.Stage);
            Assert.IsFalse(sim.Horde.Spawner.Stopped);
            sim.StepTo(10.1);
            Assert.AreEqual(BossStage.Coming, sim.Bosses.Stage);
            Assert.IsTrue(sim.Horde.Spawner.Stopped, "a call stops spawning");
            sim.StepTo(20);
            Assert.IsTrue(sim.Bosses.InDuel);
            int spawned = sim.Horde.Spawned;
            sim.StepTo(25);
            Assert.AreEqual(spawned, sim.Horde.Spawned, "no spawns in a duel");
            Strike(sim, 60);
            Assert.AreEqual(BossStage.Dead, sim.Bosses.Stage);
            Assert.IsFalse(sim.Horde.Spawner.Stopped, "the Ogre's death resumes spawning");
            Assert.IsTrue(sim.Horde.Spawner.SecondHalf, "with the second half's mix");
            sim.StepTo(sim.Now + Tick);
            Assert.AreEqual(OfferKind.Treasure, sim.State.Offer.Kind, "its treasure holds the world until a pick");
            // The treasure, then the levels its entrance's kills reached.
            while (sim.State.Offer.Up) sim.Press(Key.One, sim.Now);
            sim.StepTo(39.9);
            Assert.AreEqual(1, sim.Bosses.Called);
            sim.StepTo(40.1);
            Assert.AreEqual((2, Cast("dragon"), BossStage.Coming), (sim.Bosses.Called, sim.Bosses.Cast, sim.Bosses.Stage), "the dragon on its minute");
            Assert.IsTrue(sim.Horde.Spawner.Stopped);
        }

        // ---- The entrance ----

        [Test]
        public void TheEntranceShattersEveryMobOnScreenOutwardFromTheBossAndClearsTheRest()
        {
            var sim = Duel(null, new FixtureSong().Beats(0, 40, 120), All(Immortal, c => c.Level.Bosses[0].CallAtS = 20), level: true);
            sim.StepTo(20.1);
            Assert.Greater(sim.Horde.Alive, 100, "a horde before the boss");
            int cursor = sim.EventCount;
            sim.StepTo(40);
            var arrival = Arrival(sim);
            var at = sim.EventsSince(cursor).ToArray().Where(e => Math.Abs(e.T - arrival.T) < 1e-9).ToList();
            var kills = at.Where(e => e.Kind == SimEventKind.Kill).ToList();
            var gone = at.Where(e => e.Kind == SimEventKind.EnemyGone).ToList();
            Assert.AreEqual(arrival.I2, kills.Count, "the arrival counts its shatter");
            Assert.Greater(kills.Count, 50);
            var b = Content.Bosses;
            foreach (var k in kills)
            {
                Assert.AreEqual(BlowSource.Boss, (BlowSource)k.I1);
                double sx = (k.D3 - arrival.D3) / b.ScreenHalfWidthM, sy = (k.D4 - arrival.D4) / b.ScreenHalfHeightM;
                Assert.LessOrEqual(sx * sx + sy * sy, 1 + 1e-9, "on screen");
                Assert.Greater(k.D0 * (k.D3 - arrival.D0) + k.D1 * (k.D4 - arrival.D1), 0, "thrown away from the boss");
            }
            foreach (var g in gone)
            {
                double sx = (g.D0 - arrival.D3) / b.ScreenHalfWidthM, sy = (g.D1 - arrival.D4) / b.ScreenHalfHeightM;
                Assert.Greater(sx * sx + sy * sy, 1, "off screen, unseen");
            }
            Assert.AreEqual(0, sim.Horde.Alive, "the duel is on cleared ground");
            Assert.AreEqual(1, sim.Enemies.Alive, "the boss alone");
            Assert.IsEmpty(sim.Horde.Moves.Spores.ToArray());
        }

        [Test]
        public void TheEntranceBreaksTheStructuresOnScreenAndTheDuelKeepsNewOnesAway()
        {
            var sim = Duel(null, new FixtureSong().Beats(0, 40, 120), All(Immortal, c =>
            {
                c.Level.Bosses[0].CallAtS = 20;
                c.Enemies.Cast[Cast("fatOgre")].Health = 50;
            }), level: true);
            sim.StepTo(40);
            var arrival = Arrival(sim);
            var breaks = Events(sim, SimEventKind.StructureBreak).Where(e => Math.Abs(e.T - arrival.T) < 1e-9).ToList();
            Assert.IsNotEmpty(breaks, "structures on screen when the Ogre landed");
            foreach (var b in breaks)
            {
                Assert.AreEqual(BreakCause.Entrance, (BreakCause)b.I2);
                Assert.Greater(b.D0 * (b.D3 - arrival.D0) + b.D1 * (b.D4 - arrival.D1), 0, "thrown away from the boss");
            }
            Assert.IsTrue(sim.StructureField.InDuel, "chunks loading in the duel stand empty");
            Strike(sim, 60);
            Assert.IsFalse(sim.StructureField.InDuel, "until the boss dies");
        }

        [Test]
        public void TheDragonsEntranceTurnsTheFloorWarm()
        {
            var sim = Duel("dragon", new FixtureSong().Beats(0, 20, 120), Immortal);
            sim.StepTo(20);
            Assert.AreEqual(Content.Zones.Warm, sim.State.Zone);
            var zone = Events(sim, SimEventKind.Zone).Last();
            Assert.AreEqual((ZoneReason.Warm, Arrival(sim).T), ((ZoneReason)zone.I2, zone.T));
        }

        // ---- Telegraphs ----

        [Test]
        public void EveryTelegraphFillsOverWholeBeatsAndLandsOnABeat([Values("fatOgre", "dragon")] string boss)
        {
            // The beats run on past the duel, so every planned landing has its beat.
            var song = new FixtureSong().Beats(0, 70, 120);
            var beats = song.BeatTimes();
            var sim = Duel(boss, song, Immortal);
            sim.StepTo(60);
            var telegraphs = Events(sim, SimEventKind.BossTelegraph);
            var landed = Events(sim, SimEventKind.BossMove).Where(e => e.I2 == 0).ToList();
            Assert.Greater(landed.Count, 12);
            var type = Content.Bosses.Of(Cast(boss));
            foreach (var t in telegraphs)
            {
                Assert.IsTrue(beats.Any(b => Math.Abs(b - t.T) < 1e-9), $"{t.MarkMove} starts on a beat");
                Assert.IsTrue(beats.Any(b => Math.Abs(b - t.D0) < 1e-9), $"{t.MarkMove} lands on a beat");
                int count = (int)Math.Round((t.D0 - t.T) / 0.5);
                if (t.MarkMove == BossMove.Stomp || t.MarkMove == BossMove.Tail) Assert.GreaterOrEqual(count, type.Find(t.MarkMove).Beats(1));
                else Assert.AreEqual(type.Find(t.MarkMove).Beats(1), count, t.MarkMove.ToString());
            }
            foreach (var m in landed) Assert.IsTrue(beats.Any(b => Math.Abs(b - m.T) < 1e-9), $"{m.MarkMove} landed on its beat");
            CollectionAssert.IsSubsetOf(type.Moves.Select(m => m.Move), landed.Select(m => m.MarkMove).Distinct(), "every move comes round");
        }

        [Test]
        public void StompAndTailLandOnDownbeats([Values("fatOgre", "dragon")] string boss)
        {
            var song = new FixtureSong().Beats(0, 70, 120);
            var downbeats = Downbeats(song, 70);
            var sim = Duel(boss, song, Immortal);
            sim.StepTo(60);
            var rings = Events(sim, SimEventKind.BossMove).Where(e => e.MarkMove == BossMove.Stomp || e.MarkMove == BossMove.Tail).ToList();
            Assert.IsNotEmpty(rings);
            foreach (var r in rings) Assert.IsTrue(downbeats.Any(d => Math.Abs(d - r.T) < 1e-9), $"{r.MarkMove} at {r.T}");
        }

        [Test]
        public void WithNoBeatATelegraphTakesHalfASecondABeat([Values("fatOgre", "dragon")] string boss)
        {
            var sim = Duel(boss, new FixtureSong().Gap(0, 40), Immortal);
            sim.StepTo(40);
            var type = Content.Bosses.Of(Cast(boss));
            var telegraphs = Events(sim, SimEventKind.BossTelegraph);
            Assert.Greater(telegraphs.Count, 8);
            foreach (var t in telegraphs) Assert.AreEqual(type.Find(t.MarkMove).Beats(1) * 0.5, t.D0 - t.T, 1e-9, t.MarkMove.ToString());
            foreach (var m in Events(sim, SimEventKind.BossMove)) Assert.AreEqual(0, m.I2, "nothing called off");
        }

        [Test]
        public void TheFireballsAreMarkedOnTheHeroOnePerBeat()
        {
            var sim = Duel("dragon", new FixtureSong().Beats(0, 40, 120), Immortal);
            // Walking right from the start: at 6t at song time t.
            sim.Press(Key.Right, 0);
            sim.StepTo(40);
            var balls = Events(sim, SimEventKind.BossTelegraph).Where(e => e.MarkMove == BossMove.Fireballs).Take(3).ToList();
            Assert.AreEqual(new[] { 0, 1, 2 }, balls.Select(b => b.MarkPart).ToArray());
            Assert.AreEqual(0.5, balls[1].T - balls[0].T, 1e-9);
            Assert.AreEqual(0.5, balls[2].T - balls[1].T, 1e-9);
            foreach (var b in balls) Assert.AreEqual(Content.Hero.SpeedMps * b.T, b.D1, 0.06, "marked where the hero is on its beat");
        }

        // ---- Hits on the boss ----

        [Test]
        public void HitsNeverStaggerOrKnockBackABoss([Values("fatOgre", "dragon")] string boss)
        {
            var song = new FixtureSong().Beats(0, 40, 120);
            var calm = Duel(boss, song, All(Immortal, BossHealth(1e9)));
            var struck = Duel(boss, song, All(Immortal, BossHealth(1e9)), new[] { "swordSlash:3", "orbitBlades:3", "bladeRain:3", "keenEdge:2" });
            calm.StepTo(40);
            struck.StepTo(40);
            var hits = Events(struck, SimEventKind.Hit).Where(e => e.I0 == struck.Bosses.Id).ToList();
            Assert.Greater(hits.Count, 40, "the boss was hit again and again");
            string Moves(Simulation s) => string.Join("\n", Events(s, SimEventKind.BossTelegraph).Concat(Events(s, SimEventKind.BossMove))
                .Select(e => $"{e.Kind} {e.T:R} {e.I1} {e.I2} {e.D0:R} {e.D1:R} {e.D2:R} {e.D3:R} {e.D4:R}"));
            Assert.AreEqual(Moves(calm), Moves(struck), "its moves, where it stood and its timing, never changed");
            for (double t = 6; t < 40; t += 0.37)
            {
                calm.Bosses.At(Math.Min(t, calm.Now), out double ax, out double ay, out _);
                struck.Bosses.At(Math.Min(t, struck.Now), out double bx, out double by, out _);
                Assert.AreEqual((ax, ay), (bx, by));
            }
        }

        [Test]
        public void ABossFollowsTheHeroKeepingItsDistance()
        {
            var sim = Duel("fatOgre", new FixtureSong().Gap(0, 30), All(Immortal, BossHealth(1e9)));
            sim.StepTo(4.5);
            sim.Press(Key.Left, sim.Now);
            sim.StepTo(10);
            sim.Release(Key.Left, sim.Now);
            sim.StepTo(30);
            ref readonly var e = ref sim.Enemies[sim.Bosses.Id];
            sim.HeroWas(sim.Now, out double hx, out double hy);
            double d = Math.Sqrt((e.X - hx) * (e.X - hx) + (e.Y - hy) * (e.Y - hy));
            Assert.Less(hx, -25, "the hero walked off");
            Assert.Less(d, 12, "the Ogre came after it");
        }

        // ---- Perfect dodges and Break ----

        [Test]
        public void PerfectDodgesFillBreakByAQuarterAndAFullBreakStaggersTheBossForABar()
        {
            var song = new FixtureSong().Beats(0, 60, 120);
            var sim = Duel("fatOgre", song, All(Immortal, BossHealth(1e9)), new[] { "swordSlash:1" });
            var dodger = new Dodger();
            Frames(sim, 60, dodger.Frame);
            var perfect = Events(sim, SimEventKind.HeroHit).Where(e => (HitOutcome)e.I0 == HitOutcome.PerfectDodge).ToList();
            Assert.IsEmpty(Events(sim, SimEventKind.HeroHit).Where(e => (HitOutcome)e.I0 == HitOutcome.Hurt), "every attack dodged on its beat");
            Assert.GreaterOrEqual(perfect.Count, 8);
            var fills = Events(sim, SimEventKind.BossBreak);
            Assert.GreaterOrEqual(fills.Count, 8);
            Assert.AreEqual(new[] { 0.25, 0.5, 0.75, 1.0, 0.25, 0.5, 0.75, 1.0 }, fills.Take(8).Select(f => f.D0).ToArray(), "a quarter per dodge, emptied by the stagger");
            var staggers = Events(sim, SimEventKind.BossStagger);
            var on = staggers.Where(s => s.I1 == 1).ToList();
            Assert.AreEqual(fills.Count(f => f.D0 == 1.0), on.Count);
            var beats = song.BeatTimes();
            var landings = Events(sim, SimEventKind.BossMove).Where(m => m.I2 == 0).Select(m => m.T).ToList();
            foreach (var s in on)
            {
                var off = staggers.First(x => x.I1 == 0 && x.T > s.T);
                // A bar from the dodged attack's beat (the latest landing before the Break filled).
                double dodged = landings.Last(t => t <= s.T);
                int i = beats.FindIndex(b => Math.Abs(b - dodged) < 1e-9);
                Assert.AreEqual(beats[i + 4], off.T, 1e-9, "staggered until a bar after the dodged beat");
                Assert.IsEmpty(Events(sim, SimEventKind.BossTelegraph).Where(t => t.T > s.T && t.T < off.T - 1e-9), "no moves while staggered");
                var during = Events(sim, SimEventKind.Hit).Where(h => h.I0 == sim.Bosses.Id && h.T > s.T + 1e-9 && h.T < off.T - 1e-9).ToList();
                foreach (var h in during) Assert.AreEqual(2.0, h.D2, 1e-9, "double damage while staggered");
            }
            var hits = Events(sim, SimEventKind.Hit).Where(h => h.I0 == sim.Bosses.Id).ToList();
            Assert.IsTrue(hits.Any(h => h.D2 == 2.0) && hits.Any(h => h.D2 == 1.0), "Sword Slash hit it staggered and not");
        }

        [Test]
        public void ADodgeOffTheBeatIsNoPerfectDodge()
        {
            var sim = Duel("fatOgre", new FixtureSong().Beats(0, 40, 120), All(Immortal, BossHealth(1e9)));
            // Heard 0.14 s early: the i-frames cover the landing, but the press is off its beat.
            var dodger = new Dodger(early: 0.14);
            Frames(sim, 40, dodger.Frame);
            var outcomes = Events(sim, SimEventKind.HeroHit).Select(e => (HitOutcome)e.I0).ToList();
            Assert.Greater(outcomes.Count(o => o == HitOutcome.Dodged), 5);
            Assert.IsFalse(outcomes.Contains(HitOutcome.PerfectDodge));
            Assert.IsEmpty(Events(sim, SimEventKind.BossBreak));
            Assert.AreEqual(0, sim.Bosses.Break);
        }

        [Test]
        public void ADashThroughTheBossOnTheBeatIsAPerfectDodge()
        {
            var sim = Duel("dragon", new FixtureSong().Beats(0, 40, 120), All(Immortal, BossHealth(1e9)));
            var dodger = new Dodger(through: true);
            Frames(sim, 40, dodger.Frame);
            Assert.Greater(dodger.Presses, 10);
            var outcomes = Events(sim, SimEventKind.HeroHit).Select(e => (HitOutcome)e.I0).ToList();
            Assert.Greater(outcomes.Count(o => o == HitOutcome.PerfectDodge), 6);
            Assert.GreaterOrEqual(Events(sim, SimEventKind.BossStagger).Count(s => s.I1 == 1), 1);
        }

        [Test]
        public void WithNoBeatAFullBreakStaggersForTwoSeconds()
        {
            var sim = Duel("fatOgre", new FixtureSong().Gap(0, 60), All(Immortal, BossHealth(1e9)));
            Frames(sim, 60, new Dodger().Frame);
            var staggers = Events(sim, SimEventKind.BossStagger);
            var on = staggers.First(s => s.I1 == 1);
            var off = staggers.First(s => s.I1 == 0);
            Assert.AreEqual(-1, on.I2);
            Assert.AreEqual(Content.Bosses.StaggerBeatlessS, off.T - on.T, 1e-9);
        }

        // ---- Phase two ----

        [Test]
        public void BelowHalfHealthPhaseTwoShortensTelegraphsAndChainsTheSmashIntoTheStomp()
        {
            var song = new FixtureSong().Beats(0, 80, 120);
            var sim = Duel("fatOgre", song, All(Immortal, BossHealth(100)));
            sim.StepTo(10);
            Strike(sim, 49);
            Assert.AreEqual(1, sim.Bosses.Phase, "at half health, still phase one");
            Assert.IsEmpty(Events(sim, SimEventKind.BossPhase));
            Strike(sim, 2);
            var phase = Events(sim, SimEventKind.BossPhase).Single();
            Assert.AreEqual((2, sim.Now), (phase.I1, phase.T));
            sim.StepTo(80);
            var telegraphs = Events(sim, SimEventKind.BossTelegraph).Where(t => t.T > phase.T + 1.0).ToList();
            Assert.Greater(telegraphs.Count, 10);
            foreach (var t in telegraphs.Where(t => t.MarkMove != BossMove.Stomp)) Assert.AreEqual(0.5, t.D0 - t.T, 1e-9, $"{t.MarkMove}: one beat");
            var smashes = Events(sim, SimEventKind.BossMove).Where(m => m.MarkMove == BossMove.Smash && m.T > phase.T + 1.0).ToList();
            Assert.IsNotEmpty(smashes);
            foreach (var s in smashes)
            {
                var next = telegraphs.First(t => t.T >= s.T - 1e-9 && t.MarkMove != BossMove.Smash);
                Assert.AreEqual(BossMove.Stomp, next.MarkMove, "the stomp follows the smash");
                Assert.AreEqual(s.T + 0.5, next.T, 1e-9, "from the next beat");
            }
        }

        [Test]
        public void InPhaseTwoTheBreathSweepsAndTheDiveChainsTwice()
        {
            var sim = Duel("dragon", new FixtureSong().Beats(0, 90, 120), All(Immortal, BossHealth(100)));
            sim.StepTo(6);
            Strike(sim, 60);
            sim.StepTo(90);
            var phase = Events(sim, SimEventKind.BossPhase).Single();
            var breaths = Events(sim, SimEventKind.BossMove).Where(m => m.MarkMove == BossMove.Breath && m.T > phase.T + 1.5 && m.I2 == 0).ToList();
            Assert.IsNotEmpty(breaths);
            foreach (var b in breaths) Assert.AreEqual(Math.PI, b.D5, 1e-9, "a 180 degree sweep");
            var dives = Events(sim, SimEventKind.BossTelegraph).Where(t => t.MarkMove == BossMove.Dive && t.T > phase.T + 1.5).ToList();
            Assert.GreaterOrEqual(dives.Count, 2);
            var landings = Events(sim, SimEventKind.BossMove).Where(m => m.MarkMove == BossMove.Dive).ToList();
            var first = landings.First(l => l.T > phase.T + 1.5);
            var second = dives.First(d => d.T >= first.T - 1e-9);
            Assert.AreEqual(first.T + 0.5, second.T, 1e-9, "a second dive from the next beat");
            Assert.AreEqual(1.0, second.D0 - second.T, 1e-9, "over two beats");
            var third = Events(sim, SimEventKind.BossTelegraph).First(t => t.T > second.D0);
            Assert.AreNotEqual(BossMove.Dive, third.MarkMove, "never a third");
        }

        // ---- Death ----

        [Test]
        public void TheOgresDeathHealsTheHeroOpensTreasureAndCallsOffItsMoves()
        {
            var sim = Duel("fatOgre", new FixtureSong().Beats(0, 30, 120));
            int died = 0;
            sim.Bosses.Died += (s, type) => died++;
            sim.StepTo(12);
            Assert.Less(sim.State.Hero.Health, sim.State.Hero.MaxHealth, "the Ogre hurt the hero");
            while (sim.Bosses.Marks.Length == 0) sim.StepTo(sim.Now + Tick);
            int marks = sim.Bosses.Marks.Length;
            var pending = sim.Bosses.Marks.ToArray().Select(m => m.Id).ToList();
            Strike(sim, 1e9);
            var death = Events(sim, SimEventKind.BossDeath).Single();
            Assert.AreEqual((sim.Bosses.Id, Cast("fatOgre"), (int)BossDeath.Treasure), (death.I0, death.I1, death.I2));
            Assert.AreEqual(1, died, "the treasure's hook");
            Assert.AreEqual(sim.State.Hero.MaxHealth, sim.State.Hero.Health, "a boss kill heals fully");
            Assert.AreEqual(HealReason.BossKill, (HealReason)Events(sim, SimEventKind.HeroHeal).Last().I0);
            Assert.IsFalse(sim.Bosses.InDuel);
            Assert.AreEqual(RunEnd.None, sim.State.End);
            var calledOff = Events(sim, SimEventKind.BossMove).Where(m => m.I2 == 1).Select(m => m.I0).ToList();
            CollectionAssert.AreEquivalent(pending, calledOff, "its marks not landed are called off");
            Assert.AreEqual(marks, calledOff.Count);
            Assert.IsEmpty(sim.Bosses.Marks.ToArray());
            int count = sim.EventCount;
            sim.StepTo(30);
            Assert.IsEmpty(sim.EventsSince(count).ToArray().Where(e => e.Kind == SimEventKind.BossTelegraph));
        }

        [Test]
        public void TheDragonsDeathIsTheFinaleAndWinsTheRun()
        {
            var sim = Duel("dragon", new FixtureSong().Beats(0, 30, 120), Immortal);
            sim.StepTo(10);
            Strike(sim, 1e9);
            var death = Events(sim, SimEventKind.BossDeath).Single();
            Assert.AreEqual((int)BossDeath.Finale, death.I2);
            Assert.AreEqual(RunEnd.Victory, sim.State.End);
            Assert.AreEqual(RunEnd.Victory, (RunEnd)Events(sim, SimEventKind.RunEnd).Single().I0);
        }

        // ---- Holds ----

        [Test]
        public void APauseCallsOffTheMoveInHandAndTheBossRestsAfter()
        {
            var sim = Duel("fatOgre", new FixtureSong().Beats(0, 40, 120), All(Immortal, BossHealth(1e9)));
            while (sim.Bosses.Marks.Length == 0) sim.StepTo(sim.Now + Tick);
            sim.Control(RunControl.Pause, sim.Now);
            var off = Events(sim, SimEventKind.BossMove).Where(m => m.I2 == 1).ToList();
            Assert.IsNotEmpty(off);
            Assert.IsEmpty(sim.Bosses.Marks.ToArray());
            sim.StepTo(sim.Now + 3);
            Assert.AreEqual(off.Count, Events(sim, SimEventKind.BossMove).Count(m => m.I2 == 1));
            Assert.IsEmpty(sim.Bosses.Marks.ToArray(), "nothing while paused");
            double resumed = sim.Now;
            sim.Control(RunControl.Resume, sim.Now);
            sim.StepTo(40);
            var next = Events(sim, SimEventKind.BossTelegraph).First(t => t.T > resumed);
            Assert.GreaterOrEqual(next.T, resumed + 2 * 0.5 - 1e-9, "it rests its beats first");
        }
    }
}
