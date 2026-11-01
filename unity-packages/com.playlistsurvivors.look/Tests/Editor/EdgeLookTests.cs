using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using PS.Look.Crowd;
using PS.Look.EditorTools.Crowd;
using UnityEngine;

namespace PS.Look.Tests
{
    /// <summary>
    /// The dark-core-edges look (EdgeLook) and the baker's feature edges it draws (StyleMesh.FeatureEdges, carried
    /// into the shards by CrowdShardBaker).
    /// </summary>
    public sealed class EdgeLookTests
    {
        // A unit cube of 12 triangles: every face split by a diagonal that is no edge of the model.
        static readonly Vector3[] CubeCorners =
        {
            new Vector3(0, 0, 0), new Vector3(1, 0, 0), new Vector3(1, 1, 0), new Vector3(0, 1, 0),
            new Vector3(0, 0, 1), new Vector3(1, 0, 1), new Vector3(1, 1, 1), new Vector3(0, 1, 1),
        };
        static readonly int[] CubeTris =
        {
            0, 2, 1, 0, 3, 2, 4, 5, 6, 4, 6, 7, 0, 1, 5, 0, 5, 4,
            3, 7, 6, 3, 6, 2, 0, 4, 7, 0, 7, 3, 1, 2, 6, 1, 6, 5,
        };

        static Mesh Skinned(Vector3[] pos, int[] tris)
        {
            var m = new Mesh();
            m.SetVertices(pos);
            m.SetTriangles(tris, 0);
            m.RecalculateNormals();
            m.SetUVs(1, Enumerable.Repeat(Vector4.zero, pos.Length).ToList());
            m.SetUVs(2, Enumerable.Repeat(new Vector4(1, 0, 0, 0), pos.Length).ToList());
            return m;
        }

        [Test]
        public void ACubesFeatureEdgesAreItsTwelveEdgesNotItsDiagonals()
        {
            var flags = StyleMesh.FeatureEdges(CubeCorners, CubeTris);
            int features = 0;
            for (int t = 0; t < flags.Length; t++)
                for (int k = 0; k < 3; k++)
                {
                    var a = CubeCorners[CubeTris[3 * t + (k + 1) % 3]];
                    var b = CubeCorners[CubeTris[3 * t + (k + 2) % 3]];
                    bool diagonal = (a - b).sqrMagnitude > 1.5f;
                    Assert.AreEqual(diagonal ? 0f : 1f, flags[t][k], $"triangle {t}, edge opposite corner {k}");
                    if (!diagonal) features++;
                }
            Assert.AreEqual(24, features, "each of the 12 edges is flagged on both its faces");
        }

        [Test]
        public void AFlatSheetsEdgesAreItsOpenBorderOnly()
        {
            // Two triangles in one plane: the shared diagonal is triangulation, the open border is a real edge.
            var pos = new[] { new Vector3(0, 0, 0), new Vector3(1, 0, 0), new Vector3(1, 0, 1), new Vector3(0, 0, 1) };
            var flags = StyleMesh.FeatureEdges(pos, new[] { 0, 2, 1, 0, 3, 2 });
            Assert.AreEqual(new Vector3(1, 1, 0), flags[0]); // (0, 2, 1): the diagonal 0-2 is opposite corner 2
            Assert.AreEqual(new Vector3(1, 0, 1), flags[1]); // (0, 3, 2): the diagonal 2-0 is opposite corner 1
        }

        [Test]
        public void AGentleBendIsNoEdgeAndASharpOneIs()
        {
            // Two triangles sharing the edge (0,0,0)-(0,0,1), the second turned up by `deg` about it.
            Vector3[] Hinge(float deg)
            {
                float r = deg * Mathf.Deg2Rad;
                return new[] { new Vector3(0, 0, 0), new Vector3(0, 0, 1), new Vector3(1, 0, 0), new Vector3(-Mathf.Cos(r), Mathf.Sin(r), 0) };
            }
            var tris = new[] { 0, 1, 2, 0, 3, 1 };
            Assert.AreEqual(0f, StyleMesh.FeatureEdges(Hinge(StyleMesh.FeatureAngleDeg - 4), tris)[0].z, "a bend under the angle");
            Assert.AreEqual(1f, StyleMesh.FeatureEdges(Hinge(StyleMesh.FeatureAngleDeg + 4), tris)[0].z, "a bend over it");
        }

        [Test]
        public void UnweldWritesEachTrianglesEdgesOnItsThreeCorners()
        {
            var style = StyleMesh.Unweld(Skinned(CubeCorners, CubeTris), 3);
            var edges = new List<Vector3>();
            style.GetUVs(5, edges);
            var expected = StyleMesh.FeatureEdges(CubeCorners, CubeTris);
            Assert.AreEqual(CubeTris.Length, edges.Count);
            for (int c = 0; c < edges.Count; c++) Assert.AreEqual(expected[c / 3], edges[c]);
            Object.DestroyImmediate(style);
        }

