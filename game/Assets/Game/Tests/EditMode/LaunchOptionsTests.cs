using System;
using System.IO;
using Game.Client;
using Game.Client.Arena;
using NUnit.Framework;
using PS.Game.Sim;
using PS.Game.Sim.Bots;
using PS.Look;
using UnityEngine;

namespace Game.Tests
{
    /// <summary>The command line, parsed once: the launch's mode, the run's inputs and whether a session can record them.</summary>
    public class LaunchOptionsTests
    {
        static LaunchOptions Parse(string flags, bool batchMode = false) =>
            LaunchOptions.Parse(("player " + flags).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries), batchMode);

        static readonly object[] Modes =
        {
            // flags, batch mode: mode, title screen, recording, start
            new object[] { "", false, LaunchMode.Windowed, true, true, RunStart.OnKey },
            new object[] { "--file a.ogg", false, LaunchMode.Windowed, false, true, RunStart.OnKey },
            new object[] { "-batchmode", true, LaunchMode.Headless, false, false, RunStart.Immediately },
            new object[] { "-batchmode --file a.ogg", true, LaunchMode.Headless, false, false, RunStart.AfterClearBeat },
            new object[] { "--headless --file a.ogg", false, LaunchMode.Headless, false, false, RunStart.AfterClearBeat },
            new object[] { "--fixed-time --file a.ogg", true, LaunchMode.FixedTime, false, false, RunStart.AfterClearBeat },
            new object[] { "--fixed-time --title", true, LaunchMode.FixedTime, true, false, RunStart.AfterClearBeat },
            new object[] { "--events e.csv", false, LaunchMode.Windowed, false, true, RunStart.Immediately },
            new object[] { "--headless --file a.ogg --sessions out", false, LaunchMode.Headless, false, true, RunStart.AfterClearBeat },
            new object[] { "--headless --file a.ogg --watch", false, LaunchMode.Headless, false, true, RunStart.AfterClearBeat },
            new object[] { "--file a.ogg --no-session", false, LaunchMode.Windowed, false, false, RunStart.OnKey },
            new object[] { "--replay dir", false, LaunchMode.Windowed, false, true, RunStart.OnKey },
        };

        [TestCaseSource(nameof(Modes))]
        public void TheModeAndWhatHangsOnItComeFromOneParse(string flags, bool batchMode, LaunchMode mode, bool title, bool recording, RunStart start)
        {
            var o = Parse(flags, batchMode);
            Assert.That(o.Mode, Is.EqualTo(mode));
            Assert.That(o.Title, Is.EqualTo(title), "the title screen");
            Assert.That(o.Recording, Is.EqualTo(recording), "recording");
            Assert.That(o.Run.Start, Is.EqualTo(start), "the run's start: on a key only with a player at the keys");
        }

        static readonly object[] Levels =
        {
            // flags: level, start minute, unrecordable
            new object[] { "--file a.ogg", SessionLevel.Slice, 0, null },
            new object[] { "--file a.ogg --run", SessionLevel.Run, 0, null },
            new object[] { "--file a.ogg --start-minute 25", SessionLevel.Run, 25, null },
            new object[] { "--file a.ogg --events e.csv --start-minute 25", SessionLevel.Open, 0, null },
            new object[] { "--file a.ogg --cast bat1,zombie", SessionLevel.Cast, 0, "--cast" },
            new object[] { "--file a.ogg --arsenal garlic:8,spinach:1", SessionLevel.Slice, 0, "--arsenal" },
            new object[] { "--file a.ogg --run --no-cards", SessionLevel.Run, 0, "--no-cards" },
            new object[] { "--file a.ogg --level Content", SessionLevel.Run, 0, "--level" },
            // An open level has no arsenal for them to change: it records as it always has.
            new object[] { "--file a.ogg --events e.csv --arsenal garlic:8 --no-cards", SessionLevel.Open, 0, null },
        };

