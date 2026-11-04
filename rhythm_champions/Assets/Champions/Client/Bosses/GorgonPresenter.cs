using System;
using System.Collections.Generic;
using Champions.Render;
using Champions.Sim;
using UnityEngine;
using UnityEngine.Rendering;

namespace Champions.Client
{
    /// <summary>
    /// The Gorgon's authored effects (ps-8z4.21; boss-rush.md, The roster: "a sightline cone painted on the floor; petrified
    /// pillars go grey"). Her flavour is her hue (palette.json's type colour, never the danger red, which stays the gaze's
    /// own fill); the petrified stone is the reserved grey:
    /// - <b>Sightlines</b>: while a gaze fills, lines of her hue run along the floor from her eye down the cone's two edges
    ///   and along each shadow's two edges from its stone, so the cone and the safe gaps in it read as one drawing,
    ///   brightening to the strike; on the strike they flash and fade.
    /// - <b>Petrified stone</b>: a column or statue her gaze cracked once is drawn over with grey cracks running up its
    ///   sides and a grey collar at its foot, until her next stare there crumbles it.
    /// - <b>Entrance and death</b>: rays sweep out from her as she arrives; her shatter throws a ring of stone shards on the
    ///   impact frame (design.md: deaths break at once).
    /// Two dynamic meshes a frame in the world's Glow material, in the stills' effect ID pass.
    /// </summary>
    public sealed class GorgonPresenter : IDisposable
    {
        public const float Brightness = 2.0f, StoneBrightness = 1.6f, StrokeM = 0.08f, FloorM = 0.045f;
        const double FlashS = 0.35, DeathS = 0.7;
        const int EntranceRays = 12, DeathShards = 18, Cracks = 3;

        readonly Material _hue, _stone;
        readonly Mesh _hueMesh, _stoneMesh;
        readonly int _layer, _cast;
        readonly List<Vector3> _verts = new List<Vector3>(2048), _stoneVerts = new List<Vector3>(1024);
        readonly List<int> _tris = new List<int>(3072), _stoneTris = new List<int>(1536);
        bool _held;
        double _heldAt, _arrived = double.NaN, _died = double.NaN, _gazeStart = double.NaN, _gazeLand = double.NaN;
        int _gaze = -1;
        Vector2 _diedAt;

        public GorgonPresenter(Palette palette, EnemyRules enemies, int layer = 0)
        {
            var shared = Resources.Load<Material>(HeroMarker.MaterialPath)
                         ?? throw new InvalidOperationException($"GorgonPresenter: no material at Resources/{HeroMarker.MaterialPath}");
            _cast = enemies.IndexOf(GorgonModule.ModuleId);
            var hue = _cast >= 0 ? palette.ForType(GorgonModule.ModuleId) : new Color(0.84f, 0.18f, 1f);
            _hue = new Material(shared) { name = "Gorgon sightlines" };
            _hue.SetColor("_Color", hue.linear * Brightness);
            _hue.SetFloat(IdPass.Id, IdPass.Effect);
            _stone = new Material(shared) { name = "Gorgon petrified stone" };
            _stone.SetColor("_Color", palette.Wall.linear * StoneBrightness);
            _stone.SetFloat(IdPass.Id, IdPass.Effect);
            _hueMesh = new Mesh { name = "Gorgon sightlines" };
            _hueMesh.MarkDynamic();
            _stoneMesh = new Mesh { name = "Gorgon petrified stone" };
            _stoneMesh.MarkDynamic();
            _layer = layer;
        }

        /// <summary>Triangles drawn on the latest frame.</summary>
        public int Drawn { get; private set; }

        public void Take(in SimEvent e)
        {
            switch (e.Kind)
            {
                case SimEventKind.Hold:
                    bool held = e.I2 != 0;
                    if (held && !_held) _heldAt = e.T;
                    _held = held;
                    break;
                case SimEventKind.BossArrive when e.I1 == _cast:
                    _arrived = e.T;
                    _died = _gazeStart = _gazeLand = double.NaN;
                    _gaze = -1;
                    break;
                case SimEventKind.BossDeath when e.I1 == _cast:
                    _died = e.T;
                    _diedAt = new Vector2((float)e.D0, (float)e.D1);
                    break;
                case SimEventKind.BossMove when e.I2 != 0:
                    // Called off before striking (a phase's transition): its sightlines go with it.
                    _gazeLand = double.NaN;
                    break;
            }
        }

