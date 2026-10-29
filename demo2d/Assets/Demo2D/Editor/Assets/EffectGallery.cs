using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PS.Choreography;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Demo2D.EditorTools.Assets
{
    /// <summary>
    /// The CFXR effect candidates: the pool the manifest syncs (its entries used as "effect
    /// candidate: &lt;kind&gt;"), the checks that each prefab draws under URP's 2D renderer, and
    /// the gallery still per kind from which PX2 chose the effect list's candidates
    /// (Data/effect-candidates.json).
    /// </summary>
    public static class EffectGallery
    {
        const string UsePrefix = "effect candidate: ";
        public const string CandidatesPath = CharacterPacks.DataDir + "/effect-candidates.json";
        /// <summary>The times a gallery shows each effect at.</summary>
        public static readonly float[] Times = { 0.08f, 0.25f };
        /// <summary>
        /// CFXR effects are authored in a 3D world with y up. As authored, the top-down camera
        /// sees them from the side; laid flat, their ground plane (x, z) faces the camera.
        /// </summary>
        public static readonly bool[] Orientations = { false, true };
        public static readonly Quaternion Flat = Quaternion.Euler(-90, 0, 0);
        static readonly ShaderTagId[] Drawn = { new ShaderTagId("Universal2D"), new ShaderTagId("SRPDefaultUnlit") };

        /// <summary>The pool by kind, in manifest order: vendor prefab paths.</summary>
        public static Dictionary<string, List<string>> Pool()
        {
            var manifest = JsonValue.Parse(File.ReadAllText("manifest.json"));
            var pool = new Dictionary<string, List<string>>();
            foreach (var a in manifest["assets"].Items)
            {
                var use = a["use"].AsString() ?? "";
                if (!use.StartsWith(UsePrefix, StringComparison.Ordinal)) continue;
                var kind = use.Substring(UsePrefix.Length);
                if (!pool.TryGetValue(kind, out var list)) pool[kind] = list = new List<string>();
                list.Add(VendorPath(a["path"].AsString()));
            }
            return pool;
        }

        /// <summary>"Assets/JMO Assets/..." in a source is "Assets/Vendor/JMO Assets/..." here.</summary>
        public static string VendorPath(string sourcePath) => "Assets/Vendor/" + sourcePath.Substring("Assets/".Length);

        /// <summary>The chosen candidates by kind (vendor prefab paths), best first.</summary>
        public static Dictionary<string, List<string>> Candidates()
        {
            var j = JsonValue.Parse(File.ReadAllText(CandidatesPath))["kinds"];
            return j.Members.ToDictionary(kv => kv.Key, kv => kv.Value.Items.Select(c => VendorPath(c["path"].AsString())).ToList());
        }

        public static GameObject Spawn(string prefabPath, Transform parent, Vector2 at, bool flat = false)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath)
                         ?? throw new FileNotFoundException("EffectGallery: no prefab " + prefabPath);
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            go.transform.localPosition = new Vector3(at.x, at.y, 0);
            if (flat) go.transform.localRotation = Flat;
            foreach (var r in go.GetComponentsInChildren<ParticleSystemRenderer>()) r.sortingOrder = 100;
            return go;
        }

        /// <summary>Plays an effect's particle systems from the start to <paramref name="t"/> seconds.</summary>
        public static void Simulate(GameObject effect, float t)
        {
            foreach (var ps in effect.GetComponentsInChildren<ParticleSystem>())
                if (ps.transform.parent == null || ps.transform.parent.GetComponentInParent<ParticleSystem>() == null)
                    ps.Simulate(t, true, true, false);
        }

        /// <summary>Why an effect can't draw under the 2D renderer: missing scripts or meshes, and
        /// materials whose shader is missing, broken, unsupported or has no pass the renderer draws.</summary>
        public static List<string> Problems(GameObject effect)
        {
            var problems = new List<string>();
            foreach (var t in effect.GetComponentsInChildren<Transform>(true))
                if (GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject) > 0)
                    problems.Add($"{t.name}: a missing script");
            foreach (var r in effect.GetComponentsInChildren<Renderer>(true))
            {
                var materials = r.sharedMaterials.ToList();
                if (r is ParticleSystemRenderer p)
                {
                    if (p.renderMode == ParticleSystemRenderMode.Mesh && p.mesh == null)
                        problems.Add($"{r.name}: mesh particles without a mesh");
                    // A particle renderer's second slot is its trails', empty unless they're on.
                    var ps = r.GetComponent<ParticleSystem>();
                    if (!ps.trails.enabled && materials.Count > 1) materials.RemoveAt(1);
                }
                foreach (var m in materials)
                {
                    if (m == null) { problems.Add($"{r.name}: an empty material slot"); continue; }
                    var s = m.shader;
                    if (s == null || s.name == "Hidden/InternalErrorShader") problems.Add($"{r.name}: {m.name} has no shader");
                    else if (!s.isSupported || ShaderUtil.ShaderHasError(s)) problems.Add($"{r.name}: {m.name}'s shader {s.name} doesn't compile");
                    else if (!Enumerable.Range(0, s.passCount).Any(i => Drawn.Contains(s.FindPassTagValue(i, new ShaderTagId("LightMode")))))
                        problems.Add($"{r.name}: {m.name}'s shader {s.name} has no pass the 2D renderer draws");
                }
            }
            return problems;
        }

        /// <summary>
        /// How many pixels an effect changes, at its largest of the gallery's times and
        /// orientations, in an 8×8 m view over forest ground, under the moon (<paramref name="lit"/>)
        /// or with no 2D light.
        /// </summary>
        public static int DrawnPixels(string prefabPath, bool lit)
        {
            var root = new GameObject("Effect Check");
            try
            {
                var catalog = ForestTiles.Load();
                var ground = Ground(catalog, root.transform, new RectInt(-4, -4, 8, 8));
                if (lit) PixelShots.GlobalLight(root.transform, true);
                var view = new RectInt(-4, -4, 8, 8);
                var empty = PixelShots.Render(view, PixelShots.NightSky);
                int best = 0;
                foreach (var flat in Orientations)
                foreach (var t in Times)
                {
                    var effect = Spawn(prefabPath, root.transform, Vector2.zero, flat);
                    Simulate(effect, t);
                    var px = PixelShots.Render(view, PixelShots.NightSky);
                    int n = 0;
                    for (int i = 0; i < px.Length; i++)
                        if (Math.Abs(px[i].r - empty[i].r) + Math.Abs(px[i].g - empty[i].g) + Math.Abs(px[i].b - empty[i].b) > 24) n++;
                    best = Math.Max(best, n);
                    UnityEngine.Object.DestroyImmediate(effect);
                }
                return best;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        /// <summary>A rectangle of the catalog's base grass, lit, behind everything.</summary>
        public static GameObject Ground(ForestTiles.Catalog c, Transform parent, RectInt world)
        {
            var sprite = ForestTiles.Sprites(c.Sheet, c.TilePx)[c.Base];
            var go = new GameObject("Ground");
            go.transform.SetParent(parent, false);
            for (int y = world.yMin; y < world.yMax; y++)
            for (int x = world.xMin; x < world.xMax; x++)
            {
                var r = new GameObject("Tile").AddComponent<SpriteRenderer>();
                r.transform.SetParent(go.transform, false);
                r.transform.localPosition = new Vector3(x + 0.5f, y + 0.5f, 0);
                r.sprite = sprite;
                r.sharedMaterial = PixelShots.SpriteLit;
                r.sortingOrder = -100;
            }
            return go;
        }

        /// <summary>
        /// One still per kind: the pool's prefabs in columns (5 m each); in rows, as authored then
        /// laid flat, each at the gallery's times; over forest ground at night with a soft light
        /// under each, the knight beside each for scale. Drawn at 960×544 grid pixels, kept at 2×.
        /// </summary>
        public static void Write(string dir)
        {
            Directory.CreateDirectory(dir);
            var catalog = ForestTiles.Load();
            var knight = CharacterPacks.Load()[0];
            var idle = knight.Frames()["idle_1"][0];
            foreach (var kv in Pool())
            {
                var root = new GameObject("Gallery");
                try
                {
                    var world = new RectInt(0, 0, 30, 17);
                    Ground(catalog, root.transform, world);
                    PixelShots.GlobalLight(root.transform, true);
                    var hero = HeroSprite(idle, knight.GameHeightPx);
                    for (int i = 0; i < kv.Value.Count; i++)
                    for (int row = 0; row < Orientations.Length * Times.Length; row++)
                    {
                        var centre = new Vector2(i * 5 + 3f, 16 - row * 4 - 2f);
                        var e = Spawn(kv.Value[i], root.transform, centre, Orientations[row / Times.Length]);
                        Simulate(e, Times[row % Times.Length]);
                        PixelShots.PointLight(root.transform, centre, new Color(1f, 0.9f, 0.8f), 3f, 0.6f);
                        var h = new GameObject("Knight").AddComponent<SpriteRenderer>();
                        h.transform.SetParent(root.transform, false);
                        h.transform.localPosition = new Vector3(i * 5 + 0.8f, 16 - row * 4 - 3.9f, 0);
                        h.sprite = hero;
                        h.sharedMaterial = PixelShots.SpriteLit;
                    }
                    var px = PixelShots.Render(world, PixelShots.NightSky, hdr: true);
                    int w = world.width * PixelShots.PixelsPerMetre, hgt = world.height * PixelShots.PixelsPerMetre;
                    var white = new Color32(235, 235, 235, 255);
                    PixelText.Draw(px, w, hgt, 4, hgt - 4, $"px2 cfxr gallery: {kv.Key}. rows: as authored at {Times[0]} s, {Times[1]} s; laid flat at {Times[0]} s, {Times[1]} s", white, 2);
                    for (int i = 0; i < kv.Value.Count; i++)
                        PixelText.Draw(px, w, hgt, i * 160 + 4, hgt - 24, $"{i + 1} {Short(kv.Value[i])}", white, 1);
                    var tex = PixelShots.Crop(px, w, new RectInt(0, 0, w, hgt), 2);
                    var path = Path.Combine(dir, $"cfxr-{kv.Key.Replace(' ', '-')}.jpg");
                    Runtime.RenderTag.Save(tex, path, "PX2 CFXR gallery " + kv.Key);
                    UnityEngine.Object.DestroyImmediate(tex);
                    Debug.Log("EffectGallery: wrote " + path);
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(root);
                }
            }
        }

        public static string Short(string prefabPath) => Path.GetFileNameWithoutExtension(prefabPath).Replace("CFXR ", "");

        /// <summary>A frame as a sprite at about its game height (its opaque height, in grid pixels).</summary>
        public static Sprite HeroSprite(string framePath, int gameHeightPx)
        {
            var tex = CharacterPacks.ReadPng(framePath);
            var b = ContactSheets.OpaqueBounds(new[] { tex });
            float ppu = b.height * (float)PixelShots.PixelsPerMetre / gameHeightPx;
            return Sprite.Create(tex, new Rect(b.x, b.y, b.width, b.height), new Vector2(0.5f, 0f), ppu);
        }
    }
}
