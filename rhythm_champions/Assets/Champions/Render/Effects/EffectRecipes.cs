using System;
using System.IO;
using Champions.Sim;
using UnityEngine;

namespace Champions.Render
{
    /// <summary>How a recipe's layer draws.</summary>
    public enum LayerShape
    {
        /// <summary>A Hovl slash mesh as an arc round its point, swept by its head (Slash.shader's arc).</summary>
        Arc,
        /// <summary>A flat strip along the shot's path, swept along it.</summary>
        Ribbon,
        /// <summary>A strip standing up at its point: a figure's glow.</summary>
        Upright,
        /// <summary>A Hovl mesh glowing at its rim: a spectral sword.</summary>
        Solid,
        /// <summary>A burst of compute sparks.</summary>
        Sparks,
    }

    /// <summary>Where a layer stands.</summary>
    public enum LayerPlace
    {
        /// <summary>At the shot's point.</summary>
        At,
        /// <summary>At its path's end.</summary>
        To,
        /// <summary>Its repeats spaced evenly along the path, from the start to the end.</summary>
        Path,
        /// <summary>Its repeats on a circle of <see cref="EffectLayer.Spacing"/> m round the shot's point.</summary>
        Around,
        /// <summary>Where a slash's sweep ends on its arc (sparks thrown off the blade).</summary>
        Edge,
    }

    /// <summary>The way a layer's sparks fly.</summary>
    public enum SparkWay
    {
        /// <summary>Along the layer's facing (a slash's: along its sweep at the edge).</summary>
        Facing,
        /// <summary>Straight up.</summary>
        Up,
        /// <summary>Out from the shot's point through the layer's (a burst round it).</summary>
        Out,
        /// <summary>Back along the path.</summary>
        Back,
    }

    /// <summary>One layer of a cue's recipe (Content/effects.json; the field names are the json's, camel-cased).</summary>
    public sealed class EffectLayer
    {
        public LayerShape Shape;
        /// <summary>The Hovl mesh an arc or solid draws (<see cref="EffectLibrary"/>'s names: Slash1 to Slash4, MagicSword).</summary>
        public string Mesh;
        public LayerPlace At = LayerPlace.At;
        /// <summary>Copies of the layer: placed along the path or round the point, each <see cref="Every"/> s after the last.</summary>
        public int Repeat = 1;
        public float Every, YawStep, Spacing;
        /// <summary>Every other copy's sweep runs the other way.</summary>
        public bool Alternate;
        /// <summary>Seconds after the shot's time it starts (negative: before, a sword falling onto its impact).</summary>
        public float Delay;
        /// <summary>Its radius or length (m); with <see cref="RadiusShot"/> above 0, that times the shot's radius when it gives one.</summary>
        public float Radius, RadiusShot;
        /// <summary>An arc's sweep (degrees, 360 a full turn), or the shot's when <see cref="ArcShot"/> and it gives one; a ribbon's half width (m).</summary>
        public float Arc;
        public bool ArcShot;
        /// <summary>Degrees off the facing; an arc's tilt about its facing, a solid's pitch (degrees); metres above the ground.</summary>
        public float Yaw, Tilt, Height;
        /// <summary>The head's sweep (s), or the shot's duration when <see cref="SweepShot"/> and it gives one.</summary>
        public float Sweep;
        public bool SweepShot;
        /// <summary>The travel's and growth's time (s; NaN: the sweep's), or the shot's duration when <see cref="TravelShot"/>.</summary>
        public float TravelS = float.NaN;
        public bool TravelShot;
        /// <summary>How long it lingers after its sweep and travel (s), and the fade at its end (s; NaN: the whole linger).</summary>
        public float Linger, Fade = float.NaN;
        /// <summary>The share of the band lit at the head (the crescent's width); its HDR gain; the noise streaks' depth (0-1).</summary>
        public float Width = 0.6f, Brightness = 1f, Streak = 0.5f;
        /// <summary>Its scale at birth, easing to 1 over its travel (a shockwave's racing out).</summary>
        public float Grow = 1f;
        /// <summary>1: Hovl's crescent taper behind the head; 0: an even band (a shockwave's ring, a figure's glow).</summary>
        public float Taper = 1f;
        /// <summary>It moves over its travel: along the shot's path (<see cref="TravelPath"/>), or <see cref="TravelM"/> m along its facing, and <see cref="TravelUpM"/> m up.</summary>
        public bool TravelPath;
        public float TravelM, TravelUpM;
        // Sparks.
        public int Count;
        public Vector2 Speed, Life;
        public float Spread, Up, Size, Drag, Gravity;
        public SparkWay Way;
    }

    /// <summary>
    /// The weapon effects' recipes (rhythm_champions/Content/effects.json; design.md, The Look &gt; Weapon effects): each
    /// <see cref="EffectCue"/>'s layers and the powers' gains. Read strictly: every cue must have a recipe, and an unknown
    /// or malformed field fails with its path.
    /// </summary>
    public sealed class EffectRecipes
    {
        public const string File = "effects.json";

        public struct Power
        {
            public float Brightness, Scale, Sparks;
        }

        /// <summary>By <see cref="EffectPower"/>: the brightness and spark count every layer takes, and the scale a layer
        /// sized by the recipe (not by the shot, which the sim sized already) takes.</summary>
        public readonly Power[] Powers = new Power[3];
        /// <summary>By <see cref="EffectCue"/>.</summary>
        public readonly EffectLayer[][] Cues = new EffectLayer[Enum.GetValues(typeof(EffectCue)).Length][];
        public EffectLayer[] Orbit, Halo;

