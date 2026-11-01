using System;
using Game.Client.Loop;
using PS.Game.Sim;

namespace Game.Client.Hero
{
    /// <summary>One attack slot of the hero's pose: a clip and the time into it.</summary>
    public struct HeroSlot
    {
        public HeroClip Clip;
        public float Time;
    }

    /// <summary>
    /// The hero at one song time (<see cref="HeroMotion.Evaluate"/>): where it stands, how high it
    /// hops, which way its legs mean to face and how far its upper body twists from them, and its clips'
    /// weights and times. The legs blend the idle, the hop or walk (<see cref="Run"/>, and <see cref="Back"/>
    /// backward) and the attack's full-body share; the torso (hips and spine) the walk's share (<see cref="Hips"/>)
    /// and the sword idle or the attack; the upper body the sword idle's arms and the attack. The attack has two
    /// slots: <see cref="A"/>, and <see cref="B"/>, the swing it crossfades from, by <see cref="AShare"/>.
    /// </summary>
    public struct HeroPose
    {
        public double X, Y;
        /// <summary>The hop's height above the ground (m).</summary>
        public float Lift;
        /// <summary>The facing its legs (its root) turn toward, radians clockwise from north (+y): Unity's yaw.</summary>
        public float Yaw;
        /// <summary>The upper body's turn from the legs (radians, clockwise), spread over the spine: a walking swing's aim.</summary>
        public float Twist;
        /// <summary>The way it travels (its latest step's facing), whatever the swing turns it to.</summary>
        public Direction Facing;
        public float IdleTime;
        /// <summary>The hop's or the forward walk's weight on the legs (0..1) and the time into the run clip.</summary>
        public float Run, RunTime;
        /// <summary>The backpedal's weight on the legs (0..1): the run clip played backward, at <see cref="BackTime"/>.</summary>
        public float Back, BackTime;
        /// <summary>The walk's share of the torso (hips and spine, 0..1); the sword idle or the attack has the rest.</summary>
        public float Hips;
        /// <summary>The attack's weight on the upper body (0..1); the legs take it where the hop doesn't.</summary>
        public float Attack;
        public HeroSlot A, B;
        /// <summary>Slot A's share of the attack (B has the rest).</summary>
        public float AShare;
        /// <summary>The blade's blow flare: up at each impact, gone over <see cref="HeroMotion.FlareS"/>.</summary>
        public float Flare;
        /// <summary>The drawn hero turns to this pose at once rather than easing (<see cref="HeroTurn"/>): a press's strike, quicker than the ease.</summary>
        public bool TurnAtOnce;
    }

    /// <summary>
    /// The drawn hero's turn (<see cref="HeroPresenter"/>): its legs ease to the pose's yaw over <see cref="Seconds"/>,
    /// and its upper body, apart, to the way the pose's upper body faces (the yaw plus the twist), so a walk that
    /// changes direction mid-swing turns the legs under a torso that stays on the strike, rather than swinging the
    /// torso off the aim with the legs' lag. With no twist both ease alike and the eased twist stays none. A press's
    /// strike (<see cref="HeroPose.TurnAtOnce"/>) lands a few frames after the key, too soon for the ease, so through it
    /// both take the pose at once and the blow faces its aim.
    /// </summary>
    public struct HeroTurn
    {
        /// <summary>How quickly the knight turns to the facing it means: a time constant (s).</summary>
        public const double Seconds = 0.04;
        double _legs, _upper;
        bool _set;

        /// <summary>The legs' eased yaw (radians).</summary>
        public float Legs => (float)_legs;

