using System;
using Champions.Render;
using Champions.Sim;
using UnityEngine;

namespace Champions.Client
{
    /// <summary>
    /// The bosses as the player sees them (design.md, Bosses), as stand-ins until the trailer's edge look is ported: a
    /// capsule of the boss's size whose silhouette glows in its palette colour (<see cref="StandIn"/>), at its sim position
    /// (<see cref="Bosses.At"/>) each frame, gone on its death's frame. It sends the world its special moments: the
    /// entrance's ring (and the duel's camera), Break's ring as a stagger begins, and the death's ring (the duel over). The
    /// assembly's EventDispatch hands it the events.
    /// </summary>
    public sealed class BossPresenter : IDisposable
    {
        /// <summary>The tallest a stand-in stands (the dragon's size is its wingspan).</summary>
        public const float MaxHeightM = 5f;

        readonly IWorldCommands _world;
        readonly EnemyRules _cast;
        readonly Palette _palette;
        readonly int _layer;
        GameObject _standIn;
        Material _material;
        int _id = -1, _castIndex = -1;
        double _arrived = double.NaN, _died = double.NaN;
        Vector2 _deathAt, _lastAt;
        bool _broken;

        /// <param name="layer">The stand-in's layer (the camera's).</param>
        public BossPresenter(EnemyRules cast, Palette palette, IWorldCommands world, int layer = 0)
        {
            _cast = cast;
            _palette = palette;
            _world = world;
            _layer = layer;
        }

        /// <summary>The boss shown now, its cast index (-1 none).</summary>
        public int Cast => _castIndex;

        /// <summary>Its death broke it on this frame (the death's impact frame).</summary>
        public bool BrokeThisFrame { get; private set; }

        /// <summary>One settled event, in log order.</summary>
        public void Take(in SimEvent e)
        {
            switch (e.Kind)
            {
                case SimEventKind.BossArrive:
                    Arrive(e);
                    break;
                case SimEventKind.BossStagger:
                    if (e.I1 == 1) _world?.Ring(RingKind.Break, _lastAt, e.T, _palette.HeroBreak);
                    break;
                case SimEventKind.BossDeath:
                    if (e.I0 != _id) break;
                    _died = e.T;
                    _deathAt = new Vector2((float)e.D0, (float)e.D1);
                    _world?.Ring(RingKind.BossDeath, _deathAt, e.T, Colour());
                    _world?.Duel(false);
                    break;
            }
        }

        void Arrive(in SimEvent e)
        {
            _id = e.I0;
            _castIndex = e.I1;
            _arrived = e.T;
            _died = double.NaN;
            _broken = false;
            _lastAt = new Vector2((float)e.D0, (float)e.D1);
            MakeStandIn();
            _world?.Ring(RingKind.BossEntrance, _lastAt, e.T, Colour());
            _world?.Duel(true);
        }

        Color Colour() => _palette.ForType(_cast.Cast[_castIndex].Id);

        /// <summary>The frame at song time <paramref name="song"/>: the boss where the sim had it then; gone from its death's frame.</summary>
        public void Present(double song, Simulation sim)
        {
            BrokeThisFrame = false;
            if (_id < 0 || song < _arrived) return;
            if (song >= _died)
            {
                if (!_broken)
                {
                    _broken = true;
                    BrokeThisFrame = true;
                    _lastAt = _deathAt;
                }
                _standIn.SetActive(false);
                return;
            }
            sim.Bosses.At(song, out double x, out double y, out _);
            _lastAt = new Vector2((float)x, (float)y);
            _standIn.SetActive(true);
            _standIn.transform.position = new Vector3(_lastAt.x, 0, _lastAt.y);
        }

        void MakeStandIn()
        {
            DropStandIn();
            var type = _cast.Cast[_castIndex];
            _material = StandIn.Material("Boss stand-in " + type.Id, Colour(), _palette.VoidBody, IdPass.Monster(_castIndex));
            float height = Mathf.Min((float)type.SizeM, MaxHeightM), across = 2 * (float)type.RadiusM;
            _standIn = StandIn.Make("Boss stand-in " + type.Id, PrimitiveType.Capsule, new Vector3(across, height, across), _material, _layer);
            _standIn.SetActive(false);
        }

        void DropStandIn()
        {
            StandIn.Destroy(_standIn);
            StandIn.Destroy(_material);
            _standIn = null;
            _material = null;
        }

        public void Dispose() => DropStandIn();
    }
}
