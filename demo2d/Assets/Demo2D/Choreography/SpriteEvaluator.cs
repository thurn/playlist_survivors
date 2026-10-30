using System;
using System.Numerics;
using PS.Choreography;

namespace Demo2D.Choreography
{
    /// <summary>
    /// The 2D client's timeline evaluation (PX5): the shared <see cref="ClipSchedule"/> drawn as
    /// flipbook frames. Every entity's state is a function of the timeline and the song time, so a
    /// seek shows the same horde as a run from the start.
    ///
    /// - Position comes from the entity's segments. During a hero combo whose clip carries root
    ///   motion, the sprite holds at the clip's start (the frames move it; PX3's notes).
    /// - Each clip action shows the frame at its schedule's local time. Frames are frame starts
    ///   (frame = ⌊time × rate⌋), so the impact frame shows from the impact.
    /// - Hit-stop on the hero's big blows (PX8): a spin or slam (strength from
    ///   <see cref="ClipSchedule.HeavyStrength"/>) holds its impact frame for the schedule's 50 ms
    ///   hit-stop, then catches up, as a struck monster does. A flipbook can't show demo3d's 0.6×
    ///   slowdown (15 fps frames), so it holds.
    /// - Between actions a monster walks, the walk's frame following the distance travelled (a
    ///   cycle per stride), so feet don't slide; standing still freezes it.
    /// - The hero steps as the native game's does: each step (a Pulse segment, the roster's 110 ms
    ///   from the beat) flicks through half its walk cycle, the halves alternating step by step,
    ///   and lifts it over its shadow in an arc <see cref="HeroHopM"/> high; between steps and
    ///   swings it idles.
    /// - A hit's knockback hops (PX8): while the sim slides a struck monster back, its sprite lifts
    ///   over its shadow in an arc of <see cref="HopSeconds"/>, <see cref="HopM"/> high for grunts
    ///   and a third of that for elites and the boss. Ticks (the beam's) knock nothing back and
    ///   don't hop.
    /// - Sprites can't crossfade, so a new clip simply replaces the old.
    /// - Facing is the art's right or a horizontal flip, chosen per segment from its yaw with
    ///   hysteresis (a walk straight up or down keeps its side), precomputed so seeks agree.
    /// </summary>
    public sealed class SpriteEvaluator
    {
        /// <summary>A facing turns to the other side only past this much sideways (sin of the yaw).</summary>
        public const float FlipHysteresis = 0.25f;
        /// <summary>A knockback's hop: how long, and how high for a grunt (metres; elites a third).</summary>
        public const float HopSeconds = 0.16f, HopM = 0.16f;
        /// <summary>The hero's hop on each step (metres): the native game's 5 px on its 48 px hero, scaled to the knight.</summary>
        public const float HeroHopM = 0.18f;
        /// <summary>Types wider than this hop a third as high: elites and the boss.</summary>
        const float GruntRadius = 0.5f;
        /// <summary>Snaps a clip time onto its frame: annotation times are rounded to the ms.</summary>
        const double FrameEpsilon = 0.02;

        readonly Timeline _tl;
        readonly ClipLibrary _clips;
        readonly ClipSchedule _schedule;
        readonly int[] _frames;          // per annotation: its frame count
        readonly float _rate;
        readonly int[] _walk;            // per timeline type: its walk annotation
        readonly int _heroWalk, _heroIdle;
        readonly bool[] _left;           // per segment: faces left

        public Timeline Timeline => _tl;
        public ClipSchedule Schedule => _schedule;

        public struct State
        {
            public Vector2 Position;     // the feet, metres
            public int Clip;             // annotation index
            public int Frame;
            public bool FlipX;
            public float Flash;          // 0..1, the hit flash's strength
            public float Alpha;          // 1 until a death's fade
            public bool Dying;
            public float Lift;           // the sprite's hop over its feet, metres
        }

        /// <param name="frames">Each annotation's frame count (its atlas clip's).</param>
        /// <param name="walk">Each timeline type's walk annotation.</param>
        public SpriteEvaluator(Timeline timeline, ClipLibrary clips, int[] frames, float frameRate, int[] walk, int heroWalk, int heroIdle)
        {
            _tl = timeline;
            _clips = clips;
            _schedule = new ClipSchedule(timeline, clips, slowHeavyBlows: false);
            _frames = frames;
            _rate = frameRate;
            _walk = walk;
            _heroWalk = heroWalk;
            _heroIdle = heroIdle;
            _left = new bool[timeline.Segments.Length];
            for (int e = 0; e < timeline.EntityCount; e++)
            {
                var segs = timeline.SegmentsOf(e);
                bool left = segs.Count > 0 && Math.Sin(segs.Array[segs.Offset].FacingFrom) < 0;
                for (int k = 0; k < segs.Count; k++)
                {
                    float side = (float)Math.Sin(segs.Array[segs.Offset + k].FacingTo);
                    if (side < -FlipHysteresis) left = true;
                    else if (side > FlipHysteresis) left = false;
                    _left[segs.Offset + k] = left;
                }
            }
        }

