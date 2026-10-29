using System.Collections.Generic;
using System.IO;
using System.Linq;
using Demo2D.Runtime;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Demo2D.EditorTools.Assets
{
    /// <summary>
    /// PX2's evidence stills (Tasks.AssetStills): the contact sheets, the tile test map by day
    /// (as the textures are) and at night, the swaying trees, and the CFXR gallery.
    /// </summary>
    public static class AssetStills
    {
        public const string TreesDir = "Assets/Vendor/RPGW_AncientForest/Animated";

        public static void Write(string dir)
        {
            Directory.CreateDirectory(dir);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            ContactSheets.Write(dir);
            TileMap(dir);
            Trees(dir);
            EffectGallery.Write(dir);
        }

        static void TileMap(string dir)
        {
            var catalog = ForestTiles.Load();
            var map = ForestTiles.BuildTestMap(catalog);
            var root = ForestTiles.Instantiate(map, catalog);
            try
            {
                var world = new RectInt(0, 0, map.W, map.H);
                int w = map.W * PixelShots.PixelsPerMetre, h = map.H * PixelShots.PixelsPerMetre;
                var day = PixelShots.GlobalLight(root.transform, false);
                var px = PixelShots.Render(world, Color.black);
                var label = new Color32(255, 255, 255, 255);
                var labelled = (Color32[])px.Clone();
                foreach (var (name, r) in map.Regions)
                    PixelText.Draw(labelled, w, h, r.x * 32 + 4, (map.H - r.y) * 32 - 4, name, label, 2);
                Save(PixelShots.Shrink(labelled, w, h, 2), Path.Combine(dir, "tile-test-map.jpg"), "PX2 tile test map (half size)");

                // The transition sets at the game's 2× scale, by day and at night with a few lights.
                var sets = map.Regions.Skip(catalog.Stamps.Count).Select(r => r.rect).ToList();
                var bounds = new RectInt(sets.Min(r => r.xMin), sets.Min(r => r.yMin), 0, 0);
                var crop = new RectInt(bounds.x * 32, (map.H - bounds.y) * 32 - 540, 960, 540);
                Save(PixelShots.Crop(px, w, crop, 2), Path.Combine(dir, "tile-test-map-day.jpg"), "PX2 tile test map, transitions");
                Object.DestroyImmediate(day.gameObject);
                PixelShots.GlobalLight(root.transform, true);
                var rng = new System.Random(3);
                for (int i = 0; i < 6; i++)
                {
                    var at = new Vector2(bounds.x + 3 + i * 5.5f, map.H - bounds.y - 3 - (float)rng.NextDouble() * 10);
                    var c = i % 3 == 0 ? new Color(1f, 0.6f, 0.25f) : i % 3 == 1 ? new Color(0.4f, 1f, 0.85f) : new Color(0.8f, 0.5f, 1f);
                    PixelShots.PointLight(root.transform, at, c, 5f, 1.3f);
                }
                var night = PixelShots.Render(world, PixelShots.NightSky, hdr: true);
                Save(PixelShots.Crop(night, w, crop, 2), Path.Combine(dir, "tile-test-map-night.jpg"), "PX2 tile test map, transitions at night");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        /// <summary>The swaying trees and their clips, each tree's sprite frame by name.</summary>
        public static List<(string name, AnimationClip clip)> TreeClips() =>
            Directory.GetFiles(TreesDir, "*.anim").OrderBy(p => p)
                .Select(p => (Path.GetFileNameWithoutExtension(p), AssetDatabase.LoadAssetAtPath<AnimationClip>(p.Replace('\\', '/'))))
                .ToList();

        /// <summary>A tree clip's sprite keys: the frames it swaps through and when.</summary>
        public static ObjectReferenceKeyframe[] SpriteKeys(AnimationClip clip)
        {
            var binding = AnimationUtility.GetObjectReferenceCurveBindings(clip)
                .Single(b => b.type == typeof(SpriteRenderer) && b.propertyName == "m_Sprite");
            return AnimationUtility.GetObjectReferenceCurve(clip, binding);
        }

        /// <summary>The clip's sprite at time t (the last key at or before it, looping).</summary>
        public static Sprite TreeSprite(AnimationClip clip, float t)
        {
            var keys = SpriteKeys(clip);
            t = clip.length > 0 ? t % clip.length : 0;
            return (Sprite)keys.Last(k => k.time <= t + 1e-4f).value;
        }

        static SpriteRenderer PlaceTree(AnimationClip clip, Transform parent, float t)
        {
            var r = new GameObject(clip.name).AddComponent<SpriteRenderer>();
            r.transform.SetParent(parent, false);
            r.sharedMaterial = PixelShots.SpriteLit;
            r.sprite = TreeSprite(clip, t);
            return r;
        }

        /// <summary>Every tree in a row at night, in three rows a third of its cycle apart.</summary>
        static void Trees(string dir)
        {
            var root = new GameObject("Trees");
            try
            {
                var clips = TreeClips();
                PixelShots.GlobalLight(root.transform, true);
                float x = 0, rowH = 14;
                var phases = new[] { 0f, 0.33f, 0.66f };
                foreach (var (_, clip) in clips)
                {
                    float width = 0;
                    for (int row = 0; row < phases.Length; row++)
                    {
                        var r = PlaceTree(clip, root.transform, phases[row] * clip.length);
                        var size = r.sprite.bounds.size;
                        width = Mathf.Max(width, size.x);
                        r.transform.localPosition = new Vector3(x + size.x / 2 - r.sprite.bounds.center.x + 0.5f,
                            (phases.Length - 1 - row) * rowH + rowH / 2 - r.sprite.bounds.center.y, 0);
                    }
                    PixelShots.PointLight(root.transform, new Vector2(x + width / 2, rowH * 1.5f), new Color(0.5f, 1f, 0.8f), 6f, 1f);
                    x += Mathf.Ceil(width) + 1;
                }
                var world = new RectInt(0, 0, Mathf.CeilToInt(x), Mathf.CeilToInt(rowH * phases.Length));
                var px = PixelShots.Render(world, new Color(0.06f, 0.09f, 0.07f), hdr: true);
                Save(PixelShots.Shrink(px, world.width * 32, world.height * 32, 2), Path.Combine(dir, "trees.jpg"),
                    "PX2 swaying trees (half size; rows a third of a cycle apart)");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        static void Save(Texture2D tex, string path, string tool)
        {
            RenderTag.Save(tex, path, tool);
            Object.DestroyImmediate(tex);
            Debug.Log("AssetStills: wrote " + path);
        }
    }
}
