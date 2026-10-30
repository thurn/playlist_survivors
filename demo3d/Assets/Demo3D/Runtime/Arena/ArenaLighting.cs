using System.Collections.Generic;
using PS.Choreography;
using UnityEngine;

namespace Demo3D.Runtime.Arena
{
    /// <summary>
    /// The night's lighting parameters (environment.md, Lighting the night), from the
    /// <c>lighting</c> object of the arena's layout parameters (arena-layout.json, or an
    /// environment's resolved over it: LayoutEnvironment). The scene
    /// generator (ArenaBaker) places the moon and the lights from them, and the runtime applies the
    /// ambient, fog and grading from them, so the look has one tracked source.
    /// </summary>
    public sealed class ArenaLighting
    {
        public sealed class PropLight
        {
            public string Prop;
            public float HeightM = 1.2f, RangeM = 8f, Intensity = 3f;
            public Color Color = new Color(1f, 0.55f, 0.25f);
            public bool Flicker;
        }

        public float MoonElevationDeg = 30f, MoonYawDeg = -35f, MoonIntensity = 0.5f;
        public Color MoonColor = new Color(0.55f, 0.66f, 1f);
        public Color AmbientSky = new Color(0.10f, 0.13f, 0.22f), AmbientEquator = new Color(0.06f, 0.08f, 0.12f),
            AmbientGround = new Color(0.03f, 0.03f, 0.04f);
        public Color FogColor = new Color(0.04f, 0.06f, 0.10f);
        /// <summary>
        /// The fog's mode: exponential (<see cref="FogDensity"/>) or linear in view depth, starting and
        /// ending at multiples of the camera's focus distance (its view axis to the ground), so the
        /// fighting ground at the frame's centre stays clear at every view width and only the far
        /// edge of the frame fades into the clear colour.
        /// </summary>
        public FogMode FogMode = FogMode.Exponential;
        public float FogDensity = 0.012f;
        public float FogStartAtFocus = 1.1f, FogEndAtFocus = 2.4f;
        /// <summary>TD15: the horde's extra ambient fill (added to 1) and its rim light (linear rgb), CrowdSkinned's globals.</summary>
        public float CrowdFill;
        public Color CrowdRim = Color.black;
        /// <summary>The rim's falloff exponent on 1 - N·V: higher keeps it to the silhouette's edge.</summary>
        public float CrowdRimPower = 3f;
        /// <summary>The moon's intensity on the horde, as a multiple of its intensity on the set: the horde's key light.</summary>
        public float CrowdKey = 1f;
        /// <summary>
        /// The horde's key colour (ps-bop, epic decision 4; linear rgb): the moon's light on the horde
        /// takes this colour at the moon's luminance, so a coloured moon on the set leaves the type
        /// hues alone. Clear: the moon's own colour.
        /// </summary>
        public Color CrowdKeyColor = Color.clear;
        /// <summary>The horde's hit flash (linear HDR rgb, added as emission at full flash); clear keeps the material's.</summary>
        public Color CrowdFlash = Color.clear;
        /// <summary>
        /// ps-e8r.10: the knee (linear) above which the horde's lit colour rolls off toward twice it, hue
        /// kept, so key-lit bright swatches (yellow caps, pale hides) and hit flashes stay below the
        /// effects, the brightest values on screen. 0 is off.
        /// </summary>
        public float CrowdShoulder;
        /// <summary>
        /// The hero's light (ps-e8r.5): one unshadowed realtime point light <see cref="HeroHeightM"/>
        /// above the hero's feet, lighting the set and the horde in a warm pool that fades out by
        /// <see cref="HeroRangeM"/>, so the frame's centre is a lit stage. Intensity 0 leaves it out.
        /// </summary>
        public float HeroHeightM = 4f, HeroRangeM = 12f, HeroIntensity;
        public Color HeroColor = new Color(1f, 0.78f, 0.55f);
        /// <summary>The post stack's vignette (NightLook.CreateProfile): intensity, smoothness and its tint.</summary>
        public float VignetteIntensity = 0.26f, VignetteSmoothness = 0.45f;
        public Color VignetteColor = new Color(0.01f, 0.015f, 0.04f);
        /// <summary>
        /// The post stack's base values (NightLook.CreateProfile; ps-f8a.4): colour adjustments' exposure,
        /// contrast and saturation, white balance's temperature, bloom, and the tonemapper (neutral or
        /// aces). The post feel's offsets ride on top of them.
        /// </summary>
        public float PostExposure = 0.8f, PostContrast = 25f, PostSaturation = 30f, PostTemperature;
        public float BloomThreshold = 1.5f, BloomIntensity = 1f, BloomScatter = 0.65f;
        public string Tonemap = "neutral";
        /// <summary>
        /// The grade's tints (ps-bop, epic decision 5; NightLook.Grade): the display-referred colour's
        /// multipliers in the shadows, midtones and highlights. The night's warm the midtones; an
        /// environment may set its own (all 1: no tint, the S-curve only).
        /// </summary>
        public Vector3 GradeShadow = new Vector3(0.97f, 0.99f, 1.05f), GradeMid = new Vector3(1.06f, 1.0f, 0.90f),
            GradeHigh = new Vector3(1.02f, 1.0f, 0.97f);
        /// <summary>The post feel's amounts (ps-e8r.18): offsets on the post stack that follow the music.</summary>
        public Playback.PostFeel.Amounts PostFeel = new Playback.PostFeel.Amounts();
        public int MaxFlameLights = 8;
        /// <summary>Flickering flame lights (realtime, at most <see cref="MaxFlameLights"/>), in priority order.</summary>
        public readonly List<PropLight> Flames = new List<PropLight>();
        /// <summary>Static glow (glowing trees, crystals): baked in the lit scene, realtime at rung E0.</summary>
        public readonly List<PropLight> Glow = new List<PropLight>();

