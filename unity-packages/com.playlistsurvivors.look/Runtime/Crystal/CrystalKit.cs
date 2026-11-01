using System.Collections.Generic;
using UnityEngine;

namespace PS.Look
{
    /// <summary>
    /// The void world's set dressing (neon-look.md, The Axes &gt; World; Decision 6): crystal only, every piece generated
    /// in code, no vendor model, so all of it is committable. Each piece comes with its shards, cut as it is built, which
    /// together are the piece (<see cref="StructureShards.Set"/>, its <c>Source</c> the whole), for
    /// <see cref="StructureShatter"/> to fly when it breaks.
    /// - <see cref="Formation"/>: a cluster of hexagonal spires round a tall one (demo3d's crystal formations, moved in).
    /// - <see cref="Shard"/>: one leaning spire. <see cref="Obelisk"/>: a square tapering pillar with a pyramid cap.
    /// - <see cref="Ring"/>: a standing ring of diamond-section segments, half sunk, an arch over its line.
    /// - <see cref="Wall"/>: a low hedge of spires along a line.
    /// <see cref="Fill"/> fits pieces to a structure's tiles, so what the sim routes round is what stands.
    ///
    /// Every face is flat-shaded and fanned from its centre: uv2.x is 0 on the face's own edges and 1 at its centre, so a
    /// shader draws the facets' real edges and not the fan's (the structure looks' edge lines); uv1.x is 1 on a shard's
    /// inside faces, as a baked shard's are. A piece's frame has the ground at y 0, its spires' bases sunk
    /// <see cref="SinkM"/> into it.
    /// </summary>
    public static class CrystalKit
    {
        /// <summary>How far a spire's base sinks into the ground (m), so it rises out of it rather than standing on it.</summary>
        public const float SinkM = 0.4f;

        /// <summary>A piece placed on the ground: its shards (with the whole), where it stands and its yaw (degrees).</summary>
        public struct Placement
        {
            public StructureShards.Set Set;
            public Vector3 Position;
            public float YawDeg;
            /// <summary>Its top above the ground (m).</summary>
            public float HeightM => Set.Source.bounds.max.y;
        }

        /// <summary>
        /// A formation: a main spire of <paramref name="height"/> at the middle and five to seven shorter ones leaning out
        /// round it, their bases within <paramref name="radius"/>.
        /// </summary>
        public static StructureShards.Set Formation(float radius, float height, int seed)
        {
            var rng = new System.Random(seed);
            float U(float a, float b) => a + (b - a) * (float)rng.NextDouble();
            var spires = new List<Spire>
            {
                new Spire
                {
                    Base = new Vector3(U(-0.08f, 0.08f) * radius, -SinkM, U(-0.08f, 0.08f) * radius),
                    Lean = Quaternion.AngleAxis(U(0f, 5f), new Vector3(U(-1f, 1f), 0, U(-1f, 1f)).normalized + Vector3.forward * 1e-3f),
                    Radius = 0.4f * radius, Shaft = 0.78f * height, Apex = height, Phase = U(0f, 60f), Taper = 0.18f,
                    Sides = 6, Slices = Mathf.Clamp(Mathf.RoundToInt(height / 1.5f), 2, 5), Wedges = 3,
                },
            };
            int n = 5 + rng.Next(3);
            float turn = U(0f, 360f);
            for (int i = 0; i < n; i++)
            {
                float a = (turn + i * 360f / n + U(-14f, 14f)) * Mathf.Deg2Rad;
                var dir = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                float r = U(0.2f, 0.3f) * radius, h = U(0.3f, 0.62f) * height;
                float d = Mathf.Min(U(0.42f, 0.6f) * radius, 0.95f * radius - r);
                spires.Add(new Spire
                {
                    Base = dir * d + Vector3.down * SinkM,
                    Lean = Quaternion.AngleAxis(U(16f, 34f), Vector3.Cross(Vector3.up, dir)),
                    Radius = r, Shaft = 0.8f * h, Apex = h, Phase = U(0f, 60f), Taper = 0.18f,
                    Sides = 6, Slices = Mathf.Clamp(Mathf.RoundToInt(h / 1.5f), 1, 3), Wedges = h > 2.6f ? 2 : 1,
                });
            }
            return Cut("CrystalFormation", spires, rng);
        }

