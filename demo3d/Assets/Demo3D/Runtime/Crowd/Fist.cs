using System;
using UnityEngine;

namespace Demo3D.Runtime.Crowd
{
    /// <summary>
    /// A humanoid hand's closed fist, in the hand bone's space, from its finger bones (crowd.md,
    /// Weapons): the handle of a held weapon runs through it across the knuckles, from the fused
    /// fingers' root to the index finger's, so the weapon leaves the fist on the thumb's side, its edge
    /// toward the knuckles. That is how Synty's combat clips hold a weapon, and it is 90 degrees off a
    /// Synty weapon placed on the hand bone as it comes (its +Y along the hand's Y, out of the palm).
    /// The hero's blade (HeroBlade) and the crowd's weapons (the roster's `grip`) both sit in it.
    /// </summary>
    public readonly struct Fist
    {
        /// <summary>Through the fist across the knuckles, toward the index finger (the thumb's side).</summary>
        public readonly Vector3 Axis;
        /// <summary>The way the fingers point, square to Axis: the knuckles' side.</summary>
        public readonly Vector3 Edge;
        /// <summary>Out of the palm, toward the thumb, square to both.</summary>
        public readonly Vector3 Palm;
        /// <summary>The middle of the knuckles.</summary>
        public readonly Vector3 Knuckles;

        Fist(Vector3 axis, Vector3 edge, Vector3 palm, Vector3 knuckles)
        {
            Axis = axis;
            Edge = edge;
            Palm = palm;
            Knuckles = knuckles;
        }

        /// <summary>The turn that puts an object's +Y along Axis and its +X along Edge.</summary>
        public Quaternion Turn => Quaternion.LookRotation(Vector3.Cross(Edge, Axis), Axis);

        /// <summary>The fist of a humanoid's right or left hand, from its bones in their current pose.</summary>
        public static Fist Of(Animator animator, bool right)
        {
            var hand = animator != null && animator.isHuman
                ? animator.GetBoneTransform(right ? HumanBodyBones.RightHand : HumanBodyBones.LeftHand) : null;
            if (hand == null) throw new InvalidOperationException($"Fist: {Name(animator)} has no humanoid {(right ? "right" : "left")} hand");
            Vector3 Local(Transform t) => hand.InverseTransformPoint(t.position);
            var index = Bone(animator, hand, right ? HumanBodyBones.RightIndexProximal : HumanBodyBones.LeftIndexProximal, "IndexFinger_01");
            var rest = Bone(animator, hand, right ? HumanBodyBones.RightMiddleProximal : HumanBodyBones.LeftMiddleProximal, "Finger_01")
                ?? Bone(animator, hand, right ? HumanBodyBones.RightRingProximal : HumanBodyBones.LeftRingProximal, null);
            var thumb = Bone(animator, hand, right ? HumanBodyBones.RightThumbProximal : HumanBodyBones.LeftThumbProximal, "Thumb_01");
            if (index == null || rest == null || index.childCount == 0)
                throw new InvalidOperationException($"Fist: {Name(animator)}'s {hand.name} has no index and middle finger bones");
            Vector3 a = Local(index), r = Local(rest), next = Local(index.GetChild(0));
            var axis = (a - r).normalized;
            var finger = next - a;
            var edge = (finger - Vector3.Dot(finger, axis) * axis).normalized;
            var palm = Vector3.Cross(axis, edge);
            var knuckles = (a + r) * 0.5f;
            if (thumb != null && Vector3.Dot(palm, Local(thumb) - knuckles) < 0) palm = -palm;
            return new Fist(axis, edge, palm, knuckles);
        }

        static string Name(Animator animator) => animator != null ? animator.name : "(no animator)";

        static Transform Bone(Animator animator, Transform hand, HumanBodyBones bone, string name)
        {
            var t = animator.GetBoneTransform(bone);
            if (t != null || name == null) return t;
            foreach (Transform c in hand)
                if (c.name.StartsWith(name, StringComparison.Ordinal)) return c;
            return null;
        }
    }
}
