using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;

namespace Demo3D.Runtime.Playback
{
    /// <summary>
    /// The capture tool (benchmarks.md, Fidelity A/B Checks): in fixed-time mode it renders the
    /// game camera into a 1920×1080 target each frame and keeps
    /// - stills at chosen song times, as JPEGs with the `ps-render` tag, and
    /// - video, streamed as raw frames straight into ffmpeg's encoder (never as image files),
    ///   with `ps-render` in the file's comment metadata. Videos go to the gitignored captures/.
    /// An A/B pair is two runs with the same times and one flag changed. Under `--supersample`
    /// (a trailer capture's 2× by default) URP draws the frame at the render scale, 3840×2160 at
    /// 2×, and its final blit downsamples it into the target, a 2×2 box at 2× (Supersampling), so
    /// stills and video are kept and encoded at 1920×1080 either way.
    /// </summary>
    public sealed class CaptureTool : IDisposable
    {
        public const int Width = 1920, Height = 1080;
        readonly RenderTexture _target, _resolved;
        readonly Camera _camera;
        readonly List<double> _stillTimes = new List<double>();
        readonly string _stillDir;
        PipeProcess _ffmpeg;
        bool _pipe;
        byte[] _frame;
        public int FramesWritten { get; private set; }
        public int StillsWritten { get; private set; }
        /// <summary>The song time of the last still kept from the still times (not forced ones).</summary>
        public double LastStillAt { get; private set; } = double.NaN;
        string _forced;   // a still forced for the next capture (TD15's ID masks): its file prefix, always PNG
        double _forcedAt;

