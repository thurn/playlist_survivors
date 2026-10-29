using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using Demo3D.Bench;
using Unity.Jobs.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.Rendering;
using Debug = UnityEngine.Debug;

namespace Demo3D.Runtime.Bench
{
    /// <summary>
    /// The benchmark player (benchmarks.md, Benchmark Harness): <c>--bench &lt;sweep.json&gt; --out
    /// &lt;dir&gt;</c> runs one sweep of one scene in this process and quits.
    ///
    /// It takes the machine lock (unless <c>--lock-held</c>: the `demo3d sweep` orchestrator holds
    /// it), refuses to run on a wrong backbuffer, worker count, API, power plan, a locked desktop, a
    /// display that won't wake, or a warm GPU; waits a bounded time for a quiet machine; then for
    /// each point searches for the largest passing N (FrontierSearch), writing one row per run to
    /// rows.jsonl and one per point to frontier.jsonl. Noisy runs are repeated up to twice.
    ///
    /// <c>--hold n=..,viewWidthM=..,load=..,aa=..</c> (or with colons) instead holds one configuration for the
    /// spec's seconds after its warm-up, writing one row; with <c>--pix-capture &lt;file&gt;</c> and
    /// a PIX capturer loaded (pixtool launch), it triggers a programmatic GPU capture of
    /// <c>--pix-frames</c> frames after the warm-up.
    ///
    /// Exit codes: 0 done, 2 bad arguments, 3 refused (refusal.json says why), 4 failed.
    /// </summary>
    public sealed class BenchRunner : MonoBehaviour
    {
        SweepSpec _spec;
        string _out, _sweepId;
        StreamWriter _log, _rows, _frontierOut;
        MachineLock _lock;
        NoiseSampler _sampler;
        BenchRig _rig;
        IBenchScene _scene;
        BenchContext _ctx;
        OverdrawCounter _overdraw;
        IntPtr _window;
        bool _finished, _focusLost;

        // Machine state at the start.
        string _api, _gpu, _driver, _powerPlan;
        int? _memMHz;
        int _jobWorkers, _bbW, _bbH;

        // Frame timing.
        struct FrameRec
        {
            public ulong Start;
            public double Gpu, Main, Render, Cpu;
        }

        readonly FrameTiming[] _ft = new FrameTiming[64];
        const double MaxGpuZeroShare = 0.005;
        readonly List<FrameRec> _frames = new List<FrameRec>();
        ulong _lastFrameStart;
        double _ftmPerTick = 1; // FrameTimingManager timer ticks per QueryPerformanceCounter tick
        ulong _committed;
        readonly Dictionary<ulong, FrameRec> _pending = new Dictionary<ulong, FrameRec>();
        readonly List<ulong> _keys = new List<ulong>();
        readonly List<long[]> _excluded = new List<long[]>();
        long _prevUpdate;
        bool _closeExclusion;

        // The live scene.
        bool _live;
        double _sceneT0, _lastSceneT;
        bool _measuring, _beatPending;
        int _measureFrame;
        readonly SceneCounters _counters = new SceneCounters();
        readonly Counters _acc = new Counters();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            if (!Args.Has("--bench")) return;
            var go = new GameObject("BenchRunner");
            DontDestroyOnLoad(go);
            go.AddComponent<BenchRunner>();
        }

        void Awake()
        {
            Application.logMessageReceived += OnLog;
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = 10; // idle until the sweep starts
        }

        void OnLog(string message, string stack, LogType type)
        {
            if (_finished || (type != LogType.Exception)) return;
            Log("FAILED: " + message + "\n" + stack);
            Finish(4);
        }

        void OnApplicationFocus(bool focus)
        {
            if (!focus && _measuring) _focusLost = true;
        }

