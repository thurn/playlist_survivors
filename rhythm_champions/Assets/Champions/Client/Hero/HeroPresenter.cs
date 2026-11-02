using System;
using System.Globalization;
using Champions.Render;
using Champions.Render.Crowd;
using UnityEngine;

namespace Champions.Client.Hero
{
    /// <summary>
    /// The hero (design.md, The Hero): the knight's edge cut in white (tools/recipes/edgecut/hero.py), posed on the CPU
    /// from its rig by <see cref="HeroMotion"/> and drawn by an <see cref="EdgeFigure"/> with the crowd's shaders. Each
    /// frame <see cref="Present"/> at the frame's song time, where the sim has the hero's feet: the sword idle's arms over
    /// the run's legs, the swings and the dash's pose laid over them (<see cref="HeroPose"/>'s layers), the upper body
    /// twisted to the aim, the lines in the palette's hero white (the hurt colour on a hit's flash, brighter on an
    /// impact). On its death's frame it breaks into white shards (<see cref="EdgeShatter"/>, whose palette function hands
    /// it <see cref="Palette"/> for <see cref="TypeIndex"/>), and draws no more. Without the baked hero (a checkout
    /// without the vendor files) it is inert: <see cref="Ready"/> is false and nothing draws.
    /// </summary>
    public sealed class HeroPresenter : IHeroCommands, IDisposable
    {
        /// <summary>The edge-cut library's id of the hero's type.</summary>
        public const string TypeId = "hero";
        /// <summary>How hard the killing blow throws the hero's shards (the sim's launch distance, as a monster's).</summary>
        public const float DeathForce = 2.5f;

        readonly HeroMotion _motion;
        readonly RigPose _pose, _layer, _run;
        readonly EdgeFigure _figure;
        readonly HeroPose _p = new HeroPose();
        readonly Vector4[] _rows;
        readonly Color _body, _hurt;
        HeroTurn _turn;
        bool _broken;

        /// <summary>The hero's type in the library (for the shatter's palette function), or -1 when not baked.</summary>
        public int TypeIndex { get; } = -1;

        /// <summary>The shatter its death breaks into (the game's, shared with the crowd); none, no shards.</summary>
        public EdgeShatter Shatter;

        /// <summary>Logs each swing's clip, timing and speed (`Hero: swing ...`): the scripted scenes' record.</summary>
        public bool LogSwings;

        public HeroPresenter(EdgeCutLibrary lib, Palette palette, double speedMps)
        {
            _body = palette.HeroBody;
            _hurt = palette.HeroHurt;
            int type = lib != null ? lib.IndexOf(TypeId) : -1;
            var rig = EdgeRig.Of(lib, TypeId);
            if (type < 0 || rig == null)
            {
                Debug.LogWarning("HeroPresenter: no baked hero (run `champions edgecut hero`); the hero is not drawn");
                return;
            }
            TypeIndex = type;
            _motion = new HeroMotion(new HeroClips(rig.Clips), speedMps);
            _pose = new RigPose(rig);
            _layer = new RigPose(rig);
            _run = new RigPose(rig);
            _rows = new Vector4[rig.Entries.Length * 3];
            _figure = new EdgeFigure(lib, type);
            _figure.SetIdCode(IdPass.Hero);
        }

        public bool Ready => _motion != null;

        /// <summary>The motion the commands build, or null when not <see cref="Ready"/>.</summary>
        public HeroMotion Motion => _motion;

        /// <summary>The figure's palette buffer, as the latest <see cref="Present"/> uploaded it.</summary>
        public GraphicsBuffer Palette => _figure?.Palette;

        /// <summary>The pose and figure of the latest <see cref="Present"/>.</summary>
        public HeroPose Pose => _p;
        public MonsterInstance Instance { get; private set; }

        public void Walk(double start, Vector2 velocity) => _motion?.Walk(start, velocity);

