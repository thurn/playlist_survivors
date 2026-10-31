using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using NUnit.Framework;
using PS.Choreography;

namespace Demo3D.Choreography.Tests
{
    /// <summary>
    /// `--structure-breaks` (choreography.md, Structure breaks): the overlay and its failures, the hero's bolt that
    /// breaks each piece on the grid, and the obstacle map that changes with it. The generic timeline checks run on
    /// the walls schedule as "song-walls" (<see cref="TimelineRulesTests"/>, and NavigationTests' wall check, which
    /// stops counting a broken piece at its break). Without the flag nothing changes: the song's and demo2d's dumps
    /// keep their pinned hashes (IntensityTests.NormalTimelinesAreByteIdenticalToBeforeTheFields and demo2d's
    /// SongDumpIsByteIdenticalToBeforeTheTuningFields).
    /// </summary>
    public class StructureBreaksTests
    {
        static string LevelJson => File.ReadAllText(ChoreographyFiles.DataPath(Fixtures.Root, ChoreographyFiles.LevelFile));

        static string Overlay(string sections) => "{ \"name\": \"test\", \"sections\": { " + sections + " } }";

        [Test]
        public void WithoutTheFlagThereAreNoBreaks()
        {
            Assert.That(Fixtures.Level.StructureBreaks, Is.Null);
            Assert.That(Fixtures.SongTimeline.Breaks, Is.Empty);
            StringAssert.DoesNotContain("\"breaks\"", TimelineJson.ToJson(Fixtures.SongTimeline, 0, 0.01));
            StringAssert.Contains("\"breaks\"", TimelineJson.ToJson(Fixtures.WallsTimeline, 0, 0.01));
        }

        /// <summary>The schedule loads over every roster's level script and any intensity, adding only its breaks.</summary>
        [TestCase("action-level.json", null)]
        [TestCase("action-level-forest.json", null)]
        [TestCase("action-level-heroic.json", "overdrive")]
        public void TheScheduleLoadsOverAnyScriptAndIntensity(string file, string intensity)
        {
            LevelScript plain = ChoreographyFiles.LoadLevel(Fixtures.Root, file, intensity);
            LevelScript walls = ChoreographyFiles.LoadLevel(Fixtures.Root, file, intensity, "walls");
            Assert.That((walls.StructureBreaks, walls.Intensity), Is.EqualTo(("walls", plain.Intensity)));
            Assert.That(walls.EnvelopeOnScreen, Is.EqualTo(plain.EnvelopeOnScreen));
            int breaks = 0;
            foreach (LevelSection s in walls.Sections)
            {
                LevelSection p = plain.For(s.Section);
                Assert.That((s.OnScreen, s.Load, s.Goal, s.Spells.Length), Is.EqualTo((p.OnScreen, p.Load, p.Goal, p.Spells.Length)), s.Section);
                Assert.That(p.Breaks, Is.Empty);
                breaks += s.Breaks.Length;
            }
            int entries = 0;
            foreach (KeyValuePair<string, JsonValue> s in JsonValue.Parse(File.ReadAllText(ChoreographyFiles.DataPath(Fixtures.Root, ChoreographyFiles.StructureBreaksFile("walls"))))["sections"].Members)
                entries += s.Value["breaks"].Count;
            Assert.That(breaks, Is.EqualTo(entries));
        }

        [Test]
        public void UnknownSchedulesFailListingTheKnownOnes()
        {
            Assert.That(ChoreographyFiles.StructureBreakNames(Fixtures.Root), Is.EqualTo(new[] { "walls" }));
            var e = Assert.Throws<ArgumentException>(() => ChoreographyFiles.LoadLevel(Fixtures.Root, ChoreographyFiles.LevelFile, null, "everything"));
            StringAssert.Contains("(one of walls)", e.Message);
        }

