using System;
using PS.Game.Sim;
using UnityEngine;

namespace Game.Client.Music
{
    /// <summary>
    /// A click on every committed beat (the sync spike, unity-game.md, Sound Timing): each beat the
    /// plugin commits is scheduled with PlayScheduled at the DSP time of its song time once it comes
    /// within <see cref="Horizon"/>. Beats are committed seconds ahead (the lookahead), so a click
    /// is never late; one whose time is already within <see cref="MusicPlayer.ScheduleHeadroom"/>
    /// of the mixer is dropped, never started late. With a `lead`, each click is instead scheduled
    /// on the latest frame that is at least `lead` ahead of the DSP clock's reading, which measures
    /// how little headroom PlayScheduled needs (the spike's `--click-lead`).
    /// Every scheduled or dropped click is kept in <see cref="Log"/> for the spike's measurement.
    /// </summary>
    public sealed class BeatClicks
    {
        public const double Horizon = 0.25;
        public const int Voices = 8;
        public const int Capacity = 8192;
        public const float Hz = 2000f, Decay = 0.003f, Length = 0.025f, Gain = 0.6f;

        public struct Click
        {
            public double Song;
            public double Dsp; // when it was scheduled to start; NaN: dropped
        }

        readonly AudioSource[] _voices = new AudioSource[Voices];
        readonly double[] _pending = new double[Capacity];
        int _pendingHead, _pendingCount, _next;
        readonly Click[] _log = new Click[Capacity];
        double _lastBeat = double.NegativeInfinity;

        public int Count { get; private set; }
        public int Dropped { get; private set; }
        public ArraySegment<Click> Log => new ArraySegment<Click>(_log, 0, Count);
        public AudioClip Clip { get; }

        readonly double _lead;

        /// <param name="lead">Seconds past the DSP clock's reading to schedule each click at the latest, or negative to schedule it <see cref="Horizon"/> ahead.</param>
        public BeatClicks(GameObject host, double lead = -1)
        {
            _lead = lead;
            Clip = MakeClip(AudioSettings.outputSampleRate);
            for (int i = 0; i < Voices; i++)
            {
                var v = host.AddComponent<AudioSource>();
                v.playOnAwake = false;
                v.spatialBlend = 0;
                v.clip = Clip;
                _voices[i] = v;
            }
        }

        /// <summary>A decaying 2 kHz cosine: full height on its first sample, so its onset is exact.</summary>
        public static AudioClip MakeClip(int rate)
        {
            int n = (int)(Length * rate);
            var data = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / rate;
                data[i] = Gain * Mathf.Exp(-t / Decay) * Mathf.Cos(2f * Mathf.PI * Hz * t);
            }
            var clip = AudioClip.Create("beat-click", n, 1, rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        /// <summary>Queues the committed beats among this frame's entries, then schedules those now due.</summary>
        public void Update(MusicPlayer music)
        {
            for (int i = 0; i < music.EntryCount; i++)
            {
                ref var e = ref music.Entries[i];
                // A beat is committed once; the stamp-every-group stream never repeats or revises one.
                if (e.Kind != EntryKind.Beat || e.T <= _lastBeat || _pendingCount == Capacity) continue;
                _lastBeat = e.T;
                _pending[(_pendingHead + _pendingCount++) % Capacity] = e.T;
            }
            if (!music.Clock.Anchored) return;
            double now = AudioSettings.dspTime;
            double earliest = now + (_lead >= 0 ? _lead : MusicPlayer.ScheduleHeadroom);
            double horizon = _lead >= 0 ? _lead + Time.unscaledDeltaTime : Horizon;
            while (_pendingCount > 0)
            {
                double song = _pending[_pendingHead];
                double dsp = music.Clock.DspAtSong(song);
                if (dsp > now + horizon) break;
                _pendingHead = (_pendingHead + 1) % Capacity;
                _pendingCount--;
                if (dsp < earliest)
                {
                    Dropped++;
                    Keep(song, double.NaN);
                    continue;
                }
                var voice = _voices[_next];
                _next = (_next + 1) % Voices;
                voice.PlayScheduled(dsp);
                Keep(song, dsp);
            }
        }

        void Keep(double song, double dsp)
        {
            if (Count < Capacity) _log[Count++] = new Click { Song = song, Dsp = dsp };
        }
    }
}
