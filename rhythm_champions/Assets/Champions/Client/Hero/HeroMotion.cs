using System;
using UnityEngine;

namespace Champions.Client.Hero
{
    /// <summary>The hero's clips by role (<see cref="Knight"/>'s): each one's index and annotations.</summary>
    public sealed class HeroClips
    {
        public readonly struct Clip
        {
            public readonly int Index;
            public readonly ClipInfo Info;

            public Clip(int index, ClipInfo info)
            {
                Index = index;
                Info = info;
            }

            public float Length => Info.Length;
            public float Impact => Info.Impact;
            public float End => Info.End;
        }

        public readonly Clip Idle, Run, Back, Left, Right, SlashB, SlashA, Spin, Stab, Leap, Parry, Dodge;

        /// <summary>
        /// The knight's clips (Synty's Polygon Knights, Sword Slash's set; design.md, The Hero &gt; Animation, and Dashes): the
        /// sword idle; the legs' runs, the sprint forward (3.9 m a 0.6 s cycle: 6.5 m/s, near the hero's 6) and the runs
        /// backward and to each side (for an aim the torso can't twist to); LightCombo01A and LightCombo01B and
        /// HeavyFlourish01 (the spin); Blade Rush's HeavyStab01, Leap Slam's LightLeaping01, Shield Charge's forward parry and
        /// the plain Dash's forward dodge. Impacts and ends are demo3d's measurements (the right hand's peak speed).
        /// </summary>
        public static ClipInfo[] Knight() => new[]
        {
            new ClipInfo("idle", "A_Idle_Base_Sword", 1.767f, loop: true),
            new ClipInfo("run", "A_Sprint_F_Masc", 0.6f, loop: true, strideM: 3.9f),
            new ClipInfo("back", "A_Run_BckStrafeB_Masc", 0.7f, loop: true, strideM: 1.73f),
            new ClipInfo("left", "A_Run_FwdStrafeL_Masc", 0.7f, loop: true, strideM: 1.7f),
            new ClipInfo("right", "A_Run_FwdStrafeR_Masc", 0.7f, loop: true, strideM: 1.71f),
            new ClipInfo("slashB", "A_Attack_LightCombo01B_Sword", 0.667f, 0.267f, 0.45f),
            new ClipInfo("slashA", "A_Attack_LightCombo01A_Sword", 0.8f, 0.35f, 0.717f),
            new ClipInfo("spin", "A_Attack_HeavyFlourish_Sword", 2.033f, 1.433f, 1.65f),
            new ClipInfo("stab", "A_Attack_HeavyStab01_Sword", 1.367f, 0.883f, 0.983f),
            new ClipInfo("leap", "A_Attack_LightLeaping01_Sword", 1.567f, 1.1f, 1.167f),
            new ClipInfo("parry", "A_Parry_F_Sword", 0.467f, 0.233f, 0.4f),
            new ClipInfo("dodge", "A_Dodge_F_Sword", 0.567f),
        };

        public HeroClips(ClipInfo[] clips)
        {
            Clip Find(string role, bool struck = false)
            {
                int i = Array.FindIndex(clips, c => c.Role == role);
                if (i < 0) throw new ArgumentException($"HeroClips: the hero has no {role} clip");
                if (struck && (clips[i].Impact < 0 || clips[i].End < clips[i].Impact))
                    throw new ArgumentException($"HeroClips: the hero's {role} clip needs an impact and an end after it");
                return new Clip(i, clips[i]);
            }
            Idle = Find("idle");
            Run = Find("run");
            Back = Find("back");
            Left = Find("left");
            Right = Find("right");
            SlashB = Find("slashB", true);
            SlashA = Find("slashA", true);
            Spin = Find("spin", true);
            Stab = Find("stab", true);
            Leap = Find("leap", true);
            Parry = Find("parry", true);
            Dodge = Find("dodge");
        }
    }

    /// <summary>A clip laid over the hero's base pose: its time, its weight, and its share of the legs (1: the whole body).</summary>
    public struct HeroLayer
    {
        public int Clip;
        public float Time, Weight, Legs;
    }

