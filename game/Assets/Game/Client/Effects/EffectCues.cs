using System;
using System.Collections.Generic;
using Game.Client.Hero;
using PS.Game.Sim;
using UnityEngine;

namespace Game.Client.Effects
{
    /// <summary>
    /// The effects the sim's events call for, each held until the first frame whose frameSong
    /// reaches its song time (unity-game.md, Weapon and Ability Effects: spawned by sim events at
    /// their song times, never by the client's guesses). Events settle about a lead ahead, so a cue
    /// waits here:
    /// - a hero's attack: a slash, spin or slam at its impact (not its wind-up), with the clip the
    ///   hero swings it with (<see cref="HeroMotion.ClipFor"/>, so the slash can sweep the blade's
    ///   way); a slash or spin sized to the blade's arc (<see cref="SlashArcM"/>, with the reach),
    ///   oversized when heavy, a slam to the reach; the strike's crystal wave goes with it;
    /// - a heavy strike's cleave wave, with its bands' radii and times;
    /// - a death's crystal burst, tinted by its role's gem, with the deaths of its instant counted
    ///   (<see cref="Cue.Together"/>) so they can share their light;
    /// - a hit that doesn't kill: a flash (a killing hit's flash gives way to the death's burst);
    /// - a structure's burst, from its break or <see cref="Burst"/>;
    /// - a boss's slam: a burst where it lands (the signature minotaur's mark), at its impact;
    /// - a weapon's own effects, which its view asks for (<see cref="Spark"/>).
    /// </summary>
    public sealed class EffectCues
    {
        /// <summary>
        /// The blade's arc, the radius a slash and a spin are drawn at for the sword's base reach (m; weapons.json's
        /// 7.037 m): about the trailer's (its crescent's stock 2.8 m, effects.json's scaleRef), so a swing reads as
        /// the sword's cut round the hero rather than as the area it hits (ps-w9p.3; the reach shows in the crystal
        /// wave and the kills). It grows and shrinks with the reach, so a weak free press's swing is visibly smaller
        /// and Area's larger.
        /// </summary>
        public const float SlashArcM = 3.0f, BaseReachM = 7.037f;
        /// <summary>A heavy strike's slash beyond the arc, and a follow-up's.</summary>
        public const float HeavySlash = 1.25f, FollowUpSlash = 0.85f;
        /// <summary>A boss slam's burst: its scale, and its warm tint against the crystal's cold.</summary>
        public const float SlamBurst = 1.5f;
        public static readonly Color SlamTint = new Color(1f, 0.45f, 0.15f);

        public enum Kind { Slash, Spin, Slam, Death, Flash, Burst, Cleave, Spark }

        public struct Cue
        {
            public Kind Kind;
            public double T;
            /// <summary>Where (ground x, y); the hero's attacks take the hero's position when due.</summary>
            public Vector2 At, Dir;
            public float Scale;
            /// <summary>An attack's reach, or a structure burst's height.</summary>
            public float Reach;
            public int Enemy;
            public bool Heavy;
            public Color Tint;
            /// <summary>A death's: the deaths at its instant, which share their light.</summary>
            public int Together;
            // A cleave's wave: its outer radius (Reach is the inner), bands and timing.
            public float Outer;
            public int Bands;
            public double FirstBand, BandSeconds;
            /// <summary>A spark's effect (<see cref="EffectsPresenter.IndexOf"/>).</summary>
            public int Effect;
            /// <summary>A hero's attack's: the clip the hero swings it with.</summary>
            public HeroClip Clip;
        }

        readonly RoleLooks _roles;
        readonly List<Cue> _pending = new List<Cue>(256);
        HeroClip? _lastClip;
        // The free hero's latest glides (start, velocity), ascending: what a slash moves with.
        const int GlideKeep = 16;
        readonly List<(double Start, Vector2 V)> _glides = new List<(double, Vector2)>(GlideKeep);

        public EffectCues(RoleLooks roles) => _roles = roles;

