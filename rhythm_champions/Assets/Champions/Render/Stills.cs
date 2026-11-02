using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Champions.Render
{
    /// <summary>
    /// Windowless stills: at each time it draws the camera by hand into a 1920×1080 target. A batch-mode player draws
    /// no frames of its own, so the camera is drawn only for the frames that keep a still (drawing every frame by hand
    /// grows URP's pooled targets, docs/archive/tech-demo-3d/mac.md). Two kinds:
    /// - `--capture-stills DIR --stills-at t1,t2,...` (<see cref="CaptureDue"/>): still-&lt;time&gt;.jpg with the
    ///   `ps-render` tag; stills due on one frame share its one drawing.
    /// - a scripted scene's still (<see cref="Keep"/>), what `champions look` measures: still-NAME.png (the frame as
    ///   drawn), id-NAME.png (its type-ID pass, <see cref="IdPass"/>) and view-NAME.json (the camera's
    ///   view-projection, row-major in OpenGL's clip space; the hero's feet; the ID table; impact or not).
    /// A frame as drawn carries the UI panels over it (<see cref="Overlays"/>); the ID pass doesn't.
    /// </summary>
    public sealed class Stills : IDisposable
    {
        public const int Width = 1920, Height = 1080;

        /// <summary>A scene still's view json beyond the camera.</summary>
        public sealed class View
        {
            public Vector3 Hero;
            /// <summary>An impact frame (a special moment's first frame or the next), judged by the impact rules.</summary>
            public bool Impact;
            /// <summary>The ID pass's codes: each one's type, its role (a cast role, or hero, structure, effect) and height.</summary>
            public readonly SortedDictionary<int, (string Type, string Role, float HeightM)> Ids =
                new SortedDictionary<int, (string, string, float)>();
            /// <summary>The scene's hand-queued draws queued again (<see cref="Stills.Requeue"/>), or null.</summary>
            public Action Requeue;
        }

        readonly Camera _camera;
        readonly string _dir;
        readonly List<double> _times = new List<double>();
        readonly RenderTexture _target, _resolved, _ids;
        int _drawnFrame = -1;

        /// <summary>
        /// Queues hand-queued draws again before a frame's second drawing on (a scene still's ID pass): a hand-drawn
        /// camera spends the draws queued in a frame (Effects.Queue, TelegraphPresenter.Queue).
        /// </summary>
        public Action Requeue;

        /// <summary>
        /// UI panels' targets drawn over every frame kept, in order: a batch-mode player has no window, so a panel draws
        /// into a target of its own (premultiplied, as UI Toolkit draws) and registers it here while it shows.
        /// </summary>
        public static readonly List<RenderTexture> Overlays = new List<RenderTexture>();

        public Stills(Camera camera, string dir, IEnumerable<double> times)
        {
            _camera = camera;
            _dir = dir;
            _times.AddRange(times);
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
            // Linear, so a code written as code / 255 is stored as the code itself.
            _ids = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear) { name = "Still IDs" };
            _ids.Create();
        }

        public bool Done => _times.Count == 0;

        /// <summary>Keeps every still due at time `t`.</summary>
        public void CaptureDue(double t)
        {
            Texture2D tex = null;
            while (_times.Count > 0 && t + 1e-6 >= _times[0])
            {
                var at = _times[0];
                _times.RemoveAt(0);
                tex ??= Composite(Draw(_target, _resolved));
                var name = string.Format(CultureInfo.InvariantCulture, "still-{0:000.000}.jpg", at);
                RenderTag.SaveJpeg(tex, Path.Combine(_dir, name), "champions stills t=" + at.ToString("F3", CultureInfo.InvariantCulture));
                Debug.Log($"Stills: kept {name} at t={t:F3}");
            }
            if (tex != null) UnityEngine.Object.Destroy(tex);
        }

        /// <summary>Keeps a scene's still `name`: the frame, its type-ID pass and its view.</summary>
        public void Keep(string name, View view)
        {
            Directory.CreateDirectory(_dir);
            _camera.aspect = (float)Width / Height;
            try
            {
                Matrix4x4 viewProj = _camera.projectionMatrix * _camera.worldToCameraMatrix;
                Save(Composite(Draw(_target, _resolved)), "still-" + name + ".png");
                Save(DrawIds(), "id-" + name + ".png");
                File.WriteAllText(Path.Combine(_dir, "view-" + name + ".json"), ViewJson(viewProj, view));
            }
            finally { _camera.ResetAspect(); }
            Debug.Log($"Stills: kept {name}");
        }

        void Save(Texture2D tex, string file)
        {
            File.WriteAllBytes(Path.Combine(_dir, file), tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex); // a texture, so in play mode too, and in an EditMode test
        }

        /// <summary>The ID pass: the camera drawn with the global flag up, cleared to 0, without post, MSAA or HDR.</summary>
        Texture2D DrawIds()
        {
            var data = _camera.GetUniversalAdditionalCameraData();
            bool post = data.renderPostProcessing, hdr = _camera.allowHDR, msaa = _camera.allowMSAA;
            var aa = data.antialiasing;
            var (clear, background) = (_camera.clearFlags, _camera.backgroundColor);
            data.renderPostProcessing = false;
            data.antialiasing = AntialiasingMode.None;
            _camera.allowHDR = _camera.allowMSAA = false;
            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.backgroundColor = Color.clear;
            Shader.SetGlobalFloat(IdPass.On, 1f);
            try { return Draw(_ids, null); }
            finally
            {
                Shader.SetGlobalFloat(IdPass.On, 0f);
                data.renderPostProcessing = post;
                data.antialiasing = aa;
                _camera.allowHDR = hdr;
                _camera.allowMSAA = msaa;
                (_camera.clearFlags, _camera.backgroundColor) = (clear, background);
            }
        }

        /// <summary>Draws the <see cref="Overlays"/> over <paramref name="tex"/> by their alpha; the texture, for chaining.</summary>
        static Texture2D Composite(Texture2D tex)
        {
            if (Overlays.Count == 0) return tex;
            var under = tex.GetPixels32();
            foreach (var overlay in Overlays)
            {
                var top = Read(overlay);
                var over = top.GetPixels32();
                UnityEngine.Object.DestroyImmediate(top);
                for (int i = 0; i < under.Length; i++)
                {
                    int keep = 255 - over[i].a;
                    var u = under[i];
                    under[i] = new Color32((byte)Math.Min(255, over[i].r + u.r * keep / 255), (byte)Math.Min(255, over[i].g + u.g * keep / 255),
                        (byte)Math.Min(255, over[i].b + u.b * keep / 255), 255);
                }
            }
            tex.SetPixels32(under);
            tex.Apply();
            return tex;
        }

        Texture2D Draw(RenderTexture target, RenderTexture resolved)
        {
            if (_drawnFrame == Time.frameCount) Requeue?.Invoke();
            _drawnFrame = Time.frameCount;
            var previous = _camera.targetTexture;
            _camera.targetTexture = target;
            try { _camera.Render(); }
            finally { _camera.targetTexture = previous; }
            var source = target;
            if (resolved != null)
            {
                Graphics.Blit(target, resolved);
                source = resolved;
            }
            return Read(source);
        }

        static Texture2D Read(RenderTexture source)
        {
            var req = AsyncGPUReadback.Request(source, 0, TextureFormat.RGBA32);
            req.WaitForCompletion();
            if (req.hasError) throw new Exception("Stills: GPU readback failed");
            var tex = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false);
            tex.LoadRawTextureData(req.GetData<byte>());
            tex.Apply();
            return tex;
        }

        static string ViewJson(Matrix4x4 viewProj, View view)
        {
            var c = CultureInfo.InvariantCulture;
            var s = new StringBuilder("{\n \"viewProj\": [");
            for (int i = 0; i < 16; i++) s.Append(i > 0 ? ", " : "").Append(viewProj[i / 4, i % 4].ToString("R", c));
            s.Append("],\n \"hero\": [").Append(view.Hero.x.ToString("R", c)).Append(", ").Append(view.Hero.y.ToString("R", c))
                .Append(", ").Append(view.Hero.z.ToString("R", c)).Append("],\n \"impact\": ").Append(view.Impact ? "true" : "false")
                .Append(",\n \"ids\": {");
            bool first = true;
            foreach (var kv in view.Ids)
            {
                s.Append(first ? "\n" : ",\n").Append("  \"").Append(kv.Key.ToString(c)).Append("\": {\"type\": \"").Append(kv.Value.Type)
                    .Append("\", \"role\": \"").Append(kv.Value.Role).Append("\", \"heightM\": ").Append(kv.Value.HeightM.ToString("R", c)).Append('}');
                first = false;
            }
            return s.Append("\n }\n}\n").ToString();
        }

        public void Dispose()
        {
            _target.Release();
            if (_resolved != null) _resolved.Release();
            _ids.Release();
        }
    }
}
