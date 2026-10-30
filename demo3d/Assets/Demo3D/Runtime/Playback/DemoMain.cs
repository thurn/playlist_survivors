using System;
using System.Collections.Generic;
using System.Globalization;
using PS.Choreography;
using Demo3D.Runtime.Crowd;
using Demo3D.Runtime.Effects;
using Unity.Collections;
using UnityEngine;
using ChoreoRoster = PS.Choreography.Roster;

namespace Demo3D.Runtime.Playback
{
    /// <summary>
    /// The demo player (TD4): builds the song's timeline (or a stress scene's) at load, then each
    /// frame evaluates it at the song clock's time and draws the crowd, the hero, projectiles and
    /// the camera. Flags are qa.md's (DevFlags). It starts itself in a player unless the frame
    /// probe or the benchmark harness owns the run.
    /// </summary>
    public sealed class DemoMain : MonoBehaviour
    {
        DevFlags _flags;
        Timeline _tl;
        TimelineEvaluator _eval;
        ICrowdRenderer _crowd;
        CrowdSlots _slots;
        StockCrowd _heroRenderer;
        GameCameraRig _rig;
        SongClock _clock;
        CaptureTool _capture;
        RosterAssets _roster;
        DemoOverlay _overlay;
        Demo3D.Runtime.Arena.ArenaRuntime _arena;
        EffectSystem _fx;
        EffectDriver _fxDriver;
        double _fxLast = double.NaN;
        SongRunCheck _songCheck;
        CameraFeel _feel;
        SoundScheduler _sounds;
        // TD15's beauty shots: --hold-at x,y (a stress scene's hero holds that point), --beauty-empty (no
        // horde and no combat effects: the arena, the hero and the ambient particles), --capture-mask
        // (after each still, the same frame again as an ID render of the monsters: mask-<t>.png).
        bool _beautyEmpty, _captureMask, _maskOn;
        double _maskAt = double.NaN;

        // Per timeline type: the crowd's type index, walk clip and roster asset data.
        int[] _crowdType;
        float[] _walkStride, _walkLength, _hover, _height;
        RosterAssets.TypeAssets[] _typeAssets;
        int[] _clipToRoster; // per annotation index: the clip index in its owner's RosterAssets clips
        RosterAssets.TypeAssets _hero;
        int _heroWalk;
        float _heroStride = 1.45f;

        readonly List<int> _active = new List<int>();
        int[] _bySpawn;
        int _nextSpawn;
        readonly List<GameObject> _projectiles = new List<GameObject>();
        List<int>[] _typeLists;
        double _endAt;
        public int OnScreenAlive { get; private set; }
        public int OnScreenDying { get; private set; }
        public double SongTime { get; private set; }
        public Timeline Timeline => _tl;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoStart()
        {
            if (UnityEngine.Object.FindAnyObjectByType<Demo3D.Runtime.Crowd.SkeletonLodPreview>() != null) return;
            if (Application.isEditor || Args.Has("--frame-probe") || Args.Has("--bench") || Args.Has("--crowd-bench") || Args.Has("--no-demo") || Args.Has("--effect-gallery")) return;
            new GameObject("DemoMain").AddComponent<DemoMain>();
        }

        void Start()
        {
            try { StartDemo(); }
            catch (Exception e)
            {
                // A batch-mode player would otherwise sit with no demo until something kills it
                // (an unbaked worktree cost ps-e8r.9 15 minutes of timeouts).
                Debug.LogException(e);
                enabled = false;
                if (Application.isBatchMode)
                {
                    Debug.LogError("DemoMain: start-up failed; quitting with exit 1");
                    Application.Quit(1);
                }
            }
        }

