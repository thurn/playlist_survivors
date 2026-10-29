using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Demo2D.Choreography;
using Demo2D.Runtime.Playback;
using NUnit.Framework;
using PS.Choreography;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Demo2D.Tests
{
    /// <summary>PX5: the client's playback of the song's timeline.</summary>
    public class PlaybackTests
    {
        static Timeline timeline;
        static SpriteEvaluator evaluator;
        static ClipLibrary clips;

        /// <summary>The song's timeline and its sprite evaluation, from the tracked data (no bake needed).</summary>
        static SpriteEvaluator Evaluator()
        {
            if (evaluator != null) return evaluator;
            string root = ChoreographyFiles.FindRepoRoot();
            var level = ChoreographyFiles.LoadLevel(root);
            var data = ChoreographyFiles.LoadData(root, level.Layout);
            timeline = Choreographer.Run(ChoreographyFiles.LoadSong(root, level.Song), level, data);
            clips = data.Clips;
            var json = JsonValue.Parse(File.ReadAllText(ChoreographyFiles.DataPath(root, ChoreographyFiles.MeasuredAnnotationsFile)));
            var counts = json["annotations"].Items.ToDictionary(a => a["use"].AsString(), a => a["frames"].AsInt());
            var frames = clips.Clips.Select(c => counts[c.Use]).ToArray();
            var walk = Array.ConvertAll(timeline.TypeNames, n => clips.Require(n + " walk"));
            evaluator = new SpriteEvaluator(timeline, clips, frames, json["frameRate"].AsInt(), walk, clips.Require("hero walk"), clips.Find("hero idle"));
            return evaluator;
        }

        /// <summary>
        /// Every blow, flinch and death shows its clip's impact frame (the blow, the flinch's or
        /// the fall's onset) from its impact on the beat grid, unless a later clip of the same
        /// entity has already cut in.
        /// </summary>
        [Test]
        public void EveryBlowFlinchAndDeathShowsItsImpactFrameOnItsBeat()
        {
            var eval = Evaluator();
            int landed = 0, cut = 0;
            var wrong = new List<string>();
            for (int i = 0; i < timeline.Actions.Length; i++)
            {
                var a = timeline.Actions[i];
                if (a.Clip < 0 || (a.Kind != ActionKind.Attack && a.Kind != ActionKind.Hit && a.Kind != ActionKind.Death)) continue;
                if (a.Kind == ActionKind.Hit && timeline.Actions.Any(d => d.Entity == a.Entity && d.Kind == ActionKind.Death && d.Impact == a.Impact))
                {
                    var fall = eval.Evaluate(a.Entity, a.Impact);
                    Assert.That(timeline.Actions.Any(d => d.Entity == a.Entity && d.Kind == ActionKind.Death && d.Clip == fall.Clip), "a killing blow shows the death");
                    continue;
                }
                var st = eval.Evaluate(a.Entity, a.Impact);
                int impactFrame = Mathf.RoundToInt(clips[a.Clip].Impact * 15);
                if (st.Clip == a.Clip && st.Frame == impactFrame) { landed++; continue; }
                bool cutIn = false;
                var acts = timeline.ActionsOf(a.Entity);
                for (int k = 0; k < acts.Count && !cutIn; k++)
                {
                    int j = acts.Offset + k;
                    var b = timeline.Actions[j];
                    cutIn = j != i && b.Clip >= 0 && b.Kind != ActionKind.Tick && b.Kind != ActionKind.Spawn &&
                            eval.ClipStart(j) > eval.ClipStart(i) && eval.ClipStart(j) <= a.Impact;
                }
                if (cutIn) cut++;
                else if (wrong.Count < 10) wrong.Add($"{a.Kind} of {a.Entity} at {a.Impact:F3}: clip {st.Clip} frame {st.Frame}, want {a.Clip} frame {impactFrame}");
            }
            Assert.That(wrong, Is.Empty);
            Assert.That(landed, Is.GreaterThan(19 * cut), $"{landed} landed, {cut} cut in by a later clip");
        }

        /// <summary>A hit holds its frame through the hit-stop, then the flinch plays on.</summary>
        [Test]
        public void AStruckMonsterHoldsItsFrameThroughTheHitStopAndFlashes()
        {
            var eval = Evaluator();
            var hit = timeline.Actions.First(a => a.Kind == ActionKind.Hit && a.Clip >= 0 && clips[a.Clip].End > 0.3f &&
                                                  timeline.Actions.All(b => b.Entity != a.Entity || b.Impact <= a.Impact || b.Impact > a.Impact + 0.5));
            var at = eval.Evaluate(hit.Entity, hit.Impact);
            var held = eval.Evaluate(hit.Entity, hit.Impact + SpriteEvaluator.HitStop * 0.9);
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
            var death = timeline.Actions.First(a => a.Kind == ActionKind.Death && a.Clip >= 0);
            var end = timeline.Entities[death.Entity].Despawn;
            Assert.That(eval.Evaluate(death.Entity, death.Impact).Alpha, Is.EqualTo(1f));
            Assert.That(eval.Evaluate(death.Entity, death.Impact).Dying, Is.True);
            Assert.That(eval.Evaluate(death.Entity, end - 1e-3).Alpha, Is.LessThan(0.1f), "faded by its despawn");
        }

        // ---- On stills ----

        static PlaybackData BakedData()
        {
            var data = Resources.Load<PlaybackData>(PlaybackData.ResourcePath);
            if (data == null) Assert.Ignore("PlaybackData isn't baked: run demo2d unity BakePlayback");
            return data;
        }

        static Color32[] Still(SongPlayback playback, double t, RenderTexture rt)
        {
            playback.Frame(t, rt.width, rt.height);
            playback.Pipeline.Render(rt);
            var req = AsyncGPUReadback.Request(rt, 0, TextureFormat.RGBA32);
            req.WaitForCompletion();
            return req.GetData<Color32>().ToArray();
        }

        /// <summary>
        /// A seek shows the same horde as a run from the start: a still at a song time is the same
        /// image whether the playback ran every 1/60 s frame up to it, jumped straight to it, or
        /// jumped back to it. (Edit mode runs no LateUpdate, so the 2D lights keep their first
        /// culling bounds here; the player's stills cover the lights, DemoMain.)
        /// </summary>
        [Test]
        public void ASeekDrawsTheSameStillAsARunFromTheStart()
        {
            var data = BakedData();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var moon = new GameObject("Moon").AddComponent<Light2D>();
            moon.lightType = Light2D.LightType.Global;
            moon.intensity = 0.4f;
            var rt = new RenderTexture(960, 540, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            SongPlayback playback = null;
            try
            {
                // One playback in the scene at a time: both would draw into each other's stills.
                const double at = 116; // the build, a few hundred on screen
                playback = new SongPlayback(data, PixelPipeline.TargetZoom, null);
                for (int f = 0; f < at * 60; f++) playback.Frame(f / 60.0, rt.width, rt.height);
                var fromStart = Still(playback, at, rt);
                Assert.That(playback.OnScreenAlive, Is.GreaterThan(100), "the still shows a horde");
                playback.Frame(150, rt.width, rt.height);
                var backAgain = Still(playback, at, rt);
                playback.Dispose();

                playback = new SongPlayback(data, PixelPipeline.TargetZoom, null);
                var jumped = Still(playback, at, rt);

                Assert.That(Differing(jumped, fromStart), Is.Zero, "a seek forward");
                Assert.That(Differing(backAgain, fromStart), Is.Zero, "a seek back");
            }
            finally
            {
                playback?.Dispose();
                rt.Release();
                UnityEngine.Object.DestroyImmediate(moon.gameObject);
            }
        }

        static int Differing(Color32[] a, Color32[] b)
        {
            int n = 0;
            for (int i = 0; i < a.Length; i++)
                if (a[i].r != b[i].r || a[i].g != b[i].g || a[i].b != b[i].b) n++;
            return n;
        }
    }
}
