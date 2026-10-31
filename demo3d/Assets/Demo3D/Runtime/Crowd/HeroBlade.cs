using System;
using System.Collections.Generic;
using PS.Choreography;
using UnityEngine;
using UnityEngine.Rendering;

namespace Demo3D.Runtime.Crowd
{
    /// <summary>
    /// The hero's crystal blade (crowd.md, The Hero): a big cut-crystal sword, white and glowing, in
    /// place of the knight's broadsword, under every monster style. Its mesh is made here (no vendor
    /// asset): a pommel gem, a crossguard and a long faceted blade with a spine, every triangle
    /// flat with its own barycentrics, as the crowd's style mesh, so CrystalCommon's facets, edges and
    /// glints read on it. CrystalBlade.shader draws it.
    ///
    /// The grip comes from the hand's bones: the handle runs through the fist across the knuckles, the
    /// blade leaving it on the thumb's side with its edge toward the knuckles, as the Sword Combat clips
    /// hold a sword. The broadsword's renderer is turned off, not removed.
    /// </summary>
    public sealed class HeroBlade : IDisposable
    {
        /// <summary>The blade's length from the guard to the tip, in metres: big, but short enough that the
        /// sword clips' swings stay clear of the body and the ground.</summary>
        public const float BladeM = 1.15f;
        /// <summary>The blade's width at the guard and its thickness at the spine, in metres.</summary>
        public const float WidthM = 0.15f, ThickM = 0.05f;
        /// <summary>The middle of the handle, which sits in the fist (mesh metres, below the guard).</summary>
        const float GripY = -0.06f;

        /// <summary>A blow's flare: up at once, gone over this long (s).</summary>
        const float FlareS = 0.22f;

        static readonly int Flare = Shader.PropertyToID("_BladeFlare");

        /// <summary>
        /// `--blade-tune key=value,...`: overrides the blade material's values for a run, to judge a
        /// change without a rebuild. Keys: behind, glow, flare, gain, peak, refraction, facets, the
        /// gem's colour as r, g, b, and the grip's palm, back, pitch, yaw, roll and length (GripTune).
        /// </summary>
        public const string TuneFlag = "--blade-tune";

        static readonly (string Key, string Property)[] TuneKeys =
        {
            ("behind", "_Behind"), ("glow", "_Glow"), ("gain", "_Gain"), ("peak", "_Peak"),
            ("refraction", "_Refraction"), ("facets", "_Facets"),
        };

        float _flareGain = 1f;

        /// <summary>The grip's tuning (`--blade-tune`): the handle's offset toward the palm and back from
        /// the knuckles (m), a turn on top of the fist's axes (degrees: pitch about the edge, yaw about
        /// the blade, roll about the flats) and the sword's scale.</summary>
        struct GripTune { public float PalmM, BackM, Pitch, Yaw, Roll, Length; }
        GripTune _tune = new GripTune { PalmM = 0.02f, Length = 1f };

        readonly Material _material, _motion;
        readonly Mesh _mesh;
        Timeline _blowsOf; // a trailer switches timelines at its cuts
        double[] _blows;   // the hero's blows' impact times in it, ascending

        public HeroBlade(CrowdLibrary lib)
        {
            var shader = lib.StyleShader("Demo3D/Styles/CrystalBlade");
            _material = new Material(shader) { name = "hero_blade", enableInstancing = false };
            _motion = new Material(shader) { name = "hero_blade_motion", renderQueue = (int)RenderQueue.GeometryLast, enableInstancing = false };
            _motion.SetShaderPassEnabled("UniversalForward", false);
            _motion.SetShaderPassEnabled("ShadowCaster", false);
            _mesh = BuildMesh();
            if (Args.Get(TuneFlag) is string tune) Tune(tune);
        }