        IEnumerator Start()
        {
            // Arguments, the spec and the output folder.
            var specPath = Args.Get("--bench");
            _out = Args.Get("--out");
            if (specPath == null || _out == null || !File.Exists(specPath))
            {
                Debug.LogError("bench: usage --bench <sweep.json> --out <dir>");
                Finish(2);
                yield break;
            }
            Directory.CreateDirectory(_out);
            _log = new StreamWriter(Path.Combine(_out, "bench.log"), true) { AutoFlush = true };
            _sweepId = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            try
            {
                _spec = SweepSpec.Parse(File.ReadAllText(specPath));
            }
            catch (FormatException e)
            {
                Log("bad sweep spec: " + e.Message);
                Finish(2);
                yield break;
            }
            Log($"sweep {_sweepId}: {specPath} -> {_out}");

            // The machine lock: wait for it, never around it.
            if (!Args.Has("--lock-held"))
            {
                _lock = new MachineLock(MachineLock.DefaultPath());
                float lastNote = -999;
                while (!_lock.TryAcquire($"bench {specPath}"))
                {
                    if (Time.realtimeSinceStartup - lastNote > 30)
                    {
                        Log($"waiting for the machine lock {_lock.Path} ({_lock.Holder()})");
                        lastNote = Time.realtimeSinceStartup;
                    }
                    yield return new WaitForSecondsRealtime(1);
                }
                Log("took the machine lock " + _lock.Path);
            }
            else Log("the orchestrator holds the machine lock");

            _sampler = new NoiseSampler(_spec.Quiet.WindowS);
            yield return null;
            yield return null;

            // Hard refusals.
            _window = Win32.OwnWindow();
            _sampler.Window = _window;
            _bbW = Screen.width;
            _bbH = Screen.height;
            int rw = Display.main.renderingWidth, rh = Display.main.renderingHeight;
            if (_bbW != _spec.ScreenWidth || _bbH != _spec.ScreenHeight || rw != _spec.ScreenWidth || rh != _spec.ScreenHeight)
            {
                Refuse("backbuffer", $"the backbuffer is {_bbW}x{_bbH} (rendering {rw}x{rh}); the sweep needs " +
                                     $"{_spec.ScreenWidth}x{_spec.ScreenHeight} (-screen-width {_spec.ScreenWidth} " +
                                     $"-screen-height {_spec.ScreenHeight} -screen-fullscreen 0)");
                yield break;
            }
            _jobWorkers = JobsUtility.JobWorkerCount;
            if (_jobWorkers != _spec.JobWorkers)
            {
                Refuse("jobWorkers", $"JobsUtility.JobWorkerCount is {_jobWorkers}; the sweep needs {_spec.JobWorkers} " +
                                     $"(-job-worker-count {_spec.JobWorkers})");
                yield break;
            }
            _api = SystemInfo.graphicsDeviceType == GraphicsDeviceType.Direct3D12 ? "d3d12" : SystemInfo.graphicsDeviceType.ToString();
            if (SystemInfo.graphicsDeviceType != GraphicsDeviceType.Direct3D12)
            {
                Refuse("api", $"the graphics API is {SystemInfo.graphicsDeviceType}; sweeps run on Direct3D 12 (-force-d3d12)");
                yield break;
            }
            if (!Win32.ActivePowerScheme(out var scheme, out var schemeName))
            {
                Refuse("powerPlan", "can't read the active power plan");
                yield break;
            }
            _powerPlan = MachineFacts.PowerPlanName(scheme, schemeName);
            if (!MachineFacts.PowerPlanAllowed(scheme, schemeName))
            {
                Refuse("powerPlan", $"the power plan is {schemeName ?? scheme.ToString()} ({scheme}); sweeps need " +
                                    "High performance or Ultimate Performance");
                yield break;
            }
            if (Win32.DesktopLocked())
            {
                Refuse("desktopLocked", "the desktop is locked (or on the secure desktop), which stops composition");
                yield break;
            }

            // Wake the display and hold it on for the sweep's whole life.
            bool watching = Win32.WatchDisplay();
            yield return new WaitForSecondsRealtime(0.3f); // registration reports the current state
            int initialDisplay = Win32.DisplayState;
            Win32.HoldDisplay();
            Win32.NudgeInput();
            float wakeStart = Time.realtimeSinceStartup;
            while (Win32.DisplayState != 1 && Time.realtimeSinceStartup - wakeStart < 15)
            {
                if (Win32.DisplayState == -1 && Time.realtimeSinceStartup - wakeStart > 3) break; // no notification
                yield return new WaitForSecondsRealtime(0.5f);
            }
            Log($"display was {DisplayName(initialDisplay)}, now {DisplayName(Win32.DisplayState)} after " +
                $"{Time.realtimeSinceStartup - wakeStart:F1} s (watching: {watching}); held on for the sweep");
            if (Win32.DisplayState == 0 || Win32.DisplayState == 2)
            {
                Refuse("display", $"the display didn't come back on within 15 s (state {DisplayName(Win32.DisplayState)})");
                yield break;
            }
            float fgStart = Time.realtimeSinceStartup;
            while (true)
            {
                if (_window == IntPtr.Zero) _window = _sampler.Window = Win32.OwnWindow();
                if (Win32.BringToForeground(_window) || Time.realtimeSinceStartup - fgStart > 10) break;
                yield return new WaitForSecondsRealtime(0.5f);
            }
            var fgNow = Win32.GetForegroundWindow();
            if (_window != IntPtr.Zero && fgNow != _window && !_spec.Quiet.RequireForeground)
                Log($"WARNING: the foreground is held by {Win32.ClassName(fgNow)} \"{Win32.Title(fgNow)}\"; " +
                    "quiet.requireForeground is off, so focus loss is recorded, not noise");
            else if (_window == IntPtr.Zero || fgNow != _window)
            {
                Refuse("foreground", "the player's window couldn't take the foreground " +
                                     (_window == IntPtr.Zero ? "(no window found)" :
                                         $"(held by {Win32.ClassName(fgNow)} \"{Win32.Title(fgNow)}\")"));
                yield break;
            }

            // An idle-cool GPU: under the limit with no thermal reason, after a bounded cool-down.
            float coolStart = Time.realtimeSinceStartup;
            while (true)
            {
                var temp = Nvml.TemperatureC();
                var reasons = Nvml.ThrottleReasons() ?? 0;
                if (temp == null)
                {
                    Log("NVML unavailable: GPU temperature unknown");
                    break;
                }
                bool cool = temp.Value < _spec.Quiet.GpuMaxC && (reasons & MachineFacts.ThermalReasons) == 0;
                if (cool)
                {
                    Log($"GPU {temp.Value} °C, throttle {MachineFacts.ThrottleNames(reasons)}");
                    break;
                }
                if (Time.realtimeSinceStartup - coolStart > _spec.Quiet.CoolWaitS)
                {
                    Refuse("gpuTemp", $"the GPU is at {temp.Value} °C (throttle {MachineFacts.ThrottleNames(reasons)}) after " +
                                      $"{_spec.Quiet.CoolWaitS:F0} s; a sweep starts under {_spec.Quiet.GpuMaxC:F0} °C with no thermal reason");
                    yield break;
                }
                yield return new WaitForSecondsRealtime(2);
            }

            // A bounded wait for other processes to quiet down, then run anyway.
            double waited = 0;
            yield return WaitQuiet(_spec.Quiet.MaxWaitS, w => waited = w);

            // Machine state.
            _gpu = Nvml.Name() ?? SystemInfo.graphicsDeviceName;
            _driver = Nvml.DriverVersion() ?? SystemInfo.graphicsDeviceVersion;
            _memMHz = MachineFacts.MemoryMHz(Win32.RawSmbios());
            var latest = _sampler.Latest;
            var machine = new JsonObject()
                .Add("sweep", _sweepId).Add("api", _api).Add("gpu", _gpu).Add("driver", _driver)
                .Add("cpu", SystemInfo.processorType).Add("logicalCores", SystemInfo.processorCount)
                .Add("powerPlan", _powerPlan).Add("powerPlanName", schemeName).Add("memMHz", _memMHz)
                .Add("jobWorkers", _jobWorkers).Add("backbuffer", $"{_bbW}x{_bbH}")
                .Add("display", DisplayName(Win32.DisplayState))
                .Add("gpuTempC", latest?.TempC, 0).Add("gpuThrottle", MachineFacts.ThrottleNames(latest?.Reasons ?? 0))
                .Add("quietWaitS", waited, 0).Add("otherCpuPctAtStart", latest?.OtherCpuPctWindow, 1)
                .Add("requireForeground", _spec.Quiet.RequireForeground)
                .Add("unity", Application.unityVersion)
                .Add("backend", Application.isEditor ? "editor" : IsIl2cpp() ? "il2cpp" : "mono")
                .Add("commandLine", string.Join(" ", Environment.GetCommandLineArgs()));
            File.WriteAllText(Path.Combine(_out, $"machine-{_sweepId}.json"), machine + "\n");
            Log("machine " + machine);

            // The scene, loaded once.
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = -1;
            _rig = BenchRig.Create();
            _ctx = new BenchContext
            {
                Camera = _rig.Camera, Sun = _rig.Sun, Root = _rig.Root, Options = _spec.SceneOptions,
                Assets = BenchAssets.Load(),
            };
            _scene = BenchScenes.Create(_spec.Scene);
            _scene.Load(_ctx);
            _overdraw = new OverdrawCounter(_ctx.Assets.Overdraw, _bbW, _bbH);
            yield return null;
            CheckClocks();

            _rows = new StreamWriter(Path.Combine(_out, "rows.jsonl"), true) { AutoFlush = true };
            _frontierOut = new StreamWriter(Path.Combine(_out, "frontier.jsonl"), true) { AutoFlush = true };

            var hold = Args.Get("--hold");
            if (hold != null) yield return Hold(hold);
            else yield return Sweep();
            Log("sweep done");
            Finish(0);
        }

