using System;
using System.Text;
using Game.Client.Loop;
using NUnit.Framework;
using PS.Game.Sim;

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
    }
}
