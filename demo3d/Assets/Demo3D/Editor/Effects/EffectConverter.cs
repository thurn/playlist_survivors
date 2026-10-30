using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Demo3D.Runtime.Effects;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using Slots = Demo3D.Runtime.Effects.EmitterSlots;

namespace Demo3D.EditorTools.Effects
{
    /// <summary>
    /// TD8's converter (effects.md, The converter): reads the chosen prefabs (Data/effects.json)
    /// through the public ParticleSystem, ParticleSystemRenderer and LineRenderer APIs and writes
    /// the GPU runtime's effect library, its lookup rows and its texture array into the
    /// gitignored Assets/Baked/Effects/. It implements exactly the entries in TD8's inventory
    /// (evidence/td8/inventory.md); anything else fails loudly, per prefab and child, unless the
    /// selection drops the module (cosmetic) or skips the child, which the notes record.
    /// </summary>
    public static class EffectConverter
    {
        public const string SelectionPath = "Assets/Demo3D/Data/effects.json";
        public const string OutDir = "Assets/Baked/Effects/Resources";
        public const int TextureSize = 512;

        [InitializeOnLoadMethod]
        static void Register() => Bakers.Register("effects", Bake);

        public static void Bake()
        {
            var selection = JObject.Parse(File.ReadAllText(SelectionPath));
            var guids = ManifestGuids();
            var lib = ScriptableObject.CreateInstance<EffectLibrary>();
            var b = new Builder();
            var errors = new StringBuilder();
            var entries = new List<EffectEntry>();
            var stock = new List<GameObject>();
            foreach (JObject fx in selection["effects"])
            {
                var name = (string)fx["name"];
                var src = (string)fx["prefab"];
                if (!guids.TryGetValue(src, out var guid)) { errors.AppendLine($"error: {name}: {src} is not in manifest.json"); continue; }
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
                if (prefab == null) { errors.AppendLine($"error: {name}: {src} is not imported (run setup)"); continue; }
                var opts = new Options(fx);
                var entry = b.Convert(name, src, prefab, opts, errors);
                if (entry == null) continue;
                entries.Add(entry);
                stock.Add(prefab);
            }
            // The runtime spike's billboard burst (effects.md, Choosing the GPU runtime): our own
            // particle system, built here and converted like the rest, so it isn't vendor-derived.
            var spike = SpikeBurst();
            var spikeEntry = b.Convert(SpikeName, "built in (TD8's runtime spike)", spike, new Options(new JObject { ["noLight"] = true }), errors);
            UnityEngine.Object.DestroyImmediate(spike);
            if (spikeEntry != null) { entries.Add(spikeEntry); stock.Add(null); }
            if (errors.Length > 0) throw new Exception("EffectConverter: unsupported modules in use\n" + errors);

            lib.Effects = entries.ToArray();
            lib.Emitters = b.Emitters.ToArray();
            lib.Emission = b.Emission.ToArray();
            lib.MeshVertices = b.MeshVerts.ToArray();
            lib.Meshes = b.Meshes.ToArray();
            lib.TextureNames = b.Textures.Select(t => t.name).ToArray();
            lib.StockPrefabs = stock.ToArray();
            lib.Selection = File.ReadAllText(SelectionPath);
            lib.Simulate = AssetDatabase.LoadAssetAtPath<ComputeShader>("Assets/Demo3D/Runtime/Effects/Shaders/EffectSim.compute");
            lib.Sort = AssetDatabase.LoadAssetAtPath<ComputeShader>("Assets/Demo3D/Runtime/Effects/Shaders/EffectSort.compute");
            lib.Draw = Shader.Find("Demo3D/EffectParticles");
            if (lib.Simulate == null || lib.Sort == null || lib.Draw == null) throw new Exception("EffectConverter: the runtime's shaders are missing");

            Directory.CreateDirectory(OutDir);
            var lut = b.BuildLut();
            var array = b.BuildTextureArray();
            var libPath = OutDir + "/" + EffectLibrary.ResourcePath + ".asset";
            AssetDatabase.DeleteAsset(libPath);
            AssetDatabase.CreateAsset(lib, libPath);
            lut.name = "EffectLut";
            array.name = "EffectTextures";
            AssetDatabase.AddObjectToAsset(lut, lib);
            AssetDatabase.AddObjectToAsset(array, lib);
            lib.Lut = lut;
            lib.Textures = array;
            EditorUtility.SetDirty(lib);
            AssetDatabase.SaveAssets();
            Debug.Log($"EffectConverter: {entries.Count} effects, {b.Emission.Count} emitters, {b.Textures.Count} textures, " +
                      $"{b.Meshes.Count} meshes, {b.Rows} lookup rows -> {libPath}\n" + string.Join("\n", b.Notes));
        }