        [TestCaseSource(nameof(Levels))]
        public void TheRunsInputsSayWhetherASessionCanRecordThem(string flags, SessionLevel level, int startMinute, string unrecordable)
        {
            var run = Parse(flags).Run;
            Assert.That(run.Level, Is.EqualTo(level));
            Assert.That(run.StartMinute, Is.EqualTo(startMinute));
            Assert.That(run.Unrecordable, Is.EqualTo(unrecordable));
        }

        [Test]
        public void TheRunsOtherInputsAndScriptedPlay()
        {
            var o = Parse("--headless --file a.ogg --seed 7 --easy --arsenal magic-wand:8,empty-tome:1 --bot idle --pause-at 12.5 " +
                          "--debug change@7 --walk right@9-11 --walk-lead 0.3 --loop-out loop.csv");
            Assert.That(o.Run.Seed, Is.EqualTo(7UL));
            Assert.That(o.Run.Easy, Is.True);
            Assert.That(o.Run.Loadout, Is.EqualTo(new[] { ("magic-wand", 8), ("empty-tome", 1) }));
            Assert.That(o.Scripted.Bot, Is.EqualTo(BotKind.Idle));
            Assert.That(o.Scripted.PauseAt, Is.EqualTo(12.5));
            Assert.That(o.Scripted.Debug, Is.EqualTo("change@7"));
            Assert.That(o.Scripted.Walk, Is.EqualTo("right@9-11"));
            Assert.That(o.Scripted.WalkLead, Is.EqualTo(0.3).Within(1e-6));
            Assert.That(System.IO.Path.IsPathRooted(o.Outputs.Loop), Is.True, "paths are absolute");
            Assert.That(System.IO.Path.IsPathRooted(o.File), Is.True);
            Assert.That(o.Flags[0], Is.EqualTo("--headless"), "the flags without the executable");
            Assert.That(Parse("").Scripted.Bot, Is.Null);
            Assert.That(Parse("").Run.Mode, Is.EqualTo(GameMode.Grid));
            Assert.That(Parse("--mode free").Run.Mode, Is.EqualTo(GameMode.Free));
            Assert.That(Parse("").Scripted.PauseAt, Is.LessThan(0));
            Assert.That(Parse("").Scripted.WalkLead, Is.EqualTo(0), "keys reach the sim at once");
        }

        [Test]
        public void BadFlagsFailAtTheStartNotAfterTheRun()
        {
            Assert.Throws<ArgumentException>(() => Parse("--start-minute -1"));
            Assert.Throws<ArgumentException>(() => Parse("--arsenal garlic"));
            Assert.Throws<ArgumentException>(() => Parse("--bot fast"));
            Assert.That(Assert.Throws<ArgumentException>(() => Parse("--mode fast")).Message, Does.Contain("grid, free"), "the modes listed");
            Assert.Throws<ArgumentException>(() => Parse("--mode free --bot positioning"), "the bots play the grid");
            // --watch replays the run it records: one that can't be recorded leaves nothing to watch.
            Assert.Throws<ArgumentException>(() => Parse("--headless --file a.ogg --watch --cast bat1"));
            Assert.Throws<ArgumentException>(() => Parse("--headless --file a.ogg --watch --level Content"));
        }

        static readonly string[] Recognized =
        {
            // just game's own launch, Unity's and the tools' one-hyphen flags with their values, every flag read.
            "-screen-width 1920 -screen-height 1080 -screen-fullscreen 0 --file a.m3u --run --shuffle",
            "-batchmode -force-metal -logFile p.log --headless --file a.ogg --seconds 10 --sessions out",
            "--events e.csv --run --cast bat1 --start-minute 3", // --run read though --events wins
            "--spike --spike-out dir --file a.ogg",
        };

        [TestCaseSource(nameof(Recognized))]
        public void ALaunchOfKnownFlagsHasNothingUnrecognized(string flags) =>
            Assert.That(Parse(flags).Unrecognized, Is.Empty);

