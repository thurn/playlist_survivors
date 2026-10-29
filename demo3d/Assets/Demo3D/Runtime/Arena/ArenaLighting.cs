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
        public float FogDensity = 0.012f;
        /// <summary>TD15: the horde's extra ambient fill (added to 1) and its rim light (linear rgb), CrowdSkinned's globals.</summary>
        public float CrowdFill;
        public Color CrowdRim = Color.black;
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
            l.FogDensity = fog["density"].AsFloat(l.FogDensity);
            JsonValue crowd = root["crowd"];
            l.CrowdFill = crowd["fill"].AsFloat(0f);
            l.CrowdRim = Col(crowd["rim"], Color.black);
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
