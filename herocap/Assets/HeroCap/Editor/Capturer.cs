using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Assets.HeroEditor.Common.Scripts.CharacterScripts;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace HeroCap.EditorTools
{
    /// <summary>
    /// Captures HeroEditor characters to transparent PNG frames (`herocap capture`,
    /// docs/archive/design/heroeditor-native.md). Each recipe is built on Human.prefab with
    /// Character.FromJson, and each of its clips is sampled deterministically: both of
    /// Human.controller's layers are played at a normalized time and evaluated with a manual
    /// Animator.Update(0), never by wall-clock or coroutine timing. The Animator runs a copy of
    /// Human.controller's states and layers without its transitions and behaviours, so nothing
    /// moves a state on, and with its events off, so no expression event changes the face.
    /// Every frame is drawn twice, over black and over white, which recovers its true alpha, at
    /// `supersample` times the size and box-filtered down. A clip's frames are cropped to the
    /// union of their bounds, so they share one size and feet anchor.
    /// </summary>
    public static class Capturer
    {
        const string PrefabPath = "Assets/Vendor/HeroEditor/FantasyHeroes/Prefabs/Human.prefab";
        const string ControllerPath = "Assets/Baked/HeroCap/Capture.controller";
        // The canvas, in standing heights: wide enough for a two-handed swing or a fall either side,
        // tall enough for a jump or an overhead swing, with the feet half a height above its bottom.
        const float CanvasWidth = 3.4f, CanvasHeight = 2.8f, FeetAbove = 0.5f;
        const int Pad = 2;  // transparent pixels kept round a clip's union bounds
        const int FacePx = 256;

        public static void Run()
        {
            var project = Directory.GetParent(Application.dataPath)!.FullName;
            var outRoot = TaskArgs.Get("-out") ?? throw new Exception("Capturer: -out <dir> is required");
            var table = JsonUtility.FromJson<ClipTable>(File.ReadAllText(Path.Combine(project, "clips.json")));
            var fps = TaskArgs.Get("-fps");
            if (fps != null) table.fps = float.Parse(fps, CultureInfo.InvariantCulture);
            var onlyRecipes = TaskArgs.All("-recipe");
            var onlyClips = TaskArgs.All("-clip");
            var recipes = Directory.GetFiles(Path.Combine(project, "recipes"), "*.json")
                .OrderBy(p => p, StringComparer.Ordinal)
                .Select(p => JsonUtility.FromJson<Recipe>(File.ReadAllText(p)))
                .Where(r => onlyRecipes.Count == 0 || onlyRecipes.Contains(r.atlas))
                .ToList();
            if (recipes.Count == 0) throw new Exception("Capturer: no recipe matches " + string.Join(", ", onlyRecipes));

            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath)
                         ?? throw new Exception($"Capturer: no {PrefabPath}; run `herocap setup`");
            var controller = BuildController(prefab, table);
            var cam = new GameObject("HeroCap Camera").AddComponent<Camera>();
            cam.enabled = false;
            cam.orthographic = true;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.allowHDR = cam.allowMSAA = false;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 100;
            var problems = new List<string>();
            var started = DateTime.UtcNow;
            var total = 0;
            try
            {
                var hero = JsonUtility.FromJson<Recipe>(File.ReadAllText(Path.Combine(project, "recipes", "hero.json")));
                var ppu = MeasurePpu(hero, prefab, controller, table, cam);
                foreach (var recipe in recipes)
                    total += CaptureRecipe(recipe, prefab, controller, table, ppu, cam, outRoot, onlyClips, problems);
                Debug.Log($"Capturer: {ppu:F2} px per unit");
            }
            finally
            {
                Object.DestroyImmediate(cam.gameObject);
            }
            Debug.Log($"Capturer: {total} frames of {recipes.Count} recipes at {table.fps} fps " +
                      $"in {(DateTime.UtcNow - started).TotalSeconds:F1} s -> {outRoot}");
            if (problems.Count > 0) throw new Exception("Capturer:\n  " + string.Join("\n  ", problems));
        }

        /// <summary>
        /// Human.controller's two layers (Upper, and Lower overriding it at full weight) with every
        /// state and motion, but no transitions, behaviours or speed parameters.
        /// </summary>
        static AnimatorController BuildController(GameObject prefab, ClipTable table)
        {
            var source = prefab.GetComponentInChildren<Animator>().runtimeAnimatorController as AnimatorController
                         ?? throw new Exception("Capturer: Human.prefab has no AnimatorController");
            if (source.layers.Length != 2 || source.layers[0].name != "Upper" || source.layers[1].name != "Lower")
                throw new Exception("Capturer: Human.controller's layers are not Upper and Lower");
            Directory.CreateDirectory(Path.GetDirectoryName(ControllerPath)!);
            AssetDatabase.DeleteAsset(ControllerPath);
            var c = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            foreach (var p in source.parameters) c.AddParameter(p.name, p.type);  // HeroEditor's code sets them
            c.AddLayer("Lower");
            var layers = c.layers;
            layers[1].blendingMode = AnimatorLayerBlendingMode.Override;
            layers[1].defaultWeight = 1;
            c.layers = layers;
            for (var i = 0; i < 2; i++)
            {
                var sm = c.layers[i].stateMachine;
                foreach (var (name, motion) in States(source.layers[i].stateMachine))
                    sm.AddState(name).motion = motion;
            }
            AssetDatabase.SaveAssets();
            foreach (var clip in table.clips)
            {
                if (Motion(c, 0, clip.upper) == null) throw new Exception($"Capturer: {clip.name}: no upper-body clip {clip.upper}");
                if (Motion(c, 1, clip.lower) == null) throw new Exception($"Capturer: {clip.name}: no lower-body clip {clip.lower}");
            }
            return c;
        }

        static IEnumerable<(string, Motion)> States(AnimatorStateMachine sm) =>
            sm.states.Select(s => (s.state.name, s.state.motion))
                .Concat(sm.stateMachines.SelectMany(child => States(child.stateMachine)));

        static AnimationClip Motion(AnimatorController c, int layer, string state) =>
            c.layers[layer].stateMachine.states.FirstOrDefault(s => s.state.name == state).state?.motion as AnimationClip;

        /// <summary>
        /// A character built from the recipe, posed idle with its feet (where HeroEditor's shadow
        /// sprite sits) at the origin. The shadow is hidden: native draws its own.
        /// </summary>
        static Character Spawn(GameObject prefab, AnimatorController controller, Recipe recipe, ClipTable table)
        {
            var go = (GameObject)Object.Instantiate(prefab);
            var shadow = go.GetComponentsInChildren<SpriteRenderer>(true).First(sr => sr.name == "Shadow").transform;
            var ch = go.GetComponent<Character>();
            ch.Animator.runtimeAnimatorController = controller;
            ch.Animator.fireEvents = false;
            if (recipe != null)
            {
                ch.FromJson(recipe.ToCharacterJson(ch));
                go.transform.localScale = Vector3.one * recipe.scale;
            }
            ch.Animator.Rebind();
            ch.OnEnable();  // the hair mask's sorting range; Unity calls it only in play mode
            Pose(ch, table.clips.First(c => c.name == "idle"), 0);
            go.transform.position -= shadow.position;
            shadow.gameObject.SetActive(false);
            return ch;
        }

        static void Pose(Character ch, ClipTable.Clip clip, float t)
        {
            ch.Animator.Play(clip.upper, 0, t);
            ch.Animator.Play(clip.lower, 1, t);
            ch.Animator.Update(0);
        }

        static IEnumerable<SpriteRenderer> Drawn(Character ch) =>
            ch.GetComponentsInChildren<SpriteRenderer>().Where(r => r.enabled && r.sprite != null);

        /// <summary>
        /// Pixels per unit: the hero, idle, stands targetHeightPx tall, as drawn (a sprite's bounds
        /// include its transparent margins, so they can't measure it).
        /// </summary>
        static float MeasurePpu(Recipe hero, GameObject prefab, AnimatorController controller, ClipTable table, Camera cam)
        {
            const int size = 1024;
            var ch = Spawn(prefab, controller, hero, table);
            var rt = new RenderTexture(size, size, 24, RenderTextureFormat.ARGB32);
            var read = new Texture2D(size, size, TextureFormat.RGBA32, false);
            try
            {
                var b = Drawn(ch).Select(r => r.bounds).Aggregate((u, x) => { u.Encapsulate(x); return u; });
                var span = Math.Max(b.size.x, b.size.y);
                cam.targetTexture = rt;
                cam.aspect = 1;
                cam.orthographicSize = span / 2;
                cam.transform.position = new Vector3(b.center.x, b.center.y, -10);
                var (_, y0, _, y1) = Bounds(Shoot(cam, rt, read, 1), size, size);
                return table.targetHeightPx / ((y1 - y0 + 1) * span / size) / hero.scale;
            }
            finally
            {
                cam.targetTexture = null;
                Object.DestroyImmediate(ch.gameObject);
                Object.DestroyImmediate(rt);
                Object.DestroyImmediate(read);
            }
        }

        /// <summary>The opaque pixels' bounds, inclusive, or x1 &lt; 0 if none.</summary>
        static (int x0, int y0, int x1, int y1) Bounds(Color32[] px, int w, int h)
        {
            int x0 = w, y0 = h, x1 = -1, y1 = -1;
            for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
                if (px[y * w + x].a > 0)
                {
                    x0 = Math.Min(x0, x);
                    x1 = Math.Max(x1, x);
                    y0 = Math.Min(y0, y);
                    y1 = Math.Max(y1, y);
                }
            return (x0, y0, x1, y1);
        }

        static int CaptureRecipe(Recipe recipe, GameObject prefab, AnimatorController controller, ClipTable table,
            float ppu, Camera cam, string outRoot, List<string> onlyClips, List<string> problems)
        {
            var dir = Path.Combine(outRoot, "frames", recipe.atlas);
            var manifestPath = Path.Combine(dir, "clips.json");
            var manifest = onlyClips.Count > 0 && File.Exists(manifestPath)
                ? JsonUtility.FromJson<Manifest>(File.ReadAllText(manifestPath))
                : new Manifest();
            if (onlyClips.Count == 0 && Directory.Exists(dir)) Directory.Delete(dir, true);
            Directory.CreateDirectory(dir);

            var ss = Math.Max(1, table.supersample);
            var stand = table.targetHeightPx * recipe.scale * ss;  // standing height, canvas pixels
            var w = (int)Math.Ceiling(CanvasWidth * stand / ss) * ss;
            var h = (int)Math.Ceiling(CanvasHeight * stand / ss) * ss;
            var feetX = w / 2;
            var feetY = (int)Math.Round(FeetAbove * stand / ss) * ss;  // from the bottom
            var unit = ppu * ss;
            cam.orthographicSize = h / (2 * unit);
            cam.aspect = (float)w / h;
            cam.transform.position = new Vector3((w / 2f - feetX) / unit, (h / 2f - feetY) / unit, -10);
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32);
            var read = new Texture2D(w, h, TextureFormat.RGBA32, false);
            cam.targetTexture = rt;

            var ch = Spawn(prefab, controller, recipe, table);
            var frames = 0;
            try
            {
                foreach (var name in recipe.clips.Where(c => onlyClips.Count == 0 || onlyClips.Contains(c)))
                {
                    var clip = table.clips.First(c => c.name == name);
                    var lead = clip.lead == "lower" ? Motion(controller, 1, clip.lower) : Motion(controller, 0, clip.upper);
                    var n = clip.loop ? Math.Max(1, (int)Math.Round(lead.length * table.fps))
                        : (int)Math.Round(lead.length * table.fps) + 1;
                    var shots = new List<Color32[]>();
                    for (var i = 0; i < n; i++)
                    {
                        Pose(ch, clip, clip.loop ? (float)i / n : (float)i / (n - 1));
                        shots.Add(Shoot(cam, rt, read, ss));
                    }
                    var entry = Write(dir, clip, shots, w / ss, h / ss, feetX / ss, feetY / ss, problems, recipe.atlas);
                    entry.fps = table.fps;
                    entry.lengthSeconds = lead.length;
                    entry.impact = Impact(clip, lead, n);
                    manifest.clips = manifest.clips.Where(c => c.name != name).Append(entry).ToArray();
                    frames += n;
                }
                if (onlyClips.Count == 0 || onlyClips.Contains("idle"))
                    WriteFace(ch, recipe, table, ppu, cam, Path.Combine(outRoot, "faces", recipe.atlas + ".png"));
            }
            finally
            {
                Object.DestroyImmediate(ch.gameObject);
                cam.targetTexture = null;
                Object.DestroyImmediate(rt);
                Object.DestroyImmediate(read);
            }
            manifest.atlas = recipe.atlas;
            manifest.role = recipe.role;
            manifest.scale = recipe.scale;
            manifest.ppu = ppu;
            manifest.fps = table.fps;
            manifest.standHeightPx = table.targetHeightPx * recipe.scale;
            manifest.clips = recipe.clips.Select(c => manifest.clips.FirstOrDefault(e => e.name == c))
                .Where(e => e != null).ToArray();
            File.WriteAllText(manifestPath, JsonUtility.ToJson(manifest, true) + "\n");
            return frames;
        }

        /// <summary>
        /// One frame: drawn over black (its premultiplied colour) and over white (which gives its
        /// coverage), then box-filtered from `ss` times the size, as straight-alpha pixels.
        /// </summary>
        static Color32[] Shoot(Camera cam, RenderTexture rt, Texture2D read, int ss)
        {
            Color32[] Draw(Color bg)
            {
                cam.backgroundColor = bg;
                cam.Render();
                RenderTexture.active = rt;
                read.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
                RenderTexture.active = null;
                return read.GetPixels32();
            }
            var black = Draw(new Color(0, 0, 0, 0));
            var white = Draw(Color.white);
            int w = rt.width / ss, h = rt.height / ss, area = ss * ss;
            var px = new Color32[w * h];
            for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
            {
                float r = 0, g = 0, b = 0, a = 0;
                for (var dy = 0; dy < ss; dy++)
                for (var dx = 0; dx < ss; dx++)
                {
                    var i = (y * ss + dy) * rt.width + x * ss + dx;
                    Color32 k = black[i], o = white[i];
                    a += 1 - ((o.r - k.r) + (o.g - k.g) + (o.b - k.b)) / (3 * 255f);
                    r += k.r;
                    g += k.g;
                    b += k.b;
                }
                a /= area;
                if (a <= 0.5f / 255) continue;
                var s = 1 / (a * area);
                px[y * w + x] = new Color32(Byte(r * s), Byte(g * s), Byte(b * s), Byte(a * 255));
            }
            return px;
        }

        static byte Byte(float v) => (byte)Math.Clamp((int)Math.Round(v), 0, 255);

        /// <summary>Crops a clip's frames to their union bounds and writes them, bottom-up rows to PNG.</summary>
        static Manifest.ClipEntry Write(string dir, ClipTable.Clip clip, List<Color32[]> shots, int w, int h,
            int feetX, int feetY, List<string> problems, string atlas)
        {
            int x0 = w, y0 = h, x1 = -1, y1 = -1;
            foreach (var px in shots)
            {
                var b = Bounds(px, w, h);
                if (b.x1 < 0) continue;
                x0 = Math.Min(x0, b.x0);
                y0 = Math.Min(y0, b.y0);
                x1 = Math.Max(x1, b.x1);
                y1 = Math.Max(y1, b.y1);
            }
            if (x1 < 0) throw new Exception($"Capturer: {atlas} {clip.name}: every frame is empty");
            if (x0 == 0 || y0 == 0 || x1 == w - 1 || y1 == h - 1)
                problems.Add($"{atlas} {clip.name}: the character leaves the canvas ({x0},{y0})-({x1},{y1}) of {w}x{h}");
            x0 = Math.Max(0, x0 - Pad);
            y0 = Math.Max(0, y0 - Pad);
            x1 = Math.Min(w - 1, x1 + Pad);
            y1 = Math.Min(h - 1, y1 + Pad);
            int cw = x1 - x0 + 1, ch = y1 - y0 + 1;
            var tex = new Texture2D(cw, ch, TextureFormat.RGBA32, false);
            var crop = new Color32[cw * ch];
            try
            {
                for (var i = 0; i < shots.Count; i++)
                {
                    for (var y = 0; y < ch; y++)
                        Array.Copy(shots[i], (y0 + y) * w + x0, crop, y * cw, cw);
                    tex.SetPixels32(crop);
                    File.WriteAllBytes(Path.Combine(dir, $"{clip.name}_{i:00}.png"), tex.EncodeToPNG());
                }
            }
            finally
            {
                Object.DestroyImmediate(tex);
            }
            return new Manifest.ClipEntry
            {
                name = clip.name, upper = clip.upper, lower = clip.lower, loop = clip.loop, frames = shots.Count,
                w = cw, h = ch, anchorX = feetX - x0, anchorY = y1 + 1 - feetY,  // anchors from the top left
            };
        }

        /// <summary>The strike's frame: the clip table's annotation, else the lead clip's `Hit` event; -1 if none.</summary>
        static int Impact(ClipTable.Clip clip, AnimationClip lead, int n)
        {
            var at = clip.impact;
            if (at < 0)
            {
                var hit = lead.events.FirstOrDefault(e => e.functionName == "CustomEvent" && e.stringParameter == "Hit");
                if (hit == null) return -1;
                at = hit.time / lead.length;
            }
            return clip.loop ? (int)Math.Round(at * n) % n : (int)Math.Round(at * (n - 1));
        }

        /// <summary>
        /// A head close-up, idle, for the face lineup: a square round the silhouette's top 60%
        /// (HeroEditor's heads are big), where the head and any helmet are.
        /// </summary>
        static void WriteFace(Character ch, Recipe recipe, ClipTable table, float ppu, Camera cam, string path)
        {
            Pose(ch, table.clips.First(c => c.name == "idle"), 0);
            var stand = table.targetHeightPx * recipe.scale / ppu;  // units
            var head = HeadBox(cam, stand);
            var side = Math.Max(head.width, head.height) * 1.1f;
            var ss = Math.Max(1, table.supersample);
            var rt = new RenderTexture(FacePx * ss, FacePx * ss, 24, RenderTextureFormat.ARGB32);
            var read = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);
            var tex = new Texture2D(FacePx, FacePx, TextureFormat.RGBA32, false);
            try
            {
                cam.targetTexture = rt;
                cam.aspect = 1;
                cam.orthographicSize = side / 2;
                cam.transform.position = new Vector3(head.center.x, head.center.y, -10);
                tex.SetPixels32(Shoot(cam, rt, read, ss));
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllBytes(path, tex.EncodeToPNG());
            }
            finally
            {
                cam.targetTexture = null;
                Object.DestroyImmediate(rt);
                Object.DestroyImmediate(read);
                Object.DestroyImmediate(tex);
            }
        }

        /// <summary>The bounds of the posed silhouette's top 60%, in world units (feet at the origin).</summary>
        static Rect HeadBox(Camera cam, float stand)
        {
            const int size = 512;
            var rt = new RenderTexture(size, size, 24, RenderTextureFormat.ARGB32);
            var read = new Texture2D(size, size, TextureFormat.RGBA32, false);
            try
            {
                var span = 3 * stand;
                cam.targetTexture = rt;
                cam.aspect = 1;
                cam.orthographicSize = span / 2;
                cam.transform.position = new Vector3(0, span / 2 - 0.5f * stand, -10);
                var px = Shoot(cam, rt, read, 1);
                var (_, _, _, y1) = Bounds(px, size, size);
                var rows = (int)(0.6f * stand * size / span);
                var y0 = Math.Max(0, y1 - rows);
                var (x0, _, x1, _) = Bounds(px.Skip(y0 * size).Take((y1 - y0 + 1) * size).ToArray(), size, y1 - y0 + 1);
                float World(int p) => p * span / size;
                return Rect.MinMaxRect(World(x0) - span / 2, World(y0) - 0.5f * stand,
                    World(x1 + 1) - span / 2, World(y1 + 1) - 0.5f * stand);
            }
            finally
            {
                cam.targetTexture = null;
                Object.DestroyImmediate(rt);
                Object.DestroyImmediate(read);
            }
        }

        /// <summary>frames/&lt;atlas&gt;/clips.json: what native's atlas bake needs of each clip.</summary>
        [Serializable]
        class Manifest
        {
            public string atlas, role, facing = "right";
            public float scale, ppu, fps, standHeightPx;
            public ClipEntry[] clips = Array.Empty<ClipEntry>();

            [Serializable]
            public class ClipEntry
            {
                public string name, upper, lower;
                public bool loop;
                public int frames, w, h, anchorX, anchorY, impact;
                public float fps, lengthSeconds;
            }
        }
    }
}
