using System;
using System.Collections.Generic;
using System.Linq;
using Game.Client.Crowd;
using UnityEngine;
using UnityEngine.Rendering;

namespace Game.EditorTools.Crowd
{
    /// <summary>
    /// The GPU shatter's shards (unity-game.md, Shatter): demo3d's CrowdShardBaker, copied and owned.
    /// Each type's LOD0 style mesh on its rest pose, cut into 20 to 40 thick chunks that keep the
    /// mesh's skin weights, so the shatter can pose them to whatever pose a monster is in when the
    /// blow lands (<see cref="MonsterShatter"/>). The chunks are joined into one mesh per type, each
    /// corner tagged with its chunk, so a death draws as one instance of it.
    ///
    /// Why the rest pose: a monster breaks at the blow, walking, attacking or flinching, so no single
    /// baked pose matches it. The rest pose is the one every pose is skinned from: cut there and
    /// carry each corner's bones and weights, and the shard shader skins the chunks' corners exactly
    /// as the crowd's shader skins the same corners, from the same palette, so the swap doesn't show
    /// in any pose. It is also the best pose to cut: the limbs stand apart, so a chunk rarely spans
    /// two of them.
    ///
    /// The method is surface patches closed to a point, not Voronoi slicing of a solid. The vendor
    /// meshes are rarely closed: weapons, capes, wings and eyes are open sheets or loose pieces, and
    /// many bodies are several overlapping shells, so a slice has no well-defined cross-section to cap.
    /// Instead:
    /// - Seeds are spread over the surface (best-of-12 candidates, area-weighted, so they keep apart),
    ///   each with a weight; a few are large, so the chunks are varied (a few big, many small).
    ///   Every triangle goes to the seed nearest its centre, divided by the seed's weight.
    /// - A seed's triangles split into connected pieces (by shared welded edges), so no chunk spans
    ///   two limbs; pieces below a fifth of the mean area merge into the neighbour they share most
    ///   edges with, and loose ones too small to stand alone into the nearest piece.
    /// - Each piece is closed by inside faces: every edge on its boundary (a cut, or an open edge of
    ///   the mesh) is joined to an apex inside the body, so every chunk is a closed solid with real
    ///   thickness. The apex sits under the patch's centre along its mean normal, as deep as 0.6 of the
    ///   patch's radius, but no deeper than 0.45 of the body's thickness there (a ray to the far side),
    ///   so it stays inside a limb; on an open sheet (no far side) it is 0.35 of the radius. A patch
    ///   that wraps round a limb has a short mean normal, and its apex stays near its centre.
    /// - The skin keeps the style mesh's triangles, normals and UV3 unchanged. Every corner carries
    ///   bones (UV1) and weights (UV2), as the style mesh does: a skin corner and an inside face's
    ///   corner on the skin its style mesh corner's, the apex the patch's area-weighted blend of
    ///   them, cut to its four largest, so it moves with the patch. UV4 is (inside face 0 or 1, the
    ///   chunk's index).
    /// - Each chunk's pivot (its centroid, a quarter of the way from the patch's centre to the apex)
    ///   carries the apex's bones and weights, so the shatter can pose it too: the chunk turns
    ///   about it as it flies.
    /// </summary>
    public static class CrowdShardBaker
    {
        public const int MinShards = 20, MaxShards = 40;

        /// <summary>Chunks a type is cut into: more for a taller monster.</summary>
        public static int ShardCount(float heightM) => Mathf.Clamp(24 + (int)(heightM * 4f), MinShards, MaxShards);

        /// <summary>The style mesh's triangles on its rest pose: three corners each, with their normals, UV3, bones and weights.</summary>
        public sealed class Posed
        {
            public Vector3[] Position, Normal;
            public Vector4[] Bary, Bones, Weights;
            public int Triangles => Position.Length / 3;
        }

        /// <summary>The style mesh's corners as its triangles list them, on the rest pose (root space, scale 1).</summary>
        public static Posed Rest(Mesh style)
        {
            var pos = style.vertices;
            var nor = style.normals;
            var bones = new List<Vector4>(); style.GetUVs(1, bones);
            var weights = new List<Vector4>(); style.GetUVs(2, weights);
            var bary = new List<Vector4>(); style.GetUVs(3, bary);
            var tris = new List<int>();
            for (int sm = 0; sm < style.subMeshCount; sm++) tris.AddRange(style.GetIndices(sm));
            var o = new Posed
            {
                Position = new Vector3[tris.Count], Normal = new Vector3[tris.Count], Bary = new Vector4[tris.Count],
                Bones = new Vector4[tris.Count], Weights = new Vector4[tris.Count],
            };
            for (int k = 0; k < tris.Count; k++)
            {
                int v = tris[k];
                o.Position[k] = pos[v];
                o.Normal[k] = nor[v];
                o.Bary[k] = bary[v];
                o.Bones[k] = bones[v];
                o.Weights[k] = weights[v];
            }
            return o;
        }