        /// <summary>Eases toward the pose over dt seconds (the first call lands on it) and sets its twist to the eased upper body's turn from the eased legs.</summary>
        public void Ease(ref HeroPose p, double dt)
        {
            double k = _set && !p.TurnAtOnce ? 1 - Math.Exp(-Math.Max(0, dt) / Seconds) : 1;
            _set = true;
            _legs = HeroMotion.Wrap(_legs + HeroMotion.Wrap(p.Yaw - _legs) * k);
            _upper = HeroMotion.Wrap(_upper + HeroMotion.Wrap(p.Yaw + (double)p.Twist - _upper) * k);
            p.Twist = (float)HeroMotion.Wrap(_upper - _legs);
        }
    }

    /// <summary>
    /// The hero's motion on song time (unity-game.md, The Hero; Attacks and Steps Land on the Beat): a
    /// function of the sim's HeroStep and hero Attack events and the song time, with no state machine,
    /// so the client never guesses. Events arrive ahead of the time they show (by the sim's lead),
    /// except the hero's steps, judged in heard time: a press's arrives on the key-down frame and
    /// starts at the press, a held key's about as its step is heard.
    /// - **Steps**: along each step's move from its start to its end (native's 110 ms, or compressed
    ///   to end before the next step), a hop: the run clip's half-cycle, one foot then the other,
    ///   with a parabolic lift.
    ///   A bump hops in place, lower; a stomp stands. The hero faces its travel.
    /// - **Glides** (the free hero's, <see cref="HeroGlideEvent"/>): along each glide from its start at its velocity;
    ///   a glide replaces those from its start on (a heard input's re-walk). Walking, the run clip cycles once per
    ///   <see cref="CycleM"/> walked, as a grid stride's half-cycle covers a tile, its weight up over
    ///   <see cref="HopIn"/> and down over <see cref="HopOut"/>, with no hop; the hero faces its travel, and
    ///   stopped, the way it last went.
    /// - **Attacks**: each clip is retimed so its impact frame lands on the attack's impact
    ///   (<see cref="HeroClips.Fit"/>), entered no earlier than the wind-up, so the swing before it keeps its own
    ///   impact, and showing on its entry frame. Slashes alternate left and right; a follow-up continues the combo
    ///   from the swing before (left, right, then the third); heavy strikes spin and section changes slam,
    ///   holding 50 ms at 0.6x after the impact as demo3d's heavy blows do. Standing, the hero turns to the
    ///   swing's direction from its wind-up to its follow-through.
    /// - **Presses** (the free mode's, <see cref="AttackEvent.Pressed"/>; game-modes.md, Attacking on the beat): a press
    ///   hits at once (the sim lands it two frames after the key, or a tick after it takes the key), its wind-up from
    ///   the press in heard time, so it arrives on the key-down frame, as a pressed step does. Its clip plays up to <see cref="HeroClips.PressSpeed"/>, entered just short of its
    ///   impact frame, and follows through as fast; every press holds <see cref="HitStop"/> at
    ///   <see cref="PressSlow"/> on its impact for punch, and the drawn hero turns to it at once
    ///   (<see cref="HeroPose.TurnAtOnce"/>). Its blade flares by its grade: brighter on the beat
    ///   (<see cref="OnBeatFlare"/>), dimmer off it.
    /// - **Swings on the walk** (game-modes.md, Swinging on the move): the legs keep walking and carry the hips
    ///   (<see cref="HeroPose.Hips"/>), facing the travel, and the upper body twists to the swing's aim, eased in over
    ///   the first <see cref="TwistLead"/> of its wind-up (the torso leads the blade) and out over <see cref="TwistOut"/>
    ///   after its follow-through. Up to <see cref="TwistMax"/> the twist alone reaches the aim; past it the twist
    ///   unwinds and the whole body turns, until a strike straight behind has none, the legs on the aim and
    ///   backpedalling. Legs turned off the travel blend from the forward run to the backpedal (the run clip
    ///   played backward) through the attack's own footwork, square to the travel: the Synty packs have no
    ///   strafe or backpedal clips.
    /// - **Dashes** (`--dash`, <see cref="HeroDashEvent"/>; game-modes.md, The dash): the glides carry the hero; from the
    ///   dash's start (its key, in heard time) to its end the knight faces the dash at once, untwisted, its legs held in
    ///   a stride (<see cref="DashStride"/>) rather than cycling at the dash's speed, lifted in a low arc: the lunge on the
    ///   beat skims (<see cref="LungeLiftM"/>) with its blade alight, the hop off it jumps (<see cref="HopLiftM"/>). Over
    ///   <see cref="DashOut"/> after it lands the walk's pose takes back over.
    /// </summary>
    public sealed class HeroMotion
    {
        /// <summary>The hop's peak (m), and a bump's.</summary>
        public const float HopM = 0.14f, BumpHopM = 0.07f;
        /// <summary>The hop's weight on the legs: up over this from the step's start, down over this after its end.</summary>
        public const double HopIn = 0.03, HopOut = 0.1;
        /// <summary>An attack's blend in (at most half its time to impact, so the impact pose is whole) and out after its follow-through.</summary>
        public const double AttackIn = 0.08, AttackOut = 0.2;
        /// <summary>The attack's blend on its entry frame: a 60 Hz frame's step in, so the entry frame already shows it.</summary>
        const double EntryStep = 1.0 / 60;
        public const double FlareS = 0.22;
        /// <summary>A heavy blow's flare, and a full-grade (on the beat) press's; an off-beat press's is its grade.</summary>
        public const float HeavyFlare = 1.5f, OnBeatFlare = 1.25f;
        /// <summary>demo3d's heavy blow: from the impact the clip runs at 0.6x for 50 ms, then catches up over 0.15 s.</summary>
        public const double HitStop = 0.05, CatchUp = 0.15;
        public const float HeavySlow = 0.6f;
        /// <summary>A press's hit-stop: from its impact the clip all but stops for <see cref="HitStop"/>, against its fast strike.</summary>
        public const float PressSlow = 0.25f;
        /// <summary>A song time this close short of an impact has reached it: a frame clock's n/60 can fall a rounding short.</summary>
        const double Reached = 1e-6;
        const int Keep = 8;
        /// <summary>The glides kept: a target steered round a wall changes velocity each tick, and the pose lags the sim by the lead.</summary>
        const int GlideKeep = 64;
        /// <summary>The walk a run cycle covers (m): two of the grid's tiles, a stride a foot.</summary>
        public const double CycleM = 3.0;
        /// <summary>The upper body's twist from the walking legs at most (rad, 120 degrees): past it the whole body turns.</summary>
        public const double TwistMax = 2 * Math.PI / 3;
        /// <summary>The twist reaches a swing's aim this share of the way through its wind-up, so the torso leads the blade.</summary>
        public const double TwistLead = 0.6;
        /// <summary>The twist unwinds over this after the swing's follow-through (s).</summary>
        public const double TwistOut = 0.25;
        /// <summary>A dash's arc at its peak (m): the lunge on the beat skims, the hop off it jumps.</summary>
        public const float LungeLiftM = 0.1f, HopLiftM = 0.3f;
        /// <summary>The run clip's point the legs hold through a dash: a share of its cycle, a long stride.</summary>
        public const float DashStride = 0.2f;
        /// <summary>The walk's pose takes back over this long after a dash lands (s).</summary>
        public const double DashOut = 0.12;

