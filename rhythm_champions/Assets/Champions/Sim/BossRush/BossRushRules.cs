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
        /// <summary>Its health; NaN keeps the fighter's own (enemies.json's). The progression's table: <see cref="TargetS"/>
        /// × the reference builds' sustained damage at this slot (evidence/boss-rush/progression/).</summary>
        public double Health = double.NaN;
        /// <summary>Its whole XP budget, fixed whatever the fight's length (boss-rush.md, Progression): the Boss Rush curve's
        /// XP for its <see cref="Levels"/>, shed in bursts at each 10% of health lost, drawn by its adds, the rest at the kill.</summary>
        public double Experience;
        /// <summary>Its arena (arenas.json's id): the stand-in arena until its fight authors its own.</summary>
        public string Arena = StandInArena;

        /// <summary>The arena a slot fights in when bossrush.json names none (arenas.json's stand-in).</summary>
        public const string StandInArena = "standin";
        /// <summary>The normal level-ups its budget is tuned to (the curve's; the reference build's levels before a slot).</summary>
        public int Levels;
        /// <summary>The fight length its health is scaled to (s, fight clock).</summary>
        public double TargetS;
        /// <summary>Its bursts' geometry (the file's default unless the slot authors its own).</summary>
        public BossShedShape Shed = new BossShedShape();

        public BossRushBoss Clone()
        {
            var c = (BossRushBoss)MemberwiseClone();
            c.Shed = Shed.Clone();
            return c;
        }
    }

    /// <summary>How a boss's shed gems lie round it (boss-rush.md, Progression > Shedding).</summary>
    public enum BossShedKind : byte
    {
        /// <summary>Evenly round it at <see cref="BossShedShape.RadiusM"/>, turned a little each burst.</summary>
        Ring = 0,
        /// <summary>A fan in front of it (its facing), <see cref="BossShedShape.SpreadDeg"/> wide, out to <see cref="BossShedShape.RadiusM"/>.</summary>
        Spray = 1,
        /// <summary>Flung in a line across its front, <see cref="BossShedShape.LengthM"/> long, <see cref="BossShedShape.RadiusM"/> ahead.</summary>
        Lane = 2,
    }

    /// <summary>One boss's burst geometry: a cluster of <see cref="Gems"/> drops sharing the burst's XP.</summary>
    public sealed class BossShedShape
    {
        public BossShedKind Kind = BossShedKind.Ring;
        public int Gems = 6;
        public double RadiusM = 5, SpreadDeg = 80, LengthM = 16;

        public BossShedShape Clone() => (BossShedShape)MemberwiseClone();

        internal static BossShedShape Read(ContentReader r, BossShedShape fallback)
        {
            string kind = r.StringOr("shape", fallback.Kind.ToString().ToLowerInvariant());
            var shape = new BossShedShape
            {
                Kind = kind == "ring" ? BossShedKind.Ring : kind == "spray" ? BossShedKind.Spray : kind == "lane" ? BossShedKind.Lane
                    : throw r.Fail($"shape: {kind} is not ring, spray or lane"),
                Gems = r.IntOr("gems", fallback.Gems),
                RadiusM = r.NumberOr("radiusM", fallback.RadiusM),
                SpreadDeg = r.NumberOr("spreadDeg", fallback.SpreadDeg),
                LengthM = r.NumberOr("lengthM", fallback.LengthM),
            };
            if (shape.Gems < 1 || shape.Gems > 24) throw r.Fail("gems: 1 to 24");
            if (!(shape.RadiusM > 0 && shape.RadiusM <= 25)) throw r.Fail("radiusM: in (0, 25]");
            if (!(shape.SpreadDeg > 0 && shape.SpreadDeg <= 360)) throw r.Fail("spreadDeg: in (0, 360]");
            if (!(shape.LengthM > 0 && shape.LengthM <= 50)) throw r.Fail("lengthM: in (0, 50]");
            r.End();
            return shape;
        }
    }

    /// <summary>A scripted reference build (Content/bossrush.json's <c>reference.builds</c>): the order its upgrade steps are
    /// taken in. A deep build ranks each entry to its top before the next (focused); otherwise each step is the next entry
    /// round the order that can still rank up (broad).</summary>
    public sealed class BossRushReference
    {
        public Card[] Order = new Card[0];
        public bool Deep;

        public BossRushReference Clone() => new BossRushReference { Order = (Card[])Order.Clone(), Deep = Deep };
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
        /// <summary>The Boss Rush's own XP curve (the main game's progression.json stays untouched): the XP from level 1, 2 …
        /// to the next, then each level <see cref="ExperienceGrowth"/> times the one before (<see cref="Curve"/>).</summary>
        public double[] ExperienceLevels = { 50 };
        public double ExperienceGrowth = 1.05;
        /// <summary>A burst at every this share of the boss's health lost (0.1: at 90%, 80% … 10%), each this share of its budget.</summary>
        public double ShedStep = 0.1;
        /// <summary>The scripted reference builds (boss-rush.md, Progression > Health scaling), by name ("focused", "broad"):
        /// the slots' health is scaled to their damage; <see cref="ReferenceBuild"/> backs `--boss N`.</summary>
        public Dictionary<string, BossRushReference> ReferenceBuilds = new Dictionary<string, BossRushReference>();
        public string ReferenceBuild = "focused";
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

        /// <summary>The XP from level <paramref name="level"/> (1 up) to the next on the Boss Rush's curve.</summary>
        public double Needed(int level)
        {
            if (level <= ExperienceLevels.Length) return ExperienceLevels[level - 1];
            double need = ExperienceLevels[ExperienceLevels.Length - 1];
            for (int l = ExperienceLevels.Length; l < level; l++) need *= ExperienceGrowth;
            return need;
        }

        /// <summary>The XP from level <paramref name="from"/> up <paramref name="levels"/> levels on the curve.</summary>
        public double ExperienceFor(int from, int levels)
        {
            double sum = 0;
            for (int l = from; l < from + levels; l++) sum += Needed(l);
            return sum;
        }

        /// <summary>A rush's progression: <paramref name="main"/>'s pickups, this curve.</summary>
        public ProgressionRules Curve(ProgressionRules main)
        {
            var c = main.Clone();
            c.Levels = (double[])ExperienceLevels.Clone();
            c.Growth = ExperienceGrowth;
            return c;
        }

        /// <summary>The level the run is expected to reach boss <paramref name="index"/> (0-based) at: every slot before it collected.</summary>
        public int LevelAt(int index)
        {
            int level = 1;
            for (int i = 0; i < index; i++) level += Bosses[i].Levels;
            return level;
        }

        /// <summary>The upgrade steps expected before boss <paramref name="index"/>: its earlier slots' level-ups and one reward each.</summary>
        public int StepsAt(int index) => LevelAt(index) - 1 + index;

        /// <summary>
        /// `--boss N`'s start (0-based <paramref name="index"/>): reference build <paramref name="build"/> (null:
        /// <see cref="ReferenceBuild"/>) for that point in the run, at the level the curve expects there, from Sword 1. Steps the
        /// full build has no room for become max-health fallbacks, as level-ups past it do. Boss 0 is the run's own start.
        /// </summary>
        public BossRushCheckpoint ReferenceCheckpoint(int index, CombatRules combat, string build = null)
        {
            if (index < 0 || index >= Bosses.Length) throw new ArgumentOutOfRangeException(nameof(index), $"boss {index + 1} of {Bosses.Length}");
            build = build ?? ReferenceBuild;
            if (!ReferenceBuilds.TryGetValue(build, out var reference)) throw new ArgumentException($"no reference build {build} in {File}", nameof(build));
            var order = reference.Order;
            var cards = new WeaponBuild(combat);
            cards.Set(WeaponKind.Sword, 1);
            int steps = StepsAt(index);
            for (int at = 0, stuck = 0; steps > 0 && order.Length > 0 && stuck < order.Length;)
            {
                var c = order[at];
                bool can = c.Kind == CardKind.Weapon ? cards.CanUpgrade(c.Weapon) : cards.CanUpgrade(c.Support);
                if (!can) { stuck++; at = (at + 1) % order.Length; continue; }
                if (c.Kind == CardKind.Weapon) cards.Upgrade(c.Weapon); else cards.Upgrade(c.Support);
                stuck = 0;
                steps--;
                if (!reference.Deep) at = (at + 1) % order.Length;
            }
            return new BossRushCheckpoint
            {
                Boss = index,
                Build = BossRushCheckpoint.CardsOf(cards),
                Level = LevelAt(index),
                Fallbacks = steps,
                Deaths = new int[Acts.Length],
            };
        }

        public BossRushRules Clone()
        {
            var c = (BossRushRules)MemberwiseClone();
            c.ExperienceLevels = (double[])ExperienceLevels.Clone();
            c.ReferenceBuilds = new Dictionary<string, BossRushReference>();
            foreach (var kv in ReferenceBuilds) c.ReferenceBuilds[kv.Key] = kv.Value.Clone();
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
            var experience = r.Object("experience");
            rules.ExperienceLevels = experience.Numbers("levels");
            rules.ExperienceGrowth = experience.Number("growth");
            rules.ShedStep = experience.Number("shedStep");
            if (rules.ExperienceLevels.Length == 0 || Array.Exists(rules.ExperienceLevels, l => !(l > 0))) throw experience.Fail("levels: at least one, each positive");
            if (!(rules.ExperienceGrowth >= 1)) throw experience.Fail("growth: at least 1");
            if (!(rules.ShedStep > 0 && rules.ShedStep <= 0.5)) throw experience.Fail("shedStep: in (0, 0.5]");
            experience.End();
            var shed = BossShedShape.Read(r.Object("shed"), new BossShedShape());
            var reference = r.Object("reference");
            rules.ReferenceBuild = reference.String("build");
            var builds = reference.Object("builds");
            foreach (string name in new List<string>(builds.Keys()))
            {
                var one = builds.Object(name);
                var order = one.Strings("order");
                var cards = new Card[order.Length];
                for (int i = 0; i < order.Length; i++)
                {
                    if (Enum.TryParse(order[i], true, out WeaponKind w) && Enum.IsDefined(typeof(WeaponKind), w)) cards[i] = Card.Of(w, 1, 0);
                    else if (Enum.TryParse(order[i], true, out SupportKind s) && Enum.IsDefined(typeof(SupportKind), s)) cards[i] = Card.Of(s, 1, 0);
                    else throw ContentReader.Fail(one.Child("order") + "[" + i + "]", $"{order[i]} is not a weapon or support");
                }
                rules.ReferenceBuilds[name] = new BossRushReference { Order = cards, Deep = one.Bool("deep") };
                one.End();
            }
            builds.End();
            if (!rules.ReferenceBuilds.ContainsKey(rules.ReferenceBuild)) throw ContentReader.Fail(reference.Child("build"), $"{rules.ReferenceBuild} is not one of the builds");
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
                    Levels = b.Int("levels"),
                    TargetS = b.Number("targetS"),
                    Shed = b.Has("shed") ? BossShedShape.Read(b.Object("shed"), shed) : shed.Clone(),
                };
                if (boss.Levels < 0) throw ContentReader.Fail(b.Child("levels"), "negative");
                if (!(boss.TargetS > 0)) throw ContentReader.Fail(b.Child("targetS"), "not above 0");
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