        /// <summary>
        /// The palette swap (ps-ocd): the named entry of the layout's <c>palettes</c> applied over
        /// <c>lighting</c>, or null for the night as authored. A palette is a partial
        /// <c>lighting</c> object, so the keys it leaves out keep their values, plus two keys of its
        /// own, below. It recolours what the runtime sets (the moon's realtime light, ambient, fog and
        /// clear colour, vignette, the hero's light, the ground and the mist), never the baked kit.
        /// </summary>
        public string Palette;
        /// <summary>A palette's <c>groundTint</c> (linear rgb, may pass 1): the ground material's base colour, over its generated albedo.</summary>
        public Color GroundTint = Color.white;
        /// <summary>A palette's <c>mist</c> (linear rgb): the mist cards' colour; clear keeps the baked one.</summary>
        public Color MistColor = Color.clear;
        /// <summary>
        /// The `--roster` set's look (ps-f8a.4): the layout's <c>rosters</c> entry for a set, applied
        /// over <c>lighting</c> before any palette, or null for the default roster. Like a palette it
        /// is a partial <c>lighting</c> object, read by the runtime only, so the baked kit is the night's.
        /// </summary>
        public string RosterLook;

        public Quaternion MoonRotation => Quaternion.Euler(MoonElevationDeg, MoonYawDeg, 0);

        /// <summary>The layout's palette names, in order.</summary>
        public static List<string> Palettes(string layoutJson)
        {
            var names = new List<string>();
            JsonValue palettes = JsonValue.Parse(layoutJson)["palettes"];
            if (palettes.Type == JsonType.Object)
                foreach (var kv in palettes.Members) names.Add(kv.Key);
            return names;
        }

        /// <summary>
        /// The lighting of <paramref name="layoutJson"/>, with the <paramref name="roster"/> set's look
        /// over it (none for null or default, or a set the layout gives none), then
        /// <paramref name="palette"/> over that unless it is null or empty.
        /// </summary>
        public static ArenaLighting Parse(string layoutJson, string palette = null, string roster = null)
        {
            var l = new ArenaLighting();
            JsonValue layout = JsonValue.Parse(layoutJson);
            Read(layout["lighting"], l);
            JsonValue look = layout["rosters"][roster ?? ""];
            if (look.Type == JsonType.Object)
            {
                Read(look, l);
                l.RosterLook = roster;
            }
            if (string.IsNullOrEmpty(palette)) return l;
            JsonValue p = layout["palettes"][palette];
            if (p.Type != JsonType.Object)
                throw new System.ArgumentException($"ArenaLighting: no palette {palette} (have: {string.Join(", ", Palettes(layoutJson))})");
            Read(p, l);
            l.Palette = palette;
            l.GroundTint = Col(p["groundTint"], l.GroundTint);
            l.MistColor = Col(p["mist"], l.MistColor);
            return l;
        }