    /// <summary>
    /// The hero at one song time (<see cref="HeroMotion.Evaluate"/>): the legs' run (its weight, the ground walked and
    /// the share of each way it runs relative to the legs' facing), the sword idle's time, the clips laid over them in
    /// order, which way the legs face and how far the upper body twists from them, its lift, and how it shows.
    /// </summary>
    public sealed class HeroPose
    {
        public const int MaxLayers = 4;

        public float IdleTime;
        /// <summary>The run's weight on the legs (0 standing, 1 running).</summary>
        public float Run;
        /// <summary>Each run clip's time (forward, backward, left, right) and its share of the run (summing to 1).</summary>
        public float ForwardTime, BackTime, LeftTime, RightTime;
        public float Forward, Back, Left, Right;
        /// <summary>The facing the legs (the root) turn to: radians clockwise from north (+y), Unity's yaw.</summary>
        public float Yaw;
        /// <summary>The upper body's turn from the legs (radians, clockwise), spread over the spine.</summary>
        public float Twist;
        /// <summary>The figure turns to this pose at once rather than easing: a dash, which moves on its key-down frame.</summary>
        public bool TurnAtOnce;
        /// <summary>Metres above the ground (the leap's arc).</summary>
        public float Lift;
        public readonly HeroLayer[] Layers = new HeroLayer[MaxLayers];
        public int LayerCount;
        /// <summary>False while it blinks and from its death on.</summary>
        public bool Visible;
        /// <summary>Its lines' own flare (0 none, 1 doubled): an impact's, a blink's arrival.</summary>
        public float Flare;
        /// <summary>The hurt flash, 1 on the hit's frame and gone over <see cref="HeroMotion.HurtS"/>.</summary>
        public float Hurt;
        // Metres of additional blade length, grown from its grip only for a manual cut.
        public float BladeExtension;
        internal float CleaveTurn;

        internal void Add(int clip, double time, double weight, double legs)
        {
            if (weight <= 0 || LayerCount == MaxLayers) return;
            Layers[LayerCount++] = new HeroLayer { Clip = clip, Time = (float)time, Weight = (float)Math.Min(1, weight), Legs = (float)legs };
        }
    }

    /// <summary>
    /// The drawn hero's turn (game/'s HeroTurn, ported): its legs ease to the pose's yaw over <see cref="Seconds"/>, and
    /// its upper body, apart, to the way the pose's upper body faces (the yaw plus the twist), so a walk that changes
    /// direction mid-swing turns the legs under a torso that stays on the strike. A dash takes the pose at once.
    /// </summary>
    public struct HeroTurn
    {
        public const double Seconds = 0.04;
        double _legs, _upper;
        bool _set;

        public void Ease(HeroPose p, double dt)
        {
            double k = _set && !p.TurnAtOnce ? 1 - Math.Exp(-Math.Max(0, dt) / Seconds) : 1;
            _set = true;
            _legs = HeroMotion.Wrap(_legs + HeroMotion.Wrap(p.Yaw - _legs) * k);
            _upper = HeroMotion.Wrap(_upper + HeroMotion.Wrap(p.Yaw + (double)p.Twist - _upper) * k);
            p.Yaw = (float)_legs;
            p.Twist = (float)HeroMotion.Wrap(_upper - _legs);
        }
    }

