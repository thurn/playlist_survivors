using System;
using System.Collections.Generic;

namespace PS.Choreography
{
    /// <summary>A shot's formation (trailer.md, "Kiting and formations"): its kind, streams, on-screen target and mix.</summary>
    public sealed class TrailerFormation
    {
        public string Kind;          // charge, vortex or converge
        public int Streams;          // converge: 2 to 6
        public int OnScreen;
        public MixEntry[] Mix;
    }

    /// <summary>A shot's colour-grade offsets over its environment's base (trailer.md, "Look"), in URP's units.</summary>
    public sealed class TrailerGrade
    {
        public float Exposure, Contrast, Saturation, Temperature;
    }

    /// <summary>One shot of a trailer file: a continuous stretch of picture between two cuts, simulated on its own.</summary>
    public sealed class TrailerShot
    {
        public int Number;                 // from 1
        public string CutAt;               // "start" (shot 1) or a bar.beat
        /// <summary>Song seconds: the shot's first frame (shot 1's is the trailer's start), its end (the next cut, or the cut to black) and its pre-roll's start.</summary>
        public double Cut, End, SpanFrom;
        public string Environment;
        public float ViewWidthM, ViewWidthToM, YawDeg;
        public LayoutPoint HeroAt;
        public bool HoldGround;
        public float HeroStepS = Trailer.DefaultHeroStepS;
        public HeroPath Path;
        public TrailerFormation Formation;
        public MixEntry[] Duel;
        public string[] Combo = new string[0], Spells = new string[0];
        public string Boss;
        public float ShapeScale = 1f, DensityCap = Trailer.DefaultDensityCap;
        public TrailerGrade Grade = new TrailerGrade();
        public int PrerollBeats = Trailer.DefaultPrerollBeats;

        /// <summary>The live monsters in view the shot aims for: its formation's, or its duel's cast.</summary>
        public int TargetOnScreen
        {
            get
            {
                if (Formation != null) return Formation.OnScreen;
                int n = 0;
                if (Duel != null) foreach (MixEntry e in Duel) n += (int)Math.Round(e.Weight);
                return n;
            }
        }

        /// <summary>Does any combo entry fire on the final slam?</summary>
        public bool HasFinal
        {
            get
            {
                foreach (string c in Combo) if (GridEntry.Parse(c).Fixed == FixedPosition.Final) return true;
                return false;
            }
        }
    }

    /// <summary>
    /// A trailer file (trailer.md, "The trailer file"): a music window, the base overlay every shot's script starts
    /// from, and the shots, each cut on the song map's grid. <see cref="Parse"/> validates it against the song map
    /// and the environments; <see cref="ShotScript"/> builds a shot's level script, which <see cref="Choreographer.Run"/>
    /// simulates over the shot's span.
    /// </summary>
    public sealed class Trailer
    {
        public const string StartCut = "start";
        public const float DefaultHeroStepS = 0.14f, DefaultDensityCap = 1.2f;
        public const int DefaultPrerollBeats = 16;
        /// <summary>A shot's off-screen reserve, as a fraction of its target: the kiting refill's starting buffer.</summary>
        public const double ShotReserve = 0.5;
        static readonly string[] Formations = { "charge", "vortex", "converge" };
        static readonly string[] BossSides = { "ahead", "behind", "left", "right" };

        public string Name, Song, Base;
        public double StartS, FadeOutS;
        public string BlackAtPos;
        public double BlackAt;
        public uint Seed;
        public TrailerShot[] Shots;

        /// <summary>The song second of silence: the cut to black plus the fade.</summary>
        public double EndS => BlackAt + FadeOutS;

        /// <summary>The environments the shots use, in first use.</summary>
        public string[] Environments
        {
            get
            {
                var list = new List<string>();
                foreach (TrailerShot s in Shots) if (!list.Contains(s.Environment)) list.Add(s.Environment);
                return list.ToArray();
            }
        }

        /// <summary>The index of the shot on screen at song time <paramref name="t"/>: the last whose cut is at or before it.</summary>
        public int ShotAt(double t)
        {
            for (int i = Shots.Length - 1; i > 0; i--) if (t >= Shots[i].Cut - 1e-9) return i;
            return 0;
        }

