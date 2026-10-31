using System.IO;
using PS.Choreography;
using Demo3D.EditorTools.Arena;
using Demo3D.Runtime.Arena;
using Demo3D.Runtime.Playback;
using NUnit.Framework;

namespace Demo3D.Tests
{
    /// <summary>ps-e8r.18's post feel over the song's map and the layout's amounts.</summary>
    public class PostFeelTests
    {
        static SongMap _map;
        static PostFeel.Amounts _amounts;

        [OneTimeSetUp]
        public void Load()
        {
            _map = SongMap.Parse(File.ReadAllText("Assets/Baked/Audio/action.songmap.json"));
            _amounts = ArenaLighting.Parse(File.ReadAllText(ArenaBaker.LayoutPath)).PostFeel;
        }

        static PostFeel Feel(bool enabled = true) =>
            new PostFeel(new Timeline { Map = _map, Actions = new ActionEvent[0] }, _amounts, enabled);

        static double BeatLength(int b) => _map.Beats[b + 1] - _map.Beats[b];

        [Test]
        public void BeatsAreBrighterThanTheOffBeatsInEverySection()
        {
            var feel = Feel();
            foreach (var s in _map.Sections)
            {
                if (s.Feel == SectionFeel.Free) continue;
                double on = 0, off = 0;
                int n = 0;
                // Past the section's ease, so the grade is steady and only the pulse and flashes move.
                for (int b = s.FirstBeat + 8; b < s.EndBeat - 1 && b + 1 < _map.Beats.Length; b++)
                {
                    on += feel.At(_map.Beats[b] + 1e-4).Exposure;
                    off += feel.At(_map.Beats[b] + 0.6 * BeatLength(b)).Exposure;
                    n++;
                }
                if (n == 0) continue;
                Assert.That(on / n, Is.GreaterThan(off / n), s.Label);
            }
        }

        [Test]
        public void FlashesDecayWithinABeat()
        {
            var feel = Feel();
            int isolated = 0;
            for (int i = 0; i < _map.Accents.Length; i++)
            {
                double t = _map.Accents[i].Time;
                double beat = BeatLength((int)System.Math.Floor(_map.BeatIndexAt(t)));
                bool alone = true;
                foreach (var other in _map.Accents) if (other.Time > t && other.Time < t + 2 * beat) alone = false;
                foreach (var s in _map.Sections)
                    foreach (var a in s.Anchors) if (a.Time > t && a.Time < t + 2 * beat) alone = false;
                if (!alone) continue;
                Assert.That(feel.Flash(t + 1e-4), Is.GreaterThan(0.5f * _map.Accents[i].Strength), $"accent at {t}");
                Assert.That(feel.Flash(t + beat + 1e-3), Is.EqualTo(0f), $"accent at {t}, a beat later");
                Assert.That(feel.At(t + beat + 1e-3).Chromatic, Is.EqualTo(0f));
                isolated++;
            }
            Assert.That(isolated, Is.GreaterThan(10));
        }

        [Test]
        public void DropsAndThePeakAreHotterThanTheBreaks()
        {
            var feel = Feel();
            float Mid(string label, System.Func<PostFeel.SectionGrade, float> f)
            {
                var s = _map.Sections[_map.SectionIndexOf(label)];
                return f(feel.Grade(0.5 * (s.Start + s.End), out _));
            }
            foreach (var hot in new[] { "drop-1", "drop-2", "final-peak" })
            foreach (var cool in new[] { "breakdown", "break-1", "break-2" })
            {
                Assert.That(Mid(hot, g => g.Saturation), Is.GreaterThan(Mid(cool, g => g.Saturation)), $"{hot} vs {cool}");
                Assert.That(Mid(hot, g => g.Exposure), Is.GreaterThan(Mid(cool, g => g.Exposure)), $"{hot} vs {cool}");
                Assert.That(Mid(hot, g => g.Temperature), Is.GreaterThan(Mid(cool, g => g.Temperature)), $"{hot} vs {cool}");
                // ps-e8r.10: the climaxes also close in on the stage, darker at the frame's edges.
                Assert.That(Mid(hot, g => g.Vignette), Is.GreaterThan(Mid(cool, g => g.Vignette)), $"{hot} vs {cool}");
            }
        }

        [Test]
        public void SectionOffsetsEaseInRatherThanCut()
        {
            var feel = Feel();
            var s = _map.Sections[_map.SectionIndexOf("drop-2")];
            float before = feel.Grade(s.Start - 1e-3, out _).Saturation, after = feel.Grade(s.Start + 1e-3, out _).Saturation;
            Assert.That(System.Math.Abs(after - before), Is.LessThan(0.5f), "no jump at the boundary");
        }

        [Test]
        public void AFlashCutPeaksOnItsCutAndIsGoneWithinAnEighth()
        {
            double cut = _map.TimeOfBarBeat("81.1"), eighth = 0.5 * BeatLength((int)System.Math.Round(_map.BeatIndexAt(cut)));
            var plain = Feel();
            var feel = new PostFeel(_map, _amounts, true, new (double, float)[0], null, new[] { cut });
            Assert.That(feel.CutFlash(cut - 1e-4), Is.EqualTo(0f), "before the cut");
            Assert.That(feel.CutFlash(cut + 1.0 / 60), Is.GreaterThan(0.8f), "the cut's frame");
            Assert.That(feel.At(cut + 1.0 / 60).Exposure - plain.At(cut + 1.0 / 60).Exposure, Is.GreaterThan(3f), "near-white");
            Assert.That(feel.CutFlash(cut + 0.5 * eighth), Is.LessThan(feel.CutFlash(cut + 0.25 * eighth)), "decaying");
            Assert.That(feel.CutFlash(cut + eighth + 1e-4), Is.EqualTo(0f), "an eighth on");
            Assert.That(feel.At(cut + eighth + 1e-4), Is.EqualTo(plain.At(cut + eighth + 1e-4)));
        }

        [Test]
        public void ItIsAllZeroWhenOff()
        {
            var feel = Feel(enabled: false);
            for (double t = 0; t < _map.Duration; t += 1.0 / 60)
                Assert.That(feel.At(t), Is.EqualTo(default(PostFeel.Offsets)), $"t = {t}");
        }

        [Test]
        public void ItIsAFunctionOfSongTime()
        {
            PostFeel a = Feel(), b = Feel();
            for (double t = 0; t < _map.Duration; t += 0.37)
                Assert.That(a.At(t), Is.EqualTo(b.At(t)), $"t = {t}");
        }
    }
}