    /// <summary>
    /// The hero's motion on song time (design.md, The Hero &gt; Animation; Dashes): a function of the commands
    /// (<see cref="IHeroCommands"/>) and the song time, so the pose drawn at a time is the same however the frames fall.
    /// - <b>Walking</b>: the legs run once their walk starts (in over <see cref="RunIn"/>, out over <see cref="RunOut"/>),
    ///   each run clip cycling once per its stride walked (at most <see cref="MaxLegRate"/> times its own pace), the
    ///   legs facing the travel. Turned off it (a swing behind), they run backward or sideways: forward, back, left and
    ///   right blend by the travel's angle to the legs.
    /// - <b>Swings</b>: Sword Slash alternates LightCombo01A and LightCombo01B (A from the sword idle, B from where A
    ///   ends; after a pause, A again), HeavyFlourish01 for spins; one clip per swing, its impact frame on the swing's
    ///   impact. It plays at natural speed from impact less its wind-up unless
    ///   that would start before the swing before it has followed through, or before the swing was known; then up to
    ///   <see cref="MaxSpeed"/> to fit; failing that it enters part-way at natural speed, from that earliest moment (the
    ///   spin's long flourish always does: its wind-up is longer than the sim's notice), so no swing's follow-through is
    ///   ever cut short. Each fades in over <see cref="AttackIn"/> over what came before and out over
    ///   <see cref="AttackOut"/> once its clip has played out. Standing, it is the whole body and the hero turns to the aim;
    ///   walking, the legs keep the run and the upper body twists to the aim (in over the first <see cref="TwistLead"/>
    ///   of the wind-up, out over <see cref="TwistOut"/> after the follow-through), up to <see cref="TwistMax"/>, past
    ///   which the whole body turns and the legs run backward or sideways. The aim is held from the wind-up on.
    /// - <b>Dashes</b>: on the key-down frame the hero faces the dash, untwisted, in its type's pose, held or played
    ///   through the dash, then plays on from there and fades back: the plain Dash holds the forward dodge's long low
    ///   stride, Blade Rush thrusts with HeavyStab01, Leap Slam leaps on LightLeaping01 (its landing on the dash's end,
    ///   lifted in an arc), Shield Charge holds the forward parry, Blink Strike vanishes and reappears with a flare.
    /// - <b>Hurt</b> flashes once over <see cref="HurtS"/>; <b>death</b> hides the hero.
    /// </summary>
    public sealed class HeroMotion
    {
        /// <summary>Never faster than this (design.md, Decision 9): extra strikes are spectral.</summary>
        public const float MaxSpeed = 1.15f;
        public const double RunIn = 0.08, RunOut = 0.12;
        /// <summary>The run clips cycle at most this much faster than their own pace (the sideways and backward runs are slow).</summary>
        public const float MaxLegRate = 1.5f;
        public const double AttackIn = 0.08, AttackOut = 0.25;
        public const double TwistMax = 2 * Math.PI / 3, TwistLead = 0.6, TwistOut = 0.25;
        /// <summary>The plain Dash's held stride: the forward dodge's lowest, longest (s into it).</summary>
        public const float DodgeStride = 0.2f;
        /// <summary>Blade Rush enters HeavyStab01 this long before its thrust's impact.</summary>
        public const float StabLead = 0.083f;
        /// <summary>Leap Slam's take-off in LightLeaping01 (s): it enters no earlier.</summary>
        public const float LeapTakeOff = 0.55f;
        /// <summary>Leap Slam's arc at its peak (m), and Skyfall's higher one.</summary>
        public const float LeapLiftM = 0.7f, SkyfallLiftM = 1.4f;
        /// <summary>How long a dash's pose takes to give back to the walk after it ends, at most (s).</summary>
        public const double DashOut = 0.3;
        public const double FlareS = 0.2, HurtS = 0.25;
        /// <summary>An impact's flare on the hero's lines, and a blink's arrival.</summary>
        public const float ImpactFlare = 0.5f, BlinkFlare = 1f;
        const int Keep = 8;

        public struct SwingRecord
        {
            public SwingKind Kind;
            public int Clip;
            public string Name;
            /// <summary>Song time of the clip's first frame (before <see cref="Entry"/> if it enters part-way), its speed, the impact.</summary>
            public double Start, Impact;
            public float Speed;
            /// <summary>The song time it first shows, and its clip time then (0 unless it enters part-way).</summary>
            public double Entry;
            public float EntryClipTime;
            /// <summary>Its follow-through's end, and the clip's end (song time).</summary>
            public double End, ClipEnd;
            public double CutAt;
            public float AimYaw;
            public float CleaveRadius;
            /// <summary>The twist's weight carried in from the swing before, at its entry.</summary>
            public double Carry;

