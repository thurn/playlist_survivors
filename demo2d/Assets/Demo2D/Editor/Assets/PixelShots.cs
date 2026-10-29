using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Demo2D.EditorTools.Assets
{
    /// <summary>
    /// Renders part of an editor scene through URP's 2D renderer at one target pixel per grid
    /// pixel (32 per metre) and reads it back, for PX2's checks and stills. Scenes are built in
    /// memory and never saved.
    /// </summary>
    public static class PixelShots
    {
        public const int PixelsPerMetre = 32;
        public static readonly Color NightSky = new Color(0.03f, 0.04f, 0.07f);

        /// <summary>
        /// Draws the world rectangle (whole metres) with an orthographic camera into a point-filtered
        /// sRGB target and returns its pixels, bottom row first. HDR off gives an 8-bit path from
        /// texture to target, so an unchanged texel comes back exactly.
        /// </summary>
        public static Color32[] Render(RectInt world, Color background, bool hdr = false)
        {
            int w = world.width * PixelsPerMetre, h = world.height * PixelsPerMetre;
            var go = new GameObject("PixelShot Camera");
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB)
            {
                filterMode = FilterMode.Point, antiAliasing = 1,
            };
            try
            {
                go.transform.position = new Vector3(world.x + world.width / 2f, world.y + world.height / 2f, -10);
                var cam = go.AddComponent<Camera>();
                cam.orthographic = true;
                cam.orthographicSize = world.height / 2f;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = background;
                cam.nearClipPlane = 0.1f;
                cam.farClipPlane = 100;
                cam.allowHDR = hdr;
                cam.allowMSAA = false;
                cam.targetTexture = rt;
                var data = go.AddComponent<UniversalAdditionalCameraData>();
                data.renderPostProcessing = false;
                cam.Render();
                var prev = RenderTexture.active;
                RenderTexture.active = rt;
                var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
                tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                tex.Apply();
                RenderTexture.active = prev;
                var px = tex.GetPixels32();
                UnityEngine.Object.DestroyImmediate(tex);
                cam.targetTexture = null;
                return px;
            }
            finally
            {
                rt.Release();
                UnityEngine.Object.DestroyImmediate(rt);
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        /// <summary>A global 2D light: white at 1 shows textures as they are; the moon is dim and cool.</summary>
        public static Light2D GlobalLight(Transform parent, bool night)
        {
            var l = new GameObject(night ? "Moonlight" : "Daylight").AddComponent<Light2D>();
            l.transform.SetParent(parent, false);
            l.lightType = Light2D.LightType.Global;
            l.color = night ? new Color(0.55f, 0.65f, 1f) : Color.white;
            l.intensity = night ? 0.35f : 1f;
            return l;
        }

        public static Light2D PointLight(Transform parent, Vector2 at, Color color, float radius, float intensity)
        {
            var l = new GameObject("Light").AddComponent<Light2D>();
            l.transform.SetParent(parent, false);
            l.transform.localPosition = at;
            l.lightType = Light2D.LightType.Point;
            l.color = color;
            l.intensity = intensity;
            l.pointLightOuterRadius = radius;
            l.pointLightInnerRadius = radius * 0.2f;
            return l;
        }

        /// <summary>URP's lit sprite material (renderers made from scripts don't always get it).</summary>
        public static Material SpriteLit =>
            AssetDatabase.LoadAssetAtPath<Material>(ProjectSetup.SpriteLitPath)
            ?? throw new InvalidOperationException("PixelShots: no " + ProjectSetup.SpriteLitPath);

        /// <summary>Copies a region of a bottom-up buffer into an opaque texture.</summary>
        public static Texture2D Crop(Color32[] px, int width, RectInt r)
        {
            var tex = new Texture2D(r.width, r.height, TextureFormat.RGBA32, false);
            var o = new Color32[r.width * r.height];
            for (int y = 0; y < r.height; y++)
            for (int x = 0; x < r.width; x++)
            {
                var c = px[(r.y + y) * width + r.x + x];
                c.a = 255;
                o[y * r.width + x] = c;
            }
            tex.SetPixels32(o);
            tex.Apply();
            return tex;
        }

        /// <summary>Area-downsamples a bottom-up buffer by a whole factor.</summary>
        public static Texture2D Shrink(Color32[] px, int width, int height, int factor)
        {
            int w = width / factor, h = height / factor;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            var o = new Color32[w * h];
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int r = 0, g = 0, b = 0;
                for (int dy = 0; dy < factor; dy++)
                for (int dx = 0; dx < factor; dx++)
                {
                    var c = px[(y * factor + dy) * width + x * factor + dx];
                    r += c.r; g += c.g; b += c.b;
                }
                int n = factor * factor;
                o[y * w + x] = new Color32((byte)(r / n), (byte)(g / n), (byte)(b / n), 255);
            }
            tex.SetPixels32(o);
            tex.Apply();
            return tex;
        }
    }
}
