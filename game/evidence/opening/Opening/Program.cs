using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using PS.Game.Sim;
using PS.Game.Sim.Bots;
using PS.Game.Sim.Content;
using PS.Game.Sim.Level;

// The run's opening over an entry dump (native's `ps entries`), driven as the sweeps drive it (BotRun: 60 fps
// frames, each entry arriving 60 ms after its stamp, the run's level, seed 1, the headless start), for the
// positioning and the idle bot. Each second of run clock's alive crowd and the part of it on screen (inside
// the game camera's footprint round the hero), lowest and highest over the second's frames, to OUT_DIR/LABEL.csv.
//
//   dotnet run --project game/evidence/opening/Opening -c Release -- DUMP.jsonl OUT_DIR LABEL [SECONDS]
var dump = args[0];
var outDir = args[1];
var label = args[2];
double seconds = args.Length > 3 ? double.Parse(args[3], System.Globalization.CultureInfo.InvariantCulture) : 120;
var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../../.."));
var content = GameContent.Load(Path.Combine(root, "game", "Content"));
var arena = ArenaData.Parse(File.ReadAllText(Path.Combine(root, "game", "Content", "arena.json")), "arena.json");

var entries = new List<StreamEntry>();
foreach (var line in File.ReadLines(dump))
{
    if (line.Length == 0) continue;
    using var doc = JsonDocument.Parse(line);
    var j = doc.RootElement;
    if (j.TryGetProperty("format", out _)) continue;
    var e = new StreamEntry
    {
        Kind = (EntryKind)j.GetProperty("kind").GetInt32(),
        T = j.GetProperty("t").GetDouble(),
        Stamp = j.GetProperty("known_at").GetDouble(),
    };
    int k = 0;
    foreach (var v in j.GetProperty("v").EnumerateArray()) e[k++] = v.GetDouble();
    entries.Add(e);
}
entries = entries.OrderBy(e => e.Stamp).ToList();

var footprint = RunLevel.GameFootprint;
Directory.CreateDirectory(outDir);
using var csv = new StreamWriter(Path.Combine(outDir, label + ".csv"));
csv.WriteLine("bot,second,alive_min,alive_max,on_screen_min,on_screen_max,kills,hero_health");
foreach (var kind in new[] { BotKind.Positioning, BotKind.Idle })
{
    var config = RunLevel.Config(content, arena, footprint);
    config.Start = RunStart.AfterClearBeat;
    var sim = new Simulation(config);
    var bot = new SurvivorBot(kind, 1, content);
    int next = 0, cursor = 0, second = -1, kills = 0, aliveMin = 0, aliveMax = 0, seenMin = 0, seenMax = 0, fewest = int.MaxValue;
    double frontier = sim.Now, firstSeen = double.NaN, health = 1, fewestAt = double.NaN, below = 0, longestBelow = 0, longestBelowTo = double.NaN;
    var seenSum = new double[2];
    var frames = new int[2];
    void Flush()
    {
        if (second >= 0) csv.WriteLine($"{kind.ToString().ToLowerInvariant()},{second},{aliveMin},{aliveMax},{seenMin},{seenMax},{kills},{health:F2}");
    }
    // The playing frames of the run's first SECONDS of run clock: the cards, chests and music stopping hold it.
    while (sim.State.RunClock < seconds && sim.State.Phase != RunPhase.Ended && !sim.State.Hero.Fallen)
    {
        frontier += BotRun.FrameS;
        while (next < entries.Count && entries[next].Stamp + BotRun.ArrivalLagS <= frontier) sim.Add(entries[next++]);
        bot.Act(sim);
        sim.StepTo(frontier);
        var events = sim.EventsSince(cursor);
        bot.Observe(events);
        foreach (var e in events)
            if (e.Kind == SimEventKind.Death) kills++;
        cursor = sim.EventCount;
        sim.DiscardEventsBefore(cursor);
        var state = sim.State;
        if (state.Phase != RunPhase.Playing || state.Frozen || state.RunClock >= seconds) continue;
        int alive = state.EnemiesAlive, seen = 0, s = (int)Math.Floor(state.RunClock + 1e-9);
        foreach (ref readonly var enemy in state.Enemies)
            if (enemy.Alive && Inside(footprint, enemy.X - state.Hero.X, enemy.Y - state.Hero.Y)) seen++;
        if (seen > 0 && double.IsNaN(firstSeen)) firstSeen = state.RunClock;
        seenSum[s < 60 ? 0 : 1] += seen;
        frames[s < 60 ? 0 : 1]++;
        if (s >= 5 && s < 60 && seen < fewest)
        {
            fewest = seen;
            fewestAt = state.RunClock;
        }
        // The longest the crowd stays below its minute's minimum, unscaled by the loudness.
        below = alive < content.Schedule.Minutes[Math.Min(content.Schedule.Minutes.Length - 1, s / 60)].Minimum ? below + BotRun.FrameS : 0;
        if (below > longestBelow)
        {
            longestBelow = below;
            longestBelowTo = state.RunClock;
        }
        if (s != second)
        {
            Flush();
            second = s;
            kills = 0;
            aliveMin = seenMin = int.MaxValue;
            aliveMax = seenMax = 0;
        }
        aliveMin = Math.Min(aliveMin, alive);
        aliveMax = Math.Max(aliveMax, alive);
        seenMin = Math.Min(seenMin, seen);
        seenMax = Math.Max(seenMax, seen);
        health = Math.Max(0, state.Hero.Health) / state.Hero.MaxHealth;
    }
    Flush();
    var end = sim.State;
    Console.WriteLine($"{label} {kind}: started at song {end.StartedAt:F2} s; first enemy on screen at {firstSeen:F2} s of run clock; " +
                      $"on screen on average {seenSum[0] / Math.Max(1, frames[0]):F1} in minute 0 and {seenSum[1] / Math.Max(1, frames[1]):F1} in minute 1, " +
                      $"fewest from 5 s to 60 s {fewest} (at {fewestAt:F2} s); longest below the minute's minimum {longestBelow:F2} s (to {longestBelowTo:F2} s); " +
                      (end.Hero.Fallen ? $"fell at {end.RunClock:F1} s" : $"health {health:F2} at {end.RunClock:F1} s"));
}

// Is (x, y), relative to the hero, inside the footprint's convex polygon?
static bool Inside(double[] poly, double x, double y)
{
    int n = poly.Length / 2;
    bool inside = false;
    for (int i = 0, j = n - 1; i < n; j = i++)
    {
        double xi = poly[2 * i], yi = poly[2 * i + 1], xj = poly[2 * j], yj = poly[2 * j + 1];
        if ((yi > y) != (yj > y) && x < (xj - xi) * (y - yi) / (yj - yi) + xi) inside = !inside;
    }
    return inside;
}
