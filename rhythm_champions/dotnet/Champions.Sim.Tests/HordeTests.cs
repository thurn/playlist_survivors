using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using NUnit.Framework;

namespace Champions.Sim.Tests
{
    /// <summary>
    /// The horde (design.md, "The Horde", Decisions 14, 16 and 17): steps on the beat at a speed that never
    /// follows the tempo, gliding without a beat, dark gaps, touch bites, the elites' and mushrooms'
    /// telegraphed moves, the spawner's ramp, surges and waves under the cap, and the frame budget at 1,000.
    /// </summary>
    public class HordeTests
    {
        const double Tick = Simulation.TickSeconds;
        static readonly GameContent Content = DashTests.Content;

        static int Kind(string id) => Content.Enemies.IndexOf(id);

        static EnemyType Type(string id) => Content.Enemies.Cast[Kind(id)];

        /// <summary>
        /// A sim on the content (with level 1's spawner when <paramref name="level"/>), given <paramref name="song"/>'s
        /// entries (null: no music), the hero without attacks unless <paramref name="attacks"/> (Sword Slash, the start).
        /// </summary>
        internal static Simulation Sim(FixtureSong song = null, bool level = false, Action<SimConfig> tweak = null,
            IEnumerable<StreamEntry> entries = null, bool attacks = false)
        {
            var run = new RunInputs { Level = level };
            if (!attacks) run.Loadout = new Upgrade[0];
            var config = run.Build(Content);
            tweak?.Invoke(config);
            var sim = new Simulation(config);
            var all = new List<StreamEntry>();
            if (song != null) all.AddRange(song.Entries());
            if (entries != null) all.AddRange(entries);
            foreach (var k in Drive.Known(all)) sim.AddKnown(k.Entry, k.KnownAt);
            return sim;
        }

        static void Immortal(SimConfig config) => config.Hero.Health = 1e9;

        /// <summary>Steps the sim tick by tick to <paramref name="until"/>, calling <paramref name="each"/> after every tick.</summary>
        static void Run(Simulation sim, double until, Action<Simulation> each)
        {
            while (sim.Now < until - 1e-9)
            {
                sim.StepTo(sim.Now + Tick);
                each(sim);
            }
        }

        /// <summary>Every distinct motion mob <paramref name="id"/> made up to <paramref name="until"/>, in order.</summary>
        static List<Mob> Motions(Simulation sim, int id, double until)
        {
            var list = new List<Mob>();
            Run(sim, until, s =>
            {
                var m = s.Horde.Mobs[id];
                if (m.Motion == Motion.Step && (list.Count == 0 || list[list.Count - 1].Start != m.Start)) list.Add(m);
            });
            return list;
        }

        static bool IsBeat(List<double> beats, double t) => beats.Any(b => Math.Abs(b - t) < 1e-9);

        [Test]
        public void TheCastHoldsTheDesignsTable()
        {
            var e = Content.Enemies;
            // design.md, "The cast": type, role, steps (beats), speed (m/s), and "Threats"' moves.
            var table = new (string Id, EnemyRole Role, double Steps, double Speed, EliteMove Move)[]
            {
                ("skeleton", EnemyRole.Fodder, 1, 1.5, EliteMove.None),
                ("slime", EnemyRole.Fodder, 1, 1.2, EliteMove.None),
                ("demon", EnemyRole.Fodder, 1, 1.8, EliteMove.None),
                ("bat", EnemyRole.Swarmer, 0.5, 2.6, EliteMove.None),
                ("goblin", EnemyRole.Fodder, 1, 2.0, EliteMove.None),
                ("mushroom", EnemyRole.Ranged, 2, 0.9, EliteMove.Spores),
                ("bigOrk", EnemyRole.Elite, 2, 1.4, EliteMove.Charge),
                ("troll", EnemyRole.Elite, 2, 1.2, EliteMove.Slam),
            };
            foreach (var row in table)
            {
                var t = Type(row.Id);
                Assert.AreEqual(row.Role, t.Role, row.Id);
                Assert.AreEqual(row.Steps, t.StepBeats, row.Id);
                Assert.AreEqual(row.Speed, t.SpeedMps, row.Id);
                Assert.AreEqual(row.Move, t.Move, row.Id);
                Assert.AreEqual(t.Role == EnemyRole.Elite ? 10 : row.Id == "bat" ? 0.5 : t.Experience, t.Experience, row.Id);
            }
            Assert.AreEqual(EnemyRole.Boss, Type("fatOgre").Role);
            Assert.AreEqual(EnemyRole.Boss, Type("dragon").Role);
            Assert.AreEqual(10, e.Cast.Length, "ten types");
            Assert.AreEqual(1, Type("skeleton").Experience);
            Assert.AreEqual(8, Type("troll").Toughness / Type("skeleton").Toughness, "elites take about 8x");
            Assert.AreEqual(5, e.TouchDamage);
            Assert.AreEqual(0.5, e.TouchBeatlessS);
            Assert.AreEqual((2, 8.0, 20.0), (e.Charge.TelegraphBeats, e.Charge.LengthM, e.Charge.Damage));
            Assert.AreEqual((2, 3.0, 20.0), (e.Slam.TelegraphBeats, e.Slam.RadiusM, e.Slam.Damage));
            // The design's spores fire every 2 bars for 10; ps-6y2.21's first tuning slowed them to every 4 bars for 6 (real music's
            // second half died to spores: rhythm_champions/evidence/ps-6y2.21/README.md).
            Assert.AreEqual((1, 7.0, 14.0, 4, 6.0), (e.Spores.TelegraphBeats, e.Spores.SpeedMps, e.Spores.RangeM, e.Spores.EveryBars, e.Spores.Damage));
            Assert.AreEqual(0.4, e.SettleShare);
            Assert.AreEqual(1000, Content.Level.Cap);
            // The defaults are the design's numbers with no cast.
            var defaults = new EnemyRules();
            Assert.AreEqual(0, defaults.Cast.Length);
            Assert.AreEqual((e.SettleShare, e.GapShare, e.TouchDamage), (defaults.SettleShare, defaults.GapShare, defaults.TouchDamage));
            Assert.AreEqual((e.Slam.TriggerM, e.Charge.TriggerM), (defaults.Slam.TriggerM, defaults.Charge.TriggerM));
        }

