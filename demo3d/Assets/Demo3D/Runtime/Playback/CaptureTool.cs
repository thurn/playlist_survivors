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
    /// An A/B pair is two runs with the same times and one flag changed.
    /// </summary>
    public sealed class CaptureTool : IDisposable
    {
        public const int Width = 1920, Height = 1080;
        readonly RenderTexture _target;
        readonly Camera _camera;
        readonly List<double> _stillTimes = new List<double>();
        readonly string _stillDir;
        PipeProcess _ffmpeg;
        bool _pipe;
        byte[] _frame;
        public int FramesWritten { get; private set; }
        public int StillsWritten { get; private set; }

        public CaptureTool(Camera camera, string stillDir, string stillTimes, string videoPath)
        {
            _camera = camera;
            _target = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32) { antiAliasing = 1, name = "Capture" };
            _target.Create();
            _camera.targetTexture = _target;
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
        /// is due. Capture runs are windowed players. In a batch-mode player no camera is drawn
        /// by the player loop, and drawing one by hand every frame (Camera.Render or a render
        /// request) grew memory by about 90 MB a frame in URP 17.6, since its pooled targets are
        /// only freed at the end of a frame the player loop renders (TD4).
        /// </summary>
        public void Capture(double t)
        {
            bool still = _stillTimes.Count > 0 && t + 1e-6 >= _stillTimes[0];
            if (!_pipe && !still) return;
            var req = AsyncGPUReadback.Request(_target, 0, TextureFormat.RGBA32);
            req.WaitForCompletion();
            if (req.hasError) throw new Exception("CaptureTool: GPU readback failed");
            var data = req.GetData<byte>();
            if (_pipe)
            {
                data.CopyTo(_frame);
                _ffmpeg.Write(_frame, 0, _frame.Length);
                FramesWritten++;
            }
            if (still)
            {
                double at = _stillTimes[0];
                _stillTimes.RemoveAt(0);
                var tex = new Texture2D(Width, Height, TextureFormat.RGBA32, false);
                tex.LoadRawTextureData(data);
                tex.Apply();
                var name = string.Format(CultureInfo.InvariantCulture, "still-{0:000.000}.jpg", at);
                RenderTag.Save(tex, Path.Combine(_stillDir ?? "captures", name), "capture tool t=" + at.ToString("F3", CultureInfo.InvariantCulture));
                UnityEngine.Object.Destroy(tex);
                StillsWritten++;
            }
        }

        public bool Done => !_pipe && _stillTimes.Count == 0;

        public void Dispose()
        {
            if (_pipe)
            {
                int code = _ffmpeg.CloseAndWait(300000);
                _ffmpeg.Dispose();
                _pipe = false;
                if (code != 0) UnityEngine.Debug.LogError("CaptureTool: ffmpeg exited with " + code);
            }
            _camera.targetTexture = null;
            _target.Release();
        }
    }
}
