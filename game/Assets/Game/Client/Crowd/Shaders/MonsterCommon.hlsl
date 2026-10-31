// The crystal monsters' vertex front end (MonsterCrystal.shader's HLSLINCLUDE): demo3d's
// StyleCommon.hlsl for the skinned crowd, copied and owned for the crystal alone. The style mesh's
// attributes, the palette skinning, the instance's hit flash, tint and LOD fade, on what every
// crystal shader shares (CrystalCommon.hlsl). The shader declares its UnityPerMaterial CBUFFER
// (with `half4 _Tint; half _Gain; half _Peak;`) before including this.
#ifndef GAME_MONSTER_COMMON_INCLUDED
#define GAME_MONSTER_COMMON_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
#include "../../Arena/Shaders/StaticShadow.hlsl"
#include "../../Arena/Shaders/CrystalCommon.hlsl"
#include "CrowdCommon.hlsl"

// 1 while the stills draw their ID render (Stills.cs): the crowd flat white.
float _GameIdMask;

// The crowd's style mesh (CrowdBaker.Unweld): every triangle its own vertices, the rest pose in
// root space at roster height, bone data in TEXCOORD1/2, the face normal in the tangent and
// barycentrics plus a random value per triangle in TEXCOORD3.
struct MonsterAttributes
{
    float3 positionOS : POSITION;
    float3 normalOS : NORMAL;
    float4 boneIndex : TEXCOORD1;
    float4 boneWeight : TEXCOORD2;
    float4 bary : TEXCOORD3;
    uint instanceID : SV_InstanceID;
};

struct MonsterVertex
{
    float3 positionWS;
    float3 normalWS;
    float4 fx;    // x hit, zw the LOD fade (CrowdLodFade)
    float3 tint;  // linear
};

MonsterVertex MonsterSkin(MonsterAttributes v)
{
    MonsterVertex o;
    uint slot = CrowdSlot(v.instanceID);
    MonsterInstance m = _Instances[slot];
    SkinVertex(m, slot, v.positionOS, v.normalOS, v.boneIndex, v.boneWeight, o.positionWS, o.normalWS);
    o.fx = float4(m.hitFlash, 0, CrowdLodFade(slot));
    o.tint = m.tint != 0 ? TintOf(m.tint) : _Tint.rgb;
    return o;
}

struct MonsterVaryings
{
    float4 positionCS : SV_POSITION;
    float3 positionWS : TEXCOORD0;
    float3 normalWS : TEXCOORD1;
    nointerpolation float4 fx : TEXCOORD2;
    nointerpolation float3 tint : TEXCOORD3;
    float fogFactor : TEXCOORD4;
    float4 bary : TEXCOORD5;
};

MonsterVaryings MonsterFinish(MonsterVertex c, MonsterAttributes v)
{
    MonsterVaryings o;
    o.positionWS = c.positionWS;
    o.positionCS = TransformWorldToHClip(c.positionWS);
    o.normalWS = c.normalWS;
    o.fx = c.fx;
    o.tint = c.tint;
    o.fogFactor = ComputeFogFactor(o.positionCS.z);
    o.bary = v.bary;
    return o;
}

#endif
