using System;
using System.Collections.Generic;
using Champions.Client.Loop;
using UnityEngine;

namespace Champions.Client.Sound
{
    /// <summary>The hero's sound categories (design.md, Sound): each is a `name/variant` set in the bank (Content/sounds.json).</summary>
    public enum SoundKind
    {
        Swing, SwingHeavy, Whoosh, Accent, Slam, Hit, HitHeavy, ShatterBody, ShatterGlass, ShatterTinkle, Hurt,
    }

    /// <summary>One sound: a category at a song time, with its level and pitch (playback rate).</summary>
    public struct SoundCue
    {
        public SoundKind Kind;
        public double Song;
        public float Gain, Pitch;
        /// <summary>
        /// A heard-time sound (a press's, whose events the sim stamps in heard time, before the frontier it settles them
        /// at): when the mixer is already past it, it starts as soon as the mixer can, rather than being dropped.
        /// </summary>
        public bool Asap;
    }

    /// <summary>
    /// game/'s sound scheduler, ported by review: every sound is PlayScheduled at the DSP time of its song time
    /// (<see cref="IGameClock.TryDspAt"/>), started early by its clip's onset so the attack lands on that time; when the
    /// mixer can make the attack but not the whole lead-in, the voice starts into the clip. A sound the mixer is already
    /// past is dropped, never started late, unless it is a heard-time sound (<see cref="SoundCue.Asap"/>): those start at
    /// the earliest the mixer allows, at their attack (design.md, Sound: sounds start the moment the sim knows them, in
    /// heard time). 32 voices; a new sound takes a finished voice, else the oldest sounding among the least important
    /// (<see cref="Important"/>: the hero's swings, dashes and hurt are cut off last). A voice's volume is its sound's gain
    /// times <see cref="Bus"/>; the music's source is never touched. A silent scheduler (no music in time with the
    /// frames: fixed time) keeps the same books without playing anything.
    /// </summary>
    public sealed class SoundScheduler
    {
        public const int Voices = 32;

        readonly IGameClock _clock;
        readonly Func<double> _dspNow;
        readonly AudioSource[] _voices = new AudioSource[Voices];
        readonly double[] _start = new double[Voices], _end = new double[Voices];
        readonly float[] _gain = new float[Voices], _level = new float[Voices];
        readonly int[] _generation = new int[Voices];
        readonly SoundKind[] _kind = new SoundKind[Voices];
        readonly int[][] _variants;
        readonly AudioClip[] _clips;
        readonly float[] _onsets, _levels;
        readonly bool _audible;
        readonly int[] _scheduledOf;

        /// <summary>The effects' level: the mix's limit (times the music's follower gain, when one is given).</summary>
        public float Bus { get; set; } = 1f;

        public int Scheduled { get; private set; }
        /// <summary>Sounds the mixer was already past: dropped rather than started late.</summary>
        public int Dropped { get; private set; }
        /// <summary>Sounds cut off while still ringing: their voice went to a later sound.</summary>
        public int Stolen { get; private set; }
        /// <summary>Heard-time sounds the mixer was already past: started as soon as it could (<see cref="SoundCue.Asap"/>).</summary>
        public int Late { get; private set; }

        /// <summary><paramref name="k"/>'s sounds scheduled.</summary>
        public int ScheduledOf(SoundKind k) => _scheduledOf[(int)k];

        /// <summary>Every sound scheduled, oldest first, with the DSP time its attack lands at (its song time on a silent clock).</summary>
        public readonly List<(SoundCue Cue, double Attack)> Log = new List<(SoundCue, double)>();
        /// <summary>Keep <see cref="Log"/> (the tests do).</summary>
        public bool Logging;

