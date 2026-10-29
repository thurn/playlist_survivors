using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using Demo3D.Bench;
using NUnit.Framework;

namespace Demo3D.Tests
{
    /// <summary>TD7's harness logic: the fitted search, the pass rule, statistics, geometry, rows and specs.</summary>
    public class BenchCoreTests
    {
        static readonly Lines L = new Lines();

        /// <summary>Runs a search against a model of frame times: gpu, main, render p99 as functions of N.</summary>
        static (FrontierResult result, List<RunRequest> runs) Search(Func<int, (double gpu, double main, double render)> model,
                                                                      int capacity = 5000, int? seed = null, int lo = 100,
                                                                      int hi = 12000)
        {
            var s = new FrontierSearch(lo, hi, capacity, 0.05, seed, L);
            var runs = new List<RunRequest>();
            RunRequest req;
            while ((req = s.Next()) != null)
            {
                runs.Add(req);
                var (g, m, r) = model(req.N);
                s.Report(req, new RunResult
                {
                    GpuP99 = g, MainP99 = m, RenderP99 = r,
                    GpuMax = g * 1.3, MainMax = m * 1.3, RenderMax = r * 1.3,
                });
                Assert.That(runs.Count, Is.LessThan(40), "the search must terminate");
            }
            Assert.That(s.Done, Is.True);
            return (s.Result, runs);
        }

        static (double, double, double) Linear(int n) => (0.5 + 0.0015 * n, 1.0 + 0.0004 * n, 0.8 + 0.0003 * n);

        [Test]
        public void LinearCostIsFoundByTheFitAndConfirmedInFiveRuns()
        {
            var (res, runs) = Search(Linear);
            int truth = (int)((4.2 - 0.5) / 0.0015); // 2466
            Assert.That(res.N, Is.InRange((int)(truth / 1.05), truth));
            Assert.That(res.Limit, Is.EqualTo("gpu"));
            Assert.That(res.FitConfirmed, Is.True);
            Assert.That(res.ProbeRuns, Is.EqualTo(3));
            Assert.That(res.FullRuns, Is.EqualTo(2));
            Assert.That(runs[3].Kind, Is.EqualTo(RunKind.Confirm));
            Assert.That(runs[4].Kind, Is.EqualTo(RunKind.Above));
            Assert.That(runs[4].N, Is.EqualTo((int)Math.Ceiling(runs[3].N * 1.05)));
        }

        [Test]
        public void TheMainThreadCanBeTheLimit()
        {
            var (res, _) = Search(n => (0.5 + 0.0001 * n, 1.0 + 0.004 * n, 0.8));
            int truth = (int)((10.4 - 1.0) / 0.004); // 2350
            Assert.That(res.N, Is.InRange((int)(truth / 1.05), truth));
            Assert.That(res.Limit, Is.EqualTo("main"));
            Assert.That(res.FitLimit, Is.EqualTo("main"));
        }

        [Test]
        public void ACheapLoadIsCapacityBoundInFourRuns()
        {
            var (res, runs) = Search(n => (0.3 + 0.0001 * n, 1.0, 0.8), capacity: 1656);
            Assert.That(res.N, Is.EqualTo(1656));
            Assert.That(res.CapacityBound, Is.True);
            Assert.That(res.Limit, Is.EqualTo("capacity"));
            Assert.That(runs.Count, Is.EqualTo(4));
            Assert.That(runs[3].Kind, Is.EqualTo(RunKind.Ceiling));
        }

        [Test]
        public void TheCeilingIsTheSmallerOfTheRangeAndTheCapacity()
        {
            Assert.That(new FrontierSearch(100, 12000, 50000, 0.05, null, L).Ceiling, Is.EqualTo(12000));
            Assert.That(new FrontierSearch(100, 12000, 2928, 0.05, null, L).Ceiling, Is.EqualTo(2928));
        }

