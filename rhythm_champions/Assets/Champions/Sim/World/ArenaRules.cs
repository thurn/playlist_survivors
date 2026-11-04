using System;
using System.Collections.Generic;

namespace Champions.Sim
{
    /// <summary>
    /// A closed outline on the ground, anticlockwise (x right, y up the screen), in an arena's own metres: an arena's
    /// walls, a floor zone, a grown obstacle. Content gives a circle (a regular polygon of <c>sides</c>, the low-poly
    /// look's), a turned rectangle or the points themselves; every shape is a polygon from then on.
    /// </summary>
    public sealed class ArenaShape
    {
        public double[] X = new double[0], Y = new double[0];

        public int Count => X.Length;

        /// <summary>A regular polygon of <paramref name="sides"/> round (<paramref name="cx"/>, <paramref name="cy"/>), its vertices on the circle of <paramref name="radius"/>, the first at <paramref name="degrees"/>.</summary>
        public static ArenaShape Circle(double cx, double cy, double radius, int sides, double degrees = 0)
        {
            var s = new ArenaShape { X = new double[sides], Y = new double[sides] };
            for (int i = 0; i < sides; i++)
            {
                double a = (degrees + 360.0 * i / sides) * Math.PI / 180;
                s.X[i] = cx + radius * Math.Cos(a);
                s.Y[i] = cy + radius * Math.Sin(a);
            }
            return s;
        }

        /// <summary>A <paramref name="width"/> by <paramref name="height"/> rectangle round (<paramref name="cx"/>, <paramref name="cy"/>), turned <paramref name="degrees"/> anticlockwise.</summary>
        public static ArenaShape Box(double cx, double cy, double width, double height, double degrees = 0)
        {
            double a = degrees * Math.PI / 180, c = Math.Cos(a), s = Math.Sin(a), hx = width / 2, hy = height / 2;
            var shape = new ArenaShape { X = new double[4], Y = new double[4] };
            double[] ux = { -hx, hx, hx, -hx }, uy = { -hy, -hy, hy, hy };
            for (int i = 0; i < 4; i++)
            {
                shape.X[i] = cx + ux[i] * c - uy[i] * s;
                shape.Y[i] = cy + ux[i] * s + uy[i] * c;
            }
            return shape;
        }

        public static ArenaShape Of(double[] x, double[] y) => new ArenaShape { X = (double[])x.Clone(), Y = (double[])y.Clone() };

        /// <summary>The signed area (positive anticlockwise).</summary>
        public double Area
        {
            get
            {
                double a = 0;
                for (int i = 0, j = Count - 1; i < Count; j = i++) a += X[j] * Y[i] - X[i] * Y[j];
                return a / 2;
            }
        }

        /// <summary>Its vertices' mean.</summary>
        public void Centre(out double x, out double y)
        {
            x = y = 0;
            for (int i = 0; i < Count; i++)
            {
                x += X[i];
                y += Y[i];
            }
            x /= Count;
            y /= Count;
        }

        public void Bounds(out double x0, out double y0, out double x1, out double y1)
        {
            x0 = y0 = double.PositiveInfinity;
            x1 = y1 = double.NegativeInfinity;
            for (int i = 0; i < Count; i++)
            {
                x0 = Math.Min(x0, X[i]);
                y0 = Math.Min(y0, Y[i]);
                x1 = Math.Max(x1, X[i]);
                y1 = Math.Max(y1, Y[i]);
            }
        }

        /// <summary>Is (<paramref name="px"/>, <paramref name="py"/>) inside, (<paramref name="ox"/>, <paramref name="oy"/>) added to every vertex? (Even-odd: a point on the line may go either way.)</summary>
        public bool Contains(double px, double py, double ox = 0, double oy = 0)
        {
            bool inside = false;
            for (int i = 0, j = Count - 1; i < Count; j = i++)
            {
                double yi = Y[i] + oy, yj = Y[j] + oy;
                if ((yi > py) == (yj > py)) continue;
                double xi = X[i] + ox, xj = X[j] + ox;
                if (px < xj + (py - yj) * (xi - xj) / (yi - yj)) inside = !inside;
            }
            return inside;
        }

        /// <summary>The distance from (<paramref name="px"/>, <paramref name="py"/>) to the outline (not the area), offset by (<paramref name="ox"/>, <paramref name="oy"/>).</summary>
        public double DistanceToOutline(double px, double py, double ox = 0, double oy = 0)
        {
            double best = double.PositiveInfinity;
            for (int i = 0, j = Count - 1; i < Count; j = i++)
                best = Math.Min(best, HitShape.DistanceToSegment(px, py, X[j] + ox, Y[j] + oy, X[i] + ox, Y[i] + oy));
            return best;
        }

