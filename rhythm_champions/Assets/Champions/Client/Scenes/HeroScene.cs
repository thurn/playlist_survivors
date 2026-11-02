using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Champions.Client.Hero;
using Champions.Client.Loop;
using Champions.Render;
using Champions.Render.Crowd;
using Champions.Sim;
using UnityEngine;

namespace Champions.Client.Scenes
{
    /// <summary>
    /// `hero`: the hero's scripted scene (`champions stills --scene hero`; ps-6y2.20, ps-6y2.29). The knight in the edge look
    /// in white with its crystal blade, posed by its presenter (<see cref="HeroPresenter"/>), under the game's camera, grid
    /// and post (`--look trailer`: exactly as the trailer drew it):
    /// - <b>Sword Slash at three tempos</b> (<see cref="Tempos"/>): for each, the real sim plays a scripted song from 0
    ///   with three dummies east of the hero, and its events go to the hero (a scene-local dispatch: moves, swings), the
    ///   world (beat rings) and the weapon effects (each strike's slash). The hero stands for the first
    ///   <see cref="WalkFrom"/> seconds, then runs round the dummies (a click target led round a circle), so its legs
    ///   run under swings twisted to the aim. Rank 1 at 80 BPM; rank 3 (every fourth swing a spin) at 128 and 174.
    ///   Every impact keeps a still on its frame (TEMPO-swingN), and a few swings per tempo a filmstrip round it
    ///   (TEMPO-swingN-OFFSET: a standing one, a running one and the first spin); hero-swings.json beside the stills
    ///   lists every swing with its clip, speed, impact and the beat it lands on, and the log says the same
    ///   (`Hero: swing ...`).
    /// - <b>The dash poses</b>: each dash type (and Skyfall) from where the last left off, scripted (the client can't
    ///   equip dashes), with the distance and duration of dashes.json, a still mid-dash and one as it lands.
    /// - <b>Hurt and death</b>: the hurt flash while running, then the killing blow: stills on the break's frame and as
    ///   the white shards fly.
    /// </summary>
    sealed class HeroScene : IStillScene, INamedStills
    {
        public const string Name = "hero";
        /// <summary>Each tempo's BPM and Sword Slash's rank, each played for <see cref="TempoSeconds"/>.</summary>
        internal static readonly (double Bpm, int Rank)[] Tempos = { (80, 1), (128, 3), (174, 3) };
        public const double TempoSeconds = 10, WalkFrom = 4.5;
        /// <summary>The filmstrip's frames round an impact (s).</summary>
        internal static readonly double[] Strip = { -0.24, -0.12, 0, 0.08, 0.2 };
        /// <summary>The dashes' segment: when it starts, and each dash after the one before.</summary>
        public const double DashesFrom = 30, DashEvery = 1.2;
        /// <summary>The dashes in order: its pose, its dashes.json id and whether it is the evolution.</summary>
        internal static readonly (DashPose Pose, string Id, bool Evolved, string Label)[] Dashes =
        {
            (DashPose.Dash, "dash", false, "dash"),
            (DashPose.BladeRush, "bladeRush", false, "blade-rush"),
            (DashPose.LeapSlam, "leapSlam", false, "leap-slam"),
            (DashPose.ShieldCharge, "shieldCharge", false, "shield-charge"),
            (DashPose.BlinkStrike, "blinkStrike", false, "blink-strike"),
            (DashPose.LeapSlam, "leapSlam", true, "skyfall"),
        };
        public static double EndFrom => DashesFrom + Dashes.Length * DashEvery + 0.6;
        /// <summary>The last segment's hurt and death, after <see cref="EndFrom"/>.</summary>
        public const double HurtAt = 0.6, DeathAt = 1.6;
        internal static readonly double[] DeathStills = { 0, 0.15, 0.5, 1.0 };

        readonly GameContent _content;
        readonly Palette _palette;
        readonly string _contentDir;
        readonly MonsterLook _look;
        readonly (string Label, double At)[] _fixed;
        HeroSceneDriver _driver;

