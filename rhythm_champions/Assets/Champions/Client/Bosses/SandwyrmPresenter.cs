using System;
using System.Collections.Generic;
using Champions.Render;
using Champions.Sim;
using UnityEngine;
using UnityEngine.Rendering;

namespace Champions.Client
{
    /// <summary>
    /// The Sandwyrm's authored effects (ps-8z4.17; boss-rush.md, The roster: "the floor grid displaces along its path"), all
    /// in its hue (palette.json's type colour, never the danger red, which stays the telegraphs' own):
    /// - <b>Ripples</b>: while it is under the sand, chevron ripples spread from the sand over its head, a wake of raised
    ///   ridges trails behind it along its path (the floor heaving as it passes, settling after) and a dashed line runs
    ///   ahead to where it is heading, so its travel and its target read together.
    /// - <b>Eruptions</b>: on each landing of its hunts, tunnel and breach, a crown of faceted sand shards bursts out of the
    ///   ground it marked, leaning outward, held while the hazard hurts, then sinking.
    /// - <b>The breach's arc</b>: a crest of shards rides the wave of sand across the basin.
    /// - <b>Entrance and death</b>: ripples close in on where it rises; its shatter throws a ring of shards out round it on
    ///   the impact frame (design.md: deaths break at once).
    /// One dynamic mesh a frame in the world's Glow material, in the stills' effect ID pass.
    /// </summary>
    public sealed class SandwyrmPresenter : IDisposable
    {
        public const float Brightness = 2.0f, StrokeM = 0.08f, FloorM = 0.04f;
        const float ShardM = 1.1f, ShardBaseM = 0.26f, RidgeM = 0.22f, CrestSpacingM = 1.1f;
        const double RiseS = 0.06, SinkS = 0.3, DeathS = 0.7, WakeS = 1.4, SettleS = 0.6;
        const int Chevrons = 3, EntranceRings = 4, DeathShards = 20, WakeSamples = 14;

        readonly Material _material;
        readonly Mesh _mesh;
        readonly int _layer, _cast;
        readonly List<Vector3> _verts = new List<Vector3>(4096);
        readonly List<int> _tris = new List<int>(6144);
        /// <summary>Hazards seen this fight, kept past their end so their shards can sink.</summary>
        readonly Dictionary<int, Hazard> _seen = new Dictionary<int, Hazard>();
        readonly List<int> _drop = new List<int>();
        bool _held;
        double _heldAt, _arrived = double.NaN, _died = double.NaN, _surfaced = double.NaN;
        Vector2 _diedAt;

        public SandwyrmPresenter(Palette palette, EnemyRules enemies, int layer = 0)
        {
            var shared = Resources.Load<Material>(HeroMarker.MaterialPath)
                         ?? throw new InvalidOperationException($"SandwyrmPresenter: no material at Resources/{HeroMarker.MaterialPath}");
            _cast = enemies.IndexOf(SandwyrmModule.ModuleId);
            var hue = _cast >= 0 ? palette.ForType(SandwyrmModule.ModuleId) : new Color(1f, 0.22f, 0.71f);
            _material = new Material(shared) { name = "Sandwyrm ripples" };
            _material.SetColor("_Color", hue.linear * Brightness);
            _material.SetFloat(IdPass.Id, IdPass.Effect);
            _mesh = new Mesh { name = "Sandwyrm ripples" };
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
                    _died = _surfaced = double.NaN;
                    _seen.Clear();
                    break;
                case SimEventKind.BossDeath when e.I1 == _cast:
                    _died = e.T;
                    _diedAt = new Vector2((float)e.D0, (float)e.D1);
                    break;
                case SimEventKind.BossMove when e.I2 != 0:
                    // Called off before landing (a phase's dive): its shards never rise.
                    _seen.Remove(e.I0);
                    break;
            }
        }

