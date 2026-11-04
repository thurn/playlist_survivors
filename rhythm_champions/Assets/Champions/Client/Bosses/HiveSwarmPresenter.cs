using System;
using System.Collections.Generic;
using Champions.Render;
using Champions.Sim;
using UnityEngine;
using UnityEngine.Rendering;

namespace Champions.Client
{
    /// <summary>
    /// The Hive Queen's authored effects (ps-8z4.8; boss-rush.md, The roster: "drones as tiny two-facet darts; the swarm
    /// draws shapes across the floor"), all in her hue (palette.json's type colour, never the danger red, which stays the
    /// telegraphs' own):
    /// - <b>Drones</b>: a dart every <see cref="SpacingM"/> along each row of hers (<see cref="HiveQueenModule"/>'s travelling
    ///   line hazards) and one per spiral shot. While a row's telegraph fills they stream out of her body to their places in
    ///   the formation, then hold there, bobbing, until it launches and they fly with it.
    /// - <b>The honeycomb</b>: her clearing's floor drawn as hexagonal cells that pulse on every beat.
    /// - <b>Entrance and death</b>: a ring of drones spiralling into her as she arrives, and bursting out of her on the
    ///   impact frame of her shatter (design.md: deaths break at once).
    /// All one dynamic mesh of darts a frame and one still mesh of cells, in the world's Glow material; effects in the
    /// stills' type-ID pass, the cells a structure's.
    /// </summary>
    public sealed class HiveSwarmPresenter : IDisposable
    {
        public const float SpacingM = 0.85f, HeightM = 0.6f, DartM = 0.42f, Brightness = 2.2f;
        const float CellM = 1.9f, CellLineM = 0.08f, CellFloorM = 0.03f;
        const int BurstDarts = 24;
        const double DeathS = 0.6, GatherShare = 0.5;

        readonly Material _darts, _cells;
        readonly Mesh _dartMesh, _cellMesh;
        readonly int _layer, _cast;
        readonly Color _hue;
        readonly List<Vector3> _verts = new List<Vector3>(2048);
        readonly List<int> _tris = new List<int>(3072);
        bool _held;
        double _heldAt, _arrived = double.NaN, _died = double.NaN;
        Vector2 _diedAt;
        Vector2 _cellsAt;
        bool _cellsBuilt;

        public HiveSwarmPresenter(Palette palette, EnemyRules enemies, int layer = 0)
        {
            var shared = Resources.Load<Material>(HeroMarker.MaterialPath)
                         ?? throw new InvalidOperationException($"HiveSwarmPresenter: no material at Resources/{HeroMarker.MaterialPath}");
            _cast = enemies.IndexOf(HiveQueenModule.ModuleId);
            _hue = _cast >= 0 ? palette.ForType(HiveQueenModule.ModuleId) : Color.magenta;
            _darts = new Material(shared) { name = "Hive drones" };
            _darts.SetColor("_Color", _hue.linear * Brightness);
            _darts.SetFloat(IdPass.Id, IdPass.Effect);
            _cells = new Material(shared) { name = "Hive cells" };
            _cells.SetFloat(IdPass.Id, IdPass.Structure);
            _dartMesh = new Mesh { name = "Hive drones" };
            _dartMesh.MarkDynamic();
            _cellMesh = new Mesh { name = "Hive cells" };
            _layer = layer;
        }

