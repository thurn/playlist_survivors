using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Champions.Client;
using Champions.Client.Loop;
using Champions.Client.Music;
using Champions.Sim;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools.Constraints;
using Is = NUnit.Framework.Is; // over the Unity constraints' Is, whose AllocatingGCMemory extends it

namespace Champions.Tests
{
    /// <summary>The music plugin's C# side: its bindings, the editor's reloadable copy, the clocks and the sim fed from it.</summary>
    public sealed class MusicTests
    {
        /// <summary>The plugin's own test song and its first 20 s of entries (rhythm_champions/plugin/fixtures).</summary>
        const string Song = "plugin/fixtures/house-128.ogg", SongEntries = "plugin/fixtures/house-128-20s.jsonl";
        const double HangGuard = 120; // seconds: only a hung plugin takes this long
        const double Fps = 60;

        static string BuiltPlugin()
        {
            string built = Path.GetFullPath(PluginLoader.BuiltPath);
            if (!File.Exists(built)) Assert.Fail($"no plugin at {built}: run `just champions-tool setup`");
            return built;
        }

        [Test]
        public void TheBindingsHaveTheHeadersLayout()
        {
            Assert.That(Marshal.SizeOf<StreamEntry>(), Is.EqualTo(120));
            Assert.That(Marshal.SizeOf<Plugin.Status>(), Is.EqualTo(80));
            Assert.That((int)Marshal.OffsetOf<Plugin.Status>(nameof(Plugin.Status.Stalls)), Is.EqualTo(48));
            Assert.That((int)Marshal.OffsetOf<Plugin.Status>(nameof(Plugin.Status.FollowerGain)), Is.EqualTo(68));
            Assert.That((int)Marshal.OffsetOf<Plugin.Status>(nameof(Plugin.Status.DeviceChanged)), Is.EqualTo(76));
            Assert.That((int)Marshal.OffsetOf<Plugin.Config>(nameof(Plugin.Config.Pid)), Is.EqualTo(4));
            Assert.That((int)Marshal.OffsetOf<Plugin.Config>(nameof(Plugin.Config.M)), Is.EqualTo(8));
            Assert.That((int)Marshal.OffsetOf<Plugin.Config>(nameof(Plugin.Config.Path)), Is.EqualTo(24));
            Assert.That((int)Marshal.OffsetOf<Plugin.Config>(nameof(Plugin.Config.HindsightJobs)), Is.EqualTo(48));
            Assert.That((int)Marshal.OffsetOf<Plugin.Config>(nameof(Plugin.Config.Shuffle)), Is.EqualTo(56));
            Assert.That(Marshal.SizeOf<Plugin.Config>(), Is.EqualTo(64));
        }

        [Test]
        public void AppAndDeviceRecordsReadAsTheHeaderLaysThemOut()
        {
            // ps_app: int32 pid, then a NUL-terminated UTF-8 name of 128 bytes; a full name has no NUL.
            var raw = new byte[2 * Plugin.AppBytes];
            BitConverter.GetBytes(4242).CopyTo(raw, 0);
            System.Text.Encoding.UTF8.GetBytes("Música").CopyTo(raw, 4);
            BitConverter.GetBytes(7).CopyTo(raw, Plugin.AppBytes);
            for (int i = 0; i < Plugin.AppNameBytes; i++) raw[Plugin.AppBytes + 4 + i] = (byte)'a';
            var apps = Plugin.ReadApps(raw, 2);
            Assert.That(apps[0].Pid, Is.EqualTo(4242));
            Assert.That(apps[0].Name, Is.EqualTo("Música"));
            Assert.That(apps[1].Pid, Is.EqualTo(7));
            Assert.That(apps[1].Name, Is.EqualTo(new string('a', Plugin.AppNameBytes)));

            // ps_device: a 256-byte UID, a 128-byte name, a double latency, an int32 Bluetooth flag.
            var device = new byte[Plugin.DeviceBytes];
            System.Text.Encoding.UTF8.GetBytes("BuiltInSpeakerDevice").CopyTo(device, 0);
            System.Text.Encoding.UTF8.GetBytes("MacBook Pro Speakers").CopyTo(device, Plugin.DeviceUidBytes);
            BitConverter.GetBytes(0.0123).CopyTo(device, Plugin.DeviceUidBytes + Plugin.DeviceNameBytes);
            BitConverter.GetBytes(1).CopyTo(device, Plugin.DeviceUidBytes + Plugin.DeviceNameBytes + 8);
            var d = Plugin.ReadDevice(device);
            Assert.That((d.Uid, d.Name, d.Latency, d.Bluetooth), Is.EqualTo(("BuiltInSpeakerDevice", "MacBook Pro Speakers", 0.0123, true)));
        }

