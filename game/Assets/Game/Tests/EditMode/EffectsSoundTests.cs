using System;
using System.Collections.Generic;
using System.Linq;
using Game.Client.Effects;
using Game.Client.Loop;
using Game.Client.Sound;
using NUnit.Framework;
using PS.Game.Sim;
using PS.Game.Sim.Content;
using UnityEngine;
using Is = NUnit.Framework.Is;

namespace Game.Tests
{
    /// <summary>Effects and sound: cues at their song times, the crystal waves' radii, the musical grid, scheduling and aggregation.</summary>
    public class EffectsSoundTests
    {
        const double Period = 0.5;

        /// <summary>A sim with committed beats every half second from 2 s, known 2 s ahead, stepped to <paramref name="now"/>.</summary>
        static Simulation Beats(double now = 4)
        {
            var sim = new Simulation(new SimConfig());
            for (int b = 0; b < 20; b++)
            {
                var e = StreamEntry.Beat(2 + b * Period, 0, Period, 0.9);
                sim.AddKnown(e, Math.Max(0, e.T - 2));
            }
            sim.StepTo(now);
            return sim;
        }

        /// <summary>A clock whose mixer is at <see cref="Mixer"/>: a song time maps to DSP time song + 100, schedulable from the mixer on.</summary>
        sealed class TestClock : IGameClock
        {
            public double Mixer;
            public bool Ready => true;
            public FrameTimes Frame(in ClockReadings now) => default;
            public double KeySong(double real) => real;

            public bool TryDspAt(double song, out double dsp)
            {
                dsp = song + 100;
                return song >= Mixer;
            }
        }

        static SoundBank Bank(float onset = 0)
        {
            var bank = ScriptableObject.CreateInstance<SoundBank>();
            var names = new List<string>();
            foreach (SoundKind k in Enum.GetValues(typeof(SoundKind)))
                for (int v = 0; v < 3; v++) names.Add($"{SoundScheduler.Name(k)}/{v}");
            bank.Names = names.ToArray();
            bank.Clips = names.Select(n => AudioClip.Create(n, 4800, 1, 48000, false)).ToArray();
            bank.Onsets = names.Select(_ => onset).ToArray();
            bank.Levels = names.Select(_ => 1f).ToArray();
            bank.RootNotes = names.Select(n => n.StartsWith("chime") ? 91f : 0f).ToArray();
            return bank;
        }

        GameObject _host;

        [SetUp]
        public void Host() => _host = new GameObject("sounds");

        [TearDown]
        public void Clean() => UnityEngine.Object.DestroyImmediate(_host);

        SoundScheduler Scheduler(TestClock clock, SoundBank bank = null) =>
            new SoundScheduler(bank ?? Bank(), _host, clock, () => 0, log: true);

        [Test]
        public void SlotsFallOnTheGridAndPastTheLastBeat()
        {
            var beats = Beats().Stream.Beats;
            Assert.That(MusicalGrid.SlotAt(beats, 2.2, MusicalGrid.Beat), Is.EqualTo(2.5).Within(1e-9));
            Assert.That(MusicalGrid.SlotAt(beats, 2.2, MusicalGrid.Sixteenth), Is.EqualTo(2.25).Within(1e-9));
            Assert.That(MusicalGrid.SlotAt(beats, 2.25, MusicalGrid.Sixteenth), Is.EqualTo(2.25).Within(1e-9), "on a slot");
            Assert.That(MusicalGrid.SlotAt(beats, 1.2, MusicalGrid.Beat), Is.EqualTo(1.2), "before the first beat");
            // Beats are revealed up to 6.0 s at 4 s; past the last, its period carries the grid.
            Assert.That(MusicalGrid.SlotAt(beats, 6.1, MusicalGrid.Beat), Is.EqualTo(6.5).Within(1e-9));
            Assert.That(MusicalGrid.Bucket(beats, 2.49), Is.EqualTo(0));
            Assert.That(MusicalGrid.Bucket(beats, 2.5), Is.EqualTo(1));
        }

