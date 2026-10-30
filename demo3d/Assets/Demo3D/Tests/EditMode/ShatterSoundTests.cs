using System.Collections.Generic;
using System.Linq;
using Demo3D.Runtime.Crowd;
using Demo3D.Runtime.Playback;
using NUnit.Framework;
using PS.Choreography;

namespace Demo3D.Tests
{
    /// <summary>The crystal shatter's sounds (epic ps-2iq): layered on each release beat, aggregated so a crowded beat stays a few voices.</summary>
    public class ShatterSoundTests
    {
        static CrystalShatter.ReleaseEvent R(double t, int entity, int type, float height) =>
            new CrystalShatter.ReleaseEvent { Time = t, Entity = entity, Type = type, Height = height, Seed = (uint)(entity * 7919) };

        [Test]
        public void ALoneDeathLayersABreakASprayAndALaterTinkle()
        {
            var cues = ShatterSounds.For(new[] { R(10.0, 1, 0, 1.8f) }, SongMap.Synthetic(120, 20));
            Assert.That(cues.Select(c => c.Name.Split('/')[0]), Is.EqualTo(ShatterSounds.Layers));
            Assert.That(cues[0].Time, Is.EqualTo(10.0));
            Assert.That(cues[1].Time, Is.EqualTo(10.0));
            Assert.That(cues[2].Time, Is.InRange(10.25, 10.55), "the shards land");
            Assert.That(cues[2].Gain, Is.LessThan(cues[0].Gain), "the tinkle is quieter");
            Assert.That(cues[0].Pitch, Is.EqualTo(1f).Within(0.05f), "a 1.8 m skeleton near its own pitch");
        }

        [Test]
        public void ACrowdedBeatPlaysAFewVoicesOfItsDistinctTypesLouder()
        {
            var releases = new List<CrystalShatter.ReleaseEvent>();
            for (int i = 0; i < 20; i++) releases.Add(R(10.0, 10 + i, 0, 1.8f)); // skeletons
            releases.Add(R(10.0, 40, 1, 3.2f));                                     // a troll
            releases.Add(R(10.0, 41, 2, 0.7f));                                     // a slime
            releases.Add(R(10.5, 50, 0, 1.8f));                                     // the next beat
            var cues = ShatterSounds.For(releases.OrderBy(r => r.Time).ToList(), SongMap.Synthetic(120, 20));
            var beat = cues.Where(c => c.Time < 10.5 && c.Name.StartsWith("shatter-body")).ToList();
            Assert.That(beat.Count, Is.EqualTo(ShatterSounds.MaxVoices));
            Assert.That(beat.Select(c => c.Pitch).Distinct().Count(), Is.EqualTo(3), "the troll, the skeleton and the slime each ring at their own pitch");
            Assert.That(beat.Min(c => c.Pitch), Is.LessThan(0.95f), "the troll lower");
            Assert.That(beat.Max(c => c.Pitch), Is.GreaterThan(1.2f), "the slime higher");
            var lone = ShatterSounds.For(new[] { R(10.0, 1, 0, 1.8f) }, SongMap.Synthetic(120, 20));
            Assert.That(beat[0].Gain, Is.GreaterThan(lone[0].Gain), "22 deaths are louder than one");
            Assert.That(beat[0].Gain, Is.LessThanOrEqualTo(1f));
            Assert.That(cues.Count(c => c.Time >= 10.5 && c.Time < 10.6 && c.Name.StartsWith("shatter-body")), Is.EqualTo(1), "the next beat is its own");
            for (int i = 1; i < cues.Count; i++) Assert.That(cues[i].Time, Is.GreaterThanOrEqualTo(cues[i - 1].Time), "sorted by time");
        }
    }
}
