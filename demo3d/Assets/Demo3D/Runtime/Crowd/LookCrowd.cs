using System;
using System.Collections.Generic;
using PS.Choreography;
using Unity.Collections;
using UnityEngine;
using Look = PS.Look.Crowd;

namespace Demo3D.Runtime.Crowd
{
    /// <summary>
    /// The crystal cut's horde on the look package (crowd.md, The crystal cut on the look package): the package's
    /// crowd (<see cref="Look.BoneTextureCrowd"/>) and GPU shatter (<see cref="Look.MonsterShatter"/>), drawn in the
    /// applied preset's monster look (<see cref="Look.MonsterLooks.Active"/>), fed by demo3d's playback. It draws
    /// every run whose `--monster-style` is <see cref="Style"/> (the crystal cut's), from the library demo3d's crowd
    /// baker writes for it (<see cref="Look.CrowdLibrary.TrailerResourcePath"/>).
    ///
    /// - Every monster death breaks at its killing blow (TimelineEvaluator.State.Broken: the death plays no clip),
    ///   known from the timeline ahead of time (<see cref="Breaks"/>), which also give each break's burst, light and
    ///   sound. On the blow's frame the instance is written Dying, so the crowd blends its palette without drawing it,
    ///   and the shatter seeds its chunks from that pose: they start exactly where the body was, on the frame it
    ///   disappears. From then on the instance is Hidden.
    /// - The kick is demo3d's tuning (the package's at the sword's normal force), the shards' time the song clock, so a
    ///   `--fixed-time` run gives the same shards every time. A seek backwards and a trailer's cut clear them; deaths
    ///   before a run's first frame are not reconstructed (their instance stays hidden).
    /// - Each type's gem colour (<see cref="StyleLook"/>) is its instances' tint. The horde's beat pulse and the
    ///   inner glow are shader globals: StyleBeat publishes the pulse, and the constructor the style's glow tune.
    /// - The look's motion layers (<see cref="Look.CrowdMotion"/>): each monster's clocks from the timeline
    ///   (<see cref="MotionOf"/>: its latest gait step, which moves after its beat, and its latest kick, a lunge, a hit or
    ///   a knockback) go beside its instance (<see cref="Motion"/>); the swarm places each life off its spot
    ///   (<see cref="Place"/>); the beat ripple delays each gait step by its distance from the hero (<see cref="RippleDelay"/>).
    /// </summary>
    public sealed class LookCrowd : ICrowdRenderer
    {
        /// <summary>The `--monster-style` the look package draws (the crystal cut's, trailer-crystal.json).</summary>
        public const string Style = "crystal";

        /// <summary>A death's launch distance: MonsterShards.compute's normal force, at which its kick is demo3d's own tuning.</summary>
        const double Force = 1.85;

        static readonly int IdInnerGlow = Shader.PropertyToID("_SL_InnerGlow");

        /// <summary>One death's break, known from the timeline ahead of time: its burst, light and sound.</summary>
        public struct BreakEvent
        {
            public double Time;     // the killing blow's impact
            public int Entity, Type;
            public Vector3 Hit;     // where the blow lands on the body (world, MonsterShatter.HitPoint)
            public Vector3 Blow;    // the blow's direction on the ground (the hero to the monster), or zero
            public float Height;    // the type's standing height in metres, times the instance's scale
            public Color Gem;       // the type's gem colour (linear)
            public uint Seed;
        }

        /// <summary>One monster's spawn warp (neon-look.md, The Axes &gt; VFX; ps-wnj.16), known from the timeline ahead of time: its effect and sound.</summary>
        public struct WarpEvent
        {
            public double Time;     // the spawn
            public int Entity, Type;
            public Vector2 At;      // where it spawns, on the ground
            public float Height;    // the type's standing height in metres, times the instance's scale
            public Color Gem;       // the type's gem colour (linear)
            public uint Seed;
        }

        /// <summary>The song clock, for the LOD crossfades and the shards.</summary>
        public Func<double> Clock = () => Time.timeAsDouble;

        /// <summary>The game camera, for `--shatter-log`'s view positions.</summary>
        public Camera View;