        /// <summary>
        /// The trailer from its JSON, validated: every position on <paramref name="map"/>'s grid, every environment known
        /// to <paramref name="environment"/> (its layout, or null for an unknown name), and every area in that layout.
        /// <paramref name="pinEnvironment"/> (the player's <c>--environment</c>) plays every shot in one environment.
        /// A bad value fails, naming it.
        /// </summary>
        public static Trailer Parse(string json, SongMap map, Func<string, GroundLayout> environment, string pinEnvironment = null)
        {
            JsonValue root = JsonValue.Parse(json);
            CheckKeys(root, "trailer", "name", "song", "base", "startS", "blackAt", "fadeOutS", "seed", "shots", "note");
            var grid = new BeatGrid(map);
            var tr = new Trailer
            {
                Name = root["name"].AsString(),
                Song = root["song"].AsString(),
                Base = root["base"].AsString(),
                StartS = root["startS"].AsDouble(double.NaN),
                BlackAtPos = root["blackAt"].AsString(),
                FadeOutS = root["fadeOutS"].AsDouble(0),
                Seed = (uint)root["seed"].AsDouble(1),
            };
            if (tr.Song != map.Song) throw new FormatException("trailer '" + tr.Name + "' is cut to song '" + tr.Song + "', not '" + map.Song + "'");
            if (double.IsNaN(tr.StartS)) throw new FormatException("trailer: needs startS");
            tr.BlackAt = map.TimeOfBarBeat(tr.BlackAtPos);
            if (!(tr.BlackAt > tr.StartS)) throw new FormatException("trailer: blackAt " + tr.BlackAtPos + " is not after startS");
            if (!(tr.FadeOutS >= 0)) throw new FormatException("trailer: fadeOutS must not be negative");
            JsonValue shots = root["shots"];
            if (shots.Count == 0) throw new FormatException("trailer: no shots");
            tr.Shots = new TrailerShot[shots.Count];
            for (int i = 0; i < shots.Count; i++) tr.Shots[i] = ParseShot(shots[i], i + 1, tr, map, grid, environment, pinEnvironment);
            for (int i = 0; i < tr.Shots.Length; i++)
            {
                TrailerShot s = tr.Shots[i];
                s.End = i + 1 < tr.Shots.Length ? tr.Shots[i + 1].Cut : tr.BlackAt;
                if (!(s.End > s.Cut)) throw new FormatException("shot " + s.Number + ": its cut " + s.CutAt + " is not before the next cut");
                s.SpanFrom = Math.Max(0, map.TimeOfBeatIndex(map.BeatIndexAt(s.Cut) - s.PrerollBeats));
                ValidateEntries(s, tr, map, grid);
            }
            return tr;
        }

