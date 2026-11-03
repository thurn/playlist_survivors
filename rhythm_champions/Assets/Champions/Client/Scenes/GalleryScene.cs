using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using Champions.Render;
using Champions.Sim;
using UnityEngine;
using UnityEngine.Rendering;
using Debug = UnityEngine.Debug;

namespace Champions.Client.Scenes
{
    /// <summary>
    /// `gallery`: the weapon effects' gallery (`champions stills --scene gallery`, or `champions play --gallery`): every
    /// cue (<see cref="EffectCue"/>) and Orbit Blades' ring fired in turn at the hero's feet (the white ring standing in for
    /// it) on the void and its grid, under the game's camera and post, its natural power first, the dashes and Sword Slash
    /// on the beat too. Each entry starts on a clean frame and keeps one still, gallery-LABEL, where its shape reads best
    /// (its head arrived, before the fade): a steady frame, even for a special moment's effect (its impact frames are the
    /// world's flash, not the gallery's). Each entry's draw and
    /// compute cost goes to gallery-costs.json beside the stills; with `--bench-out PATH` the GPU time each adds too
    /// (<see cref="GalleryDriver"/>), there instead.
    /// </summary>
    sealed class GalleryScene : IStillScene, INamedStills
    {
        public const string Name = "gallery";
        /// <summary>Frames between an entry's still and the next entry's start.</summary>
        const double Gap = 2.0 / Boot.FixedFps;

        /// <summary>One entry: a cue (or the orbit, Cue null) fired with its shot at its start, its still that long after.</summary>
        internal readonly struct Entry
        {
            public readonly string Label;
            public readonly EffectCue? Cue;
            public readonly EffectShot Shot;
            public readonly double StillAfter;
            public readonly bool Evolved;

            public Entry(string label, EffectCue? cue, EffectShot shot, double stillAfter, bool evolved = false)
            {
                Label = label;
                Cue = cue;
                Shot = shot;
                StillAfter = stillAfter;
                Evolved = evolved;
            }
        }

        static readonly Vector2 Aim = new Vector2(0.6f, 0.8f);
        static readonly Vector2 DashFrom = new Vector2(-2.4f, -1.8f), DashTo = new Vector2(2.4f, 1.8f);

        static EffectShot At(EffectPower power, double radius = 0, double arc = 0) =>
            new EffectShot { Facing = Aim, Power = power, Radius = (float)radius, ArcDeg = (float)arc };

        static EffectShot Path(EffectPower power, Vector2 from, Vector2 to, double duration, double radius = 0) =>
            new EffectShot { At = from, To = to, Facing = (to - from).normalized, Power = power, Duration = (float)duration, Radius = (float)radius };

