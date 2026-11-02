using System;
using System.Linq;
using NUnit.Framework;

namespace Champions.Sim.Tests
{
    /// <summary>
    /// The mods (design.md, "Mods"): each applies to what carries its tag and nothing else. Beatless unless a
    /// test needs the beat, where a beat is 0.5 s and Sword Slash strikes every second.
    /// </summary>
    public class ModTests
    {
        static double Deg(double degrees) => degrees * Math.PI / 180;

        [Test]
        public void WideArcsWidensWhatCarriesAreaOnly()
        {
            // An enemy whose edge is 3.3 m out: past Sword Slash's 3 m, inside it with +40% area (4.2 m: "area" grows reach).
            var plain = Arena.Run(null, 1.05, Arena.Build("swordSlash:1", "orbitBlades:1"), new Dummy(3.8, 0, Arena.Tough));
            var wide = Arena.Run(null, 1.05, Arena.Build("swordSlash:1", "orbitBlades:1", "wideArcs:1"), new Dummy(3.8, 0, Arena.Tough));
            Assert.AreEqual(0, Arena.Strikes(plain, BlowSource.SwordSlash).Single().I2);
            var slash = Arena.Strikes(wide, BlowSource.SwordSlash).Single();
            Assert.AreEqual(3 * 1.4, slash.D4, 1e-12, "Sword Slash carries Area");
            Assert.AreEqual(Deg(75), slash.D5, 1e-12, "the arc's angle stays");
            Assert.AreEqual(1, slash.I2);
            Assert.AreEqual(2.5, wide.Arsenal.OrbitRadius, 1e-12, "Orbit Blades carries Summon, not Area");

            var two = Arena.Strikes(Arena.Run(null, 1.05, Arena.Build("swordSlash:1", "wideArcs:2"), new Dummy(2, 0, Arena.Tough)), BlowSource.SwordSlash).Single();
            Assert.AreEqual(3 * 1.8, two.D4, 1e-12, "+80%");

            var waves = Drive.Events(Arena.Run(Arena.Song(128), 20, Arena.Build("crescent:1", "wideArcs:1")), SimEventKind.Wave);
            Assert.AreEqual(10 * 1.4, waves[0].D4, 1e-12, "Crescent carries Area");
            Assert.AreEqual(2 * 1.4, waves[0].D3, 1e-12);
        }

        [Test]
        public void TwinStrikeEchoesStrikesHalfABeatLaterSpectral()
        {
            foreach (var (rank, share) in new[] { (1, 0.5), (2, 0.75) })
            {
                var sim = Arena.Run(null, 3.3, Arena.Build("swordSlash:1", "bladeRain:1", $"twinStrike:{rank}"), new Dummy(2, 0, Arena.Tough));
                var strikes = Arena.Strikes(sim, BlowSource.SwordSlash);
                var hero = strikes.Where(s => Arena.Flags(s) == BlowFlags.None).ToList();
                var echoes = strikes.Where(s => Arena.Flags(s) == (BlowFlags.Echo | BlowFlags.Spectral)).ToList();
                Assert.AreEqual(3, hero.Count);
                Assert.AreEqual(hero.Select(s => s.T + 0.25), echoes.Select(s => s.T), "half a beat later, and no echo of an echo");
                Assert.AreEqual((hero[0].D0, hero[0].D2), (echoes[0].D0, echoes[0].D2), "the same slash again");
                var hits = Arena.HitsOn(sim, 0, BlowSource.SwordSlash);
                Assert.AreEqual(share, hits.Single(h => h.T == echoes[0].T).D2, 1e-12, $"rank {rank}");
                var rain = Arena.Strikes(sim, BlowSource.BladeRain);
                Assert.Greater(rain.Count, 2);
                Assert.IsTrue(rain.All(r => (Arena.Flags(r) & BlowFlags.Echo) == 0), "Blade Rain carries no Strike");
            }
        }

