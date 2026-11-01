using System;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using Game.Client.Loop;
using NUnit.Framework;
using PS.Game.Sim;

namespace Game.Tests
{
    /// <summary>The frame profile's rows (`--profile-out`) and the presenters' timing it reads.</summary>
    public class FrameProfileTests
    {
        static double Column(string[] header, string[] row, string name) =>
            double.Parse(row[Array.IndexOf(header, name)], CultureInfo.InvariantCulture);

        [Test]
        public void AFramesWaitAndProbeAreLeftOutOfItsWorkAndTheRestFallsToTheLoopAndTheEngine()
        {
            var parts = new double[Enum.GetValues(typeof(FrameProfile.Part)).Length];
            parts[(int)FrameProfile.Part.Sim] = 1;
            parts[(int)FrameProfile.Part.Events] = 0.5;
            parts[(int)FrameProfile.Part.Crowd] = 2;
            parts[(int)FrameProfile.Part.Shatter] = 0.25;
            parts[(int)FrameProfile.Part.Hud] = 0.25;
            var frame = new FrameProfile.Frame
            {
                Index = 41, Song = 1500.123456, Alive = 300, Collections = 1, Allocated = 1234,
                TotalMs = 20, LoopMs = 15, WaitMs = 6, ProbeMs = 3, CpuMs = 7.25, Parts = parts,
            };
            var sb = new StringBuilder();
            FrameProfile.Row(frame, sb);
            var row = sb.ToString();
            Assert.That(row, Does.EndWith("\n"));
            var header = FrameProfile.Header.Split(',');
            var fields = row.TrimEnd('\n').Split(',');
            Assert.That(fields.Length, Is.EqualTo(header.Length));

            Assert.That(Column(header, fields, "frame"), Is.EqualTo(41));
            Assert.That(Column(header, fields, "frame_song"), Is.EqualTo(1500.1235));
            Assert.That(Column(header, fields, "alive"), Is.EqualTo(300));
            // The frame's own work: 20 ms less the 6 ms of music and analysis wait and the 3 ms probe draw.
            Assert.That(Column(header, fields, "frame_ms"), Is.EqualTo(11));
            Assert.That(Column(header, fields, "wait_ms"), Is.EqualTo(6));
            Assert.That(Column(header, fields, "probe_ms"), Is.EqualTo(3));
            Assert.That(Column(header, fields, "crowd_ms"), Is.EqualTo(2));
            // GameLoop's 6 ms of work less the 4 ms timed; outside it, 5 ms.
            Assert.That(Column(header, fields, "loop_ms"), Is.EqualTo(2));
            Assert.That(Column(header, fields, "engine_ms"), Is.EqualTo(5));
            Assert.That(Column(header, fields, "alloc_bytes"), Is.EqualTo(1234));
            Assert.That(Column(header, fields, "gc"), Is.EqualTo(1));
            // The main thread's CPU time, kept as measured: short of the 11 ms when the OS ran other work.
            Assert.That(Column(header, fields, "cpu_ms"), Is.EqualTo(7.25));
            double sum = 0;
            for (int i = Array.IndexOf(header, "sim_ms"); i <= Array.IndexOf(header, "engine_ms"); i++)
                sum += double.Parse(fields[i], CultureInfo.InvariantCulture);
            Assert.That(sum, Is.EqualTo(11).Within(1e-9), "the parts add up to the frame");
        }

        [Test]
        public void NegativeAndSmallValuesKeepTheirSignAndDigits()
        {
            var parts = new double[Enum.GetValues(typeof(FrameProfile.Part)).Length];
            parts[(int)FrameProfile.Part.Sim] = 0.00004;
            var frame = new FrameProfile.Frame { TotalMs = 1, LoopMs = 2, CpuMs = -1, Parts = parts };
            var sb = new StringBuilder();
            FrameProfile.Row(frame, sb);
            var header = FrameProfile.Header.Split(',');
            var fields = sb.ToString().TrimEnd('\n').Split(',');
            Assert.That(fields[Array.IndexOf(header, "sim_ms")], Is.EqualTo("0.0000"));
            Assert.That(fields[Array.IndexOf(header, "engine_ms")], Is.EqualTo("-1.0000"));
            Assert.That(fields[Array.IndexOf(header, "frame_song")], Is.EqualTo("0.0000"));
            Assert.That(fields[Array.IndexOf(header, "cpu_ms")], Is.EqualTo("-1.0000"), "unknown where the thread's time can't be read");
        }

        sealed class Busy : IPresenter
        {
            public void OnEvent(in SimEvent e) => Spin();
            public void Present(in PresentFrame frame) => Spin();

            /// <summary>Until the stopwatch ticks, whatever the machine's speed.</summary>
            static void Spin()
            {
                long start = Stopwatch.GetTimestamp();
                while (Stopwatch.GetTimestamp() == start) { }
            }
        }

        sealed class Idle : IPresenter
        {
            public void OnEvent(in SimEvent e) { }
            public void Present(in PresentFrame frame) { }
        }

        [Test]
        public void TimedDispatchChargesEachPresentersPresentToItAndTheEventsToTheirDispatch()
        {
            var events = new EventDispatch();
            events.Subscribe(new Idle());
            events.Subscribe(new Busy());
            events.Dispatch(default);
            events.Present(default);
            Assert.That(events.EventTicks, Is.Zero, "untimed");
            Assert.That(events.PresentTicks(1), Is.Zero);

            events.Timed = true;
            events.Dispatch(default);
            events.Present(default);
            Assert.That(events.EventTicks, Is.GreaterThan(0));
            Assert.That(events.PresentTicks(1), Is.GreaterThan(0));
            events.ResetTimes();
            Assert.That(events.EventTicks, Is.Zero);
            Assert.That(events.PresentTicks(1), Is.Zero);
        }
    }
}
