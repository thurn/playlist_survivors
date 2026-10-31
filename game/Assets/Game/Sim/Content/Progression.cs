using System;
using System.Collections.Generic;

namespace PS.Game.Sim.Content
{
    /// <summary>
    /// Vampire Survivors' stat block, as the hero's base stats, a passive's per-rank change or a cap
    /// (unity-game.md, The Sword: Might multiplies damage, Area size, Cooldown intervals; Amount adds
    /// strikes). MaxHp's change is a fraction compounded per rank (0.2 is +20%); the others add.
    /// </summary>
    public struct Stats
    {
        public double Might, Cooldown, Amount, Area, Speed, Duration, MaxHp, Recovery, Armor, Magnet, Luck, Growth;

        internal static readonly string[] Names =
            { "might", "cooldown", "amount", "area", "speed", "duration", "maxHp", "recovery", "armor", "magnet", "luck", "growth" };

        public double this[int i]
        {
            get
            {
                switch (i)
                {
                    case 0: return Might;
                    case 1: return Cooldown;
                    case 2: return Amount;
                    case 3: return Area;
                    case 4: return Speed;
                    case 5: return Duration;
                    case 6: return MaxHp;
                    case 7: return Recovery;
                    case 8: return Armor;
                    case 9: return Magnet;
                    case 10: return Luck;
                    case 11: return Growth;
                    default: throw new ArgumentOutOfRangeException(nameof(i));
                }
            }
            set
            {
                switch (i)
                {
                    case 0: Might = value; break;
                    case 1: Cooldown = value; break;
                    case 2: Amount = value; break;
                    case 3: Area = value; break;
                    case 4: Speed = value; break;
                    case 5: Duration = value; break;
                    case 6: MaxHp = value; break;
                    case 7: Recovery = value; break;
                    case 8: Armor = value; break;
                    case 9: Magnet = value; break;
                    case 10: Luck = value; break;
                    case 11: Growth = value; break;
                    default: throw new ArgumentOutOfRangeException(nameof(i));
                }
            }
        }

        internal static int Index(ContentReader r, string name)
        {
            int i = Array.IndexOf(Names, name);
            if (i < 0) throw ContentReader.Fail(r.Child(name), "unknown stat");
            return i;
        }

        internal static Stats Filled(double value)
        {
            var s = new Stats();
            for (int i = 0; i < Names.Length; i++) s[i] = value;
            return s;
        }

        /// <summary>Every stat (<paramref name="all"/>), or any of them with the rest 0.</summary>
        internal static Stats Read(ContentReader r, bool all)
        {
            var s = new Stats();
            if (all)
                for (int i = 0; i < Names.Length; i++) s[i] = r.Number(Names[i]);
            else
                foreach (string key in new List<string>(r.Keys())) s[Index(r, key)] = r.Number(key);
            return s;
        }
    }

    /// <summary>A passive (passives.json): its ranks and what each rank adds.</summary>
    public sealed class PassiveDef
    {
        public string Id;
        public int MaxRank;
        public Stats PerRank;

        internal static PassiveDef Read(ContentReader r)
        {
            var p = new PassiveDef { Id = r.String("id"), MaxRank = r.Int("maxRank") };
            var per = r.Object("perRank");
            p.PerRank = Stats.Read(per, false);
            per.End();
            if (p.MaxRank < 1) throw ContentReader.Fail(r.Child("maxRank"), "expected at least one rank");
            return p;
        }
    }

    /// <summary>
    /// The scripted offer table (offers.json, unity-game.md, Pickups, Level-ups and Chests). Each level
    /// reached shows three cards; each card is a preference list whose first entry still offerable
    /// (owned and below its top rank, or not owned with a free slot) is shown, "any" falling back to
    /// <see cref="Fallback"/>'s rule.
    /// </summary>
    public sealed class OfferTable
    {
        public const string Any = "any";
        /// <summary>native's fallback, in order; the only one the game supports.</summary>
        public static readonly string[] Rule = { "lowest-level weapon owned", "lowest-level passive owned", "any offerable" };

        /// <summary>The first level with cards (the hero starts at 1).</summary>
        public const int FirstLevel = 2;
        public const int LastLevel = 70;
        /// <summary>Levels[i] holds level FirstLevel + i's three cards.</summary>
        public string[][][] Levels;
        public string[] Fallback;
        /// <summary>The order "any offerable" scans: every base weapon, then every passive.</summary>
        public string[] AnyOrder;

        /// <summary>A level's three cards; past the table, every card falls back.</summary>
        public string[][] Cards(int level) =>
            level - FirstLevel < Levels.Length && level >= FirstLevel ? Levels[level - FirstLevel] : null;

        internal static OfferTable Read(ContentReader r)
        {
            var t = new OfferTable();
            var levels = r.Objects("levels", lr =>
            {
                int level = lr.Int("level");
                List<JsonValue> cards = lr.Array("cards");
                if (cards.Count != 3) throw ContentReader.Fail(lr.Child("cards"), "expected three cards");
                var row = new string[3][];
                for (int c = 0; c < 3; c++)
                {
                    string path = lr.Child("cards") + "[" + c + "]";
                    if (cards[c].Type != JsonType.Array) throw ContentReader.Fail(path, "expected a preference list");
                    row[c] = ContentReader.Strings(cards[c].Items, path);
                    if (row[c].Length == 0) throw ContentReader.Fail(path, "empty preference list");
                }
                return (level, row);
            });
            t.Levels = new string[levels.Length][][];
            for (int i = 0; i < levels.Length; i++)
            {
                if (levels[i].level != FirstLevel + i)
                    throw ContentReader.Fail(r.Child("levels") + "[" + i + "]", "levels must run from " + FirstLevel + " without gaps");
                t.Levels[i] = levels[i].row;
            }
            if (levels.Length != LastLevel - FirstLevel + 1)
                throw ContentReader.Fail(r.Child("levels"), "levels must run from " + FirstLevel + " to " + LastLevel);
            t.Fallback = r.Strings("fallback");
            if (string.Join("|", t.Fallback) != string.Join("|", Rule))
                throw ContentReader.Fail(r.Child("fallback"), "unsupported fallback");
            t.AnyOrder = r.Strings("anyOrder");
            return t;
        }
    }