        /// <summary>`--shatter-log`: a line per break and per spawn warp, to find deaths and spawns for filmstrips.</summary>
        readonly bool _log = Args.Has("--shatter-log");

        readonly Look.CrowdLibrary _lib;
        Look.BoneTextureCrowd _crowd;
        Look.MonsterShatter _shatter;
        int[] _libType = Array.Empty<int>(); // per crowd type: its index in the library
        uint[] _tint = Array.Empty<uint>();  // per crowd type: its gem colour, packed
        Color[] _gem = Array.Empty<Color>(); // per crowd type: its gem colour, linear
        Timeline _tl;
        readonly Dictionary<int, BreakEvent> _breaks = new Dictionary<int, BreakEvent>(); // by entity
        readonly HashSet<int> _broken = new HashSet<int>(); // entities whose break has been seeded (or skipped)
        double _t0 = double.NaN, _last = double.NaN;
        CrowdCounters _counters;

        /// <summary>Deaths broken so far, and the most shards live at once, for logs.</summary>
        public int Deaths => _shatter?.Breaks ?? 0;
        public int Shards => _shatter?.LiveShards ?? 0;
        public int PeakShards { get; private set; }

        /// <summary>The horde for a timeline, its gems lit from the core by <paramref name="glow"/> (the style's glow tune, MonsterStyle.Glow).</summary>
        public LookCrowd(Timeline tl, float glow)
        {
            _tl = tl;
            _lib = Look.CrowdLibrary.Load(Look.CrowdLibrary.TrailerResourcePath)
                ?? throw new InvalidOperationException($"LookCrowd: no {Look.CrowdLibrary.TrailerResourcePath} library (run `demo3d unity BakeCrowd`)");
            Shader.SetGlobalFloat(IdInnerGlow, glow);
        }

        public CrowdCounters Counters => _counters;

        public void Initialize(string[] typeIds, int capacityPerType)
        {
            Dispose();
            _libType = new int[typeIds.Length];
            _tint = new uint[typeIds.Length];
            _gem = new Color[typeIds.Length];
            for (int i = 0; i < typeIds.Length; i++)
            {
                _libType[i] = _lib.IndexOf(typeIds[i]);
                if (_libType[i] < 0) throw new ArgumentException($"LookCrowd: no baked type '{typeIds[i]}' (run `demo3d unity BakeCrowd`)");
                _gem[i] = StyleLook.Of(typeIds[i]).gem;
                _tint[i] = Look.MonsterInstance.Pack(_gem[i].gamma);
            }
            _crowd = new Look.BoneTextureCrowd(_lib, capacityPerType);
            _shatter = new Look.MonsterShatter(_lib, _crowd.Palette);
            Debug.Log($"LookCrowd: {typeIds.Length} types, {capacityPerType} per type, in the {Look.MonsterLooks.Active.Name} monster look");
        }

        public NativeArray<MonsterInstance> Instances(int type, int count) =>
            _crowd.Instances(_libType[type], count).Reinterpret<MonsterInstance>();

        /// <summary>The type's motion clocks for this frame, by the same slots as its instances (<see cref="Look.BoneTextureCrowd.Motion"/>).</summary>
        public NativeArray<Look.MonsterMotion> Motion(int type, int count) => _crowd.Motion(_libType[type], count);

