using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Game.Client.Crowd;
using PS.Game.Sim.Content;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace Game.EditorTools.Crowd
{
    /// <summary>
    /// The crowd baker (unity-game.md, The Crowd): demo3d's CrowdBaker, copied and owned for the
    /// crystal style. For each roster type it writes, only under the gitignored Assets/Baked/Crowd/:
    /// - the combined mesh's three LODs as style meshes: every active skinned mesh plus rigid
    ///   attachments (the weapons on their hand bones) in root space at roster height, LOD1 and
    ///   LOD2 decimated to about a half and a quarter of its triangles by Unity's MeshLodUtility,
    ///   each unwelded (<see cref="Unweld"/>) into one submesh with 4 bone indices in UV1, weights
    ///   in UV2, the face normal in the tangent and barycentrics plus a value per triangle in UV3;
    /// - one float texture of skinning matrices (bone world × bind pose, relative to the
    ///   character's root), 3 float4 rows per bone per frame, every clip sampled at 30 Hz;
    /// - the clip table (first row, frame count, loop, length and the annotations) with each
    ///   clip's bounding sphere;
    /// and the CrowdLibrary asset that lists them with the cast (game/Content/cast.json: each
    /// role's model, scale and gem colour) and the crowd's shaders.
    /// </summary>
    public static class CrowdBaker
    {
        public const string OutDir = "Assets/Baked/Crowd";
        public const string LibraryPath = OutDir + "/Resources/" + CrowdLibrary.ResourcePath + ".asset";
        const string ShaderDir = "Assets/Game/Client/Crowd/Shaders";
        const string ContentDir = "Content";
        static readonly string[] ClipRoles = { "idle", "walk", "attack", "hit" };

        /// <summary>A bone (or rigid attachment) of the combined mesh.</summary>
        sealed class Entry
        {
            public Transform Bone;
            public Matrix4x4 Bind; // maps the combined mesh's root-space vertices into the bone's space
        }

        /// <summary>One type's bake, in memory.</summary>
        sealed class TypeBake : IDisposable
        {
            public Roster.Type Type;
            public GameObject Root;
            public Animator Animator;
            public readonly List<Entry> Entries = new List<Entry>();
            public readonly List<Vector3> Positions = new List<Vector3>();
            public readonly List<Vector3> Normals = new List<Vector3>();
            public readonly List<Vector4> BoneIndices = new List<Vector4>();
            public readonly List<Vector4> BoneWeights = new List<Vector4>();
            public readonly List<int> Indices = new List<int>();
            public readonly List<CrowdClip> Clips = new List<CrowdClip>();
            public readonly List<AnimationClip> AnimationClips = new List<AnimationClip>();
            public float[] Texels; // RGBAFloat, width = 3 × bones, height = total frames
            public int Width, Height;
            public float CenterY, Radius, HeightM;

            public Mesh BuildMesh(string name)
            {
                var m = new Mesh { name = name };
                if (Positions.Count > 65535) m.indexFormat = IndexFormat.UInt32;
                m.SetVertices(Positions);
                m.SetNormals(Normals);
                m.SetUVs(1, BoneIndices);
                m.SetUVs(2, BoneWeights);
                m.SetTriangles(Indices, 0, false);
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

        /// <summary>Bakes every roster type and writes the library (the "Crowd" baker).</summary>
        public static void Bake()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            Directory.CreateDirectory(OutDir + "/Resources");
            var types = new List<CrowdType>();
            foreach (var t in Roster.Load())
            {
                var started = DateTime.UtcNow;
                using var bake = BakeType(t);
                var ct = Save(bake);
                types.Add(ct);
                Debug.Log($"CrowdBaker: {t.id}: {bake.Positions.Count} vertices, LOD triangles " +
                          string.Join("/", ct.Lods.Select(m => m.GetIndexCount(0) / 3)) + $", {ct.BoneCount} bones, " +
                          $"{bake.Height} frames ({bake.Width}x{bake.Height} texels, {bake.Width * bake.Height * 16 / 1024} KiB), " +
                          $"radius {ct.Radius:F2} m, height {ct.HeightM:F2} m, in {(DateTime.UtcNow - started).TotalSeconds:F1} s");
            }
            var lib = ScriptableObject.CreateInstance<CrowdLibrary>();
            lib.Types = types.ToArray();
            lib.Cast = Cast(GameContent.Load(ContentDir), lib);
            lib.Crystal = Load<Shader>(ShaderDir + "/MonsterCrystal.shader");
            lib.CullShader = Load<ComputeShader>(ShaderDir + "/CrowdCull.compute");
            lib.PaletteShader = Load<ComputeShader>(ShaderDir + "/CrowdPalette.compute");
            Replace(lib, LibraryPath);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single); // leave no modified scene behind
            AssetDatabase.SaveAssets();
            Debug.Log($"CrowdBaker: wrote {types.Count} types and {lib.Cast.Length} roles to {LibraryPath}");
        }

        static T Load<T>(string path) where T : UnityEngine.Object =>
            AssetDatabase.LoadAssetAtPath<T>(path) ?? throw new Exception("CrowdBaker: no " + path);

        /// <summary>Each role's look, in the content's role order (the sim's role indices).</summary>
        internal static CastLook[] Cast(GameContent content, CrowdLibrary lib)
        {
            var cast = new CastLook[content.Cast.Length];
            for (int i = 0; i < cast.Length; i++)
            {
                var c = content.Cast[i];
                int type = lib.IndexOf(c.Model);
                if (type < 0) throw new Exception($"CrowdBaker: cast.json's {c.Role} is the model '{c.Model}', which the roster lacks");
                cast[i] = new CastLook
                {
                    Role = c.Role,
                    Type = type,
                    Scale = (float)c.Scale,
                    Gem = new Color32(Hex(c.Crystal, 1), Hex(c.Crystal, 3), Hex(c.Crystal, 5), 255),
                };
            }
            return cast;
        }

        static byte Hex(string rgb, int at) => byte.Parse(rgb.Substring(at, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);

        // ---------------------------------------------------------------------------------
        // Baking one type in memory.

        static TypeBake BakeType(Roster.Type t)
        {
            var b = new TypeBake { Type = t };
            b.Root = Roster.Spawn(t);
            foreach (var tr in b.Root.GetComponentsInChildren<Transform>(true))
                GameObjectUtility.RemoveMonoBehavioursWithMissingScript(tr.gameObject); // vendor scripts we don't sync
            b.Animator = b.Root.GetComponentInChildren<Animator>();
            if (b.Animator == null) throw new Exception($"CrowdBaker: {t.id} has no Animator");
            Combine(b);
            AnimationMode.StartAnimationMode();
            try
            {
                SampleClips(b);
                MeasureBounds(b);
            }
            finally
            {
                AnimationMode.StopAnimationMode();
            }
            return b;
        }

        /// <summary>The root's rotation and translation (not its scale: the mesh is at roster height).</summary>
        static Matrix4x4 RootTR(TypeBake b) => Matrix4x4.TRS(b.Root.transform.position, b.Root.transform.rotation, Vector3.one);

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
                var verts = mesh.vertices;
                var normals = mesh.normals;
                var perVertex = rigid ? default : mesh.GetBonesPerVertex();
                var weights = rigid ? default : mesh.GetAllBoneWeights();
                int w0 = 0;
                for (int v = 0; v < verts.Length; v++)
                {
                    b.Positions.Add(toRoot.MultiplyPoint3x4(verts[v]));
                    b.Normals.Add(normals.Length > v ? toRoot.MultiplyVector(normals[v]).normalized : Vector3.up);
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
                for (int s = 0; s < mesh.subMeshCount; s++)
                    foreach (var i in mesh.GetTriangles(s)) b.Indices.Add(baseVertex + i);
            }
            if (b.Positions.Count == 0) throw new Exception($"CrowdBaker: {b.Type.id} has no active renderers");
        }

        /// <summary>The skeleton's root: the hips on a humanoid, else the largest skin's root bone.</summary>
        static Transform BodyBone(TypeBake b)
        {
            if (b.Animator.isHuman) return b.Animator.GetBoneTransform(HumanBodyBones.Hips);
            var smr = b.Root.GetComponentsInChildren<SkinnedMeshRenderer>(false)
                .Where(r => r.rootBone != null && r.sharedMesh != null)
                .OrderByDescending(r => r.sharedMesh.vertexCount).FirstOrDefault();
            return smr != null ? smr.rootBone : b.Animator.transform;
        }

        static void Pose(TypeBake b, AnimationClip clip, float time)
        {
            AnimationMode.BeginSampling();
            AnimationMode.SampleAnimationClip(b.Animator.gameObject, clip, time);
            AnimationMode.EndSampling();
        }

        static void SampleClips(TypeBake b)
        {
            int row = 0;
            foreach (var role in ClipRoles)
            {
                if (!b.Type.clips.TryGetValue(role, out var r)) throw new Exception($"CrowdBaker: {b.Type.id} has no {role} clip");
                var clip = Roster.LoadClip(r);
                var c = new CrowdClip
                {
                    Role = role,
                    Name = clip.name,
                    FirstRow = row,
                    Length = clip.length,
                    Frames = CrowdClip.FrameCount(clip.length),
                    Loop = r.loop,
                    Impact = r.impact ?? -1,
                    End = r.end ?? -1,
                    StrideM = r.strideM ?? -1,
                };
                b.Clips.Add(c);
                b.AnimationClips.Add(clip);
                row += c.Frames;
            }
            b.Width = b.Entries.Count * 3;
            b.Height = row;
            if (b.Width > 16384 || b.Height > 16384) throw new Exception($"CrowdBaker: {b.Type.id} bone texture {b.Width}x{b.Height} is too large");
            var body = BodyBone(b);
            b.Texels = new float[b.Width * b.Height * 4];
            for (int ci = 0; ci < b.Clips.Count; ci++)
            {
                var c = b.Clips[ci];
                var clip = b.AnimationClips[ci];
                // A looping clip that travels would jump at its seam: its horizontal drift is
                // removed linearly (the sim moves the monster instead).
                var drift = Vector3.zero;
                if (c.Loop)
                {
                    drift = BodyAt(b, clip, c.Length, body) - BodyAt(b, clip, 0, body);
                    drift.y = 0;
                    if (drift.magnitude <= 0.05f) drift = Vector3.zero;
                }
                for (int f = 0; f < c.Frames; f++)
                {
                    float time = c.FrameTime(f);
                    Pose(b, clip, time);
                    var rootInv = Matrix4x4.Translate(-drift * (time / c.Length)) * RootTR(b).inverse;
                    for (int e = 0; e < b.Entries.Count; e++)
                    {
                        var m = rootInv * b.Entries[e].Bone.localToWorldMatrix * b.Entries[e].Bind;
                        for (int r = 0; r < 3; r++)
                        {
                            int at = (((c.FirstRow + f) * b.Width) + e * 3 + r) * 4;
                            var v = m.GetRow(r);
                            b.Texels[at] = v.x;
                            b.Texels[at + 1] = v.y;
                            b.Texels[at + 2] = v.z;
                            b.Texels[at + 3] = v.w;
                        }
                    }
                }
            }
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

        static Vector3 Skin(TypeBake b, Matrix4x4[] mats, int v)
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

        static CrowdType Save(TypeBake b)
        {
            var id = b.Type.id;
            Directory.CreateDirectory(TypeDir(id));
            var welded = b.BuildMesh(id + "_welded");
            var decimated = BuildLods(welded);
            int seed = StableSeed(id);
            var lods = new[] { Unweld(welded, seed), Unweld(decimated[0], seed + 1), Unweld(decimated[1], seed + 2) };
            for (int l = 0; l < lods.Length; l++)
            {
                lods[l].name = $"{id}_lod{l}";
                Replace(lods[l], $"{TypeDir(id)}/{id}_lod{l}.mesh");
            }
            UnityEngine.Object.DestroyImmediate(welded);
            foreach (var m in decimated) UnityEngine.Object.DestroyImmediate(m);

            var tex = new Texture2D(b.Width, b.Height, TextureFormat.RGBAFloat, false, true)
            {
                name = id + "_bones", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, anisoLevel = 0,
            };
            tex.SetPixelData(b.Texels, 0);
            tex.Apply(false, false); // kept readable, so the GPU shatter's tests can pose shards on the CPU
            Replace(tex, $"{TypeDir(id)}/{id}_bones.asset");

            return new CrowdType
            {
                Id = id,
                Lods = lods,
                Bones = tex,
                BoneCount = b.Entries.Count,
                Clips = b.Clips.ToArray(),
                CenterY = b.CenterY,
                Radius = b.Radius,
                HeightM = b.HeightM,
                HoverM = b.Type.hoverM,
            };
        }

        /// <summary>
        /// LOD1 and LOD2 of the welded mesh: Unity's own Mesh LOD generation (MeshLodUtility, which
        /// simplifies the index buffer by about half per level and keeps every vertex and stream),
        /// run on a copy; each level's index range becomes its own mesh over the same vertices.
        /// </summary>
        static Mesh[] BuildLods(Mesh lod0)
        {
            var work = UnityEngine.Object.Instantiate(lod0);
            try
            {
                MeshLodUtility.GenerateMeshLods(work, 2);
                var result = new Mesh[2];
                for (int level = 1; level <= 2; level++)
                {
                    var m = UnityEngine.Object.Instantiate(lod0);
                    int[] idx = work.lodCount > level ? LodIndices(work, work.GetSubMesh(0), work.GetLod(0, level)) : lod0.GetIndices(0, false);
                    m.SetIndices(idx, MeshTopology.Triangles, 0, false, lod0.GetSubMesh(0).baseVertex);
                    result[level - 1] = m;
                }
                return result;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(work);
            }
        }

        /// <summary>One LOD level's indices, relative to the submesh's base vertex.</summary>
        static int[] LodIndices(Mesh work, SubMeshDescriptor d, MeshLodRange range)
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
            return idx;
        }

        /// <summary>
        /// A welded mesh unwelded into the crystal's style mesh (demo3d's CrowdBaker.Unweld): every
        /// triangle gets its own three vertices, so the crystal can facet and edge each one. It keeps
        /// the positions (the rest pose in metres), normals and bone data (UV1/UV2) and adds the face
        /// normal in the tangent and barycentric coordinates plus a random value per triangle in UV3.
        /// </summary>
        internal static Mesh Unweld(Mesh src, int seed)
        {
            var pos = src.vertices;
            var nor = src.normals;
            var bones = new List<Vector4>(); src.GetUVs(1, bones);
            var weights = new List<Vector4>(); src.GetUVs(2, weights);
            var tris = src.GetIndices(0); // with the submesh's base vertex applied
            int n = tris.Length;
            var oPos = new List<Vector3>(n);
            var oNor = new List<Vector3>(n);
            var oTan = new List<Vector4>(n);
            var oBones = new List<Vector4>(n);
            var oWeights = new List<Vector4>(n);
            var oBary = new List<Vector4>(n);
            var outTris = new int[n];
            var rng = new System.Random(seed);
            for (int t = 0; t + 2 < n; t += 3)
            {
                // Normalized by hand: Vector3.normalized zeroes a tiny triangle's normal, which the
                // shader would turn into NaN. A degenerate one takes its first vertex's normal.
                var cross = Vector3.Cross(pos[tris[t + 1]] - pos[tris[t]], pos[tris[t + 2]] - pos[tris[t]]);
                float len = cross.magnitude;
                var face = len > 0 ? cross / len : nor[tris[t]];
                float r = (float)rng.NextDouble();
                for (int k = 0; k < 3; k++)
                {
                    int v = tris[t + k];
                    outTris[t + k] = oPos.Count;
                    oPos.Add(pos[v]);
                    oNor.Add(nor[v]);
                    oTan.Add(new Vector4(face.x, face.y, face.z, 1));
                    oBones.Add(bones[v]);
                    oWeights.Add(weights[v]);
                    oBary.Add(new Vector4(k == 0 ? 1 : 0, k == 1 ? 1 : 0, k == 2 ? 1 : 0, r));
                }
            }
            var m = new Mesh();
            if (oPos.Count > 65535) m.indexFormat = IndexFormat.UInt32;
            m.SetVertices(oPos);
            m.SetNormals(oNor);
            m.SetTangents(oTan);
            m.SetUVs(1, oBones);
            m.SetUVs(2, oWeights);
            m.SetUVs(3, oBary);
            m.SetTriangles(outTris, 0, false);
            m.bounds = src.bounds;
            return m;
        }

        /// <summary>A seed from a type id that is the same in every run (string.GetHashCode needn't be).</summary>
        static int StableSeed(string id)
        {
            int h = 17;
            foreach (char c in id) h = unchecked(h * 31 + c);
            return h;
        }

        static void Replace(UnityEngine.Object asset, string path)
        {
            AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(asset, path);
        }
    }
}
