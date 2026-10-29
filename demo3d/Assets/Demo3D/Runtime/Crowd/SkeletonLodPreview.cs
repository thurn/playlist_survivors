using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Demo3D.Runtime.Crowd
{
    /// <summary>Dedicated scene/player, independent of the song and production library.</summary>
    public sealed class SkeletonLodPreview : MonoBehaviour
    {
        public CrowdLibrary Library;
        public int ClipIndex;
        public float ClipTime, Speed = 1, Yaw = 180, ViewWidth = 30, CrossfadeSeconds = 0.2f;
        public float InspectionHeight = 0.85f;
        public const float MinViewWidth = 0.5f, MaxViewWidth = 55f;
        public bool Playing = true, Overhead, Single;
        public int Variant;
        public string CandidateLabel = "B";
        public Camera[] Cameras { get; private set; }
        public Camera PresentationCamera { get; private set; }
        public RenderTexture[] Targets { get; private set; }
        public int[] ClipOrder { get; private set; }
        public float Blend = 1;
        public int PreviousClip;
        public float PreviousTime;
        readonly List<BoneTextureCrowd> _renderers = new List<BoneTextureCrowd>();
        readonly List<UnityEngine.Object> _owned = new List<UnityEngine.Object>();
        float _elapsed, _transition;
        int _cycles, _captureFrame;
        string _reel;
        float _fixedStep;
        bool _initialized;
        Color[] _bonePixels;
        Vector3[][] _positions;
        List<Vector4>[] _indices, _weights;
        bool _stillDue;
        int _renderedFrames;
        Vector2 _controlScroll;

        void Start()
        {
            Initialize();
            ViewWidth = Mathf.Clamp(Args.GetFloat("--view-width", 30), MinViewWidth, MaxViewWidth);
            Yaw = Args.GetFloat("--yaw", 180);
            InspectionHeight = Mathf.Clamp(Args.GetFloat("--focus-height", 0.85f), 0, 2);
            Overhead = Args.Has("--overhead");
            Single = Args.Has("--single");
            Variant = Mathf.Clamp(Args.GetInt("--variant", 0), 0, 2);
            ClipIndex = Mathf.Clamp(Args.GetInt("--clip", ClipOrder[0]), 0, Library.Types[0].Clips.Length - 1);
            ClipTime = Args.GetFloat("--time", 0);
            if (Args.Has("--time")) Playing = false;
            _reel = Args.Get("--reel");
            _fixedStep = 1f / Mathf.Max(1, Args.GetFloat("--reel-fps", 30));
            if (_reel != null) { Directory.CreateDirectory(_reel); Playing = true; }
            if (!Application.isBatchMode) RenderPipelineManager.endFrameRendering += AfterRender;
            Application.targetFrameRate = 60;
            Debug.Log("SkeletonLodPreview Metal: " + SystemInfo.graphicsDeviceType + "; cuts none; clips " +
                      string.Join(", ", ClipOrder.Select(i => Library.Types[0].Clips[i].Name)));
        }

        public void Initialize()
        {
            if (_initialized) return;
            if (Library == null || Library.Types.Length != 3) throw new InvalidOperationException("Preview needs Original/A/B");
            _initialized = true;
            _bonePixels = Library.Types[0].Bones.GetPixels();
            _positions = Library.Types.Select(t => t.Mesh.vertices).ToArray();
            _indices = new List<Vector4>[3]; _weights = new List<Vector4>[3];
            for (int v = 0; v < 3; v++)
            {
                _indices[v] = new List<Vector4>(); _weights[v] = new List<Vector4>();
                Library.Types[v].Mesh.GetUVs(1, _indices[v]); Library.Types[v].Mesh.GetUVs(2, _weights[v]);
            }
            var semantic = new[] { "idle", "walk", "attack", "attack2", "hit", "death" };
            ClipOrder = Enumerable.Range(0, Library.Types[0].Clips.Length)
                .OrderBy(i => { int n = Array.IndexOf(semantic, Library.Types[0].Clips[i].Role); return n < 0 ? 100 : n; })
                .ThenBy(i => Library.Types[0].Clips[i].Name, StringComparer.Ordinal).ToArray();
            ClipIndex = ClipOrder[0]; PreviousClip = ClipIndex;
            // Every model camera targets a texture. A separate, empty screen camera
            // clears the backbuffer each frame before IMGUI paints changing labels.
            // Without it, the old text remains behind the next frame's glyphs.
            var screen = new GameObject("Preview screen"); screen.transform.SetParent(transform);
            PresentationCamera = screen.AddComponent<Camera>();
            PresentationCamera.cullingMask = 0;
            PresentationCamera.clearFlags = CameraClearFlags.SolidColor;
            PresentationCamera.backgroundColor = new Color(0.045f, 0.06f, 0.08f, 1);
            PresentationCamera.depth = -100;
            PresentationCamera.gameObject.AddComponent<UniversalAdditionalCameraData>().renderPostProcessing = false;
            Cameras = new Camera[3]; Targets = new RenderTexture[3];
            var previousCuts = Cuts.Active;
            try
            {
                Cuts.Active = Cuts.Parse("none");
                for (int v = 0; v < 3; v++)
                {
                    // Each copy gets its own compute shader state; its own mesh casts its shadow.
                    var lib = Instantiate(Library); _owned.Add(lib);
                    lib.CullShader = Instantiate(Library.CullShader); _owned.Add(lib.CullShader);
                    lib.PaletteShader = Instantiate(Library.PaletteShader); _owned.Add(lib.PaletteShader);
                    var renderer = new BoneTextureCrowd(lib) { Clock = () => _elapsed };
                    renderer.Initialize(new[] { lib.Types[v].Id }, 1);
                    _renderers.Add(renderer);
                    var go = new GameObject("Preview " + lib.Types[v].Id); go.transform.SetParent(transform);
                    var cam = go.AddComponent<Camera>(); Cameras[v] = cam;
                    cam.fieldOfView = 30; cam.aspect = 16f / 9; cam.nearClipPlane = 0.1f; cam.farClipPlane = 300;
                    cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.06f, 0.075f, 0.1f);
                    cam.allowHDR = true; cam.allowMSAA = true;
                    cam.gameObject.AddComponent<UniversalAdditionalCameraData>().renderPostProcessing = false;
                    var rt = new RenderTexture(1920, 1080, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4, name = "Preview " + v };
                    rt.Create(); Targets[v] = rt; _owned.Add(rt); cam.targetTexture = rt;
                }
            }
            finally { Cuts.Active = previousCuts; }
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Preview ground"; ground.transform.SetParent(transform);
            ground.transform.localScale = Vector3.one * 20;
            ground.transform.position = new Vector3(0, -0.06f, 0);
            var mat = new Material(Shader.Find("Universal Render Pipeline/Lit")); _owned.Add(mat);
            mat.SetColor("_BaseColor", new Color(0.12f, 0.15f, 0.17f)); mat.SetFloat("_Smoothness", 0);
            ground.GetComponent<Renderer>().sharedMaterial = mat;
            var light = new GameObject("Preview sun").AddComponent<Light>(); light.transform.SetParent(transform);
            light.type = LightType.Directional; light.shadows = LightShadows.Soft; light.intensity = 1.5f;
            light.transform.rotation = Quaternion.Euler(38, -35, 0); light.color = new Color(0.9f, 0.94f, 1);
            RenderSettings.sun = light; RenderSettings.skybox = null; RenderSettings.fog = false;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.32f, 0.36f, 0.43f);
            RenderSettings.ambientEquatorColor = new Color(0.18f, 0.20f, 0.24f);
            RenderSettings.ambientGroundColor = new Color(0.08f, 0.08f, 0.09f);
        }

        void Update()
        {
            float dt = _reel == null ? Time.unscaledDeltaTime : _fixedStep;
            if (Playing && (_reel == null || _renderedFrames >= 8))
            {
                _elapsed += dt; _transition += dt; ClipTime += dt * Speed;
                var clip = Library.Types[0].Clips[ClipIndex];
                float duration = clip.Length * (clip.Loop ? 2 : 1) + (clip.Loop ? 0 : 0.6f);
                if (ClipTime >= duration)
                {
                    int pos = Array.IndexOf(ClipOrder, ClipIndex);
                    if (pos == ClipOrder.Length - 1 && _reel != null)
                    {
                        if (++_cycles == 1) { Application.Quit(); return; }
                    }
                    ChangeClip(1);
                }
                Blend = CrossfadeSeconds <= 0 ? 1 : Mathf.Clamp01(_transition / CrossfadeSeconds);
            }
            if (Input.GetKeyDown(KeyCode.Space)) Playing = !Playing;
            if (Input.GetKeyDown(KeyCode.LeftArrow)) ChangeClip(-1);
            if (Input.GetKeyDown(KeyCode.RightArrow)) ChangeClip(1);
            if (Input.GetKeyDown(KeyCode.O)) Overhead = !Overhead;
            if (Input.GetKeyDown(KeyCode.Tab)) Single = !Single;
            if (Input.GetKeyDown(KeyCode.Alpha1)) Variant = 0;
            if (Input.GetKeyDown(KeyCode.Alpha2)) Variant = 1;
            if (Input.GetKeyDown(KeyCode.Alpha3)) Variant = 2;
            if (Input.GetKeyDown(KeyCode.P)) _stillDue = true;
            if (Input.GetKeyDown(KeyCode.Equals) || Input.GetKeyDown(KeyCode.Plus) || Input.GetKeyDown(KeyCode.KeypadPlus)) Zoom(1 / 1.2f);
            if (Input.GetKeyDown(KeyCode.Minus) || Input.GetKeyDown(KeyCode.KeypadMinus)) Zoom(1.2f);
            // Leave wheel input over the controls to their scroll view.
            float uiY = (Screen.height - Input.mousePosition.y) * 1920f / Screen.width;
            if (uiY < (Single ? 650 : 510) && Input.mouseScrollDelta.y != 0)
                Zoom(Mathf.Pow(0.85f, Input.mouseScrollDelta.y));
            DrawFrame(Application.isBatchMode);
            if (Application.isBatchMode) AfterRender(default, null);
        }

        public void ChangeClip(int delta)
        {
            PreviousClip = ClipIndex; PreviousTime = SampleTime(ClipIndex, ClipTime);
            int at = Array.IndexOf(ClipOrder, ClipIndex);
            ClipIndex = ClipOrder[(at + delta + ClipOrder.Length) % ClipOrder.Length];
            ClipTime = 0; _transition = 0; Blend = Playing && CrossfadeSeconds > 0 ? 0 : 1;
        }

        float SampleTime(int clip, float time)
        {
            var c = Library.Types[0].Clips[clip];
            return c.Loop ? Mathf.Repeat(time, c.Length) : Mathf.Clamp(time, 0, c.Length);
        }

        public void Zoom(float factor) => ViewWidth = Mathf.Clamp(ViewWidth * factor, MinViewWidth, MaxViewWidth);

        public void Inspect(float height, float width)
        {
            InspectionHeight = Mathf.Clamp(height, 0, 2);
            ViewWidth = Mathf.Clamp(width, MinViewWidth, MaxViewWidth);
        }

        public void DrawFrame(bool manual = false)
        {
            Initialize();
            float distance = ViewWidth / (2 * Mathf.Tan(15 * Mathf.Deg2Rad) * (16f / 9));
            var rotation = Quaternion.Euler(Overhead ? 90 : 55, 0, 0);
            var focus = new Vector3(0, ViewWidth < 10 ? InspectionHeight : 0, 0);
            for (int v = 0; v < 3; v++)
            {
                var cam = Cameras[v]; cam.transform.SetPositionAndRotation(focus - rotation * Vector3.forward * distance, rotation);
                cam.enabled = !manual;
                var data = _renderers[v].Instances(0, 1);
                data[0] = new MonsterInstance { Position = Vector3.zero, Yaw = Yaw * Mathf.Deg2Rad,
                    ClipA = (uint)ClipIndex, TimeA = SampleTime(ClipIndex, ClipTime),
                    ClipB = (uint)PreviousClip, TimeB = PreviousTime, Blend = Blend,
                    Scale = 1, Tint = 0xffffffff, Flags = (uint)CrowdFlags.Alive };
                _renderers[v].Draw(cam);
                if (manual) cam.Render();
            }
        }

        void AfterRender(ScriptableRenderContext context, Camera[] cameras)
        {
            _renderedFrames++;
            if (_stillDue)
            {
                _stillDue = false;
                CaptureSet(Path.Combine(Application.dataPath, "../captures/preview-" + DateTime.Now.ToString("yyyyMMdd-HHmmss")));
            }
            if (Args.Has("--still-out") && _renderedFrames == 8)
            {
                CaptureSet(Args.Get("--still-out"));
                Debug.Log("Preview fixed-time capture complete");
                Application.Quit();
            }
            if (_reel != null && _renderedFrames > 8)
                CaptureSet(Path.Combine(_reel, (_captureFrame++).ToString("D06")));
        }

        public void CaptureSet(string prefix)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(prefix)));
            for (int v = 0; v < 3; v++)
            {
                var previous = RenderTexture.active; RenderTexture.active = Targets[v];
                var texture = new Texture2D(1920, 1080, TextureFormat.RGB24, false);
                texture.ReadPixels(new Rect(0, 0, 1920, 1080), 0, 0); texture.Apply();
                RenderTexture.active = previous;
                File.WriteAllBytes(prefix + "-" + Library.Types[v].Id + ".png", texture.EncodeToPNG());
                DestroyNow(texture);
            }
        }

        public float ProjectedHeight1080(int v)
        {
            var t = Library.Types[v];
            var cam = Cameras[v];
            var matrices = new Matrix4x4[t.BoneCount];
            for (int e = 0; e < matrices.Length; e++)
            {
                matrices[e] = Matrix4x4.identity;
                for (int row = 0; row < 3; row++)
                {
                    Vector4 a = SampleRow(t, ClipIndex, SampleTime(ClipIndex, ClipTime), e, row);
                    Vector4 b = SampleRow(t, PreviousClip, PreviousTime, e, row);
                    matrices[e].SetRow(row, Vector4.Lerp(b, a, Blend));
                }
            }
            float min = float.MaxValue, max = float.MinValue;
            var yaw = Quaternion.Euler(0, Yaw, 0);
            for (int i = 0; i < _positions[v].Length; i++)
            {
                var p = Vector3.zero;
                for (int k = 0; k < 4; k++)
                    p += _weights[v][i][k] * matrices[(int)_indices[v][i][k]].MultiplyPoint3x4(_positions[v][i]);
                float y = cam.WorldToViewportPoint(yaw * p).y;
                min = Mathf.Min(min, y); max = Mathf.Max(max, y);
            }
            return (max - min) * 1080;
        }

        Vector4 SampleRow(CrowdType type, int ci, float time, int bone, int row)
        {
            var clip = type.Clips[ci];
            int first = Mathf.Min((int)(time * CrowdLibrary.ClipRate), clip.Frames - 2), second = first + 1;
            float t0 = first / CrowdLibrary.ClipRate, t1 = Mathf.Min(second / CrowdLibrary.ClipRate, clip.Length);
            float fraction = Mathf.Clamp01((time - t0) / Mathf.Max(t1 - t0, 1e-5f));
            int width = type.Bones.width, x = bone * 3 + row;
            return Vector4.Lerp(_bonePixels[(clip.FirstRow + first) * width + x],
                _bonePixels[(clip.FirstRow + second) * width + x], fraction);
        }

        void OnGUI()
        {
            if (!_initialized) return;
            float scale = Screen.width / 1920f;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1));
            // Also paint an opaque UI background on every repaint. This keeps the
            // overlay self-contained when a host retains its previous drawable.
            var previousColor = GUI.color;
            GUI.color = new Color(0.045f, 0.06f, 0.08f, 1);
            GUI.DrawTexture(new Rect(0, 0, 1920, Screen.height / scale), Texture2D.whiteTexture);
            GUI.color = previousColor;
            var style = new GUIStyle(GUI.skin.label) { fontSize = 20, normal = { textColor = Color.white } };
            float imageY = 50;
            int first = Single ? Variant : 0, last = Single ? Variant + 1 : 3;
            for (int v = first; v < last; v++)
            {
                float x = Single ? 480 : v * 640, width = Single ? 960 : 640;
                var t = Library.Types[v];
                GUI.DrawTexture(new Rect(x, imageY, width, width * 9 / 16), Targets[v], ScaleMode.ScaleToFit);
                GUI.Label(new Rect(x + 12, 12, width, 36), $"{(v == 2 ? CandidateLabel : t.Id)}   {t.Triangles:N0} tris / {t.Vertices:N0} verts", style);
                GUI.Label(new Rect(x + 12, imageY + width * 9 / 16 + 4, width, 36), $"1080p projected extent: {ProjectedHeight1080(v):F1} px", style);
            }
            float controlsY = Single ? 650 : 510;
            GUILayout.BeginArea(new Rect(30, controlsY, 1860, Mathf.Max(120, Screen.height / scale - controlsY - 20)));
            _controlScroll = GUILayout.BeginScrollView(_controlScroll);
            GUILayout.Label($"{Library.Types[0].Clips[ClipIndex].Role}: {Library.Types[0].Clips[ClipIndex].Name}   {SampleTime(ClipIndex, ClipTime):F3} s   |   {(Overhead ? 90 : 55)}° / 30° FOV   |   {ViewWidth:F1} m game view width", style);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(Playing ? "Pause [Space]" : "Play [Space]", GUILayout.Height(36))) Playing = !Playing;
            if (GUILayout.Button("Previous [←]")) ChangeClip(-1);
            if (GUILayout.Button("Next [→]")) ChangeClip(1);
            if (GUILayout.Button(Overhead ? "55° camera [O]" : "90° overhead [O]")) Overhead = !Overhead;
            if (GUILayout.Button(Single ? "Three views [Tab]" : "Registered single view [Tab]")) Single = !Single;
            for (int v = 0; v < 3; v++) if (GUILayout.Button((v == 2 ? CandidateLabel : Library.Types[v].Id) + $" [{v + 1}]")) Variant = v;
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            foreach (float width in new[] { 30f, 40f, 55f, 5f })
                if (GUILayout.Button(width < 10 ? "Whole body" : $"Game {width} m", GUILayout.Height(32))) Inspect(0.85f, width);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Head close-up", GUILayout.Height(32))) Inspect(1.5f, 0.8f);
            if (GUILayout.Button("Torso close-up")) Inspect(1.1f, 1.2f);
            if (GUILayout.Button("Feet close-up")) Inspect(0.15f, 0.8f);
            if (GUILayout.Button("Zoom in [+]")) Zoom(1 / 1.2f);
            if (GUILayout.Button("Zoom out [-]")) Zoom(1.2f);
            GUILayout.EndHorizontal();
            GUILayout.Label("Scrub (pauses playback)");
            float scrub = GUILayout.HorizontalSlider(SampleTime(ClipIndex, ClipTime), 0, Library.Types[0].Clips[ClipIndex].Length);
            if (Mathf.Abs(scrub - SampleTime(ClipIndex, ClipTime)) > 0.0001f) { ClipTime = scrub; Playing = false; Blend = 1; }
            GUILayout.Label($"Speed {Speed:F2}×"); Speed = GUILayout.HorizontalSlider(Speed, 0.1f, 2);
            GUILayout.Label($"Facing {Yaw:F0}°"); Yaw = GUILayout.HorizontalSlider(Yaw, 0, 360);
            GUILayout.Label($"Zoom / game width {ViewWidth:F2} m (wheel over model, or +/-)");
            float logWidth = GUILayout.HorizontalSlider(Mathf.Log(ViewWidth), Mathf.Log(MinViewWidth), Mathf.Log(MaxViewWidth));
            ViewWidth = Mathf.Exp(logWidth);
            GUILayout.Label($"Inspection focus height {InspectionHeight:F2} m"); InspectionHeight = GUILayout.HorizontalSlider(InspectionHeight, 0, 2);
            GUILayout.Label($"Clip crossfade {CrossfadeSeconds:F2} s"); CrossfadeSeconds = GUILayout.HorizontalSlider(CrossfadeSeconds, 0, 1);
            GUILayout.Label("P: save fixed pose images in the local captures directory. Cuts disabled; each variant casts its own shadow.");
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        void OnDestroy()
        {
            DisposePreview();
        }

        public void DisposePreview()
        {
            RenderPipelineManager.endFrameRendering -= AfterRender;
            if (PresentationCamera != null) PresentationCamera.enabled = false;
            AsyncGPUReadback.WaitAllRequests();
            foreach (var r in _renderers) r.Dispose(); _renderers.Clear();
            if (Cameras != null)
                foreach (var camera in Cameras)
                    if (camera != null) { camera.enabled = false; camera.targetTexture = null; }
            foreach (var o in _owned) DestroyNow(o); _owned.Clear();
        }

        static void DestroyNow(UnityEngine.Object o)
        {
            if (Application.isPlaying) Destroy(o); else DestroyImmediate(o);
        }
    }
}