        /// <param name="dspNow">The DSP clock's reading (AudioSettings.dspTime; a silent scheduler's, the clock's): which voices have finished.</param>
        /// <param name="audible">False: the voices never play (no music plays in time with the frames).</param>
        public SoundScheduler(SoundBank bank, GameObject host, IGameClock clock, Func<double> dspNow, bool audible = true)
        {
            _clock = clock;
            _dspNow = dspNow;
            _audible = audible;
            _clips = bank != null ? bank.Clips : Array.Empty<AudioClip>();
            _onsets = bank != null ? bank.Onsets : Array.Empty<float>();
            _levels = bank != null ? bank.Levels : Array.Empty<float>();
            var kinds = (SoundKind[])Enum.GetValues(typeof(SoundKind));
            _variants = new int[kinds.Length][];
            _scheduledOf = new int[kinds.Length];
            foreach (var k in kinds)
            {
                var list = new List<int>();
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
        }

        static readonly string[] Names =
            { "swing", "swing-heavy", "whoosh", "accent", "slam", "hit", "hit-heavy", "shatter-body", "shatter-glass", "shatter-tinkle", "hurt" };

        /// <summary>A category's name in the bank and sounds.json.</summary>
        public static string Name(SoundKind k) => Names[(int)k];

        /// <summary>The sounds whose voices go last: the hero's own moves and its hurt.</summary>
        public static bool Important(SoundKind k) =>
            k == SoundKind.Swing || k == SoundKind.SwingHeavy || k == SoundKind.Whoosh || k == SoundKind.Accent || k == SoundKind.Slam
            || k == SoundKind.Hurt;

        /// <summary>Whether the bank has clips for <paramref name="k"/>.</summary>
        public bool Has(SoundKind k) => _variants[(int)k].Length > 0;

        /// <summary>
        /// Schedules <paramref name="cue"/> as its category's <paramref name="variant"/>th clip (wrapping) at gain
        /// <paramref name="gain"/> times the clip's level: the voice's handle, or -1 when it was dropped (late, or no clip).
        /// </summary>
        public int Play(in SoundCue cue, int variant, float gain)
        {
            var set = _variants[(int)cue.Kind];
            if (set.Length == 0) return -1;
            int c = set[variant % set.Length];
            var clip = _clips[c];
            if (clip == null) return -1;
            float pitch = cue.Pitch > 0 ? cue.Pitch : 1f;
            // The attack lands on the cue's song time: the voice starts its onset earlier, or as early as the mixer
            // allows, skipping that much of the lead-in.
            double lead = _onsets[c] / pitch;
            double earliest = _clock.EarliestDsp;
            if (!_clock.TryDspAt(cue.Song, out double attack))
            {
                if (!cue.Asap || !_clock.Ready)
                {
                    Dropped++;
                    return -1;
                }
                attack = earliest; // a heard-time sound the mixer is past: its attack as soon as it can be
                Late++;
            }
            double dsp = attack - lead, skip = 0;
            if (dsp < earliest)
            {
                skip = (earliest - dsp) * pitch; // clip seconds
                dsp = earliest;
            }
            int v = FreeVoice();
            if (_end[v] > _dspNow()) Stolen++;
            _level[v] = _levels[c];
            _gain[v] = gain;
            if (_audible)
            {
                var src = _voices[v];
                src.Stop();
                src.clip = clip;
                src.pitch = pitch;
                src.volume = Volume(v);
                src.time = (float)skip;
                src.PlayScheduled(dsp);
            }
            _kind[v] = cue.Kind;
            _start[v] = dsp;
            _end[v] = dsp + (clip.length - skip) / pitch;
            _generation[v]++;
            Scheduled++;
            _scheduledOf[(int)cue.Kind]++;
            if (Logging) Log.Add((cue, attack));
            return v | (_generation[v] << 8);
        }

        /// <summary>Sets a scheduled sound's gain (before its clip's level), if its voice is still its own.</summary>
        public void SetGain(int handle, float gain)
        {
            if (handle < 0) return;
            int v = handle & 0xFF;
            if (_generation[v] != handle >> 8) return;
            _gain[v] = gain;
            if (_audible) _voices[v].volume = Volume(v);
        }

        /// <summary>Applies the bus to the sounding voices (once a frame).</summary>
        public void Update()
        {
            if (!_audible) return;
            double now = _dspNow();
            for (int v = 0; v < Voices; v++)
                if (_end[v] > now) _voices[v].volume = Volume(v);
        }

        float Volume(int v) => Mathf.Clamp01(_gain[v] * _level[v] * Bus);

        /// <summary>A finished voice, else the oldest sounding one that isn't <see cref="Important"/>, else the oldest.</summary>
        int FreeVoice()
        {
            double now = _dspNow();
            int oldest = -1, oldestAny = 0;
            for (int v = 0; v < Voices; v++)
            {
                if (_end[v] <= now) return v;
                if (_start[v] < _start[oldestAny]) oldestAny = v;
                if (!Important(_kind[v]) && (oldest < 0 || _start[v] < _start[oldest])) oldest = v;
            }
            return oldest >= 0 ? oldest : oldestAny;
        }
    }
}