            /// <summary>The time into its clip at song time t.</summary>
            public double ClipTime(double t) => (t - Start) * Speed;
        }

        struct Glide
        {
            public double Start, Walked;
            public Vector2 Velocity;
            public float Yaw;
            public bool Moving;
        }

        struct DashMove
        {
            public DashPose Pose;
            public double Start, End;
            public float Yaw;
            public bool Evolved;
            public int Clip;
            /// <summary>The clip time held through the dash (NaN: played), the time it enters at, its speed through the dash.</summary>
            public float Hold, Enter, Speed;
            public double Out;
        }

        readonly HeroClips _clips;
        readonly double _speedMps;
        readonly Glide[] _glides = new Glide[64];
        readonly SwingRecord[] _swings = new SwingRecord[Keep];
        readonly DashMove[] _dashes = new DashMove[Keep];
        readonly double[] _hurts = new double[Keep];
        int _glideCount, _swingCount, _dashCount, _hurtCount;
        double _shown = double.NegativeInfinity, _death = double.PositiveInfinity;
        Vector2 _deathFrom;
        double _heldAt = double.NaN;

        /// <param name="speedMps">The hero's walking speed (hero.json), which paces the run clips.</param>
        public HeroMotion(HeroClips clips, double speedMps)
        {
            _clips = clips;
            _speedMps = speedMps;
        }

        public HeroClips Clips => _clips;
        public int SwingCount => _swingCount;
        public double DeathTime => _death;
        public Vector2 DeathFrom => _deathFrom;

        /// <summary>Swing <paramref name="i"/> (0 the first) of the latest <see cref="Keep"/>.</summary>
        public SwingRecord SwingAt(int i) => _swings[i % Keep];

        public void Walk(double start, Vector2 velocity)
        {
            // A re-walk (a heard input) replaces what was drawn from its start on.
            while (_glideCount > 0 && _glides[(_glideCount - 1) % _glides.Length].Start >= start) _glideCount--;
            var g = new Glide { Start = start, Velocity = velocity, Moving = velocity.sqrMagnitude > 1e-4f };
            if (_glideCount > 0)
            {
                var prev = _glides[(_glideCount - 1) % _glides.Length];
                g.Walked = prev.Walked + prev.Velocity.magnitude * (start - prev.Start);
                g.Yaw = prev.Yaw;
            }
            if (g.Moving) g.Yaw = YawOf(velocity);
            _glides[_glideCount++ % _glides.Length] = g;
        }

        /// <summary>Plans the swing's clip (<see cref="HeroMotion"/>, Swings) and returns it.</summary>
        public SwingRecord Swing(SwingKind kind, double impact, Vector2 aim, bool priority = false)
        {
            // LightCombo01A starts from the sword idle and ends where LightCombo01B starts: a slash right after an A is a B.
            var prev = _swingCount > 0 ? _swings[(_swingCount - 1) % Keep] : default;
            bool afterA = _swingCount > 0 && prev.Impact < prev.CutAt && prev.Clip == _clips.SlashA.Index && impact - _clips.SlashB.Impact <= prev.ClipEnd + AttackOut;
            var c = kind == SwingKind.Spin ? _clips.Spin : afterA ? _clips.SlashB : _clips.SlashA;
            return PlanSwing(kind, impact, aim, c, prev, _shown, priority);
        }

        SwingRecord PlanSwing(SwingKind kind, double impact, Vector2 aim, HeroClips.Clip c, SwingRecord prev,
            double earliest, bool priority)
        {
            // It shows no earlier than the frame it is known on, nor before the swing before has followed through.
            if (_swingCount > 0 && !priority) earliest = Math.Max(earliest, Math.Min(prev.End, prev.CutAt));
            double room = impact - earliest;
            float speed = 1;
            double start = impact - c.Impact, entry = start;
            if (room < c.Impact && room * MaxSpeed >= c.Impact)
            {
                speed = (float)(c.Impact / room);
                start = entry = impact - c.Impact / speed;
            }
            else if (room < c.Impact) entry = earliest; // part-way, at natural speed
            var s = new SwingRecord
            {
                CutAt = double.PositiveInfinity, Kind = kind, Clip = c.Index, Name = c.Info.Name, Start = start, Impact = impact, Speed = speed, Entry = entry,
                EntryClipTime = (float)((entry - start) * speed), End = start + c.End / speed, ClipEnd = start + c.Length / speed,
                AimYaw = aim.sqrMagnitude > 1e-8f ? YawOf(aim) : float.NaN,
            };
            s.Carry = _swingCount > 0 && entry < prev.CutAt ? TwistWeight(prev, entry) : 0;
            _swings[_swingCount++ % Keep] = s;
            return s;
        }

