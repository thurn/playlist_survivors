using System;
using System.Collections.Generic;
using Champions.Render;
using Champions.Render.Crowd;
using Champions.Sim;
using UnityEngine;

namespace Champions.Client
{
    /// <summary>
    /// The horde drawn in the neon edge trailer's look (ps-6y2.28's port): each mob its type's vendor mesh in the crowd
    /// (<see cref="MonsterCrowd"/>), in its type's levelled hue, walking its clip by the ground it covers, idling, or
    /// playing its attack into its landing, its body springing from its beat steps and kicks (<see cref="MonsterMotion"/>),
    /// and a kill shattering it on its frame into crack-outlined pieces thrown along the blow (<see cref="MonsterShatter"/>).
    /// A flyer hovers at its type's height. Each type writes its monster code in the stills' type-ID pass.
    /// </summary>
    public sealed class TrailerCrowd : ICrowdView
    {
        public const int CapacityPerType = 1024;
        /// <summary>A kill's pieces fly this far (m) at the weakest blow, this much further per base hit, at most the most.</summary>
        public const float LeastThrow = 1.4f, ThrowPerHit = 0.35f, MostThrow = 3.2f;

        struct Entry
        {
            public CrowdMob Mob;
            public bool Dying;
            public double Time;
            public Vector2 Blow;
            public float Power;
        }

        readonly MonsterLibrary _lib;
        readonly MonsterCrowd _crowd;
        readonly MonsterShatter _shatter;
        readonly int[] _typeOfCast;
        readonly int[] _idle, _walk, _attack;
        readonly float[] _walkLength, _stride;
        readonly List<Entry>[] _frame;
        int _serial;
        double _last = double.NaN;

        public TrailerCrowd(MonsterLibrary lib, EnemyRules cast, Palette palette)
        {
            _lib = lib;
            _crowd = new MonsterCrowd(lib, CapacityPerType, palette);
            _shatter = new MonsterShatter(lib, _crowd.Palette, palette);
            int n = lib.Types.Length;
            _idle = new int[n];
            _walk = new int[n];
            _attack = new int[n];
            _walkLength = new float[n];
            _stride = new float[n];
            _frame = new List<Entry>[n];
            for (int t = 0; t < n; t++)
            {
                var type = lib.Types[t];
                _idle[t] = Math.Max(0, type.Clip("idle"));
                _walk[t] = type.Clip("walk") >= 0 ? type.Clip("walk") : _idle[t];
                _attack[t] = type.Clip("attack");
                _walkLength[t] = type.Clips.Length > 0 ? Math.Max(0.05f, type.Clips[_walk[t]].Length) : 1;
                _stride[t] = type.Clips.Length > 0 && type.Clips[_walk[t]].StrideM > 0 ? type.Clips[_walk[t]].StrideM : 1.5f;
                _frame[t] = new List<Entry>();
            }
            _typeOfCast = new int[cast.Cast.Length];
            for (int c = 0; c < _typeOfCast.Length; c++)
            {
                _typeOfCast[c] = lib.IndexOf(cast.Cast[c].Id);
                if (_typeOfCast[c] >= 0) _crowd.SetIdCode(_typeOfCast[c], IdPass.Monster(c));
                else if (!cast.Cast[c].IsBoss) Debug.LogWarning($"TrailerCrowd: no baked {cast.Cast[c].Id} (champions monster bake {cast.Cast[c].Id}): it walks unseen");
            }
        }

        /// <summary>The crowd on <paramref name="look"/>'s baked monsters, or null when none is baked (a checkout without the vendor files).</summary>
        public static TrailerCrowd Create(MonsterLook look, EnemyRules cast, Palette palette)
        {
            var lib = MonsterLibrary.Load(look);
            return lib == null ? null : new TrailerCrowd(lib, cast, palette);
        }