        [Test]
        public void ANonlinearCostFallsBackToBisectionWithinResolution()
        {
            // Quadratic: the fit over the probes overshoots, so the confirm run fails.
            Func<int, (double, double, double)> model = n => (0.3 + 1.2e-6 * n * (double)n, 1.0, 0.8);
            var (res, runs) = Search(model);
            int truth = (int)Math.Sqrt((4.2 - 0.3) / 1.2e-6); // 1802
            Assert.That(res.N, Is.LessThanOrEqualTo(truth));
            Assert.That(res.N, Is.GreaterThanOrEqualTo((int)(truth / 1.05) - 1));
            Assert.That(res.FitConfirmed, Is.False);
            Assert.That(runs.Exists(r => r.Kind == RunKind.Bisect), Is.True);
            Assert.That(res.FullRuns, Is.LessThanOrEqualTo(FrontierSearch.MaxFullRuns));
        }

        [Test]
        public void ACostThatFallsAwayAboveTheFitIsFoundAboveTheCandidate()
        {
            // Concave (sqrt): the fit undershoots, so the run 5% above the candidate also passes.
            Func<int, (double, double, double)> model = n => (0.2 + 0.07 * Math.Sqrt(n), 1.0, 0.8);
            var (res, _) = Search(model, capacity: 12000);
            int truth = (int)Math.Pow((4.2 - 0.2) / 0.07, 2); // 3265
            Assert.That(res.N, Is.LessThanOrEqualTo(truth));
            Assert.That(res.N, Is.GreaterThanOrEqualTo((int)(truth / 1.05) - 1));
        }

        [Test]
        public void EverythingFailingIsBelowRange()
        {
            var (res, _) = Search(n => (9.0, 1.0, 0.8));
            Assert.That(res.N, Is.Null);
            Assert.That(res.BelowRange, Is.True);
            Assert.That(res.Limit, Is.EqualTo("gpu"));
        }

        [Test]
        public void ProbesStartAroundANeighboursFrontier()
        {
            Assert.That(FrontierSearch.ProbeNs(100, 5000, 2000), Is.EqualTo(new[] { 1500, 2000, 2500 }));
            Assert.That(FrontierSearch.ProbeNs(100, 2200, 2000), Is.EqualTo(new[] { 1500, 2000, 2200 }));
            Assert.That(FrontierSearch.ProbeNs(100, 1100, null), Is.EqualTo(new[] { 300, 600, 900 }));
            Assert.That(FrontierSearch.ProbeNs(500, 500, null), Is.EqualTo(new[] { 500 }));
        }

        [Test]
        public void ASeededSearchFindsTheSameFrontier()
        {
            var (unseeded, _) = Search(Linear);
            var (seeded, _) = Search(Linear, seed: 2300);
            Assert.That(seeded.N, Is.InRange(unseeded.N.Value * 0.95, unseeded.N.Value * 1.05));
            Assert.That(seeded.FitConfirmed, Is.True);
        }

        [Test]
        public void NoisyMeasurementsStillLandNearTheTruth()
        {
            var rng = new Random(3);
            Func<int, (double, double, double)> model = n =>
            {
                double j = 1 + (rng.NextDouble() - 0.5) * 0.04; // ±2%
                return ((0.5 + 0.0015 * n) * j, 1.0, 0.8);
            };
            var (res, _) = Search(model);
            Assert.That(res.N, Is.InRange(2466 * 0.9, 2466 * 1.05));
        }

        [Test]
        public void ReportingAnotherRunThanNextIsAnError()
        {
            var s = new FrontierSearch(100, 12000, 5000, 0.05, null, L);
            Assert.Throws<InvalidOperationException>(() => s.Report(new RunRequest(5, RunKind.Probe), new RunResult()));
        }

