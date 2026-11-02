using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using Champions.Client.Music;
using Champions.Sim;
using UnityEngine;

namespace Champions.Client.Loop
{
    /// <summary>
    /// The frame loop on the main thread (plan.md, Architecture), as far as the music goes: the plugin's
    /// entries into the sim, the sim stepped to the frontier, its events read. The presenters, the
    /// player's inputs and the run join it in the beads that build them (plan.md, The Client).
    ///
    /// Its music is the source Boot resolved from `--file` or `--app` (<see cref="MusicSource"/>),
    /// played through the music plugin 3 s late (<see cref="MusicPlayer"/>); live, song time is the
    /// music's (<see cref="MusicTimes"/>). Under `--fixed-time` song time is the frame's
    /// (<see cref="FrameClock"/>) and the frames drive a file's music (<see cref="MusicPlayer.Driven"/>):
    /// each renders it, silently, to the frame's mixer position and waits until the plugin's analysis
    /// covers its frontier, so entries are known exactly at stamp + B and a rerun shows the same frames.
    ///
    /// It never pauses for the music (design.md, The Run > Pauses): the music's end, a gap or a released
    /// tap play silence while song time and the sim run on.
    ///
    /// It logs the music as the sim meets it, `Music:` lines: the source and the delay once it plays (how
    /// far the analyzer has read past the speakers, M), each beat the sim reaches with the song time it
    /// became known at and how long before it is heard, a status line every few seconds, and a summary
    /// when it stops.
    /// </summary>
    [DefaultExecutionOrder(-100)] // after the music's poll
    public sealed class GameLoop : MonoBehaviour
    {
        /// <summary>
        /// The plugin's stamp trails its analysis by M - A (ps_plugin.h), and the latest block's entries
        /// may not be delivered yet when its position is reported (4096 samples at 44.1 kHz).
        /// </summary>
        const double StampLag = MusicPlayer.M - 0.048, BlockLag = 0.1;
        const double AnalysisTimeout = 10;
        /// <summary>Song time between status lines.</summary>
        const double StatusEvery = 5;

        MusicPlayer _music;
        MusicTimes _musicTimes;
        bool _fixed, _done, _summarised, _deviceNoted, _playingNoted;
        int _cursor;
        double _openedAt, _nextStatus;
        Action<int, string> _finished;
        readonly List<double> _leads = new List<double>();
        int _beatsLate;
        double _delaySum;
        int _delaySamples;

        public SimDriver Driver { get; private set; }
        public MusicPlayer Music => _music;

        /// <summary>
        /// Starts the loop on `source` on `host`, the music's listener on `listener` (a camera's object),
        /// the output silenced if `mute` (<see cref="MuteOutput"/>). `finished` gets an exit code and why
        /// when the loop can't go on (the music failed).
        /// </summary>
        public static GameLoop Begin(GameObject host, GameObject listener, MusicSource source, bool fixedTime, bool mute,
            Action<int, string> finished)
        {
            var loop = host.AddComponent<GameLoop>();
            loop._finished = finished;
            loop.Setup(listener, source, fixedTime, mute);
            return loop;
        }

        void Setup(GameObject listener, MusicSource source, bool fixedTime, bool mute)
        {
            _fixed = fixedTime;
            if (listener.GetComponent<AudioListener>() == null) listener.AddComponent<AudioListener>();
            if (mute && listener.GetComponent<MuteOutput>() == null) listener.AddComponent<MuteOutput>();
            var musicHost = new GameObject("Music");
            musicHost.AddComponent<AudioSource>();
            _music = musicHost.AddComponent<MusicPlayer>();
            _openedAt = Time.realtimeSinceStartupAsDouble;
            bool driven = _fixed && !source.IsApp;
            _music.Play(source, driven: driven);
            if (_music.Failed)
            {
                Finish(1, _music.Error);
                return;
            }
            IGameClock clock = new FrameClock();
            if (!_fixed)
                clock = _musicTimes = new MusicTimes(_music.Clock, MusicPlayer.Refresh, MusicPlayer.ScheduleHeadroom, () => AudioSettings.dspTime);
            // The content's numbers are the defaults' (beat.json and hero.json repeat them); the run's assembly loads the files.
            Driver = new SimDriver(new RunInputs().Build(null), clock);
            Log("Music: {0} {1}, {2} s late, at {3} Hz{4}; {5}", source.IsApp ? "listening to" : "playing", source.Tag,
                MusicPlayer.M, AudioSettings.outputSampleRate, driven ? ", driven by the frames" : "", DeviceText());
        }

        static string DeviceText()
        {
            try
            {
                var d = Plugin.OutputDevice();
                return d.Uid.Length == 0 ? "no output device" :
                    string.Format(CultureInfo.InvariantCulture, "output {0}, {1:F1} ms{2}", d.Name, 1000 * d.Latency, d.Bluetooth ? " (Bluetooth)" : "");
            }
            catch (InvalidOperationException e)
            {
                return e.Message;
            }
        }

        void Update()
        {
            if (_done || Driver == null) return;
            if (_music.Failed)
            {
                Finish(1, _music.Error);
                return;
            }
            if (_music.DeviceChanged && !_deviceNoted)
            {
                // The latency follows (the clock reads it each poll); the calibration's key is the device.
                _deviceNoted = true;
                Log("Music: the default output device changed: {0}", DeviceText());
            }
            if (_fixed) { if (!FeedSettled()) return; }
            else FeedLive();
            var now = new ClockReadings { Real = Time.realtimeSinceStartupAsDouble, Frame = Time.timeSinceLevelLoadAsDouble };
            if (Driver.Step(now))
            {
                ReadEvents();
                Report();
            }
            _musicTimes?.Measure(Time.unscaledDeltaTime);
        }

