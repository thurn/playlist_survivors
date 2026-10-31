using System;

namespace Game.Client.Crowd
{
    /// <summary>One clip of a type, as the event-to-clip mapping needs it (CrowdClip's index, length and annotations).</summary>
    public readonly struct MonsterClip
    {
        public readonly uint Index;
        public readonly float Length, Impact, End;

        public MonsterClip(int index, float length, float impact, float end)
        {
            Index = (uint)index;
            Length = length;
            Impact = Math.Max(0, impact);
            End = end > 0 ? end : length;
        }
    }

    /// <summary>The four clips a monster plays: the idle bob, the walk (a step is half its cycle), the lunge's attack and the hit.</summary>
    public sealed class MonsterClips
    {
        public MonsterClip Idle, Walk, Attack, Hit;

        public static MonsterClips Of(CrowdType t) => new MonsterClips
        {
            Idle = ClipOf(t, "idle"),
            Walk = ClipOf(t, "walk"),
            Attack = ClipOf(t, "attack"),
            Hit = ClipOf(t, "hit"),
        };

        static MonsterClip ClipOf(CrowdType t, string role)
        {
            int i = t.Clip(role);
            if (i < 0) throw new InvalidOperationException($"MonsterClips: {t.Id} has no {role} clip (re-run `game unity BakeCrowd`)");
            var c = t.Clips[i];
            return new MonsterClip(i, c.Length, c.Impact, c.End);
        }
    }

    /// <summary>What a monster shows at one song time: its two clips and their blend, its hit flash, its place and facing.</summary>
    public struct MonsterPose
    {
        public uint ClipA, ClipB;
        public float TimeA, TimeB, Blend;
        public float HitFlash;
        /// <summary>Ground metres (the sim's x, y).</summary>
        public double X, Y;
        /// <summary>Radians about the vertical: 0 faces +y on the ground (Unity's +z).</summary>
        public float Yaw;
    }

    /// <summary>
    /// One monster's life as the sim's events tell it (unity-game.md, The Crowd and Enemies), from
    /// its spawn to its death: the event-to-clip mapping. Events arrive ahead of the song time
    /// shown, so it keeps them and <see cref="Sample"/> answers at any song time from what it holds;
    /// it never guesses past them.
    /// - **Idle**: the idle clip loops, each monster at its own phase (the bob).
    /// - **Step** (EnemyStep): the walk's next half cycle (one footfall, feet alternating) is
    ///   squeezed into the move window, so its footplant lands on the beat; the pose holds there
    ///   briefly, then eases back to idle. Its place moves from `from` to `to` over the window,
    ///   as the sim's does, and it turns to face the step.
    /// - **Lunge** (the enemy's Attack): the attack clip is retimed so its annotated contact lands on
    ///   the impact (the beat), from the notice; it returns at the same speed, with a short lunge
    ///   toward the hero peaking on the contact, facing the attack's direction.
    /// - **Hit**: a flinch from the hit clip's onset, cut short, and a white flash over 0.12 s.
    /// - **Knock** (EnemyKnock): its place moves from `from` to `to` between the knock's times.
    /// Each new clip fades in over a few frames from what showed before it. Ground positions are
    /// the sim's (x, y): EnemyState.PositionAt's linear moves.
    /// </summary>
    public sealed class MonsterTrack
    {
        public const double FlashSeconds = 0.12;
        /// <summary>A step's pose holds at its footplant this long before easing back to idle.</summary>
        public const double StepHold = 0.12;
        /// <summary>How long a finished clip takes to ease back into idle.</summary>
        public const double IdleFade = 0.2;
        /// <summary>The flinch's speed and its longest showing.</summary>
        public const float HitSpeed = 1.5f;
        public const double HitShow = 0.45;
        /// <summary>The lunge's retiming bounds: the wind-up may be sped up to 4x or slowed to half speed.</summary>
        public const float LungeMinSpeed = 0.5f, LungeMaxSpeed = 4f;
        /// <summary>How far the lunge carries the body toward the hero at contact, metres at scale 1.</summary>
        public const double LungeReach = 0.4;
        const double StepFade = 0.05, LungeFade = 0.06, HitFade = 0.03;
        const int Moves = 4, Actions = 8;

