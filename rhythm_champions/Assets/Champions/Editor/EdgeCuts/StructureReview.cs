using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Champions.Render;
using Champions.Render.Structures;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Champions.EditorTools.EdgeCuts
{
    /// <summary>
    /// A structure's review sheet (plan.md, Rendering &gt; Edge cuts, for a structure: "the same pipeline, static, plus their
    /// fill"): the cut drawn by the structure field itself in the first zone's fill and edge with the pipeline's post,
    /// beside the vendor original (flat grey, lit), at the game camera's angle, from three headings. Each camera distance
    /// is a sheet (far.jpg, game.jpg; the monsters' distances, so a structure is as large beside a 1.8 m monster as in the
    /// game) of six cells, each heading's original then cut, at a 1080-row frame's pixel density. Beside them review.json:
    /// the cut's counts and size, and per cell its height on screen.
    /// </summary>
    public static class StructureReview
    {
        public const float PitchDeg = EdgeCutReview.PitchDeg, FovDeg = EdgeCutReview.FovDeg;
        /// <summary>The sim's angles (degrees from +x toward +z) the structure is turned to, one per pair of cells.</summary>
        public static readonly float[] HeadingsDeg = { 20, 110, 235 };
        /// <summary>The monsters' far and game distances; a structure's near sheet would pass a sheet's width.</summary>
        static readonly (string Name, float HeightPx)[] Sizes = EdgeCutReview.Sizes.Take(2).ToArray();
        const int MaxCell = 1024;

        public static void Run(EdgeCutModel model, string outDir)
        {
            var lib = StructureLibrary.Load() ?? throw new Exception("StructureReview: no structure library; bake first");
            int typeIndex = lib.IndexOf(model.id);
            if (typeIndex < 0) throw new Exception($"StructureReview: {model.id} is not baked");
            var type = lib.Types[typeIndex];
            var zones = new ZoneSweep(Palette.Load(Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Content"))).Zones);
            zones.Publish(0, Vector2.zero);
            new BeatWave().Publish(0, Vector2.zero); // at rest, as EdgeCutReview: no ring an earlier task left flares the edges
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
            var lit = Shader.Find("Universal Render Pipeline/Lit") ?? throw new Exception("StructureReview: no URP Lit shader");
            var grey = new Material(lit) { color = new Color(0.6f, 0.6f, 0.62f) };
            var original = model.Spawn();
            foreach (var r in original.GetComponentsInChildren<Renderer>(true))
                r.sharedMaterials = Enumerable.Repeat(grey, r.sharedMaterials.Length).ToArray();
            // The original stood on its corner; the cut on its middle: the original's middle over the origin too.
            var bounds = original.GetComponentsInChildren<Renderer>().Select(r => r.bounds).Aggregate((a, b) => { a.Encapsulate(b); return a; });
            var middle = new Vector3(bounds.center.x, 0, bounds.center.z) - original.transform.position;
            var field = new StructureRenderer(lib, 1);
            var cells = new List<string>();
            RenderTexture rt = null;
            Texture2D sheet = null;
            try
            {
                float radius = 0.5f * Mathf.Sqrt(type.ExtentM.x * type.ExtentM.x + type.ExtentM.y * type.ExtentM.y + type.HeightM * type.HeightM);
                foreach (var (sizeName, referencePx) in Sizes)
                {
                    float perM = referencePx / EdgeCutReview.ReferenceHeightM;
                    int cell = Mathf.Clamp(4 * Mathf.RoundToInt(1.1f * radius * perM / 2), 128, MaxCell);
                    if (rt != null) rt.Release();
                    rt = new RenderTexture(cell, cell, 24, RenderTextureFormat.ARGB32) { name = "StructureReview" };
                    rt.Create();
                    camera.targetTexture = rt;
                    camera.fieldOfView = 2 * Mathf.Rad2Deg * Mathf.Atan(Mathf.Tan(0.5f * FovDeg * Mathf.Deg2Rad) * cell / 1080f);
                    if (sheet != null) UnityEngine.Object.DestroyImmediate(sheet);
                    sheet = new Texture2D(2 * HeadingsDeg.Length * cell, cell, TextureFormat.RGB24, false);
                    float depth = EdgeCutReview.ReferenceHeightM / Mathf.Tan(0.5f * FovDeg * Mathf.Deg2Rad) * 540f / referencePx;
                    camera.transform.rotation = Quaternion.Euler(PitchDeg, 0, 0);
                    camera.transform.position = new Vector3(0, 0.5f * type.HeightM, 0) - camera.transform.forward * depth;
                    for (int h = 0; h < HeadingsDeg.Length; h++)
                    {
                        float angle = HeadingsDeg[h] * Mathf.Deg2Rad;
                        var turn = Quaternion.Euler(0, -HeadingsDeg[h], 0);
                        original.SetActive(true);
                        original.transform.rotation = turn;
                        original.transform.position = -(turn * middle);
                        data.renderPostProcessing = false;
                        Render(camera, rt, sheet, 2 * h * cell);
                        original.SetActive(false);

                        field.Remove(0);
                        field.Add(0, typeIndex, Vector2.zero, angle);
                        field.Draw(camera);
                        data.renderPostProcessing = true;
                        Render(camera, rt, sheet, 2 * h * cell + cell, field);
                        cells.Add(string.Format(CultureInfo.InvariantCulture, "{{\"size\": \"{0}\", \"headingDeg\": {1:0.#}, \"heightPx\": {2:0.#}}}",
                            sizeName, HeadingsDeg[h], type.HeightM * perM));
                    }
                    sheet.Apply();
                    RenderTag.SaveJpeg(sheet, Path.Combine(outDir, sizeName + ".jpg"), $"champions edgecut review {model.id} {sizeName}");
                }
                var json = new StringBuilder();
                json.Append("{\n  \"model\": \"").Append(model.id).Append("\",\n  \"triangles\": ").Append(type.Triangles)
                    .Append(",\n  \"segments\": ").Append(type.Segments).Append(",\n  \"pieces\": ").Append(type.Pieces.Length)
                    .Append(",\n  \"heightM\": ").Append(type.HeightM.ToString("0.###", CultureInfo.InvariantCulture))
                    .Append(",\n  \"extentM\": [").Append(type.ExtentM.x.ToString("0.###", CultureInfo.InvariantCulture)).Append(", ")
                    .Append(type.ExtentM.y.ToString("0.###", CultureInfo.InvariantCulture)).Append("]")
                    .Append(",\n  \"camera\": {\"pitchDeg\": ").Append(PitchDeg).Append(", \"fovDeg\": ").Append(FovDeg)
                    .Append(", \"headingsDeg\": [").Append(string.Join(", ", HeadingsDeg.Select(d => d.ToString(CultureInfo.InvariantCulture))))
                    .Append("]},\n  \"cells\": [\n    ").Append(string.Join(",\n    ", cells)).Append("\n  ]\n}\n");
                File.WriteAllText(Path.Combine(outDir, "review.json"), json.ToString());
                Debug.Log($"StructureReview: {model.id}: wrote its sheets and review.json to {outDir}");
            }
            finally
            {
                field.Dispose();
                camera.targetTexture = null;
                if (rt != null) rt.Release();
                if (sheet != null) UnityEngine.Object.DestroyImmediate(sheet);
                UnityEngine.Object.DestroyImmediate(original);
                UnityEngine.Object.DestroyImmediate(camera.gameObject);
                UnityEngine.Object.DestroyImmediate(light.gameObject);
                UnityEngine.Object.DestroyImmediate(grey);
            }
        }

        /// <summary>Renders the camera into the sheet at column <paramref name="x"/>, twice (a fresh pose's first render can come out stale).</summary>
        static void Render(Camera camera, RenderTexture rt, Texture2D sheet, int x, StructureRenderer requeue = null)
        {
            camera.Render();
            requeue?.Queue(camera);
            camera.Render();
            RenderTexture.active = rt;
            sheet.ReadPixels(new Rect(0, 0, rt.width, rt.height), x, 0);
            RenderTexture.active = null;
        }
    }
}
