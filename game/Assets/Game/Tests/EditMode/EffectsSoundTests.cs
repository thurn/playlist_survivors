using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Game.Client.Effects;
using Game.Client.Loop;
using Game.Client.Sound;
using Game.Client.Weapons;
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
        public void ARegrowthShattersAtItsStartAsItSettlesNotWhenItsFrameShowsIt()
        {
            // The eruption's frame is past its song time, when no sound can make it: the event, a lead ahead, schedules it.
            var sim = Beats();
            var s = Scheduler(new TestClock { Mixer = 3.5 });
            var p = new SoundPresenter(s, sim, new RoleLooks(null), 0.8f, null);
            p.OnEvent(SimEvent.Regrowth(4.0, 0, 4.0, 4.5));
            var kinds = s.Log.Where(e => Math.Abs(e.Song - 4.0) < 1e-9).Select(e => e.Kind).ToList();
            CollectionAssert.AreEquivalent(new[] { SoundKind.ShatterBody, SoundKind.ShatterGlass }, kinds);
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
            // A missile weapon with no look of its own (no looks given here): none of the sword's stands in.
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
        public void GarlicAndFireTrailSoundTheirOwnPulsesAndHitsNotTheSwords()
        {
            var content = GameContent.Load(Path.Combine(Application.dataPath, "..", "Content"));
            int garlic = content.WeaponIndex("garlic"), trail = content.WeaponIndex("fire-trail");
            var sim = Beats();
            var s = Scheduler(new TestClock { Mixer = 0 });
            var sound = new SoundPresenter(s, sim, new RoleLooks(null), 0.8f, null);
            using var aura = new GarlicAura(null, sim, content, null, sound);
            using var wake = new FireTrailView(null, content, null, sound);
            var events = new[]
            {
                SimEvent.HeroAttack(3.0, garlic, ShapeKind.Aura, 0, 3.0, 3.0, 0, 0, false, 1.83),
                SimEvent.EnemyHit(3.0, 4, garlic, WeaponHit.Strike, 0.5, 1, 0, 1, 0),
                SimEvent.HeroAttack(3.5, trail, ShapeKind.Trail, 0, 3.5, 3.5, 0, 0, false, 0.74),
                SimEvent.Burn(3.5, trail, 2.25, 0.75, 0.74),
                SimEvent.EnemyHit(3.5, 5, trail, WeaponHit.Strike, 0.4, 2.25, 0.75, 1, 0),
            };
            foreach (var e in events)
            {
                sound.OnEvent(e);
                aura.OnEvent(e);
                wake.OnEvent(e);
            }
            var categories = _host.GetComponents<AudioSource>().Where(a => a.clip != null).Select(a => a.clip.name.Split('/')[0]).OrderBy(n => n);
            Assert.That(categories, Is.EqualTo(new[] { "fire-trail-hit", "fire-trail-pulse", "garlic-hit", "garlic-pulse" }), "no sword hit");
            Assert.That(s.Log.Select(e => e.Song), Is.EquivalentTo(new[] { 3.0, 3.0, 3.5, 3.5 }), "each at its event's song time");
        }

        [Test]
        public void TheWandAndTheBibleSoundTheirShotsRingsAndHitsNotTheSwords()
        {
            var content = GameContent.Load(Path.Combine(Application.dataPath, "..", "Content"));
            int wand = content.WeaponIndex("magic-wand"), bible = content.WeaponIndex("king-bible");
            var looks = new WeaponLooks(content.Weapons);
            var sim = Beats();
            var s = Scheduler(new TestClock { Mixer = 0 });
            var sound = new SoundPresenter(s, sim, new RoleLooks(null), 0.8f, null);
            using var bolts = new WandView(null, looks, null, sound);
            using var rings = new BibleView(null, looks, null, sound);
            var events = new[]
            {
                SimEvent.HeroAttack(3.0, wand, ShapeKind.Missile, 0, 3.0, 3.0, 1, 0, false, 0.46),
                SimEvent.Projectile(3.0, wand, 0, ProjectilePhase.Launch, 4, 0, 0, 4, 0, 3.5, 0.46),
                SimEvent.Projectile(3.1, wand, 1, ProjectilePhase.Launch, 4, 0, 0, 4, 0, 3.5, 0.46),
                SimEvent.EnemyHit(3.5, 4, wand, WeaponHit.Strike, 1, 4, 0, 1, 0),
                SimEvent.Projectile(3.5, wand, 0, ProjectilePhase.End, -1, 4, 0, 4, 0, 3.5, 0.46),
                SimEvent.HeroAttack(3.25, bible, ShapeKind.Orbit, 0, 3.25, 3.25, 1, 0, false, 3.4),
                SimEvent.Orbit(3.25, bible, 0, 2, true, 3.25, 6.25, 3.14, 2.86, 0.59),
                SimEvent.EnemyHit(3.75, 5, bible, WeaponHit.Strike, 1, 2, 2, 0, 1),
                SimEvent.Orbit(4.0, bible, 0, 2, true, 3.25, 4.0, 3.14, 2.86, 0.59),
            };
            foreach (var e in events)
            {
                sound.OnEvent(e);
                bolts.OnEvent(e);
                rings.OnEvent(e);
            }
            var categories = _host.GetComponents<AudioSource>().Where(a => a.clip != null).Select(a => a.clip.name.Split('/')[0]).OrderBy(n => n);
            Assert.That(categories, Is.EqualTo(new[] { "bible-fire", "bible-hit", "wand-fire", "wand-hit" }), "no sword hit");
            Assert.That(s.Log.Select(e => Math.Round(e.Song, 6)), Is.EquivalentTo(new[] { 3.0, 3.5, 3.25, 3.75 }),
                "a shot's fire (not its burst's next bolt), a hit, a ring's birth (not its cut) and its hit, each at its time");
            Assert.That(bolts.Count, Is.EqualTo(2));
            Assert.That(rings.Count, Is.EqualTo(1), "a ring cut short is the same ring");

            var cues = new EffectCues(new RoleLooks(null));
            var due = new List<EffectCues.Cue>();
            cues.Spark(3.0, 7, new Vector3(1, 1.05f, 2), Vector2.right, 0.7f, WeaponLooks.Wand);
            cues.TakeDue(2.99, due);
            Assert.That(due, Is.Empty);
            cues.TakeDue(3.0, due);
            var spark = due.Single();
            Assert.That((spark.Kind, spark.Effect, spark.At, spark.Reach), Is.EqualTo((EffectCues.Kind.Spark, 7, new Vector2(1, 2), 1.05f)));
            Assert.That(spark.Tint, Is.EqualTo(WeaponLooks.Wand));
        }

        [Test]
        public void TheLightningSoundsThunderAZapClimbingItsChainAndACrackleForEachHitNotTheSwords()
        {
            var content = GameContent.Load(Path.Combine(Application.dataPath, "..", "Content"));
            int ring = content.WeaponIndex("lightning-ring"), loop = content.WeaponIndex("thunder-loop"), chain = content.WeaponIndex("chain-lightning");
            var looks = new WeaponLooks(content.Weapons);
            var s = Scheduler(new TestClock { Mixer = 0 });
            var sound = new SoundPresenter(s, Beats(), new RoleLooks(null), 0.8f, null);
            using var view = new LightningView(null, looks, null, sound);
            var events = new List<SimEvent>
            {
                SimEvent.Bolt(3.0, ring, BoltKind.Strike, 0, 1, 1, 1, 1, 0.46),
                SimEvent.EnemyHit(3.0, 4, ring, WeaponHit.Bolt, 1.5, 1, 1, 0, 1),
                SimEvent.Bolt(4.5, loop, BoltKind.Return, 0, 1, 1, 1, 1, 0.46),
            };
            for (int j = 0; j < 3; j++) events.Add(SimEvent.Bolt(5.0 + 0.125 * j, chain, BoltKind.Jump, j, 0, 0, 1, 1, 0.59));
            foreach (var e in events)
            {
                sound.OnEvent(e);
                view.OnEvent(e);
            }
            var played = _host.GetComponents<AudioSource>().Where(a => a.clip != null)
                .Select(a => (category: a.clip.name.Split('/')[0], a.pitch)).ToList();
            Assert.That(played.Where(x => x.category == "lightning-strike").Select(x => x.pitch), Is.EquivalentTo(new[] { 1f, 0.8f }),
                "a strike's thunder, Thunder Loop's return lower");
            Assert.That(played.Where(x => x.category == "lightning-jump").Select(x => x.pitch), Is.EquivalentTo(new[] { 1f, 9f / 8, 5f / 4 }),
                "the chain's jumps up the pentatonic");
            Assert.That(played.Count(x => x.category == "lightning-hit"), Is.EqualTo(1));
            Assert.That(played.Any(x => x.category == "hit"), Is.False, "no sword hit for a bolt");
            Assert.That(view.Count, Is.EqualTo(5));
            Assert.That((looks.Colour(ring), looks.Colour(loop), looks.Colour(chain)),
                Is.EqualTo((WeaponLooks.Ring, WeaponLooks.Thunder, WeaponLooks.Chain)), "Thunder Loop's distinct layer");
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

        [Test]
        public void AChestSoundsAsItIsTakenOnEachEighthAndAtItsLandingAndAnEvolutionOnceALanding()
        {
            var content = GameContent.Load(Path.Combine(Application.dataPath, "..", "Content"));
            var looks = new WeaponLooks(content.Weapons);
            var sim = Beats();
            var s = Scheduler(new TestClock { Mixer = 0 });
            var sound = new SoundPresenter(s, sim, new RoleLooks(null), 0.8f, null);
            using var chests = new Game.Client.Pickups.ChestView(null, sim, content, looks, null, sound);
            using var evolutions = new EvolutionLayers(null, sim, content, looks, null, sound);
            int soul = content.WeaponIndex("soul-eater"), holy = content.WeaponIndex("holy-wand"), wand = content.WeaponIndex("magic-wand");
            int Card(int weapon, int before) => (1 + weapon) | (before << 6);
            var events = new List<SimEvent>
            {
                SimEvent.Chest(2.0, 0, 1, 1, 3, 25),
                SimEvent.Chest(2.5, 0, ChestPhase.Open, 3, 25, 1, 1),
            };
            for (int k = 0; k < 8; k++) events.Add(SimEvent.Chest(3.0 + 0.25 * k, 0, ChestPhase.Tick, 3, k, 1, 1));
            events.Add(SimEvent.Chest(5.0, 0, ChestPhase.Land, 3, 3, 1, 1));
            events.Add(SimEvent.Chest(5.0, 0, ChestPhase.Reward, 3, Card(soul, 0), 1, 1));
            events.Add(SimEvent.Chest(5.0, 0, ChestPhase.Reward, 3, Card(holy, 0) | (1 << 10), 1, 1));
            events.Add(SimEvent.Chest(5.0, 0, ChestPhase.Reward, 3, Card(wand, 3) | (2 << 10), 1, 1));
            foreach (var e in events)
            {
                sound.OnEvent(e);
                chests.OnEvent(e);
                evolutions.OnEvent(e);
            }
            var categories = _host.GetComponents<AudioSource>().Where(a => a.clip != null).Select(a => a.clip.name.Split('/')[0]).ToList();
            Assert.That(categories.Count(c => c == "chest-open"), Is.EqualTo(1));
            Assert.That(categories.Count(c => c == "chest-tick"), Is.EqualTo(8), "two eighths a beat, under the aggregation's three");
            Assert.That(categories.Count(c => c == "chest-land"), Is.EqualTo(1));
            Assert.That(categories.Count(c => c == "evolve"), Is.EqualTo(1), "one transformation for the landing's two evolutions");
            var songs = s.Log.Select(e => Math.Round(e.Song, 6)).ToList();
            Assert.That(songs, Is.EquivalentTo(new[] { 2.5, 3.0, 3.25, 3.5, 3.75, 4.0, 4.25, 4.5, 4.75, 5.0, 5.0 }));
            Assert.That(chests.Count, Is.EqualTo(1));
            Assert.That(evolutions.Count, Is.EqualTo(6), "three arrival rings for each evolution");
            Assert.That(evolutions.Tint(soul), Is.EqualTo(GarlicAura.SoulEater));
            Assert.That(evolutions.Tint(holy), Is.EqualTo(WeaponLooks.Holy));
        }
    }
}