        /// <summary>
        /// A monster's motion clocks at song time <paramref name="t"/>, its instance scale <paramref name="scale"/>: its
        /// latest gait step (a pulse segment that moves) as of <paramref name="t"/> less <paramref name="delay"/> (the
        /// ripple's), planting on its beat and moving after it; and its latest kick by then: a lunge from its clip's start
        /// to its blow, toward the way it faces, or a hit, thrown back from the way it faces, or along the knockback that
        /// starts with it.
        /// </summary>
        public Look.MonsterMotion MotionOf(int entity, int type, double t, float scale, double delay = 0)
        {
            var m = Look.MonsterMotion.Still(_tl.Entities[entity].Seed);
            float height = Mathf.Max(0.1f, _lib.Types[_libType[type]].HeightM * scale);
            var segs = _tl.SegmentsOf(entity);
            double ts = t - delay;
            for (int i = segs.Count - 1; i >= 0; i--)
            {
                var g = segs.Array[segs.Offset + i];
                if (g.T0 > ts || g.Ease != Ease.Pulse) continue;
                var d = g.To - g.From;
                if (d.LengthSquared() < 1e-8f) continue;
                m.StepAge = (float)(ts - g.T0);
                m.StepMove = 0;
                m.Travel = new Vector2(d.X, d.Y) / height;
                break;
            }
            var acts = _tl.ActionsOf(entity);
            for (int i = acts.Count - 1; i >= 0; i--)
            {
                var a = acts.Array[acts.Offset + i];
                bool lunge = a.Kind == ActionKind.Attack;
                if (!lunge && a.Kind != ActionKind.Hit) continue;
                double start = lunge && a.HasClipTiming ? Math.Min(a.ClipStart, a.Impact) : a.Impact;
                if (start > t) continue;
                _tl.PositionAt(entity, a.Impact, out float facing);
                var way = Easing.Dir(facing);
                float strength = lunge ? Look.CrowdMotion.LungeKick : Look.CrowdMotion.HitKick;
                if (!lunge)
                {
                    way = -way;
                    // A knockback starting on the hit throws it along its push.
                    for (int j = segs.Count - 1; j >= 0; j--)
                    {
                        var g = segs.Array[segs.Offset + j];
                        if (g.T0 > a.Impact + 1e-4) continue;
                        if (g.T0 >= a.Impact - 1e-4 && g.Ease == Ease.Linear && (g.To - g.From).LengthSquared() > 1e-8f)
                        {
                            way = System.Numerics.Vector2.Normalize(g.To - g.From);
                            strength = Look.CrowdMotion.KnockKick;
                        }
                        break;
                    }
                }
                m.KickAge = (float)(t - a.Impact);
                m.KickWind = (float)(a.Impact - start);
                m.Kick = new Vector2(way.X, way.Y) * strength;
                break;
            }
            return m;
        }

        /// <summary>The swarm's placement of a life (<see cref="Look.CrowdMotion.Place"/>), on demo3d's instance.</summary>
        public static void Place(ref MonsterInstance inst, uint seed)
        {
            var m = Reinterpret(inst);
            Look.CrowdMotion.Place(ref m, seed);
            inst.Position = m.Position;
            inst.Yaw = m.Yaw;
            inst.Scale = m.Scale;
        }

        /// <summary>
        /// The beat ripple's delay of a monster's gait step at song time <paramref name="t"/>
        /// (<see cref="Look.CrowdMotion.RippleDelay"/>): by its distance from the hero, the beat its current pulse
        /// segment's length (a step runs from its beat to the next); none outside a pulse segment.
        /// </summary>
        public double RippleDelay(int entity, double t)
        {
            var segs = _tl.SegmentsOf(entity);
            for (int i = segs.Count - 1; i >= 0; i--)
            {
                var g = segs.Array[segs.Offset + i];
                if (g.T0 > t) continue;
                if (g.Ease != Ease.Pulse || g.T1 <= g.T0) return 0;
                var d = _tl.PositionAt(entity, t) - _tl.PositionAt(0, t);
                return Look.CrowdMotion.RippleDelay(d.Length(), g.T1 - g.T0);
            }
            return 0;
        }

        /// <summary>A crowd type's gem colour as an instance's tint.</summary>
        public uint Tint(int type) => _tint[type];

        public void Draw(Camera camera)
        {
            double now = Clock();
            _crowd.Draw(camera, (float)now);
            _shatter.Frame(now, camera);
            PeakShards = Math.Max(PeakShards, _shatter.LiveShards);
            _counters = new CrowdCounters { Instances = _crowd.Written, Visible = _crowd.OnScreen };
        }