        void Tune(string pairs)
        {
            var tint = _material.GetColor("_Tint");
            foreach (var pair in pairs.Split(','))
            {
                var kv = pair.Split('=');
                if (kv.Length != 2 || !float.TryParse(kv[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float v))
                    throw new ArgumentException($"{TuneFlag}: bad pair '{pair}'");
                string key = kv[0].Trim();
                int k = Array.FindIndex(TuneKeys, t => t.Key == key);
                if (k >= 0) _material.SetFloat(TuneKeys[k].Property, v);
                else if (key == "flare") _flareGain = v;
                else if (key == "palm") _tune.PalmM = v;
                else if (key == "back") _tune.BackM = v;
                else if (key == "pitch") _tune.Pitch = v;
                else if (key == "yaw") _tune.Yaw = v;
                else if (key == "roll") _tune.Roll = v;
                else if (key == "length") _tune.Length = v;
                else if (key == "r") tint.r = v;
                else if (key == "g") tint.g = v;
                else if (key == "b") tint.b = v;
                else throw new ArgumentException($"{TuneFlag}: no key '{key}' (behind, glow, flare, gain, peak, refraction, facets, r, g, b, palm, back, pitch, yaw, roll, length)");
            }
            _material.SetColor("_Tint", tint);
            Debug.Log($"HeroBlade: {TuneFlag} {pairs}");
        }

        /// <summary>Puts the blade in the hero's right hand (StockCrowd.Created), in its bind pose.</summary>
        public void Attach(GameObject hero)
        {
            var animator = hero.GetComponentInChildren<Animator>();
            var hand = animator != null && animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.RightHand) : null;
            if (hand == null) throw new InvalidOperationException("HeroBlade: the hero has no humanoid right hand");
            var sword = hand.GetComponentInChildren<MeshFilter>(); // the broadsword, turned off below
            float handScale = hand.lossyScale.x;

            // The grip runs through the fist: across the knuckles, from the fused fingers' root to the
            // index finger's, so the blade leaves the fist on the thumb's side, as a held sword does.
            // Its edge faces the way the fingers point (the knuckles' side), and the handle sits a
            // little in from the knuckles toward the palm. All in the hand's space, from the bind pose.
            Vector3 Local(Transform t) => hand.InverseTransformPoint(t.position);
            var index = Bone(animator, hand, HumanBodyBones.RightIndexProximal, "IndexFinger_01");
            var rest = Bone(animator, hand, HumanBodyBones.RightMiddleProximal, "Finger_01")
                ?? Bone(animator, hand, HumanBodyBones.RightRingProximal, null);
            var thumb = Bone(animator, hand, HumanBodyBones.RightThumbProximal, "Thumb_01");
            if (index == null || rest == null || index.childCount == 0)
                throw new InvalidOperationException("HeroBlade: the hero's right hand has no index and middle finger bones");
            Vector3 a = Local(index), r = Local(rest), next = Local(index.GetChild(0));
            var axis = (a - r).normalized;
            var finger = next - a;
            var edge = (finger - Vector3.Dot(finger, axis) * axis).normalized;
            var palm = Vector3.Cross(axis, edge);
            var knuckles = (a + r) * 0.5f;
            if (thumb != null && Vector3.Dot(palm, Local(thumb) - knuckles) < 0) palm = -palm;
            var grip = knuckles + (palm * _tune.PalmM - edge * _tune.BackM) / handScale;

            var turn = Quaternion.LookRotation(Vector3.Cross(edge, axis), axis) * Quaternion.Euler(_tune.Pitch, _tune.Yaw, _tune.Roll);
            var go = new GameObject("CrystalBlade");
            go.transform.SetParent(hand, false);
            // The mesh is in metres: undo the hand's scale (the prefab's, at its scale in the demo). Its
            // handle's middle (GripY) is in the fist.
            go.transform.localScale = Vector3.one * (_tune.Length / handScale);
            go.transform.localRotation = turn;
            go.transform.localPosition = grip - turn * new Vector3(0, GripY * _tune.Length / handScale, 0);
            go.AddComponent<MeshFilter>().sharedMesh = _mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterials = new[] { _material, _motion };
            mr.shadowCastingMode = ShadowCastingMode.On;
            mr.motionVectorGenerationMode = MotionVectorGenerationMode.Object;
            if (sword != null) sword.GetComponent<MeshRenderer>().enabled = false;

            // The shield rides the left hand as the broadsword did, at the hand bone's scale, which
            // drew it about 1 cm wide: give it the character's scale instead.
            var leftHand = animator.GetBoneTransform(HumanBodyBones.LeftHand);
            var shield = leftHand != null ? leftHand.GetComponentInChildren<MeshFilter>() : null;
            if (shield != null)
            {
                shield.transform.localScale = Vector3.one * (hero.transform.lossyScale.x / leftHand.lossyScale.x);
                Debug.Log($"HeroBlade: {shield.name} at the character's scale ({shield.sharedMesh.bounds.size.magnitude * hero.transform.lossyScale.x:F2} m across)");
            }
            Debug.Log($"HeroBlade: a {BladeM * _tune.Length:F2} m crystal blade in {hand.name}: axis {axis:F2}, edge {edge:F2}, palm {palm:F2}, " +
                      $"grip {grip * handScale:F3} m from the wrist" + (sword != null ? $", in place of {sword.name}" : ""));
        }

