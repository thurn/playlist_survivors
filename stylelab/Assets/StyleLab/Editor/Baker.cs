using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using Unity.Collections;
using StyleLab.Runtime;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.Rendering;

namespace StyleLab.EditorTools
{
    /// <summary>
    /// Bakes the player's StyleLabData into Assets/Baked (gitignored: it holds and references
    /// vendor-derived files): per type, the figure merged into one style mesh (StyleMesh), an
    /// albedo texture per submesh (InfinityPBR's colour-ID shader evaluated to a plain texture), the albedo's
    /// value range over the texels the mesh uses, and an animator controller holding the roster's
    /// clips; then the song (setup's Baked/Audio), its beats and energy, and every StyleLab shader.
    /// </summary>
    public static class Baker
    {
        const string Root = "Assets/Baked/StyleLab";
        public const string DataPath = "Assets/Baked/Resources/StyleLabData.asset";

        public static StyleLabData Bake()
        {
            foreach (var dir in new[] { Root + "/Meshes", Root + "/Albedo", Root + "/Controllers", "Assets/Baked/Resources" })
                Directory.CreateDirectory(dir);
            var data = ScriptableObject.CreateInstance<StyleLabData>();
            data.types = Roster.Load().Select(BakeType).ToArray();
            BakeSong(data);
            data.shaders = AssetDatabase.FindAssets("t:Shader", new[] { "Assets/StyleLab/Shaders" })
                .Select(g => AssetDatabase.LoadAssetAtPath<Shader>(AssetDatabase.GUIDToAssetPath(g))).ToArray();
            CheckShaders(data.shaders);
            Replace(data, DataPath);
            AssetDatabase.SaveAssets();
            Debug.Log($"Baker: {data.types.Length} types, {data.beats.Length} beats, {data.shaders.Length} shaders -> {DataPath}");
            return data;
        }

        static void CheckShaders(Shader[] shaders)
        {
            var errors = new List<string>();
            foreach (var shader in shaders)
            {
                foreach (var m in ShaderUtil.GetShaderMessages(shader))
                    if (m.severity == UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error)
                        errors.Add($"{shader.name}: {m.message} ({m.file}:{m.line})");
                if (ShaderUtil.ShaderHasError(shader) && errors.Count == 0) errors.Add($"{shader.name}: has errors");
            }
            if (errors.Count > 0) throw new Exception("Baker: shader errors:\n" + string.Join("\n", errors));
        }