        [Test]
        public void GemChimesClimbAPentatonicScaleHoldTheTopAndResetAfterAPause()
        {
            var chain = new PentatonicChain();
            var notes = Enumerable.Range(0, 13).Select(i => chain.Next(i * 0.125)).ToArray();
            Assert.That(notes.Take(11), Is.EqualTo(new[] { 84, 86, 88, 91, 93, 96, 98, 100, 103, 105, 108 }));
            Assert.That(notes[12], Is.EqualTo(108));
            Assert.That(chain.Next(12 * 0.125 + 0.5), Is.EqualTo(84), "a pause returns to C6");
        }

        [Test]
        public void ASoundTheMixerCantMakeIsDroppedNotStartedLate()
        {
            var clock = new TestClock { Mixer = 3.0 };
            var s = Scheduler(clock);
            var cue = new SoundCue { Kind = SoundKind.Hit, Gain = 1, Pitch = 1 };
            cue.Song = 2.99;
            Assert.That(s.Play(cue, 0, 1), Is.EqualTo(-1));
            cue.Song = 3.2;
            Assert.That(s.Play(cue, 0, 1), Is.GreaterThanOrEqualTo(0));
            Assert.That(s.Scheduled, Is.EqualTo(1));
            Assert.That(s.Dropped, Is.EqualTo(1));
            Assert.That(double.IsNaN(s.Log.Array[0].Dsp));
            Assert.That(s.Log.Array[1].Dsp, Is.EqualTo(103.2).Within(1e-9), "the sound's DSP time is its song time's");
        }

        [Test]
        public void AClipStartsItsOnsetEarlySoItsAttackLandsOnTheSongTime()
        {
            var clock = new TestClock { Mixer = 3.0 };
            var s = Scheduler(clock, Bank(onset: 0.02f));
            s.Play(new SoundCue { Kind = SoundKind.Hurt, Song = 3.5, Gain = 1, Pitch = 2 }, 0, 1);
            // The log holds where the attack lands; the voice starts 0.02 s / 2 earlier.
            Assert.That(s.Log.Array[0].Dsp, Is.EqualTo(103.5).Within(1e-9));
            clock.Mixer = 3.495;
            Assert.That(s.Play(new SoundCue { Kind = SoundKind.Hurt, Song = 3.5, Gain = 1, Pitch = 1 }, 0, 1), Is.EqualTo(-1),
                "its early start is already past");
        }

        [Test]
        public void ABeatAndCategoryPlayAtMostThreeVoicesWhoseGainGrowsWithTheCount()
        {
            var clock = new TestClock();
            var s = Scheduler(clock);
            var agg = new SoundAggregator(s);
            for (int i = 0; i < 10; i++)
                agg.Request(new SoundCue { Kind = SoundKind.Hit, Song = 3.0, Gain = 1, Pitch = 1 }, bucket: 2, strength: 1);
            agg.Request(new SoundCue { Kind = SoundKind.Death, Song = 3.0, Gain = 1, Pitch = 1 }, bucket: 2, strength: 1);
            agg.Request(new SoundCue { Kind = SoundKind.Hit, Song = 3.5, Gain = 1, Pitch = 1 }, bucket: 3, strength: 1);
            Assert.That(s.Scheduled, Is.EqualTo(3 + 1 + 1));
            Assert.That(agg.Folded, Is.EqualTo(7));
            Assert.That(SoundAggregator.Gain(1, 1), Is.EqualTo(0.35f).Within(1e-6));
            Assert.That(SoundAggregator.Gain(8, 1), Is.EqualTo(0.8f).Within(1e-6));
            Assert.That(SoundAggregator.Gain(64, 1), Is.EqualTo(1f), "capped");
            Assert.That(SoundAggregator.Gain(1, 0), Is.EqualTo(0.35f * 0.7f).Within(1e-6));
        }

        [Test]
        public void ADroppedSoundLeavesItsBeatsVoiceToALaterRequest()
        {
            var clock = new TestClock { Mixer = 3.0 };
            var s = Scheduler(clock);
            var agg = new SoundAggregator(s);
            agg.Request(new SoundCue { Kind = SoundKind.Hit, Song = 2.99, Gain = 1, Pitch = 1 }, bucket: 2, strength: 1);
            for (int i = 0; i < 3; i++)
                agg.Request(new SoundCue { Kind = SoundKind.Hit, Song = 3.2 + 0.1 * i, Gain = 1, Pitch = 1 }, bucket: 2, strength: 1);
            Assert.That(s.Dropped, Is.EqualTo(1));
            Assert.That(s.Scheduled, Is.EqualTo(3), "the beat's three voices go to the sounds that can make their time");
            Assert.That(agg.Folded, Is.EqualTo(0));
        }