        [Test]
        public void ThePassRuleChecksP99LinesMaximaSpawnsAndLights()
        {
            RunResult Ok() => new RunResult { GpuP99 = 4.0, MainP99 = 10, RenderP99 = 10, GpuMax = 8, MainMax = 20, RenderMax = 20 };
            var r = Ok();
            PassRule.Evaluate(r, L);
            Assert.That(r.Pass, Is.True);

            r = Ok(); r.GpuP99 = 4.3;
            PassRule.Evaluate(r, L);
            Assert.That(r.Pass, Is.False);
            Assert.That(r.Worst, Is.EqualTo("gpu"));

            r = Ok(); r.RenderMax = 21; // a single frame over twice its line
            PassRule.Evaluate(r, L);
            Assert.That(r.Pass, Is.False);
            Assert.That(r.Worst, Is.EqualTo("render"));

            r = Ok(); r.SpawnsRejected = 1;
            PassRule.Evaluate(r, L);
            Assert.That(r.Pass, Is.False);
            Assert.That(r.Worst, Is.EqualTo("spawns"));

            r = Ok(); r.LightDropWithinLimit = false;
            PassRule.Evaluate(r, L);
            Assert.That(r.Pass, Is.False);

            r = Ok(); r.GpuP99 = double.NaN; // no GPU timings at all
            PassRule.Evaluate(r, L);
            Assert.That(r.Pass, Is.False);
        }

        [Test]
        public void AllNoisyAttemptsAreDecidedByTheirMedian()
        {
            var a = new List<RunResult>
            {
                new RunResult { WorstRatio = 1.02, Pass = false },
                new RunResult { WorstRatio = 1.01, Pass = false },
                new RunResult { WorstRatio = 0.99, Pass = true }, // the last, lucky attempt
            };
            Assert.That(PassRule.MedianAttempt(a), Is.EqualTo(1));
            Assert.That(PassRule.MedianAttempt(new List<RunResult> { new RunResult { WorstRatio = 3 } }), Is.EqualTo(0));
        }

        [Test]
        public void PercentilesAreNearestRank()
        {
            var v = new List<double>();
            for (int i = 1; i <= 100; i++) v.Add(i);
            var s = Stats.Summarize(v);
            Assert.That(s.P99, Is.EqualTo(99));
            Assert.That(s.P50, Is.EqualTo(50));
            Assert.That(s.Max, Is.EqualTo(100));
            Assert.That(s.Mean, Is.EqualTo(50.5));
            Assert.That(Stats.Summarize(new List<double>()).P99, Is.NaN);
            Assert.That(Stats.Summarize(new List<double> { 3 }).P99, Is.EqualTo(3));
        }

        [Test]
        public void LineFitIsLeastSquares()
        {
            Assert.That(Stats.FitLine(new[] { 1.0, 2, 3 }, new[] { 3.0, 5, 7 }, out var a, out var b), Is.True);
            Assert.That(a, Is.EqualTo(1).Within(1e-9));
            Assert.That(b, Is.EqualTo(2).Within(1e-9));
            Assert.That(Stats.FitLine(new[] { 1.0, 1 }, new[] { 3.0, 5 }, out _, out _), Is.False);
        }

        [Test]
        public void OtherProcessesCpuExcludesOurOwn()
        {
            // 10 s of total time (kernel incl. idle + user), 6 s idle: 4 s busy, 3 of them ours.
            Assert.That(Stats.OtherCpuPct(0, 0, 0, 0, 60, 70, 30, 30), Is.EqualTo(10).Within(1e-9));
            Assert.That(Stats.OtherCpuPct(0, 0, 0, 0, 0, 0, 0, 0), Is.EqualTo(0));
        }

