using System;
using System.Collections.Generic;
using Demo3D.Choreography;
using UnityEngine;

namespace Demo3D.Runtime.Effects
{
    /// <summary>
    /// Feeds a timeline's effects to the runtime (choreography.md, EffectEvent): each frame the
    /// events due since the last frame (from <see cref="Timeline.FirstEffectAt"/>), mapped from the
    /// Catalog's names to converted effects by the selection's catalog table (variants by seed,
    /// heavier variants on elites and large deaths, companions spawned together). The client adds
    /// what the timeline leaves to it: a projectile effect along each bolt's flight, a dust puff
    /// on each monster attack, and ambient embers and wisps over the view. Attached events are
    /// placed at their entity's position at the event time (they don't follow it afterwards).
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
        readonly int _projectile, _dust;
        readonly int[] _ambient;
        readonly bool[] _eliteType;
        readonly int[] _actionsByTime;
        readonly int[] _projBySpawn;
        double _last = double.NaN;
        int _nextAction, _nextProj;
        public bool Ambient = true;
        /// <summary>The runtime spike: every timeline event spawns this one effect, and the client adds nothing.</summary>
        public int OverrideAll = -1;
        /// <summary>The spike: copies of each event (jittered within half a metre), to reach its spawns per beat frame.</summary>
        public int OverrideCopies = 1;
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
            _ambient = Indices(lib, client["ambient"]);
            _eliteType = new bool[tl.TypeNames.Length];
            for (int t = 0; t < _eliteType.Length; t++) _eliteType[t] = isEliteType != null && isEliteType(t);
            _actionsByTime = tl.ActionsByTime();
            var proj = new List<int>();
            for (int e = 0; e < tl.EntityCount; e++) if (tl.Entities[e].Kind == EntityKind.Projectile) proj.Add(e);
            proj.Sort((a, b) => tl.Entities[a].Spawn.CompareTo(tl.Entities[b].Spawn));
            _projBySpawn = proj.ToArray();
        }

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
                _nextAmbient = Math.Floor(from / AmbientPeriod);
            }
            _last = now;
            for (int i = _tl.FirstEffectAt(from); i < _tl.Effects.Length; i++)
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
            Add(fx, pos, yaw, ev.Scale, ev.Seed, ev.Time, Vector3.zero, yawRate, duration);
            foreach (int w in m.With) Add(w, pos, yaw, ev.Scale, ev.Seed ^ 0x5bd1e995u, ev.Time, Vector3.zero, 0, 0);
        }

        double BarSeconds(double t)
        {
            var beats = _tl.Map.Beats;
            int i = Array.BinarySearch(beats, t);
            if (i < 0) i = ~i;
            i = Math.Max(1, Math.Min(beats.Length - 1, i));
            return (beats[i] - beats[i - 1]) * _tl.Map.BeatsPerBarAt(1);
        }

        void Add(int fx, Vector3 pos, float yaw, float scale, uint seed, double time, Vector3 vel, float yawRate, float duration)
        {
            // After a seek or loop wrap, the look-back re-spawns only effects still alive now.
            if (_resetFrame && fx >= 0 && fx < _lib.Effects.Length && !StillAlive(_lib.Effects[fx], time, duration, _now)) return;
            _fx.Add(new EffectSystem.Spawn
            {
                Effect = fx, Position = pos, Yaw = yaw, Scale = scale, Seed = seed, Time = time, Velocity = vel,
                YawRate = yawRate, Duration = duration,
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
            // A dust puff at each monster's attack.
            while (_nextAction < _actionsByTime.Length)
            {
                var act = _tl.Actions[_actionsByTime[_nextAction]];
                if (act.Impact > now) break;
                _nextAction++;
                if (_dust < 0 || act.Kind != ActionKind.Attack || act.Entity == 0) continue;
                var g = _tl.PositionAt(act.Entity, act.Impact, out float facing);
                var d = Easing.Dir(facing);
                Add(_dust, new Vector3(g.X + d.X * 0.4f, 0.05f, g.Y + d.Y * 0.4f), facing, 1f, (uint)act.Entity * 2654435761u, act.Impact, Vector3.zero, 0, 0);
            }
            // Ambient embers and wisps: a few emitters scattered over the view every two seconds.
            if (Ambient && _ambient.Length > 0)
                while ((_nextAmbient + 1) * AmbientPeriod <= now)
                {
                    _nextAmbient++;
                    double t = _nextAmbient * AmbientPeriod;
                    var f = _tl.FramingAt(Math.Max(0, t));
                    uint k = (uint)_nextAmbient;
                    for (int j = 0; j < 6; j++)
                    {
                        uint seed = EffectSystem.Hash(k, (uint)j);
                        float x = (EffectSystem.Rand(seed, 1) - 0.5f) * f.ViewWidth, z = (EffectSystem.Rand(seed, 2) - 0.4f) * f.ViewWidth * 0.6f;
                        Add(_ambient[j % _ambient.Length], new Vector3(f.Focus.X + x, 0.3f, f.Focus.Y + z), 0, 1f, seed, t, Vector3.zero, 0, (float)(AmbientPeriod * 2));
                    }
                }
        }
    }
}
