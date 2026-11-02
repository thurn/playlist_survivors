using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Champions.EditorTools.Monsters;
using Champions.Render.Crowd;
using Champions.Render.Structures;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Champions.EditorTools.Structures
{
    /// <summary>
    /// The structure baker (design.md, The World &gt; Structures; plan.md, Rendering &gt; The edge look): a kind's Synty
    /// model (structures.json) made into the edge look's meshes the way the monsters' are, static. For each look it
    /// writes, only under the ignored Assets/Baked/Structures/:
    /// - KIND/LOOK_body.mesh: the model combined in the structure's own space (<see cref="StructureModel.Combine"/>), as
    ///   a style mesh (<see cref="StyleMesh.Unweld"/>, its creases over 24 degrees flagged, or over the default look's own
    ///   angle);
    /// - KIND/LOOK_shards.mesh: the body cut into closed chunks outlined along their cracks
    ///   (<see cref="MonsterShardBaker"/>), with each chunk's pivot and box for its PhysX body;
    /// - Resources/StructureTypes/LOOK/KIND.asset: the <see cref="StructureType"/>. `--look trailer`'s is always the model
    ///   unsimplified; the default's at the crease angle the kind's defaultLook gives it, else the trailer's own.
    /// A bake is skipped while its key (the kind's entry and the baker's code) matches KIND/bake.key.
    /// </summary>
    public static class StructureBaker
    {
        /// <summary>Where bakes go.</summary>
        public const string Root = "Assets/Baked/Structures";
        const string KeyFile = "bake.key";
        /// <summary>What a bake's key hashes beside the kind's entry: the code that bakes and the asset it writes.</summary>
        static readonly string[] CodeInputs =
        {
            "Assets/Champions/Editor/Structures", "Assets/Champions/Editor/Monsters/StyleMesh.cs",
            "Assets/Champions/Editor/Monsters/MonsterShardBaker.cs", "Assets/Champions/Render/Structures/StructureType.cs",
        };

        public static string KindDir(string id) => $"{Root}/{id}";
        public static string AssetPath(string id, MonsterLook look) => $"{Root}/Resources/{StructureLibrary.TypesPath(look)}/{id}.asset";

        /// <summary>Bakes every kind whose bake is out of date (all with <paramref name="force"/>); false if any failed for want of vendor files.</summary>
        public static bool BakeAll(bool force = false)
        {
            bool ok = true;
            foreach (var model in StructureModel.All()) ok &= Bake(model.id, force);
            return ok;
        }

        /// <summary>Bakes one kind for both looks unless its key is current (always with <paramref name="force"/>); false, with nothing written, when its prefab is missing.</summary>
        public static bool Bake(string id, bool force = false)
        {
            var model = StructureModel.Of(id);
            if (!model.VendorPresent())
            {
                Debug.LogWarning($"StructureBaker: {id}: no vendor prefab {model.prefab} (run `champions setup`); not baked");
                return false;
            }
            string key = Key(model);
            string keyPath = $"{KindDir(id)}/{KeyFile}";
            if (!force && File.Exists(keyPath) && File.ReadAllText(keyPath).Trim() == key
                && File.Exists(AssetPath(id, MonsterLook.Trailer)) && File.Exists(AssetPath(id, MonsterLook.Default)))
            {
                Debug.Log($"StructureBaker: {id}: current ({key})");
                return true;
            }
            var started = DateTime.UtcNow;
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            Directory.CreateDirectory(KindDir(id));
            var root = model.Spawn();
            try
            {
                var trailer = Save(model, MonsterLook.Trailer, "vendor", model.Combine(root), StyleMesh.FeatureAngleDeg);
                Write(trailer, AssetPath(id, MonsterLook.Trailer));
                StructureType standard;
                if (model.defaultLook.Any)
                {
                    float angle = model.defaultLook.edgeAngleDeg > 0 ? model.defaultLook.edgeAngleDeg : StyleMesh.FeatureAngleDeg;
                    standard = Save(model, MonsterLook.Default, "tuned", model.Combine(root), angle);
                }
                else
                {
                    standard = UnityEngine.Object.Instantiate(trailer);
                    standard.name = id;
                }
                Write(standard, AssetPath(id, MonsterLook.Default));
                Debug.Log($"StructureBaker: {id}: vendor {Describe(trailer)}" + (model.defaultLook.Any ? $"; tuned {Describe(standard)}" : "") +
                          $", in {(DateTime.UtcNow - started).TotalSeconds:F1} s");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
            File.WriteAllText(keyPath, key + "\n");
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single); // leave no modified scene behind
            AssetDatabase.SaveAssets();
            return true;
        }

        static string Describe(StructureType t) =>
            $"{t.Triangles} triangles, {t.Lines} lines, {t.Pieces.Length} pieces, {t.ExtentM.x:F2} x {t.ExtentM.y:F2} m on the ground, {t.HeightM:F2} m tall";

        /// <summary>The bake's key: the kind's entry and the baker's code.</summary>
        static string Key(StructureModel model)
        {
            using var sha = SHA256.Create();
            void Add(byte[] b) => sha.TransformBlock(b, 0, b.Length, null, 0);
            Add(Encoding.UTF8.GetBytes(model.Json()));
            var files = CodeInputs.SelectMany(p => Directory.Exists(p) ? Directory.GetFiles(p, "*", SearchOption.AllDirectories) : new[] { p })
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

        /// <summary>One look's type from its welded mesh, its meshes saved under the kind's folder as LOOK_body and LOOK_shards.</summary>
        static StructureType Save(StructureModel model, MonsterLook forLook, string source, Mesh welded, float angleDeg)
        {
            string look = MonsterLooks.Name(forLook);
            var t = Build(model.id, source, welded, angleDeg, model.pieces);
            UnityEngine.Object.DestroyImmediate(welded);
            t.Body.name = $"{model.id}_{look}_body";
            t.ShardMesh.name = $"{model.id}_{look}_shards";
            Replace(t.Body, $"{KindDir(model.id)}/{look}_body.mesh");
            Replace(t.ShardMesh, $"{KindDir(model.id)}/{look}_shards.mesh");
            return t;
        }

        /// <summary>
        /// A kind's type from its welded mesh in the structure's own space (<see cref="StructureModel.Combine"/>): the
        /// body as a style mesh whose creases over <paramref name="angleDeg"/> draw, cut into <paramref name="pieces"/>
        /// chunks, each with its pivot and a box round it (a little over half of which is solid, as a chunk closed to an
        /// apex is), every edge of their inside faces drawn. Static: every corner rides one bone that never moves, as the
        /// shard baker expects.
        /// </summary>
        public static StructureType Build(string id, string source, Mesh welded, float angleDeg, int pieces)
        {
            int n = welded.vertexCount;
            var still = UnityEngine.Object.Instantiate(welded);
            still.SetUVs(1, Enumerable.Repeat(Vector4.zero, n).ToList());
            still.SetUVs(2, Enumerable.Repeat(new Vector4(1, 0, 0, 0), n).ToList());
            int seed = StyleMesh.StableSeed(id);
            var skinned = StyleMesh.Unweld(still, seed, null, angleDeg);
            UnityEngine.Object.DestroyImmediate(still);
            var carried = new bool[skinned.GetIndexCount(0) / 3];
            var (skinnedShards, shards) = MonsterShardBaker.Bake(id, welded.bounds.max.y, skinned, seed, carried, pieces);
            var body = Still(skinned);
            var shardMesh = Still(skinnedShards);
            UnityEngine.Object.DestroyImmediate(skinned);
            UnityEngine.Object.DestroyImmediate(skinnedShards);

            // A chunk's inside faces draw every edge, not only their crack (as a monster's do): over the fill they show,
            // and a chunk outlined all round reads as a closed solid flying, not a scrap of shell.
            var chunk = new List<Vector2>();
            shardMesh.GetUVs(4, chunk);
            var shardEdges = new List<Vector4>();
            shardMesh.GetUVs(5, shardEdges);
            for (int v = 0; v < chunk.Count; v++)
                if (chunk[v].x > 0.5f) shardEdges[v] = new Vector4(1, 1, 1, 0);
            shardMesh.SetUVs(5, shardEdges);
            var positions = shardMesh.vertices;
            var lo = Enumerable.Repeat(Vector3.positiveInfinity, shards.Length).ToArray();
            var hi = Enumerable.Repeat(Vector3.negativeInfinity, shards.Length).ToArray();
            for (int v = 0; v < positions.Length; v++)
            {
                int c = (int)chunk[v].y;
                lo[c] = Vector3.Min(lo[c], positions[v]);
                hi[c] = Vector3.Max(hi[c], positions[v]);
            }
            var bounds = body.bounds;
            var t = ScriptableObject.CreateInstance<StructureType>();
            t.name = id;
            t.Id = id;
            t.Source = source;
            t.Body = body;
            t.ShardMesh = shardMesh;
            t.Pieces = new StructurePiece[shards.Length];
            for (int i = 0; i < shards.Length; i++)
            {
                var pivot = shards[i].Pivot;
                var half = Vector3.Max(Vector3.Max(hi[i] - pivot, pivot - lo[i]), Vector3.one * 0.05f);
                t.Pieces[i] = new StructurePiece { Pivot = pivot, HalfSize = half, Volume = 0.55f * 8 * half.x * half.y * half.z };
            }
            t.HeightM = bounds.max.y;
            t.ExtentM = new Vector2(bounds.size.x, bounds.size.z);
            t.Triangles = (int)(body.GetIndexCount(0) / 3);
            t.Lines = CountLines(body);
            return t;
        }

        /// <summary>A copy of a style or shard mesh with only what the structure shaders read (positions, UV3 to UV5): a structure stands still, so its bones stay behind, and its flat fill reads no normals.</summary>
        static Mesh Still(Mesh skinned)
        {
            var m = new Mesh { name = skinned.name, indexFormat = skinned.indexFormat };
            m.SetVertices(skinned.vertices);
            for (int channel = 3; channel <= 5; channel++)
            {
                var uv = new List<Vector4>();
                skinned.GetUVs(channel, uv);
                if (uv.Count > 0) m.SetUVs(channel, uv);
            }
            m.SetTriangles(skinned.GetIndices(0), 0, true);
            return m;
        }

        /// <summary>The distinct edges a style mesh draws (an edge two triangles share and both flag counts once).</summary>
        static int CountLines(Mesh style)
        {
            var pos = style.vertices;
            var edges = new List<Vector3>();
            style.GetUVs(5, edges);
            var tris = style.GetIndices(0);
            var seen = new HashSet<((long, long, long), (long, long, long))>();
            (long, long, long) Key(Vector3 v) => ((long)Math.Round(v.x * 1e4), (long)Math.Round(v.y * 1e4), (long)Math.Round(v.z * 1e4));
            for (int t = 0; t + 2 < tris.Length; t += 3)
                for (int k = 0; k < 3; k++)
                {
                    if (edges[tris[t]][k] < 0.5f) continue;
                    var a = Key(pos[tris[t + (k + 1) % 3]]);
                    var b = Key(pos[tris[t + (k + 2) % 3]]);
                    seen.Add(a.CompareTo(b) < 0 ? (a, b) : (b, a));
                }
            return seen.Count;
        }

        static void Write(StructureType t, string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            Replace(t, path);
        }

        static void Replace(UnityEngine.Object asset, string path)
        {
            AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(asset, path);
        }
    }
}