        /// <summary>A cue's name in the json: its enum name camel-cased (`swordSlash`).</summary>
        public static string Name(EffectCue cue) => Camel(cue.ToString());

        static string Camel(string s) => char.ToLowerInvariant(s[0]) + s.Substring(1);

        public static EffectRecipes Load(string contentDir)
        {
            string path = Path.Combine(contentDir, File);
            try
            {
                return Read(JsonValue.Parse(System.IO.File.ReadAllText(path)));
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is FormatException)
            {
                throw new ContentException($"{File}: can't read {path}: {e.Message}", e);
            }
        }

        public static EffectRecipes Read(JsonValue json)
        {
            var recipes = new EffectRecipes();
            var r = new ContentReader(json, File);
            r.String("note");
            var powers = r.Object("powers");
            foreach (EffectPower p in Enum.GetValues(typeof(EffectPower)))
            {
                var o = powers.Object(Camel(p.ToString()));
                recipes.Powers[(int)p] = new Power
                {
                    Brightness = (float)o.Number("brightness"), Scale = (float)o.Number("scale"), Sparks = (float)o.Number("sparks"),
                };
                o.End();
            }
            powers.End();
            var cues = r.Object("cues");
            foreach (EffectCue cue in Enum.GetValues(typeof(EffectCue)))
                recipes.Cues[(int)cue] = cues.Objects(Name(cue), ReadLayer);
            cues.End();
            recipes.Orbit = r.Objects("orbitBlades", ReadLayer);
            recipes.Halo = r.Objects("bladeHalo", ReadLayer);
            r.End();
            return recipes;
        }

        static EffectLayer ReadLayer(ContentReader r)
        {
            var l = new EffectLayer { Shape = Parse<LayerShape>(r, "shape") };
            l.At = r.Has("at") ? Parse<LayerPlace>(r, "at") : LayerPlace.At;
            l.Repeat = r.IntOr("repeat", 1);
            if (l.Repeat < 1) throw ContentReader.Fail(r.Child("repeat"), "at least 1");
            l.Every = (float)r.NumberOr("every", 0);
            l.YawStep = (float)r.NumberOr("yawStep", 0);
            l.Spacing = (float)r.NumberOr("spacing", 0);
            l.Alternate = r.BoolOr("alternate", false);
            l.Delay = (float)r.NumberOr("delay", 0);
            l.Yaw = (float)r.NumberOr("yaw", 0);
            l.Height = (float)r.NumberOr("height", 0);
            if (l.Shape == LayerShape.Sparks)
            {
                l.Count = r.Int("count");
                l.Speed = Pair(r, "speed");
                l.Life = Pair(r, "life");
                l.Spread = (float)r.Number("spread");
                l.Up = (float)r.NumberOr("up", 0);
                l.Size = (float)r.Number("size");
                l.Drag = (float)r.NumberOr("drag", 2);
                l.Gravity = (float)r.NumberOr("gravity", 9.81);
                l.Brightness = (float)r.NumberOr("brightness", 1);
                l.Way = r.Has("way") ? Parse<SparkWay>(r, "way") : SparkWay.Facing;
                l.Radius = (float)r.NumberOr("radius", 0);
                l.RadiusShot = (float)r.NumberOr("radiusShot", 0);
                return l;
            }
            if (l.Shape == LayerShape.Arc || l.Shape == LayerShape.Solid) l.Mesh = r.String("mesh");
            l.Radius = (float)r.Number("radius");
            l.RadiusShot = (float)r.NumberOr("radiusShot", 0);
            l.Arc = (float)r.NumberOr("arc", l.Shape == LayerShape.Arc ? 360 : 0.3);
            l.ArcShot = r.BoolOr("arcShot", false);
            l.Tilt = (float)r.NumberOr("tilt", 0);
            l.Sweep = (float)r.Number("sweep");
            l.SweepShot = r.BoolOr("sweepShot", false);
            l.TravelS = (float)r.NumberOr("travel", double.NaN);
            l.TravelShot = r.BoolOr("travelShot", false);
            l.Linger = (float)r.Number("linger");
            l.Fade = (float)r.NumberOr("fade", double.NaN);
            if (l.Sweep < 0 || l.Linger <= 0 || l.Fade <= 0) throw ContentReader.Fail(r.Path, "sweep at least 0, linger and fade above 0");
            l.Width = (float)r.NumberOr("width", 0.6);
            l.Brightness = (float)r.NumberOr("brightness", 1);
            l.Streak = (float)r.NumberOr("streak", 0.5);
            l.Grow = (float)r.NumberOr("grow", 1);
            l.Taper = (float)r.NumberOr("taper", l.Shape == LayerShape.Upright ? 0 : 1);
            l.TravelPath = r.BoolOr("travelPath", false);
            l.TravelM = (float)r.NumberOr("travelM", 0);
            l.TravelUpM = (float)r.NumberOr("travelUpM", 0);
            return l;
        }

        static Vector2 Pair(ContentReader r, string key)
        {
            var n = r.Numbers(key);
            if (n.Length != 2 || n[0] > n[1]) throw ContentReader.Fail(r.Child(key), "expected [min, max]");
            return new Vector2((float)n[0], (float)n[1]);
        }

        static T Parse<T>(ContentReader r, string key) where T : struct
        {
            string s = r.String(key);
            foreach (T v in Enum.GetValues(typeof(T)))
                if (Camel(v.ToString()) == s) return v;
            throw ContentReader.Fail(r.Child(key), $"{s} is not one of {string.Join(", ", Array.ConvertAll((T[])Enum.GetValues(typeof(T)), v => Camel(v.ToString())))}");
        }
    }
}
