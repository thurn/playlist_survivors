using System;
using System.Linq;
using Champions.Client.Loop;
using Champions.Client.Sound;
using Champions.Sim;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Champions.Tests
{
    /// <summary>
    /// The hero's sounds (design.md, Sound) on a silent scheduler over the frames' clock, with a bank of silent clips:
    /// sounds start the moment the sim knows them, a dash's in heard time, an on-beat dash with its accent, and one beat's
    /// kills share its voices.
    /// </summary>
    public sealed class SoundTests
    {
        const double Lead = FrameClock.Margin; // how far ahead of the mixer the frame's events settle

        GameObject _host;
        SoundBank _bank;
        FrameClock _clock;
        SoundScheduler _scheduler;
        SoundPresenter _sounds;
        Simulation _sim;

        [SetUp]
        public void Make()
        {
            _host = new GameObject("Sounds");
            _bank = ScriptableObject.CreateInstance<SoundBank>();
            var kinds = (SoundKind[])Enum.GetValues(typeof(SoundKind));
            _bank.Names = kinds.SelectMany(k => Enumerable.Range(0, 3).Select(v => $"{SoundScheduler.Name(k)}/{v}")).ToArray();
            _bank.Clips = _bank.Names.Select(n => AudioClip.Create(n, 4800, 1, 48000, false)).ToArray();
            _bank.Onsets = _bank.Names.Select(_ => 0.02f).ToArray();
            _bank.Levels = _bank.Names.Select(_ => 1f).ToArray();
            _clock = new FrameClock();
            _scheduler = new SoundScheduler(_bank, _host, _clock, () => _clock.Mixer, audible: false) { Logging = true };
            _sim = new Simulation(new RunInputs().Build(null));
            _sounds = new SoundPresenter(_scheduler, _sim, leapSlam: 2, limit: _bank.Limit);
        }

        [TearDown]
        public void Clean()
        {
            foreach (var c in _bank.Clips) Object.DestroyImmediate(c);
            Object.DestroyImmediate(_bank);
            Object.DestroyImmediate(_host);
        }

        /// <summary>Brings the frames' clock to Unity frame time <paramref name="frame"/>; its frontier.</summary>
        double Frame(double frame)
        {
            var t = _clock.Frame(new ClockReadings { Frame = frame });
            return t.Frontier;
        }

        static SimEvent Dash(double settled, double start, bool onBeat, int kind = 0) =>
            SimEvent.HeroDash(settled, 0, kind, 1, onBeat ? JudgedBy.Beat : JudgedBy.Off, false, start, start + 0.15, 0, 0, 1, 0);

        [Test]
        public void EverySoundCategoryHasClipsInTheSelection()
        {
            var s = SoundSelection.Load(GameContent.Find(Application.dataPath));
            foreach (SoundKind k in Enum.GetValues(typeof(SoundKind)))
                Assert.That(s.Categories[(int)k].Clips, Is.Not.Empty, SoundScheduler.Name(k));
            Assert.That(s.Limit, Is.InRange(0.1f, 1f), "under the music");
        }

        [Test]
        public void ASwingsWhooshLandsOnItsImpact()
        {
            _sounds.Present(Frame(1.0));
            _sounds.Take(SimEvent.Swing(1.0 + Lead, 1.4, 1, 0, spin: false, rank: 1));
            _sounds.Take(SimEvent.Swing(1.0 + Lead, 2.4, 1, 0, spin: true, rank: 3));
            Assert.That(_scheduler.Log.Select(x => (x.Cue.Kind, x.Attack)),
                Is.EqualTo(new[] { (SoundKind.Swing, 1.4), (SoundKind.SwingHeavy, 2.4) }), "attacks on the impacts, a spin heavier");
        }

        [Test]
        public void ADashWhooshesFromItsPressWithTheAccentOnTheBeat()
        {
            _sounds.Present(Frame(1.0));
            _sounds.Take(Dash(1.0 + Lead, 1.0 + Lead, onBeat: false));
            _sounds.Take(Dash(1.5 + Lead, 1.5 + Lead, onBeat: true));
            var log = _scheduler.Log.Select(x => (x.Cue.Kind, x.Attack)).ToArray();
            Assert.That(log, Is.EqualTo(new[] { (SoundKind.Whoosh, 1.0 + Lead), (SoundKind.Whoosh, 1.5 + Lead), (SoundKind.Accent, 1.5 + Lead) }));
            Assert.That(_scheduler.Log[0].Cue.Gain, Is.LessThan(_scheduler.Log[1].Cue.Gain), "off the beat a little quieter");
        }

        [Test]
        public void AHeardTimeSoundTheMixerIsPastStartsAtOnceAPlannedOneIsDropped()
        {
            // A press heard at 0.99 s settles a frame later, its time behind the previous frame's frontier.
            _sounds.Present(Frame(1.0));
            Frame(1.0 + 1 / 60.0);
            _sounds.Take(Dash(1.03, 0.99, onBeat: true));
            Assert.That(_scheduler.Late, Is.EqualTo(2), "the whoosh and its accent, late but heard");
            Assert.That(_scheduler.Log.All(x => Math.Abs(x.Attack - _clock.EarliestDsp) < 1e-9), "as soon as the mixer allows");
            _sounds.Take(SimEvent.Swing(1.03, 0.99, 1, 0, spin: false, rank: 1));
            Assert.That(_scheduler.Dropped, Is.EqualTo(0), "settled before the frontier: a heard-time sound too");
            _sounds.Present(Frame(2.0));
            _sounds.Request(SoundKind.Hit, 1.5, 1, 1, 1);
            Assert.That(_scheduler.Dropped, Is.EqualTo(1), "a sound the mixer is past and nobody heard: dropped, never late");
        }

        [Test]
        public void LeapSlamSlamsAsItLands()
        {
            _sounds.Present(Frame(1.0));
            _sounds.Take(Dash(1.0 + Lead, 1.0 + Lead, onBeat: false, kind: 2));
            Assert.That(_scheduler.Log.Last().Cue.Kind, Is.EqualTo(SoundKind.Slam));
            Assert.That(_scheduler.Log.Last().Attack, Is.EqualTo(1.15 + Lead).Within(1e-9), "at the dash's end");
        }

        [Test]
        public void OneInstantsKillsShareThreeVoicesAndTheirTinklesFollow()
        {
            _sounds.Present(Frame(1.0));
            for (int i = 0; i < 12; i++) _sounds.Take(SimEvent.Kill(1.2, i, BlowSource.SwordSlash, BlowFlags.None, 1, 0, 1, i, 0));
            Assert.That(_scheduler.ScheduledOf(SoundKind.ShatterBody), Is.EqualTo(SoundAggregator.MaxVoices));
            Assert.That(_sounds.Aggregator.Folded, Is.GreaterThan(0), "the rest raise the beat's gain");
            Assert.That(_scheduler.Log.Where(x => x.Cue.Kind == SoundKind.ShatterTinkle).All(x => x.Attack > 1.2), "the shards land after");
        }

        [Test]
        public void ABossThudsAsItsTelegraphStartsSlamsOnItsBeatAndShattersBig()
        {
            _sounds.Present(Frame(1.0));
            var mark = new BossMark { Id = 3, Move = BossMove.Smash, Telegraph = new Telegraph(1.2, 2.2, -1, -1), Shape = HitShape.Circle(0, 0, 1) };
            _sounds.Take(SimEvent.BossTelegraph(mark));
            _sounds.Take(SimEvent.BossMove(2.2, mark, false, 0, 0, 0, 0, 0, 0));
            _sounds.Take(SimEvent.BossMove(2.7, mark, true, 0, 0, 0, 0, 0, 0));
            _sounds.Take(SimEvent.BossDeath(3.0, 4, 8, BossDeath.Treasure, 0, 0, 1, 0));
            var log = _scheduler.Log.Select(x => (x.Cue.Kind, x.Attack)).ToArray();
            Assert.That(log.Take(2), Is.EqualTo(new[] { (SoundKind.Accent, 1.2), (SoundKind.Slam, 2.2) }), "a call-off is silent");
            Assert.That(_scheduler.Log[0].Cue.Gain, Is.LessThan(_scheduler.Log[1].Cue.Gain), "the thud under the slam");
            Assert.That(_scheduler.Log.Count(x => x.Cue.Kind == SoundKind.ShatterBody && x.Attack == 3.0), Is.EqualTo(1));
            Assert.That(_scheduler.Log.Last().Cue.Pitch, Is.LessThan(1f), "a big body rings low");
        }

        [Test]
        public void TheHurtSoundsOnlyWhenTheHitLands()
        {
            _sounds.Present(Frame(1.0));
            _sounds.Take(SimEvent.HeroHit(1.2, HitOutcome.Dodged, HitSource.Touch, 3, 0, 100, 1, 0, 0));
            _sounds.Take(SimEvent.HeroHit(1.3, HitOutcome.Hurt, HitSource.Touch, 3, 10, 90, 1, 0, -1));
            Assert.That(_scheduler.Log.Select(x => (x.Cue.Kind, x.Attack)), Is.EqualTo(new[] { (SoundKind.Hurt, 1.3) }));
        }
    }
}
