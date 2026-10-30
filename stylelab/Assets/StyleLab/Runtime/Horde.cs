using System;
using System.Collections.Generic;
using UnityEngine;

namespace StyleLab.Runtime
{
    public enum MonsterState { Walk, Hold, Attack, Hit, Dying, Dead }

    public sealed class Monster
    {
        public Figure Figure;
        public TypeData Type;
        public Vector3 Position;
        public float Yaw;
        public int HitPoints;
        public MonsterState State;
        public float StateTime;
        public float Hit;
        public float Death;
        public bool Alive => State != MonsterState.Dying && State != MonsterState.Dead;
    }

    /// <summary>
    /// The crowd: it steps in on the beat (a surge just after each beat, as the native game's
    /// monsters step), holds off a clear ring round the hero, attacks on beats, flinches when hit,
    /// dies with its treatment's death and comes back from the edge. Simple on purpose: the lab
    /// is about how it looks.
    /// </summary>
    public sealed class Horde
    {
        public const float ClearRing = 2.5f;
        public readonly List<Monster> All = new List<Monster>();

        static readonly (string id, float share)[] Fodder =
        {
            ("skeleton", 0.22f), ("goblin", 0.18f), ("slime", 0.14f), ("bat", 0.12f), ("mushroom", 0.16f), ("demon", 0.18f),
        };

        readonly System.Random _rng;
        readonly Transform _parent;
        readonly Effects _effects;
        readonly List<(Vector3 at, float radius)> _obstacles;
        int _lastBeat = -1;

        public Horde(StyleLabData data, Transform parent, int fodder, Effects effects,
                     List<(Vector3 at, float radius)> obstacles, int seed)
        {
            _rng = new System.Random(seed);
            _parent = parent;
            _effects = effects;
            _obstacles = obstacles;
            foreach (var (id, share) in Fodder)
                for (int i = 0; i < Mathf.RoundToInt(fodder * share); i++) Add(data.Type(id));
            for (int i = 0; i < 3; i++) Add(data.Type("bigork"));
            for (int i = 0; i < 3; i++) Add(data.Type("troll"));
            Add(data.Type("dragon"));
            // The first frame already shows a full horde, packed in from the clear ring.
            foreach (var m in All) Place(m, ClearRing + m.Type.radius + 0.5f, 22f);
        }

        void Add(TypeData type)
        {
            var m = new Monster { Type = type, Figure = new Figure(type, _parent, (float)_rng.NextDouble()) };
            All.Add(m);
        }

        float Range(float a, float b) => a + (float)_rng.NextDouble() * (b - a);

        void Place(Monster m, float minR, float maxR)
        {
            float r = Mathf.Sqrt(Range(minR * minR, maxR * maxR));
            float a = Range(0, Mathf.PI * 2);
            // The dragon looms on the far side of the hero, behind the fight in every shot.
            if (m.Type.kind == "boss") { r = Range(15f, 18f); a = Range(0.35f, 0.65f) * Mathf.PI; }
            m.Position = new Vector3(Mathf.Cos(a) * r, 0, Mathf.Sin(a) * r);
            m.Yaw = Mathf.Atan2(-m.Position.x, -m.Position.z) * Mathf.Rad2Deg;
            m.HitPoints = m.Type.kind == "elite" ? 4 : 1;
            m.State = MonsterState.Walk;
            m.StateTime = Range(0, 1);
            m.Hit = 0;
            m.Death = 0;
            m.Figure.SetVisible(true);
            Play(m, "walk", Range(0, 1));
        }

        public void ApplyTreatment(int treatment, MaterialBank bank)
        {
            foreach (var m in All) m.Figure.Apply(Treatments.StyleFor(treatment, m.Type.id), bank);
        }

        static float StopRadius(Monster m) => m.Type.kind == "boss" ? 11f : ClearRing + m.Type.radius;

