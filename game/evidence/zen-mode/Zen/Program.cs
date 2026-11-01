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

// The zen mode's sword against the beat grid over one entry dump (native's `ps entries`), driven as the sweeps drive a
// run (BotRun: 60 fps frames, each entry arriving 60 ms after its stamp, the run's level, seed 1, the headless start),
// the hero standing and walking a square (each arrow held 2 s in turn). Every sword impact goes to OUT_DIR/LABEL.csv
// with its kind, reach, the tick level at it and its offset from the nearest point of the grid it should land on
// (each committed beat, and each half beat where the beat's period is over Sword.HalfBeatsAbove); a summary on stdout:
// how many impacts, their offsets, the gaps between them, the reach by loudness, the zone changes and the enemies
// they shoved.
//
//   dotnet run --project game/evidence/zen-mode/Zen -c Release -- OUT_DIR LABEL DUMP.jsonl
var outDir = args[0];
var label = args[1];
var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../../.."));
var content = GameContent.Load(Path.Combine(root, "game", "Content"));
var arena = ArenaData.Parse(File.ReadAllText(Path.Combine(root, "game", "Content", "arena.json")), "arena.json");

var entries = new List<StreamEntry>();
double until = double.NaN;
foreach (var line in File.ReadLines(args[2]))
{
    if (line.Length == 0) continue;
    using var doc = JsonDocument.Parse(line);
    var j = doc.RootElement;
    if (j.TryGetProperty("format", out _))
    {
        until = j.GetProperty("duration").GetDouble();
        continue;
    }
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

var config = RunLevel.Config(content, arena, RunLevel.GameFootprint);
config.Start = RunStart.AfterClearBeat;
config.Mode = GameMode.Zen;
config.HeroStands = true;
// Nobody answers the level-up's cards, which hold the world.
config.Arsenal.Cards = false;
var sim = new Simulation(config);
var swings = new List<AttackEvent>();
var zones = new List<double>();
var knocks = new List<EnemyKnockEvent>();
var walk = new[] { Key.Right, Key.Up, Key.Left, Key.Down };
int next = 0, cursor = 0, held = -1;
double frontier = sim.Now;
while (frontier < until && sim.State.Phase != RunPhase.Ended)
{
    frontier += BotRun.FrameS;
    while (next < entries.Count && entries[next].Stamp + BotRun.ArrivalLagS <= frontier) sim.Add(entries[next++]);
    int side = (int)(frontier / 2) % walk.Length;
    if (side != held)
    {
        if (held >= 0) sim.Release(walk[held], sim.Now);
        sim.Press(walk[side], sim.Now);
        held = side;
    }
    sim.StepTo(frontier);
    foreach (var e in sim.EventsSince(cursor))
    {
        if (e.Kind == SimEventKind.Attack && e.AsAttack.IsSword) swings.Add(e.AsAttack);
        else if (e.Kind == SimEventKind.ZoneChange) zones.Add(e.T);
        else if (e.Kind == SimEventKind.EnemyKnock) knocks.Add(e.AsEnemyKnock);
    }
    cursor = sim.EventCount;
    sim.DiscardEventsBefore(cursor);
}

var beats = sim.Stream.Beats.ToArray();
var ticks = sim.Stream.Ticks.ToArray();
// The grid: every committed beat, and its half beat where its period is over HalfBeatsAbove.
var grid = new List<double>();
for (int b = 0; b < beats.Length; b++)
{
    grid.Add(beats[b].T);
    if (beats[b].V0 > 0.75 && b + 1 < beats.Length) grid.Add(0.5 * (beats[b].T + beats[b + 1].T));
}
grid.Sort();
double Offset(double t)
{
    int i = grid.BinarySearch(t);
    if (i >= 0) return 0;
    i = ~i;
    double best = double.PositiveInfinity;
    if (i < grid.Count) best = grid[i] - t;
    if (i > 0 && t - grid[i - 1] < Math.Abs(best)) best = grid[i - 1] - t;
    return -best;
}
LoudnessLevel LevelAt(double t)
{
    int i = Array.FindLastIndex(ticks, x => x.T <= t);
    return i >= 0 ? new TickEntry(ticks[i]).Level : LoudnessLevel.Medium;
}
double PeriodAt(double t)
{
    int i = Array.FindLastIndex(beats, x => x.T <= t + 1e-9);
    return i >= 0 ? beats[i].V0 : double.NaN;
}

Directory.CreateDirectory(outDir);
using (var csv = new StreamWriter(Path.Combine(outDir, label + ".csv")))
{
    csv.WriteLine("impact_s,kind,heavy,reach_m,level,beat_period_s,offset_ms");
    foreach (var a in swings)
        csv.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"{a.Impact:F4},{a.SwordKind},{(a.Heavy ? 1 : 0)},{a.Reach:F3},{LevelAt(a.Impact)},{PeriodAt(a.Impact):F3},{Offset(a.Impact) * 1000:F3}"));
}