        /// <summary>Validates every candidate in the manifest and writes the per-prefab result (evidence).</summary>
        public static void Report(string outPath)
        {
            var manifest = JObject.Parse(File.ReadAllText("manifest.json"));
            var sb = new StringBuilder();
            sb.AppendLine("# TD8: converter report over every effect candidate");
            sb.AppendLine();
            sb.AppendLine("Generated by `demo3d unity EffectReport`: each candidate run through the converter with no drops or skips. ");
            sb.AppendLine("`ok` converts as is; otherwise the errors the converter fails with.");
            sb.AppendLine();
            foreach (var a in manifest["assets"].Where(a => ((string)a["use"]).StartsWith("effect candidate") && ((string)a["path"]).EndsWith(".prefab")))
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath((string)a["guid"]));
                var errors = new StringBuilder();
                var b = new Builder();
                var e = b.Convert(prefab.name, (string)a["path"], prefab, new Options(new JObject()), errors);
                var parent = Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName((string)a["path"])));
                sb.AppendLine($"## {parent} / {prefab.name}");
                sb.AppendLine();
                if (errors.Length == 0) sb.AppendLine($"ok: {e.EmitterCount} emitters, lifetime {e.Lifetime:F2} s" + (e.Light.Active ? ", light cue" : ""));
                else foreach (var line in errors.ToString().Split('\n').Where(l => l.Trim().Length > 0)) sb.AppendLine("    " + line.TrimEnd());
                sb.AppendLine();
                foreach (var n in b.Notes) sb.AppendLine("- " + n);
                sb.AppendLine();
            }
            Directory.CreateDirectory(Path.GetDirectoryName(outPath));
            File.WriteAllText(outPath, sb.ToString().Replace("\r\n", "\n"));
            Debug.Log("EffectConverter: report -> " + outPath);
        }

        public const string SpikeName = "spike-burst";

        /// <summary>Converts one prefab or scene object with a selection entry's options (tests): its entry, emitter slots and emission, or the errors.</summary>
        public static EffectEntry ConvertOne(GameObject go, string optionsJson, out Vector4[] emitters, out EmitterEmission[] emission, out string errors)
        {
            var b = new Builder();
            var sb = new StringBuilder();
            var e = b.Convert(go.name, "test", go, new Options(JObject.Parse(optionsJson ?? "{}")), sb);
            emitters = b.Emitters.ToArray();
            emission = b.Emission.ToArray();
            errors = sb.ToString();
            return e;
        }

        /// <summary>
        /// The spike's effect, mirrored by the VFX Graph template: a burst of 20 additive billboards
        /// from a 0.15 m sphere at 2 to 5 m/s, living 0.35 to 0.8 s, fading out and shrinking.
        /// </summary>
        public static GameObject SpikeBurst()
        {
            var go = new GameObject(SpikeName);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.duration = 1f;
            main.loop = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.35f, 0.8f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(2f, 5f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.15f, 0.35f);
            main.startColor = new Color(1f, 0.75f, 0.4f, 1f);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 100;
            var em = ps.emission;
            em.rateOverTime = 0;
            em.SetBursts(new[] { new ParticleSystem.Burst(0f, 20) });
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.15f;
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new UnityEngine.Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(0, 1) });
            col.color = g;
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0, 1, 1, 0.2f));
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Billboard;
            var mat = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit")) { name = "SpikeBurst" };
            mat.SetTexture("_BaseMap", AssetDatabase.GetBuiltinExtraResource<Texture2D>("Default-Particle.psd"));
            mat.SetFloat("_SrcBlend", 5);
            mat.SetFloat("_DstBlend", 1);
            r.sharedMaterial = mat;
            return go;
        }

        public const string SparkName = "Sparks (look)";

        /// <summary>
        /// The selection's spark burst (effects.md, The look): our own short-lived stretched sparks
        /// thrown from where an effect hits, white-hot, through the heat ramp the selection gives
        /// them. <c>sparks</c>: count, speed [min, max] m/s, life [min, max] s, size m, gravity,
        /// radius m, upward (0 a sphere, 1 a cone straight up), stretch (length per m/s), hdr
        /// (colour scale), heightM.
        /// </summary>
        public static GameObject SparkBurst(JObject o)
        {
            float F(string k, float d) => (float?)o[k] ?? d;
            (float, float) Range(string k, float a, float b) => o[k] is JArray r ? ((float)r[0], (float)r[1]) : (a, b);
            var go = new GameObject(SparkName);
            go.transform.position = new Vector3(0, F("heightM", 0.3f), 0);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            var (lifeMin, lifeMax) = Range("life", 0.15f, 0.4f);
            var (speedMin, speedMax) = Range("speed", 6f, 14f);
            main.duration = 0.1f;
            main.loop = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(lifeMin, lifeMax);
            main.startSpeed = new ParticleSystem.MinMaxCurve(speedMin, speedMax);
            float size = F("size", 0.06f);
            main.startSize = new ParticleSystem.MinMaxCurve(size * 0.6f, size);
            float hdr = F("hdr", 4f);
            main.startColor = new Color(1f, 1f, 1f, 1f);
            main.gravityModifier = F("gravity", 1.5f);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            int count = (int)F("count", 24);
            main.maxParticles = count;
            var em = ps.emission;
            em.rateOverTime = 0;
            em.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)count) });
            var shape = ps.shape;
            float up = F("upward", 0.4f);
            if (up > 0)
            {
                shape.shapeType = ParticleSystemShapeType.Cone;
                shape.angle = Mathf.Lerp(89f, 25f, up);
                shape.rotation = new Vector3(-90, 0, 0);
            }
            else shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = F("radius", 0.2f);
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new UnityEngine.Gradient();
            // Fade from full heat to a dim ember: the heat ramp turns that into white, yellow, red.
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(new Color(0.25f, 0.25f, 0.25f), 1) },
                new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(1, 0.6f), new GradientAlphaKey(0, 1) });
            col.color = g;
            var limit = ps.limitVelocityOverLifetime;
            limit.enabled = true;
            limit.limit = speedMax;
            limit.dampen = 0.15f;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Stretch;
            r.lengthScale = 1f;
            r.velocityScale = F("stretch", 0.04f);
            var mat = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit")) { name = "LookSparks" };
            mat.SetTexture("_BaseMap", AssetDatabase.GetBuiltinExtraResource<Texture2D>("Default-Particle.psd"));
            mat.SetColor("_BaseColor", new Color(hdr, hdr, hdr, 1f).gamma);
            mat.SetFloat("_SrcBlend", 5);
            mat.SetFloat("_DstBlend", 1);
            r.sharedMaterial = mat;
            return go;
        }

        static Dictionary<string, string> ManifestGuids()
        {
            var manifest = JObject.Parse(File.ReadAllText("manifest.json"));
            var d = new Dictionary<string, string>();
            foreach (var a in manifest["assets"]) d[(string)a["path"]] = (string)a["guid"];
            return d;
        }

        /// <summary>A selection entry's choices (Data/effects.json).</summary>
        sealed class Options
        {
            public readonly HashSet<string> Skip, Drop, Approximate, Glow;
            public readonly float BeamLength;
            public readonly bool Lines;
            public readonly float ScaleRef, Duration, GalleryHeight, GalleryView, Intensity, SizeScale;
            public readonly JObject Light;
            public readonly string Notes;
            public readonly bool NoLight;
            /// <summary>The look (effects.md, The look): HDR colour and lifetime scales, the heat ramp, per-child tuning and our own spark burst.</summary>
            public readonly float Hdr, Life;
            public readonly JObject Heat, Sparks;
            public readonly Dictionary<string, JObject> Tune;

            public Options(JObject o)
            {
                Skip = new HashSet<string>((o["skip"] as JArray)?.Select(x => (string)x) ?? Array.Empty<string>());
                Drop = new HashSet<string>((o["drop"] as JArray)?.Select(x => (string)x) ?? Array.Empty<string>());
                Approximate = new HashSet<string>((o["approximate"] as JArray)?.Select(x => (string)x) ?? Array.Empty<string>());
                Glow = new HashSet<string>((o["glow"] as JArray)?.Select(x => (string)x) ?? Array.Empty<string>());
                BeamLength = (float?)o["beamLength"] ?? 0f;
                Lines = (bool?)o["lines"] ?? false;
                ScaleRef = (float?)o["scaleRef"] ?? 1f;
                Duration = (float?)o["duration"] ?? 0f;
                GalleryHeight = (float?)o["galleryHeight"] ?? 1f;
                GalleryView = (float?)o["galleryView"] ?? 8f;
                Intensity = (float?)o["intensity"] ?? 1f;
                SizeScale = (float?)o["sizeScale"] ?? 1f;
                Light = o["light"] as JObject;
                NoLight = (bool?)o["noLight"] ?? false;
                Notes = (string)o["notes"];
                Hdr = (float?)o["hdr"] ?? 1f;
                Life = (float?)o["life"] ?? 1f;
                Heat = o["heat"] as JObject;
                Sparks = o["sparks"] as JObject;
                Tune = new Dictionary<string, JObject>();
                if (o["tune"] is JObject t)
                    foreach (var kv in t) Tune[kv.Key] = (JObject)kv.Value;
                Duration *= Life;
            }

            /// <summary>A child's tuning value (its <c>tune</c> entry's, else 1).</summary>
            public float TuneOf(string child, string key) =>
                Tune.TryGetValue(child, out var t) ? (float?)t[key] ?? 1f : 1f;

            /// <summary>A child's heat ramp: its own <c>tune</c> entry's (false for none), else the effect's.</summary>
            public JObject HeatOf(string child)
            {
                if (child == SparkName) return Sparks?["heat"] != null ? Sparks["heat"] as JObject : Heat ?? new JObject(); // white-hot to red by default
                if (Tune.TryGetValue(child, out var t) && t["heat"] != null) return t["heat"] as JObject;
                return Heat;
            }
        }

        sealed class Builder
        {
            public readonly List<Vector4> Emitters = new List<Vector4>();
            public readonly List<EmitterEmission> Emission = new List<EmitterEmission>();
            public readonly List<Vector4[]> LutRows = new List<Vector4[]>();
            public readonly List<Texture> Textures = new List<Texture>();
            public readonly List<Vector4> MeshVerts = new List<Vector4>();
            public readonly List<MeshRange> Meshes = new List<MeshRange>();
            public readonly List<string> Notes = new List<string>();
            public int Rows => LutRows.Count;

            static readonly HashSet<string> Supported = new HashSet<string>
            {
                "main", "emission", "shape", "velocityOverLifetime", "limitVelocityOverLifetime", "inheritVelocity",
                "forceOverLifetime", "colorOverLifetime", "sizeOverLifetime", "sizeBySpeed", "rotationOverLifetime",
                "noise", "textureSheetAnimation", "customData", "lights", "renderer",
            };

            public EffectEntry Convert(string name, string src, GameObject prefab, Options o, StringBuilder errors)
            {
                var entry = new EffectEntry
                {
                    Name = name, Source = src, FirstEmitter = Emission.Count, ScaleRef = o.ScaleRef,
                    GalleryHeight = o.GalleryHeight, GalleryViewWidth = o.GalleryView, Notes = o.Notes,
                };
                var errs = new List<string>();
                var root = prefab.transform;
                // The root's own rotation and scale stay (spawns use them, as Instantiate would); its position doesn't.
                var rootM = Matrix4x4.TRS(Vector3.zero, root.rotation, root.lossyScale) * root.worldToLocalMatrix;
                var subTargets = new HashSet<ParticleSystem>();
                foreach (var ps in prefab.GetComponentsInChildren<ParticleSystem>(true))
                {
                    var sub = ps.subEmitters;
                    if (!sub.enabled) continue;
                    for (int i = 0; i < sub.subEmittersCount; i++)
                        if (sub.GetSubEmitterSystem(i) != null) subTargets.Add(sub.GetSubEmitterSystem(i));
                }
                float lifetime = 0, duration = 0;
                LightCue cue = default;
                // Our own spark burst (the selection's sparks): built here and converted with the
                // prefab's children, so it isn't vendor-derived.
                GameObject sparks = o.Sparks != null ? SparkBurst(o.Sparks) : null;
                var systems = prefab.GetComponentsInChildren<ParticleSystem>(true).Select(ps => (ps, root, rootM)).ToList();
                if (sparks != null) systems.Add((sparks.GetComponent<ParticleSystem>(), sparks.transform, Matrix4x4.identity));
                foreach (var (ps, psRoot, psRootM) in systems)
                {
                    string child = ps.gameObject.name;
                    if (!ActiveIn(ps.transform, psRoot)) continue;
                    if (o.Skip.Contains(child)) { Notes.Add($"{name}: skipped child '{child}' (selection)"); continue; }
                    if (subTargets.Contains(ps))
                    {
                        if (!o.Drop.Contains("subEmitters")) errs.Add($"subEmitters target on child '{child}'");
                        else Notes.Add($"{name}: child '{child}' only emits as a sub-emitter; dropped with its parent's sub-emitters");
                        continue;
                    }
                    var before = errs.Count;
                    Validate(ps, child, o, errs);
                    var r = ps.GetComponent<ParticleSystemRenderer>();
                    if (ps.lights.enabled && !cue.Active && !o.NoLight) cue = CueFromModule(ps);
                    if (r == null || !r.enabled) continue;
                    if (errs.Count > before) continue;
                    var m = psRootM * ps.transform.localToWorldMatrix;
                    bool trail = ps.trails.enabled && o.Approximate.Contains("trails") && r.trailMaterial != null;
                    if (r.renderMode == ParticleSystemRenderMode.None && !trail) continue;
                    int head = Emission.Count;
                    if (r.renderMode != ParticleSystemRenderMode.None)
                    {
                        if (!Emitter(name, ps, r, m, o, errs, out float life, out float dur, -1)) continue;
                        entry.EmitterCount++;
                        var em = Emission[head];
                        float burst = em.Bursts.Sum(x => x.CountMax * Mathf.Max(1, x.Cycles));
                        Notes.Add($"{name}/{child}: emitter {head}: size {Emitters[Emitters.Count - Slots.Stride + Slots.SizeMax].x:F2} m, " +
                                  $"{burst:F0} burst + {em.RateOverTime:F0}/s, {r.renderMode}" + (o.Glow.Contains(child) ? ", glow quad (drop-glow cut)" : ""));
                        if (o.Glow.Contains(child))
                        {
                            em.Glow = true;
                            Emission[head] = em;
                        }
                        lifetime = Mathf.Max(lifetime, life);
                        duration = Mathf.Max(duration, dur);
                    }
                    if (trail)
                    {
                        // A ribbon approximated by a stretched billboard over the same particles
                        // (same seed and births as its head), as long as the trail's lifetime of travel.
                        if (!Emitter(name, ps, r, m, o, errs, out float life, out float dur, head)) continue;
                        entry.EmitterCount++;
                        lifetime = Mathf.Max(lifetime, life);
                        duration = Mathf.Max(duration, dur);
                        Notes.Add($"{name}/{child}: trails approximated by stretched billboards (selection)");
                    }
                }
                if (sparks != null) UnityEngine.Object.DestroyImmediate(sparks);
                foreach (var lr in prefab.GetComponentsInChildren<LineRenderer>(true))
                {
                    if (!ActiveIn(lr.transform, root) || (!lr.enabled && !o.Lines)) continue;
                    if (o.Skip.Contains(lr.gameObject.name)) { Notes.Add($"{name}: skipped line '{lr.gameObject.name}' (selection)"); continue; }
                    if (!Beam(name, lr, rootM * lr.transform.localToWorldMatrix, o, errs)) continue;
                    entry.EmitterCount++;
                    duration = Mathf.Max(duration, 1f);
                    lifetime = Mathf.Max(lifetime, 1f);
                }
                if (!o.NoLight && !cue.Active)
                    foreach (var l in prefab.GetComponentsInChildren<Light>(true))
                        if (ActiveIn(l.transform, root) && l.enabled && l.type == LightType.Point)
                        {
                            cue = CueFromLight(l, Mathf.Max(0.2f, duration * 0.6f));
                            break;
                        }
                if (o.Light != null) cue = CueOverride(cue, o.Light, duration);
                if (cue.Active) cue.Intensity *= o.Intensity;
                if (errs.Count > 0)
                {
                    errors.AppendLine($"error: {src}: unsupported modules in use");
                    foreach (var e in errs) errors.AppendLine("  " + e);
                    errors.AppendLine("  fix: drop the module (cosmetic), skip the child, or choose another effect");
                    return null;
                }
                if (entry.EmitterCount == 0) { errors.AppendLine($"error: {src}: nothing renders"); return null; }
                entry.Duration = o.Duration > 0 ? o.Duration : duration;
                entry.Lifetime = Mathf.Max(lifetime, entry.Duration);
                entry.Light = cue;
                return entry;
            }

            static bool ActiveIn(Transform t, Transform root)
            {
                for (var x = t; x != null; x = x.parent)
                {
                    if (!x.gameObject.activeSelf) return false;
                    if (x == root) break;
                }
                return true;
            }

            void Validate(ParticleSystem ps, string child, Options o, List<string> errs)
            {
                void Fail(string module, string what)
                {
                    if (o.Drop.Contains(module)) return;
                    errs.Add($"{module}{(what != null ? " (" + what + ")" : "")} on child '{child}'");
                }
                var main = ps.main;
                if (main.startLifetime.mode == ParticleSystemCurveMode.Curve || main.startLifetime.mode == ParticleSystemCurveMode.TwoCurves) Fail("main.startLifetime", "curve");
                if (main.startSpeed.mode == ParticleSystemCurveMode.Curve || main.startSpeed.mode == ParticleSystemCurveMode.TwoCurves) Fail("main.startSpeed", "curve");
                if (main.startColor.mode == ParticleSystemGradientMode.Gradient || main.startColor.mode == ParticleSystemGradientMode.TwoGradients) Fail("main.startColor", main.startColor.mode.ToString());
                if (main.prewarm) Fail("main.prewarm", null);
                if (main.flipRotation != 0) Fail("main.flipRotation", null);
                if (main.simulationSpace == ParticleSystemSimulationSpace.Custom) Fail("main.simulationSpace", "custom");
                if (ps.collision.enabled) Fail("collision", ps.collision.type.ToString());
                if (ps.trigger.enabled) Fail("trigger", null);
                if (ps.externalForces.enabled) Fail("externalForces", null);
                if (ps.lifetimeByEmitterSpeed.enabled) Fail("lifetimeByEmitterSpeed", null);
                if (ps.colorBySpeed.enabled) Fail("colorBySpeed", null);
                if (ps.rotationBySpeed.enabled) Fail("rotationBySpeed", null);
                if (ps.trails.enabled && !o.Approximate.Contains("trails")) Fail("trails", ps.trails.mode.ToString());
                if (ps.subEmitters.enabled && ps.subEmitters.subEmittersCount > 0) Fail("subEmitters", ps.subEmitters.GetSubEmitterType(0).ToString());
                var em = ps.emission;
                if (em.enabled)
                {
                    if (em.rateOverTime.mode != ParticleSystemCurveMode.Constant) Fail("emission.rateOverTime", em.rateOverTime.mode.ToString());
                    if (em.rateOverDistance.mode != ParticleSystemCurveMode.Constant && em.rateOverDistance.mode != ParticleSystemCurveMode.TwoConstants) Fail("emission.rateOverDistance", em.rateOverDistance.mode.ToString());
                    if (em.burstCount > 3) Fail("emission.bursts", em.burstCount + " bursts");
                }
                var shape = ps.shape;
                if (shape.enabled)
                {
                    switch (shape.shapeType)
                    {
                        case ParticleSystemShapeType.Sphere: case ParticleSystemShapeType.Hemisphere: case ParticleSystemShapeType.Cone:
                        case ParticleSystemShapeType.ConeVolume: case ParticleSystemShapeType.Circle: case ParticleSystemShapeType.Box:
                        case ParticleSystemShapeType.SingleSidedEdge: break;
                        default: Fail("shape", shape.shapeType.ToString()); break;
                    }
                    if (shape.arc < 360f && shape.arcMode != ParticleSystemShapeMultiModeValue.Random && shape.arcMode != ParticleSystemShapeMultiModeValue.BurstSpread) Fail("shape.arcMode", shape.arcMode.ToString());
                    if (shape.alignToDirection) Fail("shape.alignToDirection", null);
                }
                var vol = ps.velocityOverLifetime;
                var lim = ps.limitVelocityOverLifetime;
                if (lim.enabled && lim.separateAxes) Fail("limitVelocityOverLifetime.separateAxes", null);
                var rol = ps.rotationOverLifetime;
                var tsa = ps.textureSheetAnimation;
                if (tsa.enabled && (tsa.mode != ParticleSystemAnimationMode.Grid || tsa.animation != ParticleSystemAnimationType.WholeSheet)) Fail("textureSheetAnimation", tsa.mode + ", " + tsa.animation);
                var cd = ps.customData;
                if (cd.enabled)
                    foreach (var s in new[] { ParticleSystemCustomData.Custom1, ParticleSystemCustomData.Custom2 })
                        if (cd.GetMode(s) == ParticleSystemCustomDataMode.Color) Fail("customData", s + " colour");
                var r = ps.GetComponent<ParticleSystemRenderer>();
                if (r != null && r.enabled && r.renderMode != ParticleSystemRenderMode.None)
                {
                    if (r.renderMode == ParticleSystemRenderMode.Mesh && FirstMesh(r) == null) Fail("renderer", "mesh mode without a mesh");
                    if (r.alignment == ParticleSystemRenderSpace.Facing || r.alignment == ParticleSystemRenderSpace.Velocity) Fail("renderer.alignment", r.alignment.ToString());
                    var mat = r.sharedMaterial;
                    if (mat == null) Fail("renderer", "no material");
                    else if (mat.shader == null || mat.shader.name == "Hidden/InternalErrorShader") Fail("material", $"'{mat.name}' has no working shader");
                    else if (!KnownShader(mat.shader.name)) Fail("material", $"'{mat.name}' uses shader {mat.shader.name}, which the draw shader doesn't cover");
                }
            }

            static bool KnownShader(string s) =>
                s.StartsWith("Shader Graphs/HS_Blend") || s == "Shader Graphs/HS_SwordSlash" || s == "Shader Graphs/HS_Electricity" ||
                s == "Shader Graphs/HS_LightGlow" || s.StartsWith("Hovl/Particles/") || s.StartsWith("Legacy Shaders/Particles/") ||
                s == "Effects/Explosions/Distortion/ParticlesCutOut" || s.StartsWith("Universal Render Pipeline/Particles/") ||
                s.StartsWith("Particles/") || s.StartsWith("Mobile/Particles/");

            static Mesh FirstMesh(ParticleSystemRenderer r)
            {
                if (r.meshCount == 0) return r.mesh;
                var meshes = new Mesh[r.meshCount];
                r.GetMeshes(meshes);
                return meshes.FirstOrDefault(m => m != null);
            }

            static float Mid(ParticleSystem.MinMaxCurve c, float u) => (Eval(c, u, false) + Eval(c, u, true)) * 0.5f;

            static bool NonZero(ParticleSystem.MinMaxCurve c) =>
                c.mode == ParticleSystemCurveMode.Constant ? c.constant != 0 :
                c.mode == ParticleSystemCurveMode.TwoConstants ? c.constantMin != 0 || c.constantMax != 0 : c.curveMultiplier != 0;

            bool Emitter(string fxName, ParticleSystem ps, ParticleSystemRenderer r, Matrix4x4 m, Options o, List<string> errs, out float lifetime, out float duration, int seedEmitter)
            {
                lifetime = duration = 0;
                bool trail = seedEmitter >= 0;
                var main = ps.main;
                string child = ps.gameObject.name;
                // Time runs sp times faster: the simulation speed, and the selection's life scale
                // (a shorter life plays the same motion and curves faster).
                float sp = Mathf.Max(1e-3f, main.simulationSpeed) / Mathf.Max(1e-3f, o.Life * o.TuneOf(child, "life"));
                var flags = (Slots.Flags)0;
                var v = new Vector4[Slots.Stride];
                int row = LutRows.Count;
                var rows = new Vector4[Slots.LutRows][];
                for (int i = 0; i < rows.Length; i++) rows[i] = Fill(Vector4.one);

                if (main.loop) flags |= Slots.Flags.Loop;
                if (main.simulationSpace == ParticleSystemSimulationSpace.Local) flags |= Slots.Flags.LocalSpace;
                if (main.scalingMode == ParticleSystemScalingMode.Shape) flags |= Slots.Flags.ShapeScaleOnly;
                float lifeMin = Min(main.startLifetime) / sp, lifeMax = Max(main.startLifetime) / sp;
                float dur = main.duration / sp;
                float delayMin = Min(main.startDelay) / sp, delayMax = Max(main.startDelay) / sp;
                v[Slots.Time] = new Vector4(dur, delayMin, delayMax, 0);
                v[Slots.Life] = new Vector4(lifeMin, lifeMax, Min(main.startSpeed) * sp, Max(main.startSpeed) * sp);

                // Child transform relative to the effect root.
                Vector3 pos = m.GetColumn(3);
                Vector3 scl = new Vector3(m.GetColumn(0).magnitude, m.GetColumn(1).magnitude, m.GetColumn(2).magnitude);
                var rot = Quaternion.LookRotation(m.GetColumn(2) / Mathf.Max(1e-6f, scl.z), m.GetColumn(1) / Mathf.Max(1e-6f, scl.y));
                float sizeK = o.SizeScale * o.TuneOf(child, "size") * (main.scalingMode == ParticleSystemScalingMode.Shape ? 1f : Mathf.Max(scl.x, Mathf.Max(scl.y, scl.z)));
                if (main.startSize3D)
                {
                    flags |= Slots.Flags.Size3D;
                    v[Slots.SizeMin] = new Vector4(Min(main.startSizeX), Min(main.startSizeY), Min(main.startSizeZ), 0) * sizeK;
                    v[Slots.SizeMax] = new Vector4(Max(main.startSizeX), Max(main.startSizeY), Max(main.startSizeZ), 0) * sizeK;
                }
                else
                {
                    v[Slots.SizeMin] = Vector4.one * Min(main.startSize) * sizeK;
                    v[Slots.SizeMax] = Vector4.one * Max(main.startSize) * sizeK;
                }
                v[Slots.SizeMin].w = Const(main.gravityModifier) * sp * sp;
                v[Slots.SizeMax].w = main.maxParticles;
                if (main.startRotation3D)
                {
                    flags |= Slots.Flags.Rot3D;
                    v[Slots.RotMin] = new Vector4(Min(main.startRotationX), Min(main.startRotationY), Min(main.startRotationZ), 0);
                    v[Slots.RotMax] = new Vector4(Max(main.startRotationX), Max(main.startRotationY), Max(main.startRotationZ), 0);
                }
                else
                {
                    v[Slots.RotMin] = new Vector4(0, 0, Min(main.startRotation), 0);
                    v[Slots.RotMax] = new Vector4(0, 0, Max(main.startRotation), 0);
                }
                var sc = main.startColor;
                if (sc.mode == ParticleSystemGradientMode.TwoColors) { flags |= Slots.Flags.StartColorTwo; v[Slots.ColA] = Lin(sc.colorMin); v[Slots.ColB] = Lin(sc.colorMax); }
                else if (sc.mode == ParticleSystemGradientMode.RandomColor) { flags |= Slots.Flags.StartColorGradient; rows[Slots.RowStartColor] = Gradient(sc.gradient); v[Slots.ColA] = Lin(sc.gradient.Evaluate(0)); }
                else { v[Slots.ColA] = Lin(sc.color); v[Slots.ColB] = v[Slots.ColA]; }

                // Shape.
                var shape = ps.shape;
                Slots.ShapeType type = Slots.ShapeType.None;
                if (shape.enabled)
                    type = shape.shapeType switch
                    {
                        ParticleSystemShapeType.Sphere => Slots.ShapeType.Sphere,
                        ParticleSystemShapeType.Hemisphere => Slots.ShapeType.Hemisphere,
                        ParticleSystemShapeType.Cone => Slots.ShapeType.Cone,
                        ParticleSystemShapeType.ConeVolume => Slots.ShapeType.ConeVolume,
                        ParticleSystemShapeType.Circle => Slots.ShapeType.Circle,
                        ParticleSystemShapeType.Box => Slots.ShapeType.Box,
                        ParticleSystemShapeType.SingleSidedEdge => Slots.ShapeType.Edge,
                        _ => Slots.ShapeType.None,
                    };
                v[Slots.Shape0] = new Vector4((float)type, shape.radius, shape.radiusThickness, shape.angle * Mathf.Deg2Rad);
                v[Slots.Shape1] = new Vector4(shape.arc * Mathf.Deg2Rad, shape.arcMode == ParticleSystemShapeMultiModeValue.BurstSpread ? 1 : 0, shape.length, shape.randomDirectionAmount);
                v[Slots.Shape2] = new Vector4(shape.scale.x, shape.scale.y, shape.scale.z, shape.sphericalDirectionAmount);
                if (type == Slots.ShapeType.Box) v[Slots.Shape2] = new Vector4(1, 1, 1, shape.sphericalDirectionAmount);
                v[Slots.ShapePos] = shape.position;
                var sq = Quaternion.Euler(shape.rotation);
                v[Slots.ShapeRot] = new Vector4(sq.x, sq.y, sq.z, sq.w);
                v[Slots.ShapeScale] = type == Slots.ShapeType.Box ? shape.scale : shape.scale;
                v[Slots.ChildPos] = new Vector4(pos.x, pos.y, pos.z, (float)main.scalingMode);
                v[Slots.ChildRot] = new Vector4(rot.x, rot.y, rot.z, rot.w);
                v[Slots.ChildScale] = new Vector4(scl.x, scl.y, scl.z, row);

                // Over-lifetime modules.
                var vol = ps.velocityOverLifetime;
                if (vol.enabled)
                {
                    flags |= Slots.Flags.Velocity;
                    if (vol.space == ParticleSystemSimulationSpace.World) flags |= Slots.Flags.VelocityWorld;
                    float k = vol.speedModifierMultiplier * sp;
                    rows[Slots.RowVel] = Curve3(vol.x, vol.y, vol.z, false, k);
                    rows[Slots.RowVel2] = Curve3(vol.x, vol.y, vol.z, true, k);
                    if (TwoValued(vol.x) || TwoValued(vol.y) || TwoValued(vol.z)) flags |= Slots.Flags.Vel2;
                    if (NonZero(vol.orbitalX) || NonZero(vol.orbitalY) || NonZero(vol.orbitalZ) || NonZero(vol.radial))
                    {
                        flags |= Slots.Flags.Orbital;
                        var orb = new Vector4[64];
                        for (int i = 0; i < 64; i++)
                        {
                            float u = i / 63f;
                            orb[i] = new Vector4(Mid(vol.orbitalX, u), Mid(vol.orbitalY, u), Mid(vol.orbitalZ, u), Mid(vol.radial, u)) * sp;
                        }
                        rows[Slots.RowOrbital] = orb;
                        v[Slots.Orbital] = new Vector4(Const(vol.orbitalOffsetX), Const(vol.orbitalOffsetY), Const(vol.orbitalOffsetZ), 0);
                    }
                }
                var force = ps.forceOverLifetime;
                if (force.enabled)
                {
                    flags |= Slots.Flags.Force;
                    if (force.space == ParticleSystemSimulationSpace.World) flags |= Slots.Flags.ForceWorld;
                    rows[Slots.RowForce] = Curve3(force.x, force.y, force.z, false, sp * sp);
                    rows[Slots.RowForce2] = Curve3(force.x, force.y, force.z, true, sp * sp);
                    if (TwoValued(force.x) || TwoValued(force.y) || TwoValued(force.z)) flags |= Slots.Flags.Force2;
                }
                var misc = Fill(Vector4.zero);
                var lim = ps.limitVelocityOverLifetime;
                if (lim.enabled)
                {
                    flags |= Slots.Flags.Limit;
                    for (int i = 0; i < 64; i++) misc[i].z = Eval(lim.limit, i / 63f, true) * sp;
                    v[Slots.RotMax].w = lim.dampen;
                    if (lim.drag.constant != 0 || lim.drag.mode != ParticleSystemCurveMode.Constant) Notes.Add($"{fxName}/{ps.name}: limit velocity's drag ignored");
                }
                var inh = ps.inheritVelocity;
                if (inh.enabled) v[Slots.RotMin].w = Const(inh.curve);
                var col = ps.colorOverLifetime;
                if (col.enabled)
                {
                    flags |= Slots.Flags.ColorOverLife;
                    var g = col.color;
                    if (g.mode == ParticleSystemGradientMode.TwoGradients) { flags |= Slots.Flags.Color2; rows[Slots.RowColor] = Gradient(g.gradientMin); rows[Slots.RowColor2] = Gradient(g.gradientMax); }
                    else if (g.mode == ParticleSystemGradientMode.Gradient) rows[Slots.RowColor] = Gradient(g.gradient);
                    else if (g.mode == ParticleSystemGradientMode.Color) rows[Slots.RowColor] = Fill(Lin(g.color));
                    else if (g.mode == ParticleSystemGradientMode.TwoColors) { flags |= Slots.Flags.Color2; rows[Slots.RowColor] = Fill(Lin(g.colorMin)); rows[Slots.RowColor2] = Fill(Lin(g.colorMax)); }
                    else errs.Add($"colorOverLifetime ({g.mode}) on child '{ps.name}'");
                }
                var sol = ps.sizeOverLifetime;
                if (sol.enabled)
                {
                    flags |= Slots.Flags.SizeOverLife;
                    if (sol.separateAxes) { flags |= Slots.Flags.SizeAxes; rows[Slots.RowSize] = Curve3(sol.x, sol.y, sol.z, false, 1); rows[Slots.RowSize2] = Curve3(sol.x, sol.y, sol.z, true, 1); }
                    else { rows[Slots.RowSize] = Curve3(sol.size, sol.size, sol.size, false, 1); rows[Slots.RowSize2] = Curve3(sol.size, sol.size, sol.size, true, 1); }
                    if (TwoValued(sol.size) || (sol.separateAxes && (TwoValued(sol.x) || TwoValued(sol.y) || TwoValued(sol.z)))) flags |= Slots.Flags.Size2;
                }
                var rol = ps.rotationOverLifetime;
                if (rol.enabled)
                {
                    flags |= Slots.Flags.RotOverLife;
                    for (int i = 0; i < 64; i++) { misc[i].x = Eval(rol.z, i / 63f, false) * sp; misc[i].y = Eval(rol.z, i / 63f, true) * sp; }
                    if (rol.separateAxes && (NonZero(rol.x) || NonZero(rol.y)))
                    {
                        flags |= Slots.Flags.RotAxes;
                        var r3 = new Vector4[64];
                        for (int i = 0; i < 64; i++)
                        {
                            float u = i / 63f;
                            r3[i] = new Vector4(Eval(rol.x, u, false), Eval(rol.x, u, true), Eval(rol.y, u, false), Eval(rol.y, u, true)) * sp;
                        }
                        rows[Slots.RowRot3] = r3;
                    }
                }
                if (main.startRotation.mode == ParticleSystemCurveMode.TwoConstants && Min(main.startRotation) == -Max(main.startRotation)) { }
                var misc2 = Fill(Vector4.one);
                var sbs = ps.sizeBySpeed;
                if (sbs.enabled)
                {
                    flags |= Slots.Flags.SizeBySpeed;
                    for (int i = 0; i < 64; i++) misc2[i].x = Eval(sbs.size, i / 63f, true);
                    v[Slots.Mod].z = sbs.range.x;
                    v[Slots.Mod].w = sbs.range.y;
                }
                var noise = ps.noise;
                if (noise.enabled)
                {
                    flags |= Slots.Flags.Noise;
                    v[Slots.Noise] = new Vector4(Max(noise.strength) * sp, noise.frequency, Const(noise.scrollSpeed) * sp, noise.damping ? 1 : 0);
                    Notes.Add($"{fxName}/{ps.name}: noise approximated with curl noise (strength {Max(noise.strength):F2}, frequency {noise.frequency:F2})");
                }
                var tsa = ps.textureSheetAnimation;
                v[Slots.Tex] = new Vector4(0, 1, 1, 1);
                if (tsa.enabled)
                {
                    flags |= Slots.Flags.SheetAnim;
                    for (int i = 0; i < 64; i++) misc[i].w = Eval(tsa.frameOverTime, i / 63f, true);
                    v[Slots.Tex] = new Vector4(0, tsa.numTilesX, tsa.numTilesY, Mathf.Max(1, tsa.cycleCount));
                    v[Slots.Mod].y = Max(tsa.startFrame) / Mathf.Max(1, tsa.numTilesX * tsa.numTilesY);
                }
                var cd = ps.customData;
                if (cd.enabled)
                {
                    flags |= Slots.Flags.Custom;
                    var c = Fill(Vector4.zero);
                    for (int i = 0; i < 64; i++)
                    {
                        float u = i / 63f;
                        c[i] = new Vector4(CustomEval(cd, ParticleSystemCustomData.Custom1, 0, u), CustomEval(cd, ParticleSystemCustomData.Custom1, 1, u),
                            CustomEval(cd, ParticleSystemCustomData.Custom2, 0, u), CustomEval(cd, ParticleSystemCustomData.Custom2, 1, u));
                    }
                    rows[Slots.RowCustom] = c;
                }
                rows[Slots.RowMisc] = misc;
                rows[Slots.RowMisc2] = misc2;

                // Renderer and material.
                if (trail) flags &= ~(Slots.Flags.SheetAnim | Slots.Flags.SizeBySpeed | Slots.Flags.RotOverLife);
                var mode = trail ? Slots.RenderMode.Stretch : r.renderMode switch
                {
                    ParticleSystemRenderMode.Stretch => Slots.RenderMode.Stretch,
                    ParticleSystemRenderMode.HorizontalBillboard => Slots.RenderMode.Horizontal,
                    ParticleSystemRenderMode.VerticalBillboard => Slots.RenderMode.Vertical,
                    ParticleSystemRenderMode.Mesh => Slots.RenderMode.Mesh,
                    _ => Slots.RenderMode.Billboard,
                };
                var align = r.alignment == ParticleSystemRenderSpace.Local ? Slots.Alignment.Local : r.alignment == ParticleSystemRenderSpace.World ? Slots.Alignment.World : Slots.Alignment.View;
                v[Slots.Render0] = new Vector4((float)mode, (float)align, r.lengthScale, r.velocityScale);
                if (trail)
                {
                    var tr = ps.trails;
                    float trailLife = Max(tr.lifetime) * (tr.mode == ParticleSystemTrailMode.PerParticle ? lifeMax : 1f);
                    v[Slots.Render0] = new Vector4((float)mode, 3, 0f, Mathf.Max(0.02f, trailLife)); // alignment 3: the quad trails behind its particle
                    float width = Max(tr.widthOverTrail) * (tr.sizeAffectsWidth ? 1f : 0.2f);
                    v[Slots.SizeMin] = new Vector4(v[Slots.SizeMin].x * width, v[Slots.SizeMin].y * width, v[Slots.SizeMin].z * width, v[Slots.SizeMin].w);
                    v[Slots.SizeMax] = new Vector4(v[Slots.SizeMax].x * width, v[Slots.SizeMax].y * width, v[Slots.SizeMax].z * width, v[Slots.SizeMax].w);
                    if (!tr.inheritParticleColor) { v[Slots.ColA] = Lin(tr.colorOverLifetime.color); v[Slots.ColB] = v[Slots.ColA]; flags &= ~(Slots.Flags.StartColorTwo | Slots.Flags.StartColorGradient); }
                }
                v[Slots.Mod].x = trail ? seedEmitter : Emission.Count;
                int mesh = 0;
                if (mode == Slots.RenderMode.Mesh)
                {
                    mesh = MeshIndex(FirstMesh(r));
                    if (r.meshCount > 1) Notes.Add($"{fxName}/{ps.name}: {r.meshCount} particle meshes; the first ('{FirstMesh(r).name}') used");
                }
                v[Slots.Render1] = new Vector4(r.minParticleSize, r.maxParticleSize, r.sortingFudge * 0.01f, mesh);
                if (!Material(trail ? r.trailMaterial : r.sharedMaterial, v, ref flags, fxName + "/" + ps.name + (trail ? " (trail)" : ""))) return false;
                Look(v, o, child, fxName);

                v[Slots.Time].w = BitConverter.Int32BitsToSingle((int)(uint)flags);
                Emitters.AddRange(v);
                foreach (var rr in rows) LutRows.Add(rr);

                var em = ps.emission;
                var bursts = new List<Burst>();
                if (em.enabled)
                    for (int i = 0; i < em.burstCount; i++)
                    {
                        var b = em.GetBurst(i);
                        bursts.Add(new Burst
                        {
                            Time = b.time / sp, CountMin = Min(b.count), CountMax = Max(b.count), Cycles = b.cycleCount,
                            Interval = b.repeatInterval / sp, Probability = b.probability,
                        });
                    }
                Emission.Add(new EmitterEmission
                {
                    Duration = dur, DelayMin = delayMin, DelayMax = delayMax, Loop = main.loop,
                    RateOverTime = em.enabled ? em.rateOverTime.constant * sp : 0,
                    RateOverDistanceMin = em.enabled ? Min(em.rateOverDistance) : 0,
                    RateOverDistanceMax = em.enabled ? Max(em.rateOverDistance) : 0,
                    Bursts = bursts.ToArray(), MaxParticles = main.maxParticles, LifeMax = lifeMax,
                    SeedEmitter = trail ? seedEmitter : Emission.Count,
                });
                lifetime = delayMax + dur + lifeMax;
                duration = delayMax + dur;
                return true;
            }

            /// <summary>The laser's beam: a LineRenderer becomes one long-lived particle drawn as a camera-facing strip.</summary>
            bool Beam(string fxName, LineRenderer lr, Matrix4x4 m, Options o, List<string> errs)
            {
                if (lr.positionCount < 2) { errs.Add($"LineRenderer with {lr.positionCount} points on '{lr.name}'"); return false; }
                var p0 = lr.GetPosition(0);
                var p1 = lr.GetPosition(lr.positionCount - 1);
                if (lr.useWorldSpace) { p0 = lr.transform.InverseTransformPoint(p0); p1 = lr.transform.InverseTransformPoint(p1); }
                Vector3 a = m.MultiplyPoint3x4(p0), b = m.MultiplyPoint3x4(p1);
                float len = Vector3.Distance(a, b);
                if (len < 1e-3f) { len = 1f; b = a + Vector3.forward; }
                if (o.BeamLength > 0) len = o.BeamLength; // its script sets the length at run time
                var v = new Vector4[Slots.Stride];
                int row = LutRows.Count;
                var flags = Slots.Flags.LocalSpace | Slots.Flags.Beam | Slots.Flags.ColorOverLife;
                v[Slots.Time] = new Vector4(1, 0, 0, 0);
                v[Slots.Life] = new Vector4(1, 1, 0, 0);
                float w = lr.widthMultiplier * lr.widthCurve.Evaluate(0) * o.SizeScale;
                v[Slots.SizeMin] = new Vector4(w, w, w, 0);
                v[Slots.SizeMax] = new Vector4(w, w, w, 1);
                v[Slots.ColA] = Lin(lr.colorGradient.Evaluate(0));
                v[Slots.ColB] = v[Slots.ColA];
                v[Slots.ShapeRot] = new Vector4(0, 0, 0, 1);
                v[Slots.ShapeScale] = Vector3.one;
                var rot = Quaternion.LookRotation(b - a);
                v[Slots.ChildPos] = new Vector4(a.x, a.y, a.z, 0);
                v[Slots.ChildRot] = new Vector4(rot.x, rot.y, rot.z, rot.w);
                v[Slots.ChildScale] = new Vector4(1, 1, 1, row);
                float tile = lr.textureMode == LineTextureMode.Tile ? 1f / Mathf.Max(1e-3f, w) : 0;
                v[Slots.Render0] = new Vector4((float)Slots.RenderMode.Beam, 0, len, tile);
                v[Slots.Render1] = new Vector4(0, 1e4f, 0, 0);
                v[Slots.Tex] = new Vector4(0, 1, 1, 1);
                if (!Material(lr.sharedMaterial, v, ref flags, fxName + "/" + lr.name)) return false;
                Look(v, o, lr.name, fxName);
                v[Slots.Time].w = BitConverter.Int32BitsToSingle((int)(uint)flags);
                Emitters.AddRange(v);
                var rows = new Vector4[Slots.LutRows][];
                for (int i = 0; i < rows.Length; i++) rows[i] = Fill(Vector4.one);
                // A quick fade in and out over the beam's life.
                for (int i = 0; i < 64; i++) { float u = i / 63f; rows[Slots.RowColor][i] = new Vector4(1, 1, 1, Mathf.Clamp01(u * 20f) * Mathf.Clamp01((1 - u) * 10f)); }
                rows[Slots.RowMisc] = Fill(Vector4.zero);
                foreach (var rr in rows) LutRows.Add(rr);
                v[Slots.Mod].x = Emission.Count;
                Emitters[Emitters.Count - Slots.Stride + Slots.Mod] = v[Slots.Mod];
                Emission.Add(new EmitterEmission { Duration = 1, Loop = false, Bursts = new[] { new Burst { Time = 0, CountMin = 1, CountMax = 1, Cycles = 1, Probability = 1 } }, MaxParticles = 1, LifeMax = 1, SeedEmitter = Emission.Count });
                Notes.Add($"{fxName}: LineRenderer '{lr.name}' converted to a beam ({len:F2} m, width {w:F2} m); its scripts' scrolling isn't reproduced");
                return true;
            }

            /// <summary>The selection's look on one emitter: its HDR colour scale and its heat ramp.</summary>
            void Look(Vector4[] v, Options o, string child, string fxName)
            {
                float hdr = o.Hdr * o.TuneOf(child, "hdr");
                var m0 = v[Slots.Mat0];
                v[Slots.Mat0] = new Vector4(m0.x * hdr, m0.y * hdr, m0.z * hdr, m0.w);
                var heat = o.HeatOf(child);
                float gain = (float?)heat?["gain"] ?? 1f;
                if (hdr != 1f || heat != null) Notes.Add($"{fxName}/{child}: look: hdr {hdr:F2}, heat " + (heat != null ? $"gain {gain:F2}" : "off"));
                if (heat == null) return;
                Vector4 Rgb(string key, Color fallback) =>
                    heat[key] is JArray c ? new Vector4((float)c[0], (float)c[1], (float)c[2], 0) : (Vector4)fallback;
                var edge = Rgb("edge", new Color(1f, 0.08f, 0.02f));
                var mid = Rgb("mid", new Color(1f, 0.55f, 0.12f));
                edge.w = (float?)heat["mix"] ?? 1f;
                mid.w = gain;
                v[Slots.Heat0] = edge;
                v[Slots.Heat1] = mid;
            }

            /// <summary>The material's observed behaviour: blend, texture, HDR tint, opacity, noise mask and its scrolling, soft particles.</summary>
            bool Material(Material mat, Vector4[] v, ref Slots.Flags flags, string who)
            {
                if (mat == null) return false;
                var shader = mat.shader.name;
                // Blend from the material's own factors where they form a transparent pair (Hovl's
                // HS_Blend graphs drive their blend state from them), else from the shader's name.
                var blend = Slots.Blend.Additive;
                int src = mat.HasProperty("_SrcBlend") ? (int)mat.GetFloat("_SrcBlend") : -1;
                int dst = mat.HasProperty("_DstBlend") ? (int)mat.GetFloat("_DstBlend") : -1;
                if (src == 5 && dst == 1 || src == 1 && dst == 1) blend = Slots.Blend.Additive;
                else if (src == 5 && dst == 10) blend = Slots.Blend.Alpha;
                else if (src == 1 && dst == 10) blend = Slots.Blend.Premultiplied;
                else if (shader.Contains("Premultiply")) blend = Slots.Blend.Premultiplied;
                else if (shader.Contains("Add")) blend = Slots.Blend.Additive;
                else if (shader.Contains("Alpha") || shader.Contains("CutOut") || shader.Contains("Blend")) blend = Slots.Blend.Alpha;
                Texture main = Tex(mat, "_MainTex") ?? Tex(mat, "_MainTexture") ?? Tex(mat, "_BaseMap") ?? Tex(mat, "_Main");
                if (main == null) { Notes.Add($"{who}: material '{mat.name}' has no main texture; white used"); }
                Color tint = Color.white;
                foreach (var p in new[] { "_TintColor", "_Color", "_BaseColor" })
                    if (mat.HasProperty(p)) { tint = mat.GetColor(p); break; }
                float emission = mat.HasProperty("_Emission") ? mat.GetFloat("_Emission") : 1f;
                if (shader.StartsWith("Legacy Shaders/Particles/") && mat.HasProperty("_TintColor")) emission *= 2f; // legacy particle shaders double the tint
                float opacity = mat.HasProperty("_Opacity") ? mat.GetFloat("_Opacity") : 1f;
                var lt = tint.linear;
                v[Slots.Mat0] = new Vector4(lt.r * emission, lt.g * emission, lt.b * emission, tint.a * opacity);
                float soft = 0;
                if (mat.HasProperty("_Usedepth") && mat.GetFloat("_Usedepth") > 0.5f && mat.HasProperty("_Depthpower")) soft = mat.GetFloat("_Depthpower");
                else if (mat.HasProperty("_InvFade")) soft = mat.GetFloat("_InvFade");
                if (soft > 0) flags |= Slots.Flags.SoftParticles;
                int noiseSlice = 0, rampSlice = 0;
                Texture noise = Tex(mat, "_Noise") ?? Tex(mat, "_Dissolve");
                if (noise != null) { flags |= Slots.Flags.NoiseTex; noiseSlice = TextureSlice(noise); }
                // Sword slashes colour their shape through a gradient texture.
                Texture ramp = shader == "Shader Graphs/HS_SwordSlash" ? Tex(mat, "_EmissionTex") : null;
                if (ramp != null) rampSlice = TextureSlice(ramp) + 1;
                v[Slots.Mat1] = new Vector4((float)blend, soft, noiseSlice, rampSlice);
                Notes.Add($"{who}: '{mat.name}' ({shader}) drawn {blend}" + (soft > 0 ? $", soft {soft:F2}" : "") + (noise != null ? ", noise mask" : "") + (ramp != null ? ", gradient map" : ""));
                Vector4 speeds = Vector4.zero;
                if (mat.HasProperty("_SpeedMainTexUVNoiseZW")) speeds = mat.GetVector("_SpeedMainTexUVNoiseZW");
                v[Slots.Mat2] = new Vector4(speeds.z, speeds.w, speeds.x, speeds.y);
                v[Slots.Tex].x = main != null ? TextureSlice(main) : TextureSlice(Texture2D.whiteTexture);
                if (mat.HasProperty("_Usecenterglow") && mat.GetFloat("_Usecenterglow") > 0.5f) Notes.Add($"{who}: '{mat.name}' centre glow ignored");
                return true;
            }

            static Texture Tex(Material m, string p) => m.HasProperty(p) ? m.GetTexture(p) : null;

            int TextureSlice(Texture t)
            {
                int i = Textures.IndexOf(t);
                if (i >= 0) return i;
                Textures.Add(t);
                return Textures.Count - 1;
            }

            int MeshIndex(Mesh mesh)
            {
                for (int i = 0; i < Meshes.Count; i++) if (Meshes[i].Name == mesh.name) return i;
                var src = mesh; // the editor reads unreadable meshes too
                var verts = src.vertices;
                var uv = src.uv;
                var normals = src.normals;
                var colors = src.colors;
                var tris = src.triangles;
                int first = MeshVerts.Count / 3;
                foreach (int t in tris)
                {
                    var p = verts[t];
                    var u = uv.Length > t ? uv[t] : Vector2.zero;
                    var n = normals.Length > t ? normals[t] : Vector3.up;
                    var c = colors.Length > t ? colors[t].linear : Color.white;
                    MeshVerts.Add(new Vector4(p.x, p.y, p.z, u.x));
                    MeshVerts.Add(new Vector4(n.x, n.y, n.z, u.y));
                    MeshVerts.Add(new Vector4(c.r, c.g, c.b, c.a));
                }
                Meshes.Add(new MeshRange { Name = mesh.name, FirstVertex = first, VertexCount = tris.Length });
                return Meshes.Count - 1;
            }

            static LightCue CueFromModule(ParticleSystem ps)
            {
                var lm = ps.lights;
                var l = lm.light;
                if (l == null) return default;
                float alive = Mathf.Min(lm.maxLights, Mathf.Max(1, ps.main.maxParticles) * lm.ratio);
                var curve = new float[16];
                var col = ps.colorOverLifetime;
                for (int i = 0; i < 16; i++)
                {
                    float u = i / 15f;
                    curve[i] = col.enabled ? col.color.Evaluate(u).a : 1f - u;
                }
                return new LightCue
                {
                    Color = l.color.linear, Range = l.range * Max(lm.range), Intensity = l.intensity * Max(lm.intensity) * Mathf.Sqrt(Mathf.Max(1, alive)),
                    Duration = Mathf.Max(0.1f, Max(ps.main.startLifetime) / Mathf.Max(1e-3f, ps.main.simulationSpeed)), Curve = curve,
                };
            }

            static LightCue CueFromLight(Light l, float duration)
            {
                var curve = new float[16];
                for (int i = 0; i < 16; i++) { float u = i / 15f; curve[i] = Mathf.Clamp01(u * 15f) * Mathf.Pow(1 - u, 1.5f); }
                curve[0] = 0.6f;
                return new LightCue { Color = l.color.linear, Range = l.range, Intensity = l.intensity, Duration = duration, Curve = curve };
            }

            static LightCue CueOverride(LightCue cue, JObject o, float duration)
            {
                if (cue.Curve == null) cue = CueFromLightValues(Color.white, 4, 1, Mathf.Max(0.2f, duration));
                if (o["color"] is JArray c) cue.Color = new Color((float)c[0], (float)c[1], (float)c[2]).linear;
                if (o["range"] != null) cue.Range = (float)o["range"];
                if (o["intensity"] != null) cue.Intensity = (float)o["intensity"];
                if (o["duration"] != null) cue.Duration = (float)o["duration"];
                return cue;
            }

            static LightCue CueFromLightValues(Color c, float range, float intensity, float duration)
            {
                var curve = new float[16];
                for (int i = 0; i < 16; i++) { float u = i / 15f; curve[i] = Mathf.Clamp01(u * 15f) * Mathf.Pow(1 - u, 1.5f); }
                curve[0] = 0.6f;
                return new LightCue { Color = c.linear, Range = range, Intensity = intensity, Duration = duration, Curve = curve };
            }

            static float CustomEval(ParticleSystem.CustomDataModule cd, ParticleSystemCustomData s, int comp, float u)
            {
                if (cd.GetMode(s) != ParticleSystemCustomDataMode.Vector || comp >= cd.GetVectorComponentCount(s)) return 0;
                return Eval(cd.GetVector(s, comp), u, true);
            }

            // ---- curves and gradients, 64 samples, linear colour

            static Vector4[] Fill(Vector4 x)
            {
                var r = new Vector4[64];
                for (int i = 0; i < 64; i++) r[i] = x;
                return r;
            }

            static Vector4 Lin(Color c) { var l = c.linear; return new Vector4(l.r, l.g, l.b, c.a); }

            static Vector4[] Gradient(UnityEngine.Gradient g)
            {
                var r = new Vector4[64];
                for (int i = 0; i < 64; i++) r[i] = Lin(g.Evaluate(i / 63f));
                return r;
            }

            static Vector4[] Curve3(ParticleSystem.MinMaxCurve x, ParticleSystem.MinMaxCurve y, ParticleSystem.MinMaxCurve z, bool max, float k)
            {
                var r = new Vector4[64];
                for (int i = 0; i < 64; i++) { float u = i / 63f; r[i] = new Vector4(Eval(x, u, max), Eval(y, u, max), Eval(z, u, max), 0) * k; }
                return r;
            }

            static bool TwoValued(ParticleSystem.MinMaxCurve c) => c.mode == ParticleSystemCurveMode.TwoConstants || c.mode == ParticleSystemCurveMode.TwoCurves;

            public static float Eval(ParticleSystem.MinMaxCurve c, float u, bool max)
            {
                switch (c.mode)
                {
                    case ParticleSystemCurveMode.Constant: return c.constant;
                    case ParticleSystemCurveMode.TwoConstants: return max ? c.constantMax : c.constantMin;
                    case ParticleSystemCurveMode.Curve: return c.curve.Evaluate(u) * c.curveMultiplier;
                    default: return (max ? c.curveMax : c.curveMin).Evaluate(u) * c.curveMultiplier;
                }
            }

            static float Min(ParticleSystem.MinMaxCurve c) => c.mode == ParticleSystemCurveMode.TwoConstants ? c.constantMin : c.mode == ParticleSystemCurveMode.Constant ? c.constant : Eval(c, 0, false);
            static float Max(ParticleSystem.MinMaxCurve c) => c.mode == ParticleSystemCurveMode.TwoConstants ? c.constantMax : c.mode == ParticleSystemCurveMode.Constant ? c.constant : Eval(c, 0, true);
            static float Const(ParticleSystem.MinMaxCurve c) => c.mode == ParticleSystemCurveMode.Constant ? c.constant : (Min(c) + Max(c)) * 0.5f;

            public Texture2D BuildLut()
            {
                int h = Mathf.Max(1, LutRows.Count);
                var tex = new Texture2D(Slots.LutWidth, h, TextureFormat.RGBAFloat, false, true) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
                var px = new Color[Slots.LutWidth * h];
                for (int r = 0; r < LutRows.Count; r++)
                    for (int i = 0; i < Slots.LutWidth; i++) px[r * Slots.LutWidth + i] = LutRows[r][i];
                tex.SetPixels(px);
                tex.Apply(false, false);
                return tex;
            }

            /// <summary>Every texture resampled to one size, mipmapped, into an sRGB array (DXT5).</summary>
            public Texture2DArray BuildTextureArray()
            {
                int n = Mathf.Max(1, Textures.Count), s = TextureSize;
                int mips = (int)Mathf.Log(s, 2) + 1;
                var array = new Texture2DArray(s, s, n, TextureFormat.DXT5, mips, false) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = 2 };
                var rt = RenderTexture.GetTemporary(new RenderTextureDescriptor(s, s, RenderTextureFormat.ARGB32, 0) { sRGB = true, useMipMap = false });
                for (int i = 0; i < Textures.Count; i++)
                {
                    Graphics.Blit(Textures[i], rt);
                    var prev = RenderTexture.active;
                    RenderTexture.active = rt;
                    var tex = new Texture2D(s, s, TextureFormat.RGBA32, true, false);
                    tex.ReadPixels(new Rect(0, 0, s, s), 0, 0);
                    tex.Apply(true, false);
                    RenderTexture.active = prev;
                    EditorUtility.CompressTexture(tex, TextureFormat.DXT5, TextureCompressionQuality.Normal);
                    for (int m = 0; m < mips; m++) array.SetPixelData(tex.GetPixelData<byte>(m), m, i);
                    UnityEngine.Object.DestroyImmediate(tex);
                }
                RenderTexture.ReleaseTemporary(rt);
                array.Apply(false, false);
                return array;
            }
        }
    }
}