        public void Hold(double t, bool held)
        {
            if (held && double.IsNaN(_heldAt)) { _heldAt = t; return; }
            if (held || double.IsNaN(_heldAt)) return;
            double elapsed = t - _heldAt;
            for (int i = Math.Max(0, _glideCount - _glides.Length); i < _glideCount; i++) _glides[i % _glides.Length].Start += elapsed;
            for (int i = Math.Max(0, _swingCount - Keep); i < _swingCount; i++)
            {
                ref var s = ref _swings[i % Keep];
                // Grid points spent while held cannot become automatic hits on resume.
                if (s.Kind != SwingKind.Spin && s.CleaveRadius == 0 && s.Impact >= _heldAt) s.CutAt = Math.Min(s.CutAt, _heldAt);
                s.Start += elapsed; s.Impact += elapsed; s.Entry += elapsed;
                s.End += elapsed; s.ClipEnd += elapsed; s.CutAt += elapsed;
            }
            for (int i = Math.Max(0, _dashCount - Keep); i < _dashCount; i++)
            { _dashes[i % Keep].Start += elapsed; _dashes[i % Keep].End += elapsed; }
            for (int i = Math.Max(0, _hurtCount - Keep); i < _hurtCount; i++) _hurts[i % Keep] += elapsed;
            _shown += elapsed; _death += elapsed; _heldAt = double.NaN;
        }

        public void AimSwing(double impact, Vector2 aim)
        {
            for (int i = Math.Max(0, _swingCount - Keep); i < _swingCount; i++)
                if (Math.Abs(_swings[i % Keep].Impact - impact) < 1e-6)
                    _swings[i % Keep].AimYaw = YawOf(aim);
        }

        void CutSwings(double start)
        {
            for (int i = Math.Max(0, _swingCount - Keep); i < _swingCount; i++)
                _swings[i % Keep].CutAt = Math.Min(_swings[i % Keep].CutAt, start);
        }

        public void WalkingCleave(double start, double impact, Vector2 direction, float radius, bool backhand)
        {
            CutSwings(start);
            var clip = backhand ? _clips.SlashB : _clips.SlashA;
            var swing = PlanSwing(SwingKind.Slash, impact, direction, clip, default, start, true);
            // The automatic sword can hit again after the manual cut's .22 s recovery.
            swing.End = Math.Min(swing.End, impact + .20);
            swing.ClipEnd = swing.End;
            swing.CleaveRadius = radius;
            _swings[(_swingCount - 1) % Keep] = swing;
        }

        public void LandingSpin(double start, double impact, Vector2 direction)
        {
            CutSwings(start);
            // Enter the authored spin's final .15 s windup at natural speed; the full 1.43 s
            // lead cannot precede an immediate dash. The dash owns travel, the spin owns exit.
            Swing(SwingKind.Spin, impact, direction, priority: true);
            if (_dashCount > 0) _dashes[(_dashCount - 1) % Keep].Out = 0;
        }