        /// <summary>
        /// A type's shards: its LOD0 style mesh on the rest pose, cut into chunks joined into one mesh
        /// (<see cref="CrowdType.ShardMesh"/>), and each chunk's pivot and size (<see cref="CrowdType.Shards"/>).
        /// </summary>
        public static (Mesh mesh, CrowdShard[] shards) Bake(string id, float heightM, Mesh styleLod0, int seed)
        {
            var rest = Rest(styleLod0);
            var pieces = Cut(rest, ShardCount(heightM), new System.Random(seed));
            var rng = new System.Random(seed ^ 0x5eed);
            var ids = WeldIds(rest);
            var into = new Joined();
            var shards = new CrowdShard[pieces.Count];
            for (int i = 0; i < pieces.Count; i++) shards[i] = Build(rest, ids, pieces[i], i, into, rng);
            var mesh = new Mesh { name = id + "_shards" };
            if (into.Positions.Count > 65535) mesh.indexFormat = IndexFormat.UInt32;
            mesh.SetVertices(into.Positions);
            mesh.SetNormals(into.Normals);
            mesh.SetUVs(1, into.Bones);
            mesh.SetUVs(2, into.Weights);
            mesh.SetUVs(3, into.Bary);
            mesh.SetUVs(4, into.Chunk);
            mesh.SetTriangles(Enumerable.Range(0, into.Positions.Count).ToArray(), 0, true);
            return (mesh, shards);
        }

        /// <summary>The joined mesh's corners, chunk after chunk.</summary>
        sealed class Joined
        {
            public readonly List<Vector3> Positions = new List<Vector3>(), Normals = new List<Vector3>();
            public readonly List<Vector4> Bary = new List<Vector4>(), Bones = new List<Vector4>(), Weights = new List<Vector4>();
            public readonly List<Vector2> Chunk = new List<Vector2>();
        }

        // ---------------------------------------------------------------------------------
        // Cutting the surface into pieces.

