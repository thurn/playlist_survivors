using System;
using System.Collections.Generic;
using System.Globalization;
using PS.Choreography;
using Demo3D.Runtime.Crowd;
using Demo3D.Runtime.Effects;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Demo3D.Runtime.Playback
{
    /// <summary>
    /// Trailer mode, `--trailer [name]` (trailer.md, "How Trailer Mode Works"): the trailer file's shots, each its own
    /// simulation in its own resident environment, played on the song clock from the file's <c>startS</c>. Each frame
    /// draws the active shot's timeline as the song player draws the song's; a cut switches the environment, clears the
    /// effects, shards and hit-stop, seeks the new shot's effect driver, and turns the camera to the shot's heading, in
    /// one frame. Cuts on the song map's accents flash near-white (PostFeel.CutFlash). Only each shot's visible span
    /// sounds; sounds ringing at a cut fade over <see cref="CutFadeS"/>, and from the cut to black the picture is black
    /// while the music and every sound fade out over <c>fadeOutS</c>.
    /// The time flags take trailer seconds, song time less <c>startS</c> (<see cref="_timeBase"/>).
    /// </summary>
    public sealed partial class DemoMain
    {
        /// <summary>A cut's fade of the sounds still ringing from the shot before it.</summary>
        public const double CutFadeS = 0.06;
        /// <summary>How close to a song map accent a cut lands to flash (the accents are curated, the cuts on the grid).</summary>
        const double FlashCutToleranceS = 0.01;

        /// <summary>The flags a trailer rejects, and why: each controls the song's own timeline or the player's residency.</summary>
        static readonly (string flag, string why)[] TrailerConflicts =
        {
            (Intensity.Flag, "the trailer carries its own tuning, built on its file's base overlay"),
            ("--scene", "it controls the song's own timeline"),
            ("--envelope", "it controls the song's own timeline"),
            ("--view-width", "it controls the song's own timeline"),
            ("--hold-at", "it controls the song's own timeline"),
            ("--beauty-empty", "it controls the song's own timeline"),
            (BreakSchedule.Flag, "the trailer's shots break no structures"),
            ("--resident", "the trailer makes its shots' environments resident itself"),
            ("--switch-every", "the trailer switches environments at its cuts"),
        };

        /// <summary>One shot's playback: its timeline and everything the client builds from it.</summary>
        sealed class ShotPlayback
        {
            public TrailerShot Info;
            public Timeline Timeline;
            public TimelineEvaluator Eval;
            public int[] BySpawn;
            public int Arena;                 // its environment's index in the ArenaSet
            public EffectDriver Effects;
            public BlowFeel Blows;
            public CameraFeel Feel;
            public CrystalShatter.BreakEvent[] Breaks = Array.Empty<CrystalShatter.BreakEvent>();
            public double From;               // its first visible song time: the cut, or with --from-preroll its pre-roll's start
            public int Liveness = -1;         // --overlay: its liveness violations (TrailerChecks)
        }

        Trailer _trailer;
        ShotPlayback[] _shots;
        int _shot = -1;      // the shot drawn
        int _cutShot = -1;   // the shot the last cut went to (-1 until the first frame after the warm-up)
        int _onlyShot = -1;  // --shot n: n - 1
        double _timeBase;    // trailer time = song time - _timeBase (0 outside a trailer)
        double _worldFrom = double.NegativeInfinity; // the horde never poses before its shot's first frame (a cut clears the hit-stop)
        int _slotBase;       // crowd slot keys per shot, so no slot carries a monster's state across a cut
        Volume _black;       // the trailer's black: before its first frame and from the cut to black
        AudioSource _music;

        /// <summary>`--overlay`'s trailer line: trailer time, the shot and its liveness violations (null outside a trailer).</summary>
        public string TrailerStatus =>
            _trailer == null || _shot < 0 ? null
            : string.Format(CultureInfo.InvariantCulture, "trailer {0:F2} s  shot {1}/{2} {3}  liveness violations {4}",
                SongTime - _timeBase, _shot + 1, _shots.Length, _shots[_shot].Info.Environment, _shots[_shot].Liveness);

        /// <summary>Fails on a flag that conflicts with `--trailer`, or a trailer flag without it, naming the conflict.</summary>
        void CheckTrailerFlags()
        {
            if (_flags.Trailer == null)
            {
                if (_flags.Shot != 0 || _flags.FromPreroll || _flags.NoTrailerLook)
                    throw new InvalidOperationException("DemoMain: --shot, --from-preroll and --no-trailer-look need --trailer");
                return;
            }
            foreach (var (flag, why) in TrailerConflicts)
                if (Args.Has(flag)) throw new InvalidOperationException($"DemoMain: --trailer conflicts with {flag}: {why}");
            if (_flags.FromPreroll && _flags.Shot == 0) throw new InvalidOperationException("DemoMain: --from-preroll needs --shot <n>");
            if (_flags.Overlay && (_flags.CaptureVideo != null || _flags.CaptureStills != null))
            {
                Debug.LogWarning("DemoMain: --overlay is live mode only; the capture leaves it out");
                _flags.Overlay = false;
            }
        }

        /// <summary>
        /// The trailer file, validated against the song map and the environments (an unknown environment, area or grid
        /// position fails, naming it), and each environment's run inputs at <paramref name="density"/>.
        /// `--environment` pins every shot to one. Then the time flags move from trailer seconds to song seconds.
        /// </summary>
        void LoadTrailer(PlaybackData data, RosterSet rosterSet, float density, out Dictionary<string, ChoreographyData> environments)
        {
            var known = data.EnvironmentNames();
            var choreo = new Dictionary<string, ChoreographyData>();
            ChoreographyData Env(string env)
            {
                if (Array.IndexOf(known, env) < 0) return null;
                if (!choreo.TryGetValue(env, out var c))
                    choreo[env] = c = ChoreographyData.FromJson(data.Annotations(rosterSet), rosterSet.Choreography(data.roster.text, _roster),
                        data.EnvironmentLayout(env), data.footprints.text, density);
                return c;
            }
            _trailer = Trailer.Parse(data.Trailer(_flags.Trailer).text, SongMap.Parse(data.songMap.text), env => Env(env)?.Layout,
                Args.Has("--environment") ? _flags.ArenaOptions.Environment : null);
            environments = choreo;
            if (_flags.Shot != 0 && (_flags.Shot < 1 || _flags.Shot > _trailer.Shots.Length))
                throw new ArgumentException($"--shot: trailer '{_trailer.Name}' has shots 1 to {_trailer.Shots.Length}, not {_flags.Shot}");
            _onlyShot = _flags.Shot - 1;
            _timeBase = _trailer.StartS;
            double from = _trailer.StartS;
            if (_onlyShot >= 0) from = _flags.FromPreroll ? _trailer.Shots[_onlyShot].SpanFrom : _trailer.Shots[_onlyShot].Cut;
            if (Args.Has("--start-at")) from = _timeBase + _flags.StartAt;
            _flags.StartAt = from;
            if (_flags.FreezeAt >= 0) _flags.FreezeAt += _timeBase;
            Debug.Log($"DemoMain: --trailer {_trailer.Name}: {_trailer.Shots.Length} shots in {string.Join(", ", _trailer.Environments)}" +
                      (_onlyShot >= 0 ? $"; only shot {_flags.Shot}" + (_flags.FromPreroll ? " from its pre-roll" : "") : ""));
        }

        /// <summary>The song time the run ends: its shot's end with --shot, else silence after the fade; --seconds may end it sooner.</summary>
        double TrailerEnd()
        {
            double end = _onlyShot >= 0 ? _trailer.Shots[_onlyShot].End : _trailer.EndS;
            return _flags.Seconds > 0 ? Math.Min(end, _flags.StartAt + _flags.Seconds) : end;
        }

        /// <summary>Every shot's simulation (trailer.md, "Shot simulations"), from the roster set's level script and the base overlay.</summary>
        void BuildShots(PlaybackData data, RosterSet rosterSet, Dictionary<string, ChoreographyData> environments)
        {
            string level = data.Level(rosterSet).text, overlay = data.Overlay(Intensity.Parse(_trailer.Base));
            var envs = _trailer.Environments;
            _shots = new ShotPlayback[_trailer.Shots.Length];
            for (int i = 0; i < _shots.Length; i++)
            {
                var info = _trailer.Shots[i];
                var started = Time.realtimeSinceStartupAsDouble;
                var tl = Choreographer.Run(SongMap.Parse(data.songMap.text), _trailer.ShotScript(i, level, overlay), environments[info.Environment]);
                if (i > 0 && string.Join(",", tl.TypeNames) != string.Join(",", _shots[0].Timeline.TypeNames))
                    throw new InvalidOperationException($"DemoMain: shot {info.Number}'s monster types differ from shot 1's");
                _shots[i] = new ShotPlayback
                {
                    Info = info, Timeline = tl, Arena = Array.IndexOf(envs, info.Environment),
                    From = i == _onlyShot && _flags.FromPreroll ? info.SpanFrom : info.Cut,
                };
                Debug.Log($"DemoMain: shot {info.Number} ({info.Environment}) built in {Time.realtimeSinceStartupAsDouble - started:F2} s, {tl.EntityCount} entities");
            }
            _tl = _shots[ShotAt(_flags.StartAt)].Timeline;
        }

        /// <summary>
        /// What the client builds from each shot's timeline: its evaluator, effect driver, blow and camera feel and crystal
        /// breaks; one post feel per environment with every shot's big hits, the flash cuts and the shots' grades; the
        /// lens hits of every shot's visible span (TrailerLook); the sound cues of every shot's visible span. Then the
        /// shot at the start is the one drawn.
        /// </summary>
        void PrepareShots(BlowFeel.Amounts feel, ChoreographyData clips, Dictionary<string, ChoreographyData> environments)
        {
            var cues = new List<SoundScheduler.Cue>();
            for (int i = 0; i < _shots.Length; i++)
            {
                var s = _shots[i];
                var tl = s.Timeline;
                s.Eval = new TimelineEvaluator(tl, clips.Clips);
                s.BySpawn = BySpawn(tl);
                s.Blows = feel != null && !_flags.NoFeel ? new BlowFeel(tl, feel) : null;
                s.Feel = new CameraFeel(tl, s.Blows);
                if (_fx != null)
                {
                    s.Effects = NewEffectDriver(tl);
                    s.Effects.Blows = s.Blows;
                }
                if (_shatter != null)
                {
                    _shatter.Retarget(tl);
                    s.Breaks = _shatter.Breaks(InstanceScale, type => _hover[type]);
                }
                if (s.Breaks.Length > 0) s.Eval.BreaksAtImpact = new HashSet<int>(Array.ConvertAll(s.Breaks, b => b.Entity));
                s.Effects?.SetShatters(Array.ConvertAll(s.Breaks, b => new EffectDriver.Shatter
                {
                    Time = b.Time, Entity = b.Entity, Position = b.Hit, Blow = b.Blow, Scale = b.Height / 1.8f, Seed = b.Seed, Tint = b.Gem,
                }));
                if (_flags.Overlay) s.Liveness = TrailerChecks.Report(_trailer, i, tl, environments[s.Info.Environment]).LivenessViolations;
                if (!Plays(s)) continue;
                foreach (var c in SoundScheduler.Cues(tl, s.Breaks.Length > 0 ? ShatterSounds.For(s.Breaks, tl.Map) : null))
                    if (c.Time >= s.From && c.Time < s.Info.End) cues.Add(c);
            }
            _soundCues = cues.ToArray(); // the shots in order, each sorted: sorted

            var map = _shots[0].Timeline.Map;
            var cutFlashes = new List<double>();
            foreach (var s in _shots)
                if (Plays(s) && s.Info.Number > 1 && Array.Exists(map.Accents, a => Math.Abs(a.Time - s.Info.Cut) < FlashCutToleranceS)) cutFlashes.Add(s.Info.Cut);
            // The trailer's look (trailer.md, Look): each shot's grade from its first frame, and its lens hits.
            var grades = new List<(double, TrailerGrade)>();
            var hits = new List<TrailerLook.Hit>();
            if (!_flags.NoTrailerLook)
            {
                foreach (var s in _shots)
                {
                    if (!Plays(s)) continue;
                    grades.Add((s.From, s.Info.Grade));
                    hits.AddRange(TrailerLook.HitsOf(s.Timeline, feel, s.From, s.Info.End));
                }
                _look = new TrailerLook(map, hits, _supersample);
            }
            _postFeels = new PostFeel[_arenas.Count];
            for (int a = 0; a < _postFeels.Length; a++)
            {
                var amounts = _arenas[a].Lighting.PostFeel;
                var flashes = new List<(double, float)>();
                foreach (var s in _shots) if (Plays(s)) flashes.AddRange(PostFeel.HitFlashes(s.Timeline, amounts, s.From, s.Info.End));
                _postFeels[a] = new PostFeel(map, amounts, !_flags.NoFeel && !_flags.NoPostFeel, flashes, null, cutFlashes, grades);
            }
            _postFeel = _postFeels[0];
            Debug.Log($"DemoMain: flash cuts at trailer {string.Join(", ", cutFlashes.ConvertAll(c => (c - _timeBase).ToString("F3", CultureInfo.InvariantCulture)))} s; {_soundCues.Length} sound cues; " +
                      (_look != null ? $"{hits.Count} lens hits at trailer {string.Join(", ", hits.ConvertAll(h => $"{h.Time - _timeBase:F3} ({h.Strength:F2})"))} s" : "no trailer look"));
            UseShot(ShotAt(_flags.StartAt));
            _black = BlackVolume();
        }

        static int[] BySpawn(Timeline tl)
        {
            var order = new int[tl.EntityCount];
            for (int i = 0; i < order.Length; i++) order[i] = i;
            Array.Sort(order, (a, b) => tl.Entities[a].Spawn.CompareTo(tl.Entities[b].Spawn));
            return order;
        }

        /// <summary>Does the run show shot <paramref name="s"/>: every shot, or only --shot's?</summary>
        bool Plays(ShotPlayback s) => _onlyShot < 0 || s.Info.Number == _onlyShot + 1;

        /// <summary>The shot on screen at song time t: --shot's, else the last whose cut is at or before t.</summary>
        int ShotAt(double t) => _onlyShot >= 0 ? _onlyShot : _trailer.ShotAt(t);

        /// <summary>Draws shot <paramref name="i"/> from the next frame on, with nothing of the shot before it left in the presentation.</summary>
        void UseShot(int i)
        {
            var s = _shots[i];
            _shot = i;
            _tl = s.Timeline;
            _eval = s.Eval;
            _bySpawn = s.BySpawn;
            _nextSpawn = 0; // Activate rebuilds the live set
            _active.Clear();
            _blows = s.Blows;
            _feel = s.Feel;
            _fxDriver = s.Effects;
            _fxDriver?.Seek(); // clears particles and effect lights, and re-spawns the shot's effects still alive (pre-roll ones in flight)
            _fxLast = double.NaN;
            _shatter?.Retarget(s.Timeline, s.Breaks); // its shards so far go
            _worldFrom = s.From;  // no hit-stop from before the shot
            _slotBase = i << 20;
            _rig.YawDeg = s.Info.YawDeg; // the camera jumps to the shot's framing (Draw reads its framing track)
        }

        /// <summary>
        /// The cut into shot <paramref name="i"/> at the frame's song time, once per shot (trailer.md, "Playback and
        /// cuts"): its environment, then the shot itself (UseShot). The sounds' fade is the scheduler's envelope
        /// (<see cref="TrailerGain"/>), and the flash the post feel's.
        /// </summary>
        void Cut(int i, double t)
        {
            SwitchArena(_shots[i].Arena); // between cuts, a no-op that reads 0 ms
            if (i == _cutShot) return;
            _cutShot = i;
            var watch = System.Diagnostics.Stopwatch.StartNew();
            UseShot(i);
            _cutMs = _arenas.LastSwitchMs + watch.Elapsed.TotalMilliseconds;
        }

        double _cutMs = -1, _lastDrawMs; // the cut being drawn: its switch and reset (ms; -1 for none); the frame before's draw

        /// <summary>The frame's draw, timed; a cut's frame logs its switch, reset and draw beside the frame before's draw (no hitch at a cut).</summary>
        void TrailerDraw(double t)
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            Draw(t);
            double ms = watch.Elapsed.TotalMilliseconds;
            if (_cutMs >= 0)
                Debug.Log(string.Format(CultureInfo.InvariantCulture,
                    "DemoMain: cut to shot {0} ({1}) at trailer {2:F3} s: switch and reset {3:F2} ms, draw {4:F2} ms (the frame before: {5:F2} ms)",
                    _shot + 1, _shots[_shot].Info.Environment, t - _timeBase, _cutMs, ms, _lastDrawMs));
            _cutMs = -1;
            _lastDrawMs = ms;
        }

        /// <summary>
        /// A sound cue's gain factor at song time t: a cue from before a cut fades out over <see cref="CutFadeS"/> from
        /// it, and every cue fades with the music from the cut to black.
        /// </summary>
        float TrailerGain(double cue, double t)
        {
            float g = EndFade(t);
            int s = _onlyShot >= 0 ? -1 : _trailer.ShotAt(cue);
            if (s >= 0 && s + 1 < _trailer.Shots.Length)
            {
                double cut = _trailer.Shots[s + 1].Cut;
                if (t > cut) g *= (float)Math.Max(0, 1 - (t - cut) / CutFadeS);
            }
            return g;
        }

        /// <summary>The music's and the sounds' gain at song time t: 1 until the cut to black, then down to 0 over fadeOutS.</summary>
        float EndFade(double t)
        {
            if (t <= _trailer.BlackAt) return 1f;
            return _trailer.FadeOutS > 0 ? (float)Math.Max(0, 1 - (t - _trailer.BlackAt) / _trailer.FadeOutS) : 0f;
        }

        /// <summary>From the cut to black: the picture black and the music fading. Returns true while it is black.</summary>
        bool Blackout(double t)
        {
            bool black = t >= _trailer.BlackAt - 1e-9;
            if (black)
            {
                SongTime = t; // nothing is drawn (Draw keeps it otherwise)
                TrailerLook.Off();
            }
            _black.weight = black ? 1f : 0f;
            if (_music != null) _music.volume = EndFade(t);
            return black;
        }

        /// <summary>
        /// A global volume over every environment's that grades the frame to black at weight 1: the screen before the
        /// first frame (while the player loads and warms) and from the cut to black.
        /// </summary>
        Volume BlackVolume()
        {
            var go = new GameObject("TrailerBlack");
            go.transform.SetParent(transform, false);
            var volume = go.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 1000;
            volume.weight = 1f;
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            var color = profile.Add<ColorAdjustments>(true);
            color.colorFilter.Override(Color.black);
            color.postExposure.Override(-10f);
            volume.sharedProfile = profile;
            return volume;
        }
    }
}