        static TrailerShot ParseShot(JsonValue o, int n, Trailer tr, SongMap map, BeatGrid grid, Func<string, GroundLayout> environment, string pin)
        {
            string what = "shot " + n;
            CheckKeys(o, what, "cut", "environment", "viewWidthM", "viewWidthToM", "yawDeg", "hero", "path", "formation", "duel", "combo", "spells",
                "boss", "shapeScale", "densityCap", "grade", "prerollBeats", "note");
            var s = new TrailerShot
            {
                Number = n,
                CutAt = o["cut"].AsString(),
                Environment = pin ?? o["environment"].AsString(),
                ViewWidthM = o["viewWidthM"].AsFloat(0f),
                ViewWidthToM = o["viewWidthToM"].AsFloat(0f),
                YawDeg = o["yawDeg"].AsFloat(0f),
                Boss = o["boss"].AsString(),
                ShapeScale = o["shapeScale"].AsFloat(1f),
                DensityCap = o["densityCap"].AsFloat(DefaultDensityCap),
                PrerollBeats = o["prerollBeats"].AsInt(DefaultPrerollBeats),
            };
            if (s.CutAt == null) throw new FormatException(what + ": needs a cut");
            if (n == 1)
            {
                if (s.CutAt != StartCut) throw new FormatException(what + ": the first shot's cut must be '" + StartCut + "', not '" + s.CutAt + "'");
                s.Cut = tr.StartS;
            }
            else
            {
                if (s.CutAt == StartCut) throw new FormatException(what + ": only the first shot's cut is '" + StartCut + "'");
                s.Cut = map.TimeOfBarBeat(s.CutAt, grid);
            }
            if (s.Environment == null) throw new FormatException(what + ": needs an environment");
            GroundLayout layout = environment(s.Environment);
            if (layout == null) throw new FormatException(what + ": unknown environment '" + s.Environment + "'");
            if (!(s.ViewWidthM > 0) || s.ViewWidthToM < 0) throw new FormatException(what + ": needs a positive viewWidthM");
            if (!(s.ShapeScale > 0) || !(s.DensityCap > 0) || s.PrerollBeats < 0) throw new FormatException(what + ": shapeScale and densityCap must be positive, prerollBeats not negative");
            JsonValue hero = o["hero"];
            if (!hero.IsNull)
            {
                CheckKeys(hero, what + " hero", "at", "holdGround", "stepS");
                if (hero.Has("at")) s.HeroAt = LayoutPoint.Parse(hero["at"], what + " hero");
                s.HoldGround = hero["holdGround"].AsBool(false);
                s.HeroStepS = hero["stepS"].AsFloat(DefaultHeroStepS);
            }
            if (o.Has("path"))
            {
                CheckKeys(o["path"], what + " path", "via", "stepM", "loop");
                s.Path = HeroPath.Parse(o["path"]);
            }
            if (s.HoldGround && s.Path != null) throw new FormatException(what + ": a hero holding ground has no path");
            if (s.HeroAt == null && s.Path == null) throw new FormatException(what + ": needs a path or the hero's start (hero.at)");
            // Every area must be one of the environment's.
            s.HeroAt?.Resolve(layout, what + " hero");
            if (s.Path != null) for (int i = 0; i < s.Path.Via.Length; i++) s.Path.Via[i].Resolve(layout, what + " path waypoint " + (i + 1));
            if (s.Boss != null && Array.IndexOf(BossSides, s.Boss) < 0) new LayoutPoint { Area = s.Boss }.Resolve(layout, what + " boss");
            if (o.Has("formation"))
            {
                JsonValue f = o["formation"];
                CheckKeys(f, what + " formation", "kind", "streams", "onScreen", "mix");
                s.Formation = new TrailerFormation { Kind = f["kind"].AsString(), Streams = f["streams"].AsInt(0), OnScreen = f["onScreen"].AsInt(0), Mix = MixEntry.ParseMix(f["mix"]) };
                if (Array.IndexOf(Formations, s.Formation.Kind) < 0) throw new FormatException(what + ": unknown formation '" + s.Formation.Kind + "'");
                if (s.Formation.Kind == "converge" && (s.Formation.Streams < 2 || s.Formation.Streams > 6)) throw new FormatException(what + ": converge needs 2 to 6 streams");
                if (s.Formation.OnScreen <= 0 || s.Formation.Mix.Length == 0) throw new FormatException(what + ": the formation needs an onScreen target and a mix");
            }
            if (o.Has("duel"))
            {
                s.Duel = MixEntry.ParseMix(o["duel"]);
                if (s.Duel.Length == 0) throw new FormatException(what + ": an empty duel");
            }
            if ((s.Formation == null) == (s.Duel == null)) throw new FormatException(what + ": needs exactly one of a formation and a duel");
            s.Combo = Strings(o["combo"]);
            s.Spells = Strings(o["spells"]);
            JsonValue g = o["grade"];
            if (!g.IsNull)
            {
                CheckKeys(g, what + " grade", "exposure", "contrast", "saturation", "temperature");
                s.Grade = new TrailerGrade { Exposure = g["exposure"].AsFloat(0f), Contrast = g["contrast"].AsFloat(0f), Saturation = g["saturation"].AsFloat(0f), Temperature = g["temperature"].AsFloat(0f) };
            }
            return s;
        }

