using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Champions.Client.Loop;
using Champions.Client.Scenes;
using Champions.Client.UI;
using Champions.Render;
using Champions.Render.Crowd;
using Champions.Sim;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Debug = UnityEngine.Debug;

namespace Champions.Client.Bench
{
    /// <summary>
    /// The performance peak (`--perf-peak`; plan.md, Performance; `champions perf` plays it once per tier): level 1 as it
    /// plays, the sim and every presenter through <see cref="RunPresentation"/> over the world's beat wave, held at the
    /// horde's cap. A loud scripted 128 BPM song; the full-build fixture's Storm build (<see cref="Weapons"/> at level 8,
    /// <see cref="Supports"/> at their most: ps-3wc.14's heaviest), its hero walking a 5 m circle round the start and
    /// dashing every 2 s; the second half's mix, every mob type, from the start; the spawner's target at the cap (1,000
    /// alive) and a toughness that lets the horde press in round the hero (<see cref="Toughness"/>); no bosses, no
    /// level-ups and a hero who can't die. So the peak holds: about a third of the horde on screen, kills shattering and
    /// dropping pickups, the weapons' effects firing all the while.
    ///
    /// Frames run as the game runs them, one after another with the GPU's work in flight behind them, as fast as they
    /// come (`--fixed-time` keeps each one 1/60 s of song). In batch mode each frame draws the camera by hand into a 1080p
    /// target with the tier's MSAA (URP draws an offscreen camera with its target's samples, as the stills' does), resolved
    /// as a window's would be, and asks for a 1-pixel readback it never waits on, which ends the frame's command buffer,
    /// so a Metal System Trace (`champions perf` records one) can tell the frames apart. Once the horde has filled and
    /// <see cref="WarmupS"/> have passed it logs "PerfPeak: measuring" and times every frame: the frame (from one to the
    /// next), the sim's step, the presenters (the frame's events and its present) and the camera's submission. At the end
    /// (`--seconds`) it writes the percentiles to `--bench-out PATH` (JSON) and logs them.
    /// </summary>
    public sealed class PerfPeak : IDisposable
    {
        public const double Bpm = 128, SongEnd = 3600, WarmupS = 8;
        /// <summary>
        /// The horde's toughness: against the maximum build, about 16 kills a second while the rest press in round the
        /// hero, a third of the horde on screen (the level's own at the cap, 3.5, has it cleared to a sixth).
        /// </summary>
        public const double Toughness = 100;
        /// <summary>A peak's spawning rate: the cap is reached within seconds.</summary>
        public const double PerS = 250;
        /// <summary>The share of the cap alive from which the horde counts as filled.</summary>
        public const double Filled = 0.98;
        public const string MeasuringLog = "PerfPeak: measuring";
        const double Frame = 1.0 / Boot.FixedFps;

        /// <summary>The peak's build, the full-build fixture's Storm (DuelScene): six weapons at <see cref="WeaponLevel"/>, six supports at their most.</summary>
        public static readonly WeaponKind[] Weapons =
            { WeaponKind.Sword, WeaponKind.Hammer, WeaponKind.Bow, WeaponKind.OrbitBlades, WeaponKind.Lightning, WeaponKind.EmberField };
        public static readonly SupportKind[] Supports =
            { SupportKind.Tempo, SupportKind.Multishot, SupportKind.Pierce, SupportKind.Split, SupportKind.Echo, SupportKind.Persistence };
        public const int WeaponLevel = 8;
        /// <summary>The hero's walk: a target on a circle of this radius round the start, at this angular speed, moved this often; a dash this often.</summary>
        public const double WalkRadiusM = 5, WalkRadPerS = 0.4, RetargetS = 0.2, DashEveryS = 2;

        readonly Camera _camera;
        readonly SimDriver _driver;
        readonly RunPresentation _presentation;
        readonly List<StreamEntry> _entries;
        readonly RenderTexture _target, _resolved;
        readonly string _out;
        readonly int _cap;
        readonly Stopwatch _clock = Stopwatch.StartNew();
        readonly List<double> _frameMs = new List<double>(), _simMs = new List<double>(), _presentMs = new List<double>(),
            _submitMs = new List<double>(), _cpuMs = new List<double>(), _alive = new List<double>(), _onScreen = new List<double>(),
            _shards = new List<double>(), _flights = new List<double>();
        double _frame, _filledAt = double.NaN, _measuredFrom = double.NaN, _lastStart = double.NaN, _nextTarget, _nextDash = DashEveryS;
        int _next, _killsAtStart;