        /// <summary>A sword attack's slash tint by its weapon (alpha 0 untinted): Bloody Tear's crimson; null leaves every slash untinted.</summary>
        public Func<int, Color> SlashTint { get; set; }

        /// <summary>Cues waiting for their frame.</summary>
        public int Waiting => _pending.Count;

        public void OnEvent(in SimEvent e)
        {
            switch (e.Kind)
            {
                case SimEventKind.Attack:
                {
                    var a = e.AsAttack;
                    if (!a.ByHero && a.EnemyKind == EnemyAttackKind.Slam)
                    {
                        Burst(a.Impact, new Vector3((float)a.SlamX, 0.5f, (float)a.SlamY), Vector2.zero, SlamBurst, SlamTint);
                        break;
                    }
                    if (!a.IsSword) break; // the other weapons' looks are their own
                    var kind = a.SwordKind == HeroAttackKind.Slam ? Kind.Slam : a.SwordKind == HeroAttackKind.Spin ? Kind.Spin : Kind.Slash;
                    float size = a.Heavy ? HeavySlash : a.SwordKind == HeroAttackKind.FollowUp ? FollowUpSlash : 1f;
                    // The hero's motion picks the same clip from the same attacks in the same order.
                    var clip = HeroMotion.ClipFor(a.SwordKind, _lastClip);
                    _lastClip = clip;
                    _pending.Add(new Cue
                    {
                        Kind = kind, T = a.Impact, Dir = new Vector2((float)a.DirX, (float)a.DirY),
                        Scale = (float)a.Reach * (kind == Kind.Slam ? 1 : SlashArcM / BaseReachM) * size,
                        Reach = (float)a.Reach, Heavy = a.Heavy || kind == Kind.Slam, Enemy = -1,
                        Tint = SlashTint?.Invoke(a.Weapon) ?? default, Clip = clip,
                    });
                    break;
                }
                case SimEventKind.Cleave:
                {
                    var c = e.AsCleave;
                    _pending.Add(new Cue
                    {
                        Kind = Kind.Cleave, T = c.T, At = new Vector2((float)c.X, (float)c.Y), Reach = (float)c.Inner,
                        Outer = (float)c.Outer, Bands = c.Bands, FirstBand = c.FirstBand, BandSeconds = c.BandSeconds, Enemy = -1,
                    });
                    break;
                }
                case SimEventKind.Hit:
                {
                    var h = e.AsHit;
                    if (h.OnHero || h.Damage <= 0) break;
                    _pending.Add(new Cue
                    {
                        Kind = Kind.Flash, T = e.T, At = new Vector2((float)h.X, (float)h.Y),
                        Dir = new Vector2((float)h.DirX, (float)h.DirY), Enemy = h.Target, Scale = 1f,
                    });
                    break;
                }
                case SimEventKind.Death:
                {
                    var d = e.AsDeath;
                    if (d.Enemy < 0) break; // the hero's fall is the hero's and the HUD's
                    for (int i = _pending.Count - 1; i >= 0; i--)
                        if (_pending[i].Kind == Kind.Flash && _pending[i].Enemy == d.Enemy && _pending[i].T == e.T) _pending.RemoveAt(i);
                    _pending.Add(new Cue
                    {
                        Kind = Kind.Death, T = e.T, At = new Vector2((float)d.X, (float)d.Y),
                        Dir = new Vector2((float)d.DirX, (float)d.DirY), Enemy = d.Enemy,
                        Scale = _roles.Size(d.Role), Tint = _roles.Gem(d.Role),
                    });
                    break;
                }
                case SimEventKind.HeroGlide:
                {
                    // A heard input's glide replaces every glide from its start on (HeroMotion.OnGlide).
                    var g = e.AsHeroGlide;
                    while (_glides.Count > 0 && _glides[_glides.Count - 1].Start >= g.Start - 1e-9) _glides.RemoveAt(_glides.Count - 1);
                    if (_glides.Count == GlideKeep) _glides.RemoveAt(0);
                    _glides.Add((g.Start, new Vector2((float)g.VX, (float)g.VY)));
                    break;
                }
                case SimEventKind.StructureBreak:
                {
                    var b = e.AsStructureBreak;
                    Burst(e.T, new Vector3((float)b.X, 0.5f, (float)b.Y), new Vector2((float)b.DirX, (float)b.DirY), 1f, default);
                    break;
                }
            }
        }