        /// <summary>
        /// Every death's break in the timeline, by time: the killing blow's impact, where <see cref="Breaking"/> breaks
        /// it. The evaluator's BreaksAtImpact takes their entities. <paramref name="scaleOf"/> is the client's instance
        /// scale for an entity's seed, <paramref name="hoverOf"/> a type's height above the ground (bats). Call it after
        /// <see cref="Initialize"/>.
        /// </summary>
        public BreakEvent[] Breaks(Func<uint, float> scaleOf, Func<int, float> hoverOf)
        {
            var list = new List<BreakEvent>();
            for (int i = 0; i < _tl.Actions.Length; i++)
            {
                var a = _tl.Actions[i];
                if (a.Kind != ActionKind.Death) continue;
                var info = _tl.Entities[a.Entity];
                if (info.Kind != EntityKind.Monster || info.Type < 0 || info.Type >= _gem.Length) continue;
                var p = _tl.PositionAt(a.Entity, a.Impact);
                float height = _lib.Types[_libType[info.Type]].HeightM * scaleOf(info.Seed);
                var blow = BlowOf(a.Entity, a.Impact);
                list.Add(new BreakEvent
                {
                    Time = a.Impact, Entity = a.Entity, Type = info.Type, Blow = blow, Height = height,
                    Hit = Look.MonsterShatter.HitPoint(new Vector3(p.X, hoverOf(info.Type), p.Y), blow, height), Gem = _gem[info.Type], Seed = info.Seed,
                });
            }
            list.Sort((x, y) => { int c = x.Time.CompareTo(y.Time); return c != 0 ? c : x.Entity.CompareTo(y.Entity); });
            _breaks.Clear();
            foreach (var b in list) _breaks[b.Entity] = b;
            return list.ToArray();
        }

        /// <summary>
        /// Every monster's spawn warp in the timeline, by time: each monster spawning after <paramref name="from"/> (a shot's
        /// cut, or the run's start: the monsters there on its first frame were already there) inside the camera's view of
        /// the ground (<see cref="Demo3D.Bench.CameraGeometry.GroundFootprint"/> round the framing's focus at its spawn, turned
        /// by the camera's heading <paramref name="yawDeg"/>, a trailer shot's). <paramref name="scaleOf"/> is the client's
        /// instance scale for an entity's seed. Call it after <see cref="Initialize"/>.
        /// </summary>
        public WarpEvent[] Warps(double from, float yawDeg, Func<uint, float> scaleOf)
        {
            float cos = Mathf.Cos(yawDeg * Mathf.Deg2Rad), sin = Mathf.Sin(yawDeg * Mathf.Deg2Rad);
            var list = new List<WarpEvent>();
            for (int e = 0; e < _tl.EntityCount; e++)
            {
                var info = _tl.Entities[e];
                if (info.Kind != EntityKind.Monster || info.Spawn <= from || info.Type < 0 || info.Type >= _gem.Length) continue;
                var p = _tl.PositionAt(e, info.Spawn);
                var framing = _tl.FramingAt(info.Spawn);
                // Into the camera's own ground frame: it looks along (sin yaw, cos yaw) (GameCameraRig).
                float dx = p.X - framing.Focus.X, dz = p.Y - framing.Focus.Y;
                if (!InView(framing.ViewWidth, dx * cos - dz * sin, dx * sin + dz * cos)) continue;
                list.Add(new WarpEvent
                {
                    Time = info.Spawn, Entity = e, Type = info.Type, At = new Vector2(p.X, p.Y), Gem = _gem[info.Type], Seed = info.Seed,
                    Height = _lib.Types[_libType[info.Type]].HeightM * scaleOf(info.Seed),
                });
            }
            list.Sort((x, y) => { int c = x.Time.CompareTo(y.Time); return c != 0 ? c : x.Entity.CompareTo(y.Entity); });
            if (_log)
                foreach (var w in list)
                    Debug.Log(FormattableString.Invariant($"LookCrowd: warp {_lib.Types[_libType[w.Type]].Id} entity {w.Entity} at t {w.Time:F3} ({w.At.x:F2}, {w.At.y:F2})"));
            return list.ToArray();
        }

