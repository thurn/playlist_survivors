using System.Collections.Generic;
using UnityEngine;

namespace Champions.EditorTools.Monsters
{
    /// <summary>
    /// The hero's crystal blade (design.md, The Hero: "with the blade's bright core"), the big cut-crystal sword the neon
    /// edge trailer's knight held in place of its broadsword: demo3d's HeroBlade (git 138684f2), its mesh and grip ported by
    /// review. Made here, no vendor asset: a pommel gem, a crossguard and a long faceted blade with a spine, every triangle
    /// flat. <see cref="MonsterModel.Spawn"/> puts it in the figure's right fist; the baker combines it with the body as a
    /// rigid part it carries, every edge a line, its UV0 the blade's (x 1 on the spine and 0 on the edges, y 0 at the guard
    /// and 1 at the tip, z 1 on the blade), which HeroEdge lights.
    /// </summary>
    public static class CrystalBlade
    {
        /// <summary>The blade's renderer: the baker knows it by this name.</summary>
        public const string Name = "CrystalBlade";
        /// <summary>The blade's length from the guard to the tip, in metres: big, but short enough that the sword clips'
        /// swings stay clear of the body and the ground.</summary>
        public const float BladeM = 1.15f;
        /// <summary>The blade's width at the guard and its thickness at the spine, in metres.</summary>
        public const float WidthM = 0.15f, ThickM = 0.05f;
        /// <summary>The middle of the handle, which sits in the fist (mesh metres, below the guard).</summary>
        const float GripY = -0.06f;
        /// <summary>The handle's offset from the knuckles toward the palm (m): the trailer's grip.</summary>
        const float PalmM = 0.02f;
        const float GuardY = 0.07f;

        /// <summary>
        /// The blade in the fist of the right hand (<see cref="Fist"/>): the handle through it across the knuckles, a little
        /// in toward the palm, the blade leaving on the thumb's side with its edge toward the knuckles, at the character's
        /// scale; the broadsword the hand held before is left out by the model.
        /// </summary>
        public static GameObject Hold(Animator animator)
        {
            var fist = Fist.Of(animator, right: true);
            var hand = fist.Hand;
            float handScale = hand.lossyScale.x, scale = hand.root.lossyScale.x / handScale;
            var grip = fist.Knuckles + fist.Palm * (PalmM / handScale);
            var go = new GameObject(Name);
            go.transform.SetParent(hand, false);
            go.transform.localScale = Vector3.one * scale;
            go.transform.localRotation = fist.Turn;
            go.transform.localPosition = grip - fist.Turn * new Vector3(0, GripY * scale, 0);
            go.AddComponent<MeshFilter>().sharedMesh = Build();
            go.AddComponent<MeshRenderer>();
            return go;
        }

