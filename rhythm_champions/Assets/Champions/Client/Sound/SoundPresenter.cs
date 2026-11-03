using System;
using Champions.Sim;

namespace Champions.Client.Sound
{
    /// <summary>
    /// The hero's sounds from the sim's events (design.md, Sound; game/'s SoundPresenter, ported by review). Each event is
    /// scheduled as it settles, on the DSP clock, so its sound starts the moment the sim knows it, never waiting for the
    /// beat grid:
    /// - Sword Slash's swing so its whoosh lands on the impact (a spin heavier); a spectral strike (Tempo, Twin Strike, a
    ///   phantom's) a lighter whoosh at its strike; a strike that reached something its hit;
    /// - a dash's whoosh from its start, in heard time (its press's), with the on-beat accent layered under it
    ///   (<see cref="AccentGain"/>); Leap Slam's slam as it lands; an afterimage's or phantom's repeat a faint whoosh;
    /// - each kill's shatter (game/'s crystal shatter: a break and a glass spray, then a tinkle as the shards land), the
    ///   hero's hurt, and its own shatter when it dies;
    /// - the bosses' (design.md, Sound: their telegraphs and hits), from the same bank, pitched down for their size: a low
    ///   thud as each move's telegraph starts, its landing's slam (a boulder's or fireball's heavy hit, the breath's roar of
    ///   air), the entrance's slam and accent, Break's accent, and the death's big shatter.
    /// An event stamped before the previous frame's frontier settled in heard time (a press's re-walk): the mixer is
    /// already past most of those, so they start as soon as they can (<see cref="SoundCue.Asap"/>), never dropped. Every
    /// request goes through the beat's aggregation.
    /// </summary>
    public sealed class SoundPresenter
    {
        /// <summary>The on-beat accent's gain under a dash's whoosh; an off-beat dash's whoosh is this much quieter.</summary>
        public const float AccentGain = 0.6f, OffBeatWhoosh = 0.75f;
        /// <summary>A spectral strike's whoosh: quieter and higher than a swing (lighter, as it is drawn dimmer).</summary>
        public const float SpectralGain = 0.45f, SpectralPitch = 1.25f;
        /// <summary>The shards land this long after a standard body's break (game/'s ShatterSounds).</summary>
        public const float LandSeconds = 0.38f;
        /// <summary>A boss telegraph's thud, and the pitch of the Ogre's and the dragon's sounds (their size).</summary>
        public const float TelegraphGain = 0.35f, OgrePitch = 0.75f, DragonPitch = 0.62f;
        /// <summary>A boss's death shatters as a body this many times a standard one.</summary>
        public const float BossShatterSize = 4f;

        readonly SoundScheduler _scheduler;
        readonly SoundAggregator _aggregator;
        readonly Simulation _sim;
        readonly int _leapSlam;
        readonly Func<float> _follower;
        // The previous frame's frontier: an event stamped before it settled in heard time.
        double _settled = double.NegativeInfinity;

        /// <param name="leapSlam">Leap Slam's dash type (DashRules.IndexOf), whose landing slams; -1 none.</param>
        /// <param name="follower">The music's follower gain (0.32 to 1), or null for none (full level).</param>
        public SoundPresenter(SoundScheduler scheduler, Simulation sim, int leapSlam, float limit, Func<float> follower = null)
        {
            _scheduler = scheduler;
            _aggregator = new SoundAggregator(scheduler);
            _sim = sim;
            _leapSlam = leapSlam;
            Limit = limit;
            _follower = follower;
        }

        /// <summary>The effects' level at the follower's full gain (sounds.json's limit).</summary>
        public float Limit { get; set; }

        public SoundAggregator Aggregator => _aggregator;

