using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Champions.Client;
using Champions.Client.Scenes;
using Champions.EditorTools;
using Champions.Render;
using Champions.Sim;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Champions.Tests
{
    /// <summary>
    /// The palette as Champions.Render reads it (its colour rules are the tools' test_palette.py), and the palette
    /// scene's stills: the frame, its type-ID pass and the view `champions look` measures them by.
    /// </summary>
    public sealed class PaletteTests
    {
        static string ContentDir => GameContent.Find(Application.dataPath);

        [Test]
        public void ThePaletteColoursEveryCastTypeAndNamesFourZones()
        {
            var palette = Palette.Load(ContentDir);
            foreach (var type in GameContent.Load(ContentDir).Enemies.Cast)
                Assert.That(palette.ForType(type.Id).a, Is.EqualTo(1f), type.Id);
            Assert.That(palette.Zones.Select(z => z.Family), Is.EqualTo(new[] { "cool", "cool", "violet", "warm" }));
            Assert.That(palette.Zones[palette.WarmZone].Family, Is.EqualTo("warm"));
            Assert.That(palette.HeroTrail, Is.EqualTo(Color.white), "the hero's trail is pure white");
            Assert.That(palette.Role("zones." + palette.Zones[0].Name + ".line"), Is.EqualTo(palette.Zones[0].Line));
        }

        [TestCase("\"#05c9ff\"", "\"#05c9fg\"", "palette.json.enemies.key.cyan: #05c9fg is not a #rrggbb colour")]
        [TestCase("\"wall\": ", "\"walls\": ", "palette.json.wall: missing")]
        [TestCase("\"skeleton\": \"enemies.key.cyan\"", "\"skeleton\": \"enemies.key.teal\"", "palette.json.types.skeleton: enemies.key.teal is not a role")]
        public void ABrokenPaletteFailsWithItsPath(string from, string to, string message)
        {
            string text = File.ReadAllText(Path.Combine(ContentDir, Palette.File));
            Assert.That(text, Does.Contain(from));
            var e = Assert.Throws<ContentException>(() => Palette.Read(JsonValue.Parse(text.Replace(from, to))));
            Assert.That(e.Message, Is.EqualTo(message));
        }

        [Test]
        public void TheStillsScenesFlagNeedsAFolderAndNoTimes()
        {
            var o = LaunchOptions.Parse(new[] { "player", "--capture-stills", "dir", "--stills-scenes", "palette,all" }, batchMode: true);
            Assert.That(o.StillsScenes, Is.EqualTo(new[] { "palette", "all" }));
            Assert.Throws<ArgumentException>(() => LaunchOptions.Parse(new[] { "player", "--stills-scenes", "palette" }, false));
            Assert.Throws<ArgumentException>(() => LaunchOptions.Parse(
                new[] { "player", "--capture-stills", "d", "--stills-at", "1", "--stills-scenes", "palette" }, false));
        }

        [Test]
        public void ThePaletteSceneKeepsAStillItsIdPassAndAViewThatLineUp()
        {
            var scene = EditorSceneManager.OpenScene(Tasks.BootScenePath, OpenSceneMode.Single);
            var camera = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Camera>()).Single();
            string dir = Path.Combine(Path.GetTempPath(), "champions-stills-" + Guid.NewGuid().ToString("N"));
            try
            {
                using (var stills = new Stills(camera, dir, Array.Empty<double>()))
                {
                    var scenes = new StillScenes(stills, camera, new[] { "palette" }, ContentDir);
                    scenes.Update(0);
                    scenes.Update(0.25);
                    Assert.That(scenes.Done);
                }
                var frame = Read(Path.Combine(dir, "still-palette-0.250.png"));
                var ids = Read(Path.Combine(dir, "id-palette-0.250.png"));
                var view = JsonValue.Parse(File.ReadAllText(Path.Combine(dir, "view-palette-0.250.json")));
                var table = view.Members.Single(m => m.Key == "ids").Value.Members.ToDictionary(m => int.Parse(m.Key), m => m.Value);

                var seen = new HashSet<int>(ids.Select(c => (int)c.r));
                seen.Remove(0);
                Assert.That(seen.Except(table.Keys), Is.Empty, "every code drawn is in the view's table");
                Assert.That(seen.Count(c => c != IdPass.Hero), Is.GreaterThanOrEqualTo(5), "the horde's types");

                // The hero's middle, projected by the view as `champions look` does (row 0 at the top), is the hero's code.
                var m = view.Members.Single(x => x.Key == "viewProj").Value.Items;
                var viewProj = new Matrix4x4();
                for (int i = 0; i < 16; i++) viewProj[i / 4, i % 4] = (float)m[i].Number;
                var clip = viewProj * new Vector4(0, 0.9f, 0, 1);
                int px = (int)((clip.x / clip.w + 1) / 2 * Stills.Width), row = (int)((1 - clip.y / clip.w) / 2 * Stills.Height);
                Assert.That((int)At(ids, px, row).r, Is.EqualTo(IdPass.Hero), $"the hero at ({px}, {row})");

                // No monster is brighter than the hero, and the monsters' bodies are black.
                float Brightest(Func<int, bool> drawn) =>
                    Enumerable.Range(0, ids.Length).Where(i => drawn(ids[i].r)).Max(i => ((Color)frame[i]).grayscale);
                Assert.That(Brightest(c => c == IdPass.Hero), Is.GreaterThan(Brightest(c => c != 0 && c != IdPass.Hero)));
                var monsters = Enumerable.Range(0, ids.Length).Where(i => ids[i].r != 0 && ids[i].r != IdPass.Hero).ToArray();
                Assert.That(monsters.Count(i => ((Color)frame[i]).grayscale < 0.1f) / (float)monsters.Length, Is.GreaterThan(0.5f));
            }
            finally
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            }
        }

        static Color32[] Read(string png)
        {
            var tex = new Texture2D(2, 2);
            Assert.That(tex.LoadImage(File.ReadAllBytes(png)), png);
            var pixels = tex.GetPixels32();
            UnityEngine.Object.DestroyImmediate(tex);
            return pixels;
        }

        /// <summary>The pixel at x and the image row (0 at the top); a texture's rows start at the bottom.</summary>
        static Color32 At(Color32[] pixels, int x, int row) => pixels[(Stills.Height - 1 - row) * Stills.Width + x];
    }
}