        public void Swing(SwingKind kind, double impact, Vector2 aim)
        {
            if (_motion == null) return;
            var s = _motion.Swing(kind, impact, aim);
            if (LogSwings)
                Debug.Log(string.Format(CultureInfo.InvariantCulture,
                    "Hero: swing {0} ({1}): impact {2:F3} s, speed {3:F2}x, clip from {4:F3} s{5}, follow-through to {6:F3} s",
                    s.Name, kind, s.Impact, s.Speed, s.Start, s.EntryClipTime > 1e-4f ? $", entered {s.EntryClipTime:F3} s in" : "", s.End));
        }

        public void Dash(DashPose pose, double start, double end, Vector2 direction, bool evolved) =>
            _motion?.Dash(pose, start, end, direction, evolved);

        public void Hurt(double t) => _motion?.Hurt(t);

        public void Death(double t, Vector2 from) => _motion?.Death(t, from);

        /// <summary>
        /// Poses and draws the hero at song time <paramref name="t"/> with its feet at <paramref name="feet"/> (ground x,
        /// y), <paramref name="dt"/> after the frame before. On its death's frame it breaks: call this before the
        /// shatter's <see cref="EdgeShatter.Frame"/>.
        /// </summary>
        public void Present(double t, Vector2 feet, double dt, Camera camera)
        {
            if (_motion == null || _broken) return;
            _motion.Evaluate(t, _p);
            _turn.Ease(_p, dt);
            Compose();
            var tint = Color.Lerp(_body, _hurt, _p.Hurt);
            Instance = new MonsterInstance
            {
                Position = new Vector3(feet.x, _p.Lift, feet.y), Yaw = _p.Yaw, Scale = 1, Flare = _p.Flare,
                Tint = MonsterInstance.Pack(tint), Flags = (uint)CrowdFlags.Alive,
            };
            _figure.Set(0, Instance, _rows);
            bool dying = t >= _motion.DeathTime;
            _figure.Draw(camera, 1, visible: _p.Visible && !dying);
            if (!dying) return;
            _broken = true;
            var blow = feet - _motion.DeathFrom;
            Shatter?.Break(TypeIndex, 0, Instance, 0, _motion.DeathTime, blow.x, blow.y, DeathForce);
        }

        /// <summary>Queues the latest draw again (a second draw of the same frame: the stills' ID pass).</summary>
        public void Queue(Camera camera) => _figure?.Queue(camera);

        /// <summary>The pose into the palette rows: the sword idle, the run's legs and spine, then each layer, then the twist.</summary>
        void Compose()
        {
            var c = _motion.Clips;
            _pose.Sample(c.Idle.Index, _p.IdleTime);
            if (_p.Run > 0)
            {
                float sum = 0;
                Way(c.Run.Index, _p.ForwardTime, _p.Forward, ref sum);
                Way(c.Back.Index, _p.BackTime, _p.Back, ref sum);
                Way(c.Left.Index, _p.LeftTime, _p.Left, ref sum);
                Way(c.Right.Index, _p.RightTime, _p.Right, ref sum);
                // The arms keep the sword idle's guard while the legs run (the blade held clear, the shield up).
                if (sum > 0) _pose.Blend(_run, _p.Run, _p.Run, 0);
            }
            for (int i = 0; i < _p.LayerCount; i++)
            {
                var l = _p.Layers[i];
                _layer.Sample(l.Clip, l.Time);
                _pose.Blend(_layer, l.Weight * l.Legs, l.Weight, l.Weight);
            }
            _pose.Palette(_rows, _p.Twist);
        }

        /// <summary>Adds one way of the run (a clip at its time and share) into the run's pose.</summary>
        void Way(int clip, float time, float share, ref float sum)
        {
            if (share <= 1e-4f) return;
            if (sum <= 0) _run.Sample(clip, time);
            else
            {
                _layer.Sample(clip, time);
                float w = share / (sum + share);
                _run.Blend(_layer, w, w, w);
            }
            sum += share;
        }

        public void Dispose() => _figure?.Dispose();
    }
}
