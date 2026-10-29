using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Demo2D.EditorTools.Assets
{
    /// <summary>
    /// PX3's sprite baker (README.md, Sprites). For each character it reads the frames of every
    /// clip the clip library names, and puts them on the grid:
    /// - <b>scale:</b> one per character, so its walk's first frame stands the catalog's
    ///   `gameHeightPx` tall;
    /// - <b>anchor:</b> each clip has its own canvas, so each clip is anchored at the feet: the walk's
    ///   first frame's feet (the bottom of its lowest opaque row, and the middle of its lowest
    ///   eighth), carried to the clip's first frame by matching its silhouette to the walk's
    ///   (or that frame's own feet when the poses differ). A clip's frames share its canvas, so
    ///   motion the frames carry (a lunge, a fall) stays in them;
    /// - <b>downsample:</b> an exact area filter from the clip's trimmed canvas to whole grid pixels
    ///   placed around the feet, with alpha snapped to 0 or 1 at half coverage, trimmed again;
    /// - <b>atlas:</b> every frame of the character, cropped to its own opaque pixels, in one
    ///   point-filtered texture, `Assets/Baked/Characters/&lt;name&gt;.png`, with `&lt;name&gt;.json`
    ///   giving each clip's frame size and pivot (the feet, in pixels from the frame's bottom-left)
    ///   and each frame's crop: its rectangle in the atlas and its offset in the frame.
    /// The bake is deterministic: the clip library's pivots and sizes (ClipAnnotator) are the
    /// atlas's.
    /// </summary>
    public static class SpriteBaker
    {
        public const string OutDir = "Assets/Baked/Characters";
        /// <summary>The coverage at which a grid pixel becomes opaque.</summary>
        public const float AlphaSnap = 0.5f;
        /// <summary>The annotator measures motion on a finer bake, this many times the game's.</summary>
        public const int AnalysisFactor = 4;
        /// <summary>The share of the walk's height, from the ground up, whose middle is the feet.</summary>
        const float FeetBand = 0.125f;
        /// <summary>Below this silhouette overlap with the walk, a clip anchors at its own feet.</summary>
        const float MinMatch = 0.45f;

        /// <summary>A clip's frames on a grid: every frame W×H, bottom-up, the feet at Pivot.</summary>
        public sealed class Sequence
        {
            public int W, H;
            public Vector2Int Pivot;
            public List<Color32[]> Frames = new List<Color32[]>();
            public bool Opaque(int frame, int x, int y) => Frames[frame][y * W + x].a != 0;
        }

        public sealed class Baked
        {
            public CharacterPacks.Character Character;
            /// <summary>Grid pixels per source pixel, and the walk's height in source pixels.</summary>
            public float Scale;
            public int SourceHeight;
            public SortedDictionary<string, Sequence> Clips = new SortedDictionary<string, Sequence>(StringComparer.Ordinal);
            public SortedDictionary<string, Sequence> Analysis = new SortedDictionary<string, Sequence>(StringComparer.Ordinal);
            /// <summary>How each clip was anchored.</summary>
            public SortedDictionary<string, string> Anchor = new SortedDictionary<string, string>(StringComparer.Ordinal);
        }

        /// <summary>Bakes every character's library clips into Baked/ and returns them.</summary>
        public static List<Baked> Run()
        {
            var baked = BakeAll();
            Directory.CreateDirectory(OutDir);
            foreach (var b in baked) WriteAtlas(b);
            AssetDatabase.Refresh();
            foreach (var b in baked) ConfigureImport($"{OutDir}/{b.Character.Name}.png");
            return baked;
        }

        /// <summary>Bakes in memory: each character's clips that the clip library names.</summary>
        public static List<Baked> BakeAll(bool analysis = true)
        {
            var wanted = CharacterPacks.LibraryClips().Select(c => c.clip).ToHashSet();
            var result = new List<Baked>();
            foreach (var ch in CharacterPacks.Load())
            {
                var clips = wanted.Where(c => c.StartsWith(ch.Prefix + "-", StringComparison.Ordinal))
                                  .Select(c => c.Substring(ch.Prefix.Length + 1)).OrderBy(c => c, StringComparer.Ordinal).ToList();
                if (clips.Count == 0) continue;
                result.Add(Bake(ch, clips, analysis));
            }
            return result;
        }

        public static Baked Bake(CharacterPacks.Character ch, List<string> clips, bool analysis = true)
        {
            var files = ch.Frames();
            if (!files.TryGetValue("walk", out var walkFiles)) throw new InvalidOperationException($"SpriteBaker: {ch.Name} has no walk");
            var walk = Image.Read(walkFiles[0]);
            var walkBounds = walk.Bounds(128) ?? throw new InvalidOperationException($"SpriteBaker: {ch.Name}'s walk is empty");
            var walkFeet = walk.Feet(walkBounds);
            var b = new Baked { Character = ch, SourceHeight = walkBounds.height, Scale = (float)ch.GameHeightPx / walkBounds.height };
            foreach (var clip in clips)
            {
                if (!files.TryGetValue(clip, out var paths)) throw new InvalidOperationException($"SpriteBaker: no frames for {ch.Prefix}-{clip}");
                var frames = paths.Select(Image.Read).ToList();
                var (feet, how) = clip == "walk" ? (walkFeet, "walk") : Anchor(frames[0], walk, walkFeet);
                b.Anchor[clip] = how;
                b.Clips[clip] = Resample(frames, feet, b.Scale);
                if (analysis) b.Analysis[clip] = Resample(frames, feet, b.Scale * AnalysisFactor);
            }
            return b;
        }

        /// <summary>A source frame's pixels, bottom-up.</summary>
        public sealed class Image
        {
            public int W, H;
            public Color32[] Px;

            public static Image Read(string path)
            {
                var tex = CharacterPacks.ReadPng(path);
                try { return new Image { W = tex.width, H = tex.height, Px = tex.GetPixels32() }; }
                finally { UnityEngine.Object.DestroyImmediate(tex); }
            }

            public bool Solid(int x, int y, byte alpha) => x >= 0 && y >= 0 && x < W && y < H && Px[y * W + x].a >= alpha;

            public RectInt? Bounds(byte alpha)
            {
                int x0 = int.MaxValue, y0 = int.MaxValue, x1 = -1, y1 = -1;
                for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    if (Px[y * W + x].a < alpha) continue;
                    x0 = Math.Min(x0, x); y0 = Math.Min(y0, y); x1 = Math.Max(x1, x); y1 = Math.Max(y1, y);
                }
                return x1 < 0 ? (RectInt?)null : new RectInt(x0, y0, x1 - x0 + 1, y1 - y0 + 1);
            }

            /// <summary>The feet: the bottom edge of the lowest solid row, at the middle of the solid
            /// pixels in the lowest eighth of the figure.</summary>
            public Vector2 Feet(RectInt bounds)
            {
                int top = bounds.y + Mathf.Max(1, Mathf.RoundToInt(bounds.height * FeetBand));
                double sum = 0;
                int n = 0;
                for (int y = bounds.y; y < top; y++)
                for (int x = bounds.x; x < bounds.xMax; x++)
                    if (Px[y * W + x].a >= 128) { sum += x + 0.5; n++; }
                return new Vector2((float)(sum / n), bounds.y);
            }
        }

        /// <summary>
        /// Where the walk's feet fall in another clip's canvas: the walk's first frame is slid over
        /// the clip's first frame (at a quarter of the resolution, then at full) to the offset where
        /// their silhouettes overlap most. A clip that starts in another pose falls back to its own feet.
        /// </summary>
        static (Vector2 feet, string how) Anchor(Image first, Image walk, Vector2 walkFeet)
        {
            var bounds = first.Bounds(128) ?? throw new InvalidOperationException("SpriteBaker: a clip's first frame is empty");
            var own = first.Feet(bounds);
            var guess = Vector2Int.RoundToInt(own - walkFeet);
            const int q = 4;
            var a = Mask.Of(walk, q);
            var c = Mask.Of(first, q);
            var (coarse, _) = Best(a, c, new Vector2Int(Mathf.RoundToInt((float)guess.x / q), Mathf.RoundToInt((float)guess.y / q)), 12);
            var (fine, iou) = Best(Mask.Of(walk, 1), Mask.Of(first, 1), coarse * q, q + 1);
            if (iou < MinMatch) return (own, $"own feet (overlap {iou:0.00})");
            return (walkFeet + fine, $"matched walk (overlap {iou:0.00})");
        }

        sealed class Mask
        {
            public int W, H;
            public bool[] On;
            public List<Vector2Int> Cells = new List<Vector2Int>();

            /// <summary>Solid where at least half of a factor×factor block is solid.</summary>
            public static Mask Of(Image img, int factor)
            {
                var m = new Mask { W = (img.W + factor - 1) / factor, H = (img.H + factor - 1) / factor };
                m.On = new bool[m.W * m.H];
                for (int y = 0; y < m.H; y++)
                for (int x = 0; x < m.W; x++)
                {
                    int n = 0;
                    for (int dy = 0; dy < factor; dy++)
                    for (int dx = 0; dx < factor; dx++)
                        if (img.Solid(x * factor + dx, y * factor + dy, 128)) n++;
                    if (n * 2 < factor * factor) continue;
                    m.On[y * m.W + x] = true;
                    m.Cells.Add(new Vector2Int(x, y));
                }
                return m;
            }

            public bool At(int x, int y) => x >= 0 && y >= 0 && x < W && y < H && On[y * W + x];
        }

        /// <summary>The offset d within ±radius of the centre maximising IoU(a shifted by d, b).</summary>
        static (Vector2Int d, float iou) Best(Mask a, Mask b, Vector2Int centre, int radius)
        {
            var best = (centre, -1f);
            for (int dy = -radius; dy <= radius; dy++)
            for (int dx = -radius; dx <= radius; dx++)
            {
                var d = new Vector2Int(centre.x + dx, centre.y + dy);
                int inter = 0;
                foreach (var p in a.Cells) if (b.At(p.x + d.x, p.y + d.y)) inter++;
                float iou = (float)inter / (a.Cells.Count + b.Cells.Count - inter);
                // Ties go to the offset nearest the centre, so the result doesn't depend on scan order.
                if (iou > best.Item2 + 1e-6f || (Mathf.Abs(iou - best.Item2) <= 1e-6f && (d - centre).sqrMagnitude < (best.centre - centre).sqrMagnitude))
                    best = (d, iou);
            }
            return best;
        }

        /// <summary>
        /// Area-downsamples a clip's frames onto whole grid pixels around the feet: grid pixel (i, j)
        /// from the pivot covers source [feet + i/scale, feet + (i+1)/scale) on each axis. Then
        /// alpha snaps at half coverage, and the frames are trimmed to their union.
        /// </summary>
        public static Sequence Resample(List<Image> frames, Vector2 feet, float scale)
        {
            RectInt? union = null;
            foreach (var f in frames)
                if (f.Bounds(1) is RectInt r)
                    union = union is RectInt u ? Union(u, r) : r;
            if (union is not RectInt src) throw new InvalidOperationException("SpriteBaker: every frame is empty");
            int gx0 = Mathf.FloorToInt((src.xMin - feet.x) * scale), gx1 = Mathf.CeilToInt((src.xMax - feet.x) * scale);
            int gy0 = Mathf.FloorToInt((src.yMin - feet.y) * scale), gy1 = Mathf.CeilToInt((src.yMax - feet.y) * scale);
            int w = gx1 - gx0, h = gy1 - gy0;
            var xs = Weights(gx0, w, feet.x, scale, frames[0].W);
            var ys = Weights(gy0, h, feet.y, scale, frames[0].H);
            float area = 1f / (scale * scale);

            var seq = new Sequence { W = w, H = h, Pivot = new Vector2Int(-gx0, -gy0) };
            foreach (var f in frames)
            {
                var o = new Color32[w * h];
                for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    double r = 0, g = 0, bl = 0, a = 0;
                    foreach (var (sy, wy) in ys[y])
                    foreach (var (sx, wx) in xs[x])
                    {
                        if (sx >= f.W || sy >= f.H) continue;
                        var c = f.Px[sy * f.W + sx];
                        double al = c.a / 255.0 * wx * wy;
                        r += c.r * al; g += c.g * al; bl += c.b * al; a += al;
                    }
                    if (a < AlphaSnap * area) continue;
                    o[y * w + x] = new Color32((byte)Math.Round(r / a), (byte)Math.Round(g / a), (byte)Math.Round(bl / a), 255);
                }
                seq.Frames.Add(o);
            }
            return Trim(seq);
        }

        static RectInt Union(RectInt a, RectInt b)
        {
            int x0 = Math.Min(a.xMin, b.xMin), y0 = Math.Min(a.yMin, b.yMin);
            return new RectInt(x0, y0, Math.Max(a.xMax, b.xMax) - x0, Math.Max(a.yMax, b.yMax) - y0);
        }

        /// <summary>For each output column (or row), the source pixels it overlaps and by how much.</summary>
        static List<(int src, double w)>[] Weights(int g0, int n, float feet, float scale, int size)
        {
            var result = new List<(int, double)>[n];
            for (int i = 0; i < n; i++)
            {
                double a = feet + (g0 + i) / (double)scale, b = feet + (g0 + i + 1) / (double)scale;
                var list = new List<(int, double)>();
                for (int s = Math.Max(0, (int)Math.Floor(a)); s < Math.Min(size, (int)Math.Ceiling(b)); s++)
                {
                    double w = Math.Min(b, s + 1) - Math.Max(a, s);
                    if (w > 0) list.Add((s, w));
                }
                result[i] = list;
            }
            return result;
        }

        /// <summary>Crops a sequence to the union of its opaque pixels, keeping the pivot.</summary>
        static Sequence Trim(Sequence s)
        {
            int x0 = int.MaxValue, y0 = int.MaxValue, x1 = -1, y1 = -1;
            for (int f = 0; f < s.Frames.Count; f++)
            for (int y = 0; y < s.H; y++)
            for (int x = 0; x < s.W; x++)
            {
                if (!s.Opaque(f, x, y)) continue;
                x0 = Math.Min(x0, x); y0 = Math.Min(y0, y); x1 = Math.Max(x1, x); y1 = Math.Max(y1, y);
            }
            if (x1 < 0) throw new InvalidOperationException("SpriteBaker: a clip vanishes at its game size");
            int w = x1 - x0 + 1, h = y1 - y0 + 1;
            var t = new Sequence { W = w, H = h, Pivot = s.Pivot - new Vector2Int(x0, y0) };
            foreach (var f in s.Frames)
            {
                var o = new Color32[w * h];
                for (int y = 0; y < h; y++) Array.Copy(f, (y0 + y) * s.W + x0, o, y * w, w);
                t.Frames.Add(o);
            }
            return t;
        }

        /// <summary>One baked frame in the atlas: its opaque crop, and where that crop sits in the clip's frame.</summary>
        public readonly struct Placed
        {
            public readonly RectInt Rect;
            public readonly Vector2Int Offset;
            public Placed(RectInt rect, Vector2Int offset) { Rect = rect; Offset = offset; }
        }

        /// <summary>
        /// Shelf-packs the character's frames into one texture, each cropped to its own opaque
        /// pixels (a slash's arc widens only its own frames), tallest first, a clear pixel between them.
        /// </summary>
        public static (Color32[] px, int w, int h, Dictionary<string, List<Placed>> frames) Pack(Baked b)
        {
            const int pad = 1;
            var crops = new List<(string clip, int frame, RectInt crop)>();
            foreach (var (name, s) in b.Clips)
                for (int f = 0; f < s.Frames.Count; f++)
                    crops.Add((name, f, Crop(s, f)));
            var order = crops.OrderByDescending(c => c.crop.height).ThenBy(c => c.clip, StringComparer.Ordinal).ThenBy(c => c.frame).ToList();
            long area = crops.Sum(c => (long)(c.crop.width + pad) * (c.crop.height + pad));
            int width = RoundUp(Mathf.Max(256, Mathf.CeilToInt(Mathf.Sqrt(area * 1.1f)), crops.Max(c => c.crop.width) + 2 * pad), 64);
            var placed = b.Clips.ToDictionary(kv => kv.Key, kv => new Placed[kv.Value.Frames.Count]);
            int x = pad, y = pad, shelf = 0;
            foreach (var (clip, frame, crop) in order)
            {
                if (x + crop.width + pad > width) { x = pad; y += shelf + pad; shelf = 0; }
                placed[clip][frame] = new Placed(new RectInt(x, y, crop.width, crop.height), crop.position);
                x += crop.width + pad;
                shelf = Math.Max(shelf, crop.height);
            }
            int height = RoundUp(y + shelf + pad, 4);
            var px = new Color32[width * height];
            foreach (var (clip, frame, crop) in crops)
            {
                var s = b.Clips[clip];
                var r = placed[clip][frame].Rect;
                for (int row = 0; row < crop.height; row++)
                    Array.Copy(s.Frames[frame], (crop.y + row) * s.W + crop.x, px, (r.y + row) * width + r.x, crop.width);
            }
            return (px, width, height, placed.ToDictionary(kv => kv.Key, kv => kv.Value.ToList()));
        }

        static int RoundUp(int v, int step) => (v + step - 1) / step * step;

        /// <summary>A frame's opaque pixels' bounds in its clip's frame (one clear pixel if it has none).</summary>
        static RectInt Crop(Sequence s, int f)
        {
            int x0 = int.MaxValue, y0 = int.MaxValue, x1 = -1, y1 = -1;
            for (int y = 0; y < s.H; y++)
            for (int x = 0; x < s.W; x++)
            {
                if (!s.Opaque(f, x, y)) continue;
                x0 = Math.Min(x0, x); y0 = Math.Min(y0, y); x1 = Math.Max(x1, x); y1 = Math.Max(y1, y);
            }
            return x1 < 0 ? new RectInt(0, 0, 1, 1) : new RectInt(x0, y0, x1 - x0 + 1, y1 - y0 + 1);
        }

        static void WriteAtlas(Baked b)
        {
            var (px, w, h, frames) = Pack(b);
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            tex.SetPixels32(px);
            tex.Apply();
            File.WriteAllBytes($"{OutDir}/{b.Character.Name}.png", tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);

            var inv = CultureInfo.InvariantCulture;
            var j = new StringBuilder();
            j.Append("{\n  \"type\": \"").Append(b.Character.Name).Append("\", \"atlas\": \"").Append(b.Character.Name).Append(".png\", ")
             .Append($"\"atlasPx\": [{w}, {h}], \"frameRate\": {CharacterPacks.FrameRate}, \"pixelsPerMetre\": {PixelShots.PixelsPerMetre},\n")
             .Append("  \"note\": \"PX3's bake. Per clip: its frame's size and pivot (the feet) in grid pixels from the frame's bottom-left, ")
             .Append("and per frame [x, y, w, h, ox, oy]: its opaque crop's rectangle in the atlas (from the atlas's bottom-left) and the crop's offset in the clip's frame.\",\n")
             .Append("  \"clips\": {\n");
            var names = b.Clips.Keys.ToList();
            for (int i = 0; i < names.Count; i++)
            {
                var s = b.Clips[names[i]];
                j.Append($"    \"{names[i]}\": {{\"sizePx\": [{s.W}, {s.H}], \"pivotPx\": [{s.Pivot.x}, {s.Pivot.y}], \"frames\": [")
                 .Append(string.Join(", ", frames[names[i]].Select(p => string.Format(inv, "[{0}, {1}, {2}, {3}, {4}, {5}]",
                     p.Rect.x, p.Rect.y, p.Rect.width, p.Rect.height, p.Offset.x, p.Offset.y))))
                 .Append(i + 1 < names.Count ? "]},\n" : "]}\n");
            }
            j.Append("  }\n}\n");
            File.WriteAllText($"{OutDir}/{b.Character.Name}.json", j.ToString());
            Debug.Log($"SpriteBaker: {b.Character.Name}, {b.Clips.Count} clips, {b.Clips.Values.Sum(s => s.Frames.Count)} frames, {w}x{h} atlas");
        }

        /// <summary>Point-filtered, uncompressed, no mipmaps: texels reach the grid as baked.</summary>
        static void ConfigureImport(string path)
        {
            var imp = (TextureImporter)AssetImporter.GetAtPath(path);
            imp.textureType = TextureImporterType.Default;
            imp.sRGBTexture = true;
            imp.alphaSource = TextureImporterAlphaSource.FromInput;
            imp.alphaIsTransparency = false;
            imp.mipmapEnabled = false;
            imp.filterMode = FilterMode.Point;
            imp.wrapMode = TextureWrapMode.Clamp;
            imp.textureCompression = TextureImporterCompression.Uncompressed;
            imp.npotScale = TextureImporterNPOTScale.None;
            imp.maxTextureSize = 8192;
            imp.isReadable = false;
            imp.SaveAndReimport();
        }

        /// <summary>A Sprite for one baked frame, pivoted at its clip's feet (for stills).</summary>
        public static Sprite Sprite(Texture2D atlas, Placed p, Vector2Int pivot)
        {
            var r = p.Rect;
            var at = pivot - p.Offset;
            return UnityEngine.Sprite.Create(atlas, new Rect(r.x, r.y, r.width, r.height),
                new Vector2((float)at.x / r.width, (float)at.y / r.height), PixelShots.PixelsPerMetre, 0, SpriteMeshType.FullRect);
        }
    }
}
