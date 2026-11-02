using System;
using UnityEngine;

namespace Champions.Render.Crowd
{
    /// <summary>
    /// An edge cut's skeleton for posing on the CPU (design.md, The Hero &gt; Animation): the hero's legs keep the run
    /// under an upper body that swings and twists to its aim, which the crowd's bone texture (whole-body skinning
    /// matrices, two clips blended) can't layer. So the hero's bake also keeps every clip as its bones' local poses at
    /// <see cref="Rate"/>: each node (a transform on the way from the model's root to a bone the cut is skinned to) with
    /// its parent and a layer, and each of the type's palette entries with its node and bind. <see cref="RigPose"/>
    /// samples, layers and composes them into the palette the edge shaders read. Baked by the edge-cut baker into the
    /// ignored Assets/Baked/EdgeCuts/&lt;model&gt;/ beside its <see cref="EdgeCutType"/>; the library lists it.
    /// </summary>
    public sealed class EdgeRig : ScriptableObject
    {
        /// <summary>Frames per second of the baked poses: twice the crowd's, for the hero's fast swings.</summary>
        public const float Rate = 60f;
        /// <summary>A node's values in <see cref="Frames"/>: its local rotation (x, y, z, w), then its local position.</summary>
        public const int Stride = 7;

        /// <summary>The <see cref="EdgeCutType"/> it poses.</summary>
        public string Id;
        public string[] Nodes = Array.Empty<string>();
        /// <summary>Each node's parent, always earlier in the list; -1 for a child of the model's root.</summary>
        public int[] Parents = Array.Empty<int>();
        /// <summary>Each node's <see cref="RigLayer"/>.</summary>
        public byte[] Layers = Array.Empty<byte>();
        /// <summary>Each node's local scale, which no clip animates.</summary>
        public Vector3[] Scales = Array.Empty<Vector3>();
        /// <summary>The model's root's scale (its height's): the space every node composes in.</summary>
        public Vector3 RootScale = Vector3.one;
        /// <summary>The type's palette entries' nodes, in the type's entry order.</summary>
        public int[] Entries = Array.Empty<int>();
        /// <summary>Each entry's bind: root-space rest positions into its node's space.</summary>
        public Matrix4x4[] Binds = Array.Empty<Matrix4x4>();
        /// <summary>The node whose model-space turn the upper body keeps from a clip laid over the legs (the first spine bone).</summary>
        public int SpineRoot = -1;
        public RigClip[] Clips = Array.Empty<RigClip>();
        /// <summary>Every clip's frames, one after another: per frame, per node, <see cref="Stride"/> values.</summary>
        public float[] Frames = Array.Empty<float>();

        public int Clip(string role)
        {
            for (int i = 0; i < Clips.Length; i++)
                if (Clips[i].Role == role) return i;
            return -1;
        }

        /// <summary>The rig of the library's type <paramref name="id"/>, or null.</summary>
        public static EdgeRig Of(EdgeCutLibrary lib, string id)
        {
            if (lib == null) return null;
            foreach (var r in lib.Rigs)
                if (r != null && r.Id == id) return r;
            return null;
        }
    }

    /// <summary>A node's part of the body, for layering a clip over the legs.</summary>
    public enum RigLayer : byte
    {
        /// <summary>The root bone, the hips and the legs.</summary>
        Legs = 0,
        /// <summary>The spine's bones, which the twist turns.</summary>
        Spine = 1,
        /// <summary>The neck, head and arms, and what the hands hold.</summary>
        Arms = 2,
    }

    /// <summary>One clip in a rig: where its frames start, how many (both ends included) and its annotations in seconds.</summary>
    [Serializable]
    public struct RigClip
    {
        public string Role;
        public string Name;
        public int FirstFrame;
        public int Frames;
        public float Length;
        public bool Loop;
        /// <summary>From the recipe; negative where absent. A looping clip's stride is measured where the recipe gives none.</summary>
        public float Impact, End, StrideM;

        /// <summary>Frames a clip of this length bakes to at the rig's rate: both ends included.</summary>
        public static int FrameCount(float length) => Math.Max(2, (int)Math.Ceiling(length * EdgeRig.Rate - 1e-3f) + 1);
    }
}
