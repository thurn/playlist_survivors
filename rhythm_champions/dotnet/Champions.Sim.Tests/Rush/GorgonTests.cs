using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;

namespace Champions.Sim.Tests
{
    /// <summary>
    /// Boss Rush boss 8, the Gorgon (ps-8z4.21; boss-rush.md, The roster: Line of sight): fair at the slowest and fastest
    /// tempos in her temple, the gaze taught alone first and striking on a downbeat everything in her line of sight but what
    /// stands in a column's shadow, the stone cracking and crumbling under it, the volley and coil, the stone ring as her
    /// dash wave, phase two's statues, the beatless fallback, determinism, her arena and her slot.
    /// </summary>
    public class GorgonTests
    {
        static GameContent Content => Fairness.Content;
        static ArenaDef Temple => Content.Arenas.Find(GorgonModule.ArenaId);
        static int SlotHealth => (int)Content.BossRush.Bosses.First(b => b.Id == GorgonModule.ModuleId).Health;

        /// <summary>Where the hero may stand in her temple (from its centre): inside the wall, off the props.</summary>
        static bool InTemple(double x, double y)
        {
            var def = Temple;
            if (!def.Outline.Contains(x, y, 0, 0) || def.Outline.DistanceToOutline(x, y) < 0.4) return false;
            foreach (var p in def.Props)
            {
                double r = Content.Arenas.Props[p.Kind].Bound + 0.4;
                if ((x - p.X) * (x - p.X) + (y - p.Y) * (y - p.Y) < r * r) return false;
            }
            return true;
        }

        static FairnessOptions Options(double bpm) => new FairnessOptions
        {
            Bpm = bpm, Act = 2, FightS = 140, Arena = GorgonModule.ArenaId, Inside = InTemple, ArenaRadiusM = GorgonModule.TempleM,
            ExtentM = GorgonModule.TempleM + 1,
            // The camera holds the whole temple still (TheCameraHoldsHerTempleWhole), so nothing in it is off screen.
            ScreenRadiusM = 2 * GorgonModule.TempleM,
            Setup = new FightSetup { Health = SlotHealth, Act = 2 },
        };

        static Simulation Sim(double bpm = 120, double songS = 240, bool beats = true, bool arena = false)
        {
            var config = new RunInputs { Loadout = new Upgrade[0] }.Build(Content);
            config.Hero.Health = 1e9;
            if (arena) config.Arenas = Content.Arenas.Clone();
            var sim = new Simulation(config);
            if (beats)
                foreach (var k in Drive.Known(new FixtureSong().Beats(0, songS, bpm).Entries())) sim.AddKnown(k.Entry, k.KnownAt);
            sim.StepTo(1);
            if (arena) sim.Arena.FormAroundHero(Temple, sim.Now);
            return sim;
        }

        static void Strike(Simulation sim, double damage)
        {
            ref readonly var b = ref sim.Fight.Bodies[0];
            sim.Strikes.Land(sim, new Blow
            {
                Shape = HitShape.Circle(b.X, b.Y, 0.1), T = sim.Now, Damage = damage, Tags = HitTags.Strike,
                Source = BlowSource.SwordSlash, Owner = -1, Part = -1,
            });
        }

        static void Until(Simulation sim, Func<bool> done, double maxS = 60, Action each = null)
        {
            double stop = sim.Now + maxS;
            while (!done() && sim.Now < stop)
            {
                sim.StepTo(sim.Now + 1.0 / 60);
                each?.Invoke();
            }
            Assert.IsTrue(done(), "timed out");
        }

        /// <summary>A hero walking a slow loop round the temple, retargeted every half second.</summary>
        static Action Wander(Simulation sim, GorgonModule m)
        {
            int s = 0;
            return () =>
            {
                if (++s % 30 != 0) return;
                sim.Target(m.OriginX + 9 * Math.Cos(s / 90.0), m.OriginY + 9 * Math.Sin(s / 90.0), sim.Now);
            };
        }

