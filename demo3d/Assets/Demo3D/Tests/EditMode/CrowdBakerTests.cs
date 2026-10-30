using System.Collections.Generic;
using System.IO;
using System.Linq;
using Demo3D.EditorTools.Assets;
using Demo3D.EditorTools.Crowd;
using Demo3D.Runtime.Crowd;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Demo3D.Tests
{
    /// <summary>TD5's bone-texture baker: texture layout, clip table and the baked matrices.</summary>
    public class CrowdBakerTests
    {
        static bool VendorSynced => Directory.Exists("Assets/Vendor") && Directory.EnumerateFileSystemEntries("Assets/Vendor").Any();

        // Baking spawns into the open scene: leave a clean one, or the next test run's
        // save-the-scene prompt blocks a batch-mode editor.
        [TearDown]
        public void CleanScene() => UnityEditor.SceneManagement.EditorSceneManager.NewScene(
            UnityEditor.SceneManagement.NewSceneSetup.EmptyScene, UnityEditor.SceneManagement.NewSceneMode.Single);

        static CrowdBaker.TypeBake Bake(string id)
        {
            if (!VendorSynced) Assert.Ignore("the vendor folder is empty: run demo3d setup");
            var t = Roster.Load().Single(x => x.id == id);
            return CrowdBaker.BakeType(t, CrowdBaker.LoadAnnotations());
        }

        [Test]
        public void TheBoneTextureHoldsThreeRowsPerBoneAndEveryFrameOfEveryClip()
        {
            using var b = Bake("slime");
            Assert.That(b.Width, Is.EqualTo(b.Entries.Count * 3));
            Assert.That(b.Height, Is.EqualTo(b.Clips.Sum(c => c.Frames)));
            Assert.That(b.Texels.Length, Is.EqualTo(b.Width * b.Height * 4));
            int row = 0;
            foreach (var c in b.Clips)
            {
                Assert.That(c.FirstRow, Is.EqualTo(row), c.Role);
                Assert.That(c.Frames, Is.EqualTo(CrowdClip.FrameCount(c.Length)), c.Role);
                // 60 Hz with both ends: the last frame is the clip's end, at most 1/60 s after the one before.
                Assert.That(c.FrameTime(c.Frames - 1), Is.EqualTo(c.Length).Within(1e-5f), c.Role);
                Assert.That(c.Length - c.FrameTime(c.Frames - 2), Is.InRange(0f, 1f / 60f + 1e-5f), c.Role);
                row += c.Frames;
            }
            // Every combined vertex's bones exist and its weights sum to 1.
            for (int v = 0; v < b.Positions.Count; v++)
            {
                var w = b.BoneWeights[v];
                Assert.That(w.x + w.y + w.z + w.w, Is.EqualTo(1f).Within(1e-4f));
                for (int k = 0; k < 4; k++)
                    if (w[k] > 0) Assert.That((int)b.BoneIndices[v][k], Is.InRange(0, b.Entries.Count - 1));
            }
        }

        [Test]
        public void TheClipTableFollowsTheRosterAndTheAnnotations()
        {
            using var b = Bake("bigork");
            var t = Roster.Load().Single(x => x.id == "bigork");
            var annotations = (JArray)CrowdBaker.LoadAnnotations()["annotations"];
            Assert.That(b.Clips.Select(c => c.Role), Is.EqualTo(t.clips.Keys.ToArray()), "roster order: MonsterInstance.ClipA indexes it");
            for (int i = 0; i < b.Clips.Count; i++)
            {
                var c = b.Clips[i];
                var a = annotations.Single(x => (string)x["type"] == "bigork" && (string)x["role"] == c.Role);
                Assert.That(c.Length, Is.EqualTo(b.AnimationClips[i].length).Within(1e-5f), c.Role);
                Assert.That(c.Loop, Is.EqualTo((bool)a["loop"]), c.Role);
                if (a["impact"] != null) Assert.That(c.Impact, Is.EqualTo((float)a["impact"]).Within(1e-5f), c.Role);
                Assert.That(c.Radius, Is.GreaterThan(0.3f), c.Role);
                Assert.That(c.HeightM, Is.InRange(1f, 5f), c.Role);
            }
            // The weapon rides the right hand as a rigid attachment: one entry with weight 1.
            Assert.That(b.Entries.Any(e => e.Bone.name.Contains("SM_Wep_Slayer")), "the Slayer weapon is in the combined mesh");
        }

        /// <summary>
        /// A sampled frame: the texture's matrices equal bone world × bind pose at that pose, and
        /// the combined mesh skinned by them lands where Unity's own skinning puts the source
        /// renderer's vertices (SkinnedMeshRenderer.BakeMesh), for a multi-skin MPG rig and a
        /// humanoid Synty rig with a weapon.
        /// </summary>
        [TestCase("skeleton", "attack", 37)]
        [TestCase("bigork", "attack", 23)]
        public void ABakedFrameMatchesTheRigAndUnitysSkinning(string id, string role, int frame)
        {
            using var b = Bake(id);
            int ci = b.Clips.FindIndex(c => c.Role == role);
            var clip = b.Clips[ci];
            Assume.That(clip.DriftRemovedM, Is.EqualTo(0f), "a clip without drift removal");
            frame = Mathf.Min(frame, clip.Frames - 1);
            int row = clip.FirstRow + frame;
            var mats = Enumerable.Range(0, b.Entries.Count).Select(e => b.Matrix(e, row)).ToArray();
            AnimationMode.StartAnimationMode();
            try
            {
                CrowdBaker.Pose(b, b.AnimationClips[ci], clip.FrameTime(frame));
                var rootInv = CrowdBaker.RootTR(b).inverse;
                for (int e = 0; e < b.Entries.Count; e++)
                {
                    var expected = rootInv * b.Entries[e].Bone.localToWorldMatrix * b.Entries[e].Bind;
                    for (int k = 0; k < 12; k++)
                        Assert.That(mats[e][k % 3, k / 3], Is.EqualTo(expected[k % 3, k / 3]).Within(1e-4f), $"entry {e} element {k}");
                }
                var range = b.Ranges.First(x => x.Renderer is SkinnedMeshRenderer s && s.bones.Length > 1);
                var smr = (SkinnedMeshRenderer)range.Renderer;
                var baked = new Mesh();
                // With useScale false, BakeMesh gives world-scale offsets from the renderer's position
                // and rotation (measured: useScale true or the full localToWorld are off by the scale).
                smr.BakeMesh(baked, false);
                var unity = baked.vertices;
                var place = Matrix4x4.TRS(smr.transform.position, smr.transform.rotation, Vector3.one);
                float worst = 0;
                for (int v = 0; v < range.Count; v++)
                {
                    var ours = CrowdBaker.Skin(b, mats, range.First + v);
                    var theirs = rootInv.MultiplyPoint3x4(place.MultiplyPoint3x4(unity[v]));
                    worst = Mathf.Max(worst, (ours - theirs).magnitude);
                }
                Object.DestroyImmediate(baked);
                // These renderers have at most 4 influences per vertex; float error only.
                Assert.That(worst, Is.LessThan(0.001f), $"{smr.name}: worst vertex error {worst:F5} m");
            }
            finally
            {
                AnimationMode.StopAnimationMode();
            }
        }

        /// <summary>The bake's matrix at a clip time, interpolated as 60 Hz frames (both ends baked).</summary>
        static Matrix4x4 Sampled(CrowdBaker.TypeBake b, CrowdClip c, int bone, float t)
        {
            t = c.Loop ? (c.Length > 0 ? t - Mathf.Floor(t / c.Length) * c.Length : 0) : Mathf.Clamp(t, 0, c.Length);
            int f0 = Mathf.Min(Mathf.FloorToInt(t * 60f), c.Frames - 2);
            float t0 = f0 / 60f, t1 = Mathf.Min((f0 + 1) / 60f, c.Length);
            float u = Mathf.Clamp01((t - t0) / Mathf.Max(t1 - t0, 1e-5f));
            return Lerp(b.Matrix(bone, c.FirstRow + f0), b.Matrix(bone, c.FirstRow + f0 + 1), u);
        }

        static Matrix4x4 Lerp(Matrix4x4 a, Matrix4x4 c, float u)
        {
            var m = new Matrix4x4();
            for (int i = 0; i < 16; i++) m[i] = a[i] + (c[i] - a[i]) * u;
            return m;
        }

        /// <summary>
        /// The palette pass on the GPU against the bake's own frames: instances with a crossfade,
        /// a looping clip past its length (wrap) and a non-looping clip past its end (its last
        /// frame) read back as the interpolated, blended baked matrices.
        /// </summary>
        [Test]
        public void ThePalettePassBlendsTheBakedFrames()
        {
            using var b = Bake("skeleton");
            int walk = b.Clips.FindIndex(c => c.Role == "walk"), death = b.Clips.FindIndex(c => c.Role == "death"),
                attack = b.Clips.FindIndex(c => c.Role == "attack");
            Assume.That(b.Clips[walk].Loop && !b.Clips[death].Loop, "walk loops and death doesn't");
            var tex = new Texture2D(b.Width, b.Height, TextureFormat.RGBAFloat, false, true) { filterMode = FilterMode.Point };
            tex.SetPixelData(b.Texels, 0);
            tex.Apply(false, false);
            var mesh = b.BuildMesh("palette-test");
            var lib = ScriptableObject.CreateInstance<CrowdLibrary>();
            lib.CullShader = AssetDatabase.LoadAssetAtPath<ComputeShader>(CrowdBaker.CullPath);
            lib.PaletteShader = AssetDatabase.LoadAssetAtPath<ComputeShader>(CrowdBaker.PalettePath);
            var material = new Material(AssetDatabase.LoadAssetAtPath<Shader>(CrowdBaker.ShaderPath));
            lib.Types = new[]
            {
                new CrowdType { Id = "skeleton", Mesh = mesh, Materials = new[] { material }, Bones = tex, BoneCount = b.Entries.Count, Clips = b.Clips.ToArray() },
            };
            var cases = new[]
            {
                // A looping clip 1.37 lengths in: it wraps.
                new MonsterInstance { ClipA = (uint)walk, TimeA = b.Clips[walk].Length * 1.37f, ClipB = (uint)walk, TimeB = 0, Blend = 1 },
                // A non-looping clip half a second past its end (its last frame), 30% over a walk.
                new MonsterInstance { ClipA = (uint)death, TimeA = b.Clips[death].Length + 0.5f, ClipB = (uint)walk, TimeB = 0.123f, Blend = 0.3f },
                // Between frames on both clips.
                new MonsterInstance { ClipA = (uint)attack, TimeA = 0.4567f, ClipB = (uint)death, TimeB = 0.2041f, Blend = 0.75f },
            };
            var camGo = new GameObject("TestCamera");
            var cam = camGo.AddComponent<Camera>();
            var rt = new RenderTexture(640, 360, 24);
            var crowd = new BoneTextureCrowd(lib);
            try
            {
                cam.targetTexture = rt;
                cam.fieldOfView = 30;
                var rot = Quaternion.Euler(55, 0, 0);
                cam.transform.SetPositionAndRotation(-(rot * Vector3.forward) * 30, rot);
                crowd.Initialize(new[] { "skeleton" }, 64);
                var inst = crowd.Instances(0, cases.Length);
                for (int i = 0; i < cases.Length; i++)
                {
                    var m = cases[i];
                    m.Position = new Vector3(i * 2f - 2f, 0, 0);
                    m.Scale = 1;
                    m.Flags = (uint)CrowdFlags.Alive;
                    inst[i] = m;
                }
                crowd.Draw(cam);
                var palette = crowd.DebugLastPalette(0);
                Assert.That(palette, Is.Not.Null, "the palette pass ran");
                var rows = new Vector4[palette.count];
                palette.GetData(rows);
                float worst = 0;
                for (int i = 0; i < cases.Length; i++)
                {
                    var m = cases[i];
                    var ca = b.Clips[(int)m.ClipA];
                    var cb = b.Clips[(int)m.ClipB];
                    for (int bone = 0; bone < b.Entries.Count; bone++)
                    {
                        var expected = Lerp(Sampled(b, cb, bone, m.TimeB), Sampled(b, ca, bone, m.TimeA), m.Blend);
                        for (int r = 0; r < 3; r++)
                        {
                            var got = rows[(i * b.Entries.Count + bone) * 3 + r];
                            worst = Mathf.Max(worst, (got - expected.GetRow(r)).magnitude);
                        }
                    }
                }
                Assert.That(worst, Is.LessThan(1e-4f), $"worst row error {worst}");
                // The non-looping clip past its end holds exactly its last baked frame.
                var last = b.Matrix(0, b.Clips[death].FirstRow + b.Clips[death].Frames - 1);
                var walkAt = Sampled(b, b.Clips[walk], 0, 0.123f);
                var held = Lerp(walkAt, last, 0.3f);
                Assert.That((rows[(1 * b.Entries.Count) * 3] - held.GetRow(0)).magnitude, Is.LessThan(1e-4f));
            }
            finally
            {
                crowd.Dispose();
                cam.targetTexture = null;
                rt.Release();
                Object.DestroyImmediate(camGo);
                Object.DestroyImmediate(material);
                Object.DestroyImmediate(mesh);
                Object.DestroyImmediate(tex);
                Object.DestroyImmediate(lib);
            }
        }

        [Test]
        public void TheColourIdBakeUsesTheVendorsHsvConversion()
        {
            Assert.That(Vector3.Distance(CrowdBaker.HsvToRgb(new Vector3(0, 1, 1)), new Vector3(1, 0, 0)), Is.LessThan(1e-5f));
            Assert.That(CrowdBaker.HsvToRgb(new Vector3(1f / 3f, 1, 1)).y, Is.EqualTo(1f).Within(1e-5f));
            Assert.That(Vector3.Distance(CrowdBaker.HsvToRgb(new Vector3(0.5f, 0, 0.25f)), new Vector3(0.25f, 0.25f, 0.25f)), Is.LessThan(1e-5f));
        }

        [Test]
        public void TheStyleMeshUnweldsEveryTriangleAndKeepsTheCount()
        {
            using var b = Bake("skeleton");
            var welded = b.BuildMesh("skeleton_test");
            var lods = CrowdBaker.BuildLods(welded, "skeleton_test");
            try
            {
                foreach (var src in new[] { welded, lods[0], lods[1] })
                {
                    var style = CrowdBaker.Unweld(src, 7);
                    try
                    {
                        Assert.That(style.subMeshCount, Is.EqualTo(src.subMeshCount));
                        for (int sm = 0; sm < src.subMeshCount; sm++)
                            Assert.That(style.GetIndexCount(sm), Is.EqualTo(src.GetIndexCount(sm)), $"{src.name} submesh {sm}");
                        int corners = 0;
                        for (int sm = 0; sm < src.subMeshCount; sm++) corners += (int)src.GetIndexCount(sm);
                        Assert.That(style.vertexCount, Is.EqualTo(corners), "three vertices of its own per triangle");
                        Assert.That(style.GetVertexAttributeDimension(VertexAttribute.TexCoord3), Is.EqualTo(4), "barycentrics and a value per triangle");
                        Assert.That(style.GetVertexAttributeDimension(VertexAttribute.Tangent), Is.EqualTo(4), "the face normal");
                        Assert.That(style.GetVertexAttributeDimension(VertexAttribute.TexCoord1), Is.EqualTo(4), "bone indices stay in UV1");
                        Assert.That(style.GetVertexAttributeDimension(VertexAttribute.TexCoord2), Is.EqualTo(4), "and weights in UV2");

                        var srcPos = src.vertices;
                        var srcUv = new List<Vector2>(); src.GetUVs(0, srcUv);
                        var srcBones = new List<Vector4>(); src.GetUVs(1, srcBones);
                        var srcWeights = new List<Vector4>(); src.GetUVs(2, srcWeights);
                        var pos = style.vertices;
                        var tan = style.tangents;
                        var uv = new List<Vector2>(); style.GetUVs(0, uv);
                        var bones = new List<Vector4>(); style.GetUVs(1, bones);
                        var weights = new List<Vector4>(); style.GetUVs(2, weights);
                        var bary = new List<Vector4>(); style.GetUVs(3, bary);
                        int moved = 0, unbound = 0, badBary = 0, splitTri = 0, badFace = 0;
                        for (int sm = 0; sm < src.subMeshCount; sm++)
                        {
                            var srcTris = src.GetIndices(sm);
                            var tris = style.GetIndices(sm);
                            for (int t = 0; t < tris.Length; t += 3)
                            {
                                for (int k = 0; k < 3; k++)
                                {
                                    int v = tris[t + k], w = srcTris[t + k];
                                    if (pos[v] != srcPos[w] || uv[v] != srcUv[w]) moved++;
                                    if (bones[v] != srcBones[w] || weights[v] != srcWeights[w]) unbound++;
                                    if (bary[v][k] != 1f || bary[v].x + bary[v].y + bary[v].z != 1f) badBary++;
                                    if (bary[v].w != bary[tris[t]].w || tan[v] != tan[tris[t]]) splitTri++;
                                }
                                var face = Vector3.Cross(srcPos[srcTris[t + 1]] - srcPos[srcTris[t]], srcPos[srcTris[t + 2]] - srcPos[srcTris[t]]);
                                if (face != Vector3.zero && Vector3.Dot(tan[tris[t]], face / face.magnitude) < 0.999f) badFace++;
                                if (Mathf.Abs(((Vector3)tan[tris[t]]).magnitude - 1f) > 1e-3f) badFace++;
                            }
                        }
                        Assert.That(moved, Is.Zero, "the rest position and UV are the welded vertex's");
                        Assert.That(unbound, Is.Zero, "the welded vertex's bones and weights");
                        Assert.That(badBary, Is.Zero, "corner k is barycentric axis k");
                        Assert.That(splitTri, Is.Zero, "one random value and one face normal per triangle");
                        Assert.That(badFace, Is.Zero, "the tangent is the unit face normal");
                    }
                    finally
                    {
                        Object.DestroyImmediate(style);
                    }
                }
            }
            finally
            {
                Object.DestroyImmediate(welded);
                foreach (var l in lods) Object.DestroyImmediate(l);
            }
        }

        /// <summary>
        /// The crystal shatter's shard bake (CrowdShardBaker) on one type: the style mesh on its rest
        /// pose cut into 20 to 40 closed chunks of varied size, whose skin is exactly the style mesh's
        /// corners (every triangle once, each with its own bones and weights) and whose inside faces
        /// alone carry the flag, every corner with weights that sum to 1.
        /// </summary>
        [Test]
        public void TheShardBakeCutsTheRestPoseIntoClosedVariedChunksThatKeepTheirSkinWeights()
        {
            using var b = Bake("slime");
            var welded = b.BuildMesh("slime_test");
            var style = CrowdBaker.Unweld(welded, 7);
            CrowdShards.Set set = null;
            try
            {
                set = CrowdShardBaker.Bake(b, style, 11);
                Assert.That(set.Shards.Length, Is.InRange(CrowdShardBaker.MinShards, CrowdShardBaker.MaxShards));
                var rest = CrowdShardBaker.Rest(style);
                // Every style corner, keyed by its position, bones and weights.
                var corners = new HashSet<(Vector3, Vector4, Vector4)>();
                for (int k = 0; k < rest.Position.Length; k++) corners.Add((rest.Position[k], rest.Bones[k], rest.Weights[k]));
                int skinTriangles = 0, offRest = 0, open = 0, badWeights = 0, badBone = 0;
                double skinArea = 0, restArea = 0;
                for (int t = 0; t < rest.Triangles; t++)
                    restArea += 0.5 * Vector3.Cross(rest.Position[3 * t + 1] - rest.Position[3 * t], rest.Position[3 * t + 2] - rest.Position[3 * t]).magnitude;
                var volumes = new List<float>();
                foreach (var s in set.Shards)
                {
                    var v = s.Mesh.vertices;
                    var flags = new List<Vector2>(); s.Mesh.GetUVs(1, flags);
                    var bones = new List<Vector4>(); s.Mesh.GetUVs(4, bones);
                    var weights = new List<Vector4>(); s.Mesh.GetUVs(5, weights);
                    Assert.That(bones.Count, Is.EqualTo(v.Length), "every corner has bones");
                    Assert.That(weights.Count, Is.EqualTo(v.Length), "and weights");
                    var tris = s.Mesh.triangles;
                    var edges = new Dictionary<(Vector3, Vector3), int>();
                    bool insideSeen = false;
                    for (int t = 0; t < tris.Length; t += 3)
                    {
                        float f = flags[tris[t]].x;
                        Assert.That(flags[tris[t + 1]].x == f && flags[tris[t + 2]].x == f, "a triangle is skin or inside, not both");
                        if (f == 0)
                        {
                            Assert.That(insideSeen, Is.False, "the skin comes first, then the inside faces");
                            skinTriangles++;
                            for (int k = 0; k < 3; k++) if (!corners.Contains((v[tris[t + k]], bones[tris[t + k]], weights[tris[t + k]]))) offRest++;
                            skinArea += 0.5 * Vector3.Cross(v[tris[t + 1]] - v[tris[t]], v[tris[t + 2]] - v[tris[t]]).magnitude;
                        }
                        else insideSeen = true;
                        for (int k = 0; k < 3; k++)
                        {
                            var w = weights[tris[t + k]];
                            if (Mathf.Abs(w.x + w.y + w.z + w.w - 1f) > 1e-4f || w.x <= 0) badWeights++;
                            var a = v[tris[t + k]];
                            var c = v[tris[t + (k + 1) % 3]];
                            if (a == c) continue;
                            var key = a.x < c.x || (a.x == c.x && (a.y < c.y || (a.y == c.y && a.z < c.z))) ? (a, c) : (c, a);
                            edges[key] = edges.TryGetValue(key, out int n) ? n + 1 : 1;
                        }
                    }
                    open += edges.Values.Count(n => n % 2 == 1);
                    if (s.Bone < 0 || s.Bone >= b.Entries.Count) badBone++;
                    Assert.That(s.Volume, Is.GreaterThan(0f), "a chunk has thickness");
                    Assert.That(s.Size, Is.GreaterThan(0f));
                    volumes.Add(s.Volume);
                }
                Assert.That(skinTriangles, Is.EqualTo(rest.Triangles), "the skins hold every triangle of the style mesh");
                Assert.That(skinArea, Is.EqualTo(restArea).Within(restArea * 1e-4), "and each once");
                Assert.That(offRest, Is.Zero, "skin corners are the style mesh's corners, with their own bones and weights");
                Assert.That(badWeights, Is.Zero, "every corner's weights sum to 1, the first the largest");
                Assert.That(badBone, Is.Zero, "each chunk's box bone is a bone of the type");
                Assert.That(open, Is.Zero, "every chunk is closed (every edge shared by an even number of its triangles)");
                volumes.Sort();
                Assert.That(volumes[volumes.Count - 1], Is.GreaterThan(4 * volumes[volumes.Count / 2]), "a few large chunks among many small ones");
            }
            finally
            {
                if (set != null) foreach (var s in set.Shards) Object.DestroyImmediate(s.Mesh);
                Object.DestroyImmediate(style);
                Object.DestroyImmediate(welded);
            }
        }

        /// <summary>
        /// The crystal shatter's swap at a live pose (CrowdPose, CrystalShatter): for monsters
        /// mid-walk, mid-attack and crossfading, the CPU's bone matrices match the crowd's GPU palette
        /// pass, and a shard corner skinned and placed on the CPU lands where the crowd's SkinVertex
        /// puts the same style mesh corner, with the palette's matrices, at the instance's position,
        /// yaw and scale.
        /// </summary>
        [Test]
        public void TheShatterPosesItsChunksWhereTheCrowdDrawsTheMonster()
        {
            using var b = Bake("skeleton");
            int walk = b.Clips.FindIndex(c => c.Role == "walk"), attack = b.Clips.FindIndex(c => c.Role == "attack"),
                hit = b.Clips.FindIndex(c => c.Role == "hit");
            var tex = new Texture2D(b.Width, b.Height, TextureFormat.RGBAFloat, false, true) { filterMode = FilterMode.Point };
            tex.SetPixelData(b.Texels, 0);
            tex.Apply(false, false);
            var mesh = b.BuildMesh("pose-test");
            var style = CrowdBaker.Unweld(mesh, 5);
            var set = CrowdShardBaker.Bake(b, style, 13);
            var lib = ScriptableObject.CreateInstance<CrowdLibrary>();
            lib.CullShader = AssetDatabase.LoadAssetAtPath<ComputeShader>(CrowdBaker.CullPath);
            lib.PaletteShader = AssetDatabase.LoadAssetAtPath<ComputeShader>(CrowdBaker.PalettePath);
            var material = new Material(AssetDatabase.LoadAssetAtPath<Shader>(CrowdBaker.ShaderPath));
            var type = new CrowdType { Id = "skeleton", Mesh = mesh, Materials = new[] { material }, Bones = tex, BoneCount = b.Entries.Count, Clips = b.Clips.ToArray() };
            lib.Types = new[] { type };
            var cases = new[]
            {
                new MonsterInstance { Position = new Vector3(-2, 0, 0), Yaw = 0.7f, Scale = 1.08f, ClipA = (uint)walk, TimeA = 0.6123f, ClipB = (uint)walk, TimeB = 0, Blend = 1 },
                new MonsterInstance { Position = new Vector3(0, 0, 1), Yaw = -2.4f, Scale = 0.93f, ClipA = (uint)attack, TimeA = 0.3871f, ClipB = (uint)walk, TimeB = 0.2f, Blend = 0.55f },
                new MonsterInstance { Position = new Vector3(2, 0, -1), Yaw = 3.1f, Scale = 1f, ClipA = (uint)(hit >= 0 ? hit : attack), TimeA = 0.071f, ClipB = (uint)attack, TimeB = 0.5f, Blend = 0.2f },
            };
            var camGo = new GameObject("TestCamera");
            var cam = camGo.AddComponent<Camera>();
            var rt = new RenderTexture(640, 360, 24);
            var crowd = new BoneTextureCrowd(lib);
            try
            {
                cam.targetTexture = rt;
                cam.fieldOfView = 30;
                var rot = Quaternion.Euler(55, 0, 0);
                cam.transform.SetPositionAndRotation(-(rot * Vector3.forward) * 30, rot);
                crowd.Initialize(new[] { "skeleton" }, 64);
                var inst = crowd.Instances(0, cases.Length);
                for (int i = 0; i < cases.Length; i++)
                {
                    var m = cases[i];
                    m.Flags = (uint)CrowdFlags.Alive;
                    inst[i] = m;
                }
                crowd.Draw(cam);
                var palette = crowd.DebugLastPalette(0);
                Assert.That(palette, Is.Not.Null, "the palette pass ran");
                var rows = new Vector4[palette.count];
                palette.GetData(rows);
                var pose = new CrowdPose(type, clips30: false);
                var cpu = new Matrix4x4[b.Entries.Count];
                var gpu = new Matrix4x4[b.Entries.Count];
                var restStyle = CrowdShardBaker.Rest(style);
                float worstBone = 0, worstCorner = 0;
                for (int i = 0; i < cases.Length; i++)
                {
                    var m = cases[i];
                    pose.Bones(m, cpu);
                    for (int bone = 0; bone < cpu.Length; bone++)
                    {
                        var g = Matrix4x4.identity;
                        for (int r = 0; r < 3; r++)
                        {
                            g.SetRow(r, rows[(i * cpu.Length + bone) * 3 + r]);
                            worstBone = Mathf.Max(worstBone, (g.GetRow(r) - cpu[bone].GetRow(r)).magnitude);
                        }
                        gpu[bone] = g;
                    }
                    // The crowd's SkinVertex, written out here, on every style corner with the GPU's matrices.
                    var drawn = new HashSet<Vector3Int>();
                    Vector3Int Key(Vector3 v) => Vector3Int.RoundToInt(v * 2e4f); // 0.05 mm cells
                    for (int k = 0; k < restStyle.Position.Length; k++)
                    {
                        var bi = restStyle.Bones[k];
                        var bw = restStyle.Weights[k];
                        var skin = new Matrix4x4();
                        for (int j = 0; j < 4; j++)
                            for (int e = 0; e < 12; e++) skin[e % 3, e / 3] += gpu[(int)bi[j]][e % 3, e / 3] * bw[j];
                        var p = skin.MultiplyPoint3x4(restStyle.Position[k]) * m.Scale;
                        float sy = Mathf.Sin(m.Yaw), cy = Mathf.Cos(m.Yaw);
                        var w = m.Position + new Vector3(p.x * cy + p.z * sy, p.y, -p.x * sy + p.z * cy);
                        foreach (var dx in new[] { -1, 0, 1 }) foreach (var dy in new[] { -1, 0, 1 }) foreach (var dz in new[] { -1, 0, 1 })
                            drawn.Add(Key(w) + new Vector3Int(dx, dy, dz));
                    }
                    // Every shard's skin corners, posed by the shatter's CPU skinning.
                    int off = 0, total = 0;
                    foreach (var s in set.Shards)
                    {
                        var v = s.Mesh.vertices;
                        var flags = new List<Vector2>(); s.Mesh.GetUVs(1, flags);
                        var bones = new List<Vector4>(); s.Mesh.GetUVs(4, bones);
                        var weights = new List<Vector4>(); s.Mesh.GetUVs(5, weights);
                        for (int k = 0; k < v.Length; k++)
                        {
                            if (flags[k].x > 0.5f) continue;
                            var at = CrowdPose.Place(CrowdPose.Blend(cpu, bones[k], weights[k]), v[k], m);
                            total++;
                            if (!drawn.Contains(Key(at))) off++;
                        }
                    }
                    Assert.That(total, Is.EqualTo(restStyle.Position.Length), "every style corner is a shard's skin corner");
                    Assert.That(off, Is.Zero, $"case {i}: shard corners off the drawn monster");
                }
                Assert.That(worstBone, Is.LessThan(1e-5f), $"worst bone row error {worstBone}");
            }
            finally
            {
                crowd.Dispose();
                cam.targetTexture = null;
                rt.Release();
                Object.DestroyImmediate(camGo);
                Object.DestroyImmediate(material);
                foreach (var s in set.Shards) Object.DestroyImmediate(s.Mesh);
                Object.DestroyImmediate(style);
                Object.DestroyImmediate(mesh);
                Object.DestroyImmediate(tex);
                Object.DestroyImmediate(lib);
            }
        }

        [Test]
        public void TheBakedLibraryIsComplete()
        {
            var lib = AssetDatabase.LoadAssetAtPath<CrowdLibrary>(CrowdBaker.LibraryPath);
            if (lib == null) Assert.Ignore("no baked library: run `demo3d unity BakeCrowd`");
            var drawn = Roster.Load().Where(CrowdBaker.Drawn).Select(t => t.id).ToArray();
            Assert.That(lib.Types.Select(t => t.Id), Is.EqualTo(drawn));
            Assert.That(lib.CullShader, Is.Not.Null);
            Assert.That(lib.PaletteShader, Is.Not.Null);
            var styleMeshes = AssetDatabase.LoadAssetAtPath<CrowdStyleMeshes>(CrowdBaker.StyleMeshesPath);
            Assert.That(styleMeshes, Is.Not.Null, "re-run BakeCrowd");
            Assert.That(styleMeshes.Types.Select(e => e.Id), Is.EqualTo(drawn));
            var shards = AssetDatabase.LoadAssetAtPath<CrowdShards>(CrowdBaker.ShardsPath);
            Assert.That(shards, Is.Not.Null, "re-run BakeCrowd");
            Assert.That(shards.Types.Select(e => e.Id), Is.EqualTo(drawn));
            Assert.That(shards.Types.SelectMany(e => e.Shards).All(s => s.Mesh != null), "every shard's mesh is saved");
            Assert.That(AssetDatabase.GetDependencies(CrowdBaker.LibraryPath, true).Where(p => p.Contains("_style")), Is.Empty,
                "the library, which every run loads, doesn't pull in the style meshes");
            foreach (var t in lib.Types)
            {
                Assert.That(t.Materials.Length, Is.EqualTo(t.Mesh.subMeshCount), t.Id);
                var e = styleMeshes.Of(t.Id);
                foreach (var (style, welded) in new[] { (e.Lod0, t.Mesh), (e.Lod1, t.Lod1), (e.Lod2, t.Lod2) })
                {
                    Assert.That(style, Is.Not.Null, t.Id);
                    for (int sm = 0; sm < welded.subMeshCount; sm++)
                        Assert.That(style.GetIndexCount(sm), Is.EqualTo(welded.GetIndexCount(sm)), $"{style.name} keeps every triangle");
                    Assert.That(style.GetVertexAttributeDimension(VertexAttribute.TexCoord3), Is.EqualTo(4), style.name);
                    Assert.That(style.GetVertexAttributeDimension(VertexAttribute.Tangent), Is.EqualTo(4), style.name);
                }
                Assert.That(t.Bones.width, Is.EqualTo(t.BoneCount * 3), t.Id);
                Assert.That(t.Bones.format, Is.EqualTo(TextureFormat.RGBAFloat), t.Id);
                Assert.That(t.Bones.isReadable && t.Bones30.isReadable, $"{t.Id}: the crystal shatter reads its bones on the CPU (CrowdPose)");
                Assert.That(t.Bones.height, Is.EqualTo(t.Clips.Sum(c => c.Frames)), t.Id);
                Assert.That(t.StockPrefab.GetComponentsInChildren<SkinnedMeshRenderer>().Length, Is.EqualTo(1), t.Id);
                Assert.That(t.Clip("walk"), Is.GreaterThanOrEqualTo(0), t.Id);
            }
        }
    }
}
