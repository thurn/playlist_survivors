using System;
using System.Collections.Generic;
using Game.Client.Loop;
using PS.Game.Sim;
using UnityEngine;

namespace Game.Client.Crowd
{
    /// <summary>
    /// The crystal horde (unity-game.md, The Crowd): the bone-texture crowd driven by the sim's
    /// events, the sim's own or a scripted feed's. A Spawn starts a monster's life
    /// (<see cref="MonsterTrack"/>) as its role's cast entry: the model, its scale and its gem
    /// colour; EnemyStep, the enemy's Attack (the lunge), Hit and EnemyKnock add to it; a Death or a
    /// recycled re-entry ends it, and from that frame the body is gone (the GPU shatter takes over).
    /// Each frame every visible monster's pose at frameSong goes into its type's instance slots and
    /// the crowd draws. Sim ground (x, y) is Unity's (x, 0, y). Without a baked library (a checkout
    /// without the vendor packs) it draws nothing.
    /// </summary>
    public sealed class CrowdView : IPresenter, IDisposable
    {
        /// <summary>The sim's cap on the living (unity-game.md, Enemies): any one type may hold them all.</summary>
        public const int Capacity = 300;
        // Lives that ended but may still show (a death not yet reached by frameSong) need room too,
        // and a freed slot waits a frame before its next owner (CrowdSlots).
        const int Pool = Capacity * 2;
        const int SlotsPerType = Capacity + Capacity / 4;

        readonly Camera _camera;
        readonly CrowdLibrary _lib;
        readonly BoneTextureCrowd _crowd;
        readonly CrowdSlots _slots;
        readonly MonsterClips[] _clips;
        readonly Stack<MonsterTrack> _free = new Stack<MonsterTrack>();
        readonly List<MonsterTrack> _live = new List<MonsterTrack>();
        readonly Dictionary<int, MonsterTrack> _byEnemy = new Dictionary<int, MonsterTrack>();
        readonly MonsterInstance[] _written = new MonsterInstance[Pool];
        readonly int[] _writtenType = new int[Pool], _writtenSlot = new int[Pool];
        int _serial, _unknownRoles;
        double _heroX, _heroY; // the latest frame's, which a spawn faces

        public CrowdView(Camera camera)
        {
            _camera = camera;
            _lib = CrowdLibrary.Load();
            if (_lib == null)
            {
                Debug.LogWarning("CrowdView: no baked crowd (Assets/Baked/Crowd; run `game unity BakeCrowd`): the horde is not drawn");
                return;
            }
            _crowd = new BoneTextureCrowd(_lib, SlotsPerType);
            _slots = new CrowdSlots(_lib.Types.Length, SlotsPerType);
            _clips = new MonsterClips[_lib.Types.Length];
            for (int i = 0; i < _clips.Length; i++) _clips[i] = MonsterClips.Of(_lib.Types[i]);
            for (int i = 0; i < Pool; i++) _free.Push(new MonsterTrack());
            Stills.Redrawing += Requeue;
        }

        void Requeue()
        {
            if (_camera != null) _crowd.Queue(_camera);
        }

        /// <summary>Monsters drawn on screen, read back from the GPU (a frame or two late).</summary>
        public int OnScreen => _crowd?.OnScreen ?? 0;

        /// <summary>Monsters written last frame.</summary>
        public int Visible => _crowd?.Written ?? 0;