        [Test]
        public void ThePluginListsTheAppsPlayingNowAndTheOutputDevice()
        {
            BuiltPlugin();
            // Whatever is playing on this machine (often nothing): the call succeeds and names each one.
            foreach (var app in Plugin.ListApps())
            {
                Assert.That(app.Pid, Is.GreaterThan(0));
                Assert.That(app.Name, Is.Not.Empty);
            }
            var device = Plugin.OutputDevice();
            if (device.Uid.Length > 0) Assert.That(device.Latency, Is.GreaterThanOrEqualTo(0));
        }

        [Test]
        public void AppPicksItsNameExactlyThenByAPartThenAPid()
        {
            var apps = new[] { new Plugin.App(10, "Music Helper"), new Plugin.App(11, "Music"), new Plugin.App(12, "Google Chrome") };
            Assert.That(MusicSource.Pick(apps, "music")?.Pid, Is.EqualTo(11));
            Assert.That(MusicSource.Pick(apps, "chrome")?.Pid, Is.EqualTo(12));
            Assert.That(MusicSource.Pick(apps, "help")?.Pid, Is.EqualTo(10));
            Assert.That(MusicSource.Pick(apps, "Spotify"), Is.Null);
            Assert.That(MusicSource.Pick(apps, "12")?.Name, Is.EqualTo("Google Chrome"));
            // A pid nobody lists is any process with audio (a tool started from a terminal).
            var other = MusicSource.Pick(apps, "999");
            Assert.That(other?.Pid, Is.EqualTo(999));
            Assert.That(MusicSource.App(other.Value).IsApp, Is.True);
            Assert.That(MusicSource.File("/music/mix.m3u").Tag, Is.EqualTo("mix.m3u"));
        }

        [Test]
        public void LaunchOptionsTakeOneMusicSource()
        {
            var file = LaunchOptions.Parse(new[] { "player", "--file", "song.ogg", "--seconds", "20" }, batchMode: true);
            Assert.That((file.File, file.App, file.Unrecognized.Length), Is.EqualTo(("song.ogg", (string)null, 0)));
            var app = LaunchOptions.Parse(new[] { "player", "--app", "Music" }, batchMode: false);
            Assert.That((app.File, app.App), Is.EqualTo(((string)null, "Music")));
            Assert.Throws<ArgumentException>(() => LaunchOptions.Parse(new[] { "player", "--file", "a.ogg", "--app", "Music" }, false));
        }

