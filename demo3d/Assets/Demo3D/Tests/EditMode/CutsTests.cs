using System;
using System.Linq;
using Demo3D.EditorTools.Crowd;
using Demo3D.Runtime;
using Demo3D.Runtime.Crowd;
using Demo3D.Runtime.Effects;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Demo3D.Tests
{
    /// <summary>TD11: the cut switches, the LOD and 30 Hz bakes, the merge and glow cuts on the CPU side.</summary>
    public class CutsTests
    {
        [Test]
        public void CutSpecsParse()
        {
            Assert.That(Cuts.Parse("none").Enabled, Is.Empty);
            Assert.That(Cuts.Parse("all").Enabled, Is.EquivalentTo(Cuts.All));
            Assert.That(Cuts.Parse("default").Enabled, Is.EquivalentTo(Cuts.Defaults));
            Assert.That(Cuts.Parse("lod,merge").Enabled, Is.EquivalentTo(new[] { Cuts.Lod, Cuts.Merge }));
            // A list starting with + or - edits the defaults.
            Assert.That(Cuts.Parse("+clips30").Enabled, Is.EquivalentTo(Cuts.Defaults.Append(Cuts.Clips30).Distinct()));
            foreach (var d in Cuts.Defaults)
                Assert.That(Cuts.Parse("-" + d).On(d), Is.False, d);
            Assert.Throws<ArgumentException>(() => Cuts.Parse("lod,blob"));
            foreach (var d in Cuts.Defaults) Assert.That(Cuts.All, Does.Contain(d));
        }

        [Test]
        public void TheLodsAndThe30HzClipsAreBaked()
        {
            var lib = AssetDatabase.LoadAssetAtPath<CrowdLibrary>(CrowdBaker.LibraryPath);
            if (lib == null) Assert.Ignore("no baked library: run `demo3d unity BakeCrowd`");
            foreach (var t in lib.Types)
            {
                Assert.That(t.Lod1, Is.Not.Null, t.Id);
                Assert.That(t.Lod2, Is.Not.Null, t.Id);
                // Same vertices and streams (bone indices and weights in UV1 and UV2), fewer triangles.
                Assert.That(t.Lod1.vertexCount, Is.EqualTo(t.Mesh.vertexCount), t.Id);
                Assert.That(t.Lod2.subMeshCount, Is.EqualTo(t.Mesh.subMeshCount), t.Id);
                Assert.That(t.Lod1.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.TexCoord2), t.Id);
                Assert.That(t.Lod1Triangles, Is.LessThanOrEqualTo(0.8 * t.Triangles), t.Id);
                Assert.That(t.Lod2Triangles, Is.LessThanOrEqualTo(0.7 * t.Lod1Triangles), t.Id);
                Assert.That(t.Lod2Triangles, Is.GreaterThan(0), t.Id);
                // 30 Hz: both ends of every clip, rows packed in clip order.
                Assert.That(t.Bones30, Is.Not.Null, t.Id);
                Assert.That(t.Bones30.width, Is.EqualTo(t.Bones.width), t.Id);
                Assert.That(t.Bones30.height, Is.EqualTo(t.Clips.Sum(c => c.Frames30)), t.Id);
                int row = 0;
                foreach (var c in t.Clips)
                {
                    Assert.That(c.FirstRow30, Is.EqualTo(row), t.Id + " " + c.Role);
                    Assert.That(c.Frames30, Is.EqualTo(CrowdClip.FrameCount(c.Length, 30)), t.Id + " " + c.Role);
                    Assert.That(c.FrameTime(c.Frames30 - 1, 30), Is.EqualTo(c.Length).Within(1e-5f));
                    row += c.Frames30;
                }
            }
        }

        [Test]
        public void The30HzRowsMatchThe60HzRowsAtTheSameTimes()
        {
            var lib = AssetDatabase.LoadAssetAtPath<CrowdLibrary>(CrowdBaker.LibraryPath);
            if (lib == null) Assert.Ignore("no baked library: run `demo3d unity BakeCrowd`");
            var t = lib.Types.First(x => x.Id == "slime");
            var p60 = t.Bones.GetPixels();
            var p30 = t.Bones30.GetPixels();
            int w = t.Bones.width;
            foreach (var c in t.Clips)
                for (int f = 0; f + 1 < c.Frames30; f++) // frame f at 30 Hz is frame 2f at 60 Hz (the last may be the clip's end)
                    for (int x = 0; x < w; x++)
                    {
                        var a = p30[(c.FirstRow30 + f) * w + x];
                        var b = p60[(c.FirstRow + 2 * f) * w + x];
                        Assert.That(Vector4.Distance(a, b), Is.LessThan(1e-4f), $"{c.Role} frame {f} texel {x}");
                    }
        }

        static EffectLibrary Library()
        {
            var lib = Resources.Load<EffectLibrary>(EffectLibrary.ResourcePath);
            if (lib == null) Assert.Ignore("no effect library: run `demo3d unity BakeEffects`");
            return lib;
        }

        [Test]
        public void TheSelectionsGlowQuadsAreMarked()
        {
            var lib = Library();
            var glows = lib.Effects.Where(e => Enumerable.Range(e.FirstEmitter, e.EmitterCount).Any(i => lib.Emission[i].Glow))
                .Select(e => e.Name).ToArray();
            Assert.That(glows, Is.EquivalentTo(new[] { "hit-spark-b", "death-burst", "death-burst-large", "slam-shock", "bolt" }));
            // Every effect keeps something to draw without its glow.
            foreach (var e in lib.Effects)
                Assert.That(Enumerable.Range(e.FirstEmitter, e.EmitterCount).Any(i => !lib.Emission[i].Glow), e.Name);
        }

        [Test]
        public void MergeFoldsCoincidentIdenticalSpawnsAndDropGlowSkipsGlowEmitters()
        {
            var lib = Library();
            var cam = new GameObject("cam").AddComponent<Camera>();
            cam.transform.SetPositionAndRotation(new Vector3(0, 30, -20), Quaternion.Euler(55, 0, 0));
            cam.pixelRect = new Rect(0, 0, 1920, 1080);
            var fx = new EffectSystem(lib, 1 << 14, 4);
            try
            {
                int burst = lib.IndexOf("death-burst");
                var cuts = Cuts.Parse("merge,drop-glow");
                fx.Cuts = cuts;
                EffectSystem.Spawn At(Vector3 p, uint seed) => new EffectSystem.Spawn { Effect = burst, Position = p, Scale = 1, Seed = seed, Time = 0 };
                fx.BeginFrame();
                fx.Add(At(new Vector3(0, 1, 0), 1));
                fx.Add(At(new Vector3(0.01f, 1, 0), 2)); // a fraction of a pixel away: merged
                fx.Add(At(new Vector3(6, 1, 0), 3));     // far: its own
                var other = At(new Vector3(0, 1, 0), 4);
                other.Scale = 2;                          // not identical: its own
                fx.Add(other);
                fx.Update(0, 0, cam);
                Assert.That(fx.MergedTotal, Is.EqualTo(1));
                var e = lib.Effects[burst];
                int glows = Enumerable.Range(e.FirstEmitter, e.EmitterCount).Count(i => lib.Emission[i].Glow);
                Assert.That(glows, Is.EqualTo(1));
                Assert.That(fx.GlowsDropped, Is.EqualTo(3)); // three spawns expanded, one glow each
                Assert.That(fx.SpawnsTotal, Is.EqualTo(4));
                // Without the cuts nothing merges or drops.
                fx.Cuts = Cuts.Parse("none");
                fx.Add(At(new Vector3(0, 1, 0), 5));
                fx.Add(At(new Vector3(0, 1, 0), 6));
                fx.Update(0.1, 0.1f, cam);
                Assert.That(fx.MergedTotal, Is.EqualTo(1));
                Assert.That(fx.GlowsDropped, Is.EqualTo(3));
                // The light-cell cut sets the cluster cell.
                Assert.That(fx.Lights.Cell, Is.EqualTo(EffectLights.DefaultCell));
                fx.Cuts = Cuts.Parse("light-cell");
                Assert.That(fx.Lights.Cell, Is.EqualTo(cuts.LightCellM));
            }
            finally
            {
                fx.Dispose();
                UnityEngine.Object.DestroyImmediate(cam.gameObject);
            }
        }
    }
}
