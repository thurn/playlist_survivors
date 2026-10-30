// The fire's body treatment's material inputs, displacement and death (FireBody.shader's HLSLINCLUDE): its
// UnityPerMaterial CBUFFER, then StyleCommon.hlsl. Shared by FireBody.shader (the crowd) and its structure
// twin (Arena/Shaders/Structures/, which defines SL_STATIC first; environment.md, Structure styles).
#ifndef DEMO3D_STYLE_FIRE_INPUT_INCLUDED
#define DEMO3D_STYLE_FIRE_INPUT_INCLUDED

CBUFFER_START(UnityPerMaterial)
    float4 _BaseMap_ST; half4 _BaseColor;
    half4 _Ember; half4 _Flame; half4 _Core; half _CrackScale; half _CrackWidth;
    half _Gain; half _Peak;
    float _HeightM;
CBUFFER_END

#define SL_DISPLACE(v, m, slot, positionWS, normalWS) \
    positionWS += normalWS * 0.012 * (SL_Noise((v).positionOS * 6.0 + float3(0, -_SL_Time * 4.0, 0)) - 0.5);
#define SL_DISSOLVE(rest, death) FireBurn(rest, death)
#include "StyleCommon.hlsl"
#include "FireCommon.hlsl"

// Burns away from the feet up through noise on the rest position. Returns the hot edge.
float FireBurn(float3 rest, float death)
{
    if (death <= 0) return 0;
    float h01 = saturate(rest.y / max(_HeightM, 0.1));
    float burn = death * 1.3 - h01 * 0.3;
    float d = SL_Noise(rest * 7.0) - burn;
    clip(d);
    return 1.0 - saturate(d / 0.1);
}

#endif
