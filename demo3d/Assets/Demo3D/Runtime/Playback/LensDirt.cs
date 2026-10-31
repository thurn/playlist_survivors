using System;
using UnityEngine;

namespace Demo3D.Runtime.Playback
{
    /// <summary>
    /// The trailer's lens dirt (trailer.md, "Look"): URP's bloom adds it where the frame blooms, so effect-heavy moments
    /// glow as if seen through a real lens. Original, generated here from a fixed seed: soft smudges gathering toward the
    /// edges, a few water spots with brighter rims, dust specks and finger wipes, over a faint edge haze. Mostly black,
    /// so a frame without bloom shows none of it.
    /// </summary>
    public static class LensDirt
    {
        public const int Width = 1024, Height = 576; // 16:9, so URP's aspect fit neither crops nor stretches it
        const int Seed = 20261030;

        static Texture2D _texture;

        public static Texture2D Texture => _texture != null ? _texture : (_texture = Build());

        /// <summary>The dirt's linear RGB, row by row from the bottom: deterministic.</summary>
        public static Color[] Pixels()
        {
            var px = new Color[Width * Height];
            var rng = new System.Random(Seed);
            float R(float lo, float hi) => lo + (float)rng.NextDouble() * (hi - lo);
            Color Tint() => Color.Lerp(new Color(1f, 0.96f, 0.88f), new Color(0.88f, 0.95f, 1f), R(0f, 1f));

            // A point weighted toward the edges, where a real lens collects its smudges.
            Vector2 EdgeBiased()
            {
                while (true)
                {
                    var p = new Vector2(R(0, Width), R(0, Height));
                    float d = Vector2.Distance(new Vector2(p.x / Width, p.y / Height), new Vector2(0.5f, 0.5f)) / 0.7071f;
                    if (rng.NextDouble() < 0.25 + 0.75 * d) return p;
                }
            }

            // The faint haze, brighter toward the corners.
            for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
            {
                float dx = x / (float)Width - 0.5f, dy = y / (float)Height - 0.5f;
                float h = 0.015f + 0.06f * (dx * dx + dy * dy) * 2f;
                px[y * Width + x] = new Color(h, h, h);
            }
            for (int i = 0; i < 70; i++) // smudges: soft elliptical blobs
            {
                var c = EdgeBiased();
                float r = Mathf.Exp(R(Mathf.Log(24), Mathf.Log(120)));
                Blob(px, c, r, r * R(1f, 2.2f), R(0, Mathf.PI), R(0.04f, 0.14f), Tint(), Falloff.Gaussian);
            }
            for (int i = 0; i < 26; i++) // water spots: flat discs with a brighter rim
            {
                var c = EdgeBiased();
                float r = R(6, 26);
                Blob(px, c, r, r, 0, R(0.1f, 0.26f), Tint(), Falloff.Spot);
            }
            for (int i = 0; i < 500; i++) // dust specks
            {
                var c = new Vector2(R(0, Width), R(0, Height));
                float r = R(0.7f, 2.4f);
                Blob(px, c, r, r, 0, R(0.25f, 0.7f), Tint(), Falloff.Gaussian);
            }
            for (int i = 0; i < 6; i++) // finger wipes: faint streaks along a curve
            {
                Vector2 a = EdgeBiased(), b = a + new Vector2(R(-260, 260), R(-160, 160)), m = (a + b) / 2 + new Vector2(R(-80, 80), R(-80, 80));
                float r = R(6, 14), s = R(0.015f, 0.04f);
                var tint = Tint();
                for (float u = 0; u <= 1; u += 0.01f)
                {
                    var p = (1 - u) * (1 - u) * a + 2 * (1 - u) * u * m + u * u * b;
                    Blob(px, p, r, r * 0.5f, Mathf.Atan2(b.y - a.y, b.x - a.x), s, tint, Falloff.Gaussian);
                }
            }
            for (int i = 0; i < px.Length; i++)
                px[i] = new Color(Mathf.Clamp01(px[i].r), Mathf.Clamp01(px[i].g), Mathf.Clamp01(px[i].b), 1f);
            return px;
        }

        enum Falloff { Gaussian, Spot }

        /// <summary>Adds an ellipse of radii <paramref name="ra"/> and <paramref name="rb"/>, turned <paramref name="angle"/>, at <paramref name="c"/>.</summary>
        static void Blob(Color[] px, Vector2 c, float ra, float rb, float angle, float strength, Color tint, Falloff falloff)
        {
            float reach = 1.6f * Math.Max(ra, rb);
            int x0 = Math.Max(0, (int)(c.x - reach)), x1 = Math.Min(Width - 1, (int)(c.x + reach));
            int y0 = Math.Max(0, (int)(c.y - reach)), y1 = Math.Min(Height - 1, (int)(c.y + reach));
            float cos = Mathf.Cos(angle), sin = Mathf.Sin(angle);
            for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                float dx = x + 0.5f - c.x, dy = y + 0.5f - c.y;
                float u = (dx * cos + dy * sin) / ra, v = (-dx * sin + dy * cos) / rb;
                float d2 = u * u + v * v;
                float w;
                if (falloff == Falloff.Gaussian) w = Mathf.Exp(-3f * d2);
                else
                {
                    float d = Mathf.Sqrt(d2);
                    w = (0.7f + 0.6f * Mathf.SmoothStep(0.6f, 0.95f, d)) * (1f - Mathf.SmoothStep(0.95f, 1.1f, d));
                }
                if (w < 1e-3f) continue;
                px[y * Width + x] += tint * (strength * w);
            }
        }

        static Texture2D Build()
        {
            var tex = new Texture2D(Width, Height, TextureFormat.RGBA32, false, true)
            {
                name = "LensDirt", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.DontSave,
            };
            tex.SetPixels(Pixels());
            tex.Apply(false, true);
            return tex;
        }
    }
}
