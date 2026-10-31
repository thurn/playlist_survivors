using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Game.Client
{
    /// <summary>
    /// Windowless stills (`--capture-stills DIR --stills-at t1,t2,...`): at each time it draws
    /// the camera by hand into a 1920×1080 target and keeps it as a JPEG with the `ps-render`
    /// tag. A batch-mode player draws no frames of its own, so a camera is drawn only for the
    /// frames that keep a still (drawing every frame by hand grows URP's pooled targets,
    /// docs/tech-demo-3d/mac.md). With `--capture-mask` it draws the same frame again as its ID
    /// render, still-&lt;time&gt;-mask.png: the crowd alone, flat white on black (MonsterCrystal
    /// under _GameIdMask), with no post, for the look tools' readability measure.
    /// </summary>
    public sealed class Stills : IDisposable
    {
        public const int Width = 1920, Height = 1080;
        readonly Camera _camera;
        readonly string _dir;
        readonly List<double> _times = new List<double>();
        readonly RenderTexture _target, _resolved;
        readonly bool _mask;
        static readonly int IdMask = Shader.PropertyToID("_GameIdMask");

        public Stills(Camera camera, string dir, string times, bool mask = false)
        {
            _camera = camera;
            _dir = dir;
            _mask = mask;
            foreach (var s in times.Split(','))
                _times.Add(double.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture));
            _times.Sort();
            // URP draws an offscreen camera with its target's sample count, so the target carries
            // the pipeline's MSAA and is resolved for readback.
            int msaa = GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset urp ? Math.Max(1, urp.msaaSampleCount) : 1;
            _target = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32) { antiAliasing = msaa, name = "Still" };
            _target.Create();
            if (msaa > 1)
            {
                _resolved = new RenderTexture(Width, Height, 0, RenderTextureFormat.ARGB32) { name = "Still resolved" };
                _resolved.Create();
            }
        }

        public bool Done => _times.Count == 0;

        /// <summary>
        /// A full-screen view kept in place of the camera's while set: the title screen's UI panel,
        /// which a batch-mode player draws into a target of its own.
        /// </summary>
        public static RenderTexture Screen;

        /// <summary>
        /// Raised before the ID render draws the frame a second time: a hand-drawn camera spends the
        /// draws queued in a frame (Graphics.RenderMesh*), so their owners queue them again.
        /// </summary>
        public static event Action Redrawing;

        /// <summary>Keeps every still due at time `t`, named still-&lt;time&gt;.jpg.</summary>
        public void CaptureDue(double t)
        {
            while (_times.Count > 0 && t + 1e-6 >= _times[0])
            {
                var at = _times[0];
                _times.RemoveAt(0);
                var tex = Draw();
                var name = string.Format(CultureInfo.InvariantCulture, "still-{0:000.000}.jpg", at);
                RenderTag.Save(tex, Path.Combine(_dir, name), "game stills t=" + at.ToString("F3", CultureInfo.InvariantCulture));
                UnityEngine.Object.Destroy(tex);
                Debug.Log($"Stills: kept {name} at t={t:F3}");
                if (_mask) KeepMask(at);
            }
        }

        void KeepMask(double at)
        {
            var data = _camera.GetUniversalAdditionalCameraData();
            var (mask, clear, background, post) = (_camera.cullingMask, _camera.clearFlags, _camera.backgroundColor, data.renderPostProcessing);
            _camera.cullingMask = 1 << Crowd.BoneTextureCrowd.Layer;
            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.backgroundColor = Color.black;
            data.renderPostProcessing = false;
            Shader.SetGlobalFloat(IdMask, 1);
            Redrawing?.Invoke();
            Texture2D tex;
            try { tex = Draw(); }
            finally
            {
                Shader.SetGlobalFloat(IdMask, 0);
                (_camera.cullingMask, _camera.clearFlags, _camera.backgroundColor, data.renderPostProcessing) = (mask, clear, background, post);
            }
            var name = string.Format(CultureInfo.InvariantCulture, "still-{0:000.000}-mask.png", at);
            RenderTag.Save(tex, Path.Combine(_dir, name), "game stills mask t=" + at.ToString("F3", CultureInfo.InvariantCulture));
            UnityEngine.Object.Destroy(tex);
        }

        Texture2D Draw()
        {
            if (Screen != null) return Read(Screen);
            var previous = _camera.targetTexture;
            _camera.targetTexture = _target;
            try { _camera.Render(); }
            finally { _camera.targetTexture = previous; }
            var source = _target;
            if (_resolved != null)
            {
                Graphics.Blit(_target, _resolved);
                source = _resolved;
            }
            return Read(source);
        }

        static Texture2D Read(RenderTexture source)
        {
            var req = AsyncGPUReadback.Request(source, 0, TextureFormat.RGBA32);
            req.WaitForCompletion();
            if (req.hasError) throw new Exception("Stills: GPU readback failed");
            var tex = new Texture2D(Width, Height, TextureFormat.RGBA32, false);
            tex.LoadRawTextureData(req.GetData<byte>());
            tex.Apply();
            return tex;
        }

        public void Dispose()
        {
            _target.Release();
            if (_resolved != null) _resolved.Release();
        }
    }
}