        /// <summary>A schedule only schedules breaks: anything else it would set, or an entry without a grid position, fails.</summary>
        [TestCase("{ \"name\": \"x\", \"comboMaxSpeed\": 2, \"sections\": {} }")]
        [TestCase("{ \"name\": \"x\", \"multiply\": { \"envelope.onScreen\": 2 }, \"sections\": {} }")]
        [TestCase("{ \"name\": \"x\" }")]
        [TestCase("{ \"name\": \"x\", \"sections\": { \"drop-2\": { \"breaks\": [\"courtyard@phrase\"], \"load\": \"heavy\" } } }")]
        [TestCase("{ \"name\": \"x\", \"sections\": { \"*\": { \"breaks\": [\"courtyard@phrase\"] } } }")]
        [TestCase("{ \"name\": \"x\", \"sections\": { \"drop-2\": { \"breaks\": [\"courtyard\"] } } }")]
        [TestCase("{ \"name\": \"x\", \"sections\": { \"drop-2\": { \"breaks\": [\"courtyard@bar\"] } } }")]
        [TestCase("{ \"name\": \"x\", \"sections\": { \"drop-9\": { \"breaks\": [\"courtyard@phrase\"] } } }")]
        public void SchedulesOnlyScheduleBreaks(string overlay) =>
            Assert.Throws<FormatException>(() => LevelScript.Parse(LevelJson, null, overlay));

        /// <summary>Targets name an area, or one of its parts; a target the layout lacks, or one that blocks nothing, fails, listing the area's parts.</summary>
        [Test]
        public void TargetsNameAreasAndTheirParts()
        {
            GroundLayout ground = Fixtures.Data.Layout;
            int runs = 0;
            foreach (string side in new[] { "south-west", "south-east", "east-south", "east-north", "north-east", "north-west", "west-north", "west-south" })
                runs += StructureBreaks.Resolve(ground, "courtyard/" + side).Length;
            Assert.That(StructureBreaks.Resolve(ground, "courtyard").Length, Is.EqualTo(runs), "the courtyard is its eight runs");
            Assert.That(StructureBreaks.Resolve(ground, "ruins-gate/piece-2"), Has.Length.EqualTo(1));
            Assert.That(StructureBreaks.Resolve(ground, "ruins-gate").Length,
                Is.EqualTo(StructureBreaks.Resolve(ground, "ruins-gate/1").Length + StructureBreaks.Resolve(ground, "ruins-gate/2").Length + 2));

            Assert.Throws<FormatException>(() => StructureBreaks.Resolve(ground, "nowhere"));
            var e = Assert.Throws<FormatException>(() => StructureBreaks.Resolve(ground, "courtyard/north"));
            StringAssert.Contains("south-west, south-east", e.Message);
            Assert.Throws<FormatException>(() => StructureBreaks.Resolve(ground, "ruins-gate/3"));
            Assert.Throws<FormatException>(() => StructureBreaks.Resolve(ground, "fairy-ring"), "flowers and stones block nothing");
            Assert.Throws<FormatException>(() => StructureBreaks.Resolve(ChoreographyFiles.LoadData(Fixtures.Root, "open-ground").Layout, "courtyard"));
        }

        /// <summary>The walls schedule's targets are in every environment, each with its own wall pieces.</summary>
        [Test]
        public void EveryEnvironmentHasTheWallsTargets()
        {
            FootprintTable fp = ChoreographyFiles.LoadFootprints(Fixtures.Root);
            var names = new List<string> { LayoutEnvironment.Default };
            foreach (string f in Directory.GetFiles(Path.Combine(Fixtures.Root, ChoreographyFiles.DataDir), "environment-*.json"))
                names.Add(Path.GetFileNameWithoutExtension(f).Substring("environment-".Length));
            Assert.That(names.Count, Is.GreaterThan(3));
            foreach (string env in names)
            {
                GroundLayout ground = env == LayoutEnvironment.Default ? Fixtures.Data.Layout
                    : GroundLayout.Parse(ChoreographyFiles.EnvironmentLayout(Fixtures.Root, env), fp);
                foreach (LevelSection s in Fixtures.WallsLevel.Sections)
                    foreach (GridEntry b in s.Breaks) Assert.That(StructureBreaks.Resolve(ground, b.Name), Is.Not.Empty, env + ": " + b.Name);
            }
        }

