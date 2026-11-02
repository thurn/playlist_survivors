using System;
using System.Collections.Generic;
using Demo3D.Runtime.Crowd;
using PS.Choreography;

namespace Demo3D.Runtime.Playback
{
    /// <summary>
    /// The look's death flourish and spawn warp sounds (neon-look.md, The Axes &gt; VFX; ps-wnj.16), under a look's vfx
    /// option as they draw: a short bright pop at each crystal death's break, over its shatter, pitched by the type's size,
    /// and a rising swell for each monster spawning in view, started <see cref="WarpLeadIn"/> early so its attack lands on
    /// the spawn and its peak as the warp's rings meet. From the timeline's breaks and warps (<see cref="LookCrowd.Breaks"/>,
    /// <see cref="LookCrowd.Warps"/>), so the live scheduler and the offline mix play the same list, aggregated as the
    /// shatter's: per beat and sound at most <see cref="ShatterSounds.MaxVoices"/> voices, their gain growing with log2 of
    /// the requests on that beat. The clips are game's (game/Content/sounds.json: Kenney's CC0 drop and plucks, and maximize
    /// swells), from the free samples.
    /// </summary>
    public static class FlourishSounds
    {
        public const string Pop = "death-pop", Warp = "spawn-warp";
        public const int Variants = 3;
        /// <summary>Each sound's level (game's sounds.json gains).</summary>
        public const float PopLevel = 0.4f, WarpLevel = 0.35f;
        /// <summary>The swell's near silence before its attack (the free samples' measured onset, about 0.12 s).</summary>
        public const double WarpLeadIn = 0.12;

        /// <summary>Every sound's name, for the bank.</summary>
        public static string[] Names()
        {
            var names = new string[2 * Variants];
            for (int v = 0; v < Variants; v++)
            {
                names[v] = Pop + "/" + v;
                names[Variants + v] = Warp + "/" + v;
            }
            return names;
        }

        /// <summary>The sounds for <paramref name="breaks"/> and <paramref name="warps"/> (each sorted by time), sorted by time.</summary>
        public static List<SoundScheduler.Cue> For(IReadOnlyList<LookCrowd.BreakEvent> breaks, IReadOnlyList<LookCrowd.WarpEvent> warps, SongMap map)
        {
            var cues = new List<SoundScheduler.Cue>();
            var pops = new List<(double Time, float Pitch)>(breaks.Count);
            foreach (var b in breaks) pops.Add((b.Time, ShatterSounds.Pitch(b.Height)));
            Aggregate(pops, Pop, PopLevel, 0, map, cues);
            var swells = new List<(double Time, float Pitch)>(warps.Count);
            foreach (var w in warps) swells.Add((w.Time, 1f));
            Aggregate(swells, Warp, WarpLevel, WarpLeadIn, map, cues);
            cues.Sort((a, b) => a.Time.CompareTo(b.Time));
            return cues;
        }

        /// <summary>
        /// Per beat, the first <see cref="ShatterSounds.MaxVoices"/> requests take a voice each (an instant's layering
        /// variants), at the beat's gain, as game's live aggregation does.
        /// </summary>
        static void Aggregate(List<(double Time, float Pitch)> requests, string sound, float level, double leadIn, SongMap map, List<SoundScheduler.Cue> cues)
        {
            for (int i = 0, end; i < requests.Count; i = end)
            {
                long bucket = Bucket(map, requests[i].Time);
                for (end = i + 1; end < requests.Count && Bucket(map, requests[end].Time) == bucket; end++) { }
                float gain = ShatterSounds.Gain(end - i) * level;
                int voices = 0;
                for (int k = i; k < end && voices < ShatterSounds.MaxVoices; k++, voices++)
                {
                    int variant = (int)(Hash((uint)bucket, (uint)voices) % Variants);
                    cues.Add(new SoundScheduler.Cue { Time = requests[k].Time - leadIn, Name = sound + "/" + variant, Gain = gain, Pitch = requests[k].Pitch });
                }
            }
        }

        static long Bucket(SongMap map, double t) => map != null ? (long)Math.Floor(map.BeatIndexAt(t) + 1e-6) : (long)Math.Floor(t * 2);

        static uint Hash(uint a, uint b)
        {
            uint h = a * 0x9E3779B1u ^ (b + 0x7F4A7C15u);
            h ^= h >> 16; h *= 0x85EBCA6Bu; h ^= h >> 13; h *= 0xC2B2AE35u; h ^= h >> 16;
            return h;
        }
    }
}