        struct Step
        {
            public double FromX, FromY, ToX, ToY, Start, End;
            public bool Bump, Stomp, Pressed;
            public Direction Facing;
            public int Foot;
        }

        struct Swing
        {
            public HeroClip Clip;
            public HeroClips.Retime Clock;
            public double Entry, Impact, End;
            public bool Heavy, Pressed;
            /// <summary>A press's beat grade (0.5 to 1); 0 for a strike the beat planned.</summary>
            public float Grade;
            public float Yaw;
            public bool Aimed;
            /// <summary>The twist's weight carried in from the aimed swing before, at this one's entry.</summary>
            public double Carry;
        }

        struct DashMove
        {
            public double Start, End;
            public bool OnBeat;
            public float Yaw;
        }

        struct Glide
        {
            public double Start, FromX, FromY, VX, VY;
            /// <summary>The walk before its start, for the run cycle's phase.</summary>
            public double Walked;
            public float Yaw;
            public bool Moving;
            public Direction Facing;
        }

        readonly HeroClips _clips;
        readonly Glide[] _glides = new Glide[GlideKeep];
        int _glideCount;
        readonly Step[] _steps = new Step[Keep];
        readonly Swing[] _swings = new Swing[Keep];
        readonly DashMove[] _dashes = new DashMove[Keep];
        int _stepCount, _swingCount, _dashCount;
        // The latest step whose start and landing, and the latest impact whose flare, the trace has (Moments).
        int _startMarked = -1, _landMarked = -1;
        double _flareMarked = double.NegativeInfinity;

