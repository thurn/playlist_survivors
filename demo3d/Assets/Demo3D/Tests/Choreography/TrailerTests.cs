using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using NUnit.Framework;
using PS.Choreography;

namespace Demo3D.Choreography.Tests
{
    /// <summary>
    /// `--trailer` (trailer.md): the trailer file's parsing and failures, the level script's shot fields and their
    /// defaults, and the trailer's checks, each run against a shot broken on purpose. The normal timelines' pinned
    /// hashes (<see cref="IntensityTests.NormalTimelinesAreByteIdenticalToBeforeTheFields"/>) prove the fields
    /// leave normal runs alone.
    /// </summary>
    public class TrailerTests
    {
        static string Report(List<string> problems) => TimelineChecks.Report(problems);

        // ------------------------------------------------------------------ the trailer file

        /// <summary>The checked-in trailer: ten shots on the song's grid, from the trailer's start to the cut to black.</summary>
        [Test]
        public void TheTrailerFileParses()
        {
            Trailer t = Fixtures.Trailer;
            Assert.That(t.Shots.Length, Is.EqualTo(10));
            Assert.That(t.Base, Is.EqualTo("overdrive"));
            Assert.That(t.Shots[0].Cut, Is.EqualTo(121.40).Within(1e-9));
            Assert.That(t.BlackAt, Is.EqualTo(Fixtures.Song.TimeOfBarBeat("99.3")));
            Assert.That(t.EndS - t.Shots[0].Cut, Is.EqualTo(32.6).Within(0.05));
            for (int i = 0; i < t.Shots.Length; i++)
            {
                TrailerShot s = t.Shots[i];
                Assert.That(s.Number, Is.EqualTo(i + 1));
                Assert.That(s.End, Is.EqualTo(i + 1 < t.Shots.Length ? t.Shots[i + 1].Cut : t.BlackAt));
                Assert.That(s.SpanFrom, Is.LessThan(s.Cut));
            }
            Assert.That(t.Shots[1].Cut, Is.EqualTo(Fixtures.Song.TimeOfBarBeat("81.1")));
            Assert.That(t.Environments, Is.EquivalentTo(new[] { "crypt", "desert", "snow", "forest" }));
            Assert.That(t.ShotAt(t.Shots[3].Cut - 0.01), Is.EqualTo(2));
            Assert.That(t.ShotAt(t.Shots[3].Cut), Is.EqualTo(3));
        }

        /// <summary>`--environment` pins every shot to one environment.</summary>
        [Test]
        public void TheEnvironmentFlagPinsEveryShot()
        {
            Trailer t = ChoreographyFiles.LoadTrailer(Fixtures.Root, "action", out _, "snow");
            foreach (TrailerShot s in t.Shots) Assert.That(s.Environment, Is.EqualTo("snow"));
        }

        /// <summary>A bad value fails at load, naming it.</summary>
        [TestCase("\"environment\": \"moon\"", "unknown environment 'moon'")]
        [TestCase("\"hero\": { \"at\": \"nowhere\", \"holdGround\": true }", "unknown area 'nowhere'")]
        [TestCase("\"combo\": [\"slashR@sometime\"]", "unknown grid position 'sometime'")]
        [TestCase("\"combo\": [\"slashR@81.9\"]", "bar 81 has 4 beats")]
        [TestCase("\"combo\": [\"slashR@cut\"]", "the first shot has no cut")]
        [TestCase("\"combo\": [\"slashR@final\"]", "the final slam (82.1) is outside the shot")]
        [TestCase("\"combo\": [\"slashR@anchor\", \"spin@anchor\", \"slashL@anchor\", \"slam@anchor\"]", "no free section's anchor is left")]
        [TestCase("\"wobble\": 3", "unknown field 'wobble'")]
        [TestCase("\"combo\": [\"slashR@anchor\"], \"spells\": [\"ring@anchor\", \"bolt@anchor\", \"ring@anchor\", \"ring@anchor\"]", "no free section's anchor is left")]
        public void ABadFirstShotFailsNamingTheValue(string field, string message)
        {
            var ex = Assert.Throws<FormatException>(() => Parse(Shot1(field), Shot2()));
            Assert.That(ex.Message, Does.Contain(message));
        }

        [TestCase("\"cut\": \"80.1\"", "not on the grid")]
        [TestCase("\"cut\": \"200.1\"", "outside the song")]
        [TestCase("\"formation\": { \"kind\": \"spiral\", \"onScreen\": 100, \"mix\": { \"skeleton\": 1 } }", "unknown formation 'spiral'")]
        [TestCase("\"formation\": { \"kind\": \"converge\", \"streams\": 7, \"onScreen\": 100, \"mix\": { \"skeleton\": 1 } }", "2 to 6 streams")]
        [TestCase("\"boss\": \"overhead\"", "unknown area 'overhead'")]
        [TestCase("\"path\": { \"via\": [\"camp\", \"atlantis\"], \"stepM\": 1.9 }", "path waypoint 2: unknown area 'atlantis'")]
        public void ABadLaterShotFailsNamingTheValue(string field, string message)
        {
            var ex = Assert.Throws<FormatException>(() => Parse(Shot1(), Shot2(field)));
            Assert.That(ex.Message, Does.Contain(message));
        }