        /// <summary>A shot's combo and spells: every grid position known, on the grid and within the shot.</summary>
        static void ValidateEntries(TrailerShot s, Trailer tr, SongMap map, BeatGrid grid)
        {
            string what = "shot " + s.Number;
            // As the run resolves them: anchors from the cut on (the first shot's, from its pre-roll's start).
            double anchorsFrom = s.Number > 1 ? s.Cut : s.SpanFrom;
            int anchors = 0;
            foreach (SongSection sec in map.Sections)
                if (sec.Feel == SectionFeel.Free)
                    foreach (TimedStrength a in sec.Anchors) if (a.Time >= anchorsFrom - 1e-9 && a.Time <= s.End + 1e-9) anchors++;
            foreach (string[] list in new[] { s.Combo, s.Spells })
                foreach (string text in list)
                {
                    GridEntry e;
                    try { e = GridEntry.Parse(text); }
                    catch (FormatException ex) { throw new FormatException(what + ": " + ex.Message); }
                    switch (e.Fixed)
                    {
                        case FixedPosition.Cut:
                            if (s.Number == 1) throw new FormatException(what + ": '" + text + "': the first shot has no cut");
                            break;
                        case FixedPosition.Final:
                            double fin = FinalTime(tr, map, grid);
                            if (fin < s.SpanFrom || fin > s.End + 1e-9) throw new FormatException(what + ": '" + text + "': the final slam (" + FinalPos(tr) + ") is outside the shot");
                            break;
                        case FixedPosition.Anchor:
                            if (anchors-- <= 0) throw new FormatException(what + ": '" + text + "': no free section's anchor is left in the shot");
                            break;
                        case FixedPosition.BarBeat:
                            double t;
                            try { t = map.TimeOfBarBeat(e.Bar, e.Beat, grid); }
                            catch (FormatException ex) { throw new FormatException(what + ": '" + text + "': " + ex.Message); }
                            if (t < s.SpanFrom || t > s.End + 1e-9) throw new FormatException(what + ": '" + text + "' is outside the shot");
                            break;
                    }
                }
        }

        /// <summary>The final slam's position: the downbeat of the cut to black's bar.</summary>
        static string FinalPos(Trailer tr)
        {
            SongMap.TryParseBarBeat(tr.BlackAtPos, out int bar, out _);
            return bar + ".1";
        }

        static double FinalTime(Trailer tr, SongMap map, BeatGrid grid) => map.TimeOfBarBeat(FinalPos(tr), grid);

        /// <summary>The song time of the final slam (the downbeat of the cut to black's bar).</summary>
        public double FinalSlamTime(SongMap map) => FinalTime(this, map, null);

