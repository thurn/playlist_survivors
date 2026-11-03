using System;
using System.Collections.Generic;
using Champions.Render;
using Champions.Sim;
using UnityEngine;

namespace Champions.Client
{
    /// <summary>
    /// The sim's events as the hero's weapon effects (design.md, Attacks, Dashes, Mods; ps-6y2.16's suggested mapping):
    /// Sword Slash's strikes (its spins whole circles, extra strikes spectral, the backhand's sweep mirrored), the phantoms'
    /// slashes, Blade Rain's swords, Crescent's and Tempest's waves, Orbit Blades' ring, each dash's own effect and its parts
    /// (Leap Slam's shockwave and rings, Shield Charge's bash, Blink Strike's bursts, Blade Rush's cuts, Momentum's trail),
    /// the afterimages, Shatterburst's kills and Encore's charge. On the beat draws bigger and brighter; evolutions are
    /// special moments.
    /// </summary>
    public sealed class EffectCues
    {
        readonly IEffectCommands _fx;
        readonly Simulation _sim;
        readonly DashRules _dashes;
        readonly Func<double, bool> _backhand;
        readonly Dictionary<int, (int Kind, int Rank, double Start, double End)> _dashesByIndex = new Dictionary<int, (int, int, double, double)>();
        readonly int _dash, _bladeRush, _shieldCharge;

        /// <param name="backhand">Whether the swing striking at a time is the backhand (LightCombo01B), its sweep mirrored.</param>
        public EffectCues(IEffectCommands fx, Simulation sim, DashRules dashes, Func<double, bool> backhand = null)
        {
            _fx = fx;
            _sim = sim;
            _dashes = dashes;
            _backhand = backhand;
            _dash = dashes.IndexOf("dash");
            _bladeRush = dashes.IndexOf("bladeRush");
            _shieldCharge = dashes.IndexOf("shieldCharge");
        }

        static Vector2 V(double x, double y) => new Vector2((float)x, (float)y);

        public void Take(in SimEvent e)
        {
            switch (e.Kind)
            {
                case SimEventKind.WeaponActivation:
                    if (e.D1 > 1) _fx.Fire(EffectCue.Encore,
                        new EffectShot { T = e.T, At = V(e.D2, e.D3) });
                    break;
                case SimEventKind.ConstructAction:
                {
                    var cue = (EffectCue)((int)((WeaponKind)e.I0 == WeaponKind.Sentinel ? EffectCue.SentinelSentry : EffectCue.OrbitCircle) + e.I1);
                    _fx.Fire(cue, new EffectShot { T = e.T, At = V(e.D0, e.D1), Facing = V(e.D2, e.D3),
                        To = (WeaponKind)e.I0 == WeaponKind.Sentinel ? V(e.D0 + e.D2 * e.D4 * .6, e.D1 + e.D3 * e.D4 * .6) : V(e.D0, e.D1),
                        Radius = (float)e.D4, Duration = (float)e.D5, Mirrored = (e.I2 & 1) != 0, Phase = e.I2 & 3, Spectral = true,
                        Power = (e.I2 & 4) != 0 ? EffectPower.OnBeat : EffectPower.Plain });
                    break;
                }
                case SimEventKind.MeleeAction:
                {
                    bool hammer = (WeaponKind)e.I0 == WeaponKind.Hammer;
                    var cue = (EffectCue)((int)(hammer ? EffectCue.HammerSwing : EffectCue.MeleeSweep) + e.I1);
                    _fx.Fire(cue, new EffectShot { T = e.T, At = V(e.D0, e.D1), Facing = V(e.D2, e.D3),
                        To = V(e.D0 + e.D2 * e.D5, e.D1 + e.D3 * e.D5),
                        Radius = (float)e.D4, Advance = (float)e.D5, Mirrored = (e.I2 & 2) != 0,
                        Spectral = (e.I2 & 1) != 0 });
                    break;
                }
                case SimEventKind.Strike:
                    Strike(e);
                    break;
                case SimEventKind.Wave:
                {
                    var source = (BlowSource)e.I0;
                    var at = V(e.D0, e.D1);
                    double heading = e.D2, range = e.D4, speed = Math.Max(1e-3, e.D5);
                    var shot = new EffectShot { T = e.T, At = at, Duration = (float)(range / speed), Radius = (float)e.D3 };
                    if (double.IsNaN(heading))
                    {
                        shot.Radius = (float)range;
                        _fx.Fire(EffectCue.FullMoon, shot);
                        break;
                    }
                    var dir = V(Math.Cos(heading), Math.Sin(heading));
                    shot.To = at + dir * (float)range;
                    shot.Facing = dir;
                    _fx.Fire(source == BlowSource.Tempest ? EffectCue.Tempest : EffectCue.Crescent, shot);
                    break;
                }
                case SimEventKind.Orbit:
                    _fx.Orbit(e.T, e.I0, e.D0, e.D1, (float)e.D2, evolved: e.I1 == Upgrade.Evolution);
                    break;
                case SimEventKind.HeroDash:
                    Dash(e);
                    break;
                case SimEventKind.LandingBlast:
                    _fx.Fire(EffectCue.BlinkBurst, new EffectShot { T = e.T, At = V(e.D0, e.D1), Radius = (float)e.D2 });
                    break;
                case SimEventKind.DashPart:
                    Part(e);
                    break;
                case SimEventKind.DashEcho:
                    _fx.Fire(EffectCue.Afterimage, new EffectShot { T = e.T, At = V(e.D0, e.D1), To = V(e.D2, e.D3), Duration = (float)e.D4 });
                    break;
                case SimEventKind.Kill when (BlowSource)e.I1 != BlowSource.Boss && (BlowSource)e.I1 != BlowSource.Shards
                                            && _sim.Arsenal.Rank(ModKind.Shatterburst) > 0:
                    _fx.Fire(EffectCue.Shatterburst, new EffectShot { T = e.T, At = V(e.D3, e.D4) });
                    break;
            }
        }