        /// <summary>Its moves in order, as the fight made them, until <paramref name="count"/> or the time runs out.</summary>
        static List<GorgonModule.Kind> Moves(Simulation sim, GorgonModule m, int count, double maxS = 180, Action each = null)
        {
            var moves = new List<GorgonModule.Kind>();
            int seen = m.Moves;
            double stop = sim.Now + maxS;
            while (moves.Count < count && sim.Now < stop)
            {
                sim.StepTo(sim.Now + 1.0 / 60);
                each?.Invoke();
                if (m.Moves > seen)
                {
                    seen = m.Moves;
                    moves.Add(m.Last);
                }
            }
            return moves;
        }

        static int Hurts(Simulation sim, double from) =>
            Drive.Events(sim, SimEventKind.HeroHit).Count(e => e.T > from && e.I0 == (int)HitOutcome.Hurt);

        /// <summary>The hazards of her latest gaze, laid now.</summary>
        static Hazard[] Gaze(Simulation sim, GorgonModule m) => sim.Fight.Hazards.ToArray().Where(h => h.Attack == m.GazeAttack).ToArray();

        /// <summary>Does any of a gaze's slices reach a hero standing at (x, y)?</summary>
        static bool Lit(Hazard[] gaze, double x, double y) => gaze.Any(h => h.Shape.Touches(x, y, Content.Hero.RadiusM));

        [TestCase(90)]
        [TestCase(180)]
        public void TheGorgonIsFairInHerTemple(double bpm)
        {
            var report = Fairness.Check(() => new GorgonModule(), Options(bpm));
            Fairness.AssertFair(report);
            Assert.IsTrue(report.Finished, report.ToString());
            Assert.That(report.DashWavesNeedingDash, Is.GreaterThan(0), "its stone ring is a real dash wave");
        }

        [Test]
        public void ItTeachesTheGazeAloneThenEveryMoveInTurn()
        {
            var sim = Sim();
            var m = new GorgonModule();
            sim.Fight.Start(m, sim.Now, new FightSetup { Health = 1e6, Act = 2 });
            // Far from her, so the volleys stay volleys.
            var moves = Moves(sim, m, 2 + 6, each: () => sim.Target(m.OriginX, m.OriginY - 14, sim.Now));
            CollectionAssert.AreEqual(GorgonModule.Teach, moves.Take(GorgonModule.Teach.Length).ToArray(), "the gaze alone, twice, first");
            CollectionAssert.AreEqual(GorgonModule.PhaseOne, moves.Skip(2).ToArray(), "then its moves in turn");
            var tells = Drive.Events(sim, SimEventKind.BossTelegraph).ToList();
            Assert.That(tells.Select(e => e.MarkMove).Distinct(), Is.SupersetOf(new[] { BossMove.Breath, BossMove.Boulders, BossMove.Wave }));
        }

        [Test]
        public void HerGazeStrikesOnADownbeatEverythingInSightButTheColumnsShadows()
        {
            var sim = Sim(arena: true);
            var m = new GorgonModule();
            sim.Fight.Start(m, sim.Now, new FightSetup { Health = 1e6, Act = 2 });
            Until(sim, () => m.GazeAttack >= 0);
            var gaze = Gaze(sim, m);
            Assert.That(gaze.Length, Is.GreaterThan(2), "a cone cut into lit slices");
            Assert.That(gaze.All(h => h.Move == BossMove.Breath && h.Shape.Kind == ShapeKind.Cone));
            Assert.AreEqual(1, gaze.Count(h => (h.Flags & HazardFlags.Signature) != 0), "one signature landing");
            var tel = gaze[0].Telegraph;
            Assert.That(gaze.All(h => h.Telegraph.Land == tel.Land), "one stare");
            Assert.GreaterOrEqual(tel.Land - tel.Start, Math.Max(GorgonModule.GazeBeats * 0.5, GorgonModule.GazeMinS) - 1e-6, "a bar at least");
            // From the temple's heart, at the hero's start: the inner column straight between them shades the hero.
            ref readonly var b = ref sim.Fight.Bodies[0];
            Assert.AreEqual((m.OriginX, m.OriginY), (b.X, b.Y), "she stares from the heart first");
            double hx = sim.Arena.HeroStartX, hy = sim.Arena.HeroStartY;
            Assert.IsFalse(Lit(gaze, hx, hy), "behind the column, out of her sight");
            Assert.IsTrue(Lit(gaze, hx + 3, hy), "beside it, in her sight");
            Assert.IsTrue(Lit(gaze, m.OriginX, m.OriginY - 4.5), "in front of it too");
            // The slices tile her cone without overlapping.
            var spans = gaze.Select(h => (Mid: Math.Atan2(h.Shape.DirY, h.Shape.DirX), h.Shape.HalfArc)).OrderBy(s => s.Mid).ToList();
            double total = spans.Sum(s => 2 * s.HalfArc);
            Assert.AreEqual(2 * GorgonModule.GazeHalfDeg * Math.PI / 180, total, 1e-6, "the whole cone, in slices");
            Assert.That(gaze.All(h => h.Shape.Radius <= GorgonModule.TempleM + 0.5 + 1e-6), "out to the temple's wall, no farther");
            var downbeats = Drive.Events(sim, SimEventKind.Beat).Where(e => e.Downbeat).Select(e => e.T).ToList();
            Until(sim, () => sim.Now > tel.Land + 0.05);
            downbeats = Drive.Events(sim, SimEventKind.Beat).Where(e => e.Downbeat).Select(e => e.T).ToList();
            Assert.IsTrue(downbeats.Any(t => Math.Abs(t - tel.Land) < 1e-6), "it strikes on a downbeat");
        }

