// The crystal monsters' vertex front end (MonsterCrystal.shader's HLSLINCLUDE): demo3d's
// StyleCommon.hlsl for the skinned crowd, copied and owned for the crystal alone. The style mesh's
// attributes, the palette skinning, the instance's hit flash, tint and LOD fade, on what every
// crystal shader shares (CrystalCommon.hlsl). The shader declares its UnityPerMaterial CBUFFER
// (with `half4 _Tint; half _Gain; half _Peak;`) before including this.
#ifndef GAME_MONSTER_COMMON_INCLUDED
#define GAME_MONSTER_COMMON_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
#include "Packages/com.playlistsurvivors.look/Runtime/Shaders/StaticShadow.hlsl"
#include "Packages/com.playlistsurvivors.look/Runtime/Shaders/CrystalCommon.hlsl"
#include "CrowdCommon.hlsl"

// 1 while the stills draw their ID render (Stills.cs, demo3d's DemoMain.MaskMode): the crowd flat white.
float _GameIdMask;

// The crystal cut's inner glow (demo3d's glow tune, trailer.md "The crystal cut"; 0 where nothing sets
// it, as in the game): light from the core, added after the grade's peak so the gems bloom on dark
// ground, each facet its own brightness, throbbing on the beat, a fresh break's faces the brightest.
float _SL_InnerGlow;

float3 MonsterInnerGlow(float3 positionWS, float3 normalWS, float4 bary, float3 tint, float throb, float inside)
{
    float3 V = GetWorldSpaceNormalizeViewDir(positionWS);
    float3 facet = normalize(cross(ddy(positionWS), ddx(positionWS)));
    if (dot(facet, V) < 0) facet = -facet;
    float3 N = normalize(lerp(normalize(normalWS), facet, _Facets));
    float fres = pow(1.0 - saturate(dot(N, V)), 3.0);
    return tint * _SL_InnerGlow * (0.35 + 0.65 * bary.w) * (0.7 + 0.6 * SL_Wave(positionWS, 6.0) + 2.0 * throb)
        * (1.0 - 0.5 * fres) * (1.0 + inside);
}

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
    float4 fx;    // x hit, y the beat throb (CrowdThrob), zw the LOD fade (CrowdLodFade)
    float3 tint;  // linear
};

MonsterVertex MonsterSkin(MonsterAttributes v)
{
    MonsterVertex o;
    uint slot = CrowdSlot(v.instanceID);
    MonsterInstance m = _Instances[slot];
    SkinVertex(m, slot, v.positionOS, v.normalOS, v.boneIndex, v.boneWeight, o.positionWS, o.normalWS);
    o.fx = float4(m.hitFlash, CrowdThrob(m), CrowdLodFade(slot));
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