        [Test]
        public void CameraGeometryMatchesTheDesign()
        {
            Assert.That(CameraGeometry.HFovDeg, Is.EqualTo(50.9).Within(0.1));
            Assert.That(CameraGeometry.Distance(30), Is.EqualTo(30 / 0.953).Within(0.05));
            Assert.That(CameraGeometry.Height(30), Is.EqualTo(26).Within(0.5));
            Assert.That(CameraGeometry.Height(55), Is.EqualTo(47).Within(0.5));
            // camera-and-sound.md's approximate visible ground: 690, 1,220, 2,300 m².
            Assert.That(CameraGeometry.VisibleGroundM2(30), Is.EqualTo(690).Within(690 * 0.06));
            Assert.That(CameraGeometry.VisibleGroundM2(40), Is.EqualTo(1220).Within(1220 * 0.06));
            Assert.That(CameraGeometry.VisibleGroundM2(55), Is.EqualTo(2300).Within(2300 * 0.06));
            // The view width is the ground's width across the middle of the screen.
            var fp = CameraGeometry.GroundFootprint(40);
            Assert.That(fp[2] - fp[0], Is.LessThan(40)); // the bottom edge is narrower
            Assert.That(fp[4] - fp[6], Is.GreaterThan(40)); // the top edge is wider
            Assert.That(CameraGeometry.PackingCapacity(40, 2.4),
                        Is.EqualTo((int)Math.Floor(CameraGeometry.VisibleGroundM2(40) * 2.4)));
        }

        [Test]
        public void RowsAreOneInvariantJsonLineWithTheDesignsFields()
        {
            var culture = Thread.CurrentThread.CurrentCulture;
            Thread.CurrentThread.CurrentCulture = new CultureInfo("de-DE");
            try
            {
                var row = new ResultRow
                {
                    Scene = "synthetic", N = 2400, ViewWidthM = 40, Load = "demo", Aa = "msaa4", GpuP99Ms = 4.0812,
                    MainP99Ms = 4.12, RenderP99Ms = 3.05, Api = "d3d12", Driver = "610.88", GpuTempC = 64.4,
                    GpuThrottle = "power_cap", PowerPlan = "high", MemMHz = 2133, Pass = true, JobWorkers = 6,
                    BackbufferWidth = 1920, BackbufferHeight = 1080,
                };
                row.NoiseReasons.Add("the display was \"off\"");
                var json = row.ToJson();
                Assert.That(json, Does.Not.Contain("\n"));
                StringAssert.StartsWith("{\"scene\": \"synthetic\", \"n\": 2400, \"viewWidthM\": 40,", json);
                StringAssert.Contains("\"gpuP99Ms\": 4.081,", json);
                StringAssert.Contains("\"particles\": null,", json);
                StringAssert.Contains("\"spawnsRejected\": null,", json);
                StringAssert.Contains("\"gpuTempC\": 64,", json);
                StringAssert.Contains("\"jobWorkers\": 6,", json);
                StringAssert.Contains("\"backbuffer\": \"1920x1080\"", json);
                StringAssert.Contains("\"computeEstimateMs\": 12.24", json);
                StringAssert.Contains("\"noiseReasons\": [\"the display was \\\"off\\\"\"]", json);
                var parsed = (Dictionary<string, object>)Json.Parse(json);
                foreach (var key in new[] { "scene", "n", "viewWidthM", "load", "aa", "gpuP99Ms", "mainP99Ms",
                                            "renderP99Ms", "maxFrameMs", "onScreenAlive", "onScreenDying", "particles",
                                            "spawnsMaxPerFrame", "spawnsRejected", "lightsUsed", "lightDropShareP99",
                                            "opaqueDepth", "transparentPerPixel", "transparentBeatPeak", "api", "driver",
                                            "gpuTempC", "gpuThrottle", "powerPlan", "memMHz", "pass", "jobWorkers",
                                            "simTickP99Ms", "noisy" })
                    Assert.That(parsed.ContainsKey(key), Is.True, key);
                Assert.That(parsed["memMHz"], Is.EqualTo(2133.0));
                Assert.That(parsed["pass"], Is.EqualTo(true));
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = culture;
            }
        }

