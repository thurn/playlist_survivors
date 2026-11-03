using System;
using System.Collections.Generic;
using Champions.Render;
using Champions.Sim;
using UnityEngine;

namespace Champions.Client.UI
{
    /// <summary>
    /// What the hero's indicator shows (design.md, The Dash > The indicator), from the sim's events, its snapshot and its
    /// beats, at the frame's song time:
    /// - the next two beats' ticks while the beat is reliable, each sliding in from the strip's ends to arrive at the
    ///   centre on its beat (<see cref="TickWindowBeats"/> beats from end to centre); none beatless;
    /// - the dash's pip: the cooldown done since the press (<see cref="SimSnapshot.DashReadyAt"/>), ready when it is over;
    ///   a flash from an on-beat dash's start (<see cref="FlashS"/>) and a blink from a dropped press (<see cref="BlinkS"/>);
    /// - health, stepping when each hit or heal lands, and a hurt's single flash on the bar (<see cref="HurtS"/>; no blinking);
    /// - hidden from the hero's death and once the run has ended.
    /// </summary>
    public sealed class IndicatorModel
    {
        public const double FlashS = 0.25, BlinkS = 0.15, HurtS = 0.3;
        public const double TickWindowBeats = 2;
        /// <summary>A tick fades in over this share of the strip after entering at its end.</summary>
        const float FadeIn = 0.15f;

        readonly List<(double T, double Health)> _steps = new List<(double, double)>();
        double _flashAt = double.NaN, _blinkAt = double.NaN, _hurtAt = double.NaN, _deadAt = double.NaN;
        double _health = double.NaN;

        /// <summary>One settled event, in log order.</summary>
        public void Take(in SimEvent e)
        {
            switch (e.Kind)
            {
                case SimEventKind.HeroDash:
                    if (e.OnBeat) _flashAt = e.D0;
                    break;
                case SimEventKind.DashDropped:
                    _blinkAt = e.D0;
                    break;
                case SimEventKind.HeroHit:
                    var outcome = (HitOutcome)e.I0;
                    if (outcome != HitOutcome.Hurt && outcome != HitOutcome.Killed) break;
                    _hurtAt = e.T;
                    _steps.Add((e.T, e.D1));
                    break;
                case SimEventKind.HeroHeal:
                    _steps.Add((e.T, e.D1));
                    break;
                case SimEventKind.HeroDeath:
                    _deadAt = e.T;
                    break;
            }
        }

        /// <summary>The indicator at song time <paramref name="now"/>.</summary>
        public IndicatorLook Look(double now, in SimSnapshot s, BeatServices beats)
        {
            var look = new IndicatorLook { Shown = s.Phase != RunPhase.Ended && !(now >= _deadAt) };
            Ticks(now, beats, ref look);
            look.Ready = now >= s.DashReadyAt;
            look.Cooldown = look.Ready || s.DashCooldownS <= 0 ? 1 : Mathf.Clamp01((float)(1 - (s.DashReadyAt - now) / s.DashCooldownS));
            look.Flash = Fade(now, _flashAt, FlashS);
            look.Blink = Fade(now, _blinkAt, BlinkS);
            look.Hurt = Fade(now, _hurtAt, HurtS);
            if (double.IsNaN(_health)) _health = s.HeroHealth;
            int applied = 0;
            while (applied < _steps.Count && _steps[applied].T <= now) _health = _steps[applied++].Health;
            _steps.RemoveRange(0, applied);
            look.Health = s.HeroMaxHealth > 0 ? Mathf.Clamp01((float)(_health / s.HeroMaxHealth)) : 1;
            return look;
        }

        /// <summary>1 at <paramref name="at"/>, falling to 0 over <paramref name="span"/>; 0 before or with none.</summary>
        static float Fade(double now, double at, double span)
        {
            double age = now - at;
            return age >= 0 && age < span ? (float)(1 - age / span) : 0;
        }

        static void Ticks(double now, BeatServices beats, ref IndicatorLook look)
        {
            if (beats.StateAt(now) != Reliability.Reliable) return;
            double t1 = beats.NextBeat(now, out int i);
            if (double.IsNaN(t1)) return;
            double t2 = beats.BeatTime(i + 1);
            double period = !double.IsNaN(t2) ? t2 - t1 : t1 - beats.BeatTime(i - 1);
            if (!(period > 0)) return;
            double window = TickWindowBeats * period;
            look.Tick1 = (float)((t1 - now) / window);
            look.Tick1Alpha = Mathf.Clamp01((1 - look.Tick1) / FadeIn);
            if (double.IsNaN(t2)) return;
            look.Tick2 = (float)((t2 - now) / window);
            look.Tick2Alpha = Mathf.Clamp01((1 - look.Tick2) / FadeIn);
        }
    }

    /// <summary>
    /// The indicator in the world (<see cref="HeroIndicator"/>) over the hero's feet, as <see cref="IndicatorModel"/> says.
    /// The assembly hands it every settled event (<see cref="Take"/>) and presents it each frame after the camera moved.
    /// </summary>
    public sealed class IndicatorPresenter : IDisposable
    {
        readonly HeroIndicator _drawn;

        public IndicatorPresenter(Palette palette, int layer = 0)
        {
            _drawn = new HeroIndicator(palette, layer);
        }

        public IndicatorModel Model { get; } = new IndicatorModel();

        public void Take(in SimEvent e) => Model.Take(e);

        /// <summary>The frame at song time <paramref name="now"/>, the hero's feet where the snapshot has them then.</summary>
        public IndicatorLook Present(double now, in SimSnapshot s, BeatServices beats, Camera camera)
        {
            var look = Model.Look(now, s, beats);
            s.HeroAt(now, out double x, out double y);
            _drawn.Present(new Vector3((float)x, 0, (float)y), camera, look);
            return look;
        }

        public void Dispose() => _drawn.Dispose();
    }
}