        /// <summary>A break on a grid position its section lacks, or on a piece already broken, fails before the run.</summary>
        [TestCase("\"intro\": { \"breaks\": [\"courtyard/north-east@phrase/9\"] }")]
        [TestCase("\"intro\": { \"breaks\": [\"courtyard@phrase\", \"courtyard/north-east@phrase/2\"] }")]
        public void ImpossibleBreaksFail(string sections) =>
            Assert.Throws<FormatException>(() => Choreographer.Run(Fixtures.Song, LevelScript.Parse(LevelJson, null, Overlay(sections)), Fixtures.Data));

        /// <summary>
        /// Each break lands on its grid position, struck by the hero's bolt: a projectile from where the hero stood at
        /// its launch to the point of the piece it strikes, arriving at the break, in view.
        /// </summary>
        [Test]
        public void EachBreakIsTheHerosBoltOnTheGrid()
        {
            Timeline tl = Fixtures.WallsTimeline;
            PlacementList list = Fixtures.Data.Layout.Placements;
            int pieces = 0;
            foreach (LevelSection s in Fixtures.WallsLevel.Sections)
                foreach (GridEntry e in s.Breaks) pieces += StructureBreaks.Resolve(Fixtures.Data.Layout, e.Name).Length;
            Assert.That(tl.Breaks.Length, Is.EqualTo(pieces), "one event per piece");
            double last = 0;
            foreach (BreakEvent b in tl.Breaks)
            {
                string what = b.Target + " at " + b.Time;
                Assert.That(b.Time, Is.GreaterThanOrEqualTo(last), "sorted by time");
                last = b.Time;
                int gi = tl.Grid.FirstAfter(b.Time - 1e-6);
                Assert.That(Math.Abs(tl.Grid.Points[gi].Time - b.Time), Is.LessThan(1e-6), what + " is off the grid");
                Assert.That(tl.Grid.Points[gi].Flags & GridFlags.Phrase, Is.EqualTo(GridFlags.Phrase), what + " is not on a phrase");

                EntityInfo bolt = tl.Entities[b.Projectile];
                Assert.That((bolt.Kind, tl.ProjectileNames[bolt.Type]), Is.EqualTo((EntityKind.Projectile, "bolt")), what);
                Assert.That(bolt.Despawn, Is.EqualTo(b.Time).Within(1e-9), what);
                Assert.That(bolt.Spawn, Is.LessThan(b.Time), what);
                Assert.That(Vector2.Distance(tl.PositionAt(b.Projectile, bolt.Spawn), b.Origin), Is.LessThan(1e-3f), what);
                Assert.That(Vector2.Distance(tl.PositionAt(0, bolt.Spawn), b.Origin), Is.LessThan(1e-3f), what + ": the bolt leaves from the hero");
                MotionSegment flight = tl.Segments[tl.SegmentStart[b.Projectile]];
                Assert.That((flight.T1, flight.To), Is.EqualTo((b.Time, b.Point)), what);

                var target = new List<int>();
                foreach (BreakEvent o in tl.Breaks) if (o.Target == b.Target) target.Add(o.Placement);
                Assert.That(Math.Abs(Only(list, target).Distance(b.Point)), Is.LessThan(0.05f), what + ": the bolt strikes the piece's edge");
                FramingSample f = tl.FramingAt(b.Time);
                Assert.That(tl.View(f.ViewWidth).Contains(f.Focus, b.Point), what + " is out of view");
            }
        }

