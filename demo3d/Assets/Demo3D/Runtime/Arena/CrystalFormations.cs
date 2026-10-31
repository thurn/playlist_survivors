using System;
using System.Collections.Generic;
using Demo3D.Runtime.Crowd;
using PS.Choreography;
using UnityEngine;
using UnityEngine.Rendering;

namespace Demo3D.Runtime.Arena
{
    /// <summary>
    /// A trailer shot's crystal formations on screen (trailer.md, "Crystal formations"; the timeline's
    /// <see cref="CrystalPiece"/>s): each a cluster of glowing hexagonal spires generated in code, no vendor model, the
    /// tallest its <see cref="CrystalPiece.HeightM"/> and their bases within its footprint. Each spire is cut at build time
    /// into chunky shards (stacked slices split round the axis, and the tip), which together are the formation, as
    /// StructureShardBaker's are a kit mesh: <see cref="StructureShatter"/> flies them when the hero's blow smashes it. They
    /// draw in crystal's structure shader in their own warm white-gold, against the horde's coloured gems, brighter than a
    /// crystal wall, their light reaching the bloom. A formation stands only while its shot is on screen (<see cref="Show"/>).
    /// </summary>
    public sealed class CrystalFormations : IDisposable
    {
        /// <summary>The formations' gem colour (linear): their tint, and their break's bursts (BreakMoments.Bursts).</summary>
        public static Color Gem => ColorUtility.TryParseHtmlString("#ffe7b0", out var c) ? c.linear : Color.white;

        /// <summary>Their structure grade (_Gain, _Peak) and crystal's inner glow (_InnerGlow): lit from inside, past the bloom's knee.</summary>
        public const float Gain = 0.55f, Peak = 0.35f, Glow = 1.6f;

        /// <summary>How far a spire's base sinks into the ground (m), so it rises out of it rather than standing on it.</summary>
        public const float SinkM = 0.4f;

        /// <summary>One formation: its placement, its renderer while it stands and the shards it breaks into.</summary>
        public sealed class Formation
        {
            public CrystalPiece Piece;
            public MeshRenderer Renderer;
            public StructureShards.Set Set;
        }

        readonly GameObject _root;
        readonly Material _material;
        readonly List<UnityEngine.Object> _owned = new List<UnityEngine.Object>();
        readonly Dictionary<int, Formation> _byPlacement = new Dictionary<int, Formation>();

        public IReadOnlyCollection<Formation> All => _byPlacement.Values;

        static readonly int PieceId = Shader.PropertyToID("_SL_Piece");
        static readonly int PieceFxId = Shader.PropertyToID("_SL_PieceFx");
        static readonly int HeightId = Shader.PropertyToID("_HeightM");

        /// <summary>
        /// The formations of <paramref name="pieces"/>, hidden until <see cref="Show"/>, under <paramref name="parent"/> (the
        /// shatter's own scene), in crystal's structure shader from <paramref name="lib"/>; out of the depth prepass unless
        /// <paramref name="prepass"/> (the arena's E6 setting).
        /// </summary>
        public CrystalFormations(IReadOnlyList<CrystalPiece> pieces, Transform parent, CrowdLibrary lib, bool prepass)
        {
            _root = new GameObject("CrystalFormations");
            _root.transform.SetParent(parent, false);
            _root.SetActive(false);
            _material = NewMaterial(lib.StructureShader("Demo3D/Structures/Crystal"), prepass);
            _owned.Add(_material);
            var block = new MaterialPropertyBlock();
            for (int i = 0; i < pieces.Count; i++)
            {
                var p = pieces[i];
                int seed = Mathf.RoundToInt(p.Position.X * 73.1f) * 7919 + Mathf.RoundToInt(p.Position.Y * 37.9f) * 104729 + i;
                Build(p.RadiusM, p.HeightM, seed, out var whole, out var shards);
                whole.name = CrystalPiece.Target(i);
                _owned.Add(whole);
                foreach (var s in shards) _owned.Add(s.Mesh);
                var go = new GameObject(whole.name);
                go.transform.SetParent(_root.transform, false);
                go.transform.SetPositionAndRotation(new Vector3(p.Position.X, 0f, p.Position.Y), Quaternion.Euler(0f, (seed & 0xffff) % 360, 0f));
                go.AddComponent<MeshFilter>().sharedMesh = whole;
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterial = _material;
                r.shadowCastingMode = ShadowCastingMode.On;
                r.receiveShadows = true;
                r.lightProbeUsage = LightProbeUsage.BlendProbes;
                r.reflectionProbeUsage = ReflectionProbeUsage.Off;
                r.motionVectorGenerationMode = MotionVectorGenerationMode.Camera;
                var b = r.bounds;
                block.Clear();
                block.SetVector(PieceId, new Vector4(b.center.x, 0f, b.center.z, Mathf.Repeat(seed * 0.618034f, 1f)));
                block.SetVector(PieceFxId, Vector4.zero);
                block.SetFloat(HeightId, p.HeightM);
                r.SetPropertyBlock(block);
                _byPlacement[p.Placement] = new Formation { Piece = p, Renderer = r, Set = new StructureShards.Set { Source = whole, Shards = shards.ToArray() } };
            }
        }

        /// <summary>The formation at <paramref name="placement"/>, or null if none stands there.</summary>
        public Formation Of(int placement) => _byPlacement.TryGetValue(placement, out var f) ? f : null;

