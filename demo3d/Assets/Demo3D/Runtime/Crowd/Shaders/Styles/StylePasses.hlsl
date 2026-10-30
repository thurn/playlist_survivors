// The skinned passes every style shader shares besides its forward pass: ShadowCaster,
// DepthOnly, DepthNormals and MotionVectors, CrowdSkinned's own (crowd.md, Drawing the crowd) on
// StyleCommon's front end. They skin through SL_Skin, so they take the style's SL_DISPLACE and
// clip with its SL_DISSOLVE and the LOD dither, as its forward pass does. A pass defines one of
// SL_PASS_SHADOWCASTER, SL_PASS_DEPTHONLY, SL_PASS_DEPTHNORMALS or SL_PASS_MOTIONVECTORS and
// includes this after StyleCommon.hlsl (the SubShader's HLSLINCLUDE); its render state is in the
// shader (see Painted.shader).
//
// Under SL_STATIC (the structure shaders) they are a static mesh's: no cascade culling and no
// MotionVectors pass (a still structure has only the camera's motion), and each clips with the kit
// material's alpha clip and, but for the shadow caster, the canopy fade (SL_StaticClip).
#ifndef DEMO3D_STYLE_PASSES_INCLUDED
#define DEMO3D_STYLE_PASSES_INCLUDED

struct SLPassVaryings
{
    float4 positionCS : SV_POSITION;
    float3 rest : TEXCOORD0;
    nointerpolation float4 fx : TEXCOORD1;
    float3 normalWS : TEXCOORD2;
    float4 positionCSNoJitter : TEXCOORD3;
    float4 previousPositionCSNoJitter : TEXCOORD4;
#if defined(SL_STATIC)
    float3 positionWS : TEXCOORD5;
    float2 uv : TEXCOORD6;
#endif
};

#if defined(SL_PASS_SHADOWCASTER)
#define SL_PASS_IS_SHADOW true
#else
#define SL_PASS_IS_SHADOW false
#endif

#if defined(SL_STATIC)
// What a static pass's clips read: its rest position, its world position and the kit's uv.
void SL_PassStatic(SLAttributes v, SLVertex c, inout SLPassVaryings o)
{
    o.rest = c.rest;
    o.positionWS = c.positionWS;
    o.uv = v.uv;
}

void SL_PassClip(SLPassVaryings i)
{
    SL_StaticClip(i.positionCS, i.positionWS, i.uv, !SL_PASS_IS_SHADOW);
    SL_DISSOLVE(i.rest, i.fx.y);
}
#else
#define SL_PassStatic(v, c, o)

// The death and LOD clips of every pass (SL_FRAGMENT_BEGIN's, without the ID render).
void SL_PassClip(SLPassVaryings i)
{
    CrowdLodClip(i.fx.zw, i.positionCS);
    SL_DISSOLVE(i.rest, i.fx.y);
}
#endif

#if defined(SL_PASS_SHADOWCASTER)
float3 _LightDirection;
float3 _LightPosition;

SLPassVaryings vert(SLAttributes v)
{
    SLPassVaryings o = (SLPassVaryings)0;
#if !defined(SL_STATIC)
    // URP repeats the shadow draw for each cascade: skip a monster outside this cascade's
    // light-space box, as CrowdSkinned does (the depth axis is not tested).
    uint slot = CrowdSlot(v.instanceID);
    float height;
    float4 sphere = CrowdSphere(_Instances[slot], height);
    float4 cs = mul(UNITY_MATRIX_VP, float4(sphere.xyz, 1));
    float2 extent = sphere.w * float2(length(UNITY_MATRIX_VP[0].xyz), length(UNITY_MATRIX_VP[1].xyz));
    if (any(abs(cs.xy) - extent > cs.w))
    {
        o.positionCS = float4(2, 2, 2, 1);
        return o;
    }
#endif
    SLVertex c = SL_Skin(v);
    #if _CASTING_PUNCTUAL_LIGHT_SHADOW
    float3 lightDirectionWS = normalize(_LightPosition - c.positionWS);
    #else
    float3 lightDirectionWS = _LightDirection;
    #endif
    o.positionCS = ApplyShadowClamping(TransformWorldToHClip(ApplyShadowBias(c.positionWS, c.normalWS, lightDirectionWS)));
    o.rest = v.positionOS;
    o.fx = c.fx;
    SL_PassStatic(v, c, o);
    return o;
}

half4 frag(SLPassVaryings i) : SV_Target
{
    SL_PassClip(i);
    return 0;
}
#endif

#if defined(SL_PASS_DEPTHONLY) || defined(SL_PASS_DEPTHNORMALS)
SLPassVaryings vert(SLAttributes v)
{
    SLVertex c = SL_Skin(v);
    SLPassVaryings o = (SLPassVaryings)0;
    o.positionCS = TransformWorldToHClip(c.positionWS);
    o.normalWS = c.normalWS;
    o.rest = v.positionOS;
    o.fx = c.fx;
    SL_PassStatic(v, c, o);
    return o;
}
#endif

#if defined(SL_PASS_DEPTHONLY)
half frag(SLPassVaryings i) : SV_Target
{
    SL_PassClip(i);
    return i.positionCS.z;
}
#endif

#if defined(SL_PASS_DEPTHNORMALS)
half4 frag(SLPassVaryings i) : SV_Target
{
    SL_PassClip(i);
    float3 normalWS = normalize(i.normalWS);
    #if defined(_GBUFFER_NORMALS_OCT)
    float2 oct = PackNormalOctQuadEncode(normalWS);
    return half4(PackFloat2To888(saturate(oct * 0.5 + 0.5)), 0);
    #else
    return half4(normalWS, 0);
    #endif
}
#endif

#if defined(SL_PASS_MOTIONVECTORS) && !defined(SL_STATIC)
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/MotionVectorsCommon.hlsl"
float2 _CrowdDebugMotion; // --debug-motion's offset (CrowdSkinned)

SLPassVaryings vert(SLAttributes v)
{
    SLVertex c = SL_Skin(v);
    bool previousPalette;
    MonsterInstance prev = PreviousOf(c.slot, c.m, previousPalette);
    // The previous pose (CrowdSkinned's), displaced as this frame's is.
    float3 prevWS, prevN;
    SkinVertex(prev, c.slot, previousPalette, v.positionOS, v.normalOS, v.boneIndex, v.boneWeight, prevWS, prevN);
    SL_DISPLACE(v, prev, c.slot, prevWS, prevN);
    SLPassVaryings o = (SLPassVaryings)0;
    o.positionCS = TransformWorldToHClip(c.positionWS);
    o.positionCSNoJitter = mul(_NonJitteredViewProjMatrix, float4(c.positionWS, 1));
    o.previousPositionCSNoJitter = mul(_PrevViewProjMatrix, float4(prevWS, 1));
    o.rest = v.positionOS;
    o.fx = c.fx;
    return o;
}

float4 frag(SLPassVaryings i) : SV_Target
{
    SL_PassClip(i);
    float2 mv = CalcNdcMotionVectorFromCsPositions(i.positionCSNoJitter, i.previousPositionCSNoJitter);
    return float4(mv + _CrowdDebugMotion, 0, 0);
}
#endif

#endif