        void StartDemo()
        {
            if (Shader.Find("Universal Render Pipeline/Lit") == null) Debug.LogError("DemoMain: URP Lit is missing from the build");
            if (Application.isBatchMode && Args.Has("--capture-video"))
                Debug.LogError("DemoMain: video needs a windowed player; a batch-mode player draws no frames");
            _flags = DevFlags.FromCommandLine();
            Cuts.Active = Cuts.FromCommandLine();
            Debug.Log($"DemoMain: effective {Cuts.Active.Describe()} (--cuts; TD11)");
            // The song check measures uncapped, as the harness does: at the display rate the GPU clocks down and its times inflate.
            QualitySettings.vSyncCount = _flags.FixedTime || _flags.SongCheck != null ? 0 : 1;
            Application.targetFrameRate = -1;
            // The empty boot scene's default camera and light would draw and light the frame too.
            foreach (var c in FindObjectsByType<Camera>(FindObjectsInactive.Include)) Destroy(c.gameObject);
            foreach (var l in FindObjectsByType<Light>(FindObjectsInactive.Include)) Destroy(l.gameObject);
            _rig = new GameCameraRig();
            Demo3D.Runtime.Arena.ArenaClock.Deterministic = _flags.FixedTime; // TD11: captures repeat exactly
            // The arena (TD9) when it is baked into the build, else TD4's flat bench ground.
            // TD14's rung switches (--grd, --merged, --tex-cap, --prepass) default to the ladder's final rung.
            var arena = _flags.ArenaOptions;
            if (arena.Rung != "off" && Demo3D.Runtime.Arena.ArenaRuntime.Available(arena))
                _arena = Demo3D.Runtime.Arena.ArenaRuntime.Load(arena, _rig.Camera, transform);
            else
            {
                if (arena.Rung != "off")
                    Debug.LogWarning($"DemoMain: no arena for {arena.Label()} (baked: {Demo3D.Runtime.Arena.ArenaRuntime.Describe()}); flat ground");
                BenchEnvironment.Build();
            }
            _roster = RosterAssets.Load();
            var data = PlaybackData.Load();
            var level = LevelScript.Parse(data.level.text);
            if (_flags.Envelope > 0) level.EnvelopeOnScreen = _flags.Envelope;
            if (_flags.ViewWidth > 0 && _flags.Scene == "song") level.ScaleViewWidth(_flags.ViewWidth);
            StressScript stress = _flags.Scene == "song" ? null
                : StressScript.Parse(data.Stress(_flags.Scene).text)
                    .With(_flags.N, _flags.ViewWidth, _flags.Load, _flags.Seconds > 0 ? _flags.StartAt + _flags.Seconds + 1 : 0);
            if (stress != null && Args.Get("--hold-at") is string holdAt)
            {
                var xy = holdAt.Split(',');
                stress.HoldAt = new System.Numerics.Vector2(float.Parse(xy[0], CultureInfo.InvariantCulture), float.Parse(xy[1], CultureInfo.InvariantCulture));
            }
            _beautyEmpty = Args.Has("--beauty-empty");
            _captureMask = Args.Has("--capture-mask") && !_beautyEmpty && _flags.CaptureVideo == null; // a mask frame would land in the video
            string layoutName = (stress != null ? stress.Layout : level.Layout) ?? "arena";
            var layout = data.Layout(layoutName) ?? data.Layout("arena");
            // The timeline navigates the density the arena scene is drawn at (TD15; the cold review's item 3).
            var choreo = ChoreographyData.FromJson(data.annotations.text, data.roster.text, layout.text, data.footprints.text,
                _arena != null ? _arena.Density : 0);
            var started = Time.realtimeSinceStartupAsDouble;
            _tl = stress == null
                ? Choreographer.Run(SongMap.Parse(data.songMap.text), level, choreo)
                : Choreographer.RunStress(stress, choreo);
            Debug.Log($"DemoMain: {_tl.Name} built in {Time.realtimeSinceStartupAsDouble - started:F2} s, " +
                      $"{_tl.EntityCount} entities; sim ticks {_tl.TickTiming?.ToJson()}");
            _eval = new TimelineEvaluator(_tl, choreo.Clips);
            MapTypes(choreo);
            _endAt = _flags.Seconds > 0 ? _flags.StartAt + _flags.Seconds : _tl.Duration;

            _crowd = CrowdFactory.Create(_flags.Crowd);
            _crowd.Initialize(Array.ConvertAll(_typeAssets, t => t.id), 16384); // instances per type; the bone path sizes small buffers by it
            _slots = new CrowdSlots(_typeAssets.Length, 16384);
            if (_crowd is BoneTextureCrowd bone) bone.Clock = () => (float)SongTime; // deterministic LOD crossfades
            _heroRenderer = new StockCrowd();
            _heroRenderer.Initialize(new[] { _hero.id }, 1);
            ApplyAntiAliasing(_flags.Aa, _rig.Camera);
            StartEffects();
            _overlay = _flags.Overlay ? gameObject.AddComponent<DemoOverlay>() : null;
            if (_overlay != null) _overlay.Main = this;

            _bySpawn = new int[_tl.EntityCount];
            for (int i = 0; i < _bySpawn.Length; i++) _bySpawn[i] = i;
            Array.Sort(_bySpawn, (a, b) => _tl.Entities[a].Spawn.CompareTo(_tl.Entities[b].Spawn));
            _typeLists = new List<int>[_typeAssets.Length];
            for (int i = 0; i < _typeLists.Length; i++) _typeLists[i] = new List<int>();

            _clock = new SongClock(_flags.FixedTime, _flags.StartAt);
            if (_flags.CaptureStills != null || _flags.CaptureVideo != null)
                _capture = new CaptureTool(_rig.Camera, _flags.CaptureStills, _flags.StillsAt, _flags.CaptureVideo);
            var music = gameObject.AddComponent<AudioSource>();
            AudioSource click = null;
            AudioClip clickClip = null;
            if (_flags.Click && _flags.Scene == "song")
            {
                click = gameObject.AddComponent<AudioSource>();
                clickClip = ClickTrack.Build(_tl.Map);
            }
            _clock.Start(_flags.Scene == "song" ? music : null, data.music, 0.5, click, clickClip);
            _feel = _tl.Map != null ? new CameraFeel(_tl) : null;
            if (_flags.DumpSounds != null) DumpSounds(_flags.DumpSounds);
            if (!_flags.FixedTime && _flags.Scene == "song")
                _sounds = new SoundScheduler(_tl, _clock, SoundBank.Load(), gameObject, _flags.StartAt);
        }