        /// <summary>
        /// A structure burst at song time <paramref name="t"/> (a break's, or a regrowth's eruption),
        /// thrown along <paramref name="blow"/> (zero: up), tinted when <paramref name="tint"/>'s alpha is above 0.
        /// </summary>
        public void Burst(double t, Vector3 position, Vector2 blow, float scale, Color tint)
        {
            _pending.Add(new Cue
            {
                Kind = Kind.Burst, T = t, At = new Vector2(position.x, position.z), Dir = blow, Scale = scale, Tint = tint,
                Reach = position.y, Enemy = -1,
            });
        }

        /// <summary>
        /// Effect <paramref name="effect"/> (<see cref="EffectsPresenter.IndexOf"/>; -1 draws nothing) at
        /// song time <paramref name="t"/>, thrown along <paramref name="dir"/>, tinted <paramref name="tint"/>:
        /// a weapon's cast or hit, which its view asks for.
        /// </summary>
        public void Spark(double t, int effect, Vector3 position, Vector2 dir, float scale, Color tint)
        {
            if (effect < 0) return;
            _pending.Add(new Cue
            {
                Kind = Kind.Spark, T = t, At = new Vector2(position.x, position.z), Dir = dir, Scale = scale, Tint = tint,
                Reach = position.y, Enemy = -1, Effect = effect,
            });
        }

        /// <summary>
        /// The arc <paramref name="clip"/>'s blade cuts round the hero, seen from above: which way it sweeps (+1
        /// clockwise, -1 counter-clockwise) and the middle of its sweep, in degrees clockwise from the hero's facing.
        /// Measured from the right hand's path over the 0.2 s round each clip's impact (all of them cut through the
        /// front right, where the sword arm is): the light combo's first and third cuts (SlashL, Slash3) go right to
        /// left, its second (SlashR) back left to right and the flourish (Spin) turns counter-clockwise
        /// (game/evidence/slashes). The slam's shock has no way round.
        /// </summary>
        public static (int Sweep, float CentreDeg) Arc(HeroClip clip)
        {
            switch (clip)
            {
                case HeroClip.SlashR: return (1, 40);
                case HeroClip.Slash3: return (-1, 60);
                case HeroClip.Spin: return (-1, 15);
                default: return (-1, 45);
            }
        }

        /// <summary>The free hero's velocity at song time <paramref name="t"/> (its glide's; zero on the grid, which glides never).</summary>
        public Vector2 HeroVelocityAt(double t)
        {
            for (int i = _glides.Count - 1; i >= 0; i--)
                if (_glides[i].Start <= t) return _glides[i].V;
            return Vector2.zero;
        }

        /// <summary>Moves the cues due by song time <paramref name="now"/> into <paramref name="due"/>, deaths with their instant's count.</summary>
        public void TakeDue(double now, List<Cue> due)
        {
            due.Clear();
            for (int i = 0; i < _pending.Count;)
            {
                if (_pending[i].T > now) { i++; continue; }
                due.Add(_pending[i]);
                _pending[i] = _pending[_pending.Count - 1];
                _pending.RemoveAt(_pending.Count - 1);
            }
            // An instant's deaths have all settled once it is due, so they are all here.
            for (int i = 0; i < due.Count; i++)
            {
                if (due[i].Kind != Kind.Death || due[i].Together > 0) continue;
                int n = 0;
                for (int j = i; j < due.Count; j++) if (due[j].Kind == Kind.Death && due[j].T == due[i].T) n++;
                for (int j = i; j < due.Count; j++)
                {
                    if (due[j].Kind != Kind.Death || due[j].T != due[i].T) continue;
                    var c = due[j];
                    c.Together = n;
                    due[j] = c;
                }
            }
        }
    }
}
