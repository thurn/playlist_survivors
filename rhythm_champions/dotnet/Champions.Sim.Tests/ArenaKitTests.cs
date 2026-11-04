using System.Linq;
using NUnit.Framework;

namespace Champions.Sim.Tests
{
    /// <summary>
    /// The Boss Rush act kits (boss-rush.md, Environments; ps-8z4.5): an act's kit dresses its arenas' placeholder props as
    /// its own kinds without changing anything the sim does, and its prop kinds are ordinary props a fight's arena can use.
    /// </summary>
    public class ArenaKitTests
    {
        static readonly GameContent Content = DashTests.Content;
        static ArenaRules Rules => Content.Arenas;

        [Test]
        public void ActOnesKitDressesEveryPlaceholderWithAKindOfTheSameFootprintAndBreaking()
        {
            string id = Content.BossRush.Acts[0].Kit;
            var kit = Rules.Kit(id);
            Assert.IsNotNull(kit, $"act 1's kit {id} is in arenas.json");
            var kinds = Rules.Props;
            int own = kinds.Count(k => k.Kit == id);
            Assert.That(own, Is.InRange(6, 12), "an act kit has 6 to 12 props");
            for (int i = 0; i < kinds.Length; i++)
            {
                var from = kinds[i];
                var to = kinds[kit.Dress[i]];
                if (from.Kit != null)
                {
                    Assert.AreEqual(i, kit.Dress[i], $"{from.Id}: a kit kind draws as itself");
                    continue;
                }
                Assert.AreEqual(id, to.Kit, $"{from.Id} is dressed as one of the kit's own kinds");
                Assert.AreEqual(from.Bound, to.Bound, 1e-9, from.Id);
                Assert.AreEqual(from.HeightM, to.HeightM, 1e-9, from.Id);
                Assert.AreEqual(from.Hits, to.Hits, from.Id);
            }
            Assert.IsNotEmpty(kit.FloorPieces, "a floor treatment");
            Assert.IsNotEmpty(kit.Backdrop, "a backdrop");
            Assert.IsTrue(kit.Backdrop.All(r => r.Facing > -0.5), "the backdrop stands behind the arena, never between it and the camera");
        }

        [Test]
        public void AKitThatWouldChangeWhatTheSimDoesOrNamesNoKitIsRefused()
        {
            string Rules(string kitProp, string dress, string arenaKit = "") => $@"{{
                ""formBeats"": 4, ""retractBeats"": 4, ""wallThicknessM"": 0.5, ""wallHeightM"": 1, ""raiseM"": 1, ""sinkM"": 1,
                ""props"": [{{ ""id"": ""pillar"", ""radiusM"": 1, ""heightM"": 3, ""hits"": 0 }}, {kitProp}],
                ""arenas"": [{{ ""id"": ""a"", ""outline"": {{ ""radiusM"": 10 }}, ""heroStart"": [0, -5], ""bossStart"": [0, 5],
                    ""camera"": {{ ""maxScale"": 1.5, ""marginM"": 1 }}, {arenaKit} ""zones"": [], ""props"": [] }}],
                ""kits"": [{{ ""id"": ""woods"", ""dress"": {dress} }}] }}";
            const string Oak = @"{ ""id"": ""oak"", ""kit"": ""woods"", ""radiusM"": 1, ""heightM"": 3, ""hits"": 0 }";
            var read = ArenaRules.Read(JsonValue.Parse(Rules(Oak, @"{ ""pillar"": ""oak"" }", @"""kit"": ""woods"",")), "t");
            Assert.AreEqual(1, read.Kit("woods").Dress[0]);
            Assert.AreEqual("woods", read.Arenas[0].Kit);
            Assert.Throws<ContentException>(() => ArenaRules.Read(JsonValue.Parse(Rules(
                @"{ ""id"": ""oak"", ""kit"": ""woods"", ""radiusM"": 1.5, ""heightM"": 3, ""hits"": 0 }", @"{ ""pillar"": ""oak"" }")), "t"), "a wider footprint");
            Assert.Throws<ContentException>(() => ArenaRules.Read(JsonValue.Parse(Rules(
                @"{ ""id"": ""oak"", ""kit"": ""woods"", ""radiusM"": 1, ""heightM"": 3, ""hits"": 1 }", @"{ ""pillar"": ""oak"" }")), "t"), "breakable where the pillar isn't");
            Assert.Throws<ContentException>(() => ArenaRules.Read(JsonValue.Parse(Rules(Oak, @"{ ""oak"": ""oak"" }")), "t"), "only placeholders are dressed");
            Assert.Throws<ContentException>(() => ArenaRules.Read(JsonValue.Parse(Rules(
                @"{ ""id"": ""oak"", ""kit"": ""marsh"", ""radiusM"": 1, ""heightM"": 3, ""hits"": 0 }", "{}")), "t"), "a prop of no kit");
            Assert.Throws<ContentException>(() => ArenaRules.Read(JsonValue.Parse(Rules(Oak, "{}", @"""kit"": ""marsh"",")), "t"), "an arena of no kit");
        }

        [Test]
        public void AKitsPropsStandAndBreakInAnArenaAsAnyPropDoes()
        {
            var config = new RunInputs { Loadout = new Upgrade[0] }.Build(Content);
            config.Arenas = Rules.Clone();
            int mushroom = Rules.PropIndex("giantMushroom"), stone = Rules.PropIndex("runeStone");
            var def = new ArenaDef
            {
                Id = "kit-test", Outline = ArenaShape.Circle(0, 0, 14, ArenaRules.CircleSides), HeroStartY = -6, BossStartY = 6,
                Props = new[]
                {
                    new ArenaPropPlacement { Kind = mushroom, X = -5, Y = 0, Blocks = true, BlocksShots = true },
                    new ArenaPropPlacement { Kind = stone, X = 5, Y = 0, Blocks = true, BlocksShots = true },
                },
            };
            config.Arenas.Arenas = config.Arenas.Arenas.Append(def).ToArray();
            var sim = new Simulation(config);
            sim.Arena.FormAroundHero(def, sim.Now);
            sim.StepTo(sim.Now + 3);
            var arena = sim.Arena;
            Assert.IsFalse(arena.Walkable(arena.Prop(0).X, arena.Prop(0).Y, 0.3), "a kit prop blocks");
            Assert.IsTrue(arena.Breakable(0), "the giant mushroom breaks under the hero's blows");
            Assert.IsFalse(arena.Breakable(1), "the rune stone doesn't");
        }
    }
}
