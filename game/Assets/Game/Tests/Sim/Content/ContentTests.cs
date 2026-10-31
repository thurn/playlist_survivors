using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using PS.Game.Sim.Content;

namespace PS.Game.Sim.Tests
{
    /// <summary>
    /// The game's content (game/Content) through its loaders: every file loads with its references
    /// resolved, native's numbers arrive in metres, and bad content fails naming the file and field.
    /// </summary>
    public class ContentTests
    {
        /// <summary>game/Content: under Unity the current directory is the project (game/); under .NET
        /// it is found above the test assembly.</summary>
        static string ContentDir()
        {
            foreach (string start in new[] { Directory.GetCurrentDirectory(), TestContext.CurrentContext.TestDirectory })
                for (var d = new DirectoryInfo(start); d != null; d = d.Parent)
                {
                    foreach (string candidate in new[] { Path.Combine(d.FullName, "Content"), Path.Combine(d.FullName, "game", "Content") })
                        if (File.Exists(Path.Combine(candidate, "enemies.json"))) return candidate;
                }
            throw new DirectoryNotFoundException("game/Content");
        }

        static GameContent Load() => GameContent.Load(ContentDir());

        /// <summary>The content with one file's text changed by <paramref name="edit"/>.</summary>
        static GameContent LoadEdited(string file, Func<string, string> edit)
        {
            string dir = ContentDir();
            return GameContent.Parse(name =>
            {
                string text = File.ReadAllText(Path.Combine(dir, name));
                if (name != file) return text;
                string edited = edit(text);
                Assert.That(edited, Is.Not.EqualTo(text), "the edit must change " + file);
                return edited;
            });
        }

        static void AssertFails(string file, Func<string, string> edit, params string[] expected)
        {
            var e = Assert.Throws<ContentException>(() => LoadEdited(file, edit));
            foreach (string part in expected) StringAssert.Contains(part, e.Message);
        }

        static string ReplaceFirst(string text, string from, string to)
        {
            int i = text.IndexOf(from, StringComparison.Ordinal);
            Assert.That(i, Is.GreaterThanOrEqualTo(0), "'" + from + "' is in the file");
            return text.Substring(0, i) + to + text.Substring(i + from.Length);
        }

        [Test]
        public void EveryFileLoadsWithNativesRoster()
        {
            GameContent c = Load();
            Assert.That(c.Roles.Length, Is.EqualTo(26), "native's 22 survivors roles, three set-piece roles and the minotaur");
            Assert.That(c.Models.Length, Is.EqualTo(9), "demo3d's nine models");
            Assert.That(c.Weapons.Length, Is.EqualTo(12));
            Assert.That(Array.FindAll(c.Weapons, w => w.IsEvolution).Length, Is.EqualTo(5));
            Assert.That(c.Passives.Length, Is.EqualTo(8));
            Assert.That(c.Offers.Levels.Length, Is.EqualTo(69), "levels 2 to 70");
            Assert.That(c.Schedule.Minutes.Length, Is.EqualTo(31), "minutes 0 to 30");
            Assert.That(c.Schedule.DurationS, Is.EqualTo(1800));
        }

        [Test]
        public void CrossReferencesResolve()
        {
            GameContent c = Load();
            for (int i = 0; i < c.Roles.Length; i++)
            {
                Assert.That(c.Cast[i].Role, Is.EqualTo(c.Roles[i].Id));
                Assert.That(Array.Exists(c.Models, m => m.Id == c.Cast[i].Model), c.Cast[i].Role);
            }
            foreach (var level in c.Offers.Levels)
                foreach (var card in level)
                    foreach (string item in card)
                        Assert.That(item == OfferTable.Any || c.PassiveIndex(item) >= 0
                            || (c.WeaponIndex(item) >= 0 && !c.Weapon(item).IsEvolution), item);
            foreach (var w in c.Weapons)
                if (w.IsEvolution)
                {
                    Assert.That(c.Weapon(w.EvolutionOf).IsEvolution, Is.False, w.Id);
                    Assert.That(c.PassiveIndex(w.Requires), Is.GreaterThanOrEqualTo(0), w.Id);
                    Assert.That(w.MaxRank, Is.EqualTo(1), w.Id);
                }
                else Assert.That(w.MaxRank, Is.EqualTo(8), w.Id);
            foreach (var (role, path) in c.Schedule.RoleRefs())
                Assert.That(c.RoleIndex(role), Is.GreaterThanOrEqualTo(0), path);
        }

        [Test]
        public void NativesNumbersArriveInMetres()
        {
            GameContent c = Load();
            WeaponDef sword = c.Weapon("sword");
            Assert.That(sword.Shape.Kind, Is.EqualTo(ShapeKind.Sword));
            Assert.That(sword.Shape.RadiusM, Is.EqualTo(190 / 27.0).Within(1e-4), "native's 190 px reach");
            Assert.That(c.Progression.Hero.SpeedMps, Is.EqualTo(140 / 27.0).Within(1e-4), "native's 140 px/s");

            EnemyRole skeleton = c.Role("skeleton");
            Assert.That(skeleton.RadiusM, Is.EqualTo(12 / 27.0).Within(1e-4));
            Assert.That(skeleton.RefSpeedMps / c.Progression.Hero.SpeedMps, Is.EqualTo(0.231 / 0.825).Within(1e-4),
                "VS's enemy-to-hero speed ratio");
            Assert.That(skeleton.Health, Is.EqualTo(1.5));
            Assert.That(skeleton.Power, Is.EqualTo(10));
            Assert.That(skeleton.Gait.StepsOn(0) && skeleton.Gait.StepsOn(1), Is.True);
            Assert.That(c.Role("mudman1").Gait.StepsOn(1), Is.False, "x. steps every other beat");
        }

