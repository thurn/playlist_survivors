using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using PS.Game.Sim;
using PS.Game.Sim.Bots;
using PS.Game.Sim.Content;
using PS.Game.Sim.Level;

// A whole run's crowd on screen over entry dumps (native's `ps entries`), laid end to end with 2 s gaps as the sweeps
// lay a playlist's tracks, driven as the sweeps drive it (BotRun: 60 fps frames, each entry arriving 60 ms after its
// stamp, the run's level, seed 1, the headless start) for the positioning bot on the grid. Each second of run clock:
// the alive crowd and the part of it on screen (inside the game camera's footprint round the hero), lowest and highest
// over the second's playing frames, to OUT_DIR/LABEL.csv; every stretch of run clock with fewer than FEW on screen for
// longer than LONG_S to OUT_DIR/LABEL-empty.csv; a summary on stdout.
//
//   dotnet run --project game/evidence/floor/Floor -c Release -- OUT_DIR LABEL DUMP.jsonl [DUMP.jsonl ...]
const int Few = 15;
const double LongS = 2;
var outDir = args[0];
var label = args[1];
var dumps = args.Skip(2).ToArray();
var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../../.."));
var content = GameContent.Load(Path.Combine(root, "game", "Content"));
var arena = ArenaData.Parse(File.ReadAllText(Path.Combine(root, "game", "Content", "arena.json")), "arena.json");

// The dumps on one timeline, each after a 2 s gap (the sweeps' playlist), played until the run ends.
var entries = new List<StreamEntry>();
long samples = 0;
const double rate = 44100;
for (int d = 0; d < dumps.Length; d++)
{
    if (d > 0) samples += (long)Math.Round(2 * rate);
    double start = samples / rate, length = double.NaN;
    foreach (var line in File.ReadLines(dumps[d]))
    {
        if (line.Length == 0) continue;
        using var doc = JsonDocument.Parse(line);
        var j = doc.RootElement;
        if (j.TryGetProperty("format", out _))
        {
            length = j.GetProperty("duration").GetDouble();
            continue;
        }
        var e = new StreamEntry
        {
            Kind = (EntryKind)j.GetProperty("kind").GetInt32(),
            T = j.GetProperty("t").GetDouble() + start,
            Stamp = j.GetProperty("known_at").GetDouble() + start,
        };
        int k = 0;
        foreach (var v in j.GetProperty("v").EnumerateArray()) e[k++] = v.GetDouble();
        if (e.Kind == EntryKind.HindsightBeat) e[1] += start;
        entries.Add(e);
    }
    samples += (long)Math.Round(length * rate);
}
entries = entries.OrderBy(e => e.Stamp).ToList();
double until = samples / rate;

