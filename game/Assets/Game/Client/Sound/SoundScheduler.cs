using System;
using Game.Client.Loop;
using UnityEngine;

namespace Game.Client.Sound
{
    /// <summary>The game's sound categories: each is a `name/variant` set in the bank (game/Content/sounds.json).</summary>
    public enum SoundKind
    {
        Swing, SwingHeavy, Slam, Hit, HitHeavy, Death, ShatterBody, ShatterGlass, ShatterTinkle, Hurt, Chime,
    }

    /// <summary>
    /// One sound: a category at a song time, with its level and pitch (playback rate), or a note
    /// (MIDI) that its clip's root note turns into one.
    /// </summary>
    public struct SoundCue
    {
        public SoundKind Kind;
        public double Song;
        public float Gain, Pitch, Note;
    }

    /// <summary>
    /// demo3d's sound scheduler on a voice pool (unity-game.md, Sound Timing and Audio), fed by sim
    /// events instead of a timeline: every sound is PlayScheduled at the DSP time of its song time
    /// (<see cref="IGameClock.TryDspAt"/>), started early by its clip's onset so the attack lands on
    /// that time, and dropped, never started late, when the mixer can't make it. 32 voices; a new
    /// sound takes a finished voice, else the one that started first. The voices' volume is each
    /// sound's gain times <see cref="Bus"/>, the effects' level (the plugin's follower gain under the
    /// mix's limit); the music's source is never touched. A voice is addressed by a handle that a
    /// later sound taking it invalidates.
    /// </summary>
    public sealed class SoundScheduler
    {
        public const int Voices = 32;
        public const int LogCapacity = 1 << 15;

        public struct Entry
        {
            public double Song;
            public double Dsp; // the voice's start; NaN: dropped
        }

        readonly IGameClock _clock;
        readonly Func<double> _dspNow;
        readonly AudioSource[] _voices = new AudioSource[Voices];
        readonly double[] _start = new double[Voices], _end = new double[Voices];
        readonly float[] _gain = new float[Voices], _level = new float[Voices];
        readonly int[] _generation = new int[Voices];
        readonly int[][] _variants;
        readonly AudioClip[] _clips;
        readonly float[] _onsets, _levels, _roots;
        readonly Entry[] _log;

        /// <summary>The effects' level: the follower gain times the limit, 1 while nothing sets it.</summary>
        public float Bus { get; set; } = 1f;

        public int Scheduled { get; private set; }
        /// <summary>Sounds the mixer was already past: dropped rather than started late.</summary>
        public int Dropped { get; private set; }

        /// <summary>
        /// The sync probe (`--sound-probe`): every sound plays this clip instead, from its first
        /// sample, at full level whatever the bus, so the mix capture can find each one's onset.
        /// Every scheduled or dropped sound is then kept in <see cref="Log"/>.
        /// </summary>
        public AudioClip Probe { get; set; }

        public int LogCount { get; private set; }
        public ArraySegment<Entry> Log => new ArraySegment<Entry>(_log, 0, LogCount);

        /// <param name="dspNow">The DSP clock's reading (AudioSettings.dspTime): which voices have finished.</param>
        public SoundScheduler(SoundBank bank, GameObject host, IGameClock clock, Func<double> dspNow, bool log = false)
        {
            _clock = clock;
            _dspNow = dspNow;
            _clips = bank != null ? bank.Clips : Array.Empty<AudioClip>();
            _onsets = bank != null ? bank.Onsets : Array.Empty<float>();
            _levels = bank != null ? bank.Levels : Array.Empty<float>();
            _roots = bank != null ? bank.RootNotes : Array.Empty<float>();
            var kinds = (SoundKind[])Enum.GetValues(typeof(SoundKind));
            _variants = new int[kinds.Length][];
            foreach (var k in kinds)
            {
                var list = new System.Collections.Generic.List<int>();
                for (int v = 0; bank != null; v++)
                {
                    int i = bank.IndexOf(Name(k) + "/" + v);
                    if (i < 0) break;
                    list.Add(i);
                }
                _variants[(int)k] = list.ToArray();
            }
            for (int v = 0; v < Voices; v++)
            {
                var src = host.AddComponent<AudioSource>();
                src.playOnAwake = false;
                src.spatialBlend = 0;
                _voices[v] = src;
                _end[v] = double.NegativeInfinity;
            }
            _log = log ? new Entry[LogCapacity] : Array.Empty<Entry>();
        }