        static Transform Bone(Animator animator, Transform hand, HumanBodyBones bone, string name)
        {
            var t = animator.GetBoneTransform(bone);
            if (t != null || name == null) return t;
            foreach (Transform c in hand)
                if (c.name == name) return c;
            return null;
        }

        /// <summary>Sets the blow flare for song time t in timeline tl (the world's time, as the hero is posed on).</summary>
        public void Pose(Timeline tl, double t)
        {
            if (tl != _blowsOf)
            {
                _blowsOf = tl;
                _blows = BlowTimes(tl);
            }
            int i = Array.BinarySearch(_blows, t);
            if (i < 0) i = ~i - 1;
            float since = i >= 0 ? (float)(t - _blows[i]) : float.MaxValue;
            _material.SetFloat(Flare, since < FlareS ? _flareGain * (1f - since / FlareS) : 0f);
        }

        static double[] BlowTimes(Timeline tl)
        {
            var blows = new List<double>();
            var trails = new HashSet<int>();
            foreach (var name in new[] { "slash-trail", "thrust-trail", "spin-trail", "slam-shock" })
            {
                int k = Array.IndexOf(tl.EffectNames, name);
                if (k >= 0) trails.Add(k);
            }
            foreach (var e in tl.Effects)
                if (trails.Contains(e.Effect)) blows.Add(e.Time);
            blows.Sort();
            return blows.ToArray();
        }

