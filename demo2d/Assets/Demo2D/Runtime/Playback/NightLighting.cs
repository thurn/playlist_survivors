using System;
using System.Collections.Generic;
using Demo2D.Choreography;
using PS.Choreography;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Demo2D.Runtime.Playback
{
    /// <summary>
    /// The night (PX7; README, Effects and lights), from Data/night-lighting.json: the moon (the 2D
    /// renderer's global light), a warm light on the hero, moonlight shafts (freeform lights),
    /// fireflies around the trees (glowing dots with small lights), glowing umbrella plants, and
    /// the present camera's bloom and colour grade. Everything moving is a function of the song
    /// time, so a seek lights the same. Point lights are placed only in view, from capped pools.
    /// </summary>
    public sealed class NightLighting : IDisposable
    {
        /// <summary>Fireflies draw over the crowd, under the effects.</summary>
        const int FireflyOrder = EffectLayer.OverOrder - 1000;

        sealed class Firefly
        {
            public Vector2 Home;
            public float Px, Py, Phx, Phy, Blink, PhB;
            public SpriteRenderer Dot;
        }

        readonly GameObject _root;
        readonly Light2D _moon, _hero;
        readonly float _heroHeight;
        readonly Light2D[] _shafts;
        readonly float[] _shaftPhase;
        readonly float _shaftIntensity, _shaftPeriod;
        readonly Firefly[] _flies;
        readonly float _flyDrift, _flyLightRadius, _flyLightIntensity, _flyGlow;
        readonly Color _flyColor;
        readonly Vector2[] _plants;
        readonly float[] _plantPhase;
        readonly Color _plantColor;
        readonly float _plantRadius, _plantIntensity, _plantPeriod;
        readonly Material _glow;
        readonly Texture2D _dotTex;
        readonly Sprite _dot;
        readonly VolumeProfile _profile;

        public readonly LightPool FireflyLights, PlantLights;
        /// <summary>The shafts, fireflies and plants (the moon, the hero's light and the grade stay).</summary>
        public bool Extras = true;

        public NightLighting(string json, ForestArena arena, Shader glow, Transform parent)
        {
            var j = JsonValue.Parse(json);
            _root = new GameObject("Night");
            _root.transform.SetParent(parent, false);

            _moon = NewLight("Moonlight", Light2D.LightType.Global);
            _moon.color = Rgb(j["moon"]["color"]);
            _moon.intensity = j["moon"]["intensity"].AsFloat(0.25f);

            var h = j["hero"];
            _hero = NewLight("Hero Light", Light2D.LightType.Point);
            _hero.color = Rgb(h["color"]);
            _hero.intensity = h["intensity"].AsFloat(0.8f);
            _hero.pointLightOuterRadius = h["radiusM"].AsFloat(4.5f);
            _hero.pointLightInnerRadius = h["innerM"].AsFloat(0.8f);
            _heroHeight = h["heightM"].AsFloat(1f);

            // Shafts: parallelograms from their foot up and back toward the moon (north-west).
            var s = j["shafts"];
            _shaftIntensity = s["intensity"].AsFloat(0.3f);
            _shaftPeriod = s["periodS"].AsFloat(9f);
            float skew = s["skewM"].AsFloat(2.5f);
            var items = s["items"].Items;
            _shafts = new Light2D[items.Count];
            _shaftPhase = new float[items.Count];
            for (int i = 0; i < items.Count; i++)
            {
                var it = items[i];
                float w = it["widthM"].AsFloat(2f), len = it["lengthM"].AsFloat(6f);
                var l = NewLight("Moon Shaft", Light2D.LightType.Freeform);
                l.transform.localPosition = new Vector3(it["at"][0].AsFloat(), it["at"][1].AsFloat(), 0);
                l.SetShapePath(new[] { new Vector3(-w / 2, 0), new Vector3(w / 2, 0), new Vector3(w / 2 - skew, len), new Vector3(-w / 2 - skew, len) });
                l.shapeLightFalloffSize = s["falloffM"].AsFloat(1.5f);
                l.color = Rgb(s["color"]);
                l.intensity = _shaftIntensity;
                _shafts[i] = l;
                _shaftPhase[i] = 2 * Mathf.PI * Unit(Rng.Hash(0x5AF7, (uint)i));
            }

            // Fireflies: one by each of `count` trees drawn by seed.
            _glow = new Material(glow) { name = "Glow" };
            var f = j["fireflies"];
            _flyGlow = f["glow"].AsFloat(3f);
            _glow.SetFloat("_Intensity", _flyGlow);
            _flyColor = Rgb(f["color"]);
            _flyDrift = f["driftM"].AsFloat(1.4f);
            _flyLightRadius = f["lightRadiusM"].AsFloat(1.3f);
            _flyLightIntensity = f["lightIntensity"].AsFloat(0.5f);
            // A bright texel with dimmer arms, pivoted on the bright texel's corner so it stays on the grid.
            _dotTex = new Texture2D(3, 3, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, name = "Firefly" };
            Color32 o = new Color32(0, 0, 0, 0), arm = new Color32(255, 255, 255, 90), core = new Color32(255, 255, 255, 255);
            _dotTex.SetPixels32(new[] { o, arm, o, arm, core, arm, o, arm, o });
            _dotTex.Apply(false, true);
            _dot = Sprite.Create(_dotTex, new Rect(0, 0, 3, 3), new Vector2(1f / 3, 1f / 3), CharacterAtlas.PixelsPerMetre, 0, SpriteMeshType.FullRect);
            uint seed = (uint)f["seed"].AsInt(7);
            var trees = new List<int>();
            var list = arena.Placements;
            for (int i = 0; i < arena.TerrainStart; i++)
                if (arena.Sprites[list.Props[list.Items[i].Prop].Id].HeightM >= 5) trees.Add(i);
            trees.Sort((a, b) => Rng.Hash(seed, (uint)a).CompareTo(Rng.Hash(seed, (uint)b)));
            float p0 = f["periodS"][0].AsFloat(5), p1 = f["periodS"][1].AsFloat(11), b0 = f["blinkS"][0].AsFloat(2.5f), b1 = f["blinkS"][1].AsFloat(6);
            _flies = new Firefly[Math.Min(f["count"].AsInt(200), trees.Count)];
            for (int i = 0; i < _flies.Length; i++)
            {
                var feet = list.Items[trees[i]].Position;
                uint k = Rng.Hash(seed, (uint)i, 0xF1F1);
                float R(uint salt) => Unit(Rng.Hash(k, salt));
                var dot = new GameObject("Firefly").AddComponent<SpriteRenderer>();
                dot.transform.SetParent(_root.transform, false);
                dot.sprite = _dot;
                dot.sharedMaterial = _glow;
                dot.sortingOrder = FireflyOrder;
                dot.enabled = false;
                _flies[i] = new Firefly
                {
                    Home = new Vector2(feet.X + (R(1) - 0.5f) * 3f, feet.Y + 1f + 2f * R(2)),
                    Px = Mathf.Lerp(p0, p1, R(3)), Py = Mathf.Lerp(p0, p1, R(4)),
                    Phx = 2 * Mathf.PI * R(5), Phy = 2 * Mathf.PI * R(6),
                    Blink = Mathf.Lerp(b0, b1, R(7)), PhB = 2 * Mathf.PI * R(8),
                    Dot = dot,
                };
            }
            FireflyLights = new LightPool("Firefly Lights", f["maxLights"].AsInt(24), _root.transform);

            // Glowing plants: every umbrella plant the arena placed.
            var pl = j["plants"];
            string prefix = pl["prefix"].AsString("forest/umbrella");
            var plants = new List<Vector2>();
            for (int i = 0; i < arena.TerrainStart; i++)
                if (list.Props[list.Items[i].Prop].Id.StartsWith(prefix, StringComparison.Ordinal))
                    plants.Add(new Vector2(list.Items[i].Position.X, list.Items[i].Position.Y + 0.3f));
            _plants = plants.ToArray();
            _plantPhase = new float[_plants.Length];
            for (int i = 0; i < _plants.Length; i++) _plantPhase[i] = 2 * Mathf.PI * Unit(Rng.Hash(0x61A7, (uint)i));
            _plantColor = Rgb(pl["color"]);
            _plantRadius = pl["radiusM"].AsFloat(1.5f);
            _plantIntensity = pl["intensity"].AsFloat(0.6f);
            _plantPeriod = pl["periodS"].AsFloat(4f);
            PlantLights = new LightPool("Plant Lights", pl["maxLights"].AsInt(24), _root.transform);

            _profile = Grade(j["post"]);
            var volume = new GameObject("Night Grade").AddComponent<Volume>();
            volume.transform.SetParent(_root.transform, false);
            volume.isGlobal = true;
            volume.priority = 10;
            volume.sharedProfile = _profile;
        }

        /// <summary>Lights song time t in <paramref name="view"/> (world metres), the hero at <paramref name="hero"/> (feet).</summary>
        public void Frame(double t, Rect view, Vector2 hero)
        {
            _hero.transform.localPosition = new Vector3(hero.x, hero.y + _heroHeight, 0);
            float tf = (float)(t % 3600.0);
            for (int i = 0; i < _shafts.Length; i++)
            {
                _shafts[i].enabled = Extras;
                _shafts[i].intensity = _shaftIntensity * (0.85f + 0.15f * Mathf.Sin(2 * Mathf.PI * tf / _shaftPeriod + _shaftPhase[i]));
            }

            const float margin = 2f;
            var lit = new Rect(view.xMin - margin, view.yMin - margin, view.width + 2 * margin, view.height + 2 * margin);
            for (int i = 0; i < _flies.Length; i++)
            {
                var fly = _flies[i];
                var p = fly.Home + _flyDrift * new Vector2(Mathf.Sin(2 * Mathf.PI * tf / fly.Px + fly.Phx), 0.6f * Mathf.Sin(2 * Mathf.PI * tf / fly.Py + fly.Phy));
                bool on = Extras && lit.Contains(p);
                if (fly.Dot.enabled != on) fly.Dot.enabled = on;
                if (!on) continue;
                // Mostly dim, now and then bright.
                float b = Mathf.Pow(0.5f + 0.5f * Mathf.Sin(2 * Mathf.PI * tf / fly.Blink + fly.PhB), 3f);
                var snapped = new Vector2(Mathf.Round(p.x * CharacterAtlas.PixelsPerMetre), Mathf.Round(p.y * CharacterAtlas.PixelsPerMetre)) / CharacterAtlas.PixelsPerMetre;
                fly.Dot.transform.localPosition = snapped;
                fly.Dot.color = new Color(_flyColor.r, _flyColor.g, _flyColor.b, 0.25f + 0.75f * b);
                FireflyLights.Offer(snapped, _flyColor, _flyLightRadius, _flyLightIntensity * b, i);
            }
            FireflyLights.Commit();

            if (Extras)
                for (int i = 0; i < _plants.Length; i++)
                    if (lit.Contains(_plants[i]))
                        PlantLights.Offer(_plants[i], _plantColor, _plantRadius,
                            _plantIntensity * (0.75f + 0.25f * Mathf.Sin(2 * Mathf.PI * tf / _plantPeriod + _plantPhase[i])), i);
            PlantLights.Commit();
        }

        /// <summary>The present camera's grade on the 1080p image: bloom, a neutral tonemap, colour adjustments, white balance, a shadows lift, a vignette.</summary>
        static VolumeProfile Grade(JsonValue p)
        {
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            profile.name = "Night Grade";
            var b = p["bloom"];
            var bloom = profile.Add<Bloom>(true);
            bloom.threshold.Override(b["threshold"].AsFloat(1f));
            bloom.intensity.Override(b["intensity"].AsFloat(0.7f));
            bloom.scatter.Override(b["scatter"].AsFloat(0.6f));
            bloom.tint.Override(Rgb(b["tint"]));
            bloom.highQualityFiltering.Override(false);
            profile.Add<Tonemapping>(true).mode.Override(TonemappingMode.Neutral);
            var adjust = profile.Add<ColorAdjustments>(true);
            adjust.postExposure.Override(p["exposure"].AsFloat(0));
            adjust.contrast.Override(p["contrast"].AsFloat(0));
            adjust.saturation.Override(p["saturation"].AsFloat(0));
            var wb = profile.Add<WhiteBalance>(true);
            wb.temperature.Override(p["temperature"].AsFloat(0));
            wb.tint.Override(p["tint"].AsFloat(0));
            var shadows = Rgb(p["shadows"]);
            profile.Add<ShadowsMidtonesHighlights>(true).shadows.Override(new Vector4(shadows.r, shadows.g, shadows.b, 0));
            profile.Add<Vignette>(true).intensity.Override(p["vignette"].AsFloat(0));
            return profile;
        }

        Light2D NewLight(string name, Light2D.LightType type)
        {
            var l = new GameObject(name).AddComponent<Light2D>();
            l.transform.SetParent(_root.transform, false);
            l.lightType = type;
            return l;
        }

        static Color Rgb(JsonValue v) => v.Count >= 3 ? new Color(v[0].AsFloat(), v[1].AsFloat(), v[2].AsFloat()) : Color.white;

        static float Unit(uint h) => (h & 0xFFFFFF) / (float)0x1000000;

        public void Dispose()
        {
            FireflyLights.Dispose();
            PlantLights.Dispose();
            SongPlayback.Destroy(_root);
            SongPlayback.Destroy(_glow);
            SongPlayback.Destroy(_dot);
            SongPlayback.Destroy(_dotTex);
            foreach (var c in _profile.components) SongPlayback.Destroy(c);
            SongPlayback.Destroy(_profile);
        }
    }
}