        public CaptureTool(Camera camera, string stillDir, string stillTimes, string videoPath)
        {
            _camera = camera;
            // URP renders an offscreen camera with its target's sample count, not the pipeline
            // asset's, so the target carries the asset's MSAA (1 under TAA) and is resolved for
            // readback; with antiAliasing 1, MSAA captures came out aliased.
            int msaa = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline is UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset urp
                       && camera.allowMSAA ? Math.Max(1, urp.msaaSampleCount) : 1;
            _target = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32) { antiAliasing = msaa, name = "Capture" };
            _target.Create();
            if (msaa > 1)
            {
                _resolved = new RenderTexture(Width, Height, 0, RenderTextureFormat.ARGB32) { antiAliasing = 1, name = "Capture resolved" };
                _resolved.Create();
            }
            _camera.targetTexture = _target;
            float scale = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline is UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset asset ? asset.renderScale : 1f;
            Debug.Log(string.Format(CultureInfo.InvariantCulture, "CaptureTool: drawing {0}×{1}, kept at {2}×{3}",
                Mathf.RoundToInt(Width * scale), Mathf.RoundToInt(Height * scale), Width, Height));
            _stillDir = stillDir;
            if (!string.IsNullOrEmpty(stillTimes))
                foreach (var s in stillTimes.Split(','))
                    if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var t)) _stillTimes.Add(t);
            _stillTimes.Sort();
            if (!string.IsNullOrEmpty(videoPath)) StartVideo(videoPath);
        }

        void StartVideo(string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            var args = $"-hide_banner -loglevel error -y -f rawvideo -pix_fmt rgba -s {Width}x{Height} -r 60 -i - " +
                       "-vf vflip -c:v libx264 -preset medium -crf 16 -pix_fmt yuv420p " +
                       $"-metadata comment=\"ps-render capture tool\" \"{path}\"";
            _ffmpeg = PipeProcess.Start(FindFfmpeg(), args);
            _pipe = true;
            _frame = new byte[Width * Height * 4];
        }

        /// <summary>ffmpeg's full path: `--ffmpeg`, else the PATH (IL2CPP's Process.Start doesn't search it).</summary>
        static string FindFfmpeg()
        {
            var explicitPath = Args.Get("--ffmpeg");
            if (!string.IsNullOrEmpty(explicitPath)) return explicitPath;
            foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
            {
                if (dir.Length == 0) continue;
                foreach (var name in new[] { "ffmpeg.exe", "ffmpeg" })
                {
                    var p = Path.Combine(dir.Trim('"'), name);
                    if (File.Exists(p)) return p;
                }
            }
            throw new FileNotFoundException("CaptureTool: ffmpeg is not on the PATH; pass --ffmpeg <path>");
        }

        /// <summary>
        /// After URP has drawn the camera into the capture target for song time `t`: keeps what
        /// is due. Video runs are windowed players. In a batch-mode player no camera is drawn
        /// by the player loop, and drawing one by hand every frame (Camera.Render or a render
        /// request) grew memory by about 90 MB a frame in URP 17.6, since its pooled targets are
        /// only freed at the end of a frame the player loop renders (TD4); a batch-mode player
        /// draws by hand only the frames that keep a still (DemoMain).
        /// </summary>
        public void Capture(double t)
        {
            bool forced = _forced != null;
            bool still = StillDue(t);
            if (!_pipe && !still) return;
            if (_resolved != null) Graphics.Blit(_target, _resolved);
            var req = AsyncGPUReadback.Request(_resolved != null ? _resolved : _target, 0, TextureFormat.RGBA32);
            req.WaitForCompletion();
            if (req.hasError) throw new Exception("CaptureTool: GPU readback failed");
            var data = req.GetData<byte>();
            if (_pipe)
            {
                data.CopyTo(_frame);
                _ffmpeg.Write(_frame, 0, _frame.Length);
                FramesWritten++;
                // Progress, which `demo3d trailer` reads from the log (a capture takes many minutes).
                if (FramesWritten % 60 == 0) Debug.Log($"CaptureTool: {FramesWritten} frames");
            }
            if (still)
            {
                double at = forced ? _forcedAt : _stillTimes[0];
                if (!forced) { _stillTimes.RemoveAt(0); LastStillAt = at; }
                var tex = new Texture2D(Width, Height, TextureFormat.RGBA32, false);
                tex.LoadRawTextureData(data);
                tex.Apply();
                // PNG for A/B scoring (no JPEG noise in the FLIP scores), JPEG for evidence.
                var ext = forced || Args.Get("--stills-format", "jpg") == "png" ? "png" : "jpg";
                var name = string.Format(CultureInfo.InvariantCulture, "{0}-{1:000.000}.{2}", forced ? _forced : "still", at, ext);
                RenderTag.Save(tex, Path.Combine(_stillDir ?? "captures", name), "capture tool t=" + at.ToString("F3", CultureInfo.InvariantCulture));
                if (!forced) SaveCrop(tex, at);
                _forced = null;
                UnityEngine.Object.Destroy(tex);
                StillsWritten++;
            }
        }

        /// <summary>
        /// `--capture-crop x,y,w,h[,scale]` (pixels from the top left): also keeps that region of
        /// each still, enlarged by whole pixels (default 3×), for edge-by-edge comparisons such as
        /// TD9's anti-aliasing pairs.
        /// </summary>
        void SaveCrop(Texture2D tex, double at)
        {
            var spec = Args.Get("--capture-crop");
            if (string.IsNullOrEmpty(spec)) return;
            var v = Array.ConvertAll(spec.Split(','), s => int.Parse(s, CultureInfo.InvariantCulture));
            int x = v[0], y = v[1], w = v[2], h = v[3], k = v.Length > 4 ? Math.Max(1, v[4]) : 3;
            x = Mathf.Clamp(x, 0, Width - 1); y = Mathf.Clamp(y, 0, Height - 1);
            w = Math.Min(w, Width - x); h = Math.Min(h, Height - y);
            var src = tex.GetPixels32();
            var dst = new Color32[w * k * h * k];
            for (int j = 0; j < h * k; j++)
            for (int i = 0; i < w * k; i++)
            {
                // The readback is bottom-up; the crop's y counts from the top.
                int sx = x + i / k, sy = Height - 1 - (y + (h * k - 1 - j) / k);
                dst[j * w * k + i] = src[sy * Width + sx];
            }
            var crop = new Texture2D(w * k, h * k, TextureFormat.RGBA32, false);
            crop.SetPixels32(dst);
            crop.Apply();
            var name = string.Format(CultureInfo.InvariantCulture, "still-{0:000.000}-crop.jpg", at);
            RenderTag.Save(crop, Path.Combine(_stillDir ?? "captures", name), "capture tool crop t=" + at.ToString("F3", CultureInfo.InvariantCulture), 95);
            UnityEngine.Object.Destroy(crop);
        }

        /// <summary>Keeps the next capture as `<prefix>-<at>.png` whatever the still times say (TD15's ID mask of the still just taken).</summary>
        public void ForceStill(double at, string prefix)
        {
            _forced = prefix;
            _forcedAt = at;
        }

        /// <summary>Whether a capture at song time `t` keeps a still.</summary>
        public bool StillDue(double t) => _forced != null || (_stillTimes.Count > 0 && t + 1e-6 >= _stillTimes[0]);

        public bool Done => !_pipe && _stillTimes.Count == 0 && _forced == null;

        /// <summary>Ends the video, waiting for ffmpeg to finish encoding; false if it failed.</summary>
        public bool Finish()
        {
            if (!_pipe) return true;
            int code = _ffmpeg.CloseAndWait(300000);
            _ffmpeg.Dispose();
            _pipe = false;
            if (code != 0) UnityEngine.Debug.LogError("CaptureTool: ffmpeg exited with " + code);
            else UnityEngine.Debug.Log($"CaptureTool: video done, {FramesWritten} frames");
            return code == 0;
        }

        public void Dispose()
        {
            Finish();
            _camera.targetTexture = null;
            _target.Release();
            if (_resolved != null) _resolved.Release();
        }
    }
}