        [Test]
        public void EveryShardIsOutlinedWhereItBroke()
        {
            // A sphere-ish body, cut into chunks: inside faces flag their edge on the skin; every skin triangle on a
            // chunk's boundary flags the crack edge, so every chunk has lit edges whatever the model's own are.
            var src = Skinned(Ico(2), IcoTris);
            var style = StyleMesh.Unweld(src, 5);
            var (mesh, shards) = CrowdShardBaker.Bake("ball", 1.8f, style, 11);
            var chunk = new List<Vector2>(); mesh.GetUVs(4, chunk);
            var edges = new List<Vector3>(); mesh.GetUVs(5, edges);
            Assert.AreEqual(chunk.Count, edges.Count);
            var outlined = new HashSet<int>();
            for (int c = 0; c < edges.Count; c += 3)
            {
                if (chunk[c].x > 0.5f) Assert.AreEqual(new Vector3(0, 0, 1), edges[c], "an inside face flags its skin edge");
                if (edges[c] != Vector3.zero) outlined.Add((int)chunk[c].y);
            }
            Assert.AreEqual(shards.Length, outlined.Count, "every chunk has lit edges");
            Object.DestroyImmediate(mesh);
            Object.DestroyImmediate(style);
            Object.DestroyImmediate(src);
        }

        [Test]
        public void TheEdgeLookDrawsItsOptionInTheTypesPaletteSlot()
        {
            Assert.IsTrue(LookAxes.Monsters.IsBuilt(EdgeLook.Option));
            Assert.IsInstanceOf<EdgeLook>(MonsterLooks.Of("dark-core-edges"));
            var lib = ScriptableObject.CreateInstance<CrowdLibrary>();
            var palette = ResolvedLook.Active?.Palette ?? ResolvedLook.FallbackPalette;
            foreach (var id in new[] { "goblin", "dragon" })
            {
                var type = new CrowdType { Id = id };
                var body = MonsterLooks.Of(EdgeLook.Option).Body(lib, type);
                var shards = MonsterLooks.Of(EdgeLook.Option).Shards(lib, type);
                Assert.AreEqual("Look/MonsterEdge", body.shader.name);
                Assert.AreEqual("Look/MonsterEdgeShard", shards.shader.name);
                var want = (Vector4)EdgeLook.Level((id == "dragon" ? palette.BossBody : palette.Enemies[2]).linear);
                Assert.That(Vector4.Distance(want, body.GetColor("_Tint")), Is.LessThan(1e-4f), id);
                Assert.That(Vector4.Distance(want, shards.GetColor("_Tint")), Is.LessThan(1e-4f), id);
                Object.DestroyImmediate(body);
                Object.DestroyImmediate(shards);
            }
            Object.DestroyImmediate(lib);
        }

        [Test]
        public void EveryRosterTypeHasTheStyleGuidesSlot()
        {
            var want = new Dictionary<string, int>
            {
                ["skeleton"] = 0, ["slime"] = 0, ["demon"] = 1, ["bat"] = 1, ["goblin"] = 2, ["mushroom"] = 3,
                ["bigork"] = 4, ["troll"] = 5, ["dragon"] = MonsterSlots.Boss, ["evilgod"] = MonsterSlots.Boss,
            };
            foreach (var kv in want) Assert.AreEqual(kv.Value, MonsterSlots.Of(kv.Key), kv.Key);
            Assert.That(MonsterSlots.Of("not-in-the-table"), Is.InRange(0, 3), "an unknown type takes a fodder slot");
        }

        // An icosphere's corners, subdivided `levels` times (IcoTris its triangles).
        static int[] IcoTris;

        static Vector3[] Ico(int levels)
        {
            float t = (1 + Mathf.Sqrt(5)) / 2;
            var v = new List<Vector3>
            {
                new Vector3(-1, t, 0), new Vector3(1, t, 0), new Vector3(-1, -t, 0), new Vector3(1, -t, 0),
                new Vector3(0, -1, t), new Vector3(0, 1, t), new Vector3(0, -1, -t), new Vector3(0, 1, -t),
                new Vector3(t, 0, -1), new Vector3(t, 0, 1), new Vector3(-t, 0, -1), new Vector3(-t, 0, 1),
            };
            var f = new List<int>
            {
                0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11, 1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8,
                3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9, 4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1,
            };
            for (int l = 0; l < levels; l++)
            {
                var mid = new Dictionary<(int, int), int>();
                int Mid(int a, int b)
                {
                    var key = a < b ? (a, b) : (b, a);
                    if (!mid.TryGetValue(key, out int i)) { i = v.Count; v.Add((v[a] + v[b]) / 2); mid[key] = i; }
                    return i;
                }
                var g = new List<int>();
                for (int i = 0; i < f.Count; i += 3)
                {
                    int a = f[i], b = f[i + 1], c = f[i + 2], ab = Mid(a, b), bc = Mid(b, c), ca = Mid(c, a);
                    g.AddRange(new[] { a, ab, ca, b, bc, ab, c, ca, bc, ab, bc, ca });
                }
                f = g;
            }
            IcoTris = f.ToArray();
            return v.Select(p => p.normalized * 0.9f + new Vector3(0, 0.9f, 0)).ToArray();
        }
    }
}