        /// <summary>
        /// TD8's effects on the GPU runtime, unless `--no-effects` or the library isn't baked:
        /// `--effect-pool N` particles, `--light-pool N` pooled lights (EffectLights.DefaultPool),
        /// `--only-effects a,b` to spawn only those library effects.
        /// </summary>
        void StartEffects()
        {
            var lib = Args.Has("--no-effects") ? null : EffectLibrary.TryLoad();
            if (lib == null)
            {
                if (!Args.Has("--no-effects")) Debug.LogWarning("DemoMain: no effect library (run `demo3d unity BakeEffects`); effects off");
                return;
            }
            _fx = new EffectSystem(lib, Args.GetInt("--effect-pool", EffectSystem.DefaultPool), Args.GetInt("--light-pool", EffectLights.DefaultPool));
            _fx.AddCamera(_rig.Camera);
            _fxDriver = new EffectDriver(_tl, _fx, t => _typeAssets[t].kind == "elite" || _typeAssets[t].kind == "boss");
            _fxDriver.OnlyAmbient = _beautyEmpty;
            _fxDriver.OnlyEffects(Args.Get("--only-effects"));
        }

        /// <summary>`--aa msaa4|taa|off` (performance.md: MSAA 4x until TD9 picks).</summary>
        public static void ApplyAntiAliasing(string mode, Camera camera)
        {
            var urp = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline as UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset;
            var data = camera.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
            bool taa = mode == "taa";
            if (urp != null) urp.msaaSampleCount = mode == "msaa4" ? 4 : 1;
            if (data != null)
                data.antialiasing = taa ? UnityEngine.Rendering.Universal.AntialiasingMode.TemporalAntiAliasing
                                        : UnityEngine.Rendering.Universal.AntialiasingMode.None;
        }