        [Test]
        public void HidingBehindAColumnIsSafeAndStandingInSightIsNot()
        {
            int During(bool hide, bool late = false)
            {
                var sim = Sim(arena: true);
                var m = new GorgonModule();
                sim.Fight.Start(m, sim.Now, new FightSetup { Health = 1e6, Act = 2 });
                Until(sim, () => m.GazeAttack >= 0);
                var stare = Gaze(sim, m)[0];
                double from = sim.Now;
                // The first gaze: stay behind the column at the start, or step out beside it (before it strikes, or late).
                if (late) Until(sim, () => sim.Now >= stare.Telegraph.Land + 0.02);
                if (!hide) sim.Target(sim.Arena.HeroStartX + 3, sim.Arena.HeroStartY, sim.Now);
                Until(sim, () => sim.Now > stare.Until + 0.1);
                return Hurts(sim, from);
            }
            Assert.AreEqual(0, During(hide: true), "the column's shadow holds the hero");
            Assert.AreEqual(1, During(hide: false), "and her stare finds the hero beside it");
            Assert.AreEqual(1, During(hide: false, late: true), "and holds a beat: stepping out of cover as it strikes is too soon");
        }

        [Test]
        public void TheStoneTheHeroHidBehindCracksThenCrumbles()
        {
            var sim = Sim(arena: true);
            var m = new GorgonModule();
            sim.Fight.Start(m, sim.Now, new FightSetup { Health = 1e6, Act = 2 });
            // The column between the heart and the hero's start.
            int column = Enumerable.Range(0, sim.Arena.PropCount).Single(i =>
                Math.Abs(sim.Arena.Prop(i).X - sim.Arena.OriginX) < 0.1 && Math.Abs(sim.Arena.Prop(i).Y - (sim.Arena.OriginY - 6.5)) < 0.1);
            Until(sim, () => m.GazeAttack >= 0);
            var land = Gaze(sim, m)[0].Telegraph.Land;
            Until(sim, () => sim.Now > land + 0.05);
            Assert.AreEqual(1, m.Cracks(column), "petrified by the first stare");
            Assert.IsTrue(sim.Arena.Prop(column).Standing, "still cover");
            int others = Enumerable.Range(0, sim.Arena.PropCount).Count(i => i != column && m.Cracks(i) > 0);
            Assert.AreEqual(0, others, "only the stone that stopped the stare");
            // The next gaze comes from a perch; the one after from the heart again: back behind the same column, it crumbles.
            double hx = sim.Arena.HeroStartX, hy = sim.Arena.HeroStartY;
            int stares = 1;
            int seen = m.GazeAttack;
            Hazard[] third = null;
            Until(sim, () => third != null, 60, () =>
            {
                if (m.GazeAttack == seen) return;
                seen = m.GazeAttack;
                if (++stares == 3) third = Gaze(sim, m);
            });
            Assert.AreEqual((m.OriginX, m.OriginY), (m.GazeX, m.GazeY), "from the heart");
            Assert.IsFalse(Lit(third, hx, hy), "the cracked column still shades its spot");
            sim.Target(hx, hy, sim.Now);
            Until(sim, () => sim.Now > third[0].Telegraph.Land + 0.05);
            Assert.AreEqual(2, m.Cracks(column));
            Assert.IsTrue(sim.Arena.Prop(column).Standing, "cracked through, it stands while her stare holds");
            Until(sim, () => sim.Now > third[0].Until + 0.05);
            Assert.IsFalse(sim.Arena.Prop(column).Standing, "crumbled on the second");
            var crumble = Drive.Events(sim, SimEventKind.ArenaProp).Last(e => e.I0 == column);
            Assert.AreEqual(third[0].Until, crumble.T, 1e-9, "as the stare ends, on the beat after its downbeat");
        }

