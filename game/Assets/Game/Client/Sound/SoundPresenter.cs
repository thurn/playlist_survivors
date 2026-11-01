using System;
using System.Collections.Generic;
using Game.Client.Effects;
using Game.Client.Loop;
using PS.Game.Sim;

namespace Game.Client.Sound
{
    /// <summary>
    /// The sim's events as sounds (unity-game.md, Sound Timing and Audio). Events settle about a
    /// lead ahead of frameSong, so each is scheduled as it arrives, on the DSP clock:
    /// - **impact sounds** at the impact's song time: the sword's swing (heavy, or the slam), its
    ///   hits on enemies (heavy strikes and cleave bands too), and each death's cry under demo3d's
    ///   crystal shatter (ShatterSounds: an ice break and a glass spray, then a tinkle as the shards
    ///   land, pitched by the body's size); a structure's break shatters the same way, lower, and so
    ///   does its regrowth's eruption, at its start; a free press's swing is as loud as its grade, and a
    ///   full-grade (on the beat) light press's main strike layers the heavy swing under its own (<see cref="OnBeatAccent"/>);
    /// - the other weapons' sounds come from their own views (<see cref="Request"/>), and a weapon that
    ///   sounds its own hits says so (<see cref="VoiceHitsElsewhere"/>);
    /// - **off-grid sounds** snapped as native snaps them: the hero's hurt on the next sixteenth, and
    ///   gem pickups' chimes on the sixteenth grid, climbing a pentatonic scale (<see cref="PentatonicChain"/>);
    /// - **heard-time sounds**: a free press's swing, hits, deaths and breaks, whose events the sim stamps in heard
    ///   time, before the frontier it settles them at (Sword's On the attack key; an event stamped before the previous
    ///   frame's frontier). The mixer is already past most of them, so each starts as soon as it can
    ///   (<see cref="SoundCue.Asap"/>), never dropped.
    /// Every request goes through the aggregation (at most three voices per beat and category). Each
    /// frame the effects' bus follows the plugin's follower gain under the mix's limit.
    /// </summary>
    public sealed class SoundPresenter : IPresenter
    {
        /// <summary>The shards land this long after a standard body's break (ShatterSounds.LandSeconds).</summary>
        public const float LandSeconds = 0.38f;
        /// <summary>A full-grade light press's accent: the heavy swing's whoosh under its own, at this gain.</summary>
        public const float OnBeatAccent = 0.5f;

        readonly SoundScheduler _scheduler;
        readonly SoundAggregator _aggregator;
        readonly Simulation _sim;
        readonly RoleLooks _roles;
        readonly Func<float> _follower;
        readonly PentatonicChain _chain = new PentatonicChain();
        readonly HashSet<int> _voiced = new HashSet<int>();
        // The previous frame's frontier: an event stamped before it settled in heard time.
        double _settled = double.NegativeInfinity;

        /// <param name="follower">The plugin's follower gain (0.32 to 1), or null for none (full level).</param>
        public SoundPresenter(SoundScheduler scheduler, Simulation sim, RoleLooks roles, float limit, Func<float> follower)
        {
            _scheduler = scheduler;
            _aggregator = new SoundAggregator(scheduler);
            _sim = sim;
            _roles = roles;
            Limit = limit;
            _follower = follower;
        }

        /// <summary>The effects' level at the follower's full gain (sounds.json's limit; tunable).</summary>
        public float Limit { get; set; }

        public SoundAggregator Aggregator => _aggregator;

        /// <summary>Weapon <paramref name="weapon"/> (content's index) sounds its own hits: the sword's hit sound leaves them.</summary>
        public void VoiceHitsElsewhere(int weapon) => _voiced.Add(weapon);