        /// <summary>One settled event, in log order.</summary>
        public void Take(in SimEvent e)
        {
            bool asap = e.T < _settled;
            switch (e.Kind)
            {
                case SimEventKind.WeaponActivation:
                    if ((WeaponKind)e.I0 == WeaponKind.Spear || (WeaponKind)e.I0 == WeaponKind.Bow)
                        Request(SoundKind.Whoosh, e.T, 0.45f, (WeaponKind)e.I0 == WeaponKind.Bow ? 1.3f : 1f, 0.4f, asap);
                    if (e.D1 > 1) Request(SoundKind.Accent, e.T, AccentGain * 0.5f, 1.15f, 0.5f, asap);
                    break;
                case SimEventKind.Swing:
                    // Survivor Swing is cancellable body windup notice. MeleeAction owns its actual impact;
                    // scheduling here too doubles the whoosh and leaves phantom sounds after a dash cancels it.
                    if (_sim.Melee != null) break;
                    Request(e.I0 != 0 ? SoundKind.SwingHeavy : SoundKind.Swing, e.D0, 1f, 1f, 1f, e.D0 < _settled);
                    break;
                case SimEventKind.ConstructAction:
                    Request(SoundKind.Swing, e.T, SpectralGain * .65f, SpectralPitch, .6f, asap);
                    break;
                case SimEventKind.MagicAction:
                    // One quiet owned cue per discharge/pulse, never one voice per chain segment.
                    Request((WeaponKind)e.I0 == WeaponKind.Lightning ? SoundKind.Hit : SoundKind.Slam,
                        e.T, (e.I2 & 1) != 0 ? .22f : .4f, (WeaponKind)e.I0 == WeaponKind.Lightning ? 1.3f : .75f, .4f, asap);
                    break;
                case SimEventKind.MeleeAction:
                {
                    bool spectral = (e.I2 & 1) != 0;
                    bool hammer = (WeaponKind)e.I0 == WeaponKind.Hammer;
                    Request(hammer ? SoundKind.Slam : SoundKind.Swing, e.T,
                        spectral ? SpectralGain : 1f, hammer ? .8f : spectral ? SpectralPitch : 1f, .6f, asap);
                    break;
                }
                case SimEventKind.Strike:
                {
                    var source = (BlowSource)e.I0;
                    var flags = (BlowFlags)(e.I1 & 0xff);
                    if ((flags & BlowFlags.Spectral) != 0 && (source == BlowSource.SwordSlash || source == BlowSource.Phantom))
                        Request(SoundKind.Swing, e.T, SpectralGain, SpectralPitch, 0.5f, asap);
                    if (e.I2 > 0)
                    {
                        bool heavy = source == BlowSource.BladeRain || source == BlowSource.Tempest || (flags & BlowFlags.Spin) != 0;
                        Request(heavy ? SoundKind.HitHeavy : SoundKind.Hit, e.T, 1f, 1f, Math.Min(1f, e.I2 / 6f), asap);
                    }
                    break;
                }
                case SimEventKind.HeroDash:
                {
                    // A dash starts when its key was heard: in heard time.
                    bool late = e.D0 < _settled;
                    Request(SoundKind.Whoosh, e.D0, e.OnBeat ? 1f : OffBeatWhoosh, 1f, 1f, late);
                    if (e.OnBeat) Request(SoundKind.Accent, e.D0, AccentGain, 1f, 1f, late);
                    if (e.DashType == _leapSlam) Request(SoundKind.Slam, e.D1, e.OnBeat ? 1f : OffBeatWhoosh, 1f, 1f, e.D1 < _settled);
                    break;
                }
                case SimEventKind.LandingBlast:
                    Request(SoundKind.Swing, e.T, 1f, 1f, .6f, asap);
                    Request(SoundKind.HitHeavy, e.T, 0.6f, 1f, 0.5f, asap);
                    break;
                case SimEventKind.DashEcho:
                    Request(SoundKind.Whoosh, e.T, SpectralGain, SpectralPitch, 0.5f, asap);
                    break;
                case SimEventKind.Hit:
                    if ((BlowSource)e.I1 == BlowSource.Spear || (BlowSource)e.I1 == BlowSource.Bow)
                        Request(SoundKind.Hit, e.T, 0.5f, 1.1f, 0.4f, asap);
                    break;
                case SimEventKind.Kill:
                    Shatter(e.T, 1f, (uint)e.I0 * 2654435761u ^ (uint)(e.T * 1000), asap);
                    break;
                case SimEventKind.HeroHit:
                {
                    var outcome = (HitOutcome)e.I0;
                    if (outcome == HitOutcome.Hurt || outcome == HitOutcome.Killed) Request(SoundKind.Hurt, e.T, 1f, 1f, 1f, asap);
                    break;
                }
                case SimEventKind.HeroDeath:
                    Shatter(e.T, 2f, 0x51ED27u ^ (uint)(e.T * 1000), asap);
                    break;
                case SimEventKind.BossArrive:
                    Request(SoundKind.Slam, e.T, 1f, 0.6f, 1f, asap);
                    Request(SoundKind.Accent, e.T, 0.8f, 0.7f, 1f, asap);
                    break;
                case SimEventKind.BossTelegraph:
                    if (e.MarkPart == 0 || e.MarkMove == BossMove.Fireballs)
                        Request(SoundKind.Accent, e.T, TelegraphGain, 0.5f * BossPitch(e.MarkMove) / OgrePitch, 0.5f);
                    break;
                case SimEventKind.BossMove:
                {
                    if (e.I2 != 0) break;
                    var move = e.MarkMove;
                    float pitch = BossPitch(move);
                    if (move == BossMove.Breath) Request(SoundKind.Whoosh, e.T, 1f, pitch * 0.8f, 1f, asap);
                    else if (move == BossMove.Boulders || move == BossMove.Fireballs) Request(SoundKind.HitHeavy, e.T, 1f, pitch, 0.6f, asap);
                    else Request(SoundKind.Slam, e.T, 1f, pitch, 1f, asap);
                    break;
                }
                case SimEventKind.BossDeath:
                    Shatter(e.T, BossShatterSize, 0xB055u ^ (uint)(e.T * 1000), asap);
                    break;
            }
        }