        /// <summary>The peak on Boot's world and run UI (which it disposes), timings to <paramref name="outPath"/> (null: logged only).</summary>
        public PerfPeak(Camera camera, GameContent content, string contentDir, Palette palette, WorldPresenter world, RunUi ui, MonsterLook look, string outPath)
        {
            _camera = camera;
            _out = outPath;
            _entries = new ScriptedSong().Beats(0, SongEnd, Bpm).Loud(0, SongEnd).Entries();
            var config = Config(content);
            _cap = config.Level.Cap;
            _driver = new SimDriver(config, new FrameClock());
            var build = _driver.Sim.State.Build;
            foreach (var kind in Weapons) build.Set(kind, WeaponLevel);
            foreach (var kind in Supports) build.Set(kind, build.Rules.Support(kind).MaxLevel);
            _presentation = new RunPresentation(_driver.Sim, config, content, contentDir, palette, camera, world, ui, null, camera.gameObject, look);
            if (Application.isBatchMode)
            {
                int msaa = GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset urp ? Math.Max(1, urp.msaaSampleCount) : 1;
                _target = new RenderTexture(Stills.Width, Stills.Height, 24, RenderTextureFormat.ARGB32) { antiAliasing = msaa, name = "PerfPeak" };
                _target.Create();
                if (msaa > 1)
                {
                    _resolved = new RenderTexture(Stills.Width, Stills.Height, 0, RenderTextureFormat.ARGB32) { name = "PerfPeak resolved" };
                    _resolved.Create();
                }
            }
            Debug.Log($"PerfPeak: level 1 held at {_cap} alive on a loud {Bpm} BPM song, {string.Join(", ", Weapons)} at {WeaponLevel} with " +
                      $"{string.Join(", ", Supports)}, toughness {Toughness}, the {QualityTiers.Current} tier" + (_target != null ? ", drawn by hand into 1080p" : ""));
        }

        /// <summary>Level 1's config bent into the peak (see the class).</summary>
        public static SimConfig Config(GameContent content)
        {
            var config = new RunInputs { Seed = 22, Level = true, SurvivorWeapons = true, Start = RunStart.Immediately }.Build(content);
            config.Hero.Health = 1e9;
            config.Progression.Levels = new[] { 1e12 };
            var level = config.Level;
            level.Ramp = new Curve((0, level.Cap));
            level.PerS = PerS;
            level.Mix = level.SecondHalfMix;
            level.Toughness = new Curve((0, Toughness));
            level.Bosses = new LevelBoss[0];
            return config;
        }

        /// <summary>One frame: the song's entries in, the hero's walk and dashes, the sim stepped, the presenters, the camera drawn.</summary>
        public void Step()
        {
            double start = _clock.Elapsed.TotalMilliseconds;
            bool measuring = !double.IsNaN(_measuredFrom);
            if (measuring) _frameMs.Add(start - _lastStart);
            _lastStart = start;
            _frame += Frame;
            double needed = _driver.StampNeededFor(FrameClock.FrontierAt(_frame));
            while (_next < _entries.Count && _entries[_next].Stamp <= needed) _driver.AddSettled(_entries[_next++]);
            var sim = _driver.Sim;
            Walk(sim);
            if (!_driver.Step(new ClockReadings { Real = Time.realtimeSinceStartupAsDouble, Frame = _frame })) return;
            double stepped = _clock.Elapsed.TotalMilliseconds;
            _presentation.Drain();
            _presentation.Present(_driver.Times.FrameSong, _driver.Times.Frontier, (float)Frame, _camera);
            double presented = _clock.Elapsed.TotalMilliseconds;
            if (_target != null)
            {
                var previous = _camera.targetTexture;
                _camera.targetTexture = _target;
                try { _camera.Render(); }
                finally { _camera.targetTexture = previous; }
                if (_resolved != null) Graphics.Blit(_target, _resolved);
                AsyncGPUReadback.Request(_resolved != null ? _resolved : _target, 0, 0, 1, 0, 1, 0, 1, TextureFormat.RGBA32);
            }
            double submitted = _clock.Elapsed.TotalMilliseconds;
            if (measuring)
            {
                _simMs.Add(_driver.StepMs);
                _presentMs.Add(presented - stepped);
                _submitMs.Add(submitted - presented);
                _cpuMs.Add(submitted - start);
                _alive.Add(sim.Enemies.Alive);
                _flights.Add(sim.Ranged.Flights.Count);
                if (_presentation.Crowd.View is TrailerCrowd crowd)
                {
                    _onScreen.Add(crowd.Crowd.OnScreen);
                    _shards.Add(crowd.Shatter.LiveShards);
                }
            }
            else if (double.IsNaN(_filledAt) && sim.Enemies.Alive >= Filled * _cap) _filledAt = _frame;
            else if (!double.IsNaN(_filledAt) && _frame >= _filledAt + WarmupS)
            {
                _measuredFrom = _frame;
                _killsAtStart = _presentation.Kills;
                Debug.Log(string.Format(CultureInfo.InvariantCulture, "{0} at song {1:F2} s ({2} alive, filled at {3:F2} s)", MeasuringLog, _frame,
                    sim.Enemies.Alive, _filledAt));
            }
        }

