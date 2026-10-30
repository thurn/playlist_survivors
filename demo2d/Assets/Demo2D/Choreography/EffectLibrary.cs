using System;
using System.Collections.Generic;
using PS.Choreography;

namespace Demo2D.Choreography
{
    /// <summary>How an effect turns with its event's yaw.</summary>
    public enum EffectAim { None, Side, Yaw }

    /// <summary>A pooled Light2D's cue for one effect (PX7): its colour, reach and intensity over the effect's age.</summary>
    public sealed class LightCue
    {
        public float R, G, B;
        public float RadiusM, Intensity, AttackS, DecayS;

        /// <summary>The intensity <paramref name="age"/> seconds after the event: a linear attack, then a quadratic decay to 0. A flight's cue (no decay) holds its peak.</summary>
        public float At(double age)
        {
            if (age < 0) return 0;
            if (DecayS <= 0) return Intensity;
            if (age < AttackS) return Intensity * (float)(age / AttackS);
            double k = 1 - (age - AttackS) / DecayS;
            return k <= 0 ? 0 : Intensity * (float)(k * k);
        }

        public static LightCue Parse(JsonValue v, string what)
        {
            if (v.IsNull) return null;
            var c = Color(v["color"], what + " light") ?? throw new FormatException(what + ": a light needs a color");
            return new LightCue
            {
                R = c[0], G = c[1], B = c[2],
                RadiusM = Positive(v["radiusM"], what + " light radiusM"),
                Intensity = Positive(v["intensity"], what + " light intensity"),
                AttackS = v["attackS"].AsFloat(0),
                DecayS = v["decayS"].AsFloat(0),
            };
        }

        internal static float Positive(JsonValue v, string what)
        {
            float f = v.AsFloat(-1);
            if (!(f > 0)) throw new FormatException(what + " must be a positive number");
            return f;
        }

        /// <summary>[r, g, b] or [r, g, b, a] (alpha 1 when left out), or null.</summary>
        internal static float[] Color(JsonValue v, string what)
        {
            if (v.IsNull) return null;
            if (v.Count != 3 && v.Count != 4) throw new FormatException(what + ": a colour is [r, g, b] or [r, g, b, a]");
            return new[] { v[0].AsFloat(), v[1].AsFloat(), v[2].AsFloat(), v.Count == 4 ? v[3].AsFloat() : 1f };
        }
    }

    /// <summary>
    /// A beam drawn as a line (the laser's sweep): a hot core in a wider glow, from the effect's
    /// anchor along its yaw, jagged anew every 1/30 s, in the pixel grid. Colours are linear HDR.
    /// </summary>
    public sealed class BeamSpec
    {
        public float LengthM, CoreWidthPx, GlowWidthPx, JitterM;
        public int Segments;
        public float[] Core, Glow;
        /// <summary>The fade after the sweep ends.</summary>
        public float FadeS;

        public static BeamSpec Parse(JsonValue v, string what)
        {
            if (v.IsNull) return null;
            return new BeamSpec
            {
                LengthM = LightCue.Positive(v["lengthM"], what + " beam lengthM"),
                CoreWidthPx = LightCue.Positive(v["coreWidthPx"], what + " beam coreWidthPx"),
                GlowWidthPx = LightCue.Positive(v["glowWidthPx"], what + " beam glowWidthPx"),
                JitterM = v["jitterM"].AsFloat(0.15f),
                Segments = Math.Max(2, v["segments"].AsInt(24)),
                Core = LightCue.Color(v["core"], what + " beam core") ?? new[] { 4f, 4f, 4f, 1f },
                Glow = LightCue.Color(v["glow"], what + " beam glow") ?? new[] { 0.6f, 0.9f, 2.5f, 1f },
                FadeS = v["fadeS"].AsFloat(0.15f),
            };
        }
    }

    /// <summary>One entry of the effect library: a CFXR prefab for a timeline effect kind, or for a projectile in flight.</summary>
    public sealed class EffectSpec
    {
        public string Name, Prefab;
        public float Scale, EventScaleUnit = 1;
        /// <summary>Multiplies the particles' start colour (RGBA), or null.</summary>
        public float[] Tint;
        /// <summary>Over the ground tiles and under the characters, rather than over everything.</summary>
        public bool Ground;
        /// <summary>How long an instance lives (a flight lives as long as its projectile).</summary>
        public float DurationS;
        public int Max;
        public EffectAim Aim;
        public float ReachM;
        /// <summary>The beam: it sweeps a full turn over its spell's bars from the event's yaw.</summary>
        public bool Sweep;
        /// <summary>Why the effect draws at full resolution instead of in the pixel grid, or null (it draws in the grid).</summary>
        public string FullRes;
        public LightCue Light;
        /// <summary>A line drawn with the prefab (the beam), or null.</summary>
        public BeamSpec Beam;

        /// <summary>The prefab's scale for an event's scale (in proportion, from a quarter to 6×).</summary>
        public float ScaleFor(float eventScale) => Scale * Ratio(eventScale, 6f);

        /// <summary>The beam's length for an event's scale (in proportion).</summary>
        public float BeamLengthFor(float eventScale) => Beam == null ? 0 : Beam.LengthM * Ratio(eventScale, 6f);

