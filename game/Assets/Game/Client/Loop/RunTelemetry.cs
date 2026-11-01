using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Game.Client.Crowd;
using Game.Client.Effects;
using Game.Client.Music;
using Game.Client.Sound;
using Game.Client.Structures;
using PS.Game.Sim;

namespace Game.Client.Loop
{
    /// <summary>
    /// What a run reports besides its session: the summary <see cref="GameLoop"/> logs when it ends (the lead,
    /// frameSong's holds, the sim's timings and what the run did), and the evidence files its flags ask for
    /// (<see cref="LaunchOptions.OutputFiles"/>): `--loop-out`'s frames, `--events-out`'s events, `--structures-out`'s
    /// shards, `--shatter-out`'s monster shards, and `--sound-out`'s sounds and frames for `game spike --sounds`.
    /// </summary>
    sealed class RunTelemetry : IDisposable
    {
        const int LeadCapacity = 1 << 17;

        readonly SimDriver _driver;
        readonly StructureShatter _shatter;
        readonly MonsterShatter _monsters;
        readonly Counts _counts;
        readonly double[] _leads = new double[LeadCapacity];
        readonly double[] _steps = new double[LeadCapacity];
        readonly List<SyncSpike.FrameRow> _frames = new List<SyncSpike.FrameRow>();
        StreamWriter _out, _structuresOut, _monstersOut;

        /// <summary>The entries the loop fed the sim and the key changes it drained into it.</summary>
        public int Entries, KeyChanges;

        public RunTelemetry(SimDriver driver, LaunchOptions.OutputFiles outputs, StructureShatter shatter, MonsterShatter monsters)
        {
            _driver = driver;
            _shatter = shatter;
            _monsters = monsters;
            _counts = new Counts(driver);
            driver.Events.Subscribe(_counts);
            if (outputs.Events != null) _counts.Open(outputs.Events);
            if (outputs.Loop != null)
            {
                _out = new StreamWriter(outputs.Loop);
                _out.WriteLine("frame,frame_song,frontier,lead,tick,hash,events,hero_x,hero_y,alive");
            }
            if (outputs.Structures != null && shatter != null)
            {
                _structuresOut = new StreamWriter(outputs.Structures);
                _structuresOut.WriteLine("frame,frame_song,breaks,shards,flying,moved_m,steps,physics_ms,shadow_ms");
            }
            if (outputs.Shatter != null && monsters != null)
            {
                _monstersOut = new StreamWriter(outputs.Shatter);
                _monstersOut.WriteLine("frame,frame_song,alive,breaks,live_deaths,live_shards");
            }
        }

        /// <summary>A stepped frame: its lead and step time, and its rows.</summary>
        public void Keep()
        {
            var driver = _driver;
            int frame = driver.Frames - 1;
            if (frame < LeadCapacity)
            {
                _leads[frame] = driver.Times.Lead;
                _steps[frame] = driver.StepMs;
            }
            _structuresOut?.WriteLine(string.Format(CultureInfo.InvariantCulture, "{0},{1:R},{2},{3},{4},{5:F4},{6},{7:F4},{8:F4}",
                frame, driver.Times.FrameSong, _shatter.Breaks, _shatter.Shards, _shatter.Flying, _shatter.MovedM,
                _shatter.FrameSteps, _shatter.FramePhysicsMs, _shatter.FrameShadowMs));
            _monstersOut?.WriteLine(string.Format(CultureInfo.InvariantCulture, "{0},{1:R},{2},{3},{4},{5}",
                frame, driver.Times.FrameSong, driver.Sim.State.EnemiesAlive, _monsters.Breaks, _monsters.LiveDeaths, _monsters.LiveShards));
            if (_out == null) return;
            var t = driver.Times;
            var hero = driver.Presented.State.Hero;
            hero.PositionAt(driver.Presented.Song, out double x, out double y);
            _out.WriteLine(string.Format(CultureInfo.InvariantCulture, "{0},{1:R},{2:R},{3:R},{4},{5:X16},{6},{7:R},{8:R},{9}",
                frame, t.FrameSong, t.Frontier, t.Lead, driver.Sim.State.Tick, driver.Sim.Hash, driver.Sim.EventCount, x, y,
                driver.Sim.State.EnemiesAlive));
        }

