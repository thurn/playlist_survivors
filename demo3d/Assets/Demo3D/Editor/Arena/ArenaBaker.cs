using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using PS.Choreography;
using Demo3D.Runtime.Arena;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Vector2 = System.Numerics.Vector2;

namespace Demo3D.EditorTools.Arena
{
    /// <summary>
    /// Generates the arena (TD9; environment.md, Layout and Lighting the night) from TD6's
    /// placement list into the gitignored Assets/Baked/Arena/: the kit's materials converted to
    /// URP Lit and saved, the ground, and one scene per rung and density:
    /// - <c>Arena-E0-&lt;d&gt;x</c> for every density (0.5, 1, 2, 4): the stock baseline, the kit's
    ///   prefabs unpacked as plain GameObjects (colliders and vendor scripts stripped), the moon
    ///   and every light realtime, every piece a shadow caster, no baked lighting;
    /// - <c>Arena-E2-&lt;d&gt;x</c> for the lit densities (1 and 2): the same placements with
    ///   lightmaps baked in batch mode (the moon mixed, shadowmask; the ground lightmapped, the
    ///   kit lit by a probe grid), glow lights baked; the runtime takes the static set out of the
    ///   cascades and renders the static shadow map;
    /// - <c>Arena-E0-&lt;d&gt;x-merged</c> and <c>Arena-E2-&lt;d&gt;x-merged</c> for the merged densities
    ///   (TD14, measured within rung E1): the same scenes with the small props (under
    ///   <see cref="MergeMaxM"/>, outside landmarks and structures) merged into one mesh per
    ///   <see cref="MergeCellM"/> cell, material and shadow mode, saved under Baked/Arena/Merged/.
    /// Each other environment (ps-bop; <see cref="ArenaEnvironment"/>) gets its own scenes, materials
    /// and ground under Baked/Arena/NAME/ ("Arena-NAME-E2-2x"), by default at the authored density
    /// only (<see cref="EnvironmentDensities"/>): E0 and E2, not merged.
    /// `demo3d unity BakeArena [-environment forest,crypt] [-e0 0.5,1,2,4] [-lit 0.5,1,2,4] [-merged 1,2] [-noLightmaps]`
    /// (densities given apply to every environment baked); also the "Arena" baker, which bakes them all.
    /// </summary>
    public static class ArenaBaker
    {
        public const string Dir = "Assets/Baked/Arena";
        public const string MaterialDir = Dir + "/Materials";
        public const string DataDir = "Assets/Demo3D/Choreography/Data";
        public const string LayoutPath = DataDir + "/arena-layout.json";
        public const string FootprintsPath = DataDir + "/footprints.json";
        public static readonly float[] E0Densities = { 0.5f, 1f, 2f, 4f };
        public static readonly float[] LitDensities = { 0.5f, 1f, 2f, 4f };
        public static readonly float[] MergedDensities = { 1f, 2f };
        /// <summary>
        /// The densities another environment bakes at by default: the authored one (the layout's
        /// decorationDensity, which the player loads and the timeline navigates). The forest's
        /// full set took most of the bake's time; each environment adds its look, not a ladder.
        /// </summary>
        public static float[] EnvironmentDensities(ArenaLayout layout) => new[] { layout.DecorationDensity };
        /// <summary>Props whose bounds' largest side is under this merge (TD14, E1): bushes, rocks, plants, logs, candles; trees stay instances.</summary>
        public const float MergeMaxM = ArenaOptions.SmallPropM;
        /// <summary>The merged meshes' cell: two probe spacings, so a merged mesh's one probe sample stays local.</summary>
        public const float MergeCellM = 16f;
        /// <summary>The ground's half-size: past the 160 m arena, so wide views near its edge see ground.</summary>
        public const float GroundHalfM = 110f;
        /// <summary>Lightmap texels per metre on the ground (the only lightmapped surface).</summary>
        public const float LightmapTexelsPerM = 6f;

        [InitializeOnLoadMethod]
        static void Register() => Bakers.Register("Arena", Bake);

        public static void Bake()
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var names = TaskArgs.Get("-environment")?.Split(',') ?? ArenaEnvironment.Names().ToArray();
            var environments = names.Select(ArenaEnvironment.Load).ToList(); // an unknown name fails before anything bakes
            bool lightmaps = !TaskArgs.Has("-noLightmaps");
            Directory.CreateDirectory(Dir + "/Resources");
            var table = FootprintTable.Parse(File.ReadAllText(FootprintsPath));
            var groundMesh = GroundMesh();
            var detail = DetailAlbedo();
            int materials = 0;

