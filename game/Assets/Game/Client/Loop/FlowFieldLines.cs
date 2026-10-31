using System;
using System.Collections.Generic;
using PS.Game.Sim;
using UnityEngine;
using UnityEngine.Rendering;

namespace Game.Client.Loop
{
    /// <summary>
    /// The debug overlay's flow field (unity-game.md, Debug Surfaces; the sim's <see cref="FlowFieldView"/>): an
    /// arrow on the ground in each cell within <see cref="RadiusM"/> of the hero, the way the enemies there head,
    /// cyan near the hero to magenta at the field's reach, and a dim red cross on each blocked cell. Lines over
    /// everything (Resources/Debug/DebugLines), rebuilt each frame it shows; a player without graphics draws none.
    /// </summary>
    public sealed class FlowFieldLines : IDisposable
    {
        public const float RadiusM = 18, HeightM = 0.08f;
        static readonly Color32 Near = new Color32(90, 230, 255, 220), Far = new Color32(255, 80, 220, 220);
        static readonly Color32 Wall = new Color32(255, 70, 60, 110);

        readonly GameObject _go;
        readonly Mesh _mesh;
        readonly Material _material;
        readonly List<Vector3> _vertices = new List<Vector3>();
        readonly List<Color32> _colours = new List<Color32>();
        readonly List<int> _indices = new List<int>();

        public FlowFieldLines()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) return;
            var shader = Resources.Load<Shader>("Debug/DebugLines");
            if (shader == null)
            {
                Debug.LogWarning("FlowFieldLines: no shader at Resources/Debug/DebugLines");
                return;
            }
            _material = new Material(shader) { name = "Flow field lines" };
            _mesh = new Mesh { name = "Flow field", indexFormat = IndexFormat.UInt32 };
            _mesh.MarkDynamic();
            _go = new GameObject("Flow field");
            _go.AddComponent<MeshFilter>().sharedMesh = _mesh;
            var renderer = _go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = _material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            _go.SetActive(false);
        }

        /// <summary>Arrows drawn in the latest frame shown.</summary>
        public int Arrows { get; private set; }

        public void Hide()
        {
            if (_go != null) _go.SetActive(false);
        }

        /// <summary>The field around the hero at (hx, hy) now; nothing when the run has no enemies.</summary>
        public void Draw(FlowFieldView field, double hx, double hy)
        {
            if (_go == null) return;
            _go.SetActive(field.Exists);
            if (!field.Exists) return;
            _vertices.Clear();
            _colours.Clear();
            _indices.Clear();
            Arrows = 0;
            double cell = field.CellM;
            int reach = (int)Math.Ceiling(RadiusM / cell);
            int cx0 = (int)Math.Floor((hx - field.MinX) / cell), cy0 = (int)Math.Floor((hy - field.MinY) / cell);
            float arm = (float)(0.4 * cell), head = (float)(0.18 * cell);
            for (int cy = Math.Max(0, cy0 - reach); cy <= Math.Min(field.H - 1, cy0 + reach); cy++)
                for (int cx = Math.Max(0, cx0 - reach); cx <= Math.Min(field.W - 1, cx0 + reach); cx++)
                {
                    float x = (float)(field.MinX + (cx + 0.5) * cell), z = (float)(field.MinY + (cy + 0.5) * cell);
                    if ((x - hx) * (x - hx) + (z - hy) * (z - hy) > RadiusM * RadiusM) continue;
                    var centre = new Vector3(x, HeightM, z);
                    if (field.Blocked(cx, cy))
                    {
                        Line(centre + new Vector3(-head, 0, -head), centre + new Vector3(head, 0, head), Wall);
                        Line(centre + new Vector3(-head, 0, head), centre + new Vector3(head, 0, -head), Wall);
                        continue;
                    }
                    int cost = field.Cost(cx, cy);
                    if (cost == FlowFieldView.Unreached || !field.Direction(x, z, out double dx, out double dy)) continue;
                    var colour = Color32.Lerp(Near, Far, Mathf.Clamp01((float)cost / field.MaxCost));
                    var dir = new Vector3((float)dx, 0, (float)dy);
                    var side = new Vector3(-dir.z, 0, dir.x);
                    Vector3 tail = centre - dir * arm, tip = centre + dir * arm;
                    Line(tail, tip, colour);
                    Line(tip, tip - dir * head + side * head * 0.7f, colour);
                    Line(tip, tip - dir * head - side * head * 0.7f, colour);
                    Arrows++;
                }
            _mesh.Clear();
            _mesh.SetVertices(_vertices);
            _mesh.SetColors(_colours);
            _mesh.SetIndices(_indices, MeshTopology.Lines, 0, calculateBounds: true);
        }

        void Line(Vector3 a, Vector3 b, Color32 colour)
        {
            _indices.Add(_vertices.Count);
            _vertices.Add(a);
            _colours.Add(colour);
            _indices.Add(_vertices.Count);
            _vertices.Add(b);
            _colours.Add(colour);
        }

        public void Dispose()
        {
            if (_go != null) UnityEngine.Object.Destroy(_go);
            if (_mesh != null) UnityEngine.Object.Destroy(_mesh);
            if (_material != null) UnityEngine.Object.Destroy(_material);
        }
    }
}
