using System;
using System.Collections.Generic;
using PS.Choreography;
using UnityEngine;
using NVector2 = System.Numerics.Vector2;

namespace Demo2D.Runtime.Playback
{
    /// <summary>
    /// The 2D client's timeline evaluation (PX5), demo3d's TimelineEvaluator for flipbook sprites.
    /// Every entity's state is a function of the timeline and the song time, so a seek shows the
    /// same horde as a run from the start.
    ///
    /// - Position comes from the entity's segments. During a hero combo whose clip carries root
    ///   motion, the sprite holds at the clip's start (the frames move it; PX3's notes).
    /// - Each action with a clip plays retimed so its blow lands on its impact: attacks as the sim
    ///   fitted them (ClipStart, ClipSpeed), hits and deaths from their onset at the blow (a
    ///   killing blow's flinch gives way to the death). Frames
    ///   are frame starts (frame = ⌊time × rate⌋), so the impact frame shows from the impact.
    /// - Between actions the entity walks, the walk's frame following the distance travelled (a
    ///   cycle per stride), so feet don't slide; standing still freezes it. The hero idles once
    ///   it hasn't moved for a beat.
    /// - Sprites can't crossfade, so a new clip simply replaces the old.
    /// - Facing is the art's right or a horizontal flip, chosen per segment from its yaw with
    ///   hysteresis (a walk straight up or down keeps its side), precomputed so seeks agree.
    /// - A struck monster holds its frame for 50 ms and flashes; a death holds one beat after its
    ///   clip's end, then fades over one beat.
    /// </summary>
    public sealed class SpriteEvaluator
    {
        public const float HitStop = 0.05f, CatchUp = 0.15f, FlashSeconds = 0.12f;
        public const float TickFlash = 0.5f, TickFlashSeconds = 0.08f, ParryFlash = 0.6f;
        /// <summary>A facing turns to the other side only past this much sideways (sin of the yaw).</summary>
        public const float FlipHysteresis = 0.25f;
        /// <summary>Snaps a clip time onto its frame: annotation times are rounded to the ms.</summary>
        const double FrameEpsilon = 0.02;

        readonly Timeline _tl;
        readonly ClipLibrary _clips;
        readonly int[] _frames;          // per annotation: its frame count
        readonly float _rate;
        readonly int[] _walk;            // per timeline type: its walk annotation
        readonly int _heroWalk, _heroIdle;

        readonly double[] _start, _end;  // per action with a clip
        readonly float[] _speed;
        readonly int[][] _clipActions;   // per entity: its clip actions, in start order
        readonly float[][] _cumulative;  // per entity: distance walked at each segment's start
        readonly bool[] _left;           // per segment: faces left

        public Timeline Timeline => _tl;

        public struct State
        {
            public Vector2 Position;     // the feet, metres
            public int Clip;             // annotation index
            public int Frame;
            public bool FlipX;
            public float Flash;          // 0..1 toward white
            public float Alpha;          // 1 until a death's fade
            public bool Dying;
        }