        /// <summary>One spire of <paramref name="height"/>, five- or six-sided, leaning 8 to 20 degrees, its base <paramref name="radius"/> across.</summary>
        public static StructureShards.Set Shard(float radius, float height, int seed)
        {
            var rng = new System.Random(seed);
            float U(float a, float b) => a + (b - a) * (float)rng.NextDouble();
            int sides = rng.Next(2) == 0 ? 5 : 6;
            float a0 = U(0f, 2 * Mathf.PI);
            var spire = new Spire
            {
                Base = Vector3.down * SinkM,
                Lean = Quaternion.AngleAxis(U(8f, 20f), new Vector3(Mathf.Cos(a0), 0, Mathf.Sin(a0))),
                Radius = radius, Shaft = 0.8f * height, Apex = height, Phase = U(0f, 72f), Taper = 0.22f,
                Sides = sides, Slices = Mathf.Clamp(Mathf.RoundToInt(height / 1.3f), 2, 4), Wedges = sides == 6 ? 2 : 1,
            };
            return Cut("CrystalShard", new List<Spire> { spire }, rng);
        }

        /// <summary>A square pillar of <paramref name="height"/> tapering to a pyramid cap, square to the grid, <paramref name="radius"/> from its axis to a corner.</summary>
        public static StructureShards.Set Obelisk(float radius, float height, int seed)
        {
            var rng = new System.Random(seed);
            float U(float a, float b) => a + (b - a) * (float)rng.NextDouble();
            float shaft = 0.84f * height;
            var spire = new Spire
            {
                Base = Vector3.down * SinkM,
                Lean = Quaternion.AngleAxis(U(0f, 3f), new Vector3(U(-1f, 1f), 0, U(-1f, 1f)).normalized + Vector3.forward * 1e-3f),
                Radius = radius, Shaft = shaft, Apex = height, Phase = 45f, Taper = 0.32f,
                Sides = 4, Slices = Mathf.Clamp(Mathf.RoundToInt(height / 0.9f), 2, 5), Wedges = 2,
            };
            return Cut("CrystalObelisk", new List<Spire> { spire }, rng);
        }

        /// <summary>
        /// A ring <paramref name="diameter"/> across standing in the piece's x-y plane, its lower part under the ground:
        /// segments of a diamond section <paramref name="thickness"/> across, each a shard.
        /// </summary>
        public static StructureShards.Set Ring(float diameter, float thickness, int seed)
        {
            var rng = new System.Random(seed);
            float r = 0.5f * (diameter - thickness), cy = 0.82f * r, half = 0.5f * thickness;
            int segments = Mathf.Clamp(Mathf.RoundToInt(2 * Mathf.PI * r / 0.8f), 8, 18);
            float turn = (float)rng.NextDouble() * Mathf.PI * 2 / segments;
            Vector3 At(float a, float radial, float z) => new Vector3(Mathf.Cos(a) * (r + radial), cy + Mathf.Sin(a) * (r + radial), z);
            var shards = new List<StructureShards.Shard>();
            var skin = new MeshBuilder();
            for (int k = 0; k < segments; k++)
            {
                float a0 = turn + k * 2 * Mathf.PI / segments, a1 = a0 + 2 * Mathf.PI / segments;
                if (cy + r * Mathf.Max(Mathf.Sin(a0), Mathf.Sin(a1)) + half < -0.05f) continue; // wholly under the ground
                // The diamond's corners at each end: outer, front, inner, back.
                var e0 = new[] { At(a0, half, 0), At(a0, 0, half), At(a0, -half, 0), At(a0, 0, -half) };
                var e1 = new[] { At(a1, half, 0), At(a1, 0, half), At(a1, -half, 0), At(a1, 0, -half) };
                var solid = new List<Vector3>(e0);
                solid.AddRange(e1);
                var centre = Centroid(solid);
                var shard = new MeshBuilder();
                for (int c = 0; c < 4; c++)
                {
                    var quad = new[] { e0[c], e0[(c + 1) % 4], e1[(c + 1) % 4], e1[c] };
                    shard.Face(quad, centre, false);
                    skin.Face(quad, centre, false);
                }
                shard.Face(e0, centre, true);
                shard.Face(e1, centre, true);
                shards.Add(shard.ToShard(centre, solid));
            }
            return new StructureShards.Set { Source = skin.ToMesh("CrystalRing"), Shards = shards.ToArray() };
        }