        static readonly object[] Unrecognized =
        {
            // flags: the notes
            new object[] { "--file a.m3u \u2013shuffle", new[] { "\u2013shuffle: starts with an en dash (\u2013), not two hyphens; did you mean --shuffle?" } },
            new object[] { "--file a.m3u --shufle", new[] { "--shufle: not a flag the game knows; did you mean --shuffle?" } },
            new object[] { "--frobnicate", new[] { "--frobnicate: not a flag the game knows" } },
            new object[] { "-shuffle", new[] { "-shuffle: has one hyphen; did you mean --shuffle?" } },
            new object[] { "--run extra", new[] { "extra: follows --run, which takes no value" } },
            new object[] { "a.ogg --run", new[] { "a.ogg: no flag takes this argument" } },
            new object[] { "--seeed 3", new[] { "--seeed: not a flag the game knows; did you mean --seed?", "3: follows --seeed, which isn't recognized" } },
            new object[] { "--run --file", new[] { "--file: needs a value after it" } },
        };

        [TestCaseSource(nameof(Unrecognized))]
        public void EachUnrecognizedArgumentGetsANote(string flags, string[] notes) =>
            Assert.That(Parse(flags).Unrecognized, Is.EqualTo(notes));

        [Test]
        public void TheLookFlagsAreReadByTheLookPackage()
        {
            var o = Parse("--headless --look hologram --monster-look solid-emissive --world pure-void --beat-ripple --palette ion");
            Assert.That(o.Unrecognized, Is.Empty);
            Assert.That(o.Look.Look, Is.EqualTo("hologram"));
            Assert.That(o.Look.Overrides[LookAxes.Monsters], Is.EqualTo("solid-emissive"));
            Assert.That(o.Look.BeatRipple, Is.True);
            Assert.That(ResolvedLook.Resolve(o.Look).Describe(), Does.Contain("monsters solid-emissive (--monster-look)"));
        }

        [Test]
        public void PaletteNamesTheLooksPaletteOrElseTheArenas()
        {
            var look = Parse("--palette ion");
            Assert.That(look.Look.LookPalette, Is.EqualTo("ion"));
            Assert.That(look.Palette, Is.EqualTo(ArenaLighting.DefaultPalette), "the arena keeps its default zone palette");
            var arena = Parse("--palette lava");
            Assert.That(arena.Palette, Is.EqualTo("lava"));
            Assert.That(arena.Look.LookPalette, Is.Null);
            Assert.That(LaunchOptions.Parse(new[] { "player", "--palette", "" }, false).Palette, Is.Empty, "the night as baked");
            // A name both could mean would be read as the look's and leave its zone palette unreachable.
            string layout = File.ReadAllText(Path.Combine(Application.dataPath, "..", "Content", "arena-layout.json"));
            foreach (var zone in ArenaLighting.Palettes(layout))
                Assert.That(LookLibrary.Shared.HasPalette(zone), Is.False, $"the arena's {zone} is also a look palette");
        }

        [Test]
        public void TheRunsInputsOnTheBuild()
        {
            double[] footprint = { -1, -1, 1, -1, 1, 1, -1, 1 };
            var run = Parse("--file a.ogg --start-minute 3").Run.On(baked: true, arena: true, footprint);
            Assert.That(run.Arena, Is.True);
            Assert.That(run.Footprint, Is.EqualTo(footprint));
            var bare = Parse("--file a.ogg --start-minute 3 --arsenal garlic:8").Run.On(baked: false, arena: true, footprint);
            Assert.That(bare.Level, Is.EqualTo(SessionLevel.Open), "no level bake: open ground");
            Assert.That(bare.Arena, Is.False);
            Assert.That(bare.Footprint, Is.Empty);
            Assert.That(bare.StartMinute, Is.EqualTo(0));
            Assert.That(bare.Unrecordable, Is.Null);
        }
    }
}