        public HeroMotion(HeroClips clips) { _clips = clips; }

        public void OnEvent(in SimEvent e)
        {
            if (e.Kind == SimEventKind.HeroStep) OnStep(e.AsHeroStep);
            else if (e.Kind == SimEventKind.HeroGlide) OnGlide(e.AsHeroGlide);
            else if (e.Kind == SimEventKind.Attack && e.AsAttack.IsSword) OnAttack(e.AsAttack);
            else if (e.Kind == SimEventKind.HeroDash) OnDash(e.AsHeroDash);
        }

        public void OnDash(in HeroDashEvent e)
        {
            _dashes[_dashCount % Keep] = new DashMove
            {
                Start = e.Start, End = e.End, OnBeat = e.OnBeat, Yaw = (float)Math.Atan2(e.DirX, e.DirY),
            };
            _dashCount++;
        }

        public void OnStep(in HeroStepEvent e)
        {
            _steps[_stepCount % Keep] = new Step
            {
                FromX = e.FromX, FromY = e.FromY, ToX = e.ToX, ToY = e.ToY, Start = e.Start, End = e.End,
                Bump = e.Bump, Stomp = e.Stomp, Pressed = e.Pressed, Facing = e.Facing, Foot = _stepCount & 1,
            };
            _stepCount++;
        }

        public void OnGlide(in HeroGlideEvent e)
        {
            while (_glideCount > 0 && _glideCount > OldestGlide && _glides[(_glideCount - 1) % GlideKeep].Start >= e.Start - 1e-9) _glideCount--;
            bool moving = e.VX * e.VX + e.VY * e.VY > 1e-12;
            var g = new Glide
            {
                Start = e.Start, FromX = e.FromX, FromY = e.FromY, VX = e.VX, VY = e.VY, Moving = moving, Facing = e.Facing,
                Yaw = moving ? (float)Math.Atan2(e.VX, e.VY) : YawOf(e.Facing),
            };
            if (_glideCount > OldestGlide)
            {
                var prev = _glides[(_glideCount - 1) % GlideKeep];
                if (!moving) g.Yaw = prev.Yaw;
                g.Walked = prev.Walked + Math.Sqrt(prev.VX * prev.VX + prev.VY * prev.VY) * (e.Start - prev.Start);
            }
            _glides[_glideCount % GlideKeep] = g;
            _glideCount++;
        }

        /// <summary>The oldest glide still in the ring: a re-walk never reaches back past it.</summary>
        int OldestGlide => Math.Max(0, _glideCount - GlideKeep + 1);

