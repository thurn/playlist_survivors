using System;
using System.Globalization;
using Champions.Render;
using UnityEngine;

namespace Champions.Client.Hero
{
    /// <summary>
    /// The hero (design.md, The Hero) as a stand-in until the trailer's edge look is ported: a capsule of the hero's size
    /// (<see cref="StandIn"/>) whose silhouette glows in the palette's hero white. Its motion (<see cref="HeroMotion"/>,
    /// on the knight's clips) still plans every swing and dash from the commands, so the stand-in stands where the sim
    /// has the hero's feet, turned to the pose's facing and lifted by a leap, gone while it blinks; it takes the hurt
    /// colour on a hit's flash and brightens on an impact. From its death's frame on it draws no more.
    /// </summary>
    public sealed class HeroPresenter : IHeroCommands, IDisposable
    {
        public const float HeightM = 1.85f, RadiusM = 0.35f;

        readonly HeroMotion _motion;
        readonly HeroPose _p = new HeroPose();
        readonly Material _material;
        readonly GameObject _figure;
        readonly Color _body, _hurt;
        HeroTurn _turn;

        /// <summary>Logs each swing's clip, timing and speed (`Hero: swing ...`): the scripted scenes' record.</summary>
        public bool LogSwings;

        /// <param name="layer">The stand-in's layer (the camera's).</param>
        public HeroPresenter(Palette palette, double speedMps, int layer = 0)
        {
            _body = palette.HeroBody;
            _hurt = palette.HeroHurt;
            _motion = new HeroMotion(new HeroClips(HeroClips.Knight()), speedMps);
            _material = StandIn.Material("Hero stand-in", _body, palette.VoidBody, IdPass.Hero);
            _figure = StandIn.Make("Hero stand-in", PrimitiveType.Capsule, new Vector3(2 * RadiusM, HeightM, 2 * RadiusM), _material, layer);
            _figure.SetActive(false);
        }

        /// <summary>The motion the commands build.</summary>
        public HeroMotion Motion => _motion;

        /// <summary>The pose of the latest <see cref="Present"/>.</summary>
        public HeroPose Pose => _p;

        /// <summary>The stand-in's root, at the hero's feet.</summary>
        internal GameObject Figure => _figure;

        public void Walk(double start, Vector2 velocity) => _motion.Walk(start, velocity);

        public void Swing(SwingKind kind, double impact, Vector2 aim)
        {
            var s = _motion.Swing(kind, impact, aim);
            if (LogSwings)
                Debug.Log(string.Format(CultureInfo.InvariantCulture,
                    "Hero: swing {0} ({1}): impact {2:F3} s, speed {3:F2}x, clip from {4:F3} s{5}, follow-through to {6:F3} s",
                    s.Name, kind, s.Impact, s.Speed, s.Start, s.EntryClipTime > 1e-4f ? $", entered {s.EntryClipTime:F3} s in" : "", s.End));
        }

        public void Dash(DashPose pose, double start, double end, Vector2 direction, bool evolved) =>
            _motion.Dash(pose, start, end, direction, evolved);

        public void Hurt(double t) => _motion.Hurt(t);

        public void Death(double t, Vector2 from) => _motion.Death(t, from);

        /// <summary>
        /// Poses the hero at song time <paramref name="t"/> with its feet at <paramref name="feet"/> (ground x, y),
        /// <paramref name="dt"/> after the frame before.
        /// </summary>
        public void Present(double t, Vector2 feet, double dt)
        {
            _motion.Evaluate(t, _p);
            _turn.Ease(_p, dt);
            _figure.SetActive(_p.Visible);
            if (!_p.Visible) return;
            _figure.transform.SetPositionAndRotation(new Vector3(feet.x, _p.Lift, feet.y), Quaternion.Euler(0, _p.Yaw * Mathf.Rad2Deg, 0));
            StandIn.SetLine(_material, Color.Lerp(_body, _hurt, _p.Hurt) * (1 + _p.Flare));
        }

        public void Dispose()
        {
            StandIn.Destroy(_figure);
            StandIn.Destroy(_material);
        }
    }
}
