using System.Collections.Generic;
using PS.Game.Sim;
using UnityEngine;

namespace Game.Client.Effects
{
    /// <summary>
    /// The effects the sim's events call for, each held until the first frame whose frameSong
    /// reaches its song time (unity-game.md, Weapon and Ability Effects: spawned by sim events at
    /// their song times, never by the client's guesses). Events settle about a lead ahead, so a cue
    /// waits here:
    /// - a hero's attack: a slash, spin or slam at its impact (not its wind-up), sized to its reach,
    ///   oversized when heavy; the strike's crystal wave goes with it;
    /// - a heavy strike's cleave wave, with its bands' radii and times;
    /// - a death's crystal burst, tinted by its role's gem, with the deaths of its instant counted
    ///   (<see cref="Cue.Together"/>) so they can share their light;
    /// - a hit that doesn't kill: a flash (a killing hit's flash gives way to the death's burst);
    /// - a structure's burst, from its break or <see cref="Burst"/>.
    /// </summary>
    public sealed class EffectCues
    {
        /// <summary>A heavy strike's slash beyond its 1.3x reach, and a follow-up's.</summary>
        public const float HeavySlash = 1.25f, FollowUpSlash = 0.85f;

        public enum Kind { Slash, Spin, Slam, Death, Flash, Burst, Cleave }

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
        }

        readonly RoleLooks _roles;
        readonly List<Cue> _pending = new List<Cue>(256);

        public EffectCues(RoleLooks roles) => _roles = roles;

        /// <summary>Cues waiting for their frame.</summary>
        public int Waiting => _pending.Count;

        public void OnEvent(in SimEvent e)
        {
            switch (e.Kind)
            {
                case SimEventKind.Attack:
                {
                    var a = e.AsAttack;
                    if (a.Attacker != -1) break;
                    var kind = a.Kind == (int)HeroAttackKind.Slam ? Kind.Slam : a.Kind == (int)HeroAttackKind.Spin ? Kind.Spin : Kind.Slash;
                    float size = a.Heavy ? HeavySlash : a.Kind == (int)HeroAttackKind.FollowUp ? FollowUpSlash : 1f;
                    _pending.Add(new Cue
                    {
                        Kind = kind, T = a.Impact, Dir = new Vector2((float)a.DirX, (float)a.DirY), Scale = (float)a.Reach * size,
                        Reach = (float)a.Reach, Heavy = a.Heavy || kind == Kind.Slam, Enemy = -1,
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
                    if (h.Target < 0 || h.Damage <= 0) break;
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
