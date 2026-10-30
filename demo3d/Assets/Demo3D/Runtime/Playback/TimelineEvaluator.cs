using PS.Choreography;
using UnityEngine;

namespace Demo3D.Runtime.Playback
{
    /// <summary>
    /// The client's timeline evaluation (TD4): the shared <see cref="ClipSchedule"/> drawn as two
    /// animation slots. Every entity's state is a function of the timeline and the song time, so
    /// a seek shows the right horde at once.
    ///
    /// - Position and facing come from the entity's segments.
    /// - Each clip action plays at its schedule's local time (retimed so its blow lands on its
    ///   impact, with the hit-stop and the hero's heavy-blow slowdown).
    /// - Between actions the entity walks, the walk's phase following the distance it has
    ///   travelled, so feet don't slide; standing still freezes the phase.
    /// - Two slots: when the clip changes, the leaving clip keeps playing and crossfades out
    ///   over 0.12 s.
    /// - A death rests on its clip's last pose; the schedule's flash and fade become the hit
    ///   flash and the dissolve.
    /// </summary>
    public sealed class TimelineEvaluator
    {
        public const float Crossfade = 0.12f;

        readonly Timeline _tl;
        readonly ClipLibrary _clips;
        readonly ClipSchedule _schedule;

        // Per clip annotation: the role ("walk", "attack", ...) and owner ("skeleton", "hero").
        readonly string[] _role, _owner;

        public Timeline Timeline => _tl;
        public ClipSchedule Schedule => _schedule;

        public TimelineEvaluator(Timeline timeline, ClipLibrary clips)
        {
            _tl = timeline;
            _clips = clips;
            _schedule = new ClipSchedule(timeline, clips, slowHeavyBlows: true);
            _role = new string[clips.Count];
            _owner = new string[clips.Count];
            for (int c = 0; c < clips.Count; c++)
            {
                var use = clips[c].Use;
                int space = use.LastIndexOf(' ');
                _owner[c] = space > 0 ? use.Substring(0, space) : use;
                _role[c] = space > 0 ? use.Substring(space + 1) : use;
            }
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
            /// <summary>A death resting on its clip's last pose: the hold and the dissolve (the crystal shatter's swap, CrystalShatter).</summary>
            public bool Held;
        }

        Slot SlotFor(int action, int e, double t, float walkStride, float walkLength)
        {
            if (action < 0)
            {
                float phase = walkStride > 0 ? _schedule.DistanceAt(e, t) / walkStride : 0f;
                return new Slot { Clip = -1, Time = (phase - Mathf.Floor(phase)) * walkLength };
            }
            var a = _tl.Actions[action];
            var ann = _clips[a.Clip];
            float time = (float)_schedule.LocalTime(action, t);
            if (a.Kind == ActionKind.Death) time = Mathf.Min(time, Mathf.Max(ann.End, ann.Impact));
            return new Slot { Clip = a.Clip, Time = time };
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

            int act = _schedule.ActiveAction(e, t, out double since);
            st.A = SlotFor(act, e, t, walkStride, walkLength);
            if (act >= 0 && _tl.Actions[act].Kind == ActionKind.Death)
            {
                var ann = _clips[_tl.Actions[act].Clip];
                st.Held = st.A.Time >= Mathf.Max(ann.End, ann.Impact);
            }
            st.Blend = 1f;
            st.B = st.A;
            if (t - since < Crossfade && !double.IsInfinity(since))
            {
                int prev = _schedule.ActiveAction(e, since - 1e-4, out _);
                if (prev != act)
                {
                    st.B = SlotFor(prev, e, t, walkStride, walkLength);
                    st.Blend = (float)((t - since) / Crossfade);
                }
            }

            var fb = _schedule.FeedbackAt(e, t);
            st.HitFlash = fb.Flash;
            st.Dissolve = fb.Fade;
            st.Dying = fb.Dying;
            return st;
        }
    }
}
