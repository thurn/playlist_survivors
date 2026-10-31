using System;
using System.Globalization;
using System.IO;
using Game.Client.Loop;
using PS.Game.Sim;
using UnityEngine;

namespace Game.Client.Hero
{
    /// <summary>
    /// The hero (unity-game.md, The Hero): the Synty knight on its song-time graph (<see cref="HeroRig"/>),
    /// moved and animated by the sim's HeroStep and Attack events (<see cref="HeroMotion"/>), with the
    /// white crystal blade in its hand (<see cref="HeroBlade"/>), flaring on each impact. Extras:
    /// - the low-health pulse: under a third of its health the knight glows red on each beat, more as it nears none;
    /// - the aura's hook: <see cref="AuraAnchor"/> follows the hero's feet (not its turn) for the aura's
    ///   effects to hang on, and <see cref="Aura"/> is its strength, from <see cref="AuraStart"/> growing
    ///   with the build to 1.
    /// Player flag `--hero-out FILE`: each frame's song time, position, lift, yaw, clips and weights, and the flare, as CSV.
    /// </summary>
    public sealed class HeroPresenter : IPresenter, IDisposable
    {
        /// <summary>How quickly the knight turns to the facing it means: a time constant (s).</summary>
        public const double TurnSeconds = 0.04;
        public const double LowHealth = 1 / 3.0, PulseDecay = 0.15;
        public const float AuraStart = 0.2f;
        static readonly Color PulseColor = new Color(1f, 0.08f, 0.06f) * 2.5f;
        static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");
        const int Beats = 8;

        readonly HeroMotion _motion;
        readonly HeroRig _rig;
        readonly HeroBlade _blade;
        readonly Renderer[] _body;
        readonly MaterialPropertyBlock _block = new MaterialPropertyBlock();
        readonly double[] _beats = new double[Beats];
        int _beatCount;
        double _last = double.NaN;
        float _yaw;
        HeroPose _pose;
        bool _pulsing;
        StreamWriter _out;

        /// <summary>The hero as the latest frame drew it: its feet on the ground and the way it travels (the look's camera follows it).</summary>
        public void Drawn(out double x, out double y, out Direction facing)
        {
            x = _pose.X;
            y = _pose.Y;
            facing = _pose.Facing;
        }

        /// <summary>The aura's anchor: the hero's feet, unturned.</summary>
        public Transform AuraAnchor { get; }

        /// <summary>The aura's strength, 0..1: <see cref="AuraStart"/> from the run's start, growing with the build.</summary>
        public float Aura { get; set; } = AuraStart;

        public HeroPresenter(HeroBake bake)
        {
            var root = new GameObject("HeroPresenter").transform;
            _motion = new HeroMotion(HeroClips.Parse(bake.clipsJson));
            _rig = new HeroRig(bake, root);
            _blade = new HeroBlade(bake.blade);
            _blade.Attach(_rig.Animator);
            _body = _rig.Knight.GetComponentsInChildren<SkinnedMeshRenderer>();
            AuraAnchor = new GameObject("Aura").transform;
            AuraAnchor.SetParent(root, false);
            var outPath = Args.Get("--hero-out");
            if (outPath != null)
            {
                _out = new StreamWriter(Args.Path(outPath));
                _out.WriteLine("song,x,y,lift,yaw,run,run_time,attack,a_clip,a_time,b_clip,b_time,a_share,flare");
            }
        }

        public void OnEvent(in SimEvent e)
        {
            if (e.Kind == SimEventKind.Beat) _beats[_beatCount++ % Beats] = e.T;
            else _motion.OnEvent(e);
        }

        public void Present(in PresentFrame frame)
        {
            double t = frame.Song;
            var hero = frame.State.Hero;
            hero.PositionAt(t, out double x, out double y);
            var pose = _pose = _motion.Evaluate(t, x, y, hero.Facing);
            double dt = double.IsNaN(_last) ? double.PositiveInfinity : Math.Max(0, t - _last);
            _last = t;
            float turn = Mathf.DeltaAngle(_yaw * Mathf.Rad2Deg, pose.Yaw * Mathf.Rad2Deg) * Mathf.Deg2Rad;
            _yaw += turn * (float)(1 - Math.Exp(-dt / TurnSeconds));
            _rig.Pose(pose, _yaw);
            _blade.Flare = pose.Flare;
            AuraAnchor.position = new Vector3((float)pose.X, 0, (float)pose.Y);
            Pulse(t, hero);
            _out?.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "{0:R},{1:R},{2:R},{3:R},{4:R},{5:R},{6:R},{7:R},{8},{9:R},{10},{11:R},{12:R},{13:R}",
                t, pose.X, pose.Y, pose.Lift, _yaw, pose.Run, pose.RunTime, pose.Attack,
                pose.A.Clip, pose.A.Time, pose.B.Clip, pose.B.Time, pose.AShare, pose.Flare));
        }

        /// <summary>The low-health pulse: red emission on each beat, its strength rising as health falls under <see cref="LowHealth"/>.</summary>
        void Pulse(double t, in HeroSnapshot hero)
        {
            double ratio = hero.MaxHealth > 0 ? hero.Health / hero.MaxHealth : 1;
            float pulse = 0;
            if (ratio < LowHealth && !hero.Fallen)
            {
                double since = double.PositiveInfinity;
                for (int i = 0; i < Beats && i < _beatCount; i++)
                    if (_beats[i] <= t) since = Math.Min(since, t - _beats[i]);
                pulse = (float)((1 - ratio / LowHealth) * Math.Exp(-since / PulseDecay));
            }
            if (pulse <= 1e-3f && !_pulsing) return;
            _pulsing = pulse > 1e-3f;
            _block.Clear();
            if (_pulsing) _block.SetColor(EmissionId, PulseColor * pulse);
            foreach (var r in _body) r.SetPropertyBlock(_pulsing ? _block : null);
        }

        public void Dispose()
        {
            _out?.Dispose();
            _out = null;
            _blade.Dispose();
            _rig.Dispose();
            if (AuraAnchor != null) UnityEngine.Object.Destroy(AuraAnchor.parent.gameObject);
        }
    }
}