        public HeroScene(GameContent content, Palette palette, string contentDir, MonsterLook look = MonsterLook.Default)
        {
            _content = content;
            _palette = palette;
            _contentDir = contentDir;
            _look = look;
            var f = new List<(string, double)>();
            for (int i = 0; i < Dashes.Length; i++)
            {
                var (start, end) = DashSpan(content.Dashes, i);
                f.Add((Dashes[i].Label + "-mid", DashesFrom + 0.5 * (start + end)));
                f.Add((Dashes[i].Label + "-end", DashesFrom + end + 0.02));
            }
            f.Add(("hurt", EndFrom + HurtAt + 0.02));
            foreach (double d in DeathStills) f.Add(("death-" + d.ToString("0.00", CultureInfo.InvariantCulture), EndFrom + DeathAt + d));
            _fixed = f.ToArray();
        }

        /// <summary>Dash i's start and end, from the dashes' segment's start.</summary>
        internal static (double Start, double End) DashSpan(DashRules rules, int i)
        {
            double start = 0.6 + i * DashEvery;
            return (start, start + Form(rules, i).DurationS);
        }

        internal static DashForm Form(DashRules rules, int i)
        {
            var kind = rules.Kinds[rules.IndexOf(Dashes[i].Id)];
            return Dashes[i].Evolved ? kind.Evolution : kind.Ranks[0];
        }

        public double[] Times => Array.ConvertAll(_fixed, f => f.At);

        public string Label(int index) => _fixed[index].Label;

        public string Moment => _driver != null ? _driver.TakeMoment() : null;

        public void Begin(Transform root, Camera camera, Stills.View view)
        {
            _driver = root.gameObject.AddComponent<HeroSceneDriver>();
            _driver.Begin(camera, _content, _palette, _contentDir, view, _look);
        }
    }

    /// <summary>Plays <see cref="HeroScene"/>'s script each frame, from the frame it began; torn down with its root.</summary>
    [DefaultExecutionOrder(-100)]
    sealed class HeroSceneDriver : MonoBehaviour
    {
        static readonly Vector2 Dummies = new Vector2(2.6f, 0);
        const float CircleM = 2.3f, LeadRad = 0.7f;

        struct Swung
        {
            public int N;
            public HeroMotion.SwingRecord Record;
            public double PlannedAt;
        }

        Camera _camera;
        GameContent _content;
        Palette _palette;
        MonsterLook _look;
        string _stillsDir;
        Stills.View _view;
        double _start, _lastSong;
        Effects _effects;
        HeroPresenter _hero;
        WorldPresenter _world;
        int _segment = -1;
        // The tempo playing (-1 none): its sim, its beats and swings, and which swings keep a filmstrip.
        int _tempo = -1, _next, _cursor, _stripStanding, _stripRunning, _stripSpin;
        double _lockedAt;
        SimDriver _driver;
        List<StreamEntry> _entries;
        readonly List<double> _beats = new List<double>();
        readonly List<Swung> _swung = new List<Swung>();
        readonly List<string> _tempoJson = new List<string>();
        readonly List<(double At, string Label)> _moments = new List<(double, string)>();
        // The dashes' and the end's progress.
        int _dash;
        Vector2 _feet, _dashFrom, _dashDir;
        double _dashM;
        bool _walking, _hurt, _dead;

        public void Begin(Camera camera, GameContent content, Palette palette, string contentDir, Stills.View view, MonsterLook look)
        {
            _camera = camera;
            _content = content;
            _palette = palette;
            _look = look;
            _view = view;
            _stillsDir = LaunchOptions.Current.StillsDir;
            _start = Time.timeAsDouble;
            var effects = EffectLibrary.Load();
            if (effects != null) _effects = new Effects(effects, EffectRecipes.Load(contentDir), palette);
            view.Ids[IdPass.Hero] = ("hero", "hero", 1.85f);
            view.Ids[IdPass.Effect] = ("hero effects", "effect", 1.8f);
            view.Requeue = () =>
            {
                _hero?.Queue(_camera);
                _effects?.Queue(_camera);
            };
            Debug.Log($"Hero: the scripted scene, Sword Slash at {string.Join(", ", Array.ConvertAll(HeroScene.Tempos, x => $"{x.Bpm} BPM (rank {x.Rank})"))}, " +
                      $"{HeroScene.TempoSeconds} s each, then the dash poses from {HeroScene.DashesFrom} s and the hurt and death from {HeroScene.EndFrom:F1} s");
        }

