using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Demo2D.Runtime;
using PS.Choreography;
using UnityEditor;
using UnityEngine;

namespace Demo2D.EditorTools.Assets
{
    /// <summary>
    /// PX3's clip annotator. It writes the clip library, Data/clip-annotations.json, from the
    /// baker's frames: for each clip the choreography uses, its frame count and rate, its pivot and
    /// size on the grid, and, measured from the motion between frames (on a bake
    /// <see cref="SpriteBaker.AnalysisFactor"/> times finer than the game's):
    /// - <b>strikes</b> (attacks and the hero's slashes, bash and spin): the impact at the first
    ///   peak of motion within 85% of the clip's largest, the end after the last frame moving at
    ///   least a quarter of that;
    /// - <b>the slam</b> (a jump): the impact on the frame the feet land after the jump's apex;
    /// - <b>hits and deaths:</b> the impact on the first frame moving at least half the clip's
    ///   largest, the end once motion drops below a fifth of it (a tenth for deaths: the body at rest);
    /// - <b>walks and idles:</b> the cycle (the last frame repeats the first when the motion between
    ///   them is small) and, for walks, the stride: how far the planted foot slides back over a
    ///   cycle, from the shift that best matches the soles (the lowest 4% of the figure) from frame
    ///   to frame. A walk with no planted foot (a hovering boss, a lava blob) gets two of the
    ///   catalog's gait steps;
    /// - <b>the hero's combos:</b> the root motion, how far the feet travel forward in the frames
    ///   from the first frame to the end.
    /// The clips and uses it annotates are the library's own (or PX4's placeholders before it
    /// exists), so choosing another clip is an edit of `clip` there and a re-run.
    /// </summary>
    public static class ClipAnnotator
    {
        public const string OutPath = CharacterPacks.DataDir + "/clip-annotations.json";
        const string PlaceholderPath = CharacterPacks.DataDir + "/placeholder-annotations.json";

        public sealed class Annotation
        {
            public string Guid, Clip, Use, Method, Anchor;
            public int Frames, ImpactFrame, EndFrame;
            public float StrideM, RootMotionM;
            public Vector2Int Pivot, Size;
            public float Impact => (float)ImpactFrame / CharacterPacks.FrameRate;
            public float End => (float)EndFrame / CharacterPacks.FrameRate;
            public string Role => Use.Substring(Use.LastIndexOf(' ') + 1);
        }

        /// <summary>The library's (clip, use) pairs, in its order.</summary>
        public static List<(string clip, string use)> Entries()
        {
            var file = File.Exists(OutPath) ? OutPath : PlaceholderPath;
            return JsonValue.Parse(File.ReadAllText(file))["annotations"].Items
                .Select(a => (a["clip"].AsString(), a["use"].AsString())).ToList();
        }

        /// <summary>Annotates every entry from the bake, writes the library and its contact sheets.</summary>
        public static List<Annotation> Run(List<SpriteBaker.Baked> baked, string evidenceDir)
        {
            var annotations = Annotate(baked, Entries());
            File.WriteAllText(OutPath, Json(annotations));
            Debug.Log($"ClipAnnotator: wrote {annotations.Count} annotations to {OutPath}");
            Directory.CreateDirectory(evidenceDir);
            foreach (var b in baked) ContactSheet(b, annotations, Path.Combine(evidenceDir, $"annotations-{b.Character.Name}.jpg"));
            return annotations;
        }

        public static List<Annotation> Annotate(List<SpriteBaker.Baked> baked, List<(string clip, string use)> entries)
        {
            var byPrefix = baked.ToDictionary(b => b.Character.Prefix);
            var list = new List<Annotation>();
            foreach (var (clip, use) in entries)
            {
                int cut = clip.IndexOf('-');
                if (cut < 0 || !byPrefix.TryGetValue(clip.Substring(0, cut), out var b))
                    throw new InvalidOperationException($"ClipAnnotator: no baked character for {clip}");
                string name = clip.Substring(cut + 1);
                var a = Measure(b.Analysis[name], use, b.Character.GameHeightPx * SpriteBaker.AnalysisFactor, b.Character.GaitStepM);
                var game = b.Clips[name];
                a.Guid = AssetDatabase.AssetPathToGUID($"{b.Character.ClipsDir}/{name}.anim");
                a.Clip = clip;
                a.Use = use;
                a.Pivot = game.Pivot;
                a.Size = new Vector2Int(game.W, game.H);
                a.Anchor = b.Anchor[name];
                list.Add(a);
            }
            return list;
        }

