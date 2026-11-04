using System;
using System.Collections.Generic;

namespace Champions.Sim
{
    /// <summary>One act of the Boss Rush (Content/bossrush.json's <c>acts</c>; boss-rush.md, Environments).</summary>
    public sealed class BossRushAct
    {
        public string Id, Name;
        /// <summary>Its environment kit's id (the act kit beads dress its arenas from it).</summary>
        public string Kit;
        /// <summary>Its floor key: an index into the palette's zones, held for the whole act.</summary>
        public int Zone;

        public BossRushAct Clone() => (BossRushAct)MemberwiseClone();
    }

    /// <summary>One of the 25 bosses in its fixed slot (Content/bossrush.json's <c>bosses</c>).</summary>
    public sealed class BossRushBoss
    {
        /// <summary>Its id ("treasureGoblin"): its fight's enemies.json cast once that fight lands.</summary>
        public string Id, Name;
        /// <summary>Its act, 0-based (the file's is 1-based).</summary>
        public int Act;
        /// <summary>The main-game boss that plays it until its fight lands (a cast id with a duel).</summary>
        public string StandIn;
        /// <summary>The cast index that fights in this slot: its own once landed, else its stand-in's.</summary>
        public int Cast;
        /// <summary>Its own fight is in the content (<see cref="Cast"/> is its own).</summary>
        public bool Landed;
        /// <summary>Its health; NaN keeps the fighter's own (enemies.json's).</summary>
        public double Health = double.NaN;
        /// <summary>Its whole XP budget, fixed whatever the fight's length (boss-rush.md, Progression).</summary>
        public double Experience;
        /// <summary>Its arena (arenas.json's id): the stand-in arena until its fight authors its own.</summary>
        public string Arena = StandInArena;

        /// <summary>The arena a slot fights in when bossrush.json names none (arenas.json's stand-in).</summary>
        public const string StandInArena = "standin";

        public BossRushBoss Clone() => (BossRushBoss)MemberwiseClone();
    }

    /// <summary>What a slot whose fight hasn't landed does (<see cref="BossRushRules.SkipUnlanded"/>).</summary>
    public enum BossRushUnlanded : byte { StandIn = 0, Skip = 1 }

    /// <summary>
    /// The Boss Rush's acts, boss order and sequencing numbers (Content/bossrush.json; boss-rush.md, The Run). A slot is
    /// <see cref="BossRushBoss.Landed"/> when its id names an enemies.json cast with a bosses.json duel; until then its
    /// stand-in fights there, or the slot is passed over with <c>"unlanded": "skip"</c>.
    /// </summary>
    public sealed class BossRushRules
    {
        public const string File = "bossrush.json";

        /// <summary>The arena forms over this many beats (the beatless fallback's without a beat) before the boss is called.</summary>
        public int FormBeats = 4;
        /// <summary>The act card between acts shows this long (run clock), unless skipped.</summary>
        public double ActCardS = 3;
        public BossRushUnlanded Unlanded;
        /// <summary>`--boss N`'s scripted reference build: this many levels and upgrade steps per boss before N, the steps
        /// taken along <see cref="ReferenceOrder"/> (each step the next entry that can still rank up).</summary>
        public int ReferenceLevelsPerBoss = 2, ReferenceStepsPerBoss = 3;
        public Card[] ReferenceOrder = new Card[0];
        public BossRushAct[] Acts = new BossRushAct[0];
        public BossRushBoss[] Bosses = new BossRushBoss[0];

        public int Count => Bosses.Length;

        /// <summary>The first boss of act <paramref name="act"/>.</summary>
        public int FirstOf(int act)
        {
            for (int i = 0; i < Bosses.Length; i++) if (Bosses[i].Act == act) return i;
            throw new ArgumentOutOfRangeException(nameof(act));
        }

        /// <summary>Boss <paramref name="index"/> is fought (landed, or its stand-in plays).</summary>
        public bool Plays(int index) => Bosses[index].Landed || Unlanded == BossRushUnlanded.StandIn;

        /// <summary>The first boss played at or after <paramref name="index"/>, or <see cref="Count"/> when none is.</summary>
        public int NextPlayed(int index)
        {
            while (index < Bosses.Length && !Plays(index)) index++;
            return index;
        }

        /// <summary>The last boss played: its kill wins the run.</summary>
        public int LastPlayed
        {
            get
            {
                for (int i = Bosses.Length - 1; i >= 0; i--) if (Plays(i)) return i;
                return -1;
            }
        }

