using System;
using System.Collections.Generic;
using PS.Choreography;
using UnityEngine;

namespace Demo3D.Runtime.Playback
{
    /// <summary>
    /// Sound scheduling (camera-and-sound.md, Sound): each cue is scheduled with PlayScheduled at
    /// its DSP time once it comes within 150 ms, using the timeline's lookahead, on a pool of 32
    /// voices (the oldest is reused). The cues are the timeline's SoundEvents, already aggregated by
    /// the choreography layer (at most 3 voices per beat and category), plus the client's own
    /// (<see cref="ShatterSounds"/>), so the live mix matches the video's offline mix of the same list.
    /// </summary>
    public sealed class SoundScheduler
    {
        public const double Lookahead = 0.15;
        public const int Voices = 32;

        /// <summary>One sound to play: a bank name at a song time, with a gain and a pitch (playback rate).</summary>
        public struct Cue
        {
            public double Time;
            public string Name;
            public float Gain, Pitch;
        }

        readonly Cue[] _cues;
        readonly SongClock _clock;
        readonly AudioClip[] _clips;     // per cue
        readonly AudioSource[] _voices = new AudioSource[Voices];
        readonly double[] _voiceStart = new double[Voices];
        int _next;
        public int Scheduled { get; private set; }

        /// <summary>The timeline's sounds and the client's <paramref name="extra"/> ones, by time (ties keep the timeline's first).</summary>
        public static Cue[] Cues(Timeline tl, IEnumerable<Cue> extra)
        {
            var list = new List<Cue>(tl.Sounds.Length);
            foreach (var s in tl.Sounds)
                list.Add(new Cue { Time = s.Time, Name = s.Sound >= 0 && s.Sound < tl.SoundNames.Length ? tl.SoundNames[s.Sound] : null, Gain = s.Gain, Pitch = 1f });
            if (extra != null) list.AddRange(extra);
            var order = new int[list.Count];
            for (int i = 0; i < order.Length; i++) order[i] = i;
            Array.Sort(order, (a, b) => { int c = list[a].Time.CompareTo(list[b].Time); return c != 0 ? c : a.CompareTo(b); });
            return Array.ConvertAll(order, i => list[i]);
        }

        public SoundScheduler(Cue[] cues, SongClock clock, SoundBank bank, GameObject host, double startAt)
        {
            _cues = cues;
            _clock = clock;
            _clips = new AudioClip[cues.Length];
            var byName = new Dictionary<string, AudioClip>();
            for (int i = 0; i < cues.Length; i++)
            {
                var name = cues[i].Name;
                if (bank == null || name == null) continue;
                if (!byName.TryGetValue(name, out var clip)) byName[name] = clip = bank.Find(name);
                _clips[i] = clip;
            }
            for (int v = 0; v < Voices; v++)
            {
                var src = host.AddComponent<AudioSource>();
                src.playOnAwake = false;
                src.spatialBlend = 0;
                _voices[v] = src;
            }
            int lo = 0, hi = cues.Length;
            while (lo < hi) { int mid = (lo + hi) >> 1; if (cues[mid].Time < startAt) lo = mid + 1; else hi = mid; }
            _next = lo;
        }

        /// <summary>Schedules every sound due within the lookahead of song time t.</summary>
        public void Update(double t)
        {
            while (_next < _cues.Length && _cues[_next].Time <= t + Lookahead)
            {
                int i = _next++;
                var s = _cues[i];
                // Already past on the mixer's clock: a late sound is worse than none.
                if (_clock.DspAt(s.Time) < AudioSettings.dspTime - 0.02) continue;
                var clip = _clips[i];
                if (clip == null) continue;
                int v = OldestVoice();
                var src = _voices[v];
                src.Stop();
                src.clip = clip;
                src.volume = Mathf.Clamp01(s.Gain);
                src.pitch = s.Pitch > 0 ? s.Pitch : 1f;
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