        public void OnEvent(in SimEvent e)
        {
            if (_lib == null) return;
            switch (e.Kind)
            {
                case SimEventKind.Spawn:
                    Spawn(e.AsSpawn);
                    break;
                case SimEventKind.EnemyStep:
                {
                    var s = e.AsEnemyStep;
                    Of(s.Enemy)?.Step(s.FromX, s.FromY, s.ToX, s.ToY, s.MoveStart, s.Footplant);
                    break;
                }
                case SimEventKind.EnemyKnock:
                {
                    var k = e.AsEnemyKnock;
                    Of(k.Enemy)?.Knock(k.FromX, k.FromY, k.ToX, k.ToY, k.Start, k.End);
                    break;
                }
                case SimEventKind.Attack:
                {
                    var a = e.AsAttack;
                    if (a.Attacker >= 0) Of(a.Attacker)?.Lunge(a.WindupStart, a.Impact, a.DirX, a.DirY);
                    break;
                }
                case SimEventKind.Hit:
                {
                    var h = e.AsHit;
                    if (h.Target >= 0) Of(h.Target)?.Hit(h.T);
                    break;
                }
                case SimEventKind.Death:
                {
                    var d = e.AsDeath;
                    if (d.Enemy >= 0) Of(d.Enemy)?.End(d.T);
                    break;
                }
            }
        }

        void Spawn(in SpawnEvent s)
        {
            // A recycled enemy leaves its old place at once; a reused id's previous life has died.
            Of(s.Enemy)?.End(s.T);
            if (s.Role < 0 || s.Role >= _lib.Cast.Length)
            {
                if (_unknownRoles++ == 0) Debug.LogWarning($"CrowdView: a spawn of role {s.Role}, which the cast lacks ({_lib.Cast.Length} roles); not drawn");
                return;
            }
            if (_free.Count == 0)
            {
                Debug.LogWarning($"CrowdView: more than {Pool} lives at once; the spawn of enemy {s.Enemy} is not drawn");
                return;
            }
            var track = _free.Pop();
            track.Begin(s.Enemy, _serial++, s.Role, _clips[_lib.Cast[s.Role].Type], s.T, s.X, s.Y, _heroX, _heroY);
            _live.Add(track);
            _byEnemy[s.Enemy] = track;
        }

        MonsterTrack Of(int enemy) => _byEnemy.TryGetValue(enemy, out var t) ? t : null;

        public void Present(in PresentFrame frame)
        {
            if (_lib == null) return;
            double t = frame.Song;
            frame.State.Hero.PositionAt(t, out _heroX, out _heroY);
            _slots.Begin();
            int n = 0;
            for (int i = _live.Count - 1; i >= 0; i--)
            {
                var track = _live[i];
                if (t >= track.EndTime)
                {
                    if (_byEnemy.TryGetValue(track.Enemy, out var current) && current == track) _byEnemy.Remove(track.Enemy);
                    _live.RemoveAt(i);
                    _free.Push(track);
                    continue;
                }
                if (!track.Visible(t)) continue;
                var look = _lib.Cast[track.Role];
                var pose = track.Sample(t);
                var type = _lib.Types[look.Type];
                _writtenType[n] = look.Type;
                _writtenSlot[n] = _slots.Assign(look.Type, track.Serial);
                _written[n] = new MonsterInstance
                {
                    Position = new Vector3((float)pose.X, type.HoverM * look.Scale, (float)pose.Y),
                    Yaw = pose.Yaw,
                    ClipA = pose.ClipA,
                    TimeA = pose.TimeA,
                    ClipB = pose.ClipB,
                    TimeB = pose.TimeB,
                    Blend = pose.Blend,
                    Scale = look.Scale,
                    HitFlash = pose.HitFlash,
                    Tint = MonsterInstance.Pack(look.Gem),
                    Flags = (uint)CrowdFlags.Alive,
                };
                n++;
            }
            _slots.End();
            for (int type = 0; type < _crowd.TypeCount; type++)
            {
                int count = _slots.Count(type);
                if (count == 0) continue;
                var instances = _crowd.Instances(type, count);
                _slots.FillHidden(type, instances);
                for (int k = 0; k < n; k++)
                    if (_writtenType[k] == type) instances[_writtenSlot[k]] = _written[k];
            }
            if (_camera != null) _crowd.Draw(_camera, (float)t);
        }

        public void Dispose()
        {
            if (_crowd == null) return;
            Stills.Redrawing -= Requeue;
            _crowd.Dispose();
        }
    }
}