        /// <summary>
        /// Reads a shape object: <c>radiusM</c> (with <c>at</c>, <c>sides</c>, default <see cref="ArenaRules.CircleSides"/>,
        /// and <c>degrees</c>), <c>sizeM</c> [width, height] (with <c>at</c> and <c>degrees</c>) or <c>pointsM</c> [[x, y], ...].
        /// Clockwise points are turned round, so every shape winds anticlockwise.
        /// </summary>
        internal static ArenaShape Read(ContentReader r)
        {
            ArenaShape shape;
            int kinds = (r.Has("radiusM") ? 1 : 0) + (r.Has("sizeM") ? 1 : 0) + (r.Has("pointsM") ? 1 : 0);
            if (kinds != 1) throw r.Fail("expected exactly one of radiusM, sizeM or pointsM");
            if (r.Has("pointsM"))
            {
                var items = r.Array("pointsM");
                if (items.Count < 3) throw ContentReader.Fail(r.Child("pointsM"), "at least three points");
                var x = new double[items.Count];
                var y = new double[items.Count];
                for (int i = 0; i < items.Count; i++)
                {
                    var p = items[i];
                    if (p.Type != JsonType.Array || p.Items.Count != 2 || p.Items[0].Type != JsonType.Number || p.Items[1].Type != JsonType.Number)
                        throw ContentReader.Fail($"{r.Child("pointsM")}[{i}]", "expected [x, y]");
                    x[i] = p.Items[0].Number;
                    y[i] = p.Items[1].Number;
                }
                shape = new ArenaShape { X = x, Y = y };
            }
            else
            {
                double cx = 0, cy = 0;
                if (r.Has("at"))
                {
                    var at = r.Numbers("at");
                    if (at.Length != 2) throw ContentReader.Fail(r.Child("at"), "expected [x, y]");
                    cx = at[0];
                    cy = at[1];
                }
                double degrees = r.NumberOr("degrees", 0);
                if (r.Has("radiusM"))
                {
                    double radius = r.Number("radiusM");
                    int sides = r.IntOr("sides", ArenaRules.CircleSides);
                    if (!(radius > 0)) throw ContentReader.Fail(r.Child("radiusM"), "positive");
                    if (sides < 3) throw ContentReader.Fail(r.Child("sides"), "at least 3");
                    shape = Circle(cx, cy, radius, sides, degrees);
                }
                else
                {
                    var size = r.Numbers("sizeM");
                    if (size.Length != 2 || !(size[0] > 0) || !(size[1] > 0)) throw ContentReader.Fail(r.Child("sizeM"), "expected [width, height], both positive");
                    shape = Box(cx, cy, size[0], size[1], degrees);
                }
            }
            if (Math.Abs(shape.Area) < 1e-6) throw r.Fail("a shape with an area");
            if (shape.Area < 0)
            {
                Array.Reverse(shape.X);
                Array.Reverse(shape.Y);
            }
            return shape;
        }
    }

    /// <summary>What a floor zone is to a fight (boss-rush.md, Arenas): its kind is the authoring's, its state the fight's.</summary>
    public enum ArenaZoneKind : byte
    {
        /// <summary>Floor the fight promises stays safe to stand on.</summary>
        Safe = 0,
        /// <summary>Floor the fight hurts on (its own rule: the arena only marks it).</summary>
        Hazard = 1,
        /// <summary>Floor that can break away for the rest of the fight (<see cref="BossArena.Break"/>: islands).</summary>
        Breakable = 2,
        /// <summary>A zone that travels round its <see cref="ArenaZone.Path"/> (moving floor, drifting islands).</summary>
        Moving = 3,
    }

    /// <summary>A floor zone's state now: open floor walks; raised, sunk and broken floor block bodies.</summary>
    public enum ArenaZoneState : byte
    {
        Open = 0,
        /// <summary>Raised into a block: it stops bodies and shots.</summary>
        Raised = 1,
        /// <summary>Sunk into a pit: it stops bodies, shots fly over it.</summary>
        Sunk = 2,
        /// <summary>Broken away for the rest of the fight: a hole bodies can't cross, shots fly over.</summary>
        Broken = 3,
    }