        public void OnAttack(in AttackEvent e)
        {
            var clip = ClipFor(e.SwordKind, _swingCount > 0 ? _swings[(_swingCount - 1) % Keep].Clip : (HeroClip?)null);
            var ann = _clips[clip];
            var clock = HeroClips.Fit(ann.impact, e.WindupStart, e.Impact, e.Pressed ? HeroClips.PressSpeed : HeroClips.MaxSpeed);
            bool aimed = e.DirX != 0 || e.DirY != 0;
            var swing = new Swing
            {
                Clip = clip, Clock = clock, Entry = Math.Max(clock.Start, e.WindupStart), Impact = e.Impact,
                End = clock.TimeAt(ann.end), Heavy = e.Heavy, Pressed = e.Pressed, Grade = (float)e.Grade,
                Yaw = aimed ? (float)Math.Atan2(e.DirX, e.DirY) : 0f, Aimed = aimed,
            };
            for (int i = _swingCount - 1; aimed && i >= Math.Max(0, _swingCount - Keep); i--)
                if (_swings[i % Keep].Aimed)
                {
                    swing.Carry = TwistWeight(_swings[i % Keep], swing.Entry);
                    break;
                }
            _swings[_swingCount % Keep] = swing;
            _swingCount++;
        }

        /// <summary>The clip an attack plays: a follow-up continues the light combo from the swing before.</summary>
        public static HeroClip ClipFor(HeroAttackKind kind, HeroClip? previous)
        {
            switch (kind)
            {
                case HeroAttackKind.SlashRight: return HeroClip.SlashR;
                case HeroAttackKind.Spin: return HeroClip.Spin;
                case HeroAttackKind.Slam: return HeroClip.Slam;
                case HeroAttackKind.FollowUp:
                    switch (previous)
                    {
                        case HeroClip.SlashL: return HeroClip.SlashR;
                        case HeroClip.SlashR: return HeroClip.Slash3;
                        case HeroClip.Slash3: return HeroClip.SlashR;
                        default: return HeroClip.SlashL;
                    }
                default: return HeroClip.SlashL;
            }
        }

        /// <summary>
        /// The hero at song time t. Before its first step it stands at (<paramref name="x"/>,
        /// <paramref name="y"/>) facing <paramref name="facing"/> (the sim's snapshot).
        /// </summary>
        public HeroPose Evaluate(double t, double x, double y, Direction facing)
        {
            var p = new HeroPose { X = x, Y = y, Yaw = YawOf(facing), Facing = facing, AShare = 1f };
            p.IdleTime = (float)Repeat(t, _clips[HeroClip.Idle].length);
            bool walking = _glideCount > 0;
            if (walking) Walk(t, ref p);
            else Legs(t, ref p);
            float standing = Swings(t, ref p);
            if (walking) Torso(t, ref p, standing);
            else p.Yaw = standing;
            Dash(t, ref p);
            return p;
        }

        /// <summary>The dash's pose over the walk's (<see cref="HeroMotion"/>, Dashes), and back over <see cref="DashOut"/>.</summary>
        void Dash(double t, ref HeroPose p)
        {
            int k = Latest(_dashes, _dashCount, t, (in DashMove d) => d.Start);
            if (k < 0) return;
            var d = _dashes[k % Keep];
            if (t >= d.End + DashOut) return;
            double w = t <= d.End ? 1 : 1 - (t - d.End) / DashOut;
            p.Yaw = (float)Wrap(LerpAngle(p.Yaw, d.Yaw, w));
            p.Twist = (float)(p.Twist * (1 - w));
            p.Run = (float)Math.Max(p.Run, w);
            p.Back = (float)(p.Back * (1 - w));
            p.Hips = (float)Math.Max(p.Hips, w);
            if (t > d.End) return;
            double u = Clamp01((t - d.Start) / Math.Max(1e-6, d.End - d.Start));
            p.Lift = (float)Math.Max(p.Lift, (d.OnBeat ? LungeLiftM : HopLiftM) * 4 * u * (1 - u));
            p.RunTime = DashStride * _clips[HeroClip.Run].length;
            p.TurnAtOnce = true;
            if (d.OnBeat) p.Flare = Math.Max(p.Flare, OnBeatFlare);
        }