        void MapTypes(ChoreographyData choreo)
        {
            int n = _tl.TypeNames.Length;
            _typeAssets = new RosterAssets.TypeAssets[n];
            _walkStride = new float[n];
            _walkLength = new float[n];
            _hover = new float[n];
            _height = new float[n];
            for (int i = 0; i < n; i++)
            {
                var name = _tl.TypeNames[i];
                var asset = _roster.Find(name) ?? (name == "boss" ? Array.Find(_roster.types, t => t.kind == "boss") : null)
                    ?? throw new InvalidOperationException("DemoMain: no roster asset for type " + name);
                _typeAssets[i] = asset;
                int walkAnn = choreo.Clips.Find(name + " walk");
                _walkStride[i] = walkAnn >= 0 ? choreo.Clips[walkAnn].StrideM : 1f;
                int walk = asset.ClipIndex("walk");
                _walkLength[i] = walk >= 0 ? asset.clips[walk].length : 1f;
                _hover[i] = asset.hoverM;
                _height[i] = asset.heightM;
            }
            _hero = _roster.Find("hero");
            _heroWalk = _hero.ClipIndex("walk");
            int heroWalkAnn = choreo.Clips.Find("hero walk");
            if (heroWalkAnn >= 0 && choreo.Clips[heroWalkAnn].StrideM > 0) _heroStride = choreo.Clips[heroWalkAnn].StrideM;
            _clipToRoster = new int[choreo.Clips.Count];
            for (int c = 0; c < choreo.Clips.Count; c++)
            {
                var owner = _eval.OwnerOf(c);
                var asset = owner == "hero" ? _hero : _roster.Find(owner) ?? (owner == "boss" ? Array.Find(_roster.types, t => t.kind == "boss") : null);
                _clipToRoster[c] = asset != null ? asset.ClipIndex(_eval.RoleOf(c)) : -1;
            }
        }

        void Update()
        {
            if (_tl == null) return;
            try { Frame(); }
            catch (Exception e)
            {
                // A broken frame never recovers; end the run instead of spinning on it.
                Debug.LogException(e);
                Quit(1);
            }
        }

        double _captureAt = double.NaN;

        void OnEnable() => UnityEngine.Rendering.RenderPipelineManager.endCameraRendering += AfterCamera;
        void OnDisable() => UnityEngine.Rendering.RenderPipelineManager.endCameraRendering -= AfterCamera;

        void AfterCamera(UnityEngine.Rendering.ScriptableRenderContext context, Camera camera)
        {
            if (_capture == null || double.IsNaN(_captureAt) || _rig == null || camera != _rig.Camera) return;
            int before = _capture.StillsWritten;
            _capture.Capture(_captureAt);
            _captureAt = double.NaN;
            if (_maskOn || _capture.StillsWritten == before) return;
            if (_captureMask) _maskAt = _capture.LastStillAt;
            WriteView(camera, _capture.LastStillAt);
        }

        Vector3 _heroFeet;

        /// <summary>
        /// Beside each still, view-&lt;t&gt;.json: the camera's view-projection (OpenGL clip space, row-major)
        /// and the hero's feet, so the look harness finds the ground around the hero (ps-e8r.5's lit stage).
        /// </summary>
        void WriteView(Camera camera, double at)
        {
            if (_flags.CaptureStills == null) return;
            var m = camera.projectionMatrix * camera.worldToCameraMatrix;
            var sb = new System.Text.StringBuilder("{\"viewProj\": [");
            for (int r = 0; r < 4; r++)
            for (int c = 0; c < 4; c++)
                sb.Append(string.Format(CultureInfo.InvariantCulture, "{0}{1:R}", r + c == 0 ? "" : ", ", m[r, c]));
            sb.Append(string.Format(CultureInfo.InvariantCulture, "], \"hero\": [{0:R}, {1:R}, {2:R}]}}\n", _heroFeet.x, _heroFeet.y, _heroFeet.z));
            System.IO.File.WriteAllText(System.IO.Path.Combine(_flags.CaptureStills,
                string.Format(CultureInfo.InvariantCulture, "view-{0:000.000}.json", at)), sb.ToString());
        }

        /// <summary>
        /// The ID render's frame (TD15's readability measure): only the crowd, drawn flat white
        /// (CrowdSkinned's _Demo3DIdMask) on black. At E2 the set and the hero draw black with their depth
        /// (LitStaticShadow), so a monster behind a tree or a wall is masked out; the ground, the
        /// atmosphere's cards and particles are hidden; effects and post are off. At E0 (URP Lit kit) the
        /// arena is hidden instead, without that occlusion.
        /// </summary>
        void MaskMode(bool on)
        {
            _maskOn = on;
            Shader.SetGlobalFloat("_Demo3DIdMask", on ? 1f : 0f);
            if (_arena != null)
            {
                if (_arena.Rung == Demo3D.Runtime.Arena.ArenaIndex.E2) _arena.SetIdMask(on);
                else
                {
                    var scene = UnityEngine.SceneManagement.SceneManager.GetSceneByName(_arena.SceneName);
                    if (scene.IsValid()) foreach (var go in scene.GetRootGameObjects()) go.SetActive(!on);
                }
                if (on) _maskBackground = _rig.Camera.backgroundColor;
            }
            if (on) _rig.Camera.backgroundColor = Color.black;
            else if (_arena != null) _rig.Camera.backgroundColor = _maskBackground;
            var data = _rig.Camera.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
            if (data != null) data.renderPostProcessing = !on;
            if (on) _fx?.RemoveCamera(_rig.Camera); else _fx?.AddCamera(_rig.Camera);
        }