        public void Update(float dt, Beat beat, Vector3 hero)
        {
            bool newBeat = beat.Index != _lastBeat;
            _lastBeat = beat.Index;
            float env = (0.35f + 1.3f * Mathf.Exp(-beat.Phase * 4f)) / 0.67f; // a surge just after each beat
            foreach (var m in All)
            {
                m.StateTime += dt;
                m.Hit = Mathf.Max(0, m.Hit - dt * 9f);
                switch (m.State)
                {
                    case MonsterState.Walk:
                    case MonsterState.Hold:
                    case MonsterState.Attack:
                    case MonsterState.Hit:
                        Move(m, dt, beat, hero, env, newBeat);
                        break;
                    case MonsterState.Dying:
                        float delay = DeathDelay(m.Figure.Style);
                        m.Death = Mathf.Clamp01((m.StateTime - delay) / 1.1f);
                        if (m.Death >= 1f) { m.State = MonsterState.Dead; m.StateTime = 0; m.Figure.SetVisible(false); }
                        break;
                    case MonsterState.Dead:
                        if (m.StateTime > 0.6f) Place(m, 19f, 26f);
                        break;
                }
            }
            Separate();
            foreach (var m in All)
            {
                if (m.State == MonsterState.Dead) continue;
                var t = m.Figure.Root.transform;
                t.SetPositionAndRotation(m.Position + Vector3.up * m.Type.hoverM, Quaternion.Euler(0, m.Yaw, 0));
                m.Figure.SetEffects(m.Hit, m.Death, m.Position);
                KeepLooping(m);
            }
        }

        // Shattering and imploding looks start at the blow; the rest let the death clip play first.
        static float DeathDelay(Style s) =>
            s == Style.Crystal || s == Style.Synth ? 0.15f : s == Style.Void || s == Style.Hologram ? 0.35f : 0.7f;

        void Move(Monster m, float dt, Beat beat, Vector3 hero, float env, bool newBeat)
        {
            var to = hero - m.Position;
            to.y = 0;
            float dist = to.magnitude;
            float stop = StopRadius(m);
            float targetYaw = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg;
            m.Yaw = Mathf.MoveTowardsAngle(m.Yaw, targetYaw, 300f * dt);
            bool busy = (m.State == MonsterState.Attack || m.State == MonsterState.Hit) && m.StateTime < 0.7f;
            if (dist > stop + 0.15f && !busy)
            {
                float speed = StepOf(m.Type) / Mathf.Max(beat.BeatLength, 0.2f) * env;
                m.Position += to / dist * Mathf.Min(speed * dt, dist - stop);
                if (m.State != MonsterState.Walk) { m.State = MonsterState.Walk; m.StateTime = 0; Play(m, "walk"); }
            }
            else if (!busy)
            {
                if (m.State == MonsterState.Walk || m.State == MonsterState.Attack || m.State == MonsterState.Hit)
                {
                    m.State = MonsterState.Hold;
                    m.StateTime = 0;
                    Play(m, "idle");
                }
                if (newBeat && _rng.NextDouble() < 0.3)
                {
                    m.State = MonsterState.Attack;
                    m.StateTime = 0;
                    Play(m, _rng.NextDouble() < 0.5 ? "attack" : "attack2");
                }
            }
        }

        static float StepOf(TypeData t)
        {
            switch (t.id)
            {
                case "bat": return 0.95f;
                case "goblin": return 0.85f;
                case "demon": return 0.8f;
                case "skeleton": return 0.75f;
                case "slime": return 0.65f;
                case "dragon": return 0.5f;
                default: return 0.62f;
            }
        }

