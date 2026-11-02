using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace Champions.Sim.Tests
{
    /// <summary>
    /// The attacks (design.md, "Attacks"; Decision 9): Sword Slash on the attack cadence, aimed at the densest
    /// group in reach and holding with nothing near, and each attack's ranks and evolution.
    /// </summary>
    public class AttackTests
    {
        const double Tick = Simulation.TickSeconds;

        static double Deg(double degrees) => degrees * Math.PI / 180;

        // ---- Sword Slash: when ----

        [TestCase(80, 1)]
        [TestCase(128, 2)]
        [TestCase(174, 2)]
        public void SwordSlashSwingsOnTheCadenceItsImpactOnTheBeat(double bpm, int count)
        {
            var sim = Arena.Run(Arena.Song(bpm), 30, Arena.Build("swordSlash:1"), new Dummy(2, 0, Arena.Tough));
            double period = 60.0 / bpm;
            var lockedAt = Drive.Events(sim, SimEventKind.Cadence).Single().T;
            var beats = new HashSet<double>(Arena.ReliableBeats(sim).Select(e => e.T));
            var strikes = Arena.Strikes(sim, BlowSource.SwordSlash).Where(e => e.T > lockedAt + 2 * count * period).ToList();
            Assert.Greater(strikes.Count, 15);
            var swings = Drive.Events(sim, SimEventKind.Swing);
            for (int i = 0; i < strikes.Count; i++)
            {
                var s = strikes[i];
                Assert.IsTrue(beats.Contains(s.T), $"{bpm} BPM: strike {i} at {s.T:F4} lands on a beat");
                if (i > 0) Assert.AreEqual(count * period, s.T - strikes[i - 1].T, 1e-6, $"{bpm} BPM: every {count} beats");
                Assert.AreEqual(BlowFlags.None, Arena.Flags(s), "the hero's own swing");
                var swing = swings.Single(w => w.D0 == s.T);
                Assert.That(s.T - swing.T, Is.InRange(0.25 - Tick, 0.25 + 1e-9), "planned a wind-up before its impact");
                Assert.AreEqual(s.D2, swing.D1, 1e-12, "the aim chosen at the wind-up");
                Assert.AreEqual(s.D3, swing.D2, 1e-12);
            }
        }

        [Test]
        public void BeatlessSwordSlashSwingsEverySecond()
        {
            foreach (var song in new[] { Arena.Song(0, 20), null })
            {
                var sim = Arena.Run(song, 20.05, Arena.Build("swordSlash:1"), new Dummy(2, 0, Arena.Tough));
                var strikes = Arena.Strikes(sim, BlowSource.SwordSlash);
                Assert.AreEqual(20, strikes.Count, song == null ? "no music at all" : "a beatless song");
                for (int i = 0; i < strikes.Count; i++) Assert.AreEqual(i + 1.0, strikes[i].T, 1e-9);
            }
        }

        // ---- Sword Slash: where ----

        [Test]
        public void SwordSlashAimsAtTheDensestGroupInReach()
        {
            var sim = Arena.Run(null, 1.05, Arena.Build("swordSlash:1"),
                new Dummy(2.5, 0, Arena.Tough), new Dummy(2.5, 0.6, Arena.Tough), new Dummy(2.6, -0.6, Arena.Tough),
                new Dummy(-0.5, 2.0, Arena.Tough));
            var strike = Arena.Strikes(sim, BlowSource.SwordSlash).Single();
            Assert.Greater(strike.D2, 0.97, "east, at the three, not the nearer one");
            Assert.AreEqual(3, strike.I2, "the three in the arc");
            Assert.AreEqual(3.0, strike.D4, 1e-12);
            Assert.AreEqual(Deg(75), strike.D5, 1e-12, "150 degrees");
        }

        [Test]
        public void WithNothingInReachSwordSlashAimsAtTheNearestWithinTheSlack()
        {
            var sim = Arena.Run(null, 1.05, Arena.Build("swordSlash:1"), new Dummy(0, -3.8, Arena.Tough), new Dummy(-4.4, 0, Arena.Tough));
            var strike = Arena.Strikes(sim, BlowSource.SwordSlash).Single();
            Assert.Less(strike.D3, -0.99, "toward the nearest, south");
            Assert.AreEqual(0, strike.I2, "out of reach as it swings");
        }

        [Test]
        public void ABossIsNeverWeighted()
        {
            var sim = Arena.Sim(null, Arena.Build("swordSlash:1"), new Dummy(2.5, 0.3, Arena.Tough), new Dummy(2.5, -0.3, Arena.Tough));
            sim.Enemies.Spawn(sim, -2.5, 0, 1.0, Arena.Tough, boss: true);
            sim.StepTo(1.05);
            Assert.Greater(Arena.Strikes(sim, BlowSource.SwordSlash).Single().D2, 0.99, "the two, not the nearer boss");
        }

        [Test]
        public void WithNothingNearTheHeroHoldsAReadyStanceAndTheCountCarriesOn()
        {
            var sim = Arena.Sim(null, Arena.Build("swordSlash:3"), new Dummy(6, 0, Arena.Tough));
            sim.StepTo(2.2);
            Assert.IsEmpty(Arena.Strikes(sim, BlowSource.SwordSlash), "nothing within reach plus 1 m");
            Assert.AreEqual(1, Drive.Events(sim, SimEventKind.SwingHold).Count, "one hold, however long");
            Assert.IsTrue(sim.Arsenal.Ready);
            Assert.IsTrue(sim.Snapshot().Ready);
            Assert.AreEqual(2, Drive.Events(sim, SimEventKind.Pulse).Count, "the cadence goes on");
            sim.Enemies.Edit(0).X = 2;
            sim.Enemies.Moved();
            sim.StepTo(4.05);
            var strikes = Arena.Strikes(sim, BlowSource.SwordSlash);
            Assert.AreEqual(new[] { 3.0, 4.0 }, strikes.Select(s => s.T).ToArray());
            Assert.IsFalse(sim.Arsenal.Ready);
            Assert.AreEqual(BlowFlags.None, Arena.Flags(strikes[0]));
            Assert.AreEqual(BlowFlags.Spin, Arena.Flags(strikes[1]), "the fourth strike of the cadence spins, the holds counted");
        }

        [Test]
        public void TheAimIsHeldFromTheWindUp()
        {
            var sim = Arena.Sim(null, Arena.Build("swordSlash:1"), new Dummy(2, 0, Arena.Tough));
            sim.StepTo(0.9);
            var swing = Drive.Events(sim, SimEventKind.Swing).Single();
            Assert.AreEqual(1.0, swing.D0, 1e-9);
            Assert.AreEqual(1.0, swing.D1, 1e-12);
            sim.Enemies.Edit(0).X = -2;
            sim.Enemies.Moved();
            sim.StepTo(1.05);
            var strike = Arena.Strikes(sim, BlowSource.SwordSlash).Single();
            Assert.AreEqual(1.0, strike.D2, 1e-12, "no re-aiming mid-swing");
            Assert.AreEqual(0, strike.I2);
        }

        [Test]
        public void HitsAndKillsCarryTheBlowsDirectionAndPower()
        {
            var sim = Arena.Run(null, 1.05, Arena.Build("swordSlash:1"), new Dummy(2, 0.5, 3), new Dummy(2, -0.5, 0.5));
            var hit = Arena.HitsOn(sim, 0).Single();
            Assert.AreEqual(SimEventKind.Hit, hit.Kind);
            Assert.AreEqual(1.0, hit.D2, 1e-12, "a base hit");
            Assert.AreEqual(2.0, hit.D3, 1e-12, "health left");
            Assert.AreEqual(2 / Math.Sqrt(4.25), hit.D0, 1e-9, "thrown away from the hero");
            Assert.AreEqual(0.5 / Math.Sqrt(4.25), hit.D1, 1e-9);
            var kill = Arena.HitsOn(sim, 1).Single();
            Assert.AreEqual(SimEventKind.Kill, kill.Kind);
            Assert.AreEqual(1.0, kill.D2, 1e-12);
            Assert.AreEqual(-0.5 / Math.Sqrt(4.25), kill.D1, 1e-9);
            Assert.AreEqual((2.0, -0.5), (kill.D3, kill.D4));
            Assert.AreEqual(1, sim.Enemies.Alive);
            Assert.AreEqual(1, sim.Arsenal.Kills);
            Assert.AreEqual(1.0, kill.T, 1e-12, "on the impact frame");
        }

        // ---- Each attack's ranks ----

        [Test]
        public void SwordSlashsRanksWidenItThenSpinThenThrowWaves()
        {
            var two = Arena.Strikes(Arena.Run(null, 1.05, Arena.Build("swordSlash:2"), new Dummy(2, 0, Arena.Tough)), BlowSource.SwordSlash).Single();
            Assert.AreEqual(3.8, two.D4, 1e-12);
            Assert.AreEqual(Deg(105), two.D5, 1e-12, "210 degrees");

            var three = Arena.Run(null, 8.05, Arena.Build("swordSlash:3"), new Dummy(2, 0, Arena.Tough));
            var strikes = Arena.Strikes(three, BlowSource.SwordSlash);
            Assert.AreEqual(8, strikes.Count);
            var hits = Arena.HitsOn(three, 0);
            for (int i = 0; i < 8; i++)
            {
                bool spin = i % 4 == 3;
                Assert.AreEqual(spin ? BlowFlags.Spin : BlowFlags.None, Arena.Flags(strikes[i]), $"strike {i}");
                Assert.AreEqual(spin ? ShapeKind.Circle : ShapeKind.Cone, Arena.Shape(strikes[i]), "a spin's 360 degrees");
                Assert.AreEqual(spin ? 1.5 : 1.0, hits[i].D2, 1e-12, "a spin hits for 1.5x");
            }
            Assert.AreEqual(1, Drive.Events(three, SimEventKind.Swing).Count(s => s.I0 == 1 && s.D0 == 4.0), "the spin is announced at its wind-up");

            var tempest = Arena.Run(null, 5.5, Arena.Build("swordSlash:4"), new Dummy(2, 0, Arena.Tough), new Dummy(10, 0, Arena.Tough));
            var waves = Drive.Events(tempest, SimEventKind.Wave).Where(w => w.I0 == (int)BlowSource.Tempest).ToList();
            Assert.AreEqual(5, waves.Count, "every slash throws a wave");
            Assert.AreEqual(12.0, waves[0].D4, 1e-12);
            Assert.AreEqual(0.0, waves[0].D2, 1e-12, "along the aim");
            Assert.AreEqual(5, Arena.HitsOn(tempest, 1, BlowSource.Tempest).Count, "each wave hits the enemy 10 m out, once");
        }

        [Test]
        public void OrbitBladesCircleALapABarEachSwordHittingOnceALap()
        {
            const double bpm = 128, period = 60 / bpm, lap = 4 * period;
            foreach (var (rank, swords) in new[] { (1, 2), (2, 3), (3, 4), (4, 6) })
            {
                var sim = Arena.Run(Arena.Song(bpm), 30, Arena.Build($"orbitBlades:{rank}"), new Dummy(4.0, 0, Arena.Tough));
                var orbit = Drive.Events(sim, SimEventKind.Orbit).Last();
                Assert.AreEqual(swords, orbit.I0, $"rank {rank}");
                Assert.AreEqual(2 * Math.PI / lap, orbit.D1, 0.01 * 2 * Math.PI / lap, "a lap a bar");
                Assert.AreEqual(swords, sim.Snapshot().OrbitSwords);
                if (rank == 4) continue;
                // A steady ring of 2.5 m misses an enemy at 4 m; the rank 3 ring's downbeat push reaches it.
                int hits = Arena.HitsOn(sim, 0, BlowSource.OrbitBlades).Count(h => h.T >= 10);
                if (rank < 3) Assert.AreEqual(0, hits, $"rank {rank}");
                else Assert.Greater(hits, 0, "pushed out on the downbeats");
            }

            var standing = Arena.Run(Arena.Song(bpm), 30, Arena.Build("orbitBlades:2"), new Dummy(2.5, 0, Arena.Tough));
            int laps = (int)Math.Floor(20 / lap);
            int got = Arena.HitsOn(standing, 0, BlowSource.OrbitBlades).Count(h => h.T >= 10 && h.T < 30);
            Assert.That(got, Is.InRange(3 * laps, 3 * (laps + 1)), "each of three swords once a lap");
            Assert.IsTrue(Arena.HitsOn(standing, 0).All(h => h.D2 == 0.6 && (BlowFlags)h.I2 == (BlowFlags.Spectral | BlowFlags.Effect)), "0.6x, spectral swords");
        }

        [Test]
        public void OrbitBladesRingPushesOutOnDownbeatsAndBreathesAtTheEvolution()
        {
            const double bpm = 128, period = 60 / bpm;
            var song = Arena.Song(bpm);
            var downbeat = Arena.ReliableBeats(Arena.Run(song, 20, Arena.Build())).First(b => b.Downbeat && b.T > 15).T;

            var pushed = Arena.Sim(song, Arena.Build("orbitBlades:3"));
            pushed.StepTo(downbeat + Tick);
            double since = (pushed.Now - downbeat) / period;
            Assert.AreEqual(2.5 + 1.5 * (1 - since) * (1 - since), pushed.Arsenal.OrbitRadius, 1e-6, "out to 4 m on the downbeat, easing back");
            Assert.Greater(pushed.Arsenal.OrbitRadius, 3.8);
            pushed.StepTo(downbeat + period + Tick);
            Assert.AreEqual(2.5, pushed.Arsenal.OrbitRadius, 1e-9, "back a beat later");

            var halo = Arena.Sim(song, Arena.Build("orbitBlades:4"));
            foreach (double at in new[] { downbeat + Tick, downbeat + 0.5 * period })
            {
                halo.StepTo(at);
                double phase = (halo.Now - downbeat) / period;
                Assert.AreEqual(3.5 + 1.5 * Math.Cos(2 * Math.PI * phase), halo.Arsenal.OrbitRadius, 1e-6, "breathing 2-5 m with the beat");
            }
            Assert.Less(halo.Arsenal.OrbitRadius, 2.05, "narrowest between beats");
        }

        [Test]
        public void BladeRainDropsSwordsOnTheDensestSpotsOnTheGrid()
        {
            const double bpm = 128, period = 60 / bpm;
            var song = Arena.Song(bpm);
            var cluster = new[] { new Dummy(5, 0, Arena.Tough), new Dummy(5.4, 0.4, Arena.Tough), new Dummy(5.4, -0.4, Arena.Tough) };
            var single = new Dummy(-5, 0, Arena.Tough);
            var far = new Dummy(12, 0, Arena.Tough);
            var all = cluster.Concat(new[] { single, far }).ToArray();
            foreach (var (rank, everyBeats, swords) in new[] { (1, 2, 1), (2, 1, 1), (3, 1, 2) })
            {
                var sim = Arena.Run(song, 30, Arena.Build($"bladeRain:{rank}"), all);
                var beats = Arena.ReliableBeats(sim).ToDictionary(b => b.T);
                var drops = Arena.Strikes(sim, BlowSource.BladeRain).Where(s => s.T > 10).ToList();
                var times = drops.Select(d => d.T).Distinct().ToList();
                Assert.Greater(times.Count, 10, $"rank {rank}");
                for (int i = 1; i < times.Count; i++) Assert.AreEqual(everyBeats * period, times[i] - times[i - 1], 1e-6, $"rank {rank}: every {everyBeats} beats");
                Assert.IsTrue(times.All(beats.ContainsKey), "on the beats");
                if (rank == 1) Assert.IsTrue(times.Any(t => beats[t].Downbeat), "in step with the bar");
                foreach (var t in times)
                {
                    var at = drops.Where(d => d.T == t).ToList();
                    Assert.AreEqual(swords, at.Count, $"rank {rank}: swords at a time");
                    Assert.AreEqual((5.0, 0.0), (at[0].D0, at[0].D1), "the densest spot first");
                    Assert.AreEqual(3, at[0].I2, "a 1.5 m impact");
                    if (swords == 2) Assert.AreEqual((-5.0, 0.0), (at[1].D0, at[1].D1), "then the next, an impact apart");
                }
                Assert.IsEmpty(Arena.HitsOn(sim, 4), "nothing beyond 10 m");
                Assert.IsTrue(Arena.HitsOn(sim, 0).All(h => h.D2 == 1.2), "1.2x");
            }

            var ring = Enumerable.Range(0, 8).Select(k => new Dummy(6 * Math.Cos(k * Math.PI / 4), 6 * Math.Sin(k * Math.PI / 4), Arena.Tough)).ToArray();
            var storm = Arena.Run(song, 30, Arena.Build("bladeRain:4"), ring);
            var downbeats = new HashSet<double>(Arena.ReliableBeats(storm).Where(b => b.Downbeat).Select(b => b.T));
            var stormDrops = Arena.Strikes(storm, BlowSource.BladeRain).Where(s => s.T > 10).ToList();
            Assert.IsTrue(stormDrops.All(d => downbeats.Contains(d.T)), "Sword Storm: on each downbeat");
            Assert.IsTrue(stormDrops.GroupBy(d => d.T).All(g => g.Count() == 8), "eight swords at once");
        }

        [Test]
        public void CrescentRollsWavesOnTheDownbeatsTheWayTheHeroMoves()
        {
            var song = Arena.Song(128);
            // The hero faces down the screen (0, -1) until it walks.
            var ahead = new Dummy(0, -8, Arena.Tough);
            var behind = new Dummy(0, 8, Arena.Tough);
            var side = new Dummy(8, 0, Arena.Tough);
            var diagonal = new Dummy(9, 9, Arena.Tough);
            foreach (var (rank, ways, hit) in new[] { (1, 1, new[] { 0 }), (2, 2, new[] { 0, 1 }), (3, 4, new[] { 0, 1, 2 }), (4, 1, new[] { 0, 1, 2, 3 }) })
            {
                var sim = Arena.Run(song, 30, Arena.Build($"crescent:{rank}"), ahead, behind, side, diagonal);
                var downbeats = new HashSet<double>(Arena.ReliableBeats(sim).Where(b => b.Downbeat).Select(b => b.T));
                var waves = Drive.Events(sim, SimEventKind.Wave).Where(w => w.T > 10).ToList();
                Assert.Greater(waves.Count, 3);
                Assert.IsTrue(waves.All(w => downbeats.Contains(w.T)), $"rank {rank}: on each downbeat");
                Assert.IsTrue(waves.GroupBy(w => w.T).All(g => g.Count() == ways), $"rank {rank}: {ways} at a time");
                if (rank == 1) Assert.AreEqual(-Math.PI / 2, waves[0].D2, 1e-12, "the way the hero faces");
                if (rank == 4)
                {
                    Assert.IsTrue(double.IsNaN(waves[0].D2), "Full Moon: a ring");
                    Assert.AreEqual(14.0, waves[0].D4, 1e-12);
                }
                int perEnemy = waves.Select(w => w.T).Distinct().Count();
                for (int e = 0; e < 4; e++)
                {
                    int got = Arena.HitsOn(sim, e, BlowSource.Crescent).Count(h => h.T > 10);
                    if (hit.Contains(e)) Assert.That(got, Is.InRange(perEnemy - 1, perEnemy), $"rank {rank}: enemy {e} once a wave");
                    else Assert.AreEqual(0, got, $"rank {rank}: enemy {e} missed");
                }
            }
        }

        [Test]
        public void PhantomsRepeatEachSwordSlashABeatLater()
        {
            // Beatless, a beat is 0.5 s. The hero faces down the screen, so its left is +x.
            foreach (var (rank, phantoms) in new[] { (1, 1), (3, 2), (4, 3) })
            {
                var sim = Arena.Run(null, 3.55, Arena.Build("swordSlash:1", $"phantom:{rank}"), new Dummy(2.5, 0, Arena.Tough));
                Assert.AreEqual(phantoms, sim.Arsenal.PhantomCount);
                var slashes = Arena.Strikes(sim, BlowSource.SwordSlash);
                var repeats = Arena.Strikes(sim, BlowSource.Phantom);
                Assert.AreEqual(3, slashes.Count);
                foreach (var slash in slashes)
                {
                    var at = repeats.Where(r => r.T == slash.T + 0.5).ToList();
                    Assert.AreEqual(phantoms, at.Count, $"rank {rank}: each phantom repeats it a beat later");
                    Assert.IsTrue(at.All(r => Arena.Flags(r) == BlowFlags.Spectral));
                }
                if (rank == 1)
                {
                    Assert.AreEqual(1.5, repeats[0].D0, 1e-12, "beside the hero, at its left");
                    Assert.AreEqual(0.0, repeats[0].D1, 1e-12);
                    Assert.AreEqual(0.7, Arena.HitsOn(sim, 0, BlowSource.Phantom)[0].D2, 1e-12, "0.7x");
                }
            }
        }

        [Test]
        public void FromRankTwoAPhantomAlsoRepeatsTheDash()
        {
            foreach (int rank in new[] { 1, 2 })
            {
                // Blade Rush from (0, 0) down the screen, 5 m; beatless, so on the beat: 3x within 2.25 m of its path.
                var sim = Arena.Sim(null, Arena.Build("swordSlash:1", $"phantom:{rank}"), new Dummy(1, -3, Arena.Tough), new Dummy(3.2, -3, Arena.Tough));
                Arena.Equip(sim, "bladeRush", 1);
                Arena.Dash(sim, 1.6);
                sim.StepTo(3.0);
                var repeats = Arena.Strikes(sim, BlowSource.Dash);
                var moves = Drive.Events(sim, SimEventKind.DashEcho);
                if (rank == 1)
                {
                    Assert.IsEmpty(repeats);
                    Assert.IsEmpty(moves);
                    continue;
                }
                var repeat = repeats.Single();
                Assert.AreEqual(1.6 + 0.5, repeat.T, 1e-9, "a beat later");
                Assert.AreEqual(BlowFlags.Repeat | BlowFlags.Spectral, Arena.Flags(repeat));
                Assert.AreEqual((ShapeKind.Path, 1.5, 0.0, 1.5, -5.0), (Arena.Shape(repeat), repeat.D0, Math.Round(repeat.D1, 9), repeat.D2, repeat.D3),
                    "its whole path, from where the phantom stands");
                var original = Arena.HitsOn(sim, 0, BlowSource.Dash).First();
                var copy = Arena.HitsOn(sim, 1, BlowSource.Dash).Single();
                Assert.AreEqual(0.7 * original.D2, copy.D2, 1e-12, "at its share");
                Assert.AreEqual((0, 2.1), (moves.Single().I0, Math.Round(moves.Single().T, 9)), "the phantom dashes too");
            }
        }
    }
}