        public void OnEvent(in SimEvent e)
        {
            bool asap = e.T < _settled;
            switch (e.Kind)
            {
                case SimEventKind.Attack:
                {
                    var a = e.AsAttack;
                    if (!a.IsSword) break; // the other weapons' sounds are their own
                    var kind = a.SwordKind == HeroAttackKind.Slam ? SoundKind.Slam : a.Heavy ? SoundKind.SwingHeavy : SoundKind.Swing;
                    Request(kind, a.Impact, a.Pressed ? (float)a.Grade : 1f, 1f, a.Heavy ? 1f : 0.6f, asap: asap);
                    if (a.Pressed && a.Grade >= 1 && kind == SoundKind.Swing && a.SwordKind != HeroAttackKind.FollowUp)
                        Request(SoundKind.SwingHeavy, a.Impact, OnBeatAccent, 1f, 0.6f, asap: asap);
                    break;
                }
                case SimEventKind.Hit:
                {
                    var h = e.AsHit;
                    if (h.Damage <= 0) break;
                    if (h.OnHero)
                    {
                        // The hero hurt: on the next sixteenth (native's never-late hurt).
                        Request(SoundKind.Hurt, MusicalGrid.SlotAt(_sim.Stream.Beats, e.T, MusicalGrid.Sixteenth), 1f, 1f, 1f);
                        break;
                    }
                    if (_voiced.Contains(h.Weapon)) break; // a weapon with its own hit sound
                    bool heavy = h.WeaponHit == WeaponHit.HeavyStrike;
                    Request(heavy ? SoundKind.HitHeavy : SoundKind.Hit, e.T, 1f, 1f, heavy ? 1f : 0.6f, asap: asap);
                    break;
                }
                case SimEventKind.Death:
                {
                    var d = e.AsDeath;
                    if (d.Enemy < 0) break; // the hero's fall is the HUD's
                    Request(SoundKind.Death, e.T, 1f, 1f, 1f, asap: asap);
                    Shatter(e.T, _roles.Size(d.Role), (uint)d.Enemy * 2654435761u ^ (uint)(e.T * 1000), asap);
                    break;
                }
                case SimEventKind.StructureBreak:
                {
                    var b = e.AsStructureBreak;
                    var structures = _sim.State.Structures;
                    bool large = b.Structure >= 0 && b.Structure < structures.Length && structures[b.Structure].Large;
                    Shatter(e.T, large ? 4f : 2.5f, (uint)b.Structure * 0x9E3779B1u ^ (uint)(e.T * 1000), asap);
                    break;
                }
                case SimEventKind.Regrowth:
                {
                    // Scheduled as it settles, a lead ahead: the eruption's frame is already past its time (ps-msz.8.5).
                    var r = e.AsRegrowth;
                    var structures = _sim.State.Structures;
                    bool large = r.Structure >= 0 && r.Structure < structures.Length && structures[r.Structure].Large;
                    Shatter(r.Start, large ? 4f : 2.5f, (uint)r.Structure * 0x9E3779B1u ^ (uint)(r.Start * 1000));
                    break;
                }
                case SimEventKind.Pickup:
                {
                    var p = e.AsPickup;
                    if (p.ItemKind != (int)ItemKind.Gem) break;
                    double slot = MusicalGrid.SlotAt(_sim.Stream.Beats, e.T, MusicalGrid.Sixteenth);
                    var cue = new SoundCue { Kind = SoundKind.Chime, Song = slot, Gain = 1f, Note = _chain.Next(slot) };
                    _aggregator.Request(cue, MusicalGrid.Bucket(_sim.Stream.Beats, slot), 1f);
                    break;
                }
            }
        }

        /// <summary>
        /// demo3d's crystal shatter at song time <paramref name="t"/> for a body of
        /// <paramref name="size"/> (1 standard): the ice break and glass spray at once, the tinkle
        /// as the shards land; larger bodies ring lower and land later. A heard-time break's (<paramref name="asap"/>)
        /// start as soon as they can when the mixer is past them.
        /// </summary>
        public void Shatter(double t, float size, uint seed, bool asap = false)
        {
            float pitch = Pitch(size) * (1f + 0.08f * (Rand(seed, 1) - 0.5f));
            Request(SoundKind.ShatterBody, t, 1f, pitch, 1f, asap: asap);
            Request(SoundKind.ShatterGlass, t, 1f, pitch, 1f, asap: asap);
            double land = t + LandSeconds * Math.Sqrt(size) * (0.85 + 0.3 * Rand(seed, 2));
            Request(SoundKind.ShatterTinkle, land, 1f, pitch * (1f + 0.1f * (Rand(seed, 3) - 0.5f)), 1f, asap: asap);
        }

        /// <summary>A body's pitch (playback rate) by its size: (1 / size)^0.3, within 0.8 to 1.3 (ShatterSounds.Pitch).</summary>
        public static float Pitch(float size) => Math.Min(1.3f, Math.Max(0.8f, (float)Math.Pow(1f / Math.Max(0.05f, size), 0.3)));

        /// <summary>
        /// A sound of <paramref name="kind"/> at song time <paramref name="t"/>, through the beat's aggregation;
        /// a <paramref name="note"/> (MIDI, above 0) pitches a category with a root note to it instead. A heard-time
        /// sound (<paramref name="asap"/>) starts as soon as it can when the mixer is past its time (<see cref="SoundCue.Asap"/>).
        /// </summary>
        public void Request(SoundKind kind, double t, float gain, float pitch, float strength, float note = 0, bool asap = false)
        {
            var cue = new SoundCue { Kind = kind, Song = t, Gain = gain, Pitch = pitch, Note = note, Asap = asap };
            _aggregator.Request(cue, MusicalGrid.Bucket(_sim.Stream.Beats, t), strength);
        }

        public void Present(in PresentFrame frame)
        {
            _settled = frame.Frontier;
            _scheduler.Bus = Limit * (_follower?.Invoke() ?? 1f);
            _scheduler.Update();
        }

        static float Rand(uint seed, uint salt)
        {
            uint h = seed * 0x9E3779B1u ^ (salt + 0x7F4A7C15u);
            h ^= h >> 16; h *= 0x85EBCA6Bu; h ^= h >> 13; h *= 0xC2B2AE35u; h ^= h >> 16;
            return (h >> 8) * (1f / 16777216f);
        }
    }
}
