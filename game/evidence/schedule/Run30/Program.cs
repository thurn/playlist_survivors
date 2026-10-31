using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using PS.Game.Sim;
using PS.Game.Sim.Content;
using PS.Game.Sim.Level;

// The full schedule over an entry dump (native's `ps entries`), driven as the client drives the sim:
// 60 fps frames, each entry arriving once the frontier passes its stamp. The hero stands still with
// the starting sword and never falls. Writes per-minute spawns by role, bosses, chests and the most
// alive to the output folder, and prints a summary.
//
//   dotnet run --project game/evidence/schedule/Run30 -- DUMP.jsonl OUT_DIR
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

var sim = new Simulation(new SimConfig
{
    Arena = arena,
    Horde = new HordeConfig { Content = content, Schedule = true },
    Sword = new SwordConfig { Content = content },
    HeroStands = true,
    Start = RunStart.AfterClearBeat,
});
int minutes = content.Schedule.Minutes.Length;
var spawns = new int[minutes, content.Roles.Length];
var killed = new int[minutes];
var reliable = new double[minutes];
var bosses = new List<string>();
var chests = new List<string>();
int maxAlive = 0, kills = 0, cursor = 0, next = 0, charges = 0, slams = 0, slamBreaks = 0, bossHits = 0;
double frontier = 0;
var watch = System.Diagnostics.Stopwatch.StartNew();
while (frontier < duration + 5 && sim.State.Phase != RunPhase.Ended && sim.State.RunClock < content.Schedule.DurationS + 5)
{
    frontier += 1.0 / 60;
    while (next < entries.Count && entries[next].Stamp + 0.06 <= frontier) sim.Add(entries[next++]);
    double clock = sim.State.RunClock;
    sim.StepTo(frontier);
    int minute = Math.Min(minutes - 1, (int)Math.Floor(sim.State.RunClock / 60 + 1e-9));
    if (sim.State.Beat == Reliability.Reliable) reliable[minute] += sim.State.RunClock - clock;
    foreach (var e in sim.EventsSince(cursor))
    {
        if (e.Kind == SimEventKind.Spawn && !e.AsSpawn.Recycled)
        {
            var s = e.AsSpawn;
            spawns[minute, s.Role]++;
            if (s.Boss) bosses.Add($"{sim.State.RunClock:F2},{s.T:F3},{content.Roles[s.Role].Id}");
        }
        else if (e.Kind == SimEventKind.Death && e.I0 >= 0)
        {
            kills++;
            killed[minute]++;
        }
        else if (e.Kind == SimEventKind.Attack && !e.AsAttack.ByHero && e.AsAttack.EnemyKind != EnemyAttackKind.Lunge)
        {
            if (e.AsAttack.EnemyKind == EnemyAttackKind.Charge) charges++;
            else slams++;
        }
        else if (e.Kind == SimEventKind.StructureBreak && e.AsStructureBreak.Source == StructureHit.BossSlam) slamBreaks++;
        else if (e.Kind == SimEventKind.Hit && e.AsHit.OnHero && e.AsHit.EnemyKind != EnemyAttackKind.Lunge) bossHits++;
        else if (e.Kind == SimEventKind.Drop && e.AsDrop.ItemKind == ItemKind.Chest)
            chests.Add($"{e.T:F3},{e.AsDrop.Value},{e.AsDrop.Minute}");
    }
    cursor = sim.EventCount;
    sim.DiscardEventsBefore(cursor);
    maxAlive = Math.Max(maxAlive, sim.State.EnemiesAlive);
}

Directory.CreateDirectory(outDir);
using (var w = new StreamWriter(Path.Combine(outDir, "spawns.csv")))
{
    w.WriteLine("minute,reliable_s,killed,spawned," + string.Join(",", content.Roles.Select(r => r.Id)));
    for (int m = 0; m < minutes; m++)
    {
        var row = Enumerable.Range(0, content.Roles.Length).Select(r => spawns[m, r]).ToArray();
        w.WriteLine($"{m},{reliable[m]:F1},{killed[m]},{row.Sum()}," + string.Join(",", row));
    }
}
File.WriteAllLines(Path.Combine(outDir, "bosses.csv"), new[] { "run_clock,song_t,role" }.Concat(bosses));
File.WriteAllLines(Path.Combine(outDir, "chests.csv"), new[] { "song_t,tier,minute" }.Concat(chests));
Console.WriteLine($"song {sim.Now:F1} s, run clock {sim.State.RunClock:F1} s, phase {sim.State.Phase}, end {sim.State.End}");
Console.WriteLine($"spawned {spawns.Cast<int>().Sum()}, killed {kills}, most alive {maxAlive} (cap {content.Schedule.Cap}), bosses {bosses.Count}, chests {chests.Count}");
Console.WriteLine($"boss telegraphs: {charges} charges, {slams} slams; {slamBreaks} structures broken by slams, {bossHits} charge or slam hits on the hero");
Console.WriteLine($"wall {watch.Elapsed.TotalSeconds:F1} s");
