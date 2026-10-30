using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using PS.Choreography;

namespace Demo2D.Choreography.Tests
{
    /// <summary>PX7: the effect library (Data/effects.json) against the song's timeline and the manifest.</summary>
    public class EffectLibraryTests
    {
        static EffectLibrary Library => EffectLibrary.Parse(File.ReadAllText(ChoreographyFiles.DataPath(Fixtures.Root, EffectLibrary.File)));

        [Test]
        public void TheLibraryHasAnEntryForEveryEffectKindAndProjectileAndNoOther()
        {
            var tl = Fixtures.SongTimeline;
            var problems = Library.Problems(tl.EffectNames, tl.ProjectileNames);
            Assert.That(problems, Is.Empty, string.Join("\n", problems));
        }

        [Test]
        public void EveryEffectKindAndProjectileFiresInTheSong()
        {
            var tl = Fixtures.SongTimeline;
            var counts = new int[tl.EffectNames.Length];
            var first = Enumerable.Repeat(double.NaN, counts.Length).ToArray();
            foreach (var e in tl.Effects)
            {
                if (counts[e.Effect]++ == 0) first[e.Effect] = e.Time;
            }
            var flights = new int[tl.ProjectileNames.Length];
            foreach (var info in tl.Entities) if (info.Kind == EntityKind.Projectile) flights[info.Type]++;
            for (int k = 0; k < counts.Length; k++) TestContext.WriteLine($"{tl.EffectNames[k]}: {counts[k]}, first at {first[k]:F2} s");
            for (int k = 0; k < flights.Length; k++) TestContext.WriteLine($"{tl.ProjectileNames[k]} in flight: {flights[k]}");
            Assert.That(Enumerable.Range(0, counts.Length).Where(k => counts[k] == 0).Select(k => tl.EffectNames[k]), Is.Empty, "kinds that never fire");
            Assert.That(Enumerable.Range(0, flights.Length).Where(k => flights[k] == 0).Select(k => tl.ProjectileNames[k]), Is.Empty, "projectiles never fired");
        }

        [Test]
        public void EveryPrefabTheLibraryNamesIsInTheManifest()
        {
            var manifest = JsonValue.Parse(File.ReadAllText(Path.Combine(Fixtures.Root, "demo2d", "manifest.json")));
            var paths = new HashSet<string>(manifest["assets"].Items.Select(a => a["path"].AsString()), StringComparer.Ordinal);
            Assert.That(Library.Prefabs().Where(p => !paths.Contains(p)), Is.Empty);
        }

        [Test]
        public void ALightCueRisesThenDecaysToNothing()
        {
            var cue = new LightCue { Intensity = 2, AttackS = 0.1f, DecayS = 0.4f };
            Assert.That(cue.At(-0.01), Is.Zero);
            Assert.That(cue.At(0.05), Is.EqualTo(1).Within(1e-5));
            Assert.That(cue.At(0.1), Is.EqualTo(2).Within(1e-5));
            Assert.That(cue.At(0.3), Is.EqualTo(0.5).Within(1e-5));
            Assert.That(cue.At(0.5), Is.EqualTo(0).Within(1e-6));
            Assert.That(new LightCue { Intensity = 0.7f }.At(3), Is.EqualTo(0.7f), "a flight holds its light");
        }
    }
}