        /// <summary>The entries in order: every cue, each at the moment it shows best.</summary>
        static readonly Entry[] LegacyEntries =
        {
            new Entry("sword-slash", EffectCue.SwordSlash, At(EffectPower.Plain, 3, 150), 0.1),
            new Entry("sword-slash-on-beat", EffectCue.SwordSlash, At(EffectPower.OnBeat, 3.8, 210), 0.1),
            new Entry("sword-spin", EffectCue.SwordSpin, At(EffectPower.OnBeat, 4.5), 0.2),
            new Entry("spectral-slash", EffectCue.SpectralSlash, At(EffectPower.Plain, 3, 150), 0.11),
            new Entry("tempest", EffectCue.Tempest, Path(EffectPower.Special, Vector2.zero, Aim * 12, 0.45), 0.2),
            new Entry("orbit-blades", null, At(EffectPower.Plain, 2.5), 0.5),
            new Entry("blade-halo", null, At(EffectPower.Special, 3.5), 0.5, evolved: true),
            new Entry("blade-rain", EffectCue.BladeRain, new EffectShot { At = Aim * 3, Facing = Aim, Radius = 1.5f }, 0.03),
            new Entry("sword-storm", EffectCue.SwordStorm, new EffectShot { Facing = Vector2.up, Power = EffectPower.Special }, 0.05),
            new Entry("crescent", EffectCue.Crescent, Path(EffectPower.Plain, Vector2.zero, Aim * 10, 0.6, 2), 0.25),
            new Entry("full-moon", EffectCue.FullMoon, Path(EffectPower.Special, Vector2.zero, Vector2.zero, 0.6, 14), 0.3),
            new Entry("phantom", EffectCue.Phantom, new EffectShot { At = new Vector2(1.5f, 0.6f), Facing = Aim, Radius = 3, ArcDeg = 150 }, 0.11),
            new Entry("legion", EffectCue.Legion, new EffectShot { Facing = Vector2.up, Power = EffectPower.Special }, 0.15),
            new Entry("dash", EffectCue.Dash, Path(EffectPower.Plain, DashFrom, DashTo, 0.15), 0.17),
            new Entry("dash-on-beat", EffectCue.Dash, Path(EffectPower.OnBeat, DashFrom, DashTo, 0.15), 0.17),
            new Entry("blade-rush", EffectCue.BladeRush, Path(EffectPower.OnBeat, DashFrom, DashTo * 1.04f, 0.22, 2.25), 0.22),
            new Entry("thousand-cuts", EffectCue.ThousandCuts, Path(EffectPower.Special, DashFrom, DashTo * 1.04f, 0.22), 0.3),
            new Entry("leap-slam", EffectCue.LeapSlam, At(EffectPower.OnBeat, 4.5), 0.12),
            new Entry("leap-ring", EffectCue.LeapRing, At(EffectPower.Plain, 7), 0.2),
            new Entry("skyfall", EffectCue.Skyfall, At(EffectPower.Special, 6), 0.14),
            new Entry("shield-charge", EffectCue.ShieldCharge, Path(EffectPower.Plain, DashFrom, DashFrom + (DashTo - DashFrom).normalized * 6, 0.3), 0.2),
            new Entry("shield-bash", EffectCue.ShieldBash, Path(EffectPower.Plain, DashFrom, DashTo, 0.3), 0.06),
            new Entry("juggernaut", EffectCue.Juggernaut, Path(EffectPower.Special, DashFrom * 1.3f, DashTo * 1.3f, 0.4), 0.3),
            new Entry("blink-burst", EffectCue.BlinkBurst, At(EffectPower.OnBeat, 3.75), 0.07),
            new Entry("phase-walk", EffectCue.PhaseWalk, At(EffectPower.Special, 2.5), 0.07),
            new Entry("momentum-trail", EffectCue.MomentumTrail, Path(EffectPower.Plain, DashFrom, DashTo, 0.15), 0.2),
            new Entry("afterimage", EffectCue.Afterimage, Path(EffectPower.Plain, DashFrom, DashTo, 0.15), 0.1),
            new Entry("shatterburst", EffectCue.Shatterburst, new EffectShot { At = Aim * 2.5f, Facing = Aim }, 0.05),
            new Entry("encore", EffectCue.Encore, At(EffectPower.OnBeat), 0.1),
        };

