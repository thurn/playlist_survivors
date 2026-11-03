using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace Champions.Sim.Tests
{
    public class WeaponSchedulerTests
    {
        static Simulation Make(FixtureSong song = null)
        {
            var sim = new Simulation(new SimConfig { Loadout = Array.Empty<Upgrade>() });
            if (song != null) foreach (var k in Drive.Known(song.Entries())) sim.AddKnown(k.Entry, k.KnownAt);
            return sim;
        }
        [TestCase(80)] [TestCase(120)] [TestCase(174)]
        public void FasterActivationsUseMusicalSubdivisionsWithoutReducingHitPower(double bpm)
        {
            var sim = Make(new FixtureSong().Beats(0, 120, bpm));
            var actions = new List<WeaponActivation>();
            sim.Weapons.Bind(WeaponKind.Sword, _ => true, actions.Add);
            sim.StepTo(120);
            double period = 60 / bpm;
            var beats = Drive.Events(sim, SimEventKind.Beat).Where(e => e.BeatReliability == Reliability.Reliable).Select(e => e.T).ToArray();
            var settled = actions.Where(a => a.T > 10).ToArray();
            Assert.That(settled.All(a => beats.Any(t => Math.Abs((a.T-t) / (period/4) - Math.Round((a.T-t) / (period/4))) < 1e-6)), Is.True);
            Assert.That(settled.Average(a => a.DamageFactor), Is.GreaterThanOrEqualTo(1));
            Assert.That(settled.Sum(a => a.DamageFactor), Is.GreaterThanOrEqualTo(218));
            Assert.That(settled.All(a => !a.Spectral(0)), Is.True);
        }
        [Test]
        public void FractionalTempoCreatesOneSpectralStrikeEveryFourthActivation()
        {
            var sim = Make();
            var weapons = Enumerable.Range(0, CombatRules.WeaponCount).Select(i => CombatRules.Default.Weapon((WeaponKind)i)).ToArray();
            var supports = Enumerable.Range(0, CombatRules.SupportCount).Select(i => CombatRules.Default.Support((SupportKind)i)).ToArray();
            supports[(int)SupportKind.Tempo] = new SupportDefinition(SupportKind.Tempo, 5, WeaponCapabilities.Automatic, StatFamily.Strikes, .25);
            sim.State.Build = new WeaponBuild(new CombatRules(weapons, supports));
            sim.State.Build.Set(WeaponKind.Sword, 1); sim.State.Build.Set(SupportKind.Tempo, 1);
            var actions = new List<WeaponActivation>();
            sim.Weapons.Bind(WeaponKind.Sword, _ => true, actions.Add);
            sim.StepTo(4);
            Assert.That(actions.Select(a => a.Strikes), Is.EqualTo(new[] { 1, 1, 1, 2, 1, 1, 1, 2 }));
            Assert.That(actions[3].Spectral(1), Is.True);
            Assert.That(sim.State.Build.Weapon(WeaponKind.Sword).StrikeCarry, Is.EqualTo(0).Within(1e-9));
        }
        [Test]
        public void SharedAuraRefreshesExpiresAndFreezesAcrossHoldsWithoutBanking()
        {
            var sim = Make(); bool target = true;
            sim.State.Build.Set(SupportKind.Tempo, 1);
            var actions = new List<WeaponActivation>();
            sim.Weapons.Bind(WeaponKind.Sword, _ => target, actions.Add);
            sim.StepTo(1);
            sim.State.Build.Empower(sim.CombatTime(sim.Now));
            sim.StepTo(2);
            sim.State.Build.Empower(sim.CombatTime(sim.Now)); // refresh to 5, not stack to 7
            sim.State.Build.Set(WeaponKind.Sword, 8);
            sim.State.Build.Set(WeaponKind.Hammer, 1);
            Assert.That(sim.State.Build.AreaAt(2), Is.EqualTo(2));
            sim.Hold(HoldReason.Pause, true); int count = actions.Count;
            sim.StepTo(20.2);
            Assert.That(actions.Count, Is.EqualTo(count));
            Assert.That(sim.State.Build.AreaAt(sim.CombatTime(sim.Now)), Is.EqualTo(2));
            sim.Hold(HoldReason.Pause, false);
            sim.StepTo(20.4);
            Assert.That(actions.Count, Is.EqualTo(count), "no resume burst");
            target = false; sim.StepTo(24);
            Assert.That(sim.State.Build.AreaAt(sim.CombatTime(sim.Now)), Is.EqualTo(1), "empty ground cannot bank it");
            target = true; sim.StepTo(25);
            Assert.That(actions.Last().EmpowermentArea, Is.EqualTo(1));
            Assert.That(sim.State.Build.AreaAt(5 - 1e-8), Is.EqualTo(2));
            Assert.That(sim.State.Build.AreaAt(5), Is.EqualTo(1), "inactive exactly at expiry");
        }
        [Test]
        public void BeatlessUsesTwiceTheRosterCadenceAndPersistentPulses()
        {
            var sim = Make();
            sim.State.Build.Set(WeaponKind.Hammer, 1); sim.State.Build.Set(WeaponKind.EmberField, 1);
            sim.State.Build.Set(SupportKind.Tempo, 5);
            var hammer = new List<WeaponActivation>(); var field = new List<WeaponActivation>();
            sim.Weapons.Bind(WeaponKind.Hammer, _ => true, hammer.Add);
            sim.Weapons.Bind(WeaponKind.EmberField, _ => true, field.Add);
            sim.State.Build.Empower(); sim.StepTo(6);
            Assert.That(hammer.Select(a => a.T), Is.EqualTo(new[] { .75, 1.5, 2.25, 3, 3.75, 4.5, 5.25, 6 }));
            Assert.That(field.Sum(a => a.Strikes), Is.EqualTo(18));
            Assert.That(field.Where(a => a.T < 3).All(a => a.EmpowermentArea == 2), Is.True);
            Assert.That(field.Where(a => a.T >= 3).All(a => a.EmpowermentArea == 1), Is.True);
            Assert.That(field.All(a => a.Persistent && a.Spectral(0)), Is.True);
            Assert.That(sim.State.Build.Weapon(WeaponKind.EmberField).PulseSequence, Is.EqualTo(18));
        }
        [Test]
        public void TempoAndConfidenceChangesAreDeterministicAcrossFramePartitions()
        {
            var song = new FixtureSong().Beats(0, 15, 80).Beats(15, 30, 174).Gap(30, 38).Beats(38, 60, 120);
            (Simulation sim, List<WeaponActivation> actions) Run(bool frames)
            {
                var sim = Make(song); var actions = new List<WeaponActivation>();
                sim.State.Build.Set(SupportKind.Tempo, 1);
                sim.Weapons.Bind(WeaponKind.Sword, _ => true, actions.Add);
                if (frames) for (int i = 1; i <= 3600; i++) sim.StepTo(i / 60.0); else sim.StepTo(60);
                return (sim, actions);
            }
            var a = Run(false); var b = Run(true);
            Assert.That(a.sim.Hash, Is.EqualTo(b.sim.Hash));
            Assert.That(a.actions.Select(x => (x.T, x.Strikes)), Is.EqualTo(b.actions.Select(x => (x.T, x.Strikes))));
            Assert.That(a.actions.Sum(x => x.Strikes), Is.EqualTo(a.actions.Count + a.actions.Count / 10));
            for (int i = 1; i < a.actions.Count; i++) Assert.That(a.actions[i].T - a.actions[i - 1].T, Is.GreaterThan(.2));
        }
    }
}
