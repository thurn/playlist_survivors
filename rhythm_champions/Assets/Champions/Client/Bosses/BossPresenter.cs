using System;
using Champions.Render;
using Champions.Render.Crowd;
using Champions.Sim;
using UnityEngine;

namespace Champions.Client
{
    /// <summary>
    /// The bosses as the player sees them (design.md, Bosses, The Look &gt; Edge cuts, Shatter and Restraint): the boss's edge
    /// cut (ps-6y2.12's <c>fatOgre</c> and <c>dragon</c>) in its palette colour, written into the edge crowd each frame at its
    /// sim position and facing (<see cref="Bosses.At"/>) with the clip <see cref="BossAnimator"/> picks; a hit flashes it;
    /// its death breaks it into its pieces on the killing frame, thrown along the blow. It sends the world its special
    /// moments: the entrance's ring (and the duel's camera), Break's ring as a stagger begins, and the death's ring (the duel
    /// over). Without a baked cut it draws a glowing capsule stand-in. The crowd and the shatter belong to whoever draws
    /// them (<see cref="EdgeCrowd.Draw"/> after every writer, <see cref="EdgeShatter.Frame"/> after that). The assembly's
    /// EventDispatch hands it the events.
    /// </summary>
    public sealed class BossPresenter : IDisposable
    {
        /// <summary>A hit's flash fades over this long.</summary>
        public const float HitFlashS = 0.15f;
        /// <summary>The death's pieces fly as from a blow this strong (the shatter's launch distance).</summary>
        public const float DeathForce = 3f;

        readonly EdgeCrowd _crowd;
        readonly EdgeShatter _shatter;
        readonly IWorldCommands _world;
        readonly EnemyRules _cast;
        readonly Palette _palette;
        readonly Material _standInMaterial;
        GameObject _standIn;
        Material _standInOwn;
        BossAnimator _animator;
        int _id = -1, _castIndex = -1, _type = -1, _serial;
        double _arrived = double.NaN, _died = double.NaN, _hitAt = double.NegativeInfinity, _lastSong = double.NaN;
        Vector2 _blow, _deathAt, _lastAt;
        float _speed;
        bool _broken;

        /// <param name="crowd">The edge crowd it writes the boss into, or null (stand-ins only).</param>
        /// <param name="shatter">The crowd's shatter, for the death's pieces; null with no crowd.</param>
        public BossPresenter(EdgeCrowd crowd, EdgeShatter shatter, EnemyRules cast, Palette palette, IWorldCommands world)
        {
            _crowd = crowd;
            _shatter = shatter;
            _cast = cast;
            _palette = palette;
            _world = world;
            var shader = Resources.Load<Shader>("Champions/StandIn") ?? throw new InvalidOperationException("no Champions/StandIn shader");
            _standInMaterial = new Material(shader) { name = "Boss stand-in" };
            _standInMaterial.SetColor("_Body", palette.VoidBody);
        }

        /// <summary>The boss shown now, its cast index (-1 none), and whether it draws as a stand-in.</summary>
        public int Cast => _castIndex;
        public bool StandIn => _standIn != null;

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
                case SimEventKind.BossTelegraph:
                    _animator?.Telegraph(e.MarkMove, e.MarkPart, e.T, e.D0);
                    break;
                case SimEventKind.BossMove:
                    if (e.I2 == 0) _animator?.Landed(e.MarkMove, e.MarkPart, e.T, e.D4);
                    break;
                case SimEventKind.BossStagger:
                    if (e.I1 == 1)
                    {
                        _animator?.Stagger(e.T, e.D0);
                        _world?.Ring(RingKind.Break, Where(), e.T, _palette.HeroBreak);
                    }
                    else _animator?.Stagger(double.NaN, double.NaN);
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
            _broken = false;
            _lastSong = double.NaN;
            _lastAt = new Vector2((float)e.D0, (float)e.D1);
            string id = _cast.Cast[_castIndex].Id;
            var lib = _crowd != null ? EdgeCutLibrary.Load() : null;
            _type = lib != null ? lib.IndexOf(id) : -1;
            if (_type >= 0)
            {
                _crowd.SetIdCode(_type, IdPass.Monster(_castIndex));
                _animator = new BossAnimator(_crowd.Type(_type).Clips);
            }
            else
            {
                Debug.LogWarning($"BossPresenter: no baked edge cut for {id} (champions edgecut {id}): a stand-in draws it");
                _animator = null;
                MakeStandIn(id);
            }
            if (_type >= 0) DropStandIn();
            _animator?.Arrived(e.T);
            _world?.Ring(RingKind.BossEntrance, _lastAt, e.T, Colour());
            _world?.Duel(true);
        }