        /// <summary>Shows the formations (their shot is on screen) or hides them.</summary>
        public void Show(bool on)
        {
            if (_root.activeSelf != on) _root.SetActive(on);
        }

        /// <summary>A crystal structure material on its own: the formations' gem, grade and glow, with no kit material behind it.</summary>
        static Material NewMaterial(Shader shader, bool prepass)
        {
            var m = new Material(shader) { name = "CrystalFormation", enableInstancing = false };
            m.SetVector("_Tint", Gem);
            m.SetFloat("_InnerGlow", Glow);
            m.SetFloat("_Refraction", 0.05f);
            m.SetFloat("_Facets", 0.7f);
            m.SetFloat("_Gain", Gain);
            m.SetFloat("_Peak", Peak);
            m.SetFloat("_SL_CellScale", 1.1f);
            m.SetTexture("_SL_ClipMap", Texture2D.whiteTexture);
            m.SetVector("_SL_ClipMap_ST", new Vector4(1, 1, 0, 0));
            m.SetFloat("_SL_Cutoff", 0f);
            m.SetFloat("_SL_Canopy", 0f);
            m.SetFloat("_Cull", (float)CullMode.Back);
            if (!prepass)
            {
                m.SetShaderPassEnabled("DepthNormals", false);
                m.SetShaderPassEnabled("DepthOnly", false);
            }
            return m;
        }

        // ------------------------------------------------------------------ geometry

        const int Sides = 6;

        /// <summary>One spire: its base and lean, its radius at the base, its shaft's height and its apex's, and how it is cut.</summary>
        struct Spire
        {
            public Vector3 Base;
            public Quaternion Lean;
            public float Radius, Shaft, Apex, Phase;
            public int Slices, Wedges;
        }

        /// <summary>
        /// A formation's mesh (every spire's skin) and its shards, in its own frame: a main spire of <paramref name="height"/>
        /// at the middle and five to seven shorter ones leaning out round it, their bases within <paramref name="radius"/>.
        /// </summary>
        public static void Build(float radius, float height, int seed, out Mesh whole, out List<StructureShards.Shard> shards)
        {
            var rng = new System.Random(seed);
            float U(float a, float b) => a + (b - a) * (float)rng.NextDouble();
            var spires = new List<Spire>();
            spires.Add(new Spire
            {
                Base = new Vector3(U(-0.08f, 0.08f) * radius, -SinkM, U(-0.08f, 0.08f) * radius),
                Lean = Quaternion.AngleAxis(U(0f, 5f), new Vector3(U(-1f, 1f), 0, U(-1f, 1f)).normalized + Vector3.forward * 1e-3f),
                Radius = 0.4f * radius, Shaft = 0.78f * height, Apex = height, Phase = U(0f, 60f),
                Slices = Mathf.Clamp(Mathf.RoundToInt(height / 1.5f), 2, 5), Wedges = 3,
            });
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
                    Radius = r, Shaft = 0.8f * h, Apex = h, Phase = U(0f, 60f),
                    Slices = Mathf.Clamp(Mathf.RoundToInt(h / 1.5f), 1, 3), Wedges = h > 2.6f ? 2 : 1,
                });
            }

            shards = new List<StructureShards.Shard>();
            var skin = new MeshBuilder();
            foreach (var s in spires) CutSpire(s, rng, shards, skin);
            whole = skin.ToMesh("CrystalFormation");
        }

        /// <summary>A spire's ring corner k at height y (spire frame, tapering toward the tip).</summary>
        static Vector3 Corner(Spire s, float y, int k)
        {
            float a = (s.Phase + 60f * k) * Mathf.Deg2Rad;
            float r = s.Radius * (1f - 0.18f * Mathf.Clamp01(y / s.Shaft));
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
            int per = Sides / s.Wedges;
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
                for (int k = 0; k < Sides; k++) ring.Add(W(Corner(s, s.Shaft, k)));
                float off = s.Radius * 0.12f;
                Vector3 apex = W(new Vector3(((float)rng.NextDouble() - 0.5f) * off, s.Apex, ((float)rng.NextDouble() - 0.5f) * off));
                var solid = new List<Vector3>(ring) { apex };
                Vector3 centre = Centroid(solid);
                for (int k = 0; k < Sides; k++)
                {
                    var tri = new[] { ring[k], ring[(k + 1) % Sides], apex };
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

        /// <summary>Flat-shaded convex faces: each face its own corners, wound outward from a point inside its solid, its inside faces flagged in uv1.x as a baked shard's are.</summary>
        sealed class MeshBuilder
        {
            readonly List<Vector3> _v = new List<Vector3>(), _n = new List<Vector3>();
            readonly List<Vector2> _uv1 = new List<Vector2>();
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
                int b = _v.Count;
                for (int i = 0; i < poly.Length; i++)
                {
                    _v.Add(poly[flip ? poly.Length - 1 - i : i]);
                    _n.Add(nrm);
                    _uv1.Add(new Vector2(cut ? 1f : 0f, 0f));
                }
                // Unity's front faces wind clockwise seen from outside: in its left-handed frame, a fan whose cross product points outward.
                for (int i = 1; i + 1 < poly.Length; i++) { _t.Add(b); _t.Add(b + i); _t.Add(b + i + 1); }
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

        public void Dispose()
        {
            if (_root != null) UnityEngine.Object.Destroy(_root);
            foreach (var o in _owned) if (o != null) UnityEngine.Object.Destroy(o);
            _owned.Clear();
        }
    }
}