        /// <summary>
        /// The moments the pose at song time t first shows, into the on-the-beat trace (<see cref="Moments"/>): the
        /// latest step setting off and touching down (one the next cuts short never lands), and the blade's flare
        /// on the latest impact.
        /// </summary>
        public void MarkMoments(double t)
        {
            int k = Latest(_steps, _stepCount, t, (in Step s) => s.Start);
            if (k >= 0)
            {
                var s = _steps[k % Keep];
                if (k > _startMarked)
                {
                    _startMarked = k;
                    if (!s.Stomp) Moments.Mark(s.Pressed ? MomentKind.HeroPress : MomentKind.HeroStride, s.Start, k);
                }
                if (k > _landMarked && t >= s.End && !s.Stomp)
                {
                    _landMarked = k;
                    Moments.Mark(MomentKind.HeroLand, s.End, k);
                }
            }
            for (int i = Math.Max(0, _swingCount - Keep); i < _swingCount; i++)
            {
                var s = _swings[i % Keep];
                if (s.Impact <= t + Reached && s.Impact > _flareMarked)
                {
                    _flareMarked = s.Impact;
                    Moments.Mark(MomentKind.Blade, s.Impact, i);
                }
            }
        }

        void Legs(double t, ref HeroPose p)
        {
            int k = Latest(_steps, _stepCount, t, (in Step s) => s.Start);
            if (k < 0) return;
            var s = _steps[k % Keep];
            double span = Math.Max(1e-6, s.End - s.Start);
            double u = Clamp01((t - s.Start) / span);
            p.X = s.FromX + (s.ToX - s.FromX) * u;
            p.Y = s.FromY + (s.ToY - s.FromY) * u;
            p.Facing = s.Facing;
            p.Yaw = YawOf(s.Facing);
            if (s.Stomp) return;
            p.Lift = (float)((s.Bump ? BumpHopM : HopM) * 4 * u * (1 - u));
            // A stride follows the one before before its weight is gone: it carries that weight on.
            double carried = 0;
            if (k > 0 && k - 1 >= _stepCount - Keep)
            {
                var prev = _steps[(k - 1) % Keep];
                if (!prev.Stomp) carried = 1 - Clamp01((s.Start - prev.End) / HopOut);
            }
            double w = t <= s.End ? carried + (1 - carried) * Clamp01((t - s.Start) / HopIn) : 1 - Clamp01((t - s.End) / HopOut);
            p.Run = (float)w;
            p.RunTime = (float)((s.Foot * 0.5 + u * 0.5) * _clips[HeroClip.Run].length);
        }

        void Walk(double t, ref HeroPose p)
        {
            int k = -1;
            for (int i = _glideCount - 1; i >= OldestGlide - 1 && i >= 0; i--)
                if (_glides[i % GlideKeep].Start <= t) { k = i; break; }
            if (k < 0) return;
            var g = _glides[k % GlideKeep];
            double since = t - g.Start;
            p.X = g.FromX + g.VX * since;
            p.Y = g.FromY + g.VY * since;
            p.Facing = g.Facing;
            p.Yaw = g.Yaw;
            bool wasMoving = k > 0 && k - 1 >= OldestGlide - 1 && _glides[(k - 1) % GlideKeep].Moving;
            if (g.Moving)
                p.Run = (float)(wasMoving ? 1 : Clamp01(since / HopIn));
            else
                p.Run = (float)(wasMoving ? 1 - Clamp01(since / HopOut) : 0);
            double walked = g.Walked + Math.Sqrt(g.VX * g.VX + g.VY * g.VY) * since;
            p.RunTime = (float)(Repeat(walked / CycleM, 1) * _clips[HeroClip.Run].length);
            p.BackTime = (float)(Repeat(-walked / CycleM, 1) * _clips[HeroClip.Run].length);
        }

