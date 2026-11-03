using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace Champions.Sim.Tests
{
    /// <summary>
    /// A scripted duel's session (plan.md, "Sessions and replay"; ps-6y2.17): the Ogre called at the start records its
    /// duel with the run's inputs, and replays to its hash.
    /// </summary>
    public class DuelSessionTests
    {
        /// <summary>The recorded session CI replays: rhythm_champions/sessions/fixture-duel.</summary>
        public const string FixtureSession = "fixture-duel";

        static readonly string[] Build = { "sword:5", "orbitBlades:5", "bow:5", "might:3", "tempo:3", "armor:3" };

        /// <summary>
        /// A live run as the client drives it: 128 BPM, a beatless stretch and 150 BPM, the Ogre called at the start and
        /// arriving on a strong downbeat (strong onsets on every beat from 6 s to 8 s), standing enemies on screen and off for
        /// its entrance, the player keeping near it and dashing through its attacks on their beats, quitting at its death.
        /// </summary>
        public static (RunInputs Run, Simulation Sim, List<StreamEntry> Fed) DuelRun()
        {
            var song = new FixtureSong(41)
                .Beats(0, 30, 128)
                .Gap(30, 34)
                .Beats(34, 180, 150, jitter: 0.002);
            double beat = 60.0 / 128;
            for (int i = (int)Math.Ceiling(6 / beat); i * beat < 8; i++) song.Onset(i * beat, true);
            var entries = song.Entries();
            var dummies = Enumerable.Range(0, 40)
                .Select(i => new Dummy((4 + i % 8 * 3.5) * Math.Cos(1.7 * i), (4 + i % 8 * 3.5) * Math.Sin(1.7 * i), 3, 0.45))
                .ToArray();
            var run = new RunInputs { Seed = 41, Duel = "fatOgre", SurvivorWeapons = true, Loadout = Array.Empty<Upgrade>(), SurvivorLoadout = AttackSessionTests.Preset(Build), Dummies = dummies };
            var sim = new Simulation(run.Build(GameContent.Load(Checkout.In("Content"))));
            var dodger = new BossTests.Dodger(through: true);
            double chased = 0, after = double.NaN;
            int fed = Drive.Feed(sim, entries, 180, eachFrame: s =>
            {
                if (!double.IsNaN(after) || s.State.Phase != RunPhase.Playing) return;
                if (s.Bosses.Stage == BossStage.Dead)
                {
                    after = s.Now;
                    s.Control(RunControl.Quit, s.Now - BossTests.Dodger.Lead);
                    return;
                }
                if (!s.Bosses.InDuel) return;
                dodger.Frame(s);
                foreach (var mark in s.Bosses.Marks)
                {
                    if (!Bosses.IsWave(mark.Move) || s.State.Hero.DashReadyAt > s.Now) continue;
                    double soon = s.Now + 0.05;
                    if (soon < s.Telegraphs.LandTime(mark.Telegraph)) continue;
                    s.HeroWas(s.Now, out double px, out double py);
                    if (!s.Bosses.WaveShape(mark, soon).Touches(px, py, 1.4)) continue;
                    double wx = mark.Move == BossMove.Wall ? 0 : mark.Shape.X - px;
                    double wy = mark.Move == BossMove.Wall ? -1 : mark.Shape.Y - py;
                    double length = Math.Max(0.01, Math.Sqrt(wx * wx + wy * wy));
                    s.Target(px + wx / length * 8, py + wy / length * 8, s.Now - BossTests.Dodger.Lead);
                    s.Press(Key.Space, s.Now - BossTests.Dodger.Lead);
                    s.Release(Key.Space, s.Now - BossTests.Dodger.Lead);
                }
                if (s.Now - chased < 0.5) return;
                chased = s.Now;
                ref readonly var b = ref s.Enemies[s.Bosses.Id];
                s.HeroWas(s.Now, out double hx, out double hy);
                double dx = b.X - hx, dy = b.Y - hy, d = Math.Sqrt(dx * dx + dy * dy);
                if (d > 4.5) s.Target(b.X - dx / d * 3, b.Y - dy / d * 3, s.Now - BossTests.Dodger.Lead);
            });
            return (run, sim, entries.GetRange(0, fed));
        }

        static (Simulation Sim, SessionReplay Replay) Replay(string dir)
        {
            var record = SessionRecord.Read(dir);
            var replay = new SessionReplay(record);
            foreach (var e in SessionRecord.LoadEntries(dir)) replay.Supply(e);
            var sim = new Simulation(record.Run.Build(GameContent.Load(Checkout.In("Content"))));
            Assert.IsTrue(replay.StepTo(sim, double.PositiveInfinity));
            return (sim, replay);
        }

        [Test]
        public void AScriptedDuelReplaysToItsHash()
        {
            var (run, live, fed) = DuelRun();
            Assert.AreEqual(RunEnd.Quit, live.State.End, "the script ran to the Ogre's death");
            Assert.AreEqual(BossStage.Dead, live.Bosses.Stage);
            Assert.AreEqual(0, Drive.Events(live, SimEventKind.BossArrive).Single().I2, "its entrance retains the horde");
            Assert.AreEqual(0, Drive.Events(live, SimEventKind.BossStagger).Count, "dodges never gate damage");
            Assert.AreEqual(2, Drive.Events(live, SimEventKind.BossPhase).Single().I1);
            string dir = Path.Combine(Path.GetTempPath(), "champions-duel-" + Guid.NewGuid().ToString("N"));
            try
            {
                var record = SessionRecord.Of(run, live);
                record.Write(dir);
                SessionRecord.WriteEntries(dir, fed);
                var read = SessionRecord.Read(dir);
                Assert.AreEqual("fatOgre", read.Run.Duel, "the duel is recorded");
                StringAssert.Contains("\"duel\": \"fatOgre\"", File.ReadAllText(Path.Combine(dir, SessionRecord.RunFile)));
                var (sim, replay) = Replay(dir);
                Assert.IsTrue(replay.Matches(sim), $"replayed {sim.Hash:X16} ({sim.EventCount} events), recorded {record.Hash:X16} ({record.Events})");
                Assert.AreEqual(BossStage.Dead, sim.Bosses.Stage);
            }
            finally
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
            }
        }

        [Test]
        public void TheRecordedDuelSessionReplaysToItsHash()
        {
            string dir = Checkout.In("sessions", FixtureSession);
            var (sim, replay) = Replay(dir);
            Assert.IsTrue(replay.Matches(sim),
                $"{dir} replays to {sim.Hash:X16} ({sim.EventCount} events), recorded {replay.Record.Hash:X16} ({replay.Record.Events}): " +
                "if a rule or the content changed on purpose, re-record it (dotnet run --project rhythm_champions/dotnet/Champions.Replay -- " +
                "rhythm_champions/sessions/fixture-duel --update) and say so in the commit");
        }

        [Test]
        public void TheDuelSessionIsTheScriptedRunsRecording()
        {
            var record = SessionRecord.Read(Checkout.In("sessions", FixtureSession));
            var (_, live, fed) = DuelRun();
            Assert.AreEqual("fatOgre", record.Run.Duel);
            Assert.AreEqual(record.Inputs.Length, live.Inputs.Length);
            Assert.AreEqual(record.Entries.Length, fed.Count);
            Assert.AreEqual(record.End, live.Now);
        }

        [Test, Explicit("writes rhythm_champions/sessions/fixture-duel")]
        public void RecordTheDuelSession()
        {
            var (run, live, fed) = DuelRun();
            string dir = Checkout.In("sessions", FixtureSession);
            var record = SessionRecord.Of(run, live);
            record.Source = "dotnet/Champions.Sim.Tests DuelSessionTests.DuelRun: a synthetic fixture song, no audio";
            record.Build = "test";
            record.Write(dir);
            SessionRecord.WriteEntries(dir, fed);
        }

        [Test]
        public void TheRecordedAttackSessionHasNoDuel()
        {
            var record = SessionRecord.Read(Checkout.In("sessions", AttackSessionTests.FixtureSession));
            Assert.IsNull(record.Run.Duel);
        }
    }
}