    /// <summary>One floor zone of an arena's definition.</summary>
    public sealed class ArenaZone
    {
        public string Id;
        public ArenaZoneKind Kind;
        public ArenaZoneState State;
        public ArenaShape Shape;
        /// <summary>A moving zone's loop: the offsets (m) it travels through in turn, back to the first, at <see cref="SpeedMps"/>.</summary>
        public double[] PathX = new double[0], PathY = new double[0];
        public double SpeedMps;

        public bool Path => PathX.Length > 1;
    }

    /// <summary>A placeholder or kit prop's kind: its footprint, its height as drawn and how it breaks.</summary>
    public sealed class ArenaPropKind
    {
        public string Id;
        public FootprintShape Shape;
        public double Radius, HalfX, HalfY;
        public double HeightM = 2;
        /// <summary>The hero's blows it takes before it crumbles; 0: the hero can't break it (a boss's attack still can).</summary>
        public int Hits;
        /// <summary>By default it stops bodies and shots; a placement may say otherwise.</summary>
        public bool Blocks = true, BlocksShots = true;

        public double Bound => Shape == FootprintShape.Circle ? Radius : Math.Sqrt(HalfX * HalfX + HalfY * HalfY);
    }

    /// <summary>A prop placed in an arena: its kind, middle, angle (degrees) and whether it stops bodies and shots.</summary>
    public struct ArenaPropPlacement
    {
        public int Kind;
        public double X, Y, Degrees;
        public bool Blocks, BlocksShots;
    }

    /// <summary>How the camera frames an arena (boss-rush.md, Arenas): its whole outline when the view eases out no farther than <see cref="MaxScale"/>; past that it scrolls.</summary>
    public struct ArenaCamera
    {
        /// <summary>The farthest out the view goes, as a share of the camera's normal distance (the main game's duel is 1.2).</summary>
        public double MaxScale;
        /// <summary>Ground kept clear round the outline, so the walls never touch the frame's edge (m).</summary>
        public double MarginM;
    }

    /// <summary>
    /// One arena (boss-rush.md, Arenas): its walls' outline, floor zones, props, where the hero and the boss start, and its
    /// camera. In its own metres: <see cref="BossArena.Form"/> places it so <see cref="HeroStart"/> lands on the hero.
    /// </summary>
    public sealed class ArenaDef
    {
        public string Id;
        /// <summary>The walls: the ground inside them is the arena's.</summary>
        public ArenaShape Outline;
        public ArenaZone[] Zones = new ArenaZone[0];
        public ArenaPropPlacement[] Props = new ArenaPropPlacement[0];
        public double HeroStartX, HeroStartY, BossStartX, BossStartY;
        public ArenaCamera Camera = new ArenaCamera { MaxScale = 1.8, MarginM = 1.5 };

        public int ZoneIndex(string id) => Array.FindIndex(Zones, z => z.Id == id);
    }

    /// <summary>
    /// The Boss Rush arenas' rules (Content/arenas.json; boss-rush.md, Arenas and Fights): how long an arena takes to form
    /// and retract, its walls' look, the placeholder props and the arenas. Fights add their arenas here; act kits dress
    /// them with their own prop kinds.
    /// </summary>
    public sealed class ArenaRules
    {
        public const string File = "arenas.json";
        /// <summary>A circle's sides when content doesn't say: low-poly, but round at an arena's size.</summary>
        public const int CircleSides = 32;

        /// <summary>An arena draws itself in round the hero over this many beats, and its lines retract over <see cref="RetractBeats"/>.</summary>
        public double FormBeats = 4, RetractBeats = 4;
        /// <summary>The walls as drawn: their thickness outward from the outline and their height (m). Bodies stop at the outline.</summary>
        public double WallThicknessM = 0.5, WallHeightM = 1.2;
        /// <summary>A raised zone's height as drawn (m), and a sunk one's depth.</summary>
        public double RaiseM = 1, SinkM = 1;

        public ArenaPropKind[] Props = new ArenaPropKind[0];
        public ArenaDef[] Arenas = new ArenaDef[0];

        public int PropIndex(string id) => Array.FindIndex(Props, k => k.Id == id);

        /// <summary>The arena <paramref name="id"/>, or null.</summary>
        public ArenaDef Find(string id) => Array.Find(Arenas, a => a.Id == id);

        public ArenaRules Clone()
        {
            var c = (ArenaRules)MemberwiseClone();
            c.Props = (ArenaPropKind[])Props.Clone();
            c.Arenas = (ArenaDef[])Arenas.Clone();
            return c;
        }

