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

// The finishers' rings over one entry dump (native's `ps entries`), in a zen run with `--finishers` driven as the sweeps
// drive a run (BotRun: 60 fps frames, each entry arriving 60 ms after its stamp, the run's level, seed 1, the headless
// start), the hero standing and walking a square (each arrow held 2 s in turn), with the starting sword and no cards.
// Rings are answered in turn: the even ones with Space heard near the downbeat (-60, -20, +20 or +60 ms, in turn), the
// odd ones with Space heard 180 ms after it (outside the window), each key taken a live lead (90 ms) after it was
// heard, and planned on the ring's downbeat as its latest event has it (a player follows a retimed ring). Every ring goes to OUT_DIR/LABEL.csv (its beat, start, downbeat as first predicted and as it ended, its
// retimes, the press, the outcome, the slam's impact, kills and structure breaks); a summary on stdout.
//
//   dotnet run --project game/evidence/zen-finishers/Finishers -c Release -- OUT_DIR LABEL DUMP.jsonl
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

const double Lead = 0.09, MissOffset = 0.18;
double[] hitOffsets = { -0.06, -0.02, 0.02, 0.06 };
var config = RunLevel.Config(content, arena, RunLevel.GameFootprint);
config.Start = RunStart.AfterClearBeat;
config.Mode = GameMode.Zen;
config.Finishers = true;
config.HeroStands = true;
// Nobody answers the level-up's cards, which hold the world.
config.Arsenal.Cards = false;
var sim = new Simulation(config);

var rings = new SortedDictionary<int, Ring>();
// Each ring's press, planned on its downbeat and moved with its retimes until taken.
var presses = new SortedDictionary<int, (double taken, double heard)>();
var deaths = new List<double>();
var breaks = new List<double>();
// The time the beat was reliable (rings start only then), from the beat-state changes.
double reliableS = 0, reliableFrom = double.NaN;
var walk = new[] { Key.Right, Key.Up, Key.Left, Key.Down };
int next = 0, cursor = 0, held = -1, hits = 0;
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
    foreach (var id in presses.Keys.ToList())
    {
        var (taken, heard) = presses[id];
        if (taken > sim.Now + 1e-9) continue;
        sim.Press(Key.Space, heard);
        sim.Release(Key.Space, heard + 0.05);
        presses.Remove(id);
    }
    sim.StepTo(frontier);
    foreach (var e in sim.EventsSince(cursor))
    {
        if (e.Kind == SimEventKind.Death && e.I0 >= 0) deaths.Add(e.T);
        else if (e.Kind == SimEventKind.StructureBreak) breaks.Add(e.T);
        else if (e.Kind == SimEventKind.BeatState)
        {
            var b = e.AsBeatState;
            if (b.Now == Reliability.Reliable) reliableFrom = e.T;
            else if (!double.IsNaN(reliableFrom))
            {
                reliableS += e.T - reliableFrom;
                reliableFrom = double.NaN;
            }
        }
        if (e.Kind != SimEventKind.Finisher) continue;
        var f = e.AsFinisher;
        if (f.Phase == FinisherPhase.Ring)
        {
            var ring = new Ring { Id = f.Ring, Beat = f.Beat, StartedAt = f.T, Start = f.Start, First = f.Downbeat, Downbeat = f.Downbeat };
            rings[f.Ring] = ring;
            ring.Offset = f.Ring % 2 == 0 ? hitOffsets[(f.Ring / 2) % hitOffsets.Length] : MissOffset;
            presses[f.Ring] = (f.Downbeat + ring.Offset + Lead, f.Downbeat + ring.Offset);
            continue;
        }
        var r = rings[f.Ring];
        r.Downbeat = f.Downbeat;
        r.Beat = f.Beat;
        if (f.Phase == FinisherPhase.Retime)
        {
            // The player follows the ring as it eases onto its new downbeat.
            if (presses.ContainsKey(f.Ring)) presses[f.Ring] = (f.Downbeat + r.Offset + Lead, f.Downbeat + r.Offset);
            r.Retimes++;
            r.MaxShift = Math.Max(r.MaxShift, Math.Abs(f.Downbeat - r.First));
            continue;
        }
        r.Outcome = f.Phase;
        if (f.Phase == FinisherPhase.Hit)
        {
            r.Impact = f.Impact;
            r.Heard = f.Heard;
            hits++;
        }
    }
    cursor = sim.EventCount;
    sim.DiscardEventsBefore(cursor);
}

