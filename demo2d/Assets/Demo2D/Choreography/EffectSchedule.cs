using System;
using System.Collections.Generic;
using System.Numerics;
using PS.Choreography;

namespace Demo2D.Choreography
{
    /// <summary>An axis-aligned box on the ground, in world metres (the view).</summary>
    public readonly struct Box2
    {
        public readonly float XMin, YMin, XMax, YMax;
        public Box2(float xMin, float yMin, float xMax, float yMax) { XMin = xMin; YMin = yMin; XMax = xMax; YMax = yMax; }
    }

    /// <summary>
    /// An effect the schedule draws: a timeline effect event or a projectile in flight. Each
    /// event and each projectile has one, the same object every time it is live.
    /// </summary>
    public abstract class LiveEffect
    {
        public readonly EffectSpec Spec;
        public readonly double Born;
        public readonly uint Seed;
        /// <summary>
        /// The draw order and the light pool's tie-break: flights first (the newest projectile
        /// first), then events in event order.
        /// </summary>
        public readonly long Order;

        protected LiveEffect(EffectSpec spec, double born, uint seed, long order) { Spec = spec; Born = born; Seed = seed; Order = order; }

        /// <summary>The prefab's scale.</summary>
        public abstract float Scale { get; }
        /// <summary>The light cue's radius.</summary>
        public abstract float LightRadius { get; }
    }

    /// <summary>A timeline effect event.</summary>
    public sealed class EventEffect : LiveEffect
    {
        public readonly int Event;
        public readonly EffectEvent Data;
        /// <summary>The beam's sweep in seconds (NaN when the entry doesn't sweep).</summary>
        public readonly double SweepS;

        internal EventEffect(EffectSpec spec, int index, EffectEvent ev, double sweepS) : base(spec, ev.Time, ev.Seed, index)
        {
            Event = index;
            Data = ev;
            SweepS = sweepS;
        }

        public override float Scale => Spec.ScaleFor(Data.Scale);
        public override float LightRadius => Spec.LightRadiusFor(Data.Scale);
    }

    /// <summary>A projectile in flight.</summary>
    public sealed class FlightEffect : LiveEffect
    {
        public readonly int Entity;

        internal FlightEffect(EffectSpec spec, EntityInfo info) : base(spec, info.Spawn, info.Seed, -(info.Id + 1L)) => Entity = info.Id;

        public override float Scale => Spec.Scale;
        public override float LightRadius => Spec.LightRadiusFor(1f);
    }

    /// <summary>
    /// The timeline's effects as a function of song time (PX7), apart from drawing them
    /// (EffectLayer): which are live, where each is, how it turns and how bright its light is.
    ///
    /// The live set at t: the events whose lifetime covers t and the projectiles in flight, near
    /// the view, at most each kind's <c>max</c> (the newest win). The beam sweeps a full turn over
    /// its line spell's bars, as the sim's ticks do (Sim.CastSpell, HitShape.Line).
    /// </summary>
    public sealed class EffectSchedule
    {
        /// <summary>Effects this far beyond the view (plus their scale) still live, so they don't pop in at the edges.</summary>
        public const float MarginM = 3f;
        /// <summary>A projectile flies this high over the ground (the crowd draws its bolt there).</summary>
        public const float ProjectileHeightM = 1f;

        readonly Timeline _tl;
        readonly EffectSpec[] _byEffect, _byProjectile;
        readonly int[] _effectCount, _projectileCount;
        /// <summary>Each effect kind's line spell's bars (NaN when no line spell casts it).</summary>
        readonly float[] _sweepBars;
        readonly double _maxDuration;
        readonly EventEffect[] _events;
        readonly FlightEffect[] _flights;

        public EffectSchedule(Timeline tl, EffectLibrary library, Roster roster)
        {
            _tl = tl;
            var problems = library.Problems(tl.EffectNames, tl.ProjectileNames);
            _byEffect = Array.ConvertAll(tl.EffectNames, n => library.Kinds.TryGetValue(n, out var s) ? s : null);
            _byProjectile = Array.ConvertAll(tl.ProjectileNames, n => library.Flights.TryGetValue(n, out var s) ? s : null);
            _effectCount = new int[_byEffect.Length];
            _projectileCount = new int[_byProjectile.Length];
            _sweepBars = new float[_byEffect.Length];
            for (int k = 0; k < _sweepBars.Length; k++) _sweepBars[k] = float.NaN;
            foreach (var mv in roster.Spells)
            {
                if (mv.Shape != HitShape.Line || mv.Effect < 0) continue;
                if (!float.IsNaN(_sweepBars[mv.Effect]) && _sweepBars[mv.Effect] != mv.Bars)
                    problems.Add($"line spells with the effect {tl.EffectNames[mv.Effect]} sweep over different bars");
                _sweepBars[mv.Effect] = mv.Bars;
            }
            for (int k = 0; k < _byEffect.Length; k++)
                if (_byEffect[k] != null && _byEffect[k].Sweep && float.IsNaN(_sweepBars[k])) problems.Add(tl.EffectNames[k] + " sweeps, but no line spell casts it");
            if (problems.Count > 0) throw new InvalidOperationException("EffectSchedule: " + string.Join("; ", problems));
            foreach (var s in _byEffect) _maxDuration = Math.Max(_maxDuration, s.DurationS);
            _events = new EventEffect[tl.Effects.Length];
            _flights = new FlightEffect[tl.EntityCount];
        }

