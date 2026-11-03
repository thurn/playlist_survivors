using System;
using System.Globalization;
using Champions.Render;
using Champions.Render.Crowd;
using UnityEngine;

namespace Champions.Client.Hero
{
    /// <summary>
    /// The hero (design.md, The Hero): the knight in the edge look in white, as the neon edge trailer drew it, the crystal
    /// blade in its fist (monsters.json's `hero` figure, drawn by HeroEdge: <see cref="EdgeLook.Hero"/>), posed on the CPU
    /// from its rig by <see cref="HeroMotion"/> and drawn by a <see cref="MonsterFigure"/>. Each frame <see cref="Present"/>
    /// at the frame's song time, where the sim has the hero's feet: the sword idle's arms over the run's legs, the swings and
    /// the dash's pose laid over them (<see cref="HeroPose"/>'s layers), the upper body twisted to the aim, lifted by a
    /// leap, gone while it blinks; the hurt colour on a hit's flash, the blade and lines flaring on an impact. On its death's
    /// frame it breaks into white shards thrown away from the killing blow (its own <see cref="MonsterShatter"/>, seeded
    /// from the figure's palette), and draws no more. Without the baked hero (a checkout without the vendor files) a capsule
    /// of its size stands in (<see cref="StandIn"/>), placed, turned, lifted and hidden by the same motion on the knight's
    /// clip annotations (<see cref="HeroClips.Knight"/>).
    /// </summary>
    public sealed class HeroPresenter : IHeroCommands, IDisposable
    {
        public const float HeightM = 1.85f, RadiusM = 0.35f;
        /// <summary>How hard the killing blow throws the hero's shards (the shatter's launch distance, as a monster's).</summary>
        public const float DeathForce = 2.5f;

        readonly HeroMotion _motion;
        readonly HeroPose _p = new HeroPose();
        readonly Color _body, _hurt;
        HeroTurn _turn;

        // The edge look's figure, when baked.
        readonly RigPose _pose, _layer, _run;
        readonly MonsterFigure _figure;
        readonly HeroFocus _focus;
        readonly MonsterShatter _shatter;
        readonly Vector4[] _rows;
        readonly int _bladeEntry;
        // Local metres of the rigid attachment, shared with its mesh builder.
        public const string BladeNode = "CrystalBlade";
        public const float GripY = -.06f, GuardY = .07f, BladeM = 1.15f;
        bool _broken;

        // The stand-in, when not.
        readonly Material _material;
        readonly GameObject _standIn;

        /// <summary>Logs each swing's clip, timing and speed (`Hero: swing ...`): the scripted scenes' record.</summary>
        public bool LogSwings;

        /// <param name="layer">The stand-in's layer (the camera's).</param>
        /// <param name="look">The hero's drawing (`--look trailer`: exactly the trailer's).</param>
        public HeroPresenter(Palette palette, double speedMps, int layer = 0, MonsterLook look = MonsterLook.Default)
        {
            _body = palette.HeroBody;
            _hurt = palette.HeroHurt;
            var type = MonsterLibrary.LoadFigure(look, MonsterLibrary.HeroId);
            if (type == null)
            {
                Debug.LogWarning("HeroPresenter: no baked hero (champions monster bake hero): a stand-in draws it");
                _motion = new HeroMotion(new HeroClips(HeroClips.Knight()), speedMps);
                _material = StandIn.Material("Hero stand-in", _body, palette.VoidBody, IdPass.Hero);
                _standIn = StandIn.Make("Hero stand-in", PrimitiveType.Capsule, new Vector3(2 * RadiusM, HeightM, 2 * RadiusM), _material, layer);
                _standIn.SetActive(false);
                return;
            }
            var rig = type.Rig;
            _motion = new HeroMotion(new HeroClips(Array.ConvertAll(rig.Clips,
                c => new ClipInfo(c.Role, c.Name, c.Length, c.Impact, c.End, c.Loop, c.StrideM))), speedMps);
            _bladeEntry = Array.FindIndex(rig.Entries, node => rig.Nodes[node] == BladeNode);
            _pose = new RigPose(rig);
            _layer = new RigPose(rig);
            _run = new RigPose(rig);
            _rows = new Vector4[rig.Entries.Length * 3];
            var material = EdgeLook.Hero(type, look, palette);
            if (look != MonsterLook.Trailer)
            {
                material.shader = Resources.Load<Shader>("Crowd/HeroForeground");
                _focus = new HeroFocus();
            }
            _figure = new MonsterFigure(type, material);
            _figure.SetIdCode(IdPass.Hero);
            _shatter = new MonsterShatter(new MonsterLibrary(look, new[] { type }), _ => _figure.Palette, palette);
        }

