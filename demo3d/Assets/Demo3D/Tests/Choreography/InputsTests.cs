using System;
using System.Numerics;
using NUnit.Framework;

namespace Demo3D.Choreography.Tests
{
    /// <summary>The layer's inputs and helpers: JSON, the song map and grid, retiming, the view, the scripts.</summary>
    public class InputsTests
    {
        [Test]
        public void JsonRoundTripsValuesAndKeepsKeyOrder()
        {
            JsonValue v = JsonValue.Parse("{ \"b\": [1, -2.5e1, true, null], \"a\": \"x\\\"\\u00e9\\n\", \"c\": {} }");
            Assert.That(v.Members[0].Key, Is.EqualTo("b"));
            Assert.That(v["b"][1].AsDouble(), Is.EqualTo(-25.0));
            Assert.That(v["b"][2].AsBool(), Is.True);
            Assert.That(v["b"][3].IsNull, Is.True);
            Assert.That(v["a"].AsString(), Is.EqualTo("x\"é\n"));
            Assert.That(v["missing"].IsNull, Is.True);
            var sw = new System.IO.StringWriter();
            new JsonOut(sw).BeginObject().Field("t", 1.23456789, 6).Field("s", "q\"").Field("z", -0.0000001, 3).EndObject();
            Assert.That(sw.ToString(), Is.EqualTo("{\"t\":1.234568,\"s\":\"q\\\"\",\"z\":0}"));
            Assert.Throws<FormatException>(() => JsonValue.Parse("{\"a\": 1,}"));
        }

        [Test]
        public void ActionSongMapResolvesSectionsOnDownbeats()
        {
            SongMap map = Fixtures.Song;
            Assert.That(map.Beats.Length, Is.EqualTo(514));
            Assert.That(map.Sections.Length, Is.EqualTo(12));
            SongSection drop1 = map.Sections[map.SectionIndexOf("drop-1")];
            Assert.That(drop1.FirstBeat, Is.EqualTo(96));
            Assert.That(drop1.Start, Is.EqualTo(37.1711).Within(1e-9));
            Assert.That(map.Sections[0].Start, Is.EqualTo(0));
            Assert.That(map.Sections[map.Sections.Length - 1].End, Is.EqualTo(map.Duration).Within(1e-9));
            Assert.That(map.Sections[map.SectionIndexOf("ending")].Feel, Is.EqualTo(SectionFeel.Free));
            Assert.That(map.TimeOfBeatIndex(map.BeatIndexAt(100.0)), Is.EqualTo(100.0).Within(1e-9));
            Assert.That(map.TimeOfBeatIndex(-1), Is.EqualTo(map.Beats[0] - (map.Beats[1] - map.Beats[0])).Within(1e-9));
        }

        [Test]
        public void GridHasSubdivisionsInPulsedSectionsAndAccentsInFreeOnes()
        {
            var grid = new BeatGrid(Fixtures.Song);
            SongMap map = grid.Map;
            // A pulsed beat, its eighth and its sixteenths.
            int i = grid.Nearest(map.Beats[40]);
            Assert.That(grid.Points[i].Has(GridFlags.Beat), Is.True);
            Assert.That(grid.Points[i + 2].Has(GridFlags.Eighth), Is.True);
            Assert.That(grid.Points[i + 2].Time, Is.EqualTo((map.Beats[40] + map.Beats[41]) / 2).Within(1e-9));
            // Bar 25's downbeat opens a phrase and carries the drop accent (strength 1).
            GridPoint drop = grid.Points[grid.Nearest(37.1711)];
            Assert.That(drop.Has(GridFlags.Phrase | GridFlags.Downbeat | GridFlags.Accent), Is.True);
            Assert.That(drop.AccentStrength, Is.EqualTo(1f));
            // break-1's grid is its accents and anchors only.
            SongSection brk = map.Sections[map.SectionIndexOf("break-1")];
            int count = 0;
            foreach (GridPoint p in grid.Points)
            {
                if (p.Time < brk.Start || p.Time >= brk.End) continue;
                Assert.That(p.Has(GridFlags.Free | GridFlags.Accent), Is.True);
                count++;
            }
            Assert.That(count, Is.EqualTo(4));   // anchors 34.58, 35.64 (an accent too), 36.44 (an accent too), 37.10
            // An accent 32 ms off its beat is not a grid accent.
            Assert.That(grid.Points[grid.Nearest(4.655)].Has(GridFlags.Accent), Is.False);
        }

        [Test]
        public void RetimingClampsSpeedAndRefusesWhatCannotFit()
        {
            var clip = new ClipAnnotation("g", "c", "x attack", 0.40f, 0.90f, 0, 0);
            Assert.That(ClipLibrary.Fit(clip, 0.0, 1.0, out double start, out double end, out float speed), Is.True);
            Assert.That(speed, Is.EqualTo(0.75f));
            Assert.That(start, Is.EqualTo(1.0 - 0.40 / 0.75).Within(1e-6));
            Assert.That(end, Is.EqualTo(start + 0.90 / 0.75).Within(1e-6));
            Assert.That(ClipLibrary.Fit(clip, 0.699, 1.0, out start, out _, out speed), Is.True);
            Assert.That(speed, Is.EqualTo(0.40f / 0.301f).Within(1e-4));
            Assert.That(start, Is.EqualTo(0.699).Within(1e-6));
            Assert.That(ClipLibrary.Fit(clip, 0.70, 1.0, out _, out _, out _), Is.False);   // would need 1.333x
        }