        internal static readonly Entry[] Entries = BuildEntries();
        static Entry[] BuildEntries()
        {
            var entries = new List<Entry>(LegacyEntries);
            string[] names = { "sword-sweep", "sword-cross", "sword-spin-form", "sword-dance", "hammer-swing", "hammer-slam", "hammer-eruption", "hammer-procession" };
            for (int form = 0; form < 8; form++)
                for (int empowered = 0; empowered < 2; empowered++)
                    for (int phase = 0; phase < 7; phase++)
                    {
                        float radius = (form < 4 ? 3 : 2.6f) * (empowered == 1 ? Mathf.Sqrt(1.5f) : 1);
                        var shot = new EffectShot { Facing = Aim, Radius = radius,
                            Advance = form == 7 ? radius * .7f : form == 3 ? radius * 1.5f : 0 };
                        shot.To = Aim * shot.Advance;
                        entries.Add(new Entry(names[form] + (empowered == 1 ? "-empowered" : "-normal") + "-phase" + phase,
                            (EffectCue)((int)EffectCue.MeleeSweep + form), shot, .1 + phase * .075));
                    }
            string[] constructs = { "orbit-circle", "orbit-sweep", "orbit-scythes", "orbit-crown",
                "sentinel-sentry", "sentinel-combo", "sentinel-leap", "sentinel-formation" };
            for (int form = 0; form < 8; form++)
                for (int empowered = 0; empowered < 2; empowered++)
                    for (int phase = 0; phase < 9; phase++)
                    {
                        float radius = (form == 0 ? 2.5f : form < 4 ? 3.4f : 2.3f) * (empowered == 1 ? Mathf.Sqrt(1.5f) : 1);
                        entries.Add(new Entry(constructs[form] + (empowered == 1 ? "-empowered" : "-normal") + "-phase" + phase,
                            (EffectCue)((int)EffectCue.OrbitCircle + form),
                            new EffectShot { Facing = Aim, Radius = radius, To = form >= 4 ? Aim * radius * .6f : Vector2.zero,
                                Duration = 2.4f, Mirrored = form >= 5, Phase = form == 6 ? 2 : form >= 5 ? 1 : 0,
                                Power = empowered == 1 ? EffectPower.OnBeat : EffectPower.Plain },
                            .025 + phase * .075));
                    }
            string[] magic = { "lightning-bolt", "lightning-fork", "lightning-sigil", "lightning-constellation", "ember-seal", "ember-fissure", "ember-brazier", "ember-procession" };
            for (int form = 0; form < 8; form++)
                for (int empowered = 0; empowered < 2; empowered++)
                    for (int phase = 0; phase < 6; phase++)
                    {
                        float scale = empowered == 1 ? Mathf.Sqrt(1.5f) : 1;
                        bool field = form >= 4;
                        var shot = new EffectShot { Facing = Aim, Radius = (field ? 2.4f : 1) * scale,
                            RestRadius = field ? 2.4f : 1, Duration = 3,
                            At = Vector2.zero, To = field ? Aim * 2.4f * scale * .65f : Aim * 2 * scale,
                            Advance = field ? 2.4f * scale * .65f : 0 };
                        entries.Add(new Entry(magic[form] + (empowered == 1 ? "-empowered" : "-normal") + "-phase" + phase,
                            (EffectCue)((int)EffectCue.LightningBolt + form), shot, .025 + phase * .08));
                    }
            entries.Add(new Entry("walking-cleave", EffectCue.WalkingCleave, At(EffectPower.Plain, 4.2), .09));
            entries.Add(new Entry("lightning-link", EffectCue.LightningLink, Path(EffectPower.Plain, Vector2.zero, Aim * 5, .22), .08));
            return entries.ToArray();
        }

        /// <summary>When each entry starts and keeps its still, from the scene's start.</summary>
        internal static (double Start, double Still)[] Schedule()
        {
            var s = new (double, double)[Entries.Length];
            double t = 0.1;
            for (int i = 0; i < Entries.Length; i++)
            {
                s[i] = (t, t + Entries[i].StillAfter);
                t = s[i].Item2 + Gap;
            }
            return s;
        }

        readonly Palette _palette;
        readonly string _contentDir;

        public GalleryScene(Palette palette, string contentDir)
        {
            _palette = palette;
            _contentDir = contentDir;
        }

        public double[] Times => Array.ConvertAll(Schedule(), s => s.Still);

        public string Moment => null;

        public string Label(int index) => Entries[index].Label;

        public void Begin(Transform root, Camera camera, Stills.View view)
        {
            var driver = root.gameObject.AddComponent<GalleryDriver>();
            driver.Begin(camera, _palette, _contentDir, view, loop: false);
            view.Hero = Vector3.zero;
            view.Ids[IdPass.Effect] = ("hero effects", "effect", 1.8f);
        }

        /// <summary>The gallery in a window (`--gallery` without stills): every entry in turn, over and over.</summary>
        public static GalleryDriver Play(Camera camera, Palette palette, string contentDir)
        {
            var go = new GameObject("Gallery");
            var driver = go.AddComponent<GalleryDriver>();
            driver.Begin(camera, palette, contentDir, null, loop: true);
            return driver;
        }
    }