        [Test]
        public void SheGlidesBetweenTheHeartAndHerPerchesBeforeEachGaze()
        {
            var sim = Sim(arena: true);
            var m = new GorgonModule();
            sim.Fight.Start(m, sim.Now, new FightSetup { Health = 1e6, Act = 2 });
            var spots = new List<(double X, double Y)>();
            int last = -1;
            Until(sim, () => spots.Count >= 4, 120, () =>
            {
                sim.Target(m.OriginX, m.OriginY - 14, sim.Now);
                if (m.GazeAttack != last && m.GazeAttack >= 0)
                {
                    last = m.GazeAttack;
                    spots.Add((m.GazeX - m.OriginX, m.GazeY - m.OriginY));
                }
            });
            Assert.AreEqual((0.0, 0.0), (Math.Round(spots[0].X, 6), Math.Round(spots[0].Y, 6)), "the heart");
            Assert.AreEqual(GorgonModule.Perches[0].X, spots[1].X, 1e-6, "a perch");
            Assert.AreEqual(GorgonModule.Perches[0].Y, spots[1].Y, 1e-6);
            Assert.AreEqual(0, Math.Sqrt(spots[2].X * spots[2].X + spots[2].Y * spots[2].Y), 1e-6, "the heart again");
            Assert.AreEqual(GorgonModule.Perches[1].X, spots[3].X, 1e-6, "the next perch");
            // Her glides cross no column: every perch is a clear walk from the heart for her body.
            var def = Temple;
            double r = Content.Enemies.Cast[Content.Enemies.IndexOf(GorgonModule.ModuleId)].RadiusM;
            foreach (var (px, py) in GorgonModule.Perches)
                foreach (var prop in def.Props)
                {
                    double len = Math.Sqrt(px * px + py * py), u = Math.Max(0, Math.Min(len, (prop.X * px + prop.Y * py) / len));
                    double cx = px / len * u - prop.X, cy = py / len * u - prop.Y;
                    Assert.Greater(Math.Sqrt(cx * cx + cy * cy), Content.Arenas.Props[prop.Kind].Bound + r, $"{prop.Kind} at {prop.X},{prop.Y}");
                }
        }

        [Test]
        public void HerVolleyLandsOnTheHerosSpotAndHerCoilWhenTheHeroIsClose()
        {
            var sim = Sim();
            var m = new GorgonModule();
            sim.Fight.Start(m, sim.Now, new FightSetup { Health = 1e6, Act = 2 });
            Moves(sim, m, GorgonModule.Teach.Length + 1, each: () => sim.Target(m.OriginX + 2, m.OriginY - 13, sim.Now));
            Assert.AreEqual(GorgonModule.Kind.Volley, m.Last);
            var volley = sim.Fight.Hazards.ToArray();
            Assert.AreEqual(GorgonModule.VolleyCount, volley.Length);
            Assert.That(volley.All(h => h.Move == BossMove.Boulders));
            sim.HeroWas(volley[0].Telegraph.Start, out double hx, out double hy);
            Assert.IsTrue(volley.Any(h => h.Shape.Touches(hx, hy, 0)), "on the hero's spot");
            // A hero at her side gets her tail instead.
            var close = Sim();
            var n = new GorgonModule();
            close.Fight.Start(n, close.Now, new FightSetup { Health = 1e6, Act = 2 });
            Moves(close, n, GorgonModule.Teach.Length + 1, each: () =>
            {
                if (!close.Fight.InFight) return;
                ref readonly var b = ref close.Fight.Bodies[0];
                close.Target(b.X + 3.5, b.Y, close.Now);
            });
            Assert.AreEqual(GorgonModule.Kind.Coil, n.Last);
            var coil = close.Fight.Hazards.ToArray().Single();
            Assert.AreEqual(BossMove.Tail, coil.Move);
            Assert.AreEqual(GorgonModule.CoilRadiusM, coil.Shape.Radius, 1e-9);
        }

