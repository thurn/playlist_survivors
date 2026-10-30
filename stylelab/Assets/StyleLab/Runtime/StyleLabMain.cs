using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;

namespace StyleLab.Runtime
{
    /// <summary>
    /// The style lab (stylelab.md): a horde fighting the knight to the song, wearing one
    /// treatment at a time. Keys: 1-9 or the arrows pick a treatment, C the camera shot,
    /// O the painted look's ink lines, A auto-cycles, H hides the label, M mutes, Esc quits.
    ///
    /// Flags: --treatment N and --shot N (1-based) to start on, --start-at S (song seconds),
    /// --count N (fodder, default 260), --no-sound, and for agents, windowless:
    /// `-batchmode --stills DIR [--shots 1,2,3] [--treatments 1,...]` writes one tagged still per
    /// shot and treatment, each mid-beat (after the hit flashes fade), then quits.
    /// </summary>
    public sealed class StyleLabMain : MonoBehaviour
    {
        static readonly int HeroPosId = Shader.PropertyToID("_SL_HeroPos");
        static readonly int RingGainId = Shader.PropertyToID("_SL_RingGain");

        StyleLabData _data;
        MaterialBank _bank;
        Beat _beat;
        Stage _stage;
        Horde _horde;
        Hero _hero;
        Effects _effects;
        CameraRig _rig;
        AudioSource _audio;
        int _treatment;
        bool _label = true;
        bool _auto;
        int _autoBar = -1;
        float _labelShownAt;
        string _error;

        // Stills.
        string _stillsDir;
        readonly List<(int shot, int treatment)> _stills = new List<(int, int)>();
        int _stillFrames;
        RenderTexture _stillTarget;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (FindAnyObjectByType<StyleLabMain>() == null) new GameObject("StyleLab").AddComponent<StyleLabMain>();
        }

        void Start()
        {
            _data = Resources.Load<StyleLabData>(StyleLabData.ResourcePath);
            if (_data == null)
            {
                _error = "StyleLabData is missing: run `uv run --project demo3d/tools stylelab unity Bake`, then rebuild.";
                Debug.LogError(_error);
                if (Application.isBatchMode) Application.Quit(1);
                return;
            }
            _stillsDir = Args.Get("--stills");
            float startAt = Args.GetFloat("--start-at", 37.2f);
            if (_stillsDir != null) Time.captureFramerate = 60;
            else Application.targetFrameRate = 120;

            _bank = new MaterialBank(_data);
            _rig = new CameraRig(transform);
            _stage = new Stage(_data, transform);
            _effects = new Effects(_data, transform);
            var figures = new GameObject("Figures").transform;
            figures.SetParent(transform, false);
            _hero = new Hero(_data, figures);
            _horde = new Horde(_data, figures, Args.GetInt("--count", 260), _effects, _stage.Obstacles, 1);
            bool sound = _stillsDir == null && !Args.Has("--no-sound") && !Application.isBatchMode && _data.song != null;
            if (sound)
            {
                _audio = gameObject.AddComponent<AudioSource>();
                _audio.playOnAwake = false;
                _audio.volume = 0.8f;
            }
            _beat = new Beat(_data, _audio, startAt);
            _rig.Set(Args.GetInt("--shot", 1) - 1);
            SetTreatment(Args.GetInt("--treatment", 2) - 1);

            if (_stillsDir != null)
            {
                Directory.CreateDirectory(_stillsDir);
                var shots = ParseList(Args.Get("--shots", "1,2,3"));
                var treatments = ParseList(Args.Get("--treatments", "1,2,3,4,5,6,7,8,9"));
                foreach (var s in shots)
                    foreach (var t in treatments)
                        _stills.Add((s - 1, t - 1));
                _stillTarget = new RenderTexture(1920, 1080, 24, RenderTextureFormat.ARGB32) { name = "Still", antiAliasing = 1 };
                BeginStill();
            }
        }

