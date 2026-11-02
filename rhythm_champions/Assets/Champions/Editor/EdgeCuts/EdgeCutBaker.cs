using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Champions.Render.Crowd;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Champions.EditorTools.EdgeCuts
{
    /// <summary>
    /// Bakes a model's cut (the recipe's cut.json/.bytes) into the edge crowd's assets under the ignored
    /// Assets/Baked/EdgeCuts/&lt;model&gt;/ (plan.md, Rendering &gt; Edge cuts, step 6): the body and line meshes, a bone
    /// texture of every clip sampled at 30 Hz onto the entries the cut uses (game/'s crowd baker's sampling, ported),
    /// each clip's bounds, the shard pieces, and the <see cref="EdgeCutType"/> asset; then the library in
    /// Resources that lists every baked type with the crowd's shaders.
    /// </summary>
    public static class EdgeCutBaker
    {
        public const string LibraryPath = EdgeCutModel.BakedRoot + "/Resources/" + EdgeCutLibrary.ResourcePath + ".asset";
        const string ShaderDir = "Assets/Champions/Render/Crowd/Shaders";

        public static EdgeCutType Bake(EdgeCutModel model)
        {
            string dir = EdgeCutModel.Dir(model.id);
            var cut = ArrayFile.Read($"{dir}/cut.json");
            var sourceNames = JsonUtility.FromJson<SourceEntries>(File.ReadAllText($"{dir}/source.json")).entries.Select(e => e.name).ToArray();
            var root = model.Spawn();
            try
            {
                var source = new EdgeCutSource(root);
                if (!source.Entries.Select(e => e.Bone.name).SequenceEqual(sourceNames))
                    throw new Exception($"EdgeCutBaker: {model.id}'s rig no longer matches its export; run `champions edgecut {model.id} --force`");

                var bodyBones = cut.Ints("body_bones");
                var lineBones = cut.Ints("line_bones");
                var pieceEntries = cut.Ints("piece_entries");
                var bodyWeights = cut.Floats("body_weights");
                var lineWeights = cut.Floats("line_weights");
                // The entries the cut uses, in the export's order: the bone texture holds only these.
                var used = new SortedSet<int>(pieceEntries);
                for (int i = 0; i < bodyBones.Length; i++) if (bodyWeights[i] > 0) used.Add(bodyBones[i]);
                for (int i = 0; i < lineBones.Length; i++) if (lineWeights[i] > 0) used.Add(lineBones[i]);
                var entries = used.Select(e => source.Entries[e]).ToList();
                var remap = new Dictionary<int, int>();
                foreach (var e in used) remap[e] = remap.Count;
                int Remap(int e, float w) => w > 0 ? remap[e] : 0;

                var body = BodyMesh(model.id, cut, Remap);
                var lines = LineMesh(model.id, cut, Remap);
                var pivots = cut.Floats("piece_pivots");
                var sizes = cut.Floats("piece_sizes");
                var pieces = new EdgeShardPiece[sizes.Length];
                for (int i = 0; i < pieces.Length; i++)
                    pieces[i] = new EdgeShardPiece
                    {
                        Pivot = new Vector3(pivots[3 * i], pivots[3 * i + 1], pivots[3 * i + 2]),
                        Size = sizes[i],
                        Bones = new Vector4(remap[pieceEntries[i]], 0, 0, 0),
                        Weights = new Vector4(1, 0, 0, 0),
                    };

                var bake = new ClipBake(model, source, entries);
                bake.Sample();
                bake.MeasureBounds(Skinned(cut));

                Directory.CreateDirectory(dir);
                var bones = bake.Texture(model.id);
                Replace(body, $"{dir}/{model.id}_body.mesh");
                Replace(lines, $"{dir}/{model.id}_lines.mesh");
                Replace(bones, $"{dir}/{model.id}_bones.asset");
                var type = ScriptableObject.CreateInstance<EdgeCutType>();
                type.Id = model.id;
                type.Body = body;
                type.Lines = lines;
                type.Bones = bones;
                type.BoneCount = entries.Count;
                type.Clips = bake.Clips.ToArray();
                type.CenterY = bake.CenterY;
                type.Radius = bake.Radius;
                type.HeightM = model.heightM;
                type.HoverM = model.hoverM;
                type.Pieces = pieces;
                type.Triangles = cut.Info("body_triangles").shape[0];
                type.Segments = cut.Info("line_segments").shape[0];
                Replace(type, $"{dir}/{model.id}.asset");
                WriteLibrary();
                Debug.Log($"EdgeCutBaker: {model.id}: {type.Triangles} body triangles, {type.Segments} line segments, {pieces.Length} pieces, " +
                          $"{entries.Count} of {source.Entries.Count} entries, {bake.Rows} frames ({entries.Count * 3}x{bake.Rows} texels), " +
                          $"reach {bake.HeightM:F2} m high, radius {type.Radius:F2} m");
                return type;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Serializable]
        sealed class SourceEntries
        {
            [Serializable]
            public sealed class Entry { public string name; }
            public Entry[] entries = Array.Empty<Entry>();
        }

        static Vector3 V3(float[] a, int i) => new Vector3(a[3 * i], a[3 * i + 1], a[3 * i + 2]);

        static Vector4 Bones4(int[] b, float[] w, int i, Func<int, float, int> remap) =>
            new Vector4(remap(b[4 * i], w[4 * i]), remap(b[4 * i + 1], w[4 * i + 1]), remap(b[4 * i + 2], w[4 * i + 2]), remap(b[4 * i + 3], w[4 * i + 3]));

        static Vector4 W4(float[] w, int i) => new Vector4(w[4 * i], w[4 * i + 1], w[4 * i + 2], w[4 * i + 3]);

        static Mesh BodyMesh(string id, ArrayFile cut, Func<int, float, int> remap)
        {
            var p = cut.Floats("body_positions");
            var b = cut.Ints("body_bones");
            var w = cut.Floats("body_weights");
            var piece = cut.Ints("body_piece");
            int n = piece.Length;
            var pos = new Vector3[n];
            var bones = new Vector4[n];
            var weights = new Vector4[n];
            var pieces = new Vector4[n];
            for (int i = 0; i < n; i++)
            {
                pos[i] = V3(p, i);
                bones[i] = Bones4(b, w, i, remap);
                weights[i] = W4(w, i);
                pieces[i] = new Vector4(piece[i], 0, 0, 0);
            }
            var m = new Mesh { name = id + "_body" };
            m.SetVertices(pos);
            m.SetUVs(1, bones);
            m.SetUVs(2, weights);
            m.SetUVs(3, pieces);
            m.SetTriangles(cut.Ints("body_triangles"), 0, false);
            m.RecalculateBounds();
            return m;
        }

        /// <summary>Each segment a quad: per corner its own end and the other, the side, both ends' skins, importance and piece.</summary>
        static Mesh LineMesh(string id, ArrayFile cut, Func<int, float, int> remap)
        {
            var p = cut.Floats("line_positions");
            var b = cut.Ints("line_bones");
            var w = cut.Floats("line_weights");
            var seg = cut.Ints("line_segments");
            var importance = cut.Floats("line_importance");
            var piece = cut.Ints("line_piece");
            int n = importance.Length;
            var pos = new Vector3[4 * n];
            var other = new Vector4[4 * n];
            var ownBones = new Vector4[4 * n];
            var ownWeights = new Vector4[4 * n];
            var otherBones = new Vector4[4 * n];
            var otherWeights = new Vector4[4 * n];
            var info = new Vector4[4 * n];
            var tris = new int[6 * n];
            for (int s = 0; s < n; s++)
            {
                int a = seg[2 * s], c = seg[2 * s + 1];
                for (int k = 0; k < 4; k++)
                {
                    int own = k < 2 ? a : c, far = k < 2 ? c : a;
                    int v = 4 * s + k;
                    // Corners 0, 1 at end a and 2, 3 at end c; the side flips with the end, since each end's
                    // shader sees the segment pointing the other way.
                    float side = (k == 0 || k == 3) ? -1 : 1;
                    pos[v] = V3(p, own);
                    var o = V3(p, far);
                    other[v] = new Vector4(o.x, o.y, o.z, side);
                    ownBones[v] = Bones4(b, w, own, remap);
                    ownWeights[v] = W4(w, own);
                    otherBones[v] = Bones4(b, w, far, remap);
                    otherWeights[v] = W4(w, far);
                    info[v] = new Vector4(importance[s], piece[s], 0, 0);
                }
                int q = 4 * s;
                tris[6 * s] = q; tris[6 * s + 1] = q + 1; tris[6 * s + 2] = q + 2;
                tris[6 * s + 3] = q; tris[6 * s + 4] = q + 2; tris[6 * s + 5] = q + 3;
            }
            var m = new Mesh { name = id + "_lines" };
            m.SetVertices(pos);
            m.SetUVs(0, other);
            m.SetUVs(1, ownBones);
            m.SetUVs(2, ownWeights);
            m.SetUVs(3, otherBones);
            m.SetUVs(4, otherWeights);
            m.SetUVs(5, info);
            m.SetTriangles(tris, 0, false);
            m.RecalculateBounds();
            return m;
        }

        /// <summary>Every body and line vertex with its skin (for the bounds), in the export's entry indices.</summary>
        static List<(Vector3 p, int[] b, float[] w)> Skinned(ArrayFile cut)
        {
            var o = new List<(Vector3, int[], float[])>();
            foreach (var (pk, bk, wk) in new[] { ("body_positions", "body_bones", "body_weights"), ("line_positions", "line_bones", "line_weights") })
            {
                var p = cut.Floats(pk);
                var b = cut.Ints(bk);
                var w = cut.Floats(wk);
                for (int i = 0; i < p.Length / 3; i++)
                    o.Add((V3(p, i), b.Skip(4 * i).Take(4).ToArray(), w.Skip(4 * i).Take(4).ToArray()));
            }
            return o;
        }

        /// <summary>Lists every baked type in the library asset, with the crowd's shaders.</summary>
        public static void WriteLibrary()
        {
            var types = AssetDatabase.FindAssets("t:" + nameof(EdgeCutType), new[] { EdgeCutModel.BakedRoot })
                .Select(g => AssetDatabase.LoadAssetAtPath<EdgeCutType>(AssetDatabase.GUIDToAssetPath(g)))
                .Where(t => t != null).OrderBy(t => t.Id, StringComparer.Ordinal).ToArray();
            var lib = ScriptableObject.CreateInstance<EdgeCutLibrary>();
            lib.Types = types;
            lib.Body = Load<Shader>($"{ShaderDir}/EdgeBody.shader");
            lib.Lines = Load<Shader>($"{ShaderDir}/EdgeLines.shader");
            lib.Cull = Load<ComputeShader>($"{ShaderDir}/CrowdCull.compute");
            lib.Palette = Load<ComputeShader>($"{ShaderDir}/CrowdPalette.compute");
            lib.Shards = Load<ComputeShader>($"{ShaderDir}/EdgeShards.compute");
            Directory.CreateDirectory(Path.GetDirectoryName(LibraryPath));
            Replace(lib, LibraryPath);
            AssetDatabase.SaveAssets();
        }

        static T Load<T>(string path) where T : UnityEngine.Object =>
            AssetDatabase.LoadAssetAtPath<T>(path) ?? throw new Exception("EdgeCutBaker: no " + path);

        static void Replace(UnityEngine.Object asset, string path)
        {
            AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(asset, path);
        }

        /// <summary>The clips sampled onto the cut's entries (game/'s crowd baker's sampling, ported).</summary>
        sealed class ClipBake
        {
            readonly EdgeCutModel _model;
            readonly EdgeCutSource _source;
            readonly List<EdgeCutSource.Entry> _entries;
            readonly Animator _animator;
            readonly List<AnimationClip> _clips = new List<AnimationClip>();
            public readonly List<EdgeClip> Clips = new List<EdgeClip>();
            float[] _texels; // RGBAFloat, width = 3 x entries, height = total frames
            int _width;
            public int Rows;
            public float CenterY, Radius, HeightM;

            public ClipBake(EdgeCutModel model, EdgeCutSource source, List<EdgeCutSource.Entry> entries)
            {
                _model = model;
                _source = source;
                _entries = entries;
                _animator = source.Root.GetComponentInChildren<Animator>();
            }

            void Pose(AnimationClip clip, float time)
            {
                AnimationMode.BeginSampling();
                AnimationMode.SampleAnimationClip(_animator.gameObject, clip, time);
                AnimationMode.EndSampling();
            }

            /// <summary>The skeleton's root: the hips on a humanoid, else the largest skin's root bone.</summary>
            Transform BodyBone()
            {
                if (_animator.isHuman) return _animator.GetBoneTransform(HumanBodyBones.Hips);
                var smr = _source.Root.GetComponentsInChildren<SkinnedMeshRenderer>(false)
                    .Where(r => r.rootBone != null && r.sharedMesh != null)
                    .OrderByDescending(r => r.sharedMesh.vertexCount).FirstOrDefault();
                return smr != null ? smr.rootBone : _animator.transform;
            }

            Vector3 BodyAt(AnimationClip clip, float time, Transform body)
            {
                Pose(clip, time);
                return _source.RootTR.inverse.MultiplyPoint3x4(body.position);
            }

            public void Sample()
            {
                AnimationMode.StartAnimationMode();
                try
                {
                    int row = 0;
                    foreach (var c in _model.clips)
                    {
                        var clip = _model.LoadClip(c);
                        Clips.Add(new EdgeClip
                        {
                            Role = c.role, Name = clip.name, FirstRow = row, Length = clip.length,
                            Frames = EdgeClip.FrameCount(clip.length), Loop = c.loop, Impact = c.impact, End = c.end, StrideM = c.strideM,
                        });
                        _clips.Add(clip);
                        row += Clips[^1].Frames;
                    }
                    _width = _entries.Count * 3;
                    Rows = row;
                    if (_width > 16384 || Rows > 16384) throw new Exception($"EdgeCutBaker: {_model.id}'s bone texture {_width}x{Rows} is too large");
                    var body = BodyBone();
                    _texels = new float[_width * Rows * 4];
                    for (int ci = 0; ci < Clips.Count; ci++)
                    {
                        var c = Clips[ci];
                        var clip = _clips[ci];
                        // A looping clip that travels would jump at its seam: its horizontal drift is removed
                        // linearly (the sim moves the monster instead). A clip authored on the ground for the air is
                        // raised by its lift.
                        var drift = Vector3.zero;
                        var lift = Vector3.up * _model.clips[ci].liftM;
                        if (c.Loop)
                        {
                            drift = BodyAt(clip, c.Length, body) - BodyAt(clip, 0, body);
                            drift.y = 0;
                            if (drift.magnitude <= 0.05f) drift = Vector3.zero;
                        }
                        for (int f = 0; f < c.Frames; f++)
                        {
                            float time = c.FrameTime(f);
                            Pose(clip, time);
                            var rootInv = Matrix4x4.Translate(lift - drift * (time / c.Length)) * _source.RootTR.inverse;
                            for (int e = 0; e < _entries.Count; e++)
                            {
                                var m = rootInv * _entries[e].Bone.localToWorldMatrix * _entries[e].Bind;
                                for (int r = 0; r < 3; r++)
                                {
                                    int at = ((c.FirstRow + f) * _width + e * 3 + r) * 4;
                                    var v = m.GetRow(r);
                                    _texels[at] = v.x;
                                    _texels[at + 1] = v.y;
                                    _texels[at + 2] = v.z;
                                    _texels[at + 3] = v.w;
                                }
                            }
                        }
                    }
                }
                finally
                {
                    AnimationMode.StopAnimationMode();
                }
            }

            Matrix4x4 Matrix(int e, int row)
            {
                var m = Matrix4x4.identity;
                for (int r = 0; r < 3; r++)
                {
                    int at = (row * _width + e * 3 + r) * 4;
                    m.SetRow(r, new Vector4(_texels[at], _texels[at + 1], _texels[at + 2], _texels[at + 3]));
                }
                return m;
            }

            /// <summary>
            /// Yaw-invariant bounding spheres round the vertical axis, one per clip and one over every clip, from every
            /// cut vertex on every second frame, with a 5% margin. `points` use the export's entry indices.
            /// </summary>
            public void MeasureBounds(List<(Vector3 p, int[] b, float[] w)> points)
            {
                var index = new Dictionary<EdgeCutSource.Entry, int>();
                for (int e = 0; e < _entries.Count; e++) index[_entries[e]] = e;
                var all = new List<Vector3>();
                for (int ci = 0; ci < Clips.Count; ci++)
                {
                    var c = Clips[ci];
                    var pts = new List<Vector3>();
                    for (int f = 0; f < c.Frames; f += f + 2 < c.Frames ? 2 : 1)
                    {
                        var mats = new Matrix4x4[_entries.Count];
                        for (int e = 0; e < mats.Length; e++) mats[e] = Matrix(e, c.FirstRow + f);
                        foreach (var (p, b, w) in points)
                        {
                            var q = Vector3.zero;
                            for (int k = 0; k < 4; k++)
                                if (w[k] > 0) q += w[k] * mats[index[_source.Entries[b[k]]]].MultiplyPoint3x4(p);
                            pts.Add(q);
                        }
                        if (f == c.Frames - 1) break;
                    }
                    Sphere(pts, out c.CenterY, out c.Radius, out c.HeightM);
                    Clips[ci] = c;
                    all.AddRange(pts);
                }
                Sphere(all, out CenterY, out Radius, out HeightM);
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
                foreach (var p in pts) r2 = Mathf.Max(r2, p.x * p.x + p.z * p.z + (p.y - centerY) * (p.y - centerY));
                radius = Mathf.Sqrt(r2) * 1.05f + 0.02f;
                height = Mathf.Max(0.05f, maxY);
            }

            public Texture2D Texture(string id)
            {
                var tex = new Texture2D(_width, Rows, TextureFormat.RGBAFloat, false, true)
                {
                    name = id + "_bones", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, anisoLevel = 0,
                };
                tex.SetPixelData(_texels, 0);
                tex.Apply(false, false);
                return tex;
            }
        }
    }
}
