using System;
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
    ///   land, pitched by the body's size); a structure's break shatters the same way, lower;
    /// - **off-grid sounds** snapped as native snaps them: the hero's hurt on the next sixteenth, and
    ///   gem pickups' chimes on the sixteenth grid, climbing a pentatonic scale (<see cref="PentatonicChain"/>).
    /// Every request goes through the aggregation (at most three voices per beat and category). Each
    /// frame the effects' bus follows the plugin's follower gain under the mix's limit.
    /// </summary>
    public sealed class SoundPresenter : IPresenter
    {
        /// <summary>The shards land this long after a standard body's break (ShatterSounds.LandSeconds).</summary>
        public const float LandSeconds = 0.38f;

        readonly SoundScheduler _scheduler;
        readonly SoundAggregator _aggregator;
        readonly Simulation _sim;
        readonly RoleLooks _roles;
        readonly Func<float> _follower;
        readonly PentatonicChain _chain = new PentatonicChain();

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

        public void OnEvent(in SimEvent e)
        {
            switch (e.Kind)
            {
                case SimEventKind.Attack:
                {
                    var a = e.AsAttack;
                    if (!a.IsSword) break; // the other weapons' sounds are their own
                    var kind = a.SwordKind == HeroAttackKind.Slam ? SoundKind.Slam : a.Heavy ? SoundKind.SwingHeavy : SoundKind.Swing;
                    Request(kind, a.Impact, 1f, 1f, a.Heavy ? 1f : 0.6f);
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
                    bool heavy = h.WeaponHit == WeaponHit.HeavyStrike;
                    Request(heavy ? SoundKind.HitHeavy : SoundKind.Hit, e.T, 1f, 1f, heavy ? 1f : 0.6f);
                    break;
                }
                case SimEventKind.Death:
                {
                    var d = e.AsDeath;
                    if (d.Enemy < 0) break; // the hero's fall is the HUD's
                    Request(SoundKind.Death, e.T, 1f, 1f, 1f);
                    Shatter(e.T, _roles.Size(d.Role), (uint)d.Enemy * 2654435761u ^ (uint)(e.T * 1000));
                    break;
                }
                case SimEventKind.StructureBreak:
                {
                    var b = e.AsStructureBreak;
                    var structures = _sim.State.Structures;
                    bool large = b.Structure >= 0 && b.Structure < structures.Length && structures[b.Structure].Large;
                    Shatter(e.T, large ? 4f : 2.5f, (uint)b.Structure * 0x9E3779B1u ^ (uint)(e.T * 1000));
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
        /// as the shards land; larger bodies ring lower and land later.
        /// </summary>
        public void Shatter(double t, float size, uint seed)
        {
            float pitch = Pitch(size) * (1f + 0.08f * (Rand(seed, 1) - 0.5f));
            Request(SoundKind.ShatterBody, t, 1f, pitch, 1f);
            Request(SoundKind.ShatterGlass, t, 1f, pitch, 1f);
            double land = t + LandSeconds * Math.Sqrt(size) * (0.85 + 0.3 * Rand(seed, 2));
            Request(SoundKind.ShatterTinkle, land, 1f, pitch * (1f + 0.1f * (Rand(seed, 3) - 0.5f)), 1f);
        }

        /// <summary>A body's pitch (playback rate) by its size: (1 / size)^0.3, within 0.8 to 1.3 (ShatterSounds.Pitch).</summary>
        public static float Pitch(float size) => Math.Min(1.3f, Math.Max(0.8f, (float)Math.Pow(1f / Math.Max(0.05f, size), 0.3)));

        void Request(SoundKind kind, double t, float gain, float pitch, float strength)
        {
            var cue = new SoundCue { Kind = kind, Song = t, Gain = gain, Pitch = pitch };
            _aggregator.Request(cue, MusicalGrid.Bucket(_sim.Stream.Beats, t), strength);
        }

        public void Present(in PresentFrame frame)
        {
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
