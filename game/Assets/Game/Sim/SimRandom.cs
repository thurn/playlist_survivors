namespace PS.Game.Sim
{
    /// <summary>
    /// The run's one random generator (xoshiro256**, seeded through SplitMix64). Every random choice
    /// in the sim draws from it in simulation order, never from time or hash ordering, so a seed and
    /// a stream always give the same run.
    /// </summary>
    public sealed class SimRandom
    {
        ulong s0, s1, s2, s3;

        public SimRandom(ulong seed)
        {
            ulong x = seed;
            s0 = SplitMix(ref x);
            s1 = SplitMix(ref x);
            s2 = SplitMix(ref x);
            s3 = SplitMix(ref x);
        }

        static ulong SplitMix(ref ulong x)
        {
            ulong z = x += 0x9E3779B97F4A7C15UL;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }

        static ulong Rotl(ulong x, int k) => (x << k) | (x >> (64 - k));

        public ulong NextULong()
        {
            ulong result = Rotl(s1 * 5, 7) * 9;
            ulong t = s1 << 17;
            s2 ^= s0;
            s3 ^= s1;
            s1 ^= s2;
            s0 ^= s3;
            s2 ^= t;
            s3 = Rotl(s3, 45);
            return result;
        }

        /// <summary>A double in [0, 1), from the top 53 bits.</summary>
        public double NextDouble() => (NextULong() >> 11) * (1.0 / (1UL << 53));

        /// <summary>A double in [min, max).</summary>
        public double Range(double min, double max) => min + (max - min) * NextDouble();

        /// <summary>An integer in [0, n), unbiased (Lemire's method with rejection).</summary>
        public int Below(int n)
        {
            if (n <= 0) throw new System.ArgumentOutOfRangeException(nameof(n));
            uint bound = (uint)n;
            uint threshold = (uint)(0x100000000UL % bound);
            while (true)
            {
                ulong m = (NextULong() >> 32) * bound;
                if ((uint)m >= threshold) return (int)(m >> 32);
            }
        }
    }
}