        /// <summary>
        /// The horde uses the openings: monsters step over a broken piece's footprint only after its break, and some
        /// step through each moment's openings in the bars after it (NobodyStandsInsideAWall("song-walls") keeps
        /// everyone out of the pieces still standing). Counted per moment, not per target: under Unity's Mono, which
        /// rounds differently, one of two runs that break together may go unused.
        /// </summary>
        [Test]
        public void MonstersPathThroughEachOpeningOnlyOnceItIsOpen()
        {
            Timeline tl = Fixtures.WallsTimeline;
            PlacementList list = Fixtures.Data.Layout.Placements;
            var targets = new Dictionary<string, (double time, List<int> pieces)>();
            foreach (BreakEvent b in tl.Breaks)
            {
                if (!targets.ContainsKey(b.Target)) targets[b.Target] = (b.Time, new List<int>());
                targets[b.Target].pieces.Add(b.Placement);
            }
            int total = 0;
            var moments = new SortedDictionary<double, int>();
            foreach (KeyValuePair<string, (double time, List<int> pieces)> kv in targets)
            {
                WallGeometry piece = Only(list, kv.Value.pieces);
                double t0 = kv.Value.time, t1 = tl.Map.TimeOfBeatIndex(tl.Map.BeatIndexAt(t0) + 16);
                int before = 0, after = 0;
                for (int e = 1; e < tl.EntityCount; e++)
                {
                    if (tl.Entities[e].Kind != EntityKind.Monster) continue;
                    bool crossed = false;
                    foreach (MotionSegment s in tl.SegmentsOf(e))
                    {
                        if (s.T0 > t1) break;
                        int n = Math.Max(1, (int)Math.Ceiling(Vector2.Distance(s.From, s.To) / 0.1f));
                        bool inside = false;
                        for (int k = 0; k <= n && !inside; k++) inside = piece.Distance(Vector2.Lerp(s.From, s.To, k / (float)n)) < 0;
                        if (!inside) continue;
                        if (s.T1 <= t0) before++;
                        else if (s.T0 >= t0) crossed = true;
                    }
                    if (crossed) after++;
                }
                TestContext.WriteLine(kv.Key + ": " + after + " monsters through it in the 4 bars after its break at " + t0.ToString("F2"));
                Assert.That(before, Is.EqualTo(0), kv.Key + ": monsters walked through it before it broke");
                moments[t0] = (moments.TryGetValue(t0, out int m) ? m : 0) + after;
                total += after;
            }
            foreach (KeyValuePair<double, int> m in moments) Assert.That(m.Value, Is.GreaterThan(0), "nobody used the openings made at " + m.Key.ToString("F2"));
            Assert.That(total, Is.GreaterThanOrEqualTo(20));
            TestContext.WriteLine(TimelineReport.BreakTable(tl, list));
        }

        /// <summary>
        /// After its rebuild, an obstacle map with pieces removed is exactly the map of a placement list without them,
        /// however the uncapped field's rebuild is sliced; the grid it was cloned from is untouched; and every walls
        /// opening has room for the largest monster (choreography.md, Room for every size).
        /// </summary>
        [Test]
        public void ARemovedPieceLeavesTheMapOfALayoutWithoutIt()
        {
            GroundLayout ground = Fixtures.Data.Layout;
            NavGrid original = ground.Nav;
            float[] before = (float[])original.Clearance.Clone();
            var gone = new HashSet<int>();
            NavGrid whole = original.Clone(), sliced = original.Clone();
            bool[] passable = sliced.PassableFor(0.4f);
            float need = Math.Min(Fixtures.Data.Roster.MaxMonsterRadius + 0.5f, NavGrid.MaxClearance - NavGrid.CellM);
            foreach (LevelSection s in Fixtures.WallsLevel.Sections)
                foreach (GridEntry b in s.Breaks)
                {
                    int[] pieces = StructureBreaks.Resolve(ground, b.Name);
                    foreach (int k in pieces) gone.Add(k);
                    Assert.That(whole.Remove(pieces), Is.All.GreaterThanOrEqualTo(need), b.Name + "'s opening is too narrow");
                    sliced.Remove(pieces);
                }
            whole.BeginOpenRebuild();
            Assert.That(whole.StepOpen(int.MaxValue), Is.True);
            sliced.BeginOpenRebuild();
            int steps = 0;
            while (!sliced.StepOpen(7)) steps++;
            Assert.That(steps, Is.GreaterThan(50), "the rebuild is sliced");

            PlacementList list = ground.Placements;
            var kept = new List<Placement>();
            for (int k = 0; k < list.Items.Length; k++) if (!gone.Contains(k)) kept.Add(list.Items[k]);
            var fresh = new NavGrid(new PlacementList { Layout = list.Layout, Seed = list.Seed, Size = list.Size, Props = list.Props, Items = kept.ToArray() });
            Assert.That(whole.Clearance, Is.EqualTo(fresh.Clearance));
            Assert.That(whole.Open, Is.EqualTo(fresh.Open));
            Assert.That(sliced.Open, Is.EqualTo(fresh.Open));
            Assert.That(whole.BlockedCells, Is.EqualTo(fresh.BlockedCells));
            Assert.That(passable, Is.EqualTo(fresh.PassableFor(0.4f)), "a flow field's passable cells follow the map");
            Assert.That(original.Clearance, Is.EqualTo(before), "the layout's own grid changed");
            Assert.That(whole.BlockedCells, Is.LessThan(original.BlockedCells));
        }