        /// <summary>Triangle index lists, one per piece; every triangle in exactly one.</summary>
        public static List<List<int>> Cut(Posed p, int count, System.Random rng)
        {
            int n = p.Triangles;
            var centre = new Vector3[n];
            var area = new float[n];
            double total = 0;
            for (int t = 0; t < n; t++)
            {
                var a = p.Position[3 * t];
                var b = p.Position[3 * t + 1];
                var c = p.Position[3 * t + 2];
                centre[t] = (a + b + c) / 3f;
                area[t] = 0.5f * Vector3.Cross(b - a, c - a).magnitude;
                total += area[t];
            }
            var neighbours = Adjacency(p);

            // Seeds: best of 12 area-weighted candidates, each the farthest from the seeds so far.
            var cumulative = new double[n];
            double run = 0;
            for (int t = 0; t < n; t++) cumulative[t] = run += area[t];
            int Sample()
            {
                double x = rng.NextDouble() * run;
                int i = Array.BinarySearch(cumulative, x);
                return Math.Min(n - 1, i < 0 ? ~i : i);
            }
            var seeds = new List<Vector3> { centre[Sample()] };
            var nearest = new float[n];
            for (int t = 0; t < n; t++) nearest[t] = (centre[t] - seeds[0]).sqrMagnitude;
            while (seeds.Count < count)
            {
                int best = Sample();
                for (int k = 1; k < 12; k++)
                {
                    int c = Sample();
                    if (nearest[c] > nearest[best]) best = c;
                }
                seeds.Add(centre[best]);
                for (int t = 0; t < n; t++) nearest[t] = Mathf.Min(nearest[t], (centre[t] - centre[best]).sqrMagnitude);
            }
            // Weights: one in six seeds grows a large chunk; the rest vary a little.
            var weight = new float[seeds.Count];
            for (int s = 0; s < weight.Length; s++)
                weight[s] = rng.NextDouble() < 1.0 / 6 ? 1.6f + 0.4f * (float)rng.NextDouble() : 0.7f + 0.45f * (float)rng.NextDouble();

            var cell = new int[n];
            for (int t = 0; t < n; t++)
            {
                float best = float.MaxValue;
                for (int s = 0; s < seeds.Count; s++)
                {
                    float d = (centre[t] - seeds[s]).magnitude / weight[s];
                    if (d < best) { best = d; cell[t] = s; }
                }
            }

            // Connected pieces within each cell.
            var piece = Enumerable.Repeat(-1, n).ToArray();
            var pieceArea = new List<double>();
            var stack = new Stack<int>();
            for (int t = 0; t < n; t++)
            {
                if (piece[t] >= 0) continue;
                int id = pieceArea.Count;
                double sum = 0;
                piece[t] = id;
                stack.Push(t);
                while (stack.Count > 0)
                {
                    int u = stack.Pop();
                    sum += area[u];
                    foreach (int v in neighbours[u])
                        if (piece[v] < 0 && cell[v] == cell[u]) { piece[v] = id; stack.Push(v); }
                }
                pieceArea.Add(sum);
            }

            // Merge, smallest first, until no piece is below a fifth of the mean area and there are no
            // more than `count`: each into the piece it touches most (shared edges, and corners within
            // 1.5 cm of another piece's, for separate shells such as armour on a body), or, for a loose
            // part touching none, the piece with the nearest centre.
            int pieces = pieceArea.Count;
            var areaOf = pieceArea.ToArray();
            var centreOf = new Vector3[pieces];
            var trisOf = new List<int>[pieces];
            for (int i = 0; i < pieces; i++) trisOf[i] = new List<int>();
            for (int t = 0; t < n; t++)
            {
                centreOf[piece[t]] += centre[t] * area[t];
                trisOf[piece[t]].Add(t);
            }
            for (int i = 0; i < pieces; i++) centreOf[i] = areaOf[i] > 0 ? centreOf[i] / (float)areaOf[i] : centre[trisOf[i][0]];
            var touch = Touching(p, piece, neighbours, pieces);
            var live = new HashSet<int>(Enumerable.Range(0, pieces));
            double minArea = total / count * 0.2;
            while (live.Count > 1)
            {
                int i = live.OrderBy(x => areaOf[x]).ThenBy(x => x).First();
                if (areaOf[i] >= minArea && live.Count <= count) break;
                int into = -1;
                foreach (var kv in touch[i])
                    if (into < 0 || kv.Value > touch[i][into] || (kv.Value == touch[i][into] && kv.Key < into)) into = kv.Key;
                if (into < 0)
                {
                    float best = float.MaxValue;
                    foreach (int j in live)
                    {
                        if (j == i) continue;
                        float d = (centreOf[j] - centreOf[i]).sqrMagnitude;
                        if (d < best) { best = d; into = j; }
                    }
                }
                double sum = areaOf[into] + areaOf[i];
                if (sum > 0) centreOf[into] = (centreOf[into] * (float)areaOf[into] + centreOf[i] * (float)areaOf[i]) / (float)sum;
                areaOf[into] = sum;
                trisOf[into].AddRange(trisOf[i]);
                foreach (var kv in touch[i])
                {
                    if (kv.Key == into) continue;
                    touch[into][kv.Key] = touch[into].TryGetValue(kv.Key, out int a) ? a + kv.Value : kv.Value;
                    touch[kv.Key][into] = touch[kv.Key].TryGetValue(into, out int b) ? b + kv.Value : kv.Value;
                    touch[kv.Key].Remove(i);
                }
                touch[into].Remove(i);
                live.Remove(i);
            }
            return live.OrderBy(i => i).Select(i => trisOf[i]).ToList();
        }

        /// <summary>
        /// How much each pair of pieces touches: the welded edges they share plus the corners of one
        /// within 1.5 cm of a corner of the other.
        /// </summary>
        static Dictionary<int, int>[] Touching(Posed p, int[] piece, List<int>[] neighbours, int pieces)
        {
            var touch = new Dictionary<int, int>[pieces];
            for (int i = 0; i < pieces; i++) touch[i] = new Dictionary<int, int>();
            void Add(int a, int b)
            {
                if (a == b) return;
                touch[a][b] = touch[a].TryGetValue(b, out int x) ? x + 1 : 1;
            }
            for (int t = 0; t < neighbours.Length; t++)
                foreach (int v in neighbours[t]) Add(piece[t], piece[v]);
            const float near = 0.015f;
            var grid = new Dictionary<(int, int, int), List<int>>();
            (int, int, int) Cell(Vector3 v) => (Mathf.FloorToInt(v.x / near), Mathf.FloorToInt(v.y / near), Mathf.FloorToInt(v.z / near));
            for (int k = 0; k < p.Position.Length; k++)
            {
                var c = Cell(p.Position[k]);
                if (!grid.TryGetValue(c, out var list)) grid[c] = list = new List<int>();
                list.Add(k);
            }
            var seen = new HashSet<int>();
            for (int k = 0; k < p.Position.Length; k++)
            {
                var (cx, cy, cz) = Cell(p.Position[k]);
                int own = piece[k / 3];
                seen.Clear();
                for (int x = -1; x <= 1; x++)
                    for (int y = -1; y <= 1; y++)
                        for (int z = -1; z <= 1; z++)
                        {
                            if (!grid.TryGetValue((cx + x, cy + y, cz + z), out var list)) continue;
                            foreach (int o in list)
                            {
                                int other = piece[o / 3];
                                if (other != own && !seen.Contains(other) && (p.Position[o] - p.Position[k]).sqrMagnitude < near * near)
                                {
                                    seen.Add(other);
                                    Add(own, other);
                                }
                            }
                        }
            }
            return touch;
        }

