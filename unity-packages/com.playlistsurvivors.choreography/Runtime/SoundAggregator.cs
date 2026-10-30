using System;
using System.Collections.Generic;

namespace PS.Choreography
{
    /// <summary>
    /// Sound aggregation (camera-and-sound.md, "Sound"): per beat and per sound category, at most
    /// 3 voices play; their gain grows with log2 of the number of hits, and variants are chosen by
    /// seed. The voices go to the times with the most requests (ties to the earliest). The gain's
    /// curve is the level script's (<see cref="SimTuning.SoundGainBase"/>); the default is the one above.
    /// </summary>
    static class SoundAggregator
    {
        public const int MaxVoices = 3;

        public static float Gain(int requests, float strongest, SimTuning tuning = null)
        {
            tuning ??= SimTuning.Default;
            return (float)Math.Min(tuning.SoundGainMax,
                (tuning.SoundGainBase + tuning.SoundGainPerDoubling * Math.Log(requests, 2)) * (0.7 + 0.3 * strongest));
        }

        public static SoundEvent[] Aggregate(List<SoundRequest> reqs, SongMap map, uint seed, SimTuning tuning = null)
        {
            int n = reqs.Count;
            var bucket = new int[n];
            var idx = new int[n];
            for (int i = 0; i < n; i++)
            {
                bucket[i] = (int)Math.Floor(map.BeatIndexAt(reqs[i].Time) + 1e-6);
                idx[i] = i;
            }
            Array.Sort(idx, (a, b) =>
            {
                int c = bucket[a].CompareTo(bucket[b]);
                if (c != 0) return c;
                c = reqs[a].Category.CompareTo(reqs[b].Category);
                if (c != 0) return c;
                c = reqs[a].Time.CompareTo(reqs[b].Time);
                return c != 0 ? c : reqs[a].Seq.CompareTo(reqs[b].Seq);
            });

            var result = new List<SoundEvent>();
            var times = new List<double>();
            var counts = new List<int>();
            int g = 0;
            while (g < n)
            {
                int h = g;
                int b = bucket[idx[g]], cat = reqs[idx[g]].Category;
                float strongest = 0;
                times.Clear();
                counts.Clear();
                while (h < n && bucket[idx[h]] == b && reqs[idx[h]].Category == cat)
                {
                    SoundRequest r = reqs[idx[h]];
                    strongest = Math.Max(strongest, r.Strength);
                    if (times.Count > 0 && times[times.Count - 1] == r.Time) counts[counts.Count - 1]++;
                    else { times.Add(r.Time); counts.Add(1); }
                    h++;
                }
                int total = h - g;
                float gain = Gain(total, strongest, tuning);
                // Pick up to MaxVoices times by request count, ties to the earliest; play them in time order.
                int voices = Math.Min(MaxVoices, times.Count);
                var chosen = new bool[times.Count];
                for (int v = 0; v < voices; v++)
                {
                    int best = -1;
                    for (int i = 0; i < times.Count; i++)
                        if (!chosen[i] && (best < 0 || counts[i] > counts[best])) best = i;
                    chosen[best] = true;
                }
                // A beat whose requests all share one instant layers up to 3 variants there.
                int layers = times.Count == 1 ? Math.Min(MaxVoices, total) : 1;
                int voice = 0;
                for (int i = 0; i < times.Count; i++)
                {
                    if (!chosen[i]) continue;
                    for (int l = 0; l < layers; l++)
                    {
                        int variant = (int)(Rng.Hash(seed, (uint)b, (uint)(cat * 16 + voice)) % Catalog.Variants);
                        result.Add(new SoundEvent(cat * Catalog.Variants + variant, times[i], gain));
                        voice++;
                    }
                }
                g = h;
            }
            result.Sort((x, y) =>
            {
                int c = x.Time.CompareTo(y.Time);
                if (c != 0) return c;
                c = x.Sound.CompareTo(y.Sound);
                return c != 0 ? c : x.Gain.CompareTo(y.Gain);
            });
            return result.ToArray();
        }
    }
}