        /// <summary>The full-build fixture's hero (DuelScene): round a small circle, a dash every <see cref="DashEveryS"/>, any card taken.</summary>
        void Walk(Simulation sim)
        {
            if (sim.State.Offer.Up)
            {
                sim.Press(Key.One, sim.Now);
                sim.Release(Key.One, sim.Now);
            }
            if (sim.Now >= _nextTarget)
            {
                sim.Target(WalkRadiusM * Math.Cos(sim.Now * WalkRadPerS), WalkRadiusM * Math.Sin(sim.Now * WalkRadPerS), sim.Now);
                _nextTarget = sim.Now + RetargetS;
            }
            if (sim.Now >= _nextDash)
            {
                sim.Press(Key.Space, sim.Now);
                sim.Release(Key.Space, sim.Now);
                _nextDash += DashEveryS;
            }
        }

        /// <summary>Logs the percentiles and writes `--bench-out`.</summary>
        public void Report()
        {
            var c = CultureInfo.InvariantCulture;
            string F(double v) => double.IsNaN(v) ? "null" : v.ToString("0.000", c);
            string P(List<double> s) => $"{{\"p50\": {F(At(s, 0.5))}, \"p99\": {F(At(s, 0.99))}, \"max\": {F(At(s, 1))}}}";
            double seconds = double.IsNaN(_measuredFrom) ? 0 : _frame - _measuredFrom;
            var sb = new StringBuilder();
            sb.Append("{\n  \"tier\": \"").Append(QualityTiers.Current.ToString().ToLowerInvariant()).Append('"')
                .Append(",\n  \"device\": \"").Append(SystemInfo.graphicsDeviceName).Append('"')
                .Append(",\n  \"cpu\": \"").Append(SystemInfo.processorType).Append('"')
                .Append(",\n  \"backend\": \"").Append(Backend).Append('"')
                .Append(",\n  \"look\": \"").Append(MonsterLooks.Name(LaunchOptions.Current.Look)).Append('"')
                .Append(",\n  \"cap\": ").Append(_cap)
                .Append(",\n  \"toughness\": ").Append(F(Toughness))
                .Append(",\n  \"filledAtS\": ").Append(F(_filledAt))
                .Append(",\n  \"frames\": ").Append(_simMs.Count)
                .Append(",\n  \"seconds\": ").Append(F(seconds))
                .Append(",\n  \"killsPerS\": ").Append(F(seconds > 0 ? (_presentation.Kills - _killsAtStart) / seconds : double.NaN))
                .Append(",\n  \"alive\": ").Append(P(_alive))
                .Append(",\n  \"onScreen\": ").Append(P(_onScreen))
                .Append(",\n  \"liveShards\": ").Append(P(_shards))
                .Append(",\n  \"flights\": ").Append(P(_flights))
                .Append(",\n  \"frameMs\": ").Append(P(_frameMs))
                .Append(",\n  \"cpuMs\": ").Append(P(_cpuMs))
                .Append(",\n  \"simMs\": ").Append(P(_simMs))
                .Append(",\n  \"presentMs\": ").Append(P(_presentMs))
                .Append(",\n  \"submitMs\": ").Append(P(_submitMs)).Append("\n}\n");
            Debug.Log("PerfPeak: " + sb.ToString().Replace("\n", " "));
            if (_out != null) File.WriteAllText(_out, sb.ToString());
        }

        /// <summary>The <paramref name="q"/> quantile of <paramref name="values"/>, nearest rank (NaN if none).</summary>
        static double At(List<double> values, double q)
        {
            if (values.Count == 0) return double.NaN;
            var sorted = values.OrderBy(v => v).ToList();
            return sorted[Math.Min(sorted.Count - 1, (int)Math.Floor(q * sorted.Count))];
        }

#if ENABLE_IL2CPP
        const string Backend = "il2cpp";
#else
        const string Backend = "mono";
#endif

        public void Dispose()
        {
            _presentation.Dispose();
            _presentation.Ui?.Dispose();
            foreach (var t in new[] { _target, _resolved })
            {
                if (t == null) continue;
                t.Release();
                UnityEngine.Object.Destroy(t);
            }
        }
    }
}
