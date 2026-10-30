using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Demo2D.Runtime
{
    /// <summary>
    /// PX1's empty scene content, all generated (no vendor art): a 30×17 m checkered ground at 32
    /// grid pixels per metre, a few 56 px figures sorted by their feet, and three point lights
    /// under the moon's global light.
    /// </summary>
    public sealed class TestCard : MonoBehaviour
    {
        /// <summary>URP's lit sprite material. A sprite renderer made at runtime in a player gets
        /// the unlit default, so the scene names this one (ProjectSetup sets it).</summary>
        public Material litMaterial;

        public const int PixelsPerMetre = 32;
        public const float BaseViewHeightM = 540f / PixelsPerMetre;
        const int GroundW = 960, GroundH = 544;

        void Awake()
        {
            var ground = new GameObject("Ground").AddComponent<SpriteRenderer>();
            ground.transform.SetParent(transform, false);
            ground.sprite = Sprite.Create(GroundTexture(), new Rect(0, 0, GroundW, GroundH), new Vector2(0.5f, 0.5f), PixelsPerMetre);
            ground.sharedMaterial = litMaterial;
            ground.sortingOrder = -1000;

            var figure = Sprite.Create(FigureTexture(), new Rect(0, 0, 24, 56), new Vector2(0.5f, 0f), PixelsPerMetre);
            float[] xs = { -6f, -2.5f, 1f, 4.5f, 8f };
            for (int i = 0; i < xs.Length; i++)
                Figure(figure, new Vector2(xs[i], (i % 2 == 0 ? -1.5f : 0.5f) + 0.3f * i), i % 3);

            Light(new Vector2(-5, 1), new Color(1f, 0.6f, 0.25f), 6f, 1.4f);   // a fire
            Light(new Vector2(3, -2), new Color(0.4f, 1f, 0.9f), 4f, 1.2f);    // a glowing plant
            Light(new Vector2(9, 3), new Color(0.9f, 0.4f, 1f), 5f, 1.0f);     // a spell
        }

        void Figure(Sprite sprite, Vector2 at, int tint)
        {
            var r = new GameObject("Figure").AddComponent<SpriteRenderer>();
            r.transform.SetParent(transform, false);
            r.transform.localPosition = at;
            r.sprite = sprite;
            r.sharedMaterial = litMaterial;
            r.color = tint == 0 ? Color.white : tint == 1 ? new Color(0.8f, 0.9f, 1f) : new Color(1f, 0.85f, 0.8f);
        }

        void Light(Vector2 at, Color color, float radius, float intensity)
        {
            var l = new GameObject("Light").AddComponent<Light2D>();
            l.transform.SetParent(transform, false);
            l.transform.localPosition = at;
            l.lightType = Light2D.LightType.Point;
            l.color = color;
            l.intensity = intensity;
            l.pointLightOuterRadius = radius;
            l.pointLightInnerRadius = radius * 0.2f;
        }

        static Texture2D GroundTexture()
        {
            var tex = NewTexture(GroundW, GroundH);
            var px = new Color32[GroundW * GroundH];
            Color32 a = new Color32(58, 82, 52, 255), b = new Color32(50, 72, 46, 255), path = new Color32(96, 82, 62, 255);
            for (int y = 0; y < GroundH; y++)
            for (int x = 0; x < GroundW; x++)
            {
                bool odd = ((x / PixelsPerMetre) + (y / PixelsPerMetre)) % 2 == 1;
                bool onPath = Mathf.Abs(y - GroundH / 2 - 60 * Mathf.Sin(x / 150f)) < 20;
                px[y * GroundW + x] = onPath ? path : odd ? a : b;
            }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            return tex;
        }

        static Texture2D FigureTexture()
        {
            const int w = 24, h = 56;
            var tex = NewTexture(w, h);
            var px = new Color32[w * h];
            Color32 body = new Color32(196, 200, 210, 255), head = new Color32(232, 214, 190, 255), clear = new Color32(0, 0, 0, 0);
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                bool inBody = y < 40 && x >= 5 && x < 19;
                bool inHead = y >= 41 && y < 55 && (x - 11.5f) * (x - 11.5f) + (y - 48f) * (y - 48f) < 42;
                px[y * w + x] = inHead ? head : inBody ? body : clear;
            }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            return tex;
        }

        static Texture2D NewTexture(int w, int h) =>
            new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
    }
}
