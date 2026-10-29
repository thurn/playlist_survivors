namespace Demo3D.Runtime.Effects
{
    /// <summary>
    /// Rejected spawns (effects.md, The runtime: budgets are hard, and any rejection fails the
    /// pass rule). The CPU's count (a frame's instance list full) and the GPU's (the pool full,
    /// read back once per frame as that frame's count) are kept apart and summed. A reset (a seek,
    /// a rebuild, a loop wrap) starts a new epoch: readbacks issued before it are ignored, and the
    /// reset frame's own rejections, from re-spawning what is still alive, are excused (counted
    /// apart and logged, not reported to the pass rule).
    /// </summary>
    public sealed class RejectionLedger
    {
        long _reported;

        public int Epoch { get; private set; }
        public long Cpu { get; private set; }
        public long Gpu { get; private set; }
        public long Excused { get; private set; }
        public long ExcusedCpuThisReset { get; private set; }
        public long Total => Cpu + Gpu;

        public void NewEpoch()
        {
            Epoch++;
            ExcusedCpuThisReset = 0;
        }

        public void AddCpu(int spawns, bool excused)
        {
            if (excused) { Excused += spawns; ExcusedCpuThisReset += spawns; }
            else Cpu += spawns;
        }

        /// <summary>One frame's GPU count, from a readback issued in <paramref name="epoch"/>; stale epochs are ignored.</summary>
        public void AddGpu(int epoch, int spawns, bool excused)
        {
            if (epoch != Epoch || spawns <= 0) return;
            if (excused) Excused += spawns;
            else Gpu += spawns;
        }

        /// <summary>Rejections (not excused) since the last call.</summary>
        public int Take()
        {
            int d = (int)(Total - _reported);
            _reported = Total;
            return d;
        }
    }
}