        /// <summary>Proposes one clip's timing from its frames (bottom-up masks, the feet at the pivot).</summary>
        public static Annotation Measure(SpriteBaker.Sequence s, string use, int heightPx, float gaitStepM)
        {
            int n = s.Frames.Count;
            var a = new Annotation { Frames = n, Use = use, EndFrame = n - 1 };
            var motion = new float[n];
            for (int i = 1; i < n; i++) motion[i] = Changed(s, i - 1, i);
            float max = Mathf.Max(1, motion.Max());
            int Last(float share) => Enumerable.Range(1, n - 1).Where(i => motion[i] >= share * max).DefaultIfEmpty(0).Max();
            int First(float share) => Enumerable.Range(1, n - 1).Where(i => motion[i] >= share * max).DefaultIfEmpty(0).Min();
            int band = Mathf.Max(2, Mathf.RoundToInt(heightPx * 0.125f));
            float metresPerPx = 1f / (SpriteBaker.AnalysisFactor * PixelShots.PixelsPerMetre);

            switch (a.Role)
            {
                case "walk":
                case "idle":
                {
                    var steps = motion.Skip(1).OrderBy(m => m).ToList();
                    float median = steps.Count == 0 ? 0 : steps[steps.Count / 2];
                    int cycle = n > 2 && Changed(s, n - 1, 0) <= 0.3f * median ? n - 1 : n;
                    a.ImpactFrame = 0;
                    a.EndFrame = cycle;
                    if (a.Role == "walk")
                    {
                        int soles = Mathf.Max(2, Mathf.RoundToInt(heightPx * 0.04f)), reach = Mathf.RoundToInt(heightPx * 0.15f);
                        int slide = 0;
                        for (int i = 0; i < cycle; i++) slide += SoleShift(s, i, (i + 1) % n, soles, reach);
                        a.StrideM = Mathf.Max(0, -slide) * metresPerPx;
                        if (a.StrideM >= MinStrideM)
                            a.Method = $"cycle of {cycle} frames; stride from the planted foot's slide";
                        else
                        {
                            a.Method = $"cycle of {cycle} frames; no planted foot ({a.StrideM:0.00} m), stride two gait steps";
                            a.StrideM = 2 * gaitStepM;
                        }
                    }
                    else a.Method = $"cycle of {cycle} frames";
                    break;
                }
                case "hit":
                case "death":
                {
                    a.ImpactFrame = First(0.5f);
                    a.EndFrame = Math.Max(a.ImpactFrame, Math.Min(n - 1, Last(a.Role == "death" ? 0.1f : 0.2f) + 1));
                    a.Method = a.Role == "death" ? "impact at the first large motion; end once the body rests" : "impact at the first large motion; end once it settles";
                    break;
                }
                case "slam":
                {
                    var bottom = Enumerable.Range(0, n).Select(i => Bottom(s, i)).ToList();
                    int apex = bottom.IndexOf(bottom.Max());
                    int airborne = 3 * SpriteBaker.AnalysisFactor;
                    int land = bottom.Max() > airborne ? Enumerable.Range(apex, n - apex).FirstOrDefault(i => bottom[i] <= airborne) : 0;
                    if (land > 0)
                    {
                        a.ImpactFrame = land;
                        a.Method = "impact where the feet land after the apex";
                    }
                    else
                    {
                        a.ImpactFrame = FirstPeak(motion, 0.85f * max);
                        a.Method = "no jump in the frames: impact at the first peak of motion";
                    }
                    a.EndFrame = Math.Max(a.ImpactFrame, Math.Min(n - 1, Last(0.25f) + 1));
                    break;
                }
                default: // strikes
                {
                    a.ImpactFrame = FirstPeak(motion, 0.85f * max);
                    a.EndFrame = Math.Max(a.ImpactFrame, Math.Min(n - 1, Last(0.25f) + 1));
                    a.Method = "impact at the first peak of motion; end once it settles";
                    break;
                }
            }
            if (use.StartsWith("hero ", StringComparison.Ordinal) && a.Role != "walk" && a.Role != "idle")
            {
                float x0 = FeetX(s, 0, band), x1 = FeetX(s, a.EndFrame, band);
                a.RootMotionM = float.IsNaN(x0) || float.IsNaN(x1) ? 0 : Mathf.Max(0, (x1 - x0) * metresPerPx);
            }
            return a;
        }

        /// <summary>The first local maximum of motion at or above the threshold.</summary>
        static int FirstPeak(float[] motion, float threshold)
        {
            for (int i = 1; i < motion.Length; i++)
                if (motion[i] >= threshold && (i + 1 == motion.Length || motion[i] >= motion[i + 1])) return i;
            return 1;
        }

        /// <summary>How many grid pixels change between opaque and clear from one frame to another.</summary>
        static float Changed(SpriteBaker.Sequence s, int a, int b)
        {
            int n = 0;
            for (int i = 0; i < s.W * s.H; i++) if ((s.Frames[a][i].a != 0) != (s.Frames[b][i].a != 0)) n++;
            return n;
        }