        enum Kind : byte { Step, Lunge, Hit }

        struct Move
        {
            public double FromX, FromY, ToX, ToY, Start, End;
        }

        struct Action
        {
            public Kind Kind;
            public double Start, Impact, End;
            public float Speed, Phase;      // the clip's speed; a step's starting phase in the walk
            public double DirX, DirY;       // a lunge's direction
            public float YawFrom, YawTo;    // the facing it turns through, over Start..Impact
        }

        readonly Move[] _moves = new Move[Moves];
        readonly Action[] _actions = new Action[Actions];
        int _moveCount, _actionCount, _steps;
        MonsterClips _clips;
        float _idlePhase, _yaw;
        double _forgottenHit;
        double _spawnX, _spawnY;

        /// <summary>The sim's enemy id (its dense slot, reused after a death).</summary>
        public int Enemy { get; private set; }
        /// <summary>Unique to this life: the crowd's entity key, so a reused enemy id is a new monster.</summary>
        public int Serial { get; private set; }
        public int Role { get; private set; }
        public double SpawnTime { get; private set; }
        /// <summary>When it leaves (its death, or a recycled spawn's re-entry), else +infinity.</summary>
        public double EndTime { get; private set; }

        /// <summary>
        /// Starts a life (a Spawn event) at a ground point, facing another (the hero); <paramref name="serial"/>
        /// also picks its idle phase.
        /// </summary>
        public void Begin(int enemy, int serial, int role, MonsterClips clips, double t, double x, double y, double faceX, double faceY)
        {
            Enemy = enemy;
            Serial = serial;
            Role = role;
            _clips = clips;
            SpawnTime = t;
            EndTime = double.PositiveInfinity;
            Died = false;
            _spawnX = x;
            _spawnY = y;
            _moveCount = _actionCount = _steps = 0;
            _forgottenHit = double.NegativeInfinity;
            _yaw = faceX != x || faceY != y ? (float)Math.Atan2(faceX - x, faceY - y) : 0;
            _idlePhase = Jitter(serial) * clips.Idle.Length;
        }

        /// <summary>Whether its life ends in a death (it shatters), and the killing blow's direction on the ground and launch distance.</summary>
        public bool Died { get; private set; }
        public double BlowX { get; private set; }
        public double BlowY { get; private set; }
        public double Force { get; private set; }

        public bool Visible(double t) => t >= SpawnTime && t < EndTime;

        /// <summary>It leaves at <paramref name="t"/> (a death, or its id re-entering elsewhere).</summary>
        public void End(double t)
        {
            if (t < EndTime) Died = false;
            EndTime = Math.Min(EndTime, t);
        }

        /// <summary>A Death at <paramref name="t"/>: it breaks there, by a blow along (dirX, dirY) that throws it <paramref name="force"/> metres.</summary>
        public void Die(double t, double dirX, double dirY, double force)
        {
            if (t >= EndTime) return;
            EndTime = t;
            Died = true;
            BlowX = dirX;
            BlowY = dirY;
            Force = force;
        }

        /// <summary>An EnemyStep: from, to, the move's start and its footplant (the beat).</summary>
        public void Step(double fromX, double fromY, double toX, double toY, double start, double footplant)
        {
            AddMove(fromX, fromY, toX, toY, start, footplant);
            float yaw = LatestYaw();
            float to = toX != fromX || toY != fromY ? (float)Math.Atan2(toX - fromX, toY - fromY) : yaw;
            AddAction(new Action
            {
                Kind = Kind.Step,
                Start = start,
                Impact = Math.Max(start, footplant),
                End = Math.Max(start, footplant) + StepHold,
                Phase = (_steps++ & 1) * 0.5f * _clips.Walk.Length,
                YawFrom = yaw,
                YawTo = to,
            });
        }

        /// <summary>An EnemyKnock: from, to and its times.</summary>
        public void Knock(double fromX, double fromY, double toX, double toY, double start, double end) =>
            AddMove(fromX, fromY, toX, toY, start, end);

