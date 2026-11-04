using System;
using System.Collections.Generic;
using Champions.Render;
using Champions.Sim;
using UnityEngine;
using UnityEngine.Rendering;

namespace Champions.Client
{
    /// <summary>
    /// The Rootwarden's authored effects (ps-8z4.12; boss-rush.md, The roster: "roots as long, branching line strokes"), all
    /// in its hue (palette.json's type colour, never the danger red, which stays the telegraphs' own):
    /// - <b>Creeping roots</b>: while each of its telegraphs fills, a branching line stroke crawls along the floor from its
    ///   body to the marked ground, arriving as the fill completes, so every echo is visibly the boss reaching for where
    ///   the hero stood.
    /// - <b>Eruptions</b>: on each landing, faceted spikes burst up out of the ground it marked (a cluster on a circle, a
    ///   row along a chained path, a crown round the growing ring of a quake), held while the hazard hurts, then sinking.
    /// - <b>Entrance and death</b>: roots crawl in to its body as it arrives; its shatter throws a ring of spikes up round
    ///   it on the impact frame (design.md: deaths break at once).
    /// One dynamic mesh a frame in the world's Glow material, in the stills' effect ID pass.
    /// </summary>
    public sealed class RootwardenPresenter : IDisposable
    {
        public const float Brightness = 2.0f, StrokeM = 0.09f, FloorM = 0.04f;
        const float SpikeM = 1.5f, SpikeBaseM = 0.28f, SpacingM = 0.9f;
        const double RiseS = 0.07, SinkS = 0.35, DeathS = 0.7;
        const int Segments = 7, EntranceRoots = 9, DeathSpikes = 18;

        readonly Material _material;
        readonly Mesh _mesh;
        readonly int _layer, _cast;
        readonly List<Vector3> _verts = new List<Vector3>(4096);
        readonly List<int> _tris = new List<int>(6144);
        readonly List<Vector2> _path = new List<Vector2>(Segments + 1);
        /// <summary>Hazards seen this fight, kept past their end so their spikes can sink.</summary>
        readonly Dictionary<int, Hazard> _seen = new Dictionary<int, Hazard>();
        readonly List<int> _drop = new List<int>();
        bool _held;
        double _heldAt, _arrived = double.NaN, _died = double.NaN;
        Vector2 _diedAt;

        public RootwardenPresenter(Palette palette, EnemyRules enemies, int layer = 0)
        {
            var shared = Resources.Load<Material>(HeroMarker.MaterialPath)
                         ?? throw new InvalidOperationException($"RootwardenPresenter: no material at Resources/{HeroMarker.MaterialPath}");
            _cast = enemies.IndexOf(RootwardenModule.ModuleId);
            var hue = _cast >= 0 ? palette.ForType(RootwardenModule.ModuleId) : new Color(0.84f, 0.18f, 1f);
            _material = new Material(shared) { name = "Rootwarden roots" };
            _material.SetColor("_Color", hue.linear * Brightness);
            _material.SetFloat(IdPass.Id, IdPass.Effect);
            _mesh = new Mesh { name = "Rootwarden roots" };
            _mesh.MarkDynamic();
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
                    _died = double.NaN;
                    _seen.Clear();
                    break;
                case SimEventKind.BossDeath when e.I1 == _cast:
                    _died = e.T;
                    _diedAt = new Vector2((float)e.D0, (float)e.D1);
                    break;
                case SimEventKind.BossMove when e.I2 != 0:
                    // Called off before landing: its roots withdraw with it.
                    _seen.Remove(e.I0);
                    break;
            }
        }

        /// <summary>Draws its effects at song time <paramref name="song"/> for <paramref name="camera"/> while its fight is on.</summary>
        public void Present(double song, Simulation sim, Camera camera)
        {
            Drawn = 0;
            var fight = sim.Fight;
            bool its = fight.Module is RootwardenModule;
            bool dying = !double.IsNaN(_died) && song >= _died && song < _died + DeathS;
            if (!its || (!fight.Active && !dying && _seen.Count == 0)) return;
            if (_held) song = Math.Min(song, _heldAt);
            _verts.Clear();
            _tris.Clear();
            foreach (var h in fight.Hazards) _seen[h.Id] = h;
            bool standing = fight.InFight && fight.Bodies.Length > 0 && fight.Bodies[0].Alive;
            var body = Vector2.zero;
            if (standing)
            {
                fight.At(0, song, out double bx, out double by, out _);
                body = new Vector2((float)bx, (float)by);
                Entrance(song, body, sim);
            }
            _drop.Clear();
            foreach (var kv in _seen)
            {
                var h = kv.Value;
                if (song > h.Until + SinkS) { _drop.Add(kv.Key); continue; }
                if (song < h.Telegraph.Land)
                {
                    if (standing) Creep(h, song, body);
                }
                else Erupt(h, song);
            }
            foreach (int id in _drop) _seen.Remove(id);
            if (dying) Death(song);
            if (_verts.Count == 0) return;
            _mesh.Clear();
            _mesh.indexFormat = _verts.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            _mesh.SetVertices(_verts);
            _mesh.SetTriangles(_tris, 0);
            _mesh.RecalculateBounds();
            var rp = new RenderParams(_material) { layer = _layer, shadowCastingMode = ShadowCastingMode.Off, receiveShadows = false, camera = camera };
            Graphics.RenderMesh(rp, _mesh, 0, Matrix4x4.identity);
            Drawn = _tris.Count / 3;
        }