        // ---- Steps ----

        [Test]
        public void StepsStartOnTheirBeatsAndSettleWithinFortyPercentOfTheInterval()
        {
            var song = new FixtureSong().Beats(0, 30, 128);
            var beats = song.BeatTimes();
            double period = 60.0 / 128;
            foreach (var (id, every) in new[] { ("skeleton", 1.0), ("bat", 0.5), ("troll", 2.0) })
            {
                var sim = Sim(song, tweak: Immortal);
                int mob = sim.Horde.Spawn(Kind(id), 60, 0);
                var steps = Motions(sim, mob, 20);
                Assert.Greater(steps.Count, 10, id);
                for (int k = 0; k < steps.Count; k++)
                {
                    var s = steps[k];
                    double interval = every * period;
                    // Half beats lie halfway between beats; the rest start on a beat.
                    if (every >= 1) Assert.IsTrue(IsBeat(beats, s.Start), $"{id} step {k} at {s.Start} is on a beat");
                    else Assert.IsTrue(beats.Any(b => Math.Abs(b - s.Start) < 1e-9 || Math.Abs(b + period / 2 - s.Start) < 1e-6), $"{id} step {k} on a beat or half beat");
                    Assert.AreEqual(0.4 * interval, s.End - s.Start, 1e-6, id);
                    if (k > 0) Assert.AreEqual(interval, s.Start - steps[k - 1].Start, 1e-6, $"{id}: one step every {every} beats");
                    s.At(s.End, out double x, out double y);
                    Assert.AreEqual(s.ToX, x, 1e-12);
                    // The ease is sharp: most of the step is done a third of the way in.
                    s.At(s.Start + (s.End - s.Start) / 3, out x, out y);
                    Assert.Greater((s.FromX - x) / (s.FromX - s.ToX), 0.75, id);
                }
            }
        }

        [Test]
        public void TwoBeatSteppersStepOnTheBarsFirstAndThirdBeats()
        {
            var sim = Sim(new FixtureSong().Beats(0, 30, 128), tweak: Immortal);
            int troll = sim.Horde.Spawn(Kind("troll"), 60, 0);
            var steps = Motions(sim, troll, 20);
            var downbeats = Drive.Events(sim, SimEventKind.Beat).Where(e => e.Downbeat).Select(e => e.T).ToList();
            Assert.IsTrue(steps.Where(s => s.Start > downbeats[0]).All(s =>
                downbeats.Any(d => Math.Abs(d - s.Start) < 1e-9) || downbeats.Any(d => Math.Abs(d + 2 * 60.0 / 128 - s.Start) < 1e-6)));
        }

        [Test]
        public void AverageSpeedIsTheSameAt80And128And174Bpm()
        {
            foreach (var id in new[] { "skeleton", "bat", "goblin", "troll" })
            {
                var speeds = new List<double>();
                foreach (double bpm in new[] { 80.0, 128, 174 })
                {
                    var sim = Sim(new FixtureSong().Beats(0, 40, bpm), tweak: Immortal);
                    int mob = sim.Horde.Spawn(Kind(id), 100, 0);
                    var steps = Motions(sim, mob, 30);
                    var settled = steps.Skip(1).ToList();
                    double path = settled.Take(settled.Count - 1).Sum(s => Math.Abs(s.ToX - s.FromX));
                    double speed = path / (settled[settled.Count - 1].Start - settled[0].Start);
                    speeds.Add(speed);
                    Assert.AreEqual(Type(id).SpeedMps, speed, 0.01 * Type(id).SpeedMps, $"{id} at {bpm} BPM");
                }
                Assert.AreEqual(speeds.Max(), speeds.Min(), 0.005 * speeds.Max(), id);
            }
        }