        public void Dash(DashPose pose, double start, double end, Vector2 direction, bool evolved)
        {
            var d = new DashMove
            {
                Pose = pose, Start = start, End = Math.Max(end, start), Yaw = YawOf(direction), Evolved = evolved, Clip = -1,
                Hold = float.NaN, Speed = 1, Out = DashOut,
            };
            double span = d.End - d.Start;
            switch (pose)
            {
                case DashPose.Dash:
                    d.Clip = _clips.Dodge.Index;
                    d.Hold = d.Enter = DodgeStride;
                    d.Out = Math.Min(DashOut, _clips.Dodge.Length - DodgeStride);
                    break;
                case DashPose.BladeRush:
                    d.Clip = _clips.Stab.Index;
                    d.Enter = _clips.Stab.Impact - StabLead;
                    break;
                case DashPose.LeapSlam:
                    d.Clip = _clips.Leap.Index;
                    // The landing on the dash's end, from no earlier than the take-off, at most as fast as a swing.
                    d.Enter = (float)Math.Max(LeapTakeOff, _clips.Leap.Impact - span * MaxSpeed);
                    d.Speed = span > 0 ? (float)((_clips.Leap.Impact - d.Enter) / span) : 1;
                    d.Out = Math.Min(DashOut, _clips.Leap.Length - _clips.Leap.Impact);
                    break;
                case DashPose.ShieldCharge:
                    d.Clip = _clips.Parry.Index;
                    d.Hold = d.Enter = _clips.Parry.Impact;
                    break;
            }
            _dashes[_dashCount++ % Keep] = d;
        }

        public void Hurt(double t) => _hurts[_hurtCount++ % Keep] = t;

        public void Death(double t, Vector2 from)
        {
            if (t >= _death) return;
            _death = t;
            _deathFrom = from;
        }

        /// <summary>The hero's pose at song time <paramref name="t"/>, into <paramref name="p"/>.</summary>
        public void Evaluate(double t, HeroPose p)
        {
            if (!double.IsNaN(_heldAt)) t = Math.Min(t, _heldAt);
            _shown = Math.Max(_shown, t);
            p.LayerCount = 0;
            p.Lift = 0;
            p.Flare = 0;
            p.TurnAtOnce = false;
            p.Visible = t < _death;
            var idle = _clips.Idle;
            p.IdleTime = (float)Repeat(t, idle.Length);
            Walking(t, out double travel, out double run, out double walked);
            p.BladeExtension = p.CleaveTurn = 0;
            double standing = Swings(t, p, travel, run);
            Torso(t, p, travel, standing, run, walked);
            p.Twist += p.CleaveTurn;
            DashOver(t, p);
            p.Hurt = 0;
            for (int i = Math.Max(0, _hurtCount - Keep); i < _hurtCount; i++)
            {
                double since = t - _hurts[i % Keep];
                if (since >= 0 && since < HurtS) p.Hurt = (float)Math.Max(p.Hurt, 1 - since / HurtS);
            }
        }

        void Walking(double t, out double travel, out double run, out double walked)
        {
            travel = 0;
            run = 0;
            walked = 0;
            int oldest = Math.Max(0, _glideCount - _glides.Length);
            int k = -1;
            for (int i = _glideCount - 1; i >= oldest; i--)
                if (_glides[i % _glides.Length].Start <= t) { k = i; break; }
            if (k < 0) return;
            var g = _glides[k % _glides.Length];
            double since = t - g.Start;
            bool wasMoving = k > oldest && _glides[(k - 1) % _glides.Length].Moving;
            travel = g.Yaw;
            walked = g.Walked + g.Velocity.magnitude * since;
            run = g.Moving ? (wasMoving ? 1 : Clamp01(since / RunIn)) : (wasMoving ? 1 - Clamp01(since / RunOut) : 0);
        }

