using System.IO;
using System.Linq;
using Demo3D.EditorTools.Assets;
using Demo3D.EditorTools.Crowd;
using Demo3D.Runtime.Crowd;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

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
        public void TheBakedLibraryIsComplete()
        {
            var lib = AssetDatabase.LoadAssetAtPath<CrowdLibrary>(CrowdBaker.LibraryPath);
            if (lib == null) Assert.Ignore("no baked library: run `demo3d unity BakeCrowd`");
            var drawn = Roster.Load().Where(CrowdBaker.Drawn).Select(t => t.id).ToArray();
            Assert.That(lib.Types.Select(t => t.Id), Is.EqualTo(drawn));
            Assert.That(lib.CullShader, Is.Not.Null);
            Assert.That(lib.PaletteShader, Is.Not.Null);
            foreach (var t in lib.Types)
            {
                Assert.That(t.Materials.Length, Is.EqualTo(t.Mesh.subMeshCount), t.Id);
                Assert.That(t.Bones.width, Is.EqualTo(t.BoneCount * 3), t.Id);
                Assert.That(t.Bones.format, Is.EqualTo(TextureFormat.RGBAFloat), t.Id);
                Assert.That(t.Bones.height, Is.EqualTo(t.Clips.Sum(c => c.Frames)), t.Id);
                Assert.That(t.StockPrefab.GetComponentsInChildren<SkinnedMeshRenderer>().Length, Is.EqualTo(1), t.Id);
                Assert.That(t.Clip("walk"), Is.GreaterThanOrEqualTo(0), t.Id);
            }
        }
    }
}