        static List<int> ParseList(string s)
        {
            var list = new List<int>();
            foreach (var p in s.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
                list.Add(int.Parse(p, CultureInfo.InvariantCulture));
            return list;
        }

        void SetTreatment(int index)
        {
            _treatment = (index % Treatments.All.Length + Treatments.All.Length) % Treatments.All.Length;
            _horde.ApplyTreatment(_treatment, _bank);
            _hero.Figure.Apply(Treatments.StyleFor(_treatment, "hero"), _bank);
            _stage.ApplyPost(Treatments.All[_treatment].post);
            Shader.SetGlobalFloat(RingGainId, Treatments.All[_treatment].style == Style.Baseline ? 0f : 1f);
            _labelShownAt = Time.unscaledTime;
        }

        void Update()
        {
            if (_data == null) return;
            if (_stillsDir == null) HandleKeys();
            float dt = _stillsDir != null ? 1f / 60f : Mathf.Min(Time.deltaTime, 0.05f);
            _beat.Update(dt);
            Shader.SetGlobalVector(HeroPosId, _hero.Position);
            _horde.Update(dt, _beat, _hero.Position);
            _hero.Update(dt, _beat, _horde, _effects);
            _effects.Update(dt);
            _effects.Ambient(_horde, _beat, dt);
            _stage.Torch.intensity = 8f + 3f * _beat.Pulse;
            _rig.Update(dt);
            if (_auto)
            {
                int bar = _beat.BarOf(_beat.Index);
                if (_autoBar < 0) _autoBar = bar;
                if (bar - _autoBar >= 8) { _autoBar = bar; SetTreatment(_treatment + 1); }
            }
        }

        void HandleKeys()
        {
            for (int i = 0; i < 9; i++)
                if (Input.GetKeyDown(KeyCode.Alpha1 + i) || Input.GetKeyDown(KeyCode.Keypad1 + i)) SetTreatment(i);
            if (Input.GetKeyDown(KeyCode.RightArrow) || Input.GetKeyDown(KeyCode.Space)) SetTreatment(_treatment + 1);
            if (Input.GetKeyDown(KeyCode.LeftArrow)) SetTreatment(_treatment - 1);
            if (Input.GetKeyDown(KeyCode.C)) _rig.Set(_rig.Index + 1);
            if (Input.GetKeyDown(KeyCode.O)) { _bank.Outlines = !_bank.Outlines; SetTreatment(_treatment); }
            if (Input.GetKeyDown(KeyCode.A)) { _auto = !_auto; _autoBar = -1; _labelShownAt = Time.unscaledTime; }
            if (Input.GetKeyDown(KeyCode.H)) _label = !_label;
            if (Input.GetKeyDown(KeyCode.M)) AudioListener.volume = AudioListener.volume > 0 ? 0 : 1;
            if (Input.GetKeyDown(KeyCode.Escape)) Application.Quit(0);
        }

        // ---------------------------------------------------------------------------------
        // Stills: each (shot, treatment) settles for a second, then is drawn mid-beat.

        void BeginStill()
        {
            var (shot, treatment) = _stills[0];
            _rig.Set(shot);
            SetTreatment(treatment);
            _stillFrames = 0;
        }

        void LateUpdate()
        {
            if (_stillsDir == null || _stills.Count == 0) return;
            _stillFrames++;
            if (_stillFrames < 60 || _beat.Phase < 0.35f || _beat.Phase > 0.55f) return;
            var (shot, treatment) = _stills[0];
            var cam = _rig.Camera;
            cam.targetTexture = _stillTarget;
            cam.Render();
            cam.targetTexture = null;
            var name = string.Format(CultureInfo.InvariantCulture, "{0}-{1}-{2}.jpg",
                CameraRig.Shots[shot].name.ToLowerInvariant(), treatment + 1, Slug(Treatments.All[treatment].name));
            Save(_stillTarget, Path.Combine(_stillsDir, name),
                $"stylelab still: {Treatments.All[treatment].name}, {CameraRig.Shots[shot].name} shot, t={_beat.Time:F2}");
            _stills.RemoveAt(0);
            if (_stills.Count > 0) BeginStill();
            else
            {
                Debug.Log("StyleLabMain: stills done");
                Application.Quit(0);
            }
        }

        static string Slug(string s) => s.ToLowerInvariant().Replace(' ', '-');

        static void Save(RenderTexture target, string path, string tool)
        {
            var req = AsyncGPUReadback.Request(target, 0, TextureFormat.RGBA32);
            req.WaitForCompletion();
            if (req.hasError) throw new Exception("StyleLabMain: GPU readback failed");
            var tex = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false);
            tex.LoadRawTextureData(req.GetData<byte>());
            tex.Apply();
            RenderTag.Save(tex, path, tool);
            Destroy(tex);
            Debug.Log("StyleLabMain: wrote " + path);
        }

        // ---------------------------------------------------------------------------------

        GUIStyle _title, _body;

        void OnGUI()
        {
            if (_error != null)
            {
                GUI.Label(new Rect(20, 20, 1200, 40), _error);
                return;
            }
            if (!_label || _data == null || _stillsDir != null) return;
            if (_title == null)
            {
                _title = new GUIStyle(GUI.skin.label) { fontSize = 26, fontStyle = FontStyle.Bold };
                _title.normal.textColor = new Color(1f, 1f, 1f, 0.92f);
                _body = new GUIStyle(GUI.skin.label) { fontSize = 15, wordWrap = true };
                _body.normal.textColor = new Color(1f, 1f, 1f, 0.75f);
            }
            var t = Treatments.All[_treatment];
            GUI.Label(new Rect(24, 18, 900, 36), $"{_treatment + 1}  {t.name}", _title);
            float age = Time.unscaledTime - _labelShownAt;
            if (age < 6f)
            {
                var c = _body.normal.textColor;
                c.a = 0.75f * Mathf.Clamp01((6f - age) / 1.5f);
                _body.normal.textColor = c;
                GUI.Label(new Rect(26, 54, 760, 60), t.blurb, _body);
            }
            var help = $"1-9 / ← → treatment   C camera ({CameraRig.Shots[_rig.Index].name})   O ink lines ({(_bank.Outlines ? "on" : "off")})   " +
                       $"A auto-cycle ({(_auto ? "on" : "off")})   H hide   M mute   Esc quit";
            var hs = new GUIStyle(_body) { fontSize = 13 };
            hs.normal.textColor = new Color(1f, 1f, 1f, 0.45f);
            GUI.Label(new Rect(26, Screen.height - 34, 1400, 24), help, hs);
        }

        void OnDestroy()
        {
            if (_stillTarget != null) _stillTarget.Release();
        }
    }
}
