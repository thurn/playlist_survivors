using System.Linq;
using NUnit.Framework;
using PS.Look.EditorTools.Hero;
using PS.Look.Hero;
using UnityEngine;

namespace PS.Look.Tests
{
    /// <summary>The hero looks (HeroLooks): one per preset, their materials, and the bake's faceted meshes they draw.</summary>
    public sealed class HeroLookTests
    {
        [Test]
        public void EveryPresetsHeroLookIsBuiltAndClassicHasNone()
        {
            foreach (var preset in new[] { "edge", "solid", "hologram", "prism" })
            {
                var look = ResolvedLook.Resolve(LookFlags.Parse(new[] { "--look", preset }));
                Assert.AreEqual(preset, look.Option(LookAxes.Hero), preset);
                Assert.AreEqual(preset, HeroLooks.Of(look.Option(LookAxes.Hero))?.Name, preset);
            }
            var classic = ResolvedLook.Resolve(LookFlags.Parse(new[] { "--look", "classic" }));
            Assert.IsNull(HeroLooks.Of(classic.Option(LookAxes.Hero)), "classic draws the knight and blade as before");
            Assert.IsTrue(HeroLooks.Of(HeroLooks.Prism).Refracts);
            Assert.IsFalse(HeroLooks.Of(HeroLooks.Edge).Refracts);
        }

        [Test]
        public void AHeroLooksMaterialsTakeTheHeroRolesAndTheBladeEdgesEveryFacet()
        {
            var palette = ResolvedLook.Active?.Palette ?? ResolvedLook.FallbackPalette;
            foreach (var option in HeroLooks.Options)
            {
                var look = HeroLooks.Of(option);
                var body = look.Body();
                var blade = look.Blade();
                try
                {
                    Assert.That(Vector4.Distance(palette.HeroBody.linear, body.GetColor("_Body")), Is.LessThan(1e-4f), option);
                    Assert.That(Vector4.Distance(palette.HeroBlade.linear, blade.GetColor("_Body")), Is.LessThan(1e-4f), option);
                    Assert.That(Vector4.Distance(palette.HeroTrail.linear, body.GetColor("_Accent")), Is.LessThan(1e-4f), option);
                    Assert.AreEqual(0f, body.GetFloat("_AllEdges"), option);
                    Assert.AreEqual(1f, blade.GetFloat("_AllEdges"), option);
                    Assert.AreEqual(1f, blade.GetFloat("_Blade"), option);
                    Assert.IsTrue(blade.HasProperty("_BladeFlare"), $"{option}: HeroBlade sets the flare");
                    Assert.IsTrue(body.HasProperty("_EmissionColor"), $"{option}: the game's low-health pulse");
                    Assert.That(body.FindPass("MotionVectors"), Is.GreaterThanOrEqualTo(0), $"{option}: a swing reprojects");
                }
                finally
                {
                    Object.DestroyImmediate(body);
                    Object.DestroyImmediate(blade);
                }
            }
        }

        [Test]
        public void AFacetedSkinnedMeshKeepsItsWeightsAndGivesEachTriangleItsCornersAndEdges()
        {
            // Two triangles sharing an edge, folded 90 degrees (a feature edge), weighted to two bones.
            var src = new Mesh();
            src.SetVertices(new[] { new Vector3(0, 0, 0), new Vector3(0, 0, 1), new Vector3(1, 0, 0), new Vector3(0, 1, 0) });
            src.SetTriangles(new[] { 0, 1, 2, 0, 3, 1 }, 0);
            src.RecalculateNormals();
            src.boneWeights = new[]
            {
                new BoneWeight { boneIndex0 = 0, weight0 = 1 }, new BoneWeight { boneIndex0 = 1, weight0 = 1 },
                new BoneWeight { boneIndex0 = 0, weight0 = 0.5f, boneIndex1 = 1, weight1 = 0.5f }, new BoneWeight { boneIndex0 = 1, weight0 = 1 },
            };
            src.bindposes = new[] { Matrix4x4.identity, Matrix4x4.Translate(Vector3.up) };
            var m = HeroFacetBaker.Faceted(src, 3);
            try
            {
                Assert.AreEqual(6, m.vertexCount, "every triangle its own corners");
                Assert.AreEqual(src.bindposes, m.bindposes);
                var weights = m.boneWeights;
                var tris = m.triangles;
                var srcTris = src.triangles;
                for (int i = 0; i < 6; i++)
                    Assert.AreEqual(src.boneWeights[srcTris[i]], weights[tris[i]], $"corner {i} keeps its weights");
                var bary = new System.Collections.Generic.List<Vector4>();
                m.GetUVs(3, bary);
                var edges = new System.Collections.Generic.List<Vector3>();
                m.GetUVs(5, edges);
                Assert.AreEqual(new Vector4(1, 0, 0, bary[0].w), bary[tris[0]]);
                Assert.AreEqual(bary[tris[0]].w, bary[tris[2]].w, "one random value per triangle");
                // The fold (0-1) is opposite corner 2 of the first triangle and corner 1 of the second: both flagged.
                Assert.AreEqual(1f, edges[tris[0]].z);
                Assert.AreEqual(1f, edges[tris[3]].y);
                Assert.IsTrue(Enumerable.Range(0, 6).All(i => edges[tris[i]] == Vector3.one), "on a fold every edge is real (open or creased)");
            }
            finally
            {
                Object.DestroyImmediate(src);
                Object.DestroyImmediate(m);
            }
        }
    }
}
