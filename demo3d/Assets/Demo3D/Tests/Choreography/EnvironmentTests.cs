using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using PS.Choreography;

namespace Demo3D.Choreography.Tests
{
    /// <summary>ps-bop's environments: a layout that extends the arena keeps its area plan and merges the rest over it.</summary>
    public class EnvironmentTests
    {
        const string Base = "{ \"name\": \"arena\", \"size\": [160, 160], \"heroStart\": [0, -50],"
            + " \"areas\": [ { \"type\": \"landmark\", \"name\": \"tree\", \"at\": [3, 4], \"pieces\": [ { \"prop\": \"a/Tree\", \"at\": [0, 0] } ] },"
            + "   { \"type\": \"clearing\", \"name\": \"camp\", \"center\": [0, 0], \"radius\": 10 } ],"
            + " \"props\": { \"trees\": { \"a/Tree\": 1, \"a/Pine\": 2 }, \"rocks\": { \"a/Rock\": 1 } },"
            + " \"lighting\": { \"moon\": { \"elevationDeg\": 42, \"color\": [1, 0.9, 0.7] }, \"glow\": { \"a/Tree\": { \"rangeM\": 18 } },"
            + "   \"kitValue\": { \"PackA\": 0.5, \"PackB\": 0.7 } } }";

        static Func<string, string> Layouts(string json = Base) => name => name == "arena" ? json : null;

        static JsonValue Resolve(string env) => JsonValue.Parse(LayoutEnvironment.Resolve(env, Layouts(), "test"));

        [Test]
        public void AnEnvironmentKeepsTheAreaPlanAndMergesItsOverrides()
        {
            JsonValue l = Resolve("{ \"extends\": \"arena\", \"props\": { \"trees\": { \"b/Dead\": 1 } },"
                + " \"lighting\": { \"moon\": { \"color\": [0.9, 0.9, 0.9] }, \"glow\": null, \"kitValue\": { \"PackB\": null, \"PackC\": 0.6 } },"
                + " \"ground\": { \"forest\": [0.1, 0.1, 0.1] } }");
            Assert.That(l["name"].AsString(), Is.EqualTo("arena"), "scripts still name the arena");
            Assert.That(l["heroStart"][1].AsFloat(), Is.EqualTo(-50f));
            Assert.That(l["areas"].Count, Is.EqualTo(2));
            Assert.That(l["areas"][0]["pieces"][0]["prop"].AsString(), Is.EqualTo("a/Tree"));
            Assert.That(l["props"]["trees"].Members.Select(m => m.Key), Is.EqualTo(new[] { "b/Dead" }), "a prop group replaces whole");
            Assert.That(l["props"]["rocks"]["a/Rock"].AsFloat(), Is.EqualTo(1f), "a group it leaves out is the arena's");
            Assert.That(l["lighting"]["moon"]["elevationDeg"].AsFloat(), Is.EqualTo(42f), "objects merge key by key");
            Assert.That(l["lighting"]["moon"]["color"][2].AsFloat(), Is.EqualTo(0.9f), "arrays replace");
            Assert.That(l["lighting"].Has("glow"), Is.False, "null removes a key");
            Assert.That(l["lighting"]["kitValue"].Members.Select(m => m.Key), Is.EqualTo(new[] { "PackA", "PackC" }));
            Assert.That(l["ground"]["forest"][0].AsFloat(), Is.EqualTo(0.1f), "a key the arena lacks is added");
            Assert.That(l.Has("extends"), Is.False);
            Assert.That(ArenaLayout.Parse(LayoutEnvironment.Resolve("{ \"extends\": \"arena\" }", Layouts())).Areas.Length, Is.EqualTo(2), "a bare environment is the arena");
        }

        [Test]
        public void PiecesReplaceALandmarksKitAtTheSameAnchor()
        {
            JsonValue l = Resolve("{ \"extends\": \"arena\", \"pieces\": { \"tree\": [ { \"prop\": \"b/Statue\", \"at\": [1, 0] } ] } }");
            JsonValue tree = l["areas"][0];
            Assert.That(tree["at"][0].AsFloat(), Is.EqualTo(3f));
            Assert.That(tree["pieces"].Count, Is.EqualTo(1));
            Assert.That(tree["pieces"][0]["prop"].AsString(), Is.EqualTo("b/Statue"));
            Assert.That(l.Has("pieces"), Is.False);
            var e = Assert.Throws<FormatException>(() => Resolve("{ \"extends\": \"arena\", \"pieces\": { \"camp\": [] } }"));
            StringAssert.Contains("camp", e.Message, "pieces only for a landmark or structure");
        }

        [Test]
        public void APiecesLiftRaisesItsPlacementOnly()
        {
            string root = Fixtures.Root;
            string arena = File.ReadAllText(ChoreographyFiles.DataPath(root, "arena-layout.json"));
            string env = "{ \"extends\": \"arena\", \"pieces\": { \"glowing-tree\": [ { \"prop\": \"elven/SM_Env_Tree_Glowing_01\", \"at\": [0, 0] },"
                + " { \"prop\": \"elven/SM_Env_Tree_Glowing_01\", \"at\": [2, 0], \"liftM\": 3.5 } ] } }";
            ArenaLayout l = ArenaLayout.Parse(LayoutEnvironment.Resolve(env, name => name == "arena" ? arena : null, "test"));
            PlacementList list = l.Generate(ChoreographyFiles.LoadFootprints(root));
            int tree = Array.FindIndex(l.Areas, a => a.Name == "glowing-tree");
            var lifts = list.Items.Where(p => p.Area == tree).Select(p => p.Lift).ToArray();
            Assert.That(lifts, Is.EqualTo(new[] { 0f, 3.5f }));
            Assert.That(list.Items.Where(p => p.Area != tree).All(p => p.Lift == 0f), "scatter and other areas stay on the ground");
        }

