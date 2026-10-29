using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Demo2D.Runtime;
using PS.Choreography;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Demo2D.EditorTools.Assets
{
    /// <summary>
    /// PX3's stills (Tasks.SpriteStills), from the baked atlases on the forest ground at night,
    /// rendered at one pixel per grid pixel and saved at twice that, nearest-neighbour, as the game
    /// shows its 960×540 grid at 1080p:
    /// - <b>lineup:</b> every type at its game size, walking, labelled: grunts and casters with
    ///   the hero in front, elites and the boss behind;
    /// - <b>crowd:</b> the hero inside a mixed horde of about 130, at the game's base view, for
    ///   readability (each type recognizable, the hero findable).
    /// </summary>
    public static class SpriteStills
    {
        const int ViewW = 30, ViewH = 17; // metres: 960×544 grid pixels

        public static void Write(string dir)
        {
            Directory.CreateDirectory(dir);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var atlases = Load();
            Lineup(atlases, Path.Combine(dir, "lineup.png"));
            Crowd(atlases, Path.Combine(dir, "crowd.png"));
        }

        sealed class Atlas
        {
            public CharacterPacks.Character Character;
            public Texture2D Texture;
            public Dictionary<string, (Vector2Int pivot, List<SpriteBaker.Placed> frames)> Clips =
                new Dictionary<string, (Vector2Int, List<SpriteBaker.Placed>)>();

            public Sprite Frame(string clip, int i)
            {
                var (pivot, frames) = Clips[clip];
                return SpriteBaker.Sprite(Texture, frames[i % frames.Count], pivot);
            }
        }

        static List<Atlas> Load()
        {
            var list = new List<Atlas>();
            foreach (var ch in CharacterPacks.Load())
            {
                string png = $"{SpriteBaker.OutDir}/{ch.Name}.png", json = $"{SpriteBaker.OutDir}/{ch.Name}.json";
                var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(png) ?? throw new FileNotFoundException($"SpriteStills: no {png}: run demo2d unity BakeSprites");
                var a = new Atlas { Character = ch, Texture = tex };
                var j = JsonValue.Parse(File.ReadAllText(json))["clips"];
                Vector2Int V(JsonValue v) => new Vector2Int(v[0].AsInt(), v[1].AsInt());
                foreach (var (name, c) in j.Members)
                    a.Clips[name] = (V(c["pivotPx"]), c["frames"].Items.Select(f => new SpriteBaker.Placed(
                        new RectInt(f[0].AsInt(), f[1].AsInt(), f[2].AsInt(), f[3].AsInt()), new Vector2Int(f[4].AsInt(), f[5].AsInt()))).ToList());
                list.Add(a);
            }
            return list;
        }

        /// <summary>The forest's grass with patches of moss and dirt, as two Tilemaps.</summary>
        static GameObject Ground(int w, int h, int seed)
        {
            var catalog = ForestTiles.Load();
            return ForestTiles.Instantiate(GroundMap(catalog, w, h, seed), catalog);
        }

        /// <summary>
        /// The forest's grass with <paramref name="patches"/> patches of moss and dirt, w × h tiles
        /// (PX3's stills' ground).
        /// </summary>
        public static ForestTiles.TestMap GroundMap(ForestTiles.Catalog catalog, int w, int h, int seed, int patches = 7)
        {
            var map = new ForestTiles.TestMap { W = w, H = h, Base = catalog.Base, Overlay = new Vector2Int?[w, h] };
            var sets = catalog.Transitions.Where(t => t.name == "dark-grass-on-grass" || t.name == "dirt-on-grass").Select(t => ForestTiles.CornerTiles(t.at)).ToList();
            var rng = new System.Random(seed);
            // Blobs on the vertex grid: a vertex is inner when it lies in one of a few ellipses.
            var blobs = Enumerable.Range(0, patches).Select(i => (set: i % sets.Count, c: new Vector2((float)rng.NextDouble() * w, (float)rng.NextDouble() * h),
                r: new Vector2(2.5f + (float)rng.NextDouble() * 4, 1.5f + (float)rng.NextDouble() * 2.5f))).ToList();
            for (int k = 0; k < sets.Count; k++)
            {
                bool Inner(int vx, int vy) => blobs.Any(b => b.set == k && Sq((vx - b.c.x) / b.r.x) + Sq((vy - b.c.y) / b.r.y) <= 1);
                for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int key = (Inner(x, y) ? ForestTiles.TL : 0) | (Inner(x + 1, y) ? ForestTiles.TR : 0) |
                              (Inner(x, y + 1) ? ForestTiles.BL : 0) | (Inner(x + 1, y + 1) ? ForestTiles.BR : 0);
                    if (key == 0 || ForestTiles.Diagonals.Contains(key) || map.Overlay[x, y] != null) continue;
                    var options = sets[k][key];
                    map.Overlay[x, y] = options[(x * 7 + y * 13) % options.Count];
                }
            }
            return map;
        }

        static float Sq(float v) => v * v;

        static SpriteRenderer Place(Transform parent, Sprite sprite, Vector2 at, bool flip)
        {
            var r = new GameObject(sprite.name).AddComponent<SpriteRenderer>();
            r.transform.SetParent(parent, false);
            r.transform.localPosition = new Vector3(at.x, at.y, 0);
            r.sharedMaterial = PixelShots.SpriteLit;
            r.sprite = sprite;
            r.flipX = flip;
            r.sortingOrder = 10000 - Mathf.RoundToInt(at.y * 100); // lower feet draw in front
            return r;
        }

        static void Lineup(List<Atlas> atlases, string path)
        {
            var root = new GameObject("Lineup");
            var created = new List<Sprite>();
            try
            {
                var ground = Ground(ViewW, ViewH, 5);
                ground.transform.SetParent(root.transform, false);
                PixelShots.GlobalLight(root.transform, true);
                var labels = new List<(string text, Vector2 at)>();
                void Row(IEnumerable<Atlas> row, float y, float gap)
                {
                    var items = row.Select(a => (a, sprite: a.Frame("walk", 0))).ToList();
                    created.AddRange(items.Select(i => i.sprite));
                    float total = items.Sum(i => i.sprite.rect.width / PixelShots.PixelsPerMetre) + gap * (items.Count - 1);
                    float x = Mathf.Round((ViewW - total) / 2 * PixelShots.PixelsPerMetre) / PixelShots.PixelsPerMetre;
                    foreach (var (a, sprite) in items)
                    {
                        float width = sprite.rect.width / PixelShots.PixelsPerMetre;
                        var feet = new Vector2(x + sprite.pivot.x / PixelShots.PixelsPerMetre, y);
                        Place(root.transform, sprite, feet, false);
                        labels.Add((a.Character.Name, new Vector2(x + width / 2, y - 0.25f)));
                        PixelShots.PointLight(root.transform, feet + new Vector2(0.4f, 1.2f), new Color(1f, 0.75f, 0.45f), 2.6f, 0.9f);
                        x += width + gap;
                    }
                }
                Row(atlases.Where(a => a.Character.Rank == "elite" || a.Character.Rank == "boss"), 7.5f, 0.6f);
                Row(atlases.Where(a => a.Character.Rank != "elite" && a.Character.Rank != "boss"), 2f, 0.35f);
                var world = new RectInt(0, 0, ViewW, ViewH);
                var px = PixelShots.Render(world, PixelShots.NightSky, hdr: true);
                int w = ViewW * PixelShots.PixelsPerMetre, h = ViewH * PixelShots.PixelsPerMetre;
                foreach (var (text, at) in labels)
                {
                    int tw = text.Length * PixelText.Advance;
                    PixelText.Draw(px, w, h, Mathf.RoundToInt(at.x * PixelShots.PixelsPerMetre) - tw / 2, Mathf.RoundToInt(at.y * PixelShots.PixelsPerMetre),
                        text, new Color32(235, 235, 225, 255), 1);
                }
                Save(px, w, h, path, "PX3 lineup at game size, night (2x nearest)");
            }
            finally
            {
                foreach (var s in created) UnityEngine.Object.DestroyImmediate(s);
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        static void Crowd(List<Atlas> atlases, string path)
        {
            var root = new GameObject("Crowd");
            var created = new List<Sprite>();
            try
            {
                var ground = Ground(ViewW, ViewH, 11);
                ground.transform.SetParent(root.transform, false);
                PixelShots.GlobalLight(root.transform, true);
                var rng = new System.Random(7);
                var hero = atlases.Single(a => a.Character.Rank == "hero");
                var grunts = atlases.Where(a => a.Character.Rank == "fodder").ToList();
                var elites = atlases.Where(a => a.Character.Rank == "elite").ToList();
                var boss = atlases.Single(a => a.Character.Rank == "boss");
                var centre = new Vector2(ViewW / 2f, ViewH / 2f - 1);
                var taken = new List<(Vector2 at, float r)> { (centre, 1.4f) };
                void Add(Atlas a, Vector2 at, float r)
                {
                    var walk = a.Clips["walk"].frames.Count;
                    var sprite = a.Frame("walk", rng.Next(walk));
                    created.Add(sprite);
                    Place(root.transform, sprite, at, at.x > centre.x); // the art faces right: face the hero
                    taken.Add((at, r));
                }
                bool Free(Vector2 at, float r) => taken.All(t => (t.at - at).magnitude >= t.r + r);
                Add(boss, new Vector2(centre.x + 9.5f, centre.y + 4.5f), 2.2f);
                // A ring of grunts and a few elites around the hero, denser close in.
                for (int tries = 0, placed = 0; placed < 130 && tries < 20000; tries++)
                {
                    bool elite = placed % 18 == 5;
                    float r = elite ? 0.75f : 0.4f;
                    float dist = 1.8f + Mathf.Pow((float)rng.NextDouble(), 0.8f) * 14f;
                    float ang = (float)(rng.NextDouble() * Math.PI * 2);
                    var at = centre + new Vector2(Mathf.Cos(ang) * dist, Mathf.Sin(ang) * dist * 0.62f);
                    at = new Vector2(Mathf.Round(at.x * 32) / 32, Mathf.Round(at.y * 32) / 32);
                    if (at.x < -0.5f || at.x > ViewW + 0.5f || at.y < -0.3f || at.y > ViewH - 0.6f || !Free(at, r)) continue;
                    Add(elite ? elites[placed % elites.Count] : grunts[rng.Next(grunts.Count)], at, r);
                    placed++;
                }
                var heroSprite = hero.Frame("walk", 3);
                created.Add(heroSprite);
                Place(root.transform, heroSprite, centre, false);
                PixelShots.PointLight(root.transform, centre + new Vector2(0, 1f), new Color(1f, 0.8f, 0.55f), 4.5f, 1.1f);
                PixelShots.PointLight(root.transform, centre + new Vector2(-7, 3), new Color(0.4f, 1f, 0.85f), 4f, 0.8f);
                PixelShots.PointLight(root.transform, centre + new Vector2(9, 4.5f), new Color(1f, 0.35f, 0.25f), 5f, 0.9f);
                var world = new RectInt(0, 0, ViewW, ViewH);
                var px = PixelShots.Render(world, PixelShots.NightSky, hdr: true);
                Save(px, ViewW * PixelShots.PixelsPerMetre, ViewH * PixelShots.PixelsPerMetre, path, "PX3 crowd at the base view, night (2x nearest)");
            }
            finally
            {
                foreach (var s in created) UnityEngine.Object.DestroyImmediate(s);
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        /// <summary>Saves a bottom-up buffer at twice its size, nearest-neighbour.</summary>
        static void Save(Color32[] px, int w, int h, string path, string tool)
        {
            var o = new Color32[w * h * 4];
            for (int y = 0; y < h * 2; y++)
            for (int x = 0; x < w * 2; x++)
            {
                var c = px[(y / 2) * w + x / 2];
                c.a = 255;
                o[y * w * 2 + x] = c;
            }
            var tex = new Texture2D(w * 2, h * 2, TextureFormat.RGBA32, false);
            tex.SetPixels32(o);
            tex.Apply();
            RenderTag.Save(tex, path, tool);
            UnityEngine.Object.DestroyImmediate(tex);
            Debug.Log("SpriteStills: wrote " + path);
        }
    }
}