        /// <summary>Reads arenas.json's object.</summary>
        public static ArenaRules Read(JsonValue json, string path)
        {
            var r = new ContentReader(json, path);
            var rules = new ArenaRules
            {
                FormBeats = r.Number("formBeats"),
                RetractBeats = r.Number("retractBeats"),
                WallThicknessM = r.Number("wallThicknessM"),
                WallHeightM = r.Number("wallHeightM"),
                RaiseM = r.Number("raiseM"),
                SinkM = r.Number("sinkM"),
            };
            rules.Props = r.Objects("props", k =>
            {
                var kind = new ArenaPropKind
                {
                    Id = k.String("id"),
                    HeightM = k.Number("heightM"),
                    Hits = k.Int("hits"),
                    Blocks = k.BoolOr("blocks", true),
                    BlocksShots = k.BoolOr("blocksShots", true),
                };
                if (k.Has("radiusM"))
                {
                    kind.Shape = FootprintShape.Circle;
                    kind.Radius = k.Number("radiusM");
                }
                else
                {
                    kind.Shape = FootprintShape.Box;
                    var size = k.Numbers("sizeM");
                    if (size.Length != 2) throw ContentReader.Fail(k.Child("sizeM"), "expected [length, depth]");
                    kind.HalfX = size[0] / 2;
                    kind.HalfY = size[1] / 2;
                }
                if (kind.Hits < 0) throw ContentReader.Fail(k.Child("hits"), "at least 0");
                if (!(kind.Bound > 0)) throw ContentReader.Fail(k.Path, "a footprint with an area");
                if (!(kind.HeightM > 0)) throw ContentReader.Fail(k.Child("heightM"), "positive");
                return kind;
            });
            var props = rules.Props;
            rules.Arenas = r.Objects("arenas", a => ReadArena(a, props));
            r.End();
            rules.Validate(path);
            return rules;
        }

        static ArenaDef ReadArena(ContentReader a, ArenaPropKind[] props)
        {
            var outline = a.Object("outline");
            var def = new ArenaDef { Id = a.String("id"), Outline = ArenaShape.Read(outline) };
            outline.End();
            Point(a, "heroStart", out def.HeroStartX, out def.HeroStartY);
            Point(a, "bossStart", out def.BossStartX, out def.BossStartY);
            var cam = a.Object("camera");
            def.Camera = new ArenaCamera { MaxScale = cam.Number("maxScale"), MarginM = cam.Number("marginM") };
            cam.End();
            def.Zones = a.Objects("zones", z =>
            {
                var zone = new ArenaZone
                {
                    Id = z.String("id"),
                    Kind = Parse<ArenaZoneKind>(z, "kind"),
                    State = z.Has("state") ? Parse<ArenaZoneState>(z, "state") : ArenaZoneState.Open,
                };
                var shape = z.Object("shape");
                zone.Shape = ArenaShape.Read(shape);
                shape.End();
                if (z.Has("path"))
                {
                    var items = z.Array("path");
                    zone.PathX = new double[items.Count];
                    zone.PathY = new double[items.Count];
                    for (int i = 0; i < items.Count; i++)
                    {
                        var p = items[i];
                        if (p.Type != JsonType.Array || p.Items.Count != 2 || p.Items[0].Type != JsonType.Number || p.Items[1].Type != JsonType.Number)
                            throw ContentReader.Fail($"{z.Child("path")}[{i}]", "expected [x, y]");
                        zone.PathX[i] = p.Items[0].Number;
                        zone.PathY[i] = p.Items[1].Number;
                    }
                    zone.SpeedMps = z.Number("speedMps");
                }
                if (zone.Kind == ArenaZoneKind.Moving && (!zone.Path || !(zone.SpeedMps > 0)))
                    throw ContentReader.Fail(z.Path, "a moving zone needs a path of at least two offsets and a positive speedMps");
                if (zone.Kind != ArenaZoneKind.Moving && zone.Path) throw ContentReader.Fail(z.Child("path"), "only a moving zone travels");
                if (zone.State == ArenaZoneState.Broken) throw ContentReader.Fail(z.Child("state"), "a zone starts whole");
                return zone;
            });
            def.Props = a.Objects("props", p =>
            {
                string kind = p.String("kind");
                int index = Array.FindIndex(props, k => k.Id == kind);
                if (index < 0) throw ContentReader.Fail(p.Child("kind"), $"no prop kind {kind}");
                var at = p.Numbers("at");
                if (at.Length != 2) throw ContentReader.Fail(p.Child("at"), "expected [x, y]");
                return new ArenaPropPlacement
                {
                    Kind = index, X = at[0], Y = at[1], Degrees = p.NumberOr("degrees", 0),
                    Blocks = p.BoolOr("blocks", props[index].Blocks), BlocksShots = p.BoolOr("blocksShots", props[index].BlocksShots),
                };
            });
            return def;
        }

