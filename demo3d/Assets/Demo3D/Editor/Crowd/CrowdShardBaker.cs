using System;
using System.Collections.Generic;
using System.Linq;
using Demo3D.Runtime.Crowd;
using UnityEngine;
using UnityEngine.Rendering;

namespace Demo3D.EditorTools.Crowd
{
    /// <summary>
    /// The crystal shatter's shard sets (crowd.md, Monster styles; epic ps-2iq, Decision 3): each
    /// type's LOD0 style mesh, posed as the crowd holds its death, cut into 20 to 40 thick chunks.
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
    /// - Inside faces carry UV1.x 1 so the shard shader draws them brighter (and only once the
    ///   chunk is released: where an apex pokes past a thin part the assembled shards still match the
    ///   crowd exactly). The skin keeps the style mesh's triangles, normals and UV3 unchanged.
    /// - The collider is convex: its points are the chunk's support points in 42 directions, so
    ///   PhysX cooks a hull of at most 42 points.
    ///
    /// The pose is the one the crowd draws at the hold: the death clip at max(end, impact)
    /// (TimelineEvaluator), interpolated between the 30 Hz bone texture's frames as CrowdSkinning's
    /// ClipRows does under the default cuts (clips30), and skinned as SkinVertex does.
    /// </summary>
    public static class CrowdShardBaker
    {
        public const int MinShards = 20, MaxShards = 40;

        /// <summary>Chunks a type is cut into: more for a taller monster.</summary>
        public static int ShardCount(float heightM) => Mathf.Clamp(24 + (int)(heightM * 4f), MinShards, MaxShards);

        /// <summary>The death clip's held time: where TimelineEvaluator rests a death.</summary>
        public static float HoldTime(CrowdClip death) => Mathf.Clamp(Mathf.Max(death.End, death.Impact), 0, death.Length);

        /// <summary>
        /// Every bone's skinning matrix at the hold, from the 30 Hz texture (the default cuts'),
        /// interpolated as CrowdSkinning.hlsl's ClipRows and BlendBone do.
        /// </summary>
        public static Matrix4x4[] HeldPose(CrowdBaker.TypeBake b, out float holdTime)
        {
            int ci = b.Clips.FindIndex(c => c.Role == "death");
            if (ci < 0) throw new Exception($"CrowdShardBaker: {b.Type.id} has no death clip");
            var clip = b.Clips[ci];
            holdTime = HoldTime(clip);
            const float rate = CrowdLibrary.CutClipRate;
            float t = holdTime;
            int f0 = Math.Min((int)(t * rate), clip.Frames30 - 2);
            float t0 = f0 / rate, t1 = Mathf.Min((f0 + 1) / rate, clip.Length);
            float u = Mathf.Clamp01((t - t0) / Mathf.Max(t1 - t0, 1e-5f));
            var mats = new Matrix4x4[b.Entries.Count];
            for (int e = 0; e < mats.Length; e++)
            {
                var a = Matrix30(b, e, clip.FirstRow30 + f0);
                var c = Matrix30(b, e, clip.FirstRow30 + f0 + 1);
                var m = Matrix4x4.identity;
                for (int i = 0; i < 12; i++) m[i % 3, i / 3] = a[i % 3, i / 3] + (c[i % 3, i / 3] - a[i % 3, i / 3]) * u;
                mats[e] = m;
            }
            return mats;
        }

        static Matrix4x4 Matrix30(CrowdBaker.TypeBake b, int bone, int row)
        {
            var m = Matrix4x4.identity;
            for (int r = 0; r < 3; r++)
            {
                int at = ((row * b.Width) + bone * 3 + r) * 4;
                m.SetRow(r, new Vector4(b.Texels30[at], b.Texels30[at + 1], b.Texels30[at + 2], b.Texels30[at + 3]));
            }
            return m;
        }

        /// <summary>The style mesh's triangles at a pose: three corners each, with their normals and UV3.</summary>
        public sealed class Posed
        {
            public Vector3[] Position, Normal;
            public Vector4[] Bary;
            public int Triangles => Position.Length / 3;
        }

