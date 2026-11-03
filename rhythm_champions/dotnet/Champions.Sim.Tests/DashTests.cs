using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace Champions.Sim.Tests
{
    /// <summary>
    /// The dash (design.md, "The Dash" and "Dashes"; Decisions 5 and 6): Space, fixed distance and duration
    /// per form on the beat or off at every tempo, cooldown and buffer, direction, click targets, structures,
    /// and each dash type's blows at each rank and evolution.
    /// </summary>
    public class DashTests
    {
        const double Tick = Simulation.TickSeconds;
        /// <summary>The sim's lead over the speakers: a press reaches it this long after it was heard.</summary>
        const double Lead = 0.05;
        const double Bpm = 128;
        static readonly double P = 60 / Bpm;

        internal static readonly GameContent Content = LegacyEncounterContent.Load();

        static DashTests() { Content.Dashes.LegacyForms = true; }

        static int Kind(string id) => Content.Dashes.IndexOf(id);

        static DashForm Form(string id, int rank) => Content.Dashes.Kinds[Kind(id)].Form(rank);

        /// <summary>A sim on the content, given <paramref name="song"/>'s entries at their normal known-at times (null: no music).</summary>
        internal static Simulation Sim(FixtureSong song = null)
        {
            var sim = new Simulation(new RunInputs().Build(Content));
            if (song != null)
                foreach (var k in Drive.Known(song.Entries())) sim.AddKnown(k.Entry, k.KnownAt);
            return sim;
        }

        /// <summary>Space heard at <paramref name="heard"/>, reaching the sim <paramref name="lead"/> later.</summary>
        internal static void Dash(Simulation sim, double heard, double lead = Lead)
        {
            sim.StepTo(heard + lead);
            sim.Press(Key.Space, heard);
        }

        static void At(Simulation sim, out double x, out double y) => sim.State.Hero.PositionAt(sim.Now, out x, out y);

        static SimEvent LastDash(Simulation sim) => Drive.Events(sim, SimEventKind.HeroDash).Last();

        static IEnumerable<(int Kind, int Rank)> Forms()
        {
            var kinds = Content.Dashes.Kinds;
            for (int k = 0; k < kinds.Length; k++)
            {
                for (int r = 1; r <= kinds[k].TopRank; r++) yield return (k, r);
                if (kinds[k].Evolution != null) yield return (k, DashKind.EvolutionRank);
            }
        }

        static readonly double[] Tempos = { 80, 128, 174, 0 };

        [TestCaseSource(nameof(Tempos))]
        public void EveryDashGoesItsFixedDistanceOverItsFixedDurationOnTheBeatAndOff(double bpm)
        {
            // bpm 0: no music, so every press is on the beat.
            var forms = Forms().ToList();
            double period = bpm > 0 ? 60 / bpm : 1;
            int dashes = forms.Count * (bpm > 0 ? 2 : 1);
            var sim = Sim(bpm > 0 ? new FixtureSong().Beats(0, (12 + 3 * dashes + 8) * period, bpm, onsets: false) : null);
            int j = 0;
            foreach (var (kind, rank) in forms)
                foreach (bool on in bpm > 0 ? new[] { true, false } : new[] { true })
                {
                    double beat = (12 + 3 * j++) * period;
                    double heard = on ? beat : beat + period / 4;
                    sim.StepTo(heard);
                    sim.Dashes.Equip(sim, kind, rank);
                    At(sim, out double x0, out double y0);
                    Dash(sim, heard);
                    var e = LastDash(sim);
                    var form = Content.Dashes.Kinds[kind].Form(rank);
                    string what = $"{Content.Dashes.Kinds[kind].Id} rank {rank}, {(on ? "on" : "off")} the beat at {bpm} BPM";
                    Assert.AreEqual(j, sim.State.Hero.Dashes, what);
                    Assert.AreEqual(on, e.OnBeat, what);
                    Assert.AreEqual(bpm > 0 ? (on ? JudgedBy.Beat : JudgedBy.Off) : JudgedBy.Beatless, e.Judged, what);
                    Assert.AreEqual(heard, e.D0, 1e-9, what + ": from when it was heard");
                    Assert.AreEqual(form.DurationS, e.D1 - e.D0, 1e-9, what);
                    Assert.AreEqual(e.D1 + Content.Dashes.IFramesAfterS, sim.State.Hero.IFramesUntil, 1e-9, what + ": i-frames");
                    sim.StepTo(e.D1 + Tick);
                    At(sim, out double x1, out double y1);
                    Assert.AreEqual(form.DistanceM, Math.Sqrt((x1 - x0) * (x1 - x0) + (y1 - y0) * (y1 - y0)), 1e-9, what);
                }
        }

        [Test]
        public void ADashStartsFromWhenItsPressWasHeard()
        {
            var sim = Sim();
            sim.Press(Key.Right, 0);
            sim.StepTo(1.1);
            sim.Press(Key.Space, 1.0);
            var e = LastDash(sim);
            Assert.AreEqual(1.1, e.T, 1e-9, "taken at the frontier");
            Assert.AreEqual(1.0, e.D0, 1e-9, "drawn from when it was heard");
            double speed = 4 / 0.15;
            At(sim, out double x, out _);
            Assert.AreEqual(6 + speed * 0.1, x, 1e-9, "dashing since 1.0 s");
            var move = Drive.Events(sim, SimEventKind.HeroMove).Last();
            Assert.AreEqual(1.0, move.D0, 1e-9);
            Assert.AreEqual(speed, move.D3, 1e-9);
            sim.StepTo(1.5);
            At(sim, out x, out _);
            Assert.AreEqual(6 + 4 + 6 * 0.35, x, 1e-9, "the walk goes on after it");
            // Never further back than the walk's history.
            sim.StepTo(3);
            sim.Press(Key.Space, 1.9);
            Assert.AreEqual(3 - (HeroWalk.History - 1) * Tick, LastDash(sim).D0, 1e-9);
        }

        [Test]
        public void APressInTheCooldownsLastMomentsFiresWhenReadyAndAnEarlierOneIsDropped()
        {
            var sim = Sim();
            Dash(sim, 1.0);
            Assert.AreEqual(1.6, sim.State.Hero.DashReadyAt, 1e-9);
            Dash(sim, 1.3);
            var dropped = Drive.Events(sim, SimEventKind.DashDropped).Single();
            Assert.AreEqual(1.3, dropped.D0, 1e-9, "the pip blinks for it");
            Assert.AreEqual(1.6, dropped.D1, 1e-9);
            Assert.AreEqual(1, sim.State.Hero.Dashes);
            Dash(sim, 1.45);
            Assert.IsTrue(sim.State.Hero.DashWaiting, "0.15 s before ready: it waits");
            sim.StepTo(1.6 - Tick);
            Assert.AreEqual(1, sim.State.Hero.Dashes);
            sim.StepTo(1.7);
            var second = LastDash(sim);
            Assert.AreEqual(2, sim.State.Hero.Dashes);
            Assert.AreEqual(1.6, second.D0, 1e-9, "fired the moment it was ready");
            Assert.IsTrue(second.Buffered);
            Assert.IsFalse(sim.State.Hero.DashWaiting);
            Assert.AreEqual(2.2, sim.State.Hero.DashReadyAt, 1e-9, "the cooldown runs from the dash's start");
            Dash(sim, 2.2 - 0.151);
            Assert.AreEqual(2, Drive.Events(sim, SimEventKind.DashDropped).Count, "just outside the buffer");
            // A buffered press the sim takes after the dash was ready starts at the ready moment.
            Dash(sim, 2.06, lead: 0.2);
            var third = LastDash(sim);
            Assert.AreEqual(2.2, third.D0, 1e-9);
            Assert.IsTrue(third.Buffered);
        }

        [Test]
        public void ABufferedPressIsJudgedByWhenItWasHeard()
        {
            var song = new FixtureSong().Beats(0, 20, Bpm, onsets: false);
            var sim = Sim(song);
            double b12 = 12 * P, b13 = 13 * P;
            Dash(sim, b12);
            // Beat 13 is 131 ms before the dash is ready.
            Dash(sim, b13, lead: 0.02);
            Dash(sim, b13 + 0.1, lead: 0.02);
            sim.StepTo(b12 + 1);
            var e = LastDash(sim);
            Assert.AreEqual(2, sim.State.Hero.Dashes);
            Assert.IsTrue(e.Buffered);
            Assert.AreEqual(b12 + 0.6, e.D0, 1e-9);
            Assert.IsTrue(e.OnBeat, "heard on beat 13, though it fired 131 ms after it; the later off-beat press didn't replace it");
            Assert.AreEqual(JudgedBy.Beat, e.Judged);
        }

        [Test]
        public void QuickstepLowersTheCooldown()
        {
            var sim = Sim();
            sim.Dashes.CooldownS = 0.45;
            Dash(sim, 1.0);
            Assert.AreEqual(1.45, sim.State.Hero.DashReadyAt, 1e-9);
            Assert.AreEqual(0.45, sim.Snapshot().DashCooldownS, 1e-12);
            Dash(sim, 1.45);
            Assert.AreEqual(2, sim.State.Hero.Dashes);
            Assert.AreEqual(1.45, LastDash(sim).D0, 1e-9);
        }

        [Test]
        public void ADashGoesTheHeldKeysWayElseTowardTheClickTargetElseTheFacing()
        {
            var sim = Sim();
            Dash(sim, 1.0);
            var e = LastDash(sim);
            Assert.AreEqual((0.0, -1.0), (e.D4, e.D5), "the facing: down the screen at the start");
            sim.StepTo(2);
            sim.Press(Key.Right, 2);
            sim.Press(Key.Up, 2);
            Dash(sim, 2);
            e = LastDash(sim);
            Assert.AreEqual(Math.Sqrt(0.5), e.D4, 1e-12, "the held keys, eight ways");
            Assert.AreEqual(Math.Sqrt(0.5), e.D5, 1e-12);
            sim.Target(-100, 0, sim.Now);
            Dash(sim, 3);
            e = LastDash(sim);
            Assert.AreEqual(Math.Sqrt(0.5), e.D4, 1e-12, "the keys win over a target");
            Assert.AreEqual(Math.Sqrt(0.5), e.D5, 1e-12);
            sim.Release(Key.Right, 3.5);
            sim.Release(Key.Up, 3.5);
            sim.StepTo(4);
            At(sim, out double x, out double y);
            sim.Target(x + 3, y - 4, 4);
            Dash(sim, 4.1);
            e = LastDash(sim);
            Assert.AreEqual(0.6, e.D4, 1e-9, "toward the click target from where the hero was");
            Assert.AreEqual(-0.8, e.D5, 1e-9);
            sim.StepTo(6);
            Dash(sim, 6);
            e = LastDash(sim);
            Assert.AreEqual(0.6, e.D4, 1e-9, "the facing: its latest travel's");
            Assert.AreEqual(-0.8, e.D5, 1e-9);
        }

        [Test]
        public void ADashPassingItsClickTargetClearsIt()
        {
            var sim = Sim();
            sim.Target(0, 3, 0);
            Dash(sim, 0.1);
            sim.StepTo(2);
            At(sim, out _, out double y);
            Assert.AreEqual(0.6 + 4, y, 1e-9, "past the target, it stays where the dash left it");
            sim.Target(0, 20, 2);
            Dash(sim, 2.2);
            sim.StepTo(5);
            At(sim, out _, out y);
            Assert.AreEqual(20, y, 1e-9, "a target the dash fell short of is walked on to");
        }

        [Test]
        public void AStructureStopsADashWhichSlidesAlongIt()
        {
            var sim = Sim();
            var walls = new Walls();
            var wall = walls.Add(2, -10, 3, 10, hits: 1);
            sim.UseStructures(walls);
            sim.StepTo(1);
            sim.Press(Key.Right, 1);
            sim.Press(Key.Space, 1);
            sim.StepTo(2);
            At(sim, out double x, out _);
            Assert.AreEqual(2 - 0.4, x, 1e-9, "stopped against the wall");
            Assert.IsFalse(wall.Broken);
            Assert.IsEmpty(wall.Struck, "the plain Dash has no damage");
            // Diagonally into it, the dash slides along it.
            sim = Sim();
            sim.UseStructures(walls);
            sim.StepTo(1);
            sim.Press(Key.Right, 1);
            sim.Press(Key.Up, 1);
            sim.Press(Key.Space, 1);
            sim.Release(Key.Right, 1);
            sim.Release(Key.Up, 1);
            sim.StepTo(2);
            At(sim, out x, out double y);
            Assert.AreEqual(1.6, x, 1e-9);
            Assert.AreEqual(4 * Math.Sqrt(0.5), y, 1e-9, "it slid on along the wall");
        }

        /// <summary>A dash of <paramref name="kind"/> right into a wall at x 2-3 that breaks after <paramref name="hits"/>; where the hero ends.</summary>
        static (double X, Walls.Wall Wall) IntoAWall(string kind, int hits)
        {
            var sim = Sim();
            sim.Dashes.Equip(sim, Kind(kind), 1);
            var walls = new Walls();
            var wall = walls.Add(2, -10, 3, 10, hits);
            sim.UseStructures(walls);
            sim.StepTo(1);
            sim.Press(Key.Right, 1);
            sim.Press(Key.Space, 1);
            sim.Release(Key.Right, 1);
            sim.StepTo(2);
            At(sim, out double x, out _);
            return (x, wall);
        }

        [Test]
        public void ADamagingDashBreaksThroughAStructureItBreaksAndStopsAtOneItDoesnt()
        {
            // Leap Slam deals nothing on the way: it strikes the wall as it runs into it.
            var (x, wall) = IntoAWall("leapSlam", 1);
            var blow = wall.Struck[0];
            Assert.AreEqual(-1, blow.Part, "struck as the dash ran into it");
            Assert.AreEqual(2.0 * 2, blow.Damage, 1e-9, "the dash's damage, on the beat (no music)");
            Assert.AreEqual(HitTags.Area | HitTags.Dash, blow.Tags);
            Assert.IsTrue(wall.Broken);
            Assert.AreEqual(5, x, 1e-9, "it broke, so the dash carried on through");
            (x, wall) = IntoAWall("leapSlam", 3);
            Assert.AreEqual(1.6, x, 1e-9, "it held, so the dash stopped");
            Assert.AreEqual(new[] { -1, 0 }, wall.Struck.Select(b => b.Part).ToArray(), "struck once running into it, then by the landing's shockwave");
            Assert.IsFalse(wall.Broken);
            // Blade Rush's path reaches the wall before the hero does, and strikes it once either way.
            (x, wall) = IntoAWall("bladeRush", 1);
            Assert.AreEqual(5, x, 1e-9);
            Assert.AreEqual(0, wall.Struck.Single().Part);
            (x, wall) = IntoAWall("bladeRush", 3);
            Assert.AreEqual(1.6, x, 1e-9);
            Assert.AreEqual(1, wall.Struck.Count, "running into it doesn't strike it again");
        }

        [Test]
        public void NothingDashesWhileTheWorldIsHeld()
        {
            var sim = Sim();
            Dash(sim, 1.0);
            Dash(sim, 1.5);
            Assert.IsTrue(sim.State.Hero.DashWaiting);
            sim.Control(RunControl.Pause, sim.Now);
            sim.StepTo(1.7);
            Assert.AreEqual(1, sim.State.Hero.Dashes, "the waiting press was dropped");
            Assert.IsFalse(sim.State.Hero.DashWaiting);
            Dash(sim, 2);
            Assert.AreEqual(1, sim.State.Hero.Dashes, "no dash while held");
            sim.Control(RunControl.Resume, sim.Now);
            Dash(sim, 3);
            Assert.AreEqual(2, sim.State.Hero.Dashes);
        }

        [Test]
        public void EquippingReplacesTheDashAndChecksItsRank()
        {
            var sim = Sim();
            Assert.AreEqual(Kind("dash"), sim.State.Hero.Dash, "the start dash");
            sim.Dashes.Equip(sim, Kind("leapSlam"), 2);
            Assert.AreEqual((Kind("leapSlam"), 2), (sim.State.Hero.Dash, sim.State.Hero.DashRank));
            var e = Drive.Events(sim, SimEventKind.DashEquip).Single();
            Assert.AreEqual((Kind("leapSlam"), 2), (e.I0, e.I1));
            Assert.Throws<ArgumentOutOfRangeException>(() => sim.Dashes.Equip(sim, Kind("dash"), DashKind.EvolutionRank));
            Assert.Throws<ArgumentOutOfRangeException>(() => sim.Dashes.Equip(sim, Kind("bladeRush"), 0));
            sim.Dashes.Equip(sim, Kind("bladeRush"), DashKind.EvolutionRank);
            Assert.AreEqual(DashKind.EvolutionRank, sim.Snapshot().DashRank);
        }

        [Test]
        public void TheModsHooksScaleTheDamageAddPartsAndHearEachDash()
        {
            var sim = Sim();
            var dummies = new Dummies();
            int ahead = dummies.Add(0, -2);
            sim.Strikes.Add(dummies);
            var heard = new List<DashRecord>();
            sim.Dashes.Dashed += (s, d) => heard.Add(d);
            sim.Dashes.DamageScale = 2;
            // Momentum's trail: the plain Dash's path strikes once.
            sim.Dashes.ExtraParts.Add(new DashPart { Shape = ShapeKind.Path, At = PartAt.Path, When = PartWhen.Dash, Repeat = 1, RadiusM = 1, Damage = 0.8 });
            Dash(sim, 1);
            sim.StepTo(2);
            var blow = dummies.On(ahead).Single();
            Assert.AreEqual(0.8 * 2 * 2, blow.Damage, 1e-9, "the trail's damage, doubled, on the beat");
            Assert.AreEqual(1, heard.Count);
            Assert.AreEqual(1.0, heard[0].Start, 1e-9);
            Assert.AreEqual(4, heard[0].DistanceM);
            Assert.IsTrue(heard[0].OnBeat);
        }

        // ---- Each dash's blows ----

        /// <summary>
        /// A dash of <paramref name="kind"/> at <paramref name="rank"/>, heard on beat 12 of a 128 BPM song (or a
        /// quarter beat after it), from (0, 0) down the screen, with <paramref name="dummies"/> standing in the world.
        /// </summary>
        static (Simulation Sim, SimEvent Dash) Shoot(string kind, int rank, bool onBeat, Dummies dummies, double after = 4)
        {
            var sim = Sim(new FixtureSong().Beats(0, 30, Bpm, onsets: false));
            sim.Strikes.Add(dummies);
            sim.Dashes.Equip(sim, Kind(kind), rank);
            double heard = 12 * P + (onBeat ? 0 : P / 4);
            Dash(sim, heard);
            sim.StepTo(heard + after);
            var dash = Drive.Events(sim, SimEventKind.HeroDash).Single();
            Assert.AreEqual(onBeat, dash.OnBeat);
            return (sim, dash);
        }

        /// <summary>A dummy <paramref name="lateral"/> m to the side of the path and <paramref name="along"/> m along it.</summary>
        static int Stand(Dummies d, double lateral, double along) => d.Add(lateral, -along);

        static double NextBeat(Simulation sim, double t, int k = 1)
        {
            sim.Beats.NextBeat(t + 1e-6, out int i);
            return sim.Beats.PredictBeat(i + k - 1);
        }

        static double NextDownbeat(Simulation sim, double t) => sim.Beats.NextDownbeat(t + 1e-6, out _);

        [Test]
        public void BladeRushStrikesEverythingWithinReachOfItsPathOnceAsItPasses()
        {
            foreach (bool on in new[] { false, true })
            {
                var d = new Dummies();
                int near = Stand(d, 1.4, 2.5), far = Stand(d, 1.6, 2.5), wide = Stand(d, 2.2, 1), beyond = Stand(d, 0, 7.4);
                var (_, dash) = Shoot("bladeRush", 1, on, d);
                var hit = d.On(near).Single();
                Assert.AreEqual(on ? 3.0 : 1.5, hit.Damage, 1e-9, "1.5x, twice that on the beat");
                Assert.AreEqual(HitTags.Strike | HitTags.Dash, hit.Tags);
                Assert.AreEqual(on, hit.OnBeat);
                Assert.Greater(hit.T, dash.D0, "struck as the hero passes it");
                Assert.Less(hit.T, dash.D1);
                Assert.AreEqual(on ? 1 : 0, d.On(far).Count, "1.5 m off the beat, 2.25 m on it");
                Assert.AreEqual(on ? 1 : 0, d.On(wide).Count);
                Assert.IsEmpty(d.On(beyond));
                Assert.AreEqual(3, Form("bladeRush", 1).Parts[0].Slashes, "three slashes drawn");
            }
        }

        [Test]
        public void BladeRushsRanksWidenItAddASlashAndCutAgainOnTheNextBeats()
        {
            var d = new Dummies();
            int near = Stand(d, 1, 2.5), wide = Stand(d, 2.4, 2.5);
            Shoot("bladeRush", 2, false, d);
            Assert.AreEqual(2.0, d.On(wide).Single().Damage, 1e-9, "rank 2: 2.5 m wide either side, four slashes");
            Assert.AreEqual(4, Form("bladeRush", 2).Parts[0].Slashes);

            d = new Dummies();
            near = Stand(d, 1, 2.5);
            var (sim, dash) = Shoot("bladeRush", 3, false, d);
            var hits = d.On(near);
            Assert.AreEqual(2, hits.Count, "rank 3: the cut strikes again");
            Assert.AreEqual(0, hits[0].Part);
            Assert.AreEqual(1, hits[1].Part);
            Assert.AreEqual(NextBeat(sim, dash.D1), hits[1].T, 1e-9, "on the next beat");
            Assert.AreEqual(2.0, hits[1].Damage, 1e-9);

            d = new Dummies();
            near = Stand(d, 1, 2.5);
            (sim, dash) = Shoot("bladeRush", DashKind.EvolutionRank, true, d);
            hits = d.On(near);
            Assert.AreEqual(3, hits.Count, "Thousand Cuts: the pass and the next two beats");
            Assert.AreEqual(NextBeat(sim, dash.D1), hits[1].T, 1e-9);
            Assert.AreEqual(NextBeat(sim, dash.D1, 2), hits[2].T, 1e-9);
            Assert.IsTrue(hits.All(h => h.OnBeat && Math.Abs(h.Damage - 4.0) < 1e-9), "every blow keeps the dash's on-beat payoff");
        }

        [Test]
        public void LeapSlamLandsAShockwaveAndItsRanksRollARingAndAnAftershock()
        {
            var d = new Dummies();
            int path = Stand(d, 0, 1.5), inside = Stand(d, 0, 5 + 2.9), outside = Stand(d, 0, 5 + 3.1);
            var (sim, dash) = Shoot("leapSlam", 1, false, d);
            Assert.IsEmpty(d.On(path), "it leaps over what's under it");
            var slam = d.On(inside).Single();
            Assert.AreEqual(dash.D1, slam.T, 1e-9, "on landing");
            Assert.AreEqual(2.0, slam.Damage, 1e-9);
            Assert.AreEqual(HitTags.Area | HitTags.Dash, slam.Tags);
            Assert.IsEmpty(d.On(outside));
            d = new Dummies();
            outside = Stand(d, 0, 5 + 3.1);
            Shoot("leapSlam", 1, true, d);
            Assert.AreEqual(4.0, d.On(outside).Single().Damage, 1e-9, "on the beat: 4.5 m, 2x");

            d = new Dummies();
            int near = Stand(d, 0, 5 + 3), ring = Stand(d, 0, 5 + 5.5);
            (sim, dash) = Shoot("leapSlam", 2, false, d);
            Assert.AreEqual(2.0, d.On(near).Single().Damage, 1e-9, "rank 2: the 4 m shockwave, inside the ring");
            var rolled = d.On(ring).Single();
            Assert.AreEqual(NextBeat(sim, dash.D1), rolled.T, 1e-9, "the ring rolls on to 7 m on the next beat");
            Assert.AreEqual(1.0, rolled.Damage, 1e-9);

            d = new Dummies();
            near = Stand(d, 0, 5 + 3);
            (sim, dash) = Shoot("leapSlam", 3, false, d);
            var hits = d.On(near);
            Assert.AreEqual(2, hits.Count, "rank 3: an aftershock");
            double downbeat = NextDownbeat(sim, dash.D1);
            Assert.AreEqual(downbeat, hits[1].T, 1e-9, "on the next downbeat");
            Assert.IsTrue(sim.Beats.IsDownbeat((int)Math.Round(downbeat / P)));

            d = new Dummies();
            int crater = Stand(d, 0, 5 + 5.9), far = Stand(d, 0, 5 + 11);
            (sim, dash) = Shoot("leapSlam", DashKind.EvolutionRank, false, d);
            Assert.AreEqual(0.45, dash.D1 - dash.D0, 1e-9, "Skyfall: a higher leap");
            var shatter = d.On(crater)[0];
            Assert.IsTrue(shatter.Shatter, "it shatters everything within 6 m");
            Assert.AreEqual(4.0, shatter.Damage, 1e-9);
            Assert.AreEqual(NextBeat(sim, dash.D1), d.On(far).Single().T, 1e-9, "and sends a ring to 12 m");
        }

        [Test]
        public void ShieldChargeThrowsWhatItStrikesAsideAndItsRanksAddLandingsAndABash()
        {
            var d = new Dummies();
            int near = Stand(d, 0.9, 3), far = Stand(d, 1.1, 3);
            Shoot("shieldCharge", 1, false, d);
            var hit = d.On(near).Single();
            Assert.AreEqual(1.2, hit.Damage, 1e-9);
            Assert.AreEqual(ThrowKind.Aside, hit.Throw);
            Assert.AreEqual(HitTags.Dash, hit.Tags);
            Assert.AreEqual(0, hit.ThrownDamage);
            Assert.IsEmpty(d.On(far), "a 2 m path");

            d = new Dummies();
            near = Stand(d, 0.9, 3);
            Shoot("shieldCharge", 2, false, d);
            Assert.AreEqual(1.0, d.On(near).Single().ThrownDamage, 1e-9, "rank 2: thrown enemies damage what they land on");

            d = new Dummies();
            int ahead = Stand(d, 0, 6 + 2.5), beside = Stand(d, 2.9, 6);
            var (_, dash) = Shoot("shieldCharge", 3, false, d);
            var bash = d.On(ahead).Single();
            Assert.AreEqual(dash.D1, bash.T, 1e-9, "rank 3: it ends in a bash");
            Assert.AreEqual(ThrowKind.Away, bash.Throw);
            Assert.AreEqual(1.5, bash.Damage, 1e-9);
            Assert.IsEmpty(d.On(beside), "a cone ahead, not beside");

            d = new Dummies();
            near = Stand(d, 0.9, 7);
            ahead = Stand(d, 0, 8 + 2.5);
            (_, dash) = Shoot("shieldCharge", DashKind.EvolutionRank, false, d);
            Assert.AreEqual(0.35, dash.D1 - dash.D0, 1e-9);
            Assert.IsTrue(d.On(near).Single().ThrownShatter, "Juggernaut: 8 m, everything thrown shatters");
            Assert.IsTrue(d.On(ahead).Single().ThrownShatter);
        }

        [Test]
        public void BlinkStrikeBurstsAtBothEndsAndItsRanksAddTheMidpointAndRepeats()
        {
            var d = new Dummies();
            int origin = Stand(d, 2.4, 0), end = Stand(d, 2.4, 6), mid = Stand(d, 0, 3);
            var (sim, dash) = Shoot("blinkStrike", 1, false, d);
            Assert.AreEqual(dash.D0, d.On(origin).Single().T, 1e-9, "a burst where it left");
            var arrival = d.On(end).Single();
            Assert.AreEqual(dash.D0 + 0.05, arrival.T, 1e-9, "and where it arrived, 0.05 s later");
            Assert.AreEqual(1.5, arrival.Damage, 1e-9);
            Assert.AreEqual(HitTags.Strike | HitTags.Area | HitTags.Dash, arrival.Tags);
            Assert.IsEmpty(d.On(mid));

            d = new Dummies();
            mid = Stand(d, 0, 3);
            (sim, dash) = Shoot("blinkStrike", 2, false, d);
            Assert.AreEqual(dash.D0 + 0.025, d.On(mid).Single().T, 1e-9, "rank 2: a third burst at the midpoint");

            d = new Dummies();
            end = Stand(d, 2.4, 6);
            (sim, dash) = Shoot("blinkStrike", 3, false, d);
            var ends = d.On(end);
            Assert.AreEqual(2, ends.Count, "rank 3: the arrival burst repeats");
            Assert.AreEqual(NextBeat(sim, dash.D1), ends[1].T, 1e-9, "on the next beat");

            d = new Dummies();
            origin = Stand(d, 2.4, 0);
            (sim, dash) = Shoot("blinkStrike", DashKind.EvolutionRank, false, d);
            var phantom = d.On(origin);
            Assert.AreEqual(5, phantom.Count, "Phase Walk: the burst, then a phantom's on every beat for a bar");
            for (int k = 1; k <= 4; k++) Assert.AreEqual(NextBeat(sim, dash.D1, k), phantom[k].T, 1e-9);
        }

        [Test]
        public void ADelayedBlowWithNoBeatWaitsHalfASecondABeat()
        {
            var d = new Dummies();
            int end = Stand(d, 2.4, 6);
            var sim = Sim();
            sim.Strikes.Add(d);
            sim.Dashes.Equip(sim, Kind("blinkStrike"), 3);
            Dash(sim, 1);
            sim.StepTo(3);
            var hits = d.On(end);
            Assert.AreEqual(2, hits.Count);
            Assert.AreEqual(1.05 + 0.5, hits[1].T, 1e-9);
        }

        // ---- A session ----

        [Test]
        public void ASessionWithDashesReplaysToItsHash()
        {
            var content = LegacyEncounterContent.Load();
            content.Dashes = content.Dashes.Clone();
            content.Dashes.LegacyForms = true;
            content.Dashes.Start = "bladeRush";
            var entries = new FixtureSong(5).Beats(0, 14, Bpm, jitter: 0.003, halfOnsets: true).Gap(14, 16).Beats(16, 30, 100).Entries();
            var run = new RunInputs { Seed = 5 };
            Simulation Build()
            {
                var s = new Simulation(run.Build(content));
                var dummies = new Dummies { Body = 0.3 };
                for (int i = 0; i < 40; i++) dummies.Add(-10 + (i % 8) * 3, -6 + (i / 8) * 3);
                s.Strikes.Add(dummies);
                return s;
            }
            var live = Build();
            var script = new Queue<(double At, Action<Simulation> Give)>(new (double, Action<Simulation>)[]
            {
                (1.0, s => s.Press(Key.Left, s.Now - 0.06)),
                (5.2, s => s.Press(Key.Space, 11 * P)),
                (5.4, s => s.Press(Key.Space, s.Now - 0.05)),
                (5.8, s => s.Press(Key.Space, s.Now - 0.05)),
                (6.0, s => s.Release(Key.Left, s.Now - 0.04)),
                (6.1, s => s.Target(6, 8, s.Now - 0.07)),
                (7.0, s => s.Press(Key.Space, s.Now - 0.08)),
                (9.0, s => s.Control(RunControl.Pause, s.Now - 0.02)),
                (9.5, s => s.Press(Key.Space, s.Now - 0.05)),
                (10.0, s => s.Control(RunControl.Resume, s.Now - 0.03)),
                (14.6, s => s.Press(Key.Space, s.Now - 0.05)),
                (20.0, s => s.Press(Key.Down, s.Now - 0.05)),
                (20.3, s => s.Press(Key.Space, s.Now - 0.06)),
                (20.6, s => s.Press(Key.Space, s.Now - 0.07)),
                (21.0, s => s.Release(Key.Down, s.Now - 0.05)),
            });
            int fed = Drive.Feed(live, entries, 25, eachFrame: s =>
            {
                while (script.Count > 0 && script.Peek().At <= s.Now) script.Dequeue().Give(s);
            });
            Assert.GreaterOrEqual(live.State.Hero.Dashes, 5);
            Assert.Greater(live.Strikes.Struck, 0, "its blows reached the dummies");
            Assert.IsNotEmpty(Drive.Events(live, SimEventKind.DashDropped));
            var replay = new SessionReplay(SessionRecord.Of(run, live));
            foreach (var e in entries.GetRange(0, fed)) replay.Supply(e);
            var sim = Build();
            Assert.IsTrue(replay.StepTo(sim, double.PositiveInfinity));
            Assert.IsTrue(replay.Matches(sim), $"replayed {sim.Hash:X16}, recorded {live.Hash:X16}");
        }
    }
}
