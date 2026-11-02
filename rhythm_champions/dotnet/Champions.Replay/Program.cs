using System;
using System.Diagnostics;
using System.IO;
using Champions.Sim;

// Replays a session folder (plan.md, "Sessions and replay") headlessly in .NET: the plugin's recorded
// entries with their recorded known-at times and the inputs at their effective times, through the sim on
// the session's run inputs and the game's content (rhythm_champions/Content), to the run's end; then
// compares the state hash. Exit 0: the hash matches; 1: it differs from a session recorded in .NET, a
// failure; 2: it differs from one recorded in another runtime (the player's), which may round
// differently: replay it there; 64: usage.
//
// --update writes the replay's hash and event count into the session instead (after a deliberate change
// to the rules or the content: say so in the commit).
//
//   champions-replay SESSION [--content DIR] [--update]
static class Program
{
    static int Main(string[] args)
    {
        string session = null, content = null;
        bool update = false;
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--content" && i + 1 < args.Length) content = args[++i];
            else if (args[i] == "--update") update = true;
            else if (session == null && !args[i].StartsWith("--", StringComparison.Ordinal)) session = args[i];
            else return Usage($"unknown argument {args[i]}");
        }
        if (session == null) return Usage("which session?");
        content ??= GameContent.Find(Directory.GetCurrentDirectory()) ?? GameContent.Find(AppContext.BaseDirectory);
        if (content == null) return Usage("no rhythm_champions/Content above here: give --content");

        var started = Stopwatch.StartNew();
        var record = SessionRecord.Read(session);
        var entries = SessionRecord.LoadEntries(session);
        var replay = new SessionReplay(record);
        foreach (var e in entries) replay.Supply(e);
        if (!replay.AllSupplied)
        {
            Console.Error.WriteLine($"replay: {session}: the run took {record.Entries.Length} entries, the record holds {entries.Count}");
            return 1;
        }
        var sim = new Simulation(record.Run.Build(GameContent.Load(content)));
        replay.StepTo(sim, double.PositiveInfinity);

        bool matches = replay.Matches(sim);
        bool sameRuntime = record.Runtime.StartsWith(".NET", StringComparison.Ordinal);
        Console.WriteLine($"replay: {session}: {record.Entries.Length} entries, {record.Inputs.Length} inputs to song {sim.Now:F3} s " +
                          $"in {started.Elapsed.TotalSeconds:F1} s; hash {sim.Hash:X16} ({sim.EventCount} events), recorded {record.Hash:X16} " +
                          $"({record.Events} events) in {(record.Runtime.Length > 0 ? record.Runtime : "an unknown runtime")}: " +
                          (matches ? "matches" : sameRuntime ? "DIFFERS" : $"differs (recorded in {record.Runtime}: replay it there)"));
        if (update)
        {
            if (!replay.Done(sim))
            {
                Console.Error.WriteLine($"replay: {session}: the replay didn't reach the run's end; not updated");
                return 1;
            }
            record.Hash = sim.Hash;
            record.Events = sim.EventCount;
            record.Runtime = SessionRecord.CurrentRuntime;
            record.Write(session);
            Console.WriteLine($"replay: {session}: updated to {sim.Hash:X16} ({sim.EventCount} events)");
            return 0;
        }
        return matches ? 0 : sameRuntime ? 1 : 2;
    }

    static int Usage(string why)
    {
        Console.Error.WriteLine($"replay: {why}\nusage: champions-replay SESSION [--content DIR] [--update]");
        return 64;
    }
}
