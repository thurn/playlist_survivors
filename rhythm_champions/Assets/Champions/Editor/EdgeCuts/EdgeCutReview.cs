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
    /// crowd itself with the pipeline's post, beside the vendor original (flat grey, lit), at three camera distances
    /// and in the poses its recipe names (idle, walk and attack unless it names others; a boss names its every
    /// attack). Each distance is a sheet (far.jpg, game.jpg, near.jpg) of square cells twice the larger of the
    /// monster's height and its reach from its middle over all clips, three poses to a row, each the original then the
    /// cut; a last row shows its shatter, the first pose broken by a blow away from the camera and its pieces at
    /// <see cref="ShatterAt"/> after the break. The cells keep a 1080-row frame's pixel density at the camera's 30
    /// degree field and every model stands at the same distances, so a monster is exactly
    /// as large as it would be in the game (a 0.3 m bat a sixth of a skeleton); a sheet wider than
    /// <see cref="MaxSheetPx"/> is saved scaled down by a whole factor, but measured at full size. Beside the sheets it
    /// writes review.json: per cell the cut's black share (the share of its silhouette, from a flat-white mask render,
    /// darker than L* 10 in the beauty render, and without the post's bloom), its height on screen, and the cut's
    /// counts.
    /// </summary>
    public static class EdgeCutReview
    {
        public const float PitchDeg = 60f, FovDeg = 30f, YawDeg = 160f;
        /// <summary>The camera distances, named by a <see cref="ReferenceHeightM"/> monster's standing height in pixels
        /// at 1080 rows there; the middle is game scale.</summary>
        public static readonly (string Name, float HeightPx)[] Sizes = { ("far", 56), ("game", 113), ("near", 226) };
        public const float ReferenceHeightM = 1.8f;
        const int PosesPerRow = 3;
        /// <summary>The shatter row's times after the break, one per cell, and the blow's launch distance (the sword's).</summary>
        static readonly float[] ShatterAt = { 0, 0.08f, 0.2f, 0.4f, 0.8f, 1.4f };
        const float BlowForce = 1.85f;
        const int MaxSheetPx = 6144;
        public const float BlackLStar = 10f;
        static readonly Color32 Tint = new Color32(90, 220, 255, 255);

        public static void Run(EdgeCutModel model, string outDir)
        {
            var lib = EdgeCutLibrary.Load() ?? throw new Exception("EdgeCutReview: no edge-cut library; bake first");
            int typeIndex = lib.IndexOf(model.id);
            if (typeIndex < 0) throw new Exception($"EdgeCutReview: {model.id} is not baked");
            var type = lib.Types[typeIndex];
            // The world at rest: no beat ring an earlier task left in this editor (the EditMode tests publish them),
            // which would flare the lines and bloom over the body.
            new BeatWave().Publish(0, Vector2.zero);
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
            var shatter = new EdgeShatter(lib, crowd.Palette);
            var cells = new List<string>();
            var scales = new List<string>();
            var poseClips = model.reviewPoses.Select(role => type.Clip(role) is var c && c >= 0 ? c
                : throw new Exception($"EdgeCutReview: {model.id} has no {role} clip")).ToArray();
            RenderTexture rt = null;
            Texture2D sheet = null;
            AnimationMode.StartAnimationMode();
            try
            {
                for (int si = 0; si < Sizes.Length; si++)
                {
                    var (sizeName, referencePx) = Sizes[si];
                    float perM = referencePx / ReferenceHeightM, px = type.HeightM * perM;
                    float reach = type.Radius + Mathf.Abs(type.CenterY - 0.5f * type.HeightM);
                    int cell = Mathf.Max(128, 4 * Mathf.RoundToInt(Mathf.Max(px, reach * perM) / 2));
                    if (rt != null) rt.Release();
                    rt = new RenderTexture(cell, cell, 24, RenderTextureFormat.ARGB32) { name = "EdgeCutReview" };
                    rt.Create();
                    camera.targetTexture = rt;
                    // The cell's field keeps a 1080-row frame's pixels per radian at the game's field of view.
                    camera.fieldOfView = 2 * Mathf.Rad2Deg * Mathf.Atan(Mathf.Tan(0.5f * FovDeg * Mathf.Deg2Rad) * cell / 1080f);
                    if (sheet != null) UnityEngine.Object.DestroyImmediate(sheet);
                    int columns = 2 * Mathf.Min(model.reviewPoses.Length, PosesPerRow);
                    int rows = (model.reviewPoses.Length + PosesPerRow - 1) / PosesPerRow + 1;
                    int shrink = Mathf.CeilToInt(columns * cell / (float)MaxSheetPx), saved = cell / shrink;
                    sheet = new Texture2D(columns * saved, rows * saved, TextureFormat.RGB24, false);
                    sheet.SetPixels32(new Color32[sheet.width * sheet.height]); // black where a short last row leaves cells empty
                    scales.Add(string.Format(CultureInfo.InvariantCulture, "\"{0}\": {1}", sizeName, 1f / shrink));
                    MonsterInstance first = default;
                    float depth = ReferenceHeightM / Mathf.Tan(0.5f * FovDeg * Mathf.Deg2Rad) * 540f / referencePx;
                    var focus = new Vector3(0, 0.5f * type.HeightM, 0);
                    camera.transform.rotation = Quaternion.Euler(PitchDeg, 0, 0);
                    camera.transform.position = focus - camera.transform.forward * depth;
                    for (int pi = 0; pi < model.reviewPoses.Length; pi++)
                    {
                        var role = model.reviewPoses[pi];
                        int clipIndex = poseClips[pi];
                        var clip = type.Clips[clipIndex];
                        float time = PoseTime(clip);
                        int x = 2 * (pi % PosesPerRow) * saved, y = (rows - 1 - pi / PosesPerRow) * saved;
                        // The original, posed and raised by its clip.
                        var source = model.clips.First(c => c.role == role);
                        original.SetActive(true);
                        original.transform.position = Vector3.up * source.liftM;
                        AnimationMode.BeginSampling();
                        AnimationMode.SampleAnimationClip(animator.gameObject, model.LoadClip(source), time);
                        AnimationMode.EndSampling();
                        data.renderPostProcessing = false;
                        Place(sheet, Render(camera, rt, true), cell, shrink, x, y);
                        original.SetActive(false);

                        // The cut: its mask, then the beauty with the pipeline's post.
                        var body = new MonsterInstance
                        {
                            Position = Vector3.zero, Yaw = YawDeg * Mathf.Deg2Rad, ClipA = (uint)clipIndex, TimeA = time,
                            ClipB = (uint)clipIndex, TimeB = time, Blend = 1, Scale = 1, Tint = MonsterInstance.Pack(Tint),
                            Flags = (uint)CrowdFlags.Alive,
                        };
                        if (pi == 0) first = body;
                        var mask = DrawCut(crowd, typeIndex, camera, rt, body, mask: true, post: false);
                        var plain = DrawCut(crowd, typeIndex, camera, rt, body, mask: false, post: false);
                        var beauty = DrawCut(crowd, typeIndex, camera, rt, body, mask: false, post: true);
                        Place(sheet, beauty, cell, shrink, x + saved, y);
                        var (share, silhouette, top, bottom) = BlackShare(mask, beauty, cell);
                        var (plainShare, _, _, _) = BlackShare(mask, plain, cell);
                        cells.Add(string.Format(CultureInfo.InvariantCulture,
                            "{{\"size\": \"{0}\", \"heightPx\": {1:0.#}, \"pose\": \"{2}\", \"clipTime\": {3:F3}, \"blackShare\": {4:F4}, " +
                            "\"blackShareWithoutPost\": {5:F4}, \"silhouettePx\": {6}, \"screenHeightPx\": {7}}}",
                            sizeName, px, role, time, share, plainShare, silhouette, top - bottom + 1));
                    }
                    // Each distance breaks it afresh, after the last distance's pieces are gone.
                    Shatter(crowd, shatter, typeIndex, camera, rt, sheet, first, 10.0 * (si + 1), Mathf.Min(columns, ShatterAt.Length), cell, shrink);
                    sheet.Apply();
                    RenderTag.SaveJpeg(sheet, Path.Combine(outDir, sizeName + ".jpg"), $"champions edgecut review {model.id} {sizeName}");
                }
                var json = new StringBuilder();
                json.Append("{\n  \"model\": \"").Append(model.id).Append("\",\n  \"triangles\": ").Append(type.Triangles)
                    .Append(",\n  \"segments\": ").Append(type.Segments).Append(",\n  \"pieces\": ").Append(type.Pieces.Length)
                    .Append(",\n  \"entries\": ").Append(type.BoneCount)
                    .Append(",\n  \"camera\": {\"pitchDeg\": ").Append(PitchDeg).Append(", \"fovDeg\": ").Append(FovDeg)
                    .Append(", \"yawDeg\": ").Append(YawDeg).Append("},\n  \"blackLStar\": ")
                    .Append(BlackLStar.ToString(CultureInfo.InvariantCulture))
                    .Append(",\n  \"sheetScales\": {").Append(string.Join(", ", scales)).Append("},\n  \"cells\": [\n    ")
                    .Append(string.Join(",\n    ", cells)).Append("\n  ]\n}\n");
                File.WriteAllText(Path.Combine(outDir, "review.json"), json.ToString());
                Debug.Log($"EdgeCutReview: {model.id}: wrote its sheets and review.json to {outDir}");
            }
            finally
            {
                AnimationMode.StopAnimationMode();
                EdgeLook.Mask(false);
                shatter.Dispose();
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

        /// <summary>Where a pose samples its clip: at its impact when it has one, else a share of its length.</summary>
        static float PoseTime(EdgeClip clip) =>
            clip.Impact >= 0 ? clip.Impact : (clip.Role == "idle" ? 0.3f : clip.Role == "walk" ? 0.25f : 0.5f) * clip.Length;

        /// <summary>The camera's render, read back.</summary>
        static Color32[] Render(Camera camera, RenderTexture rt, bool warm)
        {
            if (warm) camera.Render(); // the first render of a fresh pose or material can come out stale
            camera.Render();
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);
            RenderTexture.active = rt;
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            RenderTexture.active = null;
            var px = tex.GetPixels32();
            UnityEngine.Object.DestroyImmediate(tex);
            return px;
        }

        /// <summary>A cell's pixels into the sheet at (x, y), each <paramref name="shrink"/> square of them averaged.</summary>
        static void Place(Texture2D sheet, Color32[] px, int cell, int shrink, int x, int y)
        {
            if (shrink == 1)
            {
                sheet.SetPixels32(x, y, cell, cell, px);
                return;
            }
            int n = cell / shrink, area = shrink * shrink;
            var o = new Color32[n * n];
            for (int j = 0; j < n; j++)
            for (int i = 0; i < n; i++)
            {
                int r = 0, g = 0, b = 0;
                for (int dy = 0; dy < shrink; dy++)
                for (int dx = 0; dx < shrink; dx++)
                {
                    var c = px[(j * shrink + dy) * cell + i * shrink + dx];
                    r += c.r;
                    g += c.g;
                    b += c.b;
                }
                o[j * n + i] = new Color32((byte)(r / area), (byte)(g / area), (byte)(b / area), 255);
            }
            sheet.SetPixels32(x, y, n, n, o);
        }

        /// <summary>
        /// The shatter row along the sheet's bottom: <paramref name="body"/> broken at song time <paramref name="broke"/>
        /// by a blow away from the camera, then drawn with the post at each of the first <paramref name="count"/>
        /// <see cref="ShatterAt"/> times after.
        /// </summary>
        static void Shatter(EdgeCrowd crowd, EdgeShatter shatter, int type, Camera camera, RenderTexture rt, Texture2D sheet,
            MonsterInstance body, double broke, int count, int cell, int shrink)
        {
            EdgeLook.Mask(false);
            camera.GetUniversalAdditionalCameraData().renderPostProcessing = true;
            for (int k = 0; k < count; k++)
            {
                double now = broke + ShatterAt[k];
                body.Flags = (uint)(k == 0 ? CrowdFlags.Dying : CrowdFlags.Hidden);
                if (k == 0) shatter.Break(type, 0, body, 1, now, 0, 1, BlowForce);
                var slots = crowd.Instances(type, 1);
                slots[0] = body;
                crowd.Draw(camera);
                shatter.Frame(now, camera);
                Place(sheet, Render(camera, rt, false), cell, shrink, k * (cell / shrink), 0);
            }
        }

        static Color32[] DrawCut(EdgeCrowd crowd, int type, Camera camera, RenderTexture rt, MonsterInstance body, bool mask, bool post)
        {
            EdgeLook.Mask(mask);
            camera.GetUniversalAdditionalCameraData().renderPostProcessing = post;
            var slots = crowd.Instances(type, 1);
            slots[0] = body;
            crowd.Draw(camera);
            return Render(camera, rt, false);
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