        /// <summary>
        /// Fills <paramref name="into"/> with the effects live at song time t near
        /// <paramref name="view"/>: events in event order, then the projectiles among
        /// <paramref name="active"/>, the last first.
        /// </summary>
        public void Live(double t, Box2 view, IReadOnlyList<int> active, List<LiveEffect> into)
        {
            into.Clear();
            Array.Clear(_effectCount, 0, _effectCount.Length);
            Array.Clear(_projectileCount, 0, _projectileCount.Length);
            int first = _tl.FirstEffectAt(t - _maxDuration);
            int end = first;
            while (end < _tl.Effects.Length && _tl.Effects[end].Time <= t) end++;
            // Newest first, so each kind keeps its newest max; then back into event order.
            for (int i = end - 1; i >= first; i--)
            {
                var ev = _tl.Effects[i];
                var spec = _byEffect[ev.Effect];
                if (t - ev.Time >= spec.DurationS || _effectCount[ev.Effect] >= spec.Max) continue;
                var fx = Event(i);
                if (!InView(view, Position(fx, t), fx.Scale)) continue;
                _effectCount[ev.Effect]++;
                into.Add(fx);
            }
            into.Reverse();
            for (int j = active.Count - 1; j >= 0; j--)
            {
                var info = _tl.Entities[active[j]];
                if (info.Kind != EntityKind.Projectile) continue;
                if (_projectileCount[info.Type] >= _byProjectile[info.Type].Max) continue;
                var fx = _flights[info.Id] ?? (_flights[info.Id] = new FlightEffect(_byProjectile[info.Type], info));
                if (!InView(view, Position(fx, t), fx.Scale)) continue;
                _projectileCount[info.Type]++;
                into.Add(fx);
            }
        }

        EventEffect Event(int i)
        {
            if (_events[i] != null) return _events[i];
            var ev = _tl.Effects[i];
            var spec = _byEffect[ev.Effect];
            return _events[i] = new EventEffect(spec, i, ev, spec.Sweep ? SweepSeconds(_sweepBars[ev.Effect], ev.Time) : double.NaN);
        }

        /// <summary>A sweep over <paramref name="bars"/> starting at song time t: the sim's, in bars of the beat there.</summary>
        double SweepSeconds(float bars, double t)
        {
            var map = _tl.Map;
            double b = Math.Floor(map.BeatIndexAt(t) + 1e-6);
            double beat = map.TimeOfBeatIndex(b + 1) - map.TimeOfBeatIndex(b);
            return bars * map.BeatsPerBarAt(1) * beat;
        }

        static bool InView(Box2 view, Vector2 p, float scale)
        {
            float m = MarginM + 2 * scale;
            return p.X > view.XMin - m && p.X < view.XMax + m && p.Y > view.YMin - m && p.Y < view.YMax + m;
        }

        /// <summary>Where an effect is at song time τ: its anchor (a projectile 1 m up, an entity plus the event's offset, or the event's point), moved along its yaw.</summary>
        public Vector2 Position(LiveEffect fx, double tau)
        {
            if (fx is FlightEffect flight)
            {
                var p = _tl.PositionAt(flight.Entity, tau);
                return new Vector2(p.X, p.Y + ProjectileHeightM);
            }
            var e = (EventEffect)fx;
            var ev = e.Data;
            // The sim's 3D offsets: x and z on the ground, y up, which the side view draws as up the screen.
            var at = new Vector2(ev.Position.X, ev.Position.Z + ev.Position.Y);
            if (ev.AttachTo >= 0) at += _tl.PositionAt(ev.AttachTo, tau);
            var spec = e.Spec;
            if (spec.ReachM > 0) at += Easing.Dir(Yaw(e, tau)) * (spec.ReachM * spec.ScaleFor(ev.Scale) / spec.Scale);
            return at;
        }

        /// <summary>The event's yaw at song time τ: its own, or turning through the beam's sweep.</summary>
        public float Yaw(EventEffect fx, double tau)
        {
            if (!fx.Spec.Sweep) return fx.Data.Yaw;
            return fx.Data.Yaw + (float)(2 * Math.PI * (tau - fx.Data.Time) / fx.SweepS);
        }

        /// <summary>The beam's opacity at song time τ: whole through the sweep, then fading out.</summary>
        public float BeamAlpha(EventEffect fx, double tau)
        {
            double age = tau - fx.Data.Time;
            if (age < fx.SweepS) return 1f;
            float a = 1f - (float)((age - fx.SweepS) / fx.Spec.Beam.FadeS);
            return a < 0 ? 0 : a > 1 ? 1 : a;
        }

        /// <summary>The effect's light at song time t (0 when it has no light).</summary>
        public float LightIntensity(LiveEffect fx, double t) => fx.Spec.Light == null ? 0 : fx.Spec.Light.At(t - fx.Born);
    }
}
