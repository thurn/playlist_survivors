using System;
using System.Collections.Generic;
using Champions.Sim;
using UnityEngine;

namespace Champions.Client
{
    /// <summary>
    /// The horde on screen (design.md, The Horde &gt; Moving to the beat, The Look &gt; Shatter): every mob the sim spawns, where
    /// its step or glide puts it at the frame's song time (<see cref="Mob.At"/>, read from the sim's horde, which the frame
    /// trails by the lead), walking by the ground it covers, facing where it goes (the hero when it stands), flashing when
    /// hit, an elite playing its attack into its telegraphed landing and a mushroom's lines flaring before its spore. A kill
    /// breaks it on the kill's own frame, thrown along the blow; a mob left behind just goes. It hands each frame's mobs to
    /// an <see cref="ICrowdView"/>, the drawing (the beat wave's flare and hop are the drawing's shaders').
    /// </summary>
    public sealed class CrowdPresenter : IDisposable
    {
        public const float HitFlashS = 0.15f, TurnRate = 10f;
        /// <summary>An elite's attack plays on this long past its landing.</summary>
        public const double FollowThroughS = 0.4;

        sealed class Tracked
        {
            public int Id, Cast, Serial;
            public double SpawnedAt, GoneAt = double.NaN, KilledAt = double.NaN, HitAt = double.NegativeInfinity;
            public double FlareFrom = double.NaN, FlareTo = double.NaN, ActFrom = double.NaN, ActTo = double.NaN;
            public Mob Motion;
            public Vector2 KillAt, Blow, Last, HitWay, Aim;
            public float Power, Yaw, Odometer;
            public bool Placed;
        }

        readonly ICrowdView _view;
        readonly EnemyRules _cast;
        readonly int _mushroom;
        readonly Dictionary<int, Tracked> _live = new Dictionary<int, Tracked>();
        readonly List<Tracked> _tracked = new List<Tracked>();
        int _serial;

        public CrowdPresenter(ICrowdView view, EnemyRules cast)
        {
            _view = view ?? throw new ArgumentNullException(nameof(view));
            _cast = cast;
            _mushroom = cast.IndexOf("mushroom");
        }

        public ICrowdView View => _view;

        /// <summary>Mobs drawn alive on the latest frame, and every break handed on.</summary>
        public int Drawn { get; private set; }
        public int Breaks { get; private set; }

        /// <summary>One settled event, in log order.</summary>
        public void Take(in SimEvent e)
        {
            switch (e.Kind)
            {
                case SimEventKind.EnemySpawn:
                {
                    // Bosses are the boss presenter's; a test's dummies (type 0) are nobody's.
                    int cast = e.I1 - 1;
                    if (e.I2 != 0 || cast < 0 || cast >= _cast.Cast.Length) break;
                    var t = new Tracked { Id = e.I0, Cast = cast, SpawnedAt = e.T, Serial = ++_serial };
                    t.Motion.FromX = t.Motion.ToX = e.D0;
                    t.Motion.FromY = t.Motion.ToY = e.D1;
                    t.Motion.Start = t.Motion.End = e.T;
                    _live[e.I0] = t;
                    _tracked.Add(t);
                    break;
                }
                case SimEventKind.EnemyGone when _live.TryGetValue(e.I0, out var gone):
                    gone.GoneAt = e.T;
                    _live.Remove(e.I0);
                    break;
                case SimEventKind.Kill when _live.TryGetValue(e.I0, out var killed):
                    killed.KilledAt = e.T;
                    killed.KillAt = new Vector2((float)e.D3, (float)e.D4);
                    killed.Blow = new Vector2((float)e.D0, (float)e.D1);
                    killed.Power = (float)e.D2;
                    _live.Remove(e.I0);
                    break;
                case SimEventKind.Hit when _live.TryGetValue(e.I0, out var hit):
                    hit.HitAt = e.T;
                    hit.HitWay = new Vector2((float)e.D0, (float)e.D1);
                    break;
                case SimEventKind.EliteTelegraph when _live.TryGetValue(e.I0, out var elite):
                    elite.ActFrom = e.D0;
                    elite.ActTo = e.D1;
                    elite.Aim = new Vector2((float)e.D4, (float)e.D5);
                    if (elite.Cast == _mushroom)
                    {
                        elite.FlareFrom = e.D0;
                        elite.FlareTo = e.D1;
                    }
                    break;
                case SimEventKind.EliteMove when e.I2 != 0 && _live.TryGetValue(e.I0, out var calledOff):
                    calledOff.ActTo = Math.Min(calledOff.ActTo, e.T);
                    calledOff.FlareTo = Math.Min(calledOff.FlareTo, e.T);
                    break;
            }
        }

