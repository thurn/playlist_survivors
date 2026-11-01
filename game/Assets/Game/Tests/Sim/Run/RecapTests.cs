using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using PS.Game.Sim.Content;
using PS.Game.Sim.Level;

namespace PS.Game.Sim.Tests
{
    /// <summary>
    /// The run's recap (<see cref="RunRecap"/>, the end card's numbers): the kills, each weapon's
    /// damage and how long it was held, tallied by the sim and final from the run's end.
    /// </summary>
    public class RecapTests
    {
        static GameContent Content => HordeFixture.Content;
        static readonly double[] Footprint = { -18.5, -9.25, 18.5, -9.25, 24.75, 15.5, -24.75, 15.5 };

        /// <summary>The run's level on beats at 120 BPM, the hero standing, playing from the start; levels open no cards.</summary>
        static Simulation StartRun()
        {
            var config = RunLevel.Config(Content, null, Footprint);
            config.HeroStands = true;
            config.Start = RunStart.Immediately;
            config.Arsenal.Cards = false;
            var sim = new Simulation(config);
            foreach (var k in Drive.Known(new FixtureSong(3).Beats(0, 60, 120).Entries()
                         .Where(e => e.Kind == EntryKind.Beat || e.Kind == EntryKind.Tick)))
                sim.AddKnown(k.Entry, k.KnownAt);
            return sim;
        }

        [Test]
        public void AWeaponIsHeldFromJoiningTheSlotsUntilItsEvolutionAndAJumpIsntHeld()
        {
            var sim = StartRun();
            int sword = Content.WeaponIndex("sword"), wand = Content.WeaponIndex("magic-wand"), tear = Content.WeaponIndex("bloody-tear");
            var recap = sim.State.Recap;
            sim.StepTo(5);
            double wandFrom = sim.State.RunClock;
            Assert.Greater(wandFrom, 3, "the run plays");
            sim.Command(DebugCommand.GrantWeapon, wand);
            sim.StepTo(10);
            sim.Command(DebugCommand.GrantWeapon, wand); // a rank up doesn't restart it
            sim.StepTo(15);
            double evolvedAt = sim.State.RunClock;
            sim.Command(DebugCommand.GrantEvolution, tear);
            Assert.AreEqual(evolvedAt, recap.Held(sword), 1e-9, "the sword, held from the start, stops at its evolution");
            sim.Command(DebugCommand.JumpClock, 600);
            sim.StepTo(20);
            double now = sim.State.RunClock, jumped = 600 - evolvedAt;
            Assert.Greater(now, 600);
            Assert.AreEqual(evolvedAt, recap.Held(sword), 1e-9);
            Assert.AreEqual(now - wandFrom - jumped, recap.Held(wand), 1e-9, "the jump isn't time held");
            Assert.AreEqual(now - 600, recap.Held(tear), 1e-9);
            Assert.AreEqual(0, recap.Held(Content.WeaponIndex("garlic")), "never held");
        }

        [Test]
        public void TheRecapTalliesTheRunsKillsAndHitsAndIsFinalFromItsEnd()
        {
            var sim = StartRun();
            sim.StepTo(20);
            sim.Control(RunControl.Quit);
            Assert.AreEqual(RunPhase.Ended, sim.State.Phase);
            var events = Drive.Events(sim);
            var recap = sim.State.Recap;
            int kills = events.Count(e => e.Kind == SimEventKind.Death && e.AsDeath.Enemy >= 0);
            Assert.Greater(kills, 5, "a run with kills");
            Assert.AreEqual(kills, recap.Kills);
            var hits = events.Where(e => e.Kind == SimEventKind.Hit && !e.AsHit.OnHero).Select(e => e.AsHit).ToList();
            for (int w = 0; w < Content.Weapons.Length; w++)
                Assert.AreEqual(hits.Where(h => h.Weapon == w).Sum(h => h.Damage), recap.Damage[w], 1e-9, Content.Weapons[w].Id);
            var final = (recap.Kills, recap.Damage.ToArray(), recap.Held(Content.WeaponIndex("sword")));

            sim.StepTo(40);
            Assert.AreEqual(final, (recap.Kills, recap.Damage.ToArray(), recap.Held(Content.WeaponIndex("sword"))), "nothing tallies after the end");
        }
    }
}
