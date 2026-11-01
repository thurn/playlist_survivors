using System;
using Game.Client.Loop;
using UnityEngine;

namespace Game.Client.Sound
{
    /// <summary>The game's sound categories: each is a `name/variant` set in the bank (game/Content/sounds.json).</summary>
    public enum SoundKind
    {
        Swing, SwingHeavy, Slam, Hit, HitHeavy, Death, ShatterBody, ShatterGlass, ShatterTinkle, Hurt, Chime,
        GarlicPulse, GarlicHit, FireTrailPulse, FireTrailHit,
        WandFire, WandHit, BibleFire, BibleHit,
        LightningStrike, LightningJump, LightningHit,
        ChestOpen, ChestTick, ChestLand, Evolve,
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
        /// <summary>
        /// A heard-time sound (a free press's, whose events the sim stamps in heard time, before the frontier it
        /// settles them at): when the mixer is already past it, it starts as soon as the mixer can, rather than dropped.
        /// </summary>
        public bool Asap;
    }

    /// <summary>
    /// demo3d's sound scheduler on a voice pool (unity-game.md, Sound Timing and Audio), fed by sim
    /// events instead of a timeline: every sound is PlayScheduled at the DSP time of its song time
    /// (<see cref="IGameClock.TryDspAt"/>), started early by its clip's onset so the attack lands on
    /// that time, and dropped, never started late, when the mixer can't make the attack; when it
    /// can make the attack but not the whole lead-in, the voice starts into the clip, at the
    /// earliest it can, so the attack still lands on time. A heard-time sound (<see cref="SoundCue.Asap"/>, a
    /// free press's) is never dropped for being late: it starts at the earliest the mixer allows, at its attack
    /// (its lead-in skipped), a DSP buffer past the clock's reading. 64 voices; a new sound takes a finished
    /// voice, else the one that started first among the least important sounding (<see cref="Important"/>:
    /// the hero's swing, slam and hurt, the chests' and the evolutions' are cut off last). The voices' volume is each
    /// sound's gain times <see cref="Bus"/>, the effects' level (the plugin's follower gain under the
    /// mix's limit); the music's source is never touched. A voice is addressed by a handle that a
    /// later sound taking it invalidates. A silent scheduler (no music in time with the frames:
    /// fixed time) keeps the same books without playing anything.
    /// </summary>
    public sealed class SoundScheduler
    {
        public const int Voices = 64;
        public const int LogCapacity = 1 << 15;

        public struct Entry
        {
            public SoundKind Kind;
            public double Song;
            public double Dsp; // the voice's start; NaN: dropped
            public float Gain; // the cue's own, before its beat's aggregation and the bus
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
        readonly bool _audible;
        readonly SoundKind[] _kind = new SoundKind[Voices];
        readonly int[] _scheduledOf, _droppedOf, _stolenOf;

        /// <summary>The effects' level: the follower gain times the limit, 1 while nothing sets it.</summary>
        public float Bus { get; set; } = 1f;

        public int Scheduled { get; private set; }
        /// <summary>Sounds the mixer was already past: dropped rather than started late.</summary>
        public int Dropped { get; private set; }
        /// <summary>Sounds cut off while still ringing: their voice went to a later sound.</summary>
        public int Stolen { get; private set; }
        /// <summary>Sounds started into their clip: the mixer made the attack but not all of the lead-in before it.</summary>
        public int Trimmed { get; private set; }
        /// <summary>Heard-time sounds the mixer was already past: started late, as soon as it could (<see cref="SoundCue.Asap"/>).</summary>
        public int Late { get; private set; }

        /// <summary><paramref name="k"/>'s sounds scheduled.</summary>
        public int ScheduledOf(SoundKind k) => _scheduledOf[(int)k];
        /// <summary><paramref name="k"/>'s sounds dropped (<see cref="Dropped"/>).</summary>
        public int DroppedOf(SoundKind k) => _droppedOf[(int)k];
        /// <summary><paramref name="k"/>'s sounds cut off (<see cref="Stolen"/>).</summary>
        public int StolenOf(SoundKind k) => _stolenOf[(int)k];

        /// <summary>
        /// The sync probe (`--sound-probe`): every sound plays this clip instead, its click
        /// <see cref="ProbeLeadIn"/> into it, at full level whatever the bus and at its own rate whatever
        /// the pitch, so the mix capture can find each one's onset (a resampled click ends in a one-sample
        /// pop, which the onset search takes for a click). Every scheduled or dropped sound is then kept in
        /// <see cref="Log"/>.
        /// </summary>
        public AudioClip Probe { get; set; }

