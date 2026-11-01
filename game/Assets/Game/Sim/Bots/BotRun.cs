using System;
using System.Collections.Generic;
using PS.Game.Sim.Content;

namespace PS.Game.Sim.Bots
{
    /// <summary>
    /// A chest the hero took: its roulette opening, at run clock and song time, the minute whose treasure it held, and
    /// the run clock its boss dropped it at.
    /// </summary>
    public struct ChestTaken
    {
        public double RunClock, Song, DroppedAt;
        public int Minute, Tier;
    }

    /// <summary>
    /// The alive crowd in one minute of run clock, sampled at every frame's end: its peak, and over the frames with at
    /// least <see cref="BotReport.CrowdFloor"/> alive, the largest share any one role held and that role. At the
    /// peak's frame, how many of each role were alive.
    /// </summary>
    public sealed class CrowdMinute
    {
        public int Peak;
        public double TopShare;
        public int TopRole = -1;
        public int[] AtPeak;
    }

    /// <summary>What a bot's run did: native's survive report (native/crates/eval/src/survive.rs), for the sweeps.</summary>
    public sealed class BotReport
    {
        /// <summary>The song time the run was driven to, and the run clock there (at the fall, for a fallen hero).</summary>
        public double Song, RunClock;
        public bool Fallen, Victory;
        /// <summary>The lowest health share seen at a frame's end, 0 to 1.</summary>
        public double LowestHealthShare = 1;
        /// <summary>The hero's level as the run clock reached each minute, from minute 0.</summary>
        public readonly List<int> LevelByMinute = new List<int>();
        public readonly List<ChestTaken> Chests = new List<ChestTaken>();
        /// <summary>The crowd by minute of run clock, from minute 0.</summary>
        public readonly List<CrowdMinute> Crowd = new List<CrowdMinute>();
        /// <summary>A frame counts toward a minute's <see cref="CrowdMinute.TopShare"/> with this many alive.</summary>
        public const int CrowdFloor = 250;
        /// <summary>The evolutions owned, in the order they arrived.</summary>
        public readonly List<string> Evolutions = new List<string>();
        /// <summary>The weapons and passives at the end, as "id:rank".</summary>
        public readonly List<string> Arsenal = new List<string>();
        public int Level = 1, PeakEnemies, Kills, Freezes;
        /// <summary>The song time the run started at.</summary>
        public double StartedAt = double.NaN;
        /// <summary>
        /// Song seconds the started run spent frozen, by <see cref="FreezeReason"/> (several at once count
        /// for the first), and in <see cref="FrozenResuming"/>, waiting for the beat a resume lands on.
        /// </summary>
        public readonly double[] Frozen = new double[5];
        public const int FrozenResuming = 4;
        /// <summary>The enemies' hits on the hero, and the damage they did.</summary>
        public int HeroHits;
        public double HeroDamage;
        public ulong Hash;
        public int Events;

        /// <summary>The level at minute <paramref name="minute"/>, or -1 if the run never reached it.</summary>
        public int LevelAt(int minute) => minute < LevelByMinute.Count ? LevelByMinute[minute] : -1;
    }

    /// <summary>
    /// Drives a run headlessly for a bot as the client drives it (native's <c>eval::survive::drive</c>):
    /// 60 fps frames, each entry arriving <see cref="ArrivalLagS"/> after its stamp (the sim knows it
    /// from its stamp plus B regardless), the bot's keys before each frame's step, its events after.
    /// Stops at the run's end, the hero's fall, <paramref name="runSeconds"/> of run clock, or the
    /// song time <paramref name="until"/>.
    /// </summary>
    public static class BotRun
    {
        public const double FrameS = 1.0 / 60, ArrivalLagS = 0.06;