        /// <summary>
        /// The sword in metres, the grip at the origin: +Y along the blade, X across its edges, Z through its flats. UV0 is
        /// the blade's (spine, along, 1) as <see cref="CrystalBlade"/> says.
        /// </summary>
        public static Mesh Build()
        {
            var b = new Builder();

            // The blade: an eight-sided section (two edges, a spine down each flat and a bevel either side of it) in many
            // short rings, tapering a little, then to the point. Every corner but the spine's is nudged its own way, so
            // each triangle tilts on its own and the blade reads as a mosaic of cut faces, not as two flats.
            var rand = new System.Random(7);
            float Nudge(float m) => ((float)rand.NextDouble() - 0.5f) * m;
            const int ringCount = 14, sides = 8;
            var rings = new Vector3[ringCount][];
            for (int k = 0; k < ringCount; k++)
            {
                float u = k / (ringCount - 1f) * 0.9f; // the last ring at 90%, then the point
                float y = GuardY + u * BladeM;
                float w = WidthM * 0.5f * (u < 0.75f ? 1f - 0.2f * u : 0.85f - 1.4f * (u - 0.75f));
                float th = ThickM * 0.5f * (1f - 0.4f * u);
                float j = k == 0 ? 0 : 1; // the root ring stays flat, against the guard
                rings[k] = new[]
                {
                    new Vector3(w, y + Nudge(0.03f) * j, 0),
                    new Vector3(w * (0.5f + Nudge(0.2f) * j), y + Nudge(0.03f) * j, th * (0.75f + Nudge(0.3f) * j)),
                    new Vector3(0, y, th),
                    new Vector3(-w * (0.5f + Nudge(0.2f) * j), y + Nudge(0.03f) * j, th * (0.75f + Nudge(0.3f) * j)),
                    new Vector3(-w, y + Nudge(0.03f) * j, 0),
                    new Vector3(-w * (0.5f + Nudge(0.2f) * j), y + Nudge(0.03f) * j, -th * (0.75f + Nudge(0.3f) * j)),
                    new Vector3(0, y, -th),
                    new Vector3(w * (0.5f + Nudge(0.2f) * j), y + Nudge(0.03f) * j, -th * (0.75f + Nudge(0.3f) * j)),
                };
            }
            var tipPoint = new Vector3(0, GuardY + BladeM, 0);
            b.Axis(Vector3.zero, Vector3.up);
            for (int k = 0; k + 1 < ringCount; k++)
                for (int s = 0; s < sides; s++)
                {
                    int n = (s + 1) % sides;
                    // Alternate the diagonal, so neighbouring facets don't line up into stripes.
                    if (((k + s) & 1) == 0) b.Quad(rings[k][s], rings[k][n], rings[k + 1][n], rings[k + 1][s]);
                    else b.Quad(rings[k][n], rings[k + 1][n], rings[k + 1][s], rings[k][s]);
                }
            var last = rings[ringCount - 1];
            for (int s = 0; s < sides; s++) b.Tri(last[s], last[(s + 1) % sides], tipPoint);
            // The blade's root (hidden in the guard) faces down.
            var first = rings[0];
            var rootCentre = new Vector3(0, GuardY, 0);
            b.Axis(new Vector3(0, GuardY + 0.5f, 0), Vector3.right);
            for (int s = 0; s < sides; s++) b.Tri(first[s], first[(s + 1) % sides], rootCentre);

            // The crossguard: a flat crystal bar across the edges, each arm a long point.
            float gw = 0.2f, gh = 0.035f, gt = 0.045f;
            b.Bipyramid(new Vector3(0, GuardY - 0.01f, 0), new Vector3(gw, 0, 0), new Vector3(0, gh, 0), new Vector3(0, 0, gt), 6);
            // Shards growing from the blade's root past the guard, angled out and up.
            for (int s = 0; s < 4; s++)
            {
                float side = s < 2 ? 1 : -1, len = s % 2 == 0 ? 0.16f : 0.1f;
                var dir = new Vector3(side * (s % 2 == 0 ? 0.55f : 0.35f), 1f, s % 2 == 0 ? 0.25f : -0.3f).normalized;
                var root = new Vector3(side * WidthM * 0.32f, GuardY + 0.03f, 0);
                b.Spike(root, dir, len, 0.022f);
            }
            // The grip, inside the hand, and the pommel gem.
            b.Bipyramid(new Vector3(0, GripY, 0), new Vector3(0.02f, 0, 0), new Vector3(0, 0.085f, 0), new Vector3(0, 0, 0.02f), 6);
            b.Bipyramid(new Vector3(0, GripY - 0.11f, 0), new Vector3(0.035f, 0, 0), new Vector3(0, 0.045f, 0), new Vector3(0, 0, 0.035f), 5);
            return b.ToMesh();
        }

        /// <summary>
        /// Flat triangles, each with its own corners. Every part is convex about an axis, and each triangle faces away from
        /// it (<see cref="Axis"/>), whatever order its corners come in.
        /// </summary>
        sealed class Builder
        {
            readonly List<Vector3> _p = new List<Vector3>(), _n = new List<Vector3>();
            readonly List<Vector3> _blade = new List<Vector3>();
            Vector3 _axisFrom, _axisDir = Vector3.up;

