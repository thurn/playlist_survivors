// The bone-texture crowd's shared shader code (crowd.md, Bone-texture path): palette skinning
// (CrowdPalette.compute blends each visible monster's bones once per frame; every pass reads 4
// matrices per vertex) and the per-instance effects (tint, hit flash, dissolve, LOD dither).
// Every pass of CrowdSkinned.shader includes it. TD5 measured a per-vertex variant too (48
// texel loads per vertex in every pass) and deleted it as the slower (evidence/td5/).
#ifndef DEMO3D_CROWD_COMMON_INCLUDED
#define DEMO3D_CROWD_COMMON_INCLUDED

#define UNITY_INDIRECT_DRAW_ARGS IndirectDrawIndexedArgs
#include "UnityIndirect.cginc"
#include "CrowdSkinning.hlsl"

StructuredBuffer<MonsterInstance> _Instances;
StructuredBuffer<MonsterInstance> _PrevInstances;
StructuredBuffer<uint> _VisibleList;     // instance slots, by command (bin) ranges
StructuredBuffer<uint4> _LodState;       // x current LOD, y previous LOD, z fade start (asfloat)
uint _PrevCount;
uint _BoneCount;
float _LodIndex;
float _LodDither;     // 0 for the shadow proxy draw, which never crossfades
float _CrowdTime;
float _CrowdNoSkin;    // a global, set only by the --ablate skin measurement

StructuredBuffer<float4> _Palette;       // 3 rows per bone, slot-major
StructuredBuffer<float4> _PrevPalette;
StructuredBuffer<uint> _PrevStamp;       // the frame each slot's previous palette was written
uint _CrowdFrame;

uint CrowdSlot(uint svInstanceID)
{
    InitIndirectDrawArgs(0);
    return _VisibleList[GetIndirectInstanceID_Base(svInstanceID)];
}

float3x4 BoneMatrix(uint slot, uint bone, bool previous)
{
    uint at = (slot * _BoneCount + bone) * 3;
    if (previous)
        return float3x4(_PrevPalette[at], _PrevPalette[at + 1], _PrevPalette[at + 2]);
    return float3x4(_Palette[at], _Palette[at + 1], _Palette[at + 2]);
}

// Skins a root-space vertex and normal with 4 palette bones (this frame's, or the previous
// frame's for motion vectors), then places it by the instance.
void SkinVertex(MonsterInstance m, uint slot, bool previous, float3 positionOS, float3 normalOS,
                float4 boneIndex, float4 boneWeight, out float3 positionWS, out float3 normalWS)
{
    float3x4 skin;
    [branch] if (_CrowdNoSkin > 0.5)
    {
        // Measurement only (--ablate skin): the rest pose, every bone load skipped.
        skin = float3x4(1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0);
    }
    else
    {
        skin = BoneMatrix(slot, (uint)boneIndex.x, previous) * boneWeight.x;
        UNITY_BRANCH if (boneWeight.y > 0) skin += BoneMatrix(slot, (uint)boneIndex.y, previous) * boneWeight.y;
        UNITY_BRANCH if (boneWeight.z > 0) skin += BoneMatrix(slot, (uint)boneIndex.z, previous) * boneWeight.z;
        UNITY_BRANCH if (boneWeight.w > 0) skin += BoneMatrix(slot, (uint)boneIndex.w, previous) * boneWeight.w;
    }
    float3 p = mul(skin, float4(positionOS, 1)) * m.scale;
    float3 n = mul((float3x3)skin, normalOS);
    float sy, cy;
    sincos(m.yaw, sy, cy);
    positionWS = m.position + float3(p.x * cy + p.z * sy, p.y, -p.x * sy + p.z * cy);
    normalWS = normalize(float3(n.x * cy + n.z * sy, n.y, -n.x * sy + n.z * cy));
}

// The previous frame's instance for motion vectors. A slot new this frame, or whose previous
// frame was Hidden or unscaled (a spawn, a refill, a slot that changed owner), has none: it
// takes the current instance, so it shows no motion.
MonsterInstance PreviousOf(uint slot, MonsterInstance current, out bool previousPalette)
{
    previousPalette = false;
    if (slot >= _PrevCount) return current;
    MonsterInstance p = _PrevInstances[slot];
    if ((p.flags & CROWD_HIDDEN) != 0 || p.scale <= 0) return current;
    previousPalette = _PrevStamp[slot] == _CrowdFrame - 1;
    return p;
}

float4 TintOf(uint tint)
{
    if (tint == 0) return 1;
    return float4(tint & 0xFF, (tint >> 8) & 0xFF, (tint >> 16) & 0xFF, tint >> 24) / 255.0;
}

// Value noise over the rest-pose position, for the dissolve pattern.
float CrowdHash(float3 p)
{
    p = frac(p * 0.3183099 + 0.1);
    p *= 17.0;
    return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
}

float CrowdNoise(float3 x)
{
    float3 i = floor(x);
    float3 f = frac(x);
    f = f * f * (3.0 - 2.0 * f);
    return lerp(lerp(lerp(CrowdHash(i + float3(0, 0, 0)), CrowdHash(i + float3(1, 0, 0)), f.x),
                     lerp(CrowdHash(i + float3(0, 1, 0)), CrowdHash(i + float3(1, 1, 0)), f.x), f.y),
                lerp(lerp(CrowdHash(i + float3(0, 0, 1)), CrowdHash(i + float3(1, 0, 1)), f.x),
                     lerp(CrowdHash(i + float3(0, 1, 1)), CrowdHash(i + float3(1, 1, 1)), f.x), f.y), f.z);
}

#define CROWD_DISSOLVE_EDGE 0.06

// Clips dissolved fragments; returns the emissive edge's strength (0..1).
float CrowdDissolve(float dissolve, float3 restPosition)
{
    if (dissolve <= 0) return 0;
    float n = CrowdNoise(restPosition * 9.0) * 0.7 + CrowdNoise(restPosition * 23.0) * 0.3;
    float d = n - dissolve * (1 + CROWD_DISSOLVE_EDGE);
    clip(d);
    return 1 - saturate(d / CROWD_DISSOLVE_EDGE);
}

// The LOD crossfade (0.2 s, dithered): x = this draw's dither sign, y = fade (0..1).
float2 CrowdLodFade(uint slot)
{
    uint4 st = _LodState[slot];
    if (_LodDither == 0 || st.x == st.y) return float2(0, 1);
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
