using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Demo2D.Runtime;
using UnityEngine;

namespace Demo2D.EditorTools.Assets
{
    /// <summary>
    /// PX2's contact sheets: one per character, a row per clip with every frame in order, each
    /// row labelled with the clip, its frame count and its .anim's length (a `*` marks the clips
    /// the clip library uses). Each clip's frames are cropped to their union of opaque pixels and
    /// area-downsampled at one scale per character, the tallest clip filling the row; the sheet
    /// shows that every sequence is complete and in order, not the game-size bake (PX3's).
    /// </summary>
    public static class ContactSheets
    {
        const int RowH = 48, Gap = 2, LabelW = 200, Scale = 2;
        static readonly Color32 Background = new Color32(34, 36, 44, 255), Cell = new Color32(52, 56, 66, 255);
        static readonly Color32 Label = new Color32(230, 230, 230, 255), Used = new Color32(255, 214, 120, 255);

        public static void Write(string dir)
        {
            Directory.CreateDirectory(dir);
            var used = new HashSet<string>(CharacterPacks.LibraryClips().Select(c => c.clip));
            foreach (var ch in CharacterPacks.Load())
            {
                var path = Path.Combine(dir, $"contact-{ch.Name}.jpg");
                Write(ch, used, path);
                Debug.Log("ContactSheets: wrote " + path);
            }
        }

        static void Write(CharacterPacks.Character ch, HashSet<string> used, string path)
        {
            var clips = ch.Frames();
            if (clips.Count == 0) throw new InvalidOperationException($"ContactSheets: no frames in {ch.FramesDir}");
            var frames = clips.ToDictionary(kv => kv.Key, kv => kv.Value.Select(CharacterPacks.ReadPng).ToList());
            try
            {
                // Each clip has its own canvas; one scale for the character keeps sizes comparable.
                var bounds = frames.ToDictionary(kv => kv.Key, kv => OpaqueBounds(kv.Value));
                float scale = (float)RowH / bounds.Values.Max(b => b.height);
                var cellW = bounds.ToDictionary(kv => kv.Key, kv => Mathf.Max(4, Mathf.RoundToInt(kv.Value.width * scale)));
                int w = LabelW + frames.Max(kv => kv.Value.Count * (cellW[kv.Key] + Gap)) + Gap;
                int h = frames.Count * (RowH + Gap) + Gap + 24;
                var px = Enumerable.Repeat(Background, w * h).ToArray();
                PixelText.Draw(px, w, h, 6, h - 6, $"{ch.Name} ({ch.Pack}, {ch.Rank}), frames at {CharacterPacks.FrameRate} fps", Label, 3);
                int row = 0;
                foreach (var kv in frames)
                {
                    int yTop = h - 24 - Gap - row * (RowH + Gap);
                    var clip = ch.Clip(kv.Key);
                    string length = clip == null ? "no .anim" : clip.length.ToString("0.00", CultureInfo.InvariantCulture) + " s";
                    bool isUsed = used.Contains(ch.Prefix + "-" + kv.Key);
                    PixelText.Draw(px, w, h, 6, yTop - 8, (isUsed ? "*" : " ") + kv.Key, isUsed ? Used : Label, Scale);
                    PixelText.Draw(px, w, h, 6, yTop - 24, $" {kv.Value.Count} frames, {length}", Label, Scale);
                    int cw = cellW[kv.Key], cellH = Mathf.Max(4, Mathf.RoundToInt(bounds[kv.Key].height * scale));
                    for (int i = 0; i < kv.Value.Count; i++)
                        Blit(kv.Value[i], bounds[kv.Key], px, w, LabelW + Gap + i * (cw + Gap), yTop - RowH + 1, cw, cellH);
                    row++;
                }
                var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
                tex.SetPixels32(px);
                tex.Apply();
                RenderTag.Save(tex, path, "PX2 contact sheet " + ch.Name, 72);
                UnityEngine.Object.DestroyImmediate(tex);
            }
            finally
            {
                foreach (var t in frames.Values.SelectMany(f => f)) UnityEngine.Object.DestroyImmediate(t);
            }
        }

        /// <summary>The union of the frames' opaque pixels (a clip's frames share a canvas).</summary>
        public static RectInt OpaqueBounds(IEnumerable<Texture2D> frames)
        {
            int x0 = int.MaxValue, y0 = int.MaxValue, x1 = -1, y1 = -1;
            foreach (var f in frames)
            {
                var p = f.GetPixels32();
                for (int y = 0; y < f.height; y++)
                for (int x = 0; x < f.width; x++)
                {
                    if (p[y * f.width + x].a == 0) continue;
                    if (x < x0) x0 = x;
                    if (y < y0) y0 = y;
                    if (x > x1) x1 = x;
                    if (y > y1) y1 = y;
                }
            }
            if (x1 < 0) throw new InvalidOperationException("ContactSheets: every frame is empty");
            return new RectInt(x0, y0, x1 - x0 + 1, y1 - y0 + 1);
        }

        /// <summary>Area-downsamples the frame's crop into a cell (premultiplied, over the cell colour).</summary>
        static void Blit(Texture2D frame, RectInt crop, Color32[] dst, int dstW, int dx, int dy, int cw, int ch)
        {
            var src = frame.GetPixels32();
            float sx = (float)crop.width / cw, sy = (float)crop.height / ch;
            for (int y = 0; y < ch; y++)
            for (int x = 0; x < cw; x++)
            {
                int ax = crop.x + Mathf.FloorToInt(x * sx), bx = Mathf.Max(ax + 1, crop.x + Mathf.FloorToInt((x + 1) * sx));
                int ay = crop.y + Mathf.FloorToInt(y * sy), by = Mathf.Max(ay + 1, crop.y + Mathf.FloorToInt((y + 1) * sy));
                float r = 0, g = 0, b = 0, a = 0;
                int n = 0;
                for (int yy = ay; yy < by && yy < frame.height; yy++)
                for (int xx = ax; xx < bx && xx < frame.width; xx++)
                {
                    var c = src[yy * frame.width + xx];
                    float al = c.a / 255f;
                    r += c.r * al; g += c.g * al; b += c.b * al; a += al;
                    n++;
                }
                if (n == 0) continue;
                float cov = a / n;
                var bg = Cell;
                dst[(dy + y) * dstW + dx + x] = new Color32(
                    (byte)Mathf.Clamp(r / n + bg.r * (1 - cov), 0, 255),
                    (byte)Mathf.Clamp(g / n + bg.g * (1 - cov), 0, 255),
                    (byte)Mathf.Clamp(b / n + bg.b * (1 - cov), 0, 255), 255);
            }
        }
    }
}