        [Test]
        public void WithNoReliableBeatMobsGlideAtTheSameSpeedAndNeverStop()
        {
            // No music at all, and a song whose beat drops out.
            foreach (var song in new[] { null, new FixtureSong().Beats(0, 12, 128).Gap(12, 30) })
            {
                var sim = Sim(song, tweak: Immortal);
                int mob = sim.Horde.Spawn(Kind("skeleton"), 60, 0);
                double from = song == null ? 1.1 : 16;
                sim.StepTo(from);
                double lastX = sim.Enemies[mob].X;
                Run(sim, from + 5, s =>
                {
                    var m = s.Horde.Mobs[mob];
                    Assert.AreEqual(Motion.Glide, m.Motion);
                    double x = s.Enemies[mob].X;
                    Assert.AreEqual(1.5 * Tick, lastX - x, 1e-9, "every tick, at 1.5 m/s");
                    lastX = x;
                });
            }
        }

        [Test]
        public void ACrowdKeepsDarkGapsBetweenItsBodies()
        {
            foreach (var song in new[] { new FixtureSong().Beats(0, 60, 128), null })
            {
                var sim = Sim(song, tweak: Immortal);
                var kinds = new[] { "skeleton", "slime", "goblin", "demon", "troll" };
                var ids = new List<int>();
                for (int i = 0; i < 120; i++)
                    ids.Add(sim.Horde.Spawn(Kind(kinds[i % kinds.Length]), 12 + (i % 10) * 0.6, (i / 10) * 0.6 - 3.5));
                sim.StepTo(40);
                double gap = 1 + Content.Enemies.GapShare, worst = double.MaxValue, near = 0;
                foreach (int a in ids)
                foreach (int b in ids)
                {
                    if (a >= b) continue;
                    var ea = sim.Enemies[a];
                    var eb = sim.Enemies[b];
                    double d = Math.Sqrt((ea.X - eb.X) * (ea.X - eb.X) + (ea.Y - eb.Y) * (ea.Y - eb.Y));
                    double share = d / (ea.Radius + eb.Radius);
                    worst = Math.Min(worst, share);
                    if (share < gap - 0.05) near++;
                }
                // Packed round the hero, every pair keeps black between its bodies, nearly all the full gap.
                Assert.Greater(worst, 1.1, song == null ? "gliding" : "stepping");
                Assert.Less(near / (ids.Count * (ids.Count - 1) / 2.0), 0.002, song == null ? "gliding" : "stepping");
                // And they did close in, packed round the hero.
                var distances = ids.Select(id => Math.Sqrt(sim.Enemies[id].X * sim.Enemies[id].X + sim.Enemies[id].Y * sim.Enemies[id].Y)).OrderBy(d => d).ToList();
                Assert.Less(distances[distances.Count / 2], 6, song == null ? "gliding" : "stepping");
            }
        }

        [Test]
        public void DistantBodiesInACrowdedQueryDoNotChangeSlides()
        {
            foreach (bool music in new[] { true, false })
            {
                Simulation Make(int distant)
                {
                    var bodies = new Dummy[distant + 1];
                    bodies[0] = new Dummy(11.5, .65, 1e9, .4);
                    // Inside the initial neighbourhood but beyond the slide's reach; enough
                    // results to grow scratch storage, and an even count to keep mover parity.
                    for (int i = 1; i < bodies.Length; i++) bodies[i] = new Dummy(14, 0, 1e9, .1);
                    return Sim(music ? new FixtureSong().Beats(0, 30, 120) : null,
                        tweak: c => { Immortal(c); c.Dummies = bodies; });
                }
                var sparse = Make(0);
                var crowded = Make(300);
                int a = sparse.Horde.Spawn(Kind("skeleton"), 12, 0);
                int b = crowded.Horde.Spawn(Kind("skeleton"), 12, 0);
                double side = 0;
                for (int tick = 1; tick <= 720; tick++)
                {
                    sparse.StepTo(tick * Tick);
                    crowded.StepTo(tick * Tick);
                    var ea = sparse.Enemies[a]; var eb = crowded.Enemies[b];
                    Assert.That((eb.X, eb.Y), Is.EqualTo((ea.X, ea.Y)), $"music={music}, tick={tick}");
                    var ma = sparse.Horde.Mobs[a]; var mb = crowded.Horde.Mobs[b];
                    Assert.That((mb.ToX, mb.ToY, mb.Detour), Is.EqualTo((ma.ToX, ma.ToY, ma.Detour)));
                    double dx = eb.X - 11.5, dy = eb.Y - .65;
                    Assert.That(dx * dx + dy * dy, Is.GreaterThanOrEqualTo(.8 * .8 - 1e-6));
                    side = Math.Max(side, Math.Abs(eb.Y));
                }
                Assert.That(side, Is.GreaterThan(.1), "the nearby body actually bends the path");
                Assert.That(crowded.Enemies[b].X, Is.LessThan(4), "the mob gets past the obstacle");
            }
        }

