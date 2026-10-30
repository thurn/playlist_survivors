using System;
using UnityEngine;

namespace StyleLab.Runtime
{
    /// <summary>
    /// The knight at the centre: a blow on every beat, wound up just before it so the impact
    /// lands on the beat. Slashes alternate toward the nearest monster; every other downbeat is
    /// a spin, and the third bar of four ends with a slam.
    /// </summary>
    public sealed class Hero
    {
        enum Blow { SlashL, SlashR, Spin, Slam }

        const float Windup = 0.17f;
        public readonly Figure Figure;
        public Vector3 Position => Vector3.zero;
        float _yaw = 180f;
        int _armed = -1;
        bool _struck = true;
        Blow _blow;
        float _sinceBlow;
        bool _left;

        public Hero(StyleLabData data, Transform parent)
        {
            Figure = new Figure(data.Type("hero"), parent, 0.5f);
            Play("idle");
        }

        public void Update(float dt, Beat beat, Horde horde, Effects effects)
        {
            _sinceBlow += dt;
            int next = beat.Index + 1;
            if (_armed != next && beat.TimeOfBeat(next) - beat.Time <= Windup)
            {
                _armed = next;
                _struck = false;
                _blow = Choose(beat, next);
                var target = horde.Nearest(Position);
                if (target != null && (_blow == Blow.SlashL || _blow == Blow.SlashR))
                    _yaw = Mathf.Atan2(target.Position.x, target.Position.z) * Mathf.Rad2Deg;
                Play(_blow == Blow.SlashL ? "slashL" : _blow == Blow.SlashR ? "slashR" : _blow == Blow.Spin ? "spin" : "slam");
                if (Figure.Animator != null) Figure.Animator.speed = _blow == Blow.Slam ? 1.3f : 1.6f;
                _sinceBlow = 0;
            }
            if (!_struck && beat.Index >= _armed)
            {
                _struck = true;
                Strike(horde, effects);
            }
            if (_sinceBlow > 0.75f && _struck)
            {
                _sinceBlow = float.NegativeInfinity;
                if (Figure.Animator != null) Figure.Animator.speed = 1f;
                Play("idle");
            }
            Figure.Root.transform.SetPositionAndRotation(Position, Quaternion.Euler(0, _yaw, 0));
            Figure.SetEffects(0, 0, Position);
        }

        Blow Choose(Beat beat, int n)
        {
            int bar = beat.BarOf(n);
            if (beat.IsDownbeat(n) && bar % 2 == 0) return Blow.Spin;
            if (bar % 4 == 3 && beat.IsDownbeat(n + 2)) return Blow.Slam;
            _left = !_left;
            return _left ? Blow.SlashL : Blow.SlashR;
        }

        void Strike(Horde horde, Effects effects)
        {
            switch (_blow)
            {
                case Blow.Spin:
                    foreach (var m in horde.InArc(Position, _yaw, 3.4f, 360f, 64)) horde.Damage(m, Position);
                    effects.Arc(Position + Vector3.up * 0.9f, _yaw, 360f, 3.0f, new Color(3.2f, 1.5f, 0.5f), true);
                    break;
                case Blow.Slam:
                    foreach (var m in horde.InArc(Position, _yaw, 5.2f, 360f, 128)) horde.Damage(m, Position, 2);
                    effects.Shock(Position, 5.5f, new Color(1.2f, 0.9f, 3.5f));
                    break;
                default:
                    foreach (var m in horde.InArc(Position, _yaw, 3.3f, 170f, 5)) horde.Damage(m, Position);
                    effects.Arc(Position + Vector3.up * 1.0f, _yaw, 170f, 2.7f, new Color(3.5f, 1.8f, 0.6f), _blow == Blow.SlashL);
                    break;
            }
        }

        void Play(string state)
        {
            var a = Figure.Animator;
            if (a == null || Array.IndexOf(Figure.Type.states, state) < 0) return;
            a.CrossFadeInFixedTime(state, 0.06f, 0, 0f);
        }
    }
}