        static void Point(ContentReader r, string key, out double x, out double y)
        {
            var at = r.Numbers(key);
            if (at.Length != 2) throw ContentReader.Fail(r.Child(key), "expected [x, y]");
            x = at[0];
            y = at[1];
        }

        static T Parse<T>(ContentReader r, string key) where T : struct
        {
            string s = r.String(key);
            if (string.IsNullOrEmpty(s) || char.IsDigit(s[0]) || !Enum.TryParse(s, true, out T value) || !Enum.IsDefined(typeof(T), value))
                throw ContentReader.Fail(r.Child(key), $"no {typeof(T).Name} {s}");
            return value;
        }

        /// <summary>
        /// Checks every arena: unique ids, the hero's and the boss's starts on open floor clear of the props, every zone and
        /// prop inside the walls, every moving zone's whole loop inside them.
        /// </summary>
        void Validate(string path)
        {
            if (!(FormBeats > 0) || !(RetractBeats > 0)) throw ContentReader.Fail(path + ".formBeats", "formBeats and retractBeats positive");
            if (!(WallThicknessM > 0) || !(WallHeightM > 0)) throw ContentReader.Fail(path + ".wallThicknessM", "wallThicknessM and wallHeightM positive");
            var ids = new HashSet<string>();
            for (int i = 0; i < Arenas.Length; i++)
            {
                var a = Arenas[i];
                string at = $"{path}.arenas[{i}]";
                if (!ids.Add(a.Id)) throw ContentReader.Fail(at + ".id", $"another arena is {a.Id}");
                if (!(a.Camera.MaxScale >= 1)) throw ContentReader.Fail(at + ".camera.maxScale", "at least 1");
                if (!(a.Camera.MarginM >= 0)) throw ContentReader.Fail(at + ".camera.marginM", "at least 0");
                var zoneIds = new HashSet<string>();
                for (int z = 0; z < a.Zones.Length; z++)
                {
                    var zone = a.Zones[z];
                    if (!zoneIds.Add(zone.Id)) throw ContentReader.Fail($"{at}.zones[{z}].id", $"another zone is {zone.Id}");
                    int loop = Math.Max(1, zone.PathX.Length);
                    for (int k = 0; k < loop; k++)
                    {
                        double ox = zone.Path ? zone.PathX[k] : 0, oy = zone.Path ? zone.PathY[k] : 0;
                        for (int v = 0; v < zone.Shape.Count; v++)
                            if (!a.Outline.Contains(zone.Shape.X[v] + ox, zone.Shape.Y[v] + oy))
                                throw ContentReader.Fail($"{at}.zones[{z}]", "reaches past the walls");
                    }
                }
                for (int p = 0; p < a.Props.Length; p++)
                {
                    var prop = a.Props[p];
                    var kind = Props[prop.Kind];
                    if (!a.Outline.Contains(prop.X, prop.Y) || a.Outline.DistanceToOutline(prop.X, prop.Y) < kind.Bound - 1e-9)
                        throw ContentReader.Fail($"{at}.props[{p}]", "reaches past the walls");
                }
                StartClear(a, at + ".heroStart", a.HeroStartX, a.HeroStartY);
                StartClear(a, at + ".bossStart", a.BossStartX, a.BossStartY);
            }
        }

        /// <summary>The hero's start must stand clear: <see cref="BossArena.Form"/> builds the arena round the hero where it stands.</summary>
        void StartClear(ArenaDef a, string at, double x, double y)
        {
            const double Room = 1;
            if (!a.Outline.Contains(x, y) || a.Outline.DistanceToOutline(x, y) < Room) throw ContentReader.Fail(at, "inside the walls, 1 m clear of them");
            foreach (var z in a.Zones)
                if (z.State != ArenaZoneState.Open && !z.Path && z.Shape.Contains(x, y)) throw ContentReader.Fail(at, $"on zone {z.Id}, which isn't open floor");
            foreach (var p in a.Props)
            {
                var k = Props[p.Kind];
                if (p.Blocks && Math.Sqrt((x - p.X) * (x - p.X) + (y - p.Y) * (y - p.Y)) < k.Bound + Room) throw ContentReader.Fail(at, $"within 1 m of a {k.Id}");
            }
        }
    }
}
