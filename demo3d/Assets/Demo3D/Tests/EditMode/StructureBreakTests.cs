using System.Collections.Generic;
using System.IO;
using System.Linq;
using Demo3D.Choreography;
using Demo3D.EditorTools.Arena;
using Demo3D.EditorTools.Structures;
using Demo3D.Runtime.Arena;
using Demo3D.Runtime.Crowd;
using Demo3D.Runtime.Playback;
using NUnit.Framework;
using PS.Choreography;
using UnityEngine;

namespace Demo3D.Tests
{
    /// <summary>
    /// The structure breaks on screen (epic ps-suy, bead ps-suy.3): the walls schedule's breaks as moments with their
    /// feel, bursts and sounds, and the shards a structure's mesh is cut into.
    /// </summary>
    public class StructureBreakTests
    {
        static Timeline _walls;
        static ChoreographyData _data;

        [OneTimeSetUp]
        public void Load()
        {
            string root = ChoreographyFiles.FindRepoRoot();
            var level = ChoreographyFiles.LoadLevel(root, ChoreographyFiles.LevelFile, null, "walls");
            _data = ChoreographyFiles.LoadData(root, level.Layout);
            _walls = Choreographer.Run(ChoreographyFiles.LoadSong(root, level.Song), level, _data);
        }

        [Test]
        public void EachBoltIsOneMomentOfThePiecesItBreaks()
        {
            var moments = BreakMoments.Of(_walls, _data.Layout);
            Assert.That(moments.Moments.Sum(m => m.Pieces.Length), Is.EqualTo(_walls.Breaks.Length));
            Assert.That(moments.Count, Is.EqualTo(_walls.Breaks.Select(b => (b.Time, b.Projectile)).Distinct().Count()));
            foreach (var m in moments.Moments)
            {
                Assert.That(m.SizeM, Is.InRange(1f, 12f), m.Target);
                Assert.That(m.Hit.y, Is.EqualTo(BreakMoments.BoltHeightM));
                Assert.That(m.Blow.magnitude, Is.EqualTo(1f).Within(1e-3f));
                // The bolt strikes the wall it breaks.
                float nearest = m.Pieces.Min(p => Vector2.Distance(new Vector2(p.Base.x, p.Base.z), new Vector2(m.Hit.x, m.Hit.z)) - p.WidthM);
                Assert.That(nearest, Is.LessThan(1f), m.Target);
            }
        }

        [Test]
        public void ARunWithoutBreaksHasNoMoments()
        {
            Assert.That(BreakMoments.Of(new Timeline(), _data.Layout).Count, Is.Zero);
        }

        [Test]
        public void ABreakFlashesPunchesAndShakesItsBeat()
        {
            var moments = BreakMoments.Of(_walls, _data.Layout);
            var amounts = ArenaLighting.Parse(File.ReadAllText(ArenaBaker.LayoutPath)).PostFeel;
            var post = new PostFeel(_walls, amounts, true, moments.Flashes());
            var camera = new CameraFeel(_walls, null, moments.Shakes());
            var plain = new CameraFeel(_walls);
            foreach (var m in moments.Moments)
            {
                Assert.That(post.Flash(m.Time + 1e-4), Is.GreaterThanOrEqualTo(BreakMoments.FlashOf(m.SizeM) * 0.99f), m.Target);
                double beat = _walls.Map.TimeOfBeatIndex(System.Math.Floor(_walls.Map.BeatIndexAt(m.Time)) + 1) - m.Time;
                Assert.That(camera.Punch(m.Time + beat / 4), Is.LessThanOrEqualTo(1f - CameraFeel.BreakPunch + 1e-4f), m.Target);
                // The shake's envelope over the beat's first moments: never weaker than the plain run's, and as big as the break's.
                float peak = 0, plainPeak = 0;
                for (int k = 0; k < 40; k++)
                {
                    double t = m.Time + k * 0.002;
                    peak = Mathf.Max(peak, camera.Shake(t).magnitude);
                    plainPeak = Mathf.Max(plainPeak, plain.Shake(t).magnitude);
                }
                Assert.That(peak, Is.GreaterThan(plainPeak), m.Target);
                Assert.That(peak, Is.LessThanOrEqualTo(Mathf.Max(BreakMoments.ShakeOf(m.SizeM), CameraFeel.ShakeCap) + 1e-4f), m.Target);
            }
        }

