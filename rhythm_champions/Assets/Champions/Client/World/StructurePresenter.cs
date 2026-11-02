using System;
using System.Collections.Generic;
using Champions.Render;
using Champions.Sim;
using UnityEngine;

namespace Champions.Client
{
    /// <summary>
    /// Maps the sim's structure events onto the structures as the player sees them (design.md, The World &gt; Structures),
    /// as stand-ins until the trailer's edge look is ported: each a block (a box footprint) or a cylinder (a disc) on its
    /// footprint, <see cref="HeightM"/> tall, whose silhouette glows in the first zone's edge colour over its fill
    /// (<see cref="StandIn"/>). A structure stands when its chunk loads (<see cref="SimEventKind.StructureSpawn"/>) and goes
    /// when it unloads, and a break takes it away on the break's own frame. Breaks at one instant send one structure-break
    /// ring to the world (a special moment, design.md, Restraint) from their middle; a boss's breaks send none, its own
    /// entrance or attack rings for them. The assembly's EventDispatch hands it the events.
    /// </summary>
    public sealed class StructurePresenter : IDisposable
    {
        const double SameInstant = 1e-4;

        /// <summary>A stand-in's height: its footprint's bounding radius, within these (m).</summary>
        public const float MinHeightM = 1f, MaxHeightM = 3f;

        struct Pending
        {
            public int Id;
            public double T;
        }

        readonly StructureRules _rules;
        readonly IWorldCommands _world;
        readonly Material _material;
        readonly int _layer;
        readonly Dictionary<int, GameObject> _standing = new Dictionary<int, GameObject>();
        readonly List<Pending> _pending = new List<Pending>();
        double _ringT = double.NaN;
        int _ringBreaks;
        Vector2 _ringSum;

        /// <summary>Stands the sim's <paramref name="rules"/>' kinds as stand-ins on <paramref name="layer"/>, sending rings to <paramref name="world"/>.</summary>
        public StructurePresenter(StructureRules rules, Palette palette, IWorldCommands world, int layer = 0)
        {
            _rules = rules;
            _world = world;
            _layer = layer;
            var zone = palette.Zones[0];
            _material = StandIn.Material("Structure stand-in", zone.Edge, zone.Fill, IdPass.Structure);
        }

        /// <summary>The structures standing.</summary>
        public int Standing => _standing.Count;

        /// <summary>Where structure <paramref name="id"/> stands (its footprint's middle on the ground), if it stands.</summary>
        public bool TryGet(int id, out Vector3 position)
        {
            bool stands = _standing.TryGetValue(id, out var go);
            position = stands ? go.transform.position : default;
            return stands;
        }

        /// <summary>A stand-in's height for <paramref name="kind"/>.</summary>
        public static float HeightM(StructureKind kind) => Mathf.Clamp((float)kind.Bound, MinHeightM, MaxHeightM);

        /// <summary>Breaks this frame showed (their impact frame).</summary>
        public int BrokeThisFrame { get; private set; }

        /// <summary>One settled event, in log order.</summary>
        public void Take(in SimEvent e)
        {
            if (!double.IsNaN(_ringT) && (e.Kind != SimEventKind.StructureBreak || Math.Abs(e.T - _ringT) > SameInstant)) Flush();
            switch (e.Kind)
            {
                case SimEventKind.StructureSpawn:
                    Add(e.I0, _rules.Kinds[e.I1], new Vector2((float)e.D0, (float)e.D1), (float)e.D2);
                    break;
                case SimEventKind.StructureGone:
                    Remove(e.I0);
                    break;
                case SimEventKind.StructureBreak:
                {
                    int i = _pending.Count;
                    while (i > 0 && _pending[i - 1].T > e.T) i--;
                    _pending.Insert(i, new Pending { Id = e.I0, T = e.T });
                    if (e.I2 != (int)BreakCause.Blow) break;
                    if (double.IsNaN(_ringT)) _ringT = e.T;
                    _ringBreaks++;
                    _ringSum += new Vector2((float)e.D3, (float)e.D4);
                    break;
                }
            }
        }

        /// <summary>Ends a batch of events: the breaks at one instant send their ring.</summary>
        public void Flush()
        {
            if (double.IsNaN(_ringT)) return;
            _world?.Ring(RingKind.StructureBreak, _ringSum / _ringBreaks, _ringT, Color.white, Mathf.Clamp01(_ringBreaks / 6f));
            _ringT = double.NaN;
            _ringBreaks = 0;
            _ringSum = Vector2.zero;
        }

        /// <summary>The frame at song time <paramref name="song"/>: the breaks it reaches take their structures away.</summary>
        public void Present(double song)
        {
            BrokeThisFrame = 0;
            while (_pending.Count > 0 && _pending[0].T <= song)
            {
                int id = _pending[0].Id;
                _pending.RemoveAt(0);
                if (Remove(id)) BrokeThisFrame++;
            }
        }

        /// <summary>Structure <paramref name="id"/> of <paramref name="kind"/> stands at <paramref name="at"/> (ground x, y), turned <paramref name="angle"/> (the sim's: radians from +x toward +y).</summary>
        void Add(int id, StructureKind kind, Vector2 at, float angle)
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

        bool Remove(int id)
        {
            if (!_standing.TryGetValue(id, out var go)) return false;
            _standing.Remove(id);
            StandIn.Destroy(go);
            return true;
        }

        public void Dispose()
        {
            foreach (var go in _standing.Values) StandIn.Destroy(go);
            _standing.Clear();
            StandIn.Destroy(_material);
        }
    }
}
