using System;
using System.Linq;
using Demo2D.Runtime.Playback;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace Demo2D.Tests
{
    /// <summary>PX5: the client's playback of the song's timeline, on stills (its frames' checks are Demo2D.Choreography.Tests' SpriteEvaluatorTests).</summary>
    public class PlaybackTests
    {
        static PlaybackData BakedData()
        {
            var data = Resources.Load<PlaybackData>(PlaybackData.ResourcePath);
            if (data == null) Assert.Ignore("PlaybackData isn't baked: run demo2d unity BakePlayback");
            return data;
        }

        static Color32[] Still(SongPlayback playback, double t, RenderTexture rt)
        {
            playback.Frame(t, rt.width, rt.height, still: true);
            playback.Pipeline.Render(rt);
            var req = AsyncGPUReadback.Request(rt, 0, TextureFormat.RGBA32);
            req.WaitForCompletion();
            return req.GetData<Color32>().ToArray();
        }

        /// <summary>
        /// The live mix's limiter (PX10's review): the song's quiet passages pass untouched, and a
        /// dense beat, several times full scale, comes out under full scale with its loudness order kept.
        /// </summary>
        [Test]
        public void TheLimiterKeepsTheMixUnderFullScale()
        {
            Assert.That(MixLimiter.Limit(0.5f), Is.EqualTo(0.5f));
            Assert.That(MixLimiter.Limit(-0.5f), Is.EqualTo(-0.5f));
            float previous = -1;
            for (float x = 0; x <= 4f; x += 0.01f)
            {
                float y = MixLimiter.Limit(x);
                Assert.That(y, Is.LessThan(1f), $"Limit({x})");
                Assert.That(y, Is.GreaterThanOrEqualTo(previous), $"Limit({x}) is quieter than a quieter input's");
                Assert.That(MixLimiter.Limit(-x), Is.EqualTo(-y), $"Limit(-{x})");
                previous = y;
            }
        }

        /// <summary>
        /// A seek shows the same horde as a run from the start: a still at a song time is the same
        /// image whether the playback ran every 1/60 s frame up to it, jumped straight to it, or
        /// jumped back to it, effects and night included (PX7: each effect re-simulated from its
        /// event's seed). (Edit mode runs no LateUpdate, so the 2D lights keep their first culling
        /// bounds here; the player's stills cover the lights, DemoMain.)
        /// </summary>
        [Test]
        public void ASeekDrawsTheSameStillAsARunFromTheStart()
        {
            var data = BakedData();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single); // the playback brings its own moon
            var rt = new RenderTexture(960, 540, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            SongPlayback playback = null;
            try
            {
                // One playback in the scene at a time: both would draw into each other's stills.
                const double at = 116; // the build, a few hundred on screen, spells and their lights
                // A throwaway draw first: the editor's first draw in a session samples atlases it
                // hasn't uploaded yet, so its sprites show other types' pixels (a player's don't).
                playback = new SongPlayback(data, PixelPipeline.TargetZoom, null);
                Still(playback, at, rt);
                playback.Dispose();

                playback = new SongPlayback(data, PixelPipeline.TargetZoom, null);
                var jumped = Still(playback, at, rt);
                playback.Dispose();

                playback = new SongPlayback(data, PixelPipeline.TargetZoom, null);
                for (int f = 0; f < at * 60; f++) playback.Frame(f / 60.0, rt.width, rt.height);
                var fromStart = Still(playback, at, rt);
                Assert.That(playback.OnScreenAlive, Is.GreaterThan(100), "the still shows a horde");
                playback.Frame(150, rt.width, rt.height);
                var backAgain = Still(playback, at, rt);

                Assert.That(Differing(jumped, fromStart), Is.Zero, "a seek forward");
                Assert.That(Differing(backAgain, fromStart), Is.Zero, "a seek back");
            }
            finally
            {
                playback?.Dispose();
                rt.Release();
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
