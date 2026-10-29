using System;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace Demo3D.Runtime.Crowd.Bench
{
    /// <summary>
    /// TD5's test scenario: the roster's clips at random phases, standing in for TD4's timeline
    /// evaluation until it lands. Every monster walks its own circle (smooth turns, the walk's
    /// phase following distance). In combat (S2-like) each monster, every 7 s, attacks, takes a
    /// hit (flash, flinch, knockback) or dies (death clip, a 1-beat hold, a 1-beat dissolve with
    /// an emissive edge, then a refill that dissolves in). Stateless: a pure function of time and
    /// slot, evaluated by a Burst job straight into the crowd's instance array.
    /// </summary>
    public static class CrowdScenario
    {
        public const float Beat = 60f / 155f;   // Action's tempo
        public const float Cycle = 7f;
        public const float CrossFade = 0.2f;

        public struct Motion
        {
            public uint Walk, Attack, Hit, Death;
            public float WalkLength, Window, DeathEnd, Speed, Hover;
        }

        public struct Slot
        {
            public float2 Center;
            public float Radius, Direction, Theta0, Phase, Offset, Scale;
            public uint Tint, Seed;
        }

        public static Motion MotionOf(CrowdType t)
        {
            int walk = t.Clip("walk"), attack = t.Clip("attack"), hit = t.Clip("hit"), death = t.Clip("death");
            if (walk < 0 || attack < 0 || hit < 0 || death < 0) throw new InvalidOperationException($"CrowdScenario: {t.Id} lacks a walk, attack, hit or death clip");
            var w = t.Clips[walk];
            float stride = w.StrideM > 0.05f ? w.StrideM : 0.9f * t.HeightM;
            var d = t.Clips[death];
            float deathEnd = d.End > 0 ? d.End : d.Length;
            // The whole death (hold and dissolve included) fits in the cycle after the latest event.
            deathEnd = math.min(deathEnd, Cycle - 3.5f - 2 * Beat - 0.05f);
            return new Motion
            {
                Walk = (uint)walk, Attack = (uint)attack, Hit = (uint)hit, Death = (uint)death,
                WalkLength = w.Length,
                Window = math.clamp(t.Clips[attack].Length, 1f, 2f),
                DeathEnd = deathEnd,
                Speed = stride / math.max(0.1f, w.Length),
                Hover = t.HoverM,
            };
        }

        [BurstCompile]
        public struct Job : IJobParallelFor
        {
            [ReadOnly] public NativeArray<Slot> Slots;
            [WriteOnly] public NativeArray<MonsterInstance> Out;
            public Motion M;
            public float Time;
            public bool Combat;

            public void Execute(int i) => Out[i] = Evaluate(Slots[i], M, Time, Combat);
        }

        public static MonsterInstance Evaluate(Slot s, Motion mo, float t, bool combat)
        {
            float speed = mo.Speed * s.Scale;
            var m = new MonsterInstance
            {
                Scale = s.Scale, Tint = s.Tint, Flags = (uint)CrowdFlags.Alive, Blend = 1,
                ClipA = mo.Walk, ClipB = mo.Walk,
            };
            float dist;
            int ev = -1; // 0 attack, 1 hit, 2 death
            float tau = -1;
            float refill = -1;
            if (!combat) dist = speed * t;
            else
            {
                float u0 = t + s.Offset;
                float k = math.floor(u0 / Cycle);
                float u = u0 - k * Cycle;
                uint h = math.hash(new uint2(s.Seed, (uint)(int)k));
                float e = 1.5f + 2f * ((h >> 16) / 65535f);
                float pick = (h & 0xFFFF) / 65535f;
                ev = pick < 0.35f ? 0 : pick < 0.7f ? 1 : 2;
                tau = u - e;
                float baseDist = k * (Cycle - mo.Window);
                dist = ev == 2 ? speed * (baseDist + math.min(u, e)) : speed * (baseDist + u - math.clamp(tau, 0, mo.Window));
                uint hp = math.hash(new uint2(s.Seed, (uint)(int)(k - 1)));
                if ((hp & 0xFFFF) / 65535f >= 0.7f && u < Beat) refill = u / Beat;
            }
            float theta = s.Theta0 + s.Direction * dist / s.Radius;
            math.sincos(theta, out float st, out float ct);
            float2 p = s.Center + s.Radius * new float2(ct, st);
            float2 fwd = s.Direction * new float2(-st, ct);
            m.Yaw = math.atan2(fwd.x, fwd.y);
            float walkTime = s.Phase + dist / math.max(1e-3f, speed);
            m.TimeA = m.TimeB = walkTime;

            if (ev >= 0 && tau >= 0)
            {
                if (ev == 2)
                {
                    m.ClipA = mo.Death;
                    m.TimeA = tau;
                    m.Blend = math.saturate(tau / 0.15f);
                    m.HitFlash = math.saturate(1 - tau / 0.15f);
                    m.Flags = (uint)CrowdFlags.Dying;
                    m.Dissolve = math.saturate((tau - mo.DeathEnd - Beat) / Beat);
                    if (tau > mo.DeathEnd + 2 * Beat) m.Flags = (uint)CrowdFlags.Hidden;
                }
                else
                {
                    uint clip = ev == 0 ? mo.Attack : mo.Hit;
                    if (tau < mo.Window)
                    {
                        m.ClipA = clip;
                        m.TimeA = tau;
                        m.Blend = math.saturate(tau / (ev == 1 ? 0.1f : CrossFade));
                    }
                    else
                    {
                        m.ClipB = clip;
                        m.TimeB = mo.Window;
                        m.Blend = math.saturate((tau - mo.Window) / CrossFade);
                    }
                    if (ev == 1)
                    {
                        m.HitFlash = math.saturate(1 - tau / 0.15f);
                        // Knockback: pushed back and recovering over 0.6 s.
                        float push = 0.35f * s.Scale * math.sin(math.PI * math.saturate(tau / 0.6f));
                        p -= fwd * push;
                    }
                }
            }
            if (refill >= 0 && (m.Flags & (uint)CrowdFlags.Hidden) == 0) m.Dissolve = math.max(m.Dissolve, 1 - refill);
            m.Position = new float3(p.x, mo.Hover, p.y);
            return m;
        }

        /// <summary>The roster mix: six fodder types in equal parts, 4.5% each elite, one boss.</summary>
        public static int[] Mix(CrowdLibrary lib, int n)
        {
            var counts = new int[lib.Types.Length];
            int fodderTypes = 0;
            foreach (var t in lib.Types) if (t.Kind == "fodder") fodderTypes++;
            int assigned = 0;
            for (int i = 0; i < counts.Length; i++)
            {
                var t = lib.Types[i];
                if (t.Kind == "elite") counts[i] = (int)math.round(n * 0.045f);
                else if (t.Kind == "boss") counts[i] = n >= 300 ? 1 : 0;
                assigned += counts[i];
            }
            int left = math.max(0, n - assigned), f = 0;
            for (int i = 0; i < counts.Length; i++)
                if (lib.Types[i].Kind == "fodder")
                {
                    counts[i] = left / fodderTypes + (f < left % fodderTypes ? 1 : 0);
                    f++;
                }
            return counts;
        }

        static readonly Color32[] Tints =
        {
            new Color32(255, 255, 255, 255), new Color32(255, 236, 214, 255), new Color32(220, 232, 255, 255),
            new Color32(232, 232, 220, 255), new Color32(255, 222, 222, 255),
        };

        public static uint Pack(Color32 c) => (uint)(c.r | (c.g << 8) | (c.b << 16) | (c.a << 24));

        /// <summary>
        /// Places `onScreen` monsters' circles inside the ground footprint (a quad in x, z) and
        /// `offScreen` more in a band up to `band` metres outside it, deterministic from the seed.
        /// </summary>
        public static Slot[] Place(int onScreen, int offScreen, Vector2[] footprint, float band, uint seed)
        {
            var rng = new Unity.Mathematics.Random(seed | 1);
            var slots = new Slot[onScreen + offScreen];
            var center = Vector2.zero;
            foreach (var c in footprint) center += c / 4;
            var outer = new Vector2[4];
            float min = float.MaxValue, maxX = float.MinValue, minX = float.MaxValue, minZ = float.MaxValue, maxZ = float.MinValue;
            for (int i = 0; i < 4; i++)
            {
                var d = footprint[i] - center;
                outer[i] = footprint[i] + d.normalized * band * 1.42f;
                minX = Mathf.Min(minX, outer[i].x); maxX = Mathf.Max(maxX, outer[i].x);
                minZ = Mathf.Min(minZ, outer[i].y); maxZ = Mathf.Max(maxZ, outer[i].y);
            }
            _ = min;
            for (int i = 0; i < slots.Length; i++)
            {
                bool inside = i < onScreen;
                Vector2 c;
                float edge;
                int guard = 0;
                while (true)
                {
                    c = new Vector2(rng.NextFloat(minX, maxX), rng.NextFloat(minZ, maxZ));
                    edge = EdgeDistance(footprint, c);
                    if (inside ? edge > 0 : edge < 0 && EdgeDistance(outer, c) > 0) break;
                    if (++guard > 10000) break;
                }
                float radius = rng.NextFloat(1.5f, 6f);
                if (inside) radius = Mathf.Max(0.6f, Mathf.Min(radius, edge - 0.3f));
                slots[i] = new Slot
                {
                    Center = c, Radius = radius, Direction = rng.NextBool() ? 1 : -1, Theta0 = rng.NextFloat(0, 2 * Mathf.PI),
                    Phase = rng.NextFloat(0, 10), Offset = rng.NextFloat(0, Cycle), Scale = rng.NextFloat(0.9f, 1.1f),
                    Tint = Pack(Tints[rng.NextInt(Tints.Length)]), Seed = rng.NextUInt(),
                };
            }
            return slots;
        }

        /// <summary>Signed distance to a convex quad's edges, positive inside (either winding).</summary>
        public static float EdgeDistance(Vector2[] quad, Vector2 p)
        {
            float area = 0;
            for (int i = 0; i < 4; i++)
            {
                var a = quad[i];
                var b = quad[(i + 1) & 3];
                area += a.x * b.y - b.x * a.y;
            }
            float sign = area >= 0 ? 1 : -1, best = float.MaxValue;
            for (int i = 0; i < 4; i++)
            {
                var a = quad[i];
                var b = quad[(i + 1) & 3];
                var e = (b - a).normalized;
                float d = sign * (e.x * (p.y - a.y) - e.y * (p.x - a.x));
                best = Mathf.Min(best, d);
            }
            return best;
        }

        /// <summary>The camera's ground footprint (corner rays to y = 0), in screen-corner order.</summary>
        public static Vector2[] Footprint(Camera camera)
        {
            var q = new Vector2[4];
            var order = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };
            for (int i = 0; i < 4; i++)
            {
                var ray = camera.ViewportPointToRay(new Vector3(order[i].x, order[i].y, 0));
                float d = ray.direction.y < -1e-4f ? ray.origin.y / -ray.direction.y : camera.farClipPlane;
                var hit = ray.origin + ray.direction * Mathf.Min(d, camera.farClipPlane);
                q[i] = new Vector2(hit.x, hit.z);
            }
            return q;
        }

        public static float Area(Vector2[] quad)
        {
            float a = 0;
            for (int i = 0; i < 4; i++)
            {
                var p = quad[i];
                var r = quad[(i + 1) & 3];
                a += p.x * r.y - r.x * p.y;
            }
            return Mathf.Abs(a) / 2;
        }
    }
}
