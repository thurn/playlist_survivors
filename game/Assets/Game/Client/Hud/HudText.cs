using System;
using System.Globalization;
using PS.Game.Sim;
using PS.Game.Sim.Content;
using UnityEngine;

namespace Game.Client.Hud
{
    /// <summary>
    /// The HUD's words, numbers and colours, as native draws them (native/crates/sim/src/survivors/offers.rs'
    /// names and descriptions, hud.rs' item colours, recap.rs' and damage.rs' number formats).
    /// Damage is shown times 10, as native's HUD does; the sim keeps the raw amounts.
    /// </summary>
    public static class HudText
    {
        /// <summary>What the HUD shows of a damage amount: 10 times it.</summary>
        public const double DamageScale = 10;

        /// <summary>A weapon's or passive's name by its content id, in capitals (the sword is native's "sword spin").</summary>
        public static string Name(string id)
        {
            switch (id)
            {
                case "sword": return "SWORD SPIN";
                case null: return "HEAL";
                default: return id.Replace('-', ' ').ToUpperInvariant();
            }
        }

        /// <summary>A card's item id: the weapon's or passive's, null for the heal.</summary>
        public static string Id(GameContent content, in OfferCard card) =>
            card.Item == CardItem.Weapon ? content.Weapons[card.Index].Id
            : card.Item == CardItem.Passive ? content.Passives[card.Index].Id
            : null;

        /// <summary>What a card does, as native's card says it.</summary>
        public static string Description(GameContent content, in OfferCard card)
        {
            switch (card.Item)
            {
                case CardItem.Heal: return "RECOVER " + content.Progression.MaxedHeal.ToString("0", CultureInfo.InvariantCulture) + " HP";
                case CardItem.Passive: return Passive(content.Passives[card.Index].Id);
            }
            if (card.Before == 0) return "NEW ATTACK";
            var def = content.Weapons[card.Index];
            var before = def.Rank(card.Before);
            var after = def.Rank(card.After);
            if (after.Amount > before.Amount)
                return def.Id == "sword" ? "+1 SPIN" : def.Id == "chain-lightning" ? "+1 CHAIN JUMP" : "+1 PROJECTILE";
            if (after.Power > before.Power) return "MORE DAMAGE";
            if (after.Area > before.Area) return "MORE ATTACK AREA";
            if (after.IntervalS < before.IntervalS) return "FASTER ATTACKS";
            if (after.DurationS > before.DurationS) return "LONGER DURATION";
            return "WEAPON UPGRADE";
        }

        static string Passive(string id)
        {
            switch (id)
            {
                case "spinach": return "+10% DAMAGE";
                case "hollow-heart": return "+20% MAX HP, HEALS WHAT IT ADDS";
                case "pummarola": return "+0.2 HP A SECOND";
                case "duplicator": return "+1 PROJECTILE OR SPIN";
                case "empty-tome": return "-8% COOLDOWN";
                case "spellbinder": return "+10% DURATION";
                case "magnet": return "+30% PICKUP REACH";
                case "armour": return "-1 CONTACT DAMAGE";
                default: return "";
            }
        }

        /// <summary>A card's rank line: NEW!, LV 2 > 3, or MAXED for the heal.</summary>
        public static string Rank(in OfferCard card) =>
            card.Item == CardItem.Heal ? "MAXED" : card.Before == 0 ? "NEW!" : $"LV {card.Before} > {card.After}";

        /// <summary>A short mark for an item's slot and card gem: its words' initials (SW, GA, MW, HH...).</summary>
        public static string Mark(string id)
        {
            if (id == null) return "+";
            var parts = id.Split('-');
            return parts.Length > 1 ? $"{char.ToUpperInvariant(parts[0][0])}{char.ToUpperInvariant(parts[1][0])}"
                : id.Substring(0, Math.Min(2, id.Length)).ToUpperInvariant();
        }

        /// <summary>An item's colour (hud.rs' item_color): an evolution shares its base's and its partner passive's.</summary>
        public static Color Colour(string id)
        {
            switch (id)
            {
                case "sword": case "sword-spin": case "bloody-tear": return Rgb(0xff708c);
                case "garlic": case "soul-eater": case "pummarola": return Rgb(0x8cffa9);
                case "magic-wand": case "holy-wand": case "empty-tome": return Rgb(0x68dfff);
                case "king-bible": case "unholy-vespers": case "spellbinder": return Rgb(0xc59bff);
                case "lightning-ring": case "thunder-loop": case "duplicator": return Rgb(0xffdf70);
                case "chain-lightning": return Rgb(0xa8c8ff);
                case "fire-trail": return Rgb(0xff8a3a);
                case "magnet": return Rgb(0x60ffc0);
                case "armour": return Rgb(0xc0c8d8);
                case "spinach": return Rgb(0x6bff79);
                default: return Rgb(0xff8dac);
            }
        }

        public static Color Rgb(uint c) => new Color(((c >> 16) & 0xff) / 255f, ((c >> 8) & 0xff) / 255f, (c & 0xff) / 255f);

        /// <summary>A damage number over an enemy: 10 times the amount, rounded, 12.3K past 10 000 and 1.2M past a million.</summary>
        public static string Damage(double amount)
        {
            double v = Math.Round(amount * DamageScale);
            if (v >= 1_000_000) return (v / 1_000_000).ToString("0.0", CultureInfo.InvariantCulture) + "M";
            if (v >= 10_000) return (v / 1000).ToString("0.0", CultureInfo.InvariantCulture) + "K";
            return v.ToString("0", CultureInfo.InvariantCulture);
        }

        /// <summary>An end card's amount (already scaled): 1.2K, 3.4M, 5.6B, one decimal under 10.</summary>
        public static string Amount(double v)
        {
            var c = CultureInfo.InvariantCulture;
            if (v >= 1e9) return (v / 1e9).ToString("0.0", c) + "B";
            if (v >= 1e6) return (v / 1e6).ToString("0.0", c) + "M";
            if (v >= 1e3) return (v / 1e3).ToString("0.0", c) + "K";
            if (v > 0 && v < 10) return v.ToString("0.0", c);
            return v.ToString("0", c);
        }

        /// <summary>A run clock as minutes and seconds: 12:05.</summary>
        public static string Clock(double seconds)
        {
            long s = (long)Math.Max(0, seconds);
            return $"{s / 60}:{s % 60:00}";
        }

        /// <summary>How long a weapon was held, to tenths: 0:12.3.</summary>
        public static string Held(double seconds)
        {
            long tenths = (long)Math.Round(Math.Max(0, seconds) * 10);
            return $"{tenths / 600}:{tenths / 10 % 60:00}.{tenths % 10}";
        }
    }
}