        /// <summary>Draws her effects at song time <paramref name="song"/> for <paramref name="camera"/> while her fight is on.</summary>
        public void Present(double song, Simulation sim, Camera camera)
        {
            Drawn = 0;
            var fight = sim.Fight;
            var gorgon = fight.Module as GorgonModule;
            bool dying = !double.IsNaN(_died) && song >= _died && song < _died + DeathS;
            if (gorgon == null || (!fight.Active && !dying)) return;
            if (_held) song = Math.Min(song, _heldAt);
            _verts.Clear();
            _tris.Clear();
            _stoneVerts.Clear();
            _stoneTris.Clear();
            Track(fight, gorgon);
            if (fight.InFight && fight.Bodies.Length > 0 && fight.Bodies[0].Alive)
            {
                fight.At(0, song, out double bx, out double by, out _);
                Entrance(song, new Vector2((float)bx, (float)by), fight);
                Sightlines(song, gorgon);
                Petrified(sim, gorgon);
            }
            if (dying) Death(song);
            Draw(_hueMesh, _hue, _verts, _tris, camera);
            Draw(_stoneMesh, _stone, _stoneVerts, _stoneTris, camera);
        }

        /// <summary>Picks up the gaze laid last: its fill's start and strike.</summary>
        void Track(BossFight fight, GorgonModule gorgon)
        {
            if (gorgon.GazeAttack < 0 || gorgon.GazeAttack == _gaze) return;
            foreach (var h in fight.Hazards)
            {
                if (h.Attack != gorgon.GazeAttack) continue;
                _gaze = h.Attack;
                _gazeStart = h.Telegraph.Start;
                _gazeLand = h.Telegraph.Land;
                return;
            }
        }

        void Draw(Mesh mesh, Material material, List<Vector3> verts, List<int> tris, Camera camera)
        {
            if (verts.Count == 0) return;
            mesh.Clear();
            mesh.indexFormat = verts.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            var rp = new RenderParams(material) { layer = _layer, shadowCastingMode = ShadowCastingMode.Off, receiveShadows = false, camera = camera };
            Graphics.RenderMesh(rp, mesh, 0, Matrix4x4.identity);
            Drawn += tris.Count / 3;
        }

        // ---- Sightlines ----

        /// <summary>The cone's edges and every shadow's edges, brightening as the gaze fills, flashing at its strike.</summary>
        void Sightlines(double song, GorgonModule gorgon)
        {
            if (double.IsNaN(_gazeLand) || song < _gazeStart || song > _gazeLand + FlashS) return;
            float strength = song <= _gazeLand
                ? 0.35f + 0.65f * Mathf.Clamp01((float)((song - _gazeStart) / Math.Max(1e-3, _gazeLand - _gazeStart)))
                : 1.6f * (1 - (float)((song - _gazeLand) / FlashS));
            float width = 1.6f * StrokeM * strength;
            var eye = new Vector2((float)gorgon.GazeX, (float)gorgon.GazeY);
            var centre = new Vector2((float)gorgon.OriginX, (float)gorgon.OriginY);
            double facing = gorgon.GazeFacing;
            var shadows = gorgon.Shadows;
            double half = gorgon.GazeHalf;
            Ray(eye, centre, facing - half, 1.2f, width);
            Ray(eye, centre, facing + half, 1.2f, width);
            foreach (var s in shadows)
            {
                float from = (float)(s.NearM + s.Cover.Radius);
                if (s.From > -half + 1e-4) Ray(eye, centre, facing + s.From, from, width);
                if (s.To < half - 1e-4) Ray(eye, centre, facing + s.To, from, width);
            }
        }

