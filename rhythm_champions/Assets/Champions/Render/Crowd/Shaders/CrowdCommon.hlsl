// The crowd's draw-side shader code (plan.md, Rendering > Crowd), ported by review from the frozen look package's
// CrowdCommon.hlsl: palette skinning (CrowdPalette.compute blends each visible monster's bones once per frame; every
// pass reads 4 matrices per vertex), the motion layers' springs and swarm (CrowdMotion.cs), the instance's tint and the
// LOD crossfade's dither. The shader declares its UnityPerMaterial CBUFFER, with `half _Springs; half _Swarm;` (the
// look's motion layers, 0 or 1), before including this. The trailer's horde pulse (_SL_Bounce, _SL_Throb) is not
// ported: the world's beat wave bobs and flares a monster instead (MonsterCommon.hlsl).
#ifndef CHAMPIONS_CROWD_COMMON_INCLUDED
#define CHAMPIONS_CROWD_COMMON_INCLUDED

#define UNITY_INDIRECT_DRAW_ARGS IndirectDrawIndexedArgs
#include "UnityIndirect.cginc"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
#include "CrowdSkinning.hlsl"

StructuredBuffer<MonsterInstance> _Instances;
StructuredBuffer<uint> _VisibleList;     // instance slots, by command (bin) ranges
StructuredBuffer<uint4> _LodState;       // x current LOD, y previous LOD, z fade start (asfloat)
uint _BoneCount;
float _LodIndex;
float _CrowdTime;
StructuredBuffer<float4> _Palette;       // 3 rows per bone, slot-major

// The motion layers (CrowdMotion.cs holds the same numbers): the presenter's clocks per slot (MonsterCrowd.Motion;
// _MotionCount 0 when it wrote none this frame, and for the shards' draws, whose shatter binds a stand-in).
// Matches Champions.Render.Crowd.MonsterMotion (48 bytes).
struct MonsterMotion
{
    float stepAge; float stepMove; float2 travel;
    float kickAge; float kickWind; float2 kick;
    uint seed; float3 pad;
};
StructuredBuffer<MonsterMotion> _Motion;
uint _MotionCount;
#define MOTION_STEP_LEAN 0.2
#define MOTION_STEP_STRETCH 0.07
#define MOTION_STEP_SQUASH 0.13
#define MOTION_KICK_LEAN 0.3
#define MOTION_KICK_STRETCH 0.08
#define MOTION_KICK_SQUASH 0.1
#define MOTION_STEP_FULL 0.3
#define MOTION_SWARM_TILT 0.08      // the standing tilt's furthest lean (tangent)
#define MOTION_SWARM_WOBBLE 0.025   // the wobble's lean
#define MOTION_SWARM_BREATH 0.015   // the wobble's stretch (share of the height)
#define MOTION_TAU 6.2831853

uint CrowdSlot(uint svInstanceID)
{
    InitIndirectDrawArgs(0);
    return _VisibleList[GetIndirectInstanceID_Base(svInstanceID)];
}

float3x4 BoneMatrix(uint slot, uint bone)
{
    uint at = (slot * _BoneCount + bone) * 3;
    return float3x4(_Palette[at], _Palette[at + 1], _Palette[at + 2]);
}

// A stable value in [0, 1) per seed and salt (CrowdMotion.Hash).
float MotionHash(uint seed, uint salt)
{
    uint h = seed * 0x9E3779B1u + salt * 0x85EBCA77u;
    h ^= h >> 15; h *= 0x2C1B3C6Du; h ^= h >> 12; h *= 0x297A2D39u; h ^= h >> 15;
    return (h & 0xFFFFFF) / 16777216.0;
}

// Damped springs let go from 1 (cos) or kicked from rest (sin) at age 0.
float SpringCos(float age, float period, float decay) { return exp(-age / decay) * cos(MOTION_TAU * age / period); }
float SpringSin(float age, float period, float decay) { return exp(-age / decay) * sin(MOTION_TAU * age / period); }