        // ---- The creeping root ----

        /// <summary>A branching stroke from the body to <paramref name="h"/>'s ground, grown to its telegraph's share filled.</summary>
        void Creep(in Hazard h, double song, Vector2 body)
        {
            double span = h.Telegraph.Land - h.Telegraph.Start;
            float u = span > 0 ? Smooth((float)((song - h.Telegraph.Start) / span)) : 1;
            if (u <= 0) return;
            var target = Target(h.Shape);
            Branch(body, target, h.Id, u, 1f);
            // Two offshoots, splitting off partway and reaching sideways.
            for (int k = 0; k < 2; k++)
            {
                float at = 0.35f + 0.25f * k;
                if (u <= at) continue;
                var from = Vector2.Lerp(body, target, at) + Wobble(body, target, h.Id, at);
                var dir = (target - body).normalized;
                var side = new Vector2(-dir.y, dir.x) * (k == 0 ? 1 : -1);
                float reach = Mathf.Min(3.5f, 0.3f * Vector2.Distance(body, target));
                Branch(from, from + (dir * 0.6f + side) * reach, h.Id * 7 + k, (u - at) / (1 - at), 0.6f);
            }
        }

        /// <summary>The middle of a shape's ground: a circle's or ring's centre, a path's far end (where the root arrives).</summary>
        static Vector2 Target(in HitShape s) => s.Kind == ShapeKind.Path ? new Vector2((float)s.X2, (float)s.Y2) : new Vector2((float)s.X, (float)s.Y);

        /// <summary>A jagged stroke from <paramref name="a"/> to <paramref name="b"/>, drawn to <paramref name="share"/> of its length.</summary>
        void Branch(Vector2 a, Vector2 b, int seed, float share, float width)
        {
            _path.Clear();
            for (int k = 0; k <= Segments; k++)
            {
                float t = k / (float)Segments;
                _path.Add(Vector2.Lerp(a, b, t) + (k == 0 || k == Segments ? Vector2.zero : Wobble(a, b, seed, t)));
            }
            float total = 0;
            for (int k = 1; k < _path.Count; k++) total += Vector2.Distance(_path[k - 1], _path[k]);
            float left = total * Mathf.Clamp01(share);
            for (int k = 1; k < _path.Count && left > 0; k++)
            {
                float len = Vector2.Distance(_path[k - 1], _path[k]);
                var end = len <= left ? _path[k] : Vector2.Lerp(_path[k - 1], _path[k], left / len);
                // The stroke tapers toward its growing tip.
                float w = StrokeM * width * Mathf.Lerp(1.6f, 0.7f, k / (float)Segments);
                Ribbon(_path[k - 1], end, w);
                left -= len;
            }
        }

        /// <summary>A stroke's sideways offset at <paramref name="t"/> along it: a fixed jag per root, larger in the middle.</summary>
        static Vector2 Wobble(Vector2 a, Vector2 b, int seed, float t)
        {
            var d = b - a;
            float len = d.magnitude;
            if (len < 1e-4f) return Vector2.zero;
            var n = new Vector2(-d.y, d.x) / len;
            float jag = Mathf.Sin(seed * 12.9898f + t * 17.3f) * 43758.5453f;
            jag -= Mathf.Floor(jag);
            return n * ((jag - 0.5f) * Mathf.Min(2.2f, 0.18f * len) * Mathf.Sin(Mathf.PI * t));
        }

        // ---- Eruptions ----

