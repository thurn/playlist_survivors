using System;
using System.Collections.Generic;
using Champions.Sim;
using UnityEngine;
using UnityEngine.Rendering;

namespace Champions.Render.Telegraphs
{
    /// <summary>
    /// The ground telegraphs (design.md, Bosses &gt; Every boss, The Horde &gt; Threats; plan.md, Rendering: Telegraph): each
    /// mark is a shape on the ground (a sim <see cref="HitShape"/>) in its colour, drawn by the Telegraph shader on a quad
    /// over its bounds. It shows its outline from its telegraph's start, fills toward its far edge until its landing, flashes
    /// as it lands (holding a dimmer glow while it lasts: a breath's burn, sweeping when it sweeps) and fades; one called off
    /// fades at once. A frame presents every mark at its song time (<see cref="Present"/>) and queues them for a camera.
    /// </summary>
    public sealed class TelegraphRenderer : IDisposable
    {
        public const string MaterialPath = "Telegraphs/Telegraph";
        /// <summary>A mark lies this far above the floor, under every body.</summary>
        public const float HeightM = 0.03f;
        /// <summary>The landing's flash decays over about this long; then the mark fades over <see cref="FadeS"/>; one called off over <see cref="CallOffS"/>.</summary>
        public const double FlashS = 0.09, FadeS = 0.35, CallOffS = 0.15;
        /// <summary>The landing's flash at its peak, and a lasting mark's glow while it lasts (a breath's burn), as shares of the lines'.</summary>
        public const float FlashPeak = 0.45f, HoldGlow = 0.12f;
        /// <summary>The quad reaches this far past the shape, for the outline's smoothing.</summary>
        const float MarginM = 0.3f;

        static readonly int IdColor = Shader.PropertyToID("_Color");
        static readonly int IdA = Shader.PropertyToID("_A");
        static readonly int IdSize = Shader.PropertyToID("_Size");
        static readonly int IdState = Shader.PropertyToID("_State");

        struct Mark
        {
            public int Id;
            public HitShape Shape;
            public double Start, Land, Landed, Hold, Sweep, SweepHalfArc, OffAt;
            public Color Colour;
        }

        readonly Material _material;
        readonly Mesh _quad;
        readonly int _layer;
        readonly List<Mark> _marks = new List<Mark>();
        readonly List<MaterialPropertyBlock> _blocks = new List<MaterialPropertyBlock>();
        readonly List<Matrix4x4> _matrices = new List<Matrix4x4>();
        int _drawn;

        /// <param name="layer">The layer the marks draw on (the cameras that see them).</param>
        public TelegraphRenderer(int layer = 0)
        {
            var shared = Resources.Load<Material>(MaterialPath) ?? throw new InvalidOperationException("no Resources/" + MaterialPath + ".mat");
            _material = new Material(shared) { name = "Telegraph (instance)" };
            _layer = layer;
            _quad = new Mesh { name = "Telegraph quad" };
            _quad.SetVertices(new[] { new Vector3(-1, 0, -1), new Vector3(1, 0, -1), new Vector3(1, 0, 1), new Vector3(-1, 0, 1) });
            _quad.SetTriangles(new[] { 0, 2, 1, 0, 3, 2 }, 0);
            _quad.bounds = new Bounds(Vector3.zero, new Vector3(2, 0.1f, 2));
        }

        /// <summary>Marks showing now (after the latest <see cref="Present"/>).</summary>
        public int Showing => _drawn;

        /// <summary>
        /// A mark <paramref name="id"/> covering <paramref name="shape"/>, filling from <paramref name="start"/> to land at
        /// <paramref name="land"/> (as planned), in <paramref name="colour"/> (linear, HDR).
        /// </summary>
        public void Add(int id, in HitShape shape, double start, double land, Color colour)
        {
            _marks.Add(new Mark { Id = id, Shape = shape, Start = start, Land = land, Landed = double.NaN, OffAt = double.NaN, Colour = colour });
        }

        /// <summary>
        /// Mark <paramref name="id"/> landed at <paramref name="t"/>: it flashes, and holds a glow for <paramref name="hold"/> s
        /// (a burn); a cone sweeping <paramref name="sweep"/> radians (its telegraph covered the whole sweep) burns as a
        /// narrower cone crossing it.
        /// </summary>
        public void Land(int id, double t, double hold = 0, double sweep = 0)
        {
            int i = Find(id);
            if (i < 0) return;
            var m = _marks[i];
            m.Landed = t;
            m.Land = t;
            m.Hold = hold;
            m.Sweep = sweep;
            m.SweepHalfArc = m.Shape.HalfArc - Math.Abs(sweep) / 2;
            _marks[i] = m;
        }

        /// <summary>Mark <paramref name="id"/> was called off at <paramref name="t"/>: it fades without landing.</summary>
        public void CallOff(int id, double t)
        {
            int i = Find(id);
            if (i < 0) return;
            var m = _marks[i];
            m.OffAt = t;
            _marks[i] = m;
        }

        /// <summary>Every mark at song time <paramref name="song"/>: the finished go, and the rest queue for <paramref name="camera"/>.</summary>
        public void Present(double song, Camera camera)
        {
            _drawn = 0;
            for (int i = _marks.Count - 1; i >= 0; i--)
            {
                var m = _marks[i];
                if (State(m, song, out var shape, out float fill, out float flash, out float fade)) Write(shape, m.Colour, fill, flash, fade);
                else if (Done(m, song)) _marks.RemoveAt(i);
            }
            Queue(camera);
        }

