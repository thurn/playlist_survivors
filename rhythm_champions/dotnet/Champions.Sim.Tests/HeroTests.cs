using System;
using System.Linq;
using NUnit.Framework;

namespace Champions.Sim.Tests
{
    /// <summary>
    /// The hero's walk and heard-time inputs (design.md, "Controls"; plan.md, "Inputs and heard time"): 6 m/s,
    /// eight ways, click targets, and an input re-walking the hero from when it was heard, up to 64 ticks and
    /// never across a hold.
    /// </summary>
    public class HeroTests
    {
        const double Speed = 6;
        const double Tick = Simulation.TickSeconds;

        static void At(Simulation sim, out double x, out double y) => sim.State.Hero.PositionAt(sim.Now, out x, out y);

        [Test]
        public void TheHeroWalksAtSixMetresASecondEightWays()
        {
            var sim = Drive.Sim();
            sim.StepTo(1);
            sim.Press(Key.Right, sim.Now);
            sim.StepTo(2);
            At(sim, out double x, out double y);
            Assert.AreEqual(Speed, x, 1e-9);
            Assert.AreEqual(0, y, 1e-12);
            sim.Press(Key.Up, sim.Now);
            sim.StepTo(3);
            At(sim, out x, out y);
            Assert.AreEqual(Speed + Speed * Math.Sqrt(0.5), x, 1e-9, "diagonals as fast as the rest");
            Assert.AreEqual(Speed * Math.Sqrt(0.5), y, 1e-9);
            sim.Press(Key.Left, sim.Now);
            sim.StepTo(4);
            At(sim, out double x2, out double y2);
            Assert.AreEqual(x, x2, 1e-9, "opposite keys cancel");
            Assert.AreEqual(y + Speed, y2, 1e-9);
            Assert.AreEqual(0, sim.State.Hero.FacingX, 1e-12);
            Assert.AreEqual(1, sim.State.Hero.FacingY, 1e-12, "it faces its travel");
        }

        [Test]
        public void AKeyHeardBeforeTheSimTakesItMovesTheHeroFromWhenItWasHeard()
        {
            var sim = Drive.Sim();
            sim.StepTo(2);
            sim.Press(Key.Right, 1.9);
            At(sim, out double x, out _);
            Assert.AreEqual(Speed * 0.1, x, 1e-9, "walked since 1.9 s");
            var move = Drive.Events(sim, SimEventKind.HeroMove).Last(e => e.D3 != 0 || e.D4 != 0);
            Assert.AreEqual(2.0, move.T, 1e-9, "taken at the frontier");
            Assert.AreEqual(1.9, move.D0, 1e-9, "drawn from when it was heard");
            Assert.AreEqual(Speed, move.D3, 1e-9);
            sim.HeroWas(1.95, out double was, out _);
            Assert.AreEqual(Speed * 0.05, was, 1e-9, "the history holds the re-walked path");
            sim.StepTo(2.5);
            sim.Release(Key.Right, 2.4);
            At(sim, out x, out _);
            Assert.AreEqual(Speed * 0.5, x, 1e-9, "a release heard earlier stops it where it was then");
        }

        [Test]
        public void AReWalkGoesBackAtMostSixtyFourTicks()
        {
            var sim = Drive.Sim();
            sim.StepTo(2);
            sim.Press(Key.Right, 1.0);
            At(sim, out double x, out _);
            Assert.AreEqual(Speed * (HeroWalk.History - 1) * Tick, x, 1e-9);
        }

        [Test]
        public void AReWalkNeverCrossesAHold()
        {
            var sim = Drive.Sim();
            sim.StepTo(1.5);
            sim.Control(RunControl.Pause, 1.5);
            sim.StepTo(1.95);
            sim.Control(RunControl.Resume, 1.95);
            sim.StepTo(2);
            sim.Press(Key.Right, 1.9);
            At(sim, out double x, out _);
            Assert.AreEqual(Speed * 0.05, x, 1e-9, "from the hold's end, not 1.9 s");
        }

