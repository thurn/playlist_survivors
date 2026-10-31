using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace Game.Client.Arena
{
    /// <summary>
    /// The arena at run time (unity-game.md, "The Arena"), demo3d's ArenaRuntime copied and owned:
    /// loads the baked arena scene additively, makes it the active scene (its lightmaps, probes,
    /// ambient and fog), takes its moon as the sun, applies the zone's palette, the night's render
    /// settings and base post stack, and adds the hero's light.
    /// - The static kit's shadows are baked (the ground's shadowmask) and in the static shadow map,
    ///   so it leaves the realtime cascades.
    /// - The crystal structures are out of the bake: they cast realtime shadows and are in the
    ///   static shadow map, which <see cref="RefreshShadows"/> renders again where a structure breaks or regrows.
    /// <see cref="Sites"/> lists each structure site's renderers in the level data's order
    /// (game/Content/arena.json's <c>structures</c>). The scene finishes loading on the next frame;
    /// <see cref="Ready"/> says when.
    /// </summary>
    public sealed class ArenaRuntime : IDisposable
    {
        public const string SitePrefix = "Site ";
        public readonly string SceneName;
        public readonly ArenaLighting Lighting;
        /// <summary>The layout the bake was generated from (game/Content/arena-layout.json at bake time).</summary>
        public readonly string LayoutJson;
        public readonly Shader CrystalShader;
        readonly Camera _camera;
        readonly Transform _volumeParent;
        Scene _scene;
        public bool Ready { get; private set; }
        /// <summary>Why the loaded scene failed to set up, or null.</summary>
        public Exception Failure { get; private set; }
        public Light Moon { get; private set; }
        public Renderer Ground { get; private set; }
        /// <summary>The static kit's renderers: everything under Static but the structures.</summary>
        public readonly List<Renderer> Static = new List<Renderer>();
        /// <summary>Each structure site's renderers, by the site's index in the level data.</summary>
        public readonly List<Renderer[]> Sites = new List<Renderer[]>();
        /// <summary>Every structure site's renderers.</summary>
        public readonly List<Renderer> Structures = new List<Renderer>();
        /// <summary>The realtime point lights the scene keeps (the flames).</summary>
        public readonly List<Light> Lights = new List<Light>();
        /// <summary>The static shadow map, or null in a headless player.</summary>
        public StaticShadowMap ShadowMap { get; private set; }
        /// <summary>The hero's light, or null without one; <see cref="FollowHero"/> moves it.</summary>
        public Light HeroLight { get; private set; }
        public Bounds ArenaBounds { get; private set; }
        public event Action<ArenaRuntime> OnReady;
        /// <summary>The night's post volume (NightLook), once the scene is set up.</summary>
        public UnityEngine.Rendering.Volume Post { get; private set; }
        float _heroHeightM;

        /// <summary>
        /// Starts loading the baked arena in <paramref name="palette"/>'s look (the default zone's
        /// unless given; empty for the night as the bake lit it). <paramref name="camera"/> gets the
        /// night's clear colour and post; the post volume goes under <paramref name="volumeParent"/>
        /// (or the scene root). Throws when the build has no arena.
        /// </summary>
        public static ArenaRuntime Load(Camera camera, string palette = ArenaLighting.DefaultPalette, Transform volumeParent = null)
        {
            var bake = ArenaBake.Load() ?? throw new InvalidOperationException("ArenaRuntime: no arena baked into this build (run `game unity BakeArena`, or `game build`)");
            var a = new ArenaRuntime(bake, ArenaLighting.Parse(bake.layoutJson, palette), camera, volumeParent);
            SceneManager.sceneLoaded += a.Loaded;
            SceneManager.LoadScene(bake.scene, LoadSceneMode.Additive);
            return a;
        }

        ArenaRuntime(ArenaBake bake, ArenaLighting lighting, Camera camera, Transform volumeParent)
        {
            SceneName = bake.scene;
            LayoutJson = bake.layoutJson;
            CrystalShader = bake.crystal;
            Lighting = lighting;
            _camera = camera;
            _volumeParent = volumeParent;
        }

        void Loaded(Scene scene, LoadSceneMode mode)
        {
            if (scene.name != SceneName) return;
            SceneManager.sceneLoaded -= Loaded;
            try { Setup(scene); }
            catch (Exception e)
            {
                Failure = e;
                throw;
            }
        }

        void Setup(Scene scene)
        {
            _scene = scene;
            SceneManager.SetActiveScene(scene);
            Transform root = null;
            foreach (var go in scene.GetRootGameObjects())
                if (go.name == "Arena") root = go.transform;
            if (root == null) throw new InvalidOperationException("ArenaRuntime: scene " + SceneName + " has no Arena root");
            if (root.GetComponent<ArenaClock>() == null) root.gameObject.AddComponent<ArenaClock>();
            foreach (var l in root.GetComponentsInChildren<Light>(true))
            {
                if (l.type == LightType.Directional) Moon = l;
                else if (l.enabled && !(l.bakingOutput.isBaked && l.bakingOutput.lightmapBakeType == LightmapBakeType.Baked)) Lights.Add(l);
            }
            var ground = root.Find("Ground");
            Ground = ground != null ? ground.GetComponent<Renderer>() : null;
            var stat = root.Find("Static") ?? throw new InvalidOperationException("ArenaRuntime: no Static group in " + SceneName);
            FindSites(stat.Find("Structures"));
            var structures = new HashSet<Renderer>(Structures);
            foreach (var r in stat.GetComponentsInChildren<Renderer>(true))
                if (!(r is ParticleSystemRenderer) && !structures.Contains(r)) Static.Add(r);
            var b = new Bounds(Vector3.zero, Vector3.zero);
            bool first = true;
            foreach (var r in Static)
            {
                if (first) { b = r.bounds; first = false; }
                else b.Encapsulate(r.bounds);
            }
            ArenaBounds = b;

            if (Moon != null)
            {
                RenderSettings.sun = Moon;
                // The palette recolours the moon's realtime light (mixed: its direct light on the set and
                // the structures follows; the baked bounce keeps the night's).
                Moon.color = Lighting.MoonColor;
                Moon.intensity = Lighting.MoonIntensity;
            }
            if (Lighting.Palette != null) ApplyPalette(root);
            NightLook.ApplyRenderSettings(Lighting);
            if (_camera != null) NightLook.ApplyCamera(_camera, Lighting);
            Post = NightLook.CreateVolume(_volumeParent != null ? _volumeParent : root, Lighting);
            if (Lighting.HeroIntensity > 0) CreateHeroLight(root);
            SetBeatAtRest();

            QualitySettings.shadowmaskMode = ShadowmaskMode.Shadowmask;
            foreach (var r in Static) r.shadowCastingMode = ShadowCastingMode.Off;
            RenderShadowMap();
            Ready = true;
            Debug.Log($"ArenaRuntime: {SceneName} ready: {Static.Count} kit renderers, {Sites.Count} structure sites ({Structures.Count} renderers), " +
                      $"{Lights.Count} realtime lights, {LightmapSettings.lightProbes?.count ?? 0} light probes, palette {Lighting.Palette ?? "none"}");
            OnReady?.Invoke(this);
        }

        /// <summary>The sites under Static/Structures, each a "Site N" group of pieces; their opaque mesh renderers.</summary>
        void FindSites(Transform structures)
        {
            if (structures == null) return;
            var byIndex = new SortedDictionary<int, Renderer[]>();
            foreach (Transform site in structures)
            {
                if (!site.name.StartsWith(SitePrefix, StringComparison.Ordinal)
                    || !int.TryParse(site.name.Substring(SitePrefix.Length), NumberStyles.Integer, CultureInfo.InvariantCulture, out int i))
                    throw new InvalidOperationException("ArenaRuntime: unexpected " + site.name + " under Static/Structures");
                var list = new List<Renderer>();
                foreach (var r in site.GetComponentsInChildren<MeshRenderer>(true))
                {
                    bool opaque = true;
                    foreach (var m in r.sharedMaterials) opaque &= m != null && m.renderQueue < (int)RenderQueue.Transparent;
                    if (opaque) list.Add(r);
                }
                byIndex[i] = list.ToArray();
            }
            int expected = 0;
            foreach (var kv in byIndex)
            {
                if (kv.Key != expected++) throw new InvalidOperationException($"ArenaRuntime: structure sites skip {expected - 1}");
                Sites.Add(kv.Value);
                Structures.AddRange(kv.Value);
            }
        }

        /// <summary>
        /// Renders the static shadow map again where <paramref name="regions"/> (world bounds of the
        /// structures that changed) fall on it, from the kit and the structures enabled now: call after a
        /// structure breaks (its renderers disabled) or regrows. The kit's own baked shadows never held a
        /// structure, so nothing else needs patching.
        /// </summary>
        public void RefreshShadows(IReadOnlyList<Bounds> regions) => ShadowMap?.Redraw(_casters, regions);

        readonly List<Renderer> _casters = new List<Renderer>();

        void RenderShadowMap()
        {
            // A headless player (-nographics) has no device to render it with.
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) return;
            _casters.Clear();
            _casters.AddRange(Static);
            _casters.AddRange(Structures);
            ShadowMap = StaticShadowMap.Render(_casters, Moon != null ? Moon.transform.rotation : Lighting.MoonRotation, ArenaBounds);
            Debug.Log($"ArenaRuntime: static shadow map {ShadowMap.Texture.width}² of {ShadowMap.Casters} casters, " +
                      $"texel {ShadowMap.TexelM.x * 100:F1}×{ShadowMap.TexelM.y * 100:F1} cm, {ShadowMap.RenderMs:F0} ms");
        }

        /// <summary>The palette's ground tint and mist colour on the loaded scene; the rest reads <see cref="Lighting"/>.</summary>
        void ApplyPalette(Transform root)
        {
            if (Ground != null) Ground.material.SetColor("_BaseColor", Lighting.GroundTint);
            var mist = root.Find("Mist");
            if (Lighting.MistColor.a > 0 && mist != null && mist.TryGetComponent<Renderer>(out var r))
                r.material.SetColor("_MistColor", Lighting.MistColor);
        }

        /// <summary>
        /// The lit stage around the hero: one unshadowed realtime point light, outside the flame lights
        /// and the effects' lights. Forward+ lights the set, the crystal and the horde with it.
        /// </summary>
        void CreateHeroLight(Transform root)
        {
            var go = new GameObject("HeroLight");
            go.transform.SetParent(root, false);
            HeroLight = go.AddComponent<Light>();
            HeroLight.type = LightType.Point;
            HeroLight.shadows = LightShadows.None;
            HeroLight.renderMode = LightRenderMode.ForcePixel;
            HeroLight.range = Lighting.HeroRangeM;
            HeroLight.color = Lighting.HeroColor;
            HeroLight.intensity = Lighting.HeroIntensity;
            _heroHeightM = Lighting.HeroHeightM;
        }

        static readonly int HeroPosId = Shader.PropertyToID("_SL_HeroPos");

        /// <summary>Keeps the hero's light above the hero's feet and the canopy fade and beat wave on it; call every frame.</summary>
        public void FollowHero(Vector3 feet)
        {
            if (HeroLight != null) HeroLight.transform.position = feet + Vector3.up * _heroHeightM;
            CanopyFade.SetHero(feet);
            Shader.SetGlobalVector(HeroPosId, feet);
        }

        /// <summary>
        /// The structures' beat globals at rest (StructureCommon.hlsl), until the look's beat feel drives
        /// them: a wave too fast to see, half a beat in, so the crystal's glints hold still.
        /// </summary>
        static void SetBeatAtRest()
        {
            Shader.SetGlobalFloat("_SL_Beat", 0.5f);
            Shader.SetGlobalFloat("_SL_WaveSpeed", 1e6f);
            Shader.SetGlobalFloat("_SL_Pulse", 0f);
            Shader.SetGlobalFloat("_SL_BarPulse", 0f);
        }

        public void Dispose()
        {
            SceneManager.sceneLoaded -= Loaded;
            ShadowMap?.Dispose();
            ShadowMap = null;
            StaticShadowMap.Disable();
            CanopyFade.Clear();
            if (Post != null)
            {
                if (Application.isPlaying) UnityEngine.Object.Destroy(Post.gameObject);
                else UnityEngine.Object.DestroyImmediate(Post.gameObject);
                Post = null;
            }
            if (Application.isPlaying && _scene.IsValid() && _scene.isLoaded && SceneManager.sceneCount > 1) SceneManager.UnloadSceneAsync(_scene);
            Ready = false;
        }
    }
}