        /// <summary>The attack's slots and weight, and the flare; returns the yaw a standing hero turns to (its swing's, or <see cref="HeroPose.Yaw"/>).</summary>
        float Swings(double t, ref HeroPose p)
        {
            double latestImpact = double.NegativeInfinity;
            float peak = 1f;
            for (int i = _swingCount - 1; i >= 0 && i >= _swingCount - Keep; i--)
            {
                var s = _swings[i % Keep];
                if (s.Impact <= t + Reached && s.Impact > latestImpact)
                {
                    latestImpact = s.Impact;
                    peak = s.Heavy ? HeavyFlare : !s.Pressed ? 1f : s.Grade >= 1 ? OnBeatFlare : s.Grade;
                }
            }
            double since = Math.Max(0, t - latestImpact);
            if (since < FlareS) p.Flare = (float)(1 - since / FlareS) * peak;

            int k = Latest(_swings, _swingCount, t, (in Swing s) => s.Entry);
            if (k < 0) return p.Yaw;
            var a = _swings[k % Keep];
            // The blend's first step shows on the entry frame itself: a press moves the hero on its key-down frame.
            double ramp = Clamp01((t - a.Entry + EntryStep) / (EntryStep + Math.Max(1e-3, Math.Min(AttackIn, 0.5 * (a.Impact - a.Entry)))));
            double carried = 0;
            if (k > 0 && k - 1 >= _swingCount - Keep)
            {
                var b = _swings[(k - 1) % Keep];
                carried = 1 - Clamp01((a.Entry - b.End) / AttackOut);
                if (carried > 0)
                {
                    p.B = SlotAt(b, t);
                    p.AShare = (float)ramp;
                }
            }
            p.A = SlotAt(a, t);
            p.Attack = (float)((carried + (1 - carried) * ramp) * (1 - Clamp01((t - a.End) / AttackOut)));
            p.TurnAtOnce = a.Pressed && t <= a.Impact + Reached;
            return a.Aimed && t < a.End + AttackOut * 0.5 ? a.Yaw : p.Yaw;
        }

        /// <summary>
        /// The walking hero's legs and upper body (<see cref="HeroMotion"/>, Swings on the walk), from the walk's
        /// weight (<see cref="HeroPose.Run"/> on entry) and facing: blended toward the standing hero's
        /// (<paramref name="standing"/>, no twist) as the walk's weight goes.
        /// </summary>
        void Torso(double t, ref HeroPose p, float standing)
        {
            double walk = p.Run, travel = p.Yaw;
            double upper = UpperYaw(t, travel);
            double d = Wrap(upper - travel), twist = d, legs = travel;
            if (Math.Abs(d) > TwistMax)
            {
                double past = (Math.Abs(d) - TwistMax) / (Math.PI - TwistMax);
                twist = Math.Sign(d) * TwistMax * (1 - past);
                legs = upper - twist;
            }
            double ahead = Math.Cos(legs - travel);
            double forward = Math.Max(0, ahead), back = Math.Max(0, -ahead);
            p.Yaw = (float)Wrap(LerpAngle(standing, legs, walk));
            p.Twist = (float)(twist * walk);
            p.Run = (float)(walk * forward);
            p.Back = (float)(walk * back);
            p.Hips = (float)(walk * (forward + back));
        }

        /// <summary>
        /// The way the walking hero's upper body faces at song time t: the travel, turned toward the aimed swings' aims
        /// (each from the one before as its twist comes in) by the latest aimed swing's twist weight. The chain reaches
        /// back only through swings that carried a twist in: one entering after the twist before has unwound starts
        /// from its own aim, so an old swing's aim never bends its ease in.
        /// </summary>
        double UpperYaw(double t, double travel)
        {
            int latest = -1, first = -1;
            for (int i = _swingCount - 1; i >= Math.Max(0, _swingCount - Keep); i--)
            {
                var s = _swings[i % Keep];
                if (!s.Aimed || s.Entry > t) continue;
                if (latest < 0) latest = i;
                first = i;
                if (s.Carry <= 0 || TwistIn(s, t) >= 1) break;
            }
            double weight = latest < 0 ? 0 : TwistWeight(_swings[latest % Keep], t);
            if (weight <= 0) return travel;
            double aim = _swings[first % Keep].Yaw;
            for (int i = first + 1; i <= latest; i++)
            {
                var s = _swings[i % Keep];
                if (s.Aimed) aim = LerpAngle(aim, s.Yaw, TwistIn(s, t));
            }
            return LerpAngle(travel, aim, weight);
        }

