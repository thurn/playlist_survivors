using System.Collections.Generic;
using System.IO;
using Game.Client.Arena;
using NUnit.Framework;
using PS.Game.Sim.Level;
using PS.Look;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Game.Tests
{
    /// <summary>
    /// The void world's impassable ground (VoidArena.Blocking, BlockedGround) on the game's own arena.json: under each
    /// world option every tile the sim blocks shows, as a crystal piece on the tile where it meets walkable ground and
    /// as gridless floor beyond, and nothing stands on a tile the hero can walk; without a world option nothing is made.
    /// </summary>
    public class VoidArenaTests
    {
        /// <summary>How far a piece may lean over its tiles' edge (m).</summary>
        const float OverM = 0.15f;
        /// <summary>A vertex this high is above the floor, seen.</summary>
        const float SeenM = 0.25f;

        static ArenaData Level => ArenaData.Parse(File.ReadAllText(Path.Combine(Application.dataPath, "..", "Content", "arena.json")));

        static ResolvedLook Look(params string[] flags)
        {
            var args = new List<string> { "player" };
            args.AddRange(flags);
            return ResolvedLook.Resolve(LookFlags.Parse(args.ToArray()));
        }

        [Test]
        public void UnderEveryWorldOptionEveryBlockedTileShowsAndNothingStandsOnWalkableGround()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var level = Level;
            int w = level.Width, h = level.Height;
            var blocked = new bool[w * h];
            foreach (var t in level.Blocked) blocked[t.Y * w + t.X] = true;
            bool Blocked(int x, int y) => x < 0 || y < 0 || x >= w || y >= h || blocked[y * w + x];
            var rim = BlockedGround.Rim(blocked, w, h);
            Assert.That(level.Blocked.Length, Is.GreaterThan(1000), "the arena's dressing blocks its edge");

            foreach (string option in LookAxes.World.Built)
            {
                var look = Look("--look", "edge", "--world", option);
                using var terrain = new StructureLook(StructureLook.EdgeGlass, look.Palette);
                using var ground = VoidArena.Blocking(level, look, terrain.Terrain);
                Assert.That(ground, Is.Not.Null, option);
                Assert.That(ground.Renderers.Count, Is.GreaterThan(0), option);

                // The floor: no grid over every blocked tile (structures' tiles open, as they are once broken).
                var mask = ground.Mask.GetPixelData<byte>(0);
                for (int i = 0; i < w * h; i++)
                    Assert.That(mask[i] > 127, Is.EqualTo(!blocked[i]), $"{option}: the floor's mark at tile ({i % w}, {i / w})");
                using (var floor = new GridFloor(option, look.Palette))
                {
                    floor.SetBlocked(ground);
                    Assert.That(floor.Material.GetTexture("_BlockMask"), Is.SameAs(ground.Mask), option);
                    Assert.That(floor.Material.GetVector("_BlockRect").z, Is.EqualTo(1f / (w * (float)level.TileM)).Within(1e-6f), option);
                }

                // The pieces: something seen on every rim tile, and nothing on a walkable one.
                var seen = new bool[w * h];
                var off = new List<string>();
                foreach (var r in ground.Renderers)
                {
                    var mesh = r.GetComponent<MeshFilter>().sharedMesh;
                    foreach (var v in mesh.vertices)
                    {
                        var p = r.transform.TransformPoint(v);
                        if (p.y < SeenM) continue;
                        float fx = (float)((p.x - level.OriginX) / level.TileM), fy = (float)((p.z - level.OriginY) / level.TileM);
                        int x = Mathf.FloorToInt(fx), y = Mathf.FloorToInt(fy);
                        if (x >= 0 && y >= 0 && x < w && y < h) seen[y * w + x] = true;
                        float over = OverM / (float)level.TileM;
                        bool on = false;
                        foreach (var (dx, dy) in new[] { (0f, 0f), (over, over), (over, -over), (-over, over), (-over, -over) })
                            on |= Blocked(Mathf.FloorToInt(fx + dx), Mathf.FloorToInt(fy + dy));
                        if (!on && off.Count < 5) off.Add($"({p.x:F2}, {p.y:F2}, {p.z:F2})");
                    }
                }
                Assert.That(off, Is.Empty, $"{option}: pieces standing on walkable ground");
                var bare = new List<string>();
                int rims = 0;
                for (int i = 0; i < w * h; i++)
                {
                    if (!rim[i]) continue;
                    rims++;
                    if (!seen[i] && bare.Count < 5) bare.Add($"({i % w}, {i / w})");
                }
                Assert.That(bare, Is.Empty, $"{option}: rim tiles with no piece seen on them");
                Assert.That(ground.RimTiles, Is.EqualTo(rims), option);
            }
        }

        [Test]
        public void WithoutAWorldOptionTheArenasOwnSetShowsItsBlockedGround()
        {
            var level = Level;
            foreach (var look in new[] { Look("--look", "classic"), Look("--look", "edge", "--world", "classic") })
            {
                using var terrain = new StructureLook(StructureLook.EdgeGlass, look.Palette);
                Assert.That(VoidArena.Blocking(level, look, terrain.Terrain), Is.Null);
            }
        }
    }
}
