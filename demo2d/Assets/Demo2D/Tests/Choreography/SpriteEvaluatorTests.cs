using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using PS.Choreography;

namespace Demo2D.Choreography.Tests
{
    /// <summary>PX5: the client's frames for the song's timeline (the stills' checks are PlaybackTests).</summary>
    public class SpriteEvaluatorTests
    {
        static SpriteEvaluator evaluator;
        static int frameRate;

        static Timeline Timeline => Fixtures.SongTimeline;
        static ClipLibrary Clips => Fixtures.Data.Clips;

        /// <summary>The song's sprite evaluation, with each clip's frame count from the tracked annotations (no bake needed).</summary>
        static SpriteEvaluator Evaluator()
        {
            if (evaluator != null) return evaluator;
            var json = JsonValue.Parse(File.ReadAllText(ChoreographyFiles.DataPath(Fixtures.Root, ChoreographyFiles.MeasuredAnnotationsFile)));
            var counts = json["annotations"].Items.ToDictionary(a => a["use"].AsString(), a => a["frames"].AsInt());
            var frames = Clips.Clips.Select(c => counts[c.Use]).ToArray();
            var walk = Array.ConvertAll(Timeline.TypeNames, n => Clips.Require(n + " walk"));
            frameRate = json["frameRate"].AsInt();
            evaluator = new SpriteEvaluator(Timeline, Clips, frames, frameRate, walk, Clips.Require("hero walk"), Clips.Find("hero idle"));
            return evaluator;
        }

        /// <summary>
        /// Every blow, flinch and death shows its clip's impact frame (the blow, the flinch's or
        /// the fall's onset) from its impact on the beat grid, unless a later clip of the same
        /// entity has already cut in. A killing blow shows the death.
        /// </summary>
        [Test]
        public void EveryBlowFlinchAndDeathShowsItsImpactFrameOnItsBeat()
        {
            var eval = Evaluator();
            int landed = 0, cut = 0, killing = 0;
            var wrong = new List<string>();
            for (int i = 0; i < Timeline.Actions.Length; i++)
            {
                var a = Timeline.Actions[i];
                if (a.Clip < 0 || (a.Kind != ActionKind.Attack && a.Kind != ActionKind.Hit && a.Kind != ActionKind.Death)) continue;
                var st = eval.Evaluate(a.Entity, a.Impact);
                if (a.Kind == ActionKind.Hit && double.IsNaN(eval.Schedule.Start(i)))
                {
                    Assert.That(Timeline.Actions.Any(d => d.Entity == a.Entity && d.Kind == ActionKind.Death && d.Impact == a.Impact && d.Clip == st.Clip),
                        "a killing blow shows the death");
                    killing++;
                    continue;
                }
                int impactFrame = (int)Math.Round(Clips[a.Clip].Impact * frameRate);
                if (st.Clip == a.Clip && st.Frame == impactFrame) { landed++; continue; }
                if (eval.Schedule.Interrupted(i)) cut++;
                else if (wrong.Count < 10) wrong.Add($"{a.Kind} of {a.Entity} at {a.Impact:F3}: clip {st.Clip} frame {st.Frame}, want {a.Clip} frame {impactFrame}");
            }
            Assert.That(wrong, Is.Empty);
            Assert.That(landed, Is.GreaterThan(19 * cut), $"{landed} landed, {cut} cut in by a later clip");
            Assert.That(landed, Is.GreaterThan(1000));
            Assert.Pass($"{landed} landed, {cut} cut in, {killing} killing blows");
        }

        /// <summary>A hit holds its frame through the hit-stop, then the flinch plays on.</summary>
        [Test]
        public void AStruckMonsterHoldsItsFrameThroughTheHitStopAndFlashes()
        {
            var eval = Evaluator();
            var hit = Timeline.Actions.First(a => a.Kind == ActionKind.Hit && a.Clip >= 0 && Clips[a.Clip].End > 0.3f &&
                                                  Timeline.Actions.All(b => b.Entity != a.Entity || b.Impact <= a.Impact || b.Impact > a.Impact + 0.5));
            var at = eval.Evaluate(hit.Entity, hit.Impact);
            var held = eval.Evaluate(hit.Entity, hit.Impact + ClipSchedule.HitStop * 0.9);
            var later = eval.Evaluate(hit.Entity, hit.Impact + 0.3);
            Assert.That(held.Frame, Is.EqualTo(at.Frame));
            Assert.That(later.Frame, Is.GreaterThan(at.Frame));
            Assert.That(at.Flash, Is.EqualTo(1f));
            Assert.That(later.Flash, Is.EqualTo(0f));
        }

        [Test]
        public void ADeadMonsterFadesOutAfterItsClipAndABeat()
        {
            var eval = Evaluator();
            var death = Timeline.Actions.First(a => a.Kind == ActionKind.Death && a.Clip >= 0);
            var end = Timeline.Entities[death.Entity].Despawn;
            Assert.That(eval.Evaluate(death.Entity, death.Impact).Alpha, Is.EqualTo(1f));
            Assert.That(eval.Evaluate(death.Entity, death.Impact).Dying, Is.True);
            Assert.That(eval.Evaluate(death.Entity, end - 1e-3).Alpha, Is.LessThan(0.1f), "faded by its despawn");
        }
    }
}