        /// <summary>Whether the baked figure draws it (else the stand-in).</summary>
        public bool Baked => _figure != null;

        /// <summary>The motion the commands build.</summary>
        public HeroMotion Motion => _motion;

        /// <summary>The pose of the latest <see cref="Present"/>, and the figure's instance then.</summary>
        public HeroPose Pose => _p;
        public MonsterInstance Instance { get; private set; }

        /// <summary>Whether the latest <see cref="Present"/> drew the hero (not blinking, not broken).</summary>
        public bool Drawn { get; private set; }

        /// <summary>The death's shatter (its breaks and live shards), or null for the stand-in.</summary>
        public MonsterShatter Shatter => _shatter;

        /// <summary>The stand-in's root, at the hero's feet, or null for the figure.</summary>
        internal GameObject StandInRoot => _standIn;

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
        /// Poses and draws the hero at song time <paramref name="t"/> with its feet at <paramref name="feet"/> (ground x, y),
        /// <paramref name="dt"/> after the frame before, for <paramref name="camera"/>. On its death's frame it breaks.
        /// </summary>
        public void Present(double t, Vector2 feet, double dt, Camera camera)
        {
            _motion.Evaluate(t, _p);
            _turn.Ease(_p, dt);
            var tint = Color.Lerp(_body, _hurt, _p.Hurt);
            Instance = new MonsterInstance
            {
                Position = new Vector3(feet.x, _p.Lift, feet.y), Yaw = _p.Yaw, Scale = 1, Flare = _p.Flare,
                Tint = MonsterInstance.Pack(tint), Flags = (uint)CrowdFlags.Alive,
            };
            bool dying = t >= _motion.DeathTime;
            Drawn = _p.Visible && !dying;
            _focus?.Present(Instance.Position, camera, Drawn);
            if (_standIn != null)
            {
                _standIn.SetActive(Drawn);
                if (!Drawn) return;
                _standIn.transform.SetPositionAndRotation(Instance.Position, Quaternion.Euler(0, _p.Yaw * Mathf.Rad2Deg, 0));
                StandIn.SetLine(_material, tint * (1 + _p.Flare));
                return;
            }
            if (!_broken)
            {
                Compose();
                _figure.Set(0, Instance, _rows);
                _figure.Draw(camera, 1, Drawn);
                if (dying)
                {
                    // The death's frame: the figure's palette as it fell seeds its shards, thrown away from the blow.
                    _broken = true;
                    var blow = feet - _motion.DeathFrom;
                    _shatter.Break(0, 0, Instance, 0, _motion.DeathTime, blow.x, blow.y, DeathForce);
                }
            }
            _shatter.Frame(t, camera);
        }

        /// <summary>Queues the latest draws again (a second draw of the same frame: the stills' ID pass).</summary>
        public void Queue(Camera camera)
        {
            _focus?.Queue(camera);
            _figure?.Queue(camera);
            _shatter?.Queue(camera);
        }

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
            if (_bladeEntry >= 0 && _p.BladeExtension > 0)
                _pose.Extend(_rows, _bladeEntry, GripY, GuardY + BladeM, _p.BladeExtension);
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

        public void Dispose()
        {
            _shatter?.Dispose();
            _focus?.Dispose();
            _figure?.Dispose();
            StandIn.Destroy(_standIn);
            StandIn.Destroy(_material);
        }
    }
}
