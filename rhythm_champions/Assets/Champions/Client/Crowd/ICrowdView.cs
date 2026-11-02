using System;
using UnityEngine;

namespace Champions.Client
{
    /// <summary>What a mob is doing on a frame, for its clip.</summary>
    public enum CrowdAction : byte
    {
        /// <summary>Standing: <see cref="CrowdMob.ActionTime"/> is song time (an idle loops on it).</summary>
        Idle,
        /// <summary>Stepping or gliding: <see cref="CrowdMob.ActionTime"/> is the ground it has covered, m (a walk's stride matches it, so its feet never slide).</summary>
        Walk,
        /// <summary>An elite's move: <see cref="CrowdMob.ActionTime"/> is the time to its landing, s (negative after it), for the attack's impact frame.</summary>
        Attack,
    }

    /// <summary>One mob as the crowd presenter hands it to the drawing on a frame.</summary>
    public struct CrowdMob
    {
        /// <summary>The sim's enemy id (a slot a later spawn may reuse) and its cast index (enemies.json).</summary>
        public int Id, Cast;
        /// <summary>Its feet on the ground (x, 0, z: the sim's x, y); a flyer's hover is the drawing's.</summary>
        public Vector3 Position;
        /// <summary>Its facing: radians about +y from +z (Mathf.Atan2(dir.x, dir.y)).</summary>
        public float Yaw;
        public CrowdAction Action;
        public float ActionTime;
        /// <summary>A hit's flash (1 on its frame, fading out) and a telegraph's flare of its lines (a mushroom's spore, 0 or 1).</summary>
        public float HitFlash, Flare;
        /// <summary>A number seeding what varies per mob (an idle's phase, a shatter's throws).</summary>
        public int Serial;
        /// <summary>
        /// Its latest beat step: seconds since it started (the step moves from its beat with a sharp ease-out), and its travel
        /// on the ground (m); <see cref="Long"/> without one (a glide, a stand).
        /// </summary>
        public float StepAge;
        public Vector2 StepTravel;
        /// <summary>
        /// Its latest kick: seconds since its contact (negative through a lunge's wind-up of <see cref="KickWind"/> s), and
        /// its way on the ground times its strength: away from a hit's blow, toward an elite's target; <see cref="Long"/> without.
        /// </summary>
        public float KickAge, KickWind;
        public Vector2 Kick;

        /// <summary>An age past every spring's settling: no event.</summary>
        public const float Long = 1e4f;
    }

    /// <summary>
    /// The horde's drawing (plan.md, The Client: presenters built against small command interfaces): each frame
    /// <see cref="CrowdPresenter"/> hands it every mob alive and every mob dying on that frame, then has it draw. The ported
    /// trailer crowd (ps-6y2.28) implements it; <see cref="StandInCrowd"/> draws rim-lit stand-ins until then.
    /// </summary>
    public interface ICrowdView : IDisposable
    {
        /// <summary>A frame at song time <paramref name="song"/> begins.</summary>
        void Begin(double song);

        /// <summary>A mob alive this frame.</summary>
        void Mob(in CrowdMob mob);

        /// <summary>
        /// A mob killed at song time <paramref name="time"/>, on this frame: it shatters from this pose, thrown along
        /// <paramref name="blow"/> (a ground unit vector) as hard as <paramref name="power"/> (the killing hit, in Sword Slash's base hits).
        /// </summary>
        void Break(in CrowdMob mob, double time, Vector2 blow, float power);

        /// <summary>The frame's mobs and shards, drawn for <paramref name="camera"/>.</summary>
        void Draw(double song, Camera camera);

        /// <summary>Queues the frame's draws again (the stills' second drawing of a frame).</summary>
        void Queue(Camera camera);
    }
}