        [Test]
        public void AFormationWithoutAPathFails()
        {
            string shot = "{ \"cut\": \"81.1\", \"environment\": \"desert\", \"viewWidthM\": 45, \"hero\": { \"at\": \"camp\" },"
                + " \"formation\": { \"kind\": \"charge\", \"onScreen\": 600, \"mix\": { \"skeleton\": 1 } }, \"combo\": [\"slam@cut\"] }";
            var ex = Assert.Throws<FormatException>(() => Parse(Shot1(), shot));
            Assert.That(ex.Message, Does.Contain("shot 2: a formation's hero kites, so it needs a path"));
        }

        /// <summary>The first shot's anchors count from its pre-roll's start, so its pre-roll may hold none.</summary>
        [Test]
        public void AFirstShotWhosePrerollCoversAnAnchorFails()
        {
            string json = Trailer(Shot1(), Shot2()).Replace("\"startS\": 121.40", "\"startS\": 121.70");
            var ex = Assert.Throws<FormatException>(() => PS.Choreography.Trailer.Parse(json, Fixtures.Song, Environments));
            Assert.That(ex.Message, Does.Contain("its pre-roll covers an anchor"));
        }

        [Test]
        public void ATrailerOnAnotherSongFails()
        {
            string json = Trailer(Shot1(), Shot2()).Replace("\"song\": \"action\"", "\"song\": \"orpheus\"");
            var ex = Assert.Throws<FormatException>(() => PS.Choreography.Trailer.Parse(json, Fixtures.Song, Environments));
            Assert.That(ex.Message, Does.Contain("'orpheus'"));
        }

        /// <summary>
        /// A shot's script (trailer.md, "How a shot uses the base"): the base overlay's globals, never its sections;
        /// the shot's combo, spells, horde and view in every section; the shot's fields; and seed plus the shot's number.
        /// </summary>
        [Test]
        public void AShotScriptTakesTheBasesGlobalsAndTheShotsFields()
        {
            Trailer t = Fixtures.Trailer;
            LevelScript ls = t.ShotScript(9, File.ReadAllText(ChoreographyFiles.DataPath(Fixtures.Root, ChoreographyFiles.LevelFile)),
                File.ReadAllText(ChoreographyFiles.DataPath(Fixtures.Root, ChoreographyFiles.IntensityFile("overdrive"))));
            TrailerShot s = t.Shots[9];
            Assert.That(ls.Intensity, Is.EqualTo("overdrive"));
            Assert.That((ls.Tuning.ComboMinSpeed, ls.Tuning.ComboMaxSpeed), Is.EqualTo((PS.Choreography.Trailer.ComboMinSpeed, PS.Choreography.Trailer.ComboMaxSpeed)),
                "the trailer's natural-rate blows, over overdrive's 2-3x");
            Assert.That(ls.Tuning.SoundGainBase, Is.EqualTo(0.22).Within(1e-9), "overdrive's global sound gain");
            Assert.That(ls.ComboOn, Is.EqualTo(GridFlags.Strong), "blows on the strong beats by default");
            Assert.That(ls.Seed, Is.EqualTo(t.Seed + 10));
            Assert.That((ls.SpanFromS, ls.SpanToS), Is.EqualTo((s.SpanFrom, s.End)));
            Assert.That((ls.Cut, ls.Final, ls.Boss), Is.EqualTo(("97.1", "99.1", "ahead")));
            Assert.That((ls.KillBudget, ls.ClearRadiusM, ls.HeroStepS), Is.EqualTo((false, 0f, PS.Choreography.Trailer.DefaultHeroStepS)));
            Assert.That(new[] { ls.Tuning.FodderHitPointScale, ls.Tuning.EliteHitPointScale, ls.Tuning.BossHitPointScale }, Is.All.EqualTo(0f));
            Assert.That((ls.ShapeScale, ls.YawDeg, ls.ViewWidthToM), Is.EqualTo((s.ShapeScale, s.YawDeg, s.ViewWidthToM)));
            Assert.That(ls.EnvelopeOnScreen, Is.EqualTo(s.TargetOnScreen));
            Assert.That((ls.Formation, ls.FormationStreams, ls.DensityCap), Is.EqualTo(("converge", 6, s.DensityCap)));
            Assert.That(ls.EnvelopeViewWidthM, Is.EqualTo(s.ViewWidthToM));
            foreach (LevelSection sec in ls.Sections)
            {
                Assert.That(sec.ViewWidthM, Is.EqualTo(s.ViewWidthM), sec.Section);
                Assert.That(sec.OnScreen, Is.EqualTo(1f), sec.Section);
                Assert.That(Array.ConvertAll(sec.Combo, e => e.ToString()), Is.EqualTo(s.Combo), sec.Section);
                Assert.That(Array.ConvertAll(sec.Spells, e => e.ToString()), Is.EqualTo(s.Spells), sec.Section);
                Assert.That((sec.Goal, sec.Boss, sec.Clear), Is.EqualTo(((string)null, (string)null, false)), sec.Section);
            }
        }

        // ------------------------------------------------------------------ the shot fields