        /// <param name="frames">Each annotation's frame count (its atlas clip's).</param>
        /// <param name="walk">Each timeline type's walk annotation.</param>
        public SpriteEvaluator(Timeline timeline, ClipLibrary clips, int[] frames, float frameRate, int[] walk, int heroWalk, int heroIdle)
        {
            _tl = timeline;
            _clips = clips;
            _frames = frames;
            _rate = frameRate;
            _walk = walk;
            _heroWalk = heroWalk;
            _heroIdle = heroIdle;
            int n = timeline.Actions.Length;
            _start = new double[n];
            _end = new double[n];
            _speed = new float[n];
            _clipActions = new int[timeline.EntityCount][];
            _cumulative = new float[timeline.EntityCount][];
            _left = new bool[timeline.Segments.Length];
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
                    if (a.Kind == ActionKind.Hit && DiesAt(acts, a.Impact)) continue; // a killing blow shows the fall
                    var ann = clips[a.Clip];
                    double s;
                    float speed;
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
                bool left = segs.Count > 0 && Math.Sin(segs.Array[segs.Offset].FacingFrom) < 0;
                for (int k = 0; k < segs.Count; k++)
                {
                    var s = segs.Array[segs.Offset + k];
                    double cut = k + 1 < segs.Count ? segs.Array[segs.Offset + k + 1].T0 : s.T1;
                    cum[k + 1] = cum[k] + NVector2.Distance(s.From, s.PositionAt(Math.Min(cut, s.T1)));
                    float side = (float)Math.Sin(s.FacingTo);
                    if (side < -FlipHysteresis) left = true;
                    else if (side > FlipHysteresis) left = false;
                    _left[segs.Offset + k] = left;
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

        /// <summary>The segment holding at t (index into Segments), or -1 before the first or with none.</summary>
        int SegmentAt(int e, double t)
        {
            int lo = _tl.SegmentStart[e], hi = _tl.SegmentStart[e + 1] - 1;
            if (hi < lo || t < _tl.Segments[lo].T0) return hi < lo ? -1 : lo;
            while (lo < hi)
            {
                int mid = (lo + hi + 1) >> 1;
                if (_tl.Segments[mid].T0 <= t) lo = mid; else hi = mid - 1;
            }
            return lo;
        }

        /// <summary>The distance an entity has walked by t (the walk's phase).</summary>
        public float DistanceAt(int e, double t)
        {
            int seg = SegmentAt(e, t);
            if (seg < 0 || t < _tl.Segments[seg].T0) return 0;
            var s = _tl.Segments[seg];
            return _cumulative[e][seg - _tl.SegmentStart[e]] + NVector2.Distance(s.From, s.PositionAt(t));
        }

        /// <summary>The clip action playing at t (index into Actions), or -1 for walking.</summary>
        int ActiveAction(int e, double t)
        {
            var acts = _clipActions[e];
            int lo = 0, hi = acts.Length - 1, found = -1;
            while (lo <= hi)
            {
                int mid = (lo + hi) >> 1;
                if (_start[acts[mid]] <= t) { found = mid; lo = mid + 1; } else hi = mid - 1;
            }
            return found >= 0 && t < _end[acts[found]] ? acts[found] : -1;
        }

        /// <summary>When an action's clip starts (retimed), for actions with clips.</summary>
        public double ClipStart(int action) => _start[action];

        /// <summary>The frame of annotation <paramref name="clip"/> showing <paramref name="time"/> s into it.</summary>
        public int FrameAt(int clip, double time) =>
            Mathf.Clamp((int)Math.Floor(time * _rate + FrameEpsilon), 0, _frames[clip] - 1);

        /// <summary>A cycle's frame (walks and idles repeat their first frame as their last).</summary>
        int CycleFrame(int clip, double phase)
        {
            int cycle = Math.Max(1, _frames[clip] - 1);
            return (int)Math.Floor((phase - Math.Floor(phase)) * cycle + 1e-6) % cycle;
        }

        /// <summary>Local hit-stop (choreography.md): the clip runs at `rate` for 50 ms from the impact, then catches up over 0.15 s.</summary>
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
            double i = Math.Floor(_tl.Map.BeatIndexAt(t));
            return _tl.Map.TimeOfBeatIndex(i + 1) - _tl.Map.TimeOfBeatIndex(i);
        }

        public State Evaluate(int e, double t)
        {
            var info = _tl.Entities[e];
            bool hero = info.Kind == EntityKind.Hero;
            var st = new State { Alpha = 1f };
            var p = _tl.PositionAt(e, t);
            if (info.Kind == EntityKind.Projectile)
            {
                st.Position = new Vector2(p.X, p.Y);
                st.Clip = -1;
                return st;
            }
            int seg = SegmentAt(e, t);
            st.FlipX = seg >= 0 && _left[seg];

            int act = ActiveAction(e, t);
            if (act >= 0)
            {
                var a = _tl.Actions[act];
                var ann = _clips[a.Clip];
                double local = a.Kind == ActionKind.Hit ? HoldThenCatchUp(t, a.Impact, 0f) : t;
                st.Clip = a.Clip;
                st.Frame = FrameAt(a.Clip, Math.Max(0, (local - _start[act]) * _speed[act]));
                if (hero && a.Kind == ActionKind.Attack && ann.RootMotionM > 0)
                    p = _tl.PositionAt(e, _start[act]);
            }
            else if (hero && _heroIdle >= 0 && DistanceAt(e, t) - DistanceAt(e, t - BeatLength(t)) < 0.05f)
            {
                st.Clip = _heroIdle;
                st.Frame = CycleFrame(_heroIdle, t * _rate / Math.Max(1, _frames[_heroIdle] - 1));
            }
            else
            {
                int walk = hero ? _heroWalk : _walk[info.Type];
                float stride = _clips[walk].StrideM;
                st.Clip = walk;
                st.Frame = stride > 0 ? CycleFrame(walk, DistanceAt(e, t) / stride) : 0;
            }
            st.Position = new Vector2(p.X, p.Y);

            // Flash on the latest hit, tick or parry; fade after a death.
            var acts = _tl.ActionsOf(e);
            for (int k = acts.Count - 1; k >= 0; k--)
            {
                var a = acts.Array[acts.Offset + k];
                if (a.Impact > t) continue;
                if (a.Kind == ActionKind.Hit || a.Kind == ActionKind.Tick || a.Kind == ActionKind.Parry)
                {
                    float peak = a.Kind == ActionKind.Hit ? 1f : a.Kind == ActionKind.Tick ? TickFlash : ParryFlash;
                    float span = a.Kind == ActionKind.Tick ? TickFlashSeconds : FlashSeconds;
                    st.Flash = Mathf.Max(st.Flash, peak * (1f - (float)((t - a.Impact) / span)));
                    if (t - a.Impact > FlashSeconds) break;
                }
                else if (a.Kind == ActionKind.Death)
                {
                    st.Dying = true;
                    st.Flash = Mathf.Max(st.Flash, 1f - (float)((t - a.Impact) / FlashSeconds));
                    int i = acts.Offset + k;
                    var ann = _clips[a.Clip >= 0 ? a.Clip : 0];
                    double clipEnd = _start[i] + Math.Max(ann.End, ann.Impact) / Math.Max(0.01f, _speed[i]);
                    double beat = BeatLength(a.Impact);
                    st.Alpha = 1f - Mathf.Clamp01((float)((t - clipEnd - beat) / beat));
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