        [Test]
        public void MobsSlideAlongAndSteerAroundStructures()
        {
            var sim = Sim(new FixtureSong().Beats(0, 60, 128), tweak: Immortal);
            var walls = new Walls();
            var wall = walls.Add(4, -2, 5, 2, 3);
            sim.UseStructures(walls);
            int mob = sim.Horde.Spawn(Kind("skeleton"), 12, 0.5);
            double closest = double.MaxValue;
            Run(sim, 30, s =>
            {
                var e = s.Enemies[mob];
                Assert.IsFalse(e.X - 0.4 < 5 - 1e-6 && e.X + 0.4 > 4 + 1e-6 && e.Y - 0.4 < 2 - 1e-6 && e.Y + 0.4 > -2 + 1e-6, "never inside the wall");
                closest = Math.Min(closest, Math.Sqrt(e.X * e.X + e.Y * e.Y));
            });
            Assert.Less(closest, 1.0, "it found its way round to the hero");
            Assert.IsFalse(wall.Broken, "monsters never break structures");
            Assert.AreEqual(0, wall.Struck.Count);
        }

        // ---- Touch ----

        [Test]
        public void AStepLandingAgainstTheHeroBitesOnThatBeat()
        {
            var song = new FixtureSong().Beats(0, 30, 128);
            var beats = song.BeatTimes();
            var sim = Sim(song, tweak: Immortal);
            sim.Horde.Spawn(Kind("skeleton"), 0.85, 0);
            sim.StepTo(10);
            double reliable = Drive.Events(sim, SimEventKind.BeatState).First(e => (Reliability)e.I0 == Reliability.Reliable).T;
            var bites = Drive.Events(sim, SimEventKind.HeroHit).Where(e => (HitSource)e.I1 == HitSource.Touch && e.T > reliable).ToList();
            Assert.Greater(bites.Count, 10);
            foreach (var b in bites) Assert.IsTrue(IsBeat(beats, b.T), $"a bite at {b.T} lands on a beat");
            var hurt = bites.Where(b => (HitOutcome)b.I0 == HitOutcome.Hurt).ToList();
            Assert.AreEqual(5, hurt[0].D0);
            // Every beat bites; the half second after a hit lets every other through at 128 BPM.
            Assert.AreEqual(60.0 / 128, bites[1].T - bites[0].T, 1e-6);
        }

        [Test]
        public void WithNoBeatATouchingMobBitesEveryHalfSecond()
        {
            var sim = Sim(null, tweak: Immortal);
            sim.Horde.Spawn(Kind("skeleton"), 0.85, 0);
            sim.StepTo(6);
            var bites = Drive.Events(sim, SimEventKind.HeroHit).Select(e => e.T).ToList();
            Assert.Greater(bites.Count, 10);
            for (int i = 1; i < bites.Count; i++) Assert.AreEqual(0.5, bites[i] - bites[i - 1], Tick / 2);
            Assert.AreEqual(1e9 - 5 * bites.Count, sim.State.Hero.Health, 1e-3, "each bite hurts: the half second after a hit has passed");
        }

        [Test]
        public void AnIdleHeroInTheHordeDiesWithinNinetySeconds()
        {
            // With Sword Slash swinging, as at the start of a run.
            var sim = Sim(new FixtureSong().Beats(0, 120, 128), level: true, attacks: true);
            sim.StepTo(90);
            Assert.AreEqual(RunEnd.Death, sim.State.End, $"health {sim.State.Hero.Health}");
            Assert.Greater(sim.Arsenal.Kills, 0);
        }

        [Test]
        public void TheHerosBlowsKillTheHordeAndTheHordeForgetsItsDead()
        {
            var sim = Sim(new FixtureSong().Beats(0, 120, 128), level: true, attacks: true, tweak: Immortal);
            Run(sim, 40, s =>
            {
                int owned = 0;
                for (int id = 0; id < s.Enemies.Count; id++)
                    if (s.Horde.Owns(id)) owned++;
                Assert.AreEqual(owned, s.Horde.Alive);
            });
            var kills = Drive.Events(sim, SimEventKind.Kill);
            Assert.Greater(kills.Count, 20);
            Assert.AreEqual(sim.Horde.Spawned - kills.Count - Drive.Events(sim, SimEventKind.EnemyGone).Count, sim.Horde.Alive);
            // Slots freed by kills are spawned into again.
            Assert.Less(sim.Enemies.Count, sim.Horde.Spawned);
        }

        // ---- Elite moves ----

        static SimEvent First(Simulation sim, SimEventKind kind) => Drive.Events(sim, kind).First();

        [Test]
        public void TheBigOrksChargeLineGrowsOverTwoBeatsAndChargesOnTheNext()
        {
            var song = new FixtureSong().Beats(0, 30, 128);
            var beats = song.BeatTimes();
            var sim = Sim(song, tweak: Immortal);
            sim.StepTo(4);
            int ork = sim.Horde.Spawn(Kind("bigOrk"), 6, 0);
            sim.StepTo(8);
            var tel = First(sim, SimEventKind.EliteTelegraph);
            Assert.AreEqual(ork, tel.I0);
            Assert.AreEqual((int)EliteMove.Charge, tel.I1);
            int start = beats.FindIndex(b => Math.Abs(b - tel.D0) < 1e-9);
            Assert.GreaterOrEqual(start, 0, "the line starts on a beat");
            Assert.AreEqual(start + 2, tel.I2, "and lands two beats later");
            Assert.AreEqual(beats[start + 2], tel.D1, 1e-9);
            Assert.AreEqual(-1, tel.D4, 1e-9, "aimed at the hero");
            var move = First(sim, SimEventKind.EliteMove);
            Assert.AreEqual(beats[start + 2], move.T, 1e-9, "the charge lands on its beat");
            Assert.AreEqual(6 - 8, move.D2, 1e-9, "8 m along the line");
            var hit = Drive.Events(sim, SimEventKind.HeroHit).First(e => (HitSource)e.I1 == HitSource.Charge);
            Assert.AreEqual(beats[start + 2], hit.T, 1e-9);
            Assert.AreEqual(20, hit.D0);
        }

