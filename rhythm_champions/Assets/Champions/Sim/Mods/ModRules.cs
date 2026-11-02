using System;

namespace Champions.Sim
{
    /// <summary>
    /// The mods' numbers (Content/mods.json; design.md, "Mods"). Each mod has its tag and its two ranks; a
    /// mod applies only to what carries its tag. The defaults are the design's; mods.json holds the same.
    ///
    /// "Area" grows a shape's radii and reach, a path's width, a wave's range and width, as the design's
    /// "1.5x area" for an on-beat dash is "half again as wide".
    /// </summary>
    public sealed class ModRules
    {
        public const string File = "mods.json";

        public sealed class WideArcsRules
        {
            public HitTags Tag = HitTags.Area;
            public double[] Area = { 1.4, 1.8 };
        }

        public sealed class TwinStrikeRules
        {
            public HitTags Tag = HitTags.Strike;
            /// <summary>The echo's share of the strike, by rank.</summary>
            public double[] Damage = { 0.5, 0.75 };
            public double AfterBeats = 0.5;
        }

        public sealed class TempoRules
        {
            /// <summary>The attacks whose grid periods halve, by rank.</summary>
            public AttackKind[][] Attacks =
            {
                new[] { AttackKind.SwordSlash, AttackKind.Crescent },
                new[] { AttackKind.SwordSlash, AttackKind.OrbitBlades, AttackKind.BladeRain, AttackKind.Crescent, AttackKind.Phantom },
            };
        }

        public sealed class KeenEdgeRules
        {
            public HitTags Tag = HitTags.Strike;
            public double[] Damage = { 1.5, 2.0 };
            /// <summary>A hit on a downbeat deals this many times more.</summary>
            public double[] Downbeat = { 2, 2 };
        }

        public sealed class QuickstepRules
        {
            /// <summary>The dash's cooldown, by rank.</summary>
            public double[] CooldownS = { 0.45, 0.35 };
        }

        public sealed class MomentumRules
        {
            public HitTags Tag = HitTags.Dash;
            /// <summary>The dashes' own damage multiplies by this.</summary>
            public double DashDamage = 2;
            /// <summary>Every dash's trail along its path, hitting once.</summary>
            public double TrailDamage = 0.8;
            public double TrailHalfWidthM = 1.0;
            /// <summary>The trail strikes again on the next beat this many times, by rank.</summary>
            public int[] Repeats = { 0, 1 };
        }

        public sealed class AfterimageRules
        {
            public HitTags Tag = HitTags.Dash;
            /// <summary>The afterimages' delays in beats, by rank.</summary>
            public double[][] Beats = { new[] { 1.0 }, new[] { 1.0, 2.0 } };
        }

        public sealed class ShatterburstRules
        {
            /// <summary>A kill's shards cut the enemies within this of it.</summary>
            public double RadiusM = 1.5;
            /// <summary>The shards' share of the killing hit, by rank.</summary>
            public double[] Share = { 0.3, 0.6 };
        }

        public sealed class LifebloodRules
        {
            public int Kills = 25;
            /// <summary>The share of full health healed every <see cref="Kills"/> kills, by rank.</summary>
            public double[] Heal = { 0.02, 0.04 };
        }

        public sealed class EncoreRules
        {
            public double Damage = 3;
            public double WithinBeats = 1;
            /// <summary>The strikes an on-beat dash boosts, by rank.</summary>
            public int[] Strikes = { 1, 2 };
        }

        public WideArcsRules WideArcs = new WideArcsRules();
        public TwinStrikeRules TwinStrike = new TwinStrikeRules();
        public TempoRules Tempo = new TempoRules();
        public KeenEdgeRules KeenEdge = new KeenEdgeRules();
        public QuickstepRules Quickstep = new QuickstepRules();
        public MomentumRules Momentum = new MomentumRules();
        public AfterimageRules Afterimage = new AfterimageRules();
        public ShatterburstRules Shatterburst = new ShatterburstRules();
        public LifebloodRules Lifeblood = new LifebloodRules();
        public EncoreRules Encore = new EncoreRules();

