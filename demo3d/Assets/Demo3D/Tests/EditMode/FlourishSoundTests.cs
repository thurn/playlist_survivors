using System.Collections.Generic;
using System.Linq;
using Demo3D.Runtime.Crowd;
using Demo3D.Runtime.Playback;
using NUnit.Framework;
using PS.Choreography;

namespace Demo3D.Tests
{
    /// <summary>The look's death flourish and spawn warp sounds (ps-wnj.16): on their events, a crowded beat a few voices.</summary>
    public class FlourishSoundTests
    {
        static LookCrowd.BreakEvent B(double t, int entity, float height = 1.8f) =>
            new LookCrowd.BreakEvent { Time = t, Entity = entity, Height = height, Seed = (uint)(entity * 7919) };

        static LookCrowd.WarpEvent W(double t, int entity) => new LookCrowd.WarpEvent { Time = t, Entity = entity, Height = 1.8f };

        [Test]
        public void EachBreakPopsOnItsBlowAndAWarpsSwellPeaksOnItsSpawn()
        {
            var cues = FlourishSounds.For(new[] { B(10.0, 1) }, new[] { W(11.0, 2) }, SongMap.Synthetic(120, 20));
            var pop = cues.Single(c => c.Name.StartsWith(FlourishSounds.Pop));
            var warp = cues.Single(c => c.Name.StartsWith(FlourishSounds.Warp));
            Assert.That(pop.Time, Is.EqualTo(10.0), "on the killing blow");
            Assert.That(pop.Pitch, Is.EqualTo(1f).Within(0.05f));
            Assert.That(warp.Time, Is.EqualTo(11.0 - FlourishSounds.WarpLeadIn).Within(1e-9), "started early by the swell's lead-in, its attack on the spawn");
        }

        [Test]
        public void ACrowdedBeatPlaysAtMostThreeLouderVoices()
        {
            var breaks = new List<LookCrowd.BreakEvent>();
            for (int i = 0; i < 30; i++) breaks.Add(B(10.0, i));
            breaks.Add(B(10.5, 99)); // the next beat
            var cues = FlourishSounds.For(breaks, new LookCrowd.WarpEvent[0], SongMap.Synthetic(120, 20));
            var crowded = cues.Where(c => c.Time < 10.5).ToList();
            Assert.That(crowded.Count, Is.EqualTo(ShatterSounds.MaxVoices));
            Assert.That(crowded[0].Gain, Is.GreaterThan(cues.Single(c => c.Time == 10.5).Gain), "louder for the crowd");
            Assert.That(FlourishSounds.Names(), Is.SupersetOf(cues.Select(c => c.Name).Distinct()), "every cue is a sound in the bank");
        }
    }
}
