using System;

namespace Champions.Sim
{
    /// <summary>
    /// The tags a mod applies by (design.md, "Mods"): a blow carries every tag of what dealt it. The
    /// Kill tag is a kill's, not a blow's.
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
    }

    /// <summary>What dealt a blow.</summary>
    public enum BlowSource : byte
    {
        Dash = 0,
        /// <summary>An attack (the attacks' bead).</summary>
        Attack = 1,
    }

    /// <summary>Where a blow throws what it reaches (the horde moves it; design.md, Shield Charge).</summary>
    public enum ThrowKind : byte
    {
        None = 0,
        /// <summary>Aside of a path, to the target's side of it.</summary>
        Aside = 1,
        /// <summary>Away from the shape's centre.</summary>
        Away = 2,
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
        /// <summary>The dash's run index (<see cref="DashRecord.Index"/>), or the attack's.</summary>
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