        /// <summary>A level script without the shot fields gets each one's default, under which the sim runs as before.</summary>
        [Test]
        public void ShotFieldsDefaultToNone()
        {
            LevelScript ls = LevelScript.Parse(Script(""));
            Assert.That(double.IsNaN(ls.SpanFromS) && double.IsNaN(ls.SpanToS), Is.True);
            Assert.That((ls.Cut, ls.Final, ls.Boss), Is.EqualTo(((string)null, (string)null, (string)null)));
            Assert.That(ls.HeroAt, Is.Null);
            Assert.That(ls.HeroPath, Is.Null);
            Assert.That(ls.Duel, Is.Null);
            Assert.That(ls.HoldGround, Is.False);
            Assert.That(ls.HeroStepS, Is.LessThan(0), "the roster's step time");
            Assert.That(ls.KillBudget, Is.True);
            Assert.That((ls.ShapeScale, ls.YawDeg, ls.ViewWidthToM), Is.EqualTo((0f, 0f, 0f)));
            Assert.That(ls.Formation, Is.Null, "the song's opening horde, releases and refill");
            Assert.That(ls.ComboOn, Is.EqualTo(GridFlags.Beat), "plain combo entries on every beat");
            foreach (string pos in new[] { "beat", "eighth", "downbeat", "phrase", "accent" })
                Assert.That(GridEntry.Parse("slashL@" + pos).Fixed, Is.EqualTo(FixedPosition.None), pos);
        }

        [Test]
        public void ShotFieldsParse()
        {
            LevelScript ls = LevelScript.Parse(Script("\"spanS\": [100, 110], \"cut\": \"62.1\", \"final\": \"64.1\", \"heroAt\": [3, -4], "
                + "\"heroPath\": { \"via\": [\"camp\", [10, 20]], \"stepM\": 1.9, \"loop\": true }, \"holdGround\": true, \"heroStepS\": 0.12, "
                + "\"killBudget\": false, \"shapeScale\": 2.5, \"yawDeg\": -45, \"viewWidthToM\": 65, \"boss\": \"ahead\", \"duel\": { \"troll\": 1 },"));
            Assert.That((ls.SpanFromS, ls.SpanToS), Is.EqualTo((100.0, 110.0)));
            Assert.That((ls.Cut, ls.Final, ls.Boss), Is.EqualTo(("62.1", "64.1", "ahead")));
            Assert.That(ls.HeroAt.At, Is.EqualTo(new Vector2(3, -4)));
            Assert.That(ls.HeroPath.Via[0].Area, Is.EqualTo("camp"));
            Assert.That(ls.HeroPath.Via[1].At, Is.EqualTo(new Vector2(10, 20)));
            Assert.That((ls.HeroPath.StepM, ls.HeroPath.Loop), Is.EqualTo((1.9f, true)));
            Assert.That((ls.HoldGround, ls.HeroStepS, ls.KillBudget), Is.EqualTo((true, 0.12f, false)));
            Assert.That((ls.ShapeScale, ls.YawDeg, ls.ViewWidthToM), Is.EqualTo((2.5f, -45f, 65f)));
            Assert.That(ls.Duel[0].Type, Is.EqualTo("troll"));
            LevelScript formed = LevelScript.Parse(Script("\"formation\": { \"kind\": \"converge\", \"streams\": 3 }, \"densityCap\": 0.9,"));
            Assert.That((formed.Formation, formed.FormationStreams, formed.DensityCap), Is.EqualTo(("converge", 3, 0.9f)));
            Assert.That(Assert.Throws<FormatException>(() => LevelScript.Parse(Script("\"formation\": { \"kind\": \"spiral\" },"))).Message, Does.Contain("unknown formation 'spiral'"));
            Assert.That(Assert.Throws<FormatException>(() => LevelScript.Parse(Script("\"formation\": { \"kind\": \"converge\", \"streams\": 1 },"))).Message, Does.Contain("2 to 6 streams"));
            Assert.Throws<FormatException>(() => LevelScript.Parse(Script("\"spanS\": [110, 100],")));
            Assert.That(LevelScript.Parse(Script("\"comboOn\": \"downbeat\",")).ComboOn, Is.EqualTo(GridFlags.Downbeat));
            Assert.That(Assert.Throws<FormatException>(() => LevelScript.Parse(Script("\"comboOn\": \"cut\","))).Message, Does.Contain("comboOn must be a grid position"));
            Assert.That(Assert.Throws<FormatException>(() => LevelScript.Parse(Script("\"viewWidthToM\": 65,"))).Message, Does.Contain("needs spanS"));
        }

        /// <summary>The trailer's grid positions rank above the song's, final highest (trailer.md, "How a shot uses the base").</summary>
        [Test]
        public void FixedPositionsOutrankTheGrids()
        {
            string[] order = { "slam@final", "slam@cut", "slam@82.3", "slam@anchor", "slam@phrase", "slam@downbeat", "slam@accent", "slam@beat" };
            for (int i = 1; i < order.Length; i++)
                Assert.That(GridEntry.Parse(order[i - 1]).Priority, Is.GreaterThan(GridEntry.Parse(order[i]).Priority), order[i - 1] + " over " + order[i]);
            GridEntry e = GridEntry.Parse("spin@82.3");
            Assert.That((e.Fixed, e.Bar, e.Beat, e.ToString()), Is.EqualTo((FixedPosition.BarBeat, 82, 3, "spin@82.3")));
        }