        /// <summary>A position key that welds corners closer than 0.1 mm (the style mesh repeats every corner).</summary>
        static (long, long, long) Key(Vector3 v) =>
            ((long)Math.Round(v.x * 1e4), (long)Math.Round(v.y * 1e4), (long)Math.Round(v.z * 1e4));

        /// <summary>Each triangle's neighbours across a shared (welded) edge.</summary>
        static List<int>[] Adjacency(Posed p)
        {
            int n = p.Triangles;
            var ids = WeldIds(p);
            var edges = new Dictionary<(int, int), List<int>>();
            for (int t = 0; t < n; t++)
                for (int k = 0; k < 3; k++)
                {
                    int a = ids[3 * t + k], b = ids[3 * t + (k + 1) % 3];
                    if (a == b) continue;
                    var key = a < b ? (a, b) : (b, a);
                    if (!edges.TryGetValue(key, out var list)) edges[key] = list = new List<int>(2);
                    list.Add(t);
                }
            var neighbours = new List<int>[n];
            for (int t = 0; t < n; t++) neighbours[t] = new List<int>(3);
            foreach (var list in edges.Values)
                for (int i = 0; i < list.Count; i++)
                    for (int j = 0; j < list.Count; j++)
                        if (i != j && !neighbours[list[i]].Contains(list[j])) neighbours[list[i]].Add(list[j]);
            return neighbours;
        }

        /// <summary>Each corner's welded id: corners closer than 0.1 mm share one.</summary>
        public static int[] WeldIds(Posed p)
        {
            var map = new Dictionary<(long, long, long), int>();
            var ids = new int[p.Position.Length];
            for (int k = 0; k < ids.Length; k++)
            {
                var key = Key(p.Position[k]);
                if (!map.TryGetValue(key, out int id)) map[key] = id = map.Count;
                ids[k] = id;
            }
            return ids;
        }

        // ---------------------------------------------------------------------------------
        // Closing a piece into a chunk.

        static CrowdShard Build(Posed p, int[] ids, List<int> tris, int index, Joined into, System.Random rng)
        {
            // The patch: its area-weighted centre, mean normal (short where it wraps round a limb) and skin weights.
            Vector3 c = Vector3.zero, nbar = Vector3.zero;
            float areaSum = 0;
            var boneWeight = new Dictionary<int, float>();
            foreach (int t in tris)
            {
                var a = p.Position[3 * t];
                var b = p.Position[3 * t + 1];
                var d = p.Position[3 * t + 2];
                var cross = Vector3.Cross(b - a, d - a);
                float ar = 0.5f * cross.magnitude;
                c += (a + b + d) / 3f * ar;
                nbar += cross * 0.5f;
                areaSum += ar;
                for (int k = 0; k < 3; k++)
                    for (int j = 0; j < 4; j++)
                    {
                        float w = p.Weights[3 * t + k][j];
                        if (w <= 0) continue;
                        int bone = (int)p.Bones[3 * t + k][j];
                        boneWeight[bone] = (boneWeight.TryGetValue(bone, out float x) ? x : 0) + w * Mathf.Max(ar, 1e-9f);
                    }
            }
            if (areaSum > 0) { c /= areaSum; nbar /= areaSum; }
            else c = p.Position[3 * tris[0]];
            float radius = 0;
            foreach (int t in tris)
                for (int k = 0; k < 3; k++) radius = Mathf.Max(radius, (p.Position[3 * t + k] - c).magnitude);
            radius = Mathf.Max(radius, 1e-3f);
            // The apex's weights: the patch's four heaviest bones, renormalized.
            var top = boneWeight.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key).Take(4).ToList();
            float topSum = top.Sum(kv => kv.Value);
            Vector4 apexBones = Vector4.zero, apexWeights = Vector4.zero;
            for (int j = 0; j < top.Count; j++)
            {
                apexBones[j] = top[j].Key;
                apexWeights[j] = top[j].Value / topSum;
            }