        /// <summary>
        /// `--boss N`'s start (0-based <paramref name="index"/>): the reference build for that point in the run, at the
        /// reference level, from Sword 1. Boss 0 is the run's own start.
        /// </summary>
        public BossRushCheckpoint ReferenceCheckpoint(int index, CombatRules combat)
        {
            if (index < 0 || index >= Bosses.Length) throw new ArgumentOutOfRangeException(nameof(index), $"boss {index + 1} of {Bosses.Length}");
            var build = new WeaponBuild(combat);
            build.Set(WeaponKind.Sword, 1);
            int steps = index * ReferenceStepsPerBoss;
            for (int at = 0, stuck = 0; steps > 0 && ReferenceOrder.Length > 0 && stuck < ReferenceOrder.Length; at = (at + 1) % ReferenceOrder.Length)
            {
                var c = ReferenceOrder[at];
                bool can = c.Kind == CardKind.Weapon ? build.CanUpgrade(c.Weapon) : build.CanUpgrade(c.Support);
                if (!can) { stuck++; continue; }
                if (c.Kind == CardKind.Weapon) build.Upgrade(c.Weapon); else build.Upgrade(c.Support);
                stuck = 0;
                steps--;
            }
            return new BossRushCheckpoint
            {
                Boss = index,
                Build = BossRushCheckpoint.CardsOf(build),
                Level = 1 + index * ReferenceLevelsPerBoss,
                Deaths = new int[Acts.Length],
            };
        }

        public BossRushRules Clone()
        {
            var c = (BossRushRules)MemberwiseClone();
            c.ReferenceOrder = (Card[])ReferenceOrder.Clone();
            c.Acts = Array.ConvertAll(Acts, a => a.Clone());
            c.Bosses = Array.ConvertAll(Bosses, b => b.Clone());
            return c;
        }

        /// <summary>Reads bossrush.json, resolving each slot's fighter against the cast and the duels.</summary>
        public static BossRushRules Read(JsonValue json, string path, EnemyRules enemies, BossRules duels)
        {
            var r = new ContentReader(json, path);
            r.String("note");
            var rules = new BossRushRules
            {
                FormBeats = r.Int("formBeats"),
                ActCardS = r.Number("actCardS"),
            };
            string unlanded = r.String("unlanded");
            rules.Unlanded = unlanded == "standIn" ? BossRushUnlanded.StandIn
                : unlanded == "skip" ? BossRushUnlanded.Skip
                : throw r.Fail($"unlanded: {unlanded} is not standIn or skip");
            if (rules.FormBeats < 1) throw r.Fail("formBeats: below 1");
            if (!(rules.ActCardS >= 0)) throw r.Fail("actCardS: negative");
            var reference = r.Object("reference");
            rules.ReferenceLevelsPerBoss = reference.Int("levelsPerBoss");
            rules.ReferenceStepsPerBoss = reference.Int("stepsPerBoss");
            var order = reference.Strings("order");
            rules.ReferenceOrder = new Card[order.Length];
            for (int i = 0; i < order.Length; i++)
            {
                if (Enum.TryParse(order[i], true, out WeaponKind w) && Enum.IsDefined(typeof(WeaponKind), w)) rules.ReferenceOrder[i] = Card.Of(w, 1, 0);
                else if (Enum.TryParse(order[i], true, out SupportKind s) && Enum.IsDefined(typeof(SupportKind), s)) rules.ReferenceOrder[i] = Card.Of(s, 1, 0);
                else throw ContentReader.Fail(reference.Child("order") + "[" + i + "]", $"{order[i]} is not a weapon or support");
            }
            reference.End();
            rules.Acts = r.Objects("acts", a => new BossRushAct { Id = a.String("id"), Name = a.String("name"), Kit = a.String("kit"), Zone = a.Int("zone") });
            if (rules.Acts.Length == 0) throw r.Fail("acts: none");
            var ids = new HashSet<string>();
            rules.Bosses = r.Objects("bosses", b =>
            {
                var boss = new BossRushBoss
                {
                    Id = b.String("id"),
                    Name = b.String("name"),
                    Act = b.Int("act") - 1,
                    StandIn = b.String("standIn"),
                    Health = b.NumberOrNull("health"),
                    Experience = b.Number("experience"),
                    Arena = b.StringOr("arena", BossRushBoss.StandInArena),
                };
                if (!ids.Add(boss.Id)) throw b.Fail($"id: {boss.Id} twice");
                if (boss.Act < 0 || boss.Act >= rules.Acts.Length) throw ContentReader.Fail(b.Child("act"), $"no act {boss.Act + 1}");
                if (!(boss.Experience >= 0)) throw ContentReader.Fail(b.Child("experience"), "negative");
                if (!double.IsNaN(boss.Health) && !(boss.Health > 0)) throw ContentReader.Fail(b.Child("health"), "not above 0");
                int stand = enemies.IndexOf(boss.StandIn);
                if (stand < 0 || duels.Of(stand) == null) throw ContentReader.Fail(b.Child("standIn"), $"{boss.StandIn} has no duel in {BossRules.File}");
                int own = enemies.IndexOf(boss.Id);
                boss.Landed = own >= 0 && duels.Of(own) != null;
                boss.Cast = boss.Landed ? own : stand;
                return boss;
            });
            if (rules.Bosses.Length == 0) throw r.Fail("bosses: none");
            for (int i = 1; i < rules.Bosses.Length; i++)
                if (rules.Bosses[i].Act < rules.Bosses[i - 1].Act) throw r.Fail($"bosses[{i}]: act {rules.Bosses[i].Act + 1} after act {rules.Bosses[i - 1].Act + 1}");
            for (int a = 0; a < rules.Acts.Length; a++)
                if (Array.FindIndex(rules.Bosses, b => b.Act == a) < 0) throw r.Fail($"acts[{a}]: no boss");
            r.End();
            return rules;
        }
    }
}
