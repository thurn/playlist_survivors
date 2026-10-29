using System;
using Demo3D.EditorTools.Crowd;
using Demo3D.Runtime.Crowd;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Demo3D.Tests
{
    public class SkeletonLodPreviewTests
    {
        [Test]
        public void SelectingAClipWhilePausedImmediatelyDisplaysItsPose()
        {
            WithPreview(preview =>
            {
                preview.Playing = false;
                preview.CrossfadeSeconds = 0.2f;
                int previous = preview.ClipIndex;
                preview.ChangeClip(1);
                Assert.That(preview.ClipIndex, Is.Not.EqualTo(previous));
                Assert.That(preview.ClipTime, Is.Zero);
                Assert.That(preview.Blend, Is.EqualTo(1), "a paused preview must display the selected clip, not freeze on its predecessor");
            });
        }

        [Test]
        public void ScreenCameraClearsOldContentsBeforeTheUiRepaints()
        {
            WithPreview(preview =>
            {
                var camera = preview.PresentationCamera;
                Assert.That(camera, Is.Not.Null);
                Assert.That(camera.targetTexture, Is.Null, "normal presentation must render to the screen");
                Assert.That(camera.cullingMask, Is.Zero, "the screen camera must not duplicate the model draws");
                var rt = new RenderTexture(64, 64, 24);
                var pixels = new Texture2D(64, 64, TextureFormat.RGB24, false);
                var previous = RenderTexture.active;
                rt.Create();
                try
                {
                    camera.targetTexture = rt;
                    camera.enabled = false;
                    Color? cleared = null;
                    foreach (var oldContent in new[] { Color.white, Color.red, Color.green })
                    {
                        RenderTexture.active = rt;
                        GL.Clear(true, true, oldContent);
                        camera.Render();
                        RenderTexture.active = rt;
                        pixels.ReadPixels(new Rect(0, 0, 64, 64), 0, 0); pixels.Apply();
                        var current = pixels.GetPixel(32, 32);
                        Assert.That(current.r + current.g + current.b, Is.LessThan(1.5f), "old bright glyph pixels must be erased");
                        if (cleared.HasValue)
                            Assert.That(Vector4.Distance(current, cleared.Value), Is.LessThan(0.01f), "the next frame must not depend on its old contents");
                        cleared = current;
                    }
                }
                finally
                {
                    camera.targetTexture = null;
                    RenderTexture.active = previous;
                    rt.Release();
                    UnityEngine.Object.DestroyImmediate(rt); UnityEngine.Object.DestroyImmediate(pixels);
                }
            });
        }

        [Test]
        public void CloseZoomKeepsAllVariantsRegisteredAndCanInspectTheHeadAndFeet()
        {
            WithPreview(preview =>
            {
                preview.Inspect(1.5f, 0.8f);
                preview.Zoom(0.001f);
                Assert.That(preview.ViewWidth, Is.EqualTo(0.5f));
                preview.DrawFrame(true);
                var camera = preview.Cameras[0];
                var head = camera.WorldToViewportPoint(new Vector3(0, 1.5f, 0));
                Assert.That(head.x, Is.EqualTo(0.5f).Within(0.001f));
                Assert.That(head.y, Is.EqualTo(0.5f).Within(0.001f));
                foreach (var other in preview.Cameras)
                    Assert.That(other.transform.position, Is.EqualTo(camera.transform.position));
                preview.Inspect(0.15f, 0.8f);
                preview.DrawFrame(true);
                var feet = camera.WorldToViewportPoint(new Vector3(0, 0.15f, 0));
                Assert.That(feet.y, Is.EqualTo(0.5f).Within(0.001f));
                preview.Zoom(1000);
                Assert.That(preview.ViewWidth, Is.EqualTo(55));
            });
        }

        static void WithPreview(Action<SkeletonLodPreview> check)
        {
            var lib = AssetDatabase.LoadAssetAtPath<CrowdLibrary>(SkeletonLodExperiment.LibraryPath);
            if (lib == null) Assert.Ignore("isolated preview bake absent: run skeleton_lod.py scene");
            var go = new GameObject("paused clip regression");
            var preview = go.AddComponent<SkeletonLodPreview>();
            try
            {
                preview.Library = lib;
                preview.Initialize();
                check(preview);
            }
            finally
            {
                preview.DisposePreview();
                UnityEngine.Object.DestroyImmediate(go);
            }
        }
    }
}