        /// <summary>A contact lunge (the enemy's Attack): its notice, its contact on the beat and its direction toward the hero.</summary>
        public void Lunge(double notice, double impact, double dirX, double dirY)
        {
            var a = _clips.Attack;
            impact = Math.Max(notice, impact);
            float speed = Clamp((float)(a.Impact / Math.Max(impact - notice, 1e-3)), LungeMinSpeed, LungeMaxSpeed);
            float yaw = LatestYaw();
            AddAction(new Action
            {
                Kind = Kind.Lunge,
                Start = notice,
                Impact = impact,
                End = impact + Math.Max(0, a.End - a.Impact) / speed,
                Speed = speed,
                DirX = dirX,
                DirY = dirY,
                YawFrom = yaw,
                YawTo = dirX != 0 || dirY != 0 ? (float)Math.Atan2(dirX, dirY) : yaw,
            });
        }

        /// <summary>A hit on it: the flinch and the flash.</summary>
        public void Hit(double t)
        {
            var h = _clips.Hit;
            float yaw = LatestYaw();
            AddAction(new Action
            {
                Kind = Kind.Hit,
                Start = t,
                Impact = t,
                End = t + Math.Min(Math.Max(0, h.End - h.Impact) / HitSpeed, HitShow),
                Speed = HitSpeed,
                YawFrom = yaw,
                YawTo = yaw,
            });
        }

        /// <summary>What it shows at song time <paramref name="t"/>; also forgets what can no longer show.</summary>
        public MonsterPose Sample(double t)
        {
            var pose = new MonsterPose();
            PositionAt(t, out pose.X, out pose.Y);

            int active = ActiveAt(t);
            Forget(active - 1);
            if (active > 0) active = Math.Min(active, 1);
            if (active < 0)
            {
                Idle(t, out pose.ClipA, out pose.TimeA);
                pose.ClipB = pose.ClipA;
                pose.TimeB = pose.TimeA;
                pose.Blend = 1;
                pose.Yaw = _yaw;
                return pose;
            }

            ref var a = ref _actions[active];
            pose.Yaw = LerpAngle(a.YawFrom, a.YawTo, Ramp(t, a.Start, a.Impact));
            pose.HitFlash = HitFlash(t);
            if (a.Kind == Kind.Lunge)
            {
                double reach = LungeReach * LungeBump(a, t);
                pose.X += a.DirX * reach;
                pose.Y += a.DirY * reach;
            }
            if (t >= a.End)
            {
                // Done: idle eases back in over its last pose.
                Idle(t, out pose.ClipA, out pose.TimeA);
                Clip(a, a.End, out pose.ClipB, out pose.TimeB);
                pose.Blend = (float)Ramp(t, a.End, a.End + IdleFade);
                return pose;
            }
            Clip(a, t, out pose.ClipA, out pose.TimeA);
            // The pose before it: the previous clip held where this one cut in, or idle.
            if (active > 0 && a.Start < _actions[active - 1].End) Clip(_actions[active - 1], a.Start, out pose.ClipB, out pose.TimeB);
            else Idle(t, out pose.ClipB, out pose.TimeB);
            pose.Blend = (float)Ramp(t, a.Start, a.Start + FadeOf(a.Kind));
            return pose;
        }

        void Idle(double t, out uint clip, out float time)
        {
            clip = _clips.Idle.Index;
            time = (float)(t - SpawnTime) + _idlePhase;
        }

        void Clip(in Action a, double t, out uint clip, out float time)
        {
            switch (a.Kind)
            {
                case Kind.Step:
                {
                    double window = a.Impact - a.Start;
                    double f = window > 0 ? Ramp(t, a.Start, a.Impact) : 1;
                    clip = _clips.Walk.Index;
                    time = a.Phase + (float)f * 0.5f * _clips.Walk.Length;
                    return;
                }
                case Kind.Lunge:
                {
                    var c = _clips.Attack;
                    clip = c.Index;
                    time = Clamp(c.Impact + (float)(t - a.Impact) * a.Speed, 0, c.End);
                    return;
                }
                default:
                {
                    var c = _clips.Hit;
                    clip = c.Index;
                    time = Clamp(c.Impact + (float)(t - a.Start) * a.Speed, 0, c.End);
                    return;
                }
            }
        }

