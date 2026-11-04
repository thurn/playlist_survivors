using System;
using System.Collections.Generic;
using Champions.Render;
using Champions.Sim;
using UnityEngine;
using UnityEngine.Rendering;

namespace Champions.Client
{
    /// <summary>
    /// Moonhowl's moon (ps-8z4.14; boss-rush.md, The roster: "a moon disc on the backdrop whose phase is the timer"): a
    /// faceted disc standing beyond the glade's wall in the fixed camera's top right corner, turned to the camera, whose lit share is <see cref="MoonhowlModule.Moon"/>
    /// (full through the howl and the frenzy, new halfway through the stalk), so the moon tells the hero how far off the
    /// next frenzy is. Drawn in its hue (palette.json's type colour, never the danger red) in the edge look: the disc's
    /// rim dim, its lit part filled and outlined bright along the limb and the terminator, a halo ring breathing on the
    /// beat while it is full. Below half health (the hunter's moon) it burns brighter and its halo doubles. One dynamic
    /// mesh of fill and one of lines a frame, in the world's Glow material; an effect to the stills' type-ID pass.
    /// </summary>
    public sealed class MoonhowlPresenter : IDisposable
    {
        /// <summary>Its place from the glade's centre (beyond the wall up and right of it, in the fixed camera's top corner over the pines), its height and radius.</summary>
        public const float RightM = 21.5f, UpM = 7.5f, HeightM = 3f, RadiusM = 2.8f;
        const float LineM = 0.09f, FillBrightness = 0.55f, LineBrightness = 2.4f, RimBrightness = 0.35f;
        const int Segments = 40, Rows = 16;

        readonly Material _fill, _lines, _rim;
        readonly Mesh _fillMesh, _lineMesh, _rimMesh;
        readonly int _layer;
        readonly Color _hue;
        readonly List<Vector3> _verts = new List<Vector3>(512);
        readonly List<int> _tris = new List<int>(1024);
        Matrix4x4 _at;
        bool _drawn;

        MoonhowlPresenter(Palette palette, int layer)
        {
            var shared = Resources.Load<Material>(HeroMarker.MaterialPath)
                         ?? throw new InvalidOperationException($"MoonhowlPresenter: no material at Resources/{HeroMarker.MaterialPath}");
            _hue = palette.ForType(MoonhowlModule.ModuleId);
            _fill = Make(shared, "Moon fill");
            _lines = Make(shared, "Moon lines");
            _rim = Make(shared, "Moon rim");
            _fillMesh = new Mesh { name = "Moon fill" };
            _fillMesh.MarkDynamic();
            _lineMesh = new Mesh { name = "Moon lines" };
            _lineMesh.MarkDynamic();
            _rimMesh = new Mesh { name = "Moon rim" };
            _rimMesh.MarkDynamic();
            _layer = layer;
        }

        static Material Make(Material shared, string name)
        {
            var m = new Material(shared) { name = name };
            m.SetFloat(IdPass.Id, IdPass.Effect);
            return m;
        }

        /// <summary>The presenter, or null when the content has no Moonhowl.</summary>
        public static MoonhowlPresenter Create(Palette palette, EnemyRules enemies, int layer = 0) =>
            enemies.IndexOf(MoonhowlModule.ModuleId) < 0 ? null : new MoonhowlPresenter(palette, layer);

        /// <summary>Draws the moon at song time <paramref name="song"/> for <paramref name="camera"/> while Moonhowl's fight is on.</summary>
        public void Present(double song, Simulation sim, Camera camera)
        {
            _drawn = false;
            var fight = sim.Fight;
            if (!(fight.Module is MoonhowlModule wolf) || !fight.InFight) return;
            float moon = (float)wolf.Moon(fight, song);
            bool hunter = fight.Phase > 1;
            // Beyond the wall in the camera's top corner, turned square to the camera.
            var centre = new Vector3((float)wolf.OriginX + RightM, HeightM, (float)wolf.OriginY + UpM);
            var toCamera = camera != null ? camera.transform.position - centre : Vector3.back;
            _at = Matrix4x4.TRS(centre, Quaternion.LookRotation(-toCamera.normalized, Vector3.up), Vector3.one * RadiusM);
            double beatS = Math.Max(0.2, fight.BeatS);
            float pulse = 0.5f + 0.5f * Mathf.Cos((float)(2 * Math.PI * (song % beatS) / beatS));
            float lift = hunter ? 1.5f : 1f;
            _fill.SetColor("_Color", _hue.linear * FillBrightness * lift * (0.6f + 0.4f * moon));
            _lines.SetColor("_Color", _hue.linear * LineBrightness * lift);
            _rim.SetColor("_Color", _hue.linear * RimBrightness * lift * (moon >= 0.999f ? 1 + 1.5f * pulse : 1));
            Fill(moon);
            Upload(_fillMesh);
            Lines(moon, moon >= 0.999f, hunter, pulse);
            Upload(_lineMesh);
            Rim();
            Upload(_rimMesh);
            _drawn = true;
            Queue(camera);
        }

