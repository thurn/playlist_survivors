using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Champions.Render;
using Champions.Render.Crowd;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Champions.EditorTools.EdgeCuts
{
    /// <summary>
    /// A cut's review sheet (plan.md, Rendering &gt; Edge cuts): the cut at the game camera's angle, drawn by the edge
    /// crowd itself with the pipeline's post, beside the vendor original (flat grey, lit), at three sizes on screen
    /// and in its idle, walk and attack poses. Each size is a sheet (far.jpg, game.jpg, near.jpg) of six cells, twice
    /// the monster's height square: idle, walk and attack, each the original then the cut. The cells keep a 1080-row
    /// frame's pixel density at the camera's 30 degree field, so a monster is exactly as large as it would be in the
    /// game. Beside the sheets it writes review.json: per cell the
    /// cut's black share (the share of its silhouette, from a flat-white mask render, darker than L* 10 in the
    /// beauty render, and without the post's bloom), its height on screen, and the cut's counts.
    /// </summary>
    public static class EdgeCutReview
    {
        public const float PitchDeg = 60f, FovDeg = 30f, YawDeg = 160f;
        /// <summary>The sizes: the monster's standing height in pixels at 1080 rows; the middle is game scale.</summary>
        public static readonly (string Name, float HeightPx)[] Sizes = { ("far", 56), ("game", 113), ("near", 226) };
        /// <summary>The poses: a clip role and where in it (a share of its length, or its impact when it has one).</summary>
        static readonly (string Name, string Role, float At)[] Poses = { ("idle", "idle", 0.3f), ("walk", "walk", 0.25f), ("attack", "attack", -1) };
        public const float BlackLStar = 10f;
        static readonly Color32 Tint = new Color32(90, 220, 255, 255);

        public static void Run(EdgeCutModel model, string outDir)
        {
            var lib = EdgeCutLibrary.Load() ?? throw new Exception("EdgeCutReview: no edge-cut library; bake first");
            int typeIndex = lib.IndexOf(model.id);
            if (typeIndex < 0) throw new Exception($"EdgeCutReview: {model.id} is not baked");
            var type = lib.Types[typeIndex];
            Directory.CreateDirectory(outDir);

            var camera = new GameObject("ReviewCamera").AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
            camera.nearClipPlane = 0.3f;
            camera.farClipPlane = 500f;
            var data = camera.GetUniversalAdditionalCameraData();
            var light = new GameObject("ReviewLight").AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 2f;
            light.transform.rotation = Quaternion.Euler(50, -30, 0);
            var lit = Shader.Find("Universal Render Pipeline/Lit") ?? throw new Exception("EdgeCutReview: no URP Lit shader");
            var grey = new Material(lit) { color = new Color(0.6f, 0.6f, 0.62f) };
            var original = model.Spawn();
            foreach (var r in original.GetComponentsInChildren<Renderer>(true))
                r.sharedMaterials = Enumerable.Repeat(grey, r.sharedMaterials.Length).ToArray();
            foreach (var smr in original.GetComponentsInChildren<SkinnedMeshRenderer>(true)) smr.forceMatrixRecalculationPerRender = true;
            original.transform.rotation = Quaternion.Euler(0, YawDeg, 0);
            var animator = original.GetComponentInChildren<Animator>();
            var crowd = new EdgeCrowd(lib, 1);
            var cells = new List<string>();
            RenderTexture rt = null;
            Texture2D sheet = null;
            AnimationMode.StartAnimationMode();
            try
            {
                for (int si = 0; si < Sizes.Length; si++)
                {
                    var (sizeName, px) = Sizes[si];
                    int cell = Mathf.Max(128, 4 * Mathf.RoundToInt(px / 2));
                    if (rt != null) rt.Release();
                    rt = new RenderTexture(cell, cell, 24, RenderTextureFormat.ARGB32) { name = "EdgeCutReview" };
                    rt.Create();
                    camera.targetTexture = rt;
                    // The cell's field keeps a 1080-row frame's pixels per radian at the game's field of view.
                    camera.fieldOfView = 2 * Mathf.Rad2Deg * Mathf.Atan(Mathf.Tan(0.5f * FovDeg * Mathf.Deg2Rad) * cell / 1080f);
                    if (sheet != null) UnityEngine.Object.DestroyImmediate(sheet);
                    sheet = new Texture2D(6 * cell, cell, TextureFormat.RGB24, false);
                    float depth = type.HeightM / Mathf.Tan(0.5f * FovDeg * Mathf.Deg2Rad) * 540f / px;
                    var focus = new Vector3(0, 0.5f * type.HeightM, 0);
                    camera.transform.rotation = Quaternion.Euler(PitchDeg, 0, 0);
                    camera.transform.position = focus - camera.transform.forward * depth;
                    for (int pi = 0; pi < Poses.Length; pi++)
                    {
                        var (poseName, role, at) = Poses[pi];
                        int clipIndex = type.Clip(role);
                        if (clipIndex < 0) throw new Exception($"EdgeCutReview: {model.id} has no {role} clip");
                        var clip = type.Clips[clipIndex];
                        float time = at < 0 ? (clip.Impact >= 0 ? clip.Impact : 0.5f * clip.Length) : at * clip.Length;
                        // The original, posed by its clip.
                        original.SetActive(true);
                        AnimationMode.BeginSampling();
                        AnimationMode.SampleAnimationClip(animator.gameObject, model.LoadClip(model.clips.First(c => c.role == role)), time);
                        AnimationMode.EndSampling();
                        data.renderPostProcessing = false;
                        Render(camera, rt, sheet, 2 * pi * cell, true);
                        original.SetActive(false);

                        // The cut: its mask, then the beauty with the pipeline's post.
                        var body = new MonsterInstance
                        {
                            Position = Vector3.zero, Yaw = YawDeg * Mathf.Deg2Rad, ClipA = (uint)clipIndex, TimeA = time,
                            ClipB = (uint)clipIndex, TimeB = time, Blend = 1, Scale = 1, Tint = MonsterInstance.Pack(Tint),
                            Flags = (uint)CrowdFlags.Alive,
                        };
                        var mask = DrawCut(crowd, typeIndex, camera, rt, body, mask: true, post: false);
                        var plain = DrawCut(crowd, typeIndex, camera, rt, body, mask: false, post: false);
                        var beauty = DrawCut(crowd, typeIndex, camera, rt, body, mask: false, post: true);
                        sheet.SetPixels32(2 * pi * cell + cell, 0, cell, cell, beauty);
                        var (share, silhouette, top, bottom) = BlackShare(mask, beauty, cell);
                        var (plainShare, _, _, _) = BlackShare(mask, plain, cell);
                        cells.Add(string.Format(CultureInfo.InvariantCulture,
                            "{{\"size\": \"{0}\", \"heightPx\": {1}, \"pose\": \"{2}\", \"clipTime\": {3:F3}, \"blackShare\": {4:F4}, " +
                            "\"blackShareWithoutPost\": {5:F4}, \"silhouettePx\": {6}, \"screenHeightPx\": {7}}}",
                            sizeName, px, poseName, time, share, plainShare, silhouette, top - bottom + 1));
                    }
                    sheet.Apply();
                    RenderTag.SaveJpeg(sheet, Path.Combine(outDir, sizeName + ".jpg"), $"champions edgecut review {model.id} {sizeName}");
                }
                var json = new StringBuilder();
                json.Append("{\n  \"model\": \"").Append(model.id).Append("\",\n  \"triangles\": ").Append(type.Triangles)
                    .Append(",\n  \"segments\": ").Append(type.Segments).Append(",\n  \"pieces\": ").Append(type.Pieces.Length)
                    .Append(",\n  \"entries\": ").Append(type.BoneCount)
                    .Append(",\n  \"camera\": {\"pitchDeg\": ").Append(PitchDeg).Append(", \"fovDeg\": ").Append(FovDeg)
                    .Append(", \"yawDeg\": ").Append(YawDeg).Append("},\n  \"blackLStar\": ")
                    .Append(BlackLStar.ToString(CultureInfo.InvariantCulture)).Append(",\n  \"cells\": [\n    ")
                    .Append(string.Join(",\n    ", cells)).Append("\n  ]\n}\n");
                File.WriteAllText(Path.Combine(outDir, "review.json"), json.ToString());
                Debug.Log($"EdgeCutReview: {model.id}: wrote its sheets and review.json to {outDir}");
            }
            finally
            {
                AnimationMode.StopAnimationMode();
                EdgeLook.Mask(false);
                crowd.Dispose();
                camera.targetTexture = null;
                if (rt != null) rt.Release();
                if (sheet != null) UnityEngine.Object.DestroyImmediate(sheet);
                UnityEngine.Object.DestroyImmediate(original);
                UnityEngine.Object.DestroyImmediate(camera.gameObject);
                UnityEngine.Object.DestroyImmediate(light.gameObject);
                UnityEngine.Object.DestroyImmediate(grey);
            }
        }

        static void Render(Camera camera, RenderTexture rt, Texture2D sheet, int x, bool warm)
        {
            if (warm) camera.Render(); // the first render of a fresh pose or material can come out stale
            camera.Render();
            RenderTexture.active = rt;
            sheet.ReadPixels(new Rect(0, 0, rt.width, rt.height), x, 0);
            RenderTexture.active = null;
        }

        static Color32[] DrawCut(EdgeCrowd crowd, int type, Camera camera, RenderTexture rt, MonsterInstance body, bool mask, bool post)
        {
            EdgeLook.Mask(mask);
            camera.GetUniversalAdditionalCameraData().renderPostProcessing = post;
            var slots = crowd.Instances(type, 1);
            slots[0] = body;
            crowd.Draw(camera);
            camera.Render();
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);
            RenderTexture.active = rt;
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            RenderTexture.active = null;
            var px = tex.GetPixels32();
            UnityEngine.Object.DestroyImmediate(tex);
            return px;
        }

        /// <summary>The share of the mask's pixels the beauty render leaves darker than L* <see cref="BlackLStar"/>, the mask's pixel count and rows.</summary>
        public static (float share, int silhouette, int top, int bottom) BlackShare(Color32[] mask, Color32[] beauty, int width)
        {
            int inside = 0, black = 0, top = -1, bottom = int.MaxValue;
            for (int i = 0; i < mask.Length; i++)
            {
                if (Mathf.Max(mask[i].r, Mathf.Max(mask[i].g, mask[i].b)) < 128) continue;
                inside++;
                int row = i / width;
                top = Math.Max(top, row);
                bottom = Math.Min(bottom, row);
                if (LStar(beauty[i]) < BlackLStar) black++;
            }
            return (inside > 0 ? (float)black / inside : 0, inside, top, bottom == int.MaxValue ? 0 : bottom);
        }

        /// <summary>CIE L* of an sRGB colour.</summary>
        public static float LStar(Color32 c)
        {
            float y = 0.2126f * Linear(c.r) + 0.7152f * Linear(c.g) + 0.0722f * Linear(c.b);
            return y > 0.008856f ? 116f * Mathf.Pow(y, 1f / 3f) - 16f : 903.3f * y;
        }

        static float Linear(byte v)
        {
            float s = v / 255f;
            return s <= 0.04045f ? s / 12.92f : Mathf.Pow((s + 0.055f) / 1.055f, 2.4f);
        }
    }
}