        /// <summary>The strong beats are each bar's first and third (4/4's halves), and <c>strong</c> names them in a combo or <c>blowsOn</c>.</summary>
        [Test]
        public void StrongBeatsAreTheDownbeatAndTheMiddleOfTheBar()
        {
            var grid = new BeatGrid(Fixtures.Song);
            for (int beat = 1; beat <= 4; beat++)
            {
                GridPoint p = grid.Points[grid.Nearest(Fixtures.Song.TimeOfBarBeat("85." + beat))];
                Assert.That(p.Has(GridFlags.Strong), Is.EqualTo(beat == 1 || beat == 3), "85." + beat);
            }
            Assert.That(GridEntry.Parse("slam@strong").Position, Is.EqualTo(GridFlags.Strong));
            Assert.That(Assert.Throws<FormatException>(() => Parse(Shot1(), Shot2("\"blowsOn\": \"82.1\""))).Message, Does.Contain("blowsOn must be a grid position"));
            Assert.That(Parse(Shot1(), Shot2("\"blowsOn\": \"downbeat\"")).Shots[1].BlowsOn, Is.EqualTo("downbeat"));
        }

        /// <summary>A heading turns the view: a point just past the far edge is in view of a camera turned a quarter, and the counted area is the same.</summary>
        [Test]
        public void AHeadingTurnsTheView()
        {
            var straight = new ViewGeometry(40);
            var east = new ViewGeometry(40, ViewGeometry.PitchDeg, 90);
            Vector2 focus = new Vector2(5, 5);
            Vector2 farAhead = focus + new Vector2(0, straight.Far + 1);   // past the far edge, but within half the width to a camera looking along +X
            Assert.That(straight.Contains(focus, farAhead), Is.False);
            Assert.That(east.Contains(focus, farAhead), Is.True);
            Assert.That(east.Area, Is.EqualTo(straight.Area));
            // Edge points turn with the view: the same perimeter point lies as far outside either.
            Assert.That(east.OutsideDistance(focus, east.EdgePoint(focus, 0.3f, 2f, out _)),
                Is.EqualTo(straight.OutsideDistance(focus, straight.EdgePoint(focus, 0.3f, 2f, out _))).Within(1e-3));
            Assert.That(new ViewGeometry(40, ViewGeometry.PitchDeg, 0).Local(focus, farAhead), Is.EqualTo(farAhead - focus));
        }

        // ------------------------------------------------------------------ the trailer's checks

        /// <summary>
        /// The checked-in trailer passes every check in every shot (`trailer --checks`), monster liveness and density
        /// included. The formations hold them with room: other trailer seeds and both roster sets pass too.
        /// </summary>
        [Test]
        public void EveryShotPassesTheTrailersChecks()
        {
            var problems = new List<string>();
            for (int i = 0; i < Fixtures.Trailer.Shots.Length; i++)
            {
                foreach (KeyValuePair<string, List<string>> check in TrailerChecks.All(Fixtures.Trailer, i, Fixtures.Shot(i), Fixtures.ShotData(i)))
                    problems.AddRange(check.Value);
                ShotReport r = TrailerChecks.Report(Fixtures.Trailer, i, Fixtures.Shot(i), Fixtures.ShotData(i));
                TestContext.WriteLine(TrailerChecks.Table(new[] { r }));
            }
            Assert.That(problems, Is.Empty, Report(problems));
        }

        /// <summary>The duel's hero strikes only on the anchors, and each blow fells one brute.</summary>
        [Test]
        public void TheDuelFellsABruteOnEachAnchor()
        {
            Timeline tl = Fixtures.Shot(0);
            var blows = new List<double>();
            foreach (ActionEvent a in tl.ActionsOf(0)) if (a.Kind == ActionKind.Attack) blows.Add(a.Impact);
            var anchors = new List<double>();
            foreach (TimedStrength a in Fixtures.Song.Sections[Fixtures.Song.SectionIndexOf("break-2")].Anchors) anchors.Add(a.Time);
            Assert.That(blows, Is.EqualTo(anchors));
            foreach (double t in anchors)
            {
                int deaths = 0;
                foreach (ActionEvent a in tl.Actions) if (a.Kind == ActionKind.Death && Math.Abs(a.Impact - t) < 1e-6) deaths++;
                Assert.That(deaths, Is.EqualTo(1), "deaths at " + t);
            }
        }

        /// <summary>
        /// Every combo entry at an explicit bar.beat lands on it: a flurry's blow just before a spin's windup doesn't
        /// cost the hero the spin (shot 2's on 82.3 and 84.3).
        /// </summary>
        [Test]
        public void EveryBarBeatBlowLands()
        {
            var missing = new List<string>();
            for (int i = 0; i < Fixtures.Trailer.Shots.Length; i++)
            {
                TrailerShot s = Fixtures.Trailer.Shots[i];
                var impacts = new List<double>();
                foreach (ActionEvent a in Fixtures.Shot(i).ActionsOf(0)) if (a.Kind == ActionKind.Attack) impacts.Add(a.Impact);
                foreach (string entry in s.Combo)
                {
                    int at = entry.IndexOf('@');
                    if (at < 0 || !char.IsDigit(entry[at + 1])) continue;
                    double t = Fixtures.Song.TimeOfBarBeat(entry.Substring(at + 1));
                    if (!impacts.Exists(x => Math.Abs(x - t) < 1e-6)) missing.Add("shot " + s.Number + ": " + entry);
                }
            }
            Assert.That(missing, Is.Empty);
        }