        [Test]
        public void TempoHalvesPeriodsWithTheExtraStrikesSpectral()
        {
            // 128 BPM: Sword Slash every 2 beats, so with Tempo every beat, the hero swinging every other one.
            const double bpm = 128, period = 60 / bpm;
            var song = Arena.Song(bpm);
            var build = new[] { "swordSlash:1", "crescent:1", "bladeRain:1" };
            var plain = Arena.Run(song, 30, Arena.Build(build), new Dummy(2, 0, Arena.Tough));
            var fast = Arena.Run(song, 30, Arena.Build(build.Append("tempo:1").ToArray()), new Dummy(2, 0, Arena.Tough));
            var beats = Arena.ReliableBeats(fast).Select(b => b.T).ToHashSet();

            var slashes = Arena.Strikes(fast, BlowSource.SwordSlash).Where(s => s.T > 10).ToList();
            for (int i = 1; i < slashes.Count; i++) Assert.AreEqual(period, slashes[i].T - slashes[i - 1].T, 1e-6, "every beat");
            Assert.IsTrue(slashes.All(s => beats.Contains(s.T)), "on the beats");
            var own = slashes.Where(s => Arena.Flags(s) == BlowFlags.None).ToList();
            var extra = slashes.Where(s => Arena.Flags(s) == BlowFlags.Spectral).ToList();
            Assert.That(own.Count - extra.Count, Is.InRange(-1, 1), "every other strike is spectral");
            for (int i = 1; i < own.Count; i++) Assert.AreEqual(2 * period, own[i].T - own[i - 1].T, 1e-6, "the hero's own swing keeps its period");
            var swings = Drive.Events(fast, SimEventKind.Swing).Select(s => s.D0).ToHashSet();
            Assert.IsTrue(own.All(s => swings.Contains(s.T)) && extra.All(s => !swings.Contains(s.T)), "only the hero's swings wind up");

            int Waves(Simulation sim) => Drive.Events(sim, SimEventKind.Wave).Count(w => w.T > 10);
            Assert.AreEqual(2 * Waves(plain), Waves(fast), 1, "Crescent: twice a bar");
            int Drops(Simulation sim) => Arena.Strikes(sim, BlowSource.BladeRain).Count(s => s.T > 10);
            Assert.AreEqual(Drops(plain), Drops(fast), "Blade Rain keeps its period at rank 1");

            var all = Arena.Run(song, 30, Arena.Build(build.Append("tempo:2").ToArray()), new Dummy(2, 0, Arena.Tough));
            Assert.AreEqual(2 * Drops(plain), Drops(all), 1, "at rank 2 every attack does");

            var beatless = Arena.Strikes(Arena.Run(null, 4.1, Arena.Build("swordSlash:1", "tempo:1"), new Dummy(2, 0, Arena.Tough)), BlowSource.SwordSlash);
            Assert.AreEqual(new[] { 1.0, 1.5, 2.0, 2.5, 3.0, 3.5, 4.0 }, beatless.Select(s => s.T).ToArray(), "beatless: every half second");
        }

        [Test]
        public void TempoAtABeatPerSlashStrikesOnTheHalfBeats()
        {
            const double bpm = 80, period = 60 / bpm;
            var sim = Arena.Run(Arena.Song(bpm), 30, Arena.Build("swordSlash:1", "tempo:1"), new Dummy(2, 0, Arena.Tough));
            var extra = Arena.Strikes(sim, BlowSource.SwordSlash).Where(s => s.T > 10 && Arena.Flags(s) == BlowFlags.Spectral).ToList();
            var beats = Arena.ReliableBeats(sim).Select(b => b.T).ToList();
            Assert.Greater(extra.Count, 10);
            foreach (var s in extra)
            {
                double before = beats.Last(b => b < s.T);
                Assert.AreEqual(before + 0.5 * period, s.T, 1e-6, "1 beat to a half beat");
            }
        }