        [Test]
        public void EveryStylesBurstsAreLibraryEffectsAndItsSoundsAreInTheBank()
        {
            var moments = BreakMoments.Of(_walls, _data.Layout);
            var effects = new HashSet<string>(JsonValue.Parse(File.ReadAllText("Assets/Demo3D/Data/effects.json"))["effects"].Items.Select(e => e["name"].AsString()));
            var bank = new HashSet<string>(Catalog.SoundNames().Concat(ShatterSounds.Names()));
            foreach (var style in StructureStyle.Names.Where(n => n != StructureStyle.Match))
            {
                var bursts = moments.Bursts(style, StructureStyle.CrystalGem);
                Assert.That(bursts.Count, Is.EqualTo(moments.Count + _walls.Breaks.Length), style + ": one at each strike and one at each piece");
                foreach (var b in bursts) Assert.That(effects, Does.Contain(b.Effect), style);
                var sounds = moments.Sounds(style, _walls.Map);
                Assert.That(sounds, Is.Not.Empty, style);
                foreach (var c in sounds) Assert.That(bank, Does.Contain(c.Name), style);
            }
        }

        [Test]
        public void MomentsOnOneBeatShareTheirVoicesLouder()
        {
            var moments = BreakMoments.Of(_walls, _data.Layout);
            var layers = BreakMoments.Layers("baseline").Length;
            var cues = moments.Sounds("baseline", _walls.Map);
            var beats = moments.Moments.Select(m => (long)System.Math.Floor(_walls.Map.BeatIndexAt(m.Time) + 1e-6)).Distinct().Count();
            Assert.That(cues.Count, Is.EqualTo(beats * layers));
            Assert.That(beats, Is.LessThan(moments.Count), "the walls schedule breaks two runs on some beats");
            float lone = 0.6f * BreakMoments.Layers("baseline")[0].level; // BreakMoments.Sounds' gain for one piece
            Assert.That(cues.Max(c => c.Gain), Is.GreaterThan(lone), "a beat of several pieces is louder than one");
        }

        /// <summary>A closed box of the given size, its faces two triangles each, as the kit's walls are a few large triangles.</summary>
        static Mesh Box(Vector3 size)
        {
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var src = cube.GetComponent<MeshFilter>().sharedMesh;
            var mesh = new Mesh { name = "wall" };
            mesh.vertices = src.vertices.Select(v => Vector3.Scale(v, size)).ToArray();
            mesh.normals = src.normals;
            mesh.uv = src.uv;
            mesh.triangles = src.triangles;
            Object.DestroyImmediate(cube);
            return mesh;
        }

        static float SkinArea(Mesh m)
        {
            var v = m.vertices;
            var inside = new List<Vector2>();
            m.GetUVs(1, inside);
            float area = 0;
            for (int s = 0; s < m.subMeshCount; s++)
            {
                var t = m.GetTriangles(s);
                for (int i = 0; i < t.Length; i += 3)
                    if (inside[t[i]].x < 0.5f) area += 0.5f * Vector3.Cross(v[t[i + 1]] - v[t[i]], v[t[i + 2]] - v[t[i]]).magnitude;
            }
            return area;
        }

        [Test]
        public void AWallIsCutIntoSolidChunksThatTogetherAreItsSurface()
        {
            var size = new Vector3(4f, 3f, 0.5f);
            var wall = Box(size);
            float area = 2 * (size.x * size.y + size.y * size.z + size.x * size.z);
            var set = StructureShardBaker.Bake(wall, 1f, 7);
            Assert.That(set.Source, Is.SameAs(wall));
            Assert.That(set.Shards.Length, Is.InRange(StructureShardBaker.MinShards, StructureShardBaker.ShardCount(area)));
            Assert.That(set.Shards.Length, Is.GreaterThan(StructureShardBaker.ShardCount(area) / 2), "the split surface gives the seeds room");
            Assert.That(set.Shards.Sum(s => SkinArea(s.Mesh)), Is.EqualTo(area).Within(area * 0.01f), "every triangle in one chunk");
            var box = new Bounds(Vector3.zero, size + Vector3.one * 0.02f);
            foreach (var s in set.Shards)
            {
                var inside = new List<Vector2>();
                s.Mesh.GetUVs(1, inside);
                Assert.That(inside.Any(u => u.x > 0.5f), Is.True, "closed by inside faces");
                Assert.That(s.Mesh.bounds.center.magnitude, Is.LessThan(1e-3f), "its corners about its centre");
                Assert.That(s.Volume, Is.GreaterThan(0f));
                foreach (var v in s.Mesh.vertices) Assert.That(box.Contains(v + s.Center), Is.True, "inside the wall");
                Object.DestroyImmediate(s.Mesh);
            }
            Object.DestroyImmediate(wall);
        }
    }
}