        [Test]
        public void TheVoicePoolAndTheMusicFitInUnitysRealVoices()
        {
            // Past its real voices Unity virtualizes (silences) the least audible: never the music or a scheduled sound.
            Assert.That(SoundScheduler.Voices + 1, Is.LessThanOrEqualTo(AudioSettings.GetConfiguration().numRealVoices));
        }

        [Test]
        public void EventsScheduleImpactsAtTheirTimesAndSnapHurtsAndChimesToTheSixteenth()
        {
            var sim = Beats();
            var clock = new TestClock { Mixer = 0 };
            var s = Scheduler(clock);
            var p = new SoundPresenter(s, sim, new RoleLooks(null), 0.8f, null);
            p.OnEvent(SimEvent.SwordAttack(2.68, 0, HeroAttackKind.SlashLeft, 2.68, 3.0, 1, 0, false, 7.04));
            p.OnEvent(SimEvent.EnemyHit(3.0, 4, 0, WeaponHit.Strike, 1, 2, 0, 1, 0));
            p.OnEvent(SimEvent.Death(3.0, 4, 0, 2, 0, 1, 0, 1.85));
            p.OnEvent(SimEvent.HeroHit(3.3, EnemyAttackKind.Lunge, 10, 0, 0, 1, 0));
            p.OnEvent(SimEvent.Pickup(3.55, 0, (int)ItemKind.Gem, 0, 0, 1));
            p.OnEvent(SimEvent.Pickup(3.6, 1, (int)ItemKind.Gem, 0, 0, 1));
            var songs = s.Log.Select(e => Math.Round(e.Song, 6)).ToList();
            // Swing, hit, death cry, ice break and glass spray at the impact; the tinkle as the shards land.
            Assert.That(songs.Count(t => t == 3.0), Is.EqualTo(5));
            Assert.That(songs.Single(t => t > 3.0 && t < 3.5 && t != 3.375), Is.InRange(3.0 + 0.38 * 0.85, 3.0 + 0.38 * 1.15));
            Assert.That(songs, Does.Contain(3.375), "the hurt on the next sixteenth");
            Assert.That(songs.Count(t => t == 3.625), Is.EqualTo(2), "both chimes on the sixteenth after them");
            Assert.That(s.Dropped, Is.EqualTo(0));
        }

        [Test]
        public void EffectsWaitForTheFrameThatReachesTheirSongTime()
        {
            var cues = new EffectCues(new RoleLooks(null));
            var due = new List<EffectCues.Cue>();
            // The attack settles at its wind-up; its slash belongs to its impact.
            cues.OnEvent(SimEvent.SwordAttack(2.68, 0, HeroAttackKind.Spin, 2.68, 3.0, 0, 1, true, 9.152));
            for (int e = 0; e < 4; e++)
            {
                cues.OnEvent(SimEvent.EnemyHit(3.0, e, 0, WeaponHit.HeavyStrike, 30, e, 0, 1, 0));
                if (e < 3) cues.OnEvent(SimEvent.Death(3.0, e, 0, e, 0, 1, 0, 4.63));
            }
            cues.OnEvent(SimEvent.Cleave(3.0, 0, 7, 0, 0, 9.152, 15.07, 3.25, 0.125));
            cues.OnEvent(SimEvent.EnemyHit(3.375, 9, 0, WeaponHit.Cleave, 30, 11, 0, 1, 0));

            cues.TakeDue(2.99, due);
            Assert.That(due, Is.Empty, "nothing before its time");
            cues.TakeDue(3.0, due);
            var spin = due.Single(c => c.Kind == EffectCues.Kind.Spin);
            Assert.That(spin.T, Is.EqualTo(3.0));
            Assert.That(spin.Scale, Is.EqualTo(9.152f * EffectCues.HeavySlash).Within(1e-4), "a heavy strike oversizes its slash");
            Assert.That(due.Count(c => c.Kind == EffectCues.Kind.Death), Is.EqualTo(3));
            Assert.That(due.Where(c => c.Kind == EffectCues.Kind.Death).All(c => c.Together == 3), "an instant's deaths share their light");
            Assert.That(due.Single(c => c.Kind == EffectCues.Kind.Flash).Enemy, Is.EqualTo(3), "only the hit that didn't kill flashes");
            Assert.That(due.Single(c => c.Kind == EffectCues.Kind.Cleave).Bands, Is.EqualTo(7));
            cues.TakeDue(3.374, due);
            Assert.That(due, Is.Empty);
            cues.TakeDue(3.4, due);
            Assert.That(due.Single().T, Is.EqualTo(3.375), "the cleave band's hit on its own frame");
            Assert.That(cues.Waiting, Is.EqualTo(0));
        }