        Color Colour() => _palette.ForType(_cast.Cast[_castIndex].Id);

        Vector2 Where() => _lastAt;

        /// <summary>
        /// The frame at song time <paramref name="song"/>: the boss where the sim had it then, posed, into the crowd (call before
        /// the crowd draws); its death breaks it on the first frame at or after it.
        /// </summary>
        public void Present(double song, Simulation sim)
        {
            BrokeThisFrame = false;
            if (_id < 0 || song < _arrived) return;
            bool dead = song >= _died;
            Vector2 at;
            float facing;
            if (double.IsNaN(_died) || song < _died)
            {
                sim.Bosses.At(song, out double x, out double y, out double f);
                at = new Vector2((float)x, (float)y);
                facing = (float)f;
            }
            else
            {
                at = _deathAt;
                sim.Bosses.At(_died, out _, out _, out double f);
                facing = (float)f;
            }
            float dt = double.IsNaN(_lastSong) ? 0 : (float)(song - _lastSong);
            if (dt > 1e-4) _speed = Mathf.Lerp(_speed, (at - _lastAt).magnitude / dt, Mathf.Clamp01(dt * 12));
            _lastSong = song;
            _lastAt = at;
            if (_standIn != null)
            {
                _standIn.SetActive(!dead);
                _standIn.transform.position = new Vector3(at.x, _standIn.transform.localScale.y, at.y);
                return;
            }
            if (_type < 0 || (dead && _broken)) return;
            var pose = _animator.At(song, _speed);
            var body = new MonsterInstance
            {
                Position = new Vector3(at.x, 0, at.y), Yaw = Mathf.PI / 2 - facing,
                ClipA = (uint)pose.ClipA, TimeA = pose.TimeA, ClipB = (uint)pose.ClipB, TimeB = pose.TimeB, Blend = pose.Blend,
                Scale = 1, HitFlash = Mathf.Clamp01(1 - (float)(song - _hitAt) / HitFlashS),
                Tint = MonsterInstance.Pack(Colour()), Flags = (uint)CrowdFlags.Alive,
            };
            if (dead)
            {
                body.Flags = (uint)CrowdFlags.Dying;
                _shatter?.Break(_type, 0, body, ++_serial, _died, _blow.x, _blow.y, DeathForce);
                _broken = true;
                BrokeThisFrame = true;
            }
            var slots = _crowd.Instances(_type, 1);
            slots[0] = body;
        }

        void MakeStandIn(string id)
        {
            DropStandIn();
            var type = _cast.Cast[_castIndex];
            _standIn = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            _standIn.name = "Boss stand-in " + id;
            Destroy(_standIn.GetComponent<Collider>());
            _standInOwn = new Material(_standInMaterial) { name = "Boss stand-in " + id };
            _standInOwn.SetColor("_Color", Colour());
            _standInOwn.SetFloat(IdPass.Id, IdPass.Monster(_castIndex));
            _standIn.GetComponent<MeshRenderer>().sharedMaterial = _standInOwn;
            float height = Mathf.Min((float)type.SizeM, 5f), radius = (float)type.RadiusM;
            // Unity's capsule is 2 m tall and 1 m across.
            _standIn.transform.localScale = new Vector3(2 * radius, height / 2, 2 * radius);
        }

        void DropStandIn()
        {
            Destroy(_standIn);
            Destroy(_standInOwn);
            _standIn = null;
            _standInOwn = null;
        }

        static void Destroy(UnityEngine.Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(o);
            else UnityEngine.Object.DestroyImmediate(o);
        }

        public void Dispose()
        {
            DropStandIn();
            Destroy(_standInMaterial);
        }
    }
}