        /// <summary>The style mesh skinned with <paramref name="mats"/> (SkinVertex: weighted matrices, then the point).</summary>
        public static Posed Pose(Mesh style, Matrix4x4[] mats)
        {
            var pos = style.vertices;
            var nor = style.normals;
            var bones = new List<Vector4>(); style.GetUVs(1, bones);
            var weights = new List<Vector4>(); style.GetUVs(2, weights);
            var bary = new List<Vector4>(); style.GetUVs(3, bary);
            var tris = new List<int>();
            for (int sm = 0; sm < style.subMeshCount; sm++) tris.AddRange(style.GetIndices(sm));
            var o = new Posed { Position = new Vector3[tris.Count], Normal = new Vector3[tris.Count], Bary = new Vector4[tris.Count] };
            for (int k = 0; k < tris.Count; k++)
            {
                int v = tris[k];
                var bi = bones[v];
                var bw = weights[v];
                var skin = new Matrix4x4();
                for (int j = 0; j < 4; j++)
                {
                    if (j > 0 && bw[j] <= 0) continue;
                    var m = mats[(int)bi[j]];
                    for (int i = 0; i < 12; i++) skin[i % 3, i / 3] += m[i % 3, i / 3] * bw[j];
                }
                skin[3, 3] = 1;
                o.Position[k] = skin.MultiplyPoint3x4(pos[v]);
                var n = skin.MultiplyVector(nor[v]);
                o.Normal[k] = n.sqrMagnitude > 0 ? n.normalized : nor[v];
                o.Bary[k] = bary[v];
            }
            return o;
        }

        /// <summary>A type's shard set: its style mesh at the held death pose, cut into chunks.</summary>
        public static CrowdShards.Set Bake(CrowdBaker.TypeBake b, Mesh styleLod0, Mesh styleLod2, int seed)
        {
            var mats = HeldPose(b, out float hold);
            var posed = Pose(styleLod0, mats);
            var shadow = Pose(styleLod2, mats);
            var pieces = Cut(posed, ShardCount(b.HeightM), new System.Random(seed));
            var rng = new System.Random(seed ^ 0x5eed);
            var ids = WeldIds(posed);
            var uses = EdgeUses(posed, ids);
            var near = NearOtherPiece(posed, pieces);
            var shards = new CrowdShards.Shard[pieces.Count];
            for (int i = 0; i < pieces.Count; i++) shards[i] = Build(posed, ids, uses, near, pieces[i], $"{b.Type.id}_shard{i}", rng);
            return new CrowdShards.Set { Id = b.Type.id, HoldTime = hold, Shards = shards, HoldShadow = Mesh(shadow, $"{b.Type.id}_hold_shadow") };
        }