        static double FadeOf(Kind k) => k == Kind.Step ? StepFade : k == Kind.Lunge ? LungeFade : HitFade;

        /// <summary>The lunge's reach (0..1): easing out to the contact, then back by the end of its return.</summary>
        static double LungeBump(in Action a, double t)
        {
            if (t <= a.Impact) return Smooth(Ramp(t, a.Start, a.Impact));
            return 1 - Smooth(Ramp(t, a.Impact, a.End));
        }

        float HitFlash(double t)
        {
            double latest = _forgottenHit;
            for (int i = 0; i < _actionCount; i++)
                if (_actions[i].Kind == Kind.Hit && _actions[i].Start <= t) latest = _actions[i].Start;
            return (float)Math.Max(0, 1 - (t - latest) / FlashSeconds);
        }

        void PositionAt(double t, out double x, out double y)
        {
            x = _spawnX;
            y = _spawnY;
            for (int i = _moveCount - 1; i >= 0; i--)
            {
                ref var m = ref _moves[i];
                if (m.Start > t) continue;
                double f = Ramp(t, m.Start, m.End);
                x = m.FromX + (m.ToX - m.FromX) * f;
                y = m.FromY + (m.ToY - m.FromY) * f;
                return;
            }
        }

        /// <summary>The latest action started by t, or -1.</summary>
        int ActiveAt(double t)
        {
            for (int i = _actionCount - 1; i >= 0; i--)
                if (_actions[i].Start <= t) return i;
            return -1;
        }

        /// <summary>Drops the actions before index <paramref name="keepFrom"/>, keeping their facing and latest hit.</summary>
        void Forget(int keepFrom)
        {
            if (keepFrom <= 0) return;
            _yaw = _actions[keepFrom - 1].YawTo;
            for (int i = 0; i < keepFrom; i++)
                if (_actions[i].Kind == Kind.Hit) _forgottenHit = Math.Max(_forgottenHit, _actions[i].Start);
            Array.Copy(_actions, keepFrom, _actions, 0, _actionCount - keepFrom);
            _actionCount -= keepFrom;
        }

        float LatestYaw() => _actionCount > 0 ? _actions[_actionCount - 1].YawTo : _yaw;

        void AddMove(double fromX, double fromY, double toX, double toY, double start, double end)
        {
            if (_moveCount == Moves)
            {
                Array.Copy(_moves, 1, _moves, 0, Moves - 1);
                _moveCount--;
            }
            _moves[_moveCount++] = new Move { FromX = fromX, FromY = fromY, ToX = toX, ToY = toY, Start = start, End = end };
        }

        void AddAction(in Action a)
        {
            if (_actionCount == Actions) Forget(1);
            // Events come in time order; an action never starts before the last one.
            _actions[_actionCount++] = a;
        }

        static double Ramp(double t, double a, double b) => b > a ? Math.Max(0, Math.Min(1, (t - a) / (b - a))) : t >= a ? 1 : 0;

        static double Smooth(double f) => f * f * (3 - 2 * f);

        static float Clamp(float v, float lo, float hi) => v < lo ? lo : v > hi ? hi : v;

        static float LerpAngle(float a, float b, double f)
        {
            double d = b - a;
            d -= Math.Floor((d + Math.PI) / (2 * Math.PI)) * 2 * Math.PI;
            return (float)(a + d * f);
        }

        /// <summary>A stable value in [0, 1) per monster.</summary>
        static float Jitter(int serial)
        {
            uint h = (uint)serial * 0x9E3779B1u;
            h ^= h >> 15; h *= 0x2C1B3C6Du; h ^= h >> 12; h *= 0x297A2D39u; h ^= h >> 15;
            return (h & 0xFFFFFF) / 16777216f;
        }
    }
}
