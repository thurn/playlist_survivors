using System.Collections.Generic;
using PS.Choreography;
using UnityEngine;

namespace Demo3D.Runtime.Arena
{
    /// <summary>
    /// The night's lighting parameters (environment.md, Lighting the night), from the
    /// <c>lighting</c> object of the arena's layout parameters (arena-layout.json). The scene
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
        /// <summary>The post feel's amounts (ps-e8r.18): offsets on the post stack that follow the music.</summary>
        public Playback.PostFeel.Amounts PostFeel = new Playback.PostFeel.Amounts();
        public int MaxFlameLights = 8;
        /// <summary>Flickering flame lights (realtime, at most <see cref="MaxFlameLights"/>), in priority order.</summary>
        public readonly List<PropLight> Flames = new List<PropLight>();
        /// <summary>Static glow (glowing trees, crystals): baked in the lit scene, realtime at rung E0.</summary>
        public readonly List<PropLight> Glow = new List<PropLight>();

        public Quaternion MoonRotation => Quaternion.Euler(MoonElevationDeg, MoonYawDeg, 0);

        public static ArenaLighting Parse(string layoutJson)
        {
            var l = new ArenaLighting();
            JsonValue root = JsonValue.Parse(layoutJson)["lighting"];
            if (root.Type != JsonType.Object) return l;
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
            l.CrowdFill = crowd["fill"].AsFloat(0f);
            l.CrowdRim = Col(crowd["rim"], Color.black);
            l.CrowdRimPower = crowd["rimPower"].AsFloat(l.CrowdRimPower);
            l.CrowdKey = crowd["key"].AsFloat(l.CrowdKey);
            l.CrowdFlash = Col(crowd["flash"], Color.clear);
            l.CrowdShoulder = crowd["shoulder"].AsFloat(0f);
            JsonValue hero = root["hero"];
            l.HeroHeightM = hero["heightM"].AsFloat(l.HeroHeightM);
            l.HeroRangeM = hero["rangeM"].AsFloat(l.HeroRangeM);
            l.HeroIntensity = hero["intensity"].AsFloat(l.HeroIntensity);
            l.HeroColor = Col(hero["color"], l.HeroColor);
            JsonValue vignette = root["vignette"];
            l.VignetteIntensity = vignette["intensity"].AsFloat(l.VignetteIntensity);
            l.VignetteSmoothness = vignette["smoothness"].AsFloat(l.VignetteSmoothness);
            l.VignetteColor = Col(vignette["color"], l.VignetteColor);
            l.PostFeel = Playback.PostFeel.Amounts.Parse(root["postFeel"]);
            JsonValue flames = root["flames"];
            l.MaxFlameLights = flames["maxLights"].AsInt(l.MaxFlameLights);
            Lights(flames["props"], l.Flames, true);
            Lights(root["glow"], l.Glow, false);
            return l;
        }

        static void Lights(JsonValue v, List<PropLight> into, bool flicker)
        {
            if (v.Type != JsonType.Object) return;
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

        static Color Col(JsonValue v, Color fallback) =>
            v.Count >= 3 ? new Color(v[0].AsFloat(), v[1].AsFloat(), v[2].AsFloat()) : fallback;
    }
}
