using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Game.Client;
using Game.Client.Loop;
using NUnit.Framework;
using PS.Game.Sim;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.TestTools.Constraints;
using Is = NUnit.Framework.Is;

namespace Game.Tests
{
    /// <summary>The session's timing log's rows (timing.csv).</summary>
    public class TimingLogTests
    {
        static string[] Fields(TimingLog.Frame f, long[] before)
        {
            var sb = new StringBuilder();
            TimingLog.Row(f, before, sb);
            var row = sb.ToString();
            Assert.That(row, Does.EndWith("\n"));
            var fields = row.TrimEnd('\n').Split(',');
            Assert.That(fields.Length, Is.EqualTo(TimingLog.Header.Split(',').Length));
            return fields;
        }

        static string Column(string[] fields, string name) => fields[Array.IndexOf(TimingLog.Header.Split(','), name)];

        static long[] Counts() => new long[Enum.GetValues(typeof(TimingLog.Count)).Length];

        [Test]
        public void ARowGivesTheFramesTimesAndItsShareOfTheRunsCounts()
        {
            var before = Counts();
            var counts = Counts();
            before[(int)TimingLog.Count.Kills] = 40;
            counts[(int)TimingLog.Count.Kills] = 43;
            before[(int)TimingLog.Count.Dropped] = 2;
            counts[(int)TimingLog.Count.Dropped] = 2;
            counts[(int)TimingLog.Count.Late] = 1;
            var fields = Fields(new TimingLog.Frame
            {
                Index = 812, Stepped = true, RealS = 31.25, IntervalMs = 41.6667, CpuMs = 12.5, LoopMs = 3.25, StepMs = 2.5, SimMs = 0.75,
                RenderMs = 4, GpuMs = 9.125, PresentWaitMs = 0.5, Song = 120.5, LeadMs = 73.61, Phase = RunPhase.Playing, Frozen = false,
                Alive = 300, HeroX = -1.5, HeroY = 2.25, Counts = counts,
            }, before);

            Assert.That(Column(fields, "frame"), Is.EqualTo("812"));
            Assert.That(Column(fields, "stepped"), Is.EqualTo("1"));
            Assert.That(Column(fields, "interval_ms"), Is.EqualTo("41.667"));
            Assert.That(Column(fields, "cpu_ms"), Is.EqualTo("12.500"));
            Assert.That(Column(fields, "gpu_ms"), Is.EqualTo("9.125"));
            Assert.That(Column(fields, "frame_song"), Is.EqualTo("120.5000"));
            Assert.That(Column(fields, "lead_ms"), Is.EqualTo("73.61"));
            Assert.That(Column(fields, "phase"), Is.EqualTo("playing"));
            Assert.That(Column(fields, "alive"), Is.EqualTo("300"));
            Assert.That(Column(fields, "hero_x"), Is.EqualTo("-1.500"));
            Assert.That(Column(fields, "kills"), Is.EqualTo("3"), "the frame's own kills, not the run's");
            Assert.That(Column(fields, "dropped"), Is.EqualTo("0"));
            Assert.That(Column(fields, "late"), Is.EqualTo("1"));
        }

        [Test]
        public void BeforeTheSimsFirstStepTheSongAndHeroAreNotNumbersAndUnknownTimesAreNegative()
        {
            var fields = Fields(new TimingLog.Frame
            {
                Index = 3, IntervalMs = 16.7, CpuMs = -1, RenderMs = -1, GpuMs = -1, PresentWaitMs = -1,
                Song = double.NaN, LeadMs = double.NaN, HeroX = double.NaN, HeroY = double.NaN, Counts = Counts(),
            }, Counts());

            Assert.That(Column(fields, "stepped"), Is.EqualTo("0"));
            Assert.That(Column(fields, "frame_song"), Is.EqualTo("nan"));
            Assert.That(Column(fields, "hero_y"), Is.EqualTo("nan"));
            Assert.That(Column(fields, "cpu_ms"), Is.EqualTo("-1.000"));
            Assert.That(Column(fields, "phase"), Is.EqualTo("waiting"));
        }
    
        [Test]
        public void ARunsLogHasARowPerFrameWhoseCountsAddUpToTheRunsAndASteadyFrameAllocatesNothing()
        {
            string dir = Path.Combine(Path.GetTempPath(), "timing-log-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                var driver = LoopTests.FixedDriver(out var frames, 300);
                foreach (var e in LoopTests.House128()) driver.AddSettled(e);
                var telemetry = new RunTelemetry(driver, new LaunchOptions.OutputFiles(), null, null);
                var log = TimingLog.Open(dir, driver, telemetry, null, null, null);
                Assert.That(log, Is.Not.Null);
                int f = 0;
                void Next()
                {
                    log.Begin();
                    driver.Step(frames[f++]);
                    log.Write(0);
                }
                while (f < 250) Next();
                Assert.That(() => Next(), Is.Not.AllocatingGCMemory());
                while (f < frames.Length) Next();
                log.Dispose();
                telemetry.Dispose();

                var lines = File.ReadAllLines(Path.Combine(dir, TimingLog.FileName));
                var header = lines[0].Split(',');
                Assert.That(lines[0], Is.EqualTo(TimingLog.Header));
                var rows = lines.Skip(1).Select(l => l.Split(',')).ToArray();
                Assert.That(rows.Length, Is.EqualTo(frames.Length));
                Assert.That(rows.All(r => r.Length == header.Length));
                int Col(string name) => Array.IndexOf(header, name);
                Assert.That(rows.All(r => r[Col("stepped")] == "1"));
                Assert.That(double.Parse(rows[rows.Length - 1][Col("frame_song")], System.Globalization.CultureInfo.InvariantCulture),
                    Is.EqualTo(frames[frames.Length - 1].Frame).Within(1e-4));
                Assert.That(driver.Sim.EventCount, Is.GreaterThan(0));
                Assert.That(rows.Sum(r => long.Parse(r[Col("events")])), Is.EqualTo(driver.Sim.EventCount),
                    "each row holds its frame's share of the run's events");
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }

        [Test]
        public void ALogThatCantBeOpenedLeavesTheRunWithoutOne()
        {
            var driver = LoopTests.FixedDriver(out _, 1);
            var telemetry = new RunTelemetry(driver, new LaunchOptions.OutputFiles(), null, null);
            LogAssert.Expect(LogType.Warning, new Regex("^TimingLog: no timing log in "));
            string missing = Path.Combine(Path.GetTempPath(), "timing-log-" + Guid.NewGuid().ToString("N"), "gone");
            Assert.That(TimingLog.Open(missing, driver, telemetry, null, null, null), Is.Null);
            telemetry.Dispose();
        }
    }
}
