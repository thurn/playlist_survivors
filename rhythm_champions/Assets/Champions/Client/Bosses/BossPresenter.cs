using System;
using Champions.Render;
using Champions.Render.Crowd;
using Champions.Sim;
using UnityEngine;

namespace Champions.Client
{
    /// <summary>Draws the three encounters in the crowd's edge look, without clearing the retained field.</summary>
    public sealed class BossPresenter : IDisposable
    {
        /// <summary>A hit's flash fades over this long.</summary>
        public const float HitFlashS = 0.15f;
        /// <summary>The death's pieces fly as from a blow this strong (the shatter's launch distance).</summary>
        public const float DeathForce = 3f;
        /// <summary>The tallest a stand-in stands (the dragon's size is its wingspan).</summary>
        public const float MaxHeightM = 5f;

        readonly TrailerCrowd _crowd;
        readonly IWorldCommands _world;
        readonly EnemyRules _cast;
        readonly Palette _palette;
        readonly int _layer;
        GameObject _standIn;
        Material _material;
        BossAnimator _animator;
        int _id = -1, _castIndex = -1, _type = -1, _serial;
        double _arrived = double.NaN, _died = double.NaN, _hitAt = double.NegativeInfinity, _lastSong = double.NaN;
        Vector2 _blow, _deathAt, _lastAt;
        float _speed;
        bool _broken;

        /// <param name="crowd">The trailer's crowd it writes the boss into, or null (a stand-in).</param>
        /// <param name="layer">The stand-in's layer (the camera's).</param>
        public BossPresenter(TrailerCrowd crowd, EnemyRules cast, Palette palette, IWorldCommands world, int layer = 0)
        {
            _crowd = crowd;
            _cast = cast;
            _palette = palette;
            _world = world;
            _layer = layer;
        }

        /// <summary>The boss shown now, its cast index (-1 none), and whether a stand-in draws it.</summary>
        public int Cast => _castIndex;
        public bool StandsIn => _standIn != null;

        /// <summary>Its death broke it on this frame (the death's impact frame).</summary>
        public bool BrokeThisFrame { get; private set; }

        /// <summary>The boss's instance as the latest <see cref="Present"/> wrote it into the crowd.</summary>
        public MonsterInstance Instance { get; private set; }

        /// <summary>One settled event, in log order.</summary>
        public void Take(in SimEvent e)
        {
            switch (e.Kind)
            {
                case SimEventKind.BossCall:
                    // The camera eases out over the retained field, so the boss lands whole in the frame.
                    _world?.Duel(true);
                    break;
                case SimEventKind.BossArrive:
                    Arrive(e);
                    break;
                case SimEventKind.BossTelegraph:
                    _animator?.Telegraph(e.MarkMove, e.MarkPart, e.T, e.D0);
                    break;
                case SimEventKind.BossMove:
                    if (e.I2 == 0) _animator?.Landed(e.MarkMove, e.MarkPart, e.T, e.D4);
                    break;
                case SimEventKind.Hit:
                    if (e.I0 == _id && double.IsNaN(_died)) _hitAt = e.T;
                    break;
                case SimEventKind.BossDeath:
                    if (e.I0 != _id) break;
                    _died = e.T;
                    _deathAt = new Vector2((float)e.D0, (float)e.D1);
                    _blow = new Vector2((float)e.D2, (float)e.D3);
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
            _hitAt = double.NegativeInfinity;
            _broken = false;
            _lastSong = double.NaN;
            _lastAt = new Vector2((float)e.D0, (float)e.D1);
            string id = _cast.Cast[_castIndex].Id;
            var lib = _crowd?.Crowd.Library;
            _type = lib != null ? lib.IndexOf(id) : -1;
            DropStandIn();
            if (_type >= 0)
            {
                _crowd.Crowd.SetIdCode(_type, IdPass.Monster(_castIndex));
                _animator = new BossAnimator(Array.ConvertAll(lib.Types[_type].Clips,
                    c => new ClipInfo(c.Role, c.Name, c.Length, c.Impact, c.End, c.Loop, c.StrideM)));
            }
            else
            {
                if (_crowd != null) Debug.LogWarning($"BossPresenter: no baked {id} (champions monster bake {id}): a stand-in draws it");
                _animator = null;
                MakeStandIn();
            }
            _animator?.Arrived(e.T);
            _world?.Ring(RingKind.BossEntrance, _lastAt, e.T, Colour());
            _world?.Duel(true);
        }

        Color Colour() => _palette.ForType(_cast.Cast[_castIndex].Id);

        /// <summary>
        /// The frame at song time <paramref name="song"/>: the boss where the sim had it then, posed, into the crowd; its
        /// death breaks it on the first frame at or after it, and it draws no more.
        /// </summary>
        public void Present(double song, Simulation sim)
        {
            BrokeThisFrame = false;
            if (_id < 0 || song < _arrived) return;
            bool dead = song >= _died;
            Vector2 at;
            double facing;
            if (!dead)
            {
                sim.Bosses.At(song, out double x, out double y, out facing);
                at = new Vector2((float)x, (float)y);
            }
            else
            {
                at = _deathAt;
                sim.Bosses.At(_died, out _, out _, out facing);
            }
            float dt = double.IsNaN(_lastSong) ? 0 : (float)(song - _lastSong);
            if (dt > 1e-4) _speed = Mathf.Lerp(_speed, (at - _lastAt).magnitude / dt, Mathf.Clamp01(dt * 12));
            _lastSong = song;
            _lastAt = at;
            if (_standIn != null)
            {
                _standIn.SetActive(!dead);
                _standIn.transform.position = new Vector3(at.x, 0, at.y);
                if (dead && !_broken) _broken = BrokeThisFrame = true;
                return;
            }
            if (_type < 0 || (dead && _broken)) return;
            var pose = _animator.At(song, _speed);
            var body = new MonsterInstance
            {
                Position = new Vector3(at.x, 0, at.y), Yaw = Mathf.PI / 2 - (float)facing,
                ClipA = (uint)pose.ClipA, TimeA = pose.TimeA, ClipB = (uint)pose.ClipB, TimeB = pose.TimeB, Blend = pose.Blend,
                Scale = _cast.Cast[_castIndex].Id == "conductor" ? 4f / 2.6f : 1, HitFlash = 0.35f * Mathf.Clamp01(1 - (float)(song - _hitAt) / HitFlashS), Flags = (uint)CrowdFlags.Alive,
            };
            if (dead)
            {
                body.Flags = (uint)CrowdFlags.Dying;
                _crowd.Shatter.Break(_type, 0, body, ++_serial, _died, _blow.x, _blow.y, DeathForce);
                _broken = BrokeThisFrame = true;
            }
            Instance = body;
            var slots = _crowd.Crowd.Instances(_type, 1);
            slots[0] = body;
        }

        void MakeStandIn()
        {
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
