using System;
using System.Collections.Generic;
using PS.Choreography;
using Demo3D.Runtime.Crowd;
using UnityEngine;
using NVector2 = System.Numerics.Vector2;

namespace Demo3D.Runtime.Playback
{
    /// <summary>
    /// The client's timeline evaluation (TD4): every entity's state is a function of the
    /// timeline and the song time, so a seek shows the right horde at once.
    ///
    /// - Position and facing come from the entity's segments.
    /// - Each action with a clip plays retimed so its blow lands on its impact
    ///   (ClipLibrary.Fit: speed in 0.75..1.33, working back from the impact), starting when the
    ///   entity's previous action has reached its annotated end.
    /// - Between actions the entity walks, the walk's phase following the distance it has
    ///   travelled, so feet don't slide; standing still freezes the phase.
    /// - Two slots: when the clip changes, the leaving clip keeps playing and crossfades out
    ///   over 0.12 s.
    /// - A struck monster holds its pose for 50 ms and flashes; a death holds one beat after the
    ///   clip's end, then dissolves over one beat.
    /// </summary>
    public sealed class TimelineEvaluator
    {
        public const float Crossfade = 0.12f, HitStop = 0.05f, FlashSeconds = 0.12f;
        public const float TickFlash = 0.5f, TickFlashSeconds = 0.08f, ParryFlash = 0.6f;
        /// <summary>After a hold or slowdown the clip catches up over this long, well inside a beat.</summary>
        public const float CatchUp = 0.15f;
        /// <summary>Heavy blows (strength 0.8 or more) slow the hero's clip to 0.6x for 50 ms.</summary>
        public const float HeavyStrength = 0.8f, HeavySlow = 0.6f;

        readonly Timeline _tl;
        readonly ClipLibrary _clips;
        readonly SongMap _map;

        // Per action with a clip: when it starts and ends, and its speed.
        readonly double[] _start, _end;
        readonly float[] _speed;
        // Per entity: indices of its clip actions, in start order.
        readonly int[][] _clipActions;
        // Per entity: cumulative ground distance at each segment's start.
        readonly float[][] _cumulative;
        // Per clip annotation: the role ("walk", "attack", ...) and owner ("skeleton", "hero").
        readonly string[] _role, _owner;

        public Timeline Timeline => _tl;

        public TimelineEvaluator(Timeline timeline, ClipLibrary clips)
        {
            _tl = timeline;
            _clips = clips;
            _map = timeline.Map;
            int n = timeline.Actions.Length;
            _start = new double[n];
            _end = new double[n];
            _speed = new float[n];
            _role = new string[clips.Count];
            _owner = new string[clips.Count];
            for (int c = 0; c < clips.Count; c++)
            {
                var use = clips[c].Use;
                int space = use.LastIndexOf(' ');
                _owner[c] = space > 0 ? use.Substring(0, space) : use;
                _role[c] = space > 0 ? use.Substring(space + 1) : use;
            }

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
                    var ann = clips[a.Clip];
                    double s, end;
                    float speed;
                    if (a.HasClipTiming)
                    {
                        // Exactly as the sim scheduled it (its root-motion segment starts here too).
                        s = a.ClipStart;
                        speed = a.ClipSpeed;
                        end = s + ann.End / speed;
                    }
                    else
                    {
                        // Hits and deaths start at the blow (their annotated impact is the
                        // reaction's onset), at their own speed, cutting into whatever plays.
                        speed = 1f;
                        s = a.Impact - ann.Impact;
                        end = s + ann.End;
                    }
                    _start[i] = s;
                    _end[i] = a.Kind == ActionKind.Death ? double.PositiveInfinity : end;
                    _speed[i] = speed;
                    list.Add(i);
                }
                list.Sort((x, y) => _start[x].CompareTo(_start[y]));
                _clipActions[e] = list.ToArray();