        /// <summary>Below this a walk's soles don't slide: it has no planted foot to measure.</summary>
        const float MinStrideM = 0.25f;

        /// <summary>
        /// The horizontal shift (within ±reach) that best carries frame a's soles, the opaque pixels
        /// in the band above the pivot, onto frame b's (most overlap for their union; ties to the
        /// smallest shift). In an in-place walk the planted foot slides back by the step's distance.
        /// </summary>
        static int SoleShift(SpriteBaker.Sequence s, int a, int b, int band, int reach)
        {
            int y0 = Math.Max(0, s.Pivot.y), y1 = Math.Min(s.H, s.Pivot.y + band);
            int na = 0, nb = 0;
            for (int y = y0; y < y1; y++)
            for (int x = 0; x < s.W; x++)
            {
                if (s.Opaque(a, x, y)) na++;
                if (s.Opaque(b, x, y)) nb++;
            }
            int best = 0;
            float bestScore = -1;
            for (int d = 0; d <= reach; d++)
            foreach (int dx in d == 0 ? new[] { 0 } : new[] { -d, d })
            {
                int inter = 0;
                for (int y = y0; y < y1; y++)
                for (int x = Math.Max(0, -dx); x < Math.Min(s.W, s.W - dx); x++)
                    if (s.Opaque(a, x, y) && s.Opaque(b, x + dx, y)) inter++;
                float score = (float)inter / Math.Max(1, na + nb - inter);
                if (score > bestScore + 1e-6f) { bestScore = score; best = dx; }
            }
            return best;
        }

        /// <summary>The middle of the opaque pixels in the band above the pivot (NaN if none).</summary>
        static float FeetX(SpriteBaker.Sequence s, int f, int band)
        {
            double sum = 0;
            int n = 0;
            for (int y = Math.Max(0, s.Pivot.y); y < Math.Min(s.H, s.Pivot.y + band); y++)
            for (int x = 0; x < s.W; x++)
                if (s.Opaque(f, x, y)) { sum += x; n++; }
            return n == 0 ? float.NaN : (float)(sum / n);
        }

        /// <summary>How far above the ground (the pivot) the frame's lowest opaque row is.</summary>
        static int Bottom(SpriteBaker.Sequence s, int f)
        {
            for (int y = 0; y < s.H; y++)
            for (int x = 0; x < s.W; x++)
                if (s.Opaque(f, x, y)) return y - s.Pivot.y;
            return 0;
        }

        static string Json(List<Annotation> list)
        {
            var inv = CultureInfo.InvariantCulture;
            var j = new StringBuilder();
            j.Append("{\n  \"note\": \"demo2d's clip library (PX3), written by ClipAnnotator (`demo2d unity AnnotateClips`) from the sprite baker's frames: ")
             .Append("one entry per clip the choreography uses, 'clip' naming the pack's frame sequence (<clipPrefix>-<clip>_<n>.png) and 'use' its role. ")
             .Append("Frames play at 'frameRate'; times are seconds from the clip's start, each at the start of its frame ('impact' = impactFrame / frameRate). ")
             .Append("'pivotPx' is the feet and 'sizePx' the frame on the 960x540 grid, as baked into Assets/Baked/Characters; 'anchor' says how the baker found the feet. ")
             .Append("Strides and root motion are metres (32 grid pixels). The frames carry any root motion themselves (the clips share one anchor per clip), ")
             .Append("so a client drawing a hero combo whose segment moves rootMotionM holds the sprite at the segment's start. ")
             .Append("To choose another clip, edit 'clip' here and re-run the annotator.\",\n");
            j.Append("  \"frameRate\": ").Append(CharacterPacks.FrameRate).Append(",\n  \"annotations\": [\n");
            for (int i = 0; i < list.Count; i++)
            {
                var a = list[i];
                j.Append(string.Format(inv,
                    "    {{\"guid\": \"{0}\", \"clip\": \"{1}\", \"use\": \"{2}\", \"frames\": {3}, \"impactFrame\": {4}, \"impact\": {5:0.###}, " +
                    "\"endFrame\": {6}, \"end\": {7:0.###}, \"strideM\": {8:0.##}, \"rootMotionM\": {9:0.##}, \"pivotPx\": [{10}, {11}], \"sizePx\": [{12}, {13}], \"anchor\": \"{15}\", \"method\": \"{14}\"}}",
                    a.Guid, a.Clip, a.Use, a.Frames, a.ImpactFrame, a.Impact, a.EndFrame, a.End, a.StrideM, a.RootMotionM,
                    a.Pivot.x, a.Pivot.y, a.Size.x, a.Size.y, a.Method, a.Anchor));
                j.Append(i + 1 < list.Count ? ",\n" : "\n");
            }
            j.Append("  ]\n}\n");
            return j.ToString();
        }