        static TypeData BakeType(Roster.Type rt)
        {
            var t = new TypeData
            {
                id = rt.id, kind = rt.kind == "rejected-boss" ? "boss" : rt.kind, humanoid = rt.rig == "humanoid",
                radius = rt.radius, heightM = rt.heightM, hoverM = rt.hoverM,
                prefab = Roster.LoadPrefab(rt.prefab.guid),
                weapons = rt.weapons.Select(w => new WeaponData { prefab = Roster.LoadPrefab(w.guid), bone = w.bone }).ToArray(),
            };
            var go = Rig.Spawn(t.prefab, t.weapons, null);
            try
            {
                t.nativeHeight = Rig.MeasureHeight(go);
                float toMetres = t.heightM / Mathf.Max(t.nativeHeight, 0.01f);
                var renderers = Rig.Renderers(go);
                var merged = Merge(rt.id, go.transform, renderers, out t.albedo, out t.bones);
                var verts = merged.vertices;
                var rest = new Vector3[verts.Length];
                for (int i = 0; i < verts.Length; i++) rest[i] = verts[i] * toMetres;
                t.mesh = StyleMesh.Build(merged, rest, rt.id.GetHashCode());
                Replace(t.mesh, $"{Root}/Meshes/{rt.id}.asset");
                var values = new List<float>();
                SampleValues(merged, t.albedo, values);
                values.Sort();
                t.lumRange = values.Count == 0 ? new Vector2(0, 1)
                    : new Vector2(values[(int)(values.Count * 0.04f)], values[(int)(values.Count * 0.97f)]);
                if (t.lumRange.y - t.lumRange.x < 0.05f) t.lumRange.y = t.lumRange.x + 0.05f;
                (t.controller, t.states) = BuildController(rt);
                Debug.Log($"Baker: {rt.id}: {renderers.Count} renderers merged ({verts.Length} vertices, {t.bones.Length} bones, " +
                          $"{t.albedo.Length} submeshes), height {t.nativeHeight:F2} -> {t.heightM} m, value range " +
                          $"{t.lumRange.x:F3}..{t.lumRange.y:F3}, states {string.Join(",", t.states)}");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
            return t;
        }

        /// <summary>
        /// Merges a figure's renderers into one skinned mesh in its root space, a submesh per
        /// albedo texture (demo3d's CrowdBaker merges the same way). Each vertex moves from its
        /// renderer's mesh space into root space and each bind pose absorbs the inverse, so
        /// skinning is unchanged; a rigid piece (a weapon) is skinned wholly to its own transform.
        /// </summary>
        static Mesh Merge(string id, Transform root, List<Renderer> renderers, out Texture2D[] albedo, out string[] bonePaths)
        {
            var rootInv = root.worldToLocalMatrix;
            var bones = new List<Transform>();
            var binds = new List<Matrix4x4>();
            int Entry(Transform bone, Matrix4x4 bind)
            {
                for (int i = 0; i < bones.Count; i++)
                    if (bones[i] == bone && Near(binds[i], bind)) return i;
                bones.Add(bone);
                binds.Add(bind);
                return bones.Count - 1;
            }

            var pos = new List<Vector3>();
            var nor = new List<Vector3>();
            var uv = new List<Vector2>();
            var perVertex = new List<byte>();
            var weights = new List<BoneWeight1>();
            var textures = new List<Texture2D>();
            var subTris = new List<List<int>>();
            for (int ri = 0; ri < renderers.Count; ri++)
            {
                var r = renderers[ri];
                var mesh = Rig.MeshOf(r);
                var toRoot = rootInv * r.transform.localToWorldMatrix;
                var fromRoot = toRoot.inverse;
                var smr = r as SkinnedMeshRenderer;
                bool rigid = smr == null || smr.bones.Length == 0 || mesh.bindposes.Length == 0;
                int[] entryOf;
                if (rigid) entryOf = new[] { Entry(r.transform, fromRoot) };
                else
                {
                    var bp = mesh.bindposes;
                    entryOf = new int[smr.bones.Length];
                    for (int i = 0; i < entryOf.Length; i++)
                        entryOf[i] = smr.bones[i] == null ? -1 : Entry(smr.bones[i], bp[i] * fromRoot);
                }
                int baseVertex = pos.Count;
                var verts = mesh.vertices;
                var normals = mesh.normals;
                var uvs = mesh.uv;
                var bonesPer = rigid ? default : mesh.GetBonesPerVertex();
                var all = rigid ? default : mesh.GetAllBoneWeights();
                int w0 = 0;
                for (int v = 0; v < verts.Length; v++)
                {
                    pos.Add(toRoot.MultiplyPoint3x4(verts[v]));
                    nor.Add(normals.Length > v ? toRoot.MultiplyVector(normals[v]).normalized : Vector3.up);
                    uv.Add(uvs.Length > v ? uvs[v] : Vector2.zero);
                    if (rigid)
                    {
                        perVertex.Add(1);
                        weights.Add(new BoneWeight1 { boneIndex = entryOf[0], weight = 1 });
                        continue;
                    }
                    int count = bonesPer[v];
                    var kept = new List<BoneWeight1>();
                    float sum = 0;
                    for (int i = 0; i < count; i++)
                    {
                        var bw = all[w0 + i];
                        int e = bw.boneIndex < entryOf.Length ? entryOf[bw.boneIndex] : -1;
                        if (e < 0 || bw.weight <= 0) continue;
                        kept.Add(new BoneWeight1 { boneIndex = e, weight = bw.weight });
                        sum += bw.weight;
                    }
                    w0 += count;
                    if (kept.Count == 0) { kept.Add(new BoneWeight1 { boneIndex = Entry(r.transform, fromRoot), weight = 1 }); sum = 1; }
                    perVertex.Add((byte)kept.Count);
                    foreach (var bw in kept) weights.Add(new BoneWeight1 { boneIndex = bw.boneIndex, weight = bw.weight / sum });
                }
                var mats = r.sharedMaterials;
                for (int s = 0; s < mesh.subMeshCount; s++)
                {
                    var mat = mats.Length == 0 ? null : mats[Math.Min(s, mats.Length - 1)];
                    var tex = AlbedoOf(mat, $"{id}_{ri}_{s}");
                    int sub = textures.IndexOf(tex);
                    if (sub < 0) { textures.Add(tex); subTris.Add(new List<int>()); sub = textures.Count - 1; }
                    foreach (int i in mesh.GetTriangles(s)) subTris[sub].Add(baseVertex + i);
                }
            }

            var merged = new Mesh { name = id, indexFormat = pos.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            merged.SetVertices(pos);
            merged.SetNormals(nor);
            merged.SetUVs(0, uv);
            merged.subMeshCount = subTris.Count;
            for (int s = 0; s < subTris.Count; s++) merged.SetTriangles(subTris[s], s);
            merged.bindposes = binds.ToArray();
            using (var pv = new NativeArray<byte>(perVertex.ToArray(), Allocator.Temp))
            using (var bw = new NativeArray<BoneWeight1>(weights.ToArray(), Allocator.Temp))
                merged.SetBoneWeights(pv, bw);
            merged.RecalculateBounds();
            albedo = textures.ToArray();
            bonePaths = bones.Select(b => b == root ? "" : AnimationUtility.CalculateTransformPath(b, root)).ToArray();
            return merged;
        }

        static bool Near(Matrix4x4 a, Matrix4x4 c)
        {
            for (int i = 0; i < 16; i++)
                if (Mathf.Abs(a[i] - c[i]) > 1e-3f) return false;
            return true;
        }

        // ---------------------------------------------------------------------------------
        // Albedo.

        static readonly Dictionary<Texture, Color[]> Pixels = new Dictionary<Texture, Color[]>();

        static readonly Dictionary<Material, Texture2D> ColorIds = new Dictionary<Material, Texture2D>();

        static Texture2D AlbedoOf(Material m, string name)
        {
            if (m == null) return Texture2D.whiteTexture;
            if (m.shader != null && m.shader.name.Contains("LPColor"))
            {
                if (!ColorIds.TryGetValue(m, out var baked) || baked == null)
                    ColorIds[m] = baked = BakeColorId(m, $"{Root}/Albedo/{name}.asset");
                return baked;
            }
            foreach (var prop in new[] { "_BaseMap", "_MainTex", "_Albedo" })
                if (m.HasProperty(prop) && m.GetTexture(prop) is Texture2D tex) return tex;
            return Texture2D.whiteTexture;
        }

        /// <summary>The albedo's linear value at every vertex's uv: the colours the mesh really uses.</summary>
        static void SampleValues(Mesh src, Texture2D[] albedo, List<float> values)
        {
            var uvs = src.uv;
            if (uvs.Length == 0) return;
            for (int s = 0; s < src.subMeshCount; s++)
            {
                var tex = albedo[s];
                if (tex == null || tex == Texture2D.whiteTexture) continue;
                var px = PixelsOf(tex);
                foreach (int v in src.GetTriangles(s).Distinct())
                {
                    var uv = uvs[v];
                    int x = Mathf.Clamp((int)((uv.x - Mathf.Floor(uv.x)) * tex.width), 0, tex.width - 1);
                    int y = Mathf.Clamp((int)((uv.y - Mathf.Floor(uv.y)) * tex.height), 0, tex.height - 1);
                    var c = px[y * tex.width + x].linear;
                    values.Add(0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b);
                }
            }
        }

        static Color[] PixelsOf(Texture2D src)
        {
            if (Pixels.TryGetValue(src, out var px)) return px;
            // Through a blit, so the vendor texture needs no read/write import setting.
            var rt = RenderTexture.GetTemporary(src.width, src.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            Graphics.Blit(src, rt);
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var copy = new Texture2D(src.width, src.height, TextureFormat.RGBA32, false, false);
            copy.ReadPixels(new Rect(0, 0, src.width, src.height), 0, 0);
            copy.Apply();
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            px = copy.GetPixels();
            UnityEngine.Object.DestroyImmediate(copy);
            Pixels[src] = px;
            return px;
        }

        /// <summary>
        /// InfinityPBR's LPColor shader picks each texel's colour by matching its colour-ID
        /// texture against 49 ID colours and lerping in an HSV replacement; the same chain,
        /// evaluated once per texel, gives a plain albedo texture (demo3d's CrowdBaker does the same).
        /// </summary>
        static Texture2D BakeColorId(Material m, string path)
        {
            var src = m.GetTexture("_MainTex") as Texture2D ?? throw new Exception($"Baker: {m.name} has no colour-ID texture");
            var importer = AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(src)) as TextureImporter;
            bool srgb = importer == null || importer.sRGBTexture;
            var pixels = PixelsOf(src);
            float range = m.GetFloat("_ColorIDRange"), fuzz = Mathf.Max(m.GetFloat("_ColorIDFuzziness"), 1e-5f);
            var ids = new Vector3[49];
            var hsv = new Vector3[49];
            for (int i = 0; i < 49; i++)
            {
                var c = m.GetColor("_Color" + i).linear;
                ids[i] = new Vector3(c.r, c.g, c.b);
                hsv[i] = HsvToRgb(new Vector3(m.GetFloat("_H" + i), m.GetFloat("_S" + i), m.GetFloat("_V" + i)));
            }
            var output = new Color[pixels.Length];
            for (int p = 0; p < pixels.Length; p++)
            {
                var px = srgb ? pixels[p].linear : pixels[p];
                var t = new Vector3(px.r, px.g, px.b);
                var result = hsv[0];
                for (int i = 0; i < 49; i++)
                {
                    float w = Mathf.Clamp01(1f - (Vector3.Distance(ids[i], t) - range) / fuzz);
                    result = Vector3.Lerp(result, hsv[i], w);
                }
                output[p] = new Color(result.x, result.y, result.z, 1f).gamma;
            }
            var tex = new Texture2D(src.width, src.height, TextureFormat.RGBA32, false, false)
            {
                name = Path.GetFileNameWithoutExtension(path), filterMode = src.filterMode, wrapMode = src.wrapMode,
            };
            tex.SetPixels(output);
            tex.Apply(false, false);
            Replace(tex, path);
            Pixels[tex] = output;
            return tex;
        }

        static Vector3 HsvToRgb(Vector3 c)
        {
            var k = new Vector4(1f, 2f / 3f, 1f / 3f, 3f);
            var p = new Vector3(Mathf.Abs(Frac(c.x + k.x) * 6f - k.w), Mathf.Abs(Frac(c.x + k.y) * 6f - k.w), Mathf.Abs(Frac(c.x + k.z) * 6f - k.w));
            return c.z * Vector3.Lerp(Vector3.one * k.x, new Vector3(Mathf.Clamp01(p.x - k.x), Mathf.Clamp01(p.y - k.x), Mathf.Clamp01(p.z - k.x)), c.y);
        }

        static float Frac(float x) => x - Mathf.Floor(x);

        // ---------------------------------------------------------------------------------

        static (RuntimeAnimatorController, string[]) BuildController(Roster.Type rt)
        {
            var path = $"{Root}/Controllers/{rt.id}.controller";
            AssetDatabase.DeleteAsset(path);
            var controller = AnimatorController.CreateAnimatorControllerAtPath(path);
            var sm = controller.layers[0].stateMachine;
            var states = new List<string>();
            // Idle first, so it's the default state.
            foreach (var kv in rt.clips.OrderBy(kv => kv.Key == "idle" ? 0 : 1))
            {
                var clip = Roster.LoadClip(kv.Value);
                var st = sm.AddState(kv.Key);
                st.motion = clip;
                st.writeDefaultValues = true;
                states.Add(kv.Key);
            }
            EditorUtility.SetDirty(controller);
            return (controller, states.ToArray());
        }

        static void BakeSong(StyleLabData data)
        {
            var audio = new[] { "Assets/Baked/Audio/action.ogg", "Assets/Baked/Audio/action.wav" }.FirstOrDefault(File.Exists);
            if (audio == null) throw new Exception("Baker: no song in Assets/Baked/Audio; run `stylelab setup`");
            if (AssetImporter.GetAtPath(audio) is AudioImporter ai)
            {
                var s = ai.defaultSampleSettings;
                if (s.loadType != AudioClipLoadType.Streaming || s.compressionFormat != AudioCompressionFormat.Vorbis)
                {
                    s.loadType = AudioClipLoadType.Streaming;
                    s.compressionFormat = AudioCompressionFormat.Vorbis;
                    s.quality = 0.7f;
                    ai.defaultSampleSettings = s;
                    ai.SaveAndReimport();
                }
            }
            data.song = AssetDatabase.LoadAssetAtPath<AudioClip>(audio);
            var map = JObject.Parse(File.ReadAllText("Assets/Baked/Audio/action.songmap.json"));
            data.beats = map["beats"].Select(b => (float)b).ToArray();
            data.firstDownbeat = (int)map["firstDownbeatIndex"];
            data.beatsPerBar = (int)map["meter"][0]["beatsPerBar"];
            data.energy = map["energy"].Select(e => (float)e).ToArray();
        }

        static void Replace(UnityEngine.Object asset, string path)
        {
            AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(asset, path);
        }
    }
}
