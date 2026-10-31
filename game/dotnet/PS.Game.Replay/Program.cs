using System;
using System.Diagnostics;
using System.IO;
using PS.Game.Sim;
using PS.Game.Sim.Content;
using PS.Game.Sim.Level;

// Replays a session folder (unity-game.md, "Sessions and Replay") headlessly in .NET: the plugin's
// recorded entries with their recorded known-at times, the keys and debug commands at their effective
// times, through the sim on the session's config and the game's content (game/Content), to the run's
// end; then compares the hash. Exit 0: the hash matches; 1: it differs from a session recorded in
// .NET, a failure; 2: it differs from one recorded in another runtime (the player's), which may round
// differently (unity-game.md, "Shape and Determinism": replay it there with --replay); 64: usage.
//
//   ps-game-replay SESSION [--content DIR]   (DIR: game/Content, found from here by default)
static class Program
{
    static int Main(string[] args)
    {
        string session = null, content = null;
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--content" && i + 1 < args.Length) content = args[++i];
            else if (session == null && !args[i].StartsWith("--", StringComparison.Ordinal)) session = args[i];
            else return Usage($"unknown argument {args[i]}");
        }
        if (session == null) return Usage("which session?");
        content ??= FindContent();
        if (content == null) return Usage("no game/Content above here: give --content");

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
        var level = GameContent.Load(content);
        var arena = record.Run.Arena ? ArenaData.Parse(File.ReadAllText(Path.Combine(content, "arena.json")), "arena.json") : null;
        var sim = new Simulation(record.Run.Build(level, arena));
        replay.StepTo(sim, double.PositiveInfinity);

        bool matches = replay.Matches(sim);
        bool sameRuntime = record.Runtime.StartsWith(".NET", StringComparison.Ordinal);
        Console.WriteLine($"replay: {session}: {record.Entries.Length} entries, {record.Keys.Length} key changes, " +
                          $"{record.Commands.Length} debug commands to song {sim.Now:F3} s in {started.Elapsed.TotalSeconds:F1} s; " +
                          $"hash {sim.Hash:X16} ({sim.EventCount} events), recorded {record.Hash:X16} ({record.Events} events) " +
                          $"in {(record.Runtime.Length > 0 ? record.Runtime : "an unknown runtime")}: " +
                          (matches ? "matches" : sameRuntime ? "DIFFERS" : $"differs (recorded in {record.Runtime}: replay it there)"));
        return matches ? 0 : sameRuntime ? 1 : 2;
    }

    static string FindContent()
    {
        for (var dir = new DirectoryInfo(Directory.GetCurrentDirectory()); dir != null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "game", "Content");
            if (File.Exists(Path.Combine(candidate, "enemies.json"))) return candidate;
        }
        return null;
    }

    static int Usage(string why)
    {
        Console.Error.WriteLine($"replay: {why}\nusage: ps-game-replay SESSION [--content DIR]");
        return 64;
    }
}