        void Strike(in SimEvent e)
        {
            var source = (BlowSource)e.I0;
            var flags = (BlowFlags)(e.I1 & 0xFF);
            var shot = new EffectShot
            {
                T = e.T, At = V(e.D0, e.D1), Facing = V(e.D2, e.D3), Radius = (float)e.D4, ArcDeg = (float)(2 * e.D5 * 180 / Math.PI),
            };
            switch (source)
            {
                case BlowSource.SwordSlash:
                    if ((flags & BlowFlags.Spin) != 0) _fx.Fire(EffectCue.SwordSpin, shot);
                    else if ((flags & (BlowFlags.Spectral | BlowFlags.Echo)) != 0) _fx.Fire(EffectCue.SpectralSlash, shot);
                    else
                    {
                        shot.Mirrored = _backhand != null && _backhand(e.T);
                        _fx.Fire(EffectCue.SwordSlash, shot);
                    }
                    break;
                case BlowSource.Phantom:
                    _fx.Fire(_sim.Arsenal.Rank(AttackKind.Phantom) == Upgrade.Evolution ? EffectCue.Legion : EffectCue.Phantom, shot);
                    break;
                case BlowSource.BladeRain:
                    if (_sim.Arsenal.Rank(AttackKind.BladeRain) == Upgrade.Evolution) shot.Power = EffectPower.Special;
                    _fx.Fire(shot.Power == EffectPower.Special ? EffectCue.SwordStorm : EffectCue.BladeRain, shot);
                    break;
            }
        }

        void Dash(in SimEvent e)
        {
            int kind = e.DashType, rank = e.DashRank;
            _dashesByIndex[e.I0] = (kind, rank, e.D0, e.D1);
            if (_dashesByIndex.Count > 64) _dashesByIndex.Remove(e.I0 - 64);
            var form = kind < _dashes.Kinds.Length ? _dashes.Kinds[kind].Form(rank) : null;
            var at = V(e.D2, e.D3);
            var dir = V(e.D4, e.D5);
            var shot = new EffectShot
            {
                T = e.D0, At = at, To = at + dir * (float)(form?.DistanceM ?? 4), Facing = dir, Duration = (float)(e.D1 - e.D0),
                Power = rank == Upgrade.Evolution ? EffectPower.Special : e.OnBeat ? EffectPower.OnBeat : EffectPower.Plain,
            };
            if (kind == _dash) _fx.Fire(EffectCue.Dash, shot);
            else if (kind == _bladeRush) _fx.Fire(rank == Upgrade.Evolution ? EffectCue.ThousandCuts : EffectCue.BladeRush, shot);
            else if (kind == _shieldCharge) _fx.Fire(rank == Upgrade.Evolution ? EffectCue.Juggernaut : EffectCue.ShieldCharge, shot);
            // Leap Slam's and Blink Strike's effects are their parts'.
            if (e.OnBeat && _sim.Arsenal.Rank(ModKind.Encore) > 0) _fx.Fire(EffectCue.Encore, new EffectShot { T = e.D1, At = shot.To });
        }

        void Part(in SimEvent e)
        {
            if (!_dashesByIndex.TryGetValue(e.I0, out var dash)) return;
            var form = dash.Kind < _dashes.Kinds.Length ? _dashes.Kinds[dash.Kind].Form(dash.Rank) : null;
            if (form == null) return;
            // The form's parts, then the mods' extra parts (Momentum's trail and its strike).
            string look = e.I1 < form.Parts.Length ? form.Parts[e.I1].Look : "trail";
            bool onBeat = (e.I2 & 1) != 0;
            var shape = (ShapeKind)(e.I2 >> 1);
            var shot = new EffectShot
            {
                T = e.T, At = V(e.D0, e.D1), To = V(e.D2, e.D3), Facing = V(e.D2, e.D3), Radius = (float)e.D4,
                Power = dash.Rank == Upgrade.Evolution ? EffectPower.Special : onBeat ? EffectPower.OnBeat : EffectPower.Plain,
            };
            if (shape == ShapeKind.Path) shot.Duration = (float)(dash.End - dash.Start);
            switch (look)
            {
                case "shockwave":
                    _fx.Fire(dash.Rank == Upgrade.Evolution ? EffectCue.Skyfall : EffectCue.LeapSlam, shot);
                    break;
                case "bash":
                    _fx.Fire(EffectCue.ShieldBash, shot);
                    break;
                case "burst":
                    _fx.Fire(EffectCue.BlinkBurst, shot);
                    break;
                case "phantomBurst":
                    _fx.Fire(EffectCue.PhaseWalk, shot);
                    break;
                case "afterimageBlades":
                    _fx.Fire(EffectCue.ThousandCuts, shot);
                    break;
                case "cut":
                case "trail":
                    _fx.Fire(EffectCue.MomentumTrail, shot);
                    break;
                default:
                    if (shape == ShapeKind.Ring) _fx.Fire(EffectCue.LeapRing, shot);
                    break;
            }
        }
    }
}