        /// <summary>
        /// The probe's silence before its click, as long as the longest real clip's lead-in (the
        /// evolution's 94 ms): the probe starts early by it as a clip with an onset does, or into it when
        /// the mixer can't make all of it, so the mix capture finds both on the music (ps-msz.8.13).
        /// </summary>
        public const float ProbeLeadIn = 0.1f;

        public int LogCount { get; private set; }
        public ArraySegment<Entry> Log => new ArraySegment<Entry>(_log, 0, LogCount);

        /// <param name="dspNow">The DSP clock's reading (AudioSettings.dspTime; a silent scheduler's, the clock's): which voices have finished.</param>
        /// <param name="audible">False: the voices never play (no music plays in time with the frames).</param>
        public SoundScheduler(SoundBank bank, GameObject host, IGameClock clock, Func<double> dspNow, bool log = false, bool audible = true)
        {
            _clock = clock;
            _dspNow = dspNow;
            _audible = audible;
            _clips = bank != null ? bank.Clips : Array.Empty<AudioClip>();
            _onsets = bank != null ? bank.Onsets : Array.Empty<float>();
            _levels = bank != null ? bank.Levels : Array.Empty<float>();
            _roots = bank != null ? bank.RootNotes : Array.Empty<float>();
            var kinds = (SoundKind[])Enum.GetValues(typeof(SoundKind));
            _variants = new int[kinds.Length][];
            _scheduledOf = new int[kinds.Length];
            _droppedOf = new int[kinds.Length];
            _stolenOf = new int[kinds.Length];
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
            { "swing", "swing-heavy", "slam", "hit", "hit-heavy", "death", "shatter-body", "shatter-glass", "shatter-tinkle", "hurt", "chime",
              "garlic-pulse", "garlic-hit", "fire-trail-pulse", "fire-trail-hit", "wand-fire", "wand-hit", "bible-fire", "bible-hit",
              "lightning-strike", "lightning-jump", "lightning-hit", "chest-open", "chest-tick", "chest-land", "evolve" };

        /// <summary>A category's name in the bank and sounds.json.</summary>
        public static string Name(SoundKind k) => Names[(int)k];

        /// <summary>The sounds whose voices go last: the hero's own (swings, the slam, the hurt), the chests' and the evolutions'.</summary>
        public static bool Important(SoundKind k) =>
            k == SoundKind.Swing || k == SoundKind.SwingHeavy || k == SoundKind.Slam || k == SoundKind.Hurt
            || k == SoundKind.ChestOpen || k == SoundKind.ChestTick || k == SoundKind.ChestLand || k == SoundKind.Evolve;

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
            if (Probe != null) { clip = Probe; onset = ProbeLeadIn; level = 1; root = 0; }
            else if (set.Length > 0)
            {
                int i = set[variant % set.Length];
                clip = _clips[i];
                onset = _onsets[i];
                level = _levels[i];
                root = _roots[i];
            }
            else return -1;
            float pitch = Probe != null ? 1f
                : cue.Note > 0 && root > 0 ? Mathf.Pow(2f, (cue.Note - root) / 12f) : cue.Pitch > 0 ? cue.Pitch : 1f;
            // The attack lands on the cue's song time: the voice starts its onset earlier, or as early
            // as the mixer allows, skipping that much of the lead-in.
            double lead = onset / pitch;
            double earliest = _clock.EarliestDsp;
            bool late = false;
            if (clip == null || !_clock.TryDspAt(cue.Song, out double attack))
            {
                if (clip == null || !cue.Asap || !_clock.Ready)
                {
                    Dropped++;
                    _droppedOf[(int)cue.Kind]++;
                    Keep(cue, double.NaN);
                    return -1;
                }
                // A heard-time sound the mixer is past: its attack as soon as it can be.
                attack = earliest;
                late = true;
                Late++;
            }
            double dsp = attack - lead, skip = 0;
            if (dsp < earliest)
            {
                skip = (earliest - dsp) * pitch; // clip seconds
                dsp = earliest;
                if (!late) Trimmed++;
            }
            int v = FreeVoice();
            if (_end[v] > _dspNow())
            {
                Stolen++;
                _stolenOf[(int)_kind[v]]++;
            }
            var src = _voices[v];
            _level[v] = level;
            _gain[v] = gain;
            if (_audible)
            {
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
            Keep(cue, attack);
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

        float Volume(int v) => Probe != null ? 1f : Mathf.Clamp01(_gain[v] * _level[v] * Bus);

        /// <summary>
        /// A finished voice, else the oldest sounding one that isn't <see cref="Important"/>, else
        /// (every voice important) the oldest.
        /// </summary>
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

        void Keep(in SoundCue cue, double dsp)
        {
            if (LogCount < _log.Length) _log[LogCount++] = new Entry { Kind = cue.Kind, Song = cue.Song, Dsp = dsp, Gain = cue.Gain };
        }
    }
}
