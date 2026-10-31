using System;
using System.Collections.Generic;
using Game.Client.Loop;
using PS.Game.Sim;
using PS.Game.Sim.Content;
using UnityEngine;
using UnityEngine.Rendering;

namespace Game.Client.Pickups
{
    /// <summary>
    /// The experience gems in 3D (unity-game.md, Pickups, Level-ups and Chests): small faceted
    /// crystals (Resources/Gems/Gem.shader) in their value's colour (progression.json's blue, green
    /// and red), hovering and turning where their enemies died, throbbing on the beat.
    ///
    /// - A <see cref="SimEventKind.Drop"/> of a gem pops it in at its time; one for a gem already
    ///   down (the field's cap) grows it to its new value.
    /// - A <see cref="SimEventKind.GemFlight"/> sends it off: kicked out from the hero, then pulled
    ///   in fast to the hero's chest, reaching it at its <see cref="SimEventKind.Pickup"/>, which
    ///   arrives from the sim before frameSong gets there (or, until it does, after the attraction's
    ///   usual time). The pickup's chime is the sound presenter's.
    ///
    /// Events settle ahead of frameSong, so each change waits for its own time.
    /// </summary>
    public sealed class GemView : IPresenter, IDisposable
    {
        const float HoverM = 0.45f, ChestM = 1.1f, PopS = 0.15f, TurnDegPerS = 70;
        /// <summary>A gem's height and width at the smallest tier, metres; each tier up is a third larger.</summary>
        const float HeightM = 0.42f, WidthM = 0.24f, TierScale = 1.33f;
        /// <summary>The flight before its pickup is known: about native's attraction from the pickup radius.</summary>
        const double GuessFlightS = 0.6;
        /// <summary>The outward kick's reach, as a share of the gem's distance from the hero.</summary>
        const float Kick = 0.55f;
        const double PulseS = 0.12;
        const int Batch = 1023;

        struct Gem
        {
            public int Id;
            public double X, Y, Value;
            public double DropT, GrowT, FlightT, ArriveT;
            public double PrevValue;
        }

        static readonly int PulseId = Shader.PropertyToID("_Pulse");
        static readonly int ColorId = Shader.PropertyToID("_Color");

        readonly List<Gem> _gems = new List<Gem>();
        readonly Dictionary<int, int> _index = new Dictionary<int, int>();
        readonly double[] _tierFrom;
        readonly Material[] _materials;
        readonly Matrix4x4[][] _matrices;
        readonly int[] _counts;
        readonly Mesh _mesh;
        double _lastBeat = double.NegativeInfinity;

        public GemView(GemRules rules)
        {
            _tierFrom = rules.ColourFrom;
            var shader = Resources.Load<Shader>("Gems/Gem");
            _materials = new Material[rules.Colours.Length];
            _matrices = new Matrix4x4[rules.Colours.Length][];
            _counts = new int[rules.Colours.Length];
            for (int i = 0; i < _materials.Length; i++)
            {
                _materials[i] = new Material(shader) { name = "gem_" + rules.Colours[i], enableInstancing = true };
                _materials[i].SetColor(ColorId, Colour(rules.Colours[i]));
                _matrices[i] = new Matrix4x4[Batch];
            }
            _mesh = BuildMesh();
        }

        /// <summary>The gem's crystal colour, HDR: native's blue, green and red gems.</summary>
        static Color Colour(string name)
        {
            switch (name)
            {
                case "green": return new Color(0.3f, 1.0f, 0.5f);
                case "red": return new Color(1.0f, 0.28f, 0.36f);
                default: return new Color(0.32f, 0.62f, 1.0f);
            }
        }

        int Tier(double value)
        {
            int t = 0;
            for (int i = 0; i < _tierFrom.Length; i++)
                if (value >= _tierFrom[i]) t = i;
            return t;
        }

        public void OnEvent(in SimEvent e)
        {
            switch (e.Kind)
            {
                case SimEventKind.Beat:
                    _lastBeat = e.T;
                    break;
                case SimEventKind.Drop:
                {
                    var d = e.AsDrop;
                    if (d.ItemKind != ItemKind.Gem) break;
                    if (_index.TryGetValue(d.Item, out int i))
                    {
                        var g = _gems[i];
                        g.PrevValue = g.Value;
                        g.Value = d.Value;
                        g.GrowT = d.T;
                        _gems[i] = g;
                        break;
                    }
                    _index[d.Item] = _gems.Count;
                    _gems.Add(new Gem
                    {
                        Id = d.Item, X = d.X, Y = d.Y, Value = d.Value, PrevValue = d.Value, DropT = d.T,
                        GrowT = double.NegativeInfinity, FlightT = double.PositiveInfinity, ArriveT = double.PositiveInfinity,
                    });
                    break;
                }
                case SimEventKind.GemFlight:
                {
                    var f = e.AsGemFlight;
                    if (!_index.TryGetValue(f.Gem, out int i)) break;
                    var g = _gems[i];
                    g.FlightT = f.T;
                    _gems[i] = g;
                    break;
                }
                case SimEventKind.Pickup:
                {
                    var p = e.AsPickup;
                    if (p.ItemKind != (int)ItemKind.Gem || !_index.TryGetValue(p.Item, out int i)) break;
                    var g = _gems[i];
                    g.ArriveT = p.T;
                    _gems[i] = g;
                    break;
                }
            }
        }

