using System;
using System.Collections.Generic;
using PS.Choreography;
using UnityEngine;

namespace Demo3D.Runtime.Effects
{
    /// <summary>
    /// Feeds a timeline's effects to the runtime (choreography.md, EffectEvent): each frame the
    /// events due since the last frame (from <see cref="Timeline.FirstEffectAt"/>), mapped from the
    /// Catalog's names to converted effects by the selection's catalog table (variants by seed,
    /// heavier variants on elites and large deaths, companions spawned together). The client adds
    /// what the timeline leaves to it: a projectile effect along each bolt's flight, a dust puff
    /// on each monster attack, and the ambient particles over the view (AmbientEffects). Attached events are
    /// placed at their entity's position at the event time (they don't follow it afterwards).
    /// Crystal deaths (<see cref="SetShatters"/>) swap their death burst for the selection's
    /// client.crystalShatter effect at the killing blow, where their shards break, sprayed along the
    /// blow and tinted by the type's gem.
    /// With an intensity's blow feel (<see cref="Blows"/>), the hero's trails and shocks and the sparks
    /// of its blows grow with the blow's weight, and death bursts by the feel's own factor.
    /// </summary>
    public sealed class EffectDriver
    {
        sealed class Mapping
        {
            public int[] Variants = Array.Empty<int>();
            public int[] With = Array.Empty<int>();
            public int Elite = -1, Large = -1;
            public float LargeScale = float.MaxValue;
        }

        readonly Timeline _tl;
        readonly ISpawnSink _fx;
        readonly EffectLibrary _lib;
        bool _resetFrame;
        double _now;
        readonly Mapping[] _map;
        readonly int _projectile, _dust, _crystal;
        readonly int _deathBurstEvent, _hitSparkEvent; // the timeline's death-burst and hit-spark effect names, -1 if it has none
        readonly AmbientEffects _ambient;
        readonly bool[] _eliteType;
        readonly int[] _actionsByTime;
        readonly int[] _projBySpawn;
        double _last = double.NaN;
        int _nextAction, _nextProj, _nextShatter, _nextBurst;
        Playback.BreakMoments.Burst[] _bursts = Array.Empty<Playback.BreakMoments.Burst>();
        int[] _burstFx = Array.Empty<int>();
        Shatter[] _shatters = Array.Empty<Shatter>();
        float[] _shatterLight = Array.Empty<float>(); // each break's light gain: 1/sqrt(the breaks at its moment)
        readonly HashSet<int> _shattered = new HashSet<int>();

        /// <summary>A crystal death's burst at its killing blow (CrystalShatter.Breaks), in place of its death burst.</summary>
        public struct Shatter
        {
            public double Time;
            public int Entity;
            public Vector3 Position;
            /// <summary>The blow's direction on the ground: the burst's yaw (the effect sprays along its +z). Zero keeps yaw 0.</summary>
            public Vector3 Blow;
            public float Scale;
            public uint Seed;
            public Color Tint;
        }
        public bool Ambient = true;
        /// <summary>The blows' weights and the feel's sizes (an intensity's BlowFeel); null: every event at its own scale.</summary>
        public Playback.BlowFeel Blows;
        /// <summary>Only the ambient particles: no timeline events, bolts or dust (TD15's empty beauty shots).</summary>
        public bool OnlyAmbient;
        /// <summary>The runtime spike: every timeline event spawns this one effect, and the client adds nothing.</summary>
        public int OverrideAll = -1;
        /// <summary>The spike: copies of each event (jittered within half a metre), to reach its spawns per beat frame.</summary>
        public int OverrideCopies = 1;
        /// <summary>When set, only these effects spawn (`--only-effects a,b`: telling effects apart in a still).</summary>
        public bool[] Only;
        /// <summary>Effects and spawns this frame (timeline events, plus the client's own).</summary>
        public int EventsThisFrame { get; private set; }

        public EffectDriver(Timeline tl, EffectSystem fx, Func<int, bool> isEliteType = null) : this(tl, fx.Library, fx, isEliteType) { }

