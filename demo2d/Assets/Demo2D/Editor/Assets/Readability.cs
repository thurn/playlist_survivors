using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using PS.Choreography;
using UnityEngine;

namespace Demo2D.EditorTools.Assets
{
    /// <summary>
    /// PX9's readability measure (Tasks.Readability): is the hero findable, and is each monster's
    /// silhouette distinct, at the climax's zoom? From the baked atlases and the night's data, per type
    /// (the knight as the hero), the walk's first frame at game size:
    /// - <b>lit colour:</b> the mean of its opaque texels' albedo times the light, in linear light as the
    ///   2D renderer lights them, then CIELAB; under the moon alone, and near the hero (the moon plus
    ///   half the hero's light, where most of the climax's crowd stands);
    /// - <b>ground contrast:</b> ΔE (CIE76) from the base grass under the same light;
    /// - <b>colour distance:</b> ΔE to the nearest other type;
    /// - <b>shape overlap:</b> the largest intersection over union of its alpha mask with another type's,
    ///   both facing right with their feet aligned (at the climax's 1.48× upscale a texel is about a
    ///   screen pixel and a half, so the masks are what the eye gets).
    /// A type is distinct when its nearest neighbour differs by colour (ΔE ≥ <see cref="MinDeltaE"/>)
    /// or by shape (overlap ≤ <see cref="MaxOverlap"/>). The hero is findable when he is distinct and
    /// his outline (SpriteCrowd's rim) stands off the ground and every type by at least
    /// <see cref="MinRimDeltaE"/>. Writes readability.txt and returns whether everything passes.
    /// </summary>
    public static class Readability
    {
        const float MinDeltaE = 8f, MaxOverlap = 0.7f, MinRimDeltaE = 25f;
        /// <summary>SpriteCrowd's HeroOutline, as the 1080p image shows it (clipped to 1).</summary>
        static readonly Vector3 Rim = new Vector3(1f, 0.92f, 0.55f);

        sealed class Type
        {
            public string Name;
            public bool[] Mask;
            public int W, H, PivotX;
            public Vector3 Albedo; // mean linear albedo of the opaque texels
            public Vector3 LabMoon, LabNear;
        }

        public static bool Write(string dir)
        {
            Directory.CreateDirectory(dir);
            var night = JsonValue.Parse(File.ReadAllText(Path.Combine(CharacterPacks.DataDir, "night-lighting.json")));
            Vector3 Rgb(JsonValue v) => new Vector3(v[0].AsFloat(), v[1].AsFloat(), v[2].AsFloat());
            Vector3 moon = Rgb(night["moon"]["color"]) * night["moon"]["intensity"].AsFloat();
            Vector3 near = moon + Rgb(night["hero"]["color"]) * (0.5f * night["hero"]["intensity"].AsFloat());

            var types = CharacterPacks.Load().Select(c => Load(c.Name)).ToList();
            var grass = GrassAlbedo();
            Vector3 groundMoon = Lab(Vector3.Scale(grass, moon)), groundNear = Lab(Vector3.Scale(grass, near));
            foreach (var t in types)
            {
                t.LabMoon = Lab(Vector3.Scale(t.Albedo, moon));
                t.LabNear = Lab(Vector3.Scale(t.Albedo, near));
            }

            var sb = new StringBuilder();
            sb.AppendLine("PX9 readability at the climax's zoom: per type, the walk's first frame lit by the night (night-lighting.json).");
            sb.AppendLine($"Distinct: nearest colour ΔE ≥ {MinDeltaE} or largest shape overlap ≤ {MaxOverlap}. Hero findable: distinct, and his rim ΔE ≥ {MinRimDeltaE} from the ground and every type.");
            sb.AppendLine();
            sb.AppendLine("type             px   L*moon  ΔE ground  ΔE nearest (moon / near hero)       overlap (with)            distinct");
            bool pass = true;
            foreach (var t in types)
            {
                var others = types.Where(o => o != t).ToList();
                var nearMoon = others.OrderBy(o => Dist(o.LabMoon, t.LabMoon)).First();
                var nearNear = others.OrderBy(o => Dist(o.LabNear, t.LabNear)).First();
                float dMoon = Dist(nearMoon.LabMoon, t.LabMoon), dNear = Dist(nearNear.LabNear, t.LabNear);
                var shape = others.Select(o => (o, iou: Overlap(t, o))).OrderByDescending(x => x.iou).First();
                // Colour must hold in both lights for it to count; shape holds in any light.
                bool distinct = Math.Min(dMoon, dNear) >= MinDeltaE || shape.iou <= MaxOverlap;
                pass &= distinct;
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "{0,-16} {1,3}  {2,6:F1}  {3,9:F1}  {4,5:F1} ({5}) / {6,5:F1} ({7})  {8,5:F2} ({9})  {10}",
                    t.Name, t.H, t.LabMoon.x, Dist(t.LabMoon, groundMoon), dMoon, nearMoon.Name, dNear, nearNear.Name,
                    shape.iou, shape.o.Name, distinct ? "yes" : "NO"));
            }

