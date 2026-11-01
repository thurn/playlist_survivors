using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace PS.Look.Tests
{
    /// <summary>The void world's crystal kit (CrystalKit, CrystalProps, StructureLook): its pieces, how they fit a structure's tiles, and their breaks.</summary>
    public class CrystalKitTests
    {
        const float TileM = 1.5f;

        readonly List<StructureShards.Set> _made = new List<StructureShards.Set>();

        [TearDown]
        public void TearDown()
        {
            foreach (var s in _made) CrystalKit.Destroy(s);
            _made.Clear();
        }

        StructureShards.Set Keep(StructureShards.Set s)
        {
            _made.Add(s);
            return s;
        }

        IEnumerable<(string name, StructureShards.Set set)> EveryPiece()
        {
            yield return ("formation", Keep(CrystalKit.Formation(2.2f, 4f, 7)));
            yield return ("shard", Keep(CrystalKit.Shard(0.45f, 2.4f, 7)));
            yield return ("obelisk", Keep(CrystalKit.Obelisk(0.6f, 2.8f, 7)));
            yield return ("ring", Keep(CrystalKit.Ring(5f, 0.42f, 7)));
            yield return ("wall", Keep(CrystalKit.Wall(6f, 1.8f, 7)));
        }

        /// <summary>The area of a mesh's faces, its inside faces (uv1.x 1) apart.</summary>
        static (float skin, float inside) Areas(Mesh m)
        {
            var v = m.vertices;
            var t = m.triangles;
            var uv1 = new List<Vector2>();
            m.GetUVs(1, uv1);
            float skin = 0, inside = 0;
            for (int i = 0; i < t.Length; i += 3)
            {
                float a = 0.5f * Vector3.Cross(v[t[i + 1]] - v[t[i]], v[t[i + 2]] - v[t[i]]).magnitude;
                if (uv1[t[i]].x > 0.5f) inside += a;
                else skin += a;
            }
            return (skin, inside);
        }

        [Test]
        public void EveryPiecesShardsTogetherAreTheWhole()
        {
            foreach (var (name, set) in EveryPiece())
            {
                Assert.That(set.Shards.Length, Is.GreaterThan(1), name);
                float skin = 0;
                foreach (var s in set.Shards)
                {
                    var (outer, inside) = Areas(s.Mesh);
                    skin += outer;
                    Assert.That(inside, Is.GreaterThan(0), $"{name}: every shard is closed by inside faces");
                    Assert.That(s.Volume, Is.GreaterThan(0), name);
                    Assert.That(set.Source.bounds.Contains(s.Center), name);
                }
                var whole = Areas(set.Source);
                Assert.That(whole.inside, Is.EqualTo(0), $"{name}: the whole shows only its skin");
                Assert.That(skin, Is.EqualTo(whole.skin).Within(1e-3f * whole.skin), $"{name}: the shards' skins are the whole's");
            }
        }

        [Test]
        public void EveryFaceCarriesItsEdges()
        {
            foreach (var (name, set) in EveryPiece())
            {
                var uv2 = new List<Vector2>();
                set.Source.GetUVs(2, uv2);
                int centres = 0;
                foreach (var e in uv2)
                {
                    Assert.That(e.x == 0f || e.x == 1f, name);
                    if (e.x == 1f) centres++;
                }
                // Each face fans from its one centre, so a face of n corners is n triangles.
                Assert.That(set.Source.triangles.Length / 3, Is.EqualTo(uv2.Count - centres), name);
            }
        }

        static List<Vector2> Line(int n, bool alongX)
        {
            var tiles = new List<Vector2>();
            for (int i = 0; i < n; i++) tiles.Add(alongX ? new Vector2(10 + i * TileM, 4) : new Vector2(10, 4 + i * TileM));
            return tiles;
        }

        static IEnumerable<List<Vector2>> Footprints()
        {
            yield return new List<Vector2> { new Vector2(0.75f, 0.75f) };
            yield return Line(2, true);
            yield return Line(4, false);
            yield return Line(7, true);
            var blob = new List<Vector2>();
            for (int x = 0; x < 3; x++)
                for (int z = 0; z < 3; z++)
                    blob.Add(new Vector2(-6 + x * TileM, -6 + z * TileM));
            blob.Add(new Vector2(-6 + 3 * TileM, -6));
            yield return blob;
        }

        [Test]
        public void PiecesStandOnTheirTilesAndTheSameTilesGiveTheSamePieces()
        {
            foreach (var tiles in Footprints())
                foreach (bool large in new[] { false, true })
                {
                    var placed = CrystalKit.Fill(tiles, TileM, large, 11);
                    var again = CrystalKit.Fill(tiles, TileM, large, 11);
                    Assert.That(placed.Count, Is.GreaterThan(0));
                    Assert.That(again.Count, Is.EqualTo(placed.Count));
                    for (int i = 0; i < placed.Count; i++)
                    {
                        Keep(placed[i].Set);
                        Keep(again[i].Set);
                        Assert.That(again[i].Set.Source.vertexCount, Is.EqualTo(placed[i].Set.Source.vertexCount));
                        Assert.That(again[i].Position, Is.EqualTo(placed[i].Position));
                        // Where it meets the ground, every piece is on one of its structure's tiles (a little over its edge at most).
                        var rot = Quaternion.Euler(0, placed[i].YawDeg, 0);
                        foreach (var v in placed[i].Set.Source.vertices)
                        {
                            if (Mathf.Abs(v.y) > 0.05f) continue;
                            var w = placed[i].Position + rot * v;
                            bool on = false;
                            foreach (var t in tiles)
                                on |= Mathf.Abs(w.x - t.x) <= 0.75f * TileM && Mathf.Abs(w.z - t.y) <= 0.75f * TileM;
                            Assert.That(on, $"{tiles.Count} tiles: ({w.x:F2}, {w.z:F2}) off them");
                        }
                    }
                }
        }

        [Test]
        public void APropBreaksIntoItsKitShardsOnTheImpactFrame()
        {
            var palette = Palette.Parse(LookLibrary.Shared.PaletteJson("ion"));
            var look = new StructureLook(StructureLook.EdgeGlass, palette);
            var props = new CrystalProps(null, look.Material);
            props.Add(Line(3, true), TileM, false, 5);
            var shatter = new StructureShatter();
            var physics = EditorSceneManager.NewPreviewScene();
            try
            {
                shatter.Attach(props.Sites, null, physics, props.Sets);
                int broke = 0;
                shatter.Broke += (t, site) => broke++;
                shatter.Break(10, 0, new Vector3(12, 0, 4), Vector3.left, 1f);
                shatter.Present(10 - 1 / 60.0);
                Assert.That(props.Sites[0][0].enabled, "nothing shows before the impact frame");
                shatter.Present(10);
                Assert.That(props.Sites[0][0].enabled, Is.False);
                Assert.That(broke, Is.EqualTo(1));
                int shards = 0;
                foreach (var set in props.Sets[0]) shards += set.Shards.Length;
                Assert.That(shatter.Shards, Is.EqualTo(shards), "every piece flies in its own shards");
                Assert.That(shatter.Flying, Is.GreaterThan(0));
            }
            finally
            {
                shatter.Dispose();
                EditorSceneManager.ClosePreviewScene(physics);
                props.Dispose();
                look.Dispose();
            }
        }

        [Test]
        public void EveryStructureLookDrawsInThePalettesGem()
        {
            var palette = Palette.Parse(LookLibrary.Shared.PaletteJson("arcade"));
            foreach (var option in LookAxes.Structures.Built)
            {
                var look = new StructureLook(option, palette);
                try
                {
                    Assert.That(look.Material.shader.name, Is.EqualTo("PS/Look/CrystalProp"), option);
                    Assert.That((Vector4)look.Material.GetColor("_Tint"), Is.EqualTo((Vector4)palette.ShatterGem.linear).Using(new Vector4Comparer(1e-4f)), option);
                    Assert.That(look.Refracts, Is.EqualTo(option == StructureLook.RefractiveGem), option);
                    Assert.That(look.Material.IsKeywordEnabled("_REFRACT"), Is.EqualTo(look.Refracts), option);
                    Assert.That(look.Featured.GetFloat("_Body"), Is.GreaterThan(look.Material.GetFloat("_Body")), $"{option}: formations outshine the props");
                    Assert.That(look.Featured.GetFloat("_Edge"), Is.GreaterThanOrEqualTo(look.Material.GetFloat("_Edge")), option);
                }
                finally { look.Dispose(); }
            }
        }

        sealed class Vector4Comparer : IEqualityComparer<Vector4>
        {
            readonly float _tolerance;
            public Vector4Comparer(float tolerance) => _tolerance = tolerance;
            public bool Equals(Vector4 a, Vector4 b) => (a - b).magnitude <= _tolerance;
            public int GetHashCode(Vector4 v) => 0;
        }
    }
}