        [Test]
        public void HerStoneRingRollsAcrossTheWholeTempleFasterThanAnyWalk()
        {
            var sim = Sim();
            var m = new GorgonModule();
            sim.Fight.Start(m, sim.Now, new FightSetup { Health = 1e6, Act = 2 });
            Moves(sim, m, GorgonModule.Teach.Length + Array.IndexOf(GorgonModule.PhaseOne, GorgonModule.Kind.Petrify) + 1,
                each: () => sim.Target(m.OriginX, m.OriginY - 14, sim.Now));
            Assert.AreEqual(GorgonModule.Kind.Petrify, m.Last);
            var ring = sim.Fight.Hazards.ToArray().Single();
            Assert.IsTrue(ring.DashWave);
            Assert.AreEqual(BossMove.Wave, ring.Move);
            Assert.Greater(ring.GrowMps, sim.Fight.HeroSpeedMps * 2, "it outruns any walk");
            Assert.GreaterOrEqual(ring.GrowMps * ring.ActiveS, 2 * GorgonModule.TempleM, "across the whole temple");
        }

        [Test]
        public void BelowHalfHealthHerVictimsRiseAsStatuesAndHerGazeWidens()
        {
            var sim = Sim(arena: true);
            var m = new GorgonModule();
            sim.Fight.Start(m, sim.Now, new FightSetup { Health = 1000, Act = 2 });
            Until(sim, () => sim.Fight.Stage == FightStage.Fighting);
            sim.StepTo(sim.Now + 6);
            Strike(sim, 510);
            Assert.AreEqual((FightStage.Transition, 2), (sim.Fight.Stage, sim.Fight.Phase));
            double health = sim.Fight.Health, from = sim.Now;
            Strike(sim, 100);
            Assert.AreEqual(health, sim.Fight.Health, 1e-9, "untouchable while they rise");
            Assert.AreEqual(GorgonModule.StatueCount, m.RaisedStatues.Count, "her victims rise");
            Until(sim, () => sim.Fight.Stage == FightStage.Fighting);
            Assert.AreEqual(4 * 0.5, sim.Now - from, 0.1, "four beats at 120 BPM");
            Assert.AreEqual((m.OriginX, m.OriginY), (sim.Fight.Bodies[0].X, sim.Fight.Bodies[0].Y), "at the heart");
            foreach (int id in m.RaisedStatues)
            {
                var o = sim.Arena.Obstacle(id);
                Assert.IsTrue(o.Standing && o.Kind == ArenaObstacleKind.Wall);
                o.Shape.Centre(out double x, out double y);
                Assert.IsFalse(sim.Arena.Walkable(x, y, 0.1), "they stand in the way");
            }
            var moves = Moves(sim, m, 2, each: () => sim.Target(m.OriginX, m.OriginY - 14, sim.Now));
            CollectionAssert.AreEqual(new[] { GorgonModule.Kind.Gaze, GorgonModule.Kind.Volley }, moves, "a volley on the heels of each gaze");
            Until(sim, () => m.GazeAttack >= 0 && Gaze(sim, m).Length > 0, 20);
            var gaze = Gaze(sim, m);
            Assert.AreEqual(2 * GorgonModule.GazeHalfDegTwo * Math.PI / 180, gaze.Sum(h => 2 * h.Shape.HalfArc), 1e-6, "wider");
            Assert.That(m.Shadows.Any(s => s.Cover.Statue) || gaze.Length > 0);
        }