        /// <summary>Reads mods.json's object.</summary>
        public static ModRules Read(JsonValue json, string path)
        {
            var r = new ContentReader(json, path);
            var m = new ModRules();

            var k = r.Object("wideArcs");
            m.WideArcs.Tag = TagNames.ReadOne(k, "tag");
            m.WideArcs.Area = RankList(k, x => x.Number("area"));
            k.End();

            k = r.Object("twinStrike");
            m.TwinStrike.Tag = TagNames.ReadOne(k, "tag");
            m.TwinStrike.AfterBeats = k.Number("afterBeats");
            m.TwinStrike.Damage = RankList(k, x => x.Number("damage"));
            k.End();

            k = Fixed(r, "tempo", HitTags.Attacks);
            m.Tempo.Attacks = RankList(k, Attacks);
            k.End();

            k = r.Object("keenEdge");
            m.KeenEdge.Tag = TagNames.ReadOne(k, "tag");
            var keen = RankList(k, x => (Damage: x.Number("damage"), Downbeat: x.Number("downbeat")));
            m.KeenEdge.Damage = Array.ConvertAll(keen, x => x.Damage);
            m.KeenEdge.Downbeat = Array.ConvertAll(keen, x => x.Downbeat);
            k.End();

            k = Fixed(r, "quickstep", HitTags.Dash);
            m.Quickstep.CooldownS = RankList(k, x => x.Number("cooldownS"));
            k.End();

            k = r.Object("momentum");
            m.Momentum.Tag = TagNames.ReadOne(k, "tag");
            m.Momentum.DashDamage = k.Number("dashDamage");
            m.Momentum.TrailDamage = k.Number("trailDamage");
            m.Momentum.TrailHalfWidthM = k.Number("trailHalfWidthM");
            m.Momentum.Repeats = RankList(k, x => x.Int("repeats"));
            k.End();

            k = r.Object("afterimage");
            m.Afterimage.Tag = TagNames.ReadOne(k, "tag");
            m.Afterimage.Beats = RankList(k, x => x.Numbers("beats"));
            k.End();

            k = Fixed(r, "shatterburst", HitTags.Kill);
            m.Shatterburst.RadiusM = k.Number("radiusM");
            m.Shatterburst.Share = RankList(k, x => x.Number("share"));
            k.End();

            k = Fixed(r, "lifeblood", HitTags.Kill);
            m.Lifeblood.Kills = k.Int("kills");
            m.Lifeblood.Heal = RankList(k, x => x.Number("heal"));
            k.End();

            k = Fixed(r, "encore", HitTags.Dash);
            m.Encore.Damage = k.Number("damage");
            m.Encore.WithinBeats = k.Number("withinBeats");
            m.Encore.Strikes = RankList(k, x => x.Int("strikes"));
            k.End();

            r.End();
            if (m.Lifeblood.Kills < 1) throw ContentReader.Fail(path + ".lifeblood.kills", "at least 1");
            foreach (var beats in m.Afterimage.Beats)
                if (beats.Length == 0) throw ContentReader.Fail(path + ".afterimage.ranks.beats", "at least one afterimage");
            return m;
        }

        /// <summary>Opens mod <paramref name="key"/>, whose tag is its nature: the content names it, and it must be <paramref name="expected"/>.</summary>
        static ContentReader Fixed(ContentReader parent, string key, HitTags expected)
        {
            var r = parent.Object(key);
            if (TagNames.ReadOne(r, "tag") != expected) throw ContentReader.Fail(r.Child("tag"), $"{expected}: this mod's nature");
            return r;
        }

        /// <summary>The mod's two ranks, each read by <paramref name="rank"/>.</summary>
        static T[] RankList<T>(ContentReader r, Func<ContentReader, T> rank)
        {
            var ranks = r.Objects("ranks", rank);
            if (ranks.Length != Upgrade.ModTop) throw r.Fail($"ranks: {Upgrade.ModTop}");
            return ranks;
        }

        static AttackKind[] Attacks(ContentReader k)
        {
            var ids = k.Strings("attacks");
            var kinds = new AttackKind[ids.Length];
            for (int i = 0; i < ids.Length; i++)
                if (!Upgrade.TryAttack(ids[i], out kinds[i])) throw ContentReader.Fail(k.Child("attacks") + "[" + i + "]", "an attack");
            return kinds;
        }

        /// <summary>A deep copy, so a run's config never shares the content's.</summary>
        public ModRules Clone()
        {
            // The rules are read-only once built; a copy keeps a run's config apart from edits to the content's.
            var copy = new ModRules
            {
                WideArcs = new WideArcsRules { Tag = WideArcs.Tag, Area = (double[])WideArcs.Area.Clone() },
                TwinStrike = new TwinStrikeRules { Tag = TwinStrike.Tag, Damage = (double[])TwinStrike.Damage.Clone(), AfterBeats = TwinStrike.AfterBeats },
                Tempo = new TempoRules { Attacks = Array.ConvertAll(Tempo.Attacks, a => (AttackKind[])a.Clone()) },
                KeenEdge = new KeenEdgeRules { Tag = KeenEdge.Tag, Damage = (double[])KeenEdge.Damage.Clone(), Downbeat = (double[])KeenEdge.Downbeat.Clone() },
                Quickstep = new QuickstepRules { CooldownS = (double[])Quickstep.CooldownS.Clone() },
                Momentum = new MomentumRules
                {
                    Tag = Momentum.Tag, DashDamage = Momentum.DashDamage, TrailDamage = Momentum.TrailDamage,
                    TrailHalfWidthM = Momentum.TrailHalfWidthM, Repeats = (int[])Momentum.Repeats.Clone(),
                },
                Afterimage = new AfterimageRules { Tag = Afterimage.Tag, Beats = Array.ConvertAll(Afterimage.Beats, b => (double[])b.Clone()) },
                Shatterburst = new ShatterburstRules { RadiusM = Shatterburst.RadiusM, Share = (double[])Shatterburst.Share.Clone() },
                Lifeblood = new LifebloodRules { Kills = Lifeblood.Kills, Heal = (double[])Lifeblood.Heal.Clone() },
                Encore = new EncoreRules { Damage = Encore.Damage, WithinBeats = Encore.WithinBeats, Strikes = (int[])Encore.Strikes.Clone() },
            };
            return copy;
        }
    }
}