        /// <summary>The terminator's x at height y on the unit disc, for lit share <paramref name="moon"/>: the lit part lies between it and the right limb.</summary>
        static float Terminator(float y, float moon) => (1 - 2 * moon) * Mathf.Sqrt(Mathf.Max(0, 1 - y * y));

        void Fill(float moon)
        {
            _verts.Clear();
            _tris.Clear();
            if (moon <= 1e-3) return;
            for (int r = 0; r <= Rows; r++)
            {
                float y = -1 + 2f * r / Rows, limb = Mathf.Sqrt(Mathf.Max(0, 1 - y * y));
                _verts.Add(new Vector3(Terminator(y, moon), y, 0));
                _verts.Add(new Vector3(limb, y, 0));
                if (r == 0) continue;
                int a = _verts.Count - 4;
                Quad(a, a + 1, a + 3, a + 2);
            }
        }

        void Lines(float moon, bool full, bool hunter, float pulse)
        {
            _verts.Clear();
            _tris.Clear();
            if (moon > 1e-3)
            {
                // The lit limb and the terminator, as facets.
                for (int r = 0; r < Rows; r++)
                {
                    float y0 = -1 + 2f * r / Rows, y1 = -1 + 2f * (r + 1) / Rows;
                    Line(new Vector2(Mathf.Sqrt(Mathf.Max(0, 1 - y0 * y0)), y0), new Vector2(Mathf.Sqrt(Mathf.Max(0, 1 - y1 * y1)), y1));
                    if (moon < 0.999f) Line(new Vector2(Terminator(y0, moon), y0), new Vector2(Terminator(y1, moon), y1));
                }
            }
            if (full)
            {
                // The full moon's halo, breathing on the beat (two rings for the hunter's moon).
                Circle(1.22f + 0.06f * pulse);
                if (hunter) Circle(1.45f + 0.1f * pulse);
            }
        }

        void Rim()
        {
            _verts.Clear();
            _tris.Clear();
            Circle(1f);
        }

        void Circle(float radius)
        {
            for (int i = 0; i < Segments; i++)
            {
                float a0 = 2 * Mathf.PI * i / Segments, a1 = 2 * Mathf.PI * (i + 1) / Segments;
                Line(new Vector2(Mathf.Cos(a0), Mathf.Sin(a0)) * radius, new Vector2(Mathf.Cos(a1), Mathf.Sin(a1)) * radius);
            }
        }

        /// <summary>A line on the unit disc, <see cref="LineM"/> wide in world metres.</summary>
        void Line(Vector2 a, Vector2 b)
        {
            var d = b - a;
            if (d.sqrMagnitude < 1e-10) return;
            var n = new Vector2(-d.y, d.x).normalized * (LineM / RadiusM / 2);
            int i = _verts.Count;
            _verts.Add(a - n);
            _verts.Add(a + n);
            _verts.Add(b + n);
            _verts.Add(b - n);
            Quad(i, i + 1, i + 2, i + 3);
        }

        /// <summary>A quad seen from either side.</summary>
        void Quad(int a, int b, int c, int d)
        {
            _tris.Add(a); _tris.Add(b); _tris.Add(c);
            _tris.Add(a); _tris.Add(c); _tris.Add(d);
            _tris.Add(a); _tris.Add(c); _tris.Add(b);
            _tris.Add(a); _tris.Add(d); _tris.Add(c);
        }

        void Upload(Mesh mesh)
        {
            mesh.Clear();
            mesh.SetVertices(_verts);
            mesh.SetTriangles(_tris, 0);
            mesh.RecalculateBounds();
        }

        /// <summary>Queues the latest frame's moon again (the stills' second drawing of a frame).</summary>
        public void Queue(Camera camera)
        {
            if (!_drawn) return;
            Draw(_rim, _rimMesh, camera);
            Draw(_fill, _fillMesh, camera);
            Draw(_lines, _lineMesh, camera);
        }

        void Draw(Material material, Mesh mesh, Camera camera)
        {
            if (mesh.vertexCount == 0) return;
            var rp = new RenderParams(material) { layer = _layer, shadowCastingMode = ShadowCastingMode.Off, receiveShadows = false, camera = camera };
            Graphics.RenderMesh(rp, mesh, 0, _at);
        }

        public void Dispose()
        {
            foreach (var o in new UnityEngine.Object[] { _fill, _lines, _rim, _fillMesh, _lineMesh, _rimMesh })
            {
                if (o == null) continue;
                if (Application.isPlaying) UnityEngine.Object.Destroy(o);
                else UnityEngine.Object.DestroyImmediate(o);
            }
        }
    }
}