    /// <summary>
    /// Plays <see cref="GalleryScene"/>'s entries each frame from the frame it began, on the scene's own clock: the world
    /// (camera, grid, post) round the hero's spot, the effects presented at the scene's time. On each still's frame it
    /// records the entry's cost (<see cref="Effects.Cost"/>, and what the frame drew) and, under `--bench-out`, times the
    /// frame drawn by hand with and without the effects, alternating, each to the GPU's completion: the median difference
    /// is the effects' cost with the CPU's submission included, an upper bound on their GPU time.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    sealed class GalleryDriver : MonoBehaviour
    {
        const int TimingPairs = 31, WarmPairs = 4;

        Effects _effects;
        ICrowdView _meleeCrowd;
        WorldPresenter _world;
        HeroMarker _marker;
        Champions.Render.Telegraphs.TelegraphRenderer _magicWarning;
        Camera _camera;
        Stills.View _view;
        (double Start, double Still)[] _schedule;
        double _start;
        int _next = -1, _measured;
        bool _loop;
        string _costsDir, _benchOut;
        RenderTexture _timingTarget;
        readonly StringBuilder _costs = new StringBuilder();

        public void Begin(Camera camera, Palette palette, string contentDir, Stills.View view, bool loop)
        {
            _camera = camera;
            _view = view;
            _loop = loop;
            _start = Time.timeAsDouble;
            _schedule = GalleryScene.Schedule();
            _world = new WorldPresenter(camera, palette);
            var enemies = GameContent.Load(contentDir).Enemies;
            _meleeCrowd = (ICrowdView)TrailerCrowd.Create(LaunchOptions.Current.Look, enemies, palette)
                ?? new StandInCrowd(enemies, palette, camera.gameObject.layer);
            if (view != null) view.Ids[IdPass.Monster(0)] = ("skeleton", "monster", 1.8f);
            _magicWarning = new Champions.Render.Telegraphs.TelegraphRenderer(camera.gameObject.layer);
            _magicWarning.Add(1, HitShape.Path(-5, -1, 5, -1, .28), 0, 100000, palette.BossTelegraph.linear * 2.5f);
            _marker = new HeroMarker(camera.gameObject.layer);
            _marker.Place(Vector2.zero);
            var lib = EffectLibrary.Load();
            if (lib == null)
            {
                Debug.LogError("Gallery: no effect library (the build bakes it from the vendor files: run `champions setup`, then build)");
                return;
            }
            _effects = new Effects(lib, EffectRecipes.Load(contentDir), palette);
            if (view != null)
            {
                view.Requeue = () => { _effects.Queue(_camera); if (IsMelee) { _meleeCrowd.Queue(_camera); _magicWarning.Queue(_camera); } };
                _costsDir = LaunchOptions.Current.StillsDir;
                _benchOut = LaunchOptions.Current.BenchOut;
            }
            Debug.Log($"Gallery: {GalleryScene.Entries.Length} entries over {_schedule[_schedule.Length - 1].Still:F2} s{(loop ? ", looping" : "")}");
        }

        void Update()
        {
            if (_effects == null) return;
            double t = Time.timeAsDouble - _start;
            double cycle = _schedule[_schedule.Length - 1].Still + 0.5;
            if (_loop && t >= cycle)
            {
                _start += cycle;
                t -= cycle;
                _next = -1;
            }
            while (_next + 1 < _schedule.Length && t + 1e-6 >= _schedule[_next + 1].Start) Fire(++_next);
            _world.Present(t, Vector2.zero, Time.deltaTime);
            _effects.Present(t, Vector2.zero, Time.deltaTime, _camera);
            if (IsMelee)
            {
                _magicWarning.Present(t, _camera);
                _meleeCrowd.Begin(t);
                for (int k = 0; k < 22; k++)
                {
                    float a = k * 2.39996f, r = 3.4f + (k % 3) * 1.5f;
                    _meleeCrowd.Mob(new CrowdMob { Id = k, Serial = k, Cast = 0,
                        Position = new Vector3(Mathf.Cos(a) * r, 0, Mathf.Sin(a) * r), Action = CrowdAction.Idle,
                        ActionTime = (float)t, StepAge = CrowdMob.Long, KickAge = CrowdMob.Long });
                }
                _meleeCrowd.Draw(t, _camera);
            }
            if (_view != null && _next >= 0 && _measured == _next && t + 1e-6 >= _schedule[_next].Still)
            {
                Measure(_next);
                _measured++;
            }
        }

        bool IsMelee => _next >= 0 && GalleryScene.Entries[_next].Cue is EffectCue cue && (MeleeVisuals.Handles(cue) || ConstructVisuals.Handles(cue) || MagicVisuals.Handles(cue));

        void Fire(int i)
        {
            var e = GalleryScene.Entries[i];
            _effects.Clear();
            double t = _schedule[i].Start;
            var shot = e.Shot;
            shot.T = t + (e.Cue is EffectCue melee && MeleeVisuals.Handles(melee) ? .35 : 0);
            if (e.Cue is EffectCue cue)
            {
                _effects.Fire(cue, shot);
                if (cue >= EffectCue.LightningBolt && cue <= EffectCue.StormConstellation)
                {
                    int count = cue == EffectCue.LightningBolt ? 1 : cue == EffectCue.LightningFork ? 2 : 3;
                    var origin = cue >= EffectCue.StormSigil ? shot.To : shot.At;
                    for (int k = 0; k < count; k++)
                        _effects.Fire(EffectCue.LightningLink, new EffectShot { T = t, At = origin,
                            To = new Vector2(2.7f + k * 1.1f, 1.4f - k * 1.8f) * shot.Radius });
                }
            }
            else _effects.Orbit(t - 0.5, e.Evolved ? 6 : 3, 0.4, 2 * Math.PI / 2.0, shot.Radius, e.Evolved);
            Debug.Log(string.Format(CultureInfo.InvariantCulture, "Gallery: {0} at {1:F3} s, its still at {2:F3} s", e.Label, t, _schedule[i].Still));
        }

        /// <summary>The entry's cost on its still's frame (before the still is kept: Boot keeps it after this Update).</summary>
        void Measure(int i)
        {
            var e = GalleryScene.Entries[i];
            var cost = e.Cue is EffectCue cue ? _effects.Cost(cue, e.Shot.Power) : default;
            var c = CultureInfo.InvariantCulture;
            _costs.Append(_costs.Length == 0 ? "[\n" : ",\n")
                .AppendFormat(c, "  {{\"label\": \"{0}\", \"cue\": \"{1}\", \"power\": \"{2}\", \"draws\": {3}, \"shapes\": {4}, \"triangles\": {5}, " +
                                 "\"sparks\": {6}, \"computeGroups\": {7}, \"frameDraws\": {8}, \"frameShapes\": {9}, \"frameSparks\": {10}",
                    e.Label, e.Cue?.ToString() ?? "Orbit", e.Shot.Power, cost.Draws, cost.Shapes, cost.Triangles, cost.Sparks,
                    cost.ComputeGroups, _effects.Draws, _effects.LiveShapes, _effects.LiveSparks);
            if (_benchOut != null) _costs.AppendFormat(c, ", \"gpuMs\": {0:F3}", TimeFrame());
            _costs.Append('}');
            // The timing's renders spent the queued draws: queue them again for the still.
            _effects.Queue(_camera);
            if (i == GalleryScene.Entries.Length - 1) WriteCosts();
        }

        /// <summary>The median, over <see cref="TimingPairs"/> pairs, of a hand-drawn frame's time to the GPU's completion with the effects less without.</summary>
        double TimeFrame()
        {
            if (_timingTarget == null)
            {
                _timingTarget = new RenderTexture(Stills.Width, Stills.Height, 24, RenderTextureFormat.ARGB32) { name = "Gallery timing" };
                _timingTarget.Create();
            }
            var with = new List<double>();
            var without = new List<double>();
            var previous = _camera.targetTexture;
            _camera.targetTexture = _timingTarget;
            try
            {
                for (int k = 0; k < WarmPairs; k++) // the first draws of a shader or buffer pay its setup
                {
                    Draw(true);
                    Draw(false);
                }
                for (int k = 0; k < TimingPairs; k++)
                {
                    with.Add(Draw(true));
                    without.Add(Draw(false));
                }
            }
            finally { _camera.targetTexture = previous; }
            with.Sort();
            without.Sort();
            return with[with.Count / 2] - without[without.Count / 2];
        }

        double Draw(bool effects)
        {
            var watch = Stopwatch.StartNew();
            if (effects) _effects.Queue(_camera);
            _camera.Render();
            var req = AsyncGPUReadback.Request(_timingTarget, 0, 0, 1, 0, 1, 0, 1, TextureFormat.RGBA32);
            req.WaitForCompletion();
            return watch.Elapsed.TotalMilliseconds;
        }

        void WriteCosts()
        {
            string json = _costs.Append("\n]\n").ToString();
            foreach (var path in new[] { _costsDir != null ? Path.Combine(_costsDir, "gallery-costs.json") : null, _benchOut })
            {
                if (path == null) continue;
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
                File.WriteAllText(path, json);
                Debug.Log("Gallery: costs -> " + path);
            }
        }

        void OnDestroy()
        {
            _effects?.Dispose();
            _meleeCrowd?.Dispose();
            _magicWarning?.Dispose();
            _marker?.Dispose();
            _world?.Dispose();
            if (_timingTarget != null) _timingTarget.Release();
        }
    }
}