            // The apex: under the centre, inside the body (a ray to its far side bounds the depth).
            var inward = nbar.sqrMagnitude > 1e-12f ? -nbar.normalized : Vector3.down;
            float far = Thickness(p, c - inward * 0.002f, inward);
            float depth = float.IsInfinity(far) ? 0.35f * radius : Mathf.Min(0.6f * radius, 0.45f * far);
            var apex = c + inward * depth * Mathf.Clamp01(nbar.magnitude);
            if ((apex - c).magnitude < 0.08f * radius) apex = c + inward * 0.08f * radius; // never flat: it has a volume
            var pivot = Vector3.Lerp(c, apex, 0.25f); // a pyramid's centroid sits a quarter of the way to its apex

            int first = into.Positions.Count;
            void Corner(Vector3 pos, Vector3 nor, Vector4 bc, float flag, Vector4 bi, Vector4 bw)
            {
                into.Positions.Add(pos);
                into.Normals.Add(nor);
                into.Bary.Add(bc);
                into.Chunk.Add(new Vector2(flag, index));
                into.Bones.Add(bi);
                into.Weights.Add(bw);
            }
            var directed = new Dictionary<(int, int), int>(); // boundary edges: welded (from, to), with a corner index
            foreach (int t in tris)
                for (int k = 0; k < 3; k++)
                {
                    int at = 3 * t + k;
                    int a = ids[at], b = ids[3 * t + (k + 1) % 3];
                    if (a == b) continue;
                    if (directed.ContainsKey((b, a))) directed.Remove((b, a)); // shared inside the piece
                    else directed[(a, b)] = at;
                }
            // The skin: the style mesh's own corners.
            foreach (int t in tris)
                for (int k = 0; k < 3; k++)
                {
                    int at = 3 * t + k;
                    Corner(p.Position[at], p.Normal[at], p.Bary[at], 0, p.Bones[at], p.Weights[at]);
                }
            // Inside faces: each boundary edge a→b, turned round (b, a, apex) to face out of the solid.
            foreach (var kv in directed.OrderBy(kv => kv.Value))
            {
                int at = kv.Value, t = at / 3, k = at % 3;
                int bt = 3 * t + (k + 1) % 3;
                var a = p.Position[at];
                var b = p.Position[bt];
                var face = Vector3.Cross(a - b, apex - b);
                var nor = face.sqrMagnitude > 0 ? face.normalized : -inward;
                float r = (float)rng.NextDouble();
                Corner(b, nor, new Vector4(1, 0, 0, r), 1, p.Bones[bt], p.Weights[bt]);
                Corner(a, nor, new Vector4(0, 1, 0, r), 1, p.Bones[at], p.Weights[at]);
                Corner(apex, nor, new Vector4(0, 0, 1, r), 1, apexBones, apexWeights);
            }

            float size = 0;
            for (int k = first; k < into.Positions.Count; k++) size = Mathf.Max(size, (into.Positions[k] - pivot).magnitude);
            return new CrowdShard { Pivot = pivot, Size = size, Bones = apexBones, Weights = apexWeights };
        }

        /// <summary>The distance along a ray to the posed mesh (infinity if it hits nothing).</summary>
        public static float Thickness(Posed p, Vector3 origin, Vector3 dir)
        {
            float best = float.PositiveInfinity;
            for (int t = 0; t < p.Triangles; t++)
            {
                if (RayTriangle(origin, dir, p.Position[3 * t], p.Position[3 * t + 1], p.Position[3 * t + 2], out float d) && d > 0.004f && d < best)
                    best = d;
            }
            return best;
        }

        static bool RayTriangle(Vector3 o, Vector3 d, Vector3 a, Vector3 b, Vector3 c, out float t)
        {
            t = 0;
            var e1 = b - a;
            var e2 = c - a;
            var h = Vector3.Cross(d, e2);
            float det = Vector3.Dot(e1, h);
            if (Mathf.Abs(det) < 1e-12f) return false;
            float inv = 1f / det;
            var s = o - a;
            float u = Vector3.Dot(s, h) * inv;
            if (u < 0 || u > 1) return false;
            var q = Vector3.Cross(s, e1);
            float v = Vector3.Dot(d, q) * inv;
            if (v < 0 || u + v > 1) return false;
            t = Vector3.Dot(e2, q) * inv;
            return true;
        }
    }
}
