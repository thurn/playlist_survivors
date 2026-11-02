using NUnit.Framework;

namespace Champions.Sim.Tests
{
    /// <summary>
    /// The on-beat judgement (design.md Decision 6): binary, 80 ms either side of a beat, 60 ms either side
    /// of a half beat the music plays or a strong onset, every press with no reliable beat; in heard time,
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
        public void WithinEightyMillisecondsOfABeatIsOnTheBeat(double bpm)
        {
            var song = new FixtureSong().Beats(0, 20, bpm, onsets: false);
            double beat = Beat(bpm);
            foreach (double off in new[] { -0.079, -0.04, 0, 0.04, 0.079 })
            {
                var j = Judge(song, beat + off);
                Assert.AreEqual(JudgedBy.Beat, j.By, $"{bpm} BPM, {off * 1000:F0} ms off");
                Assert.AreEqual(beat, j.Point, 1e-9);
            }
            foreach (double off in new[] { -0.081, 0.081 })
                Assert.AreEqual(JudgedBy.Off, Judge(song, beat + off).By, $"{bpm} BPM, {off * 1000:F0} ms off");
        }

        [TestCaseSource(nameof(Tempos))]
        public void WithinSixtyMillisecondsOfAHalfBeatTheMusicPlaysIsOnTheBeat(double bpm)
        {
            double half = Beat(bpm) + 30.0 / bpm;
            var played = new FixtureSong().Beats(0, 20, bpm, onsets: false, halfOnsets: true);
            foreach (double off in new[] { -0.059, 0, 0.059 })
            {
                var j = Judge(played, half + off);
                Assert.AreEqual(JudgedBy.HalfBeat, j.By, $"{bpm} BPM, {off * 1000:F0} ms off the half beat");
                Assert.AreEqual(half, j.Point, 1e-9);
            }
            foreach (double off in new[] { -0.061, 0.061 })
                Assert.AreEqual(JudgedBy.Off, Judge(played, half + off).By, $"{bpm} BPM, {off * 1000:F0} ms off the half beat");
            var silentHalf = new FixtureSong().Beats(0, 20, bpm, onsets: false);
            Assert.AreEqual(JudgedBy.Off, Judge(silentHalf, half).By, $"{bpm} BPM: a half beat the music doesn't play counts for nothing");
        }

        [TestCaseSource(nameof(Tempos))]
        public void WithinSixtyMillisecondsOfAStrongOnsetIsOnTheBeat(double bpm)
        {
            // Between a beat's window and its half beat's, where nothing else counts.
            double onset = Beat(bpm) + 0.1;
            var song = new FixtureSong().Beats(0, 20, bpm, onsets: false).Onset(onset, strong: true);
            Assert.AreEqual(JudgedBy.Off, Judge(new FixtureSong().Beats(0, 20, bpm, onsets: false), onset + 0.03).By, $"{bpm} BPM without the onset");
            Assert.AreEqual(JudgedBy.StrongOnset, Judge(song, onset + 0.03).By, $"{bpm} BPM");
            Assert.AreEqual(JudgedBy.StrongOnset, Judge(song, onset + 0.059).By, $"{bpm} BPM");
            var weak = new FixtureSong().Beats(0, 20, bpm, onsets: false).Onset(onset, strong: false);
            Assert.AreEqual(JudgedBy.Off, Judge(weak, onset + 0.03).By, $"{bpm} BPM: a weak onset counts only at a half beat");
        }

        [Test]
        public void EveryPressIsOnTheBeatWithNoReliableBeat()
        {
            var unclear = new FixtureSong().Gap(0, 20);
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
            double onset = Beat(128) + 0.1;
            var entries = new FixtureSong().Beats(0, 20, 128, onsets: false).Onset(onset, strong: true).Entries();
            var known = Drive.Known(entries);
            // The strong onset known only after the press is taken.
            for (int i = 0; i < known.Count; i++)
                if (known[i].Entry.Kind == EntryKind.Onset) known[i] = new KnownEntry(known[i].Entry, onset + 0.2);
            double heard = onset + 0.03;
            Assert.AreEqual(JudgedBy.Off, Drive.Run(known, heard + 0.05).Judge(heard).By, "taken before the onset is known");
            Assert.AreEqual(JudgedBy.StrongOnset, Drive.Run(known, onset + 0.25).Judge(heard).By, "taken after");
        }
    }
}
