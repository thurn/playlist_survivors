using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Game.Client.Music;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools.Constraints;
using Is = NUnit.Framework.Is; // over the Unity constraints' Is, whose AllocatingGCMemory extends it

namespace Game.Tests
{
    /// <summary>The music plugin's C# side: its bindings, the editor's reloadable copy and the client's clocks.</summary>
    public class MusicTests
    {
        const string Song = "../public/reactive/house-128/house-128.ogg";
        const double HangGuard = 120; // seconds: only a hung plugin takes this long

        static string BuiltPlugin()
        {
            string built = Path.GetFullPath(PluginLoader.BuiltPath);
            if (!File.Exists(built)) Assert.Fail($"no plugin at {built}: run `game plugin` (game setup does)");
            return built;
        }

        [Test]
        public void TheBindingsHaveTheHeadersLayout()
        {
            Assert.That(Marshal.SizeOf<Plugin.Entry>(), Is.EqualTo(120));
            Assert.That((int)Marshal.OffsetOf<Plugin.Entry>(nameof(Plugin.Entry.T)), Is.EqualTo(8));
            Assert.That((int)Marshal.OffsetOf<Plugin.Entry>(nameof(Plugin.Entry.V0)), Is.EqualTo(24));
            Assert.That(Marshal.SizeOf<Plugin.Status>(), Is.EqualTo(72));
            Assert.That((int)Marshal.OffsetOf<Plugin.Status>(nameof(Plugin.Status.Stalls)), Is.EqualTo(48));
            Assert.That((int)Marshal.OffsetOf<Plugin.Status>(nameof(Plugin.Status.FollowerGain)), Is.EqualTo(68));
            Assert.That((int)Marshal.OffsetOf<Plugin.Config>(nameof(Plugin.Config.M)), Is.EqualTo(8));
            Assert.That((int)Marshal.OffsetOf<Plugin.Config>(nameof(Plugin.Config.Path)), Is.EqualTo(24));
            Assert.That(Marshal.SizeOf<Plugin.Config>(), Is.EqualTo(48));
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
                // The new copy's functions run: a null config fails as the header says.
                var config = new Plugin.Config { Source = Plugin.SourceFile, M = 3, OutputRate = 48000 };
                Assert.That(Plugin.Open(ref config, out IntPtr session), Is.EqualTo(-1));
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
            const int rate = 48000, frames = 512;
            var config = new Plugin.Config
            {
                Source = Plugin.SourceFile,
                M = MusicPlayer.M,
                OutputRate = rate,
                Path = Plugin.Utf8(Path.GetFullPath(Song)),
                Models = Plugin.Utf8(Path.GetFullPath(Path.Combine(Application.streamingAssetsPath, "ps", "madmom.bin"))),
            };
            IntPtr session;
            try
            {
                Assert.That(Plugin.Open(ref config, out session), Is.EqualTo(Plugin.Ok), Plugin.LastError());
            }
            finally
            {
                Marshal.FreeHGlobal(config.Path);
                Marshal.FreeHGlobal(config.Models);
            }
            var buffer = new float[frames * 2];
            var entries = new Plugin.Entry[256];
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
                    if (entries[i].Kind != Plugin.KindBeat) continue;
                    Assert.That(entries[i].T, Is.GreaterThan(lastBeat));
                    lastBeat = entries[i].T;
                }
                return s.AnalysisDone == 0;
            }
            try
            {
                // Two seconds of music, each buffer only once the line holds it, as Unity's output
                // would take it (faster than real time, so the analysis may trail meanwhile).
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
                // Unity's audio thread renders through the binding, and the main thread polls it
                // every frame: neither may allocate.
                while (true)
                {
                    PollBeats();
                    Plugin.GetStatus(session, out var s);
                    if (s.Buffered > 0.05) break;
                    System.Threading.Thread.Sleep(1);
                }
                dsp += (double)frames / rate;
                Assert.That(() => { Plugin.RenderMusic(session, buffer, frames, 2, dsp); },
                    Is.Not.AllocatingGCMemory());
                buffers++;
                int polled = 0;
                Assert.That(() =>
                {
                    Plugin.GetStatus(session, out _);
                    polled = Plugin.Poll(session, entries);
                }, Is.Not.AllocatingGCMemory());
                for (int i = 0; i < polled; i++)
                    if (entries[i].Kind == Plugin.KindBeat) lastBeat = entries[i].T;
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
        }
    }
}