        /// <summary>
        /// The crowd and its shatter, for another writer of a frame's types (a boss's presenter): write a type the horde never
        /// has between the crowd presenter's Present and its Draw, which draws every type written that frame.
        /// </summary>
        public MonsterCrowd Crowd => _crowd;
        public MonsterShatter Shatter => _shatter;

        public void Begin(double song)
        {
            foreach (var list in _frame) list.Clear();
        }

        public void Mob(in CrowdMob mob) => Add(new Entry { Mob = mob });

        public void Break(in CrowdMob mob, double time, Vector2 blow, float power) =>
            Add(new Entry { Mob = mob, Dying = true, Time = time, Blow = blow, Power = power });

        void Add(in Entry e)
        {
            int type = e.Mob.Cast < _typeOfCast.Length ? _typeOfCast[e.Mob.Cast] : -1;
            if (type >= 0 && _frame[type].Count < CapacityPerType) _frame[type].Add(e);
        }

        public void Draw(double song, Camera camera)
        {
            if (!double.IsNaN(_last) && song < _last - 1e-6) _shatter.Clear();
            _last = song;
            for (int type = 0; type < _frame.Length; type++)
            {
                var list = _frame[type];
                if (list.Count == 0) continue;
                var slots = _crowd.Instances(type, list.Count);
                var motion = _crowd.Motion(type, list.Count);
                var t = _lib.Types[type];
                for (int i = 0; i < list.Count; i++)
                {
                    var e = list[i];
                    var m = e.Mob;
                    Pose(type, m, out uint clip, out float time);
                    var body = new MonsterInstance
                    {
                        Position = m.Position + Vector3.up * t.HoverM, Yaw = m.Yaw, ClipA = clip, TimeA = time, ClipB = clip, TimeB = time,
                        Blend = 1, Scale = 1, HitFlash = m.HitFlash, Flare = m.Flare, Flags = (uint)CrowdFlags.Alive,
                    };
                    float height = Math.Max(0.1f, t.HeightM);
                    motion[i] = new MonsterMotion
                    {
                        StepAge = m.StepAge, Travel = m.StepTravel / height, KickAge = m.KickAge, KickWind = m.KickWind,
                        Kick = m.Kick.sqrMagnitude > 1e-6f ? m.Kick.normalized * (m.KickWind > 0 ? MonsterMotion.LungeKick : MonsterMotion.HitKick) : Vector2.zero,
                        Seed = (uint)m.Serial * 2654435761u + 1,
                    };
                    if (e.Dying)
                    {
                        body.Flags = (uint)CrowdFlags.Dying;
                        float force = Mathf.Clamp(LeastThrow + ThrowPerHit * e.Power, LeastThrow, MostThrow);
                        _shatter.Break(type, i, body, ++_serial, e.Time, e.Blow.x, e.Blow.y, force);
                    }
                    slots[i] = body;
                }
            }
            _crowd.Draw(camera, song);
            _shatter.Frame(song, camera);
        }

        /// <summary>The clip and its time for the mob's action: a walk by the ground covered, an attack by its landing, an idle.</summary>
        void Pose(int type, in CrowdMob m, out uint clip, out float time)
        {
            var clips = _lib.Types[type].Clips;
            float phase = (m.Serial * 0.618f) % 1f;
            if (m.Action == CrowdAction.Attack && _attack[type] >= 0)
            {
                var c = clips[_attack[type]];
                clip = (uint)_attack[type];
                time = Mathf.Clamp(c.Impact - m.ActionTime, 0, c.Length);
            }
            else if (m.Action == CrowdAction.Walk)
            {
                clip = (uint)_walk[type];
                time = (phase + m.ActionTime / _stride[type]) * _walkLength[type];
            }
            else
            {
                clip = (uint)_idle[type];
                time = m.ActionTime + phase * 3;
            }
        }

        public void Queue(Camera camera)
        {
            _crowd.Queue(camera);
            _shatter.Queue(camera);
        }

        public void Dispose()
        {
            _shatter.Dispose();
            _crowd.Dispose();
        }
    }
}
