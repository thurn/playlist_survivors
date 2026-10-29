using System;
using System.Numerics;

namespace PS.Choreography
{
    /// <summary>One section's targets and what the choreographer produced in it.</summary>
    public sealed class SectionStats
    {
        public string Label, Load, Goal;
        public SectionFeel Feel;
        public double Start, End;
        public int TargetOnScreen;
        public float ViewWidthM;
        public float TargetDeathsPerSecond, TargetHitsPerSecond;
        public int Deaths, Hits, Ticks, Spawns, Attacks, Parries, HeroActions, HeroStrides;
        public double MeanOnScreen;          // alive and dying, sampled each tick
        public int MaxOnScreen;
        public double GoalReachedAt = -1;    // song seconds, -1 if never
        public float FinalShapeScale;

        public double Duration => End - Start;
        public double DeathsPerSecond => Duration > 0 ? Deaths / Duration : 0;
        public double HitsPerSecond => Duration > 0 ? Hits / Duration : 0;
    }

    /// <summary>
    /// The timeline (choreography.md, "Timeline contract"): the only interface between the
    /// choreography layer and the client.
    /// <list type="bullet">
    /// <item>Entity ids are dense and never reused: <c>Entities[id].Id == id</c>; the hero is 0.</item>
    /// <item><see cref="Segments"/> and <see cref="Actions"/> are grouped by entity, then sorted by
    /// time; <see cref="SegmentsOf"/> and <see cref="ActionsOf"/> give an entity's range.</item>
    /// <item><see cref="Effects"/> and <see cref="Sounds"/> are sorted by time.</item>
    /// <item><see cref="Framing"/> is sampled at <see cref="FramingRate"/> Hz from t = 0.</item>
    /// </list>
    /// </summary>
    public sealed class Timeline
    {
        public const double FramingRate = 60;

        public string Name;
        public uint Seed;
        public double Duration;
        public SongMap Map;
        public BeatGrid Grid;

        public EntityInfo[] Entities;
        public MotionSegment[] Segments;
        public int[] SegmentStart;     // Entities.Length + 1 offsets
        public ActionEvent[] Actions;
        public int[] ActionStart;      // Entities.Length + 1 offsets
        public EffectEvent[] Effects;
        public SoundEvent[] Sounds;
        public FramingSample[] Framing;

        public string[] TypeNames, ProjectileNames, ClipNames, EffectNames, SoundNames;
        public float[] TypeRadii;
        public SectionStats[] Sections;

        /// <summary>Per-tick wall times of the run that built this timeline (not part of the dump).</summary>
        public SimTickTiming TickTiming;
        /// <summary>Wall time of the whole build in seconds (not part of the dump).</summary>
        public double BuildSeconds;

        public int EntityCount => Entities.Length;

        public ArraySegment<MotionSegment> SegmentsOf(int entity) =>
            new ArraySegment<MotionSegment>(Segments, SegmentStart[entity], SegmentStart[entity + 1] - SegmentStart[entity]);

        public ArraySegment<ActionEvent> ActionsOf(int entity) =>
            new ArraySegment<ActionEvent>(Actions, ActionStart[entity], ActionStart[entity + 1] - ActionStart[entity]);

        /// <summary>
        /// An entity's ground position and facing at song time <paramref name="t"/>: within the
        /// last segment that started by then, or before its first segment, that segment's start.
        /// </summary>
        public Vector2 PositionAt(int entity, double t, out float facing)
        {
            int lo = SegmentStart[entity], hi = SegmentStart[entity + 1] - 1;
            if (hi < lo) { facing = 0; return Vector2.Zero; }
            if (t < Segments[lo].T0) { facing = Segments[lo].FacingFrom; return Segments[lo].From; }
            while (lo < hi)
            {
                int mid = (lo + hi + 1) >> 1;
                if (Segments[mid].T0 <= t) lo = mid; else hi = mid - 1;
            }
            MotionSegment s = Segments[lo];
            facing = s.FacingAt(t);
            return s.PositionAt(t);
        }

        public Vector2 PositionAt(int entity, double t) => PositionAt(entity, t, out _);

        public bool Exists(int entity, double t) => t >= Entities[entity].Spawn && t < Entities[entity].Despawn;

        /// <summary>The framing at song time <paramref name="t"/>, interpolated between samples.</summary>
        public FramingSample FramingAt(double t)
        {
            double x = t * FramingRate;
            if (x <= 0) return Framing[0];
            int i = (int)x;
            if (i >= Framing.Length - 1) return Framing[Framing.Length - 1];
            float u = (float)(x - i);
            FramingSample a = Framing[i], b = Framing[i + 1];
            return new FramingSample(Vector2.Lerp(a.Focus, b.Focus, u), a.ViewWidth + (b.ViewWidth - a.ViewWidth) * u);
        }

        /// <summary>The index of the first effect at or after <paramref name="t"/>.</summary>
        public int FirstEffectAt(double t)
        {
            int lo = 0, hi = Effects.Length;
            while (lo < hi) { int mid = (lo + hi) >> 1; if (Effects[mid].Time < t) lo = mid + 1; else hi = mid; }
            return lo;
        }

        /// <summary>The index of the first sound at or after <paramref name="t"/>.</summary>
        public int FirstSoundAt(double t)
        {
            int lo = 0, hi = Sounds.Length;
            while (lo < hi) { int mid = (lo + hi) >> 1; if (Sounds[mid].Time < t) lo = mid + 1; else hi = mid; }
            return lo;
        }

        /// <summary>Indices into <see cref="Actions"/> sorted by impact time (ties by entity, then kind).</summary>
        public int[] ActionsByTime()
        {
            var idx = new int[Actions.Length];
            for (int i = 0; i < idx.Length; i++) idx[i] = i;
            ActionEvent[] a = Actions;
            Array.Sort(idx, (x, y) =>
            {
                int c = a[x].Impact.CompareTo(a[y].Impact);
                if (c != 0) return c;
                c = a[x].Entity.CompareTo(a[y].Entity);
                return c != 0 ? c : x.CompareTo(y);
            });
            return idx;
        }

        public int CountActions(ActionKind kind)
        {
            int n = 0;
            foreach (ActionEvent a in Actions) if (a.Kind == kind) n++;
            return n;
        }
    }
}