        /// <summary>A swing's twist coming in: eased over the first <see cref="TwistLead"/> of its wind-up.</summary>
        static double TwistIn(in Swing s, double t) =>
            Smooth(Clamp01((t - s.Entry) / Math.Max(1e-3, TwistLead * (s.Impact - s.Entry))));

        /// <summary>
        /// A swing's twist weight at song time t: up from the weight carried in from the swing before (so a chain of swings
        /// never lets the twist go between them), eased out over <see cref="TwistOut"/> after its follow-through.
        /// </summary>
        static double TwistWeight(in Swing s, double t) =>
            (s.Carry + (1 - s.Carry) * TwistIn(s, t)) * (1 - Smooth(Clamp01((t - s.End) / TwistOut)));

        HeroSlot SlotAt(in Swing s, double t)
        {
            double local = s.Clock.ClipTime(s.Pressed ? HoldThenCatchUp(t, s.Impact, PressSlow) : s.Heavy ? HoldThenCatchUp(t, s.Impact, HeavySlow) : t);
            return new HeroSlot { Clip = s.Clip, Time = (float)Math.Min(_clips[s.Clip].length, Math.Max(0, local)) };
        }

        /// <summary>The time into a swing's clip at song time t (for its tests and evidence): its retimed clock.</summary>
        public bool ClipTime(int swing, double t, out HeroClip clip, out float time)
        {
            clip = default;
            time = 0;
            if (swing < Math.Max(0, _swingCount - Keep) || swing >= _swingCount) return false;
            var s = _swings[swing % Keep];
            var slot = SlotAt(s, t);
            clip = slot.Clip;
            time = slot.Time;
            return true;
        }

        public int SwingCount => _swingCount;

        delegate double TimeOf<T>(in T item);

        /// <summary>The newest kept item whose time is at or before t, or -1.</summary>
        static int Latest<T>(T[] ring, int count, double t, TimeOf<T> time)
        {
            for (int i = count - 1; i >= 0 && i >= count - Keep; i--)
                if (time(ring[i % Keep]) <= t) return i;
            return -1;
        }

        /// <summary>demo3d's local hit-stop: from the impact the clip runs at `rate` for 50 ms, then catches up over 0.15 s.</summary>
        public static double HoldThenCatchUp(double t, double impact, float rate)
        {
            double u = t - impact;
            if (u <= 0) return t;
            double lag = HitStop * (1 - rate);
            if (u < HitStop) return impact + u * rate;
            if (u < HitStop + CatchUp) return t - lag * (1 - (u - HitStop) / CatchUp);
            return t;
        }

        public static float YawOf(Direction d) =>
            d == Direction.Right ? (float)(Math.PI / 2) : d == Direction.Down ? (float)Math.PI : d == Direction.Left ? (float)(-Math.PI / 2) : 0f;

        static double Clamp01(double v) => v < 0 ? 0 : v > 1 ? 1 : v;

        static double Smooth(double u) => u * u * (3 - 2 * u);

        /// <summary>An angle wrapped into (-pi, pi].</summary>
        internal static double Wrap(double a)
        {
            a = Math.IEEERemainder(a, 2 * Math.PI);
            return a <= -Math.PI ? a + 2 * Math.PI : a;
        }

        /// <summary>From angle a toward b the short way round, by u.</summary>
        static double LerpAngle(double a, double b, double u) => a + Wrap(b - a) * u;

        static double Repeat(double t, double length) => length <= 0 ? 0 : t - Math.Floor(t / length) * length;
    }
}