        void FeedLive()
        {
            for (int i = 0; i < _music.EntryCount; i++) Driver.Add(_music.Entries[i]);
        }

        /// <summary>
        /// Feeds entries known exactly at stamp + B, polling the plugin until its analysis covers this
        /// frame's frontier (or is done), so the frame never depends on the plugin's timing. The frame's
        /// render position lets the analysis read M past it, past that frontier's stamps; a driven music
        /// that hasn't started yet is rendered as the wait goes. False if the loop finished.
        /// </summary>
        bool FeedSettled()
        {
            double frame = Time.timeSinceLevelLoadAsDouble;
            double needed = Driver.StampNeededFor(FrameClock.FrontierAt(frame));
            double started = Time.realtimeSinceStartupAsDouble;
            while (true)
            {
                _music.RenderTo(FrameClock.SongAt(frame));
                for (int i = 0; i < _music.EntryCount; i++) Driver.AddSettled(_music.Entries[i]);
                if (_music.Failed)
                {
                    Finish(1, _music.Error);
                    return false;
                }
                // A full poll may have left entries behind; the status read before it vouches for the rest.
                bool drained = _music.EntryCount < MusicPlayer.MaxEntries;
                if (drained && (_music.Status.AnalysisDone != 0 || _music.Status.Analysed - BlockLag - StampLag >= needed))
                    return true;
                if (Time.realtimeSinceStartupAsDouble - started > AnalysisTimeout)
                {
                    Finish(1, $"the plugin's analysis stayed at {_music.Status.Analysed:F3} s for {AnalysisTimeout} s");
                    return false;
                }
                if (drained) Thread.Sleep(1);
                _music.Poll();
            }
        }

        /// <summary>Reads the events this frame settled, then frees them.</summary>
        void ReadEvents()
        {
            var sim = Driver.Sim;
            foreach (var e in sim.EventsSince(_cursor))
                if (e.Kind == SimEventKind.Beat) Beat(e);
            _cursor = sim.EventCount;
            sim.DiscardEventsBefore(_cursor);
        }

        /// <summary>A beat the sim reached: when it falls (heard), and when the sim knew it.</summary>
        void Beat(in SimEvent e)
        {
            double knownAt = Driver.Sim.Stream.KnownAt(EntryKind.Beat, e.I0);
            double lead = e.T - knownAt;
            _leads.Add(lead);
            if (lead <= 0) _beatsLate++;
            Log("Music: beat {0} heard at {1:F3} s, known at {2:F3} s, {3:F3} s ahead; reached on the frame heard at {4:F3} s",
                e.I0, e.T, knownAt, lead, Driver.Times.Heard);
        }

        /// <summary>The delay once the music plays, and a status line every <see cref="StatusEvery"/> s.</summary>
        void Report()
        {
            var s = _music.Status;
            var t = Driver.Times;
            if (s.State == Plugin.Playing)
            {
                _delaySum += s.Analysed - t.Song;
                _delaySamples++;
            }
            if (!_playingNoted && s.State == Plugin.Playing)
            {
                _playingNoted = true;
                Log("Music: playing after {0:F2} s", Time.realtimeSinceStartupAsDouble - _openedAt);
            }
            if (t.FrameSong < _nextStatus) return;
            _nextStatus = t.FrameSong + StatusEvery;
            Log("Music: song {0:F2} s, heard {1:F2} s, analysed {2:F2} s ({3:F2} s ahead), {4}, {5} beats known, lead {6:F0} ms, stalls {7}",
                t.Song, t.Heard, s.Analysed, s.Analysed - t.Song, StateName(s.State), Driver.Sim.Stream.Count(EntryKind.Beat),
                1000 * t.Lead, s.Stalls);
        }

        static string StateName(int state) => state switch
        {
            Plugin.Waiting => "waiting",
            Plugin.Playing => "playing",
            Plugin.Ended => "ended",
            Plugin.Stopped => "stopped",
            _ => "state " + state,
        };

        void Finish(int code, string why)
        {
            if (_done) return;
            _done = true;
            if (code != 0) Debug.LogError("GameLoop: " + why);
            _finished?.Invoke(code, why);
        }

        void Summarise()
        {
            if (_summarised || Driver == null) return;
            _summarised = true;
            _leads.Sort();
            string leads = _leads.Count == 0 ? "no beats" : string.Format(CultureInfo.InvariantCulture,
                "{0} beats, known {1:F3} to {2:F3} s (median {3:F3} s) before they were heard, {4} not ahead",
                _leads.Count, _leads[0], _leads[_leads.Count - 1], _leads[_leads.Count / 2], _beatsLate);
            Log("Music: {0}; the speakers {1:F3} s behind the analyzer on average; {2} stalls; {3} late entries; {4} frames, {5} held",
                leads, _delaySamples > 0 ? _delaySum / _delaySamples : double.NaN, _music.Status.Stalls, Driver.LateEntries,
                Driver.Frames, _music.Clock.HeldFrames);
        }

        void OnApplicationQuit() => Summarise();

        void OnDestroy() => Summarise();

        static void Log(string format, params object[] args) => Debug.Log(string.Format(CultureInfo.InvariantCulture, format, args));
    }
}