        /// <summary>The moment due on this frame, if any (one a frame: a second waits for the next).</summary>
        public string TakeMoment()
        {
            for (int i = 0; i < _moments.Count; i++)
            {
                if (_moments[i].At > _lastSong + 1e-6) continue;
                string label = _moments[i].Label;
                _moments.RemoveAt(i);
                return label;
            }
            return null;
        }

        void Update()
        {
            double t = Time.timeAsDouble - _start;
            int segment = t < HeroScene.DashesFrom ? (int)(t / HeroScene.TempoSeconds) : t < HeroScene.EndFrom ? 3 : 4;
            if (segment != _segment) Enter(segment);
            if (segment < 3) Tempo(t - segment * HeroScene.TempoSeconds);
            else if (segment == 3) DashPoses(t - HeroScene.DashesFrom);
            else HurtAndDeath(t - HeroScene.EndFrom);
        }

        /// <summary>A segment's fresh hero and world (song time starts again at 0 in each).</summary>
        void Enter(int segment)
        {
            if (_tempo >= 0) EndTempo();
            _segment = segment;
            _hero?.Dispose();
            _world?.Dispose();
            _hero = new HeroPresenter(_palette, _content.Hero.SpeedMps, _camera.gameObject.layer, _look) { LogSwings = true };
            _world = new WorldPresenter(_camera, _palette);
            _feet = Vector2.zero;
            _moments.Clear();
            _dash = 0;
            _walking = _hurt = _dead = false;
            if (segment < 3) BeginTempo(segment);
        }

        void BeginTempo(int tempo)
        {
            var (bpm, rank) = HeroScene.Tempos[tempo];
            _tempo = tempo;
            _next = _cursor = 0;
            _stripStanding = _stripRunning = _stripSpin = -1;
            _lockedAt = double.PositiveInfinity;
            _beats.Clear();
            _swung.Clear();
            _entries = new ScriptedSong().Beats(0, HeroScene.TempoSeconds + 1, bpm).Entries();
            var inputs = new RunInputs
            {
                Loadout = new[] { Upgrade.Of(AttackKind.SwordSlash, rank) },
                Dummies = new[]
                {
                    new Dummy(Dummies.x, Dummies.y + 0.4, 1e9), new Dummy(Dummies.x + 0.4, Dummies.y - 0.3, 1e9),
                    new Dummy(Dummies.x - 0.35, Dummies.y - 0.35, 1e9),
                },
            };
            _driver = new SimDriver(inputs.Build(_content), new FrameClock());
            Log("Hero: {0} BPM, Sword Slash rank {1}, each swing planned {2:F3} s before its impact", bpm, rank, _content.Attacks.SwordSlash.WindupS);
        }

        void Tempo(double local)
        {
            var sim = _driver.Sim;
            double needed = _driver.StampNeededFor(FrameClock.FrontierAt(local));
            while (_next < _entries.Count && _entries[_next].Stamp <= needed) _driver.AddSettled(_entries[_next++]);
            if (local >= HeroScene.WalkFrom)
            {
                // Led round the dummies: a click target a little ahead on the circle, from wherever the hero is.
                var at = _feet - Dummies;
                float angle = at.sqrMagnitude > 1e-4f ? Mathf.Atan2(at.y, at.x) : Mathf.PI;
                var target = Dummies + CircleM * new Vector2(Mathf.Cos(angle - LeadRad), Mathf.Sin(angle - LeadRad));
                sim.Target(target.x, target.y, _driver.Times.Heard);
            }
            if (!_driver.Step(new ClockReadings { Real = Time.realtimeSinceStartupAsDouble, Frame = local })) return;
            foreach (var e in sim.EventsSince(_cursor)) Take(e);
            _world.Flush();
            _cursor = sim.EventCount;
            sim.DiscardEventsBefore(_cursor);
            double now = _driver.Times.FrameSong;
            sim.Snapshot().HeroAt(now, out double x, out double y);
            Present(now, new Vector2((float)x, (float)y));
        }

