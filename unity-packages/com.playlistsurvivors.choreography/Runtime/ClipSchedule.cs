using System;
using System.Collections.Generic;
using System.Numerics;

namespace PS.Choreography
{
    /// <summary>
    /// When each entity's clips play, shared by both demos' clients (choreography.md, "Clip
    /// annotations and retiming"): a function of the timeline and the song time, so a seek shows
    /// the same horde as a run from the start. Each client keeps only its presentation (demo3d's
    /// slots and crossfade, demo2d's frames).
    ///
    /// - Attacks play as the sim fitted them (ClipStart, ClipSpeed). Hits and deaths start at the
    ///   blow (their annotated impact is the reaction's onset), at their own speed, cutting into
    ///   whatever plays. A death never ends.
    /// - A killing blow's flinch gives way to the death: a hit at the same impact as its entity's
    ///   death isn't scheduled (the sim gives both only when two blows land on one tick).
    /// - Each entity's clips are in start order, ties by action index.
    /// - A struck monster holds its pose for 50 ms; with <c>slowHeavyBlows</c>, the hero's heavy
    ///   blows slow to 0.6× for 50 ms. Both then catch up (<see cref="HoldThenCatchUp"/>).
    /// - Hits flash, ticks briefly at half strength, parries at 0.6; a death flashes, holds one
    ///   beat after its clip's end, then fades over one beat.
    /// </summary>
    public sealed class ClipSchedule
    {
        public const float HitStop = 0.05f, FlashSeconds = 0.12f;
        public const float TickFlash = 0.5f, TickFlashSeconds = 0.08f, ParryFlash = 0.6f;
        /// <summary>After a hold or slowdown the clip catches up over this long, well inside a beat.</summary>
        public const float CatchUp = 0.15f;
        /// <summary>Heavy blows (strength 0.8 or more) slow the hero's clip to 0.6x for 50 ms.</summary>
        public const float HeavyStrength = 0.8f, HeavySlow = 0.6f;

        readonly Timeline _tl;
        readonly ClipLibrary _clips;
        readonly bool _slowHeavyBlows;

        // Per action: when its clip starts (NaN if it plays none) and ends, and its speed.
        readonly double[] _start, _end;
        readonly float[] _speed;
        // Per entity: indices of its clip actions, in start order.
        readonly int[][] _clipActions;
        // Per entity: cumulative ground distance at each segment's start.
        readonly float[][] _cumulative;

        /// <summary>A flash toward white (0..1), and a death's fade (0 until it starts, then to 1).</summary>
        public struct Feedback
        {
            public float Flash, Fade;
            public bool Dying;
        }

        public ClipSchedule(Timeline timeline, ClipLibrary clips, bool slowHeavyBlows)
        {
            _tl = timeline;
            _clips = clips;
            _slowHeavyBlows = slowHeavyBlows;
            int n = timeline.Actions.Length;
            _start = new double[n];
            _end = new double[n];
            _speed = new float[n];
            Array.Fill(_start, double.NaN);
            _clipActions = new int[timeline.EntityCount][];
            _cumulative = new float[timeline.EntityCount][];
            var list = new List<int>();
            for (int e = 0; e < timeline.EntityCount; e++)
            {
                list.Clear();
                var acts = timeline.ActionsOf(e);
                for (int k = 0; k < acts.Count; k++)
                {
                    int i = acts.Offset + k;
                    var a = timeline.Actions[i];
                    if (a.Clip < 0 || a.Kind == ActionKind.Tick || a.Kind == ActionKind.Spawn) continue;
                    if (a.Kind == ActionKind.Hit && DiesAt(acts, a.Impact)) continue;
                    var ann = clips[a.Clip];
                    double s;
                    float speed;
                    // Exactly as the sim scheduled it (its root-motion segment starts here too).
                    if (a.HasClipTiming) { s = a.ClipStart; speed = a.ClipSpeed; }
                    else { s = a.Impact - ann.Impact; speed = 1f; }
                    _start[i] = s;
                    _speed[i] = speed;
                    _end[i] = a.Kind == ActionKind.Death ? double.PositiveInfinity : s + ann.End / speed;
                    list.Add(i);
                }
                list.Sort((x, y) => { int c = _start[x].CompareTo(_start[y]); return c != 0 ? c : x.CompareTo(y); });
                _clipActions[e] = list.ToArray();

                var segs = timeline.SegmentsOf(e);
                var cum = new float[segs.Count + 1];
                for (int k = 0; k < segs.Count; k++)
                {
                    var s = segs.Array[segs.Offset + k];
                    // A segment holds until the next one starts, which may cut it short.
                    double cut = k + 1 < segs.Count ? segs.Array[segs.Offset + k + 1].T0 : s.T1;
                    cum[k + 1] = cum[k] + Vector2.Distance(s.From, s.PositionAt(Math.Min(cut, s.T1)));
                }
                _cumulative[e] = cum;
            }
        }

        static bool DiesAt(ArraySegment<ActionEvent> acts, double impact)
        {
            for (int k = 0; k < acts.Count; k++)
            {
                var a = acts.Array[acts.Offset + k];
                if (a.Kind == ActionKind.Death && a.Impact == impact) return true;
            }
            return false;
        }

        public Timeline Timeline => _tl;

        /// <summary>When an action's clip starts (retimed); NaN for actions that play none.</summary>
        public double Start(int action) => _start[action];

        /// <summary>
        /// True if an action's clip isn't showing at its impact: it plays none (a tick, or a killing
        /// blow's flinch), or a later clip of the same entity starts by then.
        /// </summary>
        public bool Interrupted(int action)
        {
            if (double.IsNaN(_start[action])) return true;
            var a = _tl.Actions[action];
            foreach (int j in _clipActions[a.Entity])
                if (j != action && _start[j] > _start[action] && _start[j] <= a.Impact) return true;
            return false;
        }