        /// <summary>A low hedge of spires <paramref name="length"/> long along the piece's x axis, the tallest about <paramref name="height"/>.</summary>
        public static StructureShards.Set Wall(float length, float height, int seed)
        {
            var rng = new System.Random(seed);
            float U(float a, float b) => a + (b - a) * (float)rng.NextDouble();
            int n = Mathf.Max(2, Mathf.RoundToInt(length / 0.9f));
            float step = length / n, radius = Mathf.Clamp(0.6f * step, 0.35f, 0.62f);
            var spires = new List<Spire>();
            for (int i = 0; i < n; i++)
            {
                float u = (i + 0.5f) / n;
                // Taller in the middle, with a ragged top.
                float h = height * (0.55f + 0.45f * Mathf.Sin(u * Mathf.PI)) * U(0.8f, 1.05f);
                float side = rng.Next(2) == 0 ? -1f : 1f;
                spires.Add(new Spire
                {
                    Base = new Vector3(-0.5f * length + (i + 0.5f) * step + U(-0.12f, 0.12f) * step, -SinkM, U(-0.1f, 0.1f)),
                    Lean = Quaternion.AngleAxis(side * U(4f, 12f), Vector3.right) * Quaternion.AngleAxis(U(-6f, 6f), Vector3.forward),
                    Radius = radius * U(0.85f, 1.1f), Shaft = 0.78f * h, Apex = h, Phase = U(0f, 60f), Taper = 0.2f,
                    Sides = 6, Slices = Mathf.Clamp(Mathf.RoundToInt(h / 1.2f), 1, 2), Wedges = h > 1.6f ? 2 : 1,
                });
            }
            return Cut("CrystalWall", spires, rng);
        }

        /// <summary>
        /// Pieces standing on a structure's <paramref name="tiles"/> (their centres on the ground, metres, a tile
        /// <paramref name="tileM"/> square), within them, so the tiles the sim blocks are the ones a piece stands on:
        /// - one tile: an obelisk;
        /// - a line of two: a shard on each; of three to five: a ring arching along it or a wall; longer: a wall;
        /// - any other shape: a formation as wide as the shape allows at its middle, and a shard on each tile beyond it.
        /// A <paramref name="large"/> structure (two or three breaking hits) stands taller. The same tiles and
        /// <paramref name="seed"/> give the same pieces.
        /// </summary>
        public static List<Placement> Fill(IReadOnlyList<Vector2> tiles, float tileM, bool large, int seed)
        {
            var placed = new List<Placement>();
            if (tiles.Count == 0) return placed;
            var rng = new System.Random(seed);
            float U(float a, float b) => a + (b - a) * (float)rng.NextDouble();
            Vector2 min = tiles[0], max = tiles[0], sum = Vector2.zero;
            foreach (var t in tiles)
            {
                min = Vector2.Min(min, t);
                max = Vector2.Max(max, t);
                sum += t;
            }
            var centre = sum / tiles.Count;
            float eps = 0.1f * tileM, lift = large ? 1.25f : 1f;
            void Add(StructureShards.Set set, Vector2 at, float yaw) =>
                placed.Add(new Placement { Set = set, Position = new Vector3(at.x, 0, at.y), YawDeg = yaw });

            if (tiles.Count == 1)
            {
                Add(Obelisk(0.42f * tileM, U(2.3f, 3f) * lift, rng.Next()), centre, 0f);
                return placed;
            }
            bool alongX = max.y - min.y < eps, alongZ = max.x - min.x < eps;
            if (alongX || alongZ)
            {
                float length = tiles.Count * tileM, yaw = alongX ? 0f : 90f;
                if (tiles.Count == 2)
                    foreach (var t in tiles) Add(Shard(0.3f * tileM, U(1.8f, 2.6f) * lift, rng.Next()), t, U(0f, 360f));
                else if (tiles.Count <= 5 && rng.Next(2) == 0)
                    Add(Ring(0.95f * length, 0.42f, rng.Next()), centre, yaw);
                else
                    Add(Wall(0.92f * length, U(1.5f, 2.1f) * lift, rng.Next()), centre, yaw);
                return placed;
            }
            float radius = Mathf.Max(0.6f * tileM, 0.5f * Mathf.Min(max.x - min.x, max.y - min.y) + 0.3f * tileM);
            Add(Formation(radius, U(3f, 3.6f) * lift, rng.Next()), centre, U(0f, 360f));
            foreach (var t in tiles)
                if ((t - centre).magnitude > radius + 0.25f * tileM)
                    Add(Shard(0.3f * tileM, U(1.6f, 2.4f) * lift, rng.Next()), t, U(0f, 360f));
            return placed;
        }