        /// <summary>The scene's dispatch: what the assembly's EventDispatch will do for these events.</summary>
        void Take(in SimEvent e)
        {
            _world.Take(e);
            switch (e.Kind)
            {
                case SimEventKind.Beat:
                    _beats.Add(e.T);
                    break;
                case SimEventKind.Cadence:
                    _lockedAt = Math.Min(_lockedAt, e.T);
                    break;
                case SimEventKind.HeroMove:
                    _hero.Walk(e.D0, new Vector2((float)e.D3, (float)e.D4));
                    break;
                case SimEventKind.Swing:
                    _hero.Swing(e.I0 == 1 ? SwingKind.Spin : SwingKind.Slash, e.D0, new Vector2((float)e.D1, (float)e.D2));
                    Planned(e);
                    break;
                case SimEventKind.Strike when (BlowSource)e.I0 == BlowSource.SwordSlash && _effects != null:
                    bool spin = ((BlowFlags)(e.I1 & 0xFF) & BlowFlags.Spin) != 0;
                    _effects.Fire(spin ? EffectCue.SwordSpin : EffectCue.SwordSlash, new EffectShot
                    {
                        T = e.T, At = new Vector2((float)e.D0, (float)e.D1), Facing = new Vector2((float)e.D2, (float)e.D3),
                        Radius = (float)e.D4, ArcDeg = (float)(2 * e.D5 * 180 / Math.PI), Mirrored = Backhand(e.T),
                    });
                    break;
            }
        }

        /// <summary>Is the swing striking at <paramref name="t"/> LightCombo01B, the backhand (its sweep the other way)?</summary>
        bool Backhand(double t)
        {
            foreach (var s in _swung)
                if (Math.Abs(s.Record.Impact - t) < 1e-6) return s.Record.Clip == _hero.Motion.Clips.SlashB.Index;
            return false;
        }

        /// <summary>A swing the sim planned: its impact's still, and a filmstrip round a standing one, a running one and a spin.</summary>
        void Planned(in SimEvent e)
        {
            int n = _swung.Count;
            var motion = _hero.Motion;
            _swung.Add(new Swung { N = n, Record = motion.SwingAt(motion.SwingCount - 1), PlannedAt = e.T });
            string tempo = HeroScene.Tempos[_tempo].Bpm.ToString(CultureInfo.InvariantCulture) + "bpm";
            bool strip = false;
            if (_stripSpin < 0 && e.I0 == 1)
            {
                _stripSpin = n;
                strip = true;
            }
            else if (_stripStanding < 0 && n >= 2 && e.D0 < HeroScene.WalkFrom - 0.2)
            {
                _stripStanding = n;
                strip = true;
            }
            else if (_stripRunning < 0 && e.D0 > HeroScene.WalkFrom + 1.5)
            {
                _stripRunning = n;
                strip = true;
            }
            if (!strip)
            {
                _moments.Add((e.D0, $"{tempo}-swing{n}"));
                return;
            }
            foreach (double d in HeroScene.Strip)
                _moments.Add((e.D0 + d, d == 0 ? $"{tempo}-swing{n}-impact"
                    : $"{tempo}-swing{n}-{(d > 0 ? "+" : "")}{d.ToString("0.00", CultureInfo.InvariantCulture)}"));
        }