if (!double.IsNaN(reliableFrom)) reliableS += sim.Now - reliableFrom;
var beats = sim.Stream.Beats.ToArray();
foreach (var r in rings.Values)
{
    r.Final = r.Beat < beats.Length ? beats[r.Beat].T : double.NaN;
    if (r.Outcome != FinisherPhase.Hit) continue;
    r.Kills = deaths.Count(t => Math.Abs(t - r.Impact) < 1e-9);
    r.Breaks = breaks.Count(t => t >= r.Impact - 1e-9 && t < r.Impact + 0.5);
}

Directory.CreateDirectory(outDir);
using (var csv = new StreamWriter(Path.Combine(outDir, label + ".csv")))
{
    csv.WriteLine("ring,beat,started_s,closes_from_s,downbeat_first_s,downbeat_s,beat_s,retimes,max_shift_ms,press_offset_ms,outcome,impact_s,kills,breaks");
    foreach (var r in rings.Values)
        csv.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"{r.Id},{r.Beat},{r.StartedAt:F4},{r.Start:F4},{r.First:F4},{r.Downbeat:F4},{r.Final:F4},{r.Retimes},{r.MaxShift * 1000:F1},{r.Offset * 1000:F0},{r.Outcome},{(r.Outcome == FinisherPhase.Hit ? r.Impact.ToString("F4", CultureInfo.InvariantCulture) : "")},{r.Kills},{r.Breaks}"));
}

var done = rings.Values.Where(r => r.Outcome != null).ToList();
var gaps = done.Zip(done.Skip(1), (a, b) => b.Downbeat - a.Downbeat).ToList();
var leads = done.Select(r => r.Downbeat - r.StartedAt).ToList();
var periods = beats.Select(b => b.V0).OrderBy(p => p).ToList();
double median = periods.Count > 0 ? periods[periods.Count / 2] : double.NaN;
var hit = done.Where(r => r.Outcome == FinisherPhase.Hit).ToList();
Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
    $"{label}: {beats.Length} beats, median period {median:F3} s ({60 / median:F0} BPM), phrase {Finishers.PhraseBeats(median)} beats, telegraph {Finishers.Telegraph(median):F2} s; {rings.Count} rings, {hit.Count} hit, {done.Count(r => r.Outcome == FinisherPhase.Miss)} missed, {done.Count(r => r.Outcome == FinisherPhase.Drop)} dropped; the beat reliable {reliableS:F0} s of {sim.Now:F0}"));
if (gaps.Count > 0)
    Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
        $"  rings apart: {gaps.Min():F2}-{gaps.Max():F2} s; started {leads.Min():F2}-{leads.Max():F2} s before the downbeat"));
Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
    $"  downbeats on their beat: {done.Count(r => Math.Abs(r.Downbeat - r.Final) < 1e-9)} of {done.Count}; first prediction off by {(done.Count > 0 ? done.Max(r => Math.Abs(r.First - r.Final)) * 1000 : 0):F1} ms at most; {done.Sum(r => r.Retimes)} retimes"));
if (hit.Count > 0)
    Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
        $"  hits: impact {hit.Min(r => r.Impact - r.Heard) * 1000:F1} ms after the press; kills {hit.Min(r => r.Kills)}-{hit.Max(r => r.Kills)} (median {hit.Select(r => r.Kills).OrderBy(x => x).ElementAt(hit.Count / 2)}); structure breaks {hit.Sum(r => r.Breaks)}"));
Console.WriteLine($"  misses (Space 180 ms late): {done.Count(r => r.Outcome == FinisherPhase.Miss)} rings, none fired");

sealed class Ring
{
    public int Id, Beat, Retimes, Kills, Breaks;
    public double StartedAt, Start, First, Downbeat, Final, MaxShift, Offset, Impact, Heard;
    public FinisherPhase? Outcome;
}
