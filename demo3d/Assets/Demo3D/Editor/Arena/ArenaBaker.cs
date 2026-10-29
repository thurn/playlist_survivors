using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Demo3D.Choreography;
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
    /// `demo3d unity BakeArena [-e0 0.5,1,2,4] [-lit 0.5,1,2,4] [-merged 1,2] [-noLightmaps]`; also the "Arena" baker.
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
        public const string MergedDir = Dir + "/Merged";
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
            var e0 = Floats(TaskArgs.Get("-e0")) ?? E0Densities;
            var lit = Floats(TaskArgs.Get("-lit")) ?? LitDensities;
            var merged = Floats(TaskArgs.Get("-merged")) ?? MergedDensities;
            bool lightmaps = !TaskArgs.Has("-noLightmaps");
            Directory.CreateDirectory(MaterialDir);
            Directory.CreateDirectory(MergedDir);
            Directory.CreateDirectory(Dir + "/Resources");
            var layoutJson = File.ReadAllText(LayoutPath);
            var layout = ArenaLayout.Parse(layoutJson);
            var table = FootprintTable.Parse(File.ReadAllText(FootprintsPath));
            var lighting = ArenaLighting.Parse(layoutJson);
            var converter = new MaterialConverter(MaterialDir);
            var ground = GroundMaterial(layout);
            var groundMesh = GroundMesh();

            var indexPath = $"{Dir}/Resources/{ArenaIndex.ResourcePath}.asset";
            var old = AssetDatabase.LoadAssetAtPath<ArenaIndex>(indexPath);
            var entries = old != null ? old.entries.ToList() : new List<ArenaIndex.Entry>();
            void Put(ArenaIndex.Entry e)
            {
                entries.RemoveAll(x => x.scene == e.scene);
                entries.Add(e);
            }
            foreach (var d in e0) Put(Generate(ArenaIndex.E0, d, false, layout, table, lighting, converter, ground, groundMesh, false));
            foreach (var d in lit) Put(Generate(ArenaIndex.E2, d, false, layout, table, lighting, converter, ground, groundMesh, lightmaps));
            foreach (var d in merged)
            {
                Put(Generate(ArenaIndex.E0, d, true, layout, table, lighting, converter, ground, groundMesh, false));
                Put(Generate(ArenaIndex.E2, d, true, layout, table, lighting, converter, ground, groundMesh, lightmaps));
            }
            entries.RemoveAll(e => !File.Exists($"{Dir}/{e.scene}.unity"));
            entries.Sort((a, b) => string.CompareOrdinal(a.rung, b.rung) != 0 ? string.CompareOrdinal(a.rung, b.rung)
                : a.merged != b.merged ? a.merged.CompareTo(b.merged) : a.density.CompareTo(b.density));

            var index = ScriptableObject.CreateInstance<ArenaIndex>();
            index.entries = entries.ToArray();
            AssetDatabase.DeleteAsset(indexPath);
            AssetDatabase.CreateAsset(index, indexPath);
            AssetDatabase.SaveAssets();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single); // leave no modified scene behind
            Debug.Log($"ArenaBaker: {entries.Count} scenes ({string.Join(", ", entries.Select(e => $"{e.scene}: {e.renderers} renderers, {e.triangles} tris"))}), " +
                      $"{converter.Count} materials, in {sw.Elapsed.TotalSeconds:F0} s");
        }

        static float[] Floats(string s) =>
            string.IsNullOrEmpty(s) ? null : s.Split(',').Select(x => float.Parse(x, CultureInfo.InvariantCulture)).ToArray();

        public static string ScenePath(string rung, float density, bool merged = false) => $"{Dir}/{ArenaIndex.SceneName(rung, density, merged)}.unity";

        static ArenaIndex.Entry Generate(string rung, float density, bool merge, ArenaLayout layout, FootprintTable table, ArenaLighting lighting,
            MaterialConverter converter, Material groundMat, Mesh groundMesh, bool bakeLightmaps)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            bool lit = rung == ArenaIndex.E2;
            string name = ArenaIndex.SceneName(rung, density, merge);
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

            ArenaDressing.Dress(root.transform, stat, lit); // TD15: canopy fade, mist
            int renderers = stat.GetComponentsInChildren<Renderer>(true).Count(r => !(r is ParticleSystemRenderer));
            int mergedRenderers = 0;
            if (merge)
            {
                mergedRenderers = Merge(stat, name, lit);
                renderers = stat.GetComponentsInChildren<Renderer>(true).Count(r => !(r is ParticleSystemRenderer));
            }
            var path2 = ScenePath(rung, density, merge);
            if (lit) LightProbes(root.transform, layout);
            EditorSceneManager.SaveScene(scene, path2);
            if (lit && bakeLightmaps) BakeLightmaps(scene, name);
            Debug.Log($"ArenaBaker: {name}: {list.Items.Length} placements, {renderers} renderers, {tris} triangles, {lights} realtime lights " +
                      $"({flames} flames), stripped {string.Join(", ", stripped.Select(kv => kv.Key + " ×" + kv.Value))}, {sw.Elapsed.TotalSeconds:F1} s");
            return new ArenaIndex.Entry
            {
                scene = name, rung = rung, density = density, placements = list.Items.Length, renderers = renderers,
                lights = lights, triangles = tris, merged = merge, mergedRenderers = mergedRenderers,
            };
        }

        /// <summary>
        /// Merges the small props (largest bounds side under <see cref="MergeMaxM"/>, outside the
        /// landmarks and structures) into one mesh per <see cref="MergeCellM"/> cell, material,
        /// shadow mode and probe use, in world space, under Static/Merged. The pieces keep their
        /// transforms (and any particles); their mesh renderers go. Returns the merged renderers.
        /// </summary>
        static int Merge(Transform stat, string sceneName, bool lit)
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
            var assetPath = $"{MergedDir}/{sceneName}.asset";
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

        static void BakeLightmaps(Scene scene, string name)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var settingsPath = $"{Dir}/{name}.lighting";
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

        /// <summary>The ground's colours by type (sRGB): forest floor, edge band, clearing moss, path dirt.</summary>
        static readonly Color Forest = new Color(0.14f, 0.15f, 0.11f), Edge = new Color(0.20f, 0.23f, 0.15f),
            Clearing = new Color(0.27f, 0.31f, 0.19f), PathDirt = new Color(0.33f, 0.29f, 0.21f);

        static Material GroundMaterial(ArenaLayout layout)
        {
            const string texPath = Dir + "/GroundAlbedo.png";
            const int size = 1024;
            var tex = new Texture2D(size, size, TextureFormat.RGB24, false);
            var px = new Color32[size * size];
            float m = 2 * GroundHalfM / size;
            for (int j = 0; j < size; j++)
            for (int i = 0; i < size; i++)
            {
                float x = -GroundHalfM + (i + 0.5f) * m, z = -GroundHalfM + (j + 0.5f) * m;
                px[j * size + i] = GroundColor(layout, x, z);
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

            const string matPath = Dir + "/Ground.mat";
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
            // The kit's own ground normals (Elven Realm's, already synced for its rock shader) as a
            // detail normal map every 3 m, so the moon catches the forest floor.
            var normals = FindVendorTexture("Ground_Normals_01");
            if (normals != null)
            {
                mat.SetTexture("_DetailNormalMap", normals);
                mat.SetFloat("_DetailNormalMapScale", 0.8f);
                mat.SetTextureScale("_DetailAlbedoMap", new UnityEngine.Vector2(2 * GroundHalfM / 3f, 2 * GroundHalfM / 3f));
                mat.EnableKeyword("_DETAIL_MULX2");
            }
            EditorUtility.SetDirty(mat);
            return mat;
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

        /// <summary>The ground's colour at (x, z): by distance to fighting ground and its type, with two octaves of noise.</summary>
        public static Color32 GroundColor(ArenaLayout layout, float x, float z)
        {
            var p = new Vector2(x, z);
            float d = layout.FightingDistance(p, out AreaType type);
            Color fight = type == AreaType.Path ? PathDirt : Clearing;
            // Inside fighting ground, worn paths mix into clearings near their axis.
            Color c;
            if (d <= 0) c = Color.Lerp(fight, Clearing, Mathf.Clamp01(-d / 6f) * (type == AreaType.Path ? 0.3f : 0f));
            else if (d < 5) c = Color.Lerp(fight, Edge, Smooth(0, 5, d));
            else c = Color.Lerp(Edge, Forest, Smooth(5, 12, d));
            float n = 0.6f * Mathf.PerlinNoise(x * 0.11f + 31.7f, z * 0.11f + 17.3f) + 0.4f * Mathf.PerlinNoise(x * 0.47f + 5.1f, z * 0.47f + 9.9f);
            float k = 0.72f + 0.56f * n;
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
        readonly Dictionary<Material, Material> _done = new Dictionary<Material, Material>();
        readonly Dictionary<Material, Material> _static = new Dictionary<Material, Material>();
        public int Count => _done.Count + _static.Count;

        public MaterialConverter(string dir) { _dir = dir; }

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
            c = Build(m, existing);
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

        static Material Build(Material m, Material into)
        {
            string shader = m.shader != null ? m.shader.name : "";
            if (shader.StartsWith("Particles/")) return Particle(m, into);
            if (shader == "SyntyStudios_RockTriplanar") return Triplanar(m, into);
            var lit = Target(into, "Universal Render Pipeline/Lit", m.name);
            var tex = m.HasProperty("_MainTex") ? m.GetTexture("_MainTex") : null;
            lit.SetTexture("_BaseMap", tex);
            lit.SetTextureScale("_BaseMap", m.HasProperty("_MainTex") ? m.GetTextureScale("_MainTex") : UnityEngine.Vector2.one);
            lit.SetColor("_BaseColor", ArenaDressing.KitValue(m.HasProperty("_Color") ? m.GetColor("_Color") : Color.white, m.name)); // TD15: the kit's value
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
        static Material Triplanar(Material m, Material into)
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
            t.SetColor("_BaseColor", ArenaDressing.KitValue(new Color(bc.r, bc.g, bc.b, 1), m.name)); // TD15: the kit's value
            t.SetColor("_SpecColor", ArenaDressing.KitValue(new Color(tc.r, tc.g, tc.b, 1), m.name));
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