        /// <summary>The swings' layers and the impact flare; returns the yaw a standing hero turns to (the latest aimed swing's, else the travel).</summary>
        double Swings(double t, HeroPose p, double travel, double run)
        {
            double standing = travel;
            double latestImpact = double.NegativeInfinity;
            for (int i = Math.Max(0, _swingCount - Keep); i < _swingCount; i++)
            {
                var s = _swings[i % Keep];
                if (s.Impact <= t + 1e-6 && s.Impact < s.CutAt) latestImpact = Math.Max(latestImpact, s.Impact);
                if (s.Entry > t || t >= s.CutAt) continue;
                double blend = s.CleaveRadius > 0 ? .032 : AttackIn;
                double fadeIn = Clamp01((t - s.Entry) / Math.Max(1e-3, Math.Min(blend, 0.5 * (s.Impact - s.Entry))));
                double w = fadeIn * (1 - Smooth(Clamp01((t - s.ClipEnd) / AttackOut)));
                if (w <= 0) continue;
                double legs = 1 - run;
                if (s.CleaveRadius > 0)
                {
                    // Coil, cut through the target, then recover while the feet keep walking.
                    double before = Smooth((t - s.Entry) / Math.Max(.001, s.Impact - s.Entry));
                    double recover = Smooth((t - s.Impact) / .22);
                    double drive = before * (1 - recover);
                    double hand = s.Clip == _clips.SlashB.Index ? -1 : 1;
                    p.CleaveTurn = (float)(hand * w * ((-.55 + 1.35 * before) * (1 - recover)));
                    p.BladeExtension = (float)(Math.Max(0, s.CleaveRadius - 1.75) * drive);
                    legs += run * .28 * drive;
                }
                p.Add(s.Clip, Math.Max(0, s.ClipTime(t)), w, legs);
                if (!float.IsNaN(s.AimYaw) && t < s.End + 0.5 * AttackOut) standing = s.AimYaw;
            }
            double since = t - latestImpact;
            if (since >= 0 && since < FlareS) p.Flare = (float)(ImpactFlare * (1 - since / FlareS));
            return standing;
        }

        /// <summary>
        /// The legs' facing, the run's ways and the twist (game/'s Torso, ported): walking, the upper body faces the travel
        /// turned toward the swings' aims, the twist alone up to <see cref="TwistMax"/>, past it the legs turning too and
        /// running back or sideways; standing, the whole body turns to <paramref name="standing"/>.
        /// </summary>
        void Torso(double t, HeroPose p, double travel, double standing, double run, double walked)
        {
            double upper = UpperYaw(t, travel);
            double d = Wrap(upper - travel), twist = d, legs = travel;
            if (Math.Abs(d) > TwistMax)
            {
                double past = (Math.Abs(d) - TwistMax) / (Math.PI - TwistMax);
                twist = Math.Sign(d) * TwistMax * (1 - past);
                legs = upper - twist;
            }
            // The travel's way relative to the legs: ahead, behind, to their right (clockwise) or left.
            double a = Wrap(travel - legs), ca = Math.Cos(a), sa = Math.Sin(a);
            p.Forward = (float)(ca > 0 ? ca * ca : 0);
            p.Back = (float)(ca < 0 ? ca * ca : 0);
            p.Right = (float)(sa > 0 ? sa * sa : 0);
            p.Left = (float)(sa < 0 ? sa * sa : 0);
            p.ForwardTime = RunTime(_clips.Run, walked);
            p.BackTime = RunTime(_clips.Back, walked);
            p.LeftTime = RunTime(_clips.Left, walked);
            p.RightTime = RunTime(_clips.Right, walked);
            p.Run = (float)run;
            p.Yaw = (float)Wrap(LerpAngle(standing, legs, run));
            p.Twist = (float)(twist * run);
        }

        /// <summary>
        /// A run clip's time after <paramref name="walked"/> metres: once round per stride, at most <see cref="MaxLegRate"/>
        /// its own pace at the hero's speed (a constant, so the phase never jumps as a walk stops).
        /// </summary>
        float RunTime(HeroClips.Clip c, double walked)
        {
            double stride = Math.Max(c.Info.StrideM, _speedMps * c.Length / MaxLegRate);
            return stride <= 1e-6 ? 0 : (float)(Repeat(walked / stride, 1) * c.Length);
        }

