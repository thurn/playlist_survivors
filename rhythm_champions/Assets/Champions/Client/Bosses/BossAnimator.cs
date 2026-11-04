using System;
using System.Collections.Generic;
using Champions.Sim;

namespace Champions.Client
{
    /// <summary>
    /// Which of a boss's clips plays at a song time, and how far in, from what the sim said of its moves (design.md,
    /// Bosses: "its attack clips always play"; the clip roles are its model's):
    /// - an attack's clip (the Ogre's smash, stomp, throw; the dragon's breath, tail, fireball) so its impact frame lands on
    ///   the move's first landing, starting partway in when the telegraph is shorter than its wind-up;
    /// - the Ogre's charge: its run (looping) along the charge's run;
    /// - the dragon's dive: its takeoff from the telegraph's start, its flight, and its landing so the touchdown lands on
    ///   the mark's beat, each sped up to fit a short dive;
    /// - the dragon's hit clip through a stagger (held at its end); the Ogre, which has none, stands;
    /// - the Ogre's roar from its entrance;
    /// - else its walk (paced by its ground speed and the clip's stride) or its idle.
    /// A change of clip crossfades over <see cref="BlendS"/>.
    /// </summary>
    internal sealed class BossAnimator
    {
        public const double BlendS = 0.12;
        /// <summary>The Ogre's run plays this much faster than its own pace (the charge outruns it).</summary>
        public const float RunRate = 1.5f;
        /// <summary>A dive's landing takes at most this share of the dive; its takeoff the rest.</summary>
        const double LandShare = 0.45;
        /// <summary>Crossing the ground faster than this, a boss with a run clip runs instead of walking (the Treasure Goblin's flight).</summary>
        const float SprintMps = 3f;

        public struct Pose
        {
            public int ClipA, ClipB;
            public float TimeA, TimeB, Blend;
        }

        struct Attack
        {
            public int Clip;
            public double Land;
        }

        readonly ClipInfo[] _clips;
        readonly int _idle, _walk, _sprint, _roar, _run, _takeoff, _fly, _land, _hit;
        readonly Dictionary<BossMove, int> _attackClip = new Dictionary<BossMove, int>();
        readonly List<Attack> _attacks = new List<Attack>();
        double _arrived = double.NaN, _runFrom = double.NaN, _runTo = double.NaN;
        double _diveStart = double.NaN, _diveLand = double.NaN, _staggerFrom = double.NaN, _staggerTo = double.NaN;
        int _current = -1, _previous = -1;
        float _currentTime, _previousTime, _walkPhase;
        double _changedAt, _last = double.NaN;

        public BossAnimator(ClipInfo[] clips)
        {
            _clips = clips;
            _idle = Math.Max(0, Role("idle"));
            _walk = Role("walk");
            _sprint = Role("run");
            _roar = Role("roar");
            _run = Role("charge");
            _takeoff = Role("takeoff");
            _fly = Role("fly");
            _land = Role("land");
            _hit = Role("hit");
            // A Boss Rush boss's clips by the moves it presents as, when it lacks the main game's (the goblin's stab, slice, cast).
            _attackClip[BossMove.Smash] = Role("smash", "stab");
            _attackClip[BossMove.Stomp] = Role("stomp");
            _attackClip[BossMove.Boulders] = Role("throw");
            _attackClip[BossMove.Breath] = Role("breath");
            _attackClip[BossMove.Tail] = Role("tail");
            _attackClip[BossMove.Fireballs] = Role("fireball");
            _attackClip[BossMove.Fans] = Role("attack", "slice");
            _attackClip[BossMove.Wall] = Role("attack");
            _attackClip[BossMove.Wave] = Role("stomp", "cast");
        }

        /// <summary>The first of <paramref name="roles"/> its clips have, or -1.</summary>
        int Role(params string[] roles)
        {
            foreach (var role in roles)
            {
                int i = Array.FindIndex(_clips, c => c.Role == role);
                if (i >= 0) return i;
            }
            return -1;
        }

        /// <summary>The boss landed at <paramref name="t"/> (its roar's start).</summary>
        public void Arrived(double t) => _arrived = t;

        /// <summary>A mark of <paramref name="move"/> (its part <paramref name="part"/>) fills from <paramref name="start"/> to <paramref name="land"/>.</summary>
        public void Telegraph(BossMove move, int part, double start, double land)
        {
            if (move == BossMove.Dive)
            {
                _diveStart = start;
                _diveLand = land;
                return;
            }
            if (part != 0 || !_attackClip.TryGetValue(move, out int clip) || clip < 0) return;
            _attacks.Add(new Attack { Clip = clip, Land = land });
            if (_attacks.Count > 4) _attacks.RemoveAt(0);
        }

