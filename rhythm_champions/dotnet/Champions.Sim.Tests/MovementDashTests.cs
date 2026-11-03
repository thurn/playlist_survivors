using System;
using System.Linq;
using NUnit.Framework;

namespace Champions.Sim.Tests
{
    public class MovementDashTests
    {
        static Simulation Sim(FixtureSong song = null)
        {
            // Live content, deliberately independent of the archived Arena/DashTests fixtures.
            var content = GameContent.Load(Checkout.In("Content"));
            var sim = new Simulation(new RunInputs { Loadout = Array.Empty<Upgrade>() }.Build(content));
            if (song != null)
                foreach (var k in Drive.Known(song.Entries())) sim.AddKnown(k.Entry, k.KnownAt);
            return sim;
        }

        [TestCase(true)]
        [TestCase(false)]
        public void TimingChangesOnlyTheReward(bool onBeat)
        {
            var sim = Sim(new FixtureSong().Beats(0, 20, 120, onsets: false));
            double heard = onBeat ? 6 : 6.125;
            Arena.Dash(sim, heard);
            var h = sim.State.Hero;
            Assert.AreEqual(onBeat, h.DashOnBeat);
            Assert.AreEqual(heard, h.DashStart, 1e-9, "no grid wait");
            Assert.AreEqual(0.15, h.DashEnd - h.DashStart, 1e-9);
            Assert.AreEqual(0.6, h.DashReadyAt - h.DashStart, 1e-9);
            Assert.AreEqual(0.25, h.IFramesUntil - h.DashStart, 1e-9);
            sim.StepTo(heard + 0.4);
            h.PositionAt(sim.Now, out double x, out double y);
            Assert.AreEqual(0, x, 1e-9);
            Assert.AreEqual(-4, y, 1e-9);
            Assert.AreEqual(onBeat, sim.State.Build.Weapon(WeaponKind.Sword).Empowered, "empty ground rewards timing");
            Assert.IsEmpty(Drive.Events(sim, SimEventKind.DashPart));
        }

        [Test]
        public void BeatlessDashRefreshesIndependentFlagsAndFormsRetainThem()
        {
            var sim = Sim();
            var build = sim.State.Build;
            build.Set(WeaponKind.OrbitBlades, 1);
            Arena.Dash(sim, 1);
            sim.Release(Key.Space, sim.Now);
            Assert.AreEqual(1, build.BeginActivation(WeaponKind.Sword, false), "skipped swing");
            Assert.IsTrue(build.Weapon(WeaponKind.Sword).Empowered);
            Assert.AreEqual(1.5, build.BeginActivation(WeaponKind.OrbitBlades, true), "one persistent pulse");
            Assert.IsTrue(build.Weapon(WeaponKind.Sword).Empowered);
            Assert.AreEqual(1, build.BeginActivation(WeaponKind.OrbitBlades, true), "next pulse returns to ordinary coverage");
            build.Set(WeaponKind.Bow, 1);
            Assert.IsFalse(build.Weapon(WeaponKind.Bow).Empowered, "new acquisitions wait for a dash");
            build.Set(WeaponKind.Sword, 8);
            Assert.IsTrue(build.Weapon(WeaponKind.Sword).Empowered);
            Arena.Dash(sim, 2);
            sim.Release(Key.Space, sim.Now);
            Arena.Dash(sim, 3);
            Assert.AreEqual(3, sim.State.Hero.Dashes);
            double wholeActivation = build.BeginActivation(WeaponKind.Sword, true);
            Assert.AreEqual(1.5, wholeActivation, "two dashes cannot stack");
            Assert.AreEqual(1, build.BeginActivation(WeaponKind.Sword, true));
            Assert.IsTrue(build.Weapon(WeaponKind.Bow).Empowered);
        }

        [Test]
        public void BufferedLiveDashJudgesTheOriginalHeardPress()
        {
            var sim = Sim(new FixtureSong().Beats(0, 20, 120, onsets: false));
            Arena.Dash(sim, 6);
            sim.Release(Key.Space, sim.Now);
            sim.State.Build.BeginActivation(WeaponKind.Sword, true);
            Arena.Dash(sim, 6.5);
            Assert.IsTrue(sim.State.Hero.DashWaiting);
            Assert.IsFalse(sim.State.Build.Weapon(WeaponKind.Sword).Empowered);
            sim.StepTo(6.7);
            Assert.AreEqual(6.6, sim.State.Hero.DashStart, 1e-9);
            Assert.IsTrue(sim.State.Hero.DashOnBeat, "6.5 is on the beat; the buffered firing time 6.6 is outside its window");
            Assert.IsTrue(sim.State.Build.Weapon(WeaponKind.Sword).Empowered);
        }