        /// <summary>Darts drawn on the latest frame.</summary>
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
                    _died = double.NaN;
                    break;
                case SimEventKind.BossDeath when e.I1 == _cast:
                    _died = e.T;
                    _diedAt = new Vector2((float)e.D0, (float)e.D1);
                    break;
            }
        }

        /// <summary>Draws her effects at song time <paramref name="song"/> for <paramref name="camera"/> while her fight is on.</summary>
        public void Present(double song, Simulation sim, Camera camera)
        {
            Drawn = 0;
            var fight = sim.Fight;
            bool hers = fight.Module is HiveQueenModule;
            bool dying = !double.IsNaN(_died) && song >= _died && song < _died + DeathS;
            if (!hers || (!fight.Active && !dying)) return;
            if (_held) song = Math.Min(song, _heldAt);
            _verts.Clear();
            _tris.Clear();
            var rp = new RenderParams(_darts) { layer = _layer, shadowCastingMode = ShadowCastingMode.Off, receiveShadows = false, camera = camera };

            if (fight.InFight && fight.Bodies.Length > 0 && fight.Bodies[0].Alive)
            {
                fight.At(0, song, out double qx, out double qy, out _);
                var queen = new Vector2((float)qx, (float)qy);
                foreach (var h in fight.Hazards) Drones(h, song, queen);
                Entrance(song, queen, sim);
            }
            if (dying) Burst(song);
            if (_verts.Count > 0)
            {
                _dartMesh.Clear();
                _dartMesh.SetVertices(_verts);
                _dartMesh.SetTriangles(_tris, 0);
                _dartMesh.RecalculateBounds();
                Graphics.RenderMesh(rp, _dartMesh, 0, Matrix4x4.identity);
            }
            if (fight.Active) Cells(song, sim, camera);
        }

        /// <summary>The drones of hazard <paramref name="h"/>: a row's along its line, a shot's one.</summary>
        void Drones(in Hazard h, double song, Vector2 queen)
        {
            if (song < h.Telegraph.Start || song > h.Until) return;
            var s = h.ShapeAt(song);
            var heading = new Vector2((float)h.Vx, (float)h.Vy);
            if (heading.sqrMagnitude < 1e-6) return;
            heading.Normalize();
            // Filling: the drones stream out of her to their places over the first half, then hold.
            double fill = h.Telegraph.Land > h.Telegraph.Start ? (song - h.Telegraph.Start) / ((h.Telegraph.Land - h.Telegraph.Start) * GatherShare) : 1;
            float gather = song >= h.Telegraph.Land ? 1 : Smooth((float)Math.Min(1, fill));
            if (s.Kind == ShapeKind.Path)
            {
                var a = new Vector2((float)s.X, (float)s.Y);
                var b = new Vector2((float)s.X2, (float)s.Y2);
                int n = Math.Max(1, Mathf.CeilToInt(Vector2.Distance(a, b) / SpacingM));
                for (int k = 0; k <= n; k++)
                {
                    var at = Vector2.Lerp(a, b, k / (float)n);
                    Dart(Vector2.Lerp(queen, at, gather), heading, song, h.Id * 31 + k, gather);
                }
            }
            else if (s.Kind == ShapeKind.Circle)
            {
                // A spiral's shot: held just off her body along its heading until it launches.
                var at = song >= h.Telegraph.Land ? new Vector2((float)s.X, (float)s.Y) : queen + heading * 1.6f * gather;
                Dart(at, heading, song, h.Id, 1);
            }
        }

        void Entrance(double song, Vector2 queen, Simulation sim)
        {
            if (double.IsNaN(_arrived) || song < _arrived) return;
            double span = sim.Fight.Module.EntranceBeats * sim.Fight.BeatS;
            float u = (float)((song - _arrived) / span);
            if (u >= 1) return;
            for (int k = 0; k < BurstDarts; k++)
            {
                float a = 2 * Mathf.PI * k / BurstDarts + 3 * (1 - u);
                float r = 12 * Mathf.Pow(1 - u, 1.5f);
                var dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                Dart(queen + dir * r, new Vector2(-dir.y, dir.x), song, k, 1);
            }
        }

        void Burst(double song)
        {
            float u = (float)((song - _died) / DeathS);
            for (int k = 0; k < BurstDarts; k++)
            {
                float a = 2 * Mathf.PI * (k + 0.5f) / BurstDarts;
                var dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                Dart(_diedAt + dir * (1 + 14 * Smooth(u)), dir, song, k, 1 - u);
            }
        }

        /// <summary>One two-facet dart at <paramref name="at"/> pointing along <paramref name="heading"/>, scaled by <paramref name="size"/>.</summary>
        void Dart(Vector2 at, Vector2 heading, double song, int seed, float size)
        {
            if (size <= 0.02f) return;
            float bob = 0.08f * Mathf.Sin((float)(song * 9) + seed * 1.7f);
            var c = new Vector3(at.x, HeightM + bob, at.y);
            var f = new Vector3(heading.x, 0, heading.y) * (DartM * size);
            var r = new Vector3(heading.y, 0, -heading.x) * (DartM * 0.7f * size);
            int i = _verts.Count;
            _verts.Add(c + f);                                     // the point
            _verts.Add(c - f * 0.6f + r);                          // the rear corners
            _verts.Add(c - f * 0.6f - r);
            _verts.Add(c - f * 0.3f + Vector3.up * (DartM * 0.35f * size)); // the spine's crest
            _tris.Add(i); _tris.Add(i + 1); _tris.Add(i + 3);
            _tris.Add(i); _tris.Add(i + 3); _tris.Add(i + 2);
            Drawn++;
        }

        /// <summary>Her clearing's honeycomb, pulsing on each beat.</summary>
        void Cells(double song, Simulation sim, Camera camera)
        {
            var arena = sim.Arena;
            bool standing = arena != null && arena.Solid && arena.Def != null && arena.Def.Id == HiveQueenModule.ArenaId;
            var centre = standing ? new Vector2((float)arena.OriginX, (float)arena.OriginY) : new Vector2((float)sim.Fight.CentreX, (float)sim.Fight.CentreY);
            if (!_cellsBuilt || centre != _cellsAt) BuildCells(centre);
            double since = SinceBeat(sim, song);
            float pulse = Mathf.Exp(-(float)since / 0.16f);
            float form = standing ? (float)arena.FormedAt(song) : 1;
            _cells.SetColor("_Color", _hue.linear * ((0.08f + 0.3f * pulse) * form));
            var rp = new RenderParams(_cells) { layer = _layer, shadowCastingMode = ShadowCastingMode.Off, receiveShadows = false, camera = camera };
            Graphics.RenderMesh(rp, _cellMesh, 0, Matrix4x4.identity);
        }

        static double SinceBeat(Simulation sim, double song)
        {
            var beats = sim.Beats;
            if (beats.Reliable(song))
            {
                double next = beats.NextBeat(song, out int i);
                if (!double.IsNaN(next) && i > 0)
                {
                    double prev = beats.PredictBeat(i - 1);
                    if (!double.IsNaN(prev) && prev <= song) return song - prev;
                }
            }
            return song % 0.5;
        }

        void BuildCells(Vector2 centre)
        {
            _cellsBuilt = true;
            _cellsAt = centre;
            var v = new List<Vector3>();
            var t = new List<int>();
            float limit = (float)HiveQueenModule.ApothemM - 0.6f, s3 = Mathf.Sqrt(3);
            int n = Mathf.CeilToInt((float)HiveQueenModule.CircumM / CellM) + 1;
            for (int q = -n; q <= n; q++)
                for (int r = -n * 2; r <= n * 2; r++)
                {
                    var cell = new Vector2(1.5f * CellM * q, s3 * CellM * (r + q * 0.5f));
                    bool inside = true;
                    for (int k = 0; k < 6 && inside; k++) inside = InHex(cell + Corner(k) * CellM, limit);
                    if (!inside) continue;
                    for (int k = 0; k < 6; k++) Ribbon(v, t, centre + cell + Corner(k) * CellM * 0.94f, centre + cell + Corner(k + 1) * CellM * 0.94f);
                }
            _cellMesh.Clear();
            _cellMesh.indexFormat = v.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            _cellMesh.SetVertices(v);
            _cellMesh.SetTriangles(t, 0);
            _cellMesh.RecalculateBounds();
        }

        static Vector2 Corner(int k) => new Vector2(Mathf.Cos(k * Mathf.PI / 3), Mathf.Sin(k * Mathf.PI / 3));

        /// <summary>Inside a flat-topped hexagon of apothem <paramref name="apothem"/> (the clearing's shape)?</summary>
        static bool InHex(Vector2 p, float apothem)
        {
            for (int k = 0; k < 6; k++)
            {
                float a = Mathf.PI / 6 + k * Mathf.PI / 3;
                if (p.x * Mathf.Cos(a) + p.y * Mathf.Sin(a) > apothem) return false;
            }
            return true;
        }

        static void Ribbon(List<Vector3> v, List<int> t, Vector2 a, Vector2 b)
        {
            var d = (b - a).normalized;
            var n = new Vector2(-d.y, d.x) * (CellLineM / 2);
            int i = v.Count;
            v.Add(new Vector3(a.x + n.x, CellFloorM, a.y + n.y));
            v.Add(new Vector3(a.x - n.x, CellFloorM, a.y - n.y));
            v.Add(new Vector3(b.x - n.x, CellFloorM, b.y - n.y));
            v.Add(new Vector3(b.x + n.x, CellFloorM, b.y + n.y));
            t.Add(i); t.Add(i + 1); t.Add(i + 2);
            t.Add(i); t.Add(i + 2); t.Add(i + 3);
        }

        static float Smooth(float u)
        {
            u = Mathf.Clamp01(u);
            return u * u * (3 - 2 * u);
        }

        public void Dispose()
        {
            foreach (var o in new UnityEngine.Object[] { _dartMesh, _cellMesh, _darts, _cells })
            {
                if (o == null) continue;
                if (Application.isPlaying) UnityEngine.Object.Destroy(o);
                else UnityEngine.Object.DestroyImmediate(o);
            }
        }
    }
}
