using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Demo3D.Runtime.Playback;
using UnityEngine;
using UnityEngine.Rendering;

namespace Demo3D.Runtime.Effects
{
    /// <summary>
    /// The effects' A/B stills (benchmarks.md, Fidelity A/B Checks; TD8's Done when): in fixed time,
    /// each converted effect plays on our GPU runtime on the right while its stock prefab plays on
    /// the left, simulated to the same time (Shuriken's own Simulate with a fixed seed), under the
    /// game camera's pitch. Four times per effect go into one tagged JPEG, 2×2 cells.
    /// `--effect-gallery DIR [--effects a,b] [--gallery-night]`; a windowed player (a batch-mode
    /// player draws no frames).
    /// </summary>
    public sealed class EffectGallery : MonoBehaviour
    {
        const int Cell = 640, CellH = 360;
        static readonly float[] Fractions = { 0.05f, 0.14f, 0.3f, 0.6f };

        EffectLibrary _lib;
        EffectSystem _fx;
        ISpawnSink _alt; // `--gallery-runtime vfx`: the spike's VFX Graph arm on the left instead of a stock prefab
        GameCameraRig _rig;
        RenderTexture _rt;
        string _dir;
        readonly List<int> _queue = new List<int>();
        int _current = -1, _frame, _shot;
        float[] _times;
        GameObject _stock;
        ParticleSystem[] _stockSystems;
        Texture2D _montage;
        bool _captureDue;
        Vector3 _left, _right;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoStart()
        {
            if (Application.isEditor || !Args.Has("--effect-gallery")) return;
            new GameObject("EffectGallery").AddComponent<EffectGallery>();
        }

        void Start()
        {
            if (Application.isBatchMode) Debug.LogError("EffectGallery: needs a windowed player; a batch-mode player draws no frames");
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = -1;
            _dir = Args.Get("--effect-gallery", "captures/effects");
            var sun = BenchEnvironment.Build();
            if (Args.Has("--gallery-night"))
            {
                sun.intensity = 0.15f;
                RenderSettings.ambientSkyColor *= 0.3f;
                RenderSettings.ambientEquatorColor *= 0.3f;
                RenderSettings.ambientGroundColor *= 0.3f;
            }
            _lib = EffectLibrary.Load();
            _fx = new EffectSystem(_lib);
            _rig = new GameCameraRig();
            _rig.Camera.backgroundColor = new Color(0.05f, 0.06f, 0.08f);
            _fx.AddCamera(_rig.Camera);
            var rt = Args.Get("--gallery-runtime");
            if (!string.IsNullOrEmpty(rt)) _alt = SpawnSinks.Create(rt, _lib, _rig.Camera);
            _rt = new RenderTexture(CaptureTool.Width, CaptureTool.Height, 24, RenderTextureFormat.ARGB32) { name = "Gallery" };
            _rig.Camera.targetTexture = _rt;
            DemoMain.ApplyAntiAliasing("msaa4", _rig.Camera);
            var only = Args.Get("--effects");
            for (int i = 0; i < _lib.Effects.Length; i++)
                if (string.IsNullOrEmpty(only) ? !_lib.Effects[i].Name.StartsWith("spike") : Array.IndexOf(only.Split(','), _lib.Effects[i].Name) >= 0) _queue.Add(i);
            Directory.CreateDirectory(_dir);
            RenderPipelineManager.endCameraRendering += AfterCamera;
            Next();
        }