        Color _maskBackground;

        readonly System.Diagnostics.Stopwatch _evalWatch = new System.Diagnostics.Stopwatch();
        readonly System.Diagnostics.Stopwatch _drawWatch = new System.Diagnostics.Stopwatch();
        double _lastWall;

        bool _quitNext;

        void Frame()
        {
            if (_quitNext) { Quit(); return; }
            _evalWatch.Reset();
            _drawWatch.Reset();
            if (_maskOn) MaskMode(false);
            double t = (_flags.FreezeAt >= 0 ? _flags.FreezeAt : _clock.Now()) + _flags.TimeOffset;
            if (!double.IsNaN(_maskAt))
            {
                t = _maskAt; // the still's frame again, as its ID render
                _maskAt = double.NaN;
                MaskMode(true);
                _capture.ForceStill(t, "mask");
            }
            SongTime = t;
            Activate(t);

            foreach (var l in _typeLists) l.Clear();
            int proj = 0;
            OnScreenAlive = OnScreenDying = 0;
            var framing = _tl.FramingAt(Math.Max(0, t));
            float viewWidth = _flags.ViewWidth > 0 && _flags.Scene != "song" ? _flags.ViewWidth : framing.ViewWidth;
            // Punch and shake follow the song as heard, not --time-offset (TD11's invisible anchor
            // must change only the horde), and the shadows fit the view before its punch, so the
            // cascades don't resize every frame after an accent (the demo systems' review).
            double feelT = t - _flags.TimeOffset;
            Demo3D.Runtime.Arena.ArenaClock.Override = feelT; // the arena animates on song time, off the anchor's offset
            float punch = _feel != null && !_flags.NoFeel ? _feel.Punch(feelT) : 1f;
            _rig.Frame(new Vector2(framing.Focus.X, framing.Focus.Y), viewWidth * punch, viewWidth);
            if (_feel != null && !_flags.NoFeel) _rig.Camera.transform.position += _feel.Shake(feelT);
            _sounds?.Update(t);
            foreach (var e in _active)
            {
                var info = _tl.Entities[e];
                if (info.Kind == EntityKind.Monster && !_beautyEmpty) _typeLists[info.Type].Add(e);
                else if (info.Kind == EntityKind.Projectile && _fx == null && !_maskOn) DrawProjectile(proj++, e, t); // no stand-in spheres in the ID render
            }
            for (int i = proj; i < _projectiles.Count; i++) _projectiles[i].SetActive(false);
            if (_fx != null && !_maskOn)
            {
                _fx.BeginFrame();
                _fxDriver.Frame(t);
                _fx.Update(t, double.IsNaN(_fxLast) ? 0f : (float)Math.Max(0, t - _fxLast), _rig.Camera);
                _fxLast = t;
            }

            _evalWatch.Start();
            // Stable slots: a monster keeps its slot while it lives (motion vectors, the previous
            // palette and LOD state follow the slot); a freed slot is written Hidden first.
            _slots.Begin();
            for (int type = 0; type < _typeLists.Length; type++)
                foreach (int e in _typeLists[type]) _slots.Assign(type, e);
            _slots.End();
            for (int type = 0; type < _typeLists.Length; type++)
            {
                var list = _typeLists[type];
                var inst = _crowd.Instances(type, _slots.Count(type));
                _slots.FillHidden(type, inst);
                for (int k = 0; k < list.Count; k++)
                {
                    int e = list[k];
                    var st = _eval.Evaluate(e, t, _walkStride[type], _walkLength[type], _hover[type]);
                    inst[_slots.Assign(type, e)] = ToInstance(st, _typeAssets[type], type, _tl.Entities[e].Seed);
                    if (_rig.OnScreen(st.Position, _height[type]))
                    {
                        if (st.Dying) OnScreenDying++; else OnScreenAlive++;
                    }
                }
            }
            _evalWatch.Stop();
            _drawWatch.Start();
            _crowd.Draw(_rig.Camera);
            _drawWatch.Stop();

            var heroState = _eval.Evaluate(0, t, _heroStride, _hero.clips[_heroWalk].length, 0);
            var heroInst = _heroRenderer.Instances(0, 1);
            heroInst[0] = ToInstance(heroState, _hero, -1, 0);
            if (!_maskOn) _heroRenderer.Draw(_rig.Camera);
            Demo3D.Runtime.Arena.CanopyFade.SetHero(heroState.Position); // TD15: canopies near the hero dither out
            _arena?.FollowHero(heroState.Position);
            _heroFeet = heroState.Position;

            _captureAt = _capture != null ? t : double.NaN; // read back once URP has drawn it
            // A batch-mode player draws no frames, so it draws the camera by hand for each still it
            // owes (windowless checks on the Mac). Only those frames: URP's hand-drawn frames hold
            // their pooled targets until a frame the player loop draws (CaptureTool).
            if (Application.isBatchMode && _capture != null && _capture.StillDue(t)) _rig.Camera.Render();
            WriteStats(t);
            if (_flags.SongCheck != null)
                (_songCheck ??= new SongRunCheck()).Update(t, _fx?.TakeRejected() ?? 0, _fx?.Lights.DropShare ?? 0);
            _lastWall = Time.realtimeSinceStartupAsDouble;
            _clock.EndFrame();
            // Quit on the next frame, so this frame is drawn (and captured) first.
            if (t >= _endAt || (_capture != null && _capture.Done && _flags.CaptureVideo == null && double.IsNaN(_maskAt) && !_maskOn))
                _quitNext = true;
        }