        [Test]
        public void TheHeroStandsWhileTheWorldIsHeld()
        {
            var sim = Drive.Sim();
            sim.Press(Key.Right, 0);
            sim.StepTo(1);
            sim.Control(RunControl.Pause, 1);
            sim.StepTo(2);
            At(sim, out double x, out _);
            Assert.AreEqual(Speed, x, 1e-9);
            sim.Target(10, 10, 2);
            sim.Control(RunControl.Resume, 2);
            sim.StepTo(3);
            At(sim, out x, out double y);
            Assert.AreEqual(2 * Speed, x, 1e-9, "the key still held walks on; the target given while held was dropped");
            Assert.AreEqual(0, y, 1e-12);
            Assert.AreEqual(2.0, sim.State.RunClock, 1e-9, "the run clock stood while paused");
        }

        [Test]
        public void AClickTargetWalksTheHeroStraightToItAndAKeyDropsIt()
        {
            var sim = Drive.Sim();
            sim.Target(3, 4, 0);
            sim.StepTo(0.5);
            At(sim, out double x, out double y);
            Assert.AreEqual(3 * 0.6, x, 1e-9);
            Assert.AreEqual(4 * 0.6, y, 1e-9);
            sim.StepTo(2);
            At(sim, out x, out y);
            Assert.AreEqual(3, x, 1e-9, "it stops on the target");
            Assert.AreEqual(4, y, 1e-9);
            sim.Target(3, -10, 2);
            sim.StepTo(2.5);
            sim.Press(Key.Left, 2.5);
            sim.Release(Key.Left, 2.5);
            sim.StepTo(4);
            At(sim, out x, out y);
            Assert.AreEqual(4 - Speed * 0.5, y, 1e-9, "the key dropped the target");
        }

        [Test]
        public void BeforeTheStartTheWorldIsHeld()
        {
            var sim = new Simulation(new SimConfig { Start = RunStart.OnControl });
            Assert.AreEqual(RunPhase.Waiting, sim.State.Phase);
            sim.Press(Key.Up, 0);
            sim.StepTo(1);
            At(sim, out _, out double y);
            Assert.AreEqual(0, y, 1e-12);
            Assert.AreEqual(0, sim.State.RunClock);
            sim.Control(RunControl.Start, 1);
            sim.StepTo(2);
            At(sim, out _, out y);
            Assert.AreEqual(Speed, y, 1e-9);
            Assert.AreEqual(1.0, sim.State.RunClock, 1e-9);
            Assert.AreEqual(1.0, sim.State.StartedAt, 1e-12);
            sim.Control(RunControl.Quit, 2);
            Assert.AreEqual(RunEnd.Quit, sim.State.End);
            var end = Drive.Events(sim, SimEventKind.RunEnd).Single();
            Assert.AreEqual((int)RunEnd.Quit, end.I0);
            Assert.AreEqual(1.0, end.D0, 1e-9);
        }

        [Test]
        public void TheDirectionIsTheKeysElseTheTargetElseTheFacing()
        {
            var sim = Drive.Sim();
            var walk = new HeroWalk(sim.State.Hero, new HeroRules(), 0);
            walk.Direction(0, 0, out double dx, out double dy);
            Assert.AreEqual((0.0, -1.0), (dx, dy), "down the screen at the start");
            walk.Target(sim, 3, 4, 0);
            walk.Direction(0, 0, out dx, out dy);
            Assert.AreEqual(0.6, dx, 1e-12);
            Assert.AreEqual(0.8, dy, 1e-12);
            walk.Press(sim, Key.Left, 0);
            walk.Direction(0, 0, out dx, out dy);
            Assert.AreEqual((-1.0, 0.0), (dx, dy));
        }

        [Test]
        public void TheSnapshotCopiesTheStateForTheClient()
        {
            var sim = Drive.Run(new FixtureSong().Beats(0, 10, 128), 0);
            sim.Press(Key.Right, 0);
            sim.StepTo(5);
            var snap = sim.Snapshot();
            Assert.AreEqual(sim.Now, snap.Now);
            Assert.AreEqual(Reliability.Reliable, snap.Beat);
            Assert.AreEqual(2, snap.CadenceBeats);
            Assert.AreEqual(sim.State.LastBeat, snap.LastBeat);
            snap.HeroAt(sim.Now + Tick / 2, out double x, out _);
            Assert.AreEqual(Speed * (5 + Tick / 2), x, 1e-9);
            sim.StepTo(6);
            Assert.AreEqual(5.0, snap.Now, "a copy: the sim moving on leaves it");
        }
    }
}
