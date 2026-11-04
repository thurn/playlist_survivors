using System;
using System.Linq;
using Champions.Client;
using Champions.Client.Loop;
using Champions.Client.Scenes;
using Champions.Client.Sound;
using Champions.Render.Crowd;
using Champions.Sim;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Champions.Tests
{
    /// <summary>
    /// The Boss Rush framework's client side (boss-rush.md, Look, effects, shaders and sound; ps-8z4.3): a fight's own
    /// sound bank (its telegraph ticking on the beat, a role it lacks falling back), the shared boss-look shader, the
    /// `--fight` launch flag and the review scenes' names.
    /// </summary>
    public sealed class BossRushTests
    {
        GameObject _host;
        SoundBank _bank;

        [TearDown]
        public void Clean()
        {
            if (_bank != null)
            {
                foreach (var c in _bank.Clips) Object.DestroyImmediate(c);
                Object.DestroyImmediate(_bank);
            }
            if (_host != null) Object.DestroyImmediate(_host);
        }

        /// <summary>A bank of silent clips: every category's, and the named boss's ticks and impacts (no signature).</summary>
        SoundScheduler Scheduler(FrameClock clock, string boss)
        {
            _host = new GameObject("Sounds");
            _bank = ScriptableObject.CreateInstance<SoundBank>();
            var kinds = (SoundKind[])Enum.GetValues(typeof(SoundKind));
            _bank.Names = kinds.SelectMany(k => Enumerable.Range(0, 3).Select(v => $"{SoundScheduler.Name(k)}/{v}"))
                .Concat(new[] { BossSound.Tick, BossSound.Impact }.SelectMany(r => Enumerable.Range(0, 2).Select(v => SoundScheduler.BossClip(boss, r, v))))
                .ToArray();
            _bank.Clips = _bank.Names.Select(n => AudioClip.Create(n, 4800, 1, 48000, false)).ToArray();
            _bank.Onsets = _bank.Names.Select(_ => 0f).ToArray();
            _bank.Levels = _bank.Names.Select(_ => 1f).ToArray();
            return new SoundScheduler(_bank, _host, clock, () => clock.Mixer, audible: false) { Logging = true };
        }

        [Test]
        public void AFightsTelegraphTicksOnEachOfItsBeatsFromTheBossesBank()
        {
            var content = GameContent.Load(GameContent.Find(Application.dataPath));
            var config = new RunInputs { Loadout = new Upgrade[0], Fight = OgreModule.ModuleId }.Build(content);
            config.Hero.Health = 1e9;
            var sim = new Simulation(config);
            foreach (var e in new ScriptedSong().Beats(0, 40, 120, allStrong: true).Entries()) sim.AddKnown(e, Math.Max(0, e.Stamp + 0.5));
            var clock = new FrameClock();
            var scheduler = Scheduler(clock, OgreModule.ModuleId);
            var sounds = new SoundPresenter(scheduler, sim, leapSlam: -1, limit: 1);
            int cursor = 0;
            SimEvent mark = default;
            for (double t = 0; t < 30 && mark.Kind == SimEventKind.None; t += 1.0 / 60)
            {
                sim.StepTo(t);
                foreach (var e in sim.EventsSince(cursor))
                {
                    sounds.Take(e);
                    if (e.Kind == SimEventKind.BossTelegraph && e.I0 >= BossFight.FirstMarkId && e.MarkPart == 0) mark = e;
                }
                cursor = sim.EventCount;
            }
            Assert.AreEqual(SimEventKind.BossTelegraph, mark.Kind, "the Ogre's fight laid a hazard");
            Assert.IsTrue(scheduler.Has(BossSound.Tick), "the fight's call bound its bank");
            Assert.IsFalse(scheduler.Has(BossSound.Signature));
            clock.Frame(new ClockReadings { Frame = mark.T - 0.5 });
            sounds.Present(mark.T - 0.5);
            var ticks = scheduler.Log.Where(x => x.Cue.Boss == BossSound.Tick).Select(x => x.Cue.Song).ToList();
            int beats = (int)Math.Round((mark.D0 - mark.T) / 0.5);
            Assert.AreEqual(beats, ticks.Count, "a tick on each of its beats");
            for (int i = 0; i < ticks.Count; i++) Assert.AreEqual(mark.T + i * 0.5, ticks[i], 0.02);
            // A role the bank lacks plays its generic sound instead.
            int h = scheduler.Play(new SoundCue { Kind = SoundKind.Slam, Boss = BossSound.Signature, Song = mark.T, Gain = 1, Pitch = 1, Asap = true }, 0, 1);
            Assert.That(h, Is.GreaterThanOrEqualTo(0));
        }

        [Test]
        public void TheBossLookShaderLoadsAndTakesALooksVariations()
        {
            var shader = Resources.Load<Shader>(BossLook.Shader);
            Assert.IsNotNull(shader, BossLook.Shader);
            var m = new Material(shader);
            try
            {
                new BossLook { Scan = 0.8f, Flow = 0.5f, Ghost = 0.3f, Ember = 1, EmberTint = new Color(1f, 0.3f, 0.05f) }.Apply(m);
                Assert.AreEqual((0.8f, 0.5f, 0.3f, 1f), (m.GetFloat("_Scan"), m.GetFloat("_Flow"), m.GetFloat("_Ghost"), m.GetFloat("_Ember")));
                Assert.AreEqual(0f, m.GetFloat("_Crystal"), "an unused variation stays off");
                Assert.IsEmpty(m.shaderKeywords, "floats, not keywords: no variants for the budget");
            }
            finally { Object.DestroyImmediate(m); }
        }

        [Test]
        public void FightLaunchesAndReviewsNameARegisteredBoss()
        {
            var o = LaunchOptions.Parse(new[] { "player", "--fight", OgreModule.ModuleId, "--fight-act", "3" }, batchMode: true);
            Assert.AreEqual((OgreModule.ModuleId, 3), (o.Fight, o.FightAct));
            Assert.IsEmpty(o.Unrecognized);
            Assert.Throws<ArgumentException>(() => LaunchOptions.Parse(new[] { "player", "--fight", "noSuchBoss" }, batchMode: true));
            Assert.AreEqual(OgreModule.ModuleId, FightScene.BossOf("fight-" + OgreModule.ModuleId, out bool motion));
            Assert.IsFalse(motion);
            Assert.AreEqual(OgreModule.ModuleId, FightScene.BossOf("fight-" + OgreModule.ModuleId + "-motion", out motion));
            Assert.IsTrue(motion);
            Assert.IsNull(FightScene.BossOf("fight-noSuchBoss", out _));
        }
    }
}
