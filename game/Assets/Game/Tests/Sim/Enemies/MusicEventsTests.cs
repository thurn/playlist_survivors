using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace PS.Game.Sim.Tests
{
    /// <summary>
    /// The music-driven events (unity-game.md, Music-Driven Events): swarms land with their minute's
    /// jump, the set pieces come under their loudness conditions, and the signature minotaur slams on
    /// every 8th main beat.
    /// </summary>
    public class MusicEventsTests
    {
        const double Eps = 1e-9;

        /// <summary>
        /// A run of the music-driven events alone: no waves (an empty roster), the hero standing, and
        /// <paramref name="entries"/> fed at their normal known-at times.
        /// </summary>
        static Simulation Run(IEnumerable<StreamEntry> entries, ulong seed = 1, bool natural = true)
        {
            var config = HordeFixture.Config(minimum: 0, roster: new string[0], seed: seed, heroStands: true);
            config.Horde.MusicEvents = natural;
            var sim = new Simulation(config);
            foreach (var k in Drive.Known(entries)) sim.AddKnown(k.Entry, k.KnownAt);
            return sim;
        }

        static IEnumerable<StreamEntry> NoJumps(FixtureSong song) => song.Entries().Where(e => e.Kind != EntryKind.Jump);

        static StreamEntry Jump(double t, double db = 6) => StreamEntry.Jump(t, FixtureSong.StampOf(t + 0.3), db);

        static List<SetPieceEvent> Pieces(Simulation sim) => Drive.Events(sim, SimEventKind.SetPiece).Select(e => e.AsSetPiece).ToList();

        static int BeatIndex(Simulation sim, double t) => Array.FindIndex(sim.Stream.Beats.ToArray(), b => Math.Abs(b.T - t) < Eps);

        [Test]
        public void AMinutesSwarmArrivesOnTheBeatOfItsFirstBigJump()
        {
            // Minute 2's bat swarm, three attempts 5 s apart; the minute's first big jump at 141 s,
            // known about 2 s ahead, on a beat.
            var entries = NoJumps(new FixtureSong(3).Beats(0, 170, 120)).Append(Jump(130.5, db: 4)).Append(Jump(141)).Append(Jump(150));
            var sim = Run(entries);
            sim.StepTo(165);

            var swarms = Pieces(sim).Where(p => p.Kind == SetPieceKind.BatSwarm).ToList();
            Assert.AreEqual(3, swarms.Count, "the first and its two repeats");
            Assert.AreEqual(141, swarms[0].T, Eps, "with the jump, not after it (the 4 dB jump before it is too small)");
            Assert.AreEqual(141, swarms[0].CauseT, Eps);
            Assert.Less(swarms[0].KnownAt, 141 - 1, "known ahead");
            Assert.AreEqual(50, swarms[0].Members);
            Assert.AreEqual(146, swarms[1].T, Eps, "the repeats every 5 s of run clock, on beats");
            Assert.AreEqual(151, swarms[2].T, Eps);

            // They fly through as a flock, a step each main beat, and leave out of sight when their 10 s end.
            var spawned = Drive.Events(sim, SimEventKind.Spawn).Select(e => e.AsSpawn).Where(s => Math.Abs(s.T - 141) < Eps).Select(s => s.Enemy).ToList();
            Assert.AreEqual(50, spawned.Count);
            var steps = Drive.Events(sim, SimEventKind.EnemyStep).Select(e => e.AsEnemyStep).Where(s => s.Enemy == spawned[0] && s.T < 151).ToList();
            Assert.Greater(steps.Count, 5);
            foreach (var s in steps) Assert.GreaterOrEqual(BeatIndex(sim, s.Footplant), 0, "steps plant on main beats");
            var left = Drive.Events(sim, SimEventKind.Leave).Where(e => e.T < 151.5 + Eps).Select(e => e.AsLeave.Enemy).ToList();
            Assert.That(left, Has.Member(spawned[0]));
        }

        [Test]
        public void WithoutAJumpTheSwarmComesLateInItsMinute()
        {
            var sim = Run(NoJumps(new FixtureSong(3).Beats(0, 180, 120)));
            sim.StepTo(175);
            var first = Pieces(sim).First(p => p.Kind == SetPieceKind.BatSwarm);
            double late = 180 - HordeFixture.Content.Schedule.SwarmLateS;
            Assert.GreaterOrEqual(first.T, late - Eps);
            Assert.Less(first.T, late + 0.6, "on the next beat");
        }

        // The stampede follows a big jump after 8 s of quiet; after loud music the same jump brings none.
        [TestCase(LoudnessLevel.Quiet, true)]
        [TestCase(LoudnessLevel.Loud, false)]
        public void TheStampedeChargesInWithAJumpThatFollowsQuiet(LoudnessLevel before, bool stampede)
        {
            // After minute 2's swarms (the last ends at 190 s); minute 3's, at 10%, doesn't come on this seed.
            var song = new FixtureSong(3).Beats(0, 200, 120, level: before).Beats(200, 230, 120, level: LoudnessLevel.Loud);
            var sim = Run(NoJumps(song).Append(Jump(200)));
            sim.StepTo(210);

            var pieces = Pieces(sim).Where(p => p.Kind == SetPieceKind.Stampede).ToList();
            if (!stampede)
            {
                Assert.IsEmpty(pieces);
                return;
            }
            Assert.AreEqual(1, pieces.Count);
            Assert.AreEqual(200, pieces[0].T, Eps, "on the jump's beat");
            Assert.AreEqual(200, pieces[0].CauseT, Eps);
            Assert.AreEqual(HordeFixture.Content.Schedule.SetPieces.Stampede.Count, pieces[0].Members);
            // Two rows charging straight across, one way.
            var members = Drive.Events(sim, SimEventKind.Spawn).Select(e => e.AsSpawn).Where(s => Math.Abs(s.T - 200) < Eps).Select(s => s.Enemy).ToHashSet();
            var steps = Drive.Events(sim, SimEventKind.EnemyStep).Select(e => e.AsEnemyStep).Where(s => s.T > 200 && members.Contains(s.Enemy)).ToList();
            Assert.IsNotEmpty(steps);
            double dir = Math.Sign(steps[0].ToX - steps[0].FromX);
            foreach (var s in steps)
            {
                Assert.AreEqual(dir, Math.Sign(s.ToX - s.FromX), "all one way");
                Assert.AreEqual(s.FromY, s.ToY, 1e-9, "straight across");
            }
        }

        // Ring every 16 beats when loud, wedge every 16 beats when quiet, kick line after 16 medium beats.
        [TestCase(LoudnessLevel.Loud, SetPieceKind.Ring)]
        [TestCase(LoudnessLevel.Quiet, SetPieceKind.Wedge)]
        [TestCase(LoudnessLevel.Medium, SetPieceKind.KickLine)]
        public void EachLoudnessBringsItsOwnSetPiece(LoudnessLevel level, SetPieceKind expected)
        {
            var sim = Run(NoJumps(new FixtureSong(3).Beats(0, 340, 120, level: level)));
            sim.StepTo(335);

            var rules = HordeFixture.Content.Schedule.SetPieces;
            var pieces = Pieces(sim).Where(p => p.Kind >= SetPieceKind.Stampede && p.Kind <= SetPieceKind.Ring).ToList();
            Assert.IsNotEmpty(pieces, $"a {expected}");
            Assert.That(pieces.Select(p => p.Kind), Is.All.EqualTo(expected));
            double from = expected == SetPieceKind.Ring ? rules.Ring.FromS : expected == SetPieceKind.Wedge ? rules.Wedge.FromS : rules.KickLine.FromS;
            Assert.GreaterOrEqual(pieces[0].T, from - Eps, "from its start time");
            for (int k = 1; k < pieces.Count; k++) Assert.GreaterOrEqual(pieces[k].T - pieces[k - 1].T, rules.GapS - Eps, "one at a time, with gaps");
            if (expected != SetPieceKind.KickLine)
                foreach (var p in pieces) Assert.AreEqual(0, BeatIndex(sim, p.T) % 16, "every 16 beats");
        }

        [Test]
        public void TheRingHoldsBeyondTheViewThenClosesOnItsCentre()
        {
            var sim = HordeFixture.Empty(heroStands: true);
            foreach (var k in Drive.Known(new FixtureSong(3).Beats(0, 40, 120).Entries())) sim.AddKnown(k.Entry, k.KnownAt);
            sim.StepTo(5);
            sim.Command(DebugCommand.Ring);
            sim.StepTo(20);
            var ring = Pieces(sim).Single(p => p.Kind == SetPieceKind.Ring);
            var rules = HordeFixture.Content.Schedule.SetPieces.Ring;
            Assert.AreEqual(rules.Count, ring.Members);
            var hero = sim.State.Hero;
            var radii = new List<double>();
            for (int i = 0; i < sim.State.Enemies.Length; i++)
            {
                var e = sim.State.Enemies[i];
                if (e.Alive) radii.Add(Math.Sqrt((e.X - hero.X) * (e.X - hero.X) + (e.Y - hero.Y) * (e.Y - hero.Y)));
            }
            Assert.AreEqual(rules.Count, radii.Count);
            // Entrance (4 beats) and close (8 beats) take 6 s at 120 BPM: closed by now.
            foreach (double r in radii) Assert.AreEqual(rules.CloseToM, r, 0.6);
        }

        [Test]
        public void TheMinotaurLandsOnABeatAt90SAndSlamsOnEvery8thBeatWhereTheHeroStood()
        {
            var sim = Run(NoJumps(new FixtureSong(3).Beats(0, 130, 120)));
            sim.StepTo(125);

            var landing = Pieces(sim).Single(p => p.Kind == SetPieceKind.Signature);
            var rules = HordeFixture.Content.Schedule.Signature;
            Assert.AreEqual(rules.FromS, landing.T, Eps, "on the beat at 90 s");
            int landed = BeatIndex(sim, landing.T);
            var minotaur = Drive.Events(sim, SimEventKind.Spawn).Select(e => e.AsSpawn).Single(s => s.Role == HordeFixture.Content.RoleIndex(rules.Role));
            Assert.IsTrue(minotaur.Boss);
            var slams = Drive.Events(sim, SimEventKind.Attack).Select(e => e.AsAttack)
                .Where(a => !a.ByHero && a.Enemy == minotaur.Enemy && a.EnemyKind != EnemyAttackKind.Lunge && a.Impact <= sim.Now).ToList();
            Assert.GreaterOrEqual(slams.Count, 3);
            var hero = sim.State.Hero;
            for (int k = 0; k < slams.Count; k++)
            {
                Assert.AreEqual(EnemyAttackKind.Slam, slams[k].EnemyKind);
                Assert.AreEqual(landed + 8 * (k + 1), BeatIndex(sim, slams[k].Impact), "every 8th main beat from its landing");
                Assert.AreEqual(BeatIndex(sim, slams[k].Impact) - 2, BeatIndex(sim, slams[k].T), "telegraphed two beats before");
                Assert.AreEqual(hero.X, slams[k].SlamX, 1e-6, "at the mark under the hero");
                Assert.AreEqual(hero.Y, slams[k].SlamY, 1e-6);
            }
            // The standing hero never left its mark: each slam hits it for the signature's damage.
            var hits = Drive.Events(sim, SimEventKind.Hit).Select(e => e.AsHit).Where(h => h.OnHero && h.EnemyKind == EnemyAttackKind.Slam).ToList();
            foreach (var s in slams)
                Assert.That(hits.Select(h => h.T), Has.Some.EqualTo(s.Impact).Within(Eps));
        }

        [Test]
        public void TheEventsWaitForARun()
        {
            var sim = Run(new FixtureSong(3).Beats(0, 130, 120).Entries(), natural: false);
            sim.StepTo(125);
            Assert.IsEmpty(Pieces(sim));
        }

        [TestCase(DebugCommand.Stampede, SetPieceKind.Stampede)]
        [TestCase(DebugCommand.KickLine, SetPieceKind.KickLine)]
        [TestCase(DebugCommand.Wedge, SetPieceKind.Wedge)]
        [TestCase(DebugCommand.Ring, SetPieceKind.Ring)]
        [TestCase(DebugCommand.BatSwarm, SetPieceKind.BatSwarm)]
        [TestCase(DebugCommand.GhostSwarm, SetPieceKind.GhostSwarm)]
        [TestCase(DebugCommand.FlowerWall, SetPieceKind.FlowerWall)]
        [TestCase(DebugCommand.Minotaur, SetPieceKind.Signature)]
        public void ADebugCommandStartsItsEventOnTheNextMainBeatAndItEndsWithNothingVanishingInSight(DebugCommand command, SetPieceKind kind)
        {
            var sim = HordeFixture.Empty(arena: HordeFixture.GameArena(), heroStands: true);
            foreach (var k in Drive.Known(new FixtureSong(3).Beats(0, 200, 120).Entries())) sim.AddKnown(k.Entry, k.KnownAt);
            sim.StepTo(5.1);
            sim.Command(command);
            sim.StepTo(6);
            var piece = Pieces(sim).Single();
            Assert.AreEqual(kind, piece.Kind);
            Assert.AreEqual(5.5, piece.T, Eps, "the next main beat");
            int spawns = Drive.Events(sim, SimEventKind.Spawn).Count(e => Math.Abs(e.T - piece.T) < Eps);
            Assert.AreEqual(piece.Members, spawns);
            Assert.AreEqual(piece.Members, sim.State.EnemiesAlive);

            // Long after any event's end (the minotaur's retreat at 90 s), every member has left out of
            // sight or joined the horde: none is still scripted.
            sim.StepTo(110);
            for (int i = 0; i < sim.State.Enemies.Length; i++)
                if (sim.State.Enemies[i].Alive) Assert.IsFalse(sim.Horde.Scripts.Scripted(i), $"enemy {i} still scripted");
            var hero = sim.State.Hero;
            foreach (var e in Drive.Events(sim, SimEventKind.Leave).Select(e => e.AsLeave))
                Assert.IsTrue(sim.Horde.Music.Spawner.OutOfSight(e.X, e.Y, hero.X, hero.Y), "leaves only out of sight");
        }

        [Test]
        public void AnEventMakesRoomUnderTheCapFromTheFarthestOrdinaryEnemies()
        {
            var sim = HordeFixture.Empty(heroStands: true);
            foreach (var k in Drive.Known(new FixtureSong(3).Beats(0, 40, 120).Entries())) sim.AddKnown(k.Entry, k.KnownAt);
            int cap = HordeFixture.Content.Schedule.Cap;
            var hero = sim.State.Hero;
            for (int i = 0; i < cap; i++) HordeFixture.Place(sim, "zombie", hero.X + 3 + i * 0.1, hero.Y + 40);
            sim.StepTo(5.1);
            sim.Command(DebugCommand.BatSwarm);
            sim.StepTo(6);
            var swarm = Pieces(sim).Single();
            Assert.AreEqual(50, swarm.Members);
            Assert.AreEqual(cap, sim.State.EnemiesAlive);
            var left = Drive.Events(sim, SimEventKind.Leave).Select(e => e.AsLeave).ToList();
            Assert.AreEqual(50, left.Count);
            Assert.That(left.Select(l => l.Role), Is.All.EqualTo(HordeFixture.Content.RoleIndex("zombie")));
            // The zombies who stayed have only walked closer since.
            double Far(double x, double y) => Math.Sqrt((x - hero.X) * (x - hero.X) + (y - hero.Y) * (y - hero.Y));
            double nearestLeft = left.Min(l => Far(l.X, l.Y)), farthestStayed = 0;
            for (int i = 0; i < sim.State.Enemies.Length; i++)
            {
                var e = sim.State.Enemies[i];
                if (e.Alive && sim.Horde.Scripts.GroupOf(i) < 0) farthestStayed = Math.Max(farthestStayed, Far(e.X, e.Y));
            }
            Assert.Greater(nearestLeft, farthestStayed, "the farthest went");
        }
    }
}
