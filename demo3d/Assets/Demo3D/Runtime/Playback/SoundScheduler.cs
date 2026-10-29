using System;
using PS.Choreography;
using UnityEngine;

namespace Demo3D.Runtime.Playback
{
    /// <summary>
    /// Sound scheduling (camera-and-sound.md, Sound): each SoundEvent is scheduled with
    /// PlayScheduled at its DSP time once it comes within 150 ms, using the timeline's lookahead,
    /// on a pool of 32 voices (the oldest is reused). The events are already aggregated by the
    /// choreography layer (at most 3 voices per beat and category), so the live mix matches the
    /// video's offline mix of the same list.
    /// </summary>
    public sealed class SoundScheduler
    {
        public const double Lookahead = 0.15;
        public const int Voices = 32;
        readonly Timeline _tl;
        readonly SongClock _clock;
        readonly AudioClip[] _clips;     // per timeline sound index
        readonly AudioSource[] _voices = new AudioSource[Voices];
        readonly double[] _voiceStart = new double[Voices];
        int _next;
        public int Scheduled { get; private set; }

        public SoundScheduler(Timeline tl, SongClock clock, SoundBank bank, GameObject host, double startAt)
        {
            _tl = tl;
            _clock = clock;
            _clips = new AudioClip[tl.SoundNames.Length];
            if (bank != null)
                for (int i = 0; i < _clips.Length; i++) _clips[i] = bank.Find(tl.SoundNames[i]);
            for (int v = 0; v < Voices; v++)
            {
                var src = host.AddComponent<AudioSource>();
                src.playOnAwake = false;
                src.spatialBlend = 0;
                _voices[v] = src;
            }
            _next = tl.FirstSoundAt(startAt);
        }

        /// <summary>Schedules every sound due within the lookahead of song time t.</summary>
        public void Update(double t)
        {
            while (_next < _tl.Sounds.Length && _tl.Sounds[_next].Time <= t + Lookahead)
            {
                var s = _tl.Sounds[_next++];
                // Already past on the mixer's clock: a late sound is worse than none.
                if (_clock.DspAt(s.Time) < AudioSettings.dspTime - 0.02) continue;
                var clip = s.Sound >= 0 && s.Sound < _clips.Length ? _clips[s.Sound] : null;
                if (clip == null) continue;
                int v = OldestVoice();
                var src = _voices[v];
                src.Stop();
                src.clip = clip;
                src.volume = Mathf.Clamp01(s.Gain);
                src.PlayScheduled(_clock.DspAt(s.Time));
                _voiceStart[v] = s.Time;
                Scheduled++;
            }
        }

        int OldestVoice()
        {
            int best = _next % Voices;
            for (int v = 0; v < Voices; v++)
            {
                if (!_voices[v].isPlaying) return v;
                if (_voiceStart[v] < _voiceStart[best]) best = v;
            }
            return best;
        }
    }
}