        MonsterInstance ToInstance(in TimelineEvaluator.State st, RosterAssets.TypeAssets asset, int type, uint seed)
        {
            uint a = RosterClip(st.A, asset), b = RosterClip(st.B, asset);
            float Pose(float time) => _flags.PoseRate > 0 ? Mathf.Floor(time * _flags.PoseRate) / _flags.PoseRate : time;
            return new MonsterInstance
            {
                Position = st.Position,
                Yaw = st.Yaw,
                ClipA = a, TimeA = Pose(st.A.Time),
                ClipB = b, TimeB = Pose(st.B.Time),
                Blend = st.Blend,
                Scale = 0.92f + 0.16f * ClipSchedule.Jitter(seed, 1),
                HitFlash = st.HitFlash,
                Dissolve = st.Dissolve,
                Tint = 0,
                Flags = (uint)(st.Dying ? CrowdFlags.Dying : CrowdFlags.Alive),
            };
        }

        uint RosterClip(TimelineEvaluator.Slot s, RosterAssets.TypeAssets asset)
        {
            int idx = s.Clip < 0 ? asset.ClipIndex("walk") : _clipToRoster[s.Clip];
            return (uint)Math.Max(0, idx);
        }

        void Activate(double t)
        {
            // Seeks and freezes rebuild the active set; forward play adds and drops incrementally.
            if (_nextSpawn == 0 || t < _lastT)
            {
                _active.Clear();
                _nextSpawn = 0;
            }
            _lastT = t;
            while (_nextSpawn < _bySpawn.Length && _tl.Entities[_bySpawn[_nextSpawn]].Spawn <= t)
            {
                int e = _bySpawn[_nextSpawn++];
                if (_tl.Entities[e].Kind != EntityKind.Hero && _tl.Entities[e].Despawn > t) _active.Add(e);
            }
            _active.RemoveAll(e => _tl.Entities[e].Despawn <= t);
        }

        double _lastT = double.NegativeInfinity;

        void DrawProjectile(int slot, int e, double t)
        {
            while (_projectiles.Count <= slot)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                Destroy(go.GetComponent<Collider>());
                go.transform.localScale = Vector3.one * 0.35f;
                // URP Lit is in every build (the bench ground uses it); Unlit may be stripped.
                var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                mat.SetColor("_BaseColor", Color.black);
                mat.EnableKeyword("_EMISSION");
                mat.SetColor("_EmissionColor", new Color(4f, 2.2f, 0.8f));
                go.GetComponent<Renderer>().sharedMaterial = mat;
                _projectiles.Add(go);
            }
            var p = _tl.PositionAt(e, t);
            _projectiles[slot].SetActive(true);
            _projectiles[slot].transform.position = new Vector3(p.X, 1.2f, p.Y);
        }

