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
        [TestCase(80)]
        [TestCase(120)]
        [TestCase(174)]
        public void ActivationsUseCommittedBeatsWithComparableSustainedOutput(double bpm)
        {
            var sim = Make(new FixtureSong().Beats(0, 120, bpm));
            var actions = new List<WeaponActivation>();
            sim.Weapons.Bind(WeaponKind.Sword, _ => true, actions.Add);
            sim.StepTo(120);
            var reliable = Drive.Events(sim, SimEventKind.Beat).Where(e => e.BeatReliability == Reliability.Reliable).Select(e => e.T).ToHashSet();
            var settled = actions.Where(a => a.T > 10).ToArray();
            Assert.That(settled.All(a => reliable.Contains(a.T)), Is.True);
            Assert.That(settled.Sum(a => a.Strikes * a.DamageFactor), Is.EqualTo(110).Within(2));
            Assert.That(settled.All(a => !a.Spectral(0)), Is.True);
        }
        [Test]
        public void FractionalTempoCreatesOneSpectralStrikeEveryFourthActivation()
        {
            var sim = Make();
            // Author a 25% Tempo support to test fractional values independently of shipped 10% levels.
            var weapons = Enumerable.Range(0, CombatRules.WeaponCount).Select(i => CombatRules.Default.Weapon((WeaponKind)i)).ToArray();
            var supports = Enumerable.Range(0, CombatRules.SupportCount).Select(i => CombatRules.Default.Support((SupportKind)i)).ToArray();
            supports[(int)SupportKind.Tempo] = new SupportDefinition(SupportKind.Tempo, 5, WeaponCapabilities.Automatic, StatFamily.Strikes, 0.25);
            sim.State.Build = new WeaponBuild(new CombatRules(weapons, supports));
            sim.State.Build.Set(WeaponKind.Sword, 1);
            sim.State.Build.Set(SupportKind.Tempo, 1);
            var actions = new List<WeaponActivation>();
            sim.Weapons.Bind(WeaponKind.Sword, _ => true, actions.Add);
            sim.StepTo(8);
            Assert.That(actions.Select(a => a.Strikes), Is.EqualTo(new[] { 1, 1, 1, 2, 1, 1, 1, 2 }));
            Assert.That(actions[3].Spectral(1), Is.True);
            Assert.That(sim.State.Build.Weapon(WeaponKind.Sword).StrikeCarry, Is.EqualTo(0).Within(1e-9));
        }
        [Test]
        public void MinorTempoSurvivesHoldsNoTargetAndFormChangesWithoutBacklog()
        {
            var sim = Make();
            sim.State.Build.Set(SupportKind.Tempo, 1);
            bool target = true;
            var actions = new List<WeaponActivation>();
            sim.Weapons.Bind(WeaponKind.Sword, _ => target, actions.Add);
            sim.StepTo(3);
            sim.State.Build.Empower();
            target = false;
            sim.StepTo(10);
            sim.State.Build.Set(WeaponKind.Sword, 8);
            sim.Hold(HoldReason.Pause, true);
            target = true;
            sim.StepTo(20.2);
            Assert.That(actions.Count, Is.EqualTo(3));
            Assert.That(sim.State.Build.Weapon(WeaponKind.Sword).StrikeCarry, Is.EqualTo(0.3).Within(1e-9));
            sim.Hold(HoldReason.Pause, false);
            sim.StepTo(20.9);
            Assert.That(actions.Count, Is.EqualTo(3));
            sim.StepTo(27);
            Assert.That(actions.Count, Is.EqualTo(10));
            Assert.That(actions.Last().Strikes, Is.EqualTo(2));
            Assert.That(actions[3].EmpowermentArea, Is.EqualTo(1.5));
            Assert.That(actions.Skip(4).All(a => a.EmpowermentArea == 1), Is.True);
        }
        [Test]
        public void BeatlessUsesEachWeaponsAuthoredSecondsAndPersistentPulses()
        {
            var sim = Make();
            sim.State.Build.Set(WeaponKind.Hammer, 1);
            sim.State.Build.Set(WeaponKind.EmberField, 1);
            sim.State.Build.Set(SupportKind.Tempo, 5);
            var hammer = new List<WeaponActivation>(); var field = new List<WeaponActivation>();
            sim.Weapons.Bind(WeaponKind.Hammer, _ => true, hammer.Add);
            sim.Weapons.Bind(WeaponKind.EmberField, _ => true, field.Add);
            sim.State.Build.Empower();
            sim.StepTo(6);
            Assert.That(hammer.Select(a => a.T), Is.EqualTo(new[] { 1.5, 3, 4.5, 6 }));
            Assert.That(field.Sum(a => a.Strikes), Is.EqualTo(9));
            Assert.That(field[0].EmpowermentArea, Is.EqualTo(1.5));
            Assert.That(field.Skip(1).All(a => a.EmpowermentArea == 1), Is.True);
            Assert.That(field.All(a => a.Persistent && a.Spectral(0)), Is.True);
            Assert.That(sim.State.Build.Weapon(WeaponKind.EmberField).PulseSequence, Is.EqualTo(9));
            Assert.That(Drive.Events(sim, SimEventKind.WeaponPulse).Count, Is.EqualTo(9));
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
                if (frames) for (int i = 1; i <= 3600; i++) sim.StepTo(i / 60.0);
                else sim.StepTo(60);
                return (sim, actions);
            }
            var a = Run(false); var b = Run(true);
            Assert.That(a.sim.Hash, Is.EqualTo(b.sim.Hash));
            Assert.That(a.actions.Select(x => (x.T, x.Strikes)), Is.EqualTo(b.actions.Select(x => (x.T, x.Strikes))));
            Assert.That(a.actions.Sum(x => x.Strikes), Is.EqualTo(a.actions.Count + a.actions.Count / 10));
            for (int i = 1; i < a.actions.Count; i++) Assert.That(a.actions[i].T - a.actions[i - 1].T, Is.GreaterThan(0.3));
        }
    }
}
