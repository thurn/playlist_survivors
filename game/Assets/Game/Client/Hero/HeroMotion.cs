using System;
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
    /// hops, which way it means to face, and its clips' weights and times. The legs blend the idle,
    /// the hop (<see cref="Run"/>) and the attack's full-body share; the upper body the sword idle's arms
    /// and the attack. The attack has two slots: <see cref="A"/>, and <see cref="B"/>, the swing it
    /// crossfades from, by <see cref="AShare"/>.
    /// </summary>
    public struct HeroPose
    {
        public double X, Y;
        /// <summary>The hop's height above the ground (m).</summary>
        public float Lift;
        /// <summary>The facing it turns toward, radians clockwise from north (+y): Unity's yaw.</summary>
        public float Yaw;
        /// <summary>The way it travels (its latest step's facing), whatever the swing turns it to.</summary>
        public Direction Facing;
        public float IdleTime;
        /// <summary>The hop's weight on the legs (0..1) and the time into the run clip.</summary>
        public float Run, RunTime;
        /// <summary>The attack's weight on the upper body (0..1); the legs take it where the hop doesn't.</summary>
        public float Attack;
        public HeroSlot A, B;
        /// <summary>Slot A's share of the attack (B has the rest).</summary>
        public float AShare;
        /// <summary>The blade's blow flare: up at each impact, gone over <see cref="HeroMotion.FlareS"/>.</summary>
        public float Flare;
    }

    /// <summary>
    /// The hero's motion on song time (unity-game.md, The Hero; Attacks and Steps Land on the Beat): a
    /// function of the sim's HeroStep and hero Attack events and the song time, with no state machine,
    /// so the client never guesses. Events arrive ahead of the time they show (by the sim's lead).
    /// - **Steps**: along each step's move from its start to its end (native's 110 ms, a late stride's
    ///   compressed), a hop: the run clip's half-cycle, one foot then the other, with a parabolic lift.
    ///   A bump hops in place, lower; a stomp stands. The hero faces its travel.
    /// - **Attacks**: each clip is retimed so its impact frame lands on the attack's impact
    ///   (<see cref="HeroClips.Fit"/>), entered no earlier than the wind-up, so the swing before it
    ///   keeps its own impact. Slashes alternate left and right; a follow-up continues the combo from
    ///   the swing before (left, right, then the third); heavy strikes spin and section changes slam,
    ///   holding 50 ms at 0.6x after the impact as demo3d's heavy blows do. The hero turns to the
    ///   swing's direction from its wind-up to its follow-through.
    /// </summary>
    public sealed class HeroMotion
    {
        /// <summary>The hop's peak (m), and a bump's.</summary>
        public const float HopM = 0.14f, BumpHopM = 0.07f;
        /// <summary>The hop's weight on the legs: up over this from the step's start, down over this after its end.</summary>
        public const double HopIn = 0.03, HopOut = 0.1;
        /// <summary>An attack's blend in (at most half its time to impact, so the impact pose is whole) and out after its follow-through.</summary>
        public const double AttackIn = 0.08, AttackOut = 0.2;
        public const double FlareS = 0.22;
        public const float HeavyFlare = 1.5f;
        /// <summary>demo3d's heavy blow: from the impact the clip runs at 0.6x for 50 ms, then catches up over 0.15 s.</summary>
        public const double HitStop = 0.05, CatchUp = 0.15;
        public const float HeavySlow = 0.6f;
        /// <summary>A song time this close short of an impact has reached it: a frame clock's n/60 can fall a rounding short.</summary>
        const double Reached = 1e-6;
        const int Keep = 8;

        struct Step
        {
            public double FromX, FromY, ToX, ToY, Start, End;
            public bool Bump, Stomp;
            public Direction Facing;
            public int Foot;
        }

        struct Swing
        {
            public HeroClip Clip;
            public double Start, Entry, Impact, End;
            public float Speed;
            public bool Heavy;
            public float Yaw;
            public bool Aimed;
        }

        readonly HeroClips _clips;
        readonly Step[] _steps = new Step[Keep];
        readonly Swing[] _swings = new Swing[Keep];
        int _stepCount, _swingCount;

        public HeroMotion(HeroClips clips) { _clips = clips; }

        public void OnEvent(in SimEvent e)
        {
            if (e.Kind == SimEventKind.HeroStep) OnStep(e.AsHeroStep);
            else if (e.Kind == SimEventKind.Attack && e.AsAttack.IsSword) OnAttack(e.AsAttack);
        }

        public void OnStep(in HeroStepEvent e)
        {
            _steps[_stepCount % Keep] = new Step
            {
                FromX = e.FromX, FromY = e.FromY, ToX = e.ToX, ToY = e.ToY, Start = e.Start, End = e.End,
                Bump = e.Bump, Stomp = e.Stomp, Facing = e.Facing, Foot = _stepCount & 1,
            };
            _stepCount++;
        }

        public void OnAttack(in AttackEvent e)
        {
            var clip = ClipFor(e.SwordKind, _swingCount > 0 ? _swings[(_swingCount - 1) % Keep].Clip : (HeroClip?)null);
            var ann = _clips[clip];
            HeroClips.Fit(ann.impact, e.WindupStart, e.Impact, out double start, out float speed);
            bool aimed = e.DirX != 0 || e.DirY != 0;
            _swings[_swingCount % Keep] = new Swing
            {
                Clip = clip, Start = start, Entry = Math.Max(start, e.WindupStart), Impact = e.Impact,
                End = start + ann.end / speed, Speed = speed, Heavy = e.Heavy,
                Yaw = aimed ? (float)Math.Atan2(e.DirX, e.DirY) : 0f, Aimed = aimed,
            };
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
            Legs(t, ref p);
            Swings(t, ref p);
            return p;
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

        void Swings(double t, ref HeroPose p)
        {
            double latestImpact = double.NegativeInfinity;
            bool heavy = false;
            for (int i = _swingCount - 1; i >= 0 && i >= _swingCount - Keep; i--)
            {
                var s = _swings[i % Keep];
                if (s.Impact <= t + Reached && s.Impact > latestImpact) { latestImpact = s.Impact; heavy = s.Heavy; }
            }
            double since = Math.Max(0, t - latestImpact);
            if (since < FlareS) p.Flare = (float)(1 - since / FlareS) * (heavy ? HeavyFlare : 1f);

            int k = Latest(_swings, _swingCount, t, (in Swing s) => s.Entry);
            if (k < 0) return;
            var a = _swings[k % Keep];
            double ramp = Clamp01((t - a.Entry) / Math.Max(1e-3, Math.Min(AttackIn, 0.5 * (a.Impact - a.Entry))));
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
            if (a.Aimed && t < a.End + AttackOut * 0.5) p.Yaw = a.Yaw;
        }

        HeroSlot SlotAt(in Swing s, double t)
        {
            double local = (s.Heavy ? HoldThenCatchUp(t, s.Impact, HeavySlow) : t) - s.Start;
            return new HeroSlot { Clip = s.Clip, Time = (float)Math.Min(_clips[s.Clip].length, Math.Max(0, local * s.Speed)) };
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

        static double Repeat(double t, double length) => length <= 0 ? 0 : t - Math.Floor(t / length) * length;
    }
}
