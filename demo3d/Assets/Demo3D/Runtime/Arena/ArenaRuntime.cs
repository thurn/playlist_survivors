using System;
using System.Collections.Generic;
using System.Globalization;
using Demo3D.Runtime.Playback;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace Demo3D.Runtime.Arena
{
    /// <summary>
    /// The arena at runtime (TD9): loads a baked arena scene additively, makes it the active scene
    /// (its lightmaps, ambient and fog), applies the night look, takes the moon as the sun, and
    /// sets up its rung (environment.md, Environment rendering):
    /// - <c>e0</c>, the stock baseline: the kit as plain GameObjects, every piece a realtime
    ///   shadow caster, no lightmaps, no static shadow map;
    /// - <c>e2</c>: lightmaps baked in batch mode (moon mixed, shadowmask), the static set out of
    ///   the realtime cascades, and the static shadow map rendered at load for the crowd and hero.
    /// TD14's other rungs come with <see cref="ArenaOptions"/>: the GPU Resident Drawer and the
    /// texture cap (<see cref="ArenaPipeline"/>, before the scene loads), the merged scene variant,
    /// and the static set out of the depth-normals prepass (its materials' DepthNormals and
    /// DepthOnly passes switched off on copies).
    /// The scene finishes loading on the next frame; <see cref="Ready"/> says when.
    /// </summary>
    public sealed class ArenaRuntime : IDisposable
    {

        public readonly string Rung;
        public readonly float Density;
        public readonly ArenaOptions Options;
        public readonly string SceneName;
        public readonly ArenaLighting Lighting;
        readonly Camera _camera;
        readonly Transform _volumeParent;
        Scene _scene;
        public bool Ready { get; private set; }
        public Light Moon { get; private set; }
        public Renderer Ground { get; private set; }
        public readonly List<Renderer> Static = new List<Renderer>();
        public readonly List<Light> Lights = new List<Light>();
        public StaticShadowMap ShadowMap { get; private set; }
        /// <summary>The hero's light (<see cref="ArenaLighting.HeroIntensity"/>), or null without one; <see cref="FollowHero"/> moves it.</summary>
        public Light HeroLight { get; private set; }
        public Bounds ArenaBounds { get; private set; }
        public event Action<ArenaRuntime> OnReady;
        /// <summary>The night's post volume (NightLook), once the scene is set up.</summary>
        public Volume Post { get; private set; }
        readonly List<Material> _copies = new List<Material>();

        /// <summary>True if the arena scene for this rung and density is baked into the build.</summary>
        public static bool Available(string rung, float density, bool merged = false)
        {
            var index = ArenaIndex.Load();
            return index != null && index.Find(rung, density, merged) != null;
        }

        public static bool Available(ArenaOptions o) => Available(o.Rung, o.Density, o.Merged);

        public static string Describe()
        {
            var index = ArenaIndex.Load();
            if (index == null) return "none (run demo3d unity BakeArena)";
            var parts = new List<string>();
            foreach (var e in index.entries) parts.Add(e.scene);
            return string.Join(", ", parts);
        }

        /// <summary>
        /// Starts loading the arena. <paramref name="camera"/> gets the night's clear colour and
        /// post; the post volume goes under <paramref name="volumeParent"/> (or the scene root).
        /// </summary>
        public static ArenaRuntime Load(string rung, float density, Camera camera, Transform volumeParent = null) =>
            Load(new ArenaOptions { Rung = (rung ?? ArenaIndex.E2).ToLowerInvariant(), Density = density }, camera, volumeParent);

        public static ArenaRuntime Load(ArenaOptions o, Camera camera, Transform volumeParent = null)
        {
            o = o.Clone();
            if (o.Density <= 0) o.Density = ArenaOptions.AuthoredDensity;
            var index = ArenaIndex.Load() ?? throw new InvalidOperationException("ArenaRuntime: no arena baked (run demo3d unity BakeArena)");
            var entry = index.Find(o.Rung, o.Density, o.Merged)
                ?? throw new ArgumentException($"ArenaRuntime: no arena scene for rung {o.Rung} at density {o.Density.ToString(CultureInfo.InvariantCulture)}" +
                                               (o.Merged ? " merged" : "") + $" (have: {Describe()})");
            var layout = PlaybackData.Load().Layout("arena") ?? throw new InvalidOperationException("ArenaRuntime: no arena layout in PlaybackData");
            ArenaPipeline.Apply(o);
            // `--palette name` swaps the environment's colours for one of the layout's palettes (ps-ocd).
            var a = new ArenaRuntime(o, entry.scene, ArenaLighting.Parse(layout.text, Args.Get("--palette")), camera, volumeParent);
            SceneManager.sceneLoaded += a.Loaded;
            SceneManager.LoadScene(entry.scene, LoadSceneMode.Additive);
            return a;
        }

        ArenaRuntime(ArenaOptions o, string scene, ArenaLighting lighting, Camera camera, Transform volumeParent)
        {
            Options = o;
            Rung = o.Rung;
            Density = o.Density;
            SceneName = scene;
            Lighting = lighting;
            _camera = camera;
            _volumeParent = volumeParent;
        }

        /// <summary>Sets up an arena scene that is already open (the editor's stills).</summary>
        public static ArenaRuntime Attach(Scene scene, string rung, float density, ArenaLighting lighting, Camera camera, Transform volumeParent = null) =>
            Attach(scene, new ArenaOptions { Rung = rung, Density = density }, lighting, camera, volumeParent);

        /// <summary>Sets up an open arena scene with TD14's scene-level switches (prepass); the pipeline's are the caller's.</summary>
        public static ArenaRuntime Attach(Scene scene, ArenaOptions o, ArenaLighting lighting, Camera camera, Transform volumeParent = null)
        {
            var a = new ArenaRuntime(o.Clone(), scene.name, lighting, camera, volumeParent);
            a.Setup(scene);
            return a;
        }

        void Loaded(Scene scene, LoadSceneMode mode)
        {
            if (scene.name != SceneName) return;
            SceneManager.sceneLoaded -= Loaded;
            Setup(scene);
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
            foreach (var r in stat.GetComponentsInChildren<Renderer>(true))
                if (!(r is ParticleSystemRenderer)) Static.Add(r);
            if (!Options.Prepass) LeavePrepass();
            var b = new Bounds(Vector3.zero, Vector3.zero);
            bool first = true;
            foreach (var r in Static)
            {
                if (first) { b = r.bounds; first = false; }
                else b.Encapsulate(r.bounds);
            }
            ArenaBounds = b;

            if (Moon != null) RenderSettings.sun = Moon;
            if (Lighting.Palette != null) ApplyPalette(root);
            NightLook.ApplyRenderSettings(Lighting);
            if (_camera != null) NightLook.ApplyCamera(_camera, Lighting);
            Post = NightLook.CreateVolume(_volumeParent != null ? _volumeParent : root, Lighting);
            if (Lighting.HeroIntensity > 0) CreateHeroLight(root);

            if (Rung == ArenaIndex.E2)
            {
                QualitySettings.shadowmaskMode = ShadowmaskMode.Shadowmask;
                // The static set leaves the realtime cascades: its shadows on the static set are in
                // the lightmaps' shadowmask, and on monsters and the hero in the static shadow map.
                foreach (var r in Static) r.shadowCastingMode = ShadowCastingMode.Off;
                var casters = new List<Renderer>(Static);
                ShadowMap = StaticShadowMap.Render(casters, Moon != null ? Moon.transform.rotation : Lighting.MoonRotation, ArenaBounds);
                Debug.Log($"ArenaRuntime: static shadow map {ShadowMap.Texture.width}² of {ShadowMap.Casters} casters, " +
                          $"texel {ShadowMap.TexelM.x * 100:F1}×{ShadowMap.TexelM.y * 100:F1} cm, {ShadowMap.RenderMs:F0} ms");
            }
            else StaticShadowMap.Disable();
            if (Options.Without != "") Hide(root.Find("Static"));
            Ready = true;
            Debug.Log($"ArenaRuntime: {SceneName} ready: {Static.Count} kit renderers, {Lights.Count} realtime lights, {Options.Label()}");
            OnReady?.Invoke(this);
        }

        /// <summary>
        /// A palette's colours on the loaded scene (ps-ocd): the moon's realtime light (mixed, so its
        /// direct light on the set and the horde follows; the baked bounce keeps the night's), the
        /// ground's tint and the mist's colour. Ambient, fog, vignette and the hero's light read
        /// <see cref="Lighting"/> as the night does.
        /// </summary>
        void ApplyPalette(Transform root)
        {
            if (Moon != null)
            {
                Moon.color = Lighting.MoonColor;
                Moon.intensity = Lighting.MoonIntensity;
            }
            if (Ground != null) Ground.material.SetColor("_BaseColor", Lighting.GroundTint);
            var mist = root.Find("Mist");
            if (Lighting.MistColor.a > 0 && mist != null && mist.TryGetComponent<Renderer>(out var r))
                r.material.SetColor("_MistColor", Lighting.MistColor);
            Debug.Log("ArenaRuntime: palette " + Lighting.Palette);
        }

        /// <summary>
        /// The lit stage around the hero (ps-e8r.5): one unshadowed realtime point light, outside the
        /// flame lights and the effect-light pool. Forward+ lights the set (over its lightmaps) and the
        /// horde with it, so it adds no pass and no shader work beyond one light in the cluster loop.
        /// </summary>
        void CreateHeroLight(Transform root)
        {
            var go = new GameObject("HeroLight");
            go.transform.SetParent(root, false);
            HeroLight = go.AddComponent<Light>();
            HeroLight.type = LightType.Point;
            HeroLight.shadows = LightShadows.None;
            HeroLight.renderMode = LightRenderMode.ForcePixel;
            // `--hero-light i`, `--hero-height m`, `--hero-range m` and `--hero-color r,g,b` override them in a
            // player, for tuning against the look set (intensity 0 turns it off).
            _heroHeightM = Args.GetFloat("--hero-height", Lighting.HeroHeightM);
            HeroLight.range = Args.GetFloat("--hero-range", Lighting.HeroRangeM);
            HeroLight.color = Lighting.HeroColor;
            if (Args.Get("--hero-color") is string c && c.Split(',') is var v && v.Length == 3)
                HeroLight.color = new Color(float.Parse(v[0], CultureInfo.InvariantCulture), float.Parse(v[1], CultureInfo.InvariantCulture),
                    float.Parse(v[2], CultureInfo.InvariantCulture));
            HeroLight.intensity = Args.GetFloat("--hero-light", Lighting.HeroIntensity);
            HeroLight.enabled = HeroLight.intensity > 0;
        }

        /// <summary>Keeps the hero's light above the hero's feet; the demo calls it every frame.</summary>
        public void FollowHero(Vector3 feet)
        {
            if (HeroLight != null) HeroLight.transform.position = feet + Vector3.up * _heroHeightM;
        }

        float _heroHeightM;

        /// <summary>
        /// E6: the static set out of the depth-normals prepass. Its materials are copied with their
        /// DepthNormals and DepthOnly passes off, so the prepass (which feeds SSAO and the depth
        /// texture; with MSAA, URP does no depth priming) draws only the ground and what moves.
        /// </summary>
        void LeavePrepass()
        {
            var copies = new Dictionary<Material, Material>();
            foreach (var r in Static)
            {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    if (mats[i] == null) continue;
                    if (!copies.TryGetValue(mats[i], out var c))
                    {
                        c = new Material(mats[i]) { name = mats[i].name + " (no prepass)" };
                        c.SetShaderPassEnabled("DepthNormals", false);
                        c.SetShaderPassEnabled("DepthNormalsOnly", false);
                        c.SetShaderPassEnabled("DepthOnly", false);
                        copies[mats[i]] = c;
                        _copies.Add(c);
                    }
                    mats[i] = c;
                }
                r.sharedMaterials = mats;
            }
        }

        /// <summary>
        /// The bounds of E3 and E4 (bench only): hides the small props, or the forest's interior,
        /// after the static shadow map took them (an LOD or an impostor would still shade monsters).
        /// </summary>
        void Hide(Transform stat)
        {
            int hidden = 0;
            foreach (Transform group in stat)
            {
                bool forest = group.name == "Forest";
                bool placed = group.name == "Landmarks" || group.name == "Structures";
                foreach (var r in group.GetComponentsInChildren<Renderer>(true))
                {
                    if (r is ParticleSystemRenderer) continue;
                    var s = r.bounds.size;
                    bool small = !placed && Mathf.Max(s.x, Mathf.Max(s.y, s.z)) < ArenaOptions.SmallPropM;
                    if ((Options.Without == ArenaOptions.WithoutForest && forest) || (Options.Without == ArenaOptions.WithoutSmall && small))
                    {
                        r.enabled = false;
                        hidden++;
                    }
                }
            }
            Debug.Log($"ArenaRuntime: without {Options.Without}: {hidden} kit renderers hidden");
        }

        /// <summary>Kit renderers whose bounds the camera sees, and those that cast realtime shadows into the view.</summary>
        public void Count(Camera camera, float shadowDistance, out int visible, out int casters)
        {
            visible = casters = 0;
            if (!Ready) return;
            var planes = GeometryUtility.CalculateFrustumPlanes(camera);
            // The casters' volume: the view out to the shadow distance, swept toward the moon.
            var toLight = Moon != null ? -Moon.transform.forward : Vector3.up;
            Vector3 cam = camera.transform.position;
            foreach (var r in Static)
            {
                if (!r.enabled || !r.gameObject.activeInHierarchy) continue;
                var bounds = r.bounds;
                if (GeometryUtility.TestPlanesAABB(planes, bounds)) visible++;
                if (r.shadowCastingMode == ShadowCastingMode.Off) continue;
                if ((bounds.center - cam).magnitude - bounds.extents.magnitude > shadowDistance + 40f) continue;
                var swept = bounds;
                swept.Encapsulate(new Bounds(bounds.center - toLight * 40f, bounds.size));
                if (GeometryUtility.TestPlanesAABB(planes, swept)) casters++;
            }
        }

        readonly List<Renderer> _maskHidden = new List<Renderer>();

        /// <summary>
        /// TD15's ID render (the readability measure): hides what must not occlude or tint it, the
        /// ground, the atmosphere's cards (mist, runes, glow pools), every particle renderer and the transparent kit; the kit
        /// itself stays and draws black with its depth (LitStaticShadow under _Demo3DIdMask).
        /// </summary>
        public void SetIdMask(bool on)
        {
            if (!Ready) return;
            if (on)
            {
                _maskHidden.Clear();
                var root = Ground != null ? Ground.transform.parent : null;
                if (Ground != null) _maskHidden.Add(Ground);
                if (root != null)
                {
                    foreach (var name in new[] { "Mist", "Runes", "GlowPools" })
                    {
                        var t = root.Find(name);
                        if (t != null && t.TryGetComponent<Renderer>(out var r)) _maskHidden.Add(r);
                    }
                    _maskHidden.AddRange(root.GetComponentsInChildren<ParticleSystemRenderer>(false));
                    // And any transparent kit renderer (the candles' additive glow cards).
                    foreach (var r in Static) if (r.sharedMaterial != null && r.sharedMaterial.renderQueue >= 2500) _maskHidden.Add(r);
                }
                _maskHidden.RemoveAll(r => r == null || !r.enabled);
                foreach (var r in _maskHidden) r.enabled = false;
            }
            else
            {
                foreach (var r in _maskHidden) if (r != null) r.enabled = true;
                _maskHidden.Clear();
            }
        }

        public void Dispose()
        {
            SceneManager.sceneLoaded -= Loaded;
            ShadowMap?.Dispose();
            ShadowMap = null;
            StaticShadowMap.Disable();
            foreach (var c in _copies)
            {
                if (Application.isPlaying) UnityEngine.Object.Destroy(c);
                else UnityEngine.Object.DestroyImmediate(c);
            }
            _copies.Clear();
            ArenaPipeline.Restore();
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

    /// <summary>
    /// Unity's own render counters (draw calls, batches, set-pass calls, triangles, shadow casters)
    /// where the player exposes them; a counter the player doesn't expose reads null.
    /// </summary>
    public sealed class RenderStats : IDisposable
    {
        readonly ProfilerRecorder _draws, _batches, _setPass, _tris, _casters, _gfx;

        public RenderStats()
        {
            _draws = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count");
            _batches = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Batches Count");
            _setPass = ProfilerRecorder.StartNew(ProfilerCategory.Render, "SetPass Calls Count");
            _tris = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Triangles Count");
            _casters = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Shadow Casters Count");
            _gfx = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "Gfx Used Memory");
        }

        static long? Read(ProfilerRecorder r) => r.Valid && r.Count > 0 ? r.LastValue : (long?)null;

        public long? DrawCalls => Read(_draws);
        public long? Batches => Read(_batches);
        public long? SetPassCalls => Read(_setPass);
        public long? Triangles => Read(_tris);
        public long? ShadowCasters => Read(_casters);
        /// <summary>Graphics memory in use (textures, meshes, buffers, render targets), for E5.</summary>
        public long? GfxUsedBytes => Read(_gfx);

        public void Dispose()
        {
            _draws.Dispose();
            _batches.Dispose();
            _setPass.Dispose();
            _tris.Dispose();
            _casters.Dispose();
            _gfx.Dispose();
        }
    }
}
