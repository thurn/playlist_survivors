using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Game.Client.Arena;
using PS.Game.Arena;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Vector2 = System.Numerics.Vector2;

namespace Game.EditorTools.Arena
{
    /// <summary>
    /// Generates the arena (unity-game.md, "The Arena") from the one placement list of
    /// game/Content/arena-layout.json and footprints.json: demo3d's ArenaBaker copied and owned, at
    /// its lit rung only. `game unity BakeArena [-noLightmaps]`; `game build` runs it when its inputs
    /// change. It writes:
    /// - into the gitignored Assets/Baked/Arena/ (derived from vendor meshes, docs/tech-demo-3d/vendor-assets.md):
    ///   the kit's materials converted to URP and saved, the ground, the scene <see cref="SceneName"/>
    ///   with lightmaps baked in batch mode (the moon mixed, shadowmask; the ground lightmapped, the
    ///   kit lit by a probe grid; glow lights baked), and the player's <see cref="ArenaBake"/>;
    /// - the level data's arena part, game/Content/arena.json (<see cref="LevelExport"/>), tracked.
    /// The crystal structures' sites stay out of the bake (no ContributeGI): they break and regrow, so
    /// they cast realtime shadows and the runtime's static shadow map holds them. Each site's pieces
    /// are grouped under Static/Structures/"Site N", N its index in the level data.
    /// </summary>
    public static class ArenaBaker
    {
        public const string Dir = "Assets/Baked/Arena";
        public const string MaterialDir = Dir + "/Materials";
        public const string SceneName = "Arena";
        public const string LayoutPath = "Content/arena-layout.json";
        public const string FootprintsPath = "Content/footprints.json";
        public const string LevelPath = "Content/arena.json";
        public const string CrystalShaderPath = "Assets/Game/Client/Arena/Shaders/StructureCrystal.shader";
        /// <summary>The ground's half-size: past the 240 m arena, so wide views near its edge see ground.</summary>
        public const float GroundHalfM = 150f;
        /// <summary>Lightmap texels per metre on the ground (the only lightmapped surface): one 2048 map.</summary>
        public const float LightmapTexelsPerM = 5f;
        /// <summary>The light probes' spacing (m), at 0.5 m and 4 m above the ground.</summary>
        public const float ProbeSpacingM = 8f;

        public static string ScenePath => $"{Dir}/{SceneName}.unity";

        public static void Bake()
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            bool lightmaps = !TaskArgs.Has("-noLightmaps");
            Directory.CreateDirectory(MaterialDir);
            Directory.CreateDirectory(Dir + "/Resources");
            string json = File.ReadAllText(LayoutPath);
            var root = JsonValue.Parse(json);
            var layout = ArenaLayout.Parse(json);
            var table = FootprintTable.Parse(File.ReadAllText(FootprintsPath));
            var list = layout.Generate(table);
            var level = LevelExport.From(layout, list);
            File.WriteAllText(LevelPath, level.ToJson());

            var lighting = ArenaLighting.Parse(json);
            var converter = new MaterialConverter(MaterialDir, root["lighting"]["kitValue"]);
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var arena = new GameObject("Arena");

            var moon = new GameObject("Moon").AddComponent<Light>();
            moon.transform.SetParent(arena.transform, false);
            moon.type = LightType.Directional;
            moon.transform.rotation = lighting.MoonRotation;
            moon.color = lighting.MoonColor;
            moon.intensity = lighting.MoonIntensity;
            moon.shadows = LightShadows.Soft;
            moon.lightmapBakeType = LightmapBakeType.Mixed;
            RenderSettings.sun = moon;
            NightLook.ApplyRenderSettings(lighting);

            var groundGo = new GameObject("Ground");
            groundGo.transform.SetParent(arena.transform, false);
            groundGo.AddComponent<MeshFilter>().sharedMesh = GroundMesh();
            var groundR = groundGo.AddComponent<MeshRenderer>();
            groundR.sharedMaterial = GroundMaterial(root, layout);
            groundR.shadowCastingMode = ShadowCastingMode.Off;
            GameObjectUtility.SetStaticEditorFlags(groundGo, StaticEditorFlags.ContributeGI);
            groundR.receiveGI = ReceiveGI.Lightmaps;
            groundR.scaleInLightmap = 1f;