        /// <summary>Destroys a generated set's meshes (the whole and every shard's).</summary>
        public static void Destroy(StructureShards.Set set)
        {
            if (set == null) return;
            DestroyObject(set.Source);
            foreach (var s in set.Shards) DestroyObject(s.Mesh);
        }

        static void DestroyObject(Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) Object.Destroy(o);
            else Object.DestroyImmediate(o);
        }

        // ------------------------------------------------------------------ spires

        /// <summary>One spire: its base and lean, its radius at the base and taper, its shaft's height and its apex's, and how it is cut.</summary>
        struct Spire
        {
            public Vector3 Base;
            public Quaternion Lean;
            public float Radius, Shaft, Apex, Phase, Taper;
            public int Sides, Slices, Wedges; // Wedges divides Sides
        }

        static StructureShards.Set Cut(string name, List<Spire> spires, System.Random rng)
        {
            var shards = new List<StructureShards.Shard>();
            var skin = new MeshBuilder();
            foreach (var s in spires) CutSpire(s, rng, shards, skin);
            return new StructureShards.Set { Source = skin.ToMesh(name), Shards = shards.ToArray() };
        }

        /// <summary>A spire's ring corner k at height y (spire frame, tapering toward the tip).</summary>
        static Vector3 Corner(Spire s, float y, int k)
        {
            float a = (s.Phase + 360f / s.Sides * k) * Mathf.Deg2Rad;
            float r = s.Radius * (1f - s.Taper * Mathf.Clamp01(y / s.Shaft));
            return new Vector3(Mathf.Cos(a) * r, y, Mathf.Sin(a) * r);
        }

        /// <summary>A spire's shards: its shaft in slices, each split round the axis into wedges, and its tip; each one's skin also goes into <paramref name="skin"/>.</summary>
        static void CutSpire(Spire s, System.Random rng, List<StructureShards.Shard> shards, MeshBuilder skin)
        {
            Vector3 W(Vector3 p) => s.Base + s.Lean * p;
            var cuts = new float[s.Slices + 1];
            for (int i = 0; i <= s.Slices; i++)
            {
                float u = i / (float)s.Slices;
                if (i > 0 && i < s.Slices) u += ((float)rng.NextDouble() - 0.5f) * 0.3f / s.Slices;
                cuts[i] = u * s.Shaft;
            }
            int per = s.Sides / s.Wedges;
            for (int i = 0; i < s.Slices; i++)
                for (int w = 0; w < s.Wedges; w++)
                {
                    var shard = new MeshBuilder();
                    float y0 = cuts[i], y1 = cuts[i + 1];
                    int k0 = w * per, k1 = k0 + per;
                    var bottom = new List<Vector3>();
                    var top = new List<Vector3>();
                    if (s.Wedges > 1) { bottom.Add(W(new Vector3(0, y0, 0))); top.Add(W(new Vector3(0, y1, 0))); }
                    for (int k = k0; k <= (s.Wedges > 1 ? k1 : k1 - 1); k++) { bottom.Add(W(Corner(s, y0, k))); top.Add(W(Corner(s, y1, k))); }
                    var solid = new List<Vector3>(bottom);
                    solid.AddRange(top);
                    Vector3 centre = Centroid(solid);
                    for (int k = k0; k < k1; k++)
                    {
                        var quad = new[] { W(Corner(s, y0, k)), W(Corner(s, y0, k + 1)), W(Corner(s, y1, k + 1)), W(Corner(s, y1, k)) };
                        shard.Face(quad, centre, false);
                        skin.Face(quad, centre, false);
                    }
                    shard.Face(bottom.ToArray(), centre, true);
                    shard.Face(top.ToArray(), centre, true);
                    if (s.Wedges > 1)
                    {
                        shard.Face(new[] { bottom[0], bottom[1], top[1], top[0] }, centre, true);
                        shard.Face(new[] { bottom[0], bottom[bottom.Count - 1], top[top.Count - 1], top[0] }, centre, true);
                    }
                    shards.Add(shard.ToShard(centre, solid));
                }
            // The tip: a pyramid over the shaft's last ring, its apex a little off the axis.
            {
                var shard = new MeshBuilder();
                var ring = new List<Vector3>();
                for (int k = 0; k < s.Sides; k++) ring.Add(W(Corner(s, s.Shaft, k)));
                float off = s.Radius * 0.12f;
                Vector3 apex = W(new Vector3(((float)rng.NextDouble() - 0.5f) * off, s.Apex, ((float)rng.NextDouble() - 0.5f) * off));
                var solid = new List<Vector3>(ring) { apex };
                Vector3 centre = Centroid(solid);
                for (int k = 0; k < s.Sides; k++)
                {
                    var tri = new[] { ring[k], ring[(k + 1) % s.Sides], apex };
                    shard.Face(tri, centre, false);
                    skin.Face(tri, centre, false);
                }
                shard.Face(ring.ToArray(), centre, true);
                shards.Add(shard.ToShard(centre, solid));
            }
        }