        /// <summary>Reads a <c>lighting</c> object over <paramref name="l"/>: what it leaves out keeps its value.</summary>
        static void Read(JsonValue root, ArenaLighting l)
        {
            if (root.Type != JsonType.Object) return;
            JsonValue moon = root["moon"];
            l.MoonElevationDeg = moon["elevationDeg"].AsFloat(l.MoonElevationDeg);
            l.MoonYawDeg = moon["yawDeg"].AsFloat(l.MoonYawDeg);
            l.MoonIntensity = moon["intensity"].AsFloat(l.MoonIntensity);
            l.MoonColor = Col(moon["color"], l.MoonColor);
            JsonValue amb = root["ambient"];
            l.AmbientSky = Col(amb["sky"], l.AmbientSky);
            l.AmbientEquator = Col(amb["equator"], l.AmbientEquator);
            l.AmbientGround = Col(amb["ground"], l.AmbientGround);
            JsonValue fog = root["fog"];
            l.FogColor = Col(fog["color"], l.FogColor);
            l.FogMode = fog["mode"].AsString("exponential") == "linear" ? FogMode.Linear : FogMode.Exponential;
            l.FogDensity = fog["density"].AsFloat(l.FogDensity);
            l.FogStartAtFocus = fog["startAtFocus"].AsFloat(l.FogStartAtFocus);
            l.FogEndAtFocus = fog["endAtFocus"].AsFloat(l.FogEndAtFocus);
            JsonValue crowd = root["crowd"];
            l.CrowdFill = crowd["fill"].AsFloat(l.CrowdFill);
            l.CrowdRim = Col(crowd["rim"], l.CrowdRim);
            l.CrowdRimPower = crowd["rimPower"].AsFloat(l.CrowdRimPower);
            l.CrowdKey = crowd["key"].AsFloat(l.CrowdKey);
            l.CrowdKeyColor = Col(crowd["keyColor"], l.CrowdKeyColor);
            l.CrowdFlash = Col(crowd["flash"], l.CrowdFlash);
            l.CrowdShoulder = crowd["shoulder"].AsFloat(l.CrowdShoulder);
            JsonValue hero = root["hero"];
            l.HeroHeightM = hero["heightM"].AsFloat(l.HeroHeightM);
            l.HeroRangeM = hero["rangeM"].AsFloat(l.HeroRangeM);
            l.HeroIntensity = hero["intensity"].AsFloat(l.HeroIntensity);
            l.HeroColor = Col(hero["color"], l.HeroColor);
            JsonValue vignette = root["vignette"];
            l.VignetteIntensity = vignette["intensity"].AsFloat(l.VignetteIntensity);
            l.VignetteSmoothness = vignette["smoothness"].AsFloat(l.VignetteSmoothness);
            l.VignetteColor = Col(vignette["color"], l.VignetteColor);
            JsonValue post = root["post"];
            l.PostExposure = post["exposure"].AsFloat(l.PostExposure);
            l.PostContrast = post["contrast"].AsFloat(l.PostContrast);
            l.PostSaturation = post["saturation"].AsFloat(l.PostSaturation);
            l.PostTemperature = post["temperature"].AsFloat(l.PostTemperature);
            l.Tonemap = post["tonemap"].AsString(l.Tonemap);
            JsonValue grade = post["grade"];
            l.GradeShadow = Vec(grade["shadow"], l.GradeShadow);
            l.GradeMid = Vec(grade["mid"], l.GradeMid);
            l.GradeHigh = Vec(grade["high"], l.GradeHigh);
            JsonValue bloom = post["bloom"];
            l.BloomThreshold = bloom["threshold"].AsFloat(l.BloomThreshold);
            l.BloomIntensity = bloom["intensity"].AsFloat(l.BloomIntensity);
            l.BloomScatter = bloom["scatter"].AsFloat(l.BloomScatter);
            if (root["postFeel"].Type == JsonType.Object) l.PostFeel = Playback.PostFeel.Amounts.Parse(root["postFeel"]);
            JsonValue flames = root["flames"];
            l.MaxFlameLights = flames["maxLights"].AsInt(l.MaxFlameLights);
            Lights(flames["props"], l.Flames, true);
            Lights(root["glow"], l.Glow, false);
        }

        static void Lights(JsonValue v, List<PropLight> into, bool flicker)
        {
            if (v.Type != JsonType.Object) return;
            into.Clear();
            foreach (var kv in v.Members)
                into.Add(new PropLight
                {
                    Prop = kv.Key,
                    HeightM = kv.Value["heightM"].AsFloat(1.2f),
                    RangeM = kv.Value["rangeM"].AsFloat(8f),
                    Intensity = kv.Value["intensity"].AsFloat(3f),
                    Color = Col(kv.Value["color"], flicker ? new Color(1f, 0.55f, 0.25f) : new Color(0.4f, 0.85f, 1f)),
                    Flicker = flicker,
                });
        }

        static Vector3 Vec(JsonValue v, Vector3 fallback) =>
            v.Count >= 3 ? new Vector3(v[0].AsFloat(), v[1].AsFloat(), v[2].AsFloat()) : fallback;

        static Color Col(JsonValue v, Color fallback) =>
            v.Count >= 3 ? new Color(v[0].AsFloat(), v[1].AsFloat(), v[2].AsFloat()) : fallback;
    }
}