        [Test]
        public void WithoutABeatItFightsOnTheFallbackBeat()
        {
            var sim = Sim(beats: false);
            var m = new GorgonModule();
            sim.Fight.Start(m, sim.Now, new FightSetup { Health = 400, Act = 2 });
            Until(sim, () => sim.Fight.Stage == FightStage.Fighting, 30);
            double from = sim.Now;
            var wander = Wander(sim, m);
            var tells = new List<Hazard>();
            var seen = new HashSet<int>();
            while (sim.Now < from + 50)
            {
                sim.StepTo(sim.Now + 1.0 / 60);
                wander();
                if (sim.Now > from + 40 && sim.Fight.Stage == FightStage.Fighting) Strike(sim, 50);
                if (sim.Fight.InFight) foreach (var h in sim.Fight.Hazards) if (seen.Add(h.Id)) tells.Add(h);
            }
            double beat = Content.Beat.TelegraphBeatlessBeatS;
            Assert.That(m.Moves, Is.GreaterThanOrEqualTo(5), "it keeps attacking");
            Assert.That(tells.All(h => h.Telegraph.Land - h.Telegraph.Start >= 3 * beat - 1e-6), "three fallback beats of warning");
            Assert.AreEqual(FightStage.Dead, sim.Fight.Stage, "and it can be beaten");
        }

        [Test]
        public void TheSameSeedPlaysTheSameFight()
        {
            ulong Run()
            {
                var sim = Sim(128, arena: true);
                sim.Fight.Start(new GorgonModule(), sim.Now, new FightSetup { Health = 600, Act = 2 });
                for (int s = 0; s < 90; s++)
                {
                    sim.StepTo(sim.Now + 1);
                    if (s % 3 == 0 && sim.Fight.InFight && !sim.Fight.Untouchable) Strike(sim, 20);
                    sim.Target(sim.Fight.CentreX + 6 * Math.Sin(s), sim.Fight.CentreY + 8 + 5 * Math.Cos(s * 0.7), sim.Now);
                }
                return sim.Hash ^ (ulong)Drive.Events(sim, SimEventKind.BossTelegraph).Count();
            }
            Assert.AreEqual(Run(), Run());
        }

        [Test]
        public void TheCameraHoldsHerTempleWhole()
        {
            var config = new RunInputs { Loadout = new Upgrade[0] }.Build(Content);
            config.Arenas = Content.Arenas.Clone();
            var sim = new Simulation(config);
            sim.Arena.FormAroundHero(Temple, sim.Now);
            sim.StepTo(sim.Now + 3);
            var framing = new ArenaFraming(new CameraModel { PitchDeg = 60, FovDeg = 40, Aspect = 16.0 / 9, DistanceM = 23 });
            var f = framing.Frame(sim.Arena, 0, 0, new List<(double, double, double)>());
            Assert.IsFalse(f.Scrolls, "the whole temple in a still view");
            var o = Temple.Outline;
            for (int i = 0; i < o.Count; i++)
                Assert.AreEqual(GorgonModule.TempleM, Math.Sqrt(o.X[i] * o.X[i] + o.Y[i] * o.Y[i]), 0.01, "the module's geometry is its arena's");
            Assert.AreEqual((GorgonModule.HeroStartX, GorgonModule.HeroStartY), (Temple.HeroStartX, Temple.HeroStartY));
            Assert.AreEqual((GorgonModule.BossStartX, GorgonModule.BossStartY), (Temple.BossStartX, Temple.BossStartY));
            Assert.IsTrue(InTemple(Temple.HeroStartX, Temple.HeroStartY));
            // Its statues rise on open floor.
            foreach (var (x, y) in GorgonModule.Statues) Assert.IsTrue(InTemple(x, y), $"statue spot {x},{y}");
        }