// A living monster's motion layers this frame: the stretch of its height (negative squashed) and its lean, the world
// xz shear of a point by its height above the feet. Springs: a step leans the body into its travel and stretches it
// while it moves, then snaps into a squash on its plant, the lean overshooting back and settling (when the move
// follows the plant, as the horde's beat steps do, the lean is kicked on the plant instead); a lunge crouches and leans
// back through its wind-up, reaches into its contact and springs back; a hit throws the body back and squashes it on
// its own frame. Swarm: a standing tilt and a slow wobble, each life its own.
void CrowdMotion(MonsterInstance m, uint slot, out float stretch, out float2 lean)
{
    stretch = 0;
    lean = 0;
    UNITY_BRANCH if ((m.flags & CROWD_ALIVE) == 0 || slot >= _MotionCount) return;
    MonsterMotion mo = _Motion[slot];
    UNITY_BRANCH if (_Springs > 0)
    {
        float len = length(mo.travel);
        float2 dir = len > 1e-4 ? mo.travel / len : 0;
        float size = saturate(len / MOTION_STEP_FULL);
        float u = mo.stepAge;
        if (u < 0)
        {
            if (mo.stepMove > 0 && u >= -mo.stepMove)
            {
                float f = 1 + u / mo.stepMove;
                stretch += MOTION_STEP_STRETCH * size * f;
                lean += dir * (MOTION_STEP_LEAN * size * f * f * (3 - 2 * f));
            }
        }
        else if (u < 1.5)
        {
            stretch -= MOTION_STEP_SQUASH * size * SpringCos(u, 0.26, 0.08);
            lean += dir * (MOTION_STEP_LEAN * size * (mo.stepMove > 0 ? SpringCos(u, 0.36, 0.11) : 1.6 * SpringSin(u, 0.5, 0.11)));
        }

        float k = length(mo.kick);
        float2 kdir = k > 1e-4 ? mo.kick / k : 0;
        float a = mo.kickAge;
        if (a < 0)
        {
            if (mo.kickWind > 0 && a >= -mo.kickWind)
            {
                float f = 1 + a / mo.kickWind;
                float crouch = sin(3.14159265 * saturate(f / 0.6));
                float reach = smoothstep(0.55, 1, f);
                lean += kdir * (MOTION_KICK_LEAN * k * (reach - 0.35 * crouch));
                stretch += MOTION_KICK_STRETCH * k * (reach - 0.6 * crouch);
            }
        }
        else if (a < 1.5)
        {
            lean += kdir * (MOTION_KICK_LEAN * k * SpringCos(a, 0.4, 0.12));
            stretch -= MOTION_KICK_SQUASH * k * SpringCos(a, 0.24, 0.07);
        }
    }
    UNITY_BRANCH if (_Swarm > 0)
    {
        float2 tilt = float2(MotionHash(mo.seed, 5), MotionHash(mo.seed, 6)) * 2 - 1;
        float phase = MotionHash(mo.seed, 7) * MOTION_TAU;
        float w = _CrowdTime * MOTION_TAU * (0.45 + 0.35 * MotionHash(mo.seed, 8)) + phase;
        lean += tilt * MOTION_SWARM_TILT + MOTION_SWARM_WOBBLE * float2(sin(w), cos(0.83 * w + phase));
        stretch += MOTION_SWARM_BREATH * sin(1.7 * w);
    }
    stretch = clamp(stretch, -0.25, 0.25);
    float l = length(lean);
    if (l > 0.4) lean *= 0.4 / l;
}

// Skins a root-space vertex and normal with 4 palette bones, squashes or stretches a living monster about its feet by
// its motion layers' stretch, widening it as it squashes by more than its volume would (the camera looks down, so the
// footprint reads more than the height), places it by the instance, then leans it by its motion layers (a shear about
// its feet).
#define CROWD_BOUNCE_WIDEN 0.75
void SkinVertex(MonsterInstance m, uint slot, float3 positionOS, float3 normalOS, float4 boneIndex, float4 boneWeight,
                out float3 positionWS, out float3 normalWS)
{
    float3x4 skin = BoneMatrix(slot, (uint)boneIndex.x) * boneWeight.x;
    UNITY_BRANCH if (boneWeight.y > 0) skin += BoneMatrix(slot, (uint)boneIndex.y) * boneWeight.y;
    UNITY_BRANCH if (boneWeight.z > 0) skin += BoneMatrix(slot, (uint)boneIndex.z) * boneWeight.z;
    UNITY_BRANCH if (boneWeight.w > 0) skin += BoneMatrix(slot, (uint)boneIndex.w) * boneWeight.w;
    float3 p = mul(skin, float4(positionOS, 1)) * m.scale;
    float3 n = mul((float3x3)skin, normalOS);
    float stretch;
    float2 lean;
    CrowdMotion(m, slot, stretch, lean);
    float sy0 = 1 + stretch, sxz = 1 - CROWD_BOUNCE_WIDEN * stretch;
    p *= float3(sxz, sy0, sxz);
    n *= float3(sy0, sxz, sy0); // the inverse transpose of the squash, up to scale
    float sy, cy;
    sincos(m.yaw, sy, cy);
    float3 q = float3(p.x * cy + p.z * sy, p.y, -p.x * sy + p.z * cy);
    float3 nq = float3(n.x * cy + n.z * sy, n.y, -n.x * sy + n.z * cy);
    q.xz += lean * q.y;
    nq.y -= dot(lean, nq.xz); // the inverse transpose of the shear
    positionWS = m.position + q;
    normalWS = normalize(nq);
}

// The instance's colour (sRGB RGBA8, r in the low byte), linear.
float3 TintOf(uint tint)
{
    return SRGBToLinear(float3(tint & 0xFF, (tint >> 8) & 0xFF, (tint >> 16) & 0xFF) / 255.0);
}

// The LOD crossfade (0.2 s, dithered): x = this draw's dither sign, y = fade (0..1).
float2 CrowdLodFade(uint slot)
{
    uint4 st = _LodState[slot];
    if (st.x == st.y) return float2(0, 1);
    float since = _CrowdTime - asfloat(st.z);
    if (since < 0 || since >= 0.2) return float2(0, 1); // done, or the clock sought back
    float fade = since / 0.2;
    return float2((uint)_LodIndex == st.x ? 1 : -1, fade);
}

void CrowdLodClip(float2 lodFade, float4 positionCS)
{
    if (lodFade.x == 0) return;
    float2 p = floor(positionCS.xy);
    float dither = frac(52.9829189 * frac(dot(p, float2(0.06711056, 0.00583715)))); // interleaved gradient noise
    clip(lodFade.x > 0 ? lodFade.y - dither : dither - lodFade.y);
}

#endif