        /// <summary>Spikes up out of <paramref name="h"/>'s ground: risen in <see cref="RiseS"/>, held while it hurts, sunk over <see cref="SinkS"/>.</summary>
        void Erupt(in Hazard h, double song)
        {
            double since = song - h.Telegraph.Land, after = song - h.Until;
            float up = Mathf.Clamp01((float)(since / RiseS));
            if (after > 0) up *= 1 - Mathf.Clamp01((float)(after / SinkS));
            if (up <= 0.01f) return;
            var s = h.ShapeAt(Math.Min(song, h.Until));
            switch (s.Kind)
            {
                case ShapeKind.Circle:
                {
                    var c = new Vector2((float)s.X, (float)s.Y);
                    int n = Mathf.Clamp(Mathf.RoundToInt((float)s.Radius * 3.5f), 4, 12);
                    Spike(c, up * 1.15f, h.Id);
                    for (int k = 0; k < n; k++)
                    {
                        float a = 2 * Mathf.PI * k / n + h.Id;
                        float r = (float)s.Radius * (0.45f + 0.4f * Hash(h.Id * 13 + k));
                        Spike(c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r, up * (0.6f + 0.4f * Hash(h.Id + k * 5)), h.Id * 3 + k);
                    }
                    break;
                }
                case ShapeKind.Path:
                {
                    var a = new Vector2((float)s.X, (float)s.Y);
                    var b = new Vector2((float)s.X2, (float)s.Y2);
                    Ribbon(a, b, StrokeM * 1.8f);
                    int n = Mathf.Max(1, Mathf.CeilToInt(Vector2.Distance(a, b) / SpacingM));
                    for (int k = 0; k <= n; k++) Spike(Vector2.Lerp(a, b, k / (float)n), up * (0.7f + 0.3f * Hash(h.Id + k)), h.Id * 5 + k);
                    break;
                }
                case ShapeKind.Ring:
                {
                    // A crown of spikes round the ring's middle, as it grows.
                    var c = new Vector2((float)s.X, (float)s.Y);
                    float r = (float)(s.Inner + s.Radius) / 2;
                    int n = Mathf.Clamp(Mathf.CeilToInt(2 * Mathf.PI * r / SpacingM), 6, 160);
                    for (int k = 0; k < n; k++)
                    {
                        float a = 2 * Mathf.PI * k / n;
                        Spike(c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r, up * (0.55f + 0.35f * Hash(h.Id + k)), h.Id + k);
                    }
                    break;
                }
            }
        }

        /// <summary>One faceted spike at <paramref name="at"/>: a three-sided pyramid, its height <paramref name="height"/> of <see cref="SpikeM"/>, leaning a little.</summary>
        void Spike(Vector2 at, float height, int seed)
        {
            if (height <= 0.02f) return;
            float turn = Hash(seed) * 2 * Mathf.PI, lean = 0.25f * (Hash(seed * 3 + 1) - 0.5f);
            var tip = new Vector3(at.x + lean * Mathf.Cos(turn), SpikeM * height, at.y + lean * Mathf.Sin(turn));
            int i = _verts.Count;
            for (int k = 0; k < 3; k++)
            {
                float a = turn + 2 * Mathf.PI * k / 3;
                _verts.Add(new Vector3(at.x + SpikeBaseM * Mathf.Cos(a), 0, at.y + SpikeBaseM * Mathf.Sin(a)));
            }
            _verts.Add(tip);
            for (int k = 0; k < 3; k++)
            {
                _tris.Add(i + k);
                _tris.Add(i + 3);
                _tris.Add(i + (k + 1) % 3);
            }
        }

        // ---- Entrance and death ----

        void Entrance(double song, Vector2 body, Simulation sim)
        {
            if (double.IsNaN(_arrived) || song < _arrived) return;
            double span = sim.Fight.Module.EntranceBeats * sim.Fight.BeatS;
            float u = (float)((song - _arrived) / span);
            if (u >= 1) return;
            // Roots crawl in from round the hollow to its feet, then fade as it takes its first step.
            for (int k = 0; k < EntranceRoots; k++)
            {
                float a = 2 * Mathf.PI * k / EntranceRoots + 0.4f;
                var from = body + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 11f;
                Branch(from, body, 991 + k, Smooth(u * 1.4f), 1.2f * (1 - Smooth(Mathf.Max(0, u - 0.7f) / 0.3f)));
            }
        }

        void Death(double song)
        {
            float u = (float)((song - _died) / DeathS);
            float up = u < 0.15f ? u / 0.15f : 1 - Smooth((u - 0.15f) / 0.85f);
            for (int k = 0; k < DeathSpikes; k++)
            {
                float a = 2 * Mathf.PI * (k + 0.5f) / DeathSpikes;
                var dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                Spike(_diedAt + dir * (2f + 5f * Smooth(u)), up * (0.8f + 0.5f * Hash(k)), 7001 + k);
            }
        }

        // ---- Mesh pieces ----

        /// <summary>A flat stroke on the floor from <paramref name="a"/> to <paramref name="b"/>, <paramref name="width"/> wide.</summary>
        void Ribbon(Vector2 a, Vector2 b, float width)
        {
            var d = b - a;
            if (d.sqrMagnitude < 1e-8f) return;
            var n = new Vector2(-d.y, d.x).normalized * (width / 2);
            int i = _verts.Count;
            _verts.Add(new Vector3(a.x + n.x, FloorM, a.y + n.y));
            _verts.Add(new Vector3(a.x - n.x, FloorM, a.y - n.y));
            _verts.Add(new Vector3(b.x - n.x, FloorM, b.y - n.y));
            _verts.Add(new Vector3(b.x + n.x, FloorM, b.y + n.y));
            _tris.Add(i); _tris.Add(i + 1); _tris.Add(i + 2);
            _tris.Add(i); _tris.Add(i + 2); _tris.Add(i + 3);
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
            foreach (var o in new UnityEngine.Object[] { _mesh, _material })
            {
                if (o == null) continue;
                if (Application.isPlaying) UnityEngine.Object.Destroy(o);
                else UnityEngine.Object.DestroyImmediate(o);
            }
        }
    }
}