        /// <summary>Draws its effects at song time <paramref name="song"/> for <paramref name="camera"/> while its fight is on.</summary>
        public void Present(double song, Simulation sim, Camera camera)
        {
            Drawn = 0;
            var fight = sim.Fight;
            var worm = fight.Module as SandwyrmModule;
            bool dying = !double.IsNaN(_died) && song >= _died && song < _died + DeathS;
            if (worm == null || (!fight.Active && !dying && _seen.Count == 0)) return;
            if (_held) song = Math.Min(song, _heldAt);
            _verts.Clear();
            _tris.Clear();
            foreach (var h in fight.Hazards) _seen[h.Id] = h;
            var arena = sim.Arena;
            var centre = arena != null && arena.Solid ? new Vector2((float)arena.OriginX, (float)arena.OriginY) : new Vector2((float)fight.CentreX, (float)fight.CentreY);
            bool standing = fight.InFight && fight.Bodies.Length > 0 && fight.Bodies[0].Alive;
            if (standing)
            {
                fight.At(0, song, out double bx, out double by, out double facing);
                var head = new Vector2((float)bx, (float)by);
                Entrance(song, head, fight);
                if (worm.Under(song))
                {
                    _surfaced = worm.SurfaceAt;
                    Ripple(song, head, (float)facing);
                    Wake(song, fight, worm.SinkFrom, song, 1);
                    Heading(song, head, fight);
                }
                else if (!double.IsNaN(_surfaced) && song < _surfaced + SettleS)
                {
                    // The wake settles back into the sand once it has burst out.
                    Wake(_surfaced, fight, _surfaced - WakeS, _surfaced, 1 - (float)((song - _surfaced) / SettleS));
                }
            }
            _drop.Clear();
            foreach (var kv in _seen)
            {
                var h = kv.Value;
                if (song > h.Until + SinkS) { _drop.Add(kv.Key); continue; }
                if (song < h.Telegraph.Land) continue;
                if (h.DashWave) Crest(h, song, centre);
                else if (h.Move == BossMove.Dive || (h.Flags & HazardFlags.Signature) != 0) Erupt(h, song);
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

        // ---- Under the sand ----

        /// <summary>Chevron ripples spreading back from its head, pulsing outward, the nearest brightest.</summary>
        void Ripple(double song, Vector2 head, float facing)
        {
            var fwd = new Vector2(Mathf.Cos(facing), Mathf.Sin(facing));
            float phase = (float)(song * 2.2 % 1.0);
            for (int k = 0; k < Chevrons; k++)
            {
                float u = (k + phase) / Chevrons;
                float r = 0.8f + 3.2f * u, width = StrokeM * 1.6f * (1 - u);
                if (width <= 0.01f) continue;
                // An arc opening behind the head: a bow wave.
                var tip = head + fwd * (1.2f - 1.4f * u);
                Arc(tip, fwd, r, 0.75f + 0.55f * u, width);
            }
        }

        /// <summary>Raised ridges either side of its path from <paramref name="from"/> to <paramref name="to"/>, fading with age.</summary>
        void Wake(double song, BossFight fight, double from, double to, float strength)
        {
            if (strength <= 0.02f) return;
            double start = Math.Max(from, to - WakeS);
            if (to - start < 0.05) return;
            Vector2 prev = default;
            bool have = false;
            for (int k = 0; k <= WakeSamples; k++)
            {
                double t = start + (to - start) * k / WakeSamples;
                fight.At(0, t, out double x, out double y, out _);
                var p = new Vector2((float)x, (float)y);
                if (have && (p - prev).sqrMagnitude > 1e-4f)
                {
                    float age = (float)((song - t) / WakeS);
                    float h = RidgeM * strength * Mathf.Clamp01(1 - age) * Mathf.Clamp01(k / 2f);
                    var d = (p - prev).normalized;
                    var n = new Vector2(-d.y, d.x) * (0.9f + 0.5f * age);
                    Ridge(prev + n, p + n, h);
                    Ridge(prev - n, p - n, h);
                }
                prev = p;
                have = true;
            }
        }

        /// <summary>A dashed line from its head to where the hazard it is travelling to lands.</summary>
        void Heading(double song, Vector2 head, BossFight fight)
        {
            foreach (var h in fight.Hazards)
            {
                if (h.Move != BossMove.Dive || h.BodyMove != HazardBodyMove.DuringTelegraph || song >= h.Telegraph.Land) continue;
                var to = new Vector2((float)h.ToX, (float)h.ToY);
                var d = to - head;
                float len = d.magnitude;
                if (len < 1.5f) return;
                d /= len;
                const float dash = 0.6f, gap = 0.5f;
                float scroll = (float)(song * 3.0 % (dash + gap));
                for (float s = 1.2f + scroll; s < len - 0.4f; s += dash + gap)
                    Ribbon(head + d * s, head + d * Mathf.Min(len - 0.4f, s + dash), StrokeM * 0.8f);
                return;
            }
        }

        // ---- Eruptions ----

        /// <summary>Shards out of <paramref name="h"/>'s ground: risen in <see cref="RiseS"/>, held while it hurts, sunk over <see cref="SinkS"/>.</summary>
        void Erupt(in Hazard h, double song)
        {
            double since = song - h.Telegraph.Land, after = song - h.Until;
            float up = Mathf.Clamp01((float)(since / RiseS));
            if (after > 0) up *= 1 - Mathf.Clamp01((float)(after / SinkS));
            if (up <= 0.01f) return;
            var s = h.ShapeAt(Math.Min(song, h.Until));
            if (s.Kind != ShapeKind.Circle) return;
            var c = new Vector2((float)s.X, (float)s.Y);
            float r = (float)s.Radius;
            int n = Mathf.Clamp(Mathf.RoundToInt(r * 4f), 5, 14);
            for (int k = 0; k < n; k++)
            {
                float a = 2 * Mathf.PI * k / n + h.Id * 0.7f;
                var dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                // Thrown out from the burst: the shards ride outward as they rise.
                float out_ = r * (0.55f + 0.35f * Hash(h.Id * 13 + k)) * (0.8f + 0.2f * up);
                Shard(c + dir * out_, dir, up * (0.65f + 0.45f * Hash(h.Id + k * 5)), h.Id * 3 + k);
            }
        }

        /// <summary>The breach's crest: shards along the growing ring, inside the basin.</summary>
        void Crest(in Hazard h, double song, Vector2 centre)
        {
            if (song > h.Until) return;
            var s = h.ShapeAt(song);
            var c = new Vector2((float)s.X, (float)s.Y);
            float r = (float)(s.Inner + s.Radius) / 2;
            if (r < 0.5f) return;
            int n = Mathf.Clamp(Mathf.CeilToInt(2 * Mathf.PI * r / CrestSpacingM), 8, 220);
            float basin = (float)SandwyrmModule.BasinM - 0.3f;
            for (int k = 0; k < n; k++)
            {
                float a = 2 * Mathf.PI * k / n;
                var dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                var at = c + dir * r;
                if ((at - centre).sqrMagnitude > basin * basin) continue;
                Shard(at, dir, 0.55f + 0.3f * Hash(h.Id + k), h.Id + k);
            }
        }

        /// <summary>One faceted shard at <paramref name="at"/>: a three-sided pyramid <paramref name="height"/> of <see cref="ShardM"/>, leaning along <paramref name="lean"/>.</summary>
        void Shard(Vector2 at, Vector2 lean, float height, int seed)
        {
            if (height <= 0.02f) return;
            float turn = Hash(seed) * 2 * Mathf.PI;
            var tip = new Vector3(at.x + 0.35f * height * lean.x, ShardM * height, at.y + 0.35f * height * lean.y);
            int i = _verts.Count;
            for (int k = 0; k < 3; k++)
            {
                float a = turn + 2 * Mathf.PI * k / 3;
                _verts.Add(new Vector3(at.x + ShardBaseM * Mathf.Cos(a), 0, at.y + ShardBaseM * Mathf.Sin(a)));
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

        void Entrance(double song, Vector2 at, BossFight fight)
        {
            if (double.IsNaN(_arrived) || song < _arrived) return;
            double span = fight.Module.EntranceBeats * fight.BeatS;
            float u = (float)((song - _arrived) / span);
            if (u >= 1) return;
            // Rings of ripples closing in on where it rose, fading as it takes its first move.
            for (int k = 0; k < EntranceRings; k++)
            {
                float v = (u * 2 + k / (float)EntranceRings) % 1f;
                float r = 1.5f + 9f * (1 - v);
                Arc(at, Vector2.up, r, Mathf.PI, StrokeM * 1.4f * (1 - u) * v);
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
                Shard(_diedAt + dir * (2.2f + 5.5f * Smooth(u)), dir, up * (0.8f + 0.5f * Hash(k)), 7101 + k);
            }
        }

        // ---- Mesh pieces ----

        /// <summary>An arc of radius <paramref name="r"/> round <paramref name="c"/>, <paramref name="halfArc"/> either side of the direction away from <paramref name="facing"/>.</summary>
        void Arc(Vector2 c, Vector2 facing, float r, float halfArc, float width)
        {
            if (width <= 0.005f) return;
            float back = Mathf.Atan2(-facing.y, -facing.x);
            int n = Mathf.Clamp(Mathf.CeilToInt(halfArc * r * 2.5f), 4, 32);
            var prev = c + new Vector2(Mathf.Cos(back - halfArc), Mathf.Sin(back - halfArc)) * r;
            for (int k = 1; k <= n; k++)
            {
                float a = back - halfArc + 2 * halfArc * k / n;
                var p = c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
                Ribbon(prev, p, width);
                prev = p;
            }
        }

        /// <summary>A low ridge of sand from <paramref name="a"/> to <paramref name="b"/>: a tent <paramref name="height"/> high, its crest a line of light.</summary>
        void Ridge(Vector2 a, Vector2 b, float height)
        {
            if (height <= 0.01f) return;
            var d = b - a;
            var n = new Vector2(-d.y, d.x).normalized * (StrokeM * 0.6f);
            int i = _verts.Count;
            _verts.Add(new Vector3(a.x + n.x, FloorM, a.y + n.y));
            _verts.Add(new Vector3(a.x, FloorM + height, a.y));
            _verts.Add(new Vector3(a.x - n.x, FloorM, a.y - n.y));
            _verts.Add(new Vector3(b.x + n.x, FloorM, b.y + n.y));
            _verts.Add(new Vector3(b.x, FloorM + height, b.y));
            _verts.Add(new Vector3(b.x - n.x, FloorM, b.y - n.y));
            _tris.Add(i); _tris.Add(i + 1); _tris.Add(i + 4);
            _tris.Add(i); _tris.Add(i + 4); _tris.Add(i + 3);
            _tris.Add(i + 1); _tris.Add(i + 2); _tris.Add(i + 5);
            _tris.Add(i + 1); _tris.Add(i + 5); _tris.Add(i + 4);
        }

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
