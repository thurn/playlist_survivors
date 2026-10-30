using System;
using System.Collections.Generic;
using Demo3D.Runtime.Crowd;
using PS.Choreography;
using UnityEngine;

namespace Demo3D.Runtime.Playback
{
    /// <summary>
    /// The crystal shatter's sounds (camera-and-sound.md, Sound; epic ps-2iq): every crystal death's
    /// release beat gets a layered glass break, a crunching ice break under a bright glass spray, and
    /// a quieter tinkle as the shards land. They come from the timeline's releases
    /// (<see cref="CrystalShatter.Releases"/>), so the live scheduler and the offline mix play the same
    /// list, aggregated as the choreography aggregates its own sounds: per beat and layer at most
    /// <see cref="MaxVoices"/> voices, their gain growing with log2 of the deaths on that beat. The
    /// voices go to the beat's largest distinct types first, each pitched by its type's size (small
    /// monsters ring higher) with a little variance by seed, so a crowded beat doesn't sound like one
    /// sample.
    /// </summary>
    public static class ShatterSounds
    {
        public const int MaxVoices = 3, Variants = 3;
        /// <summary>The layers, each with <see cref="Variants"/> clips in the sound bank (`layer/variant`), and each one's level.</summary>
        public static readonly string[] Layers = { "shatter-body", "shatter-glass", "shatter-tinkle" };
        static readonly float[] Level = { 0.55f, 0.45f, 0.22f };
        /// <summary>When the shards land, after the release, for a 1.8 m monster: their flight under 2.2 g (CrystalShatter).</summary>
        public const float LandSeconds = 0.38f;

        /// <summary>Every layer's sound names, for the bank.</summary>
        public static string[] Names()
        {
            var names = new string[Layers.Length * Variants];
            for (int l = 0; l < Layers.Length; l++)
                for (int v = 0; v < Variants; v++) names[l * Variants + v] = Layers[l] + "/" + v;
            return names;
        }

        /// <summary>A beat's gain for <paramref name="deaths"/> releases, as SoundAggregator's at full strength.</summary>
        public static float Gain(int deaths) => (float)Math.Min(1.0, 0.35 + 0.15 * Math.Log(Math.Max(1, deaths), 2));

        /// <summary>A type's pitch (playback rate): (1.8 m / its height)^0.3, within 0.8 to 1.3.</summary>
        public static float Pitch(float heightM) => Mathf.Clamp(Mathf.Pow(1.8f / Mathf.Max(0.1f, heightM), 0.3f), 0.8f, 1.3f);

        /// <summary>The sounds for <paramref name="releases"/> (sorted by time), sorted by time.</summary>
        public static List<SoundScheduler.Cue> For(IReadOnlyList<CrystalShatter.ReleaseEvent> releases, SongMap map)
        {
            var cues = new List<SoundScheduler.Cue>();
            var beat = new List<CrystalShatter.ReleaseEvent>();
            var chosen = new List<CrystalShatter.ReleaseEvent>();
            int i = 0;
            while (i < releases.Count)
            {
                long bucket = Bucket(map, releases[i].Time);
                beat.Clear();
                while (i < releases.Count && Bucket(map, releases[i].Time) == bucket) beat.Add(releases[i++]);
                // The largest distinct types first, then the rest of the beat's deaths by size.
                beat.Sort((a, b) => { int c = b.Height.CompareTo(a.Height); return c != 0 ? c : a.Entity.CompareTo(b.Entity); });
                chosen.Clear();
                foreach (var r in beat)
                    if (chosen.Count < MaxVoices && !chosen.Exists(c => c.Type == r.Type)) chosen.Add(r);
                foreach (var r in beat)
                    if (chosen.Count < MaxVoices && !chosen.Contains(r)) chosen.Add(r);
                float gain = Gain(beat.Count);
                for (int v = 0; v < chosen.Count; v++)
                {
                    var r = chosen[v];
                    float pitch = Pitch(r.Height) * (1f + 0.08f * (Rand(r.Seed, 1) - 0.5f));
                    for (int l = 0; l < Layers.Length; l++)
                    {
                        int variant = (int)(Hash((uint)bucket, (uint)(l * 16 + v)) % Variants);
                        double at = r.Time;
                        if (l == 2) at += LandSeconds * Mathf.Sqrt(r.Height / 1.8f) * (0.85f + 0.3f * Rand(r.Seed, 2));
                        cues.Add(new SoundScheduler.Cue
                        {
                            Time = at, Name = Layers[l] + "/" + variant, Gain = gain * Level[l],
                            Pitch = l == 2 ? pitch * (1f + 0.1f * (Rand(r.Seed, 3) - 0.5f)) : pitch,
                        });
                    }
                }
            }
            cues.Sort((a, b) => a.Time.CompareTo(b.Time));
            return cues;
        }

        static long Bucket(SongMap map, double t) => map != null ? (long)Math.Floor(map.BeatIndexAt(t) + 1e-6) : (long)Math.Floor(t * 2);

        static uint Hash(uint a, uint b)
        {
            uint h = a * 0x9E3779B1u ^ (b + 0x7F4A7C15u);
            h ^= h >> 16; h *= 0x85EBCA6Bu; h ^= h >> 13; h *= 0xC2B2AE35u; h ^= h >> 16;
            return h;
        }

        static float Rand(uint seed, uint salt) => (Hash(seed, salt) >> 8) * (1f / 16777216f);
    }
}