        /// <summary>A frame for `--sound-out`, once the music is anchored: its mix tick, frameSong and display latency.</summary>
        public void KeepFrame(MusicPlayer music) => _frames.Add(new SyncSpike.FrameRow
        {
            tick = MixCapture.Now,
            frameSong = _driver.Times.FrameSong,
            display = music.Clock.DisplayLatency,
        });

        /// <summary>`--sound-out`'s report into <paramref name="dir"/>: Unity's mix, the sounds scheduled, the buffers and the frames.</summary>
        public void KeepSounds(string dir, MusicPlayer music, MixCapture capture, SoundScheduler sounds, string error)
        {
            var log = sounds.Log;
            var songs = new double[log.Count];
            var dsps = new double[log.Count];
            var kinds = new string[log.Count];
            for (int i = 0; i < log.Count; i++)
            {
                songs[i] = log.Array[i].Song;
                dsps[i] = log.Array[i].Dsp;
                kinds[i] = SoundScheduler.Name(log.Array[i].Kind);
            }
            SyncSpike.Write(dir, music, capture, songs, dsps, kinds, _frames.ToArray(), error);
        }

        /// <summary>The run's summary line; the music, its times, the effects and the sounds may be null.</summary>
        public string Summary(MusicPlayer music, MusicTimes musicTimes, KeyInput keys, DebugCommands commands, EffectsPresenter effects,
            SoundScheduler sounds)
        {
            var driver = _driver;
            int n = Math.Min(driver.Frames, LeadCapacity);
            var c = CultureInfo.InvariantCulture;
            var run = driver.Sim.State;
            var s = string.Format(c, "{0} frames to song {1:F3} s ({8:F1} s since launch), {2} entries ({3} late), {4} key changes ({5} dropped), " +
                "{6} events, hash {7:X16}", driver.Frames, driver.Times.FrameSong, Entries, driver.LateEntries, KeyChanges, keys.Dropped,
                driver.Sim.EventCount, driver.Sim.Hash, UnityEngine.Time.realtimeSinceStartupAsDouble);
            s += string.Format(c, "; run {0} from song {1:F3} s, run clock {2:F3} s, {3} freezes ({4} music stopped), {5} run controls",
                run.Phase, run.StartedAt, run.RunClock, _counts.Freezes, _counts.MusicStops, driver.Sim.Controls.Length);
            // Always given, 0 too, so the tools can read it (spike.py's exceptions()).
            s += string.Format(c, "; presenter faults {0}", driver.Events.Faults);
            if (driver.Events.Faults > 0) s += " (" + driver.Events.FaultReport() + ")";
            if (n == 0) return s;
            // The first second settles the DSP fit and the frame interval; the lead is measured after it.
            int skip = Math.Min(n - 1, Boot.FixedFps);
            var leads = new double[n - skip];
            Array.Copy(_leads, skip, leads, 0, leads.Length);
            Array.Sort(leads);
            var steps = new double[n];
            Array.Copy(_steps, steps, n);
            Array.Sort(steps);
            s += string.Format(c, "; lead (ms) median {0:F1}, p5..p95 {1:F1}..{2:F1}, min..max {3:F1}..{4:F1}",
                1000 * Pct(leads, 0.5), 1000 * Pct(leads, 0.05), 1000 * Pct(leads, 0.95), 1000 * leads[0], 1000 * leads[leads.Length - 1]);
            if (musicTimes != null)
                s += string.Format(c, " (output latency {0:F1} + margin {1:F1})", 1000 * music.Clock.OutputLatency, 1000 * musicTimes.Margin);
            s += string.Format(c, "; frameSong backwards {0}, held {1} frames (largest {2:F2} ms); frames at the frontier {3}",
                driver.BackwardFrames, music != null ? music.Clock.HeldFrames : 0,
                music != null ? 1000 * music.Clock.LargestHold : 0, driver.FramesAtFrontier);
            s += string.Format(c, "; sim step (ms) median {0:F3}, p99 {1:F3}, max {2:F3}",
                Pct(steps, 0.5), Pct(steps, 0.99), steps[n - 1]);
            s += string.Format(c, "; enemies alive {0}, {1} killed, {2} heavy strikes, {3} structures broken, {4} regrown, {5} zone changes, " +
                                  "{6} set pieces, {7} debug commands", driver.Sim.State.EnemiesAlive, _counts.Kills, _counts.HeavyStrikes, _counts.Breaks,
                _counts.Regrowths, _counts.ZoneChanges, _counts.SetPieces, commands.Given);
            if (effects != null)
                s += string.Format(c, "; effects {0} spawned ({1} rejected)", effects.Spawned, effects.System.RejectedTotal);
            if (sounds != null)
            {
                s += string.Format(c, "; sounds {0} scheduled ({1} into their clip), {2} dropped, {3} cut off (by category, scheduled/dropped/cut off:",
                    sounds.Scheduled, sounds.Trimmed, sounds.Dropped, sounds.Stolen);
                foreach (SoundKind k in Enum.GetValues(typeof(SoundKind)))
                    if (sounds.ScheduledOf(k) + sounds.DroppedOf(k) > 0)
                        s += string.Format(c, " {0} {1}/{2}/{3}", SoundScheduler.Name(k), sounds.ScheduledOf(k), sounds.DroppedOf(k), sounds.StolenOf(k));
                s += ")";
            }
            if (music != null && music.Source.IsApp)
                s += string.Format(c, "; app {0}: state {1}, muted {2}, stalls {3}, releases {4}", music.Source.Tag,
                    music.Status.State, music.Status.Muted, music.Status.Stalls, music.Status.Releases);
            return s;
        }

