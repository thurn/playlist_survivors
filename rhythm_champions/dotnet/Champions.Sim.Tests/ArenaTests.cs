using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace Champions.Sim.Tests
{
    /// <summary>
    /// The Boss Rush arenas (boss-rush.md, Arenas; ps-8z4.2): the content's arenas, forming round the hero over four beats,
    /// walls and props blocking bodies and shots, floor zones raised, sunk and broken away, grown obstacles, props crumbling
    /// for the rest of the fight, retracting, and the camera framing a small arena whole and scrolling through a large one
    /// with the boss and its telegraphs in frame.
    /// </summary>
    public class ArenaTests
    {
        const double Tick = Simulation.TickSeconds;
        static readonly GameContent Content = DashTests.Content;
        static ArenaRules Rules => Content.Arenas;

        static Simulation Sim(Action<SimConfig> tweak = null, FixtureSong song = null)
        {
            var config = new RunInputs { Loadout = new Upgrade[0] }.Build(Content);
            config.Arenas = Rules.Clone();
            config.Hero.Health = 1e9;
            tweak?.Invoke(config);
            var sim = new Simulation(config);
            if (song != null)
                foreach (var k in Drive.Known(song.Entries())) sim.AddKnown(k.Entry, k.KnownAt);
            return sim;
        }

        /// <summary>A sim with <paramref name="arena"/> formed round the hero at the origin, standing.</summary>
        static Simulation Formed(string arena, Action<SimConfig> tweak = null)
        {
            var sim = Sim(tweak);
            sim.Arena.FormAroundHero(Rules.Find(arena), sim.Now);
            Step(sim, 3);
            Assert.AreEqual(ArenaPhase.Standing, sim.Arena.Phase);
            return sim;
        }

        static void Step(Simulation sim, double seconds) => sim.StepTo(sim.Now + seconds);

        static (double X, double Y) Hero(Simulation sim)
        {
            sim.HeroWas(sim.Now, out double x, out double y);
            return (x, y);
        }

        /// <summary>Walks toward (x, y) by click target for <paramref name="seconds"/>, wherever the walls let it get.</summary>
        static (double X, double Y) WalkToward(Simulation sim, double x, double y, double seconds)
        {
            sim.Target(x, y, sim.Now);
            Step(sim, seconds);
            return Hero(sim);
        }

        static double HeroRadius => Content.Hero.RadiusM;

        // ---- Content ----

        [Test]
        public void EveryArenaInTheContentReadsAndStartsTheHeroOnClearFloor()
        {
            Assert.IsNotNull(Rules.Find("standin"), "the stand-in arena fights use before their own land");
            foreach (var def in Rules.Arenas)
            {
                var sim = Sim();
                sim.Arena.FormAroundHero(def, sim.Now);
                Assert.IsTrue(sim.Arena.Walkable(0, 0, HeroRadius), $"{def.Id}: the hero stands clear where it formed");
                Assert.IsTrue(sim.Arena.Walkable(sim.Arena.BossStartX, sim.Arena.BossStartY, 1), $"{def.Id}: the boss starts clear");
            }
        }

        [Test]
        public void ContentWithAZonePastTheWallsOrAStartOnAPropIsRefused()
        {
            string Arena(string zones, string props, string heroStart = "[0, -5]") => $@"{{
                ""formBeats"": 4, ""retractBeats"": 4, ""wallThicknessM"": 0.5, ""wallHeightM"": 1, ""raiseM"": 1, ""sinkM"": 1,
                ""props"": [{{ ""id"": ""pillar"", ""radiusM"": 1, ""heightM"": 3, ""hits"": 0 }}],
                ""arenas"": [{{ ""id"": ""a"", ""outline"": {{ ""radiusM"": 10 }}, ""heroStart"": {heroStart}, ""bossStart"": [0, 5],
                    ""camera"": {{ ""maxScale"": 1.5, ""marginM"": 1 }}, ""zones"": [{zones}], ""props"": [{props}] }}] }}";
            Assert.DoesNotThrow(() => ArenaRules.Read(JsonValue.Parse(Arena("", "")), "t"));
            Assert.Throws<ContentException>(() => ArenaRules.Read(JsonValue.Parse(Arena(
                @"{ ""id"": ""z"", ""kind"": ""safe"", ""shape"": { ""sizeM"": [4, 4], ""at"": [9, 0] } }", "")), "t"), "a zone past the walls");
            Assert.Throws<ContentException>(() => ArenaRules.Read(JsonValue.Parse(Arena(
                "", @"{ ""kind"": ""pillar"", ""at"": [0, -4.5] }")), "t"), "the hero's start on a pillar");
            Assert.Throws<ContentException>(() => ArenaRules.Read(JsonValue.Parse(Arena(
                @"{ ""id"": ""z"", ""kind"": ""moving"", ""shape"": { ""radiusM"": 1 } }", "")), "t"), "a moving zone with no path");
            Assert.Throws<ContentException>(() => ArenaRules.Read(JsonValue.Parse(Arena("", "", "[0, 9.5]")), "t"), "a start against the wall");
        }

        // ---- Forming ----

        [Test]
        public void AnArenaFormsRoundTheHeroWhereItStandsOverFourBeats()
        {
            var song = new FixtureSong().Beats(0, 30, 100);
            var sim = Sim(song: song);
            WalkToward(sim, 5, 3, 6);
            var (hx, hy) = Hero(sim);
            int cursor = sim.EventCount;
            var def = Rules.Find("standin");
            sim.Arena.FormAroundHero(def, sim.Now);
            Assert.AreEqual(hx, sim.Arena.HeroStartX, 1e-9, "the hero keeps its place: the arena is built round it");
            Assert.AreEqual(hy, sim.Arena.HeroStartY, 1e-9);
            Assert.AreEqual(hx - def.HeroStartX + def.BossStartX, sim.Arena.BossStartX, 1e-9);
            var form = sim.EventsSince(cursor).ToArray().Single(e => e.Kind == SimEventKind.ArenaForm);
            Assert.AreEqual(4 * 60 / 100.0, form.D3 - form.D2, 0.02, "four beats at 100 BPM");
            Assert.AreEqual(ArenaPhase.Forming, sim.Arena.Phase);
            Step(sim, 1);
            Assert.That(sim.Arena.FormedAt(sim.Now), Is.InRange(0.3, 0.6));
            Step(sim, 1.5);
            Assert.AreEqual(ArenaPhase.Standing, sim.Arena.Phase);

            // Beatless: the telegraphs' 0.5 s beat.
            var quiet = Sim();
            int c2 = quiet.EventCount;
            quiet.Arena.FormAroundHero(def, quiet.Now);
            var e2 = quiet.EventsSince(c2).ToArray().Single(e => e.Kind == SimEventKind.ArenaForm);
            Assert.AreEqual(4 * Content.Beat.TelegraphBeatlessBeatS, e2.D3 - e2.D2, 1e-9);
        }

        [Test]
        public void ARetractedArenaBlocksNothingAndAnotherCanForm()
        {
            var sim = Formed("standin");
            sim.Arena.Retract(sim.Now);
            Assert.IsFalse(sim.Arena.Solid);
            var (x, _) = WalkToward(sim, 40, -7, 8);
            Assert.Greater(x, 30, "past where the walls were");
            Step(sim, 3);
            Assert.AreEqual(ArenaPhase.None, sim.Arena.Phase);
            sim.Arena.FormAroundHero(Rules.Find("testbed"), sim.Now);
            Assert.AreEqual(2, sim.Arena.Forms);
            Assert.IsTrue(sim.Arena.Inside(x, -7) && sim.Arena.Inside(x + 19, -7) && !sim.Arena.Inside(x + 21, -7), "the next is built round the hero");
        }

        // ---- Walls and props ----

        [Test]
        public void TheWallsHoldTheHeroInsideAndItSlidesAlongThem()
        {
            var sim = Formed("standin");
            // The hero starts 7 m below the centre, (0, 7) in the world; straight down meets a vertex of the 32-gon, where
            // the two walls either side hold the hero's body r / sin(84.375°) from it.
            var (x, y) = WalkToward(sim, 0, -30, 6);
            Assert.AreEqual(0, x, 0.01);
            Assert.AreEqual(7 - 16 + HeroRadius / Math.Sin(84.375 * Math.PI / 180), y, 0.01, "stopped at the wall");
            // Pressed against the wall toward a point outside and to the side, it slides round it, never out.
            var (x2, y2) = WalkToward(sim, 30, -30, 3);
            Assert.Greater(x2, x + 5, "it slid along the wall");
            Assert.Less(Math.Sqrt(x2 * x2 + (y2 - 7) * (y2 - 7)), 16 - HeroRadius + 1e-6);
            Assert.IsTrue(sim.Arena.Inside(x2, y2));
        }

        [Test]
        public void PropsBlockAndABreakableOneCrumblesUnderTheHerosBlowsForTheRestOfTheFight()
        {
            var sim = Formed("standin");
            var arena = sim.Arena;
            int crate = Enumerable.Range(0, arena.PropCount).First(i => Rules.Props[arena.Prop(i).Kind].Id == "crate");
            int pillar = Enumerable.Range(0, arena.PropCount).First(i => Rules.Props[arena.Prop(i).Kind].Id == "pillar");
            var p = arena.Prop(pillar);
            // Walk at the pillar's middle: it stops at its side.
            var (x, y) = WalkToward(sim, p.X, p.Y, 5);
            Assert.AreEqual(Rules.Props[p.Kind].Bound + HeroRadius, Math.Sqrt((x - p.X) * (x - p.X) + (y - p.Y) * (y - p.Y)), 0.01);

            Blow At(double bx, double by) => new Blow { Shape = HitShape.Circle(bx, by, 1), T = sim.Now, Damage = 10, Source = BlowSource.Dash, Owner = -1, Part = -1 };
            var c = arena.Prop(crate);
            Assert.IsFalse(arena.Strike(sim, crate, At(c.X, c.Y)), "the crate takes two blows");
            Assert.IsTrue(arena.Prop(crate).Standing);
            Assert.IsTrue(arena.Strike(sim, crate, At(c.X, c.Y)));
            Assert.IsFalse(arena.Prop(crate).Standing);
            Assert.IsTrue(arena.Walkable(c.X, c.Y, HeroRadius), "a crumbled prop blocks no more");
            Assert.IsFalse(arena.Strike(sim, pillar, At(p.X, p.Y)), "the hero can't break a pillar");
            Assert.IsFalse(arena.Breakable(pillar));
            var reached = new StructList<int>(8);
            arena.Reach(HitShape.Circle(p.X, p.Y, 3), sim.Now, reached);
            Assert.AreEqual(0, reached.Count, "blows don't reach what they can't break");
            Assert.AreEqual(1, arena.Shatter(HitShape.Circle(p.X, p.Y, 1), sim.Now), "a boss's attack crumbles it");
            Step(sim, 30);
            Assert.IsFalse(arena.Prop(pillar).Standing || arena.Prop(crate).Standing, "broken props stay broken until the fight ends");
        }

        [Test]
        public void ADamagingDashIntoAWallStrikesNothing()
        {
            var sim = Formed("standin");
            sim.Dashes.Equip(sim, Content.Dashes.IndexOf("leapSlam"), 1);
            sim.Target(0, -30, sim.Now);
            Step(sim, 0.5);
            int struck = sim.Strikes.Struck;
            for (int i = 0; i < 3; i++)
            {
                sim.Press(Key.Space, sim.Now);
                sim.Release(Key.Space, sim.Now);
                Step(sim, 2);
            }
            var (x, y) = Hero(sim);
            Assert.GreaterOrEqual(Drive.Events(sim, SimEventKind.HeroDash).Count, 3, "it dashed into the wall");
            Assert.IsTrue(sim.Arena.Inside(x, y), "dashes stop at the wall too");
            Assert.AreEqual(struck, sim.Strikes.Struck, "a wall is no target");
        }

        [Test]
        public void ShotsStopAtWallsRaisedFloorAndShotBlockingPropsButFlyOverPits()
        {
            var sim = Formed("testbed");
            var a = sim.Arena;
            double ox = a.OriginX, oy = a.OriginY;
            // Across the centre tile, west to east.
            double Shot() => a.Cast(ox - 3, oy + 0, ox + 3, oy + 0, 0.2, out _);
            Assert.AreEqual(1, Shot(), 1e-9, "open floor");
            int tile = a.Def.ZoneIndex("tile-c");
            a.Raise(tile, sim.Now);
            Assert.AreEqual((1 - 0.2) / 6, Shot(), 1e-6, "stopped at the raised tile's west face");
            a.Sink(tile, sim.Now);
            Assert.AreEqual(1, Shot(), 1e-9, "a pit stops bodies, not shots");
            Assert.Less(a.Cast(ox, oy - 9, ox, oy - 40, 0.2, out int wall), 1, "the walls stop shots");
            Assert.GreaterOrEqual(wall, a.PropCount, "a wall's id is past the props'");
            // The two posts: one stops shots, the other only bodies.
            Assert.Less(a.Cast(ox + 6, oy - 12, ox + 6, oy - 8, 0.1, out int post), 1);
            Assert.AreEqual(1, a.Cast(ox + 7.5, oy - 12, ox + 7.5, oy - 8, 0.1, out _), 1e-9);
            Assert.IsFalse(a.Walkable(ox + 7.5, oy - 10, HeroRadius));
            Assert.That(post, Is.InRange(0, a.PropCount - 1));
        }

        // ---- Changing the arena ----

        [Test]
        public void RaisedAndSunkTilesBlockAndAHeroOnOneWhenItChangesWalksOffItFreely()
        {
            var sim = Formed("testbed");
            var a = sim.Arena;
            double ox = a.OriginX, oy = a.OriginY;
            int tile = a.Def.ZoneIndex("tile-c");
            WalkToward(sim, ox, oy, 4);
            var (hx, hy) = Hero(sim);
            Assert.AreEqual(tile, a.ZoneAt(hx, hy));
            int cursor = sim.EventCount;
            a.Sink(tile, sim.Now);
            var ev = sim.EventsSince(cursor).ToArray().Single(e => e.Kind == SimEventKind.ArenaZone);
            Assert.AreEqual((int)ArenaZoneState.Sunk, ev.I1);
            Assert.AreEqual((int)ArenaZoneState.Open, ev.I2);
            // It stood on the tile as it sank: it walks off it, and then can't walk back on.
            var (x1, _) = WalkToward(sim, ox - 9, oy, 3);
            Assert.Less(x1, ox - 2 - HeroRadius + 1e-6, "off the pit");
            var (x2, y2) = WalkToward(sim, ox + 9, oy, 3);
            Assert.AreEqual(ox - 2 - HeroRadius, x2, 0.01, "stopped at the pit's edge");
            Assert.AreEqual(oy, y2, 0.01);
            a.Level(tile, sim.Now);
            var (x3, _) = WalkToward(sim, ox + 1, oy, 3);
            Assert.AreEqual(ox + 1, x3, 1e-6, "level again, it walks across");
            a.Raise(tile, sim.Now);
            Assert.IsFalse(a.Walkable(ox, oy, HeroRadius));
        }

        [Test]
        public void OnlyBreakableFloorBreaksAndBrokenFloorStaysBroken()
        {
            var sim = Formed("testbed");
            var a = sim.Arena;
            Assert.Throws<InvalidOperationException>(() => a.Break(a.Def.ZoneIndex("tile-w"), sim.Now));
            int shelf = a.Def.ZoneIndex("shelf-w");
            double ox = a.OriginX, oy = a.OriginY;
            Assert.IsTrue(a.Walkable(ox - 17, oy, HeroRadius));
            a.Break(shelf, sim.Now);
            Assert.AreEqual(ArenaZoneState.Broken, a.ZoneState(shelf));
            Assert.IsFalse(a.Walkable(ox - 17, oy, HeroRadius));
            a.Level(shelf, sim.Now);
            a.Raise(shelf, sim.Now);
            Assert.AreEqual(ArenaZoneState.Broken, a.ZoneState(shelf), "broken for the rest of the fight");
            var (x, _) = WalkToward(sim, ox - 30, oy, 6);
            Assert.AreEqual(ox - 15 + HeroRadius, x, 0.01, "the hole's edge is the new wall");
        }

        [Test]
        public void AGrownWallBlocksOnceGrownAndClearsAndAWebOnlyMarksTheFloor()
        {
            var sim = Formed("testbed");
            var a = sim.Arena;
            double ox = a.OriginX, oy = a.OriginY;
            int wall = a.Grow(ArenaShape.Box(ox + 6, oy - 5, 1, 8), ArenaObstacleKind.Wall, sim.Now, 1);
            Assert.IsTrue(a.Walkable(ox + 6, oy - 5, HeroRadius), "still growing: nothing blocks yet");
            Assert.AreEqual(-1, a.ObstacleAt(ox + 6, oy - 5, sim.Now));
            Step(sim, 1.1);
            Assert.IsFalse(a.Walkable(ox + 6, oy - 5, HeroRadius));
            Assert.AreEqual(wall, a.ObstacleAt(ox + 6, oy - 5, sim.Now));
            WalkToward(sim, ox + 2, oy - 5, 3);
            var (x, _) = WalkToward(sim, ox + 12, oy - 5, 3);
            Assert.AreEqual(ox + 5.5 - HeroRadius, x, 0.01, "stopped by the grown wall");
            a.Clear(wall, sim.Now);
            var (x2, _) = WalkToward(sim, ox + 12, oy - 5, 3);
            Assert.AreEqual(ox + 12, x2, 1e-6);

            int web = a.Grow(ArenaShape.Circle(ox, oy - 5, 2, 8), ArenaObstacleKind.Web, sim.Now, 0);
            Assert.IsTrue(a.Walkable(ox, oy - 5, HeroRadius), "a web doesn't block");
            Assert.AreEqual(web, a.ObstacleAt(ox, oy - 5, sim.Now), "the fight reads it");
            Assert.AreEqual(1, a.Cast(ox - 5, oy - 5, ox + 5, oy - 5, 0.2, out _), 1e-9, "shots fly through it");
        }

        [Test]
        public void AMovingZoneTravelsItsLoopInPlayingTime()
        {
            var sim = Formed("testbed");
            var a = sim.Arena;
            int raft = a.Def.ZoneIndex("raft");
            a.ZoneOffset(raft, out double x0, out double y0);
            Step(sim, 1);
            a.ZoneOffset(raft, out double x1, out double y1);
            Assert.AreEqual(0, x1 - x0, 1e-9);
            Assert.AreEqual(2, y1 - y0, 0.02, "2 m/s up its first leg");
            Step(sim, 32 / 2.0 - 1);
            a.ZoneOffset(raft, out double x2, out double y2);
            Assert.AreEqual(x0, x2, 0.03, "round its 32 m loop in 16 s");
            Assert.AreEqual(y0, y2, 0.03);
            // A moving pit carries its block with it.
            a.Sink(raft, sim.Now);
            a.ZoneOffset(raft, out double rx, out double ry);
            Assert.IsFalse(a.Walkable(a.OriginX + 10 + rx, a.OriginY - 6 + ry, HeroRadius));
            Step(sim, 2);
            Assert.IsTrue(a.Walkable(a.OriginX + 10 + rx, a.OriginY - 6 + ry, HeroRadius), "it moved on");
        }

        [Test]
        public void AScriptedArenaReplaysToTheSameHash()
        {
            ulong Run()
            {
                var sim = Sim(c => c.ArenaScript = new ArenaScript
                {
                    Arena = "testbed",
                    FormAtS = 0.5,
                    Steps = new[]
                    {
                        new ArenaStep { AtS = 3, Op = ArenaOp.Raise, Zone = "tile-w" },
                        new ArenaStep { AtS = 4, Op = ArenaOp.Break, Zone = "shelf-e" },
                        new ArenaStep { AtS = 5, Op = ArenaOp.Grow, Shape = ArenaShape.Box(0, 5, 6, 1), Obstacle = ArenaObstacleKind.Wall, GrowS = 0.5 },
                        new ArenaStep { AtS = 6, Op = ArenaOp.Crumble, Index = 0 },
                        new ArenaStep { AtS = 8, Op = ArenaOp.Retract },
                    },
                });
                sim.Target(3, 20, 0.2);
                sim.StepTo(12);
                Assert.AreEqual(1, sim.Arena.Forms);
                Assert.AreEqual(ArenaPhase.None, sim.Arena.Phase);
                Assert.AreEqual(3, Drive.Events(sim, SimEventKind.ArenaZone).Count + Drive.Events(sim, SimEventKind.ArenaProp).Count);
                return sim.Hash;
            }
            Assert.AreEqual(Run(), Run());
        }

        [Test]
        public void AStandInBossDuelsInsideTheWalls()
        {
            var content = GameContent.Load(Checkout.In("Content"));
            var config = new RunInputs { Duel = "fatOgre", Loadout = Array.Empty<Upgrade>() }.Build(content);
            config.Arenas = content.Arenas.Clone();
            config.ArenaScript = new ArenaScript { Arena = "standin", FormAtS = 0 };
            config.Hero.Health = 1e9;
            var sim = new Simulation(config);
            sim.Target(-30, 7, 0);
            bool entered = false;
            int inside = 0;
            for (int i = 0; i < 80; i++)
            {
                sim.StepTo(sim.Now + 0.25);
                if (!sim.Bosses.InDuel) continue;
                var b = sim.Enemies[sim.Bosses.Id];
                bool clear = sim.Arena.Inside(b.X, b.Y) && Math.Sqrt(b.X * b.X + (b.Y - 7) * (b.Y - 7)) <= 16 - b.Radius + 0.01;
                if (clear) entered = true;
                else Assert.IsFalse(entered, $"the boss left the arena at {sim.Now:F2} s, at ({b.X:F2}, {b.Y:F2})");
                if (clear) inside++;
            }
            Assert.Greater(inside, 20, "the boss fought inside it");
        }

        [Test]
        public void EachBossRushSlotFightsInItsArenaFormedRoundTheHero()
        {
            var content = GameContent.Load(Checkout.In("Content"));
            var config = new RunInputs { Seed = 3, BossRush = true, SurvivorWeapons = true, Loadout = new Upgrade[0] }.Build(content);
            config.Hero.Health = 1e9;
            var sim = new Simulation(config);
            foreach (var e in Drive.Known(new FixtureSong().Beats(0, 300, 120).Entries())) sim.AddKnown(e.Entry, e.KnownAt);
            Assert.AreEqual(1, sim.Arena.Forms, "the first boss's arena forms as the act begins");
            Assert.AreEqual(content.BossRush.Bosses[0].Arena, sim.Arena.Def.Id);
            Assert.AreEqual(BossRushBoss.StandInArena, sim.Arena.Def.Id, "the stand-in until the fight authors its own");
            double until = sim.Now + 60;
            while (!(sim.Bosses.InDuel && sim.BossRush.Stage == BossRushStage.Fighting) && sim.Now < until) sim.StepTo(sim.Now + Tick);
            Assert.AreEqual(ArenaPhase.Standing, sim.Arena.Phase);
            sim.Target(40, 0, sim.Now);
            sim.StepTo(sim.Now + 8);
            var (x, y) = Hero(sim);
            Assert.IsTrue(sim.Arena.Inside(x, y), "the walls hold the hero through the fight");
            ref readonly var b = ref sim.Enemies[sim.Bosses.Id];
            sim.Strikes.Land(sim, new Blow { Shape = HitShape.Circle(b.X, b.Y, 0.1), T = sim.Now, Damage = 1e12, Tags = HitTags.Strike,
                Source = BlowSource.SwordSlash, Owner = -1, Part = -1 });
            until = sim.Now + 60;
            while (sim.BossRush.Boss == 0 && sim.Now < until)
            {
                if (sim.State.Offer.Up)
                {
                    sim.Press(Key.One, sim.Now);
                    sim.Release(Key.One, sim.Now);
                }
                sim.StepTo(sim.Now + Tick);
            }
            Assert.AreEqual(2, sim.Arena.Forms, "the next boss's arena forms");
            var (hx, hy) = Hero(sim);
            Assert.AreEqual(hx, sim.Arena.HeroStartX, 0.1, "round the hero where the last fight left it");
            Assert.AreEqual(hy, sim.Arena.HeroStartY, 0.1);
        }

        // ---- Framing ----

        static readonly CameraModel Camera = new CameraModel { PitchDeg = 60, FovDeg = 40, Aspect = 16.0 / 9, DistanceM = 23 };

        [Test]
        public void TheCameraHoldsASmallArenaWholeAndStill()
        {
            var sim = Formed("standin");
            var framing = new ArenaFraming(Camera);
            var a = sim.Arena;
            var none = new List<(double, double, double)>();
            var f = framing.Frame(a, 0, 0, none);
            var g = framing.Frame(a, 10, -5, none);
            Assert.IsFalse(f.Scrolls);
            Assert.AreEqual(f.FocusX, g.FocusX, 1e-9, "a fixed view: wherever the hero goes");
            Assert.AreEqual(f.FocusY, g.FocusY, 1e-9);
            Assert.That(f.Distance, Is.InRange(23.0, 23 * a.Def.Camera.MaxScale));
            var o = a.Def.Outline;
            for (int i = 0; i < o.Count; i++)
                Assert.IsTrue(framing.InFrame(f.FocusX, f.FocusY, f.Distance, a.OriginX + o.X[i], a.OriginY + o.Y[i]), $"wall vertex {i} in frame");
            Assert.IsFalse(Enumerable.Range(0, o.Count).All(i => framing.InFrame(f.FocusX, f.FocusY, f.Distance * 0.9, a.OriginX + o.X[i], a.OriginY + o.Y[i])),
                "no nearer: it eases out only as far as it must");
        }

        [Test]
        public void ALargeArenaScrollsWithTheHeroKeepingTheBossAndTelegraphsInFrame()
        {
            var sim = Formed("long-hall");
            var framing = new ArenaFraming(Camera);
            var a = sim.Arena;
            double ox = a.OriginX, oy = a.OriginY, most = 23 * a.Def.Camera.MaxScale;
            var hero = (X: ox - 28, Y: oy);
            var boss = (X: ox - 14, Y: oy);
            var keep = new List<(double, double, double)> { (hero.X, hero.Y, 1), (boss.X, boss.Y, 2) };
            var f = framing.Frame(a, hero.X, hero.Y, keep);
            Assert.IsTrue(f.Scrolls);
            Assert.AreEqual(most, f.Distance, 1e-6);
            Assert.Greater(f.FocusX, hero.X, "short of the west wall, the view doesn't look past it");
            Assert.IsTrue(framing.InFrame(f.FocusX, f.FocusY, f.Distance, ox - 36, oy), "the west wall at the frame's edge");

            // The hero walks east, the boss stays: the view follows until the boss would leave, then holds it.
            var east = (X: ox + 20, Y: oy);
            keep = new List<(double, double, double)> { (east.X, east.Y, 1), (boss.X, boss.Y, 2), (ox + 10, oy + 8, 3) };
            var g = framing.Frame(a, east.X, east.Y, keep);
            foreach (var (x, y, r) in keep)
                foreach (var (sx, sy) in new[] { (-1, -1), (1, -1), (-1, 1), (1, 1) })
                    Assert.IsTrue(framing.InFrame(g.FocusX, g.FocusY, g.Distance, x + sx * r, y + sy * r), $"({x - ox:F0}, {y - oy:F0}) in frame");
            Assert.Less(g.FocusX, east.X, "held back for the boss");

            // A boss too far to share the view eases it out past its limit.
            keep = new List<(double, double, double)> { (ox + 34, oy, 1), (ox - 34, oy, 2) };
            var h = framing.Frame(a, ox + 34, oy, keep);
            Assert.Greater(h.Distance, most);
            Assert.IsTrue(framing.InFrame(h.FocusX, h.FocusY, h.Distance, ox - 36, oy) && framing.InFrame(h.FocusX, h.FocusY, h.Distance, ox + 35, oy));
        }
    }
}
