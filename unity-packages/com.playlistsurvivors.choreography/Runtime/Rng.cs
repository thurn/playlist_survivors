namespace PS.Choreography
{
    /// <summary>A small seeded generator (xorshift64*), so the layer never depends on System.Random's implementation.</summary>
    public struct Rng
    {
        ulong state;

        public Rng(uint seed, uint stream = 0)
        {
            state = Mix(((ulong)seed << 32) ^ stream ^ 0x9E3779B97F4A7C15UL);
            if (state == 0) state = 0x2545F4914F6CDD1DUL;
        }

        public ulong NextULong()
        {
            state ^= state >> 12;
            state ^= state << 25;
            state ^= state >> 27;
            return state * 0x2545F4914F6CDD1DUL;
        }

        public uint NextUInt() => (uint)(NextULong() >> 32);

        /// <summary>Uniform in [0, 1).</summary>
        public float NextFloat() => (NextULong() >> 40) * (1f / (1 << 24));

        public float Range(float lo, float hi) => lo + (hi - lo) * NextFloat();

        /// <summary>Uniform in [0, n).</summary>
        public int Next(int n) => n <= 1 ? 0 : (int)((NextULong() >> 33) % (ulong)n);

        /// <summary>SplitMix64's finalizer.</summary>
        public static ulong Mix(ulong z)
        {
            z += 0x9E3779B97F4A7C15UL;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }

        public static uint Hash(uint a, uint b, uint c = 0) => (uint)(Mix(((ulong)a << 32 | b) ^ ((ulong)c * 0xD6E8FEB86659FD93UL)) >> 32);
    }
}