        [Test]
        public void TheTrollsRingFillsOverTwoBeatsAndSlamsOnTheNext()
        {
            var song = new FixtureSong().Beats(0, 30, 128);
            var beats = song.BeatTimes();
            var sim = Sim(song, tweak: Immortal);
            sim.StepTo(4);
            sim.Horde.Spawn(Kind("troll"), 2.5, 0);
            sim.StepTo(8);
            var tel = First(sim, SimEventKind.EliteTelegraph);
            Assert.AreEqual((int)EliteMove.Slam, tel.I1);
            int start = beats.FindIndex(b => Math.Abs(b - tel.D0) < 1e-9);
            Assert.GreaterOrEqual(start, 0);
            Assert.AreEqual(beats[start + 2], tel.D1, 1e-9);
            var hit = Drive.Events(sim, SimEventKind.HeroHit).First(e => (HitSource)e.I1 == HitSource.Slam);
            Assert.AreEqual(beats[start + 2], hit.T, 1e-9);
            Assert.AreEqual(20, hit.D0);
        }

        [Test]
        public void WithNoBeatTelegraphsTakeHalfASecondABeat()
        {
            foreach (var (id, x, beatsLong) in new[] { ("bigOrk", 6.0, 2), ("troll", 2.5, 2), ("mushroom", 10.0, 1) })
            {
                var sim = Sim(null, tweak: Immortal);
                sim.StepTo(1);
                sim.Horde.Spawn(Kind(id), x, 0);
                sim.StepTo(5);
                var tel = First(sim, SimEventKind.EliteTelegraph);
                Assert.AreEqual(-1, tel.I2, id);
                Assert.AreEqual(tel.T, tel.D0, 1e-9, $"{id}: it starts at once");
                Assert.AreEqual(0.5 * beatsLong, tel.D1 - tel.D0, 1e-9, id);
                Assert.AreEqual(tel.D1, First(sim, SimEventKind.EliteMove).T, 1e-9, id);
            }
            // A gliding troll that closes in later slams too.
            var later = Sim(null, tweak: Immortal);
            later.Horde.Spawn(Kind("troll"), 8, 0);
            later.StepTo(10);
            Assert.Greater(Drive.Events(later, SimEventKind.HeroHit).Count(e => (HitSource)e.I1 == HitSource.Slam), 0, "and catches the hero standing in its ring");
        }

        [Test]
        public void TheMushroomFlashesABeatBeforeItsSporeAndFiresEveryFewBars()
        {
            var song = new FixtureSong().Beats(0, 40, 128);
            var beats = song.BeatTimes();
            var sim = Sim(song, tweak: Immortal);
            sim.StepTo(4);
            int mushroom = sim.Horde.Spawn(Kind("mushroom"), 10, 0);
            sim.StepTo(20);
            var tels = Drive.Events(sim, SimEventKind.EliteTelegraph);
            var spores = Drive.Events(sim, SimEventKind.Spore);
            Assert.GreaterOrEqual(spores.Count, 2);
            int start = beats.FindIndex(b => Math.Abs(b - tels[0].D0) < 1e-9);
            Assert.GreaterOrEqual(start, 0, "the cap flashes on a beat");
            Assert.AreEqual(beats[start + 1], spores[0].T, 1e-9, "and fires on the next");
            int fired = beats.FindIndex(b => Math.Abs(b - spores[1].T) < 1e-9);
            Assert.AreEqual(start + 1 + 4 * Content.Enemies.Spores.EveryBars, fired, "every few bars");
            Assert.AreEqual(7, Math.Sqrt(spores[0].D2 * spores[0].D2 + spores[0].D3 * spores[0].D3), 1e-9);
            Assert.AreEqual(14, spores[0].D4);
            // It hits the idle hero once it has flown to it, and goes.
            var hit = Drive.Events(sim, SimEventKind.HeroHit).First(e => (HitSource)e.I1 == HitSource.Spore);
            double flight = (spores[0].D0 - 0.3 - 0.4) / 7;
            Assert.AreEqual(spores[0].T + flight, hit.T, Tick + 1e-9);
            Assert.AreEqual(Content.Enemies.Spores.Damage, hit.D0);
            Assert.AreEqual((int)SporeEnd.Hit, First(sim, SimEventKind.SporeEnd).I1);
            // A ranged mob keeps its distance.
            Assert.Greater(sim.Enemies[mushroom].X, 7.5);
        }

