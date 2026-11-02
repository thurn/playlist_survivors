using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Champions.Render;
using Champions.Render.Crowd;
using Champions.Sim;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Champions.EditorTools.Monsters
{
    /// <summary>
    /// A type's review sheets (plan.md, Rendering &gt; The edge look), for judging by eye: the type drawn by the crowd itself
    /// in its palette colour with the pipeline's post, from the game camera's angle, in one look:
    /// - LOOK-far.jpg, LOOK-game.jpg, LOOK-near.jpg: the camera at twice, once and half the game camera's distance, the
    ///   type in each of its review poses (idle, walk and attack; a boss's every move), three to a row, and a last row of
    ///   its shatter over the grid, broken by a blow away from the camera and shown at <see cref="ShatterAt"/> after the
    ///   break;
    /// - LOOK-crowd.jpg: a few dozen of the type walking over the grid at the game camera (a 960 by 540 crop);
    /// - compare.jpg: --look trailer on the left beside the default on the right, the game-size poses above the crowd.
    /// Every cell is a crop of a 1080-row frame at the game camera's field of view, drawn at twice that and scaled down
    /// (as the trailer's capture was), so a type is exactly as large as in the game; a sheet wider than
    /// <see cref="MaxSheetPx"/> is saved scaled down by a whole factor. Beside them review.json names the sheets and the
    /// type's numbers (diagnostics, never a verdict).
    /// </summary>
    public static class MonsterReview
    {
        /// <summary>The sheets' camera distances, as multiples of the game camera's.</summary>
        public static readonly (string Name, float Distance)[] Sizes = { ("far", 2f), ("game", 1f), ("near", 0.5f) };
        /// <summary>The shatter row's times after the break, one per cell, and the blow's launch distance (the sword's).</summary>
        static readonly float[] ShatterAt = { 0, 0.08f, 0.2f, 0.4f, 0.9f, 1.6f };
        const float BlowForce = 1.85f, YawDeg = 160f;
        const int PosesPerRow = 3, CrowdWidth = 960, CrowdHeight = 540, MaxCrowd = 36, MaxSheetPx = 2400;
        /// <summary>The frame cells are cut from, and how many times larger it is drawn.</summary>
        const int FrameWidth = 1920, FrameHeight = 1080, Super = 2;
        static Color32 _void;

        sealed class Rig : IDisposable
        {
            public MonsterLibrary Lib;
            public MonsterCrowd Crowd;
            public MonsterShatter Shatter;
            public int Type;
            public MonsterType T => Lib.Types[Type];
            public double Now;

            public void Dispose()
            {
                Shatter?.Dispose();
                Crowd?.Dispose();
            }
        }

        public static void Run(string id, MonsterLook look, string outDir)
        {
            if (MonsterModel.Of(id).figure)
                throw new Exception($"MonsterReview: {id} is a figure posed on the CPU, not a crowd's type: `champions stills --scene hero` shows it");
            var palette = Palette.Load(GameContent.Find(Application.dataPath) ?? throw new Exception("MonsterReview: no rhythm_champions/Content"));
            Directory.CreateDirectory(outDir);
            // The world at rest (an earlier task in this editor may have left a beat ring), on the first zone's grid.
            new BeatWave().Publish(0, Vector2.zero);
            new ImpulseField().Publish(0);
            new ZoneSweep(palette.Zones).Publish(0, Vector2.zero);

            var camera = new GameObject("ReviewCamera").AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = palette.VoidFloor;
            camera.nearClipPlane = 0.3f;
            camera.farClipPlane = 500f;
            camera.GetUniversalAdditionalCameraData().renderPostProcessing = true;
            camera.fieldOfView = WorldCamera.FovDeg;
            var frame = new RenderTexture(FrameWidth * Super, FrameHeight * Super, 24, RenderTextureFormat.ARGB32) { name = "MonsterReview" };
            frame.Create();
            camera.targetTexture = frame;
            _void = Color.black; // the void as graded (Grade.cs sinks the floor's colour to black)
            var floor = new GridFloor(camera.gameObject.layer);
            var rigs = new Dictionary<MonsterLook, Rig>();
            var sheets = new List<string>();
            try
            {
                foreach (var l in new[] { MonsterLook.Trailer, MonsterLook.Default })
                {
                    var lib = MonsterLibrary.Load(l) ?? throw new Exception($"MonsterReview: no {MonsterLooks.Name(l)} monsters baked (champions monster bake {id})");
                    int type = lib.IndexOf(id);
                    if (type < 0) throw new Exception($"MonsterReview: {id} is not baked for the {MonsterLooks.Name(l)} look (champions monster bake {id})");
                    var crowd = new MonsterCrowd(lib, MaxCrowd, palette);
                    rigs[l] = new Rig { Lib = lib, Crowd = crowd, Shatter = new MonsterShatter(lib, crowd.Palette, palette), Type = type };
                }
                var model = MonsterModel.Of(id);
                var main = rigs[look];
                string name = MonsterLooks.Name(look);
                floor.GameObject.SetActive(false);
                foreach (var (size, distance) in Sizes)
                {
                    var sheet = PoseSheet(main, model, camera, floor, distance, withShatter: true);
                    Save(sheet, outDir, $"{name}-{size}.jpg", id, look, sheets);
                }
                floor.GameObject.SetActive(true);
                Save(CrowdCell(main, camera, floor), outDir, $"{name}-crowd.jpg", id, look, sheets);

                // The side by side: each look's game-size poses over its crowd, the trailer's on the left.
                var columns = new[] { MonsterLook.Trailer, MonsterLook.Default }.Select(l =>
                {
                    floor.GameObject.SetActive(false);
                    var poses = PoseSheet(rigs[l], model, camera, floor, 1f, withShatter: false);
                    floor.GameObject.SetActive(true);
                    var crowd = CrowdCell(rigs[l], camera, floor);
                    return Stack(poses, crowd);
                }).ToArray();
                Save(Beside(columns[0], columns[1]), outDir, "compare.jpg", id, look, sheets);

                var t = main.T;
                var json = new StringBuilder();
                json.Append("{\n  \"type\": \"").Append(id).Append("\",\n  \"look\": \"").Append(name)
                    .Append("\",\n  \"source\": \"").Append(t.Source)
                    .Append("\",\n  \"trailerSource\": \"").Append(rigs[MonsterLook.Trailer].T.Source)
                    .Append("\",\n  \"lodTriangles\": [").Append(string.Join(", ", t.Lods.Select(m => m.GetIndexCount(0) / 3)))
                    .Append("],\n  \"trailerLodTriangles\": [").Append(string.Join(", ", rigs[MonsterLook.Trailer].T.Lods.Select(m => m.GetIndexCount(0) / 3)))
                    .Append("],\n  \"shards\": ").Append(t.Shards.Length)
                    .Append(",\n  \"bones\": ").Append(t.BoneCount)
                    .Append(",\n  \"heightM\": ").Append(t.HeightM.ToString("0.###", CultureInfo.InvariantCulture))
                    .Append(",\n  \"poses\": [").Append(string.Join(", ", model.reviewPoses.Select(p => $"\"{p}\"")))
                    .Append("],\n  \"shatterAt\": [").Append(string.Join(", ", ShatterAt.Select(s => s.ToString(CultureInfo.InvariantCulture))))
                    .Append("],\n  \"camera\": {\"pitchDeg\": ").Append(WorldCamera.PitchDeg).Append(", \"distanceM\": ").Append(WorldCamera.DistanceM)
                    .Append(", \"fovDeg\": ").Append(WorldCamera.FovDeg).Append(", \"yawDeg\": ").Append(YawDeg)
                    .Append("},\n  \"sheets\": [").Append(string.Join(", ", sheets.Select(s => $"\"{s}\""))).Append("]\n}\n");
                File.WriteAllText(Path.Combine(outDir, "review.json"), json.ToString());
                Debug.Log($"MonsterReview: {id} ({name}): wrote {string.Join(", ", sheets)} and review.json to {outDir}");
            }
            finally
            {
                foreach (var r in rigs.Values) r.Dispose();
                floor.Dispose();
                camera.targetTexture = null;
                frame.Release();
                UnityEngine.Object.DestroyImmediate(frame);
                UnityEngine.Object.DestroyImmediate(camera.gameObject);
            }
        }

        /// <summary>Saves a sheet as a small JPEG: one wider than <see cref="MaxSheetPx"/> is scaled down by a whole factor.</summary>
        static void Save(Texture2D sheet, string dir, string file, string id, MonsterLook look, List<string> sheets)
        {
            int shrink = Mathf.CeilToInt(sheet.width / (float)MaxSheetPx);
            var saved = shrink > 1 ? Shrink(sheet, shrink) : sheet;
            RenderTag.SaveJpeg(saved, Path.Combine(dir, file), $"champions monster review {id} --look {MonsterLooks.Name(look)}", 85);
            if (saved != sheet) UnityEngine.Object.DestroyImmediate(saved);
            UnityEngine.Object.DestroyImmediate(sheet);
            sheets.Add(file);
        }

        /// <summary>A box-filtered copy <paramref name="k"/> times smaller each way.</summary>
        static Texture2D Shrink(Texture2D src, int k)
        {
            int w = src.width / k, h = src.height / k, area = k * k;
            var px = src.GetPixels32();
            var o = new Color32[w * h];
            for (int j = 0; j < h; j++)
            for (int i = 0; i < w; i++)
            {
                int r = 0, g = 0, b = 0;
                for (int dy = 0; dy < k; dy++)
                for (int dx = 0; dx < k; dx++)
                {
                    var c = px[(j * k + dy) * src.width + i * k + dx];
                    r += c.r;
                    g += c.g;
                    b += c.b;
                }
                o[j * w + i] = new Color32((byte)(r / area), (byte)(g / area), (byte)(b / area), 255);
            }
            var t = new Texture2D(w, h, TextureFormat.RGB24, false);
            t.SetPixels32(o);
            t.Apply();
            return t;
        }

        /// <summary>A pose's sample time: its impact if it has one, else a moment that reads (mid-stride for a walk).</summary>
        static float PoseTime(MonsterClip clip) =>
            clip.Impact >= 0 ? clip.Impact : (clip.Role == "idle" ? 0.3f : clip.Role == "walk" ? 0.25f : 0.5f) * clip.Length;

        /// <summary>The game camera's look down at <paramref name="focus"/> from <paramref name="distance"/> times its distance.</summary>
        static void Aim(Camera camera, Vector3 focus, float distance)
        {
            camera.transform.rotation = Quaternion.Euler(WorldCamera.PitchDeg, 0, 0);
            camera.transform.position = focus - camera.transform.forward * (WorldCamera.DistanceM * distance);
        }

        /// <summary>
        /// The type's review poses in square cells on the void, three to a row, with a shatter row under them if asked, over
        /// the grid (which hides the shards as they sink, as in play).
        /// </summary>
        static Texture2D PoseSheet(Rig rig, MonsterModel model, Camera camera, GridFloor floor, float distance, bool withShatter)
        {
            var t = rig.T;
            var poses = model.reviewPoses.Select(role => t.Clip(role) is var c && c >= 0 ? c
                : throw new Exception($"MonsterReview: {t.Id} has no {role} clip")).ToArray();
            float perM = 1080f / (2 * WorldCamera.DistanceM * distance * Mathf.Tan(0.5f * WorldCamera.FovDeg * Mathf.Deg2Rad));
            // Each pose framed round its own clip's bounds (a flight's raised), the cells as large as the largest.
            float reach = poses.Max(c => t.Clips[c].Radius);
            int cell = Mathf.Clamp(4 * Mathf.CeilToInt(2.1f * reach * perM / 4), 96, FrameHeight);
            int columns = Mathf.Min(poses.Length, PosesPerRow);
            int rows = (poses.Length + PosesPerRow - 1) / PosesPerRow + (withShatter ? 1 : 0);
            int shatterCells = withShatter ? Mathf.Max(columns, Mathf.Min(ShatterAt.Length, 2 * columns)) : 0;
            int width = Mathf.Max(columns, shatterCells) * cell;
            var sheet = new Texture2D(width, rows * cell, TextureFormat.RGB24, false);
            sheet.SetPixels32(Enumerable.Repeat(_void, sheet.width * sheet.height).ToArray()); // the void where a short row leaves cells empty
            MonsterInstance first = default;
            for (int pi = 0; pi < poses.Length; pi++)
            {
                var clip = t.Clips[poses[pi]];
                float time = PoseTime(clip);
                var body = new MonsterInstance
                {
                    Position = Vector3.zero, Yaw = YawDeg * Mathf.Deg2Rad, ClipA = (uint)poses[pi], TimeA = time,
                    ClipB = (uint)poses[pi], TimeB = time, Blend = 1, Scale = 1, Flags = (uint)CrowdFlags.Alive,
                };
                if (pi == 0) first = body;
                Aim(camera, new Vector3(0, clip.CenterY, 0), distance);
                var slots = rig.Crowd.Instances(rig.Type, 1);
                slots[0] = body;
                rig.Crowd.Draw(camera, rig.Now += 10);
                sheet.SetPixels32(pi % PosesPerRow * cell, (rows - 1 - pi / PosesPerRow) * cell, cell, cell, Render(camera, cell, cell));
            }
            if (withShatter)
            {
                rig.Shatter.Clear();
                Aim(camera, new Vector3(0, t.Clips[poses[0]].CenterY, 0), distance);
                floor.GameObject.SetActive(true);
                floor.Follow(camera);
                double broke = rig.Now += 10;
                for (int k = 0; k < shatterCells; k++)
                {
                    double now = broke + ShatterAt[k];
                    var body = first;
                    body.Flags = (uint)(k == 0 ? CrowdFlags.Dying : CrowdFlags.Hidden);
                    // A blow away from the camera: up the screen.
                    if (k == 0) rig.Shatter.Break(rig.Type, 0, body, 1, now, 0, 1, BlowForce);
                    var slots = rig.Crowd.Instances(rig.Type, 1);
                    slots[0] = body;
                    rig.Crowd.Draw(camera, now);
                    rig.Shatter.Frame(now, camera);
                    sheet.SetPixels32(k * cell, 0, cell, cell, Render(camera, cell, cell));
                }
                rig.Shatter.Clear();
                floor.GameObject.SetActive(false);
            }
            sheet.Apply();
            return sheet;
        }

        /// <summary>
        /// A few dozen of the type walking toward the frame's middle over the grid at the game camera, each a dark gap
        /// apart, the swarm's tilt and wobble on (fewer of a big one, so they fit).
        /// </summary>
        static Texture2D CrowdCell(Rig rig, Camera camera, GridFloor floor)
        {
            var t = rig.T;
            Aim(camera, Vector3.zero, 1f);
            floor.Follow(camera);
            int walk = Math.Max(0, t.Clip("walk"));
            var clip = t.Clips[walk];
            float gap = Mathf.Max(0.8f, 1.6f * Mathf.Min(t.Radius, 0.6f * t.HeightM + 0.2f));
            // The ground the crop sees: about 15 m across and 10 m up the screen at the game camera.
            var spots = new List<Vector2>();
            var rng = new System.Random(StyleMesh.StableSeed(t.Id));
            for (int tries = 0; tries < 4000 && spots.Count < MaxCrowd; tries++)
            {
                var p = new Vector2((float)(rng.NextDouble() - 0.5) * 13f, (float)(rng.NextDouble() - 0.5) * 8f);
                if (p.magnitude < 1.5f || spots.Any(q => (q - p).magnitude < gap)) continue;
                spots.Add(p);
            }
            double now = rig.Now += 10;
            var slots = rig.Crowd.Instances(rig.Type, spots.Count);
            var motion = rig.Crowd.Motion(rig.Type, spots.Count);
            for (int i = 0; i < spots.Count; i++)
            {
                var p = spots[i];
                float time = (float)(rng.NextDouble() * clip.Length);
                slots[i] = new MonsterInstance
                {
                    Position = new Vector3(p.x, t.HoverM, p.y), Yaw = Mathf.Atan2(-p.x, -p.y), ClipA = (uint)walk, TimeA = time,
                    ClipB = (uint)walk, TimeB = time, Blend = 1, Scale = 1, Flags = (uint)CrowdFlags.Alive,
                };
                motion[i] = MonsterMotion.Still((uint)(i * 7919 + 1));
            }
            rig.Crowd.Draw(camera, now);
            var tex = new Texture2D(CrowdWidth, CrowdHeight, TextureFormat.RGB24, false);
            tex.SetPixels32(Render(camera, CrowdWidth, CrowdHeight));
            tex.Apply();
            return tex;
        }

        /// <summary>The middle <paramref name="w"/> by <paramref name="h"/> of the camera's 1080-row frame, drawn at <see cref="Super"/> times that and scaled down.</summary>
        static Color32[] Render(Camera camera, int w, int h)
        {
            camera.Render();
            var rt = camera.targetTexture;
            var tex = new Texture2D(w * Super, h * Super, TextureFormat.RGBA32, false);
            RenderTexture.active = rt;
            tex.ReadPixels(new Rect((rt.width - w * Super) / 2, (rt.height - h * Super) / 2, w * Super, h * Super), 0, 0);
            RenderTexture.active = null;
            tex.Apply(false);
            var small = Shrink(tex, Super);
            var px = small.GetPixels32();
            UnityEngine.Object.DestroyImmediate(tex);
            UnityEngine.Object.DestroyImmediate(small);
            return px;
        }

        /// <summary><paramref name="top"/> over <paramref name="bottom"/>, left-aligned; both destroyed.</summary>
        static Texture2D Stack(Texture2D top, Texture2D bottom)
        {
            var o = new Texture2D(Mathf.Max(top.width, bottom.width), top.height + bottom.height, TextureFormat.RGB24, false);
            o.SetPixels32(new Color32[o.width * o.height]);
            o.SetPixels32(0, 0, bottom.width, bottom.height, bottom.GetPixels32());
            o.SetPixels32(0, bottom.height, top.width, top.height, top.GetPixels32());
            o.Apply();
            UnityEngine.Object.DestroyImmediate(top);
            UnityEngine.Object.DestroyImmediate(bottom);
            return o;
        }

        /// <summary><paramref name="left"/> beside <paramref name="right"/>, a thin grey rule between, top-aligned; both destroyed.</summary>
        static Texture2D Beside(Texture2D left, Texture2D right)
        {
            const int rule = 4;
            int h = Mathf.Max(left.height, right.height);
            var o = new Texture2D(left.width + rule + right.width, h, TextureFormat.RGB24, false);
            o.SetPixels32(new Color32[o.width * o.height]);
            var grey = Enumerable.Repeat(new Color32(70, 70, 70, 255), rule * h).ToArray();
            o.SetPixels32(left.width, 0, rule, h, grey);
            o.SetPixels32(0, h - left.height, left.width, left.height, left.GetPixels32());
            o.SetPixels32(left.width + rule, h - right.height, right.width, right.height, right.GetPixels32());
            o.Apply();
            UnityEngine.Object.DestroyImmediate(left);
            UnityEngine.Object.DestroyImmediate(right);
            return o;
        }
    }
}