        [Test]
        public void ItsSlotFightsInItsTempleArrivingAtTheBossStart()
        {
            var rules = Content.BossRush;
            int index = Enumerable.Range(0, rules.Count).Single(i => rules.Bosses[i].Id == GorgonModule.ModuleId);
            Assert.AreEqual(7, index, "boss 8");
            Assert.IsTrue(rules.Bosses[index].Fight, "landed: its module and cast row");
            Assert.AreEqual(GorgonModule.ArenaId, rules.Bosses[index].Arena);
            var config = new RunInputs { Seed = 4, BossRush = true, SurvivorWeapons = true, Loadout = new Upgrade[0] }.Build(Content);
            config.Hero.Health = 1e9;
            var sim = new Simulation(config);
            foreach (var k in Drive.Known(new FixtureSong().Beats(0, 900, 120).Entries())) sim.AddKnown(k.Entry, k.KnownAt);
            double stop = 880;
            while (!(sim.Fight.Module is GorgonModule && sim.Fight.Stage == FightStage.Fighting) && sim.Now < stop)
            {
                if (sim.State.Offer.Up) { sim.Press(Key.One, sim.Now); sim.Release(Key.One, sim.Now); }
                if (RushTestKit.Fighting(sim) && sim.BossRush.Slot.Id != GorgonModule.ModuleId) RushTestKit.Strike(sim, 1e12);
                sim.StepTo(sim.Now + 1.0 / 60);
            }
            Assert.IsTrue(sim.Fight.Module is GorgonModule, "the run reached the Gorgon");
            Assert.AreEqual(GorgonModule.ArenaId, sim.Arena.Def.Id);
            ref readonly var b = ref sim.Fight.Bodies[0];
            Assert.AreEqual(sim.Arena.BossStartX, b.X, 0.05, "it arrives at its arena's boss start");
            Assert.AreEqual(sim.Arena.BossStartY, b.Y, 0.05);
        }

