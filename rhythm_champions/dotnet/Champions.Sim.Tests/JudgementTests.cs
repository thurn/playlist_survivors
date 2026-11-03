using NUnit.Framework;

namespace Champions.Sim.Tests
{
    /// <summary>
    /// The on-beat judgement (design.md Decision 6): binary, capped 100/80 ms windows around beats, halves
    /// or strong onsets, every press with no reliable beat; in heard time,
    /// from what is known when the sim takes the press.
    /// </summary>
    public class JudgementTests
    {
        static readonly double[] Tempos = { 80, 90, 128, 174 };

        /// <summary>The sim a press heard at <paramref name="heard"/> reaches: 50 ms later, the sim's lead.</summary>
        static Judged Judge(FixtureSong song, double heard) => Drive.Run(song, heard + 0.05).Judge(heard);

        /// <summary>Beat 12's time at <paramref name="bpm"/>: well inside the clear music.</summary>
        static double Beat(double bpm) => 12 * 60.0 / bpm;

        [TestCaseSource(nameof(Tempos))]
        public void EarlyLateWindowsAcceptPlausibleHalvesAndLeaveGenuineMisses(double bpm)
        {
            var song = new FixtureSong().Beats(0, 20, bpm, onsets: false);
            double beat = Beat(bpm), period = 60 / bpm;
            double bw = System.Math.Min(0.10, period * 0.20);
            double hw = System.Math.Min(0.08, period * 0.16);
            foreach (double sign in new[] { -1d, 1d })
            {
                var hit = Judge(song, beat + sign * (bw - 0.001));
                Assert.That(hit.By, Is.EqualTo(JudgedBy.Beat));
                Assert.That(hit.Point, Is.EqualTo(beat).Within(1e-9));
                var half = Judge(song, beat + period / 2 + sign * (hw - 0.001));
                Assert.That(half.By, Is.EqualTo(JudgedBy.HalfBeat), "no onset required");
                Assert.That(half.Point, Is.EqualTo(beat + period / 2).Within(1e-9));
                foreach (double offset in new[] { bw + 0.001, period * 0.25, period / 2 - hw - 0.001 })
                {
                    double heard = beat + sign * offset;
                    var miss = Judge(song, heard);
                    Assert.That(miss.By, Is.EqualTo(JudgedBy.Off), $"{bpm}: {offset}");
                    Assert.That(System.Math.Abs(heard - miss.Point), Is.GreaterThan(miss.Window));
                    Assert.That(miss.Candidate, Is.AnyOf(JudgedBy.Beat, JudgedBy.HalfBeat));
                }
            }
        }

        [TestCaseSource(nameof(Tempos))]
        public void StrongTransientRetainsActualTimestamp(double bpm)
        {
            double onset = Beat(bpm) + 15 / bpm; // in the grid's gap
            var song = new FixtureSong().Beats(0, 20, bpm, onsets: false).Onset(onset, strong: true);
            var hit = Judge(song, onset + 0.005);
            Assert.That(hit.By, Is.EqualTo(JudgedBy.StrongOnset));
            Assert.That(hit.Point, Is.EqualTo(onset).Within(1e-9));
            var weak = new FixtureSong().Beats(0, 20, bpm, onsets: false).Onset(onset, strong: false);
            Assert.That(Judge(weak, onset).By, Is.EqualTo(JudgedBy.Off));
        }

        [Test]
        public void StrongTransientWindowAcceptsSeventyFiveMillisecondsButRejectsEightyOne()
        {
            double onset = Beat(80) + 15d / 80;
            var song = new FixtureSong().Beats(0, 20, 80, onsets: false).Onset(onset, strong: true);
            foreach (double sign in new[] { -1d, 1d })
            {
                var hit = Judge(song, onset + sign * .075);
                Assert.That(hit.By, Is.EqualTo(JudgedBy.StrongOnset));
                Assert.That(hit.Point, Is.EqualTo(onset).Within(1e-9));
                Assert.That(Judge(song, onset + sign * .081).By, Is.EqualTo(JudgedBy.Off));
            }
        }

