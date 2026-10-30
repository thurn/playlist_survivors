using System;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Demo2D.Runtime.Playback
{
    /// <summary>
    /// The pixel pipeline (README, The pixel pipeline). The world camera draws into a pixel target
    /// from a whole-texel position; the present camera draws a window of that target over the
    /// screen with the sharp upscale (SharpUpscale.shader), offset by the sub-texel remainder, so
    /// the scroll is smooth while every texel stays on the grid.
    ///
    /// How a wider view keeps the grid (`--zoom`):
    /// - <see cref="TargetZoom"/> (PX5's choice): the target grows with the view, always 32 texels
    ///   per metre, sized once for the song's widest view (40.5 m: 1296 texels plus the margin);
    ///   the upscale shrinks from 2× to about 1.48×, so sprites and tiles stay on one grid.
    /// - <see cref="SoftZoom"/>: the target stays 960×540 (plus the margin) and draws the wider
    ///   view at fewer texels per metre, so the 32 px/m art is resampled before the upscale.
    /// Punch is a zoom of the upscale's window in either mode.
    ///
    /// The target is HDR (PX7), so effects and fireflies brighter than white survive the upscale;
    /// the present camera then runs the night's bloom and colour grade (NightLighting's volume) on
    /// the 1080p image, tonemapping it into the screen or a still.
    /// </summary>
    public sealed class PixelPipeline : IDisposable
    {
        public const string TargetZoom = "target", SoftZoom = "soft";
        public const int PixelsPerMetre = CharacterAtlas.PixelsPerMetre, BaseW = 960, BaseH = 540;
        /// <summary>Texels beyond the view on every side, for the shake and the sub-texel scroll.</summary>
        public const int Margin = 16;
        /// <summary>The layer only the present camera draws (its quad).</summary>
        public const int PresentLayer = 31;
        public static readonly Color NightSky = new Color(0.03f, 0.04f, 0.07f);

        readonly bool _soft;
        readonly float _baseViewM;
        readonly RenderTexture _target;
        readonly Material _upscale;
        readonly GameObject _root;
        readonly Transform _quad;
        public readonly Camera World, Present;

        public int TargetW => _target.width;
        public int TargetH => _target.height;
        /// <summary>Texels per metre of the last frame.</summary>
        public float TexelsPerMetre { get; private set; } = PixelsPerMetre;
        /// <summary>Screen pixels per texel of the last frame.</summary>
        public float Upscale { get; private set; }

        /// <summary>The present camera's bloom and grade (`--no-post` turns them off).</summary>
        public bool PostProcessing
        {
            get => Present.GetComponent<UniversalAdditionalCameraData>().renderPostProcessing;
            set => Present.GetComponent<UniversalAdditionalCameraData>().renderPostProcessing = value;
        }

        /// <param name="widestViewM">The song's widest view (its framing's), which sizes the target.</param>
        public PixelPipeline(Shader upscale, float widestViewM, string zoom, Transform parent)
        {
            if (zoom != TargetZoom && zoom != SoftZoom) throw new ArgumentException("PixelPipeline: --zoom is target or soft, not " + zoom);
            _soft = zoom == SoftZoom;
            _baseViewM = (float)BaseW / PixelsPerMetre;
            int w = _soft ? BaseW : Mathf.CeilToInt(Mathf.Max(widestViewM, _baseViewM) * PixelsPerMetre);
            int h = Mathf.CeilToInt(w * 9f / 16f);
            _target = new RenderTexture(w + 2 * Margin, h + 2 * Margin, 24, RenderTextureFormat.DefaultHDR, RenderTextureReadWrite.Linear)
            {
                name = "Pixel Target", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, antiAliasing = 1,
            };
            _target.Create();
            _root = new GameObject("Pixel Pipeline");
            _root.transform.SetParent(parent, false);

            World = NewCamera("World Camera", 0, ~(1 << PresentLayer), NightSky);
            World.targetTexture = _target;
            World.allowHDR = true;

            // Far from the world, drawing only its quad.
            Present = NewCamera("Present Camera", 1, 1 << PresentLayer, Color.black);
            Present.transform.position = new Vector3(0, -100000, -10);
            Present.orthographicSize = 0.5f;
            Present.allowHDR = true;
            Present.GetComponent<UniversalAdditionalCameraData>().renderPostProcessing = true;
            Present.gameObject.AddComponent<AudioListener>();
            _upscale = new Material(upscale) { name = "Sharp Upscale" };
            _upscale.mainTexture = _target;
            var quad = new GameObject("Present Quad", typeof(MeshFilter), typeof(MeshRenderer)) { layer = PresentLayer };
            quad.transform.SetParent(_root.transform, false);
            quad.transform.position = new Vector3(0, -100000, 0);
            quad.GetComponent<MeshFilter>().sharedMesh = QuadMesh();
            var mr = quad.GetComponent<MeshRenderer>();
            mr.sharedMaterial = _upscale;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _quad = quad.transform;
        }

        Camera NewCamera(string name, int depth, int mask, Color clear)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_root.transform, false);
            var cam = go.AddComponent<Camera>();
            cam.orthographic = true;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = clear;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 100;
            cam.allowMSAA = false;
            cam.depth = depth;
            cam.cullingMask = mask;
            go.AddComponent<UniversalAdditionalCameraData>().renderPostProcessing = false;
            return cam;
        }

        /// <summary>
        /// Places both cameras for a frame showing <paramref name="viewM"/> metres across (after
        /// punch) around <paramref name="centre"/>, on an output of <paramref name="outW"/> ×
        /// <paramref name="outH"/> pixels. <paramref name="framingViewM"/> is the view before
        /// punch, which sets the soft mode's density.
        /// </summary>
        public void Frame(Vector2 centre, float viewM, float framingViewM, int outW, int outH)
        {
            float tpm = _soft ? BaseW / Mathf.Max(framingViewM, 1f) : PixelsPerMetre;
            TexelsPerMetre = tpm;
            var snapped = new Vector2(Mathf.Round(centre.x * tpm), Mathf.Round(centre.y * tpm)) / tpm;
            var rem = (centre - snapped) * tpm; // texels, within ±0.5
            World.orthographicSize = _target.height / 2f / tpm;
            World.transform.position = new Vector3(snapped.x, snapped.y, -10);

            float wTex = Mathf.Min(viewM * tpm, _target.width - 2f), hTex = wTex * outH / outW;
            float W = _target.width, H = _target.height;
            _upscale.SetVector("_SrcRect", new Vector4((W / 2 + rem.x - wTex / 2) / W, (H / 2 + rem.y - hTex / 2) / H, wTex / W, hTex / H));
            _upscale.SetVector("_TexSize", new Vector4(W, H, 1f / W, 1f / H));
            Upscale = outW / wTex;
            _upscale.SetFloat("_Scale", Upscale);
            _quad.localScale = new Vector3((float)outW / outH, 1, 1);
        }

        /// <summary>Draws the frame by hand into <paramref name="output"/> (a batch-mode player draws no frames).</summary>
        public void Render(RenderTexture output)
        {
            World.Render();
            var previous = Present.targetTexture;
            Present.targetTexture = output;
            Present.Render();
            Present.targetTexture = previous;
        }

        static Mesh QuadMesh()
        {
            var m = new Mesh { name = "Present Quad" };
            m.vertices = new[] { new Vector3(-0.5f, -0.5f), new Vector3(0.5f, -0.5f), new Vector3(-0.5f, 0.5f), new Vector3(0.5f, 0.5f) };
            m.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1) };
            m.triangles = new[] { 0, 2, 1, 2, 3, 1 };
            return m;
        }

        public void Dispose()
        {
            if (_target != null) _target.Release();
            SongPlayback.Destroy(_root);
            SongPlayback.Destroy(_upscale);
        }
    }
}
