using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;

namespace Demo2D.Runtime
{
    /// <summary>
    /// Windowless stills: `--capture-stills &lt;dir&gt; --stills-at s,s,...` draws the main camera into a
    /// 1920×1080 target at each time and keeps a JPEG with the `ps-render` tag. A batch-mode
    /// player's loop draws no camera, so this draws it by hand, only for the frames that keep a
    /// still. Time runs fixed at 60 frames a second while it captures. `--seconds N` quits after N
    /// seconds, with or without stills.
    /// </summary>
    public sealed class StillCapture : MonoBehaviour
    {
        public const int Width = 1920, Height = 1080;
        readonly List<double> _times = new List<double>();
        string _dir;
        float _quitAt;
        RenderTexture _target;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            // The timing spike (`--frame-timing`) keeps its own clock and quits itself.
            if (!Args.Has("--capture-stills") && !(Args.Has("--seconds") && !Args.Has("--frame-timing"))) return;
            var go = new GameObject("StillCapture");
            DontDestroyOnLoad(go);
            go.AddComponent<StillCapture>();
        }

        void Awake()
        {
            _dir = Args.Get("--capture-stills");
            _quitAt = Args.GetFloat("--seconds", float.PositiveInfinity);
            foreach (var s in (Args.Get("--stills-at") ?? "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
                _times.Add(double.Parse(s, CultureInfo.InvariantCulture));
            _times.Sort();
            if (_dir != null)
            {
                Time.captureFramerate = 60;
                _target = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32) { name = "Still" };
            }
        }

        void LateUpdate()
        {
            double t = Time.time;
            while (_dir != null && _times.Count > 0 && t >= _times[0])
            {
                Save(_times[0]);
                _times.RemoveAt(0);
            }
            if (t >= _quitAt || (_dir != null && _times.Count == 0 && float.IsInfinity(_quitAt)))
            {
                Debug.Log($"StillCapture: quitting at t={t:F3} s");
                Application.Quit(0);
                enabled = false;
            }
        }

        void Save(double at)
        {
            var camera = Camera.main ?? throw new InvalidOperationException("StillCapture: no main camera");
            var previous = camera.targetTexture;
            camera.targetTexture = _target;
            camera.Render();
            camera.targetTexture = previous;
            var req = AsyncGPUReadback.Request(_target, 0, TextureFormat.RGBA32);
            req.WaitForCompletion();
            if (req.hasError) throw new Exception("StillCapture: GPU readback failed");
            var tex = new Texture2D(Width, Height, TextureFormat.RGBA32, false);
            tex.LoadRawTextureData(req.GetData<byte>());
            tex.Apply();
            var path = Path.Combine(_dir, string.Format(CultureInfo.InvariantCulture, "still-{0:000.000}.jpg", at));
            RenderTag.Save(tex, path, "demo2d still t=" + at.ToString("F3", CultureInfo.InvariantCulture));
            Destroy(tex);
            Debug.Log($"StillCapture: wrote {path} ({SystemInfo.graphicsDeviceType})");
        }

        void OnDestroy()
        {
            if (_target != null) _target.Release();
        }
    }
}
