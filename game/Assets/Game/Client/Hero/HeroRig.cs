using System;
using System.Linq;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace Game.Client.Hero
{
    /// <summary>
    /// The knight on a playable graph driven by song time (unity-game.md, The Hero; demo3d's stock
    /// crowd graph, copied and owned): every clip playable's speed is 0 and its time is set from a
    /// <see cref="HeroPose"/> each frame, and the graph is evaluated at once (manual update), so the
    /// pose drawn is exactly the frame's song time, stills included. Three layers:
    /// - the legs (base): the idle, the hop or walk (the run clip), the backpedal (the run clip played
    ///   backward) and the attack's two slots, full body;
    /// - the torso (masked to the body: the hips and spine), over the legs where the walk doesn't carry it
    ///   (<see cref="HeroPose.Hips"/>): the sword idle or the attack;
    /// - the upper body (masked to the head, arms and fingers, always on top): the sword idle's arms, which
    ///   hold the blade clear of the body through the unarmed hop and walk, and the attack's slots.
    /// An attack slot is a mixer over the attack clips with one clip at full weight, so a clip can
    /// crossfade from another swing of itself. After the graph, the pose's twist turns the spine's bones about
    /// the vertical, an equal share each, so the chest, arms and blade face a walking swing's aim.
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
        readonly AnimationLayerMixerPlayable _layers;
        readonly AnimationMixerPlayable _legs, _torso, _upper;
        readonly AnimationClipPlayable _legsIdle, _torsoIdle, _upperIdle, _run, _back;
        readonly Slot _legsA, _legsB, _torsoA, _torsoB, _upperA, _upperB;
        readonly AvatarMask _torsoMask, _upperMask;
        readonly Transform[] _spine;

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
            _layers = AnimationLayerMixerPlayable.Create(_graph, 3);
            output.SetSourcePlayable(_layers);

            _legs = AnimationMixerPlayable.Create(_graph, 5);
            _legsIdle = Clip(bake.clips[(int)HeroClip.Idle], _legs, 0);
            _run = Clip(bake.clips[(int)HeroClip.Run], _legs, 1);
            _back = Clip(bake.clips[(int)HeroClip.Run], _legs, 2);
            _legsA = AttackSlot(bake, _legs, 3);
            _legsB = AttackSlot(bake, _legs, 4);
            _graph.Connect(_legs, 0, _layers, 0);
            _layers.SetInputWeight(0, 1);

            _torso = UpperLayer(bake, out _torsoIdle, out _torsoA, out _torsoB);
            _graph.Connect(_torso, 0, _layers, 1);
            _torsoMask = Mask(AvatarMaskBodyPart.Body);
            _layers.SetLayerMaskFromAvatarMask(1, _torsoMask);

            _upper = UpperLayer(bake, out _upperIdle, out _upperA, out _upperB);
            _graph.Connect(_upper, 0, _layers, 2);
            _layers.SetInputWeight(2, 1);
            _upperMask = Mask(AvatarMaskBodyPart.Head, AvatarMaskBodyPart.LeftArm, AvatarMaskBodyPart.RightArm,
                AvatarMaskBodyPart.LeftFingers, AvatarMaskBodyPart.RightFingers);
            _layers.SetLayerMaskFromAvatarMask(2, _upperMask);

            _spine = new[] { HumanBodyBones.Spine, HumanBodyBones.Chest, HumanBodyBones.UpperChest }
                .Select(Animator.GetBoneTransform).Where(b => b != null).ToArray();
        }

        /// <summary>A masked layer's mixer: the sword idle and the attack's two slots.</summary>
        AnimationMixerPlayable UpperLayer(HeroBake bake, out AnimationClipPlayable idle, out Slot a, out Slot b)
        {
            var mixer = AnimationMixerPlayable.Create(_graph, 3);
            idle = Clip(bake.clips[(int)HeroClip.Idle], mixer, 0);
            a = AttackSlot(bake, mixer, 1);
            b = AttackSlot(bake, mixer, 2);
            return mixer;
        }

        static AvatarMask Mask(params AvatarMaskBodyPart[] parts)
        {
            var mask = new AvatarMask();
            for (int i = 0; i < (int)AvatarMaskBodyPart.LastBodyPart; i++) mask.SetHumanoidBodyPartActive((AvatarMaskBodyPart)i, false);
            foreach (var part in parts) mask.SetHumanoidBodyPartActive(part, true);
            return mask;
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

        /// <summary>
        /// Poses the knight: its root at the hop's height over the ground, turned to <paramref name="yaw"/> (radians), its clips
        /// from the pose, and its spine twisted by the pose's twist.
        /// </summary>
        public void Pose(in HeroPose p, float yaw)
        {
            Knight.transform.SetPositionAndRotation(new Vector3((float)p.X, p.Lift, (float)p.Y), Quaternion.Euler(0, yaw * Mathf.Rad2Deg, 0));
            float attack = Mathf.Clamp01(p.Attack), a = Mathf.Clamp01(p.AShare);
            float run = Mathf.Clamp01(p.Run), back = Mathf.Clamp01(p.Back), walking = Mathf.Min(1, run + back);
            float legsAttack = (1 - walking) * attack;
            _legsIdle.SetTime(p.IdleTime);
            _run.SetTime(p.RunTime);
            _back.SetTime(p.BackTime);
            _legs.SetInputWeight(0, 1 - walking - legsAttack);
            _legs.SetInputWeight(1, run);
            _legs.SetInputWeight(2, back);
            _legs.SetInputWeight(3, legsAttack * a);
            _legs.SetInputWeight(4, legsAttack * (1 - a));
            Set(_legsA, p.A, attack * a);
            Set(_legsB, p.B, attack * (1 - a));
            _layers.SetInputWeight(1, 1 - Mathf.Clamp01(p.Hips));
            SetUpper(_torso, _torsoIdle, _torsoA, _torsoB, p, attack, a);
            SetUpper(_upper, _upperIdle, _upperA, _upperB, p, attack, a);
            _graph.Evaluate(0);
            if (p.Twist != 0 && _spine.Length > 0)
            {
                var turn = Quaternion.AngleAxis(p.Twist * Mathf.Rad2Deg / _spine.Length, Vector3.up);
                foreach (var bone in _spine) bone.rotation = turn * bone.rotation;
            }
        }

        static void SetUpper(AnimationMixerPlayable mixer, AnimationClipPlayable idle, Slot a, Slot b, in HeroPose p, float attack, float share)
        {
            idle.SetTime(p.IdleTime);
            mixer.SetInputWeight(0, 1 - attack);
            mixer.SetInputWeight(1, attack * share);
            mixer.SetInputWeight(2, attack * (1 - share));
            Set(a, p.A, attack * share);
            Set(b, p.B, attack * (1 - share));
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
            if (_torsoMask != null) UnityEngine.Object.Destroy(_torsoMask);
            if (_upperMask != null) UnityEngine.Object.Destroy(_upperMask);
        }
    }
}
