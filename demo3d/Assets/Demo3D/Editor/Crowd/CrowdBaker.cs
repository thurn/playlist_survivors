using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Demo3D.EditorTools.Assets;
using Demo3D.Runtime.Crowd;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace Demo3D.EditorTools.Crowd
{
    /// <summary>
    /// The bone-texture baker (crowd.md, Bone-texture path). For each drawn roster type it
    /// writes, only under Assets/Baked/Crowd/:
    /// - a combined mesh at demo scale: every active skinned mesh plus rigid attachments (the
    ///   weapons on their hand bones), with 4 bone indices in UV1 and weights in UV2;
    /// - one float texture of skinning matrices (bone world × bind pose, relative to the
    ///   character's root), 3 float4 rows per bone per frame, every clip sampled at 60 Hz, and a
    ///   second texture at 30 Hz for TD11's clips30 cut;
    /// - LOD1 and LOD2 (TD11): the combined mesh decimated to about a half and a quarter of its
    ///   triangles by Unity's MeshLodUtility, each its own mesh over the same vertices;
    /// - the clip table (first row, frame count, loop, length and TD2's annotations);
    /// - one crowd material per albedo texture (one per type except the dragon, which has two);
    ///   InfinityPBR's colour-ID shader is baked to a plain albedo texture;
    /// - the stock baseline's prefab: the same combined mesh on one SkinnedMeshRenderer, with an
    ///   AnimatorController holding every clip as a state;
    /// and the CrowdLibrary asset that lists them.
    /// </summary>
    public static class CrowdBaker
    {
        public const string OutDir = "Assets/Baked/Crowd";
        public const string LibraryPath = OutDir + "/Resources/CrowdLibrary.asset";
        public const string ShaderPath = "Assets/Demo3D/Runtime/Crowd/Shaders/CrowdSkinned.shader";
        public const string CullPath = "Assets/Demo3D/Runtime/Crowd/Shaders/CrowdCull.compute";
        public const string PalettePath = "Assets/Demo3D/Runtime/Crowd/Shaders/CrowdPalette.compute";
        const string AnnotationsPath = "Assets/Demo3D/Data/clip-annotations.json";
        const float Rate = CrowdLibrary.ClipRate;

        [InitializeOnLoadMethod]
        static void Register() => Bakers.Register("crowd", Bake);

        public static bool Drawn(Roster.Type t) => t.kind == "fodder" || t.kind == "elite" || t.kind == "boss";

        /// <summary>A bone (or rigid attachment) of the combined mesh.</summary>
        public sealed class Entry
        {
            public Transform Bone;
            public Matrix4x4 Bind; // maps the combined mesh's root-space vertices into the bone's space
        }

        public sealed class SubmeshSource
        {
            public string Key;
            public Material Source;
            public List<int> Indices = new List<int>();
        }

        /// <summary>One type's bake, in memory.</summary>
        public sealed class TypeBake : IDisposable
        {
            public Roster.Type Type;
            public GameObject Root;
            public Animator Animator;
            public List<Entry> Entries = new List<Entry>();
            public List<Vector3> Positions = new List<Vector3>();
            public List<Vector3> Normals = new List<Vector3>();
            public List<Vector4> Tangents = new List<Vector4>();
            public List<Vector2> Uv = new List<Vector2>();
            public List<Vector4> BoneIndices = new List<Vector4>();
            public List<Vector4> BoneWeights = new List<Vector4>();
            public List<SubmeshSource> Submeshes = new List<SubmeshSource>();
            /// <summary>Each source renderer's vertex range in the combined mesh.</summary>
            public List<(Renderer Renderer, int First, int Count)> Ranges = new List<(Renderer, int, int)>();
            public List<CrowdClip> Clips = new List<CrowdClip>();
            public List<AnimationClip> AnimationClips = new List<AnimationClip>();
            public float[] Texels; // RGBAFloat, width = 3 × bones, height = total frames
            public int Width, Height;
            /// <summary>The same matrices at 30 Hz (TD11's clips30 cut), rows from each clip's FirstRow30.</summary>
            public float[] Texels30;
            public int Height30;
            public float CenterY, Radius, HeightM;

            public Mesh BuildMesh(string name, bool skinStreams = true)
            {
                var m = new Mesh { name = name };
                if (Positions.Count > 65535) m.indexFormat = IndexFormat.UInt32;
                m.SetVertices(Positions);
                m.SetNormals(Normals);
                m.SetTangents(Tangents);
                m.SetUVs(0, Uv);
                if (skinStreams)
                {
                    m.SetUVs(1, BoneIndices);
                    m.SetUVs(2, BoneWeights);
                }
                m.subMeshCount = Submeshes.Count;
                for (int i = 0; i < Submeshes.Count; i++) m.SetTriangles(Submeshes[i].Indices, i, false);
                m.RecalculateBounds();
                // The skinned extent, so Unity's own culling of a stock renderer stays correct too.
                m.bounds = new Bounds(new Vector3(0, CenterY, 0), Vector3.one * (2 * Radius));
                return m;
            }

            /// <summary>The baked matrix of entry b at a texture row, as a 4×4.</summary>
            public Matrix4x4 Matrix(int b, int row)
            {
                var m = Matrix4x4.identity;
                for (int r = 0; r < 3; r++)
                {
                    int at = ((row * Width) + b * 3 + r) * 4;
                    m.SetRow(r, new Vector4(Texels[at], Texels[at + 1], Texels[at + 2], Texels[at + 3]));
                }
                return m;
            }

            public void Dispose()
            {
                if (Root != null) UnityEngine.Object.DestroyImmediate(Root);
            }
        }

        /// <summary>Bakes every drawn type and writes the library (the "crowd" baker).</summary>
        public static void Bake()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var annotations = LoadAnnotations();
            Directory.CreateDirectory(OutDir + "/Resources");
            Directory.CreateDirectory(OutDir + "/Stock");
            var types = new List<CrowdType>();
            var shader = AssetDatabase.LoadAssetAtPath<Shader>(ShaderPath) ?? throw new Exception("CrowdBaker: no " + ShaderPath);
            var colors = Roster.TypeColors();
            foreach (var t in Roster.Load().Where(Drawn))
            {
                var started = DateTime.UtcNow;
                using var bake = BakeType(t, annotations);
                int height = bake.Height, width = bake.Width, subs = bake.Submeshes.Count;
                var ct = Save(bake, shader);
                ct.Hue = t.hue ?? -1;
                ct.HueSaturation = colors.saturation;
                ct.Dye = colors.dye;
                ct.HueRotate = t.hueRotate;
                types.Add(ct);
                Debug.Log($"CrowdBaker: {t.id}: {ct.Vertices} vertices, {ct.Triangles} triangles (LOD1 {ct.Lod1Triangles}, LOD2 {ct.Lod2Triangles}), {ct.BoneCount} bones, " +
                          $"{height} frames ({width}x{height} texels, {width * height * 16 / 1024} KiB), " +
                          $"{subs} material(s), radius {ct.Radius:F2} m, height {ct.HeightM:F2} m, " +
                          $"in {(DateTime.UtcNow - started).TotalSeconds:F1} s");
                Debug.Log($"CrowdBaker: {t.id} clip spheres (centre, radius, height): " +
                          string.Join(", ", ct.Clips.Select(c => $"{c.Role} {c.CenterY:F2}/{c.Radius:F2}/{c.HeightM:F2}")));
            }
            var lib = ScriptableObject.CreateInstance<CrowdLibrary>();
            lib.Types = types.ToArray();
            lib.CrowdShader = shader;
            lib.CullShader = AssetDatabase.LoadAssetAtPath<ComputeShader>(CullPath);
            lib.PaletteShader = AssetDatabase.LoadAssetAtPath<ComputeShader>(PalettePath);
            lib.Ground = SaveGround();
            lib.BufferDebug = Shader.Find("Hidden/Demo3D/BufferDebug");
            AssetDatabase.DeleteAsset(LibraryPath);
            AssetDatabase.CreateAsset(lib, LibraryPath);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single); // leave no modified scene behind
            AssetDatabase.SaveAssets();
            Debug.Log($"CrowdBaker: wrote {types.Count} types to {LibraryPath}");
        }

        public static JObject LoadAnnotations() => JObject.Parse(File.ReadAllText(AnnotationsPath));

        // ---------------------------------------------------------------------------------
        // Baking one type in memory.

        public static TypeBake BakeType(Roster.Type t, JObject annotations)
        {
            var b = new TypeBake { Type = t };
            b.Root = Roster.Spawn(t, Vector3.zero, 0);
            b.Root.transform.position = Vector3.zero; // the client adds hover to Position.y
            foreach (var tr in b.Root.GetComponentsInChildren<Transform>(true))
                GameObjectUtility.RemoveMonoBehavioursWithMissingScript(tr.gameObject); // vendor scripts we don't sync
            b.Animator = b.Root.GetComponentInChildren<Animator>();
            if (b.Animator == null) throw new Exception($"CrowdBaker: {t.id} has no Animator");
            Combine(b);
            AnimationMode.StartAnimationMode();
            try
            {
                SampleClips(b, annotations);
                MeasureBounds(b);
            }
            finally
            {
                AnimationMode.StopAnimationMode();
            }
            return b;
        }

        /// <summary>The root's rotation and translation (not its scale: the mesh is at demo scale).</summary>
        public static Matrix4x4 RootTR(TypeBake b) => Matrix4x4.TRS(b.Root.transform.position, b.Root.transform.rotation, Vector3.one);

        static int EntryIndex(TypeBake b, Transform bone, Matrix4x4 bind)
        {
            for (int i = 0; i < b.Entries.Count; i++)
                if (b.Entries[i].Bone == bone && Near(b.Entries[i].Bind, bind)) return i;
            b.Entries.Add(new Entry { Bone = bone, Bind = bind });
            return b.Entries.Count - 1;
        }

        static bool Near(Matrix4x4 a, Matrix4x4 c)
        {
            for (int i = 0; i < 16; i++)
                if (Mathf.Abs(a[i] - c[i]) > 1e-3f) return false; // 1 mm: the same bone seen from two skins
            return true;
        }

        static void Combine(TypeBake b)
        {
            var rootInv = RootTR(b).inverse;
            foreach (var r in b.Root.GetComponentsInChildren<Renderer>(false))
            {
                if (!r.enabled) continue;
                Mesh mesh;
                Transform[] bones;
                Matrix4x4[] bindposes;
                if (r is SkinnedMeshRenderer smr)
                {
                    mesh = smr.sharedMesh;
                    bones = smr.bones;
                    bindposes = mesh != null ? mesh.bindposes : null;
                }
                else if (r is MeshRenderer)
                {
                    mesh = r.GetComponent<MeshFilter>()?.sharedMesh;
                    bones = null;
                    bindposes = null;
                }
                else continue;
                if (mesh == null) continue;
                bool rigid = bones == null || bones.Length == 0 || bindposes == null || bindposes.Length == 0;
                // Vertices move from the renderer's mesh space into root space at the reference
                // pose; each bind pose absorbs the inverse, so skinning is unchanged exactly.
                var toRoot = rootInv * r.transform.localToWorldMatrix;
                var fromRoot = toRoot.inverse;
                int[] entryOf;
                if (rigid) entryOf = new[] { EntryIndex(b, r.transform, fromRoot) };
                else
                {
                    entryOf = new int[bones.Length];
                    for (int i = 0; i < bones.Length; i++)
                        entryOf[i] = bones[i] == null ? -1 : EntryIndex(b, bones[i], bindposes[i] * fromRoot);
                }

                int baseVertex = b.Positions.Count;
                b.Ranges.Add((r, baseVertex, mesh.vertexCount));
                var verts = mesh.vertices;
                var normals = mesh.normals;
                var tangents = mesh.tangents;
                var uv = mesh.uv;
                var perVertex = rigid ? default : mesh.GetBonesPerVertex();
                var weights = rigid ? default : mesh.GetAllBoneWeights();
                int w0 = 0;
                for (int v = 0; v < verts.Length; v++)
                {
                    b.Positions.Add(toRoot.MultiplyPoint3x4(verts[v]));
                    var n = normals.Length > v ? toRoot.MultiplyVector(normals[v]).normalized : Vector3.up;
                    b.Normals.Add(n);
                    if (tangents.Length > v)
                    {
                        var tv = toRoot.MultiplyVector(tangents[v]).normalized;
                        float sign = tangents[v].w * Mathf.Sign(toRoot.determinant);
                        b.Tangents.Add(new Vector4(tv.x, tv.y, tv.z, sign));
                    }
                    else b.Tangents.Add(new Vector4(1, 0, 0, 1));
                    b.Uv.Add(uv.Length > v ? uv[v] : Vector2.zero);
                    var idx = Vector4.zero;
                    var wt = Vector4.zero;
                    if (rigid) { idx.x = entryOf[0]; wt.x = 1; }
                    else
                    {
                        // The 4 strongest influences (Unity sorts them by weight), renormalized.
                        int count = perVertex[v];
                        float sum = 0;
                        int k = 0;
                        for (int i = 0; i < count && k < 4; i++)
                        {
                            var bw = weights[w0 + i];
                            int e = bw.boneIndex < entryOf.Length ? entryOf[bw.boneIndex] : -1;
                            if (e < 0 || bw.weight <= 0) continue;
                            idx[k] = e;
                            wt[k] = bw.weight;
                            sum += bw.weight;
                            k++;
                        }
                        w0 += count;
                        if (sum > 0) wt /= sum;
                        else { idx.x = EntryIndex(b, r.transform, fromRoot); wt = new Vector4(1, 0, 0, 0); }
                    }
                    b.BoneIndices.Add(idx);
                    b.BoneWeights.Add(wt);
                }
                var mats = r.sharedMaterials;
                for (int s = 0; s < mesh.subMeshCount; s++)
                {
                    var mat = mats.Length == 0 ? null : mats[Math.Min(s, mats.Length - 1)];
                    var key = AlbedoKey(mat);
                    var sub = b.Submeshes.FirstOrDefault(x => x.Key == key);
                    if (sub == null) b.Submeshes.Add(sub = new SubmeshSource { Key = key, Source = mat });
                    foreach (var i in mesh.GetTriangles(s)) sub.Indices.Add(baseVertex + i);
                }
            }
            if (b.Positions.Count == 0) throw new Exception($"CrowdBaker: {b.Type.id} has no active renderers");
        }

        /// <summary>Materials that share an albedo (and emission) texture share a submesh.</summary>
        static string AlbedoKey(Material m)
        {
            if (m == null) return "none";
            if (IsColorId(m)) return "colorid:" + AssetDatabase.GetAssetPath(m) + ":" + m.name;
            var tex = m.HasProperty("_BaseMap") ? m.GetTexture("_BaseMap") : null;
            var em = m.HasProperty("_EmissionMap") && m.IsKeywordEnabled("_EMISSION") ? m.GetTexture("_EmissionMap") : null;
            return "tex:" + (tex ? AssetDatabase.GetAssetPath(tex) : "-") + ":" + (em ? AssetDatabase.GetAssetPath(em) : "-");
        }

        static bool IsColorId(Material m) => m != null && m.shader != null && m.shader.name.Contains("LPColor");

        /// <summary>The skeleton's root: the hips on a humanoid, else the largest skin's root bone.</summary>
        static Transform BodyBone(TypeBake b)
        {
            if (b.Animator.isHuman) return b.Animator.GetBoneTransform(HumanBodyBones.Hips);
            var smr = b.Root.GetComponentsInChildren<SkinnedMeshRenderer>(false)
                .Where(r => r.rootBone != null && r.sharedMesh != null)
                .OrderByDescending(r => r.sharedMesh.vertexCount).FirstOrDefault();
            return smr != null ? smr.rootBone : b.Animator.transform;
        }

        public static void Pose(TypeBake b, AnimationClip clip, float time)
        {
            AnimationMode.BeginSampling();
            AnimationMode.SampleAnimationClip(b.Animator.gameObject, clip, time);
            AnimationMode.EndSampling();
        }

        static void SampleClips(TypeBake b, JObject annotations)
        {
            var body = BodyBone(b);
            int row = 0, row30 = 0;
            foreach (var kv in b.Type.clips)
            {
                var clip = Roster.LoadClip(kv.Value);
                var a = annotations["annotations"].FirstOrDefault(x => (string)x["type"] == b.Type.id && (string)x["role"] == kv.Key);
                var c = new CrowdClip
                {
                    Role = kv.Key,
                    Name = clip.name,
                    FirstRow = row,
                    Length = clip.length,
                    Frames = CrowdClip.FrameCount(clip.length),
                    FirstRow30 = row30,
                    Frames30 = CrowdClip.FrameCount(clip.length, CrowdLibrary.CutClipRate),
                    Loop = a != null ? (bool)a["loop"] : clip.isLooping,
                    Impact = (float?)a?["impact"] ?? -1,
                    End = (float?)a?["end"] ?? -1,
                    StrideM = (float?)a?["strideM"] ?? -1,
                };
                if (c.Loop)
                {
                    // A looping clip that travels would jump at its seam: remove its horizontal
                    // drift linearly (the timeline moves the monster instead).
                    var p0 = BodyAt(b, clip, 0, body);
                    var p1 = BodyAt(b, clip, c.Length, body);
                    var d = p1 - p0;
                    d.y = 0;
                    if (d.magnitude > 0.05f) c.DriftRemovedM = d.magnitude;
                }
                b.Clips.Add(c);
                b.AnimationClips.Add(clip);
                row += c.Frames;
                row30 += c.Frames30;
            }
            b.Width = b.Entries.Count * 3;
            b.Height = row;
            b.Height30 = row30;
            if (b.Width > 16384 || b.Height > 16384) throw new Exception($"CrowdBaker: {b.Type.id} bone texture {b.Width}x{b.Height} is too large");
            b.Texels = Sample(b, body, Rate, b.Height, c => c.FirstRow, c => c.Frames);
            b.Texels30 = Sample(b, body, CrowdLibrary.CutClipRate, b.Height30, c => c.FirstRow30, c => c.Frames30);
        }

        /// <summary>Every clip's skinning matrices at one rate into a texel array of <paramref name="height"/> rows.</summary>
        static float[] Sample(TypeBake b, Transform body, float rate, int height, Func<CrowdClip, int> firstRow, Func<CrowdClip, int> frames)
        {
            var texels = new float[b.Width * height * 4];
            for (int ci = 0; ci < b.Clips.Count; ci++)
            {
                var c = b.Clips[ci];
                var clip = b.AnimationClips[ci];
                Vector3 drift = Vector3.zero;
                if (c.DriftRemovedM > 0)
                {
                    drift = BodyAt(b, clip, c.Length, body) - BodyAt(b, clip, 0, body);
                    drift.y = 0;
                }
                for (int f = 0; f < frames(c); f++)
                {
                    float time = c.FrameTime(f, rate);
                    Pose(b, clip, time);
                    var rootInv = RootTR(b).inverse;
                    if (c.DriftRemovedM > 0) rootInv = Matrix4x4.Translate(-drift * (time / c.Length)) * rootInv;
                    for (int e = 0; e < b.Entries.Count; e++)
                    {
                        var m = rootInv * b.Entries[e].Bone.localToWorldMatrix * b.Entries[e].Bind;
                        for (int r = 0; r < 3; r++)
                        {
                            int at = (((firstRow(c) + f) * b.Width) + e * 3 + r) * 4;
                            var v = m.GetRow(r);
                            texels[at] = v.x;
                            texels[at + 1] = v.y;
                            texels[at + 2] = v.z;
                            texels[at + 3] = v.w;
                        }
                    }
                }
            }
            return texels;
        }

        static Vector3 BodyAt(TypeBake b, AnimationClip clip, float time, Transform body)
        {
            Pose(b, clip, time);
            return RootTR(b).inverse.MultiplyPoint3x4(body.position);
        }

        /// <summary>
        /// Yaw-invariant bounding spheres around the vertical axis: one per clip (the culling pass
        /// takes the larger of a monster's two clips) and one over every clip. Every second vertex
        /// of every second frame, fully skinned, with a 5% margin.
        /// </summary>
        static void MeasureBounds(TypeBake b)
        {
            var all = new List<Vector3>();
            for (int ci = 0; ci < b.Clips.Count; ci++)
            {
                var c = b.Clips[ci];
                var pts = new List<Vector3>();
                var mats = new Matrix4x4[b.Entries.Count];
                for (int f = 0; f < c.Frames; f += f + 2 < c.Frames ? 2 : 1)
                {
                    for (int e = 0; e < mats.Length; e++) mats[e] = b.Matrix(e, c.FirstRow + f);
                    for (int v = 0; v < b.Positions.Count; v += 2) pts.Add(Skin(b, mats, v));
                    if (f == c.Frames - 1) break;
                }
                Sphere(pts, out c.CenterY, out c.Radius, out c.HeightM);
                b.Clips[ci] = c;
                all.AddRange(pts);
            }
            Sphere(all, out b.CenterY, out b.Radius, out b.HeightM);
        }

        public static Vector3 Skin(TypeBake b, Matrix4x4[] mats, int v)
        {
            var p = Vector3.zero;
            var bi = b.BoneIndices[v];
            var bw = b.BoneWeights[v];
            for (int k = 0; k < 4; k++)
                if (bw[k] > 0) p += bw[k] * mats[(int)bi[k]].MultiplyPoint3x4(b.Positions[v]);
            return p;
        }

        static void Sphere(List<Vector3> pts, out float centerY, out float radius, out float height)
        {
            float minY = float.MaxValue, maxY = float.MinValue;
            foreach (var p in pts)
            {
                minY = Mathf.Min(minY, p.y);
                maxY = Mathf.Max(maxY, p.y);
            }
            centerY = 0.5f * (minY + maxY);
            float r2 = 0;
            foreach (var p in pts)
                r2 = Mathf.Max(r2, p.x * p.x + p.z * p.z + (p.y - centerY) * (p.y - centerY));
            radius = Mathf.Sqrt(r2) * 1.05f + 0.02f;
            height = Mathf.Max(0.05f, maxY);
        }

        // ---------------------------------------------------------------------------------
        // Saving.

        static string TypeDir(string id) => $"{OutDir}/{id}";

        static CrowdType Save(TypeBake b, Shader shader)
        {
            var id = b.Type.id;
            Directory.CreateDirectory(TypeDir(id));
            var mesh = b.BuildMesh(id + "_crowd");
            Replace(mesh, $"{TypeDir(id)}/{id}_crowd.mesh");
            var lods = BuildLods(mesh, id);
            Replace(lods[0], $"{TypeDir(id)}/{id}_lod1.mesh");
            Replace(lods[1], $"{TypeDir(id)}/{id}_lod2.mesh");

            var tex = new Texture2D(b.Width, b.Height, TextureFormat.RGBAFloat, false, true)
            {
                name = id + "_bones", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, anisoLevel = 0,
            };
            tex.SetPixelData(b.Texels, 0);
            tex.Apply(false, false);
            Replace(tex, $"{TypeDir(id)}/{id}_bones.asset");
            var tex30 = new Texture2D(b.Width, b.Height30, TextureFormat.RGBAFloat, false, true)
            {
                name = id + "_bones30", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, anisoLevel = 0,
            };
            tex30.SetPixelData(b.Texels30, 0);
            tex30.Apply(false, false);
            Replace(tex30, $"{TypeDir(id)}/{id}_bones30.asset");

            var crowdMats = new Material[b.Submeshes.Count];
            var stockMats = new Material[b.Submeshes.Count];
            for (int i = 0; i < b.Submeshes.Count; i++)
            {
                var src = b.Submeshes[i].Source;
                var look = Look.Of(src, $"{TypeDir(id)}/{id}_albedo{i}.asset");
                crowdMats[i] = look.Apply(new Material(shader) { name = $"{id}_crowd{i}" });
                crowdMats[i].enableInstancing = false;
                Replace(crowdMats[i], $"{TypeDir(id)}/{id}_crowd{i}.mat");
                stockMats[i] = look.Apply(new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = $"{id}_stock{i}" });
                // Emission stays on (black) so the stock arm's hit flash is a property change.
                stockMats[i].EnableKeyword("_EMISSION");
                stockMats[i].globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
                stockMats[i].enableInstancing = false;
                Replace(stockMats[i], $"{TypeDir(id)}/{id}_stock{i}.mat");
            }

            var ct = new CrowdType
            {
                Id = id,
                Kind = b.Type.kind,
                Mesh = mesh,
                Lod1 = lods[0],
                Lod2 = lods[1],
                Lod1Triangles = Triangles(lods[0]),
                Lod2Triangles = Triangles(lods[1]),
                Materials = crowdMats,
                Bones = tex,
                Bones30 = tex30,
                BoneCount = b.Entries.Count,
                Clips = b.Clips.ToArray(),
                CenterY = b.CenterY,
                Radius = b.Radius,
                HeightM = b.HeightM,
                RadiusM = b.Type.radius,
                HoverM = b.Type.hoverM,
                Vertices = b.Positions.Count,
                Triangles = b.Submeshes.Sum(s => s.Indices.Count) / 3,
            };
            ct.StockPrefab = SaveStock(b, stockMats);
            return ct;
        }

        /// <summary>
        /// LOD1 and LOD2 of the combined mesh (TD11, performance.md's decimated meshes): Unity 6.6's
        /// own Mesh LOD generation (MeshLodUtility, which simplifies each submesh's index buffer by
        /// about half per level and keeps every vertex and stream), run on a copy; each level's
        /// index ranges become their own mesh over the same vertices, so the crowd's indirect draws
        /// stay one mesh per LOD.
        /// </summary>
        public static Mesh[] BuildLods(Mesh lod0, string id)
        {
            var work = UnityEngine.Object.Instantiate(lod0);
            try
            {
                MeshLodUtility.GenerateMeshLods(work, 2);
                var result = new Mesh[2];
                for (int level = 1; level <= 2; level++)
                {
                    var m = UnityEngine.Object.Instantiate(lod0);
                    m.name = $"{id}_lod{level}";
                    for (int sm = 0; sm < lod0.subMeshCount; sm++)
                    {
                        int[] idx;
                        if (work.lodCount > level)
                        {
                            var range = work.GetLod(sm, level);
                            var d = work.GetSubMesh(sm);
                            // A level's range is relative to its submesh's start in the whole index buffer.
                            idx = LodIndices(work, sm, d, range);
                        }
                        else idx = lod0.GetIndices(sm, false); // too small to simplify further
                        m.SetIndices(idx, MeshTopology.Triangles, sm, false, lod0.GetSubMesh(sm).baseVertex);
                    }
                    m.bounds = lod0.bounds;
                    result[level - 1] = m;
                }
                return result;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(work);
            }
        }

        /// <summary>One LOD level's indices of a submesh, relative to the submesh's base vertex.</summary>
        static int[] LodIndices(Mesh work, int sm, SubMeshDescriptor d, MeshLodRange range)
        {
            using var data = Mesh.AcquireReadOnlyMeshData(work);
            var md = data[0];
            var idx = new int[range.indexCount];
            int start = d.indexStart + (int)range.indexStart;
            if (md.indexFormat == IndexFormat.UInt16)
            {
                var all = md.GetIndexData<ushort>();
                for (int i = 0; i < idx.Length; i++) idx[i] = all[start + i];
            }
            else
            {
                var all = md.GetIndexData<int>();
                for (int i = 0; i < idx.Length; i++) idx[i] = all[start + i];
            }
            // The index buffer holds vertex indices relative to the submesh's base vertex.
            return idx;
        }

        static int Triangles(Mesh m)
        {
            long n = 0;
            for (int sm = 0; sm < m.subMeshCount; sm++) n += m.GetIndexCount(sm);
            return (int)(n / 3);
        }

        static void Replace(UnityEngine.Object asset, string path)
        {
            AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(asset, path);
        }

        /// <summary>A type's surface: albedo, emission, smoothness and metallic.</summary>
        sealed class Look
        {
            public Texture Albedo, Emission;
            public Color BaseColor = Color.white, EmissionColor = Color.black;
            public float Smoothness = 0.2f, Metallic;

            public static Look Of(Material m, string colorIdPath)
            {
                var l = new Look();
                if (m == null) return l;
                if (IsColorId(m))
                {
                    l.Albedo = BakeColorId(m, colorIdPath);
                    l.Smoothness = m.HasProperty("_Smoothness") ? m.GetFloat("_Smoothness") : 0.2f;
                    l.Metallic = m.HasProperty("_Metal") ? m.GetFloat("_Metal") : 0;
                    return l;
                }
                l.Albedo = m.HasProperty("_BaseMap") ? m.GetTexture("_BaseMap") : null;
                if (m.HasProperty("_BaseColor")) l.BaseColor = m.GetColor("_BaseColor");
                l.BaseColor.a = 1;
                if (m.HasProperty("_Smoothness")) l.Smoothness = m.GetFloat("_Smoothness");
                if (m.HasProperty("_Metallic")) l.Metallic = m.GetFloat("_Metallic");
                if (m.IsKeywordEnabled("_EMISSION") && m.HasProperty("_EmissionMap"))
                {
                    l.Emission = m.GetTexture("_EmissionMap");
                    l.EmissionColor = m.GetColor("_EmissionColor");
                }
                return l;
            }

            public Material Apply(Material m)
            {
                m.SetTexture("_BaseMap", Albedo);
                m.SetColor("_BaseColor", BaseColor);
                m.SetFloat("_Smoothness", Smoothness);
                m.SetFloat("_Metallic", Metallic);
                if (Emission != null)
                {
                    m.SetTexture("_EmissionMap", Emission);
                    m.SetColor("_EmissionColor", EmissionColor);
                }
                else
                {
                    m.SetColor("_EmissionColor", Color.black);
                }
                return m;
            }
        }

        /// <summary>
        /// InfinityPBR's LPColor shader picks each texel's colour by matching its colour-ID texture
        /// against 49 ID colours and lerping in an HSV replacement (IPBR_LPColor_URP, Forward pass).
        /// The same chain, evaluated once per texel, gives a plain albedo texture.
        /// </summary>
        public static Texture2D BakeColorId(Material m, string path)
        {
            var src = m.GetTexture("_MainTex") as Texture2D ?? throw new Exception($"CrowdBaker: {m.name} has no colour-ID texture");
            var srcPath = AssetDatabase.GetAssetPath(src);
            var importer = AssetImporter.GetAtPath(srcPath) as TextureImporter;
            bool srgb = importer == null || importer.sRGBTexture;
            var pixels = ReadPixels(src);
            float range = m.GetFloat("_ColorIDRange"), fuzz = Mathf.Max(m.GetFloat("_ColorIDFuzziness"), 1e-5f);
            var ids = new Vector3[49];
            var hsv = new Vector3[49];
            for (int i = 0; i < 49; i++)
            {
                var c = m.GetColor("_Color" + i).linear; // Color properties reach the shader linear
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
                output[p] = new Color(result.x, result.y, result.z, 1f).gamma; // stored sRGB
            }
            var tex = new Texture2D(src.width, src.height, TextureFormat.RGBA32, false, false)
            {
                name = Path.GetFileNameWithoutExtension(path), filterMode = src.filterMode, wrapMode = src.wrapMode,
            };
            tex.SetPixels(output);
            tex.Apply(false, false);
            Replace(tex, path);
            return tex;
        }

        static Color[] ReadPixels(Texture2D src)
        {
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
            var pixels = copy.GetPixels();
            UnityEngine.Object.DestroyImmediate(copy);
            return pixels;
        }

        /// <summary>ASE's HSVToRGB, as the vendor shader uses it.</summary>
        public static Vector3 HsvToRgb(Vector3 c)
        {
            var k = new Vector4(1f, 2f / 3f, 1f / 3f, 3f);
            Vector3 p = new Vector3(
                Mathf.Abs(Frac(c.x + k.x) * 6f - k.w),
                Mathf.Abs(Frac(c.x + k.y) * 6f - k.w),
                Mathf.Abs(Frac(c.x + k.z) * 6f - k.w));
            return c.z * Vector3.Lerp(Vector3.one * k.x,
                new Vector3(Mathf.Clamp01(p.x - k.x), Mathf.Clamp01(p.y - k.x), Mathf.Clamp01(p.z - k.x)), c.y);
        }

        static float Frac(float x) => x - Mathf.Floor(x);

        // ---------------------------------------------------------------------------------
        // The stock baseline's prefab.

        static GameObject SaveStock(TypeBake b, Material[] mats)
        {
            var id = b.Type.id;
            var root = new GameObject("stock_" + id);
            try
            {
                b.Root.transform.SetParent(root.transform, false);
                // One SkinnedMeshRenderer per monster: the combined mesh with Unity's bone weights.
                var mesh = b.BuildMesh(id + "_stock", skinStreams: false);
                var perVertex = new Unity.Collections.NativeArray<byte>(b.Positions.Count, Unity.Collections.Allocator.Temp);
                var weights = new List<BoneWeight1>();
                for (int v = 0; v < b.Positions.Count; v++)
                {
                    var bi = b.BoneIndices[v];
                    var bw = b.BoneWeights[v];
                    var list = new List<BoneWeight1>();
                    for (int k = 0; k < 4; k++)
                        if (bw[k] > 0) list.Add(new BoneWeight1 { boneIndex = (int)bi[k], weight = bw[k] });
                    list.Sort((x, y) => y.weight.CompareTo(x.weight));
                    perVertex[v] = (byte)list.Count;
                    weights.AddRange(list);
                }
                var nw = new Unity.Collections.NativeArray<BoneWeight1>(weights.ToArray(), Unity.Collections.Allocator.Temp);
                mesh.SetBoneWeights(perVertex, nw);
                perVertex.Dispose();
                nw.Dispose();
                mesh.bindposes = b.Entries.Select(e => e.Bind).ToArray();
                Replace(mesh, $"{TypeDir(id)}/{id}_stock.mesh");

                foreach (var r in b.Root.GetComponentsInChildren<Renderer>(true)) UnityEngine.Object.DestroyImmediate(r);
                foreach (var f in b.Root.GetComponentsInChildren<MeshFilter>(true)) UnityEngine.Object.DestroyImmediate(f);
                var meshGo = new GameObject("Mesh");
                meshGo.transform.SetParent(root.transform, false);
                var smr = meshGo.AddComponent<SkinnedMeshRenderer>();
                smr.sharedMesh = mesh;
                smr.bones = b.Entries.Select(e => e.Bone).ToArray();
                smr.sharedMaterials = mats;
                smr.quality = SkinQuality.Bone4;
                smr.updateWhenOffscreen = false;
                smr.shadowCastingMode = ShadowCastingMode.On;
                smr.localBounds = new Bounds(new Vector3(0, b.CenterY, 0), Vector3.one * (2 * b.Radius));
                smr.skinnedMotionVectors = true;

                var controller = AnimatorController.CreateAnimatorControllerAtPath($"{OutDir}/Stock/{id}.controller");
                var sm = controller.layers[0].stateMachine;
                for (int i = 0; i < b.Clips.Count; i++)
                {
                    var st = sm.AddState(b.Clips[i].Role);
                    st.motion = b.AnimationClips[i];
                    st.writeDefaultValues = false;
                    if (b.Clips[i].Role == "walk") sm.defaultState = st;
                }
                b.Animator.runtimeAnimatorController = controller;
                b.Animator.applyRootMotion = false;
                b.Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                var prefabPath = $"{OutDir}/Stock/{id}.prefab";
                AssetDatabase.DeleteAsset(prefabPath);
                return PrefabUtility.SaveAsPrefabAsset(root, prefabPath)
                       ?? throw new Exception($"CrowdBaker: could not save {prefabPath}");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
                b.Root = null; // destroyed with the stock root
            }
        }

        static Material SaveGround()
        {
            var m = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "crowd_ground" };
            m.SetColor("_BaseColor", new Color(0.32f, 0.33f, 0.30f));
            m.SetFloat("_Smoothness", 0.1f);
            Replace(m, OutDir + "/Resources/crowd_ground.mat");
            return m;
        }
    }
}