        [Test]
        public void KeenEdgeSharpensStrikesAndDoublesThemOnDownbeats()
        {
            var song = Arena.Song(128);
            var sim = Arena.Run(song, 30, Arena.Build("swordSlash:1", "orbitBlades:1", "crescent:1", "keenEdge:1"),
                new Dummy(2, 0, Arena.Tough), new Dummy(2.5, 0.2, Arena.Tough), new Dummy(0, -8, Arena.Tough));
            var downbeats = Arena.ReliableBeats(sim).Where(b => b.Downbeat).Select(b => b.T).ToHashSet();
            var slashHits = Arena.HitsOn(sim, 0, BlowSource.SwordSlash).Where(h => h.T > 10).ToList();
            Assert.IsTrue(slashHits.Any(h => downbeats.Contains(h.T)) && slashHits.Any(h => !downbeats.Contains(h.T)));
            foreach (var h in slashHits) Assert.AreEqual(downbeats.Contains(h.T) ? 3.0 : 1.5, h.D2, 1e-12, "+50%, 2x on a downbeat");
            Assert.IsTrue(Arena.HitsOn(sim, -1, BlowSource.OrbitBlades).All(h => h.D2 == 0.6), "Orbit Blades carries no Strike");
            Assert.IsTrue(Arena.HitsOn(sim, 2, BlowSource.Crescent).All(h => h.D2 == 1.0), "nor does Crescent");

            // Beatless, the pulse's bars are 2 s: Sword Slash's second-long strikes fall on a downbeat every other time.
            var two = Arena.Run(null, 2.05, Arena.Build("swordSlash:1", "keenEdge:2"), new Dummy(2, 0, Arena.Tough));
            Assert.AreEqual(new[] { 2.0, 4.0 }, Arena.HitsOn(two, 0).Select(h => h.D2).ToArray(), "+100%, and 2x on the pulse's downbeat");
        }

        [Test]
        public void QuickstepShortensTheDashsCooldownOnly()
        {
            Assert.AreEqual(0.6, Arena.Sim(null, Arena.Build("swordSlash:1")).Arsenal.DashCooldownS(0.6));
            Assert.AreEqual(0.45, Arena.Sim(null, Arena.Build("quickstep:1")).Arsenal.DashCooldownS(0.6));
            Assert.AreEqual(0.35, Arena.Sim(null, Arena.Build("quickstep:2")).Arsenal.DashCooldownS(0.6));
            var sim = Arena.Run(null, 3.05, Arena.Build("swordSlash:1", "quickstep:2"), new Dummy(2, 0, Arena.Tough));
            Assert.AreEqual(0.35, sim.Dashes.CooldownS, 1e-12, "the dash rules take it");
            Assert.AreEqual(3, Arena.Strikes(sim, BlowSource.SwordSlash).Count, "the attacks' timing is untouched");
        }

        [Test]
        public void MomentumGivesThePlainDashATrail()
        {
            // 128 BPM, the plain Dash from (0, 0) down the screen, 4 m, heard on beat 12 or a quarter beat after it.
            const double p = 60 / 128.0;
            foreach (var (rank, onBeat) in new[] { (0, false), (1, false), (1, true), (2, false) })
            {
                var build = rank > 0 ? Arena.Build($"momentum:{rank}") : Arena.Build();
                var sim = Arena.Sim(Arena.Song(128), build, new Dummy(0.5, -2, Arena.Tough), new Dummy(1.6, -2, Arena.Tough));
                double heard = 12 * p + (onBeat ? 0 : p / 4);
                Arena.Dash(sim, heard);
                sim.StepTo(heard + 4 * p);
                Assert.AreEqual(onBeat, Drive.Events(sim, SimEventKind.HeroDash).Single().OnBeat);
                var near = Arena.HitsOn(sim, 0, BlowSource.Dash);
                var far = Arena.HitsOn(sim, 1, BlowSource.Dash);
                if (rank == 0)
                {
                    Assert.IsEmpty(near, "the plain Dash deals nothing alone");
                    continue;
                }
                Assert.AreEqual(rank, near.Count, $"rank {rank}: once as the hero passes, then again on the next beat");
                Assert.AreEqual(onBeat ? 1.6 : 0.8, near[0].D2, 1e-12, "0.8x, 2x on the beat");
                Assert.AreEqual(1.0, near[0].D0, 1e-12, "thrown off the path");
                Assert.AreEqual(onBeat ? 1 : 0, far.Count, "a 1 m trail, half again as wide on the beat");
                if (rank == 2)
                {
                    var beats = Arena.ReliableBeats(sim).Select(b => b.T).ToList();
                    Assert.AreEqual(beats.First(b => b > heard + 0.15), near[1].T, 1e-9, "the next beat");
                }
            }
        }

