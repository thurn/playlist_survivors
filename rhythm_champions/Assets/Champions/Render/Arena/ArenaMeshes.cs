using System.Collections.Generic;
using Champions.Sim;
using UnityEngine;
using UnityEngine.Rendering;

namespace Champions.Render.Arena
{
    /// <summary>
    /// Builds the arena's low-poly facets for ArenaEdge.shader (boss-rush.md, Arenas; design.md, The aim): unshared
    /// triangles, each with its barycentrics (TEXCOORD3) and the edges it draws (TEXCOORD5: x the edge opposite its first
    /// vertex, y the second's, z the third's), so a quad's diagonal and a cap's inner cuts stay unlit and every facet's
    /// outline glows. TEXCOORD0.x is a vertex's place along its outline (0 to 1), which forming reveals in order.
    /// Positions are ground metres: the sim's (x, y) is Unity's (x, z).
    /// </summary>
    public sealed class ArenaMeshes
    {
        readonly List<Vector3> _p = new List<Vector3>(1024);
        readonly List<Vector2> _order = new List<Vector2>(1024);
        readonly List<Vector4> _bary = new List<Vector4>(1024);
        readonly List<Vector3> _edges = new List<Vector3>(1024);
        readonly List<int> _tris = new List<int>(1024);
        readonly List<int> _ear = new List<int>(64);

        static readonly Vector4 B0 = new Vector4(1, 0, 0, 0), B1 = new Vector4(0, 1, 0, 0), B2 = new Vector4(0, 0, 1, 0);

        public void Clear()
        {
            _p.Clear();
            _order.Clear();
            _bary.Clear();
            _edges.Clear();
            _tris.Clear();
        }

        public bool Empty => _tris.Count == 0;

        /// <summary>A triangle drawing its edges ab, bc, ca as flagged; o* each corner's place along the outline.</summary>
        public void Triangle(Vector3 a, Vector3 b, Vector3 c, float oa, float ob, float oc, bool ab, bool bc, bool ca)
        {
            var e = new Vector3(bc ? 1 : 0, ca ? 1 : 0, ab ? 1 : 0);
            int i = _p.Count;
            _p.Add(a); _p.Add(b); _p.Add(c);
            _order.Add(new Vector2(oa, 0)); _order.Add(new Vector2(ob, 0)); _order.Add(new Vector2(oc, 0));
            _bary.Add(B0); _bary.Add(B1); _bary.Add(B2);
            _edges.Add(e); _edges.Add(e); _edges.Add(e);
            _tris.Add(i); _tris.Add(i + 1); _tris.Add(i + 2);
        }

        /// <summary>A quad a b c d (in turn round it), its four sides drawn, its diagonal not.</summary>
        public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, float oa, float ob, float oc, float od)
        {
            Triangle(a, b, c, oa, ob, oc, true, true, false);
            Triangle(a, c, d, oa, oc, od, false, true, true);
        }

        static Vector3 At(double x, double y, float h) => new Vector3((float)x, h, (float)y);

        /// <summary>
        /// An act kit's piece (<see cref="KitPiece"/>) stood at ground (<paramref name="x"/>, <paramref name="y"/>), turned
        /// <paramref name="degrees"/> anticlockwise as a prop's footprint is (<see cref="ArenaShape.Box"/>), its heights scaled
        /// by <paramref name="tall"/>; every edge its design draws, drawn.
        /// </summary>
        public void Piece(KitPiece piece, double x, double y, double degrees, float tall = 1)
        {
            double a = degrees * System.Math.PI / 180;
            float c = (float)System.Math.Cos(a), s = (float)System.Math.Sin(a), fx = (float)x, fy = (float)y;
            Vector3 P(int i)
            {
                var p = piece.Points[i];
                return new Vector3(fx + p.x * c - p.z * s, p.y * tall, fy + p.x * s + p.z * c);
            }
            var t = piece.Triangles;
            var e = piece.Edges;
            for (int k = 0; k < t.Length; k += 3)
                Triangle(P(t[k]), P(t[k + 1]), P(t[k + 2]), 0, 0, 0, e[k + 2] != 0, e[k] != 0, e[k + 1] != 0);
        }