        [Test]
        public void OnlyTheSwordsAttacksSlashAndSwing()
        {
            // Content's magic wand (weapon 4, a missile) has no look or sound yet: none of the sword's stands in.
            var wand = SimEvent.HeroAttack(2.68, 4, ShapeKind.Missile, 0, 2.68, 3.0, 1, 0, false, 0);
            var wandHit = SimEvent.EnemyHit(3.0, 4, 4, WeaponHit.Strike, 1, 2, 0, 1, 0);
            var sword = SimEvent.SwordAttack(3.18, 0, HeroAttackKind.SlashLeft, 3.18, 3.5, 1, 0, false, 7.04);

            var cues = new EffectCues(new RoleLooks(null));
            var due = new List<EffectCues.Cue>();
            cues.OnEvent(wand);
            cues.OnEvent(wandHit);
            cues.OnEvent(sword);
            cues.TakeDue(3.0, due);
            Assert.That(due.Single().Kind, Is.EqualTo(EffectCues.Kind.Flash), "the hit's chips are any weapon's; no slash");
            cues.TakeDue(3.5, due);
            Assert.That(due.Single().Kind, Is.EqualTo(EffectCues.Kind.Slash));

            var s = Scheduler(new TestClock { Mixer = 0 });
            var p = new SoundPresenter(s, Beats(), new RoleLooks(null), 0.8f, null);
            p.OnEvent(wand);
            Assert.That(s.LogCount, Is.EqualTo(0), "no swing");
            p.OnEvent(wandHit);
            Assert.That(s.Log.Single().Song, Is.EqualTo(3.0), "the hit's sound is any weapon's");
            p.OnEvent(sword);
            Assert.That(s.Log.Count(e => e.Song == 3.5), Is.EqualTo(1), "the sword's swing");
        }

        [Test]
        public void TheCrystalWavesFrontIsExactlyWhereTheStrikeAndTheCleaveReach()
        {
            using var waves = new CrystalWaves(null, Shader.Find("Game/CrystalWave"));
            waves.Strike(3.0, Vector2.zero, 7.04f, heavy: false);
            Assert.That(waves.RadiusAt(0, 3.0), Is.EqualTo(0));
            Assert.That(waves.RadiusAt(0, 3.0 + CrystalWaves.RingSeconds), Is.EqualTo(7.04f).Within(1e-5));
            Assert.That(waves.RadiusAt(0, 3.5), Is.EqualTo(7.04f).Within(1e-5), "held while it fades");
            waves.Cleave(3.0, Vector2.zero, 9.15f, 15.07f, 7, 3.25, 0.125);
            float band = (15.07f - 9.15f) / 7;
            Assert.That(waves.RadiusAt(1, 3.0 + CrystalWaves.RingSeconds), Is.EqualTo(9.15f).Within(1e-5));
            for (int k = 0; k < 7; k++)
                Assert.That(waves.RadiusAt(1, 3.25 + k * 0.125), Is.EqualTo(Mathf.Min(9.15f + (k + 1) * band, 15.07f)).Within(1e-4));
            waves.Present(3.25 + 6 * 0.125 + CrystalWaves.FadeSeconds + 0.01);
            Assert.That(waves.Active, Is.EqualTo(0), "gone once faded");
        }
    }
}