            var indexPath = $"{Dir}/Resources/{ArenaIndex.ResourcePath}.asset";
            var old = AssetDatabase.LoadAssetAtPath<ArenaIndex>(indexPath);
            var entries = old != null ? old.entries.ToList() : new List<ArenaIndex.Entry>();
            void Put(ArenaIndex.Entry e)
            {
                entries.RemoveAll(x => x.scene == e.scene);
                entries.Add(e);
            }
            foreach (var env in environments)
            {
                Directory.CreateDirectory(env.MaterialDir);
                Directory.CreateDirectory(env.MergedDir);
                var layout = ArenaLayout.Parse(env.Json);
                var lighting = ArenaLighting.Parse(env.Json);
                var converter = new MaterialConverter(env.MaterialDir, env.Root["lighting"]["kitValue"]);
                var ground = GroundMaterial(env, layout, detail);
                var authored = env.IsDefault ? null : EnvironmentDensities(layout);
                var e0 = Floats(TaskArgs.Get("-e0")) ?? authored ?? E0Densities;
                var lit = Floats(TaskArgs.Get("-lit")) ?? authored ?? LitDensities;
                var merged = Floats(TaskArgs.Get("-merged")) ?? (env.IsDefault ? MergedDensities : new float[0]);
                foreach (var d in e0) Put(Generate(env, ArenaIndex.E0, d, false, layout, table, lighting, converter, ground, groundMesh, false));
                foreach (var d in lit) Put(Generate(env, ArenaIndex.E2, d, false, layout, table, lighting, converter, ground, groundMesh, lightmaps));
                foreach (var d in merged)
                {
                    Put(Generate(env, ArenaIndex.E0, d, true, layout, table, lighting, converter, ground, groundMesh, false));
                    Put(Generate(env, ArenaIndex.E2, d, true, layout, table, lighting, converter, ground, groundMesh, lightmaps));
                }
                materials += converter.Count;
            }
            entries.RemoveAll(e => !File.Exists($"{ArenaEnvironment.DirOf(e.environment)}/{e.scene}.unity"));
            entries.Sort((a, b) =>
            {
                string ea = ArenaIndex.EnvironmentOf(a), eb = ArenaIndex.EnvironmentOf(b);
                if (ea != eb) return ea == LayoutEnvironment.Default ? -1 : eb == LayoutEnvironment.Default ? 1 : string.CompareOrdinal(ea, eb);
                return string.CompareOrdinal(a.rung, b.rung) != 0 ? string.CompareOrdinal(a.rung, b.rung)
                    : a.merged != b.merged ? a.merged.CompareTo(b.merged) : a.density.CompareTo(b.density);
            });