        public EffectDriver(Timeline tl, EffectLibrary lib, ISpawnSink fx, Func<int, bool> isEliteType = null)
        {
            _tl = tl;
            _fx = fx;
            _lib = lib;
            var root = JsonValue.Parse(string.IsNullOrEmpty(lib.Selection) ? "{}" : lib.Selection);
            var catalog = root["catalog"];
            _map = new Mapping[tl.EffectNames.Length];
            for (int i = 0; i < _map.Length; i++)
            {
                var m = new Mapping();
                var c = catalog[tl.EffectNames[i]];
                if (!c.IsNull && c.Count > 0)
                {
                    m.Variants = Indices(lib, c["effects"]);
                    m.With = Indices(lib, c["with"]);
                    m.Elite = Index(lib, c["elite"].AsString());
                    m.Large = Index(lib, c["large"].AsString());
                    m.LargeScale = c["largeScale"].AsFloat(float.MaxValue);
                }
                else if (lib.IndexOf(tl.EffectNames[i]) >= 0) m.Variants = new[] { lib.IndexOf(tl.EffectNames[i]) };
                _map[i] = m;
            }
            var client = root["client"];
            _projectile = Index(lib, client["projectile"].AsString());
            _dust = Index(lib, client["monsterAttack"].AsString());
            _crystal = Index(lib, client["crystalShatter"].AsString());
            _deathBurstEvent = Array.IndexOf(tl.EffectNames, "death-burst");
            _hitSparkEvent = Array.IndexOf(tl.EffectNames, "hit-spark");
            _ambient = new AmbientEffects(lib, fx);
            _eliteType = new bool[tl.TypeNames.Length];
            for (int t = 0; t < _eliteType.Length; t++) _eliteType[t] = isEliteType != null && isEliteType(t);
            _actionsByTime = tl.ActionsByTime();
            var proj = new List<int>();
            for (int e = 0; e < tl.EntityCount; e++) if (tl.Entities[e].Kind == EntityKind.Projectile) proj.Add(e);
            proj.Sort((a, b) => tl.Entities[a].Spawn.CompareTo(tl.Entities[b].Spawn));
            _projBySpawn = proj.ToArray();
        }

        /// <summary>Restricts spawning to the named effects (comma-separated library names); empty for all.</summary>
        public void OnlyEffects(string names)
        {
            if (string.IsNullOrEmpty(names)) { Only = null; return; }
            Only = new bool[_lib.Effects.Length];
            foreach (var n in names.Split(',')) { int i = _lib.IndexOf(n.Trim()); if (i >= 0) Only[i] = true; }
        }

        /// <summary>
        /// The crystal deaths, sorted by time: each one's death-burst event is skipped and the
        /// client.crystalShatter effect spawns at its break instead, turned to spray along the blow
        /// (nothing changes if the selection names no such effect). The breaks at one moment (one
        /// blow's kills) share their flash: each one's light is 1/sqrt(n) of n, so a mass kill is
        /// brighter than a lone death without its lights summing into a wash over the whole view.
        /// </summary>
        public void SetShatters(Shatter[] shatters)
        {
            _shattered.Clear();
            _shatters = _crystal >= 0 && shatters != null ? shatters : Array.Empty<Shatter>();
            foreach (var s in _shatters) _shattered.Add(s.Entity);
            _shatterLight = new float[_shatters.Length];
            for (int i = 0, j; i < _shatters.Length; i = j)
            {
                for (j = i + 1; j < _shatters.Length && _shatters[j].Time - _shatters[i].Time < 1e-3; j++) { }
                for (int k = i; k < j; k++) _shatterLight[k] = 1f / Mathf.Sqrt(j - i);
            }
            _last = double.NaN; // the next frame starts afresh
        }

