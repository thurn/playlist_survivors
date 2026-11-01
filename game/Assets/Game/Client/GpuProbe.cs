using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Game.Client.Loop;
using PS.Game.Sim;
using PS.Game.Sim.Content;
using PS.Look.Effects;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Stopwatch = System.Diagnostics.Stopwatch;
using PS.Look.Crowd;

namespace Game.Client
{
    /// <summary>
    /// `--gpu-probe-at t1,t2,... --gpu-probe-out FILE.json` (batch mode, for windowless checks on the
    /// Mac; demo3d's BatchGpuProbe, docs/tech-demo-3d/mac.md): a batch-mode player draws no frames, so
    /// at each song time it hand-draws the camera for a few consecutive frames into a 1080p target with
    /// the game's MSAA (<see cref="Stills.Target"/>), and times each draw through GPU completion (an
    /// AsyncGPUReadback waited on). That is one frame's CPU submission and GPU work in series, an upper
    /// bound on the frame a windowed player pipelines; the HUD's panel isn't in it. Each time's first
    /// frame is a warm-up and isn't counted, but is kept as the point's `firstMs`. Only a handful of times:
    /// hand-drawn frames hold URP's pooled targets until a frame the player loop draws. It writes the JSON
    /// after each time's frames.
    ///
    /// Each frame also records its CPU share: the camera's Render call, before the wait for the GPU.
    ///
    /// With `--gpu-probe-ablate` each time's frames cycle, for <see cref="AblateRounds"/> rounds, through
    /// the full frame and the frame with one pass turned off (<see cref="Ablations"/>), so each pass's
    /// saving is measured against full frames drawn beside it, under the same load; each kind's first
    /// frame is its warm-up. The JSON's stats are the full frames'; `ablations` has each pass's median,
    /// its saving against them, and its frames beside the full frame drawn just before each (`pairs`):
    /// under a loaded machine's bursts a pass's cost shows best in its low percentiles and its pairs.
    ///
    /// `--gpu-probe-without PASS,...` turns those passes off in every probe frame: for a moment too
    /// short for the cycle, such as a mass kill's first frames, compared with a run that keeps them, since
    /// a fixed-time run reaches the same frames each time.
    ///
    /// `--gpu-probe-firsts` adds a point at the run's first frame and at the first sighting of each kind of
    /// moment (<see cref="Firsts"/>: the first heavy strike, each role's first death, each weapon's first hit,
    /// the first break and regrowth, chest, level-up, each zone's first change...), <see cref="FirstFrames"/>
    /// full frames each, so its `firstMs` is the frame that first draws it: what a first-use hitch (a shader
    /// variant's compile, a pool's fill) costs. A batch-mode player draws nothing else, so whatever a point first
    /// shows compiles in its first frame.
    /// </summary>
    public sealed class GpuProbe : IDisposable
    {
        /// <summary>Frames drawn at each time: a warm-up, then the counted ones; at a first sighting, fewer.</summary>
        public const int Frames = 9, FirstFrames = 3;
        /// <summary>Rounds of every frame kind at each time with `--gpu-probe-ablate`: a probe frame varies by 2x under load, so more.</summary>
        public const int AblateRounds = 25;

        /// <summary>The passes `--gpu-probe-ablate` turns off, one at a time: what each costs in the frame.</summary>
        public static readonly string[] Ablations = { "ssao", "post", "bloom", "shadows", "opaque-texture", "msaa", "crowd", "effects", "effect-lights" };

        readonly List<double> _at = new List<double>();
        // Each point: its song time, its first sightings (null for a time asked for alone) and its warm-up frame's draw.
        readonly List<(double At, string Label, double FirstMs)> _points = new List<(double, string, double)>();
        readonly List<(int point, double t, int alive, double ms, double cpuMs, int kind)> _samples = new List<(int, double, int, double, double, int)>();
        readonly string _out;
        // The JSON's points, each made once (Write), and the file's text, kept for the next write.
        readonly List<string> _pointLines = new List<string>();
        readonly StringBuilder _json = new StringBuilder();
        char[] _chars = new char[0];
        readonly RenderTexture _target;
        RenderTexture _single;
        // Frame kinds: 0 the full frame, then one per ablation.
        readonly int _kinds, _rounds;
        // `--gpu-probe-without`: off in every frame.
        readonly string[] _without;
        int _next, _left, _pointKinds, _pointFrames;

        /// <summary>The first sightings to probe (`--gpu-probe-firsts`), to subscribe to the events; else null.</summary>
        public Firsts Sightings { get; }

