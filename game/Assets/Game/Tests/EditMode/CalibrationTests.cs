using System;
using System.IO;
using System.Linq;
using Game.Client.Calibration;
using Game.Client.Music;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Game.Tests
{
    /// <summary>The calibrations (unity-game.md, Calibration): the tap-along's offset, the offsets on the clocks, and their file per output device.</summary>
    public class CalibrationTests
    {
        string _dir;

        [SetUp]
        public void MakeDir()
        {
            _dir = Path.Combine(Path.GetTempPath(), "ps-calibration-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
        }

        [TearDown]
        public void RemoveDir() => Directory.Delete(_dir, true);

        static Plugin.Device Device(string uid, string name = "a device") => new Plugin.Device(uid, name, 0.01, false);

        [Test]
        public void ATapAlongTakesTheMedianErrorAfterTheFirstTwoTaps()
        {
            var clicks = Enumerable.Range(0, TapAlong.Clicks).Select(k => 1.0 + k * TapAlong.Period).ToArray();
            // Late by 30 ms, the first two way off, one a stray between clicks.
            var taps = clicks.Select(c => c + 0.03).ToArray();
            taps[0] = 1.2;
            taps[1] = 1.4;
            taps[5] = clicks[5] + 0.25;
            Assert.That(TapAlong.Offset(taps, clicks), Is.EqualTo(0.03).Within(1e-9));
            Assert.That(TapAlong.Offset(taps.Take(4).ToArray(), clicks), Is.Null, "two counted taps");
            // Early taps are a negative offset; an even count takes the middle two's mean.
            var early = new[] { 0.0, 0.0, clicks[2] - 0.01, clicks[3] - 0.02, clicks[4] - 0.03, clicks[5] - 0.05 };
            Assert.That(TapAlong.Offset(early, clicks), Is.EqualTo(-0.025).Within(1e-9));
            Assert.That(TapAlong.Error(clicks[3] + TapAlong.Window + 0.001, clicks), Is.Null, "past a quarter beat");
        }

        [Test]
        public void TheInputOffsetHearsKeysThatMuchEarlier()
        {
            var clock = SteadyClock();
            double before = clock.KeySong(10.0);
            new Offsets { Input = 0.032 }.ApplyTo(clock);
            Assert.That(clock.KeySong(10.0), Is.EqualTo(before - 0.032).Within(1e-9));
        }

        [Test]
        public void AVideoOffsetShowsEachMomentThatMuchEarlier()
        {
            // The frame that first shows song 9.9 (a flash): moving the flash earlier is a larger offset.
            double FlashAt(double video)
            {
                var clock = SteadyClock();
                new Offsets { Video = video }.ApplyTo(clock);
                for (double real = 9.0; ; real += 0.001)
                    if (clock.FrameSong(real) >= 9.9) return real;
            }
            double none = FlashAt(0), earlier = FlashAt(0.02);
            Assert.That(none - earlier, Is.EqualTo(0.02).Within(0.0011));
        }

        /// <summary>A clock whose DSP time is real time, anchored at song 0, with 40 ms of output latency and a 60 Hz display.</summary>
        static MusicClock SteadyClock()
        {
            var clock = new MusicClock { OutputLatency = 0.04, DisplayLatency = 1.0 / 60 };
            clock.SetAnchor(0, 0);
            for (double r = 0; r <= 12; r += 1.0 / 60) clock.Sample(r, r);
            return clock;
        }

        [Test]
        public void EachOutputDeviceKeepsItsOwnCalibration()
        {
            string path = Path.Combine(_dir, "sub", "calibration.json");
            var store = CalibrationStore.Load(path);
            Assert.That(store.Has("speakers"), Is.False);
            Assert.That(store.For("speakers"), Is.EqualTo(default(Offsets)));
            Assert.That(store.Set(Device("speakers", "MacBook Pro Speakers"), new Offsets { Video = 0.0154, Input = 0.03 }), Is.True);
            Assert.That(store.Set(Device("airpods", "AirPods"), new Offsets { Video = -0.01, Input = 0.12 }), Is.True);
            // Calibrating one offset again keeps the other.
            var speakers = store.For("speakers");
            speakers.Input = 0.04;
            store.Set(Device("speakers", "MacBook Pro Speakers"), speakers);

            var back = CalibrationStore.Load(path);
            Assert.That(back.For("speakers").Video, Is.EqualTo(0.015).Within(1e-12), "whole milliseconds");
            Assert.That(back.For("speakers").Input, Is.EqualTo(0.04).Within(1e-12));
            Assert.That(back.For("airpods").Input, Is.EqualTo(0.12).Within(1e-12));
            Assert.That(back.For("airpods").Video, Is.EqualTo(-0.01).Within(1e-12));
            Assert.That(back.For("headphones"), Is.EqualTo(default(Offsets)), "a device not calibrated yet has none");
            StringAssert.Contains("MacBook Pro Speakers", File.ReadAllText(path));
        }

        [Test]
        public void AnUnreadableFileStartsFromNoneAndIsReplaced()
        {
            string path = Path.Combine(_dir, "calibration.json");
            File.WriteAllText(path, "{ not json");
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("can't read"));
            var store = CalibrationStore.Load(path);
            Assert.That(store.For("speakers"), Is.EqualTo(default(Offsets)));
            Assert.That(store.Set(Device("speakers"), new Offsets { Input = 0.02 }), Is.True);
            Assert.That(CalibrationStore.Load(path).For("speakers").Input, Is.EqualTo(0.02).Within(1e-12));
        }

        [Test]
        public void DeviceRecordsReadAsTheHeaderLaysThemOut()
        {
            // ps_device: a 256-byte UID, a 128-byte name, a double latency at 384, an int32 flag at 392.
            var raw = new byte[Plugin.DeviceBytes];
            System.Text.Encoding.UTF8.GetBytes("BuiltInSpeakerDevice").CopyTo(raw, 0);
            for (int i = 0; i < Plugin.DeviceNameBytes; i++) raw[Plugin.DeviceUidBytes + i] = (byte)'n';
            BitConverter.GetBytes(0.0123).CopyTo(raw, 384);
            BitConverter.GetBytes(1).CopyTo(raw, 392);
            var d = Plugin.ReadDevice(raw);
            Assert.That(d.Uid, Is.EqualTo("BuiltInSpeakerDevice"));
            Assert.That(d.Name, Is.EqualTo(new string('n', Plugin.DeviceNameBytes)));
            Assert.That(d.Latency, Is.EqualTo(0.0123));
            Assert.That(d.Bluetooth, Is.True);
        }

        [Test]
        public void ThePluginReportsTheDefaultOutputDevice()
        {
            if (!File.Exists(Path.GetFullPath(PluginLoader.BuiltPath))) Assert.Fail("no plugin: run `game plugin` (game setup does)");
            // Whatever this machine's output is (a CI Mac may have none): the call succeeds.
            var d = Plugin.OutputDevice();
            Assert.That(d.Latency, Is.GreaterThanOrEqualTo(0));
            if (d.Uid.Length > 0) Assert.That(d.Name, Is.Not.Empty);
        }
    }
}