        [Test]
        public void TheDesignsSweepSpecParses()
        {
            var s = SweepSpec.Parse(@"{ ""scene"": ""S3"", ""seconds"": 20, ""warmup"": 5, ""probeSeconds"": 8,
              ""n"": { ""search"": [100, 12000], ""resolution"": 0.05 },
              ""viewWidthM"": [30, 40, 55], ""load"": [""light"", ""demo"", ""heavy""],
              ""aa"": [""msaa4"", ""taa""], ""jobWorkers"": 6, ""_comment"": ""ignored"" }");
            Assert.That(s.Scene, Is.EqualTo("S3"));
            Assert.That(s.NHi, Is.EqualTo(12000));
            Assert.That(s.Points().Count, Is.EqualTo(18));
            Assert.That(s.Points()[0].Key, Is.EqualTo("30m/light/msaa4"));
            Assert.That(s.Points()[1].Key, Is.EqualTo("40m/light/msaa4")); // view width varies fastest
            Assert.That(s.Lines.GpuMs, Is.EqualTo(4.2));
            Assert.Throws<FormatException>(() => SweepSpec.Parse("{\"scene\": \"S1\", \"sceens\": 1}"));
            Assert.Throws<FormatException>(() => SweepSpec.Parse("{\"n\": {\"search\": [500, 100]}}"));
            var f = SweepSpec.Parse("{\"n\": {\"fixed\": [1000, 2000]}, \"sceneOptions\": {\"triangles\": 2000}, \"quiet\": {\"gpuPct\": 7, \"requireForeground\": false}}");
            Assert.That(f.Quiet.GpuPct, Is.EqualTo(7));
            Assert.That(f.Quiet.RequireForeground, Is.False);
            Assert.That(s.Quiet.RequireForeground, Is.True);
            Assert.That(f.FixedN, Is.EqualTo(new[] { 1000, 2000 }));
            Assert.That(f.SceneOptions["triangles"], Is.EqualTo(2000.0));
        }

        [Test]
        public void NearestNeighbourDistancePrefersOneDifferingAxis()
        {
            var a = new Point { ViewWidthM = 40, Load = "demo", Aa = "msaa4" };
            var sameLoadOtherWidth = new Point { ViewWidthM = 55, Load = "demo", Aa = "msaa4" };
            var otherLoad = new Point { ViewWidthM = 40, Load = "heavy", Aa = "msaa4" };
            Assert.That(a.Distance(sameLoadOtherWidth), Is.LessThan(a.Distance(otherLoad)));
        }

        [Test]
        public void PowerPlansAndThrottleReasonsAreNamed()
        {
            Assert.That(MachineFacts.PowerPlanAllowed(MachineFacts.HighPerformance, "High performance"), Is.True);
            Assert.That(MachineFacts.PowerPlanAllowed(MachineFacts.UltimatePerformance, null), Is.True);
            Assert.That(MachineFacts.PowerPlanAllowed(MachineFacts.Balanced, "Balanced"), Is.False);
            Assert.That(MachineFacts.PowerPlanName(MachineFacts.Balanced, "Balanced"), Is.EqualTo("balanced"));
            Assert.That(MachineFacts.PowerPlanName(MachineFacts.HighPerformance, "x"), Is.EqualTo("high"));
            Assert.That(MachineFacts.ThrottleNames(0), Is.EqualTo("none"));
            Assert.That(MachineFacts.ThrottleNames(0x4 | 0x40), Is.EqualTo("power_cap|hw_thermal"));
            Assert.That(MachineFacts.SwPowerCap & MachineFacts.NoiseReasons, Is.EqualTo(0UL));
            Assert.That(MachineFacts.HwThermalSlowdown & MachineFacts.NoiseReasons, Is.Not.EqualTo(0UL));
        }

