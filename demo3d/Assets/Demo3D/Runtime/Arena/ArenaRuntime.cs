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
    /// The scene finishes loading on the next frame; <see cref="Ready"/> says when.
    /// </summary>
    public sealed class ArenaRuntime : IDisposable
    {
        public static float DefaultDensity = 1f;

        public readonly string Rung;
        public readonly float Density;
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
        public Bounds ArenaBounds { get; private set; }
        public event Action<ArenaRuntime> OnReady;
        GameObject _post;

        /// <summary>True if the arena scene for this rung and density is baked into the build.</summary>
        public static bool Available(string rung, float density)
        {
            var index = ArenaIndex.Load();
            return index != null && index.Find(rung, density) != null;
        }

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
        public static ArenaRuntime Load(string rung, float density, Camera camera, Transform volumeParent = null)
        {
            rung = (rung ?? ArenaIndex.E2).ToLowerInvariant();
            if (density <= 0) density = DefaultDensity;
            var index = ArenaIndex.Load() ?? throw new InvalidOperationException("ArenaRuntime: no arena baked (run demo3d unity BakeArena)");
            var entry = index.Find(rung, density)
                ?? throw new ArgumentException($"ArenaRuntime: no arena scene for rung {rung} at density {density.ToString(CultureInfo.InvariantCulture)} (have: {Describe()})");
            var layout = PlaybackData.Load().Layout("arena") ?? throw new InvalidOperationException("ArenaRuntime: no arena layout in PlaybackData");
            var a = new ArenaRuntime(rung, density, entry.scene, ArenaLighting.Parse(layout.text), camera, volumeParent);
            SceneManager.sceneLoaded += a.Loaded;
            SceneManager.LoadScene(entry.scene, LoadSceneMode.Additive);
            return a;
        }

        ArenaRuntime(string rung, float density, string scene, ArenaLighting lighting, Camera camera, Transform volumeParent)
        {
            Rung = rung;
            Density = density;
            SceneName = scene;
            Lighting = lighting;
            _camera = camera;
            _volumeParent = volumeParent;
        }

        /// <summary>Sets up an arena scene that is already open (the editor's stills).</summary>
        public static ArenaRuntime Attach(Scene scene, string rung, float density, ArenaLighting lighting, Camera camera, Transform volumeParent = null)
        {
            var a = new ArenaRuntime(rung, density, scene.name, lighting, camera, volumeParent);
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
            var b = new Bounds(Vector3.zero, Vector3.zero);
            bool first = true;
            foreach (var r in Static)
            {
                if (first) { b = r.bounds; first = false; }
                else b.Encapsulate(r.bounds);
            }
            ArenaBounds = b;

            if (Moon != null) RenderSettings.sun = Moon;
            NightLook.ApplyRenderSettings(Lighting);
            if (_camera != null) NightLook.ApplyCamera(_camera, Lighting);
            _post = NightLook.CreateVolume(_volumeParent != null ? _volumeParent : root).gameObject;

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
            Ready = true;
            Debug.Log($"ArenaRuntime: {SceneName} ready: {Static.Count} kit renderers, {Lights.Count} realtime lights, rung {Rung}");
            OnReady?.Invoke(this);
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

        public void Dispose()
        {
            SceneManager.sceneLoaded -= Loaded;
            ShadowMap?.Dispose();
            ShadowMap = null;
            StaticShadowMap.Disable();
            if (_post != null)
            {
                if (Application.isPlaying) UnityEngine.Object.Destroy(_post);
                else UnityEngine.Object.DestroyImmediate(_post);
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
        readonly ProfilerRecorder _draws, _batches, _setPass, _tris, _casters;

        public RenderStats()
        {
            _draws = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count");
            _batches = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Batches Count");
            _setPass = ProfilerRecorder.StartNew(ProfilerCategory.Render, "SetPass Calls Count");
            _tris = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Triangles Count");
            _casters = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Shadow Casters Count");
        }

        static long? Read(ProfilerRecorder r) => r.Valid && r.Count > 0 ? r.LastValue : (long?)null;

        public long? DrawCalls => Read(_draws);
        public long? Batches => Read(_batches);
        public long? SetPassCalls => Read(_setPass);
        public long? Triangles => Read(_tris);
        public long? ShadowCasters => Read(_casters);

        public void Dispose()
        {
            _draws.Dispose();
            _batches.Dispose();
            _setPass.Dispose();
            _tris.Dispose();
            _casters.Dispose();
        }
    }
}