        /// <summary>
        /// The frame at song time <paramref name="song"/>, <paramref name="dt"/> s after the last, the hero's feet at
        /// <paramref name="hero"/>: each living mob's step from the sim's horde, its pose to the view, a mob killed by now
        /// broken on this frame. The view draws after the other writers of its frame (<see cref="Draw"/>).
        /// </summary>
        public void Present(double song, Simulation sim, Vector2 hero, float dt)
        {
            _view.Begin(song);
            var horde = sim.Horde;
            Drawn = 0;
            foreach (var t in _tracked)
            {
                if (double.IsNaN(t.KilledAt) && double.IsNaN(t.GoneAt) && horde.Owns(t.Id)) t.Motion = horde.Mobs[t.Id];
                if (song < t.SpawnedAt || song >= t.GoneAt) continue;
                var m = Pose(t, song, hero, dt);
                if (song >= t.KilledAt)
                {
                    m.Position = new Vector3(t.KillAt.x, 0, t.KillAt.y);
                    _view.Break(m, t.KilledAt, t.Blow, t.Power);
                    Breaks++;
                    continue;
                }
                _view.Mob(m);
                Drawn++;
            }
            // The dead and the departed have had their last frame.
            _tracked.RemoveAll(t => song >= t.KilledAt || song >= t.GoneAt);
        }

        /// <summary>Draws the frame's crowd for <paramref name="camera"/> (after every writer of it).</summary>
        public void Draw(double song, Camera camera) => _view.Draw(song, camera);

        /// <summary>Queues the frame's draws again (the stills' second drawing of a frame).</summary>
        public void Queue(Camera camera) => _view.Queue(camera);

        CrowdMob Pose(Tracked t, double song, Vector2 hero, float dt)
        {
            t.Motion.At(song, out double x, out double y);
            var at = new Vector2((float)x, (float)y);
            var step = t.Placed ? at - t.Last : Vector2.zero;
            float moved = step.magnitude;
            t.Placed = true;
            t.Last = at;
            t.Odometer += moved;
            // Facing: along its step while it steps, along its motion while it moves, else the hero.
            var way = new Vector2((float)(t.Motion.ToX - t.Motion.FromX), (float)(t.Motion.ToY - t.Motion.FromY));
            bool moving = song < t.Motion.End && way.sqrMagnitude > 1e-6f;
            var heading = moving ? way : moved > 1e-4f ? step : hero - at;
            if (heading.sqrMagnitude > 1e-6f)
            {
                float want = Mathf.Atan2(heading.x, heading.y) * Mathf.Rad2Deg;
                t.Yaw = Mathf.MoveTowardsAngle(t.Yaw * Mathf.Rad2Deg, want, TurnRate * Mathf.Rad2Deg * Mathf.Max(dt, 1e-3f)) * Mathf.Deg2Rad;
            }
            var m = new CrowdMob
            {
                Id = t.Id, Cast = t.Cast, Position = new Vector3(at.x, 0, at.y), Yaw = t.Yaw, Serial = t.Serial,
                HitFlash = Mathf.Clamp01(1 - (float)(song - t.HitAt) / HitFlashS),
                Flare = !double.IsNaN(t.FlareTo) && song >= t.FlareFrom && song <= t.FlareTo ? 1 : 0,
                StepAge = CrowdMob.Long, KickAge = CrowdMob.Long,
            };
            if (t.Motion.Motion == Champions.Sim.Motion.Step && song >= t.Motion.Start)
            {
                m.StepAge = (float)(song - t.Motion.Start);
                m.StepTravel = way;
            }
            // A kick: an elite's lunge into its landing, else the latest hit's knock.
            if (!double.IsNaN(t.ActTo) && song >= t.ActFrom && song <= t.ActTo + FollowThroughS)
            {
                m.KickAge = (float)(song - t.ActTo);
                m.KickWind = (float)(t.ActTo - t.ActFrom);
                m.Kick = t.Aim;
            }
            else if (song - t.HitAt < 1)
            {
                m.KickAge = (float)(song - t.HitAt);
                m.Kick = t.HitWay;
            }
            if (!double.IsNaN(t.ActTo) && song >= t.ActFrom && song <= t.ActTo + FollowThroughS)
            {
                m.Action = CrowdAction.Attack;
                m.ActionTime = (float)(t.ActTo - song);
            }
            else if (moving || moved > 1e-4f)
            {
                m.Action = CrowdAction.Walk;
                m.ActionTime = t.Odometer;
            }
            else
            {
                m.Action = CrowdAction.Idle;
                m.ActionTime = (float)song;
            }
            return m;
        }

        public void Dispose() => _view.Dispose();
    }
}