        [Test]
        public void LandingBlastIsOptionalAndConsumesNoWeaponFlags()
        {
            var sim = Sim();
            Arena.Dash(sim, 1);
            sim.StepTo(1.5);
            Assert.IsEmpty(Drive.Events(sim, SimEventKind.LandingBlast));
            sim.State.Build.Set(SupportKind.LandingBlast, 1);
            sim.Release(Key.Space, sim.Now);
            Arena.Dash(sim, 2);
            sim.StepTo(2.4);
            var blast = Drive.Events(sim, SimEventKind.LandingBlast).Single();
            Assert.AreEqual(2.15, blast.T, 1e-9);
            Assert.AreEqual(-8, blast.D1, 1e-9);
            Assert.IsTrue(sim.State.Build.Weapon(WeaponKind.Sword).Empowered);
        }

        [TestCase(true)]
        [TestCase(false)]
        public void BlastUsesLegalEndpointScalesDamageAndAreaAndNeverRepeats(bool onBeat)
        {
            var sim = Sim(new FixtureSong().Beats(0, 20, 120, onsets: false));
            var build = sim.State.Build;
            build.Set(SupportKind.LandingBlast, 2);
            build.Set(SupportKind.Might, 5);
            build.Set(SupportKind.Reach, 5);
            build.Set(SupportKind.Echo, 5);
            build.Set(SupportKind.Tempo, 5);
            var walls = new Walls();
            var wall = walls.Add(2, -10, 3, 10, hits: 1);
            sim.UseStructures(walls);
            var targets = new Dummies();
            int endpoint = targets.Add(1.6, 0), beyond = targets.Add(4.5, 0);
            sim.Strikes.Add(targets);
            double heard = onBeat ? 6 : 6.125;
            sim.StepTo(heard);
            sim.Press(Key.Right, heard);
            sim.Press(Key.Space, heard);
            sim.Release(Key.Right, heard);
            sim.StepTo(heard + 1);
            var blast = Drive.Events(sim, SimEventKind.LandingBlast).Single();
            Assert.AreEqual(1.6, blast.D0, 1e-9, "wall blocks travel even if the landing blast breaks it");
            Assert.AreEqual(0, blast.D1, 1e-9);
            Assert.AreEqual(2 * Math.Sqrt(1.5), blast.D2, 1e-9, "coverage area rather than radius");
            var hit = targets.On(endpoint).Single();
            Assert.AreEqual(1.4 * 1.5, hit.Damage, 1e-9, "no timed damage multiplier");
            Assert.AreEqual(BlowSource.LandingBlast, hit.Source);
            Assert.IsEmpty(targets.On(beyond));
            Assert.IsTrue(wall.Broken);
            Assert.AreEqual(1, wall.Struck.Count, "no travel hit or repeats");
            Assert.AreEqual(onBeat, build.Weapon(WeaponKind.Sword).Empowered);
        }

        [Test]
        public void LiveDashRejectsArchivedFormsAndIgnoresArchivedMods()
        {
            var sim = Sim();
            Assert.Throws<InvalidOperationException>(() => sim.Dashes.Equip(sim, sim.Dashes.Rules.IndexOf("bladeRush"), 1));
            sim.Arsenal.Set(sim, Upgrade.Parse("quickstep:2"));
            sim.Arsenal.Set(sim, Upgrade.Parse("momentum:2"));
            var targets = new Dummies();
            targets.Add(0, -2);
            sim.Strikes.Add(targets);
            Arena.Dash(sim, 1);
            sim.StepTo(3);
            Assert.AreEqual(0.6, sim.State.Hero.DashCooldownS, 1e-9);
            Assert.IsEmpty(targets.Struck);
            Assert.IsEmpty(Drive.Events(sim, SimEventKind.DashPart));
        }
    }
}
