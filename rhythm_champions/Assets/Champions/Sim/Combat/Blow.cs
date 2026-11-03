using System;

namespace Champions.Sim
{
    /// <summary>
    /// The tags a mod applies by (design.md, "Mods"): a blow carries every tag of what dealt it. The
    /// Kill and Attacks tags are mods' (kills, the attacks' periods), never a blow's.
    /// </summary>
    [Flags]
    public enum HitTags : byte
    {
        None = 0,
        /// <summary>Sword Slash, phantom slashes, Blade Rush's slashes, Blink Strike's bursts.</summary>
        Strike = 1,
        /// <summary>Anything with a radius or an arc.</summary>
        Area = 2,
        /// <summary>Every dash's blows.</summary>
        Dash = 4,
        /// <summary>Orbit Blades, Phantom, Blade Rain's swords.</summary>
        Summon = 8,
        /// <summary>Kills (Shatterburst, Lifeblood).</summary>
        Kill = 16,
        /// <summary>The attacks' periods (Tempo).</summary>
        Attacks = 32,
    }

    /// <summary>What dealt a blow: a dash, an attack or its wave, or a mod's effect (and a boss's entrance, killing the mobs on screen).</summary>
    public enum BlowSource : byte
    {
        Dash = 0,
        SwordSlash = 1,
        OrbitBlades = 2,
        BladeRain = 3,
        Crescent = 4,
        Phantom = 5,
        /// <summary>Sword Slash's evolution's wave.</summary>
        Tempest = 6,
        /// <summary>Shatterburst's shards.</summary>
        Shards = 7,
        /// <summary>A boss's entrance shockwave, or the dragon's finale: never the hero's blow, only a <see cref="SimEventKind.Kill"/>'s source.</summary>
        Boss = 8,
        /// <summary>Optional destination support; never echoed or repeated.</summary>
        LandingBlast = 9,
    }

    /// <summary>How a blow is drawn and what it may still set off (<see cref="Blow.Flags"/>).</summary>
    [Flags]
    public enum BlowFlags : byte
    {
        None = 0,
        /// <summary>
        /// An extra strike from an upgrade (Tempo, Twin Strike, a summon, a phantom's or an afterimage's
        /// repeat), drawn white and spectral: the hero's body plays one clip per swing at natural speed
        /// (Decision 9).
        /// </summary>
        Spectral = 1,
        /// <summary>Twin Strike's echo: it echoes no further and takes no Encore.</summary>
        Echo = 2,
        /// <summary>A repeat of a dash's blow (Afterimage, Phantom): repeated no further.</summary>
        Repeat = 4,
        /// <summary>Sword Slash's spin (rank 3: every fourth strike).</summary>
        Spin = 8,
        /// <summary>
        /// One tick of an effect drawn from its own events (a wave's front, an orbiting sword) or not at all (a
        /// kill's shards): no <see cref="SimEventKind.Strike"/> of its own.
        /// </summary>
        Effect = 16,
    }

    /// <summary>Where a blow throws what it reaches (the horde moves it; design.md, Shield Charge).</summary>
    public enum ThrowKind : byte
    {
        None = 0,
        /// <summary>Aside of a path, to the target's side of it.</summary>
        Aside = 1,
        /// <summary>Away from the shape's centre.</summary>
        Away = 2,
        /// <summary>Along a path, the way it runs (a wave's front).</summary>
        Along = 3,
    }

    /// <summary>
    /// A blow of the hero's landing at <see cref="T"/> on everything its <see cref="Shape"/> reaches
    /// (<see cref="Strikes"/>). Damage is in multiples of Sword Slash's base hit (design.md, "The
    /// attacks"), with the on-beat payoff already in. A monster hit to death shatters on the blow's
    /// frame, thrown away from it (design.md, "Shatter"); a structure any blow reaches breaks by its own
    /// count of hits.
    /// </summary>
    public struct Blow
    {
        public HitShape Shape;
        public double T;
        public double Damage;
        public HitTags Tags;
        public bool OnBeat;
        public BlowSource Source;
        public BlowFlags Flags;
        /// <summary>The dash's run index (<see cref="DashRecord.Index"/>); -1 for an attack's.</summary>
        public int Owner;
        /// <summary>The part of the dash's form that dealt it (<see cref="DashForm.Parts"/>), or -1 (a break-through).</summary>
        public int Part;
        public ThrowKind Throw;
        /// <summary>What a thrown target deals to what it lands on (Shield Charge rank 2), base hits; 0 none.</summary>
        public double ThrownDamage;
        /// <summary>A thrown target shatters on landing (Juggernaut).</summary>
        public bool ThrownShatter;
        /// <summary>Mobs and structures it reaches break outright (Skyfall's landing); a boss takes <see cref="Damage"/>.</summary>
        public bool Shatter;
    }
}
