using System.IO;
using UnityEngine;
using UnityEngine.Rendering;
using Stopwatch = System.Diagnostics.Stopwatch;

namespace Game.Client
{
    /// <summary>
    /// The pipeline states the run draws, warmed up at boot so no first sighting compiles a shader mid-run
    /// (unity-game.md, Performance targets; ps-msz.8.9): a build's first launch on the Mac compiled each as it
    /// first drew, 3.5 s for the first frame and up to 666 ms at a first sighting. A traced
    /// <see cref="GraphicsStateCollection"/> per graphics API lives in the build's StreamingAssets/GraphicsStates/
    /// (gitignored, made per machine by `game perf --trace`, whose development player traces with
    /// `--graphics-states-out FILE`: Unity traces only in a development build), and <see cref="WarmUp"/> creates each
    /// state before the first frame. A trace holds no compute, so each system hands its kernels to <see cref="Prepare"/>
    /// as it loads (a cold first dispatch stalled its frame 50 to 160 ms). `--no-warmup` skips both, to measure without.
    /// </summary>
    public static class GraphicsStates
    {
        static bool _warmed, _warmCompute;
        static int _kernels;
        static long _computeTicks;
        static GraphicsStateCollection _trace;
        static string _tracePath;

        /// <summary>This graphics API's traced states in the build, by its name: StreamingAssets/GraphicsStates/Metal.graphicsstate.</summary>
        public static string BuiltPath =>
            Path.Combine(Application.streamingAssetsPath, "GraphicsStates", SystemInfo.graphicsDeviceType + ".graphicsstate");

        /// <summary>Warms up the build's traced states for this graphics API, once a launch; nothing if it has none or no device.</summary>
        public static void WarmUp(LaunchOptions options)
        {
            if (_warmed || options.NoWarmup || SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) return;
            _warmed = true;
            _warmCompute = SystemInfo.supportsComputeShaders;
            string path = BuiltPath;
            if (!File.Exists(path))
            {
                Debug.Log($"GraphicsStates: no traced states in {path}: first sightings compile their shaders as they come");
                return;
            }
            var sw = Stopwatch.StartNew();
            var states = new GraphicsStateCollection(path);
            if (states.graphicsDeviceType != SystemInfo.graphicsDeviceType)
            {
                Debug.LogWarning($"GraphicsStates: {path} is for {states.graphicsDeviceType}, not {SystemInfo.graphicsDeviceType}");
                return;
            }
            states.WarmUp().Complete();
            Debug.Log($"GraphicsStates: warmed {states.variantCount} variants in {states.totalGraphicsStateCount} states from {path} " +
                      $"in {sw.Elapsed.TotalMilliseconds:F0} ms");
        }

        /// <summary>
        /// Prepares a system's compute kernels as it loads (the crowd's, the shatter's, the effects'), when the boot warmed
        /// up: a trace holds no compute, and a cold launch compiled each kernel on its first dispatch, stalling that frame
        /// 50 to 160 ms.
        /// </summary>
        public static void Prepare(ComputeShader shader, params int[] kernels)
        {
            if (!_warmCompute) return;
            long start = Stopwatch.GetTimestamp();
            foreach (int k in kernels) shader.IsSupported(k);
            _computeTicks += Stopwatch.GetTimestamp() - start;
            _kernels += kernels.Length;
        }

        /// <summary>Logs the kernels <see cref="Prepare"/> prepared since the last report.</summary>
        public static void ReportCompute()
        {
            if (_kernels == 0) return;
            Debug.Log($"GraphicsStates: prepared {_kernels} compute kernels in {_computeTicks * 1000.0 / Stopwatch.Frequency:F0} ms");
            _kernels = 0;
            _computeTicks = 0;
        }

        /// <summary>`--graphics-states-out FILE`: traces the states the run draws, added to those already in FILE.</summary>
        public static void BeginTrace(string path)
        {
            if (path == null || _trace != null || SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) return;
            _tracePath = path;
            _trace = File.Exists(path) ? new GraphicsStateCollection(path) : new GraphicsStateCollection();
            if (_trace.BeginTrace()) return;
            Debug.LogWarning($"GraphicsStates: --graphics-states-out {path}: tracing didn't start (it needs a development build)");
            _trace = null;
        }

        /// <summary>Ends the trace and writes it.</summary>
        public static void EndTrace()
        {
            if (_trace == null) return;
            _trace.EndTrace();
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_tracePath)));
            _trace.SaveToFile(_tracePath);
            Debug.Log($"GraphicsStates: traced {_trace.variantCount} variants in {_trace.totalGraphicsStateCount} states into {_tracePath}");
            _trace = null;
        }
    }
}
