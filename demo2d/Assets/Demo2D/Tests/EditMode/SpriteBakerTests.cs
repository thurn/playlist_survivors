using System.Collections.Generic;
using System.IO;
using System.Linq;
using Demo2D.EditorTools.Assets;
using NUnit.Framework;
using PS.Choreography;
using UnityEngine;

namespace Demo2D.Tests
{
    /// <summary>PX3: the sprite baker's filter, and the clip library against a fresh bake.</summary>
    public class SpriteBakerTests
    {
        static SpriteBaker.Image Img(int w, int h, params (int x, int y, Color32 c)[] px)
        {
            var img = new SpriteBaker.Image { W = w, H = h, Px = new Color32[w * h] };
            foreach (var (x, y, c) in px) img.Px[y * w + x] = c;
            return img;
        }

        static readonly Color32 Red = new Color32(255, 0, 0, 255), Blue = new Color32(0, 0, 255, 255);

        [Test]
        public void DownsampleAveragesCoverageAndSnapsAlphaAtHalf()
        {
            // At half scale each grid pixel covers a 2×2 block: the left one is half covered by red
            // and blue, the right a quarter by red.
            var img = Img(4, 2, (0, 0, Red), (1, 1, Blue), (2, 0, Red));
            var s = SpriteBaker.Resample(new List<SpriteBaker.Image> { img }, Vector2.zero, 0.5f);
            Assert.That((s.W, s.H), Is.EqualTo((1, 1)), "the quarter-covered pixel snaps clear and is trimmed");
            Assert.That(s.Frames[0][0], Is.EqualTo(new Color32(128, 0, 128, 255)), "colour weighted by coverage, alpha snapped to 1");
            Assert.That(s.Pivot, Is.EqualTo(Vector2Int.zero));
        }

        [Test]
        public void TheFeetLandOnAGridPixelCornerWhereverTheyFallInTheCanvas()
        {
            // Two pixels wide at the source, feet at x = 3: one grid pixel each side of the pivot.
            var img = Img(6, 2, (2, 0, Red), (2, 1, Red), (3, 0, Blue), (3, 1, Blue));
            var s = SpriteBaker.Resample(new List<SpriteBaker.Image> { img }, new Vector2(3, 0), 0.5f);
            Assert.That((s.W, s.H, s.Pivot), Is.EqualTo((2, 1, new Vector2Int(1, 0))));
            Assert.That(s.Frames[0][0], Is.EqualTo(Red));
            Assert.That(s.Frames[0][1], Is.EqualTo(Blue));
        }

        static bool VendorSynced => Directory.Exists("Assets/Vendor") && Directory.EnumerateFileSystemEntries("Assets/Vendor").Any();
        static List<SpriteBaker.Baked> baked;

        static List<SpriteBaker.Baked> Baked()
        {
            if (!VendorSynced) Assert.Ignore("the vendor folder is empty: run demo2d setup");
            return baked ??= SpriteBaker.BakeAll();
        }

        static IEnumerable<JsonValue> Library() =>
            JsonValue.Parse(File.ReadAllText(ClipAnnotator.OutPath))["annotations"].Items;

        [Test]
        public void EveryLibraryClipBakesToItsAnnotatedSizeAndPivotWithHardAlpha()
        {
            var bake = Baked().ToDictionary(b => b.Character.Prefix);
            foreach (var a in Library())
            {
                string clip = a["clip"].AsString();
                int cut = clip.IndexOf('-');
                var s = bake[clip.Substring(0, cut)].Clips[clip.Substring(cut + 1)];
                Assert.That(s.Frames.Count, Is.EqualTo(a["frames"].AsInt()), clip + ": frames");
                Assert.That(new[] { s.W, s.H, s.Pivot.x, s.Pivot.y },
                    Is.EqualTo(new[] { a["sizePx"][0].AsInt(), a["sizePx"][1].AsInt(), a["pivotPx"][0].AsInt(), a["pivotPx"][1].AsInt() }),
                    clip + ": size and pivot (re-run demo2d unity AnnotateClips after a baker change)");
                Assert.That(s.Frames.SelectMany(f => f).All(c => c.a == 0 || c.a == 255), clip + ": alpha is 0 or 1");
            }
        }