        IEnumerator Sweep()
        {
            var frontiers = new List<(Point point, int n)>();
            bool firstRun = true;
            foreach (var point in _spec.Points())
            {
                ConfigurePoint(point);
                double perM2 = _scene.PackingPerM2 ?? _spec.PackingPerM2;
                int capacity = CameraGeometry.PackingCapacity(point.ViewWidthM, perM2);
                var deciding = new Dictionary<int, ResultRow>();
                int noisyRuns = 0;
                if (_spec.FixedN != null)
                {
                    foreach (var n in _spec.FixedN)
                    {
                        var req = new RunRequest(n, RunKind.Fixed);
                        ResultRow row = null;
                        yield return RunWithRepeats(req, point, firstRun, (r, rr) => row = r);
                        firstRun = false;
                        if (row.Noisy) noisyRuns++;
                    }
                    continue;
                }

                int? seed = null;
                double best = double.MaxValue;
                foreach (var f in frontiers)
                {
                    double d = f.point.Distance(point);
                    if (d < best) { best = d; seed = f.n; }
                }
                var search = new FrontierSearch(_spec.NLo, _spec.NHi, capacity, _spec.Resolution, seed, _spec.Lines);
                Log($"point {point.Key}: capacity {capacity}, ceiling {search.Ceiling}, seed {seed?.ToString() ?? "none"}");
                var failRows = new SortedDictionary<int, ResultRow>();
                RunRequest next;
                while ((next = search.Next()) != null)
                {
                    ResultRow row = null;
                    RunResult result = null;
                    yield return RunWithRepeats(next, point, firstRun, (r, rr) => { row = r; result = rr; });
                    firstRun = false;
                    if (row.Noisy) noisyRuns++;
                    search.Report(next, result);
                    row.Pass = result.Pass;
                    if (next.Full)
                    {
                        if (result.Pass) deciding[next.N] = row;
                        else failRows[next.N] = row;
                    }
                }
                var res = search.Result;
                ResultRow passRow = res.N.HasValue && deciding.TryGetValue(res.N.Value, out var pr) ? pr : null;
                ResultRow failRow = null;
                foreach (var kv in failRows)
                    if (!res.N.HasValue || kv.Key > res.N.Value) { failRow = kv.Value; break; }
                bool restsOnNoisy = (passRow?.Noisy ?? false) || (failRow?.Noisy ?? false);
                var fo = new JsonObject()
                    .Add("sweep", _sweepId).Add("scene", _spec.Scene).Add("viewWidthM", point.ViewWidthM, 2)
                    .Add("load", point.Load).Add("aa", point.Aa)
                    .Add("n", res.N).Add("limit", res.Limit).Add("capacityBound", res.CapacityBound)
                    .Add("belowRange", res.BelowRange).Add("ceiling", res.Ceiling).Add("capacity", capacity)
                    .Add("visibleGroundM2", CameraGeometry.VisibleGroundM2(point.ViewWidthM), 0)
                    .Add("fitCandidate", res.FitCandidate).Add("fitLimit", res.FitLimit)
                    .Add("fitConfirmed", res.FitConfirmed).Add("seed", seed)
                    .Add("probeRuns", res.ProbeRuns).Add("fullRuns", res.FullRuns).Add("hitRunCap", res.HitRunCap)
                    .Add("noisyRuns", noisyRuns).Add("restsOnNoisy", restsOnNoisy)
                    .Add("gpuP99Ms", passRow?.GpuP99Ms).Add("mainP99Ms", passRow?.MainP99Ms)
                    .Add("renderP99Ms", passRow?.RenderP99Ms).Add("computeEstimateMs", passRow?.ComputeEstimateMs, 2)
                    .Add("simTickP99Ms", passRow?.SimTickP99Ms).Add("failN", failRow?.N)
                    .Add("failWorst", failRow?.Worst);
                _frontierOut.WriteLine(fo.ToString());
                Log($"frontier {point.Key}: {res.N?.ToString() ?? "below range"} ({res.Limit}), " +
                    $"{res.ProbeRuns} probes + {res.FullRuns} full runs, fit {res.FitCandidate} confirmed {res.FitConfirmed}");
                if (res.N.HasValue) frontiers.Add((point, res.N.Value));
            }
        }