        GpuProbe(string at, bool firsts, GameContent content, string outPath, bool ablate, string without)
        {
            _without = without?.Split(',') ?? new string[0];
            foreach (var pass in _without)
                if (!Ablations.Contains(pass)) throw new ArgumentException($"GpuProbe: no pass {pass} (one of {string.Join(", ", Ablations)})");
            _kinds = ablate ? 1 + Ablations.Length : 1;
            _rounds = ablate ? AblateRounds : Frames;
            if (at != null)
                foreach (var s in at.Split(','))
                    _at.Add(double.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture));
            _at.Sort();
            if (firsts) Sightings = new Firsts(content);
            _out = outPath;
            _target = Stills.Target("GpuProbe");
        }

        /// <summary>The probe the launch asks for, or null: none asked, or not in batch mode, or no graphics device (`--headless`).</summary>
        public static GpuProbe FromOptions(LaunchOptions options, GameContent content)
        {
            if (options.GpuProbeAt == null && !options.GpuProbeFirsts) return null;
            if (!Application.isBatchMode || SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
            {
                Debug.LogWarning("GpuProbe: --gpu-probe-at and --gpu-probe-firsts need batch mode with a graphics device (not --headless)");
                return null;
            }
            return new GpuProbe(options.GpuProbeAt, options.GpuProbeFirsts, content,
                options.Outputs.GpuProbe ?? Path.Combine(Application.persistentDataPath, "gpu-probe.json"), options.GpuProbeAblate,
                options.GpuProbeWithout);
        }

        /// <summary>
        /// After the frame's presenters; draws and times the camera when a probe is due, at song time
        /// <paramref name="t"/> with <paramref name="alive"/> enemies.
        /// </summary>
        public void Frame(double t, Camera camera, int alive)
        {
            if (_left == 0)
            {
                bool timed = _next < _at.Count && t >= _at[_next];
                string firsts = Sightings?.TakeDue(t);
                if (!timed && firsts == null) return;
                double at = timed ? _at[_next++] : t;
                _points.Add((at, timed ? (firsts != null ? $"at {at:0.###} s, {firsts}" : null) : firsts, double.NaN));
                // A first sighting's point draws only full frames.
                _pointKinds = timed ? _kinds : 1;
                _left = _pointFrames = (timed ? _rounds : FirstFrames) * _pointKinds;
            }
            Draw(t, camera, alive);
        }

        // Apart from Frame: its lambdas capture the camera, and a method whose lambda captures a parameter
        // allocates the capture on entry, so Frame would make garbage every frame of a run, not only a probe's.
        void Draw(double t, Camera camera, int alive)
        {
            int point = _points.Count - 1, drawn = _pointFrames - _left, kind = drawn % _pointKinds;
            var off = kind == 0 ? _without : _without.Append(Ablations[kind - 1]).ToArray();
            var target = off.Contains("msaa") ? Single() : _target;
            var restores = off.Select(pass => Ablate(pass, camera)).ToList();
            var previous = camera.targetTexture;
            camera.targetTexture = target;
            long start, submitted, done;
            try
            {
                start = Stopwatch.GetTimestamp();
                camera.Render();
                submitted = Stopwatch.GetTimestamp();
                AsyncGPUReadback.Request(target, 0, 0, 1, 0, 1, 0, 1).WaitForCompletion();
                done = Stopwatch.GetTimestamp();
            }
            finally
            {
                // A throw mustn't leave the camera on the probe's target or a pass off for the rest of the run.
                camera.targetTexture = previous;
                restores.ForEach(restore => restore?.Invoke());
            }
            double ms = (done - start) * 1000.0 / Stopwatch.Frequency;
            if (drawn == 0) _points[point] = (_points[point].At, _points[point].Label, ms);
            if (drawn >= _pointKinds) _samples.Add((point, t, alive, ms, (submitted - start) * 1000.0 / Stopwatch.Frequency, kind));
            if (--_left == 0) Write();
        }

        /// <summary>A 1080p target without MSAA, for the "msaa" ablation.</summary>
        RenderTexture Single()
        {
            if (_single == null)
            {
                _single = new RenderTexture(Stills.Width, Stills.Height, 24, RenderTextureFormat.ARGB32) { name = "GpuProbe1x" };
                _single.Create();
            }
            return _single;
        }

        /// <summary>Turns <paramref name="pass"/> off for <paramref name="camera"/>'s next draw; returns what turns it back on.</summary>
        static Action Ablate(string pass, Camera camera)
        {
            var data = camera.GetComponent<UniversalAdditionalCameraData>();
            switch (pass)
            {
                case "ssao":
                    var features = GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset urp
                        ? urp.rendererDataList.ToArray().SelectMany(d => d.rendererFeatures).OfType<ScreenSpaceAmbientOcclusion>().Where(f => f.isActive).ToList()
                        : new List<ScreenSpaceAmbientOcclusion>();
                    features.ForEach(f => f.SetActive(false));
                    return () => features.ForEach(f => f.SetActive(true));
                case "post":
                    bool post = data.renderPostProcessing;
                    data.renderPostProcessing = false;
                    return () => data.renderPostProcessing = post;
                case "bloom":
                    var blooms = VolumeManager.instance.GetVolumes(~0)
                        .Select(v => v.sharedProfile != null && v.sharedProfile.TryGet<Bloom>(out var b) ? b : null)
                        .Where(b => b != null && b.active).ToList();
                    blooms.ForEach(b => b.active = false);
                    return () => blooms.ForEach(b => b.active = true);
                case "shadows":
                    bool shadows = data.renderShadows;
                    data.renderShadows = false;
                    return () => data.renderShadows = shadows;
                case "opaque-texture":
                    var color = data.requiresColorOption;
                    data.requiresColorOption = CameraOverrideOption.Off;
                    return () => data.requiresColorOption = color;
                case "msaa":
                    return null; // the 1x target (Single)
                case "crowd": // the monsters and their shatter's shards
                    int mask = camera.cullingMask;
                    camera.cullingMask &= ~(1 << BoneTextureCrowd.Layer);
                    return () => camera.cullingMask = mask;
                case "effects": // the particle pool
                    EffectSystem.Hidden = true;
                    return () => EffectSystem.Hidden = false;
                case "effect-lights": // the effects' pooled point lights
                    var root = GameObject.Find("EffectLights");
                    var lit = root == null ? new List<Light>() : root.GetComponentsInChildren<Light>().Where(l => l.enabled).ToList();
                    lit.ForEach(l => l.enabled = false);
                    return () => lit.ForEach(l => l.enabled = true);
                default:
                    throw new ArgumentException($"GpuProbe: no ablation {pass}");
            }
        }

        void Write()
        {
            var ci = CultureInfo.InvariantCulture;
            var full = _samples.Where(s => s.kind == 0).ToList();
            var all = full.Select(s => s.ms).OrderBy(x => x).ToList();
            var cpu = full.Select(s => s.cpuMs).OrderBy(x => x).ToList();
            var sb = _json.Clear().Append("{\n");
            sb.AppendFormat(ci, "  \"graphicsDeviceName\": \"{0}\",\n  \"size\": [{1}, {2}],\n  \"msaa\": {3},\n",
                SystemInfo.graphicsDeviceName, Stills.Width, Stills.Height, _target.antiAliasing);
            if (_without.Length > 0) sb.AppendFormat(ci, "  \"without\": [{0}],\n", string.Join(", ", _without.Select(w => $"\"{w}\"")));
            sb.AppendFormat(ci, "  \"args\": \"{0}\",\n", Escape(string.Join(" ", Environment.GetCommandLineArgs().Skip(1))));
            sb.AppendFormat(ci, "  \"frames\": {0},\n  \"p50Ms\": {1:F2},\n  \"p99Ms\": {2:F2},\n  \"maxMs\": {3:F2},\n  \"firstMaxMs\": {4:F2},\n",
                all.Count, Pct(all, 0.5), Pct(all, 0.99), all[all.Count - 1], _points.Max(p => p.FirstMs));
            // The CPU's share: the Render call, before the wait for the GPU.
            sb.AppendFormat(ci, "  \"cpuP50Ms\": {0:F2},\n  \"cpuP99Ms\": {1:F2},\n", Pct(cpu, 0.5), Pct(cpu, 0.99));
            if (_kinds > 1)
            {
                // Each pass off: its frames' median, and its saving against the full frames' median.
                sb.Append("  \"ablations\": [");
                for (int k = 1; k < _kinds; k++)
                {
                    var ms = _samples.Where(s => s.kind == k).Select(s => s.ms).OrderBy(x => x).ToList();
                    var cpuK = _samples.Where(s => s.kind == k).Select(s => s.cpuMs).OrderBy(x => x).ToList();
                    // Each frame of this kind with the full frame drawn k frames before it, in the same round.
                    var pairs = _samples.Select((s, i) => (s, i)).Where(x => x.s.kind == k && x.i >= k && _samples[x.i - k].kind == 0)
                        .Select(x => string.Format(ci, "[{0:F2}, {1:F2}]", _samples[x.i - k].ms, x.s.ms));
                    sb.Append(k > 1 ? ",\n    " : "\n    ").AppendFormat(ci, "{{\"name\": \"{0}\", \"medianMs\": {1:F2}, \"savedMs\": {2:F2}, \"cpuSavedMs\": {3:F2}, \"pairs\": [{4}]",
                        Ablations[k - 1], Pct(ms, 0.5), Pct(all, 0.5) - Pct(ms, 0.5), Pct(cpu, 0.5) - Pct(cpuK, 0.5), string.Join(", ", pairs)).Append('}');
                }
                sb.Append("],\n");
            }
            // Each point's line is made once, as its frames end: rewriting every point's after each one made a run with
            // a hundred first sightings allocate more than its frames did, and collections the profile counts.
            int done = _points.Count - 1;
            var frames = full.Where(s => s.point == done).ToList();
            var doneMs = frames.Select(s => s.ms).OrderBy(x => x).ToList();
            // frames: each counted frame in order, [song time, alive, draw ms, its CPU ms]; firstMs the warm-up frame's draw.
            _pointLines.Add(string.Format(ci, "    {{\"at\": {0:F3}, {1}\"alive\": {2}, \"firstMs\": {3:F2}, \"medianMs\": {4:F2}, \"maxMs\": {5:F2}, \"frames\": [{6}]}}",
                _points[done].At, _points[done].Label != null ? $"\"label\": \"{Escape(_points[done].Label)}\", " : "",
                frames.Max(s => s.alive), _points[done].FirstMs, Pct(doneMs, 0.5), doneMs[doneMs.Count - 1],
                string.Join(", ", frames.Select(s => string.Format(ci, "[{0:F4}, {1}, {2:F2}, {3:F2}]", s.t, s.alive, s.ms, s.cpuMs)))));
            sb.Append("  \"points\": [\n");
            for (int i = 0; i < _pointLines.Count; i++) sb.Append(_pointLines[i]).Append(i + 1 < _pointLines.Count ? ",\n" : "\n");
            sb.Append("  ]\n}\n");
            // Through a buffer kept from write to write, not a string of the whole file each time.
            if (_chars.Length < sb.Length) _chars = new char[2 * sb.Length];
            sb.CopyTo(0, _chars, 0, sb.Length);
            using (var file = new StreamWriter(_out)) file.Write(_chars, 0, sb.Length);
            var last = _points[done];
            Debug.Log(string.Format(ci, "GpuProbe: {0} frames at {1:F3} s{2}, first {3:F2} ms, median {4:F2} ms, max {5:F2} ms -> {6}",
                doneMs.Count, last.At, last.Label != null ? $" ({last.Label})" : "", last.FirstMs, Pct(doneMs, 0.5), doneMs[doneMs.Count - 1], _out));
        }

        static string Escape(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");

        static double Pct(List<double> sorted, double p) => sorted[(int)Math.Round(p * (sorted.Count - 1))];

        public void Dispose()
        {
            if (_target != null) _target.Release();
            if (_single != null) _single.Release();
        }

        /// <summary>
        /// The first sighting of each kind of moment the camera can show, by the events: the hero's first
        /// strike of each weapon (a heavy one apart), each enemy attack's, each weapon's first hit, bolt,
        /// projectile, orbit and burn, the first hit on the hero, each role's first spawn, death and leave,
        /// each zone's first change, each set piece's, and the first structure break, regrowth, glint,
        /// cleave, drop of each kind, gem flight, pickup, chest phase, level-up, card and the finale. Each
        /// is due once frameSong reaches its event's time.
        /// </summary>
        public sealed class Firsts : IPresenter
        {
            readonly GameContent _content;
            readonly HashSet<long> _seen = new HashSet<long>();
            readonly List<(double T, long Key)> _due = new List<(double, long)>();
            bool _drawn;

            /// <summary>The run's first frame: every upload and state the arena and the hero need, before any moment.</summary>
            const long FirstFrame = -2;

            public Firsts(GameContent content) => _content = content;

            public void OnEvent(in SimEvent e)
            {
                long key = Key(e);
                if (key >= 0 && _seen.Add(key)) _due.Add((e.T, key));
            }

            public void Present(in PresentFrame frame)
            {
                if (_drawn) return;
                _drawn = true;
                _due.Add((frame.Song, FirstFrame));
            }

            /// <summary>The sightings due by song time <paramref name="t"/>, named and taken; null if none.</summary>
            public string TakeDue(double t)
            {
                List<string> names = null;
                for (int i = 0; i < _due.Count; i++)
                {
                    if (_due[i].T > t) continue;
                    (names ??= new List<string>()).Add(Name(_due[i].Key));
                    _due.RemoveAt(i--);
                }
                return names == null ? null : string.Join(", ", names);
            }

            /// <summary>The kind of moment an event shows, as its kind and a detail; -1 for an event with nothing new to draw.</summary>
            static long Key(in SimEvent e)
            {
                long detail;
                switch (e.Kind)
                {
                    case SimEventKind.Attack:
                        var a = e.AsAttack;
                        detail = a.ByHero ? 2 * a.Weapon + (a.Heavy ? 1 : 0) : -1 - (int)a.EnemyKind;
                        break;
                    case SimEventKind.Hit: detail = e.AsHit.OnHero ? -2 : e.AsHit.Weapon; break;
                    case SimEventKind.Death: detail = e.AsDeath.Role; break;
                    case SimEventKind.Spawn: detail = 2 * e.AsSpawn.Role + (e.AsSpawn.Boss ? 1 : 0); break;
                    case SimEventKind.Leave: detail = e.AsLeave.Role; break;
                    case SimEventKind.ZoneChange: detail = e.AsZoneChange.Zone; break;
                    case SimEventKind.SetPiece: detail = (int)e.AsSetPiece.Kind; break;
                    case SimEventKind.Bolt: detail = 8 * e.AsBolt.Weapon + (int)e.AsBolt.Kind; break;
                    case SimEventKind.Projectile: detail = e.AsProjectile.Weapon; break;
                    case SimEventKind.Orbit: detail = e.AsOrbit.Weapon; break;
                    case SimEventKind.Burn: detail = e.AsBurn.Weapon; break;
                    case SimEventKind.Drop: detail = (int)e.AsDrop.ItemKind; break;
                    case SimEventKind.Chest: detail = (int)e.AsChest.Phase; break;
                    case SimEventKind.StructureBreak: case SimEventKind.Regrowth: case SimEventKind.Glint: case SimEventKind.Cleave:
                    case SimEventKind.GemFlight: case SimEventKind.Pickup: case SimEventKind.LevelUp: case SimEventKind.Card:
                    case SimEventKind.Finale:
                        detail = 0;
                        break;
                    default: return -1;
                }
                return ((long)e.Kind << 32) | (uint)(int)detail;
            }

            string Name(long key)
            {
                if (key == FirstFrame) return "first frame";
                var kind = (SimEventKind)(key >> 32);
                int detail = (int)(uint)key;
                string Weapon(int w) => w >= 0 && _content != null && w < _content.Weapons.Length ? _content.Weapons[w].Id : w < 0 ? "blast" : $"weapon {w}";
                string Role(int r) => _content != null && r >= 0 && r < _content.Roles.Length ? _content.Roles[r].Id : $"role {r}";
                switch (kind)
                {
                    case SimEventKind.Attack:
                        return detail >= 0 ? $"attack {Weapon(detail / 2)}{(detail % 2 == 1 ? " heavy" : "")}" : $"enemy {(EnemyAttackKind)(-1 - detail)}";
                    case SimEventKind.Hit: return detail == -2 ? "hit on the hero" : $"hit {Weapon(detail)}";
                    case SimEventKind.Death: return $"death {Role(detail)}";
                    case SimEventKind.Spawn: return $"spawn {Role(detail / 2)}{(detail % 2 == 1 ? " boss" : "")}";
                    case SimEventKind.Leave: return $"leave {Role(detail)}";
                    case SimEventKind.ZoneChange: return $"zone {detail}";
                    case SimEventKind.SetPiece: return $"set piece {(SetPieceKind)detail}";
                    case SimEventKind.Bolt: return $"bolt {Weapon(detail / 8)} {(BoltKind)(detail % 8)}";
                    case SimEventKind.Projectile: return $"projectile {Weapon(detail)}";
                    case SimEventKind.Orbit: return $"orbit {Weapon(detail)}";
                    case SimEventKind.Burn: return $"burn {Weapon(detail)}";
                    case SimEventKind.Drop: return $"drop {(ItemKind)detail}";
                    case SimEventKind.Chest: return $"chest {(ChestPhase)detail}";
                    default: return kind.ToString();
                }
            }
        }
    }
}