        public void Present(in PresentFrame frame)
        {
            double t = frame.Song;
            frame.State.Hero.PositionAt(t, out double hx, out double hy);
            Array.Clear(_counts, 0, _counts.Length);
            float turn = (float)(t * TurnDegPerS % 360);
            for (int i = 0; i < _gems.Count; i++)
            {
                var g = _gems[i];
                if (t >= g.ArriveT)
                {
                    Remove(i--);
                    continue;
                }
                if (t < g.DropT) continue;
                double value = t >= g.GrowT ? g.Value : g.PrevValue;
                int tier = Tier(value);
                float pop = Mathf.Clamp01((float)((t - g.DropT) / PopS));
                float scale = Mathf.Pow(TierScale, tier) * (1 - (1 - pop) * (1 - pop));
                Vector3 at = new Vector3((float)g.X, HoverM + 0.06f * Mathf.Sin((float)(t * 2.4 + g.Id)), (float)g.Y);
                if (t >= g.FlightT)
                {
                    double end = double.IsInfinity(g.ArriveT) ? g.FlightT + GuessFlightS : g.ArriveT;
                    float q = Mathf.Clamp01((float)((t - g.FlightT) / Math.Max(1e-3, end - g.FlightT)));
                    var hero = new Vector3((float)hx, ChestM, (float)hy);
                    var from = new Vector3((float)g.X, HoverM, (float)g.Y);
                    // Out and back in: the offset from the hero bulges by the kick, then closes ever faster.
                    float reach = (1 - q * q) * (1 + Kick * Mathf.Sin(Mathf.PI * Mathf.Min(1, q * 1.6f)));
                    at = hero + (from - hero) * reach;
                    scale *= 1 - 0.5f * q * q;
                }
                if (_counts[tier] == Batch) continue;
                _matrices[tier][_counts[tier]++] = Matrix4x4.TRS(at, Quaternion.Euler(0, turn + g.Id * 37, 0), Vector3.one * scale);
            }
            float pulse = (float)Math.Exp(-Math.Max(0, t - _lastBeat) / PulseS);
            for (int k = 0; k < _materials.Length; k++)
            {
                _materials[k].SetFloat(PulseId, pulse);
                if (_counts[k] == 0) continue;
                var rp = new RenderParams(_materials[k]) { shadowCastingMode = ShadowCastingMode.Off, receiveShadows = false };
                Graphics.RenderMeshInstanced(rp, _mesh, 0, _matrices[k], _counts[k]);
            }
        }

        void Remove(int i)
        {
            _index.Remove(_gems[i].Id);
            int last = _gems.Count - 1;
            if (i != last)
            {
                _gems[i] = _gems[last];
                _index[_gems[i].Id] = i;
            }
            _gems.RemoveAt(last);
        }

        /// <summary>An elongated octahedron with flat facets: a vertex per face corner, so each facet shades as one.</summary>
        static Mesh BuildMesh()
        {
            float h = HeightM / 2, w = WidthM / 2;
            var top = new Vector3(0, h, 0);
            var bottom = new Vector3(0, -h, 0);
            Vector3[] ring = { new Vector3(w, 0, 0), new Vector3(0, 0, w), new Vector3(-w, 0, 0), new Vector3(0, 0, -w) };
            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var triangles = new List<int>();
            void Face(Vector3 a, Vector3 b, Vector3 c)
            {
                var n = Vector3.Cross(b - a, c - a).normalized;
                triangles.Add(vertices.Count);
                triangles.Add(vertices.Count + 1);
                triangles.Add(vertices.Count + 2);
                vertices.Add(a);
                vertices.Add(b);
                vertices.Add(c);
                normals.Add(n);
                normals.Add(n);
                normals.Add(n);
            }
            for (int k = 0; k < 4; k++)
            {
                var a = ring[k];
                var b = ring[(k + 1) % 4];
                Face(top, b, a);
                Face(bottom, a, b);
            }
            var mesh = new Mesh { name = "gem" };
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        public void Dispose()
        {
            foreach (var m in _materials) UnityEngine.Object.Destroy(m);
            UnityEngine.Object.Destroy(_mesh);
        }
    }
}