        /// <summary>The light's radius for an event's scale (in proportion, at most 4×).</summary>
        public float LightRadiusFor(float eventScale) => Light == null ? 0 : Light.RadiusM * Ratio(eventScale, 4f);

        float Ratio(float eventScale, float most) => Math.Max(0.25f, Math.Min(most, eventScale / EventScaleUnit));
    }

    /// <summary>
    /// demo2d's effect library (Data/effects.json, PX7): the CFXR prefab, scale, tint, sorting and
    /// light cue for each timeline effect kind and each projectile in flight. The client pools
    /// instances of these, driven by the timeline's effect events.
    /// </summary>
    public sealed class EffectLibrary
    {
        public const string File = "effects.json";
        public int LightPool;
        public readonly Dictionary<string, EffectSpec> Kinds = new Dictionary<string, EffectSpec>(StringComparer.Ordinal);
        public readonly Dictionary<string, EffectSpec> Flights = new Dictionary<string, EffectSpec>(StringComparer.Ordinal);

        public static EffectLibrary Parse(string json)
        {
            var root = JsonValue.Parse(json);
            var lib = new EffectLibrary { LightPool = root["lightPool"].AsInt(24) };
            foreach (var kv in root["kinds"].Members) lib.Kinds[kv.Key] = Spec(kv.Key, kv.Value, false);
            foreach (var kv in root["flights"].Members) lib.Flights[kv.Key] = Spec(kv.Key, kv.Value, true);
            return lib;
        }

        static EffectSpec Spec(string name, JsonValue v, bool flight)
        {
            var aim = v["aim"].AsString("none");
            var layer = v["layer"].AsString("over");
            if (layer != "over" && layer != "ground") throw new FormatException(name + ": layer is 'over' or 'ground', not " + layer);
            var s = new EffectSpec
            {
                Name = name,
                Prefab = v["prefab"].AsString() ?? throw new FormatException(name + ": no prefab"),
                Scale = LightCue.Positive(v["scale"], name + " scale"),
                EventScaleUnit = v.Has("eventScaleUnit") ? LightCue.Positive(v["eventScaleUnit"], name + " eventScaleUnit") : 1f,
                Tint = LightCue.Color(v["tint"], name + " tint"),
                Ground = layer == "ground",
                DurationS = flight ? 0 : LightCue.Positive(v["durationS"], name + " durationS"),
                Max = v["max"].AsInt(0),
                Aim = aim == "side" ? EffectAim.Side : aim == "yaw" ? EffectAim.Yaw : aim == "none" ? EffectAim.None
                    : throw new FormatException(name + ": aim is 'none', 'side' or 'yaw', not " + aim),
                ReachM = v["reachM"].AsFloat(0),
                Sweep = v["sweep"].AsBool(),
                FullRes = v["fullRes"].AsString(),
                Light = LightCue.Parse(v["light"], name),
                Beam = BeamSpec.Parse(v["beam"], name),
            };
            if (s.Max <= 0) throw new FormatException(name + ": max must be at least 1");
            return s;
        }

        /// <summary>Every distinct prefab path the library names, in a stable order.</summary>
        public List<string> Prefabs()
        {
            var list = new List<string>();
            foreach (var s in All()) if (!list.Contains(s.Prefab)) list.Add(s.Prefab);
            list.Sort(StringComparer.Ordinal);
            return list;
        }

        public IEnumerable<EffectSpec> All()
        {
            foreach (var s in Kinds.Values) yield return s;
            foreach (var s in Flights.Values) yield return s;
        }

        /// <summary>What a timeline would draw without: effect kinds and projectiles with no entry, and entries naming neither.</summary>
        public List<string> Problems(string[] effectNames, string[] projectileNames)
        {
            var problems = new List<string>();
            foreach (var e in effectNames) if (!Kinds.ContainsKey(e)) problems.Add("no entry for the effect kind " + e);
            foreach (var p in projectileNames) if (!Flights.ContainsKey(p)) problems.Add("no flight for the projectile " + p);
            foreach (var k in Kinds.Keys) if (Array.IndexOf(effectNames, k) < 0) problems.Add("the kind " + k + " is no timeline effect");
            foreach (var k in Flights.Keys) if (Array.IndexOf(projectileNames, k) < 0) problems.Add("the flight " + k + " is no projectile");
            foreach (var s in All())
            {
                if (s.Sweep && s.Aim != EffectAim.Yaw) problems.Add(s.Name + ": a sweep needs aim 'yaw'");
                if (s.Beam != null && !s.Sweep) problems.Add(s.Name + ": a beam needs a sweep");
            }
            return problems;
        }

        /// <summary>The beam's sweep: the duration of the line spell with this effect, at song time t (the sim's: its bars of the beat there).</summary>
        public static double SweepSeconds(SongMap map, string rosterJson, string effect, double t)
        {
            int bars = 1;
            foreach (var kv in JsonValue.Parse(rosterJson)["spells"].Members)
                if (kv.Value["effect"].AsString() == effect && kv.Value["shape"].AsString() == "line") bars = kv.Value["bars"].AsInt(1);
            double b = Math.Floor(map.BeatIndexAt(t) + 1e-6);
            double beat = map.TimeOfBeatIndex(b + 1) - map.TimeOfBeatIndex(b);
            return bars * map.BeatsPerBarAt(1) * beat;
        }
    }
}