            /// <summary>The axis of the part being built: its triangles face away from it.</summary>
            public void Axis(Vector3 from, Vector3 dir)
            {
                _axisFrom = from;
                _axisDir = dir.normalized;
            }

            public void Tri(Vector3 a, Vector3 b, Vector3 c)
            {
                var n = Vector3.Cross(b - a, c - a).normalized;
                var centroid = (a + b + c) / 3f;
                var onAxis = _axisFrom + _axisDir * Vector3.Dot(centroid - _axisFrom, _axisDir);
                if (Vector3.Dot(n, centroid - onAxis) < 0)
                {
                    (b, c) = (c, b);
                    n = -n;
                }
                Add(a, n);
                Add(b, n);
                Add(c, n);
            }

            public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
            {
                Tri(a, b, c);
                Tri(a, c, d);
            }

            /// <summary>A double pyramid: a ring of <paramref name="sides"/> corners on the ellipse of <paramref name="x"/>
            /// and <paramref name="z"/>, and an apex each way along <paramref name="y"/>.</summary>
            public void Bipyramid(Vector3 c, Vector3 x, Vector3 y, Vector3 z, int sides)
            {
                Axis(c, y);
                var ring = new Vector3[sides];
                for (int i = 0; i < sides; i++)
                {
                    float a = i * Mathf.PI * 2f / sides;
                    ring[i] = c + x * Mathf.Cos(a) + z * Mathf.Sin(a);
                }
                for (int i = 0; i < sides; i++)
                {
                    var p = ring[i];
                    var q = ring[(i + 1) % sides];
                    Tri(p, c + y, q);
                    Tri(q, c - y, p);
                }
            }

            /// <summary>A crystal point: a hexagonal prism from <paramref name="root"/> along <paramref name="dir"/>, its last
            /// third a point.</summary>
            public void Spike(Vector3 root, Vector3 dir, float length, float radius)
            {
                var u = Vector3.Cross(dir, Vector3.forward).normalized;
                if (u.sqrMagnitude < 0.5f) u = Vector3.Cross(dir, Vector3.right).normalized;
                var v = Vector3.Cross(dir, u);
                Axis(root, dir);
                var top = root + dir * length * 0.65f;
                var apex = root + dir * length;
                var lo = new Vector3[6];
                var hi = new Vector3[6];
                for (int i = 0; i < 6; i++)
                {
                    float a = i * Mathf.PI / 3f;
                    var o = (u * Mathf.Cos(a) + v * Mathf.Sin(a)) * radius;
                    lo[i] = root + o * 0.7f;
                    hi[i] = top + o;
                }
                for (int i = 0; i < 6; i++)
                {
                    int j = (i + 1) % 6;
                    Quad(lo[i], hi[i], hi[j], lo[j]);
                    Tri(hi[i], apex, hi[j]);
                }
            }

            void Add(Vector3 p, Vector3 n)
            {
                _p.Add(p);
                _n.Add(n);
                // On the blade, the spine is where |x| is least for the width at that height.
                float spine = Mathf.Clamp01(1f - Mathf.Abs(p.x) / (WidthM * 0.5f));
                float along = Mathf.Clamp01((p.y - GuardY) / BladeM);
                _blade.Add(new Vector3(spine, along, 1));
            }

            public Mesh ToMesh()
            {
                var m = new Mesh { name = Name };
                m.SetVertices(_p);
                m.SetNormals(_n);
                m.SetUVs(0, _blade);
                var idx = new int[_p.Count];
                for (int i = 0; i < idx.Length; i++) idx[i] = i;
                m.SetTriangles(idx, 0);
                m.RecalculateBounds();
                return m;
            }
        }
    }
}