                var segs = timeline.SegmentsOf(e);
                var cum = new float[segs.Count + 1];
                for (int k = 0; k < segs.Count; k++)
                {
                    var s = segs.Array[segs.Offset + k];
                    // A segment holds until the next one starts, which may cut it short.
                    double cut = k + 1 < segs.Count ? segs.Array[segs.Offset + k + 1].T0 : s.T1;
                    var reached = s.PositionAt(Math.Min(cut, s.T1));
                    cum[k + 1] = cum[k] + NVector2.Distance(s.From, reached);
                }
                _cumulative[e] = cum;
            }
        }

        /// <summary>When an action's clip starts (retimed), for actions with clips.</summary>
        public double ClipStart(int action) => _start[action];

        /// <summary>True if a later action of the same entity starts before this one's impact.</summary>
        public bool Interrupted(int action)
        {
            var a = _tl.Actions[action];
            foreach (int j in _clipActions[a.Entity])
                if (j != action && _start[j] > _start[action] && _start[j] <= a.Impact) return true;
            return false;
        }

        public string RoleOf(int clip) => clip >= 0 && clip < _role.Length ? _role[clip] : null;
        public string OwnerOf(int clip) => clip >= 0 && clip < _owner.Length ? _owner[clip] : null;

        /// <summary>One animation slot: an annotation index (or -1 for the walk) and a clip time.</summary>
        public struct Slot
        {
            public int Clip;     // annotation index; -1 means the entity's walk
            public float Time;   // seconds into the clip
        }

        public struct State
        {
            public Vector3 Position;
            public float Yaw;
            public Slot A, B;
            public float Blend;  // weight of A
            public float HitFlash, Dissolve;
            public bool Dying;
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
            return _cumulative[e][lo] + NVector2.Distance(s.From, s.PositionAt(t));
        }

        /// <summary>The clip action playing at t (index into Actions), or -1 for walking.</summary>
        int ActiveAction(int e, double t, out double since)
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

        Slot SlotFor(int action, int e, double t, float walkStride, float walkLength)
        {
            if (action < 0)
            {
                float phase = walkStride > 0 ? DistanceAt(e, t) / walkStride : 0f;
                return new Slot { Clip = -1, Time = (phase - Mathf.Floor(phase)) * walkLength };
            }
            var a = _tl.Actions[action];
            var ann = _clips[a.Clip];
            double local = t;
            if (a.Kind == ActionKind.Hit) local = HoldThenCatchUp(t, a.Impact, 0f);
            else if (a.Kind == ActionKind.Attack && a.Entity == 0 && a.Strength >= HeavyStrength)
                local = HoldThenCatchUp(t, a.Impact, HeavySlow);
            float time = (float)((local - _start[action]) * _speed[action]);
            float length = Mathf.Max(ann.End, ann.Impact);
            if (a.Kind == ActionKind.Death) time = Mathf.Min(time, length);
            return new Slot { Clip = a.Clip, Time = Mathf.Max(0f, time) };
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

        public double BeatLength(double t)
        {
            double x = _map.BeatIndexAt(t);
            double i = Math.Floor(x);
            return _map.TimeOfBeatIndex(i + 1) - _map.TimeOfBeatIndex(i);
        }

        /// <summary>
        /// An entity's state at song time t. The walk's stride and length (seconds) come from
        /// the caller's clip data, since annotations name the walk per type.
        /// </summary>
        public State Evaluate(int e, double t, float walkStride, float walkLength, float hover)
        {
            var st = new State();
            var p = _tl.PositionAt(e, t, out float facing);
            st.Position = new Vector3(p.X, hover, p.Y);
            st.Yaw = facing;

            int act = ActiveAction(e, t, out double since);
            st.A = SlotFor(act, e, t, walkStride, walkLength);
            st.Blend = 1f;
            st.B = st.A;
            if (t - since < Crossfade && !double.IsInfinity(since))
            {
                int prev = ActiveAction(e, since - 1e-4, out _);
                if (prev != act)
                {
                    st.B = SlotFor(prev, e, t, walkStride, walkLength);
                    st.Blend = (float)((t - since) / Crossfade);
                }
            }

            // Flash on the latest hit or tick; dissolve after a death.
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
                    st.HitFlash = Mathf.Max(st.HitFlash, peak * (1f - (float)((t - a.Impact) / span)));
                    if (t - a.Impact > FlashSeconds) break;
                }
                else if (a.Kind == ActionKind.Death)
                {
                    st.Dying = true;
                    st.HitFlash = Mathf.Max(st.HitFlash, 1f - (float)((t - a.Impact) / FlashSeconds));
                    var ann = _clips[a.Clip >= 0 ? a.Clip : 0];
                    int i = acts.Offset + k;
                    double clipEnd = _start[i] + Math.Max(ann.End, ann.Impact) / Math.Max(0.01f, _speed[i]);
                    double beat = BeatLength(a.Impact);
                    st.Dissolve = Mathf.Clamp01((float)((t - clipEnd - beat) / beat));
                    break;
                }
            }
            return st;
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