        /// <summary>The final slam's dead are down before the cut to black: each death clip ends within the aftermath.</summary>
        [Test]
        public void TheFinalSlamsDeadFallBeforeTheCutToBlack()
        {
            int last = Fixtures.Trailer.Shots.Length - 1;
            double final = Fixtures.Trailer.FinalSlamTime(Fixtures.Song);
            ClipLibrary clips = Fixtures.ShotData(last).Clips;
            int dead = 0;
            foreach (ActionEvent a in Fixtures.Shot(last).Actions)
            {
                if (a.Kind != ActionKind.Death || Math.Abs(a.Impact - final) > 1e-6) continue;
                dead++;
                double start = a.HasClipTiming ? a.ClipStart : a.Impact - clips[a.Clip].Impact;
                float speed = a.HasClipTiming ? a.ClipSpeed : 1f;
                Assert.That(start + clips[a.Clip].End / speed, Is.LessThan(Fixtures.Trailer.BlackAt), "entity " + a.Entity);
            }
            Assert.That(dead, Is.GreaterThan(1000));
        }

        [Test]
        public void CutsOffTheGridFail()
        {
            TrailerShot s = Copy(1);
            s.Cut += 0.05;
            Assert.That(TrailerChecks.CutOnTheGrid(s, Fixtures.Shot(1)), Has.Count.EqualTo(1));
        }

        [Test]
        public void AShotWithoutItsOpeningBlowFails()
        {
            TrailerShot s = Copy(1);
            s.Cut = Fixtures.Song.Beats[(int)Math.Round(Fixtures.Song.BeatIndexAt(s.Cut)) + 1] + 0.01;   // no blow lands there
            List<string> problems = TrailerChecks.OpeningBlow(s, Fixtures.Shot(1));
            Assert.That(problems, Has.Count.EqualTo(1));
            Assert.That(problems[0], Does.Contain("shot 2"));
        }

        [Test]
        public void AHeroThatJumpsFails()
        {
            Timeline tl = Clone(Fixtures.Shot(1));
            int i = tl.SegmentStart[0] + (tl.SegmentStart[1] - tl.SegmentStart[0]) / 2;
            MotionSegment g = tl.Segments[i];
            tl.Segments[i] = new MotionSegment(0, g.T0, g.T1, g.From + new Vector2(1, 0), g.To + new Vector2(1, 0), g.FacingFrom, g.FacingTo, g.Ease);
            List<string> problems = TrailerChecks.HeroMovesContinuously(Fixtures.Trailer.Shots[1], tl);
            Assert.That(problems, Has.Count.EqualTo(2), Report(problems));   // onto the moved segment, and off it
            Assert.That(problems[0], Does.Contain("the hero jumps"));
        }

        [Test]
        public void AnImpactOffTheGridFails()
        {
            Timeline tl = Clone(Fixtures.Shot(1));
            int i = FirstAction(tl, 0, ActionKind.Attack);
            ActionEvent a = tl.Actions[i];
            tl.Actions[i] = new ActionEvent(a.Entity, a.Kind, a.Impact + 0.01, a.Clip, a.Target, a.Strength, a.ClipStart, a.ClipSpeed);
            Assert.That(TrailerChecks.ImpactsOnTheGrid(Fixtures.Trailer.Shots[1], tl), Has.Count.EqualTo(1));
        }

        /// <summary>A hero that stands and never swings fails in every beat interval.</summary>
        [Test]
        public void AHeroThatStandsAroundFails()
        {
            Timeline tl = Clone(Fixtures.Shot(1));
            for (int i = tl.SegmentStart[0]; i < tl.SegmentStart[1]; i++)
            {
                MotionSegment g = tl.Segments[i];
                Vector2 spot = tl.Segments[tl.SegmentStart[0]].From;
                tl.Segments[i] = new MotionSegment(0, g.T0, g.T1, spot, spot, g.FacingFrom, g.FacingTo, g.Ease);
            }
            for (int i = tl.ActionStart[0]; i < tl.ActionStart[1]; i++)
            {
                ActionEvent a = tl.Actions[i];
                if (a.Kind == ActionKind.Attack) tl.Actions[i] = new ActionEvent(0, ActionKind.Parry, a.Impact, -1, -1, a.Strength);
            }
            List<string> problems = TrailerChecks.HeroActsEveryBeat(Fixtures.Trailer, Fixtures.Trailer.Shots[1], tl);
            Assert.That(problems, Has.Count.EqualTo(16), Report(problems));   // four bars of beats
            Assert.That(problems[0], Does.Contain("neither strides nor attacks"));
        }