            var stat = new GameObject("Static").transform;
            stat.SetParent(arena.transform, false);
            var groups = new Dictionary<string, Transform>();
            Transform Group(string path)
            {
                if (groups.TryGetValue(path, out var t)) return t;
                int slash = path.LastIndexOf('/');
                t = new GameObject(slash < 0 ? path : path.Substring(slash + 1)).transform;
                t.SetParent(slash < 0 ? stat : Group(path.Substring(0, slash)), false);
                return groups[path] = t;
            }
            var lightsRoot = new GameObject("Lights").transform;
            lightsRoot.SetParent(arena.transform, false);
            var prefabs = new Dictionary<string, GameObject>();
            int flames = 0, renderers = 0;
            long tris = 0;
            var stripped = new Dictionary<string, int>();
            for (int i = 0; i < list.Items.Length; i++)
            {
                var p = list.Items[i];
                var prop = list.Props[p.Prop];
                if (!prefabs.TryGetValue(prop.Guid, out var prefab))
                {
                    var path = AssetDatabase.GUIDToAssetPath(prop.Guid);
                    prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path)
                        ?? throw new Exception($"ArenaBaker: kit piece {prop.Id} ({prop.Guid}) is missing; run `game setup`");
                    prefabs[prop.Guid] = prefab;
                }
                var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                int site = level.SiteOf[i];
                string group = site >= 0 ? $"Structures/{ArenaRuntime.SitePrefix}{site:000}"
                    : p.Area >= 0 ? "Landmarks" : (layout.Classify(p.Position)?.ToString() ?? "Forest");
                go.transform.SetParent(Group(group), false);
                go.transform.SetPositionAndRotation(new Vector3(p.Position.X, p.Lift, p.Position.Y), Quaternion.Euler(0, p.Yaw * Mathf.Rad2Deg, 0));
                go.transform.localScale = prefab.transform.localScale * p.Scale;
                Strip(go, stripped);
                converter.Convert(go);
                tris += Triangles(go);
                foreach (var r in go.GetComponentsInChildren<MeshRenderer>(true))
                {
                    r.receiveGI = ReceiveGI.LightProbes;
                    renderers++;
                }
                if (site < 0)
                    foreach (var t in go.GetComponentsInChildren<Transform>(true))
                        GameObjectUtility.SetStaticEditorFlags(t.gameObject, StaticEditorFlags.ContributeGI);

                var flame = lighting.Flames.Find(f => f.Prop == prop.Id);
                if (flame != null && flames < lighting.MaxFlameLights)
                {
                    var l = PointLight(lightsRoot, prop.Id, go.transform.position + Vector3.up * flame.HeightM * p.Scale, flame, LightmapBakeType.Realtime);
                    var flicker = l.gameObject.AddComponent<FlameFlicker>();
                    flicker.baseIntensity = flame.Intensity;
                    flicker.baseRange = flame.RangeM;
                    flames++;
                }
                var glow = lighting.Glow.Find(f => f.Prop == prop.Id);
                if (glow != null) PointLight(lightsRoot, prop.Id, go.transform.position + Vector3.up * glow.HeightM * p.Scale, glow, LightmapBakeType.Baked);
            }

            ArenaDressing.Dress(root, arena.transform, stat, MaterialDir, Dir);
            LightProbes(arena.transform, layout);
            EditorSceneManager.SaveScene(scene, ScenePath);
            if (lightmaps) BakeLightmaps(scene);

