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
    /// probe or the benchmark harness owns the run. `--trailer` plays a trailer's shots instead
    /// (DemoMain.Trailer.cs).
    /// </summary>
    public sealed partial class DemoMain : MonoBehaviour
    {
        DevFlags _flags;
        Timeline _tl;
        TimelineEvaluator _eval;
        ICrowdRenderer _crowd;
        CrowdSlots _slots;
        StockCrowd _heroRenderer;
        HeroBlade _heroBlade;
        GameCameraRig _rig;
        SongClock _clock;
        CaptureTool _capture;
        BatchGpuProbe _gpuProbe; // --gpu-probe-at: windowless frame times at a few song times
        RosterAssets _roster;
        DemoOverlay _overlay;
        Demo3D.Runtime.Arena.ArenaRuntime _arena; // the drawn arena (--resident: the set's active one)
        Demo3D.Runtime.Arena.ArenaSet _arenas;    // --resident: several environments in one player (trailer.md)
        PostFeel[] _postFeels;                    // --resident: each arena's post feel, on its own volume
        int _warmed;                              // --resident: environments drawn off-screen before the first frame (Warm)
        bool _arenasWarm;
        RenderTexture _warmTarget, _liveTarget; // the warm frames' target, and the camera's own (the capture's) meanwhile
        Action _startClock;
        EffectSystem _fx;
        EffectDriver _fxDriver;
        double _fxLast = double.NaN;
        SongRunCheck _songCheck;
        CameraFeel _feel;
        BlowFeel _blows; // an intensity's blow weights and feel (null: normal)
        PostFeel _postFeel;
        MonsterStyle _style;
        StructureStyle _structures;
        StyleBeat _styleBeat;
        CrystalShatter _shatter; // crystal types' deaths break into baked shards (null without one)
        Demo3D.Runtime.Arena.StructureShatter _breaks; // --structure-breaks: the struck pieces' shards (null without breaks)
        bool _stylePosted;
        float _supersample = 1f;  // --supersample (Supersampling)
        TrailerLook _look;        // a trailer's grain, dirt and lens hits (null outside a trailer and with --no-trailer-look)
        SoundScheduler _sounds;
        SoundScheduler.Cue[] _soundCues; // the timeline's sounds and the crystal shatter's
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
            CheckTrailerFlags();
            _style = MonsterStyle.FromCommandLine();
            _structures = StructureStyle.FromCommandLine(_style); // fails here on an unknown name; match follows the horde
            Debug.Log($"DemoMain: {StructureStyle.Flag} {_structures.Name}");
            var rosterSet = RosterSet.Active; // fails here on an unknown set or a style beside one
            Debug.Log($"DemoMain: {RosterSet.Flag} {rosterSet.Name}");
            var intensity = Intensity.Active; // fails here on an unknown intensity
            Debug.Log($"DemoMain: {Intensity.Flag} {intensity.Name}");
            var breakSchedule = BreakSchedule.Active; // fails here on an unknown schedule; an unknown target fails when the timeline builds
            Debug.Log($"DemoMain: {BreakSchedule.Flag} {breakSchedule.Name ?? "none"}");
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
            // `--environment NAME` (ps-bop): its whole layout, which the timeline navigates; fails here on an unknown one.
            var environmentLayout = PlaybackData.Load().EnvironmentLayout(arena.Environment);
            if (_flags.SwitchEvery > 0 && _flags.Resident == null) throw new InvalidOperationException("DemoMain: --switch-every needs --resident");
            _roster = RosterAssets.Load();
            Dictionary<string, ChoreographyData> trailerData = null;
            if (_flags.Trailer != null)
            {
                // Every environment the shots use, resident; fails here on a bad trailer file or an environment that isn't baked.
                LoadTrailer(PlaybackData.Load(), rosterSet, arena.Density, out trailerData);
                _arenas = Demo3D.Runtime.Arena.ArenaSet.Load(arena, _trailer.Environments, _rig.Camera, transform);
                _arena = _arenas[0];
            }
            else if (_flags.Resident != null)
            {
                // Fails here on an environment that isn't baked; --environment's is active first and its layout the timeline's.
                _arenas = Demo3D.Runtime.Arena.ArenaSet.Load(arena, ResidentEnvironments(arena.Environment, _flags.Resident), _rig.Camera, transform);
                _arena = _arenas[0];
            }
            else if (arena.Rung != "off" && Demo3D.Runtime.Arena.ArenaRuntime.Available(arena))
                _arena = Demo3D.Runtime.Arena.ArenaRuntime.Load(arena, _rig.Camera, transform);
            else
            {
                if (arena.Rung != "off" && arena.Environment != PS.Choreography.LayoutEnvironment.Default)
                    throw new InvalidOperationException($"DemoMain: no arena for {arena.Label()} (baked: {Demo3D.Runtime.Arena.ArenaRuntime.Describe()}); " +
                                                        $"run demo3d unity BakeArena -environment {arena.Environment}");
                if (arena.Rung != "off")
                    Debug.LogWarning($"DemoMain: no arena for {arena.Label()} (baked: {Demo3D.Runtime.Arena.ArenaRuntime.Describe()}); flat ground");
                BenchEnvironment.Build();
            }
            if (!_structures.IsBaseline)
            {
                if (_arena == null) throw new InvalidOperationException($"DemoMain: {StructureStyle.Flag} {_structures.Name} needs the arena, not flat ground");
                // A look only: the structures' materials change once each arena's scene is ready (environment.md, Structure
                // styles), every resident one's too.
                if (_arenas != null) for (int i = 0; i < _arenas.Count; i++) _arenas[i].OnReady += a => _structures.Apply(a, CrowdLibrary.Load());
                else _arena.OnReady += a => _structures.Apply(a, CrowdLibrary.Load());
            }
            var data = PlaybackData.Load();
            var level = LevelScript.Parse(data.Level(rosterSet).text, data.Overlay(intensity), data.Overlay(breakSchedule));
            if (_flags.Envelope > 0) level.EnvelopeOnScreen = _flags.Envelope;
            if (_flags.ViewWidth > 0 && _flags.Scene == "song") level.ScaleViewWidth(_flags.ViewWidth);
            if (!breakSchedule.IsNone && _flags.Scene != "song") throw new InvalidOperationException($"DemoMain: {BreakSchedule.Flag} breaks the song's structures, not stress scene {_flags.Scene}'s");
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
            // The arena's layout is the environment's (its own placement list); a test layout is its own.
            var layout = layoutName != "arena" && data.Layout(layoutName) is TextAsset other ? other.text : environmentLayout;
            // The timeline navigates the density the arena scene is drawn at (TD15; the cold review's item 3).
            var choreo = _trailer != null ? trailerData[_trailer.Shots[0].Environment]
                : ChoreographyData.FromJson(data.Annotations(rosterSet), rosterSet.Choreography(data.roster.text, _roster), layout, data.footprints.text,
                    _arena != null ? _arena.Density : 0);
            var started = Time.realtimeSinceStartupAsDouble;
            if (_trailer != null) BuildShots(data, rosterSet, trailerData);
            else
                _tl = stress == null
                    ? Choreographer.Run(SongMap.Parse(data.songMap.text), level, choreo)
                    : Choreographer.RunStress(stress, choreo);
            Debug.Log($"DemoMain: {_tl.Name} built in {Time.realtimeSinceStartupAsDouble - started:F2} s, " +
                      $"{_tl.EntityCount} entities; sim ticks {_tl.TickTiming?.ToJson()}");
            _eval = new TimelineEvaluator(_tl, choreo.Clips);
            MapTypes(choreo);
            // --structure-breaks: the pieces the bolts strike shatter on screen, and the moments' feel, bursts and sound.
            var breakMoments = BreakMoments.None;
            if (_tl.Breaks.Length > 0)
            {
                if (_arena == null) throw new InvalidOperationException($"DemoMain: {BreakSchedule.Flag} {breakSchedule.Name} needs the arena, not flat ground");
                breakMoments = BreakMoments.Of(_tl, choreo.Layout);
                _breaks = new Demo3D.Runtime.Arena.StructureShatter(breakMoments, _arena, choreo.Layout, _structures.Name);
            }
            _endAt = _trailer != null ? TrailerEnd() : _flags.Seconds > 0 ? _flags.StartAt + _flags.Seconds : _tl.Duration;

            _crowd = CrowdFactory.Create(_flags.Crowd);
            if (_crowd is BoneTextureCrowd bone)
            {
                bone.Style = _style;
                bone.Clock = () => (float)SongTime; // deterministic LOD crossfades
            }
            else if (!_style.IsBaseline) throw new InvalidOperationException($"DemoMain: {MonsterStyle.Flag} {_style.Name} needs the bone crowd, not --crowd {_flags.Crowd}");
            // Every run publishes the style beat: the hero's blade (HeroBlade) throbs on it under any style.
            _styleBeat = new StyleBeat(_tl.Map ?? SongMap.Synthetic(120, _tl.Duration));
            if (_crowd is BoneTextureCrowd)
                _shatter = CrystalShatter.Create(_tl, _style, CrowdLibrary.Load(), Array.ConvertAll(_typeAssets, t => t.id));
            if (_shatter != null) _shatter.View = _rig.Camera;
            Debug.Log($"DemoMain: {MonsterStyle.Flag} {_style.Name}");
            _crowd.Initialize(Array.ConvertAll(_typeAssets, t => t.id), 16384); // instances per type; the bone path sizes small buffers by it
            _slots = new CrowdSlots(_typeAssets.Length, 16384);
            _heroRenderer = new StockCrowd();
            _heroRenderer.Initialize(new[] { _hero.id }, 1);
            // The hero's crystal blade (HeroBlade), under every style: it refracts the camera's opaque
            // texture, so that is on in every run.
            _heroBlade = new HeroBlade(CrowdLibrary.Load());
            _heroRenderer.Created = _heroBlade.Attach;
            ApplyAntiAliasing(_flags.Aa, _rig.Camera);
            _supersample = Supersampling.Factor(_flags);
            Supersampling.Apply(_supersample);
            if (_supersample != 1f) Debug.Log($"DemoMain: {Supersampling.Flag} {_supersample.ToString(CultureInfo.InvariantCulture)}: rendering at {_supersample.ToString(CultureInfo.InvariantCulture)}x the output, downsampled");
            UnityEngine.Rendering.Universal.CameraExtensions.GetUniversalAdditionalCameraData(_rig.Camera).requiresColorOption =
                UnityEngine.Rendering.Universal.CameraOverrideOption.On;
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
            _gpuProbe = BatchGpuProbe.FromArgs(_rig.Camera);
            var music = _music = gameObject.AddComponent<AudioSource>();
            AudioSource click = null;
            AudioClip clickClip = null;
            if (_flags.Click && _flags.Scene == "song")
            {
                click = gameObject.AddComponent<AudioSource>();
                clickClip = ClickTrack.Build(_tl.Map);
            }
            // --resident: the music starts once every environment is loaded and drawn (Warm).
            _startClock = () => _clock.Start(_flags.Scene == "song" ? music : null, data.music, 0.5, click, clickClip);
            if (_arenas == null) _startClock();
            var blowFeel = BlowFeel.Amounts.FromOverlay(data.Overlay(_trailer != null ? Intensity.Parse(_trailer.Base) : intensity));
            _blows = blowFeel != null && _tl.Map != null && stress == null && !_flags.NoFeel ? new BlowFeel(_tl, blowFeel) : null;
            if (_blows != null)
            {
                if (_fxDriver != null) _fxDriver.Blows = _blows;
                if (_fx != null) _fx.Lights.Knee = _blows.Feel.LightKnee;
                if (_shatter != null) _shatter.Cap = _blows.Feel.ShardCap;
                Debug.Log($"DemoMain: {intensity.Name}'s blow feel over {_blows.Count} blows");
            }
            var breakShakes = breakMoments.Count > 0 ? breakMoments.Shakes() : null;
            var breakFlashes = breakMoments.Count > 0 ? new List<(double, float)>(breakMoments.Flashes()) : null;
            _feel = _tl.Map != null ? new CameraFeel(_tl, _blows, breakShakes) : null;
            _postFeel = _tl.Map != null && _arena != null ? new PostFeel(_tl, _arena.Lighting.PostFeel, !_flags.NoFeel && !_flags.NoPostFeel, breakFlashes) : null;
            if (_postFeel != null && _arenas != null)
            {
                _postFeels = new PostFeel[_arenas.Count];
                _postFeels[0] = _postFeel;
                for (int i = 1; i < _postFeels.Length; i++) _postFeels[i] = new PostFeel(_tl, _arenas[i].Lighting.PostFeel, _postFeel.Enabled, breakFlashes);
            }
            if (_trailer != null) PrepareShots(blowFeel, choreo, trailerData);
            else
            {
                // A crystal death breaks at its killing blow: it plays no death clip, and its burst, light
                // and sound play at the blow, known from the timeline.
                var breaks = _shatter?.Breaks(InstanceScale, type => _hover[type]) ?? Array.Empty<CrystalShatter.BreakEvent>();
                _eval.BreaksAtImpact = breaks.Length > 0 ? new HashSet<int>(Array.ConvertAll(breaks, b => b.Entity)) : null;
                _fxDriver?.SetShatters(Array.ConvertAll(breaks, b => new EffectDriver.Shatter
                {
                    Time = b.Time, Entity = b.Entity, Position = b.Hit, Blow = b.Blow, Scale = b.Height / 1.8f, Seed = b.Seed, Tint = b.Gem,
                }));
                if (breakMoments.Count > 0) _fxDriver?.SetBursts(breakMoments.Bursts(_structures.Name, StructureStyle.CrystalGem));
                var clientSounds = breaks.Length > 0 ? ShatterSounds.For(breaks, _tl.Map) : null;
                if (breakMoments.Count > 0) (clientSounds ??= new List<SoundScheduler.Cue>()).AddRange(breakMoments.Sounds(_structures.Name, _tl.Map));
                _soundCues = SoundScheduler.Cues(_tl, clientSounds);
            }
            if (_flags.DumpSounds != null) DumpSounds(_flags.DumpSounds);
            if (!_flags.FixedTime && _flags.Scene == "song")
            {
                _sounds = new SoundScheduler(_soundCues, _clock, SoundBank.Load(), gameObject, _flags.StartAt);
                if (_trailer != null) _sounds.Envelope = TrailerGain;
            }
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
            _fxDriver = NewEffectDriver(_tl);
        }

        /// <summary>An effect driver for a timeline on the effect runtime, its big types' heavier variants and --only-effects.</summary>
        EffectDriver NewEffectDriver(Timeline tl)
        {
            var driver = new EffectDriver(tl, _fx, t => _typeAssets[t].kind == "elite" || _typeAssets[t].kind == "boss");
            driver.OnlyAmbient = _beautyEmpty;
            driver.OnlyEffects(Args.Get("--only-effects"));
            return driver;
        }

        /// <summary>`--resident a,b,...`: the environment the timeline plays first, then the others, each once.</summary>
        static List<string> ResidentEnvironments(string first, string resident)
        {
            var envs = new List<string> { first };
            foreach (var s in resident.Split(','))
            {
                var e = s.Trim().ToLowerInvariant();
                if (e != "" && !envs.Contains(e)) envs.Add(e);
            }
            return envs;
        }

        /// <summary>
        /// `--resident`: once every environment is set up, one frame per environment draws it
        /// off-screen at the first frame's time, with the horde, the hero and the effects live then,
        /// so no shader or pipeline state compiles after the first frame (trailer.md, Environments
        /// in one player); then the music starts. Returns true once that is done.
        /// </summary>
        bool Warm()
        {
            if (_arenasWarm) return true;
            if (!_arenas.Poll()) return false;
            if (_warmed == 0)
            {
                // Each volume takes the style's grade before its post feel first binds, as the single arena's does.
                for (int i = 0; i < _arenas.Count; i++) PreparePost(_arenas[i]);
                _stylePosted = true;
                _warmTarget = BatchGpuProbe.Target(_rig.Camera, "ArenaWarm");
                _liveTarget = _rig.Camera.targetTexture;
                _rig.Camera.targetTexture = _warmTarget; // a windowed player's own draw goes there too
            }
            if (_warmed < _arenas.Count)
            {
                var watch = System.Diagnostics.Stopwatch.StartNew();
                SwitchArena(_warmed);
                Draw((_flags.FreezeAt >= 0 ? _flags.FreezeAt : _clock.Now()) + _flags.TimeOffset);
                if (Application.isBatchMode) _rig.Camera.Render();
                Debug.Log($"DemoMain: warmed {_arena.Options.Environment} off-screen ({watch.Elapsed.TotalMilliseconds:F0} ms to submit)");
                _warmed++;
                return false;
            }
            _rig.Camera.targetTexture = _liveTarget;
            _warmTarget.Release();
            Destroy(_warmTarget);
            _warmTarget = null;
            _arenasWarm = true;
            _startClock();
            return true;
        }

        /// <summary>`--switch-every s`: the drawn environment cycles through the set every s song seconds from --start-at.</summary>
        int ArenaIndexAt(double t)
        {
            if (_flags.SwitchEvery <= 0) return 0;
            long k = (long)Math.Floor((t - _flags.StartAt) / _flags.SwitchEvery + 1e-6);
            int n = _arenas.Count;
            return (int)((k % n + n) % n);
        }

        void SwitchArena(int i)
        {
            _arena = _arenas.Switch(i);
            if (_postFeels != null) _postFeel = _postFeels[i];
        }

        /// <summary>
        /// An arena's night volume before its post feel first binds: the style's grade, the bloom fitted to the
        /// supersampling, and a trailer's grain and lens dirt.
        /// </summary>
        void PreparePost(Demo3D.Runtime.Arena.ArenaRuntime arena)
        {
            PostStyle(arena);
            Supersampling.FitBloom(arena.Post.sharedProfile, _supersample);
            _look?.Dress(arena.Post.sharedProfile);
        }

        /// <summary>
        /// The style's grade on an arena's night volume, before its post feel first binds (it becomes PostFeel's base):
        /// one look, the monster style's offsets from the forest's over the night's own, else the structure style's over
        /// the night's own (environment.md, Structure styles; trailer.md, The style post fix).
        /// </summary>
        void PostStyle(Demo3D.Runtime.Arena.ArenaRuntime arena)
        {
            var forest = _style.IsBaseline ? default : ForestPost(); // only a monster style moves by the forest's values
            if (!(StructureStyle.PostFor(_style, _structures, PostLook.From(arena.Post.sharedProfile), forest) is PostLook look)) return;
            look.ApplyTo(arena.Post.sharedProfile);
            string by = _style.Post != null ? $"{MonsterStyle.Flag} {_style.Name}" : $"{StructureStyle.Flag} {_structures.Name}";
            Debug.Log($"DemoMain: {by}'s post look on the night volume" + (_arenas != null ? " of " + arena.Options.Environment : ""));
        }

        /// <summary>The forest's night volume values for this run's flags (its palette, roster set and post overrides): the monster styles' reference.</summary>
        PostLook ForestPost()
        {
            if (_forestPost is PostLook post) return post;
            var lighting = Demo3D.Runtime.Arena.ArenaLighting.Parse(PlaybackData.Load().EnvironmentLayout(LayoutEnvironment.Default), Args.Get("--palette"), RosterSet.Active.Name);
            var profile = Demo3D.Runtime.Arena.NightLook.CreateProfile(lighting);
            post = PostLook.From(profile);
            foreach (var c in profile.components) Destroy(c);
            Destroy(profile);
            _forestPost = post;
            return post;
        }

        PostLook? _forestPost;

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
            if (_captureMask) _maskAt = _capture.LastStillAt + _timeBase;
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
        readonly System.Diagnostics.Stopwatch _shatterWatch = new System.Diagnostics.Stopwatch(); // the crystal shatter's physics steps and shard updates
        double _lastWall;

        bool _quitNext;

        void Frame()
        {
            if (_quitNext) { Quit(); return; }
            if (_arenas != null && !Warm()) return;
            _evalWatch.Reset();
            _drawWatch.Reset();
            _shatterWatch.Reset();
            if (_maskOn) MaskMode(false);
            double t = (_flags.FreezeAt >= 0 ? _flags.FreezeAt : _clock.Now()) + _flags.TimeOffset;
            if (!double.IsNaN(_maskAt))
            {
                t = _maskAt; // the still's frame again, as its ID render
                _maskAt = double.NaN;
                MaskMode(true);
                _capture.ForceStill(t - _timeBase, "mask");
            }
            double switchMs = 0;
            if (_arenas != null)
            {
                if (_trailer != null) Cut(ShotAt(t), t);
                else SwitchArena(ArenaIndexAt(t));
                switchMs = _arenas.LastSwitchMs;
                if (switchMs > 0) Debug.Log($"DemoMain: {_arena.Options.Environment} at {t.ToString("F3", CultureInfo.InvariantCulture)} s, switched in {switchMs:F2} ms");
            }
            _sounds?.Update(t);
            if (_trailer == null) Draw(t);
            else if (!Blackout(t)) TrailerDraw(t);

            // Captures and probes take trailer seconds in a trailer (_timeBase), song seconds otherwise.
            _captureAt = _capture != null ? t - _timeBase : double.NaN; // read back once URP has drawn it
            // A batch-mode player draws no frames, so it draws the camera by hand for each still it
            // owes (windowless checks on the Mac). Only those frames: URP's hand-drawn frames hold
            // their pooled targets until a frame the player loop draws (CaptureTool).
            if (Application.isBatchMode && _capture != null && _capture.StillDue(t - _timeBase)) _rig.Camera.Render();
            else _gpuProbe?.Frame(t - _timeBase, _rig.Camera, OnScreenAlive, switchMs);
            WriteStats(t);
            if (_flags.SongCheck != null)
                (_songCheck ??= new SongRunCheck()).Update(t, _fx?.TakeRejected() ?? 0, _fx?.Lights.DropShare ?? 0);
            _lastWall = Time.realtimeSinceStartupAsDouble;
            _clock.EndFrame();
            // Quit on the next frame, so this frame is drawn (and captured) first.
            if (t >= _endAt || (_capture != null && _capture.Done && _flags.CaptureVideo == null && double.IsNaN(_maskAt) && !_maskOn))
                _quitNext = true;
        }

        /// <summary>Poses and submits everything at song time t: the camera, post, effects, the horde and the hero.</summary>
        void Draw(double t)
        {
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
            if (!_stylePosted && _arena?.Post != null)
            {
                // The arena's volume exists once its scene has loaded; the style's grade goes on it before
                // PostFeel first binds, so it becomes PostFeel's base. One look: the horde's, else the structures'.
                _stylePosted = true;
                PreparePost(_arena);
            }
            _postFeel?.Apply(_arena.Post, feelT);
            _look?.Frame(feelT, _rig.Camera);
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

            _shatterWatch.Start();
            _shatter?.Frame(t);
            _breaks?.Frame(t);
            _shatterWatch.Stop();
            _evalWatch.Start();
            // The horde and the hero pose on the world's time: it holds through a heavy blow's hit-stop (BlowFeel).
            double world = Math.Max(_blows?.WorldTime(t) ?? t, _worldFrom);
            // Stable slots: a monster keeps its slot while it lives (motion vectors, the previous
            // palette and LOD state follow the slot); a freed slot is written Hidden first.
            _slots.Begin();
            for (int type = 0; type < _typeLists.Length; type++)
                foreach (int e in _typeLists[type]) _slots.Assign(type, _slotBase + e);
            _slots.End();
            for (int type = 0; type < _typeLists.Length; type++)
            {
                var list = _typeLists[type];
                var inst = _crowd.Instances(type, _slots.Count(type));
                _slots.FillHidden(type, inst);
                for (int k = 0; k < list.Count; k++)
                {
                    int e = list[k];
                    var st = _eval.Evaluate(e, world, _walkStride[type], _walkLength[type], _hover[type]);
                    var mi = ToInstance(st, _typeAssets[type], type, _tl.Entities[e].Seed);
                    // From its killing blow a crystal monster is its shards (CrystalShatter).
                    if (st.Broken && _shatter != null)
                    {
                        _shatterWatch.Start(); // posing the chunks is the shatter's cost too
                        if (_shatter.Breaking(e, type, mi)) mi.Flags |= (uint)CrowdFlags.Hidden;
                        _shatterWatch.Stop();
                    }
                    inst[_slots.Assign(type, _slotBase + e)] = mi;
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

            float heroScale = _blows?.Feel.HeroScale ?? 1f;
            var heroState = _eval.Evaluate(0, world, _heroStride * heroScale, _hero.clips[_heroWalk].length, 0);
            var heroInst = _heroRenderer.Instances(0, 1);
            var heroMi = ToInstance(heroState, _hero, -1, 0);
            heroMi.Scale *= heroScale;
            heroInst[0] = heroMi;
            _heroBlade.Pose(_tl, world);
            if (!_maskOn) _heroRenderer.Draw(_rig.Camera);
            Demo3D.Runtime.Arena.CanopyFade.SetHero(heroState.Position); // TD15: canopies near the hero dither out
            _arena?.FollowHero(heroState.Position);
            _heroFeet = heroState.Position;
            _styleBeat?.Publish(feelT, _heroFeet); // the beat as heard, as the post feel
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
                Scale = InstanceScale(seed),
                HitFlash = st.HitFlash,
                Dissolve = st.Dissolve,
                Tint = 0,
                Flags = (uint)(st.Dying ? CrowdFlags.Dying : CrowdFlags.Alive),
            };
        }

        /// <summary>A monster's drawn scale, from its seed.</summary>
        static float InstanceScale(uint seed) => 0.92f + 0.16f * ClipSchedule.Jitter(seed, 1);

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
                _stats.WriteLine("t,section,onScreenAlive,onScreenDying,target,active,viewWidth,wallFrameMs,evalMs,crowdMs,particles,effectSpawns,lightCues,lightsUsed,lightDropShare,shards,shatterMs");
            }
            _nextStat = t + 0.5;
            int si = _tl.Map != null ? _tl.Map.SectionIndexAt(t) : 0;
            var sec = _tl.Sections != null && si >= 0 && si < _tl.Sections.Length ? _tl.Sections[si] : null;
            double wall = Time.realtimeSinceStartupAsDouble;
            _stats.WriteLine(string.Format(CultureInfo.InvariantCulture, "{0:F2},{1},{2},{3},{4},{5},{6:F1},{7:F1},{8:F2},{9:F2}",
                t, sec?.Label ?? _tl.Name, OnScreenAlive, OnScreenDying, sec?.TargetOnScreen ?? 0, _active.Count,
                _tl.FramingAt(Math.Max(0, t)).ViewWidth, (wall - _lastWall) * 1000, _evalWatch.Elapsed.TotalMilliseconds,
                _drawWatch.Elapsed.TotalMilliseconds) + string.Format(CultureInfo.InvariantCulture, ",{0},{1},{2},{3},{4:F4},{5},{6:F2}",
                _fx?.Particles ?? 0, _fx?.SpawnsTotal ?? 0, _fx?.Lights.ActiveCues ?? 0, _fx?.Lights.LightsUsed ?? 0, _fx?.Lights.DropShare ?? 0,
                _shatter?.Shards ?? 0, _shatterWatch.Elapsed.TotalMilliseconds));
        }

        /// <summary>
        /// The sounds with each one's clip name, gain and pitch, for TD13's offline mix (the same
        /// list the live scheduler plays: the timeline's events and the crystal shatter's).
        /// </summary>
        void DumpSounds(string path)
        {
            var bank = SoundBank.Load();
            var sb = new System.Text.StringBuilder("{\"sounds\": [\n");
            for (int i = 0; i < _soundCues.Length; i++)
            {
                var s = _soundCues[i];
                var clip = bank != null && s.Name != null ? bank.Find(s.Name) : null;
                // (A "}}" right after a format spec would be read into the spec, so the closing
                // brace is its own argument.)
                sb.AppendFormat(CultureInfo.InvariantCulture, "  {{\"time\": {0:F6}, \"sound\": \"{1}\", \"clip\": \"{2}\", \"gain\": {3:F4}, \"pitch\": {4:F4}{5}{6}\n",
                    s.Time, s.Name, clip != null ? clip.name : "", s.Gain, s.Pitch, "}", i + 1 < _soundCues.Length ? "," : "");
            }
            sb.Append("]}\n");
            System.IO.File.WriteAllText(path, sb.ToString());
            Debug.Log($"DemoMain: wrote {_soundCues.Length} sound events to {path} (sound bank {(bank != null ? bank.names.Length + " names" : "missing")})");
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
            _gpuProbe?.Dispose();
            _gpuProbe = null;
            Debug.Log($"DemoMain: done at song time {SongTime.ToString("F2", CultureInfo.InvariantCulture)}" +
                      (_fx != null ? $"; effects: {_fx.SpawnsTotal} spawns, {_fx.MergedTotal} merged (cuts), {_fx.GlowsDropped} glow emitters dropped (cuts)" : "") +
                      (_shatter != null ? $"; crystal shatter: {_shatter.Deaths} deaths, at most {_shatter.PeakShards} shards live" : "") +
                      (_breaks != null ? $"; structure breaks: at most {_breaks.PeakShards} shards live" : ""));
            enabled = false;
            Application.Quit(code);
        }

        void OnDestroy()
        {
            _capture?.Dispose();
            _songCheck?.Dispose();
            _fx?.Dispose();
            _crowd?.Dispose();
            _shatter?.Dispose();
            _breaks?.Dispose();
            _style?.Dispose();
            _structures?.Dispose();
            _heroRenderer?.Dispose();
            _heroBlade?.Dispose();
            if (_arenas != null) _arenas.Dispose();
            else _arena?.Dispose();
            if (_warmTarget != null) _warmTarget.Release();
        }
    }
}