        [Test]
        public void ASporeFliesItsRangeAndAHeroWhoWalksAsideIsMissed()
        {
            var sim = Sim(null, tweak: Immortal);
            sim.StepTo(1);
            sim.Horde.Spawn(Kind("mushroom"), 13, 0);
            // Fired at 1.5 s toward the hero, who walks up out of its way.
            sim.StepTo(1.55);
            sim.Press(Key.Up, 1.55);
            sim.StepTo(5);
            var end = First(sim, SimEventKind.SporeEnd);
            Assert.AreEqual((int)SporeEnd.Range, end.I1);
            Assert.AreEqual(First(sim, SimEventKind.Spore).D0 - 14, end.D0, 1e-9);
            Assert.IsFalse(Drive.Events(sim, SimEventKind.HeroHit).Any(e => (HitSource)e.I1 == HitSource.Spore));
        }

        [Test]
        public void WhileTheWorldIsHeldNothingMovesOrAttacks()
        {
            var song = new FixtureSong().Beats(0, 30, 128);
            var sim = Sim(song, tweak: Immortal);
            sim.StepTo(4);
            int ork = sim.Horde.Spawn(Kind("bigOrk"), 6, 0);
            int skeleton = sim.Horde.Spawn(Kind("skeleton"), 0, 10);
            sim.StepTo(4.6);
            Assert.AreEqual(1, Drive.Events(sim, SimEventKind.EliteTelegraph).Count);
            sim.Hold(HoldReason.Cards, true);
            var frozen = (sim.Enemies[skeleton].X, sim.Enemies[skeleton].Y);
            int hits = Drive.Events(sim, SimEventKind.HeroHit).Count;
            sim.StepTo(8);
            Assert.AreEqual(frozen, (sim.Enemies[skeleton].X, sim.Enemies[skeleton].Y));
            Assert.AreEqual(hits, Drive.Events(sim, SimEventKind.HeroHit).Count);
            var calledOff = First(sim, SimEventKind.EliteMove);
            Assert.AreEqual(1, calledOff.I2, "the charge was called off");
            sim.Hold(HoldReason.Cards, false);
            sim.StepTo(10);
            Assert.Less(sim.Enemies[skeleton].Y, frozen.Item2, "and moves again after the pick");
            Assert.AreEqual(ork, Drive.Events(sim, SimEventKind.EliteTelegraph).Last().I0);
        }

        // ---- Spawning ----

        [Test]
        public void TheRampGoesFrom150To600ByMinute12AndToughnessRampsWithIt()
        {
            var sim = Sim(null, level: true, tweak: Immortal);
            var spawner = sim.Horde.Spawner;
            Assert.AreEqual(150, spawner.TargetAt(0, 0, 0), 1e-9);
            Assert.AreEqual(375, spawner.TargetAt(360, 0, 0), 1e-9);
            Assert.AreEqual(600, spawner.TargetAt(720, 0, 0), 1e-9);
            Assert.AreEqual(600, spawner.TargetAt(800, 0, 0), 1e-9);
            // Unupgraded Sword Slash (1x) kills fodder in one hit at the start and needs four by minute 12.
            Assert.AreEqual(1, spawner.HealthAt(Kind("skeleton"), 0), 1e-9);
            Assert.AreEqual(4, Math.Ceiling(spawner.HealthAt(Kind("goblin"), 720)));
            Assert.AreEqual(8, spawner.HealthAt(Kind("bigOrk"), 0) / spawner.HealthAt(Kind("skeleton"), 0), 1e-9, "elites take 8x");
            // With no music, the ramp alone.
            sim.StepTo(20);
            Assert.AreEqual(0, spawner.LoudShare);
            Assert.AreEqual(0, spawner.QuietShare);
            Assert.AreEqual(spawner.TargetAt(sim.State.RunClock, 0, 0), spawner.Target, 1e-9);
            Assert.AreEqual(Math.Floor(spawner.Target), sim.Horde.Alive, 1.0);
            var spawns = Drive.Events(sim, SimEventKind.EnemySpawn);
            Assert.IsTrue(spawns.All(e => e.I1 == Kind("skeleton") + 1 || e.I1 == Kind("slime") + 1), "skeletons and slimes first");
            Assert.IsTrue(spawns.All(e => e.D3 == 1), "at one hit each");
            // From just off-screen.
            foreach (var e in spawns)
            {
                Assert.GreaterOrEqual(Math.Pow(e.D0 / 21, 2) + Math.Pow(e.D1 / 14, 2), 1 - 1e-9);
                Assert.LessOrEqual(Math.Pow(e.D0 / 24, 2) + Math.Pow(e.D1 / 17, 2), 1 + 1e-9);
            }
        }