// Slashes (every strike but a loud beat's follow-ups) on the grid; follow-ups on its subdivisions, a quarter beat.
var sub = new List<double>();
for (int b = 0; b + 1 < beats.Length; b++)
    for (int q = 0; q < 4; q++) sub.Add(beats[b].T + (beats[b + 1].T - beats[b].T) * q / 4);
sub.Sort();
var mains = swings.Where(a => a.SwordKind != HeroAttackKind.FollowUp).ToList();
var follows = swings.Where(a => a.SwordKind == HeroAttackKind.FollowUp).ToList();
var onGrid = mains.Where(a => Math.Abs(Offset(a.Impact)) < 1e-6).ToList();
var off = mains.Except(onGrid).ToList();
double SubOffset(double t) => sub.Min(g => Math.Abs(g - t));
int followsOn = follows.Count(a => SubOffset(a.Impact) < 1e-6);
double followsWorst = follows.Count > 0 ? follows.Max(a => SubOffset(a.Impact)) : 0;
var gaps = mains.Zip(mains.Skip(1), (a, b) => b.Impact - a.Impact).Where(g => g < 2).OrderBy(g => g).ToList();
double Q(List<double> v, double q) => v.Count == 0 ? double.NaN : v[(int)Math.Min(v.Count - 1, Math.Floor(q * v.Count))];
var periods = beats.Select(b => b.V0).OrderBy(p => p).ToList();
Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
    $"{label}: {beats.Length} beats, median period {Q(periods, 0.5):F3} s ({60 / Q(periods, 0.5):F0} BPM); {mains.Count} slashes, {onGrid.Count} on the grid, {off.Count} off it; {follows.Count} follow-ups, {followsOn} on its quarter beats (the rest within {followsWorst * 1000:F0} ms)"));
if (off.Count > 0)
    Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
        $"  off the grid: {string.Join(", ", off.Take(12).Select(a => $"{a.Impact:F2} s ({Offset(a.Impact) * 1000:F0} ms)"))}{(off.Count > 12 ? ", ..." : "")}"));
Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
    $"  slashes' gaps (s): min {Q(gaps, 0):F3}, 10% {Q(gaps, 0.1):F3}, median {Q(gaps, 0.5):F3}, 90% {Q(gaps, 0.9):F3}, max {Q(gaps, 1 - 1e-9):F3}"));
foreach (var level in new[] { LoudnessLevel.Quiet, LoudnessLevel.Medium, LoudnessLevel.Loud })
{
    var at = swings.Where(a => !a.Heavy && a.SwordKind != HeroAttackKind.Slam && LevelAt(a.Impact) == level).ToList();
    if (at.Count == 0) continue;
    Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
        $"  {level}: {at.Count} ordinary strikes, reach {at.Min(a => a.Reach):F2}-{at.Max(a => a.Reach):F2} m, {at.Count(a => a.SwordKind == HeroAttackKind.FollowUp)} follow-ups"));
}
Console.WriteLine($"  heavy: {swings.Count(a => a.Heavy)}, back to back: {mains.Zip(mains.Skip(1), (a, b) => a.Heavy && b.Heavy).Count(x => x)}");
foreach (double z in zones)
    Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
        $"  zone change at {z:F2} s: {knocks.Count(k => Math.Abs(k.Start - z) < 1e-9)} enemies shoved"));
