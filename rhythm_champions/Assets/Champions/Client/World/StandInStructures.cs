using System.Collections.Generic;
using Champions.Render;
using Champions.Sim;
using UnityEngine;

namespace Champions.Client
{
    /// <summary>
    /// The structures without baked ones (a checkout without the vendor files): each a block (a box footprint) or a cylinder
    /// (a disc) on its footprint, <see cref="HeightM"/> tall, whose silhouette glows in the first zone's edge colour over its
    /// fill (<see cref="StandIn"/>), gone on its break's frame.
    /// </summary>
    public sealed class StandInStructures : IStructureView
    {
        /// <summary>A stand-in's height: its footprint's bounding radius, within these (m).</summary>
        public const float MinHeightM = 1f, MaxHeightM = 3f;

        readonly Material _material;
        readonly int _layer;
        readonly Dictionary<int, GameObject> _standing = new Dictionary<int, GameObject>();

        public StandInStructures(Palette palette, int layer = 0)
        {
            _layer = layer;
            var zone = palette.Zones[0];
            _material = StandIn.Material("Structure stand-in", zone.Edge, zone.Fill, IdPass.Structure);
        }

        /// <summary>A stand-in's height for <paramref name="kind"/>.</summary>
        public static float HeightM(StructureKind kind) => Mathf.Clamp((float)kind.Bound, MinHeightM, MaxHeightM);

        public void Stand(int id, StructureKind kind, Vector2 at, float angle)
        {
            Remove(id);
            float height = HeightM(kind);
            bool box = kind.Shape == FootprintShape.Box;
            var size = box
                ? new Vector3(2 * (float)kind.HalfX, height, 2 * (float)kind.HalfY)
                : new Vector3(2 * (float)kind.Radius, height, 2 * (float)kind.Radius);
            var go = StandIn.Make("Structure stand-in " + kind.Id, box ? PrimitiveType.Cube : PrimitiveType.Cylinder, size, _material, _layer);
            // Unity's yaw turns +x toward -z: the sim's angle the other way.
            go.transform.SetPositionAndRotation(new Vector3(at.x, 0, at.y), Quaternion.Euler(0, -angle * Mathf.Rad2Deg, 0));
            _standing[id] = go;
        }

        public bool Remove(int id)
        {
            if (!_standing.TryGetValue(id, out var go)) return false;
            _standing.Remove(id);
            StandIn.Destroy(go);
            return true;
        }

        public void Hit(int id, double t) { }

        public bool Break(int id, double t, Vector2 blow, float power) => Remove(id);

        public void Present(double song, Camera camera) { }

        public void Queue(Camera camera) { }

        public void Dispose()
        {
            foreach (var go in _standing.Values) StandIn.Destroy(go);
            _standing.Clear();
            StandIn.Destroy(_material);
        }
    }
}
