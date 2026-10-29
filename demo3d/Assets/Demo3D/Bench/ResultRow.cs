using System.Collections.Generic;

namespace Demo3D.Bench
{
    /// <summary>
    /// One run's result row (benchmarks.md, Measurements), written as one JSON line. Counters that
    /// need later tasks' systems (particles, lights, LOD, skinning, the sim) stay null until a
    /// scene reports them.
    /// </summary>
    public sealed class ResultRow
    {
        // What ran.
        public string Sweep;
        public string Scene;
        public string Kind;
        public int Attempt = 1;
        public int N;
        public double ViewWidthM;
        public string Load;
        public string Aa;
        public double Seconds;
        public string Time;

        // Timings (FrameTimingManager), over the run's window, counter frames excluded.
        public double? GpuP99Ms, GpuP50Ms, GpuMeanMs, GpuMaxMs;
        public double? MainP99Ms, MainMaxMs;
        public double? RenderP99Ms, RenderMaxMs;
        public double? MaxFrameMs;
        public int Frames, TimedFrames, GpuZeroFrames, ExcludedFrames;

        // Counters.
        public int? OnScreenAlive, OnScreenDying, OffScreen, ShadowCasters, SkinnedVertices;
        public string VisibleByTypeLod; // a JSON object, or null
        public int? Particles, SpawnsMaxPerFrame, SpawnsRejected, LightCues, LightsUsed;
        public double? SpawnsPerFrame, LightDropShareP99;
        public double? SimTickP99Ms, SimTickMaxMs;
        public int? FlowRebuildSlices;
        public double? OpaqueDepth, TransparentPerPixel, TransparentBeatPeak;
        public int OverdrawFrames, OverdrawBeatFrames;

        // Machine state.
        public string Api, Gpu, Driver, GpuThrottle, PowerPlan;
        public double? GpuTempC;
        public int? MemMHz;
        public int JobWorkers;
        public int BackbufferWidth, BackbufferHeight;

        // Quiet machine.
        public double? OtherCpuPctMax, OtherGpuPctMax;
        public bool FocusLost, Occluded, DisplayOff, Noisy;
        public List<string> NoiseReasons = new List<string>();

        // Verdict.
        public bool Pass;
        public string Worst;
        public double WorstRatio;

        /// <summary>The target's GPU time by the compute ratio: Cygnus's GPU p99 × 2.5 × 1.2.</summary>
        public double? ComputeEstimateMs => GpuP99Ms * 3.0;

        public string ToJson()
        {
            var o = new JsonObject()
                .Add("scene", Scene).Add("n", N).Add("viewWidthM", ViewWidthM, 2).Add("load", Load).Add("aa", Aa)
                .Add("gpuP99Ms", GpuP99Ms).Add("mainP99Ms", MainP99Ms).Add("renderP99Ms", RenderP99Ms)
                .Add("maxFrameMs", MaxFrameMs)
                .Add("onScreenAlive", OnScreenAlive).Add("onScreenDying", OnScreenDying).Add("particles", Particles)
                .Add("spawnsMaxPerFrame", SpawnsMaxPerFrame).Add("spawnsRejected", SpawnsRejected)
                .Add("lightsUsed", LightsUsed).Add("lightDropShareP99", LightDropShareP99, 4)
                .Add("opaqueDepth", OpaqueDepth).Add("transparentPerPixel", TransparentPerPixel)
                .Add("transparentBeatPeak", TransparentBeatPeak)
                .Add("api", Api).Add("driver", Driver).Add("gpuTempC", GpuTempC, 0).Add("gpuThrottle", GpuThrottle)
                .Add("powerPlan", PowerPlan).Add("memMHz", MemMHz).Add("pass", Pass)
                // Beyond the design's example row.
                .Add("sweep", Sweep).Add("kind", Kind).Add("attempt", Attempt).Add("seconds", Seconds, 1)
                .Add("time", Time)
                .Add("gpuP50Ms", GpuP50Ms).Add("gpuMeanMs", GpuMeanMs).Add("gpuMaxMs", GpuMaxMs)
                .Add("mainMaxMs", MainMaxMs).Add("renderMaxMs", RenderMaxMs)
                .Add("frames", Frames).Add("timedFrames", TimedFrames).Add("gpuZeroFrames", GpuZeroFrames)
                .Add("excludedFrames", ExcludedFrames)
                .Add("offScreen", OffScreen).Add("shadowCasters", ShadowCasters).Add("skinnedVertices", SkinnedVertices)
                .AddRaw("visibleByTypeLod", VisibleByTypeLod)
                .Add("spawnsPerFrame", SpawnsPerFrame).Add("lightCues", LightCues)
                .Add("simTickP99Ms", SimTickP99Ms).Add("simTickMaxMs", SimTickMaxMs)
                .Add("flowRebuildSlices", FlowRebuildSlices)
                .Add("overdrawFrames", OverdrawFrames).Add("overdrawBeatFrames", OverdrawBeatFrames)
                .Add("gpu", Gpu).Add("jobWorkers", JobWorkers)
                .Add("backbuffer", BackbufferWidth + "x" + BackbufferHeight)
                .Add("otherCpuPctMax", OtherCpuPctMax, 1).Add("otherGpuPctMax", OtherGpuPctMax, 1)
                .Add("focusLost", FocusLost).Add("occluded", Occluded).Add("displayOff", DisplayOff)
                .Add("noisy", Noisy).Add("noiseReasons", NoiseReasons)
                .Add("worst", Worst).Add("worstRatio", WorstRatio)
                .Add("computeEstimateMs", ComputeEstimateMs, 2);
            return o.ToString();
        }
    }
}