        /// <summary>
        /// The rebuilds a break starts stay sliced: no tick of the walls run does more than a quarter of the grid's
        /// cells of navigation work, as without breaks, and the goal fields of the sections after each break are
        /// rebuilt over the new map and swapped in.
        /// </summary>
        [Test]
        public void RebuildsAfterABreakStaySliced()
        {
            NavGrid grid = Fixtures.Data.Layout.Nav;
            SimTickTiming walls = Fixtures.WallsTimeline.TickTiming, song = Fixtures.SongTimeline.TickTiming;
            TestContext.WriteLine("most navigation work in a tick: " + walls.MaxTickNavCells + " cells with breaks, " + song.MaxTickNavCells + " without, of " + grid.W * grid.H
                + "; " + walls.GoalFieldRebuilds + " goal fields rebuilt");
            Assert.That(walls.MaxTickNavCells, Is.GreaterThan(song.MaxTickNavCells), "the breaks' rebuilds added no work to any tick");
            Assert.That(walls.MaxTickNavCells, Is.LessThan(grid.W * grid.H / 4));
            Assert.That(song.GoalFieldRebuilds, Is.EqualTo(0));
            Assert.That(walls.GoalFieldRebuilds, Is.GreaterThanOrEqualTo(3), "each of the three breaks rebuilds at least its section's goal field");
        }

        /// <summary>
        /// The gap rule's measure (which ScheduleBreaks applies before the run, failing it on a narrow opening) is taken
        /// where each piece stood: one piece out of the middle of a courtyard wall run leaves a slit too narrow for the
        /// boss, while the whole run's opening is wide.
        /// </summary>
        [Test]
        public void OpeningsAreMeasuredWhereEachPieceStood()
        {
            // One piece out of the middle of a courtyard wall run leaves a slit between its neighbours.
            GroundLayout ground = Fixtures.Data.Layout;
            int[] run = StructureBreaks.Resolve(ground, "courtyard/north-west");
            Assume.That(run.Length, Is.GreaterThanOrEqualTo(3));
            NavGrid scratch = ground.Nav.Clone();
            float need = Math.Min(Fixtures.Data.Roster.MaxMonsterRadius + 0.5f, NavGrid.MaxClearance - NavGrid.CellM);
            Assert.That(scratch.Remove(new[] { run[1] })[0], Is.LessThan(need), "a middle piece's slit is narrow");
            Assert.That(ground.Nav.Clone().Remove(run), Is.All.GreaterThanOrEqualTo(need), "the whole run's opening is wide");
        }

        /// <summary>The pieces' footprints alone, as wall geometry.</summary>
        static WallGeometry Only(PlacementList list, List<int> pieces)
        {
            var items = new List<Placement>();
            foreach (int k in pieces) items.Add(list.Items[k]);
            return new WallGeometry(new PlacementList { Layout = list.Layout, Size = list.Size, Props = list.Props, Items = items.ToArray() });
        }
    }
}