        /// <summary>
        /// The way the walking hero's upper body faces at song time t (game/'s UpperYaw, ported): the travel, turned toward
        /// the aimed swings' aims (each from the one before as its twist comes in) by the latest aimed swing's weight.
        /// </summary>
        double UpperYaw(double t, double travel)
        {
            int latest = -1, first = -1;
            for (int i = _swingCount - 1; i >= Math.Max(0, _swingCount - Keep); i--)
            {
                var s = _swings[i % Keep];
                if (float.IsNaN(s.AimYaw) || s.Entry > t || t >= s.CutAt) continue;
                if (latest < 0) latest = i;
                first = i;
                if (s.Carry <= 0 || TwistIn(s, t) >= 1) break;
            }
            double weight = latest < 0 ? 0 : TwistWeight(_swings[latest % Keep], t);
            if (weight <= 0) return travel;
            double aim = _swings[first % Keep].AimYaw;
            for (int i = first + 1; i <= latest; i++)
            {
                var s = _swings[i % Keep];
                if (!float.IsNaN(s.AimYaw)) aim = LerpAngle(aim, s.AimYaw, TwistIn(s, t));
            }
            return LerpAngle(travel, aim, weight);
        }

        static double TwistIn(in SwingRecord s, double t) =>
            Smooth(Clamp01((t - s.Entry) / Math.Max(1e-3, TwistLead * (s.Impact - s.Entry))));

        static double TwistWeight(in SwingRecord s, double t) =>
            (s.Carry + (1 - s.Carry) * TwistIn(s, t)) * (1 - Smooth(Clamp01((t - s.End) / TwistOut)));

        /// <summary>The latest dash's pose over everything, at once on its start, and back over its <see cref="DashMove.Out"/>.</summary>
        void DashOver(double t, HeroPose p)
        {
            int k = -1;
            for (int i = _dashCount - 1; i >= Math.Max(0, _dashCount - Keep); i--)
                if (_dashes[i % Keep].Start <= t) { k = i; break; }
            if (k < 0) return;
            var d = _dashes[k % Keep];
            if (t >= d.End + d.Out) return;
            double w = t <= d.End ? 1 : 1 - Smooth((t - d.End) / d.Out);
            p.Yaw = (float)Wrap(LerpAngle(p.Yaw, d.Yaw, w));
            p.Twist = (float)(p.Twist * (1 - w));
            if (t <= d.End) p.TurnAtOnce = true;
            if (d.Pose == DashPose.BlinkStrike)
            {
                if (t < d.End) p.Visible = false;
                else p.Flare = Math.Max(p.Flare, (float)(BlinkFlare * (1 - Clamp01((t - d.End) / FlareS))));
                return;
            }
            double atEnd = float.IsNaN(d.Hold) ? d.Enter + (d.End - d.Start) * d.Speed : d.Hold;
            double time = t <= d.End ? (float.IsNaN(d.Hold) ? d.Enter + (t - d.Start) * d.Speed : d.Hold) : atEnd + (t - d.End);
            if (p.LayerCount == HeroPose.MaxLayers) p.LayerCount--; // the dash is on top, whatever it covers
            p.Add(d.Clip, time, w, 1);
            if (d.Pose == DashPose.LeapSlam && t <= d.End)
            {
                double u = Clamp01((t - d.Start) / Math.Max(1e-6, d.End - d.Start));
                p.Lift = (float)((d.Evolved ? SkyfallLiftM : LeapLiftM) * 4 * u * (1 - u));
            }
        }

        /// <summary>Unity's yaw of a ground direction (x right, y up the screen): radians clockwise from +y.</summary>
        public static float YawOf(Vector2 v) => Mathf.Atan2(v.x, v.y);

        static double Clamp01(double v) => v < 0 ? 0 : v > 1 ? 1 : v;

        static double Smooth(double u)
        {
            u = Clamp01(u);
            return u * u * (3 - 2 * u);
        }

        /// <summary>An angle wrapped into (-pi, pi].</summary>
        public static double Wrap(double a)
        {
            a = Math.IEEERemainder(a, 2 * Math.PI);
            return a <= -Math.PI ? a + 2 * Math.PI : a;
        }

        static double LerpAngle(double a, double b, double u) => a + Wrap(b - a) * u;

        static double Repeat(double t, double length) => length <= 0 ? 0 : t - Math.Floor(t / length) * length;
    }
}