        [Test]
        public void ViewGeometryMatchesTheCameraTable()
        {
            // camera-and-sound.md: about 690, 1,220 and 2,300 m² for 30, 40 and 55 m.
            Assert.That(new ViewGeometry(30).Area, Is.EqualTo(690f).Within(690 * 0.08f));
            Assert.That(new ViewGeometry(40).Area, Is.EqualTo(1220f).Within(1220 * 0.08f));
            Assert.That(new ViewGeometry(55).Area, Is.EqualTo(2300f).Within(2300 * 0.08f));
            var v = new ViewGeometry(40);
            Assert.That(v.HalfWidthAt(0), Is.EqualTo(20f).Within(1e-3f));
            Assert.That(v.Contains(Vector2.Zero, new Vector2(19.9f, 0)), Is.True);
            Assert.That(v.Contains(Vector2.Zero, new Vector2(0, -v.Near - 0.1f)), Is.False);
            Assert.That(v.OutsideDistance(Vector2.Zero, new Vector2(0, v.Far + 2)), Is.EqualTo(2f).Within(1e-4f));
            for (float u = 0; u < 1; u += 0.05f)
            {
                Vector2 p = v.EdgePoint(Vector2.Zero, u, 2f, out _);
                Assert.That(v.OutsideDistance(Vector2.Zero, p), Is.GreaterThan(1.4f), "u " + u);
            }
            Assert.That(v.PackingCapacity(), Is.EqualTo((int)(v.Area * 2.4f)));
        }

        [Test]
        public void GridEntriesParsePositionsAndRates()
        {
            GridEntry e = GridEntry.Parse("lightning@beat/3");
            Assert.That(e.Name, Is.EqualTo("lightning"));
            Assert.That(e.Position, Is.EqualTo(GridFlags.Beat));
            Assert.That(e.Every, Is.EqualTo(3));
            Assert.That(GridEntry.Parse("slashL").Positioned, Is.False);
            Assert.That(GridEntry.Parse("slam@phrase").Priority, Is.GreaterThan(GridEntry.Parse("x@downbeat").Priority));
            Assert.That(GridEntry.Parse("x@downbeat").Priority, Is.GreaterThan(GridEntry.Parse("x@accent").Priority));
            Assert.That(GridEntry.Parse("x@accent").Priority, Is.GreaterThan(GridEntry.Parse("x@beat").Priority));
            Assert.Throws<FormatException>(() => GridEntry.Parse("x@bar"));
        }

        [Test]
        public void LevelScriptCoversEverySongSectionWithinTheEnvelope()
        {
            LevelScript level = Fixtures.Level;
            foreach (SongSection s in Fixtures.Song.Sections)
            {
                LevelSection ls = level.For(s.Label);
                Assert.That(ls, Is.Not.Null, s.Label);
                Assert.That(ls.ViewWidthM, Is.LessThanOrEqualTo(level.EnvelopeViewWidthM));
                if (ls.Goal != null) Assert.That(Fixtures.Data.Layout.Goal(ls.Goal), Is.Not.Null, ls.Goal);
            }
            // TD12's envelope (evidence/td12/report.md): a count rounded down to 100 at a measured width,
            // 500, which TD13 lowered in 5% steps (25) until the full song passed.
            Assert.That(level.EnvelopeOnScreen, Is.GreaterThan(0).And.LessThanOrEqualTo(500));
            Assert.That((500 - level.EnvelopeOnScreen) % 25, Is.EqualTo(0));
            Assert.That(new[] { 30f, 40f, 55f }, Does.Contain(level.EnvelopeViewWidthM));
        }

        [Test]
        public void EveryRosterTypeAndHeroMoveHasItsAnnotations()
        {
            ChoreographyData d = Fixtures.Data;
            Assert.That(d.Roster.Monsters.Length, Is.EqualTo(9));
            foreach (MonsterType m in d.Roster.Monsters)
            {
                Assert.That(d.Clips[m.AttackClip].Impact, Is.GreaterThan(0f), m.Name);
                Assert.That(d.Clips[m.AttackClip].End, Is.GreaterThan(d.Clips[m.AttackClip].Impact), m.Name);
                Assert.That(d.Clips[m.WalkClip].StrideM, Is.GreaterThan(0f), m.Name);
            }
            foreach (string combo in new[] { "slashL", "slashR", "thrust", "spin", "slam" })
                Assert.That(d.Roster.Combo(combo).Clip, Is.GreaterThanOrEqualTo(0), combo);
            Assert.That(d.Clips.Find("hero walk"), Is.GreaterThanOrEqualTo(0));
        }

        [Test]
        public void StressScriptsOverrideTheSweepAxes()
        {
            StressScript s = Fixtures.StressScript("S3");
            StressScript o = s.With(n: 900, viewWidthM: 30, load: "heavy", seconds: 12);
            Assert.That(o.N, Is.EqualTo(900));
            Assert.That(o.ViewWidthM, Is.EqualTo(30f));
            Assert.That(o.Load, Is.EqualTo("heavy"));
            Assert.That(s.N, Is.EqualTo(1200));
            Assert.That(o.EffectiveSpells().Length, Is.EqualTo(LoadPreset.Heavy.Spells.Length));
            // N never exceeds the view's packing capacity.
            Timeline tl = Choreographer.RunStress(s.With(n: 100000, viewWidthM: 30, seconds: 2), Fixtures.Data);
            Assert.That(tl.Sections[0].TargetOnScreen, Is.EqualTo(new ViewGeometry(30).PackingCapacity()));
        }
    }
}