        IEnumerator Hold(string spec)
        {
            var kv = new Dictionary<string, string>();
            foreach (var part in spec.Split(','))
            {
                var p = part.Split('=', ':'); // ':' for pixtool, whose --command-line can't hold '='
                if (p.Length == 2) kv[p[0].Trim()] = p[1].Trim();
            }
            var point = new Point
            {
                ViewWidthM = double.Parse(kv.TryGetValue("viewWidthM", out var w) ? w : _spec.ViewWidthM[0].ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture),
                Load = kv.TryGetValue("load", out var l) ? l : _spec.Load[0],
                Aa = kv.TryGetValue("aa", out var a) ? a : _spec.Aa[0],
            };
            int n = int.Parse(kv.TryGetValue("n", out var ns) ? ns : "1000", CultureInfo.InvariantCulture);
            ConfigurePoint(point);
            Rebuild(n);
            yield return WarmFor(_spec.Warmup);
            var pixFile = Args.Get("--pix-capture");
            if (pixFile != null)
            {
                uint frames = (uint)Args.GetInt("--pix-frames", 3);
                var hr = Win32.PixCaptureNextFrames(Path.GetFullPath(pixFile), frames);
                Log(hr == null ? "PIX: no capturer loaded (launch the player with pixtool)"
                               : $"PIX: PIXGpuCaptureNextFrames({pixFile}, {frames}) returned 0x{hr.Value:x8}");
                yield return WarmFor(2);
            }
            ResultRow row = null;
            yield return Measure(new RunRequest(n, RunKind.Fixed), point, 1, _spec.Seconds, r => row = r);
            row.Kind = "hold";
            var rr = ToResult(row);
            PassRule.Evaluate(rr, _spec.Lines);
            row.Pass = rr.Pass;
            row.Worst = rr.Worst;
            row.WorstRatio = rr.WorstRatio;
            _rows.WriteLine(row.ToJson());
        }

        void ConfigurePoint(Point point)
        {
            _rig.SetViewWidth(point.ViewWidthM);
            _rig.SetAntiAliasing(point.Aa);
            _ctx.ViewWidthM = point.ViewWidthM;
            _ctx.Footprint = CameraGeometry.GroundFootprint(point.ViewWidthM);
            _scene.Configure(point);
            _currentN = -1;
        }

        int _currentN = -1;
        SimTickStats _sim;

