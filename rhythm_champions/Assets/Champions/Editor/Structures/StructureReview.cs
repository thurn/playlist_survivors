using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Champions.EditorTools.Monsters;
using Champions.Render;
using Champions.Render.Crowd;
using Champions.Render.Structures;
using Champions.Sim;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Champions.EditorTools.Structures
{
    /// <summary>
    /// A structure kind's review sheets (plan.md, Rendering &gt; The edge look), for judging by eye: the kind drawn by the
    /// structure field itself over the grid in the first zone, with the pipeline's post, from the game camera's angle:
    /// - KIND-game.jpg at the game camera's distance: the default look from three headings; under it `--look trailer`'s
    ///   from the same headings when the default tunes the model; and a row of its shatter, broken by a blow away
    ///   from the camera and shown at <see cref="ShatterAt"/> after the break;
    /// - KIND-near.jpg at half that distance, the default look from the three headings;
    /// - for `all`, field.jpg: every kind standing together (a hamlet, a tower and its walls, a grove) in the game camera's
    ///   whole frame, and field-trailer.jpg, the same in `--look trailer`.
    /// Every cell is a crop of a 1080-row frame at the game camera's field of view, drawn at twice that and scaled down
    /// (MonsterReview's), so a structure is exactly as large as in the game. Beside them review.json: each kind's numbers
    /// (diagnostics, never a verdict).
    /// </summary>
    public static class StructureReview
    {
        /// <summary>The sim's angles the structure is turned to (degrees from +x toward +z), one per cell.</summary>
        static readonly float[] HeadingsDeg = { 20, 110, 235 };
        /// <summary>The shatter row's times after the break.</summary>
        static readonly float[] ShatterAt = { 0, 0.08f, 0.2f, 0.4f, 0.9f, 1.6f };
        const int MaxSheetPx = 2400;

        sealed class Rig : IDisposable
        {
            public StructureLibrary Lib;
            public StructureRenderer Field;
            public StructureShatter Shatter;

            public void Dispose()
            {
                Shatter?.Dispose();
                Field?.Dispose();
            }
        }

        public static void Run(string kind, string outDir)
        {
            var content = GameContent.Find(Application.dataPath) ?? throw new Exception("StructureReview: no rhythm_champions/Content");
            var palette = Palette.Load(content);
            var rules = GameContent.Load(content).Structures;
            Directory.CreateDirectory(outDir);
            var zones = new ZoneSweep(palette.Zones);
            new ImpulseField().Publish(0);

            var camera = new GameObject("ReviewCamera").AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = palette.VoidFloor;
            camera.nearClipPlane = 0.3f;
            camera.farClipPlane = 500f;
            camera.GetUniversalAdditionalCameraData().renderPostProcessing = true;
            camera.fieldOfView = WorldCamera.FovDeg;
            var frame = new RenderTexture(MonsterReview.FrameWidth * MonsterReview.Super, MonsterReview.FrameHeight * MonsterReview.Super, 24, RenderTextureFormat.ARGB32) { name = "StructureReview" };
            frame.Create();
            camera.targetTexture = frame;
            var floor = new GridFloor(camera.gameObject.layer);
            var physics = EditorSceneManager.NewPreviewScene();
            var rigs = new Dictionary<MonsterLook, Rig>();
            var json = new List<string>();
            try
            {
                foreach (var look in new[] { MonsterLook.Trailer, MonsterLook.Default })
                {
                    var lib = StructureLibrary.Load(look) ?? throw new Exception($"StructureReview: no {MonsterLooks.Name(look)} structures baked (champions structure bake all)");
                    var gains = StructureGains.Default;
                    rigs[look] = new Rig { Lib = lib, Field = new StructureRenderer(lib, 4, gains), Shatter = new StructureShatter(lib, gains, 4, physics) };
                }
                var ids = kind == "all" ? rigs[MonsterLook.Default].Lib.Types.Select(t => t.Id).ToArray() : new[] { kind };
                double now = 0;
                if (kind == "all")
                {
                    Save(Field(rigs[MonsterLook.Default], camera, floor, zones, ref now), outDir, "field.jpg", "all");
                    Save(Field(rigs[MonsterLook.Trailer], camera, floor, zones, ref now), outDir, "field-trailer.jpg", "all --look trailer");
                }
                foreach (var id in ids)
                {
                    int type = rigs[MonsterLook.Default].Lib.IndexOf(id);
                    int trailerType = rigs[MonsterLook.Trailer].Lib.IndexOf(id);
                    if (type < 0 || trailerType < 0) throw new Exception($"StructureReview: {id} is not baked for both looks (champions structure bake {id})");
                    var t = rigs[MonsterLook.Default].Lib.Types[type];
                    bool tuned = t.Source != "vendor";
                    var rows = new List<Texture2D> { Headings(rigs[MonsterLook.Default], type, camera, floor, zones, 1f, ref now) };
                    if (tuned) rows.Add(Headings(rigs[MonsterLook.Trailer], trailerType, camera, floor, zones, 1f, ref now));
                    rows.Add(ShatterRow(rigs[MonsterLook.Default], type, camera, floor, zones, ref now));
                    var game = rows[0];
                    for (int r = 1; r < rows.Count; r++) game = MonsterReview.Stack(game, rows[r]);
                    Save(game, outDir, id + "-game.jpg", id);
                    Save(Headings(rigs[MonsterLook.Default], type, camera, floor, zones, 0.5f, ref now), outDir, id + "-near.jpg", id);

                    var trailer = rigs[MonsterLook.Trailer].Lib.Types[trailerType];
                    int k = Array.FindIndex(rules.Kinds, x => x.Id == id);
                    string footprint = k < 0 ? "null" : rules.Kinds[k].Shape == FootprintShape.Box
                        ? string.Format(CultureInfo.InvariantCulture, "[{0:0.##}, {1:0.##}]", 2 * rules.Kinds[k].HalfX, 2 * rules.Kinds[k].HalfY)
                        : string.Format(CultureInfo.InvariantCulture, "{{\"radius\": {0:0.##}}}", rules.Kinds[k].Radius);
                    json.Add(string.Format(CultureInfo.InvariantCulture,
                        "{{\"kind\": \"{0}\", \"source\": \"{1}\", \"triangles\": {2}, \"lines\": {3}, \"trailerTriangles\": {4}, \"trailerLines\": {5}, " +
                        "\"pieces\": {6}, \"heightM\": {7:0.##}, \"extentM\": [{8:0.##}, {9:0.##}], \"footprintM\": {10}, \"sheets\": [\"{0}-game.jpg\", \"{0}-near.jpg\"]}}",
                        id, t.Source, t.Triangles, t.Lines, trailer.Triangles, trailer.Lines, t.Pieces.Length, t.HeightM, t.ExtentM.x, t.ExtentM.y, footprint));
                }
                var text = new StringBuilder();
                text.Append("{\n  \"camera\": {\"pitchDeg\": ").Append(WorldCamera.PitchDeg).Append(", \"distanceM\": ").Append(WorldCamera.DistanceM)
                    .Append(", \"fovDeg\": ").Append(WorldCamera.FovDeg).Append(", \"headingsDeg\": [")
                    .Append(string.Join(", ", HeadingsDeg.Select(d => d.ToString(CultureInfo.InvariantCulture))))
                    .Append("], \"shatterAt\": [").Append(string.Join(", ", ShatterAt.Select(s => s.ToString(CultureInfo.InvariantCulture))))
                    .Append("]},\n  \"kinds\": [\n    ").Append(string.Join(",\n    ", json)).Append("\n  ]\n}\n");
                File.WriteAllText(Path.Combine(outDir, kind == "all" ? "review.json" : $"review-{kind}.json"), text.ToString());
                Debug.Log($"StructureReview: wrote {string.Join(", ", ids)}'s sheets to {outDir}");
            }
            finally
            {
                foreach (var r in rigs.Values) r.Dispose();
                EditorSceneManager.ClosePreviewScene(physics);
                floor.Dispose();
                camera.targetTexture = null;
                frame.Release();
                UnityEngine.Object.DestroyImmediate(frame);
                UnityEngine.Object.DestroyImmediate(camera.gameObject);
            }
        }

        /// <summary>A structure's cell edge at <paramref name="distance"/> times the game camera's: its bounds' reach, with room.</summary>
        static int Cell(StructureType t, float distance, out float perM)
        {
            perM = MonsterReview.FrameHeight / (2 * WorldCamera.DistanceM * distance * Mathf.Tan(0.5f * WorldCamera.FovDeg * Mathf.Deg2Rad));
            float reach = 0.5f * Mathf.Sqrt(t.ExtentM.x * t.ExtentM.x + t.ExtentM.y * t.ExtentM.y + t.HeightM * t.HeightM);
            return Mathf.Clamp(4 * Mathf.CeilToInt(2.3f * reach * perM / 4), 96, MonsterReview.FrameHeight);
        }

        /// <summary>The world at song time <paramref name="now"/>, at rest: no beat ring flares the edges, the first zone's key.</summary>
        static void World(ZoneSweep zones, double now)
        {
            new BeatWave().Publish(now, Vector2.zero);
            zones.Publish(now, Vector2.zero);
        }

        /// <summary>The structure standing from each of <see cref="HeadingsDeg"/>, a cell each, over the grid.</summary>
        static Texture2D Headings(Rig rig, int type, Camera camera, GridFloor floor, ZoneSweep zones, float distance, ref double now)
        {
            var t = rig.Lib.Types[type];
            int cell = Cell(t, distance, out _);
            var sheet = new Texture2D(HeadingsDeg.Length * cell, cell, TextureFormat.RGB24, false);
            MonsterReview.Aim(camera, new Vector3(0, 0.4f * t.HeightM, 0), distance);
            floor.Follow(camera);
            for (int h = 0; h < HeadingsDeg.Length; h++)
            {
                World(zones, now += 10);
                rig.Field.Remove(0);
                rig.Field.Add(0, type, Vector2.zero, HeadingsDeg[h] * Mathf.Deg2Rad);
                rig.Field.Draw(camera);
                sheet.SetPixels32(h * cell, 0, cell, cell, MonsterReview.Render(camera, cell, cell));
            }
            rig.Field.Remove(0);
            rig.Field.Draw(camera); // nothing standing: the next sheet starts empty
            sheet.Apply();
            return sheet;
        }

        /// <summary>Where <see cref="Field"/> stands each kind (ground x, z: a hamlet, a tower and its walls, a grove), and its turn (degrees).</summary>
        static readonly (string Kind, Vector2 At, float HeadingDeg)[] FieldLayout =
        {
            ("tower", new Vector2(0, 3), 10), ("wall", new Vector2(-4.5f, 0.5f), 0), ("wall", new Vector2(4.5f, 0.5f), 0),
            ("cottage", new Vector2(-9, -3), 15), ("hut", new Vector2(-12, 3.5f), 100), ("fence", new Vector2(-7, -7.5f), 0),
            ("fence", new Vector2(-4.5f, -7.5f), 0), ("rock", new Vector2(-2, -5), 40),
            ("pine", new Vector2(10, -2), 0), ("tree", new Vector2(6, -6), 70), ("tree", new Vector2(13, 3.5f), 200), ("pine", new Vector2(4, -2.5f), 120),
        };

        /// <summary>Every kind standing together over the grid in the game camera's whole 1080-row frame: a hamlet, a tower, a grove.</summary>
        static Texture2D Field(Rig rig, Camera camera, GridFloor floor, ZoneSweep zones, ref double now)
        {
            MonsterReview.Aim(camera, Vector3.zero, 1f);
            floor.Follow(camera);
            World(zones, now += 10);
            for (int i = 0; i < FieldLayout.Length; i++)
            {
                var (kind, at, heading) = FieldLayout[i];
                int type = rig.Lib.IndexOf(kind);
                if (type >= 0) rig.Field.Add(i, type, at, heading * Mathf.Deg2Rad);
            }
            rig.Field.Draw(camera);
            var sheet = new Texture2D(MonsterReview.FrameWidth, MonsterReview.FrameHeight, TextureFormat.RGB24, false);
            sheet.SetPixels32(MonsterReview.Render(camera, MonsterReview.FrameWidth, MonsterReview.FrameHeight));
            sheet.Apply();
            for (int i = 0; i < FieldLayout.Length; i++) rig.Field.Remove(i);
            rig.Field.Draw(camera);
            return sheet;
        }

        /// <summary>The structure breaking at the game camera, struck from the camera's side, at each of <see cref="ShatterAt"/>.</summary>
        static Texture2D ShatterRow(Rig rig, int type, Camera camera, GridFloor floor, ZoneSweep zones, ref double now)
        {
            var t = rig.Lib.Types[type];
            int cell = Cell(t, 1f, out _);
            var sheet = new Texture2D(ShatterAt.Length * cell, cell, TextureFormat.RGB24, false);
            MonsterReview.Aim(camera, new Vector3(0, 0.4f * t.HeightM, 0), 1f);
            floor.Follow(camera);
            rig.Shatter.Clear();
            double broke = now += 10;
            for (int k = 0; k < ShatterAt.Length; k++)
            {
                double at = broke + ShatterAt[k];
                World(zones, at);
                // A blow away from the camera: up the screen.
                if (k == 0) rig.Shatter.Launch(type, Vector3.zero, HeadingsDeg[0] * Mathf.Deg2Rad, at, new Vector2(0, 1), 1f);
                rig.Shatter.Present(at);
                rig.Shatter.Draw(camera);
                sheet.SetPixels32(k * cell, 0, cell, cell, MonsterReview.Render(camera, cell, cell));
            }
            rig.Shatter.Clear();
            now = broke + 10;
            sheet.Apply();
            return sheet;
        }

        /// <summary>Saves a sheet as a small JPEG: one wider than <see cref="MaxSheetPx"/> is scaled down by a whole factor.</summary>
        static void Save(Texture2D sheet, string dir, string file, string id)
        {
            int shrink = Mathf.CeilToInt(sheet.width / (float)MaxSheetPx);
            var saved = shrink > 1 ? MonsterReview.Shrink(sheet, shrink) : sheet;
            RenderTag.SaveJpeg(saved, Path.Combine(dir, file), $"champions structure review {id}", 85);
            if (saved != sheet) UnityEngine.Object.DestroyImmediate(saved);
            UnityEngine.Object.DestroyImmediate(sheet);
        }
    }
}
