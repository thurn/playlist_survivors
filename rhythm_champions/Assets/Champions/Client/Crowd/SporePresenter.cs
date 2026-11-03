using System;
using System.Collections.Generic;
using Champions.Render;
using Champions.Sim;
using UnityEngine;
using UnityEngine.Rendering;

namespace Champions.Client
{
    /// <summary>
    /// The mushrooms' spores (design.md, The Horde &gt; Threats; The Look &gt; The palette: enemy shots in electric yellow): each
    /// a small bright yellow bead flying from its mushroom's cap at its velocity until it ends, drawn with the world's Glow
    /// material in the palette's enemy-shot colour, bright enough to bloom. An effect in the stills' type-ID pass.
    /// </summary>
    public sealed class SporePresenter : IDisposable
    {
        public const float RadiusM = 0.16f, HeightM = 0.9f, Brightness = 2.5f;

        struct Spore
        {
            public int Number;
            public double From, Until;
            public Vector2 At, Velocity;
        }

        bool _held;
        double _heldAt;
        readonly Mesh _mesh;
        readonly Material _material;
        readonly int _layer;
        readonly List<Spore> _spores = new List<Spore>();

        public SporePresenter(Palette palette, int layer = 0)
        {
            var shared = Resources.Load<Material>(HeroMarker.MaterialPath)
                         ?? throw new InvalidOperationException($"SporePresenter: no material at Resources/{HeroMarker.MaterialPath}");
            _material = new Material(shared) { name = "Spores" };
            _material.SetColor("_Color", palette.EnemyShot.linear * Brightness);
            _material.SetFloat(IdPass.Id, IdPass.Effect);
            _mesh = Bead();
            _layer = layer;
        }

        /// <summary>Spores in flight on the latest frame.</summary>
        public int Drawn { get; private set; }

        public void Take(in SimEvent e)
        {
            switch (e.Kind)
            {
                case SimEventKind.Hold:
                    bool held = e.I2 != 0;
                    if (held && !_held) _heldAt = e.T;
                    if (!held && _held)
                        for (int i = 0; i < _spores.Count; i++)
                        {
                            var s = _spores[i]; s.From += e.T - _heldAt; s.Until += e.T - _heldAt; _spores[i] = s;
                        }
                    _held = held;
                    break;
                case SimEventKind.Spore:
                    double speed = Math.Sqrt(e.D2 * e.D2 + e.D3 * e.D3);
                    _spores.Add(new Spore
                    {
                        Number = e.I0, From = e.T, Until = speed > 1e-6 ? e.T + e.D4 / speed : e.T,
                        At = new Vector2((float)e.D0, (float)e.D1), Velocity = new Vector2((float)e.D2, (float)e.D3),
                    });
                    break;
                case SimEventKind.SporeEnd:
                    for (int i = 0; i < _spores.Count; i++)
                    {
                        if (_spores[i].Number != e.I0) continue;
                        var s = _spores[i];
                        s.Until = Math.Min(s.Until, e.T);
                        _spores[i] = s;
                    }
                    break;
            }
        }

        /// <summary>Draws the spores in flight at song time <paramref name="song"/> for <paramref name="camera"/>.</summary>
        public void Present(double song, Camera camera)
        {
            if (_held) song = Math.Min(song, _heldAt);
            Drawn = 0;
            _spores.RemoveAll(s => song > s.Until + 0.05);
            var rp = new RenderParams(_material) { layer = _layer, shadowCastingMode = ShadowCastingMode.Off, receiveShadows = false, camera = camera };
            foreach (var s in _spores)
            {
                if (song < s.From || song > s.Until) continue;
                var p = s.At + s.Velocity * (float)(song - s.From);
                Graphics.RenderMesh(rp, _mesh, 0, Matrix4x4.Translate(new Vector3(p.x, HeightM, p.y)));
                Drawn++;
            }
        }

        /// <summary>An octahedron of <see cref="RadiusM"/>.</summary>
        static Mesh Bead()
        {
            float r = RadiusM;
            var v = new[] { new Vector3(r, 0, 0), new Vector3(-r, 0, 0), new Vector3(0, r, 0), new Vector3(0, -r, 0), new Vector3(0, 0, r), new Vector3(0, 0, -r) };
            var i = new[] { 0, 2, 4, 4, 2, 1, 1, 2, 5, 5, 2, 0, 4, 3, 0, 1, 3, 4, 5, 3, 1, 0, 3, 5 };
            var mesh = new Mesh { name = "Spore" };
            mesh.vertices = v;
            mesh.triangles = i;
            mesh.RecalculateBounds();
            return mesh;
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