    public sealed class HeroDef
    {
        /// <summary>The reference stride's speed: enemies' <see cref="EnemyRole.RefSpeedMps"/> are against it.</summary>
        public double SpeedMps;
        /// <summary>After a hit, the hero takes no damage for this long.</summary>
        public double InvulnerableS;
        public Stats Stats;
        /// <summary>VS's final-stat clamps (unbounded where infinite).</summary>
        public Stats CapMin, CapMax;
    }

    public sealed class ExperienceCurve
    {
        public int[] FactorFrom;
        public double[] Factors;
        /// <summary>Levels at which Growth rises by <see cref="GrowthBonus"/> while the hero is at them.</summary>
        public int[] GrowthLevels;
        public double GrowthBonus;

        /// <summary>The experience needed in all to reach the level after <paramref name="level"/>:
        /// factor(level) x level².</summary>
        public double Total(int level)
        {
            double factor = Factors[0];
            for (int i = 0; i < FactorFrom.Length; i++) if (level >= FactorFrom[i]) factor = Factors[i];
            return factor * level * (double)level;
        }
    }

    public sealed class GemRules
    {
        public int Cap;
        public double PickupRadiusM;
        public double BodyRadiusM;
        /// <summary>A gem's colour: the last whose <see cref="ColourFrom"/> its value reaches.</summary>
        public double[] ColourFrom;
        public string[] Colours;
    }

    public sealed class ChestRules
    {
        /// <summary>Items in a chest of tier 1, 2 and 3.</summary>
        public int[] ItemsByTier;
        /// <summary>The base weapons a chest checks for an evolution, in order.</summary>
        public string[] EvolutionOrder;
    }

    /// <summary>progression.json: slots, the hero, experience, gems and chests.</summary>
    public sealed class Progression
    {
        public int WeaponSlots, PassiveSlots;
        public HeroDef Hero;
        public ExperienceCurve Experience;
        public GemRules Gems;
        public ChestRules Chests;
        /// <summary>The card offered when everything is maxed heals this much.</summary>
        public double MaxedHeal;

        internal static Progression Read(ContentReader r)
        {
            var p = new Progression();
            var slots = r.Object("slots");
            p.WeaponSlots = slots.Int("weapons");
            p.PassiveSlots = slots.Int("passives");
            slots.End();

            var hero = r.Object("hero");
            p.Hero = new HeroDef { SpeedMps = hero.Number("speedMps"), InvulnerableS = hero.Number("invulnerableS") };
            var stats = hero.Object("stats");
            p.Hero.Stats = Stats.Read(stats, true);
            stats.End();
            var caps = hero.Object("caps");
            p.Hero.CapMin = Stats.Filled(double.NegativeInfinity);
            p.Hero.CapMax = Stats.Filled(double.PositiveInfinity);
            foreach (string name in new List<string>(caps.Keys()))
            {
                int i = Stats.Index(caps, name);
                var cap = caps.Object(name);
                foreach (string bound in new List<string>(cap.Keys()))
                {
                    if (bound == "min") p.Hero.CapMin[i] = cap.Number("min");
                    else if (bound == "max") p.Hero.CapMax[i] = cap.Number("max");
                }
                cap.End();
            }
            caps.End();
            hero.End();

            var xp = r.Object("experience");
            var factors = xp.Objects("factors", f => (from: f.Int("fromLevel"), factor: f.Number("factor")));
            if (factors.Length == 0 || factors[0].from != 1)
                throw ContentReader.Fail(xp.Child("factors"), "the first factor must start at level 1");
            p.Experience = new ExperienceCurve
            {
                FactorFrom = Array.ConvertAll(factors, f => f.from),
                Factors = Array.ConvertAll(factors, f => f.factor),
                GrowthLevels = xp.Ints("growthLevels"),
                GrowthBonus = xp.Number("growthBonus"),
            };
            for (int i = 1; i < factors.Length; i++)
                if (factors[i].from <= factors[i - 1].from)
                    throw ContentReader.Fail(xp.Child("factors"), "factors must start at rising levels");
            xp.End();

            var gems = r.Object("gems");
            var colours = gems.Objects("colours", c => (from: c.Number("from"), colour: c.String("colour")));
            p.Gems = new GemRules
            {
                Cap = gems.Int("cap"),
                PickupRadiusM = gems.Number("pickupRadiusM"),
                BodyRadiusM = gems.Number("bodyRadiusM"),
                ColourFrom = Array.ConvertAll(colours, c => c.from),
                Colours = Array.ConvertAll(colours, c => c.colour),
            };
            gems.End();

            var chests = r.Object("chests");
            p.Chests = new ChestRules
            {
                ItemsByTier = chests.Ints("itemsByTier"),
                EvolutionOrder = chests.Strings("evolutionOrder"),
            };
            if (p.Chests.ItemsByTier.Length != 3)
                throw ContentReader.Fail(chests.Child("itemsByTier"), "expected tiers 1, 2 and 3");
            chests.End();

            p.MaxedHeal = r.Number("maxedHeal");
            return p;
        }
    }
}