        void Rebuild(int n)
        {
            var sw = Stopwatch.StartNew();
            _sim = _scene.Rebuild(n);
            _currentN = n;
            _sceneT0 = Time.realtimeSinceStartupAsDouble;
            _lastSceneT = 0;
            _live = true;
            Log($"rebuilt for n {n} in {sw.Elapsed.TotalMilliseconds:F0} ms");
        }

        /// <summary>One requested run, repeated up to the spec's limit while noisy.</summary>
        IEnumerator RunWithRepeats(RunRequest req, Point point, bool first, Action<ResultRow, RunResult> done)
        {
            ResultRow row = null;
            var rows = new List<ResultRow>();
            var results = new List<RunResult>();
            for (int attempt = 1; attempt <= 1 + _spec.Quiet.Repeats; attempt++)
            {
                if (attempt > 1)
                {
                    Log($"run noisy ({string.Join("; ", row.NoiseReasons)}), repeating ({attempt}/{1 + _spec.Quiet.Repeats})");
                    yield return WaitQuiet(_spec.Quiet.RepeatWaitS, null);
                    Win32.BringToForeground(_window);
                }
                bool rebuilt = false;
                if (_currentN != req.N)
                {
                    Rebuild(req.N);
                    rebuilt = true;
                }
                yield return WarmFor(first && attempt == 1 ? _spec.Warmup : rebuilt || attempt > 1 ? _spec.Rewarm : 1);
                double seconds = req.Full ? _spec.Seconds : _spec.ProbeSeconds;
                yield return Measure(req, point, attempt, seconds, r => row = r);
                var result = ToResult(row);
                PassRule.Evaluate(result, _spec.Lines);
                row.Pass = result.Pass;
                row.Worst = result.Worst;
                row.WorstRatio = result.WorstRatio;
                _rows.WriteLine(row.ToJson());
                Log($"{req.KindName} n {req.N} {point.Key}: gpu p99 {row.GpuP99Ms:F2} main {row.MainP99Ms:F2} " +
                    $"render {row.RenderP99Ms:F2} ms -> {(row.Pass ? "pass" : "fail")} ({row.Worst} {row.WorstRatio:F2})" +
                    (row.Noisy ? " NOISY" : ""));
                rows.Add(row);
                results.Add(result);
                if (!row.Noisy)
                {
                    done(row, result);
                    yield break;
                }
                if (attempt == 1 + _spec.Quiet.Repeats)
                {
                    int used = PassRule.MedianAttempt(results);
                    if (rows.Count > 1)
                        Log($"every attempt was noisy; the search uses attempt {used + 1}, the median of {rows.Count}");
                    done(rows[used], results[used]);
                    yield break;
                }
            }
        }

        static RunResult ToResult(ResultRow row) => new RunResult
        {
            N = row.N,
            GpuP99 = row.GpuP99Ms ?? double.NaN,
            MainP99 = row.MainP99Ms ?? double.NaN,
            RenderP99 = row.RenderP99Ms ?? double.NaN,
            GpuMax = row.GpuMaxMs ?? double.NaN,
            MainMax = row.MainMaxMs ?? double.NaN,
            RenderMax = row.RenderMaxMs ?? double.NaN,
            SpawnsRejected = row.SpawnsRejected,
            LightDropWithinLimit = row.LightDropShareP99.HasValue ? row.LightDropShareP99.Value <= Effects.EffectLights.DropLimit : (bool?)null,
            Noisy = row.Noisy,
        };

        IEnumerator WarmFor(double seconds)
        {
            double end = Time.realtimeSinceStartupAsDouble + seconds;
            while (Time.realtimeSinceStartupAsDouble < end) yield return null;
        }

        IEnumerator WaitQuiet(double maxS, Action<double> waited)
        {
            double start = Time.realtimeSinceStartupAsDouble, lastNote = start;
            yield return new WaitForSecondsRealtime((float)_spec.Quiet.WindowS + 0.5f);
            while (true)
            {
                var s = _sampler.Latest;
                double el = Time.realtimeSinceStartupAsDouble - start;
                if (s.HasValue && s.Value.OtherCpuPctWindow < _spec.Quiet.CpuPct)
                {
                    Log($"machine quiet: other processes {s.Value.OtherCpuPctWindow:F1}% CPU over {_spec.Quiet.WindowS:F0} s " +
                        $"(waited {el:F0} s)");
                    break;
                }
                if (el >= maxS)
                {
                    Log($"machine still busy after {el:F0} s (other processes {s?.OtherCpuPctWindow:F1}% CPU); running anyway");
                    break;
                }
                if (Time.realtimeSinceStartupAsDouble - lastNote > 30)
                {
                    Log($"waiting for a quiet machine: other processes {s?.OtherCpuPctWindow:F1}% CPU ({el:F0} s)");
                    lastNote = Time.realtimeSinceStartupAsDouble;
                }
                yield return new WaitForSecondsRealtime(1);
            }
            waited?.Invoke(Time.realtimeSinceStartupAsDouble - start);
        }