        [Test]
        public void SmbiosMemorySpeedIsTheFirstPopulatedDevicesConfiguredSpeed()
        {
            var table = new List<byte>();
            void Device(ushort sizeMb, ushort speed, ushort configured)
            {
                var s = new byte[0x28];
                s[0] = 17;
                s[1] = 0x28;
                BitConverter.GetBytes(sizeMb).CopyTo(s, 0x0C);
                BitConverter.GetBytes(speed).CopyTo(s, 0x15);
                BitConverter.GetBytes(configured).CopyTo(s, 0x20);
                table.AddRange(s);
                table.AddRange(new byte[] { (byte)'D', (byte)'I', (byte)'M', (byte)'M', 0, 0 }); // a string set
            }
            table.AddRange(new byte[] { 4, 4, 0, 0, 0, 0 }); // a short type-4 structure, no strings
            Device(0, 0, 0); // an empty slot
            Device(8192, 3200, 2133);
            table.AddRange(new byte[] { 127, 4, 0, 0, 0, 0 });
            var raw = new List<byte> { 0, 3, 3, 0 };
            raw.AddRange(BitConverter.GetBytes(table.Count));
            raw.AddRange(table);
            Assert.That(MachineFacts.MemoryMHz(raw.ToArray()), Is.EqualTo(2133));
            Assert.That(MachineFacts.MemoryMHz(new byte[3]), Is.Null);
        }

        static List<SongWindows.Frame> SongFrames(double seconds, Func<double, double> gpu)
        {
            var f = new List<SongWindows.Frame>();
            for (int i = 0; i < seconds * 120; i++)
                f.Add(new SongWindows.Frame { T = i / 120.0, GpuMs = gpu(i / 120.0), MainMs = 5, RenderMs = 4 });
            return f;
        }

        [Test]
        public void SongWindowsStepEveryFiveSecondsAndFailOnOneBadWindow()
        {
            var none = new List<(double, double)>();
            var ok = SongWindows.Evaluate(SongFrames(60, t => 3), none, 0, L, 0.02);
            // Every 5 s from the warm-up while a window fits, then one ending at the last frame.
            double end = 7199 / 120.0 - SongWindows.Window;
            Assert.That(ok.Windows.ConvertAll(w => w.From), Is.EqualTo(new[] { 5.0, 10, 15, 20, 25, 30, 35, end }).Within(1e-9));
            Assert.That(ok.Pass, Is.True);
            // 3 s at 4.5 ms (over the line, under twice it) fails exactly the windows that hold it.
            var slow = SongWindows.Evaluate(SongFrames(60, t => t >= 41 && t < 44 ? 4.5 : 3), none, 0, L, 0.02);
            Assert.That(slow.Windows.FindAll(w => !w.Pass).ConvertAll(w => w.From), Is.EqualTo(new[] { 25.0, 30, 35, end }).Within(1e-9));
            Assert.That(slow.NoFrameOverTwice, Is.True);
            Assert.That(slow.Pass, Is.False);
        }

        [Test]
        public void SongWindowsFailOnOneSpikeARejectionOrDroppedLight()
        {
            var none = new List<(double, double)>();
            var spike = SongWindows.Evaluate(SongFrames(40, t => Math.Abs(t - 12) < 0.004 ? 9 : 3), none, 0, L, 0.02);
            Assert.That(spike.Windows.TrueForAll(w => w.Pass), Is.True, "one frame moves no p99");
            Assert.That(spike.NoFrameOverTwice, Is.False);
            Assert.That(spike.Pass, Is.False);
            var loading = SongWindows.Evaluate(SongFrames(40, t => Math.Abs(t - 2) < 0.004 ? 900 : 3), none, 0, L, 0.02);
            Assert.That(loading.Pass, Is.True, "a warm-up frame is reported, not judged");
            Assert.That(loading.WarmupMaxGpu, Is.EqualTo(900));
            Assert.That(SongWindows.Evaluate(SongFrames(40, t => 3), none, 1, L, 0.02).Pass, Is.False);
            var drops = new List<(double, double)>();
            for (double t = 0; t < 40; t += 0.1) drops.Add((t, t > 20 && t < 21 ? 0.1 : 0));
            var dropped = SongWindows.Evaluate(SongFrames(40, t => 3), drops, 0, L, 0.02);
            Assert.That(dropped.Windows[0].DropP99, Is.EqualTo(0.1));
            Assert.That(dropped.Pass, Is.False);
        }