        /// <summary>The segment holding at t (index into Segments), or -1 with none.</summary>
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

        /// <summary>The frame of annotation <paramref name="clip"/> showing <paramref name="time"/> s into it.</summary>
        public int FrameAt(int clip, double time) =>
            Math.Clamp((int)Math.Floor(time * _rate + FrameEpsilon), 0, _frames[clip] - 1);

        /// <summary>A cycle's frame (walks and idles repeat their first frame as their last).</summary>
        int CycleFrame(int clip, double phase)
        {
            int cycle = Math.Max(1, _frames[clip] - 1);
            return (int)Math.Floor((phase - Math.Floor(phase)) * cycle + 1e-6) % cycle;
        }

        /// <summary>The hop of an entity's latest hit at t: a parabola over <see cref="HopSeconds"/>.</summary>
        float Hop(int e, double t, float height)
        {
            var acts = _tl.ActionsOf(e);
            for (int k = acts.Count - 1; k >= 0; k--)
            {
                var a = acts.Array[acts.Offset + k];
                if (a.Impact > t) continue;
                if (t - a.Impact >= HopSeconds) return 0;
                if (a.Kind != ActionKind.Hit) continue;
                float u = (float)((t - a.Impact) / HopSeconds);
                return 4 * height * u * (1 - u);
            }
            return 0;
        }

        public State Evaluate(int e, double t)
        {
            var info = _tl.Entities[e];
            bool hero = info.Kind == EntityKind.Hero;
            var st = new State { Alpha = 1f };
            var p = _tl.PositionAt(e, t);
            if (info.Kind == EntityKind.Projectile)
            {
                st.Position = p;
                st.Clip = -1;
                return st;
            }
            int seg = SegmentAt(e, t);
            st.FlipX = seg >= 0 && _left[seg];

            // The hero's step: its progress through a Pulse segment, or -1 standing.
            float step = -1f;
            if (hero && seg >= 0)
            {
                var sg = _tl.Segments[seg];
                if (sg.Ease == Ease.Pulse && t >= sg.T0 && t < sg.T1 && sg.To != sg.From) step = (float)((t - sg.T0) / (sg.T1 - sg.T0));
                if (step >= 0) st.Lift = HeroHopM * (float)Math.Sin(Math.PI * step);
            }

            int act = _schedule.ActiveAction(e, t, out _);
            if (act >= 0)
            {
                var a = _tl.Actions[act];
                st.Clip = a.Clip;
                double local = hero && a.Kind == ActionKind.Attack && a.Strength >= ClipSchedule.HeavyStrength
                    ? _schedule.LocalTime(act, ClipSchedule.HoldThenCatchUp(t, a.Impact, 0f))
                    : _schedule.LocalTime(act, t);
                st.Frame = FrameAt(a.Clip, local);
                if (hero && a.Kind == ActionKind.Attack && _clips[a.Clip].RootMotionM > 0)
                    p = _tl.PositionAt(e, _schedule.Start(act));
            }
            else if (hero && step >= 0)
            {
                st.Clip = _heroWalk;
                st.Frame = CycleFrame(_heroWalk, ((seg - _tl.SegmentStart[e]) & 1) * 0.5 + step * 0.5);
            }
            else if (hero && _heroIdle >= 0)
            {
                st.Clip = _heroIdle;
                st.Frame = CycleFrame(_heroIdle, t * _rate / Math.Max(1, _frames[_heroIdle] - 1));
            }
            else
            {
                int walk = hero ? _heroWalk : _walk[info.Type];
                float stride = _clips[walk].StrideM;
                st.Clip = walk;
                st.Frame = stride > 0 ? CycleFrame(walk, _schedule.DistanceAt(e, t) / stride) : 0;
            }
            st.Position = p;

            var fb = _schedule.FeedbackAt(e, t);
            st.Flash = fb.Flash;
            st.Alpha = 1f - fb.Fade;
            st.Dying = fb.Dying;
            if (info.Kind == EntityKind.Monster) st.Lift = Hop(e, t, _tl.TypeRadii[info.Type] > GruntRadius ? HopM / 3 : HopM);
            return st;
        }
    }
}
