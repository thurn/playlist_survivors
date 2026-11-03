using System;

namespace Champions.Sim
{
    /// <summary>Archived attack IDs, retained for existing replays. New content uses WeaponKind.</summary>
    public enum AttackKind : byte { SwordSlash, OrbitBlades, BladeRain, Crescent, Phantom }

    /// <summary>Archived mod IDs, retained for existing replays. New content uses SupportKind.</summary>
    public enum ModKind : byte { WideArcs, TwinStrike, Tempo, KeenEdge, Quickstep, Momentum, Afterimage, Shatterburst, Lifeblood, Encore }

    /// <summary>What an upgrade raises: an attack or a mod (the dashes are the hero's).</summary>
    public enum UpgradeGroup : byte { Attack = 0, Mod = 1 }

    /// <summary>
    /// An attack or mod at a rank (design.md Terms, "Upgrade" and "Rank"): attacks 1-3 and
    /// <see cref="Evolution"/>, mods 1-2. Written as <c>id:rank</c> with the content's ids
    /// (<c>swordSlash:3</c>, <c>tempo:1</c>), and the evolution as rank 4.
    /// </summary>
    public struct Upgrade : IEquatable<Upgrade>
    {
        public const int AttackTop = 3, Evolution = 4, ModTop = 2;
        public const int AttackCount = 5, ModCount = 10;
        /// <summary>The build's slots (Decision 11): Sword Slash plus two attacks, four mods.</summary>
        public const int AttackSlots = 3, ModSlots = 4;

        static readonly string[] AttackIds = { "swordSlash", "orbitBlades", "bladeRain", "crescent", "phantom" };
        static readonly string[] ModIds =
            { "wideArcs", "twinStrike", "tempo", "keenEdge", "quickstep", "momentum", "afterimage", "shatterburst", "lifeblood", "encore" };

        public UpgradeGroup Group;
        public byte Id;
        public byte Rank;

        public static Upgrade Of(AttackKind kind, int rank) => new Upgrade { Group = UpgradeGroup.Attack, Id = (byte)kind, Rank = (byte)rank };
        public static Upgrade Of(ModKind kind, int rank) => new Upgrade { Group = UpgradeGroup.Mod, Id = (byte)kind, Rank = (byte)rank };

        public AttackKind Attack => (AttackKind)Id;
        public ModKind Mod => (ModKind)Id;

        /// <summary>The content's id of attack <paramref name="kind"/>.</summary>
        public static string IdOf(AttackKind kind) => AttackIds[(int)kind];
        public static string IdOf(ModKind kind) => ModIds[(int)kind];

        /// <summary>The attack the content's id names, or false.</summary>
        public static bool TryAttack(string id, out AttackKind kind)
        {
            int i = Array.IndexOf(AttackIds, id);
            kind = (AttackKind)Math.Max(0, i);
            return i >= 0;
        }

        /// <summary>Is this a rank its group has?</summary>
        public bool Valid =>
            Group == UpgradeGroup.Attack ? Id < AttackCount && Rank >= 1 && Rank <= Evolution
            : Group == UpgradeGroup.Mod && Id < ModCount && Rank >= 1 && Rank <= ModTop;

        /// <summary>Reads <c>id:rank</c>.</summary>
        public static Upgrade Parse(string text)
        {
            int colon = text.IndexOf(':');
            if (colon <= 0 || !int.TryParse(text.Substring(colon + 1), out int rank)) throw new FormatException($"an upgrade is id:rank, not \"{text}\"");
            string id = text.Substring(0, colon);
            int a = Array.IndexOf(AttackIds, id), m = Array.IndexOf(ModIds, id);
            if (a < 0 && m < 0) throw new FormatException($"no attack or mod \"{id}\"");
            var u = a >= 0
                ? new Upgrade { Group = UpgradeGroup.Attack, Id = (byte)a, Rank = (byte)Math.Max(0, Math.Min(255, rank)) }
                : new Upgrade { Group = UpgradeGroup.Mod, Id = (byte)m, Rank = (byte)Math.Max(0, Math.Min(255, rank)) };
            if (!u.Valid || u.Rank != rank) throw new FormatException($"{id} has no rank {rank}");
            return u;
        }

        public override string ToString() => (Group == UpgradeGroup.Attack ? AttackIds[Id] : ModIds[Id]) + ":" + Rank;

        public bool Equals(Upgrade other) => Group == other.Group && Id == other.Id && Rank == other.Rank;
        public override bool Equals(object obj) => obj is Upgrade u && Equals(u);
        public override int GetHashCode() => (int)Group << 16 | Id << 8 | Rank;
    }
}
