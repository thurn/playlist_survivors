using System;
using System.IO;
using System.Linq;
using Demo3D.EditorTools.Assets;
using Demo3D.Runtime.Playback;
using NUnit.Framework;
using PS.Choreography;
using Roster = Demo3D.EditorTools.Assets.Roster;

namespace Demo3D.Tests
{
    /// <summary>`--roster` (crowd.md, Roster sets): the flag, its failures, and each set's roster and level files.</summary>
    public class RosterSetTests
    {
        const string Choreography = "Assets/Demo3D/Choreography/Data/";

        [Test]
        public void TheFlagNamesASetAndDefaultsToTodaysRoster()
        {
            Assert.That(RosterSet.FromArgs(new[] { "player" }).IsDefault);
            Assert.That(RosterSet.FromArgs(new[] { "player", "--roster", "forest", "--fixed-time" }).Name, Is.EqualTo("forest"));
            foreach (var name in RosterSet.Names)
                Assert.That(RosterSet.FromArgs(new[] { "--roster", name }).Name, Is.EqualTo(name));
            Assert.That(RosterSet.FromArgs(new[] { "--roster", "default" }).IsDefault);
            var heroic = RosterSet.Parse("heroic");
            Assert.That(heroic.AssetsResource, Is.EqualTo(RosterAssets.ResourcePath + "-heroic"));
            Assert.That(heroic.LevelName, Is.EqualTo("action-level-heroic"));
            Assert.That(RosterSet.Default.AssetsResource, Is.EqualTo(RosterAssets.ResourcePath), "the default bake is unchanged");
            Assert.That(RosterSet.Default.LevelName, Is.EqualTo("action-level"));
        }

        [Test]
        public void UnknownMissingOrStyledSetsFailLoudly()
        {
            Assert.Throws<ArgumentException>(() => RosterSet.FromArgs(new[] { "--roster", "swamp" }));
            Assert.Throws<ArgumentException>(() => RosterSet.FromArgs(new[] { "--roster", "Forest" }));
            Assert.Throws<ArgumentException>(() => RosterSet.FromArgs(new[] { "--roster" }));
            Assert.Throws<ArgumentException>(() => RosterSet.FromArgs(new[] { "--roster", "--fixed-time" }));
            Assert.Throws<ArgumentException>(() => RosterSet.FromArgs(new[] { "--roster", "forest", "--monster-style", "painted" }));
            Assert.Throws<ArgumentException>(() => RosterSet.FromArgs(new[] { "--monster-style", "fire", "--roster", "heroic" }));
            Assert.That(RosterSet.FromArgs(new[] { "--roster", "forest", "--monster-style", "baseline" }).Name, Is.EqualTo("forest"));
            Assert.That(RosterSet.FromArgs(new[] { "--monster-style", "painted" }).IsDefault, "a style alone keeps the default roster");
        }

        [Test]
        public void EachSetFillsOnlyTheDefaultsMonsterSlotsAndKeepsTheHero()
        {
            var defaults = Roster.Load();
            foreach (var set in RosterSet.Names.Where(n => n != "default"))
            {
                var types = Roster.Load(set);
                Assert.That(types.Select(t => t.id), Is.EqualTo(defaults.Select(t => t.id)), set + " keeps the slot order");
                Assert.That(types.Find(t => t.id == "hero").prefab.guid, Is.EqualTo(defaults.Find(t => t.id == "hero").prefab.guid), set + " keeps the hero");
                Assert.That(types.Where((t, i) => t.prefab.guid != defaults[i].prefab.guid), Is.Not.Empty, set + " replaces a slot");
                Assert.That(types.Where(t => t.kind != "hero"), Has.All.Matches<Roster.Type>(t => t.clips.Count == 6), set + "'s monsters have the six roles");
            }
        }