        // Contact sheets: a row per annotated clip at twice the game size (the boss at its own), the
        // impact frame framed in red and the end frame in blue, the pivot's row as a faint ground line.
        const int Gap = 3, LabelH = 30, Margin = 8, MaxW = 3000;
        static readonly Color32 Background = new Color32(30, 32, 40, 255), Cell = new Color32(70, 76, 66, 255), Ground = new Color32(96, 104, 90, 255);
        static readonly Color32 Text = new Color32(230, 230, 230, 255), Impact = new Color32(255, 70, 60, 255), EndColor = new Color32(80, 150, 255, 255);

        static void ContactSheet(SpriteBaker.Baked b, List<Annotation> all, string path)
        {
            var rows = all.Where(a => a.Clip.StartsWith(b.Character.Prefix + "-", StringComparison.Ordinal)).ToList();
            if (rows.Count == 0) return;
            int Zoom = b.Character.GameHeightPx > 120 ? 1 : 2;
            int Pitch(Annotation a) => a.Size.x * Zoom + Gap + 2;
            int w = Margin * 2 + Math.Min(MaxW, Math.Max(rows.Max(a => a.Frames * Pitch(a)), 600));
            int PerLine(Annotation a) => Math.Max(1, (w - Margin * 2) / Pitch(a));
            int Lines(Annotation a) => (a.Frames + PerLine(a) - 1) / PerLine(a);
            int h = Margin + 16 + rows.Sum(a => LabelH + Lines(a) * (a.Size.y * Zoom + Gap + 2) + Gap * 2 + 4);
            var px = Enumerable.Repeat(Background, w * h).ToArray();
            PixelText.Draw(px, w, h, Margin, h - Margin,
                $"{b.Character.Name}: {b.Character.GameHeightPx} px tall, x{Zoom}, {CharacterPacks.FrameRate} fps. red: impact, blue: end", Text, 2);
            int top = h - Margin - 16;
            foreach (var a in rows)
            {
                var s = b.Clips[a.Clip.Substring(b.Character.Prefix.Length + 1)];
                string timing = a.Role == "walk" ? $"cycle {a.EndFrame} frames, stride {a.StrideM:0.00} m"
                    : a.Role == "idle" ? $"cycle {a.EndFrame} frames"
                    : $"impact {a.ImpactFrame} ({a.Impact:0.00} s), end {a.EndFrame} ({a.End:0.00} s)" + (a.RootMotionM > 0 ? $", root {a.RootMotionM:0.00} m" : "");
                PixelText.Draw(px, w, h, Margin, top - 2, $"{a.Use}: {a.Clip}, {a.Frames} frames, {timing}", Text, 2);
                PixelText.Draw(px, w, h, Margin, top - 16, a.Method, Text, 2);
                int cellH = s.H * Zoom, cellW = s.W * Zoom, y0 = top - LabelH - cellH;
                for (int f = 0; f < s.Frames.Count; f++)
                {
                    if (f > 0 && f % PerLine(a) == 0) y0 -= cellH + Gap + 2;
                    int x0 = Margin + 1 + f % PerLine(a) * (cellW + Gap + 2);
                    var frame = f == a.ImpactFrame && a.Role != "walk" && a.Role != "idle" ? Impact : f == a.EndFrame ? EndColor : Background;
                    Fill(px, w, x0 - 1, y0 - 1, cellW + 2, cellH + 2, frame);
                    Fill(px, w, x0, y0, cellW, cellH, Cell);
                    Fill(px, w, x0, y0 + s.Pivot.y * Zoom - 1, cellW, 1, Ground);
                    for (int y = 0; y < s.H; y++)
                    for (int x = 0; x < s.W; x++)
                    {
                        var c = s.Frames[f][y * s.W + x];
                        if (c.a != 0) Fill(px, w, x0 + x * Zoom, y0 + y * Zoom, Zoom, Zoom, c);
                    }
                }
                top = y0 - Gap * 2 - 4;
            }
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            tex.SetPixels32(px);
            tex.Apply();
            RenderTag.Save(tex, path, "PX3 clip annotations " + b.Character.Name, 80);
            UnityEngine.Object.DestroyImmediate(tex);
            Debug.Log("ClipAnnotator: wrote " + path);
        }

        static void Fill(Color32[] px, int w, int x0, int y0, int cw, int ch, Color32 c)
        {
            int h = px.Length / w;
            for (int y = Math.Max(0, y0); y < Math.Min(h, y0 + ch); y++)
            for (int x = Math.Max(0, x0); x < Math.Min(w, x0 + cw); x++)
                px[y * w + x] = c;
        }
    }
}