        /// <summary>Each vertex's place along the closed outline (its share of the perimeter from the first).</summary>
        static float[] Orders(double[] xs, double[] ys)
        {
            int n = xs.Length;
            var o = new float[n + 1];
            double total = 0;
            var legs = new double[n];
            for (int i = 0; i < n; i++)
            {
                double dx = xs[(i + 1) % n] - xs[i], dy = ys[(i + 1) % n] - ys[i];
                legs[i] = System.Math.Sqrt(dx * dx + dy * dy);
                total += legs[i];
            }
            double run = 0;
            for (int i = 0; i < n; i++)
            {
                o[i] = (float)(total > 0 ? run / total : 0);
                run += legs[i];
            }
            o[n] = 1;
            return o;
        }

        /// <summary>
        /// The walls round <paramref name="outline"/> (offset by <paramref name="ox"/>, <paramref name="oy"/>): a band
        /// <paramref name="thickness"/> wide outward from it, <paramref name="height"/> tall, every segment's inner face,
        /// top and outer face a facet, its corners mitred.
        /// </summary>
        public void Walls(ArenaShape outline, double ox, double oy, float thickness, float height)
        {
            int n = outline.Count;
            var ix = new double[n];
            var iy = new double[n];
            var px = new double[n];
            var py = new double[n];
            for (int i = 0; i < n; i++)
            {
                ix[i] = outline.X[i] + ox;
                iy[i] = outline.Y[i] + oy;
            }
            for (int i = 0; i < n; i++)
            {
                // Anticlockwise: a leg's outward normal is (dy, -dx); the corner moves out along their mean, mitred.
                int a = (i + n - 1) % n, b = (i + 1) % n;
                Normal(ix[a], iy[a], ix[i], iy[i], out double n0x, out double n0y);
                Normal(ix[i], iy[i], ix[b], iy[b], out double n1x, out double n1y);
                double mx = n0x + n1x, my = n0y + n1y, ml = System.Math.Sqrt(mx * mx + my * my);
                if (ml < 1e-9)
                {
                    mx = n0x;
                    my = n0y;
                    ml = 1;
                }
                mx /= ml;
                my /= ml;
                double miter = thickness / System.Math.Max(0.35, mx * n0x + my * n0y);
                px[i] = ix[i] + mx * miter;
                py[i] = iy[i] + my * miter;
            }
            var o = Orders(ix, iy);
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                float oa = o[i], ob = o[i + 1];
                Vector3 a0 = At(ix[i], iy[i], 0), b0 = At(ix[j], iy[j], 0), a1 = At(ix[i], iy[i], height), b1 = At(ix[j], iy[j], height);
                Vector3 c0 = At(px[i], py[i], 0), d0 = At(px[j], py[j], 0), c1 = At(px[i], py[i], height), d1 = At(px[j], py[j], height);
                Quad(a0, b0, b1, a1, oa, ob, ob, oa);
                Quad(a1, b1, d1, c1, oa, ob, ob, oa);
                Quad(d0, c0, c1, d1, ob, oa, oa, ob);
            }
        }

        static void Normal(double ax, double ay, double bx, double by, out double nx, out double ny)
        {
            double dx = bx - ax, dy = by - ay, l = System.Math.Sqrt(dx * dx + dy * dy);
            nx = l > 1e-12 ? dy / l : 0;
            ny = l > 1e-12 ? -dx / l : 0;
        }

        /// <summary>A prism on <paramref name="shape"/> from <paramref name="bottom"/> to <paramref name="top"/>: its sides and its top cap's outline.</summary>
        public void Prism(ArenaShape shape, double ox, double oy, float bottom, float top)
        {
            int n = shape.Count;
            var o = Orders(shape.X, shape.Y);
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                Vector3 a0 = At(shape.X[i] + ox, shape.Y[i] + oy, bottom), b0 = At(shape.X[j] + ox, shape.Y[j] + oy, bottom);
                Vector3 a1 = At(shape.X[i] + ox, shape.Y[i] + oy, top), b1 = At(shape.X[j] + ox, shape.Y[j] + oy, top);
                Quad(a0, b0, b1, a1, o[i], o[i + 1], o[i + 1], o[i]);
            }
            Cap(shape, ox, oy, top);
        }

        /// <summary>A flat cap on <paramref name="shape"/> at height <paramref name="h"/>, its outline drawn and its inner cuts not (ear clipping: any simple polygon).</summary>
        public void Cap(ArenaShape shape, double ox, double oy, float h, bool spokes = false)
        {
            int n = shape.Count;
            var o = Orders(shape.X, shape.Y);
            if (spokes)
            {
                shape.Centre(out double cx, out double cy);
                var c = At(cx + ox, cy + oy, h);
                for (int i = 0; i < n; i++)
                {
                    int j = (i + 1) % n;
                    Triangle(c, At(shape.X[i] + ox, shape.Y[i] + oy, h), At(shape.X[j] + ox, shape.Y[j] + oy, h), o[i], o[i], o[i + 1], true, true, true);
                }
                return;
            }
            _ear.Clear();
            for (int i = 0; i < n; i++) _ear.Add(i);
            int guard = 0;
            while (_ear.Count > 3 && guard++ < 4 * n)
            {
                int m = _ear.Count;
                for (int k = 0; k < m; k++)
                {
                    int a = _ear[(k + m - 1) % m], b = _ear[k], c = _ear[(k + 1) % m];
                    if (!Ear(shape, a, b, c)) continue;
                    // Its sides on the outline are drawn: consecutive vertices of the polygon.
                    bool ab = (a + 1) % n == b, bc = (b + 1) % n == c, ca = (c + 1) % n == a;
                    Triangle(At(shape.X[a] + ox, shape.Y[a] + oy, h), At(shape.X[b] + ox, shape.Y[b] + oy, h), At(shape.X[c] + ox, shape.Y[c] + oy, h),
                        o[a], o[b], o[c], ab, bc, ca);
                    _ear.RemoveAt(k);
                    break;
                }
            }
            if (_ear.Count == 3)
            {
                int a = _ear[0], b = _ear[1], c = _ear[2];
                Triangle(At(shape.X[a] + ox, shape.Y[a] + oy, h), At(shape.X[b] + ox, shape.Y[b] + oy, h), At(shape.X[c] + ox, shape.Y[c] + oy, h),
                    o[a], o[b], o[c], (a + 1) % n == b, (b + 1) % n == c, (c + 1) % n == a);
            }
        }

        static bool Ear(ArenaShape s, int a, int b, int c)
        {
            double ax = s.X[a], ay = s.Y[a], bx = s.X[b], by = s.Y[b], cx = s.X[c], cy = s.Y[c];
            if ((bx - ax) * (cy - ay) - (by - ay) * (cx - ax) <= 1e-12) return false;
            for (int i = 0; i < s.Count; i++)
            {
                if (i == a || i == b || i == c) continue;
                double px = s.X[i], py = s.Y[i];
                if (Side(ax, ay, bx, by, px, py) >= 0 && Side(bx, by, cx, cy, px, py) >= 0 && Side(cx, cy, ax, ay, px, py) >= 0) return false;
            }
            return true;
        }

        static double Side(double ax, double ay, double bx, double by, double px, double py) => (bx - ax) * (py - ay) - (by - ay) * (px - ax);

        /// <summary>The facets so far as <paramref name="mesh"/>'s (cleared first).</summary>
        public void Into(Mesh mesh)
        {
            mesh.Clear();
            mesh.indexFormat = _p.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.SetVertices(_p);
            mesh.SetUVs(0, _order);
            mesh.SetUVs(3, _bary);
            mesh.SetUVs(5, _edges);
            mesh.SetTriangles(_tris, 0);
            mesh.RecalculateBounds();
        }
    }
}