        [Test]
        public void AnEnvironmentCannotMoveTheAreaPlan()
        {
            foreach (string key in LayoutEnvironment.PlanKeys)
            {
                var e = Assert.Throws<FormatException>(() => Resolve("{ \"extends\": \"arena\", \"" + key + "\": null }"), key);
                StringAssert.Contains(key, e.Message);
            }
            Assert.Throws<FormatException>(() => LayoutEnvironment.Resolve("{ \"extends\": \"nowhere\" }", Layouts()), "an unknown layout to extend");
            Assert.That(LayoutEnvironment.Resolve(Base, Layouts()), Is.EqualTo(Base), "a layout that extends nothing is itself");
        }

        /// <summary>Every environment beside the arena's layout (Data/environment-&lt;name&gt;.json).</summary>
        static IEnumerable<string> Environments() =>
            Directory.GetFiles(Path.Combine(Fixtures.Root, ChoreographyFiles.DataDir), "environment-*.json")
                .Select(f => Path.GetFileNameWithoutExtension(f).Substring("environment-".Length)).OrderBy(n => n, StringComparer.Ordinal);

        /// <summary>
        /// Each environment's placement list is its own kit on the arena's plan: the same goals and shots,
        /// the level's goals and the stress scenes' holds walkable, and the courtyard's stress scene runs in it.
        /// </summary>
        [TestCaseSource(nameof(Environments))]
        public void AnEnvironmentsPlacementListKeepsTheArenasPlanWalkable(string name)
        {
            string root = Fixtures.Root;
            string layout = ChoreographyFiles.EnvironmentLayout(root, name);
            FootprintTable fp = ChoreographyFiles.LoadFootprints(root);
            ArenaLayout arena = ChoreographyFiles.LoadLayout(root), env = ArenaLayout.Parse(layout);
            Assert.That(env.Goals().Select(g => (g.Name, g.Center, g.Radius)), Is.EqualTo(arena.Goals().Select(g => (g.Name, g.Center, g.Radius))));
            Assert.That(env.Shots.Select(s => (s.Name, s.At)), Is.EqualTo(arena.Shots.Select(s => (s.Name, s.At))));
            PlacementList c = env.Generate(fp);
            // No scattered blocking piece's centre inside a goal.
            foreach (GoalArea g in env.Goals())
                foreach (Placement p in c.Items)
                    if (c.Props[p.Prop].Blocks && p.Area < 0)
                        Assert.That(System.Numerics.Vector2.Distance(p.Position, g.Center), Is.GreaterThan(g.Radius), g.Name + " is clear of " + c.Props[p.Prop].Id);

            var data = ChoreographyData.FromJson(File.ReadAllText(ChoreographyFiles.DataPath(root, ChoreographyFiles.AnnotationsFile)),
                File.ReadAllText(ChoreographyFiles.DataPath(root, ChoreographyFiles.RosterFile)), layout,
                File.ReadAllText(ChoreographyFiles.DataPath(root, ChoreographyFiles.FootprintsFile)));
            Timeline tl = Choreographer.RunStress(Fixtures.StressScript("S6").With(300, 0, null, 20), data);
            Assert.That(tl.EntityCount, Is.GreaterThan(100), "S6 runs in " + name);
        }

        /// <summary>The crypt swaps the forest's kit and leaves the untouched landmarks where the arena's are.</summary>
        [Test]
        public void TheCryptsPlacementListIsItsKitOnTheArenasPlan()
        {
            string root = Fixtures.Root;
            FootprintTable fp = ChoreographyFiles.LoadFootprints(root);
            ArenaLayout arena = ChoreographyFiles.LoadLayout(root), env = ArenaLayout.Parse(ChoreographyFiles.EnvironmentLayout(root, "crypt"));
            PlacementList a = arena.Generate(fp), c = env.Generate(fp);
            var ids = new HashSet<string>(c.Items.Select(p => c.Props[p.Prop].Id));
            Assert.That(ids, Does.Not.Contain("kingdom/SM_Env_Tree_Round_01"), "the forest's trees stay in the forest");
            Assert.That(ids, Does.Contain("darkfantasy/SM_Env_Tree_Dead_02"));
            // The structures' and the untouched landmarks' pieces stand where the arena's do.
            var graveyard = arena.Area("graveyard");
            var at = a.Items.Where(p => p.Area >= 0 && arena.Areas[p.Area] == graveyard).Select(p => p.Position).ToList();
            Assert.That(c.Items.Where(p => p.Area >= 0 && env.Areas[p.Area].Name == "graveyard").Select(p => p.Position), Is.EqualTo(at));
        }
    }
}
