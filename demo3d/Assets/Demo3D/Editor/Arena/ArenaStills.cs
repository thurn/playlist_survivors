using System.Collections.Generic;
using System.IO;
using PS.Choreography;
using Demo3D.Runtime;
using Demo3D.Runtime.Arena;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Demo3D.EditorTools.Arena
{
    /// <summary>
    /// Renders the baked arena from the layout's beauty-shot positions through the game camera
    /// (TD9's look checks; TD15 owns the beauty shots themselves): the night look, the rung's
    /// lighting, and a few heroes standing at each shot (their shader takes the static shadow map
    /// at E2, the realtime cascades at E0, so the pair checks the map's alignment). Tagged JPEGs.
    /// `demo3d unity Demo3D.EditorTools.Arena.ArenaStills.Run [-rung e2] [-density 1] [-out evidence/td9]
    /// [-width 960] [-shots opening,courtyard] [-heroes]`; TD14's rung switches that change the look:
    /// `[-merged] [-texCap 1024] [-prepassOut] [-crop x,y,w,h,scale] [-task td14]`.
    /// </summary>
    public static class ArenaStills
    {
        public static void Run()
        {
            string rung = TaskArgs.Get("-rung", ArenaIndex.E2);
            float density = float.Parse(TaskArgs.Get("-density", "1"), System.Globalization.CultureInfo.InvariantCulture);
            string outDir = TaskArgs.Get("-out", "evidence/td9");
            int width = int.Parse(TaskArgs.Get("-width", "960"));
            var only = TaskArgs.Get("-shots");
            bool heroes = TaskArgs.Has("-heroes");
            var options = new ArenaOptions
            {
                Rung = rung, Density = density, Merged = TaskArgs.Has("-merged"),
                TexCap = int.Parse(TaskArgs.Get("-texCap", "0")), Prepass = !TaskArgs.Has("-prepassOut"),
            };
            string task = TaskArgs.Get("-task", "TD9");
            string label = TaskArgs.Get("-label", task + " arena still");
            var crop = TaskArgs.Get("-crop");
            string tag = (options.Merged ? "-merged" : "") + (options.TexCap > 0 ? "-tex" + options.TexCap : "") + (options.Prepass ? "" : "-prepassout");
            var path = ArenaBaker.ScenePath(rung, density, options.Merged);
            if (!File.Exists(path)) throw new System.Exception("ArenaStills: no scene " + path + " (run demo3d unity BakeArena)");
            var layoutJson = File.ReadAllText(ArenaBaker.LayoutPath);
            var layout = ArenaLayout.Parse(layoutJson);
            var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            var cam = GameCamera();
            ArenaPipeline.ApplyTextureCap(options.TexCap);
            var arena = ArenaRuntime.Attach(scene, options, ArenaLighting.Parse(layoutJson), cam);
            // Frame() fits the URP asset's shadow distance to each view: the asset is tracked, so put it back.
            var urpAsset = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            float savedShadowDistance = urpAsset != null ? urpAsset.shadowDistance : 0f;
            var placed = new List<GameObject>();
            try
            {
                var hero = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Baked/Roster/hero.prefab");
                Directory.CreateDirectory(outDir);
                var canopyAt = TaskArgs.Get("-canopyCheck");
                if (canopyAt != null && hero != null)
                {
                    CanopyCheck(arena, cam, hero, canopyAt, outDir, width, label, placed);
                    return;
                }
                foreach (var shot in layout.Shots)
                {
                    if (only != null && System.Array.IndexOf(only.Split(','), shot.Name) < 0) continue;
                    var focus = new Vector3(shot.At.X, 0, shot.At.Y);
                    if (heroes && hero != null)
                    {
                        var spots = new List<Vector3> { focus, focus + new Vector3(6, 0, 4), focus + new Vector3(-7, 0, -3) };
                        spots.AddRange(InStaticShade(arena, focus, 2));
                        foreach (var at in spots) placed.Add((GameObject)Object.Instantiate(hero, at, Quaternion.Euler(0, 160, 0)));
                    }
                    if (heroes) CanopyFade.SetHero(focus); else CanopyFade.Clear();
                    arena.FollowHero(focus);
                    Frame(cam, focus, shot.ViewWidthM);
                    var file = Path.Combine(outDir, $"arena-{rung}-{density:0.###}x{tag}-{shot.Name}.jpg");
                    Render(cam, file, width, $"{label} {options.Label()} {shot.Name}", crop);
                    foreach (var h in placed) Object.DestroyImmediate(h);
                    placed.Clear();
                }
            }
            finally
            {
                foreach (var h in placed) if (h != null) Object.DestroyImmediate(h);
                if (urpAsset != null) urpAsset.shadowDistance = savedShadowDistance;
                CanopyFade.Clear();
                arena.Dispose();
                ArenaPipeline.ApplyTextureCap(0);
                Object.DestroyImmediate(cam.gameObject);
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            }
        }

        /// <summary>
        /// TD15's canopy-fade check (`-canopyCheck x,y`): the hero stands just behind (north of, from
        /// the camera) the tallest tree within 12 m of (x, y), and the view is rendered with the fade
        /// off and on: canopy-off.jpg must hide the hero, canopy-on.jpg must show it.
        /// </summary>
        static void CanopyCheck(ArenaRuntime arena, Camera cam, GameObject hero, string at, string outDir, int width, string label, List<GameObject> placed)
        {
            var xy = at.Split(',');
            var near = new Vector3(float.Parse(xy[0], System.Globalization.CultureInfo.InvariantCulture), 0,
                float.Parse(xy[1], System.Globalization.CultureInfo.InvariantCulture));
            Renderer tree = null;
            foreach (var r in arena.Static)
            {
                if (!ArenaDressing.IsTree(PieceName(r)) || r.bounds.size.y < 5f) continue;
                var c = r.bounds.center;
                if (new Vector2(c.x - near.x, c.z - near.z).magnitude > 12f) continue;
                if (tree == null || r.bounds.size.y > tree.bounds.size.y) tree = r;
            }
            if (tree == null) throw new System.Exception("ArenaStills: no tall tree within 12 m of " + at);
            var b = tree.bounds;
            var feet = new Vector3(b.center.x, 0, b.center.z + Mathf.Min(b.extents.z, 2.5f) + 0.6f);
            placed.Add((GameObject)Object.Instantiate(hero, feet, Quaternion.Euler(0, 160, 0)));
            Frame(cam, feet, 24f);
            CanopyFade.Clear();
            Render(cam, Path.Combine(outDir, "canopy-off.jpg"), width, $"{label} canopy fade off ({tree.name})");
            CanopyFade.SetHero(feet);
            Render(cam, Path.Combine(outDir, "canopy-on.jpg"), width, $"{label} canopy fade on ({tree.name})");
            CanopyFade.Clear();
        }

        /// <summary>The kit piece a renderer belongs to: its ancestor directly under a Static group.</summary>
        static string PieceName(Renderer r)
        {
            var t = r.transform;
            while (t.parent != null && t.parent.parent != null && t.parent.parent.name != "Static") t = t.parent;
            return t.name;
        }

        /// <summary>
        /// Up to <paramref name="count"/> open ground points near <paramref name="focus"/> whose line
        /// to the moon passes through a tall kit piece's bounds: where a hero stands in tree shade.
        /// </summary>
        static IEnumerable<Vector3> InStaticShade(ArenaRuntime arena, Vector3 focus, int count)
        {
            var toLight = -arena.Moon.transform.forward;
            var tall = arena.Static.FindAll(r => r.bounds.size.y > 4f);
            int found = 0;
            for (int ring = 4; ring <= 16 && found < count; ring += 2)
            for (int k = 0; k < 16 && found < count; k++)
            {
                float a = k * Mathf.PI / 8;
                var p = focus + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * ring;
                var chest = p + Vector3.up * 1.2f;
                bool clearHere = true, shaded = false;
                foreach (var r in arena.Static)
                {
                    var b = r.bounds;
                    b.Expand(new Vector3(0.6f, 10f, 0.6f));
                    if (b.Contains(chest) && r.bounds.size.y < 4f) { clearHere = false; break; }
                }
                if (!clearHere) continue;
                foreach (var r in tall)
                {
                    if (r.bounds.Contains(chest)) { clearHere = false; break; }
                    if (r.bounds.IntersectRay(new Ray(chest, toLight), out float d) && d > 2f && d < 25f) shaded = true;
                }
                if (clearHere && shaded) { found++; yield return p; }
            }
        }

        public static Camera GameCamera()
        {
            var go = new GameObject("GameCamera");
            var cam = go.AddComponent<Camera>();
            cam.fieldOfView = 30f;
            cam.nearClipPlane = 0.5f;
            cam.farClipPlane = 400f;
            cam.allowMSAA = true;
            cam.allowHDR = true;
            var data = go.AddComponent<UniversalAdditionalCameraData>();
            data.renderPostProcessing = true;
            data.renderShadows = true;
            data.antialiasing = AntialiasingMode.None;
            return cam;
        }

        public static void Frame(Camera cam, Vector3 focus, float viewWidth)
        {
            float hfov = 2f * Mathf.Atan(Mathf.Tan(15f * Mathf.Deg2Rad) * 16f / 9f);
            float distance = viewWidth / (2f * Mathf.Tan(hfov * 0.5f));
            var rot = Quaternion.Euler(55f, 0, 0);
            cam.transform.SetPositionAndRotation(focus - rot * Vector3.forward * distance, rot);
            if (GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset urp)
                urp.shadowDistance = distance + viewWidth; // the bench rig fits it to the view likewise
        }

        /// <summary>
        /// Renders 1920×1080 and saves it at <paramref name="width"/>, or with <paramref name="crop"/>
        /// ("x,y,w,h,scale" in 1080p pixels from the top left) that region enlarged.
        /// </summary>
        public static void Render(Camera cam, string path, int width, string tool, string crop = null)
        {
            var rt = new RenderTexture(1920, 1080, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture small;
            if (crop != null)
            {
                var c = System.Array.ConvertAll(crop.Split(','), s => float.Parse(s, System.Globalization.CultureInfo.InvariantCulture));
                small = RenderTexture.GetTemporary((int)(c[2] * c[4]), (int)(c[3] * c[4]), 0, RenderTextureFormat.ARGB32);
                small.filterMode = FilterMode.Point;
                var resolved = RenderTexture.GetTemporary(1920, 1080, 0, RenderTextureFormat.ARGB32);
                Graphics.Blit(rt, resolved);
                // Texture v runs bottom-up; crops are given from the top.
                Graphics.Blit(resolved, small, new Vector2(c[2] / 1920f, c[3] / 1080f), new Vector2(c[0] / 1920f, 1f - (c[1] + c[3]) / 1080f));
                RenderTexture.ReleaseTemporary(resolved);
            }
            else
            {
                small = RenderTexture.GetTemporary(width, width * 9 / 16, 0, RenderTextureFormat.ARGB32);
                Graphics.Blit(rt, small);
            }
            RenderTexture.active = small;
            var tex = new Texture2D(small.width, small.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, small.width, small.height), 0, 0);
            tex.Apply();
            RenderTexture.active = null;
            RenderTag.Save(tex, path, tool);
            RenderTexture.ReleaseTemporary(small);
            cam.targetTexture = null;
            rt.Release();
            Object.DestroyImmediate(tex);
            Debug.Log("ArenaStills: wrote " + path);
        }
    }
}