        /// <summary>A monster frozen in view fails in every beat interval it neither moves nor plays a clip.</summary>
        [Test]
        public void AMonsterThatStandsAroundFails()
        {
            Timeline tl = Clone(Fixtures.Shot(1));
            TrailerShot s = Fixtures.Trailer.Shots[1];
            int m = -1;
            for (int e = 1; e < tl.EntityCount && m < 0; e++)
            {
                // A monster on screen at the cut, alive to the end, whose only action is its spawn (no clip ever excuses it).
                if (tl.Entities[e].Kind != EntityKind.Monster || tl.ActionsOf(e).Count != 1 || tl.Entities[e].Despawn < s.End) continue;
                FramingSample f = tl.FramingAt(s.Cut);
                if (tl.View(f.ViewWidth).Contains(f.Focus, tl.PositionAt(e, s.Cut), -3)) m = e;
            }
            Assert.That(m, Is.GreaterThan(0), "a monster to freeze");
            Assert.That(TrailerChecks.MonstersLive(s, tl, Fixtures.ShotData(1)), Is.Empty, "before freezing");
            Vector2 spot = tl.PositionAt(m, s.Cut);
            for (int i = tl.SegmentStart[m]; i < tl.SegmentStart[m + 1]; i++)
            {
                MotionSegment g = tl.Segments[i];
                tl.Segments[i] = new MotionSegment(m, g.T0, g.T1, spot, spot, g.FacingFrom, g.FacingTo, g.Ease);
            }
            List<string> problems = TrailerChecks.MonstersLive(s, tl, Fixtures.ShotData(1));
            Assert.That(problems, Is.Not.Empty);
            Assert.That(problems, Has.All.Contain("monster " + m + " ").And.All.Contain("neither moves nor plays a clip"), Report(problems));
            Assert.That(problems[0], Does.Contain(s.Cut.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)), "from the cut's interval");
        }

        [Test]
        public void AHitThatDoesntKillFails()
        {
            Timeline tl = Clone(Fixtures.Shot(1));
            int i = FirstAction(tl, -1, ActionKind.Death);
            ActionEvent a = tl.Actions[i];
            tl.Actions[i] = new ActionEvent(a.Entity, ActionKind.Hit, a.Impact, a.Clip, a.Target, a.Strength);
            List<string> problems = TrailerChecks.OneHitKills(Fixtures.Trailer.Shots[1], tl);
            Assert.That(problems, Has.Count.EqualTo(1));
            Assert.That(problems[0], Does.Contain("monster " + a.Entity));
        }

        /// <summary>The boss must take exactly one hit, the final slam's; a blow before it fails, and so does a shot with no boss rule but a boss.</summary>
        [Test]
        public void ABossHitBeforeTheFinalSlamFails()
        {
            Trailer t = Fixtures.Trailer;
            Timeline tl = Clone(Fixtures.Shot(9));
            Roster roster = Fixtures.ShotData(9).Roster;
            Assert.That(TrailerChecks.Boss(t, t.Shots[9], tl, roster), Is.Empty);
            int boss = -1;
            for (int e = 1; e < tl.EntityCount; e++) if (tl.Entities[e].Kind == EntityKind.Monster && roster.Monsters[tl.Entities[e].Type].Rank == MonsterRank.Boss) boss = e;
            int i = FirstAction(tl, boss, ActionKind.Spawn);
            ActionEvent a = tl.Actions[i];
            tl.Actions[i] = new ActionEvent(boss, ActionKind.Hit, a.Impact, -1, -1, 1f);
            Assert.That(TrailerChecks.Boss(t, t.Shots[9], tl, roster), Has.Count.EqualTo(1));
            TrailerShot noRule = Copy(9);
            noRule.Boss = null;
            Assert.That(TrailerChecks.Boss(t, noRule, Fixtures.Shot(9), roster), Has.Count.EqualTo(1));
        }

        [Test]
        public void AMonsterInViewSurvivingTheFinalSlamFails()
        {
            Trailer t = Fixtures.Trailer;
            Timeline tl = Clone(Fixtures.Shot(9));
            double final = t.FinalSlamTime(tl.Map);
            FramingSample f = tl.FramingAt(final);
            int i = -1;
            for (int k = 0; k < tl.Actions.Length && i < 0; k++)
            {
                ActionEvent d = tl.Actions[k];
                if (d.Kind == ActionKind.Death && Math.Abs(d.Impact - final) < 1e-6 && tl.View(f.ViewWidth).Contains(f.Focus, tl.PositionAt(d.Entity, final))) i = k;
            }
            ActionEvent a = tl.Actions[i];
            tl.Actions[i] = new ActionEvent(a.Entity, ActionKind.Spawn, a.Impact, -1, -1, 0);   // no death at all
            List<string> problems = TrailerChecks.FinalSlam(t, t.Shots[9], tl);
            Assert.That(problems, Has.Count.EqualTo(1));
            Assert.That(problems[0], Does.Contain("monster " + a.Entity));
        }

        [Test]
        public void ASpawnInViewFails()
        {
            Timeline tl = Clone(Fixtures.Shot(1));
            TrailerShot s = Fixtures.Trailer.Shots[1];
            int m = -1;
            for (int e = 1; e < tl.EntityCount && m < 0; e++) if (tl.Entities[e].Kind == EntityKind.Monster && tl.Entities[e].Spawn > s.Cut && tl.Entities[e].Spawn < s.End) m = e;
            Assert.That(m, Is.GreaterThan(0), "a monster spawns in the visible span");
            double t = tl.Entities[m].Spawn;
            Vector2 focus = tl.FramingAt(t).Focus;
            int first = tl.SegmentStart[m];
            MotionSegment seg = tl.Segments[first];
            tl.Segments[first] = new MotionSegment(m, seg.T0, seg.T1, focus, focus, seg.FacingFrom, seg.FacingTo, seg.Ease);
            List<string> problems = TrailerChecks.NoVisibleSpawns(s, tl);
            Assert.That(problems, Has.Count.EqualTo(1));
            Assert.That(problems[0], Does.Contain("monster " + m));
        }