        [Test]
        public void ARebuiltPluginLoadsAsANewCopyWithoutARestart()
        {
            string dir = Path.Combine(Path.GetTempPath(), "ps-plugin-reload-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            string built = Path.Combine(dir, "libps_plugin.dylib");
            string rebuiltCopy = null;
            try
            {
                File.Copy(BuiltPlugin(), built);
                PluginLoader.Load(built);
                string first = PluginLoader.LoadedPath;

                // A rebuild: the same code under another signature identifier, so other bytes.
                Run("/usr/bin/codesign", $"--force --sign - --identifier ps.reload.{Guid.NewGuid():N} \"{built}\"");
                var fns = PluginLoader.Load(built);
                rebuiltCopy = PluginLoader.LoadedPath;

                Assert.That(rebuiltCopy, Is.Not.EqualTo(first));
                Assert.That(Path.GetDirectoryName(rebuiltCopy), Is.EqualTo(Path.GetFullPath(PluginLoader.CopiesDir)));
                Assert.That(PluginLoader.Current, Is.SameAs(fns));
                // The new copy's functions run: a null path fails as the header says.
                var config = new Plugin.Config { Source = Plugin.SourceFile, M = 3, OutputRate = 48000 };
                Assert.That(Plugin.Open(ref config, out IntPtr session), Is.EqualTo(Plugin.ErrArgument));
                Assert.That(session, Is.EqualTo(IntPtr.Zero));
                Assert.That(Plugin.LastError(), Is.Not.Empty);
            }
            finally
            {
                PluginLoader.Load(BuiltPlugin());
                Directory.Delete(dir, true);
                if (rebuiltCopy != null) File.Delete(rebuiltCopy); // mapped images survive the unlink
            }
        }

        static void Run(string exe, string args)
        {
            using (var p = Process.Start(new ProcessStartInfo(exe, args) { UseShellExecute = false, RedirectStandardError = true }))
            {
                string err = p.StandardError.ReadToEnd();
                p.WaitForExit();
                Assert.That(p.ExitCode, Is.EqualTo(0), $"{exe} {args}: {err}");
            }
        }

        [Test]
        public void AFileSessionPlaysItsMusicAndCommitsBeatsAhead()
        {
            BuiltPlugin();
            const int rate = 48000, frames = 512;
            var config = new Plugin.Config
            {
                Source = Plugin.SourceFile,
                M = MusicPlayer.M,
                OutputRate = rate,
                Path = Plugin.Utf8(Path.GetFullPath(Song)),
                Models = Plugin.Utf8(File.Exists(MusicPlayer.ModelsPath) ? MusicPlayer.ModelsPath : null),
            };
            IntPtr session;
            try
            {
                Assert.That(Plugin.Open(ref config, out session), Is.EqualTo(Plugin.Ok), Plugin.LastError());
            }
            finally
            {
                Marshal.FreeHGlobal(config.Path);
                if (config.Models != IntPtr.Zero) Marshal.FreeHGlobal(config.Models);
            }
            var buffer = new float[frames * 2];
            var entries = new StreamEntry[256];
            int buffers = 0;
            double lastBeat = double.NegativeInfinity, loudest = 0, dsp = 0;
            var clock = Stopwatch.StartNew();
            // Keeps the beats among the entries committed so far; whether the session still runs.
            bool PollBeats()
            {
                Assert.That(clock.Elapsed.TotalSeconds, Is.LessThan(HangGuard), $"after {buffers} buffers");
                Plugin.GetStatus(session, out var s);
                Assert.That(s.Failed, Is.Zero, Plugin.LastError());
                int n = Plugin.Poll(session, entries);
                Assert.That(n, Is.GreaterThanOrEqualTo(0));
                for (int i = 0; i < n; i++)
                {
                    if (entries[i].Kind != EntryKind.Beat) continue;
                    Assert.That(entries[i].T, Is.GreaterThan(lastBeat));
                    lastBeat = entries[i].T;
                }
                return s.AnalysisDone == 0;
            }
            try
            {
                // Two seconds of music, each buffer only once the line holds it, as Unity's output would
                // take it (faster than real time, so the analysis may trail meanwhile).
                while (buffers * frames < 2 * rate)
                {
                    PollBeats();
                    Plugin.GetStatus(session, out var s);
                    if (s.State == Plugin.Waiting || s.Buffered > 0.05)
                    {
                        dsp = 100 + (double)buffers * frames / rate;
                        Assert.That(Plugin.RenderMusic(session, buffer, frames, 2, dsp), Is.EqualTo(Plugin.Ok));
                        Plugin.GetStatus(session, out s);
                        foreach (float x in buffer) loudest = Math.Max(loudest, Math.Abs(x));
                        if (s.State == Plugin.Playing) buffers++;
                        else System.Threading.Thread.Sleep(1); // still filling the line
                    }
                    else System.Threading.Thread.Sleep(1);
                }
                // Unity's audio thread renders through the binding, and the main thread polls it every
                // frame: neither may allocate.
                while (true)
                {
                    PollBeats();
                    Plugin.GetStatus(session, out var s);
                    if (s.Buffered > 0.05) break;
                    System.Threading.Thread.Sleep(1);
                }
                dsp += (double)frames / rate;
                Assert.That(() => { Plugin.RenderMusic(session, buffer, frames, 2, dsp); }, Is.Not.AllocatingGCMemory());
                buffers++;
                int polled = 0;
                Assert.That(() =>
                {
                    Plugin.GetStatus(session, out _);
                    polled = Plugin.Poll(session, entries);
                }, Is.Not.AllocatingGCMemory());
                for (int i = 0; i < polled; i++)
                    if (entries[i].Kind == EntryKind.Beat) lastBeat = entries[i].T;
                Plugin.GetStatus(session, out var end);
                Assert.That(end.Stalls, Is.Zero);
                Assert.That(loudest, Is.GreaterThan(0.1), "the music was silent");
                // The anchor is the latest buffer's: its DSP time, and song time by frames rendered.
                Assert.That(end.AnchorDsp, Is.EqualTo(dsp));
                Assert.That(end.AnchorSong, Is.EqualTo((double)end.AnchorFrames / rate));
                Assert.That(end.AnchorFrames, Is.EqualTo((long)(buffers - 1) * frames));
                // The analysis runs up to M past the music, so beats ahead of it get committed.
                while (lastBeat <= end.AnchorSong && PollBeats()) System.Threading.Thread.Sleep(1);
                Assert.That(lastBeat, Is.GreaterThan(end.AnchorSong), "no beat was committed ahead of the music");
            }
            finally
            {
                Plugin.Close(session);
            }
        }

        [Test]
        public void DrivenMusicIsRenderedByItsCallerAndAnalysedUpToMPastIt()
        {
            BuiltPlugin();
            const double song = 2, block = 4096 / 44100.0; // the analyzer reads whole blocks
            var host = new GameObject("Music", typeof(AudioSource));
            var music = host.AddComponent<MusicPlayer>();
            var clock = Stopwatch.StartNew();
            try
            {
                music.Play(MusicSource.File(Path.GetFullPath(Song)), driven: true);
                Assert.That(music.Failed, Is.False, music.Error);
                // Nothing else renders it: the analysis reaches M past where this thread rendered to, and no further.
                while (true)
                {
                    Assert.That(clock.Elapsed.TotalSeconds, Is.LessThan(HangGuard), $"analysed {music.Status.Analysed:F3} s");
                    music.RenderTo(song);
                    music.Poll();
                    Assert.That(music.Failed, Is.False, music.Error);
                    Assert.That(music.Status.Analysed, Is.LessThanOrEqualTo(song + MusicPlayer.M + 0.001));
                    if (music.Status.Analysed > song + MusicPlayer.M - block) break;
                    System.Threading.Thread.Sleep(1);
                }
                Assert.That(music.Status.State, Is.EqualTo(Plugin.Playing));
                // The last buffer rendered ends at the song time asked for.
                double rate = AudioSettings.outputSampleRate;
                Assert.That(music.Status.AnchorSong, Is.LessThan(song).And.GreaterThanOrEqualTo(song - 1024 / rate));
                Assert.That(music.Status.Stalls, Is.Zero);
            }
            finally
            {
                music.Close(); // (edit mode calls no OnDisable)
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void AMissingFileFailsToOpenWithTheReason()
        {
            BuiltPlugin();
            var host = new GameObject("Music", typeof(AudioSource));
            try
            {
                var music = host.AddComponent<MusicPlayer>();
                music.Play(MusicSource.File(Path.GetFullPath("plugin/fixtures/no-such-song.ogg")));
                Assert.That(music.Failed && !music.Open);
                Assert.That(music.Error, Does.StartWith("ps_open -2"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void SongTimeAndDspTimeMapThroughTheAnchor()
        {
            var clock = new MusicClock();
            clock.SetAnchor(dsp: 10.0, song: 3.5);
            Assert.That(clock.SongAtDsp(12.25), Is.EqualTo(5.75).Within(1e-12));
            Assert.That(clock.DspAtSong(clock.SongAtDsp(17.0)), Is.EqualTo(17.0).Within(1e-12));
        }

        [Test]
        public void TheDspFitSmoothsBufferStepsAndHeardSongSubtractsTheLatency()
        {
            const double rate = 48000, buffer = 256 / rate;
            var clock = new MusicClock { OutputLatency = 0.038, DisplayLatency = 1 / 60.0 };
            clock.SetAnchor(dsp: 5.0, song: 0.0);
            // 3 s of frames at 60 fps; the DSP clock advances in whole buffers, 5 s ahead of real time.
            double real = 0;
            for (int i = 0; i < 180; i++)
            {
                real = i / 60.0;
                double dsp = 5.0 + Math.Floor(real / buffer) * buffer;
                clock.Sample(real, dsp);
            }
            // The steps average half a buffer behind the continuous clock.
            Assert.That(clock.DspAt(real), Is.EqualTo(5.0 + real - buffer / 2).Within(buffer / 2));
            Assert.That(clock.HeardSong(real), Is.EqualTo(clock.SongAtDsp(clock.DspAt(real)) - 0.038).Within(1e-12));
            Assert.That(clock.FrameSong(real), Is.EqualTo(clock.HeardSong(real + 1 / 60.0)).Within(1e-12));
            clock.InputCalibration = 0.02;
            Assert.That(clock.KeySong(real), Is.EqualTo(clock.HeardSong(real) - 0.02).Within(1e-12));
        }

        [Test]
        public void FrameSongNeverRunsBackwards()
        {
            var clock = new MusicClock { DisplayLatency = 0.016 };
            clock.SetAnchor(dsp: 0, song: 0);
            for (int i = 0; i < 20; i++) clock.Sample(i / 60.0, i / 60.0);
            double before = clock.FrameSong(19 / 60.0);
            clock.SetAnchor(dsp: 0, song: -0.5); // a later anchor half a second behind
            Assert.That(clock.FrameSong(19 / 60.0), Is.EqualTo(before));
            Assert.That(clock.HeldFrames, Is.EqualTo(1));
        }

        [Test]
        public void TheFrontierRidesTheMixerByTheMarginAndLeadsTheHeardSongByLatencyPlusMargin()
        {
            const double buffer = 256 / 48000.0;
            var clock = new MusicClock { OutputLatency = 0.038, DisplayLatency = 1 / Fps };
            clock.SetAnchor(dsp: 5.0, song: 0.0);
            for (int i = 0; i <= 3 * Fps; i++) clock.Sample(i / Fps, 5.0 + i / Fps);
            var times = new MusicTimes(clock, frame: 1 / Fps, dspBuffer: buffer, dspNow: () => 8.0);
            var t = times.Frame(new ClockReadings { Real = 3.0 });

            Assert.That(t.Song, Is.EqualTo(3.0).Within(1e-9)); // songAtDsp(dspAt(now))
            Assert.That(t.Heard, Is.EqualTo(3.0 - 0.038).Within(1e-9));
            Assert.That(times.Margin, Is.EqualTo(2 / Fps + Simulation.TickSeconds + 2 * buffer).Within(1e-12));
            Assert.That(t.Frontier, Is.EqualTo(t.Song + times.Margin).Within(1e-12));
            Assert.That(t.Lead, Is.EqualTo(0.038 + times.Margin).Within(1e-9));
            Assert.That(t.FrameSong, Is.EqualTo(clock.HeardSong(3.0 + 1 / Fps)).Within(1e-12));

            // Frames longer than a refresh widen the margin to two of them.
            for (int i = 0; i < 200; i++) times.Measure(1 / 30.0);
            Assert.That(times.Margin, Is.EqualTo(2 / 30.0 + Simulation.TickSeconds + 2 * buffer).Within(1e-6));
            times.Measure(2.0); // a hitch is not a frame rate
            Assert.That(times.Margin, Is.EqualTo(2 / 30.0 + Simulation.TickSeconds + 2 * buffer).Within(1e-6));
        }

        [Test]
        public void TheFrameClockIsFrameExact()
        {
            var clock = new FrameClock();
            for (int n = 0; n < 600; n += 37)
            {
                var t = clock.Frame(new ClockReadings { Real = 123.4 + n, Frame = n / Fps });
                Assert.That(t.FrameSong, Is.EqualTo(n / Fps + FrameClock.Reached));
                Assert.That(t.Frontier, Is.EqualTo(FrameClock.FrontierAt(n / Fps)));
                Assert.That(t.Lead, Is.EqualTo(FrameClock.OutputLatency + FrameClock.Margin).Within(1e-12));
                Assert.That(clock.KeySong(999), Is.EqualTo(n / Fps));
            }
        }

        [Test]
        public void AFixedTimeRunKnowsEachBeatBeforeItIsHeardHoweverTheEntriesArrive()
        {
            var entries = House128();
            const int frameCount = 15 * 60;
            var justInTime = FixedRun(entries, f => 0, frameCount, out var beats);
            var random = new System.Random(3);
            var bursts = FixedRun(entries, f => random.Next(4) == 0 ? random.Next(200) : 0, frameCount, out _);
            var early = FixedRun(entries, f => f == 0 ? entries.Count : 0, frameCount, out _);

            Assert.That(bursts, Is.EqualTo(justInTime));
            Assert.That(early, Is.EqualTo(justInTime));
            // 128 BPM over 15 s: each beat known before the speakers play it, the analyzer's batches a
            // second apart arriving 0.3 to 1.2 s ahead (the first ones at the sim's start).
            Assert.That(beats.Count, Is.GreaterThan(20));
            foreach (var (t, knownAt) in beats)
                Assert.That(t - knownAt, Is.GreaterThan(0.2), $"the beat at {t:F3} s was known at {knownAt:F3} s");
        }

        /// <summary>
        /// Runs house-128's entries on fixed frames, handing them over as the fixed-time loop does: before
        /// each frame, every entry whose stamp the frontier needs, plus `extra(frame)` more. The sim's event
        /// hash after each frame (its state hash also counts the entries accepted so far, which a session
        /// records); `beats` each beat the sim reached and when it knew it.
        /// </summary>
        static List<ulong> FixedRun(List<StreamEntry> entries, Func<int, int> extra, int frameCount, out List<(double t, double knownAt)> beats)
        {
            var driver = new SimDriver(new RunInputs().Build(null), new FrameClock());
            var hashes = new List<ulong>();
            beats = new List<(double, double)>();
            int next = 0, cursor = 0;
            for (int f = 0; f < frameCount; f++)
            {
                var frame = new ClockReadings { Real = f / Fps, Frame = f / Fps };
                double needed = driver.StampNeededFor(FrameClock.FrontierAt(frame.Frame));
                while (next < entries.Count && entries[next].Stamp <= needed) driver.AddSettled(entries[next++]);
                for (int k = extra(f); k > 0 && next < entries.Count; k--) driver.AddSettled(entries[next++]);
                Assert.That(driver.Step(frame));
                foreach (var e in driver.Sim.EventsSince(cursor))
                    if (e.Kind == SimEventKind.Beat) beats.Add((e.T, driver.Sim.Stream.KnownAt(EntryKind.Beat, e.I0)));
                cursor = driver.Sim.EventCount;
                hashes.Add(driver.Sim.EventHash);
            }
            Assert.That(driver.LateEntries, Is.Zero);
            Assert.That(driver.BackwardFrames, Is.Zero);
            return hashes;
        }

        /// <summary>The first 20 s of house-128's entries, as the plugin's dump has them.</summary>
        static List<StreamEntry> House128()
        {
            var entries = new List<StreamEntry>();
            foreach (var line in File.ReadLines(Path.GetFullPath(SongEntries)))
            {
                var row = JsonUtility.FromJson<DumpRow>(line);
                if (row.kind == 0) continue; // the header
                var e = new StreamEntry { Kind = (EntryKind)row.kind, T = row.t, Stamp = row.known_at };
                for (int i = 0; i < row.v.Length; i++) e[i] = row.v[i];
                entries.Add(e);
            }
            return entries;
        }

        [Serializable]
        struct DumpRow
        {
            public int kind;
            public double t, known_at;
            public double[] v;
        }
    }
}
