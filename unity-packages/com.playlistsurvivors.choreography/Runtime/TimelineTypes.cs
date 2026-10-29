using System;
using System.Numerics;

namespace PS.Choreography
{
    // The timeline contract (docs/tech-demo-3d/choreography.md, "Timeline contract").
    //
    // Ground positions are System.Numerics.Vector2 in metres: X is Unity's world x and Y is
    // Unity's world z. Facings are yaws in radians about Unity's +y, 0 facing +z (ground +Y),
    // positive turning toward +x: yaw = atan2(dx, dy). A segment's FacingTo is unwrapped next to
    // its FacingFrom, so a linear blend turns the short way. Effect positions are world-space
    // Vector3 (x, height, z), or a local offset when the effect is attached to an entity.
    //
    // An entity's segments are sorted by T0. Each one holds from its T0 until the next one's T0
    // (past its own T1 the entity rests at To), so a later segment can supersede the end of an
    // earlier one: a knockback or a death cuts into a gait step without reshaping its curve.

    public enum EntityKind { Hero, Monster, Projectile }

    public readonly struct EntityInfo
    {
        public readonly int Id, Type;          // Type: roster or projectile index (-1 for the hero)
        public readonly EntityKind Kind;
        public readonly double Spawn, Despawn; // song seconds
        public readonly uint Seed;             // tint and scale jitter

        public EntityInfo(int id, int type, EntityKind kind, double spawn, double despawn, uint seed)
        {
            Id = id; Type = type; Kind = kind; Spawn = spawn; Despawn = despawn; Seed = seed;
        }
    }

    public enum Ease { Pulse, Linear }       // Pulse: one gait step

    public readonly struct MotionSegment
    {
        public readonly int Entity;
        public readonly double T0, T1;       // song seconds
        public readonly Vector2 From, To;    // ground plane, metres
        public readonly float FacingFrom, FacingTo;  // radians
        public readonly Ease Ease;

        public MotionSegment(int entity, double t0, double t1, Vector2 from, Vector2 to, float facingFrom, float facingTo, Ease ease)
        {
            Entity = entity; T0 = t0; T1 = t1; From = from; To = to; FacingFrom = facingFrom; FacingTo = facingTo; Ease = ease;
        }

        /// <summary>Progress from 0 to 1 at song time <paramref name="t"/>, clamped.</summary>
        public float Progress(double t)
        {
            if (t >= T1 || T1 <= T0) return t >= T0 ? 1f : 0f;
            if (t <= T0) return 0f;
            return Easing.Progress(Ease, (float)((t - T0) / (T1 - T0)));
        }

        public Vector2 PositionAt(double t) => Vector2.Lerp(From, To, Progress(t));

        public float FacingAt(double t)
        {
            float u = t >= T1 || T1 <= T0 ? 1f : t <= T0 ? 0f : (float)((t - T0) / (T1 - T0));
            return FacingFrom + (FacingTo - FacingFrom) * Math.Min(1f, u * 3f);
        }
    }

    public static class Easing
    {
        /// <summary>Pulse: 1 - (1 - u)^3, fast after the beat and settled by the next.</summary>
        public static float Progress(Ease ease, float u)
        {
            if (u <= 0f) return 0f;
            if (u >= 1f) return 1f;
            if (ease == Ease.Linear) return u;
            float v = 1f - u;
            return 1f - v * v * v;
        }

        public static float Yaw(Vector2 dir) => (float)Math.Atan2(dir.X, dir.Y);

        public static Vector2 Dir(float yaw) => new Vector2((float)Math.Sin(yaw), (float)Math.Cos(yaw));

        /// <summary><paramref name="to"/> moved by whole turns to lie within half a turn of <paramref name="from"/>.</summary>
        public static float Unwrap(float from, float to)
        {
            const float Tau = (float)(2 * Math.PI);
            float d = to - from;
            d -= Tau * (float)Math.Floor((d + Math.PI) / Tau);
            return from + d;
        }

        /// <summary>An angle wrapped to [-pi, pi).</summary>
        public static float Wrap(float a) => Unwrap(0f, a);
    }

    public enum ActionKind { Spawn, Attack, Hit, Tick, Death, Parry }

    public readonly struct ActionEvent
    {
        public readonly int Entity, Clip, Target;    // Target -1 for none; Clip -1 for none
        public readonly ActionKind Kind;
        public readonly double Impact;               // on the beat grid
        public readonly float Strength;              // 0..1, accents near 1
        // When the sim fitted the clip (attacks): its retimed start and speed, so the client plays
        // exactly what the sim scheduled (the hero's root-motion segment runs from ClipStart).
        // NaN start for actions whose clip simply starts at the blow (hits, deaths).
        public readonly double ClipStart;
        public readonly float ClipSpeed;

        public ActionEvent(int entity, ActionKind kind, double impact, int clip, int target, float strength,
            double clipStart = double.NaN, float clipSpeed = 1f)
        {
            Entity = entity; Kind = kind; Impact = impact; Clip = clip; Target = target; Strength = strength;
            ClipStart = clipStart; ClipSpeed = clipSpeed;
        }

        public bool HasClipTiming => !double.IsNaN(ClipStart);
    }

    public readonly struct EffectEvent     // AttachTo -1 for world space
    {
        public readonly int Effect, AttachTo;
        public readonly uint Seed;
        public readonly double Time;
        public readonly Vector3 Position;
        public readonly float Yaw, Scale;

        public EffectEvent(int effect, int attachTo, uint seed, double time, Vector3 position, float yaw, float scale)
        {
            Effect = effect; AttachTo = attachTo; Seed = seed; Time = time; Position = position; Yaw = yaw; Scale = scale;
        }
    }

    public readonly struct SoundEvent
    {
        public readonly int Sound;
        public readonly double Time;
        public readonly float Gain;
        public SoundEvent(int sound, double time, float gain) { Sound = sound; Time = time; Gain = gain; }
    }

    public readonly struct FramingSample
    {
        public readonly Vector2 Focus;
        public readonly float ViewWidth;
        public FramingSample(Vector2 focus, float viewWidth) { Focus = focus; ViewWidth = viewWidth; }
    }
}