        void Next()
        {
            if (_stock != null) Destroy(_stock);
            _queue.RemoveAll(i => i == _current);
            if (_queue.Count == 0)
            {
                Debug.Log("EffectGallery: done");
                Application.Quit(0);
                enabled = false;
                return;
            }
            _current = _queue[0];
            var fx = _lib.Effects[_current];
            float w = Mathf.Max(3f, fx.GalleryViewWidth);
            _left = new Vector3(-w * 0.25f, fx.GalleryHeight, 0);
            _right = new Vector3(w * 0.25f, fx.GalleryHeight, 0);
            _rig.Frame(new Vector2(0, 0), w);
            var prefab = _current < _lib.StockPrefabs.Length ? _lib.StockPrefabs[_current] : null;
            _stock = prefab != null ? Instantiate(prefab, _left, prefab.transform.rotation) : new GameObject("missing stock");
            _stock.transform.localScale = prefab != null ? prefab.transform.localScale : Vector3.one;
            _stockSystems = _stock.GetComponentsInChildren<ParticleSystem>(true);
            foreach (var ps in _stockSystems)
            {
                ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                ps.useAutoRandomSeed = false;
                ps.randomSeed = 1234;
                // One loop, as a spawn plays (Hovl's scripts destroy looping hits after a while).
                var main = ps.main;
                if (main.loop && fx.Duration <= main.duration + 1e-3f) main.loop = false;
            }
            // Children whose vendor shader doesn't exist here (Hovl's distortion) draw magenta in the
            // stock prefab too; they're hidden, as the converter skips them.
            foreach (var rend in _stock.GetComponentsInChildren<Renderer>(true))
                foreach (var m in rend.sharedMaterials)
                    if (m != null && (m.shader == null || m.shader.name == "Hidden/InternalErrorShader")) rend.enabled = false;
            // Hovl's scripts (lights' fades, lasers' raycasts) aren't synced; the stock prefab shows its static state.
            foreach (var mb in _stock.GetComponentsInChildren<MonoBehaviour>(true)) if (mb != null) mb.enabled = false;
            _fx.Reset(0);
            if (_alt != null)
            {
                _alt.Reset(0);
                _alt.Add(new EffectSystem.Spawn { Effect = _current, Position = _left, Scale = fx.ScaleRef, Seed = 1234 });
            }
            float life = Mathf.Clamp(fx.Lifetime, 0.6f, 4f);
            _times = new float[Fractions.Length];
            for (int i = 0; i < _times.Length; i++) _times[i] = Mathf.Max(1f / 60f, Mathf.Round(life * Fractions[i] * 60f) / 60f);
            _frame = 0;
            _shot = 0;
            _montage = new Texture2D(Cell * 2, CellH * 2, TextureFormat.RGBA32, false);
            _fx.BeginFrame();
            _fx.Add(new EffectSystem.Spawn
            {
                Effect = _current, Position = _right, Yaw = 0, Scale = fx.ScaleRef, Seed = 1234, Time = 0,
                Duration = fx.Duration,
            });
        }

        void Update()
        {
            if (_current < 0 || _times == null) return;
            float t = _frame / 60f;
            foreach (var ps in _stockSystems)
                if (ps.transform.parent == null || ps.transform.parent.GetComponentInParent<ParticleSystem>() == null)
                    ps.Simulate(t, true, true, true);
            _fx.Update(t, _frame == 0 ? 0 : 1f / 60f, _rig.Camera);
            _alt?.Update(t, 1f / 60f, _rig.Camera);
            _fx.BeginFrame();
            _captureDue = _shot < _times.Length && t + 1e-4f >= _times[_shot];
            _frame++;
        }

        void AfterCamera(ScriptableRenderContext ctx, Camera cam)
        {
            if (!_captureDue || cam != _rig.Camera) return;
            _captureDue = false;
            var req = AsyncGPUReadback.Request(_rt, 0, TextureFormat.RGBA32);
            req.WaitForCompletion();
            if (req.hasError) throw new Exception("EffectGallery: readback failed");
            var data = req.GetData<Color32>();
            int cx = (_shot % 2) * Cell, cy = (1 - _shot / 2) * CellH; // first shot top left
            var px = new Color32[Cell * CellH];
            int k = CaptureTool.Width / Cell;
            for (int y = 0; y < CellH; y++)
                for (int x = 0; x < Cell; x++)
                {
                    int r = 0, g = 0, b = 0;
                    for (int dy = 0; dy < k; dy++)
                        for (int dx = 0; dx < k; dx++)
                        {
                            var c = data[(y * k + dy) * CaptureTool.Width + x * k + dx];
                            r += c.r; g += c.g; b += c.b;
                        }
                    int n = k * k;
                    px[y * Cell + x] = new Color32((byte)(r / n), (byte)(g / n), (byte)(b / n), 255);
                }
            _montage.SetPixels32(cx, cy, Cell, CellH, px);
            _shot++;
            if (_shot == _times.Length)
            {
                _montage.Apply();
                var fx = _lib.Effects[_current];
                var name = Path.Combine(_dir, "ab-" + fx.Name + ".jpg");
                var times = string.Join(",", Array.ConvertAll(_times, x => x.ToString("F3", CultureInfo.InvariantCulture)));
                RenderTag.Save(_montage, name, $"effect gallery {fx.Name}: stock left, GPU runtime right, t={times}", 85);
                Debug.Log($"EffectGallery: {fx.Name} -> {name} (particles {_fx.Particles}, rejected {_fx.RejectedTotal})");
                Destroy(_montage);
                Next();
            }
        }

        void OnDestroy()
        {
            RenderPipelineManager.endCameraRendering -= AfterCamera;
            _fx?.Dispose();
            _alt?.Dispose();
            if (_rt != null) _rt.Release();
        }
    }
}
