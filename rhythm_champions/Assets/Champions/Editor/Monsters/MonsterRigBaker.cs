using System;
using System.Collections.Generic;
using System.Linq;
using Champions.Render.Crowd;
using UnityEditor;
using UnityEngine;

namespace Champions.EditorTools.Monsters
{
    /// <summary>
    /// Bakes the rig of a figure posed on the CPU (monsters.json's `figure`: the hero), beside its <see cref="MonsterType"/>:
    /// every transform from the model's root to a bone the type's entries use, each clip's local poses of them sampled at
    /// <see cref="MonsterRig.Rate"/> on the same spawn the bone texture was sampled on, and each node's layer from the
    /// humanoid avatar (the spine's bones; the arms, the head and what the hands hold above them; the legs and hips
    /// below). A looping clip's drift is removed from the hips as the bone texture's is. The bake checks its own poses:
    /// composed by <see cref="RigPose"/>, they give the sampled skinning matrices. Ported from the deleted edge cuts'
    /// EdgeRigBaker (git b243e08b^); the strides are monsters.json's.
    /// </summary>
    public static class MonsterRigBaker
    {
        /// <summary>The largest difference the check allows between a composed matrix and the sampled one (m, at scale 1).</summary>
        const float Tolerance = 2e-3f;

        /// <param name="bones">The type's entries' transforms, in entry order, and <paramref name="binds"/> their binds.</param>
        /// <param name="rootTR">The root's rotation and translation (the bone texture's space, without the scale).</param>
        public static MonsterRig Bake(MonsterModel model, GameObject spawned, Animator animator, Transform[] bones, Matrix4x4[] binds, Matrix4x4 rootTR)
        {
            var root = spawned.transform;
            if (animator == null || !animator.isHuman) throw new Exception($"MonsterRigBaker: {model.id} has no humanoid Animator");
            var order = root.GetComponentsInChildren<Transform>(true).ToList(); // parents before children
            var wanted = new HashSet<Transform>();
            foreach (var bone in bones)
                for (var t = bone; t != null && t != root; t = t.parent) wanted.Add(t);
            var nodes = order.Where(wanted.Contains).ToList();
            var index = new Dictionary<Transform, int>();
            for (int i = 0; i < nodes.Count; i++) index[nodes[i]] = i;

            var spine = new[] { HumanBodyBones.Spine, HumanBodyBones.Chest, HumanBodyBones.UpperChest }
                .Select(animator.GetBoneTransform).Where(b => b != null && index.ContainsKey(b)).ToList();
            if (spine.Count == 0) throw new Exception($"MonsterRigBaker: {model.id}'s spine bones skin nothing in its mesh");
            var top = spine[spine.Count - 1];
            var rig = ScriptableObject.CreateInstance<MonsterRig>();
            rig.name = model.id + "_rig";
            rig.Nodes = nodes.Select(t => t.name).ToArray();
            rig.Parents = nodes.Select(t => t.parent != null && index.TryGetValue(t.parent, out int p) ? p : -1).ToArray();
            rig.Layers = nodes.Select(t => (byte)(spine.Contains(t) ? RigLayer.Spine : t.IsChildOf(top) ? RigLayer.Arms : RigLayer.Legs)).ToArray();
            rig.Scales = nodes.Select(t => t.localScale).ToArray();
            rig.RootScale = root.localScale;
            rig.Entries = bones.Select(b => index[b]).ToArray();
            rig.Binds = binds.ToArray();
            rig.SpineRoot = index[spine[0]];
            for (int i = 0; i < nodes.Count; i++)
                if (rig.Parents[i] < 0 && nodes[i].parent != root)
                    throw new Exception($"MonsterRigBaker: {model.id}'s node {nodes[i].name} hangs from {nodes[i].parent.name}, outside the rig");

            var hips = animator.GetBoneTransform(HumanBodyBones.Hips);
            var rootInv = rootTR.inverse;
            var clips = new List<RigClip>();
            var frames = new List<float>();
            var expected = new List<(int Clip, int Frame, Matrix4x4[] Palette)>();
            AnimationMode.StartAnimationMode();
            try
            {
                foreach (var c in model.clips)
                {
                    var clip = model.LoadClip(c);
                    var rc = new RigClip
                    {
                        Role = c.role, Name = clip.name, FirstFrame = frames.Count / (nodes.Count * MonsterRig.Stride),
                        Frames = RigClip.FrameCount(clip.length), Length = clip.length, Loop = c.loop, Impact = c.impact,
                        End = c.end, StrideM = c.strideM,
                    };
                    // A looping clip that travels would jump at its seam: its horizontal drift is taken off the hips
                    // linearly (the sim moves the figure instead), as the bone texture's is.
                    var drift = Vector3.zero;
                    if (c.loop)
                    {
                        drift = At(animator, clip, rc.Length, rootInv, hips) - At(animator, clip, 0, rootInv, hips);
                        drift.y = 0;
                        if (drift.magnitude <= 0.05f) drift = Vector3.zero;
                    }
                    for (int f = 0; f < rc.Frames; f++)
                    {
                        float time = Math.Min(rc.Length, f / MonsterRig.Rate);
                        Pose(animator, clip, time);
                        var shift = -drift * (time / rc.Length);
                        foreach (var t in nodes)
                        {
                            var q = t.localRotation;
                            var p = t.localPosition;
                            if (t == hips && shift != Vector3.zero)
                                p += (rootInv * t.parent.localToWorldMatrix).inverse.MultiplyVector(shift);
                            frames.AddRange(new[] { q.x, q.y, q.z, q.w, p.x, p.y, p.z });
                        }
                        if (f == 0 || f == rc.Frames / 2)
                            expected.Add((clips.Count, f, bones.Select((b, e) =>
                                Matrix4x4.Translate(shift) * rootInv * b.localToWorldMatrix * binds[e]).ToArray()));
                    }
                    clips.Add(rc);
                }
            }
            finally
            {
                AnimationMode.StopAnimationMode();
            }
            rig.Clips = clips.ToArray();
            rig.Frames = frames.ToArray();
            Check(rig, expected);
            Debug.Log($"MonsterRigBaker: {model.id}: {nodes.Count} nodes, {rig.Entries.Length} entries, {clips.Count} clips at {MonsterRig.Rate} Hz");
            return rig;
        }