        [Test]
        public void ARecordedGorgonFightReplaysToItsHash()
        {
            var entries = new FixtureSong(8).Beats(0, 40, 128, jitter: 0.003).Gap(40, 46).Beats(46, 90, 150, jitter: 0.002).Entries();
            var run = new RunInputs { Seed = 8, Fight = GorgonModule.ModuleId, Start = RunStart.Immediately, SurvivorWeapons = true };
            var live = new Simulation(run.Build(Content));
            var rng = new Random(13);
            double next = 0;
            int fed = Drive.Feed(live, entries, 90, eachFrame: s =>
            {
                if (s.Now < next) return;
                next = s.Now + 1.5;
                s.Target(rng.NextDouble() * 24 - 12, rng.NextDouble() * 24 - 12, s.Now - 0.05);
                if (rng.NextDouble() < 0.3) s.Press(Key.Space, s.Now - 0.05);
            });
            Assert.That(live.Fight.Hazards.Length + Drive.Events(live, SimEventKind.BossMove).Count, Is.GreaterThan(10), "the fight played");
            string dir = Path.Combine(Path.GetTempPath(), "champions-gorgon-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                var record = SessionRecord.Of(run, live);
                record.Write(dir);
                SessionRecord.WriteEntries(dir, entries.GetRange(0, fed));
                var read = SessionRecord.Read(dir);
                var replay = new SessionReplay(read);
                foreach (var e in SessionRecord.LoadEntries(dir)) replay.Supply(e);
                var sim = new Simulation(read.Run.Build(Content));
                Assert.IsTrue(replay.StepTo(sim, double.PositiveInfinity));
                Assert.IsTrue(replay.Matches(sim), $"replayed {sim.Hash:X16}, recorded {record.Hash:X16}");
                Assert.AreEqual(live.Hash, sim.Hash);
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }

        [Test]
        public void ItsBurstsFallInTheShadowsBehindTheColumnsNearestHer()
        {
            var sim = Sim(arena: true);
            var bursts = new List<List<GemSpot>>();
            sim.Fight.Shed += (s, f, burst, spots) => bursts.Add(spots.ToList());
            var m = new GorgonModule();
            sim.Fight.Start(m, sim.Now, new FightSetup { Health = 1000, Act = 2 });
            Until(sim, () => sim.Fight.Stage == FightStage.Fighting);
            Strike(sim, 120);
            sim.StepTo(sim.Now + 1.1);
            Assert.AreEqual(1, bursts.Count);
            Assert.AreEqual(1, bursts[0].Sum(g => g.Share), 1e-9);
            ref readonly var b = ref sim.Fight.Bodies[0];
            var covers = new List<GorgonModule.Cover>();
            for (int i = 0; i < sim.Arena.PropCount; i++)
                covers.Add(new GorgonModule.Cover { Id = i, X = sim.Arena.Prop(i).X, Y = sim.Arena.Prop(i).Y, Radius = 0.9 });
            var shadows = new List<GorgonModule.Shadow>();
            foreach (var g in bursts[0])
            {
                // Each gem is shaded from her by a column.
                double facing = Math.Atan2(g.Y - b.Y, g.X - b.X);
                GorgonModule.Sightlines(b.X, b.Y, facing, 0.01, 40, covers, shadows);
                double d = Math.Sqrt((g.X - b.X) * (g.X - b.X) + (g.Y - b.Y) * (g.Y - b.Y));
                Assert.IsTrue(shadows.Any(s => s.NearM < d), $"gem {g.X:F1},{g.Y:F1} in a shadow");
            }
        }

        /// <summary>
        /// The Gorgon against a reference build at its place in the run (<c>--boss 8</c>), the scripted <c>positioning</c>
        /// player on the 128 BPM dance playlist, the hero unkillable: the fight's length and what the hero took.
        /// </summary>
        public static (double FightS, double Taken, int Hits) ReferenceRun(string build, ulong seed)
        {
            var rules = Content.BossRush;
            int index = Enumerable.Range(0, rules.Count).Single(i => rules.Bosses[i].Id == GorgonModule.ModuleId);
            var run = new RunInputs
            {
                Seed = seed, BossRush = true, SurvivorWeapons = true, Loadout = new Upgrade[0], Start = RunStart.Immediately,
                RushStart = rules.ReferenceCheckpoint(index, Content.Combat, build),
            };
            var config = run.Build(Content);
            config.Hero.Health = 1e9;
            var sim = new Simulation(config);
            var player = ScriptedPlayer.Parse("positioning", "reference", Content.Dashes);
            var entries = LevelRunTests.Playlist(500, seed).Entries();
            int next = 0, cursor = 0;
            double frontier = 0, from = double.NaN, taken = 0;
            int hits = 0;
            const double frame = 1.0 / 60, lead = 0.05;
            while (frontier < 490)
            {
                frontier += frame;
                while (next < entries.Count && entries[next].Stamp + 0.06 <= frontier) sim.Add(entries[next++]);
                sim.StepTo(frontier);
                foreach (var e in sim.EventsSince(cursor))
                {
                    player.Take(sim, e);
                    if (e.Kind == SimEventKind.HeroHit && e.D0 > 0 && !double.IsNaN(from)) { hits++; taken += e.D0; }
                }
                cursor = sim.EventCount;
                sim.DiscardEventsBefore(cursor);
                if (sim.State.Offer.Up) { sim.Press(Key.One, sim.Now); sim.Release(Key.One, sim.Now); }
                if (double.IsNaN(from) && sim.Fight.Module is GorgonModule && sim.Fight.Stage == FightStage.Fighting) from = sim.State.RunClock;
                if (!double.IsNaN(from) && sim.Fight.Stage == FightStage.Dead) return (sim.State.RunClock - from, taken, hits);
                player.Frame(sim, frontier - lead);
            }
            Assert.Fail($"{build} seed {seed}: the Gorgon still stands (rush {sim.BossRush?.Stage} fight {sim.Fight.Module?.Id} {sim.Fight.Stage} now {sim.Now:F1} share {sim.Fight.HealthShare:F3} taken {taken:F0} hits {hits})");
            return default;
        }

        [Test, Explicit("a minute or two: the reference builds against the Gorgon into evidence/boss-rush/gorgon/reference-runs.tsv")]
        public void MeasureTheReferenceBuildsAgainstTheGorgon()
        {
            var tsv = new StringBuilder("build\tseed\tfightS\ttaken\thits\n");
            foreach (var build in new[] { "focused", "broad" })
                foreach (ulong seed in new ulong[] { 31, 32, 33 })
                {
                    var (s, taken, hits) = ReferenceRun(build, seed);
                    tsv.Append(string.Join("\t", build, seed.ToString(CultureInfo.InvariantCulture), s.ToString("F1", CultureInfo.InvariantCulture),
                        taken.ToString("F0", CultureInfo.InvariantCulture), hits.ToString(CultureInfo.InvariantCulture)) + "\n");
                }
            string dir = Checkout.In(Path.Combine("evidence", "boss-rush", "gorgon"));
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "reference-runs.tsv"), tsv.ToString());
            TestContext.Out.WriteLine(tsv.ToString());
        }
    }
}