        [Test]
        public void SurvivorPacksKeepTheirTypeAndSpaceWithoutSpendingExtraSpawnBudget()
        {
            var content = GameContent.Load(Checkout.In("Content"));
            var config = new RunInputs { Seed = 31, Level = true, SurvivorWeapons = true, Start = RunStart.Immediately }.Build(content);
            config.Hero.Health = 1e9;
            config.Level.Mix = new[] {
                new MixEntry { Type = content.Enemies.IndexOf("skeleton"), Weight = 1 },
                new MixEntry { Type = content.Enemies.IndexOf("goblin"), Weight = 1 },
            };
            var sim = new Simulation(config);
            sim.StepTo(2);
            var spawns = Drive.Events(sim, SimEventKind.EnemySpawn);
            Assert.That(spawns.Count, Is.InRange(110, 120), "60 bodies/s, no instant free pack");
            Assert.That(spawns.Select(e => e.I1).Distinct().Count(), Is.EqualTo(2), "retain varied groups");
            int switches = 0;
            for (int i = 1; i < spawns.Count; i++)
            {
                if (spawns[i].I1 != spawns[i - 1].I1) { switches++; continue; }
                if (i % 8 == 0) continue; // two adjacent packs may happen to share a type
                double dx = spawns[i].D0 - spawns[i - 1].D0, dy = spawns[i].D1 - spawns[i - 1].D1;
                Assert.That(Math.Sqrt(dx * dx + dy * dy), Is.LessThan(11), "a pack occupies a local arc");
            }
            Assert.That(switches, Is.LessThan(20), "coherent packs, not one random type per body");
            Assert.That(sim.Horde.Alive, Is.LessThanOrEqualTo(config.Level.Cap));
        }

        [Test]
        public void GroupingFodderDoesNotDiluteEliteContactThreats()
        {
            var content = GameContent.Load(Checkout.In("Content"));
            var config = new RunInputs { Seed = 31, Level = true, SurvivorWeapons = true, Start = RunStart.Immediately }.Build(content);
            config.Hero.Health = 1e9;
            int fodder = content.Enemies.IndexOf("skeleton"), elite = content.Enemies.IndexOf("bigOrk");
            config.Level.Ramp = new Curve((0, 1000));
            config.Level.Mix = new[] { new MixEntry { Type = fodder, Weight = 8 }, new MixEntry { Type = elite, Weight = 1 } };
            var sim = new Simulation(config);
            sim.StepTo(18);
            var spawns = Drive.Events(sim, SimEventKind.EnemySpawn);
            double eliteShare = (double)spawns.Count(e => e.I1 == elite + 1) / spawns.Count;
            Assert.That(eliteShare, Is.InRange(0.07, 0.16), "retain the authored 1 in 9 mix; batching must not divide elite pressure by eight");
        }

        static List<StreamEntry> Ticks(double from, double to, LoudnessLevel level, double energy = 0.8)
        {
            var list = new List<StreamEntry>();
            for (double t = from; t < to - 1e-9; t += 0.05)
                list.Add(StreamEntry.Tick(t, FixtureSong.StampOf(t + 0.05), 0.1, energy, level: level));
            return list;
        }

        [Test]
        public void LoudStretchesSurgeTowardTheCapAndNeverPastIt()
        {
            // A loud minute raises the start's 150 by the surge's share of it.
            var sim = Sim(null, level: true, tweak: Immortal, entries: Ticks(0, 60, LoudnessLevel.Loud));
            int most = 0;
            Run(sim, 40, s => most = Math.Max(most, s.Horde.Alive));
            Assert.Greater(sim.Horde.Spawner.LoudShare, 0.99);
            double surge = 1 + Content.Level.SurgeStrength;
            Assert.AreEqual(150 * surge, sim.Horde.Spawner.TargetAt(0, 1, 0), 1e-9);
            Assert.AreEqual(600 * surge, sim.Horde.Spawner.TargetAt(720, 1, 0), 1e-9);
            Assert.Greater(most, 150 * surge - 20);
            Assert.LessOrEqual(most, sim.Horde.Spawner.TargetAt(40, 1, 0) + 1);
            // Pushed all the way, with waves on every section change: the cap holds.
            var song = new FixtureSong().Beats(0, 120, 128);
            sim = Sim(song, level: true, entries: Ticks(0, 120, LoudnessLevel.Loud), tweak: c =>
            {
                Immortal(c);
                c.Level.SurgeStrength = 10;
                c.Level.WaveMinGapS = 5;
                c.Level.PerS = 200;
            });
            most = 0;
            Run(sim, 60, s => most = Math.Max(most, s.Horde.Alive));
            Assert.LessOrEqual(most, 1000);
            Assert.Greater(most, 990);
            Assert.Greater(sim.Horde.Spawner.Waves, 0);
        }

        [Test]
        public void QuietStretchesThinTheHorde()
        {
            // A loud stretch fills the horde; a quiet one follows while the hero walks on, leaving mobs off-screen.
            var entries = Ticks(0, 20, LoudnessLevel.Loud).Concat(Ticks(20, 60, LoudnessLevel.Quiet));
            var sim = Sim(null, level: true, tweak: Immortal, entries: entries);
            sim.StepTo(20);
            int loud = sim.Horde.Alive;
            sim.Press(Key.Right, 20);
            sim.StepTo(40);
            Assert.Greater(sim.Horde.Spawner.QuietShare, 0.99);
            Assert.Less(sim.Horde.Spawner.Target, 0.65 * sim.Horde.Spawner.TargetAt(40, 0, 0), "quiet: down toward 60% of the ramp");
            Assert.Greater(loud, 2 * sim.Horde.Spawner.Target);
            Assert.AreEqual(Math.Floor(sim.Horde.Spawner.Target), sim.Horde.Alive, 2, "thinned off-screen to the target");
            Assert.Greater(Drive.Events(sim, SimEventKind.EnemyGone).Count, 0);
            // Silence is no music: the ramp alone.
            sim = Sim(null, level: true, tweak: Immortal, entries: Ticks(0, 60, LoudnessLevel.Quiet, energy: 0));
            sim.StepTo(30);
            Assert.AreEqual(0, sim.Horde.Spawner.QuietShare);
        }

