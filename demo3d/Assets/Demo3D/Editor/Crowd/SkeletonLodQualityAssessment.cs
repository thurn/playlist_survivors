using System;
using System.Collections.Generic;
using System.IO;
using Demo3D.Runtime.Crowd;
using Newtonsoft.Json;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace Demo3D.EditorTools.Crowd
{
    /// <summary>Registered game-size silhouette and colour comparisons to the original.</summary>
    public static class SkeletonLodQualityAssessment
    {
        sealed class Row
        {
            public string Variant, Clip;
            public float Width, Yaw, Phase;
            public int OriginalPixels, CandidatePixels, UnionPixels;
            public double SilhouetteIoU, ForegroundRgbMae;
        }

        public static void Run()
        {
            if (SystemInfo.graphicsDeviceType != GraphicsDeviceType.Metal) throw new Exception("Quality assessment requires Metal");
            EditorSceneManager.OpenScene(SkeletonLodPreviewBuilder.ScenePath);
            var preview = UnityEngine.Object.FindAnyObjectByType<SkeletonLodPreview>();
            float previousMask = Shader.GetGlobalFloat("_Demo3DIdMask");
            var rows = new List<Row>();
            var output = TaskArgs.Get("-qualityOut", "captures/skeleton-lod-cost/quality.json");
            var readback = new Texture2D(512, 512, TextureFormat.RGB24, false);
            try
            {
                preview.Initialize(); preview.Playing = false; preview.Blend = 1;
                foreach (float width in new[] { 30f, 40f, 55f })
                foreach (float yaw in new[] { 0f, 180f })
                foreach (int ci in preview.ClipOrder)
                foreach (float phase in new[] { .25f, .5f, .75f })
                {
                    preview.ViewWidth = width; preview.Yaw = yaw;
                    preview.ClipIndex = preview.PreviousClip = ci;
                    preview.ClipTime = preview.Library.Types[0].Clips[ci].Length * phase;
                    Shader.SetGlobalFloat("_Demo3DIdMask", 0);
                    for (int f = 0; f < 2; f++) preview.DrawFrame(true);
                    var colours = Read(preview, readback);
                    Shader.SetGlobalFloat("_Demo3DIdMask", 1);
                    for (int f = 0; f < 2; f++) preview.DrawFrame(true);
                    var masks = Read(preview, readback);
                    for (int v = 1; v < 3; v++)
                    {
                        int original = 0, candidate = 0, intersection = 0, union = 0;
                        double error = 0;
                        for (int p = 0; p < masks[0].Length; p++)
                        {
                            bool a = Foreground(masks[0][p]), b = Foreground(masks[v][p]);
                            int x = p % 512, y = p / 512;
                            if ((a || b) && (x == 0 || x == 511 || y == 0 || y == 511))
                                throw new Exception("quality crop touches a character: enlarge the ROI");
                            if (a) original++; if (b) candidate++; if (a && b) intersection++;
                            if (!a && !b) continue;
                            union++;
                            var c0 = colours[0][p]; var cv = colours[v][p];
                            error += (Math.Abs(c0.r - cv.r) + Math.Abs(c0.g - cv.g) + Math.Abs(c0.b - cv.b)) / (3d * 255);
                        }
                        if (original < 10 || candidate < 10) throw new Exception("missing foreground in quality capture");
                        rows.Add(new Row { Variant = preview.Library.Types[v].Id,
                            Clip = preview.Library.Types[0].Clips[ci].Name, Width = width, Yaw = yaw, Phase = phase,
                            OriginalPixels = original, CandidatePixels = candidate, UnionPixels = union,
                            SilhouetteIoU = intersection / (double)union, ForegroundRgbMae = error / union });
                    }
                }
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output)));
                File.WriteAllText(output, JsonConvert.SerializeObject(new
                {
                    Method = "270 registered game-size poses: 15 clips x 3 phases x 2 facings x 3 widths",
                    Resolution = "1920x1080 MSAA4 Metal; centre 512x512 ROI, border checked",
                    Silhouette = "real CrowdSkinned _Demo3DIdMask white, threshold RGB>200; own shadows excluded from white mask",
                    Colour = "mean absolute RGB error on union foreground; uncalibrated comparison, not a fidelity pass threshold",
                    Rows = rows,
                }, Formatting.Indented));
                Debug.Log("LOD quality complete: " + rows.Count + " variant/pose comparisons; " + output);
            }
            finally
            {
                Shader.SetGlobalFloat("_Demo3DIdMask", previousMask);
                preview.DisposePreview(); UnityEngine.Object.DestroyImmediate(preview.gameObject);
                UnityEngine.Object.DestroyImmediate(readback);
            }
        }

        static bool Foreground(Color32 p) => p.r > 200 && p.g > 200 && p.b > 200;

        static Color32[][] Read(SkeletonLodPreview preview, Texture2D readback)
        {
            var result = new Color32[3][];
            var previous = RenderTexture.active;
            try
            {
                for (int v = 0; v < 3; v++)
                {
                    RenderTexture.active = preview.Targets[v];
                    readback.ReadPixels(new Rect(704, 284, 512, 512), 0, 0);
                    readback.Apply(); result[v] = readback.GetPixels32();
                }
            }
            finally { RenderTexture.active = previous; }
            return result;
        }
    }
}