        [Test]
        public void EachTypeStandsItsGameHeight()
        {
            foreach (var b in Baked())
            {
                var walk = b.Clips["walk"];
                int top = Enumerable.Range(0, walk.H).Last(y => Enumerable.Range(0, walk.W).Any(x => walk.Opaque(0, x, y)));
                int bottom = Enumerable.Range(0, walk.H).First(y => Enumerable.Range(0, walk.W).Any(x => walk.Opaque(0, x, y)));
                Assert.That(top - bottom + 1, Is.InRange(b.Character.GameHeightPx - 2, b.Character.GameHeightPx + 1), b.Character.Name);
            }
        }

        [Test]
        public void TheAtlasHoldsEveryFrameWithoutOverlap()
        {
            foreach (var b in Baked())
            {
                var (px, w, h, placed) = SpriteBaker.Pack(b);
                var all = placed.SelectMany(kv => kv.Value).Select(p => p.Rect).ToList();
                Assert.That(all.Count, Is.EqualTo(b.Clips.Values.Sum(s => s.Frames.Count)), b.Character.Name);
                Assert.That(all.All(r => r.xMin >= 1 && r.yMin >= 1 && r.xMax < w && r.yMax < h), b.Character.Name + ": inside, with a gutter");
                for (int i = 0; i < all.Count; i++)
                for (int j = i + 1; j < all.Count; j++)
                    Assert.That(all[i].Overlaps(all[j]), Is.False, $"{b.Character.Name}: frames {i} and {j} overlap");
                foreach (var (clip, list) in placed)
                {
                    // The crop carries every opaque pixel of its frame, in place.
                    var s = b.Clips[clip];
                    int f = s.Frames.Count / 2;
                    var p = list[f];
                    int opaque = 0;
                    for (int y = 0; y < p.Rect.height; y++)
                    for (int x = 0; x < p.Rect.width; x++)
                    {
                        var c = px[(p.Rect.y + y) * w + p.Rect.x + x];
                        Assert.That(c, Is.EqualTo(s.Frames[f][(p.Offset.y + y) * s.W + p.Offset.x + x]), $"{b.Character.Name} {clip}");
                        if (c.a != 0) opaque++;
                    }
                    Assert.That(opaque, Is.EqualTo(s.Frames[f].Count(c => c.a != 0)), $"{b.Character.Name} {clip}: opaque pixels");
                }
            }
        }

        [Test]
        public void TheLibraryIsTheAnnotatorsProposalWithSaneTimings()
        {
            var fresh = ClipAnnotator.Annotate(Baked(), ClipAnnotator.Entries());
            var file = Library().ToList();
            Assert.That(fresh.Select(a => a.Clip), Is.EqualTo(file.Select(a => a["clip"].AsString())));
            for (int i = 0; i < fresh.Count; i++)
            {
                var a = fresh[i];
                var f = file[i];
                string what = a.Use;
                Assert.That(new[] { f["impactFrame"].AsInt(), f["endFrame"].AsInt() }, Is.EqualTo(new[] { a.ImpactFrame, a.EndFrame }), what);
                Assert.That(f["strideM"].AsFloat(), Is.EqualTo(a.StrideM).Within(0.006f), what + ": stride");
                Assert.That(f["impact"].AsFloat(), Is.EqualTo(a.ImpactFrame / (float)CharacterPacks.FrameRate).Within(0.001f), what);
                Assert.That(a.ImpactFrame, Is.InRange(0, a.EndFrame), what + ": impact before the end");
                Assert.That(a.EndFrame, Is.InRange(1, a.Frames), what + ": end within the clip");
                if (a.Role == "walk") Assert.That(a.StrideM, Is.InRange(0.25f, 2.5f), what + ": stride");
            }
            var library = ClipLibrary.Parse(File.ReadAllText(ClipAnnotator.OutPath));
            Assert.That(library.Count, Is.EqualTo(fresh.Count));
        }
    }
}