        void Separate()
        {
            for (int i = 0; i < All.Count; i++)
            {
                var a = All[i];
                if (!a.Alive) continue;
                for (int j = i + 1; j < All.Count; j++)
                {
                    var b = All[j];
                    if (!b.Alive) continue;
                    float min = (a.Type.radius + b.Type.radius) * 1.05f;
                    var d = b.Position - a.Position;
                    d.y = 0;
                    float sq = d.sqrMagnitude;
                    if (sq >= min * min || sq < 1e-8f) continue;
                    float len = Mathf.Sqrt(sq);
                    var push = d / len * (min - len) * 0.5f;
                    float wa = b.Type.radius / (a.Type.radius + b.Type.radius); // the bigger one gives less
                    a.Position -= push * wa * 2f;
                    b.Position += push * (1f - wa) * 2f;
                }
                foreach (var (at, radius) in _obstacles)
                {
                    var d = a.Position - at;
                    d.y = 0;
                    float min = radius + a.Type.radius;
                    if (d.sqrMagnitude < min * min && d.sqrMagnitude > 1e-8f) a.Position = at + d.normalized * min;
                }
            }
        }

        public void Damage(Monster m, Vector3 from, int amount = 1)
        {
            if (!m.Alive) return;
            m.HitPoints -= amount;
            m.Hit = 1f;
            var away = m.Position - from;
            away.y = 0;
            if (m.HitPoints <= 0)
            {
                m.State = MonsterState.Dying;
                m.StateTime = 0;
                Play(m, "death");
                _effects?.Burst(m.Position + Vector3.up * (m.Type.hoverM + m.Type.heightM * 0.5f), m.Figure.Style, m.Type, 26);
                return;
            }
            if (away.sqrMagnitude > 1e-6f) m.Position += away.normalized * (m.Type.kind == "fodder" ? 0.35f : 0.12f);
            if (m.Type.kind != "boss") { m.State = MonsterState.Hit; m.StateTime = 0; Play(m, "hit"); }
        }

        /// <summary>Living monsters within an arc in front of `origin`, nearest first.</summary>
        public List<Monster> InArc(Vector3 origin, float yawDeg, float reach, float arcDeg, int max)
        {
            var list = new List<Monster>();
            var fwd = new Vector3(Mathf.Sin(yawDeg * Mathf.Deg2Rad), 0, Mathf.Cos(yawDeg * Mathf.Deg2Rad));
            foreach (var m in All)
            {
                if (!m.Alive || m.Type.kind == "boss") continue; // the dragon is set dressing
                var d = m.Position - origin;
                d.y = 0;
                float dist = d.magnitude;
                if (dist > reach + m.Type.radius) continue;
                if (arcDeg < 360f && dist > 0.01f && Vector3.Angle(fwd, d) > arcDeg * 0.5f) continue;
                list.Add(m);
            }
            list.Sort((a, b) => (a.Position - origin).sqrMagnitude.CompareTo((b.Position - origin).sqrMagnitude));
            if (list.Count > max) list.RemoveRange(max, list.Count - max);
            return list;
        }

        public Monster Nearest(Vector3 origin)
        {
            Monster best = null;
            float bestSq = float.MaxValue;
            foreach (var m in All)
            {
                if (!m.Alive || m.Type.kind == "boss") continue;
                float sq = (m.Position - origin).sqrMagnitude;
                if (sq < bestSq) { bestSq = sq; best = m; }
            }
            return best;
        }

        static void Play(Monster m, string state, float offset = 0f)
        {
            var a = m.Figure.Animator;
            if (a == null || Array.IndexOf(m.Type.states, state) < 0) return;
            a.CrossFadeInFixedTime(state, 0.12f, 0, offset);
        }

        // Some vendor walk and idle clips don't loop; restart them as they end.
        static void KeepLooping(Monster m)
        {
            if (m.State != MonsterState.Walk && m.State != MonsterState.Hold) return;
            var a = m.Figure.Animator;
            if (a == null || a.IsInTransition(0)) return;
            var info = a.GetCurrentAnimatorStateInfo(0);
            if (!info.loop && info.normalizedTime >= 0.98f) a.Play(info.fullPathHash, 0, 0f);
        }
    }
}