        [Test]
        public void EachSetsLevelIsTheSongsScriptUnderASmallerEnvelope()
        {
            var level = LevelScript.Parse(File.ReadAllText(Choreography + "action-level.json"));
            foreach (var set in RosterSet.Names.Where(n => n != "default"))
            {
                var variant = LevelScript.Parse(File.ReadAllText(Choreography + RosterSet.Parse(set).LevelName + ".json"));
                Assert.That(variant.EnvelopeOnScreen, Is.LessThan(level.EnvelopeOnScreen), set);
                Assert.That(variant.EnvelopeViewWidthM, Is.LessThan(level.EnvelopeViewWidthM), set);
                Assert.That(variant.Sections.Select(s => s.Section), Is.EqualTo(level.Sections.Select(s => s.Section)), set + " keeps the song's sections");
            }
        }

        [Test]
        public void ASetsAnnotationsReplaceTheDefaultsByUseKeepingTheirIndices()
        {
            const string json = @"{""annotations"": [
                {""use"": ""skeleton walk"", ""clip"": ""Walk"", ""strideM"": 1.2},
                {""use"": ""skeleton attack"", ""clip"": ""Attack 1"", ""impact"": 0.75, ""end"": 1.5}],
              ""rosters"": {""forest"": {""annotations"": [
                {""use"": ""skeleton attack"", ""clip"": ""Attack01"", ""impact"": 0.4, ""end"": 0.9}]}}}";
            Assert.That(RosterSet.Default.Annotations(json), Is.EqualTo(json), "the default reads the file as it is");
            Assert.That(RosterSet.Parse("heroic").Annotations(json), Is.EqualTo(json), "a set with no annotations keeps the default's");
            var clips = ClipLibrary.Parse(RosterSet.Parse("forest").Annotations(json));
            Assert.That(clips.Count, Is.EqualTo(2));
            Assert.That(clips.Require("skeleton attack"), Is.EqualTo(1));
            Assert.That(clips[1].Clip, Is.EqualTo("Attack01"));
            Assert.That(clips[1].Impact, Is.EqualTo(0.4f));
            Assert.That(clips[0].Clip, Is.EqualTo("Walk"));
        }

        [Test]
        public void TheForestSetFillsEverySlotAndAnnotatesEachRole()
        {
            var forest = Roster.Load("forest").Where(t => t.kind != "hero" && t.kind != "rejected-boss").ToList();
            var own = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(Roster.SetPath("forest")))["types"].Select(t => (string)t["id"]).ToList();
            Assert.That(forest.Select(t => t.id), Is.EquivalentTo(own), "the set file lists every monster slot");
            var clips = ClipLibrary.Parse(RosterSet.Parse("forest").Annotations(File.ReadAllText(ClipAnnotator.OutPath)));
            foreach (var t in forest)
                foreach (var kv in t.clips)
                {
                    var use = (t.kind == "boss" ? "boss" : t.id) + " " + kv.Key;
                    Assert.That(clips[clips.Require(use)].Guid, Is.EqualTo(kv.Value.guid), use + " is the set's own clip");
                }
        }

        [Test]
        public void ASetsRadiiReplaceTheChoreographysWhereItGivesOne()
        {
            const string json = @"{""monsters"": [{""name"": ""bat"", ""radius"": 0.35}, {""name"": ""boss"", ""radius"": 1.5}]}";
            Assert.That(RosterSet.Default.Choreography(json, id => 9), Is.EqualTo(json), "the default reads the file as it is");
            var root = Newtonsoft.Json.Linq.JObject.Parse(RosterSet.Parse("heroic").Choreography(json, id => id == "bat" ? 0.6f : 0));
            Assert.That((float)root["monsters"][0]["radius"], Is.EqualTo(0.6f));
            Assert.That((float)root["monsters"][1]["radius"], Is.EqualTo(1.5f), "no radius keeps the choreography's");
        }

        [Test]
        public void TheHeroicSetSizesEverySlotItself()
        {
            var types = Roster.Load("heroic").Where(t => t.kind != "hero" && t.kind != "rejected-boss").ToList();
            Assert.That(Roster.SetIds("heroic").Count(), Is.EqualTo(9), "heroic fills all nine slots");
            Assert.That(types, Has.All.Matches<Roster.Type>(t => t.radius > 0 && t.heightM > 0));
        }
    }
}
