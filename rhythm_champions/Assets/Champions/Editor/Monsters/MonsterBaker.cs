using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Champions.Render.Crowd;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace Champions.EditorTools.Monsters
{
    /// <summary>
    /// The monster baker (plan.md, Rendering &gt; The edge look): game/'s and the frozen look package's crowd bakers, which
    /// made the neon edge trailer's crowd, ported by review for one type at a time. For a type in monsters.json it writes,
    /// only under the ignored Assets/Baked/Monsters/:
    /// - TYPE/: the combined vendor mesh's three LODs as style meshes (every active skinned mesh plus its rigid
    ///   attachments and held weapons, in root space at the type's height; LOD1 and LOD2 decimated to about a half and a
    ///   quarter by Unity's MeshLodUtility; each unwelded by <see cref="StyleMesh.Unweld"/>, feature edges flagged), one
    ///   float texture of skinning matrices (3 float4 rows per bone per frame, every clip sampled at 30 Hz, a flight
    ///   clip raised by its lift), and LOD0's rest pose cut into the shatter's chunks (<see cref="MonsterShardBaker"/>),
    ///   weapons and shields with the body;
    /// - Resources/MonsterTypes/trailer/TYPE.asset: the type as `--look trailer` draws it, always the vendor model;
    /// - Resources/MonsterTypes/default/TYPE.asset: the default look's, from the recipe's mesh when
    ///   `champions monster bake` left one in TYPE/recipe/ (tools/recipes/monster/TYPE.py, skinned to the same bones,
    ///   so it shares the bone texture), else the trailer's own meshes. A recipe may choose which edges draw (flags, or
    ///   its own crease angle: every edge of a designed low-poly mesh); such a mesh is not decimated.
    /// A bake is skipped while its key (the type's entry, the baker's code, the recipe's mesh) matches TYPE/bake.key.
    /// <see cref="Export"/> writes the combined vendor mesh to TYPE/source/ for a recipe to read.
    /// </summary>
    public static class MonsterBaker
    {
        /// <summary>Where bakes go (a test bakes under a root of its own).</summary>
        internal static string Root = "Assets/Baked/Monsters";
        const string KeyFile = "bake.key";
        /// <summary>What a bake's key hashes beside the type's entry and its recipe's mesh: the code that bakes and the asset it writes.</summary>
        static readonly string[] CodeInputs = { "Assets/Champions/Editor/Monsters", "Assets/Champions/Render/Crowd/MonsterType.cs" };

        public static string TypeDir(string id) => $"{Root}/{id}";
        public static string SourceDir(string id) => $"{TypeDir(id)}/source";
        public static string RecipeDir(string id) => $"{TypeDir(id)}/recipe";
        public static string AssetPath(string id, MonsterLook look) => $"{Root}/Resources/{MonsterLibrary.TypesPath(look)}/{id}.asset";

        /// <summary>A bone (or rigid attachment) of the combined mesh.</summary>
        sealed class Entry
        {
            public Transform Bone;
            public Matrix4x4 Bind; // maps the combined mesh's root-space vertices into the bone's space
        }

        /// <summary>A combined mesh in root space: positions, normals, 4 bone entries and weights per vertex, triangles.</summary>
        sealed class Geometry
        {
            public readonly List<Vector3> Positions = new List<Vector3>();
            public readonly List<Vector3> Normals = new List<Vector3>();
            public readonly List<Vector4> BoneIndices = new List<Vector4>();
            public readonly List<Vector4> BoneWeights = new List<Vector4>();
            public readonly List<int> Indices = new List<int>();
            /// <summary>Each vertex: is it of something the monster carries (a weapon, a shield; MonsterModel.carried)?</summary>
            public readonly List<bool> Carried = new List<bool>();
            /// <summary>
            /// Which edges draw: per triangle, component k 1 for the edge opposite corner k (a recipe's own flags), else
            /// every crease sharper than <see cref="EdgeAngleDeg"/>.
            /// </summary>
            public Vector3[] Edges;
            public float EdgeAngleDeg = StyleMesh.FeatureAngleDeg;
            /// <summary>A mesh that draws edges of its own choosing (flags or an angle of its own): its topology is the drawing.</summary>
            public bool Designed => Edges != null || EdgeAngleDeg != StyleMesh.FeatureAngleDeg;
            /// <summary>Each source renderer's vertex range (the export's parts).</summary>
            public readonly List<(string Name, int First, int Count, bool Rigid, bool Carried)> Parts = new List<(string, int, int, bool, bool)>();

            /// <summary>Each triangle: are most of its corners carried?</summary>
            public bool[] CarriedTriangles()
            {
                var o = new bool[Indices.Count / 3];
                for (int t = 0; t < o.Length; t++)
                    o[t] = (Carried[Indices[3 * t]] ? 1 : 0) + (Carried[Indices[3 * t + 1]] ? 1 : 0) + (Carried[Indices[3 * t + 2]] ? 1 : 0) >= 2;
                return o;
            }

            public Mesh Build(string name, float centerY, float radius)
            {
                var m = new Mesh { name = name };
                if (Positions.Count > 65535) m.indexFormat = IndexFormat.UInt32;
                m.SetVertices(Positions);
                m.SetNormals(Normals);
                m.SetUVs(1, BoneIndices);
                m.SetUVs(2, BoneWeights);
                m.SetTriangles(Indices, 0, false);
                m.bounds = new Bounds(new Vector3(0, centerY, 0), Vector3.one * (2 * radius));
                return m;
            }
        }

        /// <summary>One type's bake, in memory.</summary>
        sealed class TypeBake : IDisposable
        {
            public MonsterModel Model;
            public GameObject Root;
            public Animator Animator;
            public readonly List<Entry> Entries = new List<Entry>();
            public readonly List<MonsterClip> Clips = new List<MonsterClip>();
            public float[] Texels; // RGBAFloat, width = 3 x entries, height = total frames
            public int Width, Height;

            /// <summary>The baked matrix of entry b at a texture row, as a 4x4.</summary>
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

        /// <summary>Bakes every type in monsters.json whose bake is out of date (all with <paramref name="force"/>); false if any failed for want of vendor files.</summary>
        public static bool BakeAll(bool force = false)
        {
            bool ok = true;
            foreach (var model in MonsterModel.All()) ok &= Bake(model.id, force);
            return ok;
        }

        /// <summary>
        /// Bakes one type for both looks unless its key is current (always with <paramref name="force"/>); false, with
        /// nothing written, when its vendor files are missing.
        /// </summary>
        public static bool Bake(string id, bool force = false)
        {
            var model = MonsterModel.Of(id);
            if (!VendorPresent(model, out string missing))
            {
                Debug.LogWarning($"MonsterBaker: {id}: no vendor asset {missing} (run `champions setup`); not baked");
                return false;
            }
            string key = Key(model);
            string keyPath = $"{TypeDir(id)}/{KeyFile}";
            if (!force && File.Exists(keyPath) && File.ReadAllText(keyPath).Trim() == key
                && File.Exists(AssetPath(id, MonsterLook.Trailer)) && File.Exists(AssetPath(id, MonsterLook.Default)))
            {
                Debug.Log($"MonsterBaker: {id}: current ({key})");
                return true;
            }
            var started = DateTime.UtcNow;
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            Directory.CreateDirectory(TypeDir(id));
            using (var bake = Spawn(model, out var vendor))
            {
                var bones = SaveBones(bake);
                var trailer = Save(bake, vendor, "vendor", bones, $"{id}_vendor");
                Write(trailer, AssetPath(id, MonsterLook.Trailer));
                var recipe = ReadRecipe(id, bake.Entries.Count);
                MonsterType standard;
                if (recipe != null)
                {
                    standard = Save(bake, recipe, "recipe", bones, $"{id}_recipe");
                }
                else
                {
                    standard = UnityEngine.Object.Instantiate(trailer);
                    standard.name = id;
                }
                Write(standard, AssetPath(id, MonsterLook.Default));
                Debug.Log($"MonsterBaker: {id}: vendor {Describe(trailer)}; default from the {standard.Source}" +
                          (recipe != null ? $" {Describe(standard)}" : "") +
                          $"; {bake.Entries.Count} bones, {bake.Height} frames, in {(DateTime.UtcNow - started).TotalSeconds:F1} s");
            }
            File.WriteAllText(keyPath, key + "\n");
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single); // leave no modified scene behind
            AssetDatabase.SaveAssets();
            return true;
        }

        static string Describe(MonsterType t) =>
            $"LOD triangles {string.Join("/", t.Lods.Select(m => m.GetIndexCount(0) / 3))}, {t.Shards.Length} shards, height {t.HeightM:F2} m";

        /// <summary>
        /// Writes the type's combined vendor mesh to TYPE/source/ for its recipe (tools/src/champions_tools/monster.py
        /// reads it): source.json (the type, its bones' names and paths, each part's vertex range) and little-endian
        /// arrays: positions.f32 and normals.f32 (3 per vertex, root space, metres at the type's height, its rest pose),
        /// triangles.i32 (3 per triangle), bones.i32 and weights.f32 (4 per vertex, into source.json's bones), carried.u8
        /// (1 for a vertex of a weapon or shield).
        /// </summary>
        public static void Export(string id)
        {
            var model = MonsterModel.Of(id);
            if (!VendorPresent(model, out string missing)) throw new Exception($"MonsterBaker: {id}: no vendor asset {missing} (run `champions setup`)");
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var dir = SourceDir(id);
            Directory.CreateDirectory(dir);
            var bake = new TypeBake { Model = model, Root = model.Spawn() };
            try
            {
                bake.Animator = bake.Root.GetComponentInChildren<Animator>();
                var g = Combine(bake);
                Floats(Path.Combine(dir, "positions.f32"), g.Positions.SelectMany(v => new[] { v.x, v.y, v.z }));
                Floats(Path.Combine(dir, "normals.f32"), g.Normals.SelectMany(v => new[] { v.x, v.y, v.z }));
                Ints(Path.Combine(dir, "triangles.i32"), g.Indices);
                Ints(Path.Combine(dir, "bones.i32"), g.BoneIndices.SelectMany(v => new[] { (int)v.x, (int)v.y, (int)v.z, (int)v.w }));
                Floats(Path.Combine(dir, "weights.f32"), g.BoneWeights.SelectMany(v => new[] { v.x, v.y, v.z, v.w }));
                File.WriteAllBytes(Path.Combine(dir, "carried.u8"), g.Carried.Select(c => (byte)(c ? 1 : 0)).ToArray());
                var json = new StringBuilder();
                json.Append("{\n  \"id\": \"").Append(id).Append("\",\n  \"heightM\": ").Append(Num(model.heightM))
                    .Append(",\n  \"vertices\": ").Append(g.Positions.Count).Append(",\n  \"triangles\": ").Append(g.Indices.Count / 3)
                    .Append(",\n  \"bones\": [\n    ")
                    .Append(string.Join(",\n    ", bake.Entries.Select(e => $"{{\"name\": {Quote(e.Bone.name)}, \"path\": {Quote(PathTo(bake.Root.transform, e.Bone))}}}")))
                    .Append("\n  ],\n  \"parts\": [\n    ")
                    .Append(string.Join(",\n    ", g.Parts.Select(p => $"{{\"name\": {Quote(p.Name)}, \"first\": {p.First}, \"count\": {p.Count}, \"rigid\": {(p.Rigid ? "true" : "false")}, \"carried\": {(p.Carried ? "true" : "false")}}}")))
                    .Append("\n  ]\n}\n");
                File.WriteAllText(Path.Combine(dir, "source.json"), json.ToString());
                Debug.Log($"MonsterBaker: {id}: exported {g.Positions.Count} vertices, {g.Indices.Count / 3} triangles, {bake.Entries.Count} bones to {dir}");
            }
            finally
            {
                bake.Dispose();
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            }
        }

        static bool VendorPresent(MonsterModel model, out string missing)
        {
            foreach (var guid in model.clips.Select(c => c.guid).Prepend(model.prefab).Concat(model.weapons.Select(w => w.prefab)))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(path) || !File.Exists(path))
                {
                    missing = guid;
                    return false;
                }
            }
            missing = null;
            return true;
        }

        /// <summary>The bake's key: the type's entry, the baker's code and the recipe's mesh (if any).</summary>
        static string Key(MonsterModel model)
        {
            using var sha = SHA256.Create();
            void Add(byte[] b) => sha.TransformBlock(b, 0, b.Length, null, 0);
            Add(Encoding.UTF8.GetBytes(model.Json()));
            var files = CodeInputs.SelectMany(p => Directory.Exists(p) ? Directory.GetFiles(p, "*", SearchOption.AllDirectories) : new[] { p })
                .Concat(Directory.Exists(RecipeDir(model.id)) ? Directory.GetFiles(RecipeDir(model.id)) : Array.Empty<string>())
                .Where(f => !f.EndsWith(".meta", StringComparison.Ordinal) && File.Exists(f))
                .Select(f => f.Replace('\\', '/')).OrderBy(f => f, StringComparer.Ordinal);
            foreach (var f in files)
            {
                Add(Encoding.UTF8.GetBytes(f));
                Add(File.ReadAllBytes(f));
            }
            sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
            return string.Concat(sha.Hash.Take(8).Select(b => b.ToString("x2")));
        }

        // ---------------------------------------------------------------------------------
        // Baking one type in memory.

        static TypeBake Spawn(MonsterModel model, out Geometry vendor)
        {
            var b = new TypeBake { Model = model, Root = model.Spawn() };
            b.Animator = b.Root.GetComponentInChildren<Animator>();
            vendor = Combine(b);
            AnimationMode.StartAnimationMode();
            try
            {
                SampleClips(b);
            }
            finally
            {
                AnimationMode.StopAnimationMode();
            }
            return b;
        }

        /// <summary>The root's rotation and translation (not its scale: the mesh is at the type's height).</summary>
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

        /// <summary>
        /// Every active renderer's mesh in root space at the reference pose, each vertex with its 4 strongest bone
        /// entries: a skin's by its bones (each bind pose absorbing the move into root space, so skinning is unchanged
        /// exactly), a rigid attachment (a held weapon, a shield on a bone) by its own transform.
        /// </summary>
        static Geometry Combine(TypeBake b)
        {
            var g = new Geometry();
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
                bool carried = rigid || Array.IndexOf(b.Model.carried, r.name) >= 0;
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

                int baseVertex = g.Positions.Count;
                var verts = mesh.vertices;
                var normals = mesh.normals;
                var perVertex = rigid ? default : mesh.GetBonesPerVertex();
                var weights = rigid ? default : mesh.GetAllBoneWeights();
                int w0 = 0;
                for (int v = 0; v < verts.Length; v++)
                {
                    g.Positions.Add(toRoot.MultiplyPoint3x4(verts[v]));
                    g.Normals.Add(normals.Length > v ? toRoot.MultiplyVector(normals[v]).normalized : Vector3.up);
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
                    g.BoneIndices.Add(idx);
                    g.BoneWeights.Add(wt);
                    g.Carried.Add(carried);
                }
                for (int s = 0; s < mesh.subMeshCount; s++)
                    foreach (var i in mesh.GetTriangles(s)) g.Indices.Add(baseVertex + i);
                g.Parts.Add((r.name, baseVertex, verts.Length, rigid, carried));
            }
            if (g.Positions.Count == 0) throw new Exception($"MonsterBaker: {b.Model.id} has no active renderers");
            foreach (var name in b.Model.carried)
                if (!g.Parts.Exists(p => p.Name == name)) throw new Exception($"MonsterBaker: {b.Model.id} carries '{name}', which it has no renderer for");
            return g;
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
            var animationClips = new List<AnimationClip>();
            foreach (var r in b.Model.clips)
            {
                var clip = b.Model.LoadClip(r);
                var c = new MonsterClip
                {
                    Role = r.role, Name = clip.name, FirstRow = row, Length = clip.length, Frames = MonsterClip.FrameCount(clip.length),
                    Loop = r.loop, Impact = r.impact, End = r.end, StrideM = r.strideM, LiftM = r.liftM,
                };
                b.Clips.Add(c);
                animationClips.Add(clip);
                row += c.Frames;
            }
            b.Width = b.Entries.Count * 3;
            b.Height = row;
            if (b.Width > 16384 || b.Height > 16384) throw new Exception($"MonsterBaker: {b.Model.id} bone texture {b.Width}x{b.Height} is too large");
            var body = BodyBone(b);
            b.Texels = new float[b.Width * b.Height * 4];
            for (int ci = 0; ci < b.Clips.Count; ci++)
            {
                var c = b.Clips[ci];
                var clip = animationClips[ci];
                // A looping clip that travels would jump at its seam: its horizontal drift is removed linearly (the sim
                // moves the monster instead).
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
                    var rootInv = Matrix4x4.Translate(Vector3.up * c.LiftM - drift * (time / c.Length)) * RootTR(b).inverse;
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
        /// Yaw-invariant bounding spheres around the vertical axis, one per clip (the culling pass takes the larger of a
        /// monster's two clips) and one over every clip; every second vertex of every second frame, fully skinned, with a
        /// 5% margin.
        /// </summary>
        static (MonsterClip[] clips, float centerY, float radius, float height) MeasureBounds(TypeBake b, Geometry g)
        {
            var clips = b.Clips.ToArray();
            var all = new List<Vector3>();
            var mats = new Matrix4x4[b.Entries.Count];
            for (int ci = 0; ci < clips.Length; ci++)
            {
                var pts = new List<Vector3>();
                for (int f = 0; f < clips[ci].Frames; f += f + 2 < clips[ci].Frames ? 2 : 1)
                {
                    for (int e = 0; e < mats.Length; e++) mats[e] = b.Matrix(e, clips[ci].FirstRow + f);
                    for (int v = 0; v < g.Positions.Count; v += 2) pts.Add(Skin(g, mats, v));
                    if (f == clips[ci].Frames - 1) break;
                }
                Sphere(pts, out clips[ci].CenterY, out clips[ci].Radius, out clips[ci].HeightM);
                all.AddRange(pts);
            }
            Sphere(all, out float centerY, out float radius, out _);
            // The standing height: the idle's (else the first clip's), not a flight's or a leap's.
            int idle = Array.FindIndex(clips, c => c.Role == "idle");
            return (clips, centerY, radius, clips[Math.Max(0, idle)].HeightM);
        }

        static Vector3 Skin(Geometry g, Matrix4x4[] mats, int v)
        {
            var p = Vector3.zero;
            var bi = g.BoneIndices[v];
            var bw = g.BoneWeights[v];
            for (int k = 0; k < 4; k++)
                if (bw[k] > 0) p += bw[k] * mats[(int)bi[k]].MultiplyPoint3x4(g.Positions[v]);
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
        // A recipe's mesh.

        /// <summary>
        /// The recipe's mesh from TYPE/recipe/ (tools/src/champions_tools/monster.py writes it): mesh.json (vertices,
        /// triangles) and little-endian arrays positions.f32 (3 per vertex, root space at the type's height, the rest
        /// pose), triangles.i32, bones.i32 and weights.f32 (4 per vertex, into the source's bones), and normals.f32 if the
        /// recipe gave them (else they're computed), carried.u8 (1 for a vertex of a weapon or shield, which the
        /// shatter flings with the body) and edges.u8 (3 per triangle: 1 to draw the edge opposite that corner) if it
        /// gave them, and mesh.json's edgeAngleDeg (the creases that draw without flags); null when there is none.
        /// </summary>
        static Geometry ReadRecipe(string id, int bones)
        {
            var dir = RecipeDir(id);
            if (!File.Exists(Path.Combine(dir, "mesh.json"))) return null;
            var positions = ReadFloats(Path.Combine(dir, "positions.f32"));
            var tris = ReadInts(Path.Combine(dir, "triangles.i32"));
            var boneIdx = ReadInts(Path.Combine(dir, "bones.i32"));
            var boneWt = ReadFloats(Path.Combine(dir, "weights.f32"));
            int n = positions.Length / 3;
            if (boneIdx.Length != 4 * n || boneWt.Length != 4 * n || tris.Length % 3 != 0 || tris.Any(i => i < 0 || i >= n) || boneIdx.Any(i => i < 0 || i >= bones))
                throw new Exception($"MonsterBaker: {dir}'s arrays disagree ({n} vertices, {tris.Length} indices, {boneIdx.Length} bones, {boneWt.Length} weights, {bones} source bones)");
            var g = new Geometry();
            for (int v = 0; v < n; v++)
            {
                g.Positions.Add(new Vector3(positions[3 * v], positions[3 * v + 1], positions[3 * v + 2]));
                g.BoneIndices.Add(new Vector4(boneIdx[4 * v], boneIdx[4 * v + 1], boneIdx[4 * v + 2], boneIdx[4 * v + 3]));
                g.BoneWeights.Add(new Vector4(boneWt[4 * v], boneWt[4 * v + 1], boneWt[4 * v + 2], boneWt[4 * v + 3]));
            }
            g.Indices.AddRange(tris);
            var carriedPath = Path.Combine(dir, "carried.u8");
            var carried = File.Exists(carriedPath) ? File.ReadAllBytes(carriedPath) : new byte[n];
            if (carried.Length != n) throw new Exception($"MonsterBaker: {carriedPath} has {carried.Length} entries for {n} vertices");
            g.Carried.AddRange(carried.Select(c => c != 0));
            var edgesPath = Path.Combine(dir, "edges.u8");
            if (File.Exists(edgesPath))
            {
                var e = File.ReadAllBytes(edgesPath);
                if (e.Length != tris.Length) throw new Exception($"MonsterBaker: {edgesPath} has {e.Length} flags for {tris.Length / 3} triangles");
                g.Edges = new Vector3[tris.Length / 3];
                for (int t = 0; t < g.Edges.Length; t++) g.Edges[t] = new Vector3(e[3 * t], e[3 * t + 1], e[3 * t + 2]);
            }
            var meta = JsonUtility.FromJson<RecipeMeta>(File.ReadAllText(Path.Combine(dir, "mesh.json")));
            if (meta.edgeAngleDeg > 0) g.EdgeAngleDeg = meta.edgeAngleDeg;
            var normalsPath = Path.Combine(dir, "normals.f32");
            if (File.Exists(normalsPath))
            {
                var normals = ReadFloats(normalsPath);
                for (int v = 0; v < n; v++) g.Normals.Add(new Vector3(normals[3 * v], normals[3 * v + 1], normals[3 * v + 2]).normalized);
            }
            else
            {
                var m = new Mesh { indexFormat = n > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
                m.SetVertices(g.Positions);
                m.SetTriangles(tris, 0);
                m.RecalculateNormals();
                g.Normals.AddRange(m.normals);
                UnityEngine.Object.DestroyImmediate(m);
            }
            return g;
        }

        [Serializable]
        sealed class RecipeMeta
        {
            public float edgeAngleDeg;
        }

        // ---------------------------------------------------------------------------------
        // Saving.

        static Texture2D SaveBones(TypeBake b)
        {
            var id = b.Model.id;
            var tex = new Texture2D(b.Width, b.Height, TextureFormat.RGBAFloat, false, true)
            {
                name = id + "_bones", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, anisoLevel = 0,
            };
            tex.SetPixelData(b.Texels, 0);
            tex.Apply(false, false); // kept readable: the asset is saved from its CPU copy
            Replace(tex, $"{TypeDir(id)}/{id}_bones.asset");
            return tex;
        }

        /// <summary>One look's type from a combined mesh: its LODs as style meshes, its shards, its bounds.</summary>
        static MonsterType Save(TypeBake b, Geometry g, string source, Texture2D bones, string prefix)
        {
            var id = b.Model.id;
            var (clips, centerY, radius, height) = MeasureBounds(b, g);
            var welded = g.Build(prefix + "_welded", centerY, radius);
            int seed = StyleMesh.StableSeed(id);
            Mesh[] lods;
            if (g.Designed)
            {
                // Its edges are its own: decimated, they would be other edges. Every LOD is the mesh itself.
                var style = StyleMesh.Unweld(welded, seed, g.Edges, g.EdgeAngleDeg);
                style.name = $"{prefix}_lod0";
                Replace(style, $"{TypeDir(id)}/{prefix}_lod0.mesh");
                lods = new[] { style, style, style };
            }
            else
            {
                var decimated = BuildLods(welded);
                lods = new[] { StyleMesh.Unweld(welded, seed), StyleMesh.Unweld(decimated[0], seed + 1), StyleMesh.Unweld(decimated[1], seed + 2) };
                for (int l = 0; l < lods.Length; l++)
                {
                    lods[l].name = $"{prefix}_lod{l}";
                    Replace(lods[l], $"{TypeDir(id)}/{prefix}_lod{l}.mesh");
                }
                foreach (var m in decimated) UnityEngine.Object.DestroyImmediate(m);
            }
            UnityEngine.Object.DestroyImmediate(welded);
            var (shardMesh, shards) = MonsterShardBaker.Bake(prefix, height, lods[0], seed, g.CarriedTriangles());
            Replace(shardMesh, $"{TypeDir(id)}/{prefix}_shards.mesh");
            var t = ScriptableObject.CreateInstance<MonsterType>();
            t.name = id;
            t.Id = id;
            t.Source = source;
            t.Lods = lods;
            t.Bones = bones;
            t.BoneCount = b.Entries.Count;
            t.Clips = clips;
            t.CenterY = centerY;
            t.Radius = radius;
            t.HeightM = height;
            t.HoverM = b.Model.hoverM;
            t.ShardMesh = shardMesh;
            t.Shards = shards;
            return t;
        }

        /// <summary>
        /// LOD1 and LOD2 of the welded mesh: Unity's own Mesh LOD generation (MeshLodUtility, which simplifies the index
        /// buffer by about half per level and keeps every vertex and stream), run on a copy; each level's index range
        /// becomes its own mesh over the same vertices.
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

        static void Write(MonsterType t, string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            Replace(t, path);
        }

        static void Replace(UnityEngine.Object asset, string path)
        {
            AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(asset, path);
        }

        // ---------------------------------------------------------------------------------
        // The recipes' arrays.

        static void Floats(string path, IEnumerable<float> values)
        {
            using var w = new BinaryWriter(File.Create(path));
            foreach (var v in values) w.Write(v);
        }

        static void Ints(string path, IEnumerable<int> values)
        {
            using var w = new BinaryWriter(File.Create(path));
            foreach (var v in values) w.Write(v);
        }

        static float[] ReadFloats(string path)
        {
            var bytes = File.ReadAllBytes(path);
            var o = new float[bytes.Length / 4];
            Buffer.BlockCopy(bytes, 0, o, 0, o.Length * 4);
            return o;
        }

        static int[] ReadInts(string path)
        {
            var bytes = File.ReadAllBytes(path);
            var o = new int[bytes.Length / 4];
            Buffer.BlockCopy(bytes, 0, o, 0, o.Length * 4);
            return o;
        }

        static string PathTo(Transform root, Transform t)
        {
            var parts = new List<string>();
            for (var x = t; x != null && x != root; x = x.parent) parts.Add(x.name);
            parts.Reverse();
            return string.Join("/", parts);
        }

        static string Quote(string s) => "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

        static string Num(float v) => v.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
    }
}
