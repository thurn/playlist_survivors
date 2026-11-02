using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using PS.Look.Crowd;

namespace Game.Client
{
    /// <summary>
    /// Windowless stills (`--capture-stills DIR --stills-at t1,t2,...`): at each time it draws
    /// the camera by hand into a 1920×1080 target and keeps it as a JPEG with the `ps-render`
    /// tag. A batch-mode player draws no frames of its own, so a camera is drawn only for the
    /// frames that keep a still (drawing every frame by hand grows URP's pooled targets,
    /// docs/archive/tech-demo-3d/mac.md). With `--capture-mask` it draws the same frame again as its ID
    /// render, still-&lt;time&gt;-mask.png: the crowd alone, flat white on black (every monster look
    /// under _GameIdMask), with no post, for the look tools' readability measure, and view-&lt;time&gt;.json:
    /// the camera's view-projection and the hero's feet (<see cref="HeroFeet"/>), as demo3d writes it, for
    /// the look metrics' hero measure (the hero's margin over the monsters, neon-look-style.md).
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
            StillBodies.Collect = true;
            foreach (var s in times.Split(','))
                _times.Add(double.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture));
            _times.Sort();
            // The target carries the pipeline's MSAA and is resolved for readback.
            _target = Target("Still");
            if (_target.antiAliasing > 1)
            {
                _resolved = new RenderTexture(Width, Height, 0, RenderTextureFormat.ARGB32) { name = "Still resolved" };
                _resolved.Create();
            }
        }

        public bool Done => _times.Count == 0;

        /// <summary>The hero's feet as last drawn (HeroPresenter sets it), for the view json; null without a hero.</summary>
        public static Vector3? HeroFeet;

        /// <summary>
        /// A 1920×1080 target for a hand-drawn frame: URP draws an offscreen camera with its target's
        /// sample count, so it carries the pipeline's MSAA (the game's 4x).
        /// </summary>
        public static RenderTexture Target(string name)
        {
            int msaa = GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset urp ? Math.Max(1, urp.msaaSampleCount) : 1;
            var target = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32) { antiAliasing = msaa, name = name };
            target.Create();
            return target;
        }

        /// <summary>
        /// A full-screen view kept in place of the camera's while set: the title screen's UI panel,
        /// which a batch-mode player draws into a target of its own.
        /// </summary>
        public static RenderTexture Screen;

        /// <summary>UI panels' targets drawn over the camera's view, in order: the HUD, then the pause menu over the frozen run.</summary>
        public static readonly List<RenderTexture> Overlays = new List<RenderTexture>();

        /// <summary>
        /// Raised before the ID render draws the frame a second time: a hand-drawn camera spends the
        /// draws queued in a frame (Graphics.RenderMesh*), so their owners queue them again.
        /// </summary>
        public static event Action Redrawing;

        /// <summary>
        /// Keeps every still due at time `t`, named still-&lt;time&gt;.jpg. Stills due on one frame share
        /// its one drawing: drawn again, the frame would miss the draws the first spent (ps-msz.8.11:
        /// the second of two stills on one frame lost the crowd, the structures and the effects).
        /// </summary>
        public void CaptureDue(double t)
        {
            Texture2D tex = null, mask = null;
            while (_times.Count > 0 && t + 1e-6 >= _times[0])
            {
                var at = _times[0];
                _times.RemoveAt(0);
                tex ??= Draw();
                var name = string.Format(CultureInfo.InvariantCulture, "still-{0:000.000}.jpg", at);
                RenderTag.Save(tex, Path.Combine(_dir, name), "game stills t=" + at.ToString("F3", CultureInfo.InvariantCulture));
                Debug.Log($"Stills: kept {name} at t={t:F3}");
                if (!_mask) continue;
                mask ??= DrawMask();
                name = string.Format(CultureInfo.InvariantCulture, "still-{0:000.000}-mask.png", at);
                RenderTag.Save(mask, Path.Combine(_dir, name), "game stills mask t=" + at.ToString("F3", CultureInfo.InvariantCulture));
                WriteView(at);
            }
            if (tex != null) UnityEngine.Object.Destroy(tex);
            if (mask != null) UnityEngine.Object.Destroy(mask);
        }

        /// <summary>
        /// view-&lt;at&gt;.json beside the still: the camera's view-projection at the still's size (OpenGL clip space,
        /// row-major), the hero's feet and the monsters' median height on screen (<see cref="StillBodies"/>), the format
        /// demo3d's DemoMain writes and look.py reads. None without a hero.
        /// </summary>
        void WriteView(double at)
        {
            if (HeroFeet is not Vector3 feet) return;
            float aspect = Width / (float)Height;
            var proj = _camera.orthographic
                ? Matrix4x4.Ortho(-_camera.orthographicSize * aspect, _camera.orthographicSize * aspect, -_camera.orthographicSize,
                    _camera.orthographicSize, _camera.nearClipPlane, _camera.farClipPlane)
                : Matrix4x4.Perspective(_camera.fieldOfView, aspect, _camera.nearClipPlane, _camera.farClipPlane);
            var m = proj * _camera.worldToCameraMatrix;
            var sb = new System.Text.StringBuilder("{\"viewProj\": [");
            for (int r = 0; r < 4; r++)
            for (int c = 0; c < 4; c++)
                sb.Append(string.Format(CultureInfo.InvariantCulture, "{0}{1:R}", r + c == 0 ? "" : ", ", m[r, c]));
            // "floor": false: the ID render marks only the crowd, so look.py leaves out the floor measures, which the
            // structures and effects would count in.
            sb.Append(string.Format(CultureInfo.InvariantCulture, "], \"hero\": [{0:R}, {1:R}, {2:R}], \"floor\": false", feet.x, feet.y, feet.z));
            // "bodyM" and "bodies": the monsters in view's height, which look.py scales its swarm and form tiles by.
            sb.Append(StillBodies.ViewJson(m)).Append("}\n");
            File.WriteAllText(Path.Combine(_dir, string.Format(CultureInfo.InvariantCulture, "view-{0:000.000}.json", at)), sb.ToString());
        }

        Texture2D DrawMask()
        {
            var data = _camera.GetUniversalAdditionalCameraData();
            var (mask, clear, background, post) = (_camera.cullingMask, _camera.clearFlags, _camera.backgroundColor, data.renderPostProcessing);
            _camera.cullingMask = 1 << BoneTextureCrowd.Layer;
            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.backgroundColor = Color.black;
            data.renderPostProcessing = false;
            Shader.SetGlobalFloat(IdMask, 1);
            Redrawing?.Invoke();
            try { return Draw(); }
            finally
            {
                Shader.SetGlobalFloat(IdMask, 0);
                (_camera.cullingMask, _camera.clearFlags, _camera.backgroundColor, data.renderPostProcessing) = (mask, clear, background, post);
            }
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
            var tex = Read(source);
            foreach (var overlay in Overlays) Composite(tex, overlay);
            return tex;
        }

        /// <summary>Draws `overlay` over `tex` by its alpha.</summary>
        static void Composite(Texture2D tex, RenderTexture overlay)
        {
            var top = Read(overlay);
            var over = top.GetPixels32();
            var under = tex.GetPixels32();
            for (int i = 0; i < under.Length; i++) under[i] = Color32.Lerp(under[i], over[i], over[i].a / 255f);
            UnityEngine.Object.Destroy(top);
            tex.SetPixels32(under);
            tex.Apply();
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