        /// <summary>
        /// game/'s crystal shatter at song time <paramref name="t"/> for a body of <paramref name="size"/> (1 standard): the
        /// break and glass spray at once, the tinkle as the shards land; larger bodies ring lower and land later.
        /// </summary>
        public void Shatter(double t, float size, uint seed, bool asap = false)
        {
            float pitch = Pitch(size) * (1f + 0.08f * (Rand(seed, 1) - 0.5f));
            Request(SoundKind.ShatterBody, t, 1f, pitch, 1f, asap);
            Request(SoundKind.ShatterGlass, t, 1f, pitch, 1f, asap);
            double land = t + LandSeconds * Math.Sqrt(size) * (0.85 + 0.3 * Rand(seed, 2));
            Request(SoundKind.ShatterTinkle, land, 1f, pitch * (1f + 0.1f * (Rand(seed, 3) - 0.5f)), 1f, asap);
        }

        /// <summary>The pitch of a boss's sounds by its move: the Ogre's, or the dragon's lower.</summary>
        static float BossPitch(BossMove move) =>
            move == BossMove.Breath || move == BossMove.Tail || move == BossMove.Dive || move == BossMove.Fireballs ? DragonPitch : OgrePitch;

        /// <summary>A body's pitch (playback rate) by its size: (1 / size)^0.3, within 0.8 to 1.3.</summary>
        public static float Pitch(float size) => Math.Min(1.3f, Math.Max(0.8f, (float)Math.Pow(1f / Math.Max(0.05f, size), 0.3)));

        /// <summary>A sound of <paramref name="kind"/> at song time <paramref name="t"/>, through its beat's aggregation.</summary>
        public void Request(SoundKind kind, double t, float gain, float pitch, float strength, bool asap = false)
        {
            var cue = new SoundCue { Kind = kind, Song = t, Gain = gain, Pitch = pitch, Asap = asap };
            _aggregator.Request(cue, SoundAggregator.Bucket(_sim.Beats.Revealed, t), strength);
        }

        /// <summary>Once a frame, after the frame's events: the frontier they settled at, and the bus.</summary>
        public void Present(double frontier)
        {
            _settled = frontier;
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
