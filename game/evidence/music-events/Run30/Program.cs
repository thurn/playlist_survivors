using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using PS.Game.Sim;
using PS.Game.Sim.Content;
using PS.Game.Sim.Level;

// The run's music-driven events over an entry dump (native's `ps entries`), driven as the client
// drives the sim (60 fps frames, each entry arriving once the frontier passes its stamp), on the
// run's level (RunLevel.Config: the schedule, the events and the starting sword) in the game's
// arena, the hero standing and never falling. Writes every set piece and zone change with its
// cause, and prints a summary with the most alive.
//
//   dotnet run --project game/evidence/music-events/Run30 -c Release -- DUMP.jsonl OUT_DIR
var dump = args[0];
var outDir = args[1];
var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../../.."));
var content = GameContent.Load(Path.Combine(root, "game", "Content"));
var arena = ArenaData.Parse(File.ReadAllText(Path.Combine(root, "game", "Content", "arena.json")), "arena.json");

var entries = new List<StreamEntry>();
double duration = 0;
foreach (var line in File.ReadLines(dump))
{
    if (line.Length == 0) continue;
    using var doc = JsonDocument.Parse(line);
    var j = doc.RootElement;
    if (j.TryGetProperty("format", out _))
    {
        duration = j.GetProperty("duration").GetDouble();
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

var config = RunLevel.Config(content, arena, new double[] { -17, -11, 17, -11, 17, 11, -17, 11 });
config.HeroStands = true;
config.Start = RunStart.AfterClearBeat;
var sim = new Simulation(config);
var events = new List<string>();
var counts = new Dictionary<SetPieceKind, int>();
int maxAlive = 0, cursor = 0, next = 0, leaves = 0, slams = 0, slamHits = 0, minotaur = -1;
double frontier = 0;
while (frontier < duration + 5 && sim.State.Phase != RunPhase.Ended && sim.State.RunClock < content.Schedule.DurationS + 5)
{
    frontier += 1.0 / 60;
    while (next < entries.Count && entries[next].Stamp + 0.06 <= frontier) sim.Add(entries[next++]);
    sim.StepTo(frontier);
    foreach (var e in sim.EventsSince(cursor))
    {
        if (e.Kind == SimEventKind.SetPiece)
        {
            var p = e.AsSetPiece;
            counts[p.Kind] = counts.GetValueOrDefault(p.Kind) + 1;
            events.Add($"{sim.State.RunClock:F2},{p.T:F3},{p.Kind},{p.Members},{p.CauseT:F3},{p.KnownAt:F3}");
        }
        else if (e.Kind == SimEventKind.ZoneChange)
        {
            var z = e.AsZoneChange;
            events.Add($"{sim.State.RunClock:F2},{z.T:F3},Zone:{content.Schedule.Zones.Ids[z.Zone]},,{z.CauseT:F3},{z.KnownAt:F3}");
        }
        else if (e.Kind == SimEventKind.Spawn && e.AsSpawn.Role == content.RoleIndex(content.Schedule.Signature.Role)) minotaur = e.AsSpawn.Enemy;
        else if (e.Kind == SimEventKind.Attack && !e.AsAttack.ByHero && e.AsAttack.Enemy == minotaur && e.AsAttack.EnemyKind == EnemyAttackKind.Slam) slams++;
        else if (e.Kind == SimEventKind.Hit && e.AsHit.OnHero && e.AsHit.EnemyKind == EnemyAttackKind.Slam) slamHits++;
        else if (e.Kind == SimEventKind.Leave) leaves++;
    }
    cursor = sim.EventCount;
    sim.DiscardEventsBefore(cursor);
    maxAlive = Math.Max(maxAlive, sim.State.EnemiesAlive);
}

Directory.CreateDirectory(outDir);
File.WriteAllLines(Path.Combine(outDir, "events.csv"), new[] { "run_clock,song_t,kind,members,cause_t,known_at" }.Concat(events));
Console.WriteLine($"song {sim.Now:F1} s, run clock {sim.State.RunClock:F1} s, phase {sim.State.Phase}, end {sim.State.End}");
Console.WriteLine($"set pieces: {string.Join(", ", counts.OrderBy(c => c.Key).Select(c => $"{c.Key} {c.Value}"))}");
Console.WriteLine($"most alive {maxAlive} (cap {content.Schedule.Cap}), {leaves} left out of sight, minotaur slams {slams} ({slamHits} slam hits on the hero, its and the bosses')");