        /// <summary>Posed triangles as a mesh (positions and normals, root space).</summary>
        static Mesh Mesh(Posed p, string name)
        {
            var m = new Mesh { name = name };
            if (p.Position.Length > 65535) m.indexFormat = IndexFormat.UInt32;
            m.SetVertices(p.Position);
            m.SetNormals(p.Normal);
            m.SetTriangles(Enumerable.Range(0, p.Position.Length).ToArray(), 0, true);
            return m;
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

        /// <summary>
        /// For each corner, whether a corner of another piece lies within 1.5 cm: where separate shells
        /// (armour on a body) were cut apart without sharing an edge.
        /// </summary>
        static bool[] NearOtherPiece(Posed p, List<List<int>> pieces)
        {
            const float near = 0.015f;
            var pieceOf = new int[p.Triangles];
            for (int i = 0; i < pieces.Count; i++) foreach (int t in pieces[i]) pieceOf[t] = i;
            var grid = new Dictionary<(int, int, int), List<int>>();
            (int, int, int) Cell(Vector3 v) => (Mathf.FloorToInt(v.x / near), Mathf.FloorToInt(v.y / near), Mathf.FloorToInt(v.z / near));
            for (int k = 0; k < p.Position.Length; k++)
            {
                var c = Cell(p.Position[k]);
                if (!grid.TryGetValue(c, out var list)) grid[c] = list = new List<int>();
                list.Add(k);
            }
            var result = new bool[p.Position.Length];
            for (int k = 0; k < p.Position.Length; k++)
            {
                var (cx, cy, cz) = Cell(p.Position[k]);
                int own = pieceOf[k / 3];
                for (int x = -1; x <= 1 && !result[k]; x++)
                    for (int y = -1; y <= 1 && !result[k]; y++)
                        for (int z = -1; z <= 1 && !result[k]; z++)
                            if (grid.TryGetValue((cx + x, cy + y, cz + z), out var list))
                                foreach (int o in list)
                                    if (pieceOf[o / 3] != own && (p.Position[o] - p.Position[k]).sqrMagnitude < near * near) { result[k] = true; break; }
            }
            return result;
        }

        /// <summary>How many triangles use each welded edge (an undirected pair of weld ids).</summary>
        static Dictionary<(int, int), int> EdgeUses(Posed p, int[] ids)
        {
            var uses = new Dictionary<(int, int), int>();
            for (int t = 0; t < p.Triangles; t++)
                for (int k = 0; k < 3; k++)
                {
                    int a = ids[3 * t + k], b = ids[3 * t + (k + 1) % 3];
                    if (a == b) continue;
                    var key = a < b ? (a, b) : (b, a);
                    uses[key] = uses.TryGetValue(key, out int n) ? n + 1 : 1;
                }
            return uses;
        }

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

        static int[] WeldIds(Posed p)
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

        static CrowdShards.Shard Build(Posed p, int[] ids, Dictionary<(int, int), int> uses, bool[] nearOther, List<int> tris, string name, System.Random rng)
        {
            // The patch: its area-weighted centre and mean normal (short where it wraps round a limb).
            Vector3 c = Vector3.zero, nbar = Vector3.zero;
            float areaSum = 0;
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
            }
            if (areaSum > 0) { c /= areaSum; nbar /= areaSum; }
            else c = p.Position[3 * tris[0]];
            float radius = 0;
            foreach (int t in tris)
                for (int k = 0; k < 3; k++) radius = Mathf.Max(radius, (p.Position[3 * t + k] - c).magnitude);
            radius = Mathf.Max(radius, 1e-3f);

            // The apex: under the centre, inside the body (a ray to its far side bounds the depth).
            var inward = nbar.sqrMagnitude > 1e-12f ? -nbar.normalized : Vector3.down;
            float far = Thickness(p, c - inward * 0.002f, inward);
            float depth = float.IsInfinity(far) ? 0.35f * radius : Mathf.Min(0.6f * radius, 0.45f * far);
            var apex = c + inward * depth * Mathf.Clamp01(nbar.magnitude);
            if ((apex - c).magnitude < 0.08f * radius) apex = c + inward * 0.08f * radius; // never flat: PhysX needs a volume
            var pivot = Vector3.Lerp(c, apex, 0.25f); // a pyramid's centroid sits a quarter of the way to its apex

            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var bary = new List<Vector4>();
            var inside = new List<Vector2>();
            var seams = new List<Vector4>();
            var indices = new List<int>();
            void Corner(Vector3 pos, Vector3 nor, Vector4 bc, float flag, Vector4 seam)
            {
                indices.Add(vertices.Count);
                vertices.Add(pos - pivot);
                normals.Add(nor);
                bary.Add(bc);
                inside.Add(new Vector2(flag, 0));
                seams.Add(seam);
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
            // The skin: the style mesh's own corners. Its seams (UV2.xyz) mark the triangle's edges that
            // are cuts: on the piece's boundary and shared with a triangle of another piece, or an
            // open edge whose both ends are within 1.5 cm of another piece (a shell cut from the one
            // beneath it), but not a free open edge of the mesh (a cape's hem). Component i is the
            // edge facing the corner whose barycentric (UV3) is i, so the shard shader finds a
            // fragment's distance to the cut from UV3 alone. A cut is 1; a fifth of the edges inside
            // the piece (by a hash of the welded edge, so both its triangles agree) are hairline
            // cracks at 0.3, so the crazing runs across the chunks too.
            foreach (int t in tris)
            {
                var seam = Vector4.zero;
                for (int k = 0; k < 3; k++)
                {
                    int a = ids[3 * t + k], b = ids[3 * t + (k + 1) % 3];
                    if (a == b) continue;
                    float value;
                    if (directed.TryGetValue((a, b), out int at) && at == 3 * t + k)
                    {
                        if (uses[a < b ? (a, b) : (b, a)] < 2 && !(nearOther[3 * t + k] && nearOther[3 * t + (k + 1) % 3])) continue;
                        value = 1;
                    }
                    else if (!directed.ContainsKey((a, b)) && Hairline(Math.Min(a, b), Math.Max(a, b))) value = 0.3f;
                    else continue;
                    var facing = p.Bary[3 * t + (k + 2) % 3];
                    int axis = facing.x >= facing.y && facing.x >= facing.z ? 0 : facing.y >= facing.z ? 1 : 2;
                    seam[axis] = Mathf.Max(seam[axis], value);
                }
                for (int k = 0; k < 3; k++) Corner(p.Position[3 * t + k], p.Normal[3 * t + k], p.Bary[3 * t + k], 0, seam);
            }
            // Inside faces: each boundary edge a→b, turned round (b, a, apex) to face out of the solid.
            double volume = 0;
            foreach (int t in tris)
            {
                var a = p.Position[3 * t] - apex;
                var b = p.Position[3 * t + 1] - apex;
                var d = p.Position[3 * t + 2] - apex;
                volume += Vector3.Dot(a, Vector3.Cross(b, d)) / 6.0;
            }
            foreach (var kv in directed.OrderBy(kv => kv.Value))
            {
                int at = kv.Value, t = at / 3, k = at % 3;
                var a = p.Position[at];
                var b = p.Position[3 * t + (k + 1) % 3];
                var face = Vector3.Cross(a - b, apex - b);
                var nor = face.sqrMagnitude > 0 ? face.normalized : -inward;
                float r = (float)rng.NextDouble();
                Corner(b, nor, new Vector4(1, 0, 0, r), 1, Vector4.zero);
                Corner(a, nor, new Vector4(0, 1, 0, r), 1, Vector4.zero);
                Corner(apex, nor, new Vector4(0, 0, 1, r), 1, Vector4.zero);
            }

            var mesh = new Mesh { name = name };
            if (vertices.Count > 65535) mesh.indexFormat = IndexFormat.UInt32;
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(1, inside);
            mesh.SetUVs(2, seams);
            mesh.SetUVs(3, bary);
            mesh.SetTriangles(indices, 0, true);
            return new CrowdShards.Shard
            {
                Mesh = mesh, Hull = Hull(vertices, name + "_hull"), Center = pivot,
                Volume = (float)Math.Abs(volume), Size = (vertices.Max(v => v.magnitude)),
            };
        }

        /// <summary>Whether a welded edge inside a piece is a hairline crack: a fifth of them, by hash.</summary>
        static bool Hairline(int a, int b)
        {
            uint x = (uint)a * 73856093u ^ (uint)b * 19349663u;
            x ^= x >> 13; x *= 0x5bd1e995u; x ^= x >> 15;
            return x % 5 == 0;
        }

        /// <summary>The distance along a ray to the posed mesh (infinity if it hits nothing).</summary>
        static float Thickness(Posed p, Vector3 origin, Vector3 dir)
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

        /// <summary>
        /// A convex collider's points: the chunk's support points in the 42 directions of a once
        /// subdivided icosahedron. MeshCollider (convex) hulls a mesh's points, so the triangles are
        /// only a valid fan over them.
        /// </summary>
        static Mesh Hull(List<Vector3> points, string name)
        {
            var hull = new List<Vector3>();
            foreach (var dir in Directions)
            {
                var best = points[0];
                float bd = Vector3.Dot(best, dir);
                foreach (var v in points)
                {
                    float d = Vector3.Dot(v, dir);
                    if (d > bd) { bd = d; best = v; }
                }
                if (!hull.Any(h => (h - best).sqrMagnitude < 1e-10f)) hull.Add(best);
            }
            var fan = new List<int>();
            for (int i = 1; i + 1 < hull.Count; i++) fan.AddRange(new[] { 0, i, i + 1 });
            var m = new Mesh { name = name };
            m.SetVertices(hull);
            m.SetTriangles(fan, 0, true);
            return m;
        }

        static readonly Vector3[] Directions = IcosphereDirections();

        static Vector3[] IcosphereDirections()
        {
            float g = (1f + Mathf.Sqrt(5f)) / 2f;
            var v = new List<Vector3>
            {
                new(-1, g, 0), new(1, g, 0), new(-1, -g, 0), new(1, -g, 0),
                new(0, -1, g), new(0, 1, g), new(0, -1, -g), new(0, 1, -g),
                new(g, 0, -1), new(g, 0, 1), new(-g, 0, -1), new(-g, 0, 1),
            };
            int[] f =
            {
                0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11, 1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8,
                3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9, 4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1,
            };
            var dirs = v.Select(x => x.normalized).ToList();
            for (int i = 0; i < f.Length; i += 3)
                for (int k = 0; k < 3; k++)
                {
                    var mid = (v[f[i + k]] + v[f[i + (k + 1) % 3]]).normalized;
                    if (!dirs.Any(d => (d - mid).sqrMagnitude < 1e-6f)) dirs.Add(mid);
                }
            return dirs.ToArray();
        }
    }
}