        [Test]
        public void AShotFarOffItsTargetFails()
        {
            TrailerShot s = Copy(1);
            s.Formation = new TrailerFormation { Kind = "charge", OnScreen = 2 * s.Formation.OnScreen, Mix = s.Formation.Mix };
            List<string> problems = TrailerChecks.Density(Fixtures.Trailer, s, Fixtures.Shot(1));
            Assert.That(problems, Has.Count.EqualTo(4), Report(problems));   // each of its four bars
            Assert.That(TrailerChecks.Density(Fixtures.Trailer, Fixtures.Trailer.Shots[0], Fixtures.Shot(0)), Is.Empty, "the duel is exempt");
        }

        /// <summary>A horde whose types are dealt at random, as the trailer's first cut drew them, fails the type blocks.</summary>
        [Test]
        public void AConfettiHordeFailsTheTypeBlocks()
        {
            Timeline tl = Clone(Fixtures.Shot(1));
            TrailerShot s = Fixtures.Trailer.Shots[1];
            Assert.That(TrailerChecks.TypeBlocks(Fixtures.Trailer, s, tl), Is.Empty, "before dealing");
            Roster roster = Fixtures.ShotData(1).Roster;
            var types = new List<int>();
            foreach (MixEntry e in s.Formation.Mix)
                for (int k = 0; k < Math.Round(e.Weight); k++) types.Add(roster.MonsterIndex(e.Type));
            var rng = new Rng(3);
            tl.Entities = (EntityInfo[])tl.Entities.Clone();
            for (int e = 1; e < tl.EntityCount; e++)
            {
                EntityInfo info = tl.Entities[e];
                if (info.Kind == EntityKind.Monster) tl.Entities[e] = new EntityInfo(info.Id, types[rng.Next(types.Count)], info.Kind, info.Spawn, info.Despawn, info.Seed);
            }
            List<string> problems = TrailerChecks.TypeBlocks(Fixtures.Trailer, s, tl);
            Assert.That(problems, Has.Count.EqualTo(1), Report(problems));
            Assert.That(problems[0], Does.Contain("shot 2").And.Contain("share its type"));
        }

        /// <summary>A blow moved onto a weak beat fails twice: off the major beats, and a beat after the last.</summary>
        [Test]
        public void ABlowOffTheMajorBeatsFails()
        {
            Timeline tl = Clone(Fixtures.Shot(1));
            TrailerShot s = Fixtures.Trailer.Shots[1];
            Assert.That(TrailerChecks.BlowsOnMajorBeats(Fixtures.Trailer, s, tl), Is.Empty, "before moving");
            int i = FirstAction(tl, 0, ActionKind.Attack);
            while (tl.Actions[i].Impact < s.Cut - 1e-6) i++;   // the cut's blow
            ActionEvent a = tl.Actions[i + 1];
            double weak = Fixtures.Song.Beats[(int)Math.Round(Fixtures.Song.BeatIndexAt(s.Cut)) + 1];   // 81.2
            tl.Actions[i + 1] = new ActionEvent(a.Entity, a.Kind, weak, a.Clip, a.Target, a.Strength, a.ClipStart, a.ClipSpeed);
            List<string> problems = TrailerChecks.BlowsOnMajorBeats(Fixtures.Trailer, s, tl);
            Assert.That(problems, Has.Count.EqualTo(2), Report(problems));   // off the beats, and a beat after the cut's
            Assert.That(problems[0], Does.Contain("off the major beats"));
            Assert.That(problems[1], Does.Contain("1.0 beats after the last"));
        }