        /// <summary>Whether ground offset (<paramref name="x"/>, <paramref name="z"/>) from the camera's target is in its footprint (a convex quad, counter-clockwise).</summary>
        static bool InView(float viewWidth, float x, float z)
        {
            var q = Demo3D.Bench.CameraGeometry.GroundFootprint(viewWidth);
            for (int i = 0; i < 4; i++)
            {
                double ax = q[2 * i], az = q[2 * i + 1], bx = q[(2 * i + 2) % 8], bz = q[(2 * i + 3) % 8];
                if ((bx - ax) * (z - az) - (bz - az) * (x - ax) < 0) return false;
            }
            return true;
        }

        /// <summary>
        /// Plays another timeline from the next <see cref="Frame"/>, which clears the shards (a trailer's cut): its
        /// breaks are <paramref name="breaks"/>, from <see cref="Breaks"/> on it, or none until Breaks is called.
        /// </summary>
        public void Retarget(Timeline tl, BreakEvent[] breaks = null)
        {
            _tl = tl;
            _t0 = double.NaN;
            _breaks.Clear();
            if (breaks != null) foreach (var b in breaks) _breaks[b.Entity] = b;
        }

        /// <summary>Brings the shatter to song time t: a run's first frame, a seek backwards and a retarget clear it. Call once a frame, before <see cref="Breaking"/>.</summary>
        public void Frame(double t)
        {
            if (double.IsNaN(_t0) || t < _last)
            {
                _shatter.Clear();
                _broken.Clear();
                _t0 = t;
            }
            _last = t;
        }

        /// <summary>
        /// A monster from its killing blow on (TimelineEvaluator.State.Broken), as the crowd draws it this frame at
        /// <paramref name="slot"/>: its flags. The blow's frame breaks it (Dying: its palette seeds the shards); every
        /// frame after it is Hidden.
        /// </summary>
        public CrowdFlags Breaking(int entity, int type, int slot, in MonsterInstance inst)
        {
            if (!_breaks.TryGetValue(entity, out var brk) || !_broken.Add(entity) || brk.Time < _t0 - 1e-6) return CrowdFlags.Hidden;
            var body = inst;
            body.Flags = (uint)CrowdFlags.Dying;
            _shatter.Break(_libType[type], slot, Reinterpret(body), type, (int)brk.Seed, _last, brk.Blow.x, brk.Blow.z, Force);
            if (_log) Log(entity, type, inst);
            return CrowdFlags.Dying;
        }

        static Look.MonsterInstance Reinterpret(MonsterInstance m) => new Look.MonsterInstance
        {
            Position = m.Position, Yaw = m.Yaw, ClipA = m.ClipA, TimeA = m.TimeA, ClipB = m.ClipB, TimeB = m.TimeB, Blend = m.Blend,
            Scale = m.Scale, HitFlash = m.HitFlash, Dissolve = m.Dissolve, Tint = m.Tint, Flags = m.Flags,
        };

        /// <summary>A `--shatter-log` line: the death, its song time, world position and place in the game camera's view (1080p pixels from the top left).</summary>
        void Log(int entity, int type, in MonsterInstance inst)
        {
            var t = _lib.Types[_libType[type]];
            var at = inst.Position;
            var vp = View != null ? View.WorldToViewportPoint(at + Vector3.up * t.CenterY * inst.Scale) : Vector3.zero;
            Debug.Log(FormattableString.Invariant(
                $"LookCrowd: break {t.Id} entity {entity} at t {_last:F3} ({at.x:F2}, {at.z:F2}), view px ({vp.x * 1920:F0}, {(1 - vp.y) * 1080:F0}){(vp.z > 0 && vp.x is > 0 and < 1 && vp.y is > 0 and < 1 ? "" : " off screen")}"));
        }

        /// <summary>The killing blow's direction on the ground: from the hero to the monster at its impact.</summary>
        Vector3 BlowOf(int entity, double impact)
        {
            var m = _tl.PositionAt(entity, impact);
            var h = _tl.PositionAt(0, impact);
            var d = new Vector3(m.X - h.X, 0, m.Y - h.Y);
            return d.sqrMagnitude > 1e-6f ? d.normalized : Vector3.zero;
        }

        public void Dispose()
        {
            _shatter?.Dispose();
            _shatter = null;
            _crowd?.Dispose();
            _crowd = null;
        }
    }
}
