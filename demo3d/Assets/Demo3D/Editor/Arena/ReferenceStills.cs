using System.Collections.Generic;
using System.IO;
using System.Linq;
using Demo3D.EditorTools.Assets;
using Demo3D.Runtime.Arena;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace Demo3D.EditorTools.Arena
{
    /// <summary>
    /// TD15's reference stills (environment.md, Making it look good): a pack's own art-directed
    /// demo scene, opened in batch mode, its Standard materials swapped for URP Lit in memory
    /// (UrpMaterials; nothing is saved), rendered through our game camera (55° pitch, 30° vertical
    /// field of view) at our view widths, from the framings that see the most of its dressing.
    /// Each framing twice: under the scene's own lights (to the gitignored captures/, `-ownOut`: a
    /// screenshot of the vendor's demo isn't evidence), and under our night (the arena's moon,
    /// ambient, fog and post stack, or `-environment`'s; evidence), so its density and dressing can be
    /// set beside our shots.
    /// The demo scenes are vendor files: sync them into Assets/Vendor only for the run (a
    /// temporary manifest entry) and never commit them; only the small tagged JPEGs are kept.
    /// `demo3d unity Demo3D.EditorTools.Arena.ReferenceStills.Run -scene Assets/Vendor/.../Demo.unity
    /// -name elven -out evidence/td15/reference [-environment snow] [-views 30,40] [-count 3] [-width 960]`.
    /// </summary>
    public static class ReferenceStills
    {
        public static void Run()
        {
            string scenePath = TaskArgs.Get("-scene") ?? throw new System.Exception("ReferenceStills: -scene <path>");
            string name = TaskArgs.Get("-name", Path.GetFileNameWithoutExtension(scenePath));
            string outDir = TaskArgs.Get("-out", "evidence/td15/reference");
            // The scene under its own lights is a screenshot of the vendor's demo, not our game: it goes to
            // the gitignored captures/, never to evidence (the scene milestone's review).
            string ownDir = TaskArgs.Get("-ownOut", "../captures/td15/reference-own");
            int width = int.Parse(TaskArgs.Get("-width", "960"));
            int count = int.Parse(TaskArgs.Get("-count", "3"));
            var views = TaskArgs.Get("-views", "30,40").Split(',').Select(s => float.Parse(s, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
            Directory.CreateDirectory(outDir);
            Directory.CreateDirectory(ownDir);

            var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            foreach (var root in scene.GetRootGameObjects()) UrpMaterials.Convert(root);
            int fixedUp = 0;
            foreach (var root in scene.GetRootGameObjects()) fixedUp += ConvertUnsupported(root);
            var terrainLit = Shader.Find("Universal Render Pipeline/Terrain/Lit");
            foreach (var t in Object.FindObjectsByType<Terrain>(FindObjectsInactive.Include))
                if (terrainLit != null) t.materialTemplate = new Material(terrainLit);
            var renderers = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Renderer>())
                .Where(r => r.enabled && r.gameObject.activeInHierarchy && !(r is ParticleSystemRenderer) && r.bounds.size.y < 40f).ToList();
            foreach (var cam in Object.FindObjectsByType<Camera>(FindObjectsInactive.Include)) cam.enabled = false;
            var ownLights = Object.FindObjectsByType<Light>(FindObjectsInactive.Exclude).ToList();
            var ownVolumes = Object.FindObjectsByType<Volume>(FindObjectsInactive.Exclude).ToList();
            var ownAmbient = (RenderSettings.ambientMode, RenderSettings.ambientSkyColor, RenderSettings.ambientEquatorColor,
                RenderSettings.ambientGroundColor, RenderSettings.ambientLight, RenderSettings.fog, RenderSettings.fogColor,
                RenderSettings.fogDensity, RenderSettings.fogMode, RenderSettings.skybox, RenderSettings.sun);
            Debug.Log("ReferenceStills: shaders " + string.Join(", ", renderers.SelectMany(r => r.sharedMaterials)
                .GroupBy(m => m == null ? "(none)" : m.shader == null ? "(no shader)" : m.shader.name).Select(g => g.Key + " x" + g.Count())));
            Debug.Log($"ReferenceStills: {name}: {renderers.Count} renderers, {ownLights.Count} lights, {ownVolumes.Count} volumes, {fixedUp} custom-shader materials as plain Lit");

            var cam0 = ArenaStills.GameCamera();
            var lighting = ArenaLighting.Parse(ArenaEnvironment.Load(TaskArgs.Get("-environment")).Json);
            try
            {
                foreach (float view in views)
                {
                    var picks = Framings(renderers, view, count);
                    for (int k = 0; k < picks.Count; k++)
                    {
                        var (focus, seen) = picks[k];
                        ArenaStills.Frame(cam0, focus, view);
                        string stem = $"ref-{name}-{view:0}m-{k + 1}";
                        // As the pack shows it: its own lights, ambient and post, our camera.
                        cam0.clearFlags = ownAmbient.skybox != null ? CameraClearFlags.Skybox : CameraClearFlags.SolidColor;
                        cam0.backgroundColor = ownAmbient.fogColor;
                        ArenaStills.Render(cam0, Path.Combine(ownDir, stem + "-own.jpg"), width, $"TD15 reference still {name} own lights {view:0} m ({seen} pieces)");
                        // Under our night.
                        foreach (var l in ownLights) l.enabled = false;
                        foreach (var v in ownVolumes) v.enabled = false;
                        var moon = new GameObject("Moon").AddComponent<Light>();
                        moon.type = LightType.Directional;
                        moon.transform.rotation = lighting.MoonRotation;
                        moon.color = lighting.MoonColor;
                        moon.intensity = lighting.MoonIntensity;
                        moon.shadows = LightShadows.Soft;
                        RenderSettings.sun = moon;
                        NightLook.ApplyRenderSettings(lighting);
                        NightLook.ApplyCamera(cam0, lighting);
                        var post = NightLook.CreateVolume(null, lighting);
                        ArenaStills.Render(cam0, Path.Combine(outDir, stem + "-night.jpg"), width, $"TD15 reference still {name} our night {view:0} m ({seen} pieces)");
                        Object.DestroyImmediate(post.gameObject);
                        Object.DestroyImmediate(moon.gameObject);
                        foreach (var l in ownLights) l.enabled = true;
                        foreach (var v in ownVolumes) v.enabled = true;
                        (RenderSettings.ambientMode, RenderSettings.ambientSkyColor, RenderSettings.ambientEquatorColor,
                            RenderSettings.ambientGroundColor, RenderSettings.ambientLight, RenderSettings.fog, RenderSettings.fogColor,
                            RenderSettings.fogDensity, RenderSettings.fogMode, RenderSettings.skybox, RenderSettings.sun) = ownAmbient;
                    }
                }
            }
            finally
            {
                Object.DestroyImmediate(cam0.gameObject);
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single); // the vendor scene is never saved
            }
        }

        /// <summary>
        /// Materials UrpMaterials leaves alone but URP can't draw (the packs' built-in Shader Graphs:
        /// triplanar rock, foliage, water) become plain URP Lit copies in memory: their albedo
        /// texture if one is named like one, else their first colour; a missing material becomes a
        /// dark neutral Lit. Returns how many.
        /// </summary>
        static int ConvertUnsupported(GameObject root)
        {
            int n = 0;
            var lit = Shader.Find("Universal Render Pipeline/Lit");
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                if (r is ParticleSystemRenderer) continue;
                var mats = r.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < mats.Length; i++)
                {
                    var m = mats[i];
                    if (m == null)
                    {
                        // A material the scene names but the sync didn't bring (or the scene lost):
                        // URP would draw it magenta. A neutral dark ground colour stands in.
                        mats[i] = new Material(lit) { name = "missing (ref)" };
                        mats[i].SetColor("_BaseColor", new Color(0.22f, 0.25f, 0.2f));
                        changed = true;
                        n++;
                        continue;
                    }
                    if (DrawsInUrp(m.shader)) continue;
                    var c = new Material(lit) { name = m.name + " (ref)" };
                    Texture tex = null;
                    Color? col = null;
                    var sh = m.shader;
                    for (int p = 0; sh != null && p < sh.GetPropertyCount(); p++)
                    {
                        string pn = sh.GetPropertyName(p);
                        var type = sh.GetPropertyType(p);
                        if (type == UnityEngine.Rendering.ShaderPropertyType.Texture && tex == null && m.GetTexture(pn) != null
                            && System.Text.RegularExpressions.Regex.IsMatch(pn, "Albedo|Base|Main|Diffuse|Texture", System.Text.RegularExpressions.RegexOptions.IgnoreCase)
                            && !System.Text.RegularExpressions.Regex.IsMatch(pn, "Normal|Bump|Emiss|Noise|Mask", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                            tex = m.GetTexture(pn);
                        if (type == UnityEngine.Rendering.ShaderPropertyType.Color && col == null) col = m.GetColor(pn);
                    }
                    if (tex != null) c.SetTexture("_BaseMap", tex);
                    else if (col.HasValue) c.SetColor("_BaseColor", col.Value);
                    c.SetFloat("_Smoothness", 0.15f);
                    mats[i] = c;
                    changed = true;
                    n++;
                }
                if (changed) r.sharedMaterials = mats;
            }
            return n;
        }

        static bool DrawsInUrp(Shader s)
        {
            if (s == null || !s.isSupported || ShaderUtil.ShaderHasError(s)) return false;
            for (int p = 0; p < s.passCount; p++)
            {
                var tag = s.FindPassTagValue(p, new ShaderTagId("LightMode")).name;
                if (tag == "UniversalForward" || tag == "SRPDefaultUnlit" || tag == "UniversalForwardOnly" || tag == "UniversalGBuffer") return true;
            }
            return false;
        }

        /// <summary>
        /// The <paramref name="count"/> ground focus points (2 m grid over the scene's footprint)
        /// whose view at this width sees the most renderers, at least a view's width apart.
        /// </summary>
        static List<(Vector3 focus, int seen)> Framings(List<Renderer> renderers, float view, int count)
        {
            var b = renderers[0].bounds;
            foreach (var r in renderers) b.Encapsulate(r.bounds);
            // The walkable floor: the commonest base height (to 1 m) of the small props.
            float ground = renderers.Where(r => r.bounds.size.y < 3f).GroupBy(r => Mathf.Round(r.bounds.min.y))
                .OrderByDescending(g => g.Count()).Select(g => g.Key).DefaultIfEmpty(b.min.y).First();
            var cam = ArenaStills.GameCamera();
            var scored = new List<(Vector3, int)>();
            for (float x = b.min.x; x <= b.max.x; x += view / 4)
            for (float z = b.min.z; z <= b.max.z; z += view / 4)
            {
                float floor = GroundNear(renderers, x, z, ground);
                if (Mathf.Abs(floor - ground) > 1.5f) continue;   // on the floor, not on a cliff or roof
                var f = new Vector3(x, floor, z);
                ArenaStills.Frame(cam, f, view);
                var planes = GeometryUtility.CalculateFrustumPlanes(cam);
                int n = renderers.Count(r => r.bounds.max.y < ground + 12f && GeometryUtility.TestPlanesAABB(planes, r.bounds));
                scored.Add((f, n));
            }
            Object.DestroyImmediate(cam.gameObject);
            var picks = new List<(Vector3, int)>();
            foreach (var s in scored.OrderByDescending(s => s.Item2))
            {
                if (picks.Count >= count) break;
                if (picks.Any(p => Vector3.Distance(p.Item1, s.Item1) < view)) continue;
                picks.Add(s);
            }
            return picks;
        }

        /// <summary>The ground's height near (x, z): the lowest renderer bottom within 6 m, so a framing on a terrace frames its floor.</summary>
        static float GroundNear(List<Renderer> renderers, float x, float z, float fallback)
        {
            float best = float.MaxValue;
            foreach (var r in renderers)
            {
                var c = r.bounds.center;
                if (Mathf.Abs(c.x - x) < 6 && Mathf.Abs(c.z - z) < 6) best = Mathf.Min(best, r.bounds.min.y);
            }
            return best == float.MaxValue ? fallback : best;
        }
    }
}