        [Test]
        public void WeaponRanksAccumulateNativesDeltas()
        {
            GameContent c = Load();
            WeaponDef sword = c.Weapon("sword");
            Assert.That(sword.Rank(1).Power, Is.EqualTo(1));
            Assert.That(sword.Rank(2).Amount, Is.EqualTo(2));
            Assert.That(sword.Rank(8).Power, Is.EqualTo(4));
            Assert.That(sword.Rank(8).Area, Is.EqualTo(1.2).Within(1e-9));
            Assert.That(c.Weapon("garlic").Rank(8).IntervalS, Is.EqualTo(1.0).Within(1e-9), "1300 ms less 3 x 100");
            Assert.That(c.Weapon("bloody-tear").Requires, Is.EqualTo("hollow-heart"));
        }

        [Test]
        public void ProgressionMatchesNative()
        {
            GameContent c = Load();
            ExperienceCurve xp = c.Progression.Experience;
            Assert.That(xp.Total(1), Is.EqualTo(5));
            Assert.That(xp.Total(19), Is.EqualTo(5 * 19 * 19));
            Assert.That(xp.Total(20), Is.EqualTo(6.5 * 400));
            Assert.That(xp.Total(40), Is.EqualTo(8 * 1600));
            Assert.That(c.Passive("duplicator").MaxRank, Is.EqualTo(2));
            Assert.That(c.Passive("empty-tome").PerRank.Cooldown, Is.EqualTo(-0.08));
            Assert.That(c.Progression.Hero.Stats.MaxHp, Is.EqualTo(120));
            Assert.That(c.Progression.Hero.CapMin.Cooldown, Is.EqualTo(0.1));
            Assert.That(c.Progression.Hero.CapMax.Might, Is.EqualTo(10));
            Assert.That(double.IsPositiveInfinity(c.Progression.Hero.CapMax.Luck), Is.True);
            Assert.That(c.Schedule.Minutes[25].Treasure.Tier, Is.EqualTo(3), "minute 25's scripted tier-3 chest");
            Assert.That(c.Schedule.Minutes[10].Treasure.Evolves, Is.True);
            Assert.That(c.Schedule.Signature.FromS, Is.EqualTo(90));
        }

        [Test]
        public void UnknownFieldsFail() =>
            AssertFails("enemies.json", t => ReplaceFirst(t, "\"brute\": false", "\"brute\": false, \"hover\": 18"),
                "enemies.json", "roles[0].hover", "unknown field");

        [Test]
        public void MissingFieldsFail() =>
            AssertFails("schedule.json", t => ReplaceFirst(t, "\"minimum\": 15, ", ""),
                "schedule.json", "minutes[0].minimum", "missing");

        [Test]
        public void WrongTypesFail() =>
            AssertFails("weapons.json", t => ReplaceFirst(t, "\"grid\": \"beat\"", "\"grid\": 1"),
                "weapons.json", "weapons[0].grid", "expected a string");

        [Test]
        public void OffersNamingNoWeaponFail() =>
            AssertFails("offers.json", t => ReplaceFirst(t, "[\"magic-wand\"]", "[\"magic-wnd\"]"),
                "offers.json", "levels[0]", "magic-wnd");

        [Test]
        public void OffersOfAnEvolutionFail() =>
            AssertFails("offers.json", t => ReplaceFirst(t, "[\"magic-wand\"]", "[\"holy-wand\"]"),
                "offers.json", "holy-wand");

        [Test]
        public void CastNamingNoModelFails() =>
            AssertFails("cast.json", t => ReplaceFirst(t, "\"model\": \"bat\"", "\"model\": \"wyvern\""),
                "cast.json", "cast[0].model", "wyvern");

        [Test]
        public void CastMissingARoleFails() =>
            AssertFails("cast.json", t => ReplaceFirst(t, "{\"role\": \"bat1\", \"model\": \"bat\", \"scale\": 1, \"crystal\": \"#a77bff\"},", ""),
                "cast.json", "25 entries for 26 roles");

        [Test]
        public void ScheduleNamingNoRoleFails() =>
            AssertFails("schedule.json", t => ReplaceFirst(t, "\"enemies\": [\"bat3\"]", "\"enemies\": [\"bat9\"]"),
                "schedule.json", "minutes[0].enemies", "bat9");

        [Test]
        public void SetPieceNamingNoRoleFails() =>
            AssertFails("schedule.json", t => ReplaceFirst(t, "\"role\": \"minotaur\"", "\"role\": \"cyclops\""),
                "schedule.json", "signature.role", "cyclops");

        [Test]
        public void EvolutionNamingNoPassiveFails() =>
            AssertFails("weapons.json", t => ReplaceFirst(t, "\"requires\": \"hollow-heart\"", "\"requires\": \"hollow-hart\""),
                "weapons.json", "requires", "hollow-hart");

        [Test]
        public void DuplicateIdsFail() =>
            AssertFails("enemies.json", t => ReplaceFirst(t, "{\"id\": \"bat2\"", "{\"id\": \"bat1\""),
                "enemies.json", "duplicate id 'bat1'");

        [Test]
        public void MalformedJsonFails() =>
            AssertFails("passives.json", t => t.Replace("]", ""), "passives.json", "JSON");
    }
}