        static void Pose(Animator animator, AnimationClip clip, float time)
        {
            AnimationMode.BeginSampling();
            AnimationMode.SampleAnimationClip(animator.gameObject, clip, time);
            AnimationMode.EndSampling();
        }

        static Vector3 At(Animator animator, AnimationClip clip, float time, Matrix4x4 rootInv, Transform bone)
        {
            Pose(animator, clip, time);
            return rootInv.MultiplyPoint3x4(bone.position);
        }

        /// <summary>Composes each sampled check frame with <see cref="RigPose"/> and compares it with the matrices sampled.</summary>
        static void Check(MonsterRig rig, List<(int Clip, int Frame, Matrix4x4[] Palette)> expected)
        {
            var pose = new RigPose(rig);
            var rows = new Vector4[rig.Entries.Length * 3];
            float worst = 0;
            foreach (var (clip, frame, palette) in expected)
            {
                pose.Sample(clip, Math.Min(rig.Clips[clip].Length, frame / MonsterRig.Rate));
                pose.Palette(rows, 0);
                for (int e = 0; e < palette.Length; e++)
                    for (int r = 0; r < 3; r++)
                    {
                        var d = rows[3 * e + r] - palette[e].GetRow(r);
                        worst = Mathf.Max(worst, Mathf.Max(Mathf.Max(Mathf.Abs(d.x), Mathf.Abs(d.y)), Mathf.Max(Mathf.Abs(d.z), Mathf.Abs(d.w))));
                    }
            }
            if (worst > Tolerance)
                throw new Exception($"MonsterRigBaker: {rig.name}'s composed poses differ from the sampled ones by {worst:F4} (more than {Tolerance})");
        }
    }
}