        [Test]
        public void FormedWavesEnterOnTheDownbeatAfterASectionChange()
        {
            var song = new FixtureSong().Beats(0, 120, 128);
            var sim = Sim(song, level: true, tweak: Immortal);
            sim.StepTo(100);
            var waves = Drive.Events(sim, SimEventKind.HordeWave);
            Assert.GreaterOrEqual(waves.Count, 2);
            var downbeats = Drive.Events(sim, SimEventKind.Beat).Where(e => e.Downbeat).Select(e => e.T).ToList();
            foreach (var w in waves)
            {
                Assert.IsTrue(downbeats.Any(d => d <= w.T + 1e-9 && w.T - d < Tick + 1e-9), $"the wave at {w.T} enters on a downbeat");
                Assert.Greater(w.I1, 0);
            }
            // The fixture's changes come every 32 beats (15 s); waves keep 20 s apart.
            for (int i = 1; i < waves.Count; i++) Assert.GreaterOrEqual(waves[i].T - waves[i - 1].T, 20 - 1e-9);
        }

        [Test]
        public void TheBossesStopAndResumeTheSpawnerWithTheSecondHalfsMix()
        {
            var sim = Sim(null, level: true, tweak: Immortal);
            sim.StepTo(10);
            sim.Horde.Spawner.Stop();
            int spawned = sim.Horde.Spawned;
            sim.StepTo(20);
            Assert.AreEqual(spawned, sim.Horde.Spawned);
            sim.Horde.Spawner.Resume(secondHalf: true);
            Assert.AreEqual(150 * 1.15, sim.Horde.Spawner.TargetAt(0, 0, 0), 1e-9, "denser");
            sim.StepTo(300);
            var kinds = Drive.Events(sim, SimEventKind.EnemySpawn).Skip(spawned).Select(e => e.I1 - 1).Distinct().ToList();
            Assert.Contains(Kind("mushroom"), kinds, "mushrooms join");
        }

        [Test]
        public void MobsLeftFarBehindGoAndTheRingBringsOthers()
        {
            var sim = Sim(null, level: true, tweak: Immortal);
            sim.StepTo(5);
            int left = sim.Horde.Spawn(Kind("skeleton"), 200, 0);
            sim.StepTo(6);
            var gone = Drive.Events(sim, SimEventKind.EnemyGone);
            Assert.AreEqual(1, gone.Count(e => e.I0 == left));
            Assert.IsFalse(sim.Horde.Owns(left) && sim.Enemies[left].X > 100);
        }

        [Test]
        public void TheSameSeedAndStreamGiveTheSameHorde()
        {
            var song = new FixtureSong().Beats(0, 60, 128);
            var a = Sim(song, level: true);
            var b = Sim(song, level: true);
            a.StepTo(45);
            b.StepTo(45);
            Assert.AreEqual(a.Hash, b.Hash);
            Assert.AreEqual(a.EventHash, b.EventHash);
            var c = Sim(song, level: true, tweak: cfg => cfg.Seed = 99);
            c.StepTo(45);
            Assert.AreNotEqual(a.EventHash, c.EventHash);
        }

        // ---- The frame budget ----

        [Test]
        public void TheSimAtAThousandAliveFitsInAMillisecondAFrame()
        {
            var sim = Sim(new FixtureSong().Beats(0, 60, 128), level: true, tweak: c =>
            {
                Immortal(c);
                c.Level.Ramp = new Curve((0, 1000));
                c.Level.PerS = 5000;
            });
            sim.StepTo(3);
            Assert.AreEqual(1000, sim.Horde.Alive);
            var frames = new List<double>();
            var clock = new Stopwatch();
            for (double t = 3; t < 23; t += 1.0 / 60)
            {
                clock.Restart();
                sim.StepTo(t + 1.0 / 60);
                frames.Add(clock.Elapsed.TotalMilliseconds);
            }
            Assert.AreEqual(1000, sim.Horde.Alive);
            frames.Sort();
            double mean = frames.Average(), p50 = frames[frames.Count / 2], p99 = frames[(int)(frames.Count * 0.99)];
            TestContext.Progress.WriteLine($"the sim at 1,000 alive, per 60 fps frame: mean {mean:F3} ms, p50 {p50:F3} ms, p99 {p99:F3} ms, max {frames[frames.Count - 1]:F3} ms");
            // The budget is 1 ms a frame on the M5 Max (plan.md, "Performance"); the median holds it on a loaded runner too.
            Assert.Less(p50, 1.0);
            Assert.Less(mean, 1.0);
        }
    }
}