        /// <summary>
        /// A tempo's record into hero-swings.json: each swing with the beat nearest its impact. Swings before the cadence
        /// locked to the beat strike on the beatless 1 s pulse (`onPulse`) and are left out of the worst.
        /// </summary>
        void EndTempo()
        {
            var (bpm, rank) = HeroScene.Tempos[_tempo];
            var swings = new List<string>();
            float fastest = 0;
            double worst = 0;
            foreach (var s in _swung)
            {
                var r = s.Record;
                double beat = double.NaN;
                foreach (double b in _beats)
                    if (double.IsNaN(beat) || Math.Abs(b - r.Impact) < Math.Abs(beat - r.Impact)) beat = b;
                bool pulse = r.Impact < _lockedAt;
                fastest = Math.Max(fastest, r.Speed);
                if (!pulse && !double.IsNaN(beat)) worst = Math.Max(worst, Math.Abs(r.Impact - beat));
                swings.Add(string.Format(CultureInfo.InvariantCulture,
                    "        {{\"swing\": {0}, \"kind\": \"{1}\", \"clip\": \"{2}\", \"plannedAt\": {3:F4}, \"impact\": {4:F4}, " +
                    "\"beat\": {5:F4}, \"speed\": {6:F3}, \"clipFrom\": {7:F4}, \"enteredAt\": {8:F4}, \"entryClipTime\": {9:F3}, " +
                    "\"followThroughEnd\": {10:F4}, \"running\": {11}, \"onPulse\": {12}}}",
                    s.N, r.Kind == SwingKind.Spin ? "spin" : "slash", r.Name, s.PlannedAt, r.Impact, beat, r.Speed, r.Start, r.Entry,
                    r.EntryClipTime, r.End, r.Impact > HeroScene.WalkFrom + 0.3 ? "true" : "false", pulse ? "true" : "false"));
            }
            _tempoJson.Add(string.Format(CultureInfo.InvariantCulture,
                "    {{\"bpm\": {0}, \"rank\": {1}, \"windupS\": {2}, \"lockedAt\": {3:F4}, \"maxSpeed\": {4:F3}, \"worstOffBeat\": {5:F6}, " +
                "\"swings\": [\n{6}\n    ]}}",
                bpm, rank, _content.Attacks.SwordSlash.WindupS, _lockedAt, fastest, worst, string.Join(",\n", swings)));
            if (_stillsDir != null)
                File.WriteAllText(Path.Combine(_stillsDir, "hero-swings.json"), "{\n  \"tempos\": [\n" + string.Join(",\n", _tempoJson) + "\n  ]\n}\n");
            Log("Hero: {0} BPM: {1} swings, the fastest at {2:F3}x (at most {3}x); from the cadence's lock at {4:F3} s the worst impact {5:F6} s off its beat",
                bpm, _swung.Count, fastest, HeroMotion.MaxSpeed, _lockedAt, worst);
            if (fastest > HeroMotion.MaxSpeed + 1e-4f) Debug.LogError($"Hero: a swing played at {fastest:F3}x, faster than {HeroMotion.MaxSpeed}x");
            _tempo = -1;
        }

        void DashPoses(double local)
        {
            var rules = _content.Dashes;
            if (_dash < HeroScene.Dashes.Length && local >= HeroScene.DashSpan(rules, _dash).Start)
            {
                var d = HeroScene.Dashes[_dash];
                var (start, end) = HeroScene.DashSpan(rules, _dash);
                // Round a hexagon's corners, so the camera stays near where it began.
                float angle = Mathf.Deg2Rad * (30 + 120 * _dash);
                _dashDir = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                _dashFrom = _feet;
                _dashM = HeroScene.Form(rules, _dash).DistanceM;
                _hero.Dash(d.Pose, start, end, _dashDir, d.Evolved);
                Log("Hero: dash {0} at {1:F3} s for {2:F3} s over {3} m", d.Label, start, end - start, _dashM);
                _dash++;
            }
            if (_dash > 0)
            {
                var (start, end) = HeroScene.DashSpan(rules, _dash - 1);
                double u = Math.Max(0, Math.Min(1, (local - start) / Math.Max(1e-6, end - start)));
                _feet = _dashFrom + _dashDir * (float)(_dashM * u);
            }
            Present(local, _feet);
        }

        void HurtAndDeath(double local)
        {
            float speed = (float)_content.Hero.SpeedMps;
            if (!_walking)
            {
                _walking = true;
                _hero.Walk(0, new Vector2(0, speed));
            }
            if (!_hurt && local >= HeroScene.HurtAt)
            {
                _hurt = true;
                _hero.Hurt(HeroScene.HurtAt);
            }
            if (!_dead && local >= HeroScene.DeathAt)
            {
                _dead = true;
                _hero.Walk(HeroScene.DeathAt, Vector2.zero);
                _hero.Death(HeroScene.DeathAt, new Vector2(-1, speed * (float)HeroScene.DeathAt - 2));
            }
            _feet = new Vector2(0, speed * (float)Math.Min(local, HeroScene.DeathAt));
            Present(local, _feet);
        }

        void Present(double song, Vector2 feet)
        {
            _feet = feet;
            _lastSong = song;
            _hero.Present(song, feet, Time.deltaTime, _camera);
            _effects?.Present(song, feet, Time.deltaTime, _camera);
            _world.Present(song, feet, Time.deltaTime);
            _view.Hero = new Vector3(feet.x, 0, feet.y);
            _view.Impact = _world.World.Rings.Special > 0;
        }

        void OnDestroy()
        {
            if (_tempo >= 0) EndTempo();
            _hero?.Dispose();
            _world?.Dispose();
            _effects?.Dispose();
        }

        static void Log(string format, params object[] args) => Debug.Log(string.Format(CultureInfo.InvariantCulture, format, args));
    }
}
