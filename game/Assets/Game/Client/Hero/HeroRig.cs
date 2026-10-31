using System;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace Game.Client.Hero
{
    /// <summary>
    /// The knight on a playable graph driven by song time (unity-game.md, The Hero; demo3d's stock
    /// crowd graph, copied and owned): every clip playable's speed is 0 and its time is set from a
    /// <see cref="HeroPose"/> each frame, and the graph is evaluated at once (manual update), so the
    /// pose drawn is exactly the frame's song time, stills included. Two layers:
    /// - the legs (base): the idle, the hop (the run clip) and the attack's two slots, full body;
    /// - the upper body (masked to the body, head, arms and fingers, always on top): the sword idle's
    ///   arms, which hold the blade clear of the body through the unarmed hop, and the attack's slots.
    /// An attack slot is a mixer over the attack clips with one clip at full weight, so a clip can
    /// crossfade from another swing of itself.
    /// </summary>
    public sealed class HeroRig : IDisposable
    {
        const int FirstAttack = (int)HeroClip.SlashL;
        static readonly int AttackClips = Enum.GetValues(typeof(HeroClip)).Length - FirstAttack;

        sealed class Slot
        {
            public AnimationMixerPlayable Mixer;
            public AnimationClipPlayable[] Clips;
            public int Active = -1;
        }

        readonly PlayableGraph _graph;
        readonly AnimationMixerPlayable _legs, _upper;
        readonly AnimationClipPlayable _legsIdle, _upperIdle, _run;
        readonly Slot _legsA, _legsB, _upperA, _upperB;
        readonly AvatarMask _mask;

        public GameObject Knight { get; }
        public Animator Animator { get; }

        public HeroRig(HeroBake bake, Transform parent)
        {
            Knight = UnityEngine.Object.Instantiate(bake.knight, parent);
            Knight.name = "Hero";
            Animator = Knight.GetComponentInChildren<Animator>();
            if (Animator == null || !Animator.isHuman) throw new InvalidOperationException("HeroRig: the baked knight has no humanoid Animator");
            Animator.applyRootMotion = false;
            foreach (var r in Knight.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                r.skinnedMotionVectors = true;
                r.updateWhenOffscreen = true; // a hop and a swing reach past the bind pose's bounds
            }

            _graph = PlayableGraph.Create("hero");
            _graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            var output = AnimationPlayableOutput.Create(_graph, "hero", Animator);
            var layers = AnimationLayerMixerPlayable.Create(_graph, 2);
            output.SetSourcePlayable(layers);

            _legs = AnimationMixerPlayable.Create(_graph, 4);
            _legsIdle = Clip(bake.clips[(int)HeroClip.Idle], _legs, 0);
            _run = Clip(bake.clips[(int)HeroClip.Run], _legs, 1);
            _legsA = AttackSlot(bake, _legs, 2);
            _legsB = AttackSlot(bake, _legs, 3);
            _graph.Connect(_legs, 0, layers, 0);
            layers.SetInputWeight(0, 1);

            _upper = AnimationMixerPlayable.Create(_graph, 3);
            _upperIdle = Clip(bake.clips[(int)HeroClip.Idle], _upper, 0);
            _upperA = AttackSlot(bake, _upper, 1);
            _upperB = AttackSlot(bake, _upper, 2);
            _graph.Connect(_upper, 0, layers, 1);
            layers.SetInputWeight(1, 1);
            _mask = new AvatarMask();
            for (int i = 0; i < (int)AvatarMaskBodyPart.LastBodyPart; i++) _mask.SetHumanoidBodyPartActive((AvatarMaskBodyPart)i, false);
            foreach (var part in new[] { AvatarMaskBodyPart.Body, AvatarMaskBodyPart.Head, AvatarMaskBodyPart.LeftArm, AvatarMaskBodyPart.RightArm,
                         AvatarMaskBodyPart.LeftFingers, AvatarMaskBodyPart.RightFingers })
                _mask.SetHumanoidBodyPartActive(part, true);
            layers.SetLayerMaskFromAvatarMask(1, _mask);
        }

        AnimationClipPlayable Clip(AnimationClip clip, AnimationMixerPlayable into, int input)
        {
            var p = AnimationClipPlayable.Create(_graph, clip);
            p.SetApplyFootIK(false);
            p.SetSpeed(0);
            _graph.Connect(p, 0, into, input);
            into.SetInputWeight(input, 0);
            return p;
        }

        Slot AttackSlot(HeroBake bake, AnimationMixerPlayable into, int input)
        {
            var s = new Slot { Mixer = AnimationMixerPlayable.Create(_graph, AttackClips), Clips = new AnimationClipPlayable[AttackClips] };
            for (int c = 0; c < AttackClips; c++) s.Clips[c] = Clip(bake.clips[FirstAttack + c], s.Mixer, c);
            _graph.Connect(s.Mixer, 0, into, input);
            into.SetInputWeight(input, 0);
            return s;
        }

        /// <summary>Poses the knight: its root at the hop's height over the ground, turned to <paramref name="yaw"/> (radians), its clips from the pose.</summary>
        public void Pose(in HeroPose p, float yaw)
        {
            Knight.transform.SetPositionAndRotation(new Vector3((float)p.X, p.Lift, (float)p.Y), Quaternion.Euler(0, yaw * Mathf.Rad2Deg, 0));
            float attack = Mathf.Clamp01(p.Attack), a = Mathf.Clamp01(p.AShare), run = Mathf.Clamp01(p.Run);
            float legsAttack = (1 - run) * attack;
            _legsIdle.SetTime(p.IdleTime);
            _upperIdle.SetTime(p.IdleTime);
            _run.SetTime(p.RunTime);
            _legs.SetInputWeight(0, 1 - run - legsAttack);
            _legs.SetInputWeight(1, run);
            _legs.SetInputWeight(2, legsAttack * a);
            _legs.SetInputWeight(3, legsAttack * (1 - a));
            _upper.SetInputWeight(0, 1 - attack);
            _upper.SetInputWeight(1, attack * a);
            _upper.SetInputWeight(2, attack * (1 - a));
            Set(_legsA, p.A, attack * a);
            Set(_upperA, p.A, attack * a);
            Set(_legsB, p.B, attack * (1 - a));
            Set(_upperB, p.B, attack * (1 - a));
            _graph.Evaluate(0);
        }

        static void Set(Slot s, in HeroSlot slot, float weight)
        {
            int c = weight > 0 ? (int)slot.Clip - FirstAttack : -1;
            if (c != s.Active)
            {
                if (s.Active >= 0) s.Mixer.SetInputWeight(s.Active, 0);
                if (c >= 0) s.Mixer.SetInputWeight(c, 1);
                s.Active = c;
            }
            if (c >= 0) s.Clips[c].SetTime(slot.Time);
        }

        public void Dispose()
        {
            if (_graph.IsValid()) _graph.Destroy();
            if (Knight != null) UnityEngine.Object.Destroy(Knight);
            if (_mask != null) UnityEngine.Object.Destroy(_mask);
        }
    }
}
