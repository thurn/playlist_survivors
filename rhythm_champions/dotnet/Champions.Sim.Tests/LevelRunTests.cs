using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace Champions.Sim.Tests
{
    /// <summary>
    /// Level 1 end to end in the sim (ps-6y2.21; design.md, The Run and Difficulty), as the player's `--walk` drives it:
    /// from the first offer on a playlist of 128 BPM songs with a silent gap between them, the level's spawner,
    /// structures and bosses on, every card picked by a <see cref="ScriptedPlayer"/>. The targets: an idle hero dies within
    /// 90 s; the reference player (its ring, its dashes on the beat, its perfect dodges in the duels, the reference build's
    /// picks) reaches the Ogre and beats the dragon. The whole run takes about a minute of .NET, so it is explicit; the idle
    /// one runs always.
    /// </summary>
    public class LevelRunTests
    {
        static readonly GameContent Content = DashTests.Content;

        /// <summary>
        /// Songs of 210 s at 128 BPM, each followed by a 4 s silent gap, to <paramref name="until"/>: shaped like a dance track,
        /// a medium intro, a loud drop, a quiet break with a section change into it and out of it, a second loud drop, a medium
        /// outro.
        /// </summary>
        public static FixtureSong Playlist(double until, ulong seed = 31)
        {
            const double song = 210;
            var playlist = new FixtureSong(seed);
            for (double t = 0; t < until; t += song + 4)
            {
                playlist.Beats(t, t + song, 128, jitter: 0.003).Gap(t + song, t + song + 4, silent: true)
                    .Loudness(t + 20, t + 90, LoudnessLevel.Loud).Loudness(t + 90, t + 110, LoudnessLevel.Quiet)
                    .Loudness(t + 110, t + 190, LoudnessLevel.Loud);
                foreach (double c in new[] { 20.0, 90, 110, 190 }) playlist.Change(t + c);
            }
            return playlist;
        }

        /// <summary>What a run did, for the asserts and the tuning's log.</summary>
        public sealed class Outcome
        {
            public Simulation Sim;
            public RunInputs Run;
            public List<StreamEntry> Fed;
            public RunEnd End;
            public double EndedAt = double.NaN, RunClock;
            public readonly List<(double RunClock, string What)> Moments = new List<(double, string)>();
            public int Kills, Picks, Levels;
            public readonly Dictionary<string, int> Hits = new Dictionary<string, int>();
            public double OgreArrived = double.NaN, OgreDied = double.NaN, DragonArrived = double.NaN, DragonDied = double.NaN;
            public double LeastHealth = double.MaxValue;

            public override string ToString() =>
                $"{End} at run clock {RunClock:F1} s (song {EndedAt:F1} s): {Kills} kills, level {Levels}, {Picks} picks, least health {LeastHealth:F0}; hits {string.Join(", ", Hits.Select(h => h.Key + " " + h.Value))}; " +
                string.Join("; ", Moments.Select(m => $"{m.RunClock:F1} {m.What}"));
        }

        /// <summary>Level 1 from the first offer to its end (or <paramref name="until"/>), driven by `--walk` <paramref name="walk"/>.</summary>
        public static Outcome Run(string walk, string picks = null, double until = 1200, ulong seed = 31, Action<ScriptedPlayer> tweak = null,
            Action<SimConfig> config = null)
        {
            var entries = Playlist(until + 20).Entries();
            var run = new RunInputs { Seed = seed, Start = RunStart.FirstOffer, Level = true };
            var built = run.Build(Content);
            config?.Invoke(built);
            var sim = new Simulation(built);
            var player = ScriptedPlayer.Parse(walk, picks, Content.Dashes);
            tweak?.Invoke(player);
            var o = new Outcome { Sim = sim, Run = run };
            int next = 0, cursor = 0;
            double frontier = 0;
            const double frame = 1.0 / 60, lead = 0.05;
            while (sim.State.Phase != RunPhase.Ended && frontier < until)
            {
                frontier += frame;
                while (next < entries.Count && entries[next].Stamp + 0.06 <= frontier) sim.Add(entries[next++]);
                sim.StepTo(frontier);
                foreach (var e in sim.EventsSince(cursor))
                {
                    player.Take(sim, e);
                    Note(o, sim, e);
                }
                cursor = sim.EventCount;
                sim.DiscardEventsBefore(cursor);
                if (sim.State.Phase == RunPhase.Playing) o.LeastHealth = Math.Min(o.LeastHealth, sim.State.Hero.Health);
                player.Frame(sim, frontier - lead);
            }
            o.Fed = entries.GetRange(0, next);
            o.End = sim.State.End;
            o.EndedAt = sim.State.EndedAt;
            o.RunClock = sim.State.RunClock;
            o.Picks = player.Picks;
            o.Levels = sim.State.Level;
            return o;
        }

        static void Note(Outcome o, Simulation sim, in SimEvent e)
        {
            double clock = sim.State.RunClock;
            switch (e.Kind)
            {
                case SimEventKind.Kill when (BlowSource)e.I1 != BlowSource.Boss:
                    o.Kills++;
                    break;
                case SimEventKind.HeroHit when (HitSource)e.I1 == HitSource.Boss || (HitSource)e.I1 == HitSource.Charge:
                    o.Moments.Add((clock, $"{(HitSource)e.I1} hit {(HitOutcome)e.I0} {e.D0:F0} -> {e.D1:F0} (song {e.T:F2}, dash ready {sim.State.Hero.DashReadyAt:F2})"));
                    if (e.D0 > 0) goto case SimEventKind.None;
                    break;
                case SimEventKind.None:
                case SimEventKind.HeroHit when e.D0 > 0:
                    string why = ((HitSource)e.I1).ToString();
                    o.Hits[why] = o.Hits.TryGetValue(why, out int n) ? n + 1 : 1;
                    break;
                case SimEventKind.LevelUp:
                    o.Moments.Add((clock, $"level {e.I0}"));
                    break;
                case SimEventKind.Pick:
                    o.Moments.Add((clock, $"pick {Card.FromCode(e.I1)}"));
                    break;
                case SimEventKind.BossArrive:
                    bool ogre = Content.Enemies.Cast[e.I1].Id == "fatOgre";
                    if (ogre) o.OgreArrived = clock;
                    else o.DragonArrived = clock;
                    o.Moments.Add((clock, $"{Content.Enemies.Cast[e.I1].Id} arrives, shattering {e.I2}, hero {sim.State.Hero.Health:F0}"));
                    break;
                case SimEventKind.BossDeath:
                    if (double.IsNaN(o.OgreDied)) o.OgreDied = clock;
                    else o.DragonDied = clock;
                    o.Moments.Add((clock, $"boss dies, hero {sim.State.Hero.Health:F0}"));
                    break;
                case SimEventKind.HeroDeath:
                    o.Moments.Add((clock, $"hero dies with {sim.Enemies.Alive} alive"));
                    break;
            }
        }

        [Test]
        public void TheScriptedPlayerHoldsItsKeysAndPicksItsCards()
        {
            Assert.Throws<ArgumentException>(() => ScriptedPlayer.Parse("left@3", null, Content.Dashes));
            Assert.Throws<ArgumentException>(() => ScriptedPlayer.Parse(null, "4", Content.Dashes));
            Assert.Throws<ArgumentException>(() => ScriptedPlayer.Parse(null, null, Content.Dashes));
            Assert.AreEqual(ScriptedPlayer.Routine.Reference, ScriptedPlayer.Parse("reference", null, Content.Dashes).Kind);
            // Held keys walk the hero right for a second; the first offer is picked by its second card.
            var entries = Playlist(30).Entries();
            var sim = new Simulation(new RunInputs { Seed = 3, Start = RunStart.FirstOffer, Level = true }.Build(Content));
            var player = ScriptedPlayer.Parse("right@2-3", "2", Content.Dashes);
            int cursor = 0;
            var picks = new List<SimEvent>();
            Drive.Feed(sim, entries, 4, eachFrame: s =>
            {
                foreach (var e in s.EventsSince(cursor))
                {
                    player.Take(s, e);
                    if (e.Kind == SimEventKind.Pick) picks.Add(e);
                }
                cursor = s.EventCount;
                player.Frame(s, s.Now - 0.05);
            });
            Assert.AreEqual(1, picks.Count);
            Assert.AreEqual(1, picks[0].I0, "the second card");
            sim.HeroWas(sim.Now, out double x, out double y);
            Assert.AreEqual(6, x, 0.2, "a second at 6 m/s");
            Assert.AreEqual(0, y, 1e-9);
        }

        [Test]
        public void AnIdleHeroDiesWithinNinetySeconds()
        {
            var o = Run("idle", until: 200);
            TestContext.Out.WriteLine("idle: " + o);
            Assert.AreEqual(RunEnd.Death, o.End, o.ToString());
            Assert.Less(o.RunClock, 90, o.ToString());
        }

        /// <summary>The level session: the reference player's first <see cref="LevelSessionS"/> s from the first offer.</summary>
        public const double LevelSessionS = 75;

        [Test, Explicit("writes rhythm_champions/sessions/fixture-level")]
        public void RecordTheLevelSession()
        {
            var o = Run("reference", until: LevelSessionS);
            Assert.AreEqual(RunPhase.Playing, o.Sim.State.Phase, o.ToString());
            string dir = Checkout.In("sessions", "fixture-level");
            var record = SessionRecord.Of(o.Run, o.Sim);
            record.Source = $"dotnet/Champions.Sim.Tests LevelRunTests.RecordTheLevelSession: level 1 from the first offer, --walk reference, {LevelSessionS} s of a synthetic 128 BPM playlist, no audio";
            record.Build = "test";
            record.Write(dir);
            SessionRecord.WriteEntries(dir, o.Fed);
        }

        /// <summary>A seed whose run the reference player wins (it wins about a quarter of them: rhythm_champions/evidence/ps-6y2.21/README.md).</summary>
        public const ulong WinningSeed = 34;

        [Test, Explicit("prints the reference player's runs over eight seeds for rhythm_champions/evidence/ps-6y2.21/README.md")]
        public void PrintTheReferenceRuns()
        {
            for (ulong seed = 31; seed <= 38; seed++)
            {
                var o = Run("reference", seed: seed);
                TestContext.Out.WriteLine($"| {seed} | {o.End} at {o.RunClock:F0} s | {(double.IsNaN(o.OgreArrived) ? "-" : $"{o.OgreArrived:F0} s")} | " +
                                          $"{(double.IsNaN(o.OgreDied) ? "-" : $"{o.OgreDied - o.OgreArrived:F0} s")} | {(double.IsNaN(o.DragonArrived) ? "-" : $"{o.DragonArrived:F0} s")} | " +
                                          $"{o.Levels} | {o.Kills} | {string.Join(", ", o.Hits.Select(h => h.Key.ToString().ToLowerInvariant() + " " + h.Value))} |");
            }
        }

        [Test, Explicit("a few seconds: the whole level, for tuning (rhythm_champions/evidence/ps-6y2.21/README.md)")]
        public void TheReferencePlayerReachesTheOgreAndBeatsTheDragon()
        {
            var o = Run("reference", seed: WinningSeed);
            TestContext.Out.WriteLine("reference: " + o);
            Assert.IsFalse(double.IsNaN(o.OgreArrived), "reaches the Ogre: " + o);
            Assert.AreEqual(RunEnd.Victory, o.End, o.ToString());
        }
    }
}