        static double Pct(double[] sorted, double p) => sorted[(int)Math.Round(p * (sorted.Length - 1))];

        /// <summary>Closes the files; the summary and the sounds' report still read what was kept.</summary>
        public void Dispose()
        {
            _out?.Dispose();
            _out = null;
            _structuresOut?.Dispose();
            _structuresOut = null;
            _monstersOut?.Dispose();
            _monstersOut = null;
            _counts.Dispose();
        }

        /// <summary>
        /// What the run did, for its summary, and each event as CSV when asked: its slots, and the frameSong of the
        /// frame it settled in, which a moment it brings must not precede for its frame to show it on time.
        /// </summary>
        sealed class Counts : IPresenter, IDisposable
        {
            public int Kills, HeavyStrikes, Breaks, Regrowths, ZoneChanges, SetPieces, Freezes, MusicStops;
            readonly SimDriver _driver;
            StreamWriter _out;

            public Counts(SimDriver driver) => _driver = driver;

            public void Open(string path)
            {
                _out = new StreamWriter(path);
                _out.WriteLine("kind,t,i0,i1,i2,d0,d1,d2,d3,d4,d5,frame_song");
            }

            public void Dispose()
            {
                _out?.Dispose();
                _out = null;
            }

            public void OnEvent(in SimEvent e)
            {
                _out?.WriteLine(string.Format(CultureInfo.InvariantCulture, "{0},{1:R},{2},{3},{4},{5:R},{6:R},{7:R},{8:R},{9:R},{10:R},{11:R}",
                    e.Kind, e.T, e.I0, e.I1, e.I2, e.D0, e.D1, e.D2, e.D3, e.D4, e.D5, _driver.Times.FrameSong));
                switch (e.Kind)
                {
                    case SimEventKind.Death when e.AsDeath.Enemy >= 0: Kills++; break;
                    case SimEventKind.Attack when e.AsAttack.IsSword && e.AsAttack.Heavy: HeavyStrikes++; break;
                    case SimEventKind.StructureBreak: Breaks++; break;
                    case SimEventKind.Regrowth: Regrowths++; break;
                    case SimEventKind.ZoneChange: ZoneChanges++; break;
                    case SimEventKind.SetPiece: SetPieces++; break;
                    case SimEventKind.Freeze when e.AsFreeze.Starts:
                        Freezes++;
                        if (e.AsFreeze.Reason == FreezeReason.MusicStopped) MusicStops++;
                        break;
                }
            }

            public void Present(in PresentFrame frame) { }
        }
    }
}