        static Vector3 Centroid(List<Vector3> points)
        {
            var c = Vector3.zero;
            foreach (var p in points) c += p;
            return c / points.Count;
        }

        /// <summary>
        /// Flat-shaded convex faces: each face its own corners plus its centre, wound outward from a point inside its
        /// solid and fanned from the centre, uv2.x 0 on its edges and 1 at its centre, its inside faces flagged in uv1.x.
        /// </summary>
        sealed class MeshBuilder
        {
            readonly List<Vector3> _v = new List<Vector3>(), _n = new List<Vector3>();
            readonly List<Vector2> _uv1 = new List<Vector2>(), _uv2 = new List<Vector2>();
            readonly List<int> _t = new List<int>();

            public void Face(Vector3[] poly, Vector3 inside, bool cut)
            {
                var c = Vector3.zero;
                foreach (var p in poly) c += p;
                c /= poly.Length;
                var nrm = Vector3.zero;
                for (int i = 0; i < poly.Length; i++) nrm += Vector3.Cross(poly[i] - c, poly[(i + 1) % poly.Length] - c);
                if (nrm.sqrMagnitude < 1e-12f) return;
                bool flip = Vector3.Dot(nrm, c - inside) < 0;
                nrm = (flip ? -nrm : nrm).normalized;
                var flag = new Vector2(cut ? 1f : 0f, 0f);
                int b = _v.Count;
                _v.Add(c);
                _n.Add(nrm);
                _uv1.Add(flag);
                _uv2.Add(new Vector2(1f, 0f));
                for (int i = 0; i < poly.Length; i++)
                {
                    _v.Add(poly[flip ? poly.Length - 1 - i : i]);
                    _n.Add(nrm);
                    _uv1.Add(flag);
                    _uv2.Add(Vector2.zero);
                }
                // Unity's front faces wind clockwise seen from outside: in its left-handed frame, a fan whose cross product points outward.
                for (int i = 0; i < poly.Length; i++) { _t.Add(b); _t.Add(b + 1 + i); _t.Add(b + 1 + (i + 1) % poly.Length); }
            }

            public Mesh ToMesh(string name, Vector3 origin = default)
            {
                var m = new Mesh { name = name };
                var v = new List<Vector3>(_v.Count);
                foreach (var p in _v) v.Add(p - origin);
                m.SetVertices(v);
                m.SetNormals(_n);
                m.SetUVs(0, new List<Vector2>(new Vector2[_v.Count]));
                m.SetUVs(1, _uv1);
                m.SetUVs(2, _uv2);
                m.SetTriangles(_t, 0);
                m.RecalculateBounds();
                return m;
            }

            /// <summary>The shard: its mesh about <paramref name="centre"/>, its volume (fanned from the centre) and its reach from it.</summary>
            public StructureShards.Shard ToShard(Vector3 centre, List<Vector3> solid)
            {
                float volume = 0, size = 0;
                for (int i = 0; i < _t.Count; i += 3)
                    volume += Mathf.Abs(Vector3.Dot(_v[_t[i]] - centre, Vector3.Cross(_v[_t[i + 1]] - centre, _v[_t[i + 2]] - centre))) / 6f;
                foreach (var p in solid) size = Mathf.Max(size, (p - centre).magnitude);
                return new StructureShards.Shard { Mesh = ToMesh("CrystalShard", centre), Center = centre, Volume = volume, Size = size };
            }
        }
    }
}
