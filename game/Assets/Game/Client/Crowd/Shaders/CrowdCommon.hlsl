// The crowd's draw-side shader code (unity-game.md, The Crowd; demo3d's CrowdCommon.hlsl, copied
// and owned): palette skinning (CrowdPalette.compute blends each visible monster's bones once per
// frame; every pass reads 4 matrices per vertex), the instance's tint and the LOD crossfade's
// dither.
#ifndef GAME_CROWD_COMMON_INCLUDED
#define GAME_CROWD_COMMON_INCLUDED

#define UNITY_INDIRECT_DRAW_ARGS IndirectDrawIndexedArgs
#include "UnityIndirect.cginc"
#include "CrowdSkinning.hlsl"

StructuredBuffer<MonsterInstance> _Instances;
StructuredBuffer<uint> _VisibleList;     // instance slots, by command (bin) ranges
StructuredBuffer<uint4> _LodState;       // x current LOD, y previous LOD, z fade start (asfloat)
uint _BoneCount;
float _LodIndex;
float _LodDither;     // 0 for the shadow proxy draw, which never crossfades
float _CrowdTime;
StructuredBuffer<float4> _Palette;       // 3 rows per bone, slot-major

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

// Skins a root-space vertex and normal with 4 palette bones, then places it by the instance.
void SkinVertex(MonsterInstance m, uint slot, float3 positionOS, float3 normalOS, float4 boneIndex, float4 boneWeight,
                out float3 positionWS, out float3 normalWS)
{
    float3x4 skin = BoneMatrix(slot, (uint)boneIndex.x) * boneWeight.x;
    UNITY_BRANCH if (boneWeight.y > 0) skin += BoneMatrix(slot, (uint)boneIndex.y) * boneWeight.y;
    UNITY_BRANCH if (boneWeight.z > 0) skin += BoneMatrix(slot, (uint)boneIndex.z) * boneWeight.z;
    UNITY_BRANCH if (boneWeight.w > 0) skin += BoneMatrix(slot, (uint)boneIndex.w) * boneWeight.w;
    float3 p = mul(skin, float4(positionOS, 1)) * m.scale;
    float3 n = mul((float3x3)skin, normalOS);
    float sy, cy;
    sincos(m.yaw, sy, cy);
    positionWS = m.position + float3(p.x * cy + p.z * sy, p.y, -p.x * sy + p.z * cy);
    normalWS = normalize(float3(n.x * cy + n.z * sy, n.y, -n.x * sy + n.z * cy));
}

// The instance's gem colour (sRGB RGBA8, r in the low byte), linear.
float3 TintOf(uint tint)
{
    return SRGBToLinear(float3(tint & 0xFF, (tint >> 8) & 0xFF, (tint >> 16) & 0xFF) / 255.0);
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