        /// <summary>A line on the floor from <paramref name="eye"/> at <paramref name="angle"/>, from <paramref name="fromM"/> out to the temple's wall.</summary>
        void Ray(Vector2 eye, Vector2 centre, double angle, float fromM, float width)
        {
            var d = new Vector2((float)Math.Cos(angle), (float)Math.Sin(angle));
            // Out to the wall: |eye + s d - centre| = the temple's radius, the far root.
            var p = eye - centre;
            float r = (float)GorgonModule.TempleM - 0.3f, pd = Vector2.Dot(p, d), disc = pd * pd - (p.sqrMagnitude - r * r);
            if (disc <= 0) return;
            float to = -pd + Mathf.Sqrt(disc);
            if (to <= fromM + 0.2f) return;
            Ribbon(_verts, _tris, eye + d * fromM, eye + d * to, width);
        }

        // ---- Petrified stone ----

        /// <summary>Grey cracks up every standing column, spire and statue her gaze has cracked once.</summary>
        void Petrified(Simulation sim, GorgonModule gorgon)
        {
            var arena = sim.Arena;
            if (arena == null || !arena.Solid) return;
            for (int i = 0; i < arena.PropCount; i++)
            {
                ref readonly var p = ref arena.Prop(i);
                if (!p.Standing || gorgon.Cracks(i) == 0) continue;
                var k = arena.Rules.Props[p.Kind];
                float r = (float)(k.Shape == FootprintShape.Circle ? k.Radius : Math.Min(k.HalfX, k.HalfY));
                Crack(new Vector2((float)p.X, (float)p.Y), r, (float)k.HeightM, i);
            }
            foreach (int id in gorgon.RaisedStatues)
            {
                var o = arena.Obstacle(id);
                if (!o.Standing || gorgon.Cracks(id, true) == 0) continue;
                o.Shape.Centre(out double x, out double y);
                Crack(new Vector2((float)x, (float)y), (float)GorgonModule.StatueRadiusM * 0.87f, (float)arena.Rules.WallHeightM, 1000 + id);
            }
        }

        /// <summary>Zigzag cracks up a stone of radius <paramref name="r"/> and <paramref name="height"/>, and a collar at its foot.</summary>
        void Crack(Vector2 at, float r, float height, int seed)
        {
            float skin = r + 0.04f;
            for (int c = 0; c < Cracks; c++)
            {
                float a = 2 * Mathf.PI * (c + Hash(seed * 7 + c) * 0.5f) / Cracks;
                const int steps = 5;
                var prev = Around(at, skin, a, 0);
                for (int k = 1; k <= steps; k++)
                {
                    float jag = (k % 2 == 0 ? 1 : -1) * (0.18f + 0.12f * Hash(seed + c * 11 + k)) / skin;
                    var next = Around(at, skin, a + jag, height * 0.85f * k / steps);
                    Upright(prev, next, StrokeM * 1.1f, at);
                    prev = next;
                }
            }
            // A collar at its foot.
            const int n = 14;
            for (int k = 0; k < n; k++)
            {
                float a0 = 2 * Mathf.PI * k / n, a1 = 2 * Mathf.PI * (k + 1) / n;
                Ribbon(_stoneVerts, _stoneTris, at + new Vector2(Mathf.Cos(a0), Mathf.Sin(a0)) * (skin + 0.12f),
                    at + new Vector2(Mathf.Cos(a1), Mathf.Sin(a1)) * (skin + 0.12f), StrokeM * 1.4f);
            }
        }

        static Vector3 Around(Vector2 at, float r, float a, float y) => new Vector3(at.x + r * Mathf.Cos(a), y, at.y + r * Mathf.Sin(a));

        /// <summary>A stroke on a stone's side from <paramref name="a"/> to <paramref name="b"/>, facing out from its axis.</summary>
        void Upright(Vector3 a, Vector3 b, float width, Vector2 axis)
        {
            var d = b - a;
            var outward = new Vector3(a.x - axis.x, 0, a.z - axis.y).normalized;
            var side = Vector3.Cross(d, outward).normalized * (width / 2);
            int i = _stoneVerts.Count;
            _stoneVerts.Add(a + side);
            _stoneVerts.Add(a - side);
            _stoneVerts.Add(b - side);
            _stoneVerts.Add(b + side);
            _stoneTris.Add(i); _stoneTris.Add(i + 1); _stoneTris.Add(i + 2);
            _stoneTris.Add(i); _stoneTris.Add(i + 2); _stoneTris.Add(i + 3);
        }

