using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using PS.Game.Sim;
using PS.Game.Sim.Bots;
using PS.Game.Sim.Content;
using PS.Game.Sim.Level;

// One bot's run for `game sweep` (unity-game.md, Tools, Checks and Evidence: bots and sweeps): the
// run's level in the game's arena, its seed, the headless start, over a playlist of entry dumps
// (native's `ps entries`). Each --track is one dump, laid after the one before with --gap seconds
// of silence before it (native's playlist timeline: no entries in a gap, as native's cached-track
// streams have none), its times moved by its start; a single dump of a whole playlist is the
// continuous stream. Writes the bot's report as JSON. Exit 0: the run was played; 64: usage.
//
//   ps-game-sweep --bot positioning|idle --seed N --minutes M --out FILE.json
//                 --track DUMP [--gap S] [--track DUMP [--gap S] ...] [--content DIR]
static class Program
{
    const double Rate = 44100;

    static int Main(string[] args)
    {
        string bot = null, outPath = null, content = null;
        ulong seed = 1;
        double minutes = 30;
        var tracks = new List<(string Dump, double Gap)>();
        for (int i = 0; i < args.Length; i++)
        {
            string value = i + 1 < args.Length ? args[i + 1] : null;
            switch (args[i])
            {
                case "--bot" when value != null: bot = value; i++; break;
                case "--seed" when value != null && ulong.TryParse(value, out seed): i++; break;
                case "--minutes" when value != null && double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out minutes): i++; break;
                case "--out" when value != null: outPath = value; i++; break;
                case "--content" when value != null: content = value; i++; break;
                case "--track" when value != null: tracks.Add((value, tracks.Count == 0 ? 0 : 2)); i++; break;
                case "--gap" when value != null && tracks.Count > 0
                                  && double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double gap):
                    tracks[^1] = (tracks[^1].Dump, gap);
                    i++;
                    break;
                default: return Usage($"unknown or incomplete argument {args[i]}");
            }
        }
        var kind = bot == "positioning" ? BotKind.Positioning : bot == "idle" ? BotKind.Idle : (BotKind?)null;
        if (kind == null) return Usage("--bot positioning or idle");
        if (outPath == null || tracks.Count == 0) return Usage("give --out and at least one --track");
        if (!(minutes > 0 && minutes <= 30)) return Usage("--minutes must be in (0, 30]");
        content ??= FindContent();
        if (content == null) return Usage("no game/Content above here: give --content");

        var entries = Playlist(tracks, out double duration);
        var level = GameContent.Load(content);
        var arena = ArenaData.Parse(File.ReadAllText(Path.Combine(content, "arena.json")), "arena.json");
        var config = RunLevel.Config(level, arena, RunLevel.GameFootprint, seed);
        config.Start = RunStart.AfterClearBeat;
        var started = Stopwatch.StartNew();
        // A whole run plays to its victory card; a shorter one stops at its minutes of run clock.
        var report = BotRun.Play(config, entries, new SurvivorBot(kind.Value, seed, level), duration,
            minutes >= 30 ? double.PositiveInfinity : minutes * 60, out _);
        double wall = started.Elapsed.TotalSeconds;

        using (var stream = File.Create(outPath))
        using (var w = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            w.WriteStartObject();
            w.WriteString("bot", bot);
            w.WriteNumber("seed", seed);
            w.WriteNumber("minutes", minutes);
            w.WriteNumber("playlistSeconds", duration);
            w.WriteNumber("entries", entries.Count);
            w.WriteNumber("wallSeconds", wall);
            w.WriteNumber("song", report.Song);
            w.WriteNumber("startedAt", report.StartedAt);
            w.WriteStartObject("frozen");
            for (int r = 0; r < report.Frozen.Length; r++)
                w.WriteNumber(r == BotReport.FrozenResuming ? "resuming" : ((FreezeReason)r).ToString(), Math.Round(report.Frozen[r], 3));
            w.WriteEndObject();
            w.WriteNumber("runClock", report.RunClock);
            w.WriteBoolean("fallen", report.Fallen);
            w.WriteBoolean("victory", report.Victory);
            w.WriteNumber("lowestHealthShare", report.LowestHealthShare);
            w.WriteNumber("heroHits", report.HeroHits);
            w.WriteNumber("heroDamage", report.HeroDamage);
            w.WriteNumber("level", report.Level);
            Ints(w, "levelByMinute", report.LevelByMinute);
            w.WriteStartArray("chests");
            foreach (var c in report.Chests)
            {
                w.WriteStartObject();
                w.WriteNumber("runClock", Math.Round(c.RunClock, 3));
                w.WriteNumber("song", Math.Round(c.Song, 3));
                if (!double.IsNaN(c.DroppedAt)) w.WriteNumber("droppedAt", Math.Round(c.DroppedAt, 3));
                w.WriteNumber("minute", c.Minute);
                w.WriteNumber("tier", c.Tier);
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteStartArray("crowd");
            foreach (var c in report.Crowd)
            {
                w.WriteStartObject();
                w.WriteNumber("peak", c.Peak);
                w.WriteNumber("topShare", Math.Round(c.TopShare, 4));
                if (c.TopRole >= 0) w.WriteString("topRole", level.Roles[c.TopRole].Id);
                w.WriteStartObject("atPeak");
                for (int r = 0; r < (c.AtPeak?.Length ?? 0); r++)
                    if (c.AtPeak[r] > 0) w.WriteNumber(level.Roles[r].Id, c.AtPeak[r]);
                w.WriteEndObject();
                // The crowd's colours at the peak: each alive role's crystal (cast.json), as the client tints it.
                w.WriteNumber("coloursAtPeak", Enumerable.Range(0, c.AtPeak?.Length ?? 0).Where(r => c.AtPeak[r] > 0)
                    .Select(r => level.Cast[r].Crystal).Distinct().Count());
                w.WriteEndObject();
            }
            w.WriteEndArray();
            Strings(w, "evolutions", report.Evolutions);
            Strings(w, "arsenal", report.Arsenal);
            w.WriteNumber("peakEnemies", report.PeakEnemies);
            w.WriteNumber("kills", report.Kills);
            w.WriteNumber("freezes", report.Freezes);
            w.WriteString("hash", report.Hash.ToString("X16"));
            w.WriteNumber("events", report.Events);
            w.WriteEndObject();
        }
        Console.WriteLine($"sweep: {bot} seed {seed}: run clock {report.RunClock:F1} s at song {report.Song:F1} s, " +
                          $"{(report.Victory ? "victory" : report.Fallen ? "fell" : "stopped")}, lowest health {report.LowestHealthShare:F3}, " +
                          $"level {report.Level} (minute 5: {report.LevelAt(5)}), {report.Chests.Count} chests, {wall:F1} s");
        return 0;
    }

    /// <summary>The tracks' entries on one timeline, ordered by stamp (emission order within a stamp), and its length.</summary>
    static List<StreamEntry> Playlist(List<(string Dump, double Gap)> tracks, out double duration)
    {
        var all = new List<StreamEntry>();
        long samples = 0;
        foreach (var (dump, gap) in tracks)
        {
            samples += (long)Math.Round(gap * Rate);
            double start = samples / Rate;
            var entries = Load(dump, out double length);
            foreach (var e in entries) all.Add(Offset(e, start));
            samples += (long)Math.Round(length * Rate);
        }
        duration = samples / Rate;
        return all.OrderBy(e => e.Stamp).ToList();
    }

    /// <summary>An entry moved <paramref name="by"/> seconds later: its time, its stamp, and a hindsight beat's window end.</summary>
    static StreamEntry Offset(StreamEntry e, double by)
    {
        e.T += by;
        e.Stamp += by;
        if (e.Kind == EntryKind.HindsightBeat) e[1] += by;
        return e;
    }

    static List<StreamEntry> Load(string dump, out double duration)
    {
        var entries = new List<StreamEntry>();
        duration = double.NaN;
        foreach (string line in File.ReadLines(dump))
        {
            if (line.Length == 0) continue;
            using var doc = JsonDocument.Parse(line);
            var j = doc.RootElement;
            if (j.TryGetProperty("format", out var format))
            {
                if (format.GetString() != "ps-entries" || j.GetProperty("version").GetInt32() != 1)
                    throw new FormatException($"{dump}: not a version 1 ps-entries dump");
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
        if (double.IsNaN(duration)) throw new FormatException($"{dump}: no header");
        return entries;
    }

    static void Ints(Utf8JsonWriter w, string name, List<int> values)
    {
        w.WriteStartArray(name);
        foreach (int v in values) w.WriteNumberValue(v);
        w.WriteEndArray();
    }

    static void Strings(Utf8JsonWriter w, string name, List<string> values)
    {
        w.WriteStartArray(name);
        foreach (string v in values) w.WriteStringValue(v);
        w.WriteEndArray();
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
        Console.Error.WriteLine($"sweep: {why}\nusage: ps-game-sweep --bot positioning|idle --seed N --minutes M --out FILE.json " +
                                "--track DUMP [--gap S] [--track DUMP [--gap S] ...] [--content DIR]");
        return 64;
    }
}