        /// <summary>
        /// The structure breaks' bursts (BreakMoments.Bursts), library effects by name at their moments, sprayed along the
        /// blow; a name the library lacks is skipped with a warning.
        /// </summary>
        public void SetBursts(IReadOnlyList<Playback.BreakMoments.Burst> bursts)
        {
            var list = new List<Playback.BreakMoments.Burst>(bursts);
            list.Sort((a, b) => a.Time.CompareTo(b.Time));
            _bursts = list.ToArray();
            _burstFx = Array.ConvertAll(_bursts, b => _lib.IndexOf(b.Effect));
            foreach (var b in _bursts)
                if (_lib.IndexOf(b.Effect) < 0) Debug.LogWarning($"EffectDriver: no library effect '{b.Effect}' for a structure break's burst");
            _last = double.NaN; // the next frame starts afresh
        }

        /// <summary>The yaw that turns an effect's +z to <paramref name="blow"/> (the runtime's RotateY; Easing.Dir); 0 for no direction.</summary>
        public static float ShatterYaw(Vector3 blow) => blow.sqrMagnitude > 1e-8f ? Mathf.Atan2(blow.x, blow.z) : 0f;

        static int Index(EffectLibrary lib, string name) => string.IsNullOrEmpty(name) ? -1 : lib.IndexOf(name);

        static int[] Indices(EffectLibrary lib, JsonValue arr)
        {
            if (arr == null || arr.Type != JsonType.Array) return Array.Empty<int>();
            var r = new List<int>();
            for (int i = 0; i < arr.Count; i++) { int k = lib.IndexOf(arr[i].AsString()); if (k >= 0) r.Add(k); }
            return r.ToArray();
        }

        /// <summary>Spawns everything due in (last frame, <paramref name="now"/>]. A seek backwards (or the first frame) clears the pool and re-spawns what is still alive.</summary>
        public void Frame(double now)
        {
            EventsThisFrame = 0;
            double from = _last;
            _now = now;
            _resetFrame = double.IsNaN(_last) || now < _last;
            if (_resetFrame)
            {
                // The sink's Reset excuses this frame's overflow from the pass rule (logged instead).
                _fx.Reset(0);
                from = now - 4.0; // longest effect life: what is still alive at a seek
                _nextAction = LowerBoundAction(from);
                _nextProj = 0;
                while (_nextProj < _projBySpawn.Length && _tl.Entities[_projBySpawn[_nextProj]].Spawn <= from) _nextProj++;
                _nextShatter = 0;
                while (_nextShatter < _shatters.Length && _shatters[_nextShatter].Time <= from) _nextShatter++;
                _nextBurst = 0;
                while (_nextBurst < _bursts.Length && _bursts[_nextBurst].Time <= from) _nextBurst++;
                _ambient.Reset(from);
            }
            _last = now;
            for (int i = OnlyAmbient ? _tl.Effects.Length : _tl.FirstEffectAt(from); i < _tl.Effects.Length; i++)
            {
                var ev = _tl.Effects[i];
                if (ev.Time > now) break;
                if (ev.Time <= from) continue;
                SpawnEvent(ev);
            }
            ClientEffects(from, now);
        }

        int LowerBoundAction(double t)
        {
            int lo = 0, hi = _actionsByTime.Length;
            while (lo < hi) { int mid = (lo + hi) >> 1; if (_tl.Actions[_actionsByTime[mid]].Impact <= t) lo = mid + 1; else hi = mid; }
            return lo;
        }

