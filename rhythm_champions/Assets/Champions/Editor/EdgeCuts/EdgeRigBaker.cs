using System;
using System.Collections.Generic;
using System.Linq;
using Champions.Render.Crowd;
using UnityEditor;
using UnityEngine;

namespace Champions.EditorTools.EdgeCuts
{
    /// <summary>
    /// Bakes the rig of a model posed on the CPU (the hero's: `champions edgecut` passes `-rig` for a recipe whose role
    /// is hero), beside its <see cref="EdgeCutType"/>: every transform from the model's root to a bone the cut's entries
    /// use, each clip's local poses of them sampled at <see cref="EdgeRig.Rate"/> on the same spawn the bone texture
    /// was sampled on, and each node's layer from the humanoid avatar (the spine's bones; the arms, the head and what
    /// the hands hold above them; the legs and hips below). A looping clip's drift is removed from the hips as the
    /// bone texture's is, and its stride measured from the planted foot where the recipe gives none. The bake checks
    /// its own poses: composed by <see cref="RigPose"/>, they give the bone texture's matrices.
    /// </summary>
    public static class EdgeRigBaker
    {
        /// <summary>The largest difference the check allows between a composed matrix and the sampled one (m, at scale 1).</summary>
        const float Tolerance = 2e-3f;

        public static EdgeRig Bake(EdgeCutModel model, EdgeCutSource source, IReadOnlyList<EdgeCutSource.Entry> entries)
        {
            var root = source.Root.transform;
            var animator = source.Root.GetComponentInChildren<Animator>();
            if (animator == null || !animator.isHuman) throw new Exception($"EdgeRigBaker: {model.id} has no humanoid Animator");
            var order = root.GetComponentsInChildren<Transform>(true).ToList(); // parents before children
            var wanted = new HashSet<Transform>();
            foreach (var e in entries)
                for (var t = e.Bone; t != null && t != root; t = t.parent) wanted.Add(t);
            var nodes = order.Where(wanted.Contains).ToList();
            var index = new Dictionary<Transform, int>();
            for (int i = 0; i < nodes.Count; i++) index[nodes[i]] = i;

            var spine = new[] { HumanBodyBones.Spine, HumanBodyBones.Chest, HumanBodyBones.UpperChest }
                .Select(animator.GetBoneTransform).Where(b => b != null && index.ContainsKey(b)).ToList();
            if (spine.Count == 0) throw new Exception($"EdgeRigBaker: {model.id}'s spine bones skin nothing in its cut");
            var top = spine[spine.Count - 1];
            var rig = ScriptableObject.CreateInstance<EdgeRig>();
            rig.Id = model.id;
            rig.Nodes = nodes.Select(t => t.name).ToArray();
            rig.Parents = nodes.Select(t => t.parent != null && index.TryGetValue(t.parent, out int p) ? p : -1).ToArray();
            rig.Layers = nodes.Select(t => (byte)(spine.Contains(t) ? RigLayer.Spine : t.IsChildOf(top) ? RigLayer.Arms : RigLayer.Legs)).ToArray();
            rig.Scales = nodes.Select(t => t.localScale).ToArray();
            rig.RootScale = root.localScale;
            rig.Entries = entries.Select(e => index[e.Bone]).ToArray();
            rig.Binds = entries.Select(e => e.Bind).ToArray();
            rig.SpineRoot = index[spine[0]];
            for (int i = 0; i < nodes.Count; i++)
                if (rig.Parents[i] < 0 && nodes[i].parent != root)
                    throw new Exception($"EdgeRigBaker: {model.id}'s node {nodes[i].name} hangs from {nodes[i].parent.name}, outside the rig");

            var hips = animator.GetBoneTransform(HumanBodyBones.Hips);
            var feet = new[] { animator.GetBoneTransform(HumanBodyBones.LeftFoot), animator.GetBoneTransform(HumanBodyBones.RightFoot) };
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
                        Role = c.role, Name = clip.name, FirstFrame = frames.Count / (nodes.Count * EdgeRig.Stride),
                        Frames = RigClip.FrameCount(clip.length), Length = clip.length, Loop = c.loop, Impact = c.impact,
                        End = c.end, StrideM = c.strideM,
                    };
                    var drift = Vector3.zero;
                    if (c.loop)
                    {
                        drift = At(animator, clip, rc.Length, source, hips) - At(animator, clip, 0, source, hips);
                        drift.y = 0;
                        if (drift.magnitude <= 0.05f) drift = Vector3.zero;
                    }
                    var foot = new Vector3[rc.Frames, 2];
                    for (int f = 0; f < rc.Frames; f++)
                    {
                        float time = Math.Min(rc.Length, f / EdgeRig.Rate);
                        Pose(animator, clip, time);
                        var rootInv = source.RootTR.inverse;
                        var shift = -drift * (time / rc.Length);
                        foreach (var t in nodes)
                        {
                            var q = t.localRotation;
                            var p = t.localPosition;
                            if (t == hips && shift != Vector3.zero)
                                p += (rootInv * t.parent.localToWorldMatrix).inverse.MultiplyVector(shift);
                            frames.AddRange(new[] { q.x, q.y, q.z, q.w, p.x, p.y, p.z });
                        }
                        for (int k = 0; k < 2; k++) foot[f, k] = rootInv.MultiplyPoint3x4(feet[k].position);
                        if (f == 0 || f == rc.Frames / 2)
                            expected.Add((clips.Count, f, entries.Select(e =>
                                Matrix4x4.Translate(shift) * rootInv * e.Bone.localToWorldMatrix * e.Bind).ToArray()));
                    }
                    if (c.loop && rc.StrideM < 0) rc.StrideM = Stride(foot, rc);
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
            foreach (var c in rig.Clips.Where(c => c.Loop))
                Debug.Log($"EdgeRigBaker: {model.id}'s {c.Role} ({c.Name}) strides {c.StrideM:F3} m in its {c.Length:F3} s cycle");
            return rig;
        }