        /// <summary>
        /// Plays <paramref name="entries"/>, ordered by stamp, through a sim of <paramref name="config"/>
        /// with <paramref name="bot"/>; <paramref name="sim"/> is the sim it played, for its keys and hash.
        /// </summary>
        public static BotReport Play(SimConfig config, IReadOnlyList<StreamEntry> entries, SurvivorBot bot, double until,
            double runSeconds, out Simulation sim)
        {
            sim = new Simulation(config);
            var content = config.Content;
            var report = new BotReport();
            var drops = new Dictionary<int, double>();
            var counts = new int[content?.Roles.Length ?? 0];
            int next = 0, cursor = 0;
            double frontier = sim.Now;
            while (frontier < until - 1e-12)
            {
                double before = frontier;
                frontier = Math.Min(until, frontier + FrameS);
                while (next < entries.Count && entries[next].Stamp + ArrivalLagS <= frontier) sim.Add(entries[next++]);
                bot.Act(sim);
                sim.StepTo(frontier);
                var events = sim.EventsSince(cursor);
                bot.Observe(events);
                Take(report, sim, content, events, frontier - before, drops, counts);
                cursor = sim.EventCount;
                sim.DiscardEventsBefore(cursor);
                var state = sim.State;
                if (state.Hero.Fallen) report.Fallen = true;
                if (state.Phase == RunPhase.Ended || report.Fallen || state.RunClock >= runSeconds) break;
            }
            var end = sim.State;
            report.Song = sim.Now;
            report.StartedAt = end.StartedAt;
            report.RunClock = end.RunClock;
            report.Victory = end.End == RunEnd.Victory;
            report.Level = end.Level;
            report.Hash = sim.Hash;
            report.Events = sim.EventCount;
            if (content != null)
            {
                foreach (var w in end.WeaponSlots) report.Arsenal.Add($"{content.Weapons[w.Index].Id}:{w.Rank}");
                foreach (var p in end.PassiveSlots) report.Arsenal.Add($"{content.Passives[p.Index].Id}:{p.Rank}");
            }
            return report;
        }

        static void Take(BotReport report, Simulation sim, GameContent content, ReadOnlySpan<SimEvent> events, double frame,
            Dictionary<int, double> drops, int[] counts)
        {
            var state = sim.State;
            if (state.Frozen && state.Phase == RunPhase.Playing)
            {
                int reason = 0;
                while (reason < BotReport.FrozenResuming && !state.FrozenFor((FreezeReason)reason)) reason++;
                report.Frozen[reason] += frame;
            }
            foreach (var e in events)
            {
                if (e.Kind == SimEventKind.Death) report.Kills++;
                else if (e.Kind == SimEventKind.Hit && e.AsHit.OnHero)
                {
                    report.HeroHits++;
                    report.HeroDamage += e.AsHit.Damage;
                }
                else if (e.Kind == SimEventKind.Freeze && e.AsFreeze.Starts) report.Freezes++;
                else if (e.Kind == SimEventKind.Drop && e.AsDrop.ItemKind == ItemKind.Chest) drops[e.AsDrop.Item] = state.RunClock;
                else if (e.Kind == SimEventKind.Chest && e.AsChest.Phase == ChestPhase.Open)
                {
                    var c = e.AsChest;
                    report.Chests.Add(new ChestTaken
                    {
                        RunClock = state.RunClock, Song = c.T, Minute = c.Minute, Tier = c.Tier,
                        DroppedAt = drops.TryGetValue(c.Chest, out double at) ? at : double.NaN,
                    });
                }
            }
            if (state.Hero.MaxHealth > 0) report.LowestHealthShare = Math.Min(report.LowestHealthShare, Math.Max(0, state.Hero.Health) / state.Hero.MaxHealth);
            report.PeakEnemies = Math.Max(report.PeakEnemies, state.EnemiesAlive);
            int minute = (int)Math.Floor(state.RunClock / 60 + 1e-9);
            while (report.LevelByMinute.Count <= minute) report.LevelByMinute.Add(state.Level);
            if (content == null) return;
            Census(report, state, minute, counts);
            foreach (var w in state.WeaponSlots)
            {
                var def = content.Weapons[w.Index];
                if (def.IsEvolution && !report.Evolutions.Contains(def.Id)) report.Evolutions.Add(def.Id);
            }
        }

        /// <summary>The alive crowd's roles at this frame's end, into <paramref name="minute"/>'s <see cref="CrowdMinute"/>.</summary>
        static void Census(BotReport report, SimState state, int minute, int[] counts)
        {
            while (report.Crowd.Count <= minute) report.Crowd.Add(new CrowdMinute());
            var crowd = report.Crowd[minute];
            int alive = 0;
            Array.Clear(counts, 0, counts.Length);
            foreach (ref readonly var enemy in state.Enemies)
                if (enemy.Alive)
                {
                    counts[enemy.Role]++;
                    alive++;
                }
            if (alive > crowd.Peak || crowd.AtPeak == null)
            {
                crowd.Peak = alive;
                crowd.AtPeak = (int[])counts.Clone();
            }
            if (alive < BotReport.CrowdFloor) return;
            for (int r = 0; r < counts.Length; r++)
            {
                double share = counts[r] / (double)alive;
                if (share <= crowd.TopShare) continue;
                crowd.TopShare = share;
                crowd.TopRole = r;
            }
        }
    }
}
