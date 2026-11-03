using System;
using Champions.Render;
using Champions.Sim;
using UnityEngine;

namespace Champions.Client
{
    /// <summary>
    /// Maps the sim's events onto the world (<see cref="World"/>'s <see cref="IWorldCommands"/>) and presents it each
    /// frame at the frame's song time with the hero's feet. What it maps so far: reliable beats to beat rings, the beat
    /// turning unreliable, zone changes, on-beat dashes and mass kills (at least <see cref="MassKillCount"/> at one
    /// instant, a boss's shockwave's aside) to event rings. The assembly's EventDispatch takes this over and adds the rest
    /// (slams); the structures' and the bosses' presenters send their own rings and the duel's camera.
    /// </summary>
    public sealed class WorldPresenter : IDisposable
    {
        public const int MassKillCount = 12;
        const double SameInstant = 1e-4;

        readonly World _world;
        Vector2 _lastHero;
        bool _presented;
        double _killT = double.NaN;
        int _kills;
        Vector2 _killSum;

        public WorldPresenter(Camera camera, Palette palette, int zone = 0)
        {
            _world = new World(camera, palette, zone);
        }

        public World World => _world;

        /// <summary>One settled event, in log order.</summary>
        public void Take(in SimEvent e)
        {
            if (!double.IsNaN(_killT) && (e.Kind != SimEventKind.Kill || Math.Abs(e.T - _killT) > SameInstant)) Flush();
            switch (e.Kind)
            {
                case SimEventKind.Beat:
                    _world.Beat(e.T, e.D0, e.Downbeat, e.BeatReliability == Reliability.Reliable);
                    break;
                case SimEventKind.BeatState:
                    if ((Reliability)e.I0 != Reliability.Reliable) _world.Beatless(e.T);
                    break;
                case SimEventKind.Zone:
                    _world.Zone(e.T, e.I0, e.D0);
                    break;
                case SimEventKind.HeroDash:
                    // Routine timing reward stays local, including over empty ground.
                    if ((e.I2 & 1) != 0) _world.Ring(RingKind.DashOnBeat, new Vector2((float)e.D2, (float)e.D3), e.D0, Color.white);
                    break;
                case SimEventKind.Kill:
                    // A boss's entrance and finale kill everything on screen: their own ring stands for them (BossPresenter).
                    if ((BlowSource)e.I1 == BlowSource.Boss) break;
                    if (double.IsNaN(_killT)) _killT = e.T;
                    _kills++;
                    _killSum += new Vector2((float)e.D3, (float)e.D4);
                    break;
            }
        }

        /// <summary>Ends a batch of events: a run of kills at one instant that is a mass kill sends its ring.</summary>
        public void Flush()
        {
            if (double.IsNaN(_killT)) return;
            if (_kills >= MassKillCount)
                _world.Ring(RingKind.MassKill, _killSum / _kills, _killT, Color.white, Mathf.Clamp01((_kills - MassKillCount) / 40f));
            _killT = double.NaN;
            _kills = 0;
            _killSum = Vector2.zero;
        }

        /// <summary>The frame at song time <paramref name="now"/>, <paramref name="dt"/> s after the last, the hero's feet at <paramref name="hero"/> (the sim's ground x, y).</summary>
        public void Present(double now, Vector2 hero, float dt)
        {
            var velocity = _presented && dt > 0 ? (hero - _lastHero) / dt : Vector2.zero;
            _presented = true;
            _lastHero = hero;
            _world.Present(now, hero, velocity, dt);
        }

        public void Dispose() => _world.Dispose();
    }
}
