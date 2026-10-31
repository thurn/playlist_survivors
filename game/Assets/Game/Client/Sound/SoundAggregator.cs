using System;

namespace Game.Client.Sound
{
    /// <summary>
    /// demo3d's sound aggregation (PS.Choreography's SoundAggregator, tech-demo-3d/camera-and-sound.md),
    /// made streaming for live events: per beat and category at most <see cref="MaxVoices"/> voices,
    /// their gain growing with log2 of the sounds asked for in that beat and category, and with the
    /// strongest of them. The demo chose a whole song's voices at once, by the busiest times; live,
    /// the first three times asked for take the voices (requests at one instant layer variants, as
    /// the demo's did), and later requests raise the gain of the voices already scheduled, which
    /// a scheduled AudioSource takes until it ends. Requests arrive about a lead ahead of their
    /// time, and the sim settles one instant's events together, so a beat's busiest instant is
    /// almost always among its first.
    /// </summary>
    public sealed class SoundAggregator
    {
        public const int MaxVoices = 3, Groups = 64;
        public const float GainBase = 0.35f, GainPerDoubling = 0.15f, GainMax = 1f;

        struct Group
        {
            public long Bucket;
            public SoundKind Kind;
            public int Requests, Voices;
            public float Strongest;
            public int H0, H1, H2;
            public float G0, G1, G2;
        }

        readonly SoundScheduler _scheduler;
        readonly Group[] _groups = new Group[Groups];
        int _next;

        /// <summary>Requests that found their beat's voices taken (they only raised its gain).</summary>
        public int Folded { get; private set; }

        public SoundAggregator(SoundScheduler scheduler)
        {
            _scheduler = scheduler;
            for (int i = 0; i < Groups; i++) _groups[i].Bucket = long.MinValue;
        }

        /// <summary>A beat's gain for <paramref name="requests"/> sounds, the strongest at <paramref name="strongest"/> (0 to 1).</summary>
        public static float Gain(int requests, float strongest) =>
            Math.Min(GainMax, (GainBase + GainPerDoubling * (float)Math.Log(Math.Max(1, requests), 2)) * (0.7f + 0.3f * strongest));

        /// <summary>Asks for <paramref name="cue"/>, in beat <paramref name="bucket"/>, with strength 0 to 1.</summary>
        public void Request(in SoundCue cue, long bucket, float strength)
        {
            ref var g = ref Find(bucket, cue.Kind);
            g.Requests++;
            g.Strongest = Math.Max(g.Strongest, strength);
            float beat = Gain(g.Requests, g.Strongest);
            if (g.Voices < MaxVoices)
            {
                // Variants by beat and voice, so a layered instant plays different clips.
                int variant = (int)(Hash((uint)bucket, (uint)((int)cue.Kind * 16 + g.Voices)) % 3);
                int h = _scheduler.Play(cue, variant, cue.Gain * beat);
                // A dropped sound (too late, or no clip) leaves its voice to a later request.
                if (h >= 0)
                {
                    switch (g.Voices)
                    {
                        case 0: g.H0 = h; g.G0 = cue.Gain; break;
                        case 1: g.H1 = h; g.G1 = cue.Gain; break;
                        default: g.H2 = h; g.G2 = cue.Gain; break;
                    }
                    g.Voices++;
                }
            }
            else Folded++;
            // Every voice of the beat follows its growing gain.
            if (g.Voices > 0) _scheduler.SetGain(g.H0, g.G0 * beat);
            if (g.Voices > 1) _scheduler.SetGain(g.H1, g.G1 * beat);
            if (g.Voices > 2) _scheduler.SetGain(g.H2, g.G2 * beat);
        }

        ref Group Find(long bucket, SoundKind kind)
        {
            for (int i = 0; i < Groups; i++)
                if (_groups[i].Bucket == bucket && _groups[i].Kind == kind) return ref _groups[i];
            // A ring of recent groups: requests come in time order, a few beats ahead at most.
            ref var g = ref _groups[_next];
            _next = (_next + 1) % Groups;
            g = new Group { Bucket = bucket, Kind = kind, H0 = -1, H1 = -1, H2 = -1 };
            return ref g;
        }

        static uint Hash(uint a, uint b)
        {
            uint h = a * 0x9E3779B1u ^ (b + 0x7F4A7C15u);
            h ^= h >> 16; h *= 0x85EBCA6Bu; h ^= h >> 13; h *= 0xC2B2AE35u; h ^= h >> 16;
            return h;
        }
    }
}
