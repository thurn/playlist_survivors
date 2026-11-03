using System;
using UnityEngine;

namespace Champions.Render.Crowd
{
    /// <summary>
    /// One pose of a <see cref="MonsterRig"/>'s nodes, built up by layers: <see cref="Sample"/> a clip, <see cref="Blend"/>
    /// other poses over it by body part (a swing over the walking legs), then <see cref="Palette"/> composes it, the
    /// upper body twisted toward an aim, into the skinning matrices the edge shaders read (<see cref="MonsterFigure"/>).
    /// Ported from the deleted edge cuts' (git b243e08b^). No allocation after construction.
    /// </summary>
    public sealed class RigPose
    {
        readonly MonsterRig _rig;
        readonly Matrix4x4[] _world;
        readonly int _spineNodes;

        public readonly Quaternion[] Rotations;
        public readonly Vector3[] Positions;

        public RigPose(MonsterRig rig)
        {
            _rig = rig;
            int n = rig.Nodes.Length;
            Rotations = new Quaternion[n];
            Positions = new Vector3[n];
            _world = new Matrix4x4[n];
            foreach (var l in rig.Layers)
                if (l == (byte)RigLayer.Spine) _spineNodes++;
        }

        public MonsterRig Rig => _rig;

        /// <summary>Every node as the clip has it at <paramref name="time"/> seconds (wrapped for a loop, else clamped), between its two nearest frames.</summary>
        public void Sample(int clip, double time)
        {
            var c = _rig.Clips[clip];
            double t = c.Loop ? (c.Length > 0 ? time - Math.Floor(time / c.Length) * c.Length : 0) : Math.Max(0, Math.Min(c.Length, time));
            int f0 = Math.Min((int)(t * MonsterRig.Rate), c.Frames - 2);
            double t0 = f0 / MonsterRig.Rate, t1 = Math.Min((f0 + 1) / MonsterRig.Rate, c.Length);
            float u = (float)Math.Max(0, Math.Min(1, (t - t0) / Math.Max(t1 - t0, 1e-6)));
            int n = Rotations.Length, a = (c.FirstFrame + f0) * n * MonsterRig.Stride, b = a + n * MonsterRig.Stride;
            var f = _rig.Frames;
            for (int i = 0; i < n; i++, a += MonsterRig.Stride, b += MonsterRig.Stride)
            {
                var qa = new Quaternion(f[a], f[a + 1], f[a + 2], f[a + 3]);
                var qb = new Quaternion(f[b], f[b + 1], f[b + 2], f[b + 3]);
                Rotations[i] = Quaternion.Slerp(qa, qb, u);
                Positions[i] = Vector3.LerpUnclamped(new Vector3(f[a + 4], f[a + 5], f[a + 6]), new Vector3(f[b + 4], f[b + 5], f[b + 6]), u);
            }
        }

        /// <summary>Copies another pose of the same rig.</summary>
        public void Set(RigPose other)
        {
            Array.Copy(other.Rotations, Rotations, Rotations.Length);
            Array.Copy(other.Positions, Positions, Positions.Length);
        }

        /// <summary>
        /// Blends toward <paramref name="other"/>: the legs' nodes by <paramref name="legs"/>, the spine's by
        /// <paramref name="spine"/>, the arms' by <paramref name="arms"/>. Where the spine takes more of it than the legs
        /// (a swing laid over walking legs), the spine's first node blends toward the other's turn in model space rather
        /// than from its own hips, so the upper body swings the way the clip meant whatever the legs' hips do.
        /// </summary>
        public void Blend(RigPose other, float legs, float spine, float arms)
        {
            int s = _rig.SpineRoot;
            bool overLegs = s >= 0 && spine > legs;
            Quaternion mine = default, theirs = default;
            if (overLegs)
            {
                mine = ModelRotation(_rig.Parents[s]) * Rotations[s];
                theirs = other.ModelRotation(_rig.Parents[s]) * other.Rotations[s];
            }
            var layers = _rig.Layers;
            for (int i = 0; i < Rotations.Length; i++)
            {
                float w = layers[i] == (byte)RigLayer.Legs ? legs : layers[i] == (byte)RigLayer.Spine ? spine : arms;
                if (w <= 0) continue;
                Rotations[i] = Quaternion.Slerp(Rotations[i], other.Rotations[i], w);
                Positions[i] = Vector3.Lerp(Positions[i], other.Positions[i], w);
            }
            if (overLegs)
                Rotations[s] = Quaternion.Inverse(ModelRotation(_rig.Parents[s])) * Quaternion.Slerp(mine, theirs, spine);
        }

        /// <summary>A node's rotation in the model's root space (-1: the root's own, none).</summary>
        Quaternion ModelRotation(int node)
        {
            var q = Quaternion.identity;
            for (int i = node; i >= 0; i = _rig.Parents[i]) q = Rotations[i] * q;
            return q;
        }

        /// <summary>
        /// Composes the pose into <paramref name="rows"/>, 3 per palette entry (the root-space skinning matrix's rows), its
        /// spine turned by <paramref name="twist"/> radians about the vertical (clockwise from above, as a yaw), an equal
        /// share at each spine node, so the chest, the arms and what they hold face that much further round.
        /// </summary>
        public void Palette(Vector4[] rows, float twist)
        {
            var root = Matrix4x4.Scale(_rig.RootScale);
            bool turning = _spineNodes > 0 && twist != 0;
            var turn = turning ? Matrix4x4.Rotate(Quaternion.AngleAxis(twist * Mathf.Rad2Deg / _spineNodes, Vector3.up)) : Matrix4x4.identity;
            for (int i = 0; i < Rotations.Length; i++)
            {
                int p = _rig.Parents[i];
                var m = (p < 0 ? root : _world[p]) * Matrix4x4.TRS(Positions[i], Rotations[i], _rig.Scales[i]);
                if (turning && _rig.Layers[i] == (byte)RigLayer.Spine)
                {
                    var at = (Vector3)m.GetColumn(3);
                    m = Matrix4x4.Translate(at) * turn * Matrix4x4.Translate(-at) * m;
                }
                _world[i] = m;
            }
            for (int e = 0; e < _rig.Entries.Length; e++)
            {
                var m = _world[_rig.Entries[e]] * _rig.Binds[e];
                rows[3 * e] = m.GetRow(0);
                rows[3 * e + 1] = m.GetRow(1);
                rows[3 * e + 2] = m.GetRow(2);
            }
        }

        /// <summary>Extend a rigid attachment along local Y around its grip after composing the pose.</summary>
        public void Extend(Vector4[] rows, int entry, float gripY, float tipY, float metres)
        {
            var world = _world[_rig.Entries[entry]];
            float length = world.MultiplyVector(Vector3.up * (tipY - gripY)).magnitude;
            if (length < 1e-5f) return;
            float stretch = metres / length;
            var scale = Matrix4x4.Scale(new Vector3(1 + Mathf.Min(.9f, stretch * .4f), 1 + stretch, 1));
            scale.m13 = -gripY * stretch;
            var m = world * scale * _rig.Binds[entry];
            for (int i = 0; i < 3; i++) rows[3 * entry + i] = m.GetRow(i);
        }

        /// <summary>A node's position in root space as the latest <see cref="Palette"/> composed it.</summary>
        public Vector3 NodePosition(int node) => _world[node].GetColumn(3);
    }
}
