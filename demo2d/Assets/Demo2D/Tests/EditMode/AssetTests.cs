using System.Collections.Generic;
using System.IO;
using System.Linq;
using Demo2D.EditorTools.Assets;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Demo2D.Tests
{
    /// <summary>PX2's checks that the synced packs work in demo2d (README.md, Verifying the Assets).</summary>
    public class AssetTests
    {
        static bool VendorSynced => Directory.Exists("Assets/Vendor") && Directory.EnumerateFileSystemEntries("Assets/Vendor").Any();

        [SetUp]
        public void RequireVendor()
        {
            if (!VendorSynced) Assert.Ignore("the vendor folder is empty: run demo2d setup");
        }

        [Test]
        public void EveryCharacterClipImportsAsACompleteSequenceAtFifteenFps()
        {
            var characters = CharacterPacks.Load();
            Assert.That(characters.Count, Is.EqualTo(14), "the hero and 13 roster types");
            int clips = 0;
            foreach (var ch in characters)
            {
                var frames = ch.Frames();
                Assert.That(frames.Keys, Does.Contain("walk").And.Contain("death"), ch.Name);
                foreach (var kv in frames)
                {
                    string what = $"{ch.Name} {kv.Key}";
                    Assert.That(CharacterPacks.Character.Indices(kv.Value), Is.EqualTo(Enumerable.Range(0, kv.Value.Count)), what + ": frame numbers");
                    var clip = ch.Clip(kv.Key);
                    Assert.That(clip, Is.Not.Null, what + ": no .anim");
                    Assert.That(kv.Value.Count, Is.EqualTo(CharacterPacks.ExportedFrames(clip.length)),
                        $"{what}: {kv.Value.Count} frames for a {clip.length:F3} s clip");
                    foreach (var f in kv.Value)
                        Assert.That(AssetDatabase.LoadAssetAtPath<Texture2D>(f), Is.Not.Null, f + " didn't import");
                    clips++;
                }
            }
            Assert.That(clips, Is.GreaterThan(150));
        }

        [Test]
        public void EveryClipTheClipLibraryNamesHasItsFrames()
        {
            var byPrefix = CharacterPacks.Load().ToDictionary(c => c.Prefix);
            var library = CharacterPacks.LibraryClips();
            Assert.That(library, Is.Not.Empty);
            foreach (var (clip, count) in library)
            {
                int cut = clip.IndexOf('-');
                Assert.That(byPrefix.TryGetValue(clip.Substring(0, cut), out var ch), clip + ": no character has its prefix");
                Assert.That(ch.Frames().TryGetValue(clip.Substring(cut + 1), out var frames), clip + ": no frames");
                Assert.That(frames.Count, Is.EqualTo(count), clip);
            }
        }

        [TestCase("Assets/Vendor/RPGW_AncientForest/Sliced/MainLev.png")]
        [TestCase("Assets/Vendor/RPGW_AncientForest/Sliced/Decorative.png")]
        public void TheForestSheetsAreSlicedInto32PixelTilesAt32PerUnitPointFiltered(string sheet)
        {
            var importer = (TextureImporter)AssetImporter.GetAtPath(sheet);
            Assert.That(importer.spritePixelsPerUnit, Is.EqualTo(32f));
            Assert.That(importer.filterMode, Is.EqualTo(FilterMode.Point));
            Assert.That(importer.spriteImportMode, Is.EqualTo(SpriteImportMode.Multiple));
            Assert.That(importer.mipmapEnabled, Is.False);
            Assert.That(importer.textureCompression, Is.EqualTo(TextureImporterCompression.Uncompressed));
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(sheet);
            Assert.That(Mathf.Max(tex.width, tex.height), Is.LessThanOrEqualTo(importer.maxTextureSize), "imported at full size");
            var sprites = AssetDatabase.LoadAllAssetsAtPath(sheet).OfType<Sprite>().ToList();
            Assert.That(sprites.Count, Is.GreaterThan(500));
            foreach (var s in sprites)
            {
                var r = s.rect;
                Assert.That(r.width == 32 && r.height == 32 && r.x % 32 == 0 && r.y % 32 == 0, $"{s.name} at {r}");
            }
        }

        [Test]
        public void EveryTransitionSetHasEveryCornerPatternButTheDiagonals()
        {
            var catalog = ForestTiles.Load();
            Assert.That(catalog.Transitions.Count, Is.EqualTo(14));
            var sprites = ForestTiles.Sprites(catalog.Sheet, catalog.TilePx);
            foreach (var (name, at) in catalog.Transitions)
            {
                var tiles = ForestTiles.CornerTiles(at);
                var expected = Enumerable.Range(0, 16).Except(ForestTiles.Diagonals);
                Assert.That(tiles.Keys, Is.EquivalentTo(expected), name);
                // Only a fill may be empty: the inside of a clear (transparent) terrain.
                foreach (var kv in tiles.Where(kv => kv.Key != 0 && kv.Key != 15))
                foreach (var cell in kv.Value)
                    Assert.That(sprites.ContainsKey(cell), $"{name}: tile {cell} is empty");
            }
        }

        [Test]
        public void TheTileTestMapRendersExactlyAsItsTilesCompose()
        {
            // Any seam (a gap, a filtered or bleeding edge, a half-pixel offset) changes pixels.
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var catalog = ForestTiles.Load();
            var map = ForestTiles.BuildTestMap(catalog);
            var root = ForestTiles.Instantiate(map, catalog);
            try
            {
                PixelShots.GlobalLight(root.transform, night: false);
                var rendered = PixelShots.Render(new RectInt(0, 0, map.W, map.H), Color.black);
                var expected = ForestTiles.Compose(map, catalog);
                Assert.That(rendered.Length, Is.EqualTo(expected.Length));
                int w = map.W * PixelShots.PixelsPerMetre, off = 0, first = -1;
                for (int i = 0; i < rendered.Length; i++)
                {
                    var a = rendered[i];
                    var b = expected[i];
                    if (Mathf.Abs(a.r - b.r) > 2 || Mathf.Abs(a.g - b.g) > 2 || Mathf.Abs(a.b - b.b) > 2)
                    {
                        off++;
                        if (first < 0) first = i;
                    }
                }
                Assert.That(off, Is.Zero, first < 0 ? "" : $"first at pixel ({first % w}, {first / w}): " +
                                                           $"{rendered[first]} rendered, {expected[first]} composed");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void TheSwayingTreesPlay()
        {
            var trees = AssetStills.TreeClips();
            Assert.That(trees.Count, Is.EqualTo(7));
            foreach (var (name, clip) in trees)
            {
                Assert.That(clip.isLooping && clip.length > 0, name);
                var keys = AssetStills.SpriteKeys(clip);
                Assert.That(keys.All(k => k.value is Sprite), name + ": a key without a sprite");
                Assert.That(keys.Select(k => k.value).Distinct().Count(), Is.GreaterThan(1), name + ": doesn't move");
                Assert.That(AssetStills.TreeSprite(clip, 0), Is.Not.EqualTo(AssetStills.TreeSprite(clip, clip.length / 2)), name);
                var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>($"{AssetStills.TreesDir}/{name}_0.controller");
                Assert.That(controller, Is.Not.Null, name + ": no controller");
                Assert.That(controller.layers[0].stateMachine.defaultState.motion, Is.EqualTo(clip), name + ": controller plays another clip");
            }
        }

        static IEnumerable<string> PoolPrefabs() =>
            VendorSynced ? EffectGallery.Pool().Values.SelectMany(p => p) : Enumerable.Empty<string>();

        [TestCaseSource(nameof(PoolPrefabs))]
        public void EveryEffectCandidateDrawsUnderThe2DRendererLitAndUnlit(string prefab)
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var root = new GameObject("Check");
            try
            {
                var problems = EffectGallery.Problems(EffectGallery.Spawn(prefab, root.transform, Vector2.zero));
                Assert.That(problems, Is.Empty, string.Join("; ", problems));
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
            Assert.That(EffectGallery.DrawnPixels(prefab, lit: true), Is.GreaterThan(100), "lit");
            Assert.That(EffectGallery.DrawnPixels(prefab, lit: false), Is.GreaterThan(100), "unlit");
        }

        [Test]
        public void TheEffectListNamesTwoOrThreeOfThePoolsCandidatesPerKind()
        {
            var pool = EffectGallery.Pool();
            var chosen = EffectGallery.Candidates();
            Assert.That(chosen.Keys, Is.EquivalentTo(pool.Keys));
            foreach (var kv in chosen)
            {
                Assert.That(kv.Value.Count, Is.InRange(2, 3), kv.Key);
                Assert.That(pool[kv.Key], Is.SupersetOf(kv.Value), kv.Key);
            }
        }
    }
}