        System.IO.StreamWriter _stats;
        double _nextStat = double.NegativeInfinity;

        void WriteStats(double t)
        {
            if (_flags.Stats == null || t < _nextStat) return;
            if (_stats == null)
            {
                _stats = new System.IO.StreamWriter(_flags.Stats) { AutoFlush = true };
                _stats.WriteLine("t,section,onScreenAlive,onScreenDying,target,active,viewWidth,wallFrameMs,evalMs,crowdMs,particles,effectSpawns,lightCues,lightsUsed,lightDropShare");
            }
            _nextStat = t + 0.5;
            int si = _tl.Map != null ? _tl.Map.SectionIndexAt(t) : 0;
            var sec = _tl.Sections != null && si >= 0 && si < _tl.Sections.Length ? _tl.Sections[si] : null;
            double wall = Time.realtimeSinceStartupAsDouble;
            _stats.WriteLine(string.Format(CultureInfo.InvariantCulture, "{0:F2},{1},{2},{3},{4},{5},{6:F1},{7:F1},{8:F2},{9:F2}",
                t, sec?.Label ?? _tl.Name, OnScreenAlive, OnScreenDying, sec?.TargetOnScreen ?? 0, _active.Count,
                _tl.FramingAt(Math.Max(0, t)).ViewWidth, (wall - _lastWall) * 1000, _evalWatch.Elapsed.TotalMilliseconds,
                _drawWatch.Elapsed.TotalMilliseconds) + string.Format(CultureInfo.InvariantCulture, ",{0},{1},{2},{3},{4:F4}",
                _fx?.Particles ?? 0, _fx?.SpawnsTotal ?? 0, _fx?.Lights.ActiveCues ?? 0, _fx?.Lights.LightsUsed ?? 0, _fx?.Lights.DropShare ?? 0));
        }

        /// <summary>
        /// The timeline's sound events with each one's clip name, for TD13's offline mix (the same
        /// list the live scheduler plays).
        /// </summary>
        void DumpSounds(string path)
        {
            var bank = SoundBank.Load();
            var sb = new System.Text.StringBuilder("{\"sounds\": [\n");
            for (int i = 0; i < _tl.Sounds.Length; i++)
            {
                var s = _tl.Sounds[i];
                var name = _tl.SoundNames[s.Sound];
                var clip = bank != null ? bank.Find(name) : null;
                // (A "}}" right after a format spec would be read into the spec, so the closing
                // brace is its own argument.)
                sb.AppendFormat(CultureInfo.InvariantCulture, "  {{\"time\": {0:F6}, \"sound\": \"{1}\", \"clip\": \"{2}\", \"gain\": {3:F4}{4}{5}\n",
                    s.Time, name, clip != null ? clip.name : "", s.Gain, "}", i + 1 < _tl.Sounds.Length ? "," : "");
            }
            sb.Append("]}\n");
            System.IO.File.WriteAllText(path, sb.ToString());
            Debug.Log($"DemoMain: wrote {_tl.Sounds.Length} sound events to {path} (sound bank {(bank != null ? bank.names.Length + " names" : "missing")})");
        }

        void Quit(int code = 0)
        {
            _stats?.Dispose();
            _stats = null;
            if (_songCheck != null && !_songCheck.Write(_flags.SongCheck, _tl.Duration, Environment.CommandLine) && code == 0) code = 2;
            _songCheck?.Dispose();
            _songCheck = null;
            _capture?.Dispose();
            _capture = null;
            Debug.Log($"DemoMain: done at song time {SongTime.ToString("F2", CultureInfo.InvariantCulture)}" +
                      (_fx != null ? $"; effects: {_fx.SpawnsTotal} spawns, {_fx.MergedTotal} merged (cuts), {_fx.GlowsDropped} glow emitters dropped (cuts)" : ""));
            enabled = false;
            Application.Quit(code);
        }

        void OnDestroy()
        {
            _capture?.Dispose();
            _songCheck?.Dispose();
            _fx?.Dispose();
            _crowd?.Dispose();
            _heroRenderer?.Dispose();
            _arena?.Dispose();
        }
    }
}