        void SpawnEvent(in EffectEvent ev)
        {
            if (ev.Effect < 0 || ev.Effect >= _map.Length) return;
            if (ev.Effect == _deathBurstEvent && ev.AttachTo >= 0 && _shattered.Contains(ev.AttachTo) && OverrideAll < 0) return; // its burst is the shatter's
            var m = _map[ev.Effect];
            if (m.Variants.Length == 0 && OverrideAll < 0) return;
            Vector3 pos;
            float yaw = ev.Yaw;
            bool elite = false;
            if (ev.AttachTo >= 0)
            {
                var g = _tl.PositionAt(ev.AttachTo, ev.Time, out float facing);
                pos = new Vector3(g.X + ev.Position.X, ev.Position.Y, g.Y + ev.Position.Z);
                var info = _tl.Entities[ev.AttachTo];
                elite = info.Kind == EntityKind.Monster && info.Type >= 0 && info.Type < _eliteType.Length && _eliteType[info.Type];
            }
            else pos = new Vector3(ev.Position.X, ev.Position.Y, ev.Position.Z);
            if (OverrideAll >= 0)
            {
                for (int c = 0; c < OverrideCopies; c++)
                {
                    uint s = EffectSystem.Hash(ev.Seed, (uint)c);
                    var j = c == 0 ? Vector3.zero : new Vector3(EffectSystem.Rand(s, 1) - 0.5f, EffectSystem.Rand(s, 2) * 0.5f, EffectSystem.Rand(s, 3) - 0.5f);
                    Add(OverrideAll, pos + j, yaw, 1f, s, ev.Time, Vector3.zero, 0, 0);
                }
                return;
            }
            int fx = m.Variants[(int)(ev.Seed % (uint)m.Variants.Length)];
            if (elite && m.Elite >= 0) fx = m.Elite;
            if (ev.Scale >= m.LargeScale && m.Large >= 0) fx = m.Large;
            float yawRate = 0, duration = 0;
            if (_tl.EffectNames[ev.Effect] == "laser" && _tl.Map != null)
            {
                // The laser sweeps once round in a bar (Sim: one bar per sweep).
                double bar = BarSeconds(ev.Time);
                yawRate = (float)(2 * Math.PI / bar);
                duration = (float)bar;
            }
            float scale = ev.Scale * FeelScale(ev);
            Add(fx, pos, yaw, scale, ev.Seed, ev.Time, Vector3.zero, yawRate, duration);
            foreach (int w in m.With) Add(w, pos, yaw, scale, ev.Seed ^ 0x5bd1e995u, ev.Time, Vector3.zero, 0, 0);
        }

        /// <summary>
        /// The blow feel's factor on an event's scale: the hero's trail or shock, and the hit sparks, of a
        /// blow grow by 1 + trail × its weight (the sparks from the feel's spark size); death bursts by the
        /// feel's own factor; anything else, and everything without a feel, keeps its scale.
        /// </summary>
        float FeelScale(in EffectEvent ev)
        {
            if (Blows == null) return 1f;
            var feel = Blows.Feel;
            if (ev.Effect == _deathBurstEvent) return feel.DeathBurst;
            bool spark = ev.Effect == _hitSparkEvent;
            if (!spark && ev.AttachTo != 0) return 1f;
            float w = Blows.WeightAt(ev.Time);
            float grow = 1f + feel.Trail * Mathf.Max(0f, w);
            return spark ? feel.Spark * grow : w >= 0 ? grow : 1f;
        }

        double BarSeconds(double t)
        {
            var beats = _tl.Map.Beats;
            int i = Array.BinarySearch(beats, t);
            if (i < 0) i = ~i;
            i = Math.Max(1, Math.Min(beats.Length - 1, i));
            return (beats[i] - beats[i - 1]) * _tl.Map.BeatsPerBarAt(1);
        }

        void Add(int fx, Vector3 pos, float yaw, float scale, uint seed, double time, Vector3 vel, float yawRate, float duration, Color tint = default, float lightGain = 0)
        {
            // After a seek or loop wrap, the look-back re-spawns only effects still alive now.
            if (Only != null && (fx < 0 || fx >= Only.Length || !Only[fx])) return;
            if (_resetFrame && fx >= 0 && fx < _lib.Effects.Length && !StillAlive(_lib.Effects[fx], time, duration, _now)) return;
            _fx.Add(new EffectSystem.Spawn
            {
                Effect = fx, Position = pos, Yaw = yaw, Scale = scale, Seed = seed, Time = time, Velocity = vel,
                YawRate = yawRate, Duration = duration, Tint = tint, LightGain = lightGain,
            });
            EventsThisFrame++;
        }

