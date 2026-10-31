using System;
using System.Collections.Generic;
using System.IO;

namespace PS.Game.Sim.Content
{
    /// <summary>
    /// The game's content (game/Content/*.json, unity-game.md, Content Data and Units), loaded and
    /// checked: unknown or missing fields and references to missing roles, models, weapons or
    /// passives fail with a <see cref="ContentException"/> naming the file and field. Distances are
    /// metres and times seconds; health, power, experience and knockback are Vampire Survivors'.
    /// Lookups by id are for load time; the sim works on indices.
    /// </summary>
    public sealed class GameContent
    {
        public static readonly string[] Files =
            { "enemies.json", "cast.json", "weapons.json", "passives.json", "offers.json", "progression.json", "schedule.json" };

        public EnemyRole[] Roles;
        public CrystalModel[] Models;
        /// <summary>Cast[i] is Roles[i]'s look.</summary>
        public CastEntry[] Cast;
        /// <summary>In the inventory's tie-break order.</summary>
        public WeaponDef[] Weapons;
        /// <summary>In the inventory's tie-break order.</summary>
        public PassiveDef[] Passives;
        public OfferTable Offers;
        public Progression Progression;
        public Schedule Schedule;

        readonly Dictionary<string, int> roleIndex = new Dictionary<string, int>();
        readonly Dictionary<string, int> weaponIndex = new Dictionary<string, int>();
        readonly Dictionary<string, int> passiveIndex = new Dictionary<string, int>();

        public int RoleIndex(string id) => roleIndex.TryGetValue(id, out int i) ? i : -1;
        public int WeaponIndex(string id) => weaponIndex.TryGetValue(id, out int i) ? i : -1;
        public int PassiveIndex(string id) => passiveIndex.TryGetValue(id, out int i) ? i : -1;
        public EnemyRole Role(string id) => Roles[Find(roleIndex, id, "role")];
        public WeaponDef Weapon(string id) => Weapons[Find(weaponIndex, id, "weapon")];
        public PassiveDef Passive(string id) => Passives[Find(passiveIndex, id, "passive")];

        static int Find(Dictionary<string, int> index, string id, string what) =>
            index.TryGetValue(id, out int i) ? i : throw new KeyNotFoundException("no " + what + " '" + id + "'");

        /// <summary>Loads every file from a directory (game/Content).</summary>
        public static GameContent Load(string directory) => Parse(name => File.ReadAllText(Path.Combine(directory, name)));

        /// <summary>Parses every file, given each file's text by name.</summary>
        public static GameContent Parse(Func<string, string> text)
        {
            var c = new GameContent();
            c.Roles = Read(text, "enemies.json", r => r.Objects("roles", EnemyRole.Read));
            Read(text, "cast.json", r =>
            {
                c.Models = r.Objects("models", CrystalModel.Read);
                c.Cast = r.Objects("cast", CastEntry.Read);
                return 0;
            });
            c.Weapons = Read(text, "weapons.json", r => r.Objects("weapons", WeaponDef.Read));
            c.Passives = Read(text, "passives.json", r => r.Objects("passives", PassiveDef.Read));
            c.Offers = Read(text, "offers.json", OfferTable.Read);
            c.Progression = Read(text, "progression.json", Progression.Read);
            c.Schedule = Read(text, "schedule.json", Schedule.Read);
            c.Check();
            return c;
        }

        static T Read<T>(Func<string, string> text, string file, Func<ContentReader, T> read)
        {
            JsonValue json;
            try { json = JsonValue.Parse(text(file)); }
            catch (FormatException e) { throw new ContentException(file + ": " + e.Message, e); }
            var r = new ContentReader(json, file);
            T result = read(r);
            r.End();
            return result;
        }

        static void Index<T>(Dictionary<string, int> index, T[] items, Func<T, string> id, string path)
        {
            for (int i = 0; i < items.Length; i++)
                if (!index.TryAdd(id(items[i]), i))
                    throw new ContentException(path + "[" + i + "]: duplicate id '" + id(items[i]) + "'");
        }

        static void Ref(bool ok, string path, string what, string id)
        {
            if (!ok) throw new ContentException(path + ": names no " + what + " '" + id + "'");
        }

        /// <summary>The cross-references between files.</summary>
        void Check()
        {
            Index(roleIndex, Roles, r => r.Id, "enemies.json: roles");
            Index(weaponIndex, Weapons, w => w.Id, "weapons.json: weapons");
            Index(passiveIndex, Passives, p => p.Id, "passives.json: passives");

            // The cast: every role once, in the roles' order, each on a real model.
            var models = new Dictionary<string, int>();
            Index(models, Models, m => m.Id, "cast.json: models");
            if (Cast.Length != Roles.Length)
                throw new ContentException("cast.json: cast: " + Cast.Length + " entries for " + Roles.Length + " roles");
            for (int i = 0; i < Cast.Length; i++)
            {
                string path = "cast.json: cast[" + i + "]";
                if (Cast[i].Role != Roles[i].Id)
                    throw new ContentException(path + ".role: expected '" + Roles[i].Id + "' (the roles' order)");
                Ref(models.ContainsKey(Cast[i].Model), path + ".model", "model", Cast[i].Model);
            }

            // Evolutions: from a base weapon at its top rank, with a real partner passive.
            for (int i = 0; i < Weapons.Length; i++)
            {
                WeaponDef w = Weapons[i];
                if (!w.IsEvolution) continue;
                string path = "weapons.json: weapons[" + i + "]";
                Ref(weaponIndex.ContainsKey(w.EvolutionOf), path + ".evolutionOf", "weapon", w.EvolutionOf);
                if (Weapon(w.EvolutionOf).IsEvolution)
                    throw new ContentException(path + ".evolutionOf: '" + w.EvolutionOf + "' is itself an evolution");
                Ref(passiveIndex.ContainsKey(w.Requires), path + ".requires", "passive", w.Requires);
            }

            // Offers name base weapons, passives or "any"; "any" scans every one of them once.
            bool Offerable(string id) => passiveIndex.ContainsKey(id) || (weaponIndex.ContainsKey(id) && !Weapon(id).IsEvolution);
            for (int l = 0; l < Offers.Levels.Length; l++)
                for (int card = 0; card < 3; card++)
                    foreach (string item in Offers.Levels[l][card])
                        Ref(item == OfferTable.Any || Offerable(item),
                            "offers.json: levels[" + l + "].cards[" + card + "]", "base weapon or passive", item);
            var any = new HashSet<string>();
            foreach (string item in Offers.AnyOrder)
            {
                Ref(Offerable(item), "offers.json: anyOrder", "base weapon or passive", item);
                if (!any.Add(item)) throw new ContentException("offers.json: anyOrder: '" + item + "' twice");
            }
            foreach (var w in Weapons)
                if (!w.IsEvolution && !any.Contains(w.Id)) throw new ContentException("offers.json: anyOrder: lacks '" + w.Id + "'");
            foreach (var p in Passives)
                if (!any.Contains(p.Id)) throw new ContentException("offers.json: anyOrder: lacks '" + p.Id + "'");

            foreach (string id in Progression.Chests.EvolutionOrder)
                Ref(Array.Exists(Weapons, w => w.EvolutionOf == id), "progression.json: chests.evolutionOrder",
                    "weapon with an evolution", id);

            foreach (var (role, path) in Schedule.RoleRefs())
                Ref(roleIndex.ContainsKey(role), "schedule.json: " + path, "role", role);
        }
    }
}