var footprint = RunLevel.GameFootprint;
var config = RunLevel.Config(content, arena, footprint);
config.Start = RunStart.AfterClearBeat;
var sim = new Simulation(config);
var bot = new SurvivorBot(BotKind.Positioning, 1, content);
Directory.CreateDirectory(outDir);
using var csv = new StreamWriter(Path.Combine(outDir, label + ".csv"));
using var empty = new StreamWriter(Path.Combine(outDir, label + "-empty.csv"));
csv.WriteLine("second,alive_min,alive_max,on_screen_min,on_screen_max,kills,loudness,hero_level");
empty.WriteLine("from_s,to_s,length_s,fewest_on_screen,alive_min");
int next = 0, cursor = 0, second = -1, kills = 0, aliveMin = 0, aliveMax = 0, seenMin = 0, seenMax = 0, peak = 0;
int levelFrames = 0, runFewest = int.MaxValue, runAliveMin = int.MaxValue, longCount = 0, longPastZero = 0;
double frontier = sim.Now, levelSum = 0, lowest = 1, loudness = 1, fewFrom = double.NaN, longest = 0, longestFrom = double.NaN;
double longSeconds = 0;
int[] minuteSeen = new int[31], minuteFrames = new int[31];
double[] minuteLong = new double[31];
void Flush()
{
    if (second < 0) return;
    csv.WriteLine(string.Create(CultureInfo.InvariantCulture,
        $"{second},{aliveMin},{aliveMax},{seenMin},{seenMax},{kills},{levelSum / Math.Max(1, levelFrames):F2},{sim.State.Level}"));
}
// A stretch of fewer than FEW on screen ends at run clock `to`.
int stretchFewest = int.MaxValue, stretchAliveMin = int.MaxValue;
void EndStretch(double to)
{
    if (double.IsNaN(fewFrom)) return;
    double length = to - fewFrom;
    if (length > longest)
    {
        longest = length;
        longestFrom = fewFrom;
    }
    if (length > LongS)
    {
        longCount++;
        if (fewFrom >= 60) longPastZero++;
        longSeconds += length;
        minuteLong[Math.Min(30, (int)(fewFrom / 60))] += length;
        empty.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{fewFrom:F2},{to:F2},{length:F2},{stretchFewest},{stretchAliveMin}"));
    }
    fewFrom = double.NaN;
}
while (frontier < until && sim.State.Phase != RunPhase.Ended && !sim.State.Hero.Fallen)
{
    frontier += BotRun.FrameS;
    while (next < entries.Count && entries[next].Stamp + BotRun.ArrivalLagS <= frontier)
    {
        var e = entries[next++];
        if (e.Kind == EntryKind.Tick) loudness = e[2];
        sim.Add(e);
    }
    bot.Act(sim);
    sim.StepTo(frontier);
    var events = sim.EventsSince(cursor);
    bot.Observe(events);
    foreach (var e in events)
        if (e.Kind == SimEventKind.Death) kills++;
    cursor = sim.EventCount;
    sim.DiscardEventsBefore(cursor);
    var state = sim.State;
    lowest = Math.Min(lowest, Math.Max(0, state.Hero.Health) / state.Hero.MaxHealth);
    peak = Math.Max(peak, state.EnemiesAlive);
    // The playing frames of the run clock: the cards, chests and music stopping hold it.
    if (state.Phase != RunPhase.Playing || state.Frozen || state.RunClock >= content.Schedule.DurationS) continue;
    int alive = state.EnemiesAlive, seen = 0, s = (int)Math.Floor(state.RunClock + 1e-9);
    foreach (ref readonly var enemy in state.Enemies)
        if (enemy.Alive && Inside(footprint, enemy.X - state.Hero.X, enemy.Y - state.Hero.Y)) seen++;
    if (seen < Few)
    {
        if (double.IsNaN(fewFrom))
        {
            fewFrom = state.RunClock;
            stretchFewest = stretchAliveMin = int.MaxValue;
        }
        stretchFewest = Math.Min(stretchFewest, seen);
        stretchAliveMin = Math.Min(stretchAliveMin, alive);
    }
    else EndStretch(state.RunClock);
    if (s >= 60)
    {
        runFewest = Math.Min(runFewest, seen);
        runAliveMin = Math.Min(runAliveMin, alive);
    }
    minuteSeen[s / 60] += seen;
    minuteFrames[s / 60]++;
    if (s != second)
    {
        Flush();
        second = s;
        kills = 0;
        levelSum = levelFrames = 0;
        aliveMin = seenMin = int.MaxValue;
        aliveMax = seenMax = 0;
    }
    aliveMin = Math.Min(aliveMin, alive);
    aliveMax = Math.Max(aliveMax, alive);
    seenMin = Math.Min(seenMin, seen);
    seenMax = Math.Max(seenMax, seen);
    levelSum += loudness;
    levelFrames++;
}
EndStretch(sim.State.RunClock);
Flush();
var end = sim.State;
Console.WriteLine($"{label}: {(end.End == RunEnd.Victory ? "victory" : end.Hero.Fallen ? "fell" : "stopped")} at run clock {end.RunClock:F1} s " +
                  $"(song {sim.Now:F1} s), lowest health {lowest:F3}, level {end.Level}, most alive {peak}");
Console.WriteLine($"  past minute 0: fewest on screen {runFewest}, fewest alive {runAliveMin}; stretches under {Few} on screen longer than {LongS} s: " +
                  $"{longCount} ({longPastZero} past minute 0), {longSeconds:F1} s in all; longest {longest:F2} s from {longestFrom:F2} s");
Console.WriteLine("  on screen, mean by minute: " + string.Join(" ", Enumerable.Range(0, 30)
    .Select(m => $"{m}:{minuteSeen[m] / (double)Math.Max(1, minuteFrames[m]):F0}" + (minuteLong[m] > 0 ? $"({minuteLong[m]:F0}s)" : ""))));

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