        /// <summary>A mark of <paramref name="move"/> landed at <paramref name="t"/>; a charge then runs <paramref name="duration"/> s.</summary>
        public void Landed(BossMove move, int part, double t, double duration)
        {
            if (move == BossMove.Charge)
            {
                _runFrom = t;
                _runTo = t + duration;
            }
            else if (move == BossMove.Dive) _diveLand = t;
            else if (part == 0 && _attackClip.TryGetValue(move, out int clip) && clip >= 0)
                for (int i = 0; i < _attacks.Count; i++)
                    if (clip == _attacks[i].Clip && Math.Abs(_attacks[i].Land - t) < 0.25)
                        _attacks[i] = new Attack { Clip = _attacks[i].Clip, Land = t };
        }

        /// <summary>A stagger from <paramref name="from"/> to <paramref name="to"/> (as planned; its end event moves it).</summary>
        public void Stagger(double from, double to)
        {
            _staggerFrom = from;
            _staggerTo = to;
        }

        /// <summary>The pose at <paramref name="song"/>, the boss crossing the ground at <paramref name="speed"/> m/s.</summary>
        public Pose At(double song, float speed)
        {
            float dt = double.IsNaN(_last) ? 0 : (float)Math.Max(0, Math.Min(0.1, song - _last));
            _last = song;
            Choose(song, speed, dt, out int clip, out float time);
            if (clip != _current)
            {
                _previous = _current < 0 ? clip : _current;
                _previousTime = _currentTime;
                _current = clip;
                _changedAt = song;
            }
            else _previousTime += dt;
            _currentTime = time;
            float blend = (float)Math.Min(1, (song - _changedAt) / BlendS);
            return new Pose { ClipA = _current, TimeA = _currentTime, ClipB = _previous, TimeB = Clamp(_previous, _previousTime), Blend = blend };
        }

        void Choose(double song, float speed, float dt, out int clip, out float time)
        {
            if (song >= _staggerFrom && song < _staggerTo)
            {
                clip = _hit >= 0 ? _hit : _idle;
                time = Clamp(clip, (float)(song - _staggerFrom));
                return;
            }
            for (int i = _attacks.Count - 1; i >= 0; i--)
            {
                var c = _clips[_attacks[i].Clip];
                float t = c.Impact + (float)(song - _attacks[i].Land);
                if (t < 0 || t > End(c)) continue;
                clip = _attacks[i].Clip;
                time = t;
                return;
            }
            if (Dive(song, out clip, out time)) return;
            if (_run >= 0 && song >= _runFrom && song < _runTo)
            {
                clip = _run;
                time = (float)(song - _runFrom) * RunRate;
                return;
            }
            if (_roar >= 0 && song >= _arrived && song - _arrived < End(_clips[_roar]))
            {
                clip = _roar;
                time = (float)(song - _arrived);
                return;
            }
            if (_walk >= 0 && speed > 0.2f)
            {
                int gait = _sprint >= 0 && speed > SprintMps ? _sprint : _walk;
                var w = _clips[gait];
                _walkPhase += w.StrideM > 0 ? dt * speed * w.Length / w.StrideM : dt;
                clip = gait;
                time = _walkPhase;
                return;
            }
            clip = _idle;
            time = (float)song;
        }

        /// <summary>The dive's clips at <paramref name="song"/>: takeoff, flight, then the landing sped up to touch down on its beat.</summary>
        bool Dive(double song, out int clip, out float time)
        {
            clip = -1;
            time = 0;
            if (_land < 0 || _takeoff < 0 || !(song >= _diveStart)) return false;
            var up = _clips[_takeoff];
            var down = _clips[_land];
            double dive = Math.Max(1e-3, _diveLand - _diveStart);
            if (song > _diveLand + (End(down) - down.Impact)) return false;
            double landPhase = Math.Min(down.Impact, LandShare * dive);
            double upPhase = Math.Min(up.Length, dive - landPhase);
            double since = song - _diveStart;
            if (since < upPhase)
            {
                clip = _takeoff;
                time = (float)(since * up.Length / upPhase);
            }
            else if (song < _diveLand - landPhase)
            {
                clip = _fly >= 0 ? _fly : _takeoff;
                time = _fly >= 0 ? (float)(since - upPhase) : up.Length;
            }
            else
            {
                clip = _land;
                time = song < _diveLand
                    ? down.Impact - (float)((_diveLand - song) * down.Impact / landPhase)
                    : down.Impact + (float)(song - _diveLand);
            }
            return true;
        }

        static float End(in ClipInfo c) => c.End > 0 ? c.End : c.Length;

        /// <summary>A clip's time, held at its end unless it loops.</summary>
        float Clamp(int clip, float time) => clip < 0 || _clips[clip].Loop ? time : Math.Min(time, _clips[clip].Length);
    }
}