        static void Pose(Animator animator, AnimationClip clip, float time)
        {
            AnimationMode.BeginSampling();
            AnimationMode.SampleAnimationClip(animator.gameObject, clip, time);
            AnimationMode.EndSampling();
        }

        static Vector3 At(Animator animator, AnimationClip clip, float time, EdgeCutSource source, Transform bone)
        {
            Pose(animator, clip, time);
            return source.RootTR.inverse.MultiplyPoint3x4(bone.position);
        }

        /// <summary>How far above its lowest a foot still counts as planted (m).</summary>
        const float PlantedM = 0.03f;

        /// <summary>
        /// A looping clip's ground covered per cycle, whichever way it runs: on each frame a foot is planted (within
        /// <see cref="PlantedM"/> of its lowest), the body moves over it as fast as it slides across the ground; the
        /// median of that speed, times the cycle's length (a run's flight frames, both feet up, say nothing of it).
        /// </summary>
        static float Stride(Vector3[,] feet, RigClip c)
        {
            var lowest = new float[2];
            for (int k = 0; k < 2; k++)
            {
                lowest[k] = float.MaxValue;
                for (int f = 0; f < c.Frames; f++) lowest[k] = Mathf.Min(lowest[k], feet[f, k].y);
            }
            var speeds = new List<double>();
            for (int f = 1; f < c.Frames - 1; f++)
            {
                int k = feet[f, 0].y - lowest[0] <= feet[f, 1].y - lowest[1] ? 0 : 1;
                if (feet[f, k].y - lowest[k] > PlantedM) continue;
                var d = feet[f + 1, k] - feet[f - 1, k];
                double dt = Math.Min(c.Length, (f + 1) / EdgeRig.Rate) - (f - 1) / EdgeRig.Rate;
                speeds.Add(new Vector2(d.x, d.z).magnitude / dt);
            }
            if (speeds.Count == 0) return 0;
            speeds.Sort();
            return (float)(speeds[speeds.Count / 2] * c.Length);
        }

        /// <summary>Composes each sampled check frame with <see cref="RigPose"/> and compares it with the matrices sampled.</summary>
        static void Check(EdgeRig rig, List<(int Clip, int Frame, Matrix4x4[] Palette)> expected)
        {
            var pose = new RigPose(rig);
            var rows = new Vector4[rig.Entries.Length * 3];
            float worst = 0;
            foreach (var (clip, frame, palette) in expected)
            {
                pose.Sample(clip, Math.Min(rig.Clips[clip].Length, frame / EdgeRig.Rate));
                pose.Palette(rows, 0);
                for (int e = 0; e < palette.Length; e++)
                    for (int r = 0; r < 3; r++)
                    {
                        var d = rows[3 * e + r] - palette[e].GetRow(r);
                        worst = Mathf.Max(worst, Mathf.Max(Mathf.Max(Mathf.Abs(d.x), Mathf.Abs(d.y)), Mathf.Max(Mathf.Abs(d.z), Mathf.Abs(d.w))));
                    }
            }
            if (worst > Tolerance)
                throw new Exception($"EdgeRigBaker: {rig.Id}'s composed poses differ from the sampled ones by {worst:F4} (more than {Tolerance})");
        }
    }
}
