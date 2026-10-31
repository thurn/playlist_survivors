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
    /// docs/tech-demo-3d/mac.md).
    /// </summary>
    public sealed class Stills : IDisposable
    {
        public const int Width = 1920, Height = 1080;
        readonly Camera _camera;
        readonly string _dir;
        readonly List<double> _times = new List<double>();
        readonly RenderTexture _target, _resolved;

        public Stills(Camera camera, string dir, string times)
        {
            _camera = camera;
            _dir = dir;
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
            }
        }

        Texture2D Draw()
        {
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