        IEnumerator Measure(RunRequest req, Point point, int attempt, double seconds, Action<ResultRow> done)
        {
            _acc.Reset();
            _excluded.Clear();
            _frames.Clear();
            _focusLost = false;
            _measureFrame = 0;
            _beatPending = false;
            long t0 = Win32.Qpc();
            _measuring = true;
            double end = Time.realtimeSinceStartupAsDouble + seconds;
            while (Time.realtimeSinceStartupAsDouble < end) yield return null;
            _measuring = false;
            long t1 = Win32.Qpc();
            for (int i = 0; i < 40; i++) yield return null; // late frame timings (held 24 frames) and readbacks

            var row = new ResultRow
            {
                Sweep = _sweepId, Scene = _spec.Scene, Kind = req.KindName, Attempt = attempt, N = req.N,
                ViewWidthM = point.ViewWidthM, Load = point.Load, Aa = point.Aa, Seconds = seconds,
                Time = DateTime.Now.ToString("s", CultureInfo.InvariantCulture),
                Api = _api, Gpu = _gpu, Driver = _driver, PowerPlan = _powerPlan, MemMHz = _memMHz,
                JobWorkers = _jobWorkers, BackbufferWidth = _bbW, BackbufferHeight = _bbH,
            };

            // Timings: frames that started in the window, counter frames excluded, zero GPU readings counted.
            var gpu = new List<double>();
            var main = new List<double>();
            var render = new List<double>();
            double maxFrame = 0;
            ulong f0 = ToFtm(t0), f1 = ToFtm(t1);
            foreach (var f in _frames)
            {
                if (f.Start < f0 || f.Start >= f1) continue;
                row.Frames++;
                if (Excluded(f.Start)) { row.ExcludedFrames++; continue; }
                if (f.Gpu <= 0) row.GpuZeroFrames++;
                else gpu.Add(f.Gpu);
                main.Add(f.Main);
                render.Add(f.Render);
                maxFrame = Math.Max(maxFrame, f.Cpu);
            }
            row.TimedFrames = main.Count;
            var g = Stats.Summarize(gpu);
            var m = Stats.Summarize(main);
            var r = Stats.Summarize(render);
            row.GpuP99Ms = Nul(g.P99); row.GpuP50Ms = Nul(g.P50); row.GpuMeanMs = Nul(g.Mean); row.GpuMaxMs = Nul(g.Max);
            row.MainP99Ms = Nul(m.P99); row.MainMaxMs = Nul(m.Max);
            row.RenderP99Ms = Nul(r.P99); row.RenderMaxMs = Nul(r.Max);
            row.MaxFrameMs = row.Frames > 0 ? maxFrame : (double?)null;

            // Counters.
            _acc.Fill(row);
            if (_sim != null)
            {
                row.SimTickP99Ms = _sim.P99Ms;
                row.SimTickMaxMs = _sim.MaxMs;
                row.FlowRebuildSlices = _sim.FlowRebuildSlices;
                if (_sim.OffScreen.HasValue) row.OffScreen = _sim.OffScreen;
            }

            // The machine.
            var verdict = NoiseVerdict.Judge(_sampler.Between(t0, t1), _spec.Quiet);
            if (_focusLost && !verdict.FocusLost)
            {
                verdict.FocusLost = true;
                if (_spec.Quiet.RequireForeground)
                {
                    verdict.Reasons.Add("the player lost focus (OnApplicationFocus)");
                    verdict.Noisy = true;
                }
            }
            row.GpuTempC = verdict.TempMaxC;
            row.GpuThrottle = MachineFacts.ThrottleNames(verdict.ReasonsSeen & ~MachineFacts.GpuIdle);
            row.OtherCpuPctMax = verdict.OtherCpuPctMax;
            row.OtherGpuPctMax = verdict.OtherGpuPctMax;
            row.FocusLost = verdict.FocusLost;
            row.Occluded = verdict.Occluded;
            row.DisplayOff = verdict.DisplayOff;
            row.Noisy = verdict.Noisy;
            row.NoiseReasons = verdict.Reasons;
            // A run whose GPU times are incomplete can't be judged on its GPU p99.
            if (row.TimedFrames > 0 && row.GpuZeroFrames > MaxGpuZeroShare * row.TimedFrames)
            {
                row.Noisy = true;
                row.NoiseReasons.Add($"{row.GpuZeroFrames} of {row.TimedFrames} frames had no GPU time (over {MaxGpuZeroShare:P1})");
            }
            done(row);
        }

        static double? Nul(double v) => double.IsNaN(v) ? (double?)null : v;

        bool Excluded(ulong ftmStart)
        {
            foreach (var e in _excluded)
                if (ftmStart > ToFtm(e[0]) && ftmStart <= ToFtm(e[1])) return true;
            return false;
        }

        ulong ToFtm(long qpc) => qpc == long.MaxValue ? ulong.MaxValue : (ulong)(qpc * _ftmPerTick);