            // The rim is unlit: it shows as its own colour over whatever is behind it.
            var hero = types.Single(t => t.Name == "knight");
            Vector3 rim = Lab(Rim);
            float rimGround = Math.Min(Dist(rim, groundMoon), Dist(rim, groundNear));
            var rimNearest = types.Where(t => t != hero).Select(t => (t, d: Math.Min(Dist(rim, t.LabMoon), Dist(rim, t.LabNear)))).OrderBy(x => x.d).First();
            bool findable = rimGround >= MinRimDeltaE && rimNearest.d >= MinRimDeltaE;
            pass &= findable;
            sb.AppendLine();
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "hero rim: ΔE {0:F1} from the ground, {1:F1} from the nearest type ({2}); drawn over any grunt in front of him: {3}",
                rimGround, rimNearest.d, rimNearest.t.Name, findable ? "findable" : "NOT findable"));
            sb.AppendLine(pass ? "PASS" : "FAIL");
            File.WriteAllText(Path.Combine(dir, "readability.txt"), sb.ToString());
            Debug.Log("Readability:\n" + sb);
            return pass;
        }

        static Type Load(string name)
        {
            string png = $"{SpriteBaker.OutDir}/{name}.png", json = $"{SpriteBaker.OutDir}/{name}.json";
            if (!File.Exists(png)) throw new FileNotFoundException($"Readability: no {png}: run demo2d unity BakeSprites");
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            tex.LoadImage(File.ReadAllBytes(png)); // readable, unlike the imported atlas
            try
            {
                var walk = JsonValue.Parse(File.ReadAllText(json))["clips"]["walk"];
                var f = walk["frames"][0];
                int ax = f[0].AsInt(), ay = f[1].AsInt(), w = f[2].AsInt(), h = f[3].AsInt(), ox = f[4].AsInt(), oy = f[5].AsInt();
                int fw = walk["sizePx"][0].AsInt(), fh = walk["sizePx"][1].AsInt();
                var t = new Type { Name = name, W = fw, H = fh, PivotX = walk["pivotPx"][0].AsInt(), Mask = new bool[fw * fh] };
                var sum = Vector3.zero;
                int n = 0;
                for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    var c = tex.GetPixel(ax + x, ay + y);
                    if (c.a < 0.5f) continue;
                    t.Mask[(oy + y) * fw + ox + x] = true;
                    sum += new Vector3(c.linear.r, c.linear.g, c.linear.b);
                    n++;
                }
                t.Albedo = sum / n;
                t.H = Enumerable.Range(0, fh).Last(y => Enumerable.Range(0, fw).Any(x => t.Mask[y * fw + x])) + 1 - walk["pivotPx"][1].AsInt();
                return t;
            }
            finally { UnityEngine.Object.DestroyImmediate(tex); }
        }

        /// <summary>The base grass's mean linear albedo, from its tile on the MainLev sheet.</summary>
        static Vector3 GrassAlbedo()
        {
            var catalog = ForestTiles.Load();
            var sprite = ForestTiles.Sprites(catalog.Sheet, catalog.TilePx)[catalog.Base];
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            tex.LoadImage(File.ReadAllBytes(catalog.Sheet));
            try
            {
                var r = sprite.rect;
                var sum = Vector3.zero;
                for (int y = 0; y < (int)r.height; y++)
                for (int x = 0; x < (int)r.width; x++)
                {
                    var c = tex.GetPixel((int)r.x + x, (int)r.y + y).linear;
                    sum += new Vector3(c.r, c.g, c.b);
                }
                return sum / (r.width * r.height);
            }
            finally { UnityEngine.Object.DestroyImmediate(tex); }
        }

        /// <summary>Intersection over union of two masks, feet aligned (pivot columns and bottom rows).</summary>
        static float Overlap(Type a, Type b)
        {
            int inter = 0, union = 0;
            int x0 = -Math.Max(a.PivotX, b.PivotX), x1 = Math.Max(a.W - a.PivotX, b.W - b.PivotX), h = Math.Max(a.Mask.Length / a.W, b.Mask.Length / b.W);
            for (int y = 0; y < h; y++)
            for (int x = x0; x < x1; x++)
            {
                bool ia = At(a, x + a.PivotX, y), ib = At(b, x + b.PivotX, y);
                if (ia && ib) inter++;
                if (ia || ib) union++;
            }
            return union == 0 ? 0 : inter / (float)union;
        }

        static bool At(Type t, int x, int y) => x >= 0 && x < t.W && y >= 0 && y < t.Mask.Length / t.W && t.Mask[y * t.W + x];

        static float Dist(Vector3 a, Vector3 b) => (a - b).magnitude;

        /// <summary>Linear RGB (clipped to 1) to CIELAB (D65).</summary>
        static Vector3 Lab(Vector3 rgb)
        {
            rgb = Vector3.Min(rgb, Vector3.one);
            float X = 0.4124f * rgb.x + 0.3576f * rgb.y + 0.1805f * rgb.z;
            float Y = 0.2126f * rgb.x + 0.7152f * rgb.y + 0.0722f * rgb.z;
            float Z = 0.0193f * rgb.x + 0.1192f * rgb.y + 0.9505f * rgb.z;
            static float F(float t) => t > 0.008856f ? Mathf.Pow(t, 1f / 3) : 7.787f * t + 16f / 116;
            float fx = F(X / 0.95047f), fy = F(Y), fz = F(Z / 1.08883f);
            return new Vector3(116 * fy - 16, 500 * (fx - fy), 200 * (fy - fz));
        }
    }
}