        static readonly string[] Names =
            { "swing", "swing-heavy", "slam", "hit", "hit-heavy", "death", "shatter-body", "shatter-glass", "shatter-tinkle", "hurt", "chime" };

        /// <summary>A category's name in the bank and sounds.json.</summary>
        public static string Name(SoundKind k) => Names[(int)k];

        /// <summary>Whether the bank has clips for <paramref name="k"/>.</summary>
        public bool Has(SoundKind k) => _variants[(int)k].Length > 0 || Probe != null;

        /// <summary>
        /// Schedules <paramref name="cue"/> as its category's <paramref name="variant"/>th clip (wrapping)
        /// at gain <paramref name="gain"/> times the clip's level: the voice's handle, or -1 when it was
        /// dropped (late, or no clip).
        /// </summary>
        public int Play(in SoundCue cue, int variant, float gain)
        {
            var set = _variants[(int)cue.Kind];
            AudioClip clip;
            float onset, level, root;
            if (Probe != null) { clip = Probe; onset = 0; level = 1; root = 0; }
            else if (set.Length > 0)
            {
                int i = set[variant % set.Length];
                clip = _clips[i];
                onset = _onsets[i];
                level = _levels[i];
                root = _roots[i];
            }
            else return -1;
            float pitch = cue.Note > 0 && root > 0 ? Mathf.Pow(2f, (cue.Note - root) / 12f) : cue.Pitch > 0 ? cue.Pitch : 1f;
            // The attack lands on the cue's song time: the voice starts its onset earlier.
            double lead = onset / pitch;
            if (clip == null || !_clock.TryDspAt(cue.Song - lead, out double dsp))
            {
                Dropped++;
                Keep(cue.Song, double.NaN);
                return -1;
            }
            int v = FreeVoice();
            var src = _voices[v];
            src.Stop();
            src.clip = clip;
            src.pitch = pitch;
            _level[v] = level;
            _gain[v] = gain;
            src.volume = Volume(v);
            src.PlayScheduled(dsp);
            _start[v] = dsp;
            _end[v] = dsp + clip.length / pitch;
            _generation[v]++;
            Scheduled++;
            Keep(cue.Song, dsp + lead);
            return v | (_generation[v] << 8);
        }

        /// <summary>Sets a scheduled sound's gain (before its clip's level), if its voice is still its own.</summary>
        public void SetGain(int handle, float gain)
        {
            if (handle < 0) return;
            int v = handle & 0xFF;
            if (_generation[v] != handle >> 8) return;
            _gain[v] = gain;
            _voices[v].volume = Volume(v);
        }

        /// <summary>Applies the bus to the sounding voices (once a frame).</summary>
        public void Update()
        {
            double now = _dspNow();
            for (int v = 0; v < Voices; v++)
                if (_end[v] > now) _voices[v].volume = Volume(v);
        }

        float Volume(int v) => Probe != null ? 1f : Mathf.Clamp01(_gain[v] * _level[v] * Bus);

        int FreeVoice()
        {
            double now = _dspNow();
            int oldest = 0;
            for (int v = 0; v < Voices; v++)
            {
                if (_end[v] <= now) return v;
                if (_start[v] < _start[oldest]) oldest = v;
            }
            return oldest;
        }

        void Keep(double song, double dsp)
        {
            if (LogCount < _log.Length) _log[LogCount++] = new Entry { Song = song, Dsp = dsp };
        }
    }
}