        /// <summary>
        /// Shot <paramref name="index"/>'s level script (trailer.md, "How a shot uses the base"): the roster's level script
        /// (<paramref name="levelJson"/>) with the base overlay's global settings merged over it (never its sections),
        /// then the shot's fields: its combo, spells, formation and view replace every section's, and its span, path,
        /// positions and rules go in as the level script's shot fields. Its seed is the trailer's plus the shot's number.
        /// </summary>
        public LevelScript ShotScript(int index, string levelJson, string baseJson)
        {
            TrailerShot s = Shots[index];
            JsonValue root = IntensityOverlay.Parse(baseJson).WithoutSections().Apply(JsonValue.Parse(levelJson));
            float widest = Math.Max(s.ViewWidthM, s.ViewWidthToM);
            int target = Math.Max(1, s.TargetOnScreen);
            var set = new List<KeyValuePair<string, JsonValue>>
            {
                Pair("seed", JsonValue.OfNumber(Seed + (uint)s.Number)),
                Pair("spanS", Numbers(s.SpanFrom, s.End)),
                Pair("envelope", Object(Pair("onScreen", JsonValue.OfNumber(target)), Pair("viewWidthM", JsonValue.OfNumber(widest)))),
                Pair("heroStepS", JsonValue.OfNumber(s.HeroStepS)),
                Pair("killBudget", JsonValue.OfBool(false)),
                Pair("oneHitKills", JsonValue.OfBool(true)),
                Pair("hitPointScale", Object(Pair("fodder", JsonValue.OfNumber(0)), Pair("elite", JsonValue.OfNumber(0)), Pair("boss", JsonValue.OfNumber(0)))),
                Pair("clearRadiusM", JsonValue.OfNumber(0)),
                Pair("shapeScale", JsonValue.OfNumber(s.ShapeScale)),
                Pair("yawDeg", JsonValue.OfNumber(s.YawDeg)),
                Pair("holdGround", JsonValue.OfBool(s.HoldGround)),
                Pair("reserve", JsonValue.OfNumber(ShotReserve)),
            };
            if (s.Number > 1) set.Add(Pair("cut", JsonValue.OfString(s.CutAt)));
            if (s.HasFinal) set.Add(Pair("final", JsonValue.OfString(FinalPos(this))));
            if (s.ViewWidthToM > 0) set.Add(Pair("viewWidthToM", JsonValue.OfNumber(s.ViewWidthToM)));
            if (s.HeroAt != null) set.Add(Pair("heroAt", Point(s.HeroAt)));
            if (s.Path != null)
            {
                var via = new List<JsonValue>();
                foreach (LayoutPoint p in s.Path.Via) via.Add(Point(p));
                set.Add(Pair("heroPath", Object(Pair("via", JsonValue.OfItems(via)), Pair("stepM", JsonValue.OfNumber(s.Path.StepM)), Pair("loop", JsonValue.OfBool(s.Path.Loop)))));
            }
            if (s.Boss != null) set.Add(Pair("boss", JsonValue.OfString(s.Boss)));
            if (s.Duel != null) set.Add(Pair("duel", Mix(s.Duel)));
            foreach (KeyValuePair<string, JsonValue> kv in set) root = IntensityOverlay.Set(root, kv.Key, kv.Value);

            // Every section plays the shot: its combo, spells, horde and view, with no goal, boss or clearing of its own.
            JsonValue mix = Mix(s.Formation != null ? s.Formation.Mix : s.Duel);
            var secs = new List<JsonValue>();
            foreach (JsonValue sec in root["sections"].Items)
            {
                JsonValue v = sec;
                v = IntensityOverlay.Set(v, "onScreen", JsonValue.OfNumber(1));
                v = IntensityOverlay.Set(v, "viewWidthM", JsonValue.OfNumber(s.ViewWidthM));
                v = IntensityOverlay.Set(v, "mix", mix);
                v = IntensityOverlay.Set(v, "combo", StringItems(s.Combo));
                v = IntensityOverlay.Set(v, "spells", StringItems(s.Spells));
                v = IntensityOverlay.Set(v, "goal", JsonValue.Null);
                v = IntensityOverlay.Set(v, "boss", JsonValue.Null);
                v = IntensityOverlay.Set(v, "bossFrom", JsonValue.Null);
                v = IntensityOverlay.Set(v, "clear", JsonValue.OfBool(false));
                secs.Add(v);
            }
            root = IntensityOverlay.Set(root, "sections", JsonValue.OfItems(secs));
            return LevelScript.FromJson(root, Base);
        }

        static void CheckKeys(JsonValue o, string what, params string[] known)
        {
            if (o.Type != JsonType.Object) throw new FormatException(what + ": expected an object");
            foreach (KeyValuePair<string, JsonValue> kv in o.Members)
                if (Array.IndexOf(known, kv.Key) < 0) throw new FormatException(what + ": unknown field '" + kv.Key + "'");
        }

        static string[] Strings(JsonValue arr)
        {
            var list = new string[arr.Count];
            for (int i = 0; i < arr.Count; i++) list[i] = arr[i].AsString();
            return list;
        }

        static KeyValuePair<string, JsonValue> Pair(string k, JsonValue v) => new KeyValuePair<string, JsonValue>(k, v);
        static JsonValue Object(params KeyValuePair<string, JsonValue>[] members) => JsonValue.OfMembers(new List<KeyValuePair<string, JsonValue>>(members));
        static JsonValue Numbers(params double[] xs)
        {
            var list = new List<JsonValue>();
            foreach (double x in xs) list.Add(JsonValue.OfNumber(x));
            return JsonValue.OfItems(list);
        }
        static JsonValue StringItems(string[] xs)
        {
            var list = new List<JsonValue>();
            foreach (string x in xs) list.Add(JsonValue.OfString(x));
            return JsonValue.OfItems(list);
        }
        static JsonValue Point(LayoutPoint p) => p.Area != null ? JsonValue.OfString(p.Area) : Numbers(p.At.X, p.At.Y);
        static JsonValue Mix(MixEntry[] mix)
        {
            var members = new List<KeyValuePair<string, JsonValue>>();
            foreach (MixEntry e in mix) members.Add(Pair(e.Type, JsonValue.OfNumber(e.Weight)));
            return JsonValue.OfMembers(members);
        }
    }
}