        [Test]
        public void SongWindowsCountFramesWithoutGpuTimeOnlyOnTheCpuLines()
        {
            var f = SongFrames(30, t => 3);
            for (int i = 0; i < f.Count; i += 100) f[i] = new SongWindows.Frame { T = f[i].T, MainMs = 5, RenderMs = 4 };
            var r = SongWindows.Evaluate(f, new List<(double, double)>(), 0, L, 0.02);
            Assert.That(r.Windows.Count, Is.EqualTo(2));
            Assert.That(r.Windows[0].GpuP99, Is.EqualTo(3));
            Assert.That(r.GpuZeroFrames, Is.EqualTo(36));
            Assert.That(r.Noisy, Is.True, "1% of frames without a GPU time is noise");
            Assert.That(r.Pass, Is.True, "noise is reported, as the harness does, not failed");
            Assert.That(r.ToJson(), Does.Contain("\"song\": 5.00"));
        }

        /// <summary>The review of the answer milestone: no GPU data is no pass; the run's end is always judged; garbage readings are counted, not judged.</summary>
        [Test]
        public void SongWindowsFailWithoutGpuDataAndJudgeTheRunsEnd()
        {
            var none = new List<(double, double)>();
            var blind = SongWindows.Evaluate(SongFrames(40, t => 0), none, 0, L, 0.02);
            Assert.That(blind.Windows.TrueForAll(w => !w.Pass), Is.True);
            Assert.That(blind.Pass, Is.False, "a run with no GPU times must not pass");
            // 47 s: the last 2 s at 4.5 ms fall in no 5 s step's window, but the final window holds them.
            var tail = SongWindows.Evaluate(SongFrames(47, t => t >= 45 ? 4.5 : 3), none, 0, L, 0.02);
            Assert.That(tail.Windows[tail.Windows.Count - 1].From, Is.EqualTo(5639 / 120.0 - SongWindows.Window).Within(1e-9));
            Assert.That(tail.Windows[tail.Windows.Count - 1].Pass, Is.False);
            Assert.That(tail.Pass, Is.False);
            // Song time can start before 0 (the pre-roll): windows count from the first frame.
            var shifted = SongFrames(40, t => 3).ConvertAll(f => new SongWindows.Frame { T = f.T - 0.5, GpuMs = f.GpuMs, MainMs = f.MainMs, RenderMs = f.RenderMs });
            Assert.That(SongWindows.Evaluate(shifted, none, 0, L, 0.02).Windows[0].From, Is.EqualTo(4.5).Within(1e-9));
            var garbage = SongFrames(40, t => Math.Abs(t - 12) < 0.004 ? 1.6e13 : 3);
            var g = SongWindows.Evaluate(garbage, none, 0, L, 0.02);
            Assert.That(g.GpuGarbageFrames, Is.EqualTo(1));
            Assert.That(g.Pass, Is.True, "a garbage GPU reading is missing data, not a spike");
        }

        [Test]
        public void JsonReadsEscapesAndNesting()
        {
            var d = (Dictionary<string, object>)Json.Parse("{\"a\": [1, -2.5e1, true, null], \"b\": \"x\\\"\\u0041\"}");
            var a = (List<object>)d["a"];
            Assert.That(a[1], Is.EqualTo(-25.0));
            Assert.That(a[3], Is.Null);
            Assert.That(d["b"], Is.EqualTo("x\"A"));
            Assert.Throws<FormatException>(() => Json.Parse("{\"a\": 1} x"));
        }
    }
}