            var bake = ScriptableObject.CreateInstance<ArenaBake>();
            bake.scene = SceneName;
            bake.layoutJson = json;
            bake.crystal = AssetDatabase.LoadAssetAtPath<Shader>(CrystalShaderPath) ?? throw new Exception("ArenaBaker: no " + CrystalShaderPath);
            bake.sites = level.Data.Structures.Length;
            string bakePath = $"{Dir}/Resources/{ArenaBake.ResourcePath}.asset";
            AssetDatabase.DeleteAsset(bakePath);
            AssetDatabase.CreateAsset(bake, bakePath);
            AssetDatabase.SaveAssets();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single); // leave no modified scene behind
            int blockedTiles = level.Data.Blocked.Length, open = level.Data.Width * level.Data.Height - blockedTiles - level.Data.Structures.Sum(s => s.Tiles.Length);
            Debug.Log($"ArenaBaker: {SceneName}: {list.Items.Length} placements, {renderers} renderers, {tris} triangles, {flames} flame lights, " +
                      $"{level.Data.Structures.Length} structure sites ({level.Data.Structures.Count(s => s.Large)} large), " +
                      $"{level.Data.Width}×{level.Data.Height} tiles ({open} open, {blockedTiles} blocked) -> {LevelPath}, " +
                      $"{converter.Count} materials, stripped {string.Join(", ", stripped.Select(kv => kv.Key + " ×" + kv.Value))}, " +
                      $"in {sw.Elapsed.TotalSeconds:F0} s");
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

        /// <summary>A probe grid for the kit's baked indirect light and shadowmask occlusion: every <see cref="ProbeSpacingM"/>, at 0.5 m and 4 m.</summary>
        static void LightProbes(Transform root, ArenaLayout layout)
        {
            var go = new GameObject("LightProbes");
            go.transform.SetParent(root, false);
            var group = go.AddComponent<LightProbeGroup>();
            var pts = new List<Vector3>();
            float half = layout.Size.X / 2;
            for (float x = -half; x <= half + 0.01f; x += ProbeSpacingM)
            for (float z = -half; z <= half + 0.01f; z += ProbeSpacingM)
            {
                pts.Add(new Vector3(x, 0.5f, z));
                pts.Add(new Vector3(x, 4f, z));
            }
            group.probePositions = pts.ToArray();
        }

        static void BakeLightmaps(UnityEngine.SceneManagement.Scene scene)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var settingsPath = $"{Dir}/{SceneName}.lighting";
            AssetDatabase.DeleteAsset(settingsPath);
            var ls = new LightingSettings { name = SceneName };
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
            if (!Lightmapping.Bake()) throw new Exception("ArenaBaker: Lightmapping.Bake failed");
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log($"ArenaBaker: baked {SceneName}'s lightmaps ({LightmapSettings.lightmaps.Length} maps) in {sw.Elapsed.TotalSeconds:F0} s");
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
            const int cells = 150; // 2 m cells
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

        /// <summary>The ground's colours by type (sRGB), from the layout's <c>ground</c>: a dark forest floor, a darker edge band, and the fighting ground.</summary>
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

        static Material GroundMaterial(JsonValue root, ArenaLayout layout)
        {
            var colors = GroundColors.Parse(root["ground"]);
            string texPath = Dir + "/GroundAlbedo.png";
            const int size = 2048;
            var px = new Color32[size * size];
            float m = 2 * GroundHalfM / size;
            for (int j = 0; j < size; j++)
            for (int i = 0; i < size; i++)
                px[j * size + i] = GroundColor(layout, colors, -GroundHalfM + (i + 0.5f) * m, -GroundHalfM + (j + 0.5f) * m);
            var albedo = SaveTexture(texPath, px, size, true, TextureWrapMode.Clamp, 4);

            string matPath = Dir + "/Ground.mat";
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (mat == null)
            {
                mat = new Material(shader) { name = "ArenaGround" };
                AssetDatabase.CreateAsset(mat, matPath);
            }
            mat.shader = shader;
            mat.SetTexture("_BaseMap", albedo);
            mat.SetColor("_BaseColor", Color.white);
            mat.SetFloat("_Smoothness", root["ground"]["smoothness"].AsFloat(0.12f));
            mat.SetFloat("_Metallic", 0f);
            // Our generated cracks and stones as a detail albedo every DetailM, with the kit's own
            // ground normals (Elven Realm's, synced for its rock shader) as its detail normal map at
            // the same tiling, so the moon and the hero's light catch the ground.
            mat.SetTexture("_DetailAlbedoMap", DetailAlbedo());
            mat.SetFloat("_DetailAlbedoMapScale", 1f);
            mat.SetTextureScale("_DetailAlbedoMap", new UnityEngine.Vector2(2 * GroundHalfM / DetailM, 2 * GroundHalfM / DetailM));
            mat.SetTexture("_DetailNormalMap", FindVendorTexture("Ground_Normals_01"));
            mat.SetFloat("_DetailNormalMapScale", 0.8f);
            mat.EnableKeyword("_DETAIL_MULX2");
            mat.SetTexture("_EmissionMap", null);
            mat.SetColor("_EmissionColor", Color.black);
            mat.DisableKeyword("_EMISSION");
            mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;
            EditorUtility.SetDirty(mat);
            return mat;
        }

        static Texture2D SaveTexture(string path, Color32[] px, int size, bool srgb, TextureWrapMode wrap, int aniso)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGB24, false);
            tex.SetPixels32(px);
            tex.Apply();
            File.WriteAllBytes(path, tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            var imp = (TextureImporter)AssetImporter.GetAtPath(path);
            imp.sRGBTexture = srgb;
            imp.mipmapEnabled = true;
            imp.wrapMode = wrap;
            imp.anisoLevel = aniso;
            imp.textureCompression = TextureImporterCompression.CompressedHQ;
            imp.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
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
                float mottle = 0.6f * TileNoise(u, w, 8, 11) + 0.4f * TileNoise(u, w, 32, 23) - 0.5f;
                float v = 0.5f + 0.24f * mottle;
                // Cracks: where the nearest two cell points are about equally near, the coordinates warped so the edges wander.
                float wu = u + 0.025f * (TileNoise(u, w, 12, 37) - 0.5f), ww = w + 0.025f * (TileNoise(u, w, 12, 41) - 0.5f);
                float f1 = float.MaxValue, f2 = float.MaxValue;
                foreach (var c in seeds)
                {
                    float dx = Mathf.Repeat(wu - c.x + 0.5f, 1f) - 0.5f, dy = Mathf.Repeat(ww - c.y + 0.5f, 1f) - 0.5f;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    if (d < f1) { f2 = f1; f1 = d; }
                    else if (d < f2) f2 = d;
                }
                // Cracks run 2-4 cm wide and fade out along about half the cells' edges: a broken crust, not paving.
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
            return SaveTexture(Dir + "/GroundDetail.png", px, size, false, TextureWrapMode.Repeat, 8);
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
            return new Color(c.r * k * (1 + 0.15f * hue), c.g * k, c.b * k * (1 - 0.1f * hue));
        }

        static float Smooth(float a, float b, float x)
        {
            float t = Mathf.Clamp01((x - a) / (b - a));
            return t * t * (3 - 2 * t);
        }
    }
}