        /// <summary>Queues the latest presented marks for <paramref name="camera"/> again (the stills' second drawing).</summary>
        public void Queue(Camera camera)
        {
            for (int k = 0; k < _drawn; k++)
            {
                var rp = new RenderParams(_material)
                {
                    matProps = _blocks[k], camera = camera, layer = _layer, shadowCastingMode = ShadowCastingMode.Off,
                    receiveShadows = false, lightProbeUsage = LightProbeUsage.Off, reflectionProbeUsage = ReflectionProbeUsage.Off,
                    motionVectorMode = MotionVectorGenerationMode.ForceNoMotion,
                    worldBounds = new Bounds(_matrices[k].GetColumn(3), new Vector3(_matrices[k].m00, 0.2f, _matrices[k].m22) * 2),
                };
                Graphics.RenderMesh(rp, _quad, 0, _matrices[k]);
            }
        }

        int Find(int id)
        {
            for (int i = _marks.Count - 1; i >= 0; i--)
                if (_marks[i].Id == id) return i;
            return -1;
        }

        static bool Done(in Mark m, double song)
        {
            if (!double.IsNaN(m.OffAt)) return song >= m.OffAt + CallOffS;
            return !double.IsNaN(m.Landed) && song >= m.Landed + m.Hold + FadeS;
        }

        /// <summary>Mark <paramref name="m"/>'s look at <paramref name="song"/>; false when it shows nothing then.</summary>
        static bool State(in Mark m, double song, out HitShape shape, out float fill, out float flash, out float fade)
        {
            shape = m.Shape;
            fill = m.Land > m.Start ? Mathf.Clamp01((float)((song - m.Start) / (m.Land - m.Start))) : 1;
            flash = 0;
            fade = 1;
            if (song < m.Start - 1e-6) return false;
            if (!double.IsNaN(m.OffAt))
            {
                if (song < m.OffAt) return true;
                fill = m.Land > m.Start ? Mathf.Clamp01((float)((m.OffAt - m.Start) / (m.Land - m.Start))) : 1;
                fade = 1 - (float)((song - m.OffAt) / CallOffS);
                return fade > 0;
            }
            if (double.IsNaN(m.Landed) || song < m.Landed) return true;
            double since = song - m.Landed;
            fill = 1;
            flash = FlashPeak * Mathf.Exp(-(float)(since / FlashS));
            if (since < m.Hold)
            {
                flash += HoldGlow;
                if (m.Sweep != 0 && shape.Kind == ShapeKind.Cone)
                {
                    double facing = Math.Atan2(shape.DirY, shape.DirX), a = facing - m.Sweep / 2 + m.Sweep * since / m.Hold;
                    shape.DirX = Math.Cos(a);
                    shape.DirY = Math.Sin(a);
                    shape.HalfArc = m.SweepHalfArc;
                }
                return true;
            }
            fade = 1 - (float)((since - m.Hold) / FadeS);
            return fade > 0;
        }

        void Write(in HitShape s, Color colour, float fill, float flash, float fade)
        {
            if (_blocks.Count <= _drawn)
            {
                _blocks.Add(new MaterialPropertyBlock());
                _matrices.Add(Matrix4x4.identity);
            }
            float kind;
            Vector4 a, size;
            Vector2 min, max;
            switch (s.Kind)
            {
                case ShapeKind.Path:
                    kind = 2;
                    a = new Vector4((float)s.X, (float)s.Y, (float)s.X2, (float)s.Y2);
                    size = new Vector4((float)s.Radius, 0, 0, 0);
                    min = Vector2.Min(new Vector2(a.x, a.y), new Vector2(a.z, a.w)) - Vector2.one * (float)s.Radius;
                    max = Vector2.Max(new Vector2(a.x, a.y), new Vector2(a.z, a.w)) + Vector2.one * (float)s.Radius;
                    break;
                case ShapeKind.Cone:
                    kind = 3;
                    a = new Vector4((float)s.X, (float)s.Y, 0, 0);
                    size = new Vector4((float)s.Radius, 0, (float)s.HalfArc, (float)Math.Atan2(s.DirY, s.DirX));
                    min = new Vector2(a.x, a.y) - Vector2.one * (float)s.Radius;
                    max = new Vector2(a.x, a.y) + Vector2.one * (float)s.Radius;
                    break;
                default:
                    kind = s.Kind == ShapeKind.Ring ? 1 : 0;
                    a = new Vector4((float)s.X, (float)s.Y, 0, 0);
                    size = new Vector4((float)s.Radius, (float)s.Inner, 0, 0);
                    min = new Vector2(a.x, a.y) - Vector2.one * (float)s.Radius;
                    max = new Vector2(a.x, a.y) + Vector2.one * (float)s.Radius;
                    break;
            }
            var block = _blocks[_drawn];
            block.SetColor(IdColor, colour);
            block.SetVector(IdA, a);
            block.SetVector(IdSize, size);
            block.SetVector(IdState, new Vector4(fill, flash, fade, kind));
            var centre = (min + max) / 2;
            var half = (max - min) / 2 + Vector2.one * MarginM;
            _matrices[_drawn] = Matrix4x4.TRS(new Vector3(centre.x, HeightM, centre.y), Quaternion.identity, new Vector3(half.x, 1, half.y));
            _drawn++;
        }

        public void Dispose()
        {
            Destroy(_material);
            Destroy(_quad);
        }

        static void Destroy(UnityEngine.Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(o);
            else UnityEngine.Object.DestroyImmediate(o);
        }
    }
}