        [Test]
        public void MomentumDoublesTheDashesOwnBlowsOnly()
        {
            var plain = Arena.Sim(null, Arena.Build("swordSlash:1"), new Dummy(1, -2.5, Arena.Tough));
            var doubled = Arena.Sim(null, Arena.Build("swordSlash:1", "momentum:1"), new Dummy(1, -2.5, Arena.Tough));
            foreach (var sim in new[] { plain, doubled })
            {
                Arena.Equip(sim, "bladeRush", 1);
                Arena.Dash(sim, 1.4);
                sim.StepTo(2.05);
            }
            double rush = Arena.HitsOn(plain, 0, BlowSource.Dash).Single().D2;
            Assert.AreEqual(2 * rush, Arena.HitsOn(doubled, 0, BlowSource.Dash).Max(h => h.D2), 1e-12, "Blade Rush's slashes, 2x");
            Assert.IsTrue(Arena.HitsOn(doubled, 0, BlowSource.SwordSlash).All(h => h.D2 == 1.0), "Sword Slash carries no Dash");
        }

        [Test]
        public void AfterimageRepeatsTheDashABeatLater()
        {
            foreach (var (rank, delays) in new[] { (1, new[] { 0.5 }), (2, new[] { 0.5, 1.0 }) })
            {
                // Beatless, a beat is 0.5 s. Blade Rush from (0, 0) down the screen, 5 m.
                var sim = Arena.Sim(null, Arena.Build("swordSlash:1", $"afterimage:{rank}"), new Dummy(1, -2.5, Arena.Tough));
                Arena.Equip(sim, "bladeRush", 1);
                Arena.Dash(sim, 2.1);
                sim.StepTo(4.0);
                var repeats = Arena.Strikes(sim, BlowSource.Dash);
                Assert.AreEqual(delays.Length, repeats.Count, $"rank {rank}");
                for (int i = 0; i < delays.Length; i++) Assert.AreEqual(2.1 + delays[i], repeats[i].T, 1e-9, $"rank {rank}: a beat apart");
                Assert.IsTrue(repeats.All(s => Arena.Flags(s) == (BlowFlags.Repeat | BlowFlags.Spectral)), "spectral, and repeated no further");
                Assert.IsTrue(repeats.All(s => s.D0 == 0 && s.D2 == 0 && s.D3 == -5), "along its whole path");
                var hits = Arena.HitsOn(sim, 0, BlowSource.Dash);
                Assert.AreEqual(1 + delays.Length, hits.Count);
                Assert.IsTrue(hits.All(h => h.D2 == hits[0].D2), "and its damage");
                var moves = Drive.Events(sim, SimEventKind.DashEcho);
                Assert.AreEqual(delays.Length, moves.Count);
                Assert.IsTrue(moves.All(m => m.I0 == -1 && m.D3 == -5 && Math.Abs(m.D4 - 0.22) < 1e-9), "the afterimages move as the dash did");
                Assert.IsTrue(Arena.Strikes(sim, BlowSource.SwordSlash).All(s => (Arena.Flags(s) & BlowFlags.Repeat) == 0), "Sword Slash carries no Dash");
            }
        }