        // ---- Entrance and death ----

        void Entrance(double song, Vector2 at, BossFight fight)
        {
            if (double.IsNaN(_arrived) || song < _arrived) return;
            double span = fight.Module.EntranceBeats * fight.BeatS;
            float u = (float)((song - _arrived) / span);
            if (u >= 1) return;
            // Her first look round the temple: rays sweeping out from her, fading as she takes her first move.
            for (int k = 0; k < EntranceRays; k++)
            {
                float a = 2 * Mathf.PI * k / EntranceRays + u * 1.2f;
                var d = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                Ribbon(_verts, _tris, at + d * 1.8f, at + d * (1.8f + 12f * Smooth(u)), StrokeM * 1.4f * (1 - u));
            }
        }

        void Death(double song)
        {
            float u = (float)((song - _died) / DeathS);
            float up = u < 0.15f ? u / 0.15f : 1 - Smooth((u - 0.15f) / 0.85f);
            for (int k = 0; k < DeathShards; k++)
            {
                float a = 2 * Mathf.PI * (k + 0.5f) / DeathShards;
                var dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                Shard(_diedAt + dir * (2.0f + 5.0f * Smooth(u)), dir, up * (0.8f + 0.5f * Hash(k)), 8101 + k);
            }
        }

        /// <summary>One stone shard: a three-sided pyramid leaning along <paramref name="lean"/>.</summary>
        void Shard(Vector2 at, Vector2 lean, float height, int seed)
        {
            if (height <= 0.02f) return;
            float turn = Hash(seed) * 2 * Mathf.PI;
            var tip = new Vector3(at.x + 0.35f * height * lean.x, 1.0f * height, at.y + 0.35f * height * lean.y);
            int i = _verts.Count;
            for (int k = 0; k < 3; k++)
            {
                float a = turn + 2 * Mathf.PI * k / 3;
                _verts.Add(new Vector3(at.x + 0.24f * Mathf.Cos(a), 0, at.y + 0.24f * Mathf.Sin(a)));
            }
            _verts.Add(tip);
            for (int k = 0; k < 3; k++)
            {
                _tris.Add(i + k);
                _tris.Add(i + 3);
                _tris.Add(i + (k + 1) % 3);
            }
        }

        // ---- Mesh pieces ----

        /// <summary>A flat stroke on the floor from <paramref name="a"/> to <paramref name="b"/>, <paramref name="width"/> wide.</summary>
        static void Ribbon(List<Vector3> verts, List<int> tris, Vector2 a, Vector2 b, float width)
        {
            if (width <= 0.005f) return;
            var d = b - a;
            if (d.sqrMagnitude < 1e-8f) return;
            var n = new Vector2(-d.y, d.x).normalized * (width / 2);
            int i = verts.Count;
            verts.Add(new Vector3(a.x + n.x, FloorM, a.y + n.y));
            verts.Add(new Vector3(a.x - n.x, FloorM, a.y - n.y));
            verts.Add(new Vector3(b.x - n.x, FloorM, b.y - n.y));
            verts.Add(new Vector3(b.x + n.x, FloorM, b.y + n.y));
            tris.Add(i); tris.Add(i + 1); tris.Add(i + 2);
            tris.Add(i); tris.Add(i + 2); tris.Add(i + 3);
        }

        static float Hash(int seed)
        {
            float v = Mathf.Sin(seed * 78.233f + 1.37f) * 43758.5453f;
            return v - Mathf.Floor(v);
        }

        static float Smooth(float u)
        {
            u = Mathf.Clamp01(u);
            return u * u * (3 - 2 * u);
        }

        public void Dispose()
        {
            foreach (var o in new UnityEngine.Object[] { _hueMesh, _stoneMesh, _hue, _stone })
            {
                if (o == null) continue;
                if (Application.isPlaying) UnityEngine.Object.Destroy(o);
                else UnityEngine.Object.DestroyImmediate(o);
            }
        }
    }
}