        /// <summary>
        /// The sword in metres, the grip at the origin: +Y along the blade, X across its edges, Z through
        /// its flats. UV1 is (spine, along): 1 on the spine and 0 on the edges, 0 at the guard and 1 at
        /// the tip. UV3 is each triangle's barycentrics and its own random value.
        /// </summary>
        static Mesh BuildMesh()
        {
            var b = new Builder();
            const float guardY = 0.07f;

            // The blade: an eight-sided section (two edges, a spine down each flat and a bevel either
            // side of it) in many short rings, tapering a little, then to the point. Every corner but
            // the spine's is nudged its own way, so each triangle tilts on its own and the blade reads
            // as a mosaic of cut faces, as the crowd's crystal does, not as two flats.
            var rand = new System.Random(7);
            float Nudge(float m) => ((float)rand.NextDouble() - 0.5f) * m;
            const int ringCount = 14, sides = 8;
            var rings = new Vector3[ringCount][];
            for (int k = 0; k < ringCount; k++)
            {
                float u = k / (ringCount - 1f) * 0.9f; // the last ring at 90%, then the point
                float y = guardY + u * BladeM;
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
            var tipPoint = new Vector3(0, guardY + BladeM, 0);
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
            var rootCentre = new Vector3(0, guardY, 0);
            b.Axis(new Vector3(0, guardY + 0.5f, 0), Vector3.right);
            for (int s = 0; s < sides; s++) b.Tri(first[s], first[(s + 1) % sides], rootCentre);

            // The crossguard: a flat crystal bar across the edges, each arm a long point.
            float gw = 0.2f, gh = 0.035f, gt = 0.045f;
            b.Bipyramid(new Vector3(0, guardY - 0.01f, 0), new Vector3(gw, 0, 0), new Vector3(0, gh, 0), new Vector3(0, 0, gt), 6);
            // Shards growing from the blade's root past the guard, angled out and up.
            for (int s = 0; s < 4; s++)
            {
                float side = s < 2 ? 1 : -1, len = s % 2 == 0 ? 0.16f : 0.1f;
                var dir = new Vector3(side * (s % 2 == 0 ? 0.55f : 0.35f), 1f, s % 2 == 0 ? 0.25f : -0.3f).normalized;
                var root = new Vector3(side * WidthM * 0.32f, guardY + 0.03f, 0);
                b.Spike(root, dir, len, 0.022f);
            }
            // The grip, inside the hand, and the pommel gem.
            b.Bipyramid(new Vector3(0, GripY, 0), new Vector3(0.02f, 0, 0), new Vector3(0, 0.085f, 0), new Vector3(0, 0, 0.02f), 6);
            b.Bipyramid(new Vector3(0, GripY - 0.11f, 0), new Vector3(0.035f, 0, 0), new Vector3(0, 0.045f, 0), new Vector3(0, 0, 0.035f), 5);
            return b.ToMesh();
        }

        /// <summary>
        /// Flat triangles, each with its own corners, barycentrics and random value. Every part is
        /// convex about an axis, and each triangle faces away from it (<see cref="Axis"/>), whatever
        /// order its corners come in.
        /// </summary>
        sealed class Builder
        {
            readonly List<Vector3> _p = new List<Vector3>(), _n = new List<Vector3>();
            readonly List<Vector2> _core = new List<Vector2>();
            readonly List<Vector4> _bary = new List<Vector4>();
            readonly System.Random _rand = new System.Random(11);
            Vector3 _axisFrom, _axisDir = Vector3.up;

            /// <summary>The axis of the part being built: its triangles face away from it.</summary>
            public void Axis(Vector3 from, Vector3 dir) { _axisFrom = from; _axisDir = dir.normalized; }

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
                float v = (float)_rand.NextDouble();
                Add(a, n, new Vector4(1, 0, 0, v));
                Add(b, n, new Vector4(0, 1, 0, v));
                Add(c, n, new Vector4(0, 0, 1, v));
            }

            public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d) { Tri(a, b, c); Tri(a, c, d); }

            /// <summary>A double pyramid: a ring of <paramref name="sides"/> corners on the ellipse of
            /// <paramref name="x"/> and <paramref name="z"/>, and an apex each way along <paramref name="y"/>.</summary>
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

            /// <summary>A crystal point: a hexagonal prism from <paramref name="root"/> along
            /// <paramref name="dir"/>, its last third a point.</summary>
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

            void Add(Vector3 p, Vector3 n, Vector4 bary)
            {
                _p.Add(p);
                _n.Add(n);
                _bary.Add(bary);
                // On the blade, the spine is where |x| is least for the width at that height.
                float spine = Mathf.Clamp01(1f - Mathf.Abs(p.x) / (WidthM * 0.5f));
                float along = Mathf.Clamp01((p.y - 0.07f) / BladeM);
                _core.Add(new Vector2(spine, along));
            }

            public Mesh ToMesh()
            {
                var m = new Mesh { name = "CrystalBlade" };
                m.SetVertices(_p);
                m.SetNormals(_n);
                m.SetUVs(1, _core);
                m.SetUVs(3, _bary);
                var idx = new int[_p.Count];
                for (int i = 0; i < idx.Length; i++) idx[i] = i;
                m.SetTriangles(idx, 0);
                m.RecalculateBounds();
                return m;
            }
        }

        public void Dispose()
        {
            UnityEngine.Object.Destroy(_material);
            UnityEngine.Object.Destroy(_motion);
            UnityEngine.Object.Destroy(_mesh);
        }
    }
}