        [Test]
        public void EncoreBoostsTheNextStrikesWithinABeatOfAnOnBeatDash()
        {
            foreach (var (rank, boosted) in new[] { (1, 1), (2, 2) })
            {
                // Beatless, every dash is on the beat and a beat is 0.5 s.
                var sim = Arena.Sim(null, Arena.Build(), new Dummy(2, 0, Arena.Tough));
                sim.Arsenal.Set(sim, Upgrade.Of(ModKind.Encore, rank));
                Arena.Dash(sim, 1.6);
                Assert.AreEqual(1.0, Hit(sim, HitTags.Area), "an Area blow carries no Strike");
                Assert.AreEqual(1.0, Hit(sim, HitTags.Strike, BlowFlags.Echo), "an echo takes no Encore");
                for (int i = 0; i < 3; i++) Assert.AreEqual(i < boosted ? 3.0 : 1.0, Hit(sim, HitTags.Strike), $"rank {rank}: strike {i}");
            }

            var late = Arena.Sim(null, Arena.Build("encore:1"), new Dummy(2, 0, Arena.Tough));
            Arena.Dash(late, 1.6);
            late.StepTo(2.2);
            Assert.AreEqual(1.0, Hit(late, HitTags.Strike), "a beat passed");

            const double p = 60 / 128.0;
            var off = Arena.Sim(Arena.Song(128), Arena.Build("encore:1"), new Dummy(2, 0, Arena.Tough));
            Arena.Dash(off, 12 * p + p / 4);
            Assert.AreEqual(1.0, Hit(off, HitTags.Strike), "only an on-beat dash");
        }

        /// <summary>The damage an attack's blow of <paramref name="tags"/> landing now deals the enemy.</summary>
        static double Hit(Simulation sim, HitTags tags, BlowFlags flags = BlowFlags.None)
        {
            int before = Arena.HitsOn(sim, 0).Count;
            Arena.Land(sim, tags, 1, flags: flags);
            var hits = Arena.HitsOn(sim, 0);
            Assert.AreEqual(before + 1, hits.Count);
            return hits.Last().D2;
        }

        [Test]
        public void ShatterburstsShardsCutNeighboursForAShareOfTheKillingHit()
        {
            foreach (var (rank, share) in new[] { (0, 0.0), (1, 0.3), (2, 0.6) })
            {
                var build = rank > 0 ? Arena.Build("swordSlash:1", $"shatterburst:{rank}") : Arena.Build("swordSlash:1");
                // The slash kills the first; the second stands out of its reach, 1.2 m behind it.
                var sim = Arena.Run(null, 1.05, build, new Dummy(2.5, 0, 0.5), new Dummy(3.7, 0, Arena.Tough), new Dummy(6, 0, Arena.Tough));
                var shards = Arena.HitsOn(sim, -1, BlowSource.Shards);
                if (rank == 0)
                {
                    Assert.IsEmpty(shards);
                    continue;
                }
                var cut = shards.Single();
                Assert.AreEqual(1, cut.I0, "the neighbour, not the one 3.5 m away");
                Assert.AreEqual(share, cut.D2, 1e-12, $"rank {rank}");
                Assert.AreEqual(1.0, cut.D0, 1e-12, "thrown away from the shattered one");
            }

            // Shards that kill shatter in turn.
            var chain = Arena.Run(null, 1.05, Arena.Build("swordSlash:1", "shatterburst:2"),
                new Dummy(2.5, 0, 0.5), new Dummy(3.7, 0, 0.5), new Dummy(4.9, 0, Arena.Tough));
            Assert.AreEqual(new[] { 0.6, 0.36 }, Arena.HitsOn(chain, -1, BlowSource.Shards).Select(h => Math.Round(h.D2, 12)).ToArray());
            Assert.AreEqual(2, chain.Arsenal.Kills);
        }

        [Test]
        public void LifebloodHealsEveryTwentyFiveKills()
        {
            foreach (var (rank, heal) in new[] { (0, 0.0), (1, 0.02), (2, 0.04) })
            {
                var dummies = Enumerable.Range(0, 60).Select(i => new Dummy(2 + 0.01 * i, 0.01 * (i % 7), 0.5, 0.05)).ToArray();
                var build = rank > 0 ? Arena.Build("swordSlash:1", $"lifeblood:{rank}") : Arena.Build("swordSlash:1");
                var sim = Arena.Sim(null, build, dummies);
                sim.State.Hero.Health = 50;
                sim.StepTo(1.05);
                Assert.AreEqual(60, sim.Arsenal.Kills);
                var heals = Drive.Events(sim, SimEventKind.HeroHeal);
                Assert.AreEqual(rank > 0 ? 2 : 0, heals.Count, $"rank {rank}");
                Assert.IsTrue(heals.All(h => h.D0 == heal * 100 && h.I0 == (int)HealReason.Mod), "a share of full health");
            }
        }
    }
}