        /// <summary>The clip action playing at t (index into Actions), or -1 for walking; since: when it, or the walk, began.</summary>
        public int ActiveAction(int e, double t, out double since)
        {
            var acts = _clipActions[e];
            int lo = 0, hi = acts.Length - 1, found = -1;
            while (lo <= hi)
            {
                int mid = (lo + hi) >> 1;
                if (_start[acts[mid]] <= t) { found = mid; lo = mid + 1; } else hi = mid - 1;
            }
            if (found >= 0)
            {
                int i = acts[found];
                if (t < _end[i]) { since = _start[i]; return i; }
                since = _end[i];
                return -1;
            }
            since = double.NegativeInfinity;
            return -1;
        }

        /// <summary>Seconds into an action's clip at song time t, after its hit-stop or heavy-blow slowdown.</summary>
        public double LocalTime(int action, double t)
        {
            var a = _tl.Actions[action];
            double local = t;
            if (a.Kind == ActionKind.Hit) local = HoldThenCatchUp(t, a.Impact, 0f);
            else if (_slowHeavyBlows && a.Kind == ActionKind.Attack && a.Entity == 0 && a.Strength >= HeavyStrength)
                local = HoldThenCatchUp(t, a.Impact, HeavySlow);
            return Math.Max(0, (local - _start[action]) * _speed[action]);
        }

        /// <summary>The distance an entity has walked by time t (for the walk's phase).</summary>
        public float DistanceAt(int e, double t)
        {
            var segs = _tl.SegmentsOf(e);
            int lo = 0, hi = segs.Count - 1;
            if (hi < 0) return 0;
            var arr = segs.Array;
            int off = segs.Offset;
            if (t < arr[off].T0) return 0;
            while (lo < hi)
            {
                int mid = (lo + hi + 1) >> 1;
                if (arr[off + mid].T0 <= t) lo = mid; else hi = mid - 1;
            }
            var s = arr[off + lo];
            return _cumulative[e][lo] + Vector2.Distance(s.From, s.PositionAt(t));
        }

        public double BeatLength(double t)
        {
            double i = Math.Floor(_tl.Map.BeatIndexAt(t));
            return _tl.Map.TimeOfBeatIndex(i + 1) - _tl.Map.TimeOfBeatIndex(i);
        }

        /// <summary>
        /// When a death's fade begins (<paramref name="action"/> indexes <see cref="Timeline.Actions"/>):
        /// a beat after its clip comes to rest on its last pose. The crowd's dissolve starts here, and
        /// the crystal shatter releases its shards.
        /// </summary>
        public double FadeStart(int action)
        {
            var a = _tl.Actions[action];
            var ann = _clips[a.Clip >= 0 ? a.Clip : 0];
            double clipEnd = _start[action] + Math.Max(ann.End, ann.Impact) / Math.Max(0.01f, _speed[action]);
            return clipEnd + BeatLength(a.Impact);
        }

        /// <summary>An entity's flash on its latest hit, tick or parry, and its fade after a death, at t.</summary>
        public Feedback FeedbackAt(int e, double t)
        {
            var fb = new Feedback();
            var acts = _tl.ActionsOf(e);
            for (int k = acts.Count - 1; k >= 0; k--)
            {
                var a = acts.Array[acts.Offset + k];
                if (a.Impact > t) continue;
                if (a.Kind == ActionKind.Hit || a.Kind == ActionKind.Tick || a.Kind == ActionKind.Parry)
                {
                    // Tiered feedback (effects.md): a tick, a lingering effect's repeat hit, flashes
                    // at half strength and briefly, so a swept monster (or the boss under the laser)
                    // pulses rather than glowing white; a parry is the hero's shield flash.
                    float peak = a.Kind == ActionKind.Hit ? 1f : a.Kind == ActionKind.Tick ? TickFlash : ParryFlash;
                    float span = a.Kind == ActionKind.Tick ? TickFlashSeconds : FlashSeconds;
                    fb.Flash = Math.Max(fb.Flash, peak * (1f - (float)((t - a.Impact) / span)));
                    if (t - a.Impact > FlashSeconds) break;
                }
                else if (a.Kind == ActionKind.Death)
                {
                    fb.Dying = true;
                    fb.Flash = Math.Max(fb.Flash, 1f - (float)((t - a.Impact) / FlashSeconds));
                    fb.Fade = Math.Clamp((float)((t - FadeStart(acts.Offset + k)) / BeatLength(a.Impact)), 0f, 1f);
                    break;
                }
            }
            return fb;
        }

        /// <summary>
        /// Local hit-stop (choreography.md): from the impact the clip runs at `rate` (0 holds the
        /// pose) for 50 ms, then catches up over 0.15 s, so it rejoins real time well before the
        /// next impact, with no jump.
        /// </summary>
        public static double HoldThenCatchUp(double t, double impact, float rate)
        {
            double u = t - impact;
            if (u <= 0) return t;
            double lag = HitStop * (1 - rate);
            if (u < HitStop) return impact + u * rate;
            if (u < HitStop + CatchUp) return t - lag * (1 - (u - HitStop) / CatchUp);
            return t;
        }

        /// <summary>A stable per-entity jitter in [0, 1) from its seed.</summary>
        public static float Jitter(uint seed, uint salt)
        {
            uint h = seed * 0x9E3779B1u ^ salt * 0x85EBCA77u;
            h ^= h >> 15; h *= 0x2C1B3C6Du; h ^= h >> 12; h *= 0x297A2D39u; h ^= h >> 15;
            return (h & 0xFFFFFF) / 16777216f;
        }
    }
}