            var index = ScriptableObject.CreateInstance<ArenaIndex>();
            index.entries = entries.ToArray();
            AssetDatabase.DeleteAsset(indexPath);
            AssetDatabase.CreateAsset(index, indexPath);
            AssetDatabase.SaveAssets();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single); // leave no modified scene behind
            Debug.Log($"ArenaBaker: {entries.Count} scenes ({string.Join(", ", entries.Select(e => $"{e.scene}: {e.renderers} renderers, {e.triangles} tris"))}), " +
                      $"{materials} materials, in {sw.Elapsed.TotalSeconds:F0} s");
        }

        static float[] Floats(string s) =>
            string.IsNullOrEmpty(s) ? null : s.Split(',').Select(x => float.Parse(x, CultureInfo.InvariantCulture)).ToArray();

        static ArenaIndex.Entry Generate(ArenaEnvironment env, string rung, float density, bool merge, ArenaLayout layout, FootprintTable table, ArenaLighting lighting,
            MaterialConverter converter, Material groundMat, Mesh groundMesh, bool bakeLightmaps)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            bool lit = rung == ArenaIndex.E2;
            string name = ArenaIndex.SceneName(env.Name, rung, density, merge);
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var list = layout.WithDensity(density).Generate(table);
            var root = new GameObject("Arena");

            // The moon.
            var moon = new GameObject("Moon").AddComponent<Light>();
            moon.transform.SetParent(root.transform, false);
            moon.type = LightType.Directional;
            moon.transform.rotation = lighting.MoonRotation;
            moon.color = lighting.MoonColor;
            moon.intensity = lighting.MoonIntensity;
            moon.shadows = LightShadows.Soft;
            moon.lightmapBakeType = lit ? LightmapBakeType.Mixed : LightmapBakeType.Realtime;
            RenderSettings.sun = moon;
            NightLook.ApplyRenderSettings(lighting);

            // The ground.
            var groundGo = new GameObject("Ground");
            groundGo.transform.SetParent(root.transform, false);
            groundGo.AddComponent<MeshFilter>().sharedMesh = groundMesh;
            var groundR = groundGo.AddComponent<MeshRenderer>();
            groundR.sharedMaterial = groundMat;
            groundR.shadowCastingMode = ShadowCastingMode.Off;
            if (lit)
            {
                GameObjectUtility.SetStaticEditorFlags(groundGo, StaticEditorFlags.ContributeGI);
                groundR.receiveGI = ReceiveGI.Lightmaps;
                groundR.scaleInLightmap = 1f;
            }

            // The kit.
            var stat = new GameObject("Static").transform;
            stat.SetParent(root.transform, false);
            var groups = new Dictionary<string, Transform>();
            Transform Group(string g)
            {
                if (!groups.TryGetValue(g, out var t))
                {
                    t = new GameObject(g).transform;
                    t.SetParent(stat, false);
                    groups[g] = t;
                }
                return t;
            }
            var lightsRoot = new GameObject("Lights").transform;
            lightsRoot.SetParent(root.transform, false);
            var prefabs = new Dictionary<string, GameObject>();
            int flames = 0, lights = 0;
            long tris = 0;
            var stripped = new Dictionary<string, int>();
            foreach (var p in list.Items)
            {
                var prop = list.Props[p.Prop];
                if (!prefabs.TryGetValue(prop.Guid, out var prefab))
                {
                    var path = AssetDatabase.GUIDToAssetPath(prop.Guid);
                    prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path)
                        ?? throw new Exception($"ArenaBaker: kit piece {prop.Id} ({prop.Guid}) is missing; run demo3d setup");
                    prefabs[prop.Guid] = prefab;
                }
                var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                string group = p.Area >= 0 ? (layout.Areas[p.Area].Type == AreaType.Structure ? "Structures" : "Landmarks")
                    : (layout.Classify(p.Position)?.ToString() ?? "Forest");
                go.transform.SetParent(Group(group), false);
                go.transform.SetPositionAndRotation(new Vector3(p.Position.X, 0, p.Position.Y), Quaternion.Euler(0, p.Yaw * Mathf.Rad2Deg, 0));
                go.transform.localScale = prefab.transform.localScale * p.Scale;
                Strip(go, stripped);
                converter.Convert(go, lit);
                tris += Triangles(go);
                if (lit)
                {
                    foreach (var t in go.GetComponentsInChildren<Transform>(true))
                        GameObjectUtility.SetStaticEditorFlags(t.gameObject, StaticEditorFlags.ContributeGI);
                    foreach (var r in go.GetComponentsInChildren<MeshRenderer>(true)) r.receiveGI = ReceiveGI.LightProbes;
                }

                var flame = lighting.Flames.Find(f => f.Prop == prop.Id);
                if (flame != null && flames < lighting.MaxFlameLights)
                {
                    var l = PointLight(lightsRoot, prop.Id, go.transform.position + Vector3.up * flame.HeightM * p.Scale, flame, LightmapBakeType.Realtime);
                    var fl = l.gameObject.AddComponent<FlameFlicker>();
                    fl.baseIntensity = flame.Intensity;
                    fl.baseRange = flame.RangeM;
                    flames++;
                    lights++;
                }
                var glow = lighting.Glow.Find(f => f.Prop == prop.Id);
                if (glow != null)
                {
                    PointLight(lightsRoot, prop.Id, go.transform.position + Vector3.up * glow.HeightM * p.Scale, glow,
                        lit ? LightmapBakeType.Baked : LightmapBakeType.Realtime);
                    if (!lit) lights++;
                }
            }

            ArenaDressing.Dress(env, root.transform, stat, lit); // TD15: canopy fade, mist
            int renderers = stat.GetComponentsInChildren<Renderer>(true).Count(r => !(r is ParticleSystemRenderer));
            int mergedRenderers = 0;
            if (merge)
            {
                mergedRenderers = Merge(env, stat, name, lit);
                renderers = stat.GetComponentsInChildren<Renderer>(true).Count(r => !(r is ParticleSystemRenderer));
            }
            var path2 = env.ScenePath(rung, density, merge);
            if (lit) LightProbes(root.transform, layout);
            EditorSceneManager.SaveScene(scene, path2);
            if (lit && bakeLightmaps) BakeLightmaps(env, scene, name);
            Debug.Log($"ArenaBaker: {name}: {list.Items.Length} placements, {renderers} renderers, {tris} triangles, {lights} realtime lights " +
                      $"({flames} flames), stripped {string.Join(", ", stripped.Select(kv => kv.Key + " ×" + kv.Value))}, {sw.Elapsed.TotalSeconds:F1} s");
            return new ArenaIndex.Entry
            {
                scene = name, environment = env.Name, rung = rung, density = density, placements = list.Items.Length, renderers = renderers,
                lights = lights, triangles = tris, merged = merge, mergedRenderers = mergedRenderers,
            };
        }

        /// <summary>
        /// Merges the small props (largest bounds side under <see cref="MergeMaxM"/>, outside the
        /// landmarks and structures) into one mesh per <see cref="MergeCellM"/> cell, material,
        /// shadow mode and probe use, in world space, under Static/Merged. The pieces keep their
        /// transforms (and any particles); their mesh renderers go. Returns the merged renderers.
        /// </summary>
        static int Merge(ArenaEnvironment env, Transform stat, string sceneName, bool lit)
        {
            var groups = new Dictionary<(int, int, Material, ShadowCastingMode, LightProbeUsage), List<CombineInstance>>();
            var sources = new List<MeshRenderer>();
            foreach (Transform group in stat)
            {
                if (group.name == "Landmarks" || group.name == "Structures") continue;
                foreach (var r in group.GetComponentsInChildren<MeshRenderer>(true))
                {
                    var mf = r.GetComponent<MeshFilter>();
                    if (mf == null || mf.sharedMesh == null) continue;
                    var b = r.bounds;
                    if (Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z)) >= MergeMaxM) continue;
                    int cx = Mathf.FloorToInt(b.center.x / MergeCellM), cz = Mathf.FloorToInt(b.center.z / MergeCellM);
                    var mats = r.sharedMaterials;
                    for (int s = 0; s < mf.sharedMesh.subMeshCount && s < mats.Length; s++)
                    {
                        var key = (cx, cz, mats[s], r.shadowCastingMode, r.lightProbeUsage);
                        if (!groups.TryGetValue(key, out var list)) groups[key] = list = new List<CombineInstance>();
                        list.Add(new CombineInstance { mesh = mf.sharedMesh, subMeshIndex = s, transform = r.localToWorldMatrix });
                    }
                    sources.Add(r);
                }
            }
            var assetPath = $"{env.MergedDir}/{sceneName}.asset";
            AssetDatabase.DeleteAsset(assetPath);
            var root = new GameObject("Merged").transform;
            root.SetParent(stat, false);
            bool first = true;
            int k = 0;
            foreach (var kv in groups.OrderBy(g => g.Key.Item1).ThenBy(g => g.Key.Item2).ThenBy(g => g.Key.Item3.name))
            {
                var (cx, cz, mat, shadows, probes) = kv.Key;
                long verts = kv.Value.Sum(c => (long)c.mesh.vertexCount);
                var mesh = new Mesh
                {
                    name = $"{sceneName} {cx},{cz} {mat.name} {k}",
                    indexFormat = verts > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16,
                };
                mesh.CombineMeshes(kv.Value.ToArray(), true, true, false);
                mesh.RecalculateBounds();
                if (first) { AssetDatabase.CreateAsset(mesh, assetPath); first = false; }
                else AssetDatabase.AddObjectToAsset(mesh, assetPath);
                var go = new GameObject($"Merged {cx},{cz} {mat.name}");
                go.transform.SetParent(root, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = mat;
                mr.shadowCastingMode = shadows;
                mr.lightProbeUsage = probes;
                if (lit)
                {
                    GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.ContributeGI);
                    mr.receiveGI = ReceiveGI.LightProbes;
                }
                k++;
            }
            foreach (var r in sources)
            {
                var mf = r.GetComponent<MeshFilter>();
                UnityEngine.Object.DestroyImmediate(r);
                if (mf != null) UnityEngine.Object.DestroyImmediate(mf);
            }
            AssetDatabase.SaveAssets();
            Debug.Log($"ArenaBaker: {sceneName}: merged {sources.Count} small props into {k} renderers");
            return k;
        }

        static Light PointLight(Transform parent, string propId, Vector3 at, ArenaLighting.PropLight spec, LightmapBakeType mode)
        {
            var l = new GameObject("Light " + propId.Split('/').Last()).AddComponent<Light>();
            l.transform.SetParent(parent, false);
            l.transform.position = at;
            l.type = LightType.Point;
            l.color = spec.Color;
            l.range = spec.RangeM;
            l.intensity = spec.Intensity;
            l.shadows = LightShadows.None;
            l.lightmapBakeType = mode;
            return l;
        }

        /// <summary>Removes what a static environment piece doesn't draw with: colliders, vendor scripts, audio, prefab lights.</summary>
        static void Strip(GameObject go, Dictionary<string, int> stripped)
        {
            void Count(string what)
            {
                stripped.TryGetValue(what, out int n);
                stripped[what] = n + 1;
            }
            foreach (var t in go.GetComponentsInChildren<Transform>(true))
                if (GameObjectUtility.RemoveMonoBehavioursWithMissingScript(t.gameObject) > 0) Count("missing script");
            foreach (var c in go.GetComponentsInChildren<Collider>(true)) { UnityEngine.Object.DestroyImmediate(c); Count("collider"); }
            foreach (var c in go.GetComponentsInChildren<MonoBehaviour>(true)) { UnityEngine.Object.DestroyImmediate(c); Count("script"); }
            foreach (var c in go.GetComponentsInChildren<AudioSource>(true)) { UnityEngine.Object.DestroyImmediate(c); Count("audio"); }
            foreach (var c in go.GetComponentsInChildren<Light>(true)) { UnityEngine.Object.DestroyImmediate(c); Count("light"); }
        }

        static long Triangles(GameObject go)
        {
            long n = 0;
            var skip = new HashSet<Renderer>();
            foreach (var g in go.GetComponentsInChildren<LODGroup>(true))
            {
                var lods = g.GetLODs();
                for (int i = 1; i < lods.Length; i++) foreach (var r in lods[i].renderers) if (r != null) skip.Add(r);
            }
            foreach (var mf in go.GetComponentsInChildren<MeshFilter>(true))
            {
                var r = mf.GetComponent<Renderer>();
                if (mf.sharedMesh == null || (r != null && skip.Contains(r))) continue;
                for (int s = 0; s < mf.sharedMesh.subMeshCount; s++) n += mf.sharedMesh.GetIndexCount(s) / 3;
            }
            return n;
        }

        /// <summary>A probe grid for the kit's baked indirect light and shadowmask occlusion: every 8 m, at 0.5 m and 4 m.</summary>
        static void LightProbes(Transform root, ArenaLayout layout)
        {
            var go = new GameObject("LightProbes");
            go.transform.SetParent(root, false);
            var group = go.AddComponent<LightProbeGroup>();
            var pts = new List<Vector3>();
            float half = layout.Size.X / 2;
            for (float x = -half; x <= half + 0.01f; x += 8f)
            for (float z = -half; z <= half + 0.01f; z += 8f)
            {
                pts.Add(new Vector3(x, 0.5f, z));
                pts.Add(new Vector3(x, 4f, z));
            }
            group.probePositions = pts.ToArray();
        }

        static void BakeLightmaps(ArenaEnvironment env, Scene scene, string name)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var settingsPath = $"{env.Dir}/{name}.lighting";
            AssetDatabase.DeleteAsset(settingsPath);
            var ls = new LightingSettings { name = name };
            ls.bakedGI = true;
            ls.realtimeGI = false;
            ls.lightmapper = LightingSettings.Lightmapper.ProgressiveGPU;
            ls.mixedBakeMode = MixedLightingMode.Shadowmask;
            ls.lightmapResolution = LightmapTexelsPerM;
            ls.lightmapPadding = 2;
            ls.lightmapMaxSize = 2048;
            ls.directionalityMode = LightmapsMode.NonDirectional;
            ls.maxBounces = 2;
            ls.directSampleCount = 32;
            ls.indirectSampleCount = 256;
            ls.environmentSampleCount = 128;
            ls.lightmapCompression = LightmapCompression.NormalQuality;
            ls.ao = false;
            AssetDatabase.CreateAsset(ls, settingsPath);
            Lightmapping.lightingSettings = ls;
            if (!Lightmapping.Bake()) throw new Exception("ArenaBaker: Lightmapping.Bake failed for " + name);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log($"ArenaBaker: baked {name}'s lightmaps ({LightmapSettings.lightmaps.Length} maps) in {sw.Elapsed.TotalSeconds:F0} s");
        }

        // ---- The ground: our own mesh and an albedo generated from the layout's ground types.

        static Mesh GroundMesh()
        {
            const string path = Dir + "/Ground.asset";
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (mesh == null)
            {
                mesh = new Mesh { name = "ArenaGround" };
                AssetDatabase.CreateAsset(mesh, path);
            }
            mesh.Clear();
            const int cells = 110; // 2 m cells
            int n = cells + 1;
            var v = new Vector3[n * n];
            var uv = new UnityEngine.Vector2[n * n];
            var nrm = new Vector3[n * n];
            var tan = new Vector4[n * n];
            for (int j = 0; j < n; j++)
            for (int i = 0; i < n; i++)
            {
                float u = i / (float)cells, w = j / (float)cells;
                v[j * n + i] = new Vector3(-GroundHalfM + u * 2 * GroundHalfM, 0, -GroundHalfM + w * 2 * GroundHalfM);
                uv[j * n + i] = new UnityEngine.Vector2(u, w);
                nrm[j * n + i] = Vector3.up;
                tan[j * n + i] = new Vector4(1, 0, 0, 1);
            }
            var idx = new int[cells * cells * 6];
            int k = 0;
            for (int j = 0; j < cells; j++)
            for (int i = 0; i < cells; i++)
            {
                int a = j * n + i;
                idx[k++] = a; idx[k++] = a + n; idx[k++] = a + 1;
                idx[k++] = a + 1; idx[k++] = a + n; idx[k++] = a + n + 1;
            }
            mesh.vertices = v;
            mesh.normals = nrm;
            mesh.tangents = tan;
            mesh.uv = uv;
            mesh.uv2 = uv; // the lightmap's UVs: the whole ground, uniformly
            mesh.triangles = idx;
            mesh.RecalculateBounds();
            EditorUtility.SetDirty(mesh);
            return mesh;
        }

        /// <summary>
        /// The ground's colours by type (sRGB), from the layout's <c>ground</c> (ps-bop): the forest's
        /// are a dark forest floor, a darker edge band, and warm, mid-value earth on the fighting
        /// ground (the look epic ps-e8r: a lit stage, darkness at the edges).
        /// </summary>
        public struct GroundColors
        {
            public Color Forest, Edge, Clearing, Path;

            public static GroundColors Parse(JsonValue ground)
            {
                Color C(string key)
                {
                    var v = ground[key];
                    if (v.Count < 3) throw new FormatException($"ArenaBaker: the layout's ground needs {key} [r, g, b]");
                    return new Color(v[0].AsFloat(), v[1].AsFloat(), v[2].AsFloat());
                }
                return new GroundColors { Forest = C("forest"), Edge = C("edge"), Clearing = C("clearing"), Path = C("path") };
            }
        }

        static Material GroundMaterial(ArenaEnvironment env, ArenaLayout layout, Texture2D detail)
        {
            var colors = GroundColors.Parse(env.Root["ground"]);
            string texPath = env.Dir + "/GroundAlbedo.png";
            const int size = 1024;
            var tex = new Texture2D(size, size, TextureFormat.RGB24, false);
            var px = new Color32[size * size];
            float m = 2 * GroundHalfM / size;
            for (int j = 0; j < size; j++)
            for (int i = 0; i < size; i++)
            {
                float x = -GroundHalfM + (i + 0.5f) * m, z = -GroundHalfM + (j + 0.5f) * m;
                px[j * size + i] = GroundColor(layout, colors, x, z);
            }
            tex.SetPixels32(px);
            tex.Apply();
            File.WriteAllBytes(texPath, tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(texPath, ImportAssetOptions.ForceUpdate);
            var imp = (TextureImporter)AssetImporter.GetAtPath(texPath);
            imp.sRGBTexture = true;
            imp.mipmapEnabled = true;
            imp.wrapMode = TextureWrapMode.Clamp;
            imp.anisoLevel = 4;
            imp.textureCompression = TextureImporterCompression.CompressedHQ;
            imp.SaveAndReimport();
            var albedo = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);

            string matPath = env.Dir + "/Ground.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (mat == null)
            {
                mat = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "ArenaGround" };
                AssetDatabase.CreateAsset(mat, matPath);
            }
            mat.shader = Shader.Find("Universal Render Pipeline/Lit");
            mat.SetTexture("_BaseMap", albedo);
            mat.SetColor("_BaseColor", Color.white);
            mat.SetFloat("_Smoothness", 0.12f);
            mat.SetFloat("_Metallic", 0f);
            // Our generated cracks and stones as a detail albedo every DetailM, with the kit's own
            // ground normals (Elven Realm's, already synced for its rock shader) as its detail
            // normal map at the same tiling, so the moon and the hero's light catch the ground.
            mat.SetTexture("_DetailAlbedoMap", detail);
            mat.SetFloat("_DetailAlbedoMapScale", 1f);
            mat.SetTextureScale("_DetailAlbedoMap", new UnityEngine.Vector2(2 * GroundHalfM / DetailM, 2 * GroundHalfM / DetailM));
            var normals = FindVendorTexture("Ground_Normals_01");
            mat.SetTexture("_DetailNormalMap", normals);
            mat.SetFloat("_DetailNormalMapScale", 0.8f);
            mat.EnableKeyword("_DETAIL_MULX2");
            EditorUtility.SetDirty(mat);
            return mat;
        }

        /// <summary>The detail layer's tile (m): about 1.2 cm per pixel of <see cref="DetailAlbedo"/>.</summary>
        const float DetailM = 6f;

        /// <summary>
        /// The ground's tiling detail (linear; URP Lit's detail albedo doubles it, so 0.5 leaves the
        /// albedo as it is): mottled soil, a network of dark cracks between irregular cells, and
        /// scattered pebbles lit from the moon's side with a shadow on the other. Every term wraps
        /// at the tile's edges.
        /// </summary>
        static Texture2D DetailAlbedo()
        {
            const string path = Dir + "/GroundDetail.png";
            const int size = 512, cells = 7, pebbles = 110;
            var rng = new System.Random(6061);
            // Crack cells: jittered points on a cells x cells lattice, so a cell is about 0.85 m.
            var seeds = new UnityEngine.Vector2[cells * cells];
            for (int j = 0; j < cells; j++)
            for (int i = 0; i < cells; i++)
                seeds[j * cells + i] = new UnityEngine.Vector2((i + 0.05f + 0.9f * (float)rng.NextDouble()) / cells,
                    (j + 0.05f + 0.9f * (float)rng.NextDouble()) / cells);
            var stones = new (float x, float y, float r, float v)[pebbles];
            for (int k = 0; k < pebbles; k++)
            {
                float u = (float)rng.NextDouble();
                stones[k] = ((float)rng.NextDouble(), (float)rng.NextDouble(),
                    (k < 6 ? 0.012f + 0.008f * u : 0.003f + 0.007f * u * u), -0.06f + 0.16f * (float)rng.NextDouble());
            }
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float u = (x + 0.5f) / size, w = (y + 0.5f) / size;
                // Mottling: two octaves of value noise that wrap at the tile.
                float mottle = 0.6f * TileNoise(u, w, 8, 11) + 0.4f * TileNoise(u, w, 32, 23) - 0.5f;
                float v = 0.5f + 0.24f * mottle;
                // Cracks: where the nearest two cell points are about equally near, with the
                // coordinates warped so the edges wander.
                float wu = u + 0.025f * (TileNoise(u, w, 12, 37) - 0.5f), ww = w + 0.025f * (TileNoise(u, w, 12, 41) - 0.5f);
                float f1 = float.MaxValue, f2 = float.MaxValue;
                foreach (var c in seeds)
                {
                    float dx = Mathf.Repeat(wu - c.x + 0.5f, 1f) - 0.5f, dy = Mathf.Repeat(ww - c.y + 0.5f, 1f) - 0.5f;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    if (d < f1) { f2 = f1; f1 = d; }
                    else if (d < f2) f2 = d;
                }
                // Cracks run 2-4 cm wide and fade out along about half the cells' edges, so they
                // read as a broken crust, not paving.
                float width = 0.003f + 0.003f * TileNoise(u, w, 6, 53);
                float crack = (1 - Smooth(0, width, f2 - f1)) * Smooth(0.4f, 0.65f, TileNoise(u, w, 5, 59));
                v *= 1 - 0.6f * crack;
                // Pebbles: a lit dome with a shadow toward the far side of the moon.
                foreach (var s in stones)
                {
                    float dx = Mathf.Repeat(u - s.x + 0.5f, 1f) - 0.5f, dy = Mathf.Repeat(w - s.y + 0.5f, 1f) - 0.5f;
                    float d = Mathf.Sqrt(dx * dx + dy * dy) / s.r;
                    float sd = Mathf.Sqrt((dx - 0.35f * s.r) * (dx - 0.35f * s.r) + (dy + 0.35f * s.r) * (dy + 0.35f * s.r)) / s.r;
                    if (d < 1) v = Mathf.Lerp(v, 0.5f + s.v + 0.06f * (dy / s.r - dx / s.r), Smooth(1, 0.6f, d));
                    else if (sd < 1.1f) v *= Mathf.Lerp(0.75f, 1, Smooth(0.6f, 1.1f, sd));
                }
                byte b = (byte)Mathf.Clamp(Mathf.RoundToInt(v * 255), 0, 255);
                px[y * size + x] = new Color32(b, b, b, 255);
            }
            var tex = new Texture2D(size, size, TextureFormat.RGB24, false);
            tex.SetPixels32(px);
            tex.Apply();
            File.WriteAllBytes(path, tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            var imp = (TextureImporter)AssetImporter.GetAtPath(path);
            imp.sRGBTexture = false;
            imp.mipmapEnabled = true;
            imp.wrapMode = TextureWrapMode.Repeat;
            imp.anisoLevel = 8;
            imp.textureCompression = TextureImporterCompression.CompressedHQ;
            imp.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        /// <summary>Value noise on a period x period lattice over the unit square, wrapping at its edges (0..1).</summary>
        static float TileNoise(float u, float w, int period, uint salt)
        {
            float gx = u * period, gy = w * period;
            int x0 = Mathf.FloorToInt(gx), y0 = Mathf.FloorToInt(gy);
            float fx = gx - x0, fy = gy - y0;
            fx = fx * fx * (3 - 2 * fx); fy = fy * fy * (3 - 2 * fy);
            float N(int x, int y)
            {
                uint h = (uint)((x % period + period) % period) * 73856093u ^ (uint)((y % period + period) % period) * 19349663u ^ salt * 83492791u;
                h ^= h >> 13; h *= 0x5bd1e995u; h ^= h >> 15;
                return (h & 0xFFFFFF) / (float)0x1000000;
            }
            return Mathf.Lerp(Mathf.Lerp(N(x0, y0), N(x0 + 1, y0), fx), Mathf.Lerp(N(x0, y0 + 1), N(x0 + 1, y0 + 1), fx), fy);
        }

        static Texture2D FindVendorTexture(string name)
        {
            foreach (var guid in AssetDatabase.FindAssets(name + " t:Texture2D", new[] { "Assets/Vendor" }))
            {
                var p = AssetDatabase.GUIDToAssetPath(guid);
                if (Path.GetFileNameWithoutExtension(p) == name) return AssetDatabase.LoadAssetAtPath<Texture2D>(p);
            }
            return null;
        }

        /// <summary>The ground's colour at (x, z): by distance to fighting ground and its type, with three octaves of noise.</summary>
        public static Color32 GroundColor(ArenaLayout layout, GroundColors colors, float x, float z)
        {
            var p = new Vector2(x, z);
            float d = layout.FightingDistance(p, out AreaType type);
            Color fight = type == AreaType.Path ? colors.Path : colors.Clearing;
            // Inside fighting ground, worn paths mix into clearings near their axis.
            Color c;
            if (d <= 0) c = Color.Lerp(fight, colors.Clearing, Mathf.Clamp01(-d / 6f) * (type == AreaType.Path ? 0.3f : 0f));
            else if (d < 5) c = Color.Lerp(fight, colors.Edge, Smooth(0, 5, d));
            else c = Color.Lerp(colors.Edge, colors.Forest, Smooth(5, 12, d));
            float n = 0.6f * Mathf.PerlinNoise(x * 0.11f + 31.7f, z * 0.11f + 17.3f) + 0.4f * Mathf.PerlinNoise(x * 0.47f + 5.1f, z * 0.47f + 9.9f);
            // Patches about 30 m across, so the detail layer's 6 m tile doesn't show as a grid.
            float k = (0.72f + 0.56f * n) * (0.75f + 0.5f * Mathf.PerlinNoise(x * 0.033f + 47.2f, z * 0.033f + 61.8f));
            float hue = Mathf.PerlinNoise(x * 0.05f + 71f, z * 0.05f + 3f) - 0.5f; // moss to soil
            c = new Color(c.r * k * (1 + 0.15f * hue), c.g * k, c.b * k * (1 - 0.1f * hue));
            return c;
        }

        static float Smooth(float a, float b, float x)
        {
            float t = Mathf.Clamp01((x - a) / (b - a));
            return t * t * (3 - 2 * t);
        }
    }

    /// <summary>
    /// The kit's materials converted to URP and saved under Baked/ (the saved counterpart of
    /// TD2's in-memory UrpMaterials): Standard to URP Lit as Unity's converter maps it (albedo,
    /// colour, normal map, metallic-gloss map, emission, alpha clip), the candles' flame
    /// particles to URP's unlit additive particles, and Elven Realm's triplanar rock to
    /// Demo3D/RockTriplanar (TD14's port, at every rung).
    /// </summary>
    public sealed class MaterialConverter
    {
        readonly string _dir;
        readonly JsonValue _kitValue;
        readonly Dictionary<Material, Material> _done = new Dictionary<Material, Material>();
        readonly Dictionary<Material, Material> _static = new Dictionary<Material, Material>();
        public int Count => _done.Count + _static.Count;

        /// <summary>Converts into <paramref name="dir"/>, scaling base colours by the environment's <c>lighting.kitValue</c>.</summary>
        public MaterialConverter(string dir, JsonValue kitValue)
        {
            _dir = dir;
            _kitValue = kitValue;
        }

        /// <summary>
        /// Converts every material on <paramref name="go"/>. With <paramref name="staticShadow"/>
        /// (the E2 scenes) the Lit materials become Demo3D/LitStaticShadow twins: the kit is lit
        /// by probes there, which carry no shadow of the static set, so the kit takes the static
        /// shadow map as the crowd does (a rock under a tree stays in its shade).
        /// </summary>
        public void Convert(GameObject go, bool staticShadow = false)
        {
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < mats.Length; i++)
                {
                    if (mats[i] == null) continue;
                    var c = Converted(mats[i]);
                    if (staticShadow) c = StaticTwin(c);
                    if (c != mats[i]) { mats[i] = c; changed = true; }
                }
                if (changed) r.sharedMaterials = mats;
            }
        }

        Material Converted(Material m)
        {
            if (_done.TryGetValue(m, out var c)) return c;
            var path = $"{_dir}/{Safe(m.name)}.mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            c = Build(m, existing, _kitValue);
            if (existing == null) AssetDatabase.CreateAsset(c, path);
            else EditorUtility.SetDirty(c);
            _done[m] = c;
            return c;
        }

        Material StaticTwin(Material lit)
        {
            if (lit.shader == null || lit.shader.name != "Universal Render Pipeline/Lit") return lit;
            if (_static.TryGetValue(lit, out var twin)) return twin;
            var path = $"{_dir}/{Safe(lit.name)}_StaticShadow.mat";
            twin = AssetDatabase.LoadAssetAtPath<Material>(path);
            var shader = Shader.Find("Demo3D/LitStaticShadow") ?? throw new Exception("MaterialConverter: no Demo3D/LitStaticShadow");
            if (twin == null)
            {
                twin = new Material(lit) { name = lit.name + " static" };
                twin.shader = shader;
                twin.shaderKeywords = lit.shaderKeywords;
                twin.renderQueue = lit.renderQueue;
                AssetDatabase.CreateAsset(twin, path);
            }
            else
            {
                twin.CopyPropertiesFromMaterial(lit);
                twin.shader = shader;
                twin.shaderKeywords = lit.shaderKeywords;
                EditorUtility.SetDirty(twin);
            }
            _static[lit] = twin;
            return twin;
        }

        static Material Build(Material m, Material into, JsonValue kitValue)
        {
            string shader = m.shader != null ? m.shader.name : "";
            if (shader.StartsWith("Particles/")) return Particle(m, into);
            if (shader == "SyntyStudios_RockTriplanar") return Triplanar(m, into, kitValue);
            var lit = Target(into, "Universal Render Pipeline/Lit", m.name);
            var tex = m.HasProperty("_MainTex") ? m.GetTexture("_MainTex") : null;
            lit.SetTexture("_BaseMap", tex);
            lit.SetTextureScale("_BaseMap", m.HasProperty("_MainTex") ? m.GetTextureScale("_MainTex") : UnityEngine.Vector2.one);
            lit.SetColor("_BaseColor", ArenaDressing.KitValue(kitValue, m.HasProperty("_Color") ? m.GetColor("_Color") : Color.white, m.name)); // TD15: the kit's value
            float gloss = m.HasProperty("_Glossiness") ? m.GetFloat("_Glossiness") : 0.5f;
            var mg = m.HasProperty("_MetallicGlossMap") ? m.GetTexture("_MetallicGlossMap") : null;
            if (mg != null && m.IsKeywordEnabled("_METALLICGLOSSMAP"))
            {
                lit.SetTexture("_MetallicGlossMap", mg);
                lit.EnableKeyword("_METALLICSPECGLOSSMAP");
                lit.SetFloat("_Smoothness", m.HasProperty("_GlossMapScale") ? m.GetFloat("_GlossMapScale") : 1f);
            }
            else
            {
                lit.SetFloat("_Smoothness", gloss);
                lit.SetFloat("_Metallic", m.HasProperty("_Metallic") ? m.GetFloat("_Metallic") : 0f);
            }
            var bump = m.HasProperty("_BumpMap") ? m.GetTexture("_BumpMap") : null;
            if (bump != null)
            {
                lit.SetTexture("_BumpMap", bump);
                lit.SetFloat("_BumpScale", m.HasProperty("_BumpScale") ? m.GetFloat("_BumpScale") : 1f);
                lit.EnableKeyword("_NORMALMAP");
            }
            if (m.IsKeywordEnabled("_EMISSION") && m.HasProperty("_EmissionColor"))
            {
                var e = m.GetColor("_EmissionColor");
                var emap = m.HasProperty("_EmissionMap") ? m.GetTexture("_EmissionMap") : null;
                if (e.maxColorComponent > 0.001f)
                {
                    lit.SetTexture("_EmissionMap", emap);
                    lit.SetColor("_EmissionColor", e);
                    lit.EnableKeyword("_EMISSION");
                    lit.globalIlluminationFlags = MaterialGlobalIlluminationFlags.BakedEmissive;
                }
            }
            if ((m.HasProperty("_Mode") && Mathf.Approximately(m.GetFloat("_Mode"), 1f)) || m.IsKeywordEnabled("_ALPHATEST_ON"))
            {
                lit.SetFloat("_AlphaClip", 1f);
                lit.EnableKeyword("_ALPHATEST_ON");
                lit.SetFloat("_Cutoff", m.HasProperty("_Cutoff") ? m.GetFloat("_Cutoff") : 0.5f);
                lit.renderQueue = (int)RenderQueue.AlphaTest;
                lit.SetOverrideTag("RenderType", "TransparentCutout");
            }
            return lit;
        }

        /// <summary>
        /// Elven Realm's triplanar rock to Demo3D/RockTriplanar (TD14), its properties mapped onto
        /// URP Lit's names as the shader's header lists.
        /// </summary>
        static Material Triplanar(Material m, Material into, JsonValue kitValue)
        {
            var t = Target(into, "Demo3D/RockTriplanar", m.name);
            Texture Tex(string p) => m.HasProperty(p) ? m.GetTexture(p) : null;
            float F(string p, float d) => m.HasProperty(p) ? m.GetFloat(p) : d;
            Color C(string p) => m.HasProperty(p) ? m.GetColor(p) : Color.white;
            t.SetTexture("_BaseMap", Tex("_TriplanarAlbedo"));
            float bottom = F("_TilingBottom", 0.2f), top = F("_TilingTop", 0.2f);
            t.SetTextureScale("_BaseMap", new UnityEngine.Vector2(bottom, bottom));
            t.SetTexture("_BumpMap", Tex("_Normal"));
            t.SetFloat("_BumpScale", 1f);
            t.SetTexture("_DetailAlbedoMap", Tex("_TopAlbedo"));
            t.SetTextureScale("_DetailAlbedoMap", new UnityEngine.Vector2(top, top));
            t.SetTexture("_DetailNormalMap", Tex("_TopNormal"));
            t.SetFloat("_DetailNormalMapScale", 1f);
            var bc = C("_BottomColourShift");
            var tc = C("_TopColourShift");
            t.SetColor("_BaseColor", ArenaDressing.KitValue(kitValue, new Color(bc.r, bc.g, bc.b, 1), m.name)); // TD15: the kit's value
            t.SetColor("_SpecColor", ArenaDressing.KitValue(kitValue, new Color(tc.r, tc.g, tc.b, 1), m.name));
            t.SetFloat("_ClearCoatMask", F("_CoverageAmount", 0f));
            t.SetFloat("_ClearCoatSmoothness", F("_CoverageFalloff", 0.5f));
            t.SetFloat("_Metallic", F("_Metallic", 0.02f));
            t.SetFloat("_Smoothness", F("_Smoothness", 0.2f));
            return t;
        }

        static Material Particle(Material m, Material into)
        {
            var p = Target(into, "Universal Render Pipeline/Particles/Unlit", m.name);
            p.SetTexture("_BaseMap", m.HasProperty("_MainTex") ? m.GetTexture("_MainTex") : null);
            var col = m.HasProperty("_Color") ? m.GetColor("_Color") : m.HasProperty("_TintColor") ? m.GetColor("_TintColor") : Color.white;
            p.SetColor("_BaseColor", col);
            // Transparent, additive (the flames' glow), no depth write.
            p.SetFloat("_Surface", 1f);
            p.SetFloat("_Blend", 2f);
            p.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            p.SetFloat("_DstBlend", (float)BlendMode.One);
            p.SetFloat("_ZWrite", 0f);
            p.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            p.SetOverrideTag("RenderType", "Transparent");
            p.renderQueue = (int)RenderQueue.Transparent;
            return p;
        }

        static Material Target(Material into, string shaderName, string name)
        {
            var shader = Shader.Find(shaderName) ?? throw new Exception("MaterialConverter: no shader " + shaderName);
            if (into == null) return new Material(shader) { name = name + " (URP)" };
            into.shader = shader;
            into.shaderKeywords = new string[0];
            return into;
        }

        static string Safe(string name) => string.Concat(name.Select(ch => char.IsLetterOrDigit(ch) || ch == '_' ? ch : '_'));
    }
}