        /// <summary>A ring of monsters closed round the hero on a beat fails the open lane on that beat.</summary>
        [Test]
        public void AHeroHemmedInFails()
        {
            Timeline tl = Clone(Fixtures.Shot(1));
            TrailerShot s = Fixtures.Trailer.Shots[1];
            Roster roster = Fixtures.ShotData(1).Roster;
            Assert.That(TrailerChecks.OpenLane(Fixtures.Trailer, s, tl, roster), Is.Empty, "before closing in");
            double t = Fixtures.Song.TimeOfBarBeat("83.2");
            Vector2 h = tl.PositionAt(0, t);
            // Twenty monsters alive then, each stood on a 2 m ring round the hero for the beat.
            int placed = 0;
            for (int e = 1; e < tl.EntityCount && placed < 20; e++)
            {
                if (tl.Entities[e].Kind != EntityKind.Monster || !tl.Exists(e, t + 0.5) || tl.PositionAt(e, t) == tl.PositionAt(e, t + 0.5)) continue;
                double angle = 2 * Math.PI * placed++ / 20;
                Vector2 spot = h + 2f * new Vector2((float)Math.Cos(angle), (float)Math.Sin(angle));
                for (int k = tl.SegmentStart[e]; k < tl.SegmentStart[e + 1]; k++)
                {
                    MotionSegment g = tl.Segments[k];
                    if (g.T0 <= t + 1e-6 && g.T1 > t + 1e-6) tl.Segments[k] = new MotionSegment(e, g.T0, g.T1, spot, spot, g.FacingFrom, g.FacingTo, g.Ease);
                }
            }
            List<string> problems = TrailerChecks.OpenLane(Fixtures.Trailer, s, tl, roster);
            Assert.That(problems, Has.Count.EqualTo(1), Report(problems));
            Assert.That(problems[0], Does.Contain(t.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)).And.Contain("hemmed in"));
        }

        // ------------------------------------------------------------------ helpers

        static Trailer Parse(params string[] shots) => PS.Choreography.Trailer.Parse(Trailer(shots), Fixtures.Song, Environments);

        static GroundLayout Environments(string env) => ChoreographyFiles.LoadEnvironmentData(Fixtures.Root, env)?.Layout;

        static string Trailer(params string[] shots) =>
            "{ \"name\": \"test\", \"song\": \"action\", \"base\": \"overdrive\", \"startS\": 121.40, \"blackAt\": \"82.3\", \"fadeOutS\": 1.5, \"seed\": 7, \"shots\": ["
            + string.Join(", ", shots) + "] }";

        /// <summary>A duel in the crypt, with <paramref name="field"/> added (a later key replaces an earlier one).</summary>
        static string Shot1(string field = null) => Merge("{ \"cut\": \"start\", \"environment\": \"crypt\", \"viewWidthM\": 20, \"prerollBeats\": 4,"
            + " \"hero\": { \"at\": \"camp\", \"holdGround\": true }, \"duel\": { \"bigork\": 2, \"troll\": 1 },"
            + " \"combo\": [\"slashR@anchor\", \"spin@anchor\", \"slashL@anchor\"] }", field);

        /// <summary>A charge in the desert from the drop, with <paramref name="field"/> added.</summary>
        static string Shot2(string field = null) => Merge("{ \"cut\": \"81.1\", \"environment\": \"desert\", \"viewWidthM\": 45,"
            + " \"path\": { \"via\": [[16, 12], [70, 58]], \"stepM\": 1.9 },"
            + " \"formation\": { \"kind\": \"charge\", \"onScreen\": 600, \"mix\": { \"skeleton\": 4, \"goblin\": 3 } },"
            + " \"combo\": [\"slam@cut\", \"slashL\", \"slashR\"] }", field);

        /// <summary><paramref name="shot"/> with <paramref name="field"/> (a "key": value) replacing its key's value, or added.</summary>
        static string Merge(string shot, string field)
        {
            if (field == null) return shot;
            JsonValue o = JsonValue.Parse(shot), f = JsonValue.Parse("{" + field + "}");
            var parts = new List<string>();
            foreach (KeyValuePair<string, JsonValue> kv in o.Members)
                if (!f.Has(kv.Key)) parts.Add("\"" + kv.Key + "\": " + Json(kv.Value));
            parts.Add(field);
            return "{ " + string.Join(", ", parts) + " }";
        }

        static string Json(JsonValue v)
        {
            switch (v.Type)
            {
                case JsonType.String: return "\"" + v.String + "\"";
                case JsonType.Number: return v.Number.ToString(System.Globalization.CultureInfo.InvariantCulture);
                case JsonType.Bool: return v.Bool ? "true" : "false";
                case JsonType.Array: return "[" + string.Join(", ", v.Items.ConvertAll(Json)) + "]";
                case JsonType.Object:
                    var parts = new List<string>();
                    foreach (KeyValuePair<string, JsonValue> kv in v.Members) parts.Add("\"" + kv.Key + "\": " + Json(kv.Value));
                    return "{ " + string.Join(", ", parts) + " }";
                default: return "null";
            }
        }

        static string Script(string fields) =>
            "{ \"song\": \"action\", \"layout\": \"arena\", " + fields + " \"envelope\": { \"onScreen\": 100, \"viewWidthM\": 30 }, \"sections\": ["
            + " { \"section\": \"quiet\", \"onScreen\": 0.1, \"load\": \"light\", \"combo\": [\"slashL\"], \"spells\": [] } ] }";

        /// <summary>A copy of a checked-in shot, to break.</summary>
        static TrailerShot Copy(int index)
        {
            TrailerShot s = Fixtures.Trailer.Shots[index];
            return new TrailerShot
            {
                Number = s.Number, CutAt = s.CutAt, Cut = s.Cut, End = s.End, SpanFrom = s.SpanFrom, Environment = s.Environment,
                ViewWidthM = s.ViewWidthM, ViewWidthToM = s.ViewWidthToM, YawDeg = s.YawDeg, HeroAt = s.HeroAt, HoldGround = s.HoldGround,
                Path = s.Path, Formation = s.Formation, Duel = s.Duel, Combo = s.Combo, Spells = s.Spells, Boss = s.Boss, ShapeScale = s.ShapeScale,
            };
        }

        /// <summary>A copy of a timeline whose actions and segments can be broken without touching the shared one.</summary>
        static Timeline Clone(Timeline tl)
        {
            var c = (Timeline)typeof(Timeline).GetMethod("MemberwiseClone", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(tl, null);
            c.Actions = (ActionEvent[])tl.Actions.Clone();
            c.Segments = (MotionSegment[])tl.Segments.Clone();
            return c;
        }

        /// <summary>The index of the first action of <paramref name="kind"/> (of <paramref name="entity"/>, or any monster's for -1).</summary>
        static int FirstAction(Timeline tl, int entity, ActionKind kind)
        {
            for (int i = 0; i < tl.Actions.Length; i++)
            {
                ActionEvent a = tl.Actions[i];
                if (a.Kind != kind) continue;
                if (entity >= 0 ? a.Entity == entity : tl.Entities[a.Entity].Kind == EntityKind.Monster) return i;
            }
            throw new InvalidOperationException("no " + kind);
        }
    }
}