        /// <summary>
        /// FrameTimingManager's timestamps read QueryPerformanceCounter on Windows; this checks it
        /// (the newest frame's start must be a little before now) so run windows and counter-frame
        /// exclusions can be matched to frames by timestamp.
        /// </summary>
        void CheckClocks()
        {
            ulong ftmFreq = FrameTimingManager.GetCpuTimerFrequency();
            long qpcFreq = Win32.QpcFrequency();
            _ftmPerTick = ftmFreq > 0 && qpcFreq > 0 ? (double)ftmFreq / qpcFreq : 1;
            double lagMs = (ToFtm(Win32.Qpc()) - (double)_lastFrameStart) * 1000.0 / Math.Max(1, ftmFreq);
            Log($"clocks: FrameTimingManager {ftmFreq} Hz, QueryPerformanceCounter {qpcFreq} Hz, newest frame started {lagMs:F1} ms ago");
            if (_lastFrameStart == 0 || lagMs < 0 || lagMs > 2000)
                throw new InvalidOperationException("bench: FrameTimingManager's clock isn't QueryPerformanceCounter; can't window frames");
        }

        void Update()
        {
            long now = Win32.Qpc();
            if (_closeExclusion)
            {
                _excluded[_excluded.Count - 1][1] = now;
                _closeExclusion = false;
            }
            PollTimings();
            if (_live && _scene != null)
            {
                double t = Time.realtimeSinceStartupAsDouble - _sceneT0;
                _scene.Tick(t);
                _counters.Clear();
                if (_measuring)
                {
                    bool beat = _beatPending;
                    _beatPending = _scene.DownbeatIn(_lastSceneT, t);
                    bool every = ++_measureFrame % Math.Max(1, _spec.CounterEvery) == 0;
                    bool view = beat || every;
                    _scene.CollectCounters(_counters, view);
                    _acc.Add(_counters, view);
                    if (view)
                    {
                        // Exclude this frame and the next: frame k starts in (U[k-1], U[k]].
                        _excluded.Add(new[] { _prevUpdate, long.MaxValue });
                        _closeExclusion = true;
                        bool isBeat = beat;
                        _overdraw.Render(_rig.Camera, _scene, (o, tr) => _acc.AddOverdraw(o, tr, isBeat));
                    }
                }
                _lastSceneT = t;
            }
            _prevUpdate = now;
        }

        /// <summary>
        /// Collects every frame's timing once it is complete. GetLatestTimings returns the newest
        /// frames first, and the newest ones don't have their GPU time yet (it reads 0). A frame
        /// stays pending, updated on every poll, until at least 24 newer frames exist and its GPU
        /// time has arrived, or until it is about to leave GetLatestTimings' 64-frame window.
        /// Frames are committed in order, so an unfinished one holds back the newer ones (the
        /// foundations review: dropping zero readings biased GPU p99 low, since the frames still
        /// unfinished tend to be the slow ones; 20% of a passing probe's frames read zero).
        /// </summary>
        void PollTimings()
        {
            int Hold = Args.GetInt("--timing-hold", 24);
            FrameTimingManager.CaptureFrameTimings();
            uint n = FrameTimingManager.GetLatestTimings((uint)_ft.Length, _ft);
            for (int i = 0; i < n; i++)
            {
                var f = _ft[i];
                if (f.frameStartTimestamp <= _committed) continue;
                _lastFrameStart = Math.Max(_lastFrameStart, f.frameStartTimestamp);
                _pending[f.frameStartTimestamp] = new FrameRec
                {
                    Start = f.frameStartTimestamp,
                    Gpu = f.gpuFrameTime,
                    // The main thread's work time; its wait in Present is reported separately.
                    Main = f.cpuMainThreadFrameTime,
                    Render = f.cpuRenderThreadFrameTime,
                    Cpu = f.cpuFrameTime,
                };
            }
            if (_pending.Count <= Hold) return;
            _keys.Clear();
            _keys.AddRange(_pending.Keys);
            _keys.Sort();
            int window = _ft.Length - 2;
            for (int i = 0; i < _keys.Count - Hold; i++)
            {
                // Wait for the GPU time while the frame can still be read again.
                if (_pending[_keys[i]].Gpu <= 0 && _keys.Count - i < window) break;
                _frames.Add(_pending[_keys[i]]);
                _pending.Remove(_keys[i]);
                _committed = _keys[i];
            }
            if (_frames.Count > 200000) _frames.RemoveRange(0, 100000);
        }

        void Log(string line)
        {
            var stamped = $"{DateTime.Now:HH:mm:ss} {line}";
            Debug.Log("bench: " + line);
            _log?.WriteLine(stamped);
        }

        void Refuse(string what, string reason)
        {
            Log($"REFUSED ({what}): {reason}");
            try
            {
                File.WriteAllText(Path.Combine(_out, "refusal.json"),
                    new JsonObject().Add("refused", what).Add("reason", reason).Add("time", DateTime.Now.ToString("s", CultureInfo.InvariantCulture)) + "\n");
            }
            catch (IOException) { }
            Finish(3);
        }

        static string DisplayName(int state) => state switch { 0 => "off", 1 => "on", 2 => "dimmed", _ => "unknown" };

        static bool IsIl2cpp()
        {
#if ENABLE_IL2CPP
            return true;
#else
            return false;
#endif
        }