        /// <summary>Whether an effect spawned at <paramref name="time"/> still has live particles at <paramref name="now"/>.</summary>
        public static bool StillAlive(EffectEntry fx, double time, float duration, double now) =>
            time + Math.Max(fx.Lifetime, duration > 0 ? duration + (fx.Lifetime - fx.Duration) : 0) > now;

        const double AmbientPeriod = 2.0;
        double _nextAmbient;

        void ClientEffects(double from, double now)
        {
            if (OverrideAll >= 0) return;
            if (OnlyAmbient)
            {
                if (Ambient) EventsThisFrame += _ambient.Spawn(now, t => { var f = _tl.FramingAt(t); return (new Vector2(f.Focus.X, f.Focus.Y), f.ViewWidth); });
                return;
            }
            // A projectile effect along each bolt's straight flight.
            if (_projectile >= 0)
                while (_nextProj < _projBySpawn.Length && _tl.Entities[_projBySpawn[_nextProj]].Spawn <= now)
                {
                    int e = _projBySpawn[_nextProj++];
                    var segs = _tl.SegmentsOf(e);
                    if (segs.Count == 0) continue;
                    var s = segs.Array[segs.Offset];
                    float dur = (float)Math.Max(0.05, s.T1 - s.T0);
                    var a = new Vector3(s.From.X, 1.2f, s.From.Y);
                    var b = new Vector3(s.To.X, 1.2f, s.To.Y);
                    Add(_projectile, a, s.FacingFrom, 1f, _tl.Entities[e].Seed, s.T0, (b - a) / dur, 0, dur);
                }
            // A crystal death's burst at the blow that breaks it, along the blow.
            while (_nextShatter < _shatters.Length && _shatters[_nextShatter].Time <= now)
            {
                float light = _shatterLight[_nextShatter];
                var s = _shatters[_nextShatter++];
                if (s.Time <= from) continue;
                Add(_crystal, s.Position, ShatterYaw(s.Blow), s.Scale, s.Seed ^ 0x6C8E9CF5u, s.Time, Vector3.zero, 0, 0, s.Tint, light);
            }
            // A structure break's bursts, at its moment (BreakMoments).
            while (_nextBurst < _bursts.Length && _bursts[_nextBurst].Time <= now)
            {
                int fx = _burstFx[_nextBurst];
                var b = _bursts[_nextBurst++];
                if (b.Time <= from || fx < 0) continue;
                Add(fx, b.Position, ShatterYaw(b.Blow), b.Scale, b.Seed, b.Time, Vector3.zero, 0, 0, b.Tint);
            }
            // A dust puff at each monster's attack that lands (a trailer shot keeps windups the hero's blow cut short).
            while (_nextAction < _actionsByTime.Length)
            {
                var act = _tl.Actions[_actionsByTime[_nextAction]];
                if (act.Impact > now) break;
                _nextAction++;
                if (_dust < 0 || act.Kind != ActionKind.Attack || act.Entity == 0 || DiedBy(act.Entity, act.Impact)) continue;
                var g = _tl.PositionAt(act.Entity, act.Impact, out float facing);
                var d = Easing.Dir(facing);
                Add(_dust, new Vector3(g.X + d.X * 0.4f, 0.05f, g.Y + d.Y * 0.4f), facing, 1f, (uint)act.Entity * 2654435761u, act.Impact, Vector3.zero, 0, 0);
            }
            // Ambient fireflies and wisps over the view (AmbientEffects).
            if (Ambient) EventsThisFrame += _ambient.Spawn(now, t => { var f = _tl.FramingAt(t); return (new Vector2(f.Focus.X, f.Focus.Y), f.ViewWidth); });
        }

        /// <summary>Has entity <paramref name="e"/> died by song time <paramref name="t"/>?</summary>
        bool DiedBy(int e, double t)
        {
            foreach (ActionEvent a in _tl.ActionsOf(e)) if (a.Kind == ActionKind.Death && a.Impact <= t) return true;
            return false;
        }
    }
}