        [Test]
        public void TempoChangeUsesNewLocalIntervalWithoutAStalePhase()
        {
            var song = new FixtureSong().Beats(0, 12, 80, onsets: false).Beats(12, 24, 174, onsets: false);
            double period = 60d / 174, beat = 12 + 12 * period;
            Assert.That(Judge(song, beat + period / 2).By, Is.EqualTo(JudgedBy.HalfBeat));
            Assert.That(Judge(song, beat + period / 4).By, Is.EqualTo(JudgedBy.Off));
            Assert.That(Judge(song, beat + 0.065).By, Is.EqualTo(JudgedBy.Beat));
            Assert.That(Judge(song, beat + 0.075).By, Is.EqualTo(JudgedBy.Off));
        }

        [Test]
        public void DelayedFrameAndCooldownFireKeepTheOriginalHeardGrade()
        {
            var s = new Simulation(new SimConfig { SurvivorWeapons = true, Loadout = System.Array.Empty<Upgrade>() });
            foreach (var k in Drive.Known(new FixtureSong().Beats(0, 16, 120, onsets: false).Entries())) s.AddKnown(k.Entry, k.KnownAt);
            s.StepTo(6.04); s.Press(Key.F, 6.04); s.Release(Key.F, 6.04);
            s.StepTo(6.55); s.Press(Key.Space, 6.5); s.Release(Key.Space, 6.5);
            s.StepTo(6.7);
            var dash = Drive.Events(s, SimEventKind.HeroDash)[1];
            Assert.That(dash.OnBeat && dash.Buffered, Is.True);
            Assert.That(s.Judge(6.64).By, Is.EqualTo(JudgedBy.Off), "firing time must not replace press time");
            Assert.That(s.Judge(6.5).Point, Is.EqualTo(6.5).Within(1e-9));
        }

        [Test]
        public void EveryPressIsOnTheBeatWithNoReliableBeat()
        {
            var unclear = new FixtureSong().Gap(0, 20);
            Assert.That(Judge(new FixtureSong().Gap(0, 20, silent: true), 7.123).By, Is.EqualTo(JudgedBy.Beatless));
            foreach (double heard in new[] { 3.0, 5.123, 7.777 })
                Assert.AreEqual(JudgedBy.Beatless, Judge(unclear, heard).By);
            Assert.AreEqual(JudgedBy.Beatless, Judge(new FixtureSong().Beats(0, 20, 128, onsets: false), 0.6).By, "the music isn't clear yet");
            Assert.AreEqual(JudgedBy.Beatless, Drive.Sim().Judge(0).By, "no music at all");
            var below = new FixtureSong().Beats(0, 20, 128, evidence: 0.1, onsets: false);
            Assert.AreEqual(JudgedBy.Beatless, Judge(below, Beat(128) + 0.2).By, "beats under the gate");
        }

        [Test]
        public void APressIsJudgedFromWhatIsKnownWhenTheSimTakesIt()
        {
            double onset = Beat(128) + 15.0 / 128;
            var entries = new FixtureSong().Beats(0, 20, 128, onsets: false).Onset(onset, strong: true).Entries();
            var known = Drive.Known(entries);
            // The strong onset known only after the press is taken.
            for (int i = 0; i < known.Count; i++)
                if (known[i].Entry.Kind == EntryKind.Onset) known[i] = new KnownEntry(known[i].Entry, onset + 0.2);
            double heard = onset + 0.005;
            Assert.AreEqual(JudgedBy.Off, Drive.Run(known, heard + 0.05).Judge(heard).By, "taken before the onset is known");
            Assert.AreEqual(JudgedBy.StrongOnset, Drive.Run(known, onset + 0.25).Judge(heard).By, "taken after");
        }
    }
}