        void Finish(int code)
        {
            if (_finished) return;
            _finished = true;
            _live = false;
            try { _scene?.Dispose(); } catch (Exception) { }
            _overdraw?.Dispose();
            _sampler?.Dispose();
            Win32.ReleaseDisplay();
            _lock?.Dispose();
            _rows?.Dispose();
            _frontierOut?.Dispose();
            _log?.WriteLine($"{DateTime.Now:HH:mm:ss} exit {code}");
            _log?.Dispose();
            _log = null;
            if (!Application.isEditor) Application.Quit(code);
        }

        /// <summary>Aggregates a run's counters into its row.</summary>
        sealed class Counters
        {
            readonly List<double> _onScreen = new List<double>(), _dying = new List<double>(), _off = new List<double>();
            readonly List<double> _casters = new List<double>(), _skinned = new List<double>(), _particles = new List<double>();
            readonly List<double> _lightShare = new List<double>();
            readonly List<double> _opaque = new List<double>(), _transparent = new List<double>(), _beat = new List<double>();
            long _spawns, _rejected, _lightCues;
            int _frames, _spawnsMax, _lightsMax, _cueFrames;
            bool _hasSpawns, _hasRejected, _hasCues, _hasLights;
            string _byTypeLod, _environment;

            public void Reset()
            {
                _onScreen.Clear(); _dying.Clear(); _off.Clear(); _casters.Clear(); _skinned.Clear(); _particles.Clear();
                _lightShare.Clear(); _opaque.Clear(); _transparent.Clear(); _beat.Clear();
                _spawns = _rejected = _lightCues = 0;
                _frames = _spawnsMax = _lightsMax = _cueFrames = 0;
                _hasSpawns = _hasRejected = _hasCues = _hasLights = false;
                _byTypeLod = null;
                _environment = null;
            }

            public void Add(SceneCounters c, bool view)
            {
                _frames++;
                if (c.SpawnsThisFrame.HasValue)
                {
                    _hasSpawns = true;
                    _spawns += c.SpawnsThisFrame.Value;
                    _spawnsMax = Math.Max(_spawnsMax, c.SpawnsThisFrame.Value);
                }
                if (c.SpawnsRejected.HasValue) { _hasRejected = true; _rejected += c.SpawnsRejected.Value; }
                if (c.LightCues.HasValue) { _hasCues = true; _lightCues += c.LightCues.Value; _cueFrames++; }
                if (c.LightsUsed.HasValue) { _hasLights = true; _lightsMax = Math.Max(_lightsMax, c.LightsUsed.Value); }
                if (c.LightDropShare.HasValue) _lightShare.Add(c.LightDropShare.Value);
                if (c.Particles.HasValue) _particles.Add(c.Particles.Value);
                if (c.ShadowCasters.HasValue) _casters.Add(c.ShadowCasters.Value);
                if (!view) return;
                if (c.OnScreenAlive.HasValue) _onScreen.Add(c.OnScreenAlive.Value);
                if (c.OnScreenDying.HasValue) _dying.Add(c.OnScreenDying.Value);
                if (c.OffScreen.HasValue) _off.Add(c.OffScreen.Value);
                if (c.SkinnedVertices.HasValue) _skinned.Add(c.SkinnedVertices.Value);
                if (c.VisibleByTypeLod != null) _byTypeLod = c.VisibleByTypeLod;
                if (c.Environment != null) _environment = c.Environment;
            }

            public void AddOverdraw(double opaque, double transparent, bool beat)
            {
                _opaque.Add(opaque);
                (beat ? _beat : _transparent).Add(transparent);
            }

            static int? MeanInt(List<double> v) => v.Count == 0 ? (int?)null : (int)Math.Round(Stats.Summarize(v).Mean);

            static double? Mean(List<double> v) => v.Count == 0 ? (double?)null : Stats.Summarize(v).Mean;

            public void Fill(ResultRow row)
            {
                row.OnScreenAlive = MeanInt(_onScreen);
                row.OnScreenDying = MeanInt(_dying);
                row.OffScreen = MeanInt(_off);
                row.ShadowCasters = MeanInt(_casters);
                row.SkinnedVertices = MeanInt(_skinned);
                row.Particles = MeanInt(_particles);
                row.VisibleByTypeLod = _byTypeLod;
                row.Environment = _environment;
                row.SpawnsPerFrame = _hasSpawns && _frames > 0 ? _spawns / (double)_frames : (double?)null;
                row.SpawnsMaxPerFrame = _hasSpawns ? _spawnsMax : (int?)null;
                row.SpawnsRejected = _hasRejected ? (int)_rejected : (int?)null;
                row.LightCues = _hasCues && _cueFrames > 0 ? (int)Math.Round(_lightCues / (double)_cueFrames) : (int?)null; // mean active cues per frame
                row.LightsUsed = _hasLights ? _lightsMax : (int?)null;
                row.LightDropShareP99 = _lightShare.Count == 0 ? (double?)null : Stats.Summarize(_lightShare).P99;
                row.OpaqueDepth = Mean(_opaque);
                row.TransparentPerPixel = Mean(_transparent);
                row.TransparentBeatPeak = _beat.Count == 0 ? (double?)null : Stats.Summarize(_beat).Max;
                row.OverdrawFrames = _opaque.Count;
                row.OverdrawBeatFrames = _beat.Count;
            }
        }
    }
}
